using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace ScavengingTweaks;

public sealed class Mod : MelonMod
{
    private const string HarmonyId = "ProbablyStolen.ScavengingTweaks";
    private const string CounterMigrationMarker = "ScavengingTweaks.counter-migrated-v2";
    private static readonly HashSet<string> LoggedWeightTables = new(StringComparer.Ordinal);
    private static MelonPreferences_Entry<int> maxAttempts = null!;
    private static MelonPreferences_Entry<float> highValueMultiplier = null!;
    private static MelonPreferences_Entry<string> woundBlockMode = null!;
    private static MelonPreferences_Entry<bool> testHotkeysEnabled = null!;
    private static MelonPreferences_Entry<string> endOfDayHotkey = null!;
    private static MelonPreferences_Entry<string> directScavengeHotkey = null!;
    private static bool counterMigrationChecked;
    private static bool inScavengeWindow;
    private static long scavengeWindowTick = long.MinValue;
    private static long scavengeAttemptSequence;
    private static long activeScavengeAttempt;
    private static bool activeAttemptObservedRoll;
    private static bool warnedOutsideWindow;

    public override void OnInitializeMelon()
    {
        var category = MelonPreferences.CreateCategory("ScavengingTweaks", "Scavenging Tweaks");
        maxAttempts = category.CreateEntry<int>(
            "MaxAttempts",
            10,
            "Maximum scavenging attempts per day",
            "The number of garbage dump searches available each day.",
            false,
            false,
            null);
        highValueMultiplier = category.CreateEntry<float>(
            "HighValueMultiplier",
            2.0f,
            "High-value loot multiplier",
            "Approximate weight multiplier for the most valuable half of the dump loot table.",
            false,
            false,
            null);
        woundBlockMode = category.CreateEntry<string>(
            "WoundBlockMode",
            "ScavengingOnly",
            "Wound blocking mode",
            "ScavengingOnly: wounds are blocked during the scavenging resolution. Always: all wounds are blocked (including combat).",
            false,
            false,
            null);
        testHotkeysEnabled = category.CreateEntry<bool>(
            "TestHotkeys",
            true,
            "Enable test hotkeys",
            "F7: close the store shutter to skip straight to the evening phase. F8: resolve one scavenging attempt immediately.",
            false,
            false,
            null);
        endOfDayHotkey = category.CreateEntry<string>(
            "EndOfDayHotkey",
            "F7",
            "Skip-to-evening hotkey",
            "Closes the store shutter exactly like the player confirming the dialog.",
            false,
            false,
            null);
        directScavengeHotkey = category.CreateEntry<string>(
            "DirectScavengeHotkey",
            "F8",
            "Direct scavenge hotkey",
            "Resolves one scavenging attempt immediately without opening the dump UI.",
            false,
            false,
            null);

        InstallPatches();
        MelonLogger.Msg("ScavengingTweaks loaded: attempts={0}, high-value multiplier={1:0.##}x, wound mode={2}", MaxAttempts, HighValueMultiplier, woundBlockMode.Value);
        if (testHotkeysEnabled.Value)
        {
            MelonLogger.Msg("ScavengingTweaks test hotkeys armed: {0}=skip to evening, {1}=direct scavenge.", endOfDayHotkey.Value, directScavengeHotkey.Value);
        }
    }

    private static bool loggedUpdateAlive;

