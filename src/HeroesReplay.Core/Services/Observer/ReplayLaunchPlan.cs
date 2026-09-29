namespace HeroesReplay.Core.Services.Observer;

public enum ReplayLaunchStep
{
    AlreadyInMatch,
    Open,
    Wait,
}

public static class ReplayLaunchPlan
{
    public static ReplayLaunchStep Decide(
        bool processRunning,
        bool replayPresented,
        bool homeScreen
    )
    {
        if (processRunning && replayPresented)
        {
            return ReplayLaunchStep.AlreadyInMatch;
        }

        // Splash, the login form, and a version-mismatch dialog are still this process.
        // Closing it and opening the replay file drops the signed-in client.
        if (processRunning && !homeScreen)
        {
            return ReplayLaunchStep.Wait;
        }

        return ReplayLaunchStep.Open;
    }
}
