using System;
using System.Diagnostics;
using System.Globalization;

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

    public static Activity BeginReplaySession(int replayId)
    {
        Activity activity = StartSpan("heroesreplay.session");
        TagReplay(activity, replayId: replayId);
        return activity;
    }

    public static string FormatSession(Activity activity, int replayId)
    {
        if (activity == null || replayId <= 0)
        {
            return null;
        }

        string flags = ((byte)activity.Context.TraceFlags).ToString(
            "x2",
            CultureInfo.InvariantCulture
        );
        return string.Create(
            CultureInfo.InvariantCulture,
            $"00-{activity.TraceId}-{activity.SpanId}-{flags} {replayId}"
        );
    }

    public static bool TryParseSession(string text, out int replayId, out ActivityContext parent)
    {
        replayId = 0;
        parent = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text.Trim();
        int space = trimmed.LastIndexOf(' ');
        if (space <= 0 || space >= trimmed.Length - 1)
        {
            return false;
        }

        if (
            !int.TryParse(
                trimmed[(space + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out replayId
            )
            || replayId <= 0
        )
        {
            replayId = 0;
            return false;
        }

        string[] parts = trimmed[..space].Split('-');
        if (parts.Length != 4 || parts[0] != "00")
        {
            replayId = 0;
            return false;
        }

        if (!TryTrace(parts[1], 32, out ActivityTraceId traceId))
        {
            replayId = 0;
            return false;
        }

        if (!TrySpan(parts[2], out ActivitySpanId spanId))
        {
            replayId = 0;
            return false;
        }

        if (
            !byte.TryParse(
                parts[3],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out byte flagByte
            )
        )
        {
            replayId = 0;
            return false;
        }

        parent = new ActivityContext(traceId, spanId, (ActivityTraceFlags)flagByte, isRemote: true);
        return true;
    }

    public static Activity JoinReplaySession(string session, string name)
    {
        if (!TryParseSession(session, out int replayId, out ActivityContext parent))
        {
            return null;
        }

        Activity activity = ActivitySource.StartActivity(name, ActivityKind.Internal, parent);
        TagReplay(activity, replayId: replayId);
        return activity;
    }

    private static bool TryTrace(string hex, int length, out ActivityTraceId traceId)
    {
        traceId = default;
        if (!IsHex(hex, length))
        {
            return false;
        }

        traceId = ActivityTraceId.CreateFromString(hex);
        return true;
    }

    private static bool TrySpan(string hex, out ActivitySpanId spanId)
    {
        spanId = default;
        if (!IsHex(hex, 16))
        {
            return false;
        }

        spanId = ActivitySpanId.CreateFromString(hex);
        return true;
    }

    private static bool IsHex(string hex, int length)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != length)
        {
            return false;
        }

        foreach (char character in hex)
        {
            bool digit = character >= '0' && character <= '9';
            bool lower = character >= 'a' && character <= 'f';
            bool upper = character >= 'A' && character <= 'F';
            if (!digit && !lower && !upper)
            {
                return false;
            }
        }

        return true;
    }
}
