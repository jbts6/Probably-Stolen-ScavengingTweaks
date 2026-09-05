using System;
using System.Collections.Generic;
using System.Linq;
using ScavengingTweaks;

internal static class LootWeightingTests
{
    private static int Main()
    {
        try
        {
            EmptyInputReturnsEmpty();
            DoublesTheHighestValueHalf();
            ScalesHighestValueHalfWhilePreservingWeights();
            WeightScalingClampsOverflow();
            UpgradeChanceMatchesConfiguredMultiplier();
            EffectiveValueUsesAvailableGameValues();
            MultiplierBelowOneDoesNotChangeEntries();
            ConfiguredRemainingAttemptsNeverGoNegative();
            LegacyInjectedCountIsDetected();
            GroundGridSettingHeightCachedOnFirstObservation();
            GroundGridStringKeyCache();
            GroundGridExtraColumnsAndRowsAdded();
            GroundGridZeroExtraMeansOff();
            DropSharesParseBasic();
            DropSharesTotalExactly10000();
            DropSharesHighValueDominatesLowTier();
            DropSharesExcludeBelowValue();
            DropSharesMultiTypePriorityFollowsConfigOrder();
            DropSharesDuplicateIdAssignedOnce();
            Console.WriteLine("LootWeighting tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void EmptyInputReturnsEmpty()
    {
        var result = LootWeighting.ExpandHighValueEntries(
            Array.Empty<string>(),
            new Dictionary<string, long>(),
            2.0);

        Ensure(result.Count == 0, "empty input should remain empty");
    }

    private static void DoublesTheHighestValueHalf()
    {
        var input = new[] { "low", "high", "mid", "high" };
        var values = new Dictionary<string, long>
        {
            ["low"] = 5,
            ["mid"] = 50,
            ["high"] = 100
        };

        var result = LootWeighting.ExpandHighValueEntries(input, values, 2.0);

        // 动态阈值 0.6: 最高价值100，阈值=60，只有"high"(100)符合
        Ensure(result.Count == 6, "a 2x multiplier should append one copy per selected entry");
        Ensure(result.Count(id => id == "low") == 1, "the lowest-value entry should keep its original weight");
        Ensure(result.Count(id => id == "mid") == 1, "mid-value entry below threshold should not be doubled");
        Ensure(result.Count(id => id == "high") == 4, "all original high-value occurrences should be doubled");
    }

    private static void MultiplierBelowOneDoesNotChangeEntries()
    {
        var input = new[] { "a", "b" };
        var values = new Dictionary<string, long> { ["a"] = 20, ["b"] = 10 };

        var result = LootWeighting.ExpandHighValueEntries(input, values, 0.5);

        Ensure(result.SequenceEqual(input), "a multiplier below one must not remove or add entries");
    }

    private static void UpgradeChanceMatchesConfiguredMultiplier()
    {
        Ensure(LootWeighting.GetUpgradeChance(1.0) == 0.0, "a 1x multiplier should never upgrade a drop");
        Ensure(Math.Abs(LootWeighting.GetUpgradeChance(2.0) - 0.5) < 0.000001, "a 2x multiplier should upgrade half of low-value drops");
        Ensure(Math.Abs(LootWeighting.GetUpgradeChance(10.0) - 0.9) < 0.000001, "a 10x multiplier should upgrade 90 percent of low-value drops");
        Ensure(LootWeighting.GetUpgradeChance(double.PositiveInfinity) == 1.0, "an infinite multiplier should always upgrade a low-value drop");
    }

    private static void ScalesHighestValueHalfWhilePreservingWeights()
    {
        var ids = new[] { "low", "mid", "high" };
        var weights = new[] { 2, 3, 5 };
        var values = new Dictionary<string, long>
        {
            ["low"] = 5,
            ["mid"] = 50,
            ["high"] = 100
        };

        var result = LootWeighting.ScaleHighValueWeights(ids, weights, values, 2.0);

        // 动态阈值 0.6: 最高价值100，阈值=60，只有"high"(100)>=60
        // low=5被屏蔽(< 10)，权重设为0
        Ensure(result.SequenceEqual(new[] { 0, 3, 10 }), "a 2x multiplier should scale high values and zero out values < 10");
    }

    private static void WeightScalingClampsOverflow()
    {
        var ids = new[] { "high" };
        var weights = new[] { int.MaxValue };
        var values = new Dictionary<string, long> { ["high"] = 100 };

        var result = LootWeighting.ScaleHighValueWeights(ids, weights, values, 10.0);

        Ensure(result[0] == int.MaxValue, "scaled weights must clamp instead of overflowing");
    }

    private static void EffectiveValueUsesAvailableGameValues()
    {
        Ensure(LootWeighting.GetEffectiveValue(0, 0, 120, 90, 0, 0) == 120, "the first nonzero game value source should not hide a higher fallback value");
        Ensure(LootWeighting.GetEffectiveValue(0, 240, 120, 90, 80, 70) == 240, "the effective value should use the largest available game value");
        Ensure(LootWeighting.GetEffectiveValue(0, 0, 0, 0, 0, 0) == 0, "missing game values should remain zero");
    }

    private static void ConfiguredRemainingAttemptsNeverGoNegative()
    {
        Ensure(ScavengingCounter.GetRemainingAttempts(10, 0) == 10, "a fresh day should expose all configured attempts");
        Ensure(ScavengingCounter.GetRemainingAttempts(10, 5) == 5, "remaining attempts should use the configured maximum");
        Ensure(ScavengingCounter.GetRemainingAttempts(10, 15) == 0, "remaining attempts must not become negative");
    }

    private static void LegacyInjectedCountIsDetected()
    {
        Ensure(ScavengingCounter.IsLegacyInjectedCount(10, 10), "the previous patch value should be recognized for migration");
        Ensure(!ScavengingCounter.IsLegacyInjectedCount(10, 5), "a vanilla-sized used count should not be migrated");
    }

    private static void GroundGridExtraColumnsAndRowsAdded()
    {
        Ensure(GroundGridMath.ComputeTargetDimension(12, 6) == 18, "12 columns plus 6 extra should become 18");
        Ensure(GroundGridMath.ComputeTargetDimension(9, 9) == 18, "9 rows plus 9 extra should become 18");
    }

    private static void GroundGridZeroExtraMeansOff()
    {
        Ensure(GroundGridMath.ComputeTargetDimension(9, 0) == -1, "zero extra cells should disable enlargement");
        Ensure(GroundGridMath.ComputeTargetDimension(0, 6) == -1, "invalid original dimension should be rejected");
        Ensure(GroundGridMath.ComputeTargetDimension(-2, 6) == -1, "negative original dimension should be rejected");
    }

    private static void GroundGridSettingHeightCachedOnFirstObservation()
    {
        Ensure(GroundGridState.GetOriginalSettingHeight(3) == 3, "first positive observation should be recorded");
        Ensure(GroundGridState.GetOriginalSettingHeight(9) == 3, "later enlarged observation must not overwrite the original");
        Ensure(GroundGridState.GetOriginalSettingHeight(0) == 3, "non-positive observation must not clear the cache");
    }

    private static void GroundGridStringKeyCache()
    {
        Ensure(GroundGridState.GetOriginalHeight("win:Ground", 5) == 5, "string key should record the first positive height");
        Ensure(GroundGridState.GetOriginalHeight("win:Ground", 15) == 5, "string key must return the original on later calls");
        Ensure(GroundGridState.GetOriginalHeight("win:Other", 7) == 7, "different keys should cache independently");
        Ensure(GroundGridState.GetOriginalHeight("win:Empty", 0) == -1, "unrecorded key with non-positive height should return -1");
    }

    private static void DropSharesParseBasic()
    {
        var shares = DropSharing.ParseShares("module:25, TOOL:25, bad, TOOL:10, FOOD:x, FOOD:-1");
        Ensure(shares.Count == 2, "invalid share entries should be skipped and types deduped");
        Ensure(shares[0].Key == "MODULE" && Math.Abs(shares[0].Value - 25) < 0.001, "first entry should keep config order and uppercase the type");
        Ensure(shares[1].Key == "TOOL", "second entry should be TOOL");
    }

    private static void DropSharesTotalExactly10000()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var result = DropSharing.ComputeShares(ids, values, resolver, "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10,MEDICAL:5,MATERIAL:5,WEAPON:5", 15, 60);
        var total = 0;
        foreach (var bp in result.SharesBp)
        {
            total += bp;
        }

        Ensure(total == 10000, $"shares must sum to exactly 10000 but was {total}");
    }

    private static void DropSharesHighValueDominatesLowTier()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var result = DropSharing.ComputeShares(ids, values, resolver, "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10,MEDICAL:5,MATERIAL:5,WEAPON:5", 15, 60);

        var moduleBucket = result.SharesBp[0] + result.SharesBp[1] + result.SharesBp[2];
        Ensure(Math.Abs(moduleBucket - 3542) <= 2, $"MODULE bucket should be ~25/60 of 8500 but was {moduleBucket}");
        Ensure(result.SharesBp[3] > result.SharesBp[6], "extractor (high tier) must exceed red_beer (low tier)");
        Ensure(result.SharesBp[5] > result.SharesBp[6], "nudka (high tier) must exceed red_beer (low tier)");
        Ensure(result.SharesBp[6] == result.SharesBp[7], "low-tier items should split LowTierShare evenly");
        Ensure(Math.Abs(result.SharesBp[0] - result.SharesBp[1]) <= 1, "same-bucket items should be near-equal (largest remainder)");
        Ensure(result.SharesBp[9] > 0 && result.SharesBp[10] == 0, "first pipe_weapon occurrence enters the low tier, duplicates get 0");
    }

    private static void DropSharesExcludeBelowValue()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var junkIndex = ids.IndexOf("junk");
        var result = DropSharing.ComputeShares(ids, values, resolver, "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10", 15, 60);
        Ensure(result.SharesBp[junkIndex] == 0, "items below the exclusion floor must get 0 share");
    }

