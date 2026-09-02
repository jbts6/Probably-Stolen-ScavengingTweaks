using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace ScavengingTweaks;

public sealed class Mod : MelonMod
{
    private const string HarmonyId = "ProbablyStolen.ScavengingTweaks";
    private static readonly HashSet<int> ExpandedLocationObjects = new();
    private static MelonPreferences_Entry<int> maxAttempts = null!;
    private static MelonPreferences_Entry<float> highValueMultiplier = null!;

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

        InstallPatches();
        MelonLogger.Msg("ScavengingTweaks loaded: attempts={0}, high-value multiplier={1:0.##}x", MaxAttempts, HighValueMultiplier);
    }

    private static int MaxAttempts => Math.Max(1, maxAttempts.Value);
    private static double HighValueMultiplier => Math.Max(1.0, highValueMultiplier.Value);

    private static void InstallPatches()
    {
        var harmony = new HarmonyLib.Harmony(HarmonyId);
        Patch(harmony, typeof(ScavHelper), "GetMaxScavAttempts", postfix: nameof(Patches.GetMaxScavAttemptsPostfix));
        Patch(harmony, typeof(ScavHelper), "GetMinorWoundChance", postfix: nameof(Patches.ZeroChancePostfix));
        Patch(harmony, typeof(ScavHelper), "GetMajorWoundChance", postfix: nameof(Patches.ZeroChancePostfix));
        Patch(harmony, typeof(ScavHelper), "RollMinorWound", prefix: nameof(Patches.NeverWoundPrefix));
        Patch(harmony, typeof(ScavHelper), "RollMajorWound", prefix: nameof(Patches.NeverWoundPrefix));
        Patch(harmony, typeof(ScavHelper), "ResetScavenging", postfix: nameof(Patches.ResetScavengingPostfix));
        Patch(harmony, typeof(PlayerStore), "BeginDay", postfix: nameof(Patches.BeginDayPostfix));
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
        public static void GetMaxScavAttemptsPostfix(ref int __result)
        {
            __result = MaxAttempts;
        }

        public static void ZeroChancePostfix(ref float __result)
        {
            __result = 0.0f;
        }

        public static bool NeverWoundPrefix(ref bool __result)
        {
            __result = false;
            return false;
        }

        public static void ResetScavengingPostfix()
        {
            try
            {
                ApplyConfiguredAttempts(PlayerStore.instance);
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks could not reset the configured scavenging attempts.", exception);
            }
        }

        public static void BeginDayPostfix(PlayerStore __instance)
        {
            ApplyConfiguredAttempts(__instance);
        }

        public static void DumpingGroundsPostfix(ref ExpeditionLocation __result)
        {
            try
            {
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
                if (expanded.Count <= ids.Count)
                {
                    return;
                }

                loot.Clear();
                foreach (var id in expanded)
                {
                    loot.Add(id);
                }

                MelonLogger.Msg(
                    "ScavengingTweaks expanded dump loot table from {0} to {1} entries.",
                    ids.Count,
                    expanded.Count);
            }
            catch (Exception exception)
            {
                MelonLogger.Error("ScavengingTweaks failed to adjust dump loot; original table is preserved.", exception);
            }
        }

        private static void ApplyConfiguredAttempts(PlayerStore store)
        {
            if (store != null)
            {
                store.scavengingAttempts = MaxAttempts;
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
