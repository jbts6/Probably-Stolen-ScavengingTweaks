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

    /// <summary>Scales the base weights of the highest-value half of a table.</summary>
    public static List<int> ScaleHighValueWeights(
        IReadOnlyList<string> lootIds,
        IReadOnlyList<int> baseWeights,
        IReadOnlyDictionary<string, long> baseValues,
        double multiplier)
    {
        var scaled = new List<int>();
        if (baseWeights == null || baseWeights.Count == 0)
        {
            return scaled;
        }

        scaled.Capacity = baseWeights.Count;
        for (var index = 0; index < baseWeights.Count; index++)
        {
            scaled.Add(Math.Max(0, baseWeights[index]));
        }

        if (lootIds == null || lootIds.Count == 0 || multiplier <= 1.0 || double.IsNaN(multiplier))
        {
            return scaled;
        }

        var selectedIds = FindHighestValueHalf(lootIds, baseValues);
        var count = Math.Min(lootIds.Count, scaled.Count);
        for (var index = 0; index < count; index++)
        {
            var lootId = lootIds[index];
            if (lootId != null && selectedIds.Contains(lootId))
            {
                scaled[index] = ScaleWeight(scaled[index], multiplier);
            }
        }

        return scaled;
    }

    /// <summary>Returns the probability used to upgrade a low-value rolled drop.</summary>
    public static double GetUpgradeChance(double multiplier)
    {
        if (double.IsNaN(multiplier) || multiplier <= 1.0)
        {
            return 0.0;
        }

        if (double.IsPositiveInfinity(multiplier))
        {
            return 1.0;
        }

        return 1.0 - (1.0 / multiplier);
    }

    /// <summary>Chooses the largest non-negative value exposed by a game item.</summary>
    public static long GetEffectiveValue(params long[] values)
    {
        var effectiveValue = 0L;
        if (values == null)
        {
            return effectiveValue;
        }

        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] > effectiveValue)
            {
                effectiveValue = values[index];
            }
        }

        return effectiveValue;
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

    private static int ScaleWeight(int baseWeight, double multiplier)
    {
        var nonNegativeWeight = Math.Max(0, baseWeight);
        if (nonNegativeWeight == 0 || double.IsNaN(multiplier) || multiplier <= 1.0)
        {
            return nonNegativeWeight;
        }

        if (double.IsPositiveInfinity(multiplier))
        {
            return int.MaxValue;
        }

        var scaledWeight = nonNegativeWeight * multiplier;
        if (scaledWeight >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(1, (int)Math.Round(scaledWeight, MidpointRounding.AwayFromZero));
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
