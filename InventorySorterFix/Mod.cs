using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace InventorySorterFix;

public sealed class Mod : MelonMod
{
    private const string HarmonyId = "ProbablyStolen.InventorySorterFix";
    private const string CoreTypeName = "InventorySorter.Core";
    private const string PreferencesFileName = "MelonPreferences.cfg";
    private const string BackupFileSuffix = ".inventory-sorter-fix.bak";
    private static MelonPreferences_Entry<bool> forceDenseLayout = null!;
    private static int saveSkipLogged;
    private static int missingCoreLogged;
    private static int missingGroupByTagLogged;
    private static int processExitRegistered;
    private static FileSystemWatcher? preferencesWatcher;
    private static System.Threading.Timer? preferencesRepairTimer;
    private static readonly object preferencesWatcherLock = new();

    public override void OnInitializeMelon()
    {
        try
        {
            var category = MelonPreferences.CreateCategory("InventorySorterFix", "Inventory Sorter Fix");
            forceDenseLayout = category.CreateEntry<bool>(
                "ForceDenseLayout",
                true,
                "Force dense inventory layout",
                "Disable InventorySorter tag grouping so the compact layout can fit more items.",
                false,
                false,
                null);

            SanitizePreferences("startup");
            InstallPatches();
            RegisterProcessExitHandler();
            StartPreferenceWatcher();
            MelonLogger.Msg("InventorySorterFix loaded: ForceDenseLayout={0}", ForceDenseLayout);
        }
        catch (Exception exception)
        {
            MelonLogger.Error("InventorySorterFix failed to initialize; the original InventorySorter behavior is preserved.", exception);
        }
    }

    private static bool ForceDenseLayout => forceDenseLayout?.Value ?? true;

    private static void InstallPatches()
    {
        var harmony = new HarmonyLib.Harmony(HarmonyId);
        var save = AccessTools.Method(typeof(MelonPreferences), nameof(MelonPreferences.Save), Type.EmptyTypes);
        if (save == null)
        {
            MelonLogger.Error("InventorySorterFix could not find MelonPreferences.Save().");
        }
        else
        {
            try
            {
                var patched = harmony.Patch(save, new HarmonyMethod(typeof(Patches), nameof(Patches.MelonPreferencesSavePrefix)), null);
                if (patched is null)
                {
                    MelonLogger.Warning("InventorySorterFix could not detour managed MelonPreferences.Save(); Core caller fallback will be used.");
                }
            }
            catch (Exception exception)
            {
                MelonLogger.Warning("InventorySorterFix could not patch MelonPreferences.Save(); Core caller fallback will be used. " + exception.Message);
            }
        }

        PatchPreferenceFileSave(harmony);

        var coreType = AccessTools.TypeByName(CoreTypeName);
        if (coreType == null)
        {
            LogMissingCore();
            return;
        }

        PatchCoreMethod(
            harmony,
            coreType,
            "OnApplicationQuit",
            nameof(Patches.InventorySorterOnApplicationQuitPostfix),
            nameof(Patches.SanitizeInventorySorterTranspiler));
        PatchCoreMethod(
            harmony,
            coreType,
            "OnInitializeMelon",
            nameof(Patches.InventorySorterOnInitializePostfix),
            nameof(Patches.SanitizeInventorySorterTranspiler));

        // The original mod may have initialized before this compatibility mod.
        // Apply the preference immediately as well as from the lifecycle postfix.
        SetGroupByTag(null);
    }

