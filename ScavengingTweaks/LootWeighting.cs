using System;
using System.Collections.Generic;

namespace ScavengingTweaks;

public static class LootWeighting
{
    private const int MaxExtraCopiesPerEntry = 32;

    public static List<string> ExpandHighValueEntries(
        IReadOnlyList<string> lootIds,
        IReadOnlyDictionary<string, long> baseValues,
        double multiplier)
    {
        var expanded = new List<string>();
        if (lootIds == null || lootIds.Count == 0)
        {
            return expanded;
        }

        var selectedIds = FindHighestValueHalf(lootIds, baseValues);
        var extraCopies = GetExtraCopies(multiplier);
        expanded.Capacity = lootIds.Count + selectedIds.Count * extraCopies;

        for (var index = 0; index < lootIds.Count; index++)
        {
            var lootId = lootIds[index];
            expanded.Add(lootId);

            if (!selectedIds.Contains(lootId))
            {
                continue;
            }

            for (var copy = 0; copy < extraCopies; copy++)
            {
                expanded.Add(lootId);
            }
        }

        return expanded;
    }

    private static HashSet<string> FindHighestValueHalf(
        IReadOnlyList<string> lootIds,
        IReadOnlyDictionary<string, long> baseValues)
    {
        var candidates = new List<LootCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < lootIds.Count; index++)
        {
            var lootId = lootIds[index];
            if (lootId == null || !seen.Add(lootId))
            {
                continue;
            }

            if (baseValues != null && baseValues.TryGetValue(lootId, out var value))
            {
                candidates.Add(new LootCandidate(lootId, value, index));
            }
        }

        candidates.Sort(static (left, right) =>
        {
            var valueOrder = right.Value.CompareTo(left.Value);
            return valueOrder != 0 ? valueOrder : left.FirstIndex.CompareTo(right.FirstIndex);
        });

        var selected = new HashSet<string>(StringComparer.Ordinal);
        var selectedCount = (candidates.Count + 1) / 2;
        for (var index = 0; index < selectedCount; index++)
        {
            selected.Add(candidates[index].Id);
        }

        return selected;
    }

    private static int GetExtraCopies(double multiplier)
    {
        if (double.IsNaN(multiplier) || multiplier <= 1.0)
        {
            return 0;
        }

        if (double.IsPositiveInfinity(multiplier))
        {
            return MaxExtraCopiesPerEntry;
        }

        var extraCopies = (int)Math.Floor(multiplier) - 1;
        return Math.Clamp(extraCopies, 0, MaxExtraCopiesPerEntry);
    }

    private readonly struct LootCandidate
    {
        public LootCandidate(string id, long value, int firstIndex)
        {
            Id = id;
            Value = value;
            FirstIndex = firstIndex;
        }

        public string Id { get; }
        public long Value { get; }
        public int FirstIndex { get; }
    }
}