    public override void OnUpdate()
    {
        if (!testHotkeysEnabled.Value)
        {
            return;
        }

        if (!loggedUpdateAlive)
        {
            loggedUpdateAlive = true;
            MelonLogger.Msg("ScavengingTweaks test hotkeys are being polled.");
        }

        try
        {
            if (TryGetHotkey(endOfDayHotkey, out var endOfDayKey) && Input.GetKeyDown(endOfDayKey))
            {
                SkipToEndOfDayPhase();
            }

            if (TryGetHotkey(directScavengeHotkey, out var scavengeKey) && Input.GetKeyDown(scavengeKey))
            {
                RunDirectScavenge();
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Error("ScavengingTweaks hotkey handling failed.", exception);
        }
    }

    private static bool TryGetHotkey(MelonPreferences_Entry<string> entry, out KeyCode key)
    {
        key = default;
        return Enum.TryParse(entry.Value, ignoreCase: true, out key);
    }

    // F9: replicate the player confirming the shutter dialog, which is the
    // vanilla trigger that ends the selling phase and unlocks afterhours.
    private static void SkipToEndOfDayPhase()
    {
        var store = PlayerStore.instance;
        if (store == null)
        {
            MelonLogger.Msg("ScavengingTweaks hotkey: no store loaded yet (still in the main menu?).");
            return;
        }

        var shutter = StoreShutterButton.Instance;
        if (shutter == null)
        {
            MelonLogger.Warning("ScavengingTweaks hotkey: StoreShutterButton not found in this scene.");
            return;
        }

        if (shutter.isClosed)
        {
            MelonLogger.Msg("ScavengingTweaks hotkey: shutter is already closed; the evening phase should be active.");
            return;
        }

        shutter.EndAndClose();
        MelonLogger.Msg("ScavengingTweaks hotkey: shutter closed, skipping to the evening phase.");
    }

    // F10: resolve one scavenging attempt without navigating to the dump UI.
    private static void RunDirectScavenge()
    {
        var store = PlayerStore.instance;
        if (store == null)
        {
            MelonLogger.Msg("ScavengingTweaks hotkey: no store loaded yet (still in the main menu?).");
            return;
        }

        if (!ScavHelper.CanScavenge())
        {
            MelonLogger.Msg("ScavengingTweaks hotkey: scavenging is not available right now (attempts left={0}).", store.scavengingAttempts);
            return;
        }

        ScavHelper.ScavengeDumpingGrounds();
        MelonLogger.Msg("ScavengingTweaks hotkey: resolved one direct scavenging attempt.");
    }

    private static int MaxAttempts => Math.Max(1, maxAttempts.Value);
    private static double HighValueMultiplier => Math.Max(1.0, highValueMultiplier.Value);
    private static bool BlockAllWounds => string.Equals(woundBlockMode.Value, "Always", StringComparison.OrdinalIgnoreCase);

    private static void InstallPatches()
    {
        var harmony = new HarmonyLib.Harmony(HarmonyId);
        Patch(harmony, typeof(ScavHelper), "GetScavTimeLeft", postfix: nameof(Patches.GetScavTimeLeftPostfix));
        Patch(harmony, typeof(ScavHelper), "CanScavenge", prefix: nameof(Patches.CanScavengePrefix), postfix: nameof(Patches.CanScavengePostfix));
        Patch(harmony, typeof(ScavHelper), "GetMinorWoundChance", postfix: nameof(Patches.ZeroChancePostfix));
        Patch(harmony, typeof(ScavHelper), "GetMajorWoundChance", postfix: nameof(Patches.ZeroChancePostfix));
        Patch(harmony, typeof(ScavHelper), "RollMinorWound", prefix: nameof(Patches.RollMinorWoundPrefix));
        Patch(harmony, typeof(ScavHelper), "RollMajorWound", prefix: nameof(Patches.RollMajorWoundPrefix));
        Patch(harmony, typeof(ScavHelper), "ScavengeDumpingGrounds", prefix: nameof(Patches.ScavengePrefix), postfix: nameof(Patches.ScavengePostfix));
        Patch(harmony, typeof(ScavHelper), "GetRandomScavengedItem", postfix: nameof(Patches.ScavengedItemResultPostfix));
        Patch(
            harmony,
            typeof(ItemSpawner),
            "SpawnFromTableGroup",
            prefix: nameof(Patches.SpawnFromTableGroupPrefix),
            postfix: nameof(Patches.SpawnFromTableGroupPostfix),
            finalizer: nameof(Patches.SpawnFromTableGroupFinalizer));
        Patch(harmony, typeof(HealthData), "ReceiveMinorWound", prefix: nameof(Patches.BlockMinorWoundPrefix));
        Patch(harmony, typeof(HealthData), "ReceiveMajorWound", prefix: nameof(Patches.BlockMajorWoundPrefix));
    }

    private static void Patch(
        HarmonyLib.Harmony harmony,
        Type targetType,
        string targetMethodName,
        string? prefix = null,
        string? postfix = null,
        string? finalizer = null)
    {
        var target = AccessTools.Method(targetType, targetMethodName);
        if (target == null)
        {
            MelonLogger.Error("ScavengingTweaks could not find {0}.{1}", targetType.FullName, targetMethodName);
            return;
        }

        var prefixMethod = prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix);
        var postfixMethod = postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix);
        var finalizerMethod = finalizer == null ? null : new HarmonyMethod(typeof(Patches), finalizer);
        harmony.Patch(target, prefixMethod, postfixMethod, finalizer: finalizerMethod);
    }

    private static class Patches
    {
        public static void GetScavTimeLeftPostfix(ref int __result)
        {
            try
            {
                var store = PlayerStore.instance;
                if (store == null)
                {
                    return;
                }

                TryMigrateLegacyCounter(store);
                __result = ScavengingCounter.GetRemainingAttempts(MaxAttempts, store.scavengingAttempts);
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not update the displayed scavenging attempts.", exception);
            }
        }

        public static void CanScavengePrefix(ref int __state)
        {
            __state = 0;
            try
            {
                var store = PlayerStore.instance;
                if (store == null)
                {
                    return;
                }

                TryMigrateLegacyCounter(store);
                var vanillaMaxAttempts = ScavHelper.GetMaxScavAttempts();
                var adjustment = MaxAttempts - vanillaMaxAttempts;
                if (adjustment == 0)
                {
                    return;
                }

                store.scavengingAttempts -= adjustment;
                __state = adjustment;
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not extend the scavenging availability check.", exception);
            }
        }

        public static void CanScavengePostfix(int __state)
        {
            if (__state == 0)
            {
                return;
            }

            try
            {
                var store = PlayerStore.instance;
                if (store != null)
                {
                    store.scavengingAttempts += __state;
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not restore the scavenging counter after checking availability.", exception);
            }
        }

        public static void ZeroChancePostfix(ref float __result)
        {
            __result = 0.0f;
        }

        public static bool RollMinorWoundPrefix()
        {
            MelonLogger.Msg("ScavengingTweaks suppressed a minor wound roll.");
            return false;
        }

        public static bool RollMajorWoundPrefix()
        {
            MelonLogger.Msg("ScavengingTweaks suppressed a major wound roll.");
            return false;
        }

        public static void ScavengePrefix()
        {
            activeScavengeAttempt = ++scavengeAttemptSequence;
            activeAttemptObservedRoll = false;
            inScavengeWindow = true;
            scavengeWindowTick = Environment.TickCount64;
            MelonLogger.Msg(
                "ScavengingTweaks scavenge attempt start: sequence={0}.",
                activeScavengeAttempt);
        }

        public static void ScavengePostfix()
        {
            if (activeScavengeAttempt != 0 && !activeAttemptObservedRoll)
            {
                MelonLogger.Msg(
                    "ScavengingTweaks scavenge attempt end: sequence={0}, no random loot result (empty attempt).",
                    activeScavengeAttempt);
            }
            else if (activeScavengeAttempt != 0)
            {
                MelonLogger.Msg(
                    "ScavengingTweaks scavenge attempt end: sequence={0}, random loot result observed.",
                    activeScavengeAttempt);
            }

            inScavengeWindow = false;
            activeScavengeAttempt = 0;
        }

        // If the original throws, the postfix never runs; the tick guard makes
        // the stale window expire on its own instead of blocking wounds forever.
        private static bool ScavengeWindowOpen => inScavengeWindow && Environment.TickCount64 - scavengeWindowTick < 10_000;

        public static bool BlockMinorWoundPrefix()
        {
            return BlockWoundPrefix("minor");
        }

        public static bool BlockMajorWoundPrefix()
        {
            return BlockWoundPrefix("major");
        }

        private static bool BlockWoundPrefix(string kind)
        {
            if (!ScavengeWindowOpen && !BlockAllWounds)
            {
                if (!warnedOutsideWindow)
                {
                    warnedOutsideWindow = true;
                    MelonLogger.Warning(
                        "ScavengingTweaks saw a {0} wound outside the scavenging window; it was not blocked. Set WoundBlockMode=Always to block every wound.",
                        kind);
                }
                return true;
            }

            MelonLogger.Msg("ScavengingTweaks blocked a {0} wound (window={1}).", kind, ScavengeWindowOpen);
            return false;
        }

        public sealed class TableWeightState
        {
            public readonly List<Action<int>> RestoreActions = new();
            public readonly List<int> OriginalWeights = new();
            public int AdjustedEntries;
            public bool Restored;
        }

        public static void ScavengedItemResultPostfix(Il2CppSystem.Collections.Generic.List<GameItem> __result)
        {
            if (!ScavengeWindowOpen)
            {
                return;
            }

            var count = __result?.Count ?? 0;
            activeAttemptObservedRoll = count > 0;
            MelonLogger.Msg("ScavengingTweaks scavenge result: drops={0}.", count);
        }

        public static void SpawnFromTableGroupPrefix(string tableGroupID, ref TableWeightState __state)
        {
            __state = new TableWeightState();
            if (!ScavengeWindowOpen || HighValueMultiplier <= 1.0 || string.IsNullOrWhiteSpace(tableGroupID))
            {
                return;
            }

            try
            {
                var tableGroups = TableGroupMaster.tableGroups;
                if (tableGroups == null || !tableGroups.TryGetValue(tableGroupID, out var tableGroup) || tableGroup == null)
                {
                    return;
                }

                var tableEntries = tableGroup.tableGroup?.ProbabilityItems;
                if (tableEntries == null || tableEntries.Count == 0)
                {
                    return;
                }

                var ids = new List<string>();
                var weights = new List<int>();
                var values = new Dictionary<string, long>(StringComparer.Ordinal);
                var itemEntries = new List<Il2CppRNGNeeds.ProbabilityItem<string>>();
                for (var tableIndex = 0; tableIndex < tableEntries.Count; tableIndex++)
                {
                    var tableEntry = tableEntries[tableIndex];
                    var lootTable = tableEntry?.Value;
                    var items = lootTable?.table?.ProbabilityItems;
                    if (items == null)
                    {
                        continue;
                    }

                    for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
                    {
                        var itemEntry = items[itemIndex];
                        if (itemEntry == null)
                        {
                            continue;
                        }

                        var itemId = itemEntry.Value;
                        ids.Add(itemId ?? string.Empty);
                        weights.Add(itemEntry.m_Weight);
                        itemEntries.Add(itemEntry);
                        if (itemId != null && !values.ContainsKey(itemId) && TryGetBaseValue(itemId, out var value))
                        {
                            values[itemId] = value;
                        }
                    }
                }

                if (ids.Count == 0)
                {
                    return;
                }

                var scaledWeights = LootWeighting.ScaleHighValueWeights(ids, weights, values, HighValueMultiplier);

                // 第一步：选择探针样本并记录 BEFORE 状态
                var diagnosticSampleIndex = -1;
                for (var index = 0; index < scaledWeights.Count; index++)
                {
                    if (scaledWeights[index] != weights[index] && !string.IsNullOrWhiteSpace(ids[index]))
                    {
                        diagnosticSampleIndex = index;
                        var itemEntry = itemEntries[index];
                        MelonLogger.Msg(
                            "ScavengingTweaks weight adjustment probe BEFORE: id={0}, m_Weight={1}, m_BaseProbability={2}, BaseProbability={3}, Probability={4}.",
                            ids[index],
                            itemEntry.m_Weight,
                            itemEntry.m_BaseProbability,
                            itemEntry.BaseProbability,
                            itemEntry.Probability);
                        break;
                    }
                }

                // 第二步：调整所有权重
                for (var index = 0; index < scaledWeights.Count; index++)
                {
                    var itemEntry = itemEntries[index];
                    __state.RestoreActions.Add(new Action<int>(w => itemEntry.m_Weight = w));
                    __state.OriginalWeights.Add(weights[index]);
                    if (scaledWeights[index] == weights[index])
                    {
                        continue;
                    }

                    itemEntry.m_Weight = scaledWeights[index];
                    __state.AdjustedEntries++;
                }

                // 第三步：记录探针样本的 AFTER 状态
                if (diagnosticSampleIndex >= 0)
                {
                    var sampleEntry = itemEntries[diagnosticSampleIndex];
                    MelonLogger.Msg(
                        "ScavengingTweaks weight adjustment probe AFTER set m_Weight: id={0}, m_Weight={1}, m_BaseProbability={2}, BaseProbability={3}, Probability={4}.",
                        ids[diagnosticSampleIndex],
                        sampleEntry.m_Weight,
                        sampleEntry.m_BaseProbability,
                        sampleEntry.BaseProbability,
                        sampleEntry.Probability);

                    try
                    {
                        var updateMethod = sampleEntry.GetType().GetMethod("RNGNeeds_IProbabilityItem_UpdateProperties", BindingFlags.Public | BindingFlags.Instance);
                        if (updateMethod != null)
                        {
                            updateMethod.Invoke(sampleEntry, null);
                            MelonLogger.Msg(
                                "ScavengingTweaks weight adjustment probe AFTER UpdateProperties: id={0}, m_Weight={1}, m_BaseProbability={2}, BaseProbability={3}, Probability={4}.",
                                ids[diagnosticSampleIndex],
                                sampleEntry.m_Weight,
                                sampleEntry.m_BaseProbability,
                                sampleEntry.BaseProbability,
                                sampleEntry.Probability);
                        }
                        else
                        {
                            MelonLogger.Warning("ScavengingTweaks could not find UpdateProperties method on ProbabilityItem.");
                        }
                    }
                    catch (Exception updateException)
                    {
                        MelonLogger.Warning("ScavengingTweaks could not invoke UpdateProperties: {0}", updateException.Message);
                    }
                }

                if (LoggedWeightTables.Add(tableGroupID))
                {
                    MelonLogger.Msg(
                        "ScavengingTweaks applied dump weights: group={0}, tables={1}, itemEntries={2}, valuedEntries={3}, adjustedEntries={4}, multiplier={5:0.##}x.",
                        tableGroupID,
                        tableEntries.Count,
                        ids.Count,
                        values.Count,
                        __state.AdjustedEntries,
                        HighValueMultiplier);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not adjust the dump table weights; original weights are preserved.", exception);
            }
        }

        public static void SpawnFromTableGroupPostfix(TableWeightState __state)
        {
            RestoreTableWeights(__state);
        }

        public static Exception? SpawnFromTableGroupFinalizer(Exception? __exception, TableWeightState __state)
        {
            RestoreTableWeights(__state);
            return __exception;
        }

        private static void RestoreTableWeights(TableWeightState state)
        {
            if (state == null || state.Restored)
            {
                return;
            }

            state.Restored = true;
            var count = Math.Min(state.RestoreActions.Count, state.OriginalWeights.Count);
            for (var index = 0; index < count; index++)
            {
                try
                {
                    state.RestoreActions[index](state.OriginalWeights[index]);
                }
                catch (Exception exception)
                {
                    MelonLogger.Warning("ScavengingTweaks could not restore a dump table weight: {0}", exception.Message);
                }
            }
        }

        private static void TryMigrateLegacyCounter(PlayerStore store)
        {
            if (counterMigrationChecked)
            {
                return;
            }

            counterMigrationChecked = true;
            try
            {
                var markerPath = Path.Combine(Environment.CurrentDirectory, "UserData", CounterMigrationMarker);
                if (File.Exists(markerPath))
                {
                    return;
                }

                var vanillaMaxAttempts = ScavHelper.GetMaxScavAttempts();
                if (store.scavengingAttempts > vanillaMaxAttempts && ScavengingCounter.IsLegacyInjectedCount(MaxAttempts, store.scavengingAttempts))
                {
                    store.scavengingAttempts = 0;
                    MelonLogger.Warning("ScavengingTweaks migrated the previous invalid scavenging counter to 0.");
                }

                File.WriteAllText(markerPath, "migrated");
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not migrate the previous scavenging counter.", exception);
            }
        }

        private static bool TryGetBaseValue(string identifier, out long value)
        {
            try
            {
                var item = DirectoryMaster.Item(identifier, false);
                if (item == null)
                {
                    value = 0L;
                    return false;
                }

                var baseValue = item.GetBaseValue();
                var complexValue = item.GetComplexeItemValue();
                var unitBaseValue = item.unitBaseValue;
                var unitValue = item.unitValue;
                var lateUnitValue = item.lateUnitValue;
                var backupUnitValue = item.backupUnitValue;
                value = LootWeighting.GetEffectiveValue(
                    baseValue,
                    complexValue,
                    unitBaseValue,
                    unitValue,
                    lateUnitValue,
                    backupUnitValue);
                return value > 0L;
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not resolve loot value for " + identifier, exception);
                value = 0L;
                return false;
            }
        }

    }
}
