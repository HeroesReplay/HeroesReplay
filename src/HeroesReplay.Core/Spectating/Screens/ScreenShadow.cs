using System;
using System.Collections.Generic;
using System.Globalization;
using HeroesReplay.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectating.Screens;

public enum ShadowVerdict
{
    Agree,
    Disagree,
    MemoryUnknown,
}

/// <summary>One shadow observation: the verdict, and whether the caller saves a frame of it.</summary>
public readonly record struct ShadowObservation(ShadowVerdict Verdict, bool SaveFrame);

/// <summary>
/// Shadow mode (#292): the memory verdict next to every OCR verdict on a client screen, so a later
/// change can prove memory covers a state before OCR is removed. It only observes; the caller's
/// decision does not change. Each observation counts <c>heroesreplay.screen.shadow</c> by state
/// and verdict. A state logs at Information when its (OCR, memory) pair changes and at Debug while
/// it holds. A disagreement warns at most once per state per minute, with the number of
/// disagreeing observations since the last warning, and asks for at most one frame per state per
/// <see cref="FrameInterval"/>. Memory that cannot tell is counted, never warned, never framed.
/// </summary>
public sealed class ScreenShadow
{
    public const int ExcerptLength = 160;
    public const string FrameFolder = "shadow";

    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultFrameInterval = TimeSpan.FromMinutes(5);

    private readonly ILogger logger;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<ScreenState, StateTrack> states = new();

    public ScreenShadow(ILogger logger, TimeProvider clock = null, TimeSpan? frameInterval = null)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.clock = clock ?? TimeProvider.System;
        FrameInterval =
            frameInterval is TimeSpan interval && interval > TimeSpan.Zero
                ? interval
                : DefaultFrameInterval;
    }

    public TimeSpan FrameInterval { get; }

    public ShadowObservation Observe(
        ScreenState state,
        bool ocr,
        bool? memory,
        string ocrText,
        string memoryReason = null
    )
    {
        ShadowVerdict verdict = VerdictOf(ocr, memory);
        HeroesReplayTelemetry.CountScreenShadow(TagOf(state), TagOf(verdict));
        string memoryText = MemoryText(memory);
        string reason = string.IsNullOrWhiteSpace(memoryReason) ? "not read" : memoryReason;

        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (!states.TryGetValue(state, out StateTrack track))
            {
                track = new StateTrack();
                states[state] = track;
            }

            bool changed = !track.Seen || track.Ocr != ocr || track.Memory != memory;
            track.Seen = true;
            track.Ocr = ocr;
            track.Memory = memory;
            logger.Log(
                changed ? LogLevel.Information : LogLevel.Debug,
                "Screen shadow {State}: OCR {Ocr}, memory {Memory} ({MemoryReason}). {Verdict}.",
                state,
                ocr,
                memoryText,
                reason,
                verdict
            );

            if (verdict != ShadowVerdict.Disagree)
            {
                return new ShadowObservation(verdict, SaveFrame: false);
            }

            track.Disagreements++;
            if (track.LastWarning is not DateTimeOffset warned || now - warned >= WarningInterval)
            {
                logger.LogWarning(
                    "Screen shadow disagrees on {State}: OCR {Ocr}, memory {Memory} ({MemoryReason}). Disagreeing observations since the last warning: {Count}. OCR text: {OcrText}",
                    state,
                    ocr,
                    memoryText,
                    reason,
                    track.Disagreements,
                    Excerpt(ocrText)
                );
                track.LastWarning = now;
                track.Disagreements = 0;
            }

            bool saveFrame =
                track.LastFrame is not DateTimeOffset framed || now - framed >= FrameInterval;
            if (saveFrame)
            {
                track.LastFrame = now;
            }

            return new ShadowObservation(verdict, saveFrame);
        }
    }

    public static ShadowVerdict VerdictOf(bool ocr, bool? memory) =>
        memory is bool known
            ? known == ocr
                ? ShadowVerdict.Agree
                : ShadowVerdict.Disagree
            : ShadowVerdict.MemoryUnknown;

    public static string TagOf(ScreenState state) =>
        state switch
        {
            ScreenState.Home => "home",
            ScreenState.LoginForm => "login_form",
            ScreenState.MapLoading => "map_loading",
            ScreenState.GameDataDownload => "game_data_download",
            ScreenState.GameDataStartup => "game_data_startup",
            ScreenState.VersionMismatch => "version_mismatch",
            ScreenState.RegionUnavailable => "region_unavailable",
            ScreenState.EndScreen => "end_screen",
            _ => state.ToString().ToLowerInvariant(),
        };

    public static string TagOf(ShadowVerdict verdict) =>
        verdict switch
        {
            ShadowVerdict.Agree => "agree",
            ShadowVerdict.Disagree => "disagree",
            _ => "memory_unknown",
        };

    /// <summary>The file a disagreement frame is saved as, under the context's shadow folder.</summary>
    public static string FrameFileName(ScreenState state, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{TagOf(state)}-{at:yyyyMMdd-HHmmss}.png");

    /// <summary>One line of OCR text for a log, at most <see cref="ExcerptLength"/> characters.</summary>
    public static string Excerpt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(empty)";
        }

        string line = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= ExcerptLength ? line : line.Substring(0, ExcerptLength) + "...";
    }

    private static string MemoryText(bool? memory) =>
        memory is bool known ? known.ToString() : "unknown";

    private sealed class StateTrack
    {
        public bool Seen { get; set; }
        public bool Ocr { get; set; }
        public bool? Memory { get; set; }
        public int Disagreements { get; set; }
        public DateTimeOffset? LastWarning { get; set; }
        public DateTimeOffset? LastFrame { get; set; }
    }
}
