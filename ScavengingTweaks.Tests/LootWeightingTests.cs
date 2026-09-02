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
            MultiplierBelowOneDoesNotChangeEntries();
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
