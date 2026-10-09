using System;
using HeroesReplay.Core.Obs.Inspection;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// What OBS says about the stream output, read from more than <c>outputActive</c> (#395). On
/// 2026-10-09 production reported <c>outputActive: true, outputReconnecting: true</c> with frozen
/// bytes for 4 h 11 min, and an active output alone counted as live.
/// </summary>
public enum ObsStreamState
{
    /// <summary>OBS was not read: not running, the websocket not identified, or the request failed.</summary>
    Unknown,

    /// <summary>The stream output is not active.</summary>
    Inactive,

    /// <summary>Active, not reconnecting, and its bytes advance (or there is no earlier read yet).</summary>
    Live,

    /// <summary>Active and reconnecting: OBS lost the ingest and says it is retrying.</summary>
    Reconnecting,

    /// <summary>
    /// Active and not reconnecting, but <c>outputBytes</c> has not moved for at least
    /// <see cref="ObsStreamHealth.StallWindow"/>.
    /// </summary>
    Stalled,
}

/// <summary>One GetStreamStatus answer: <c>outputActive</c>, <c>outputReconnecting</c>, <c>outputBytes</c>.</summary>
public readonly record struct ObsStreamSample(bool Active, bool Reconnecting, long? Bytes)
{
    /// <summary>Null when the answer does not say whether the output is active.</summary>
    public static ObsStreamSample? From(JObject getStreamStatus)
    {
        bool? active = ObsResponse.Bool(getStreamStatus, "outputActive");
        if (active == null)
        {
            return null;
        }

        return new ObsStreamSample(
            active.Value,
            ObsResponse.Bool(getStreamStatus, "outputReconnecting") == true,
            ObsResponse.Long(getStreamStatus, "outputBytes")
        );
    }
}

/// <summary>
/// The stream's health from GetStreamStatus, and whether <c>outputBytes</c> advanced since the
/// last read (#395). <see cref="Next(ObsStreamHealth, ObsStreamSample, DateTimeOffset)"/> takes
/// the previous health, so a reader keeps the last one it got. Only <see cref="ObsStreamState.Live"/>
/// is a stream viewers can see. Never throws.
/// </summary>
public sealed record ObsStreamHealth
{
    /// <summary>
    /// How long <c>outputBytes</c> must stay the same before an active output that is not
    /// reconnecting is <see cref="ObsStreamState.Stalled"/>. A live stream sends bytes every few
    /// milliseconds, and two reads close together must not look frozen.
    /// </summary>
    public static readonly TimeSpan StallWindow = TimeSpan.FromSeconds(10);

    /// <summary>The cause code for an output that stayed reconnecting past <c>OBS:StreamStuckAfter</c>.</summary>
    public const string StuckReconnectingCode = "obs.stream_stuck_reconnecting";

    /// <summary>The cause code for an output whose bytes stayed frozen past <c>OBS:StreamStuckAfter</c>.</summary>
    public const string StalledCode = "obs.stream_stalled";

    public ObsStreamState State { get; init; }

    /// <summary>When OBS was read.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>
    /// When this stretch of <see cref="ObsStreamState.Reconnecting"/> or
    /// <see cref="ObsStreamState.Stalled"/> began; null in any other state. An unreadable OBS in
    /// between does not start it over.
    /// </summary>
    public DateTimeOffset? StuckSince { get; init; }

    /// <summary><c>outputBytes</c> of the last read of an active output.</summary>
    public long? Bytes { get; init; }

    /// <summary>When <see cref="Bytes"/> was first read at this value.</summary>
    public DateTimeOffset? BytesSince { get; init; }

    public string Detail { get; init; }

    /// <summary>True only for <see cref="ObsStreamState.Live"/>: the one state viewers see.</summary>
    public bool IsLive => State == ObsStreamState.Live;

    /// <summary>OBS has the stream output running, live or not. StopStream applies to it.</summary>
    public bool OutputActive =>
        State is ObsStreamState.Live or ObsStreamState.Reconnecting or ObsStreamState.Stalled;

    /// <summary>The output is active, but nothing reaches the ingest.</summary>
    public bool IsStuck => State is ObsStreamState.Reconnecting or ObsStreamState.Stalled;

