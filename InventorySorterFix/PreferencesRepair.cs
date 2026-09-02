using System;
using System.IO;
using System.Text;

namespace InventorySorterFix;

public static class PreferencesRepair
{
    public static string StripNulCharacters(string content)
    {
        return (content ?? string.Empty).Replace("\0", string.Empty, StringComparison.Ordinal);
    }

    public static string RepairContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        var result = new StringBuilder(content.Length);
        var position = 0;
        var seenInventorySorterTable = false;
        var skipDuplicateInventorySorterTable = false;

        while (position < content.Length)
        {
            var lineEnd = position;
            while (lineEnd < content.Length && content[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            var nextPosition = lineEnd;
            if (nextPosition < content.Length && content[nextPosition] == '\r')
            {
                nextPosition++;
                if (nextPosition < content.Length && content[nextPosition] == '\n')
                {
                    nextPosition++;
                }
            }
            else if (nextPosition < content.Length)
            {
                nextPosition++;
            }

            var segment = content.Substring(position, nextPosition - position);
            var cleanLine = StripNulCharacters(content.Substring(position, lineEnd - position));
            if (IsTableHeader(cleanLine))
            {
                skipDuplicateInventorySorterTable = false;
                if (string.Equals(TryGetTableName(cleanLine), "物品整理", StringComparison.Ordinal))
                {
                    skipDuplicateInventorySorterTable = seenInventorySorterTable;
                    seenInventorySorterTable = true;
                }
            }

            if (!skipDuplicateInventorySorterTable)
            {
                result.Append(StripNulCharacters(segment));
            }

            position = nextPosition;
        }

        return result.ToString();
    }

    public static bool SanitizeFile(string path, string backupPath)
    {
        string? temporaryPath = null;

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var content = File.ReadAllText(path);
            var cleaned = RepairContent(content);
            if (string.Equals(content, cleaned, StringComparison.Ordinal))
            {
                return false;
            }

            if (!File.Exists(backupPath))
            {
                File.Copy(path, backupPath, overwrite: false);
            }

            temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporaryPath, cleaned, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"InventorySorterFix failed to sanitize '{path}': {exception}");
            return false;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"InventorySorterFix failed to remove temporary file '{temporaryPath}': {exception}");
                }
            }
        }
    }

    private static string? TryGetTableName(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed.StartsWith("[[", StringComparison.Ordinal) ||
            trimmed[0] != '[' || trimmed[^1] != ']')
        {
            return null;
        }

        var tableName = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (tableName.Length >= 2 && tableName[0] == '"' && tableName[^1] == '"')
        {
            tableName = tableName.Substring(1, tableName.Length - 2)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return tableName;
    }

    private static bool IsTableHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']';
    }
}