    private static void DropSharesMultiTypePriorityFollowsConfigOrder()
    {
        var ids = new List<string> { "strong_beer", "nudka" };
        var values = new Dictionary<string, long> { ["strong_beer"] = 65, ["nudka"] = 85 };
        Func<string, IReadOnlyList<string>?> resolver = id => id == "nudka"
            ? new[] { "ALCOHOL" }
            : new[] { "FOOD", "ALCOHOL" };

        // ALCOHOL 在前：strong_beer 与 nudka 同桶，FOOD 空桶出局，ALCOHOL 独占 10000
        var alcoholFirst = DropSharing.ComputeShares(ids, values, resolver, "ALCOHOL:10,FOOD:30", 0, 60);
        Ensure(alcoholFirst.SharesBp[0] == 5000 && alcoholFirst.SharesBp[1] == 5000, "ALCOHOL-first config puts strong_beer in the same bucket as nudka");

        // FOOD 在前：strong_beer 独占 FOOD 桶（30/40），nudka 独占 ALCOHOL 桶（10/40）
        var foodFirst = DropSharing.ComputeShares(ids, values, resolver, "FOOD:30,ALCOHOL:10", 0, 60);
        Ensure(foodFirst.SharesBp[0] == 7500, $"FOOD-first config moves strong_beer into the FOOD bucket (30/40 of 10000) but got {foodFirst.SharesBp[0]}/{foodFirst.SharesBp[1]} diag=[{string.Join("; ", foodFirst.Diagnostics)}]");
        Ensure(foodFirst.SharesBp[1] == 2500, "nudka alone in the ALCOHOL bucket keeps only its 10/40 share");
    }

