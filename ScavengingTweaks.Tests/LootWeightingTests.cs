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
            GroundGridTargetHeightTripledByDefault();
            GroundGridMultiplierOneMeansOff();
            GroundGridInvalidOriginalRejected();
            GroundGridFractionalMultiplierRoundsUp();
            GroundGridSettingHeightCachedOnFirstObservation();
            GroundGridRoomHeightCachedPerRoom();
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

    private static void GroundGridTargetHeightTripledByDefault()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(3, 3.0) == 9, "3 rows at 3x should become 9");
    }

    private static void GroundGridMultiplierOneMeansOff()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(3, 1.0) == -1, "multiplier 1.0 should disable enlargement");
        Ensure(GroundGridMath.ComputeTargetHeight(3, 0.5) == -1, "multiplier below 1.0 should disable enlargement");
    }

    private static void GroundGridInvalidOriginalRejected()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(0, 3.0) == -1, "zero original height should be rejected");
        Ensure(GroundGridMath.ComputeTargetHeight(-2, 3.0) == -1, "negative original height should be rejected");
    }

    private static void GroundGridFractionalMultiplierRoundsUp()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(3, 2.1) == 7, "fractional target should round up to keep capacity");
    }

    private static void GroundGridSettingHeightCachedOnFirstObservation()
    {
        Ensure(GroundGridState.GetOriginalSettingHeight(3) == 3, "first positive observation should be recorded");
        Ensure(GroundGridState.GetOriginalSettingHeight(9) == 3, "later enlarged observation must not overwrite the original");
        Ensure(GroundGridState.GetOriginalSettingHeight(0) == 3, "non-positive observation must not clear the cache");
    }

    private static void GroundGridRoomHeightCachedPerRoom()
    {
        Ensure(GroundGridState.GetOriginalRoomHeight(7001, 4) == 4, "room cache should record the first positive height");
        Ensure(GroundGridState.GetOriginalRoomHeight(7001, 12) == 4, "room cache must return the original on later calls");
        Ensure(GroundGridState.GetOriginalRoomHeight(7002, 6) == 6, "different rooms should cache independently");
        Ensure(GroundGridState.GetOriginalRoomHeight(7003, 0) == -1, "unrecorded room with non-positive height should return -1");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + message);
        }
    }
}
