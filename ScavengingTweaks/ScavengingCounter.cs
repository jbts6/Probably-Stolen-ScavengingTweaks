using System;

namespace ScavengingTweaks;

public static class ScavengingCounter
{
    public static int GetRemainingAttempts(int configuredMaxAttempts, int usedAttempts)
    {
        var maxAttempts = Math.Max(1, configuredMaxAttempts);
        var normalizedUsedAttempts = Math.Max(0, usedAttempts);
        return Math.Max(0, maxAttempts - normalizedUsedAttempts);
    }

    public static bool IsLegacyInjectedCount(int configuredMaxAttempts, int usedAttempts)
    {
        var maxAttempts = Math.Max(1, configuredMaxAttempts);
        return maxAttempts > 5 && usedAttempts >= maxAttempts;
    }
}