    /// <summary><see cref="StuckReconnectingCode"/> or <see cref="StalledCode"/>; null otherwise.</summary>
    public string CauseCode =>
        State switch
        {
            ObsStreamState.Reconnecting => StuckReconnectingCode,
            ObsStreamState.Stalled => StalledCode,
            _ => null,
        };

    public TimeSpan StuckFor(DateTimeOffset now) =>
        IsStuck && StuckSince is DateTimeOffset since && now > since ? now - since : TimeSpan.Zero;

    /// <summary>The health after one GetStreamStatus answer, given the one before it (or null).</summary>
    public static ObsStreamHealth Next(
        ObsStreamHealth previous,
        ObsStreamSample sample,
        DateTimeOffset now
    )
    {
        if (!sample.Active)
        {
            return new ObsStreamHealth
            {
                State = ObsStreamState.Inactive,
                At = now,
                Detail = "OBS reported the stream inactive.",
            };
        }

        // Bytes are compared only with an earlier read of an active output (or an unreadable one
        // in between): a stream that just started has no bytes yet and is not frozen.
        bool comparable =
            previous != null && (previous.OutputActive || previous.State == ObsStreamState.Unknown);
        DateTimeOffset bytesSince =
            comparable
            && sample.Bytes is long bytes
            && previous.Bytes == bytes
            && previous.BytesSince is DateTimeOffset seen
                ? seen
                : now;

        ObsStreamState state;
        if (sample.Reconnecting)
        {
            state = ObsStreamState.Reconnecting;
        }
        else if (sample.Bytes != null && now - bytesSince >= StallWindow)
        {
            state = ObsStreamState.Stalled;
        }
        else
        {
            state = ObsStreamState.Live;
        }

        DateTimeOffset? stuckSince = null;
        if (state != ObsStreamState.Live)
        {
            bool continues =
                previous?.StuckSince != null
                && (previous.IsStuck || previous.State == ObsStreamState.Unknown);
            stuckSince =
                continues ? previous.StuckSince
                : state == ObsStreamState.Stalled ? bytesSince
                : now;
        }

        return new ObsStreamHealth
        {
            State = state,
            At = now,
            StuckSince = stuckSince,
            Bytes = sample.Bytes,
            BytesSince = bytesSince,
            Detail = state switch
            {
                ObsStreamState.Reconnecting => "OBS reported the stream reconnecting since "
                    + Clock(stuckSince)
                    + "."
                    + Sent(sample.Bytes),
                ObsStreamState.Stalled =>
                    "OBS reported the stream active, but no bytes went out since "
                        + Clock(stuckSince)
                        + "."
                        + Sent(sample.Bytes),
                _ => "OBS reported the stream live.",
            },
        };
    }

    /// <summary>
    /// The health from a raw GetStreamStatus answer, as a read-only session returns it. An answer
    /// that does not say whether the output is active is <see cref="ObsStreamState.Unknown"/>.
    /// </summary>
    public static ObsStreamHealth Next(
        ObsStreamHealth previous,
        JObject getStreamStatus,
        DateTimeOffset now
    ) =>
        ObsStreamSample.From(getStreamStatus) is ObsStreamSample sample
            ? Next(previous, sample, now)
            : Unknown(previous, "GetStreamStatus did not say whether the stream is active.", now);

    /// <summary>
    /// OBS was not read. The previous read's bytes and <see cref="StuckSince"/> are kept, so a
    /// websocket that misses a read does not reset a stuck output's clock.
    /// </summary>
    public static ObsStreamHealth Unknown(
        ObsStreamHealth previous,
        string detail,
        DateTimeOffset now
    )
    {
        bool carry =
            previous != null && (previous.OutputActive || previous.State == ObsStreamState.Unknown);
        return new ObsStreamHealth
        {
            State = ObsStreamState.Unknown,
            At = now,
            StuckSince = carry ? previous.StuckSince : null,
            Bytes = carry ? previous.Bytes : null,
            BytesSince = carry ? previous.BytesSince : null,
            Detail = string.IsNullOrWhiteSpace(detail) ? "OBS stream status was not read." : detail,
        };
    }

    private static string Clock(DateTimeOffset? at) =>
        at is DateTimeOffset value ? value.ToUniversalTime().ToString("HH:mm:ss") + "Z" : "-";

    private static string Sent(long? bytes) =>
        bytes is long value ? " " + value + " bytes sent." : string.Empty;
}
