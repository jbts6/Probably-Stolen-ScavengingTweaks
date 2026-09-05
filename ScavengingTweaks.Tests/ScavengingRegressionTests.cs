using System;
using System.Collections.Generic;
using System.Linq;
using ScavengingTweaks;

internal static class ScavengingRegressionTests
{
    public static void Run()
    {
        PoolReadsDoNotScanAndWarmupIsBounded();
        WarmupKeepsTheFullCatalogSelection();
        WarmupYieldsWhenTheFrameBudgetIsUsed();
        InvalidCandidatesConsumeWarmupBudget();
        BlockedWoundGetsLootWithoutDuplicatingNormalDrops();
    }

    private static void PoolReadsDoNotScanAndWarmupIsBounded()
    {
        var visited = 0;
        IEnumerable<(string Id, IReadOnlyList<string> Types, long Value)> Scan()
        {
            for (var i = 0; i < 20; i++)
            {
                visited++;
                yield return ("item" + i, new[] { "TOOL" }, i + 1);
            }
        }

        using var cache = new DoubleLootPoolCache(Scan());
        Ensure(cache.Items.Count == 0 && visited == 0, "reading the reward pool must not start a directory scan");
        cache.Advance(3);
        Ensure(visited == 3 && !cache.IsComplete, "one warmup step must respect its item budget");
        Ensure(cache.Items.Count == 0 && visited == 3, "an unfinished pool must not trigger synchronous completion");
        cache.Advance(30);
        Ensure(cache.IsComplete && cache.Items.SequenceEqual(new[] { "item19" }), "completed warmup must publish the highest-value item");
        cache.Advance(30);
        Ensure(visited == 20, "reusing the completed pool must not rescan the catalog");
    }

    private static void WarmupKeepsTheFullCatalogSelection()
    {
        var catalog = new List<(string Id, IReadOnlyList<string> Types, long Value)>
        {
            ("dump_tool", new[] { "TOOL" }, 100),
            ("rare_tool", new[] { "TOOL" }, 500),
            ("food", new[] { "FOOD", "CONSUMABLE" }, 80),
            ("invalid", new[] { "MISC" }, 0)
        };
        using var cache = new DoubleLootPoolCache(catalog);
        while (!cache.IsComplete)
        {
            cache.Advance(1);
        }

        Ensure(cache.Items.SequenceEqual(DropSharing.PickPerTypeTopItems(catalog)), "incremental selection must match the original full-catalog reward pool");
    }

    private static void BlockedWoundGetsLootWithoutDuplicatingNormalDrops()
    {
        var recoveries = 0;
        var recovery = new ScavengingRecovery();
        Action deliver = () => recoveries++;
        recovery.ResolveIfNeeded(true, true, false, deliver);
        Ensure(recoveries == 1, "a blocked wound without drops must receive one loot resolution");
        recovery.ResolveIfNeeded(true, true, false, deliver);
        Ensure(recoveries == 1, "a second recovery callback must not duplicate loot");
        recovery.Reset();
        recovery.ResolveIfNeeded(true, true, true, deliver);
        recovery.ResolveIfNeeded(true, false, false, deliver);
        recovery.ResolveIfNeeded(false, true, false, deliver);
        Ensure(recoveries == 1, "normal drops, empty non-wound attempts and unrelated wounds must not receive extra loot");
        recovery.ResolveIfNeeded(true, true, false, () =>
        {
            recoveries++;
            recovery.ResolveIfNeeded(true, true, false, deliver);
        });
        Ensure(recoveries == 2, "recovery must guard against reentrant callbacks and reset for the next attempt");
    }

    private static void WarmupYieldsWhenTheFrameBudgetIsUsed()
    {
        var visited = 0;
        IEnumerable<(string Id, IReadOnlyList<string> Types, long Value)> Scan()
        {
            for (var index = 0; index < 100; index++)
            {
                visited++;
                yield return default;
            }
        }

        using var cache = new DoubleLootPoolCache(Scan());
        cache.Advance(100, () => false);
        Ensure(visited == 1 && !cache.IsComplete, "empty candidates must also count against the frame budget");
        cache.Dispose();
        cache.Advance(100);
        Ensure(visited == 1, "a disposed scan must not access the old catalog");
    }

    private static void InvalidCandidatesConsumeWarmupBudget()
    {
        var visited = 0;
        IEnumerable<(string Id, IReadOnlyList<string> Types, long Value)> Scan()
        {
            for (var index = 0; index < 3; index++)
            {
                visited++;
                yield return default;
            }

            visited++;
            yield return ("valid", new[] { "TOOL" }, 1);
        }

        using var cache = new DoubleLootPoolCache(Scan());
        cache.Advance(2);
        Ensure(visited == 2 && !cache.IsComplete, "invalid candidates must consume the incremental warmup budget");
        cache.Advance(3);
        Ensure(cache.IsComplete && cache.Items.SequenceEqual(new[] { "valid" }), "valid candidates must still publish after invalid entries");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + message);
        }
    }
}
