namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// A child is ready when its ready file exists. The heartbeat is a later write.
/// Exit code 1 means the process ended after that ready file and before the heartbeat.
/// </summary>
public static class ServiceChildHeartbeat
{
    public static bool FollowsReady(ServiceReadyReport report)
    {
        return report?.ReadyAt != null
            && report.HeartbeatAt != null
            && report.HeartbeatAt.Value > report.ReadyAt.Value;
    }

    public static int ExitCode(ServiceReadyReport report, bool processExited)
    {
        if (!processExited || report?.ReadyAt == null || FollowsReady(report))
        {
            return 0;
        }

        return 1;
    }
}