    private static void DropSharesDuplicateIdAssignedOnce()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var result = DropSharing.ComputeShares(ids, values, resolver, "WEAPON:100", 0, 1);
        Ensure(result.SharesBp[9] == 10000, "first pipe_weapon occurrence should take the whole WEAPON share");
        Ensure(result.SharesBp[10] == 0, "second pipe_weapon occurrence must get 0");
    }

    // 模拟真实物品池：3 件 T1 模块(100)、提取器(100)/焊接器(80)、nudka(85)、
    // 红啤酒(18, FOOD+ALCOHOL)、杯面(20)，外加垃圾(3)与跨表重复的管道武器(15)
    private static (List<string> ids, Dictionary<string, long> values, Func<string, IReadOnlyList<string>?> types) BuildQuotaScenario()
    {
        var ids = new List<string>
        {
            "t1a", "t1b", "t1c",
            "extractor", "welder",
            "nudka",
            "red_beer", "cup_noodle",
            "junk",
            "pipe_weapon", "pipe_weapon"
        };
        var values = new Dictionary<string, long>
        {
            ["t1a"] = 100, ["t1b"] = 100, ["t1c"] = 100,
            ["extractor"] = 100, ["welder"] = 80,
            ["nudka"] = 85, ["red_beer"] = 18, ["cup_noodle"] = 20,
            ["junk"] = 3, ["pipe_weapon"] = 15
        };
        var typesByItem = new Dictionary<string, string[]>
        {
            ["t1a"] = new[] { "MODULE" }, ["t1b"] = new[] { "MODULE" }, ["t1c"] = new[] { "MODULE" },
            ["extractor"] = new[] { "TOOL" }, ["welder"] = new[] { "TOOL" },
            ["nudka"] = new[] { "ALCOHOL" },
            ["red_beer"] = new[] { "FOOD", "ALCOHOL" },
            ["cup_noodle"] = new[] { "FOOD" },
            ["junk"] = new[] { "MISC" },
            ["pipe_weapon"] = new[] { "WEAPON" }
        };
        Func<string, IReadOnlyList<string>?> resolver = id => typesByItem.TryGetValue(id, out var t) ? t : null;
        return (ids, values, resolver);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + message);
        }
    }
}
