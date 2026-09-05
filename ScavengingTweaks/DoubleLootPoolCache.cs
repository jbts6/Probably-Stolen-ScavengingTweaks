using System;
using System.Collections.Generic;
using System.Linq;

namespace ScavengingTweaks;

/// <summary>Incrementally selects the highest-value item for each item type.</summary>
public sealed class DoubleLootPoolCache : IDisposable
{
    private readonly IEnumerator<(string Id, IReadOnlyList<string> Types, long Value)> candidates;
    private readonly Dictionary<string, (long Value, string Id)> bestByType = new(StringComparer.Ordinal);
    private IReadOnlyList<string> items = Array.Empty<string>();
    private bool disposed;

    public DoubleLootPoolCache(IEnumerable<(string Id, IReadOnlyList<string> Types, long Value)> candidates)
    {
        this.candidates = (candidates ?? throw new ArgumentNullException(nameof(candidates))).GetEnumerator();
    }

    public bool IsComplete { get; private set; }

    public IReadOnlyList<string> Items => items;

    /// <summary>Consumes at most maxItems candidates while the optional frame budget allows.</summary>
    public void Advance(int maxItems, Func<bool>? hasTimeRemaining = null)
    {
        if (disposed || IsComplete || maxItems <= 0)
        {
            return;
        }

        for (var index = 0; index < maxItems && (index == 0 || hasTimeRemaining == null || hasTimeRemaining()); index++)
        {
            if (!candidates.MoveNext())
            {
                IsComplete = true;
                candidates.Dispose();
                items = bestByType
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Value.Id)
                    .ToArray();
                return;
            }

            AddCandidate(candidates.Current);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        candidates.Dispose();
    }

    private void AddCandidate((string Id, IReadOnlyList<string> Types, long Value) candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Id) || candidate.Value <= 0 || candidate.Types == null)
        {
            return;
        }

        foreach (var type in candidate.Types)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            if (!bestByType.TryGetValue(type, out var current) || candidate.Value > current.Value)
            {
                bestByType[type] = (candidate.Value, candidate.Id);
            }
        }
    }
}
