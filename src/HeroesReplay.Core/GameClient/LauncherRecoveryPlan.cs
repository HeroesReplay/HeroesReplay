namespace HeroesReplay.Core.GameClient;

public enum LauncherRecoveryAction
{
    None,
    RestartLauncher,
    OperatorRequired,
}

/// <summary>
/// A version-mismatch dialog is a launcher problem. The first result names one
/// launcher restart. The next result needs an operator. The replay stays leased.
/// </summary>
public static class LauncherRecoveryPlan
{
    public static LauncherRecoveryAction Decide(ClientHoldReason reason, int recoveryAttempt)
    {
        if (reason != ClientHoldReason.VersionMismatch)
        {
            return LauncherRecoveryAction.None;
        }

        if (recoveryAttempt < 1)
        {
            return LauncherRecoveryAction.RestartLauncher;
        }

        return LauncherRecoveryAction.OperatorRequired;
    }
}
