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
            FractionalMultiplierRoundsToNearestWholeStep();
            HugeMultiplierIsCappedAtMaxCopies();
            EntriesWithoutResolvedValueParticipateInHighestValueHalf();
            ConfiguredRemainingAttemptsNeverGoNegative();
            LegacyInjectedCountIsDetected();
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

        Ensure(result.Count == 7, "a 2x multiplier should append one copy per selected entry");
        Ensure(result.Count(id => id == "low") == 1, "the lowest-value entry should keep its original weight");
        Ensure(result.Count(id => id == "mid") == 2, "the middle-value entry should be selected in the top half");
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

        Ensure(result.SequenceEqual(new[] { 2, 6, 10 }), "a 2x multiplier should scale only the highest-value half");
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

    private static void FractionalMultiplierRoundsToNearestWholeStep()
    {
        var input = new[] { "a", "b" };
        var values = new Dictionary<string, long> { ["a"] = 20, ["b"] = 10 };

        var atOnePointFive = LootWeighting.ExpandHighValueEntries(input, values, 1.5);
        Ensure(atOnePointFive.Count(id => id == "a") == 2, "1.5 should round up to a 2x multiplier");

        var atTwoPointFive = LootWeighting.ExpandHighValueEntries(input, values, 2.5);
        Ensure(atTwoPointFive.Count(id => id == "a") == 3, "2.5 should round up to a 3x multiplier");

        var atTwoPointOne = LootWeighting.ExpandHighValueEntries(input, values, 2.1);
        Ensure(atTwoPointOne.Count(id => id == "a") == 2, "2.1 should round down to a 2x multiplier");

        var atOnePointFour = LootWeighting.ExpandHighValueEntries(input, values, 1.4);
        Ensure(atOnePointFour.SequenceEqual(input), "1.4 should round down to a 1x multiplier");
    }

    private static void HugeMultiplierIsCappedAtMaxCopies()
    {
        var input = new[] { "a", "b" };
        var values = new Dictionary<string, long> { ["a"] = 20, ["b"] = 10 };

        var huge = LootWeighting.ExpandHighValueEntries(input, values, 1e20);
        Ensure(huge.Count(id => id == "a") == 33, "an absurd multiplier must cap at 32 extra copies, not underflow to zero");

        var aboveCap = LootWeighting.ExpandHighValueEntries(input, values, 35.0);
        Ensure(aboveCap.Count(id => id == "a") == 33, "a multiplier above the cap must clamp to 32 extra copies");
    }

    private static void EntriesWithoutResolvedValueParticipateInHighestValueHalf()
    {
        var values = new Dictionary<string, long> { ["a"] = 100, ["b"] = 1 };

        var mixed = LootWeighting.ExpandHighValueEntries(new[] { "a", "b", "unknown" }, values, 2.0);
        Ensure(mixed.Count(id => id == "a") == 2, "resolved high-value entries should still be boosted");
        Ensure(mixed.Count(id => id == "b") == 2, "unresolved entries must not displace resolved entries from the top half");
        Ensure(mixed.Count(id => id == "unknown") == 1, "an unresolved entry should only fill the remaining top-half slot");

        var allUnknown = LootWeighting.ExpandHighValueEntries(
            new[] { "u1", "u2" },
            new Dictionary<string, long>(),
            2.0);
        Ensure(allUnknown.Count(id => id == "u1") == 2, "the top half should still apply when no values can be resolved");
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

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + message);
        }
    }
}