    private static void PatchPreferenceFileSave(HarmonyLib.Harmony harmony)
    {
        var fileType = AccessTools.TypeByName("MelonLoader.Preferences.IO.File");
        var fileSave = fileType == null ? null : AccessTools.Method(fileType, "Save", Type.EmptyTypes);
        if (fileSave == null)
        {
            MelonLogger.Warning("InventorySorterFix could not find MelonLoader.Preferences.IO.File.Save(); post-save repair is unavailable.");
            return;
        }

        try
        {
            harmony.Patch(
                fileSave,
                postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.PreferencesFileSavePostfix)));
            MelonLogger.Msg("InventorySorterFix attached post-save preference repair.");
        }
        catch (Exception exception)
        {
            MelonLogger.Error("InventorySorterFix could not patch MelonLoader.Preferences.IO.File.Save(); post-save repair is unavailable.", exception);
        }
    }

    private static void PatchCoreMethod(
        HarmonyLib.Harmony harmony,
        Type coreType,
        string methodName,
        string postfixName,
        string transpilerName)
    {
        var method = AccessTools.Method(coreType, methodName, Type.EmptyTypes);
        if (method == null)
        {
            MelonLogger.Warning("InventorySorterFix could not find {0}.{1}(); original behavior is preserved.", CoreTypeName, methodName);
            return;
        }

        try
        {
            harmony.Patch(
                method,
                postfix: new HarmonyMethod(typeof(Patches), postfixName),
                transpiler: new HarmonyMethod(typeof(Patches), transpilerName));
        }
        catch (Exception exception)
        {
            MelonLogger.Error($"InventorySorterFix could not patch {CoreTypeName}.{methodName}(); original behavior is preserved.", exception);
        }
    }

    private static void SanitizePreferences(string phase)
    {
        try
        {
            var preferencesPath = Path.Combine(Environment.CurrentDirectory, "UserData", PreferencesFileName);
            var backupPath = preferencesPath + BackupFileSuffix;
            if (PreferencesRepair.SanitizeFile(preferencesPath, backupPath))
            {
                MelonLogger.Msg("InventorySorterFix repaired InventorySorter preference data during {0} repair.", phase);
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Error("InventorySorterFix could not repair MelonPreferences.cfg during " + phase + "; original behavior is preserved.", exception);
        }
    }

    private static void RegisterProcessExitHandler()
    {
        if (System.Threading.Interlocked.Exchange(ref processExitRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
    }

    private static void OnProcessExit(object? sender, EventArgs args)
    {
        SanitizePreferences("process-exit");
    }

    private static void StartPreferenceWatcher()
    {
        try
        {
            var directory = Path.Combine(Environment.CurrentDirectory, "UserData");
            if (!Directory.Exists(directory))
            {
                return;
            }

            preferencesWatcher = new FileSystemWatcher(directory, PreferencesFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            preferencesWatcher.Changed += QueuePreferenceRepair;
            preferencesWatcher.Created += QueuePreferenceRepair;
            preferencesWatcher.Renamed += QueuePreferenceRepair;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning("InventorySorterFix could not watch MelonPreferences.cfg; post-write repair relies on Harmony callbacks. " + exception.Message);
        }
    }

    private static void QueuePreferenceRepair(object? sender, FileSystemEventArgs args)
    {
        lock (preferencesWatcherLock)
        {
            preferencesRepairTimer ??= new System.Threading.Timer(
                _ => SanitizePreferences("file-watch"),
                null,
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);
            preferencesRepairTimer.Change(250, System.Threading.Timeout.Infinite);
        }
    }

    private static void LogMissingCore()
    {
        if (System.Threading.Interlocked.Exchange(ref missingCoreLogged, 1) == 0)
        {
            MelonLogger.Warning("InventorySorterFix could not resolve InventorySorter.Core; compatibility patches were skipped.");
        }
    }

    private static bool IsInventorySorterCaller()
    {
        try
        {
            var frames = new StackTrace().GetFrames();
            if (frames == null)
            {
                return false;
            }

            foreach (var frame in frames)
            {
                var typeName = frame.GetMethod()?.DeclaringType?.FullName;
                if (typeName == CoreTypeName || typeName?.StartsWith(CoreTypeName + "+", StringComparison.Ordinal) == true)
                {
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            MelonLogger.Error("InventorySorterFix could not inspect the MelonPreferences.Save() caller; original save behavior is preserved.", exception);
        }

        return false;
    }

    private static void LogSaveSkipped()
    {
        if (System.Threading.Interlocked.Exchange(ref saveSkipLogged, 1) == 0)
        {
            MelonLogger.Msg("InventorySorterFix skipped InventorySorter global configuration save.");
        }
    }

    private static void SetGroupByTag(object? coreInstance)
    {
        try
        {
            var coreType = AccessTools.TypeByName(CoreTypeName);
            var groupByTagField = coreType == null ? null : AccessTools.Field(coreType, "GroupByTag");
            if (groupByTagField == null)
            {
                LogMissingGroupByTag();
                return;
            }

            var target = groupByTagField.IsStatic ? null : coreInstance;
            if (groupByTagField.GetValue(target) is MelonPreferences_Entry<bool> entry)
            {
                entry.Value = !ForceDenseLayout;
                MelonLogger.Msg(
                    ForceDenseLayout
                        ? "InventorySorterFix disabled InventorySorter GroupByTag; dense layout remains active."
                        : "InventorySorterFix restored InventorySorter GroupByTag.");
                return;
            }

            LogMissingGroupByTag();
        }
        catch (Exception exception)
        {
            MelonLogger.Error("InventorySorterFix could not disable InventorySorter GroupByTag; original layout behavior is preserved.", exception);
        }
    }

    private static void LogMissingGroupByTag()
    {
        if (System.Threading.Interlocked.Exchange(ref missingGroupByTagLogged, 1) == 0)
        {
            MelonLogger.Warning("InventorySorterFix could not resolve InventorySorter.Core.GroupByTag; original grouping behavior is preserved.");
        }
    }

    private static class Patches
    {
        public static bool MelonPreferencesSavePrefix()
        {
            if (!IsInventorySorterCaller())
            {
                return true;
            }

            LogSaveSkipped();
            return false;
        }

        public static IEnumerable<CodeInstruction> SanitizeInventorySorterTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string text)
                {
                    instruction.operand = PreferencesRepair.StripNulCharacters(text);
                }

                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(MelonPreferences) &&
                    method.Name == nameof(MelonPreferences.Save) &&
                    method.GetParameters().Length == 0)
                {
                    LogSaveSkipped();
                    instruction.opcode = OpCodes.Nop;
                    instruction.operand = null;
                }

                yield return instruction;
            }
        }

        public static void InventorySorterOnApplicationQuitPostfix()
        {
            SanitizePreferences("quit");
        }

        public static void PreferencesFileSavePostfix()
        {
            SanitizePreferences("file-save");
        }

        public static void InventorySorterOnInitializePostfix(object? __instance)
        {
            SetGroupByTag(__instance);
        }
    }
}
