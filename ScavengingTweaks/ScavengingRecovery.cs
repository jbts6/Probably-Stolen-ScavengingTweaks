using System;

namespace ScavengingTweaks;

public sealed class ScavengingRecovery
{
    private bool attempted;

    /// <summary>Starts a new attempt without carrying over a previous recovery.</summary>
    public void Reset() => attempted = false;

    /// <summary>Runs the one fallback resolution needed after vanilla skipped a wound attempt.</summary>
    public void ResolveIfNeeded(
        bool activeAttempt,
        bool blockedWound,
        bool observedResult,
        Action resolve)
    {
        if (!activeAttempt || !blockedWound || observedResult || attempted)
        {
            return;
        }

        if (resolve == null)
        {
            throw new ArgumentNullException(nameof(resolve));
        }

        attempted = true;
        resolve();
    }
}
