namespace HeroesReplay.Core.Services.Observer;

public enum ReplayLaunchStep
{
    AlreadyInMatch,
    Open,
    CloseThenOpen,
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

        // A login window is a running client with neither the home screen nor the match.
        if (processRunning && !homeScreen)
        {
            return ReplayLaunchStep.CloseThenOpen;
        }

        return ReplayLaunchStep.Open;
    }
}
