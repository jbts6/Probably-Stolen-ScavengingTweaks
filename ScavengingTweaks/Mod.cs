using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace ScavengingTweaks;

public sealed class Mod : MelonMod
{
    private const string HarmonyId = "ProbablyStolen.ScavengingTweaks";
    private const string CounterMigrationMarker = "ScavengingTweaks.counter-migrated-v2";
    private static readonly HashSet<int> ExpandedLocationObjects = new();
    private static MelonPreferences_Entry<int> maxAttempts = null!;
    private static MelonPreferences_Entry<float> highValueMultiplier = null!;
    private static MelonPreferences_Entry<string> woundBlockMode = null!;
    private static MelonPreferences_Entry<bool> testHotkeysEnabled = null!;
    private static MelonPreferences_Entry<string> endOfDayHotkey = null!;
    private static MelonPreferences_Entry<string> directScavengeHotkey = null!;
    private static bool counterMigrationChecked;
    private static bool inScavengeWindow;
    private static long scavengeWindowTick = long.MinValue;
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
        Patch(harmony, typeof(ScavHelper), "GetRandomScavengedItem", postfix: nameof(Patches.UpgradeRolledLootPostfix));
        Patch(harmony, typeof(HealthData), "ReceiveMinorWound", prefix: nameof(Patches.BlockMinorWoundPrefix));
        Patch(harmony, typeof(HealthData), "ReceiveMajorWound", prefix: nameof(Patches.BlockMajorWoundPrefix));
        Patch(harmony, typeof(ExpeditionLocationList), "DumpingGrounds", postfix: nameof(Patches.DumpingGroundsPostfix));
    }

    private static void Patch(
        HarmonyLib.Harmony harmony,
        Type targetType,
        string targetMethodName,
        string? prefix = null,
        string? postfix = null)
    {
        var target = AccessTools.Method(targetType, targetMethodName);
        if (target == null)
        {
            MelonLogger.Error("ScavengingTweaks could not find {0}.{1}", targetType.FullName, targetMethodName);
            return;
        }

        var prefixMethod = prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix);
        var postfixMethod = postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix);
        harmony.Patch(target, prefixMethod, postfixMethod);
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
            inScavengeWindow = true;
            scavengeWindowTick = Environment.TickCount64;
        }

        public static void ScavengePostfix()
        {
            inScavengeWindow = false;
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

        public static void DumpingGroundsPostfix(ref ExpeditionLocation __result)
        {
            try
            {
                // During EmporiumEntry the game reads DumpingGrounds to restore
                // dump state before the item directory and PlayerStore exist;
                // touching them here re-enters the init sequence and hangs the load.
                if (PlayerStore.instance == null)
                {
                    return;
                }

                if (__result == null)
                {
                    return;
                }

                var objectId = RuntimeHelpers.GetHashCode(__result);
                if (!ExpandedLocationObjects.Add(objectId))
                {
                    return;
                }

                var loot = __result.possibleLoot;
                if (loot == null || loot.Count == 0)
                {
                    return;
                }

                var ids = new List<string>(loot.Count);
                var values = new Dictionary<string, long>(StringComparer.Ordinal);
                for (var index = 0; index < loot.Count; index++)
                {
                    var id = loot[index];
                    ids.Add(id);
                    if (id != null && !values.ContainsKey(id) && TryGetBaseValue(id, out var value))
                    {
                        values[id] = value;
                    }
                }

                var expanded = LootWeighting.ExpandHighValueEntries(ids, values, HighValueMultiplier);
                UpdateBestLoot(values);

                // Diagnostic: expose the real base values so weighting issues are visible.
                var tableSummary = new System.Text.StringBuilder();
                foreach (var pair in values)
                {
                    if (tableSummary.Length > 0)
                    {
                        tableSummary.Append(", ");
                    }
                    tableSummary.Append(pair.Key).Append('=').Append(pair.Value);
                }
                MelonLogger.Msg(
                    "ScavengingTweaks dump table: [{0}] -> {1} entries.",
                    tableSummary.ToString(),
                    expanded.Count);

                if (expanded.Count <= ids.Count)
                {
                    return;
                }

                loot.Clear();
                foreach (var id in expanded)
                {
                    loot.Add(id);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks failed to adjust dump loot; original table is preserved.", exception);
            }
        }

        private static string bestLootId;
        private static long bestLootValue;

        private static void UpdateBestLoot(Dictionary<string, long> values)
        {
            foreach (var pair in values)
            {
                if (bestLootId == null || pair.Value > bestLootValue)
                {
                    bestLootId = pair.Key;
                    bestLootValue = pair.Value;
                }
            }
        }

        // The dump roll does not sample possibleLoot uniformly, so duplicating
        // table entries cannot shift the outcome. Reshape the rolled result
        // directly: low-value drops become the table's best item with
        // probability (multiplier-1)/multiplier.
        public static void UpgradeRolledLootPostfix(Il2CppSystem.Collections.Generic.List<GameItem> __result)
        {
            try
            {
                if (__result == null || __result.Count == 0 || bestLootId == null || HighValueMultiplier <= 1.0)
                {
                    return;
                }

                var upgradeChance = (HighValueMultiplier - 1.0) / HighValueMultiplier;
                var upgraded = 0;
                for (var index = 0; index < __result.Count; index++)
                {
                    var item = __result[index];
                    if (item == null)
                    {
                        continue;
                    }

                    long value;
                    try
                    {
                        value = item.GetBaseValue();
                    }
                    catch
                    {
                        continue;
                    }

                    if (value >= bestLootValue || Random.Shared.NextDouble() >= upgradeChance)
                    {
                        continue;
                    }

                    var replacement = DirectoryMaster.Item(bestLootId, false);
                    if (replacement == null)
                    {
                        continue;
                    }

                    __result[index] = replacement;
                    upgraded++;
                }

                if (upgraded > 0)
                {
                    MelonLogger.Msg("ScavengingTweaks upgraded {0}/{1} scavenged drops to {2}.", upgraded, __result.Count, bestLootId);
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks failed to upgrade scavenged loot.", exception);
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

                value = item.GetBaseValue();
                return true;
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
