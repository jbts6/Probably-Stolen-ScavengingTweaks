using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private static MelonPreferences_Entry<string> itemTypeMultipliers = null!;
    private static MelonPreferences_Entry<float> groundGridMultiplier = null!;
    private static Dictionary<string, float> parsedTypeMultipliers = new();
    private static Dictionary<string, List<string>> itemIdToTypes = new(); // itemId -> list of type names
    private static bool counterMigrationChecked;
    private static bool inScavengeWindow;
    private static long scavengeWindowTick = long.MinValue;
    private static long scavengeAttemptSequence;
    private static long activeScavengeAttempt;
    private static bool activeAttemptObservedRoll;
    private static bool blockedWoundDuringAttempt;
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
        itemTypeMultipliers = category.CreateEntry<string>(
            "ItemTypeMultipliers",
            "MODULE:3.0,TOOL:2.0,ALCOHOL:0.3",
            "Item type weight multipliers",
            "Comma-separated list of type:multiplier pairs (e.g., MODULE:3.0,ALCOHOL:0.3). Applied before high-value multiplier.",
            false,
            false,
            null);
        groundGridMultiplier = category.CreateEntry<float>(
            "GroundGridMultiplier",
            3.0f,
            "Ground grid height multiplier",
            "Multiplier applied to the dumping-grounds ground loot grid height. 1.0 keeps the vanilla size.",
            false,
            false,
            null);

        ParseItemTypeMultipliers();
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

    // 根据Table名称推断物品类型
    private static List<string>? InferTypesFromTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        var name = tableName.ToLowerInvariant();

        // 模块表
        if (name.Contains("module"))
        {
            return new List<string> { "MODULE" };
        }

        // 工具表
        if (name.Contains("tool"))
        {
            return new List<string> { "TOOL" };
        }

        // 食物表（packedFoodTable包含酒精饮料，需要添加ALCOHOL类型）
        if (name.Contains("food"))
        {
            return new List<string> { "ALCOHOL", "FOOD", "PROCESSED_FOOD" };
        }

        // 医疗表
        if (name.Contains("medical"))
        {
            return new List<string> { "MEDICAL" };
        }

        // 武器表
        if (name.Contains("weapon"))
        {
            return new List<string> { "WEAPON" };
        }

        // 材料表 - 可能包含多种类型
        if (name.Contains("material"))
        {
            return new List<string> { "MATERIAL" };
        }

        // household表可能包含酒精和物质
        if (name.Contains("household"))
        {
            return new List<string> { "ALCOHOL", "SUBSTANCE" };
        }

        return null;
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
    private static double GroundGridMultiplier => Math.Max(1.0, groundGridMultiplier.Value);
    private static bool BlockAllWounds => string.Equals(woundBlockMode.Value, "Always", StringComparison.OrdinalIgnoreCase);

    private static void ParseItemTypeMultipliers()
    {
        parsedTypeMultipliers.Clear();
        var config = itemTypeMultipliers.Value;
        if (string.IsNullOrWhiteSpace(config))
        {
            return;
        }

        var pairs = config.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var type = parts[0].Trim().ToUpperInvariant();
                if (float.TryParse(parts[1].Trim(), out var multiplier) && multiplier >= 0)
                {
                    parsedTypeMultipliers[type] = multiplier;
                }
            }
        }

        if (parsedTypeMultipliers.Count > 0)
        {
            MelonLogger.Msg("ScavengingTweaks item type multipliers: {0}", string.Join(", ", parsedTypeMultipliers.Select(kv => $"{kv.Key}:{kv.Value:0.##}x")));
        }
    }

    private static void EnsureGroundGridEnlarged()
    {
        try
        {
            var settings = UISettings.current;
            if (settings != null)
            {
                var originalSetting = GroundGridState.GetOriginalSettingHeight(settings.floorLootGridHeight);
                var settingTarget = GroundGridMath.ComputeTargetHeight(originalSetting, GroundGridMultiplier);
                if (settingTarget > 0)
                {
                    settings.floorLootGridHeight = settingTarget;
                }
            }

            var master = GameMaster.current;
            if (master == null)
            {
                return;
            }

            var raidManager = master.raidManager;
            var room = raidManager?.currentRoom;
            var floor = room?.floorInventory;
            var shape = floor?.inventoryShape;
            if (room == null || floor == null || shape == null)
            {
                return;
            }

            var originalRoomHeight = GroundGridState.GetOriginalRoomHeight(room.roomId, shape.height);
            var target = GroundGridMath.ComputeTargetHeight(originalRoomHeight, GroundGridMultiplier);
            if (target < 0 || shape.height >= target)
            {
                return;
            }

            var width = shape.width;
            var window = master.raidScene?.groundLootWindow;
            var windowWidth = window?.widthPixels ?? 0;
            var windowHeight = window?.heightPixels ?? 0;
            var gridWidthBefore = floor.widthPixels;
            var gridHeightBefore = floor.heightPixels;

            ForceRebuildGrid(floor);
            floor.SetShape(width, target);

            var widthDelta = floor.widthPixels - gridWidthBefore;
            var heightDelta = floor.heightPixels - gridHeightBefore;
            if (window != null && (widthDelta != 0 || heightDelta != 0))
            {
                window.ResizePixels(windowWidth + widthDelta, windowHeight + heightDelta);
                window.Validate();
            }

            MelonLogger.Msg(
                "ScavengingTweaks ground grid enlarged: room={0} {1}x{2} -> {1}x{3} (window {4}x{5} -> {6}x{7}).",
                room.roomId,
                width,
                shape.height,
                target,
                windowWidth,
                windowHeight,
                windowWidth + widthDelta,
                windowHeight + heightDelta);
        }
        catch (Exception exception)
        {
            MelonLogger.Error("ScavengingTweaks could not enlarge the ground grid.", exception);
        }
    }

    // 偏移取自 ContainerUpgrade.dll 在当前游戏版本的验证实现（_lastShapeHash 等渲染缓存字段）。
    // 版本升级后失效的表现只是网格不刷新，不会崩溃。
    private static void ForceRebuildGrid(GameGridInventory grid)
    {
        var pointer = grid.Pointer;
        Marshal.WriteByte(pointer, 360, 1);
        Marshal.WriteByte(pointer, 452, 0);
        Marshal.WriteInt64(pointer, 440, -1L);
        Marshal.WriteInt32(pointer, 448, 0);
    }

    private static void InstallPatches()
    {
        var harmony = new HarmonyLib.Harmony(HarmonyId);
        Patch(harmony, typeof(ScavHelper), "GetScavTimeLeft", postfix: nameof(Patches.GetScavTimeLeftPostfix));
        Patch(harmony, typeof(ScavHelper), "CanScavenge", prefix: nameof(Patches.CanScavengePrefix), postfix: nameof(Patches.CanScavengePostfix));
        Patch(harmony, typeof(ScavHelper), "GetMinorWoundChance", postfix: nameof(Patches.ZeroChancePostfix));
        Patch(harmony, typeof(ScavHelper), "GetMajorWoundChance", postfix: nameof(Patches.ZeroChancePostfix));
        Patch(harmony, typeof(ScavHelper), "RollMinorWound", postfix: nameof(Patches.RollMinorWoundPostfix));
        Patch(harmony, typeof(ScavHelper), "RollMajorWound", postfix: nameof(Patches.RollMajorWoundPostfix));
        Patch(harmony, typeof(ScavHelper), "ScavengeDumpingGrounds", prefix: nameof(Patches.ScavengePrefix), postfix: nameof(Patches.ScavengePostfix));
        Patch(harmony, typeof(MapUIManager), "VisitScavenging", postfix: nameof(Patches.VisitScavengingPostfix));
        Patch(harmony, typeof(ScavHelper), "GetRandomScavengedItem", postfix: nameof(Patches.ScavengedItemResultPostfix));
        Patch(
            harmony,
            typeof(ItemSpawner),
            "SpawnFromTableGroup",
            prefix: nameof(Patches.SpawnFromTableGroupPrefix),
            postfix: nameof(Patches.SpawnFromTableGroupPostfix),
            finalizer: nameof(Patches.SpawnFromTableGroupFinalizer));
        // 重新启用 HealthData Hook - 至少阻止真的受伤
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
        // 保存拾荒窗口期间的所有权重恢复操作，在窗口关闭时统一恢复
        private static readonly List<TableWeightState> pendingRestores = new List<TableWeightState>();

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
            var original = __result;
            __result = 0.0f;
            MelonLogger.Msg("ScavengingTweaks zero chance called: original={0}, overridden to 0.", original);
        }

        public static void RollMinorWoundPostfix(ref bool __result)
        {
            var original = __result;
            MelonLogger.Msg("ScavengingTweaks RollMinorWound called: result={0}.", original);
            if (__result)
            {
                MelonLogger.Msg("ScavengingTweaks overriding minor wound roll to false.");
                __result = false;
            }
        }

        public static void RollMajorWoundPostfix(ref bool __result)
        {
            var original = __result;
            MelonLogger.Msg("ScavengingTweaks RollMajorWound called: result={0}.", original);
            if (__result)
            {
                MelonLogger.Msg("ScavengingTweaks overriding major wound roll to false.");
                __result = false;
            }
        }

        public static void ScavengePrefix()
        {
            EnsureGroundGridEnlarged();
            activeScavengeAttempt = ++scavengeAttemptSequence;
            activeAttemptObservedRoll = false;
            blockedWoundDuringAttempt = false;
            inScavengeWindow = true;
            scavengeWindowTick = Environment.TickCount64;
            pendingRestores.Clear();
            MelonLogger.Msg(
                "ScavengingTweaks scavenge attempt start: sequence={0}.",
                activeScavengeAttempt);
        }

        public static void ScavengePostfix()
        {
            // Postfix 不再需要手动生成，因为 Prefix 已经处理了
            if (activeScavengeAttempt != 0)
            {
                if (activeAttemptObservedRoll)
                {
                    MelonLogger.Msg(
                        "ScavengingTweaks scavenge attempt end: sequence={0}, items generated.",
                        activeScavengeAttempt);
                }
                else
                {
                    MelonLogger.Msg(
                        "ScavengingTweaks scavenge attempt end: sequence={0}, no items generated.",
                        activeScavengeAttempt);
                }
            }

            // 拾荒窗口关闭，恢复所有待恢复的权重
            RestoreAllPendingWeights();

            inScavengeWindow = false;
            activeScavengeAttempt = 0;
            blockedWoundDuringAttempt = false;
        }

        public static void VisitScavengingPostfix()
        {
            EnsureGroundGridEnlarged();
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

            blockedWoundDuringAttempt = true;
            MelonLogger.Msg("ScavengingTweaks blocked a {0} wound (window={1}).", kind, ScavengeWindowOpen);
            return false;
        }

        private static void ResolveBlockedWoundAttempt()
        {
            // 简化逻辑：只阻止受伤，接受空拾荒作为代价
            // 玩家不会受伤，可以继续拾荒，但偶尔会空手而归
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

            if (count == 0 && blockedWoundDuringAttempt)
            {
                MelonLogger.Msg(
                    "ScavengingTweaks empty scavenge due to blocked wound (sequence={0}).",
                    activeScavengeAttempt);
            }

            MelonLogger.Msg("ScavengingTweaks scavenge result: drops={0}.", count);

            // 记录每个拾取到的物品的详细信息
            if (__result != null && count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    var item = __result[i];
                    if (item != null)
                    {
                        try
                        {
                            MelonLogger.Msg("  ScavengingTweaks picked item #{0}:", i + 1);

                            // 尝试通过反射获取所有字段（首次拾取时，或者对 itemTypes 为空的物品）
                            var itemType = item.GetType();
                            if (i == 0 && scavengeAttemptSequence == 1)
                            {
                                var fields = itemType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                MelonLogger.Msg("    GameItem available fields: {0}", string.Join(", ", fields.Select(f => f.Name)));

                                var properties = itemType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                MelonLogger.Msg("    GameItem available properties: {0}", string.Join(", ", properties.Select(p => p.Name)));
                            }

                            // 对于 itemTypes 为空的物品，打印所有属性和值
                            if (item.itemTypes == null || item.itemTypes.Count == 0)
                            {
                                MelonLogger.Msg("    [DEBUG] itemTypes is empty, dumping all properties:");
                                var allProperties = itemType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                foreach (var prop in allProperties)
                                {
                                    if (!prop.CanRead) continue;
                                    try
                                    {
                                        var value = prop.GetValue(item);
                                        var valueStr = value?.ToString() ?? "(null)";
                                        if (valueStr.Length > 100) valueStr = valueStr.Substring(0, 100) + "...";
                                        MelonLogger.Msg("      {0} = {1}", prop.Name, valueStr);
                                    }
                                    catch (Exception ex)
                                    {
                                        MelonLogger.Msg("      {0} = (error: {1})", prop.Name, ex.Message);
                                    }
                                }
                            }

                            // 尝试通过反射获取物品ID - 尝试字段、属性、Il2Cpp backing fields、ToString
                            string? capturedItemId = null;
                            try
                            {
                                // 尝试字段
                                var idField = itemType.GetField("id") ?? itemType.GetField("ID") ?? itemType.GetField("m_id") ?? itemType.GetField("itemID") ?? itemType.GetField("m_ItemID");
                                if (idField != null)
                                {
                                    var itemId = idField.GetValue(item) as string;
                                    capturedItemId = itemId;
                                    MelonLogger.Msg("    id (field): {0}", itemId ?? "(null)");
                                }

                                // 尝试属性
                                if (string.IsNullOrEmpty(capturedItemId))
                                {
                                    var idProp = itemType.GetProperty("id") ?? itemType.GetProperty("ID") ?? itemType.GetProperty("ItemId") ?? itemType.GetProperty("ItemID");
                                    if (idProp != null && idProp.CanRead)
                                    {
                                        var itemId = idProp.GetValue(item) as string;
                                        capturedItemId = itemId;
                                        MelonLogger.Msg("    id (property): {0}", itemId ?? "(null)");
                                    }
                                }

                                // 尝试 Il2Cpp backing field（适用于所有物品）
                                if (string.IsNullOrEmpty(capturedItemId))
                                {
                                    var identifierProp = itemType.GetProperty("identifier");
                                    if (identifierProp != null && identifierProp.CanRead)
                                    {
                                        var itemId = identifierProp.GetValue(item) as string;
                                        if (!string.IsNullOrEmpty(itemId))
                                        {
                                            capturedItemId = itemId;
                                            MelonLogger.Msg("    id (identifier property): {0}", itemId);
                                        }
                                    }
                                }

                                // 尝试ToString()作为后备
                                if (string.IsNullOrEmpty(capturedItemId))
                                {
                                    var toStringResult = item.ToString();
                                    if (!string.IsNullOrEmpty(toStringResult) && toStringResult != "GameItem" && toStringResult != item.GetType().Name)
                                    {
                                        capturedItemId = toStringResult;
                                        MelonLogger.Msg("    id (ToString): {0}", toStringResult);
                                    }
                                    else
                                    {
                                        MelonLogger.Msg("    id: (not found, ToString={0})", toStringResult);
                                    }
                                }

                                // 显示该物品的价值
                                if (!string.IsNullOrEmpty(capturedItemId) && TryGetBaseValue(capturedItemId, out var itemValue))
                                {
                                    MelonLogger.Msg("    baseValue (from dictionary): {0}", itemValue);
                                }

                                // 尝试直接读取物品对象的价值字段（用于无法获取ID的物品，如挎包）
                                try
                                {
                                    var valueField = itemType.GetField("baseValue") ?? itemType.GetField("value") ?? itemType.GetField("m_Value") ?? itemType.GetField("m_BaseValue");
                                    if (valueField != null)
                                    {
                                        var directValue = valueField.GetValue(item);
                                        MelonLogger.Msg("    baseValue (from field): {0}", directValue ?? "(null)");
                                    }

                                    var valueProp = itemType.GetProperty("baseValue") ?? itemType.GetProperty("value") ?? itemType.GetProperty("Value") ?? itemType.GetProperty("BaseValue");
                                    if (valueProp != null && valueProp.CanRead)
                                    {
                                        var directValue = valueProp.GetValue(item);
                                        MelonLogger.Msg("    baseValue (from property): {0}", directValue ?? "(null)");
                                    }

                                    // 尝试获取 element 字段（GameItem 可能包含一个 GameItemElement）
                                    MelonLogger.Msg("    [DEBUG] Searching for element field...");
                                    var elementField = itemType.GetField("element") ?? itemType.GetField("Element") ?? itemType.GetField("m_Element");
                                    if (elementField != null)
                                    {
                                        MelonLogger.Msg("    [DEBUG] Found element field: {0}", elementField.Name);
                                        var elementObj = elementField.GetValue(item);
                                        if (elementObj != null)
                                        {
                                            var elementType = elementObj.GetType();
                                            MelonLogger.Msg("    element type: {0}", elementType.Name);

                                            // 尝试从 element 获取 ID
                                            var elementIdField = elementType.GetField("id") ?? elementType.GetField("ID") ?? elementType.GetField("m_id");
                                            if (elementIdField != null)
                                            {
                                                var elementId = elementIdField.GetValue(elementObj) as string;
                                                if (!string.IsNullOrEmpty(elementId))
                                                {
                                                    MelonLogger.Msg("    element.id: {0}", elementId);
                                                    capturedItemId = elementId;

                                                    // 尝试从字典获取价值
                                                    if (TryGetBaseValue(elementId, out var elemValue))
                                                    {
                                                        MelonLogger.Msg("    element.baseValue (from dictionary): {0}", elemValue);
                                                    }
                                                }
                                                else
                                                {
                                                    MelonLogger.Msg("    [DEBUG] element.id is null or empty");
                                                }
                                            }
                                            else
                                            {
                                                MelonLogger.Msg("    [DEBUG] element has no id field");
                                            }
                                        }
                                        else
                                        {
                                            MelonLogger.Msg("    [DEBUG] element field is null");
                                        }
                                    }
                                    else
                                    {
                                        MelonLogger.Msg("    [DEBUG] No element field found");
                                    }
                                }
                                catch (Exception valueEx)
                                {
                                    MelonLogger.Msg("    baseValue: (direct read failed - {0})", valueEx.Message);
                                }
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Msg("    id: (reflection failed - {0})", ex.Message);
                            }

                            // 打印 itemTypes - 正确遍历 Il2Cpp 列表
                            if (item.itemTypes != null && item.itemTypes.Count > 0)
                            {
                                MelonLogger.Msg("    itemTypes count: {0}", item.itemTypes.Count);
                                var collectedTypes = new List<string>();
                                for (int j = 0; j < item.itemTypes.Count; j++)
                                {
                                    var itemTypeEnum = item.itemTypes[j];
                                    var typeStr = itemTypeEnum.ToString().ToUpperInvariant();
                                    collectedTypes.Add(typeStr);
                                    MelonLogger.Msg("      [{0}]: {1}", j, typeStr);
                                }

                                // 收集并缓存itemTypes信息 - 即使没有itemId也缓存（用ToString或占位符）
                                if (!string.IsNullOrEmpty(capturedItemId))
                                {
                                    if (!itemIdToTypes.ContainsKey(capturedItemId))
                                    {
                                        itemIdToTypes[capturedItemId] = collectedTypes;
                                        MelonLogger.Msg("    -> 已缓存类型映射: {0} -> [{1}]", capturedItemId, string.Join(", ", collectedTypes));
                                    }
                                }
                                else
                                {
                                    // 如果无法获取itemId，至少打印类型信息供手动分析
                                    MelonLogger.Msg("    -> 无法缓存（itemId未获取），类型: [{0}]", string.Join(", ", collectedTypes));
                                }
                            }
                            else
                            {
                                MelonLogger.Msg("    itemTypes: (empty)");
                            }

                            // 打印 itemFeatures
                            if (item.itemFeatures != null && item.itemFeatures.Count > 0)
                            {
                                MelonLogger.Msg("    itemFeatures count: {0}", item.itemFeatures.Count);
                                for (int j = 0; j < item.itemFeatures.Count; j++)
                                {
                                    var feature = item.itemFeatures[j];
                                    if (feature != null)
                                    {
                                        MelonLogger.Msg("      [{0}]: {1}", j, feature.ToString());
                                    }
                                }
                            }
                            else
                            {
                                MelonLogger.Msg("    itemFeatures: (empty)");
                            }
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Msg("  ScavengingTweaks picked item #{0}: failed to inspect - {1}", i + 1, ex.Message);
                        }
                    }
                }
            }
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

                // 诊断：打印外层LootTable权重分布
                if (LoggedWeightTables.Count == 0)
                {
                    MelonLogger.Msg("ScavengingTweaks outer layer - {0} LootTables:", tableEntries.Count);
                    for (var i = 0; i < tableEntries.Count; i++)
                    {
                        var tableEntry = tableEntries[i];
                        var tableName = tableEntry?.Value?.name ?? "(unnamed)";
                        var tableWeight = tableEntry?.m_Weight ?? 0;
                        var itemCount = tableEntry?.Value?.table?.ProbabilityItems?.Count ?? 0;
                        MelonLogger.Msg("  Table #{0}: name={1}, weight={2}, items={3}", i + 1, tableName, tableWeight, itemCount);
                    }
                }

                // 第一步：分析每个Table的最高价值，用于外层权重调整
                var tableMaxValues = new List<long>();
                for (var tableIndex = 0; tableIndex < tableEntries.Count; tableIndex++)
                {
                    var tableEntry = tableEntries[tableIndex];
                    var lootTable = tableEntry?.Value;
                    var tableName = tableEntry?.Value?.name ?? "(unnamed)";
                    var items = lootTable?.table?.ProbabilityItems;

                    long maxValueInTable = 0;
                    if (items != null)
                    {
                        // 诊断：打印所有Table的物品ID（仅首次）
                        var shouldDebug = LoggedWeightTables.Count == 0;
                        if (shouldDebug)
                        {
                            MelonLogger.Msg("  DEBUG {0} items ({1} total):", tableName, items.Count);
                        }

                        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
                        {
                            var itemEntry = items[itemIndex];
                            var itemId = itemEntry?.Value;

                            // 诊断：打印每个物品的详细信息
                            if (shouldDebug)
                            {
                                var hasValue = TryGetBaseValue(itemId, out var debugValue);
                                MelonLogger.Msg("    item #{0}: id={1}, hasValue={2}, value={3}",
                                    itemIndex + 1, itemId ?? "(null)", hasValue, debugValue);
                            }

                            if (itemId != null && TryGetBaseValue(itemId, out var itemValue))
                            {
                                if (itemValue > maxValueInTable)
                                {
                                    maxValueInTable = itemValue;
                                }
                            }
                        }
                    }
                    tableMaxValues.Add(maxValueInTable);

                    // 诊断：打印每个Table的最高价值
                    if (LoggedWeightTables.Count == 0)
                    {
                        MelonLogger.Msg("  Table {0} ({1}): maxValue={2}", tableName, tableIndex + 1, maxValueInTable);
                    }
                }

                // 第二步：调整外层Table权重（提升高价值Table的选中率 + 降低低权重类型Table）
                var overallMaxValue = 0L;
                for (var i = 0; i < tableMaxValues.Count; i++)
                {
                    if (tableMaxValues[i] > overallMaxValue)
                    {
                        overallMaxValue = tableMaxValues[i];
                    }
                }

                var highValueThreshold = (long)(overallMaxValue * 0.6);
                for (var tableIndex = 0; tableIndex < tableEntries.Count; tableIndex++)
                {
                    var tableEntry = tableEntries[tableIndex];
                    if (tableEntry == null)
                    {
                        continue;
                    }

                    var tableMaxValue = tableMaxValues[tableIndex];
                    var tableName = tableEntry.Value?.name ?? "(unnamed)";

                    // 保存外层Table权重的恢复操作
                    var originalBaseProbability = tableEntry.m_BaseProbability;
                    __state.RestoreActions.Add(new Action<int>(w => tableEntry.m_BaseProbability = w / 100f));
                    __state.OriginalWeights.Add((int)(originalBaseProbability * 100f));

                    // 检查该Table是否需要应用类型权重倍率
                    float tableTypeMultiplier = 1.0f;
                    if (parsedTypeMultipliers.Count > 0)
                    {
                        var inferredTypes = InferTypesFromTableName(tableName);
                        if (inferredTypes != null)
                        {
                            // 查找第一个匹配的类型倍率
                            foreach (var inferredType in inferredTypes)
                            {
                                if (parsedTypeMultipliers.TryGetValue(inferredType, out var multiplier))
                                {
                                    tableTypeMultiplier = multiplier;
                                    break;
                                }
                            }
                        }
                    }

                    // 组合调整：高价值提升 × 类型倍率
                    float finalMultiplier = 1.0f;
                    bool adjusted = false;

                    // 如果Table包含高价值物品，放大其权重
                    if (tableMaxValue >= highValueThreshold)
                    {
                        finalMultiplier *= (float)HighValueMultiplier;
                        adjusted = true;
                    }

                    // 应用类型倍率（无论是否高价值）
                    if (Math.Abs(tableTypeMultiplier - 1.0f) > 0.001f)
                    {
                        finalMultiplier *= tableTypeMultiplier;
                        adjusted = true;
                    }

                    if (adjusted)
                    {
                        tableEntry.m_BaseProbability = (float)(originalBaseProbability * finalMultiplier);
                        if (LoggedWeightTables.Count == 0)
                        {
                            MelonLogger.Msg(
                                "ScavengingTweaks adjusted outer Table: name={0}, maxValue={1}, typeMultiplier={2}x, highValueBoost={3}, originalProb={4:F4} -> newProb={5:F4}.",
                                tableName,
                                tableMaxValue,
                                tableTypeMultiplier,
                                tableMaxValue >= highValueThreshold ? "YES" : "NO",
                                originalBaseProbability,
                                tableEntry.m_BaseProbability);
                        }
                    }
                }

                // 第三步：收集所有物品用于内层权重调整
                var ids = new List<string>();
                var weights = new List<int>();
                var values = new Dictionary<string, long>(StringComparer.Ordinal);
                var itemEntries = new List<Il2CppRNGNeeds.ProbabilityItem<string>>();
                var tableItemCounts = new List<int>(); // 记录每个table的物品数量
                var itemToTableName = new Dictionary<string, string>(StringComparer.Ordinal); // 记录每个itemId所属的Table名称
                for (var tableIndex = 0; tableIndex < tableEntries.Count; tableIndex++)
                {
                    var tableEntry = tableEntries[tableIndex];
                    var lootTable = tableEntry?.Value;
                    var tableName = lootTable?.name ?? "(unnamed)";
                    var items = lootTable?.table?.ProbabilityItems;
                    if (items == null)
                    {
                        tableItemCounts.Add(0);
                        continue;
                    }

                    var itemsInThisTable = 0;
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
                        itemsInThisTable++;
                        if (itemId != null && !values.ContainsKey(itemId) && TryGetBaseValue(itemId, out var value))
                        {
                            values[itemId] = value;
                        }

                        // 记录itemId到Table名称的映射
                        if (!string.IsNullOrWhiteSpace(itemId) && !itemToTableName.ContainsKey(itemId))
                        {
                            itemToTableName[itemId] = tableName;
                        }
                    }
                    tableItemCounts.Add(itemsInThisTable);
                }

                if (ids.Count == 0)
                {
                    return;
                }

                // 诊断：显示价值排序前后各5个物品
                if (LoggedWeightTables.Count == 0)
                {
                    var sortedByValue = new List<(string id, long value)>();
                    var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var id = ids[i];
                        if (!string.IsNullOrWhiteSpace(id) && uniqueIds.Add(id) && values.ContainsKey(id))
                        {
                            sortedByValue.Add((id, values[id]));
                        }
                    }
                    sortedByValue.Sort((a, b) => b.value.CompareTo(a.value));

                    MelonLogger.Msg("ScavengingTweaks value diagnostic - TOP 5 highest-value items:");
                    for (var i = 0; i < Math.Min(5, sortedByValue.Count); i++)
                    {
                        MelonLogger.Msg("  #{0}: {1} = {2}", i + 1, sortedByValue[i].id, sortedByValue[i].value);
                    }

                    if (sortedByValue.Count > 5)
                    {
                        MelonLogger.Msg("ScavengingTweaks value diagnostic - BOTTOM 5 lowest-value items:");
                        for (var i = Math.Max(0, sortedByValue.Count - 5); i < sortedByValue.Count; i++)
                        {
                            MelonLogger.Msg("  #{0}: {1} = {2}", sortedByValue.Count - i, sortedByValue[i].id, sortedByValue[i].value);
                        }
                    }

                    // 打印完整物品列表
                    MelonLogger.Msg("ScavengingTweaks complete loot table - all {0} unique items:", sortedByValue.Count);
                    for (var i = 0; i < sortedByValue.Count; i++)
                    {
                        MelonLogger.Msg("  {0}. {1} = {2}", i + 1, sortedByValue[i].id, sortedByValue[i].value);
                    }

                    // 打印没有价值数据的物品
                    var noValueIds = new List<string>();
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var id = ids[i];
                        if (!string.IsNullOrWhiteSpace(id) && uniqueIds.Add(id) && !values.ContainsKey(id))
                        {
                            noValueIds.Add(id);
                        }
                    }
                    if (noValueIds.Count > 0)
                    {
                        MelonLogger.Msg("ScavengingTweaks items without value data ({0} items):", noValueIds.Count);
                        for (var i = 0; i < noValueIds.Count; i++)
                        {
                            MelonLogger.Msg("  {0}. {1} (no value)", i + 1, noValueIds[i]);
                        }
                    }
                }

                // 第四步：应用物品类型权重倍率（第一层调整）
                if (parsedTypeMultipliers.Count > 0)
                {
                    var typeMultiplierApplied = 0;
                    for (var index = 0; index < weights.Count; index++)
                    {
                        var itemId = ids[index];
                        if (string.IsNullOrWhiteSpace(itemId))
                        {
                            continue;
                        }

                        // 通过Table名称推断物品类型
                        List<string>? types = null;
                        if (itemToTableName.TryGetValue(itemId, out var tableName))
                        {
                            types = InferTypesFromTableName(tableName);
                        }

                        if (types == null || types.Count == 0)
                        {
                            continue;
                        }

                        // 查找该物品是否匹配任何配置的类型
                        float typeMultiplier = 1.0f;
                        bool foundMatch = false;
                        string matchedType = "";
                        foreach (var itemType in types)
                        {
                            if (parsedTypeMultipliers.TryGetValue(itemType, out var multiplier))
                            {
                                typeMultiplier = multiplier;
                                foundMatch = true;
                                matchedType = itemType;
                                break; // 使用第一个匹配的类型
                            }
                        }

                        if (foundMatch && Math.Abs(typeMultiplier - 1.0f) > 0.001f)
                        {
                            var originalWeight = weights[index];
                            var scaledWeight = (int)Math.Round(originalWeight * typeMultiplier);
                            weights[index] = Math.Max(0, Math.Min(int.MaxValue, scaledWeight));
                            typeMultiplierApplied++;

                            // 诊断：打印所有被调整的物品（按table分组）
                            if (LoggedWeightTables.Count == 0)
                            {
                                MelonLogger.Msg(
                                    "ScavengingTweaks type multiplier: itemId={0}, table={1}, type={2}, multiplier={3}x, weight {4} -> {5}",
                                    itemId,
                                    tableName,
                                    matchedType,
                                    typeMultiplier,
                                    originalWeight,
                                    weights[index]);
                            }
                        }
                    }

                    if (LoggedWeightTables.Count == 0)
                    {
                        MelonLogger.Msg("ScavengingTweaks applied type multipliers to {0} items", typeMultiplierApplied);
                    }
                }

                var scaledWeights = LootWeighting.ScaleHighValueWeights(ids, weights, values, HighValueMultiplier);

                // 第五步：选择探针样本并记录 BEFORE 状态
                var diagnosticSampleIndex = -1;
                var diagnosticLowValueIndex = -1;
                for (var index = 0; index < scaledWeights.Count; index++)
                {
                    if (scaledWeights[index] != weights[index] && !string.IsNullOrWhiteSpace(ids[index]))
                    {
                        if (diagnosticSampleIndex < 0)
                        {
                            // 找一个被放大的高价值物品
                            var itemId = ids[index];
                            if (values.ContainsKey(itemId) && values[itemId] >= 60)
                            {
                                diagnosticSampleIndex = index;
                                var itemEntry = itemEntries[index];
                                var probeId = ids[index];
                                var probeValue = values.ContainsKey(probeId) ? values[probeId] : -1L;
                                MelonLogger.Msg(
                                    "ScavengingTweaks high-value probe BEFORE: id={0}, value={1}, m_Weight={2}, m_BaseProbability={3:F4}, BaseProbability={4:F4}, Probability={5:F4}.",
                                    probeId,
                                    probeValue,
                                    itemEntry.m_Weight,
                                    itemEntry.m_BaseProbability,
                                    itemEntry.BaseProbability,
                                    itemEntry.Probability);
                            }
                        }

                        if (diagnosticLowValueIndex < 0)
                        {
                            // 找一个被降低的低价值物品
                            var itemId = ids[index];
                            if (values.ContainsKey(itemId) && values[itemId] < 5)
                            {
                                diagnosticLowValueIndex = index;
                                var itemEntry = itemEntries[index];
                                var probeId = ids[index];
                                var probeValue = values.ContainsKey(probeId) ? values[probeId] : -1L;
                                MelonLogger.Msg(
                                    "ScavengingTweaks low-value probe BEFORE: id={0}, value={1}, m_Weight={2}, m_BaseProbability={3:F4}, BaseProbability={4:F4}, Probability={5:F4}.",
                                    probeId,
                                    probeValue,
                                    itemEntry.m_Weight,
                                    itemEntry.m_BaseProbability,
                                    itemEntry.BaseProbability,
                                    itemEntry.Probability);
                            }
                        }

                        if (diagnosticSampleIndex >= 0 && diagnosticLowValueIndex >= 0)
                        {
                            break;
                        }
                    }
                }

                // 第四步：调整内层物品的 m_BaseProbability
                for (var index = 0; index < scaledWeights.Count; index++)
                {
                    var itemEntry = itemEntries[index];
                    __state.RestoreActions.Add(new Action<int>(w => itemEntry.m_BaseProbability = (float)w / 100f));
                    __state.OriginalWeights.Add((int)(itemEntry.m_BaseProbability * 100f));
                    if (scaledWeights[index] == weights[index])
                    {
                        continue;
                    }

                    // 按比例缩放 m_BaseProbability
                    var scaleFactor = (double)scaledWeights[index] / Math.Max(1, weights[index]);
                    itemEntry.m_BaseProbability = (float)(itemEntry.m_BaseProbability * scaleFactor);
                    __state.AdjustedEntries++;
                }

                // 第六步：记录探针样本的 AFTER 状态
                if (diagnosticSampleIndex >= 0)
                {
                    var sampleEntry = itemEntries[diagnosticSampleIndex];
                    var probeId = ids[diagnosticSampleIndex];
                    var probeValue = values.ContainsKey(probeId) ? values[probeId] : -1L;
                    MelonLogger.Msg(
                        "ScavengingTweaks high-value probe AFTER: id={0}, value={1}, m_Weight={2}, m_BaseProbability={3:F4}, BaseProbability={4:F4}, Probability={5:F4}.",
                        probeId,
                        probeValue,
                        sampleEntry.m_Weight,
                        sampleEntry.m_BaseProbability,
                        sampleEntry.BaseProbability,
                        sampleEntry.Probability);
                }

                if (diagnosticLowValueIndex >= 0)
                {
                    var lowEntry = itemEntries[diagnosticLowValueIndex];
                    var lowProbeId = ids[diagnosticLowValueIndex];
                    var lowProbeValue = values.ContainsKey(lowProbeId) ? values[lowProbeId] : -1L;
                    MelonLogger.Msg(
                        "ScavengingTweaks low-value probe AFTER: id={0}, value={1}, m_Weight={2}, m_BaseProbability={3:F4}, BaseProbability={4:F4}, Probability={5:F4}.",
                        lowProbeId,
                        lowProbeValue,
                        lowEntry.m_Weight,
                        lowEntry.m_BaseProbability,
                        lowEntry.BaseProbability,
                        lowEntry.Probability);
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
            // 不立即恢复，而是加入待恢复列表
            if (__state != null && __state.AdjustedEntries > 0)
            {
                pendingRestores.Add(__state);
            }
        }

        public static Exception? SpawnFromTableGroupFinalizer(Exception? __exception, TableWeightState __state)
        {
            // Finalizer 也不立即恢复，等待拾荒窗口关闭
            if (__exception != null && __state != null && !__state.Restored)
            {
                // 如果发生异常，立即恢复这一次的权重
                RestoreTableWeights(__state);
            }
            return __exception;
        }

        private static void RestoreAllPendingWeights()
        {
            if (pendingRestores.Count == 0)
            {
                return;
            }

            MelonLogger.Msg("ScavengingTweaks restoring {0} weight tables after scavenge window.", pendingRestores.Count);
            foreach (var state in pendingRestores)
            {
                RestoreTableWeights(state);
            }
            pendingRestores.Clear();
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
            // 处理随机模块占位符 - 映射到实际高价值模块的平均值
            if (identifier == "random_performance_module" ||
                identifier == "random_efficiency_module" ||
                identifier == "random_quality_module")
            {
                // 这些占位符会随机替换为价值100的模块，使用100作为评估值
                value = 100L;
                return true;
            }

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
