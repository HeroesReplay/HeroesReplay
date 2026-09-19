using System;
using System.Diagnostics;

namespace HeroesReplay.Core;

public static class HeroesReplayTelemetry
{
    public const string SourceName = "HeroesReplay";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public static Activity StartSpan(string name, Activity parent = null)
    {
        if (parent != null)
        {
            return ActivitySource.StartActivity(name, ActivityKind.Internal, parent.Context);
        }

        return ActivitySource.StartActivity(name, ActivityKind.Internal);
    }

    public static void RecordException(Activity activity, Exception exception)
    {
        if (activity == null || exception == null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }

    public static void TagReplay(
        Activity activity,
        string path = null,
        string map = null,
        int? replayId = null,
        string version = null
    )
    {
        if (activity == null)
        {
            return;
        }

        if (path != null)
        {
            activity.SetTag("replay.path", path);
        }

        if (map != null)
        {
            activity.SetTag("replay.map", map);
        }

        if (replayId.HasValue)
        {
            activity.SetTag("replay.id", replayId.Value);
        }

        if (version != null)
        {
            activity.SetTag("replay.version", version);
        }
    }
}
