using System;
using System.IO;
using System.Text;
using InventorySorterFix;

internal static class InventorySorterFixTests
{
    private static int Main()
    {
        try
        {
            StripNulCharactersRemovesOnlyNuls();
            RepairContentRemovesOnlyDuplicateInventorySorterTable();
            SanitizeFileCreatesBackupAndPreservesOtherText();
            Console.WriteLine("InventorySorterFix tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void StripNulCharactersRemovesOnlyNuls()
    {
        var cleaned = PreferencesRepair.StripNulCharacters("[A]\0\nValue = 1\0");
        Ensure(cleaned == "[A]\nValue = 1", "only NUL characters should be removed");
        Ensure(PreferencesRepair.StripNulCharacters("plain") == "plain", "clean text should be unchanged");
    }

    private static void SanitizeFileCreatesBackupAndPreservesOtherText()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InventorySorterFixTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "MelonPreferences.cfg");
            var backupPath = Path.Combine(directory, "MelonPreferences.cfg.inventory-sorter-fix.bak");
            var original = "[A]\0\r\nValue = 1\0";
            File.WriteAllText(path, original, new UTF8Encoding(false));

            Ensure(PreferencesRepair.SanitizeFile(path, backupPath), "a file containing NUL characters should be sanitized");
            Ensure(File.ReadAllText(path) == "[A]\r\nValue = 1", "sanitizing should preserve non-NUL text and line endings");
            Ensure(File.ReadAllText(backupPath) == original, "sanitizing should preserve the original file in the backup");
            Ensure(!PreferencesRepair.SanitizeFile(path, backupPath), "an already clean file should not be rewritten");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void RepairContentRemovesOnlyDuplicateInventorySorterTable()
    {
        var content = "[物品整理]\r\n启用 = true\r\n\r\n[\"物品整理\0\"]\r\n\"启用\0\" = false\r\n\r\n[[Other.Items]]\r\nName = \"kept\"\r\n\r\n[Other]\r\nValue = 1\r\n[Other]\r\nValue = 2\r\n";
        var cleaned = PreferencesRepair.RepairContent(content);

        Ensure(cleaned.Contains("[物品整理]", StringComparison.Ordinal), "the first table should be preserved");
        Ensure(!cleaned.Contains("[\"物品整理\"]", StringComparison.Ordinal), "a duplicate quoted table should be removed");
        Ensure(cleaned.Contains("启用 = true", StringComparison.Ordinal), "the first table values should be preserved");
        Ensure(cleaned.Contains("[[Other.Items]]\r\nName = \"kept\"", StringComparison.Ordinal), "array tables after the duplicate should be preserved");
        Ensure(cleaned.Split("[Other]", StringSplitOptions.None).Length == 3, "unrelated duplicate tables should remain unchanged");
        Ensure(!cleaned.Contains('\0'), "NUL characters should be removed from the complete document");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + message);
        }
    }
}
