using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Obs.Inspection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs.Recording;

/// <summary>What <see cref="ObsRecordingFormat.Apply"/> did before a StartRecord.</summary>
public enum ObsRecordingFormatState
{
    /// <summary>The profile already recorded the configured format. Nothing was sent.</summary>
    Unchanged,

    /// <summary>SetProfileParameter sent it, and the read-back matched.</summary>
    Set,

    /// <summary>OBS accepted SetProfileParameter, but the read-back is another format.</summary>
    Mismatch,

    /// <summary>A request failed. The profile's format is left as OBS has it.</summary>
    Failed,

    /// <summary><c>OBS:RecordingFormat</c> is not an allowed value. Nothing was sent.</summary>
    Invalid,
}

/// <summary>
/// <paramref name="Category"/> is <c>SimpleOutput</c> or <c>AdvOut</c>, <paramref name="Before"/>
/// the profile's format before the set, and <paramref name="ReadBack"/> what OBS reported after it.
/// </summary>
public sealed record ObsRecordingFormatResult(
    ObsRecordingFormatState State,
    string Format,
    string Category = null,
    string Before = null,
    string ReadBack = null
);

/// <summary>
/// The recording container (#310). The machine owns its profile, so the spectator does not
/// migrate <c>basic.ini</c>. Right before each StartRecord, where it sets the record directory,
/// it sets the active output mode's <c>RecFormat2</c> to <c>OBS:RecordingFormat</c> with
/// SetProfileParameter and reads it back. A failure is logged and the recording starts anyway.
/// </summary>
/// <remarks>
/// OBS 32.2.2 reads <c>RecFormat2</c> at every StartRecord for the file extension and, for a
/// <c>fragmented_*</c> format, the muxer's <c>movflags=frag_keyframe+empty_moov+delay_moov</c>.
/// It picks the muxer itself (its own Hybrid MP4 muxer for <c>hybrid_mp4</c>, otherwise
/// <c>obs-ffmpeg-mux</c>) only when it creates its outputs: at start, or when its settings are
/// applied. So <c>mp4</c>, <c>fragmented_mp4</c>, and <c>mkv</c> apply to the next recording, and
/// a change to or from <c>hybrid_mp4</c> applies after OBS restarts.
/// </remarks>
public static class ObsRecordingFormat
{
    public const string Mp4 = "mp4";
    public const string HybridMp4 = "hybrid_mp4";
    public const string FragmentedMp4 = "fragmented_mp4";
    public const string Mkv = "mkv";

    /// <summary>
    /// The default. In the #310 crash test (OBS 32.2.2, Simple output) it was the only format
    /// that kept a usable ffprobe duration and the <c>.mp4</c> extension after both
    /// <c>obs64</c> and <c>obs-ffmpeg-mux</c> were killed. Its fragments are complete on disk as
    /// it records, so a file cut off mid-write still opens.
    /// </summary>
    public const string Default = FragmentedMp4;

    public const string SimpleCategory = "SimpleOutput";
    public const string AdvancedCategory = "AdvOut";
    public const string ParameterName = "RecFormat2";

    /// <summary>The values <c>OBS:RecordingFormat</c> accepts.</summary>
    public static readonly IReadOnlyList<string> Allowed = [Mp4, HybridMp4, FragmentedMp4, Mkv];

    /// <summary>
    /// The configured format in its canonical spelling: <see cref="Default"/> when blank, null
    /// when it is not one of <see cref="Allowed"/>.
    /// </summary>
    public static string Resolve(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Default;
        }

        string trimmed = configured.Trim();
        return Allowed.FirstOrDefault(format =>
            string.Equals(format, trimmed, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>The validation error for <paramref name="configured"/>, or null when it is allowed.</summary>
    public static string Validate(string configured) =>
        Resolve(configured) != null
            ? null
            : "OBS:RecordingFormat '"
                + configured
                + "' is not one of "
                + string.Join(", ", Allowed)
                + ". The spectator leaves the profile's recording format as it is. Set it to "
                + Default
                + ".";

    /// <summary>
    /// True for the one format that survived the #310 crash test with a usable duration:
    /// <c>fragmented_mp4</c>. Plain MP4, and Hybrid MP4 set while OBS ran, lost the whole file when
    /// the muxer died, and MKV lost its duration.
    /// </summary>
    public static bool IsCrashSafe(string format) =>
        string.Equals(
            format?.Trim().TrimStart('.'),
            FragmentedMp4,
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>The profile section OBS reads in this output mode.</summary>
    public static string Category(string outputMode) =>
        string.Equals(outputMode, "Advanced", StringComparison.OrdinalIgnoreCase)
            ? AdvancedCategory
            : SimpleCategory;

    /// <summary>
    /// Sets the active output mode's <c>RecFormat2</c> to <paramref name="configured"/> and
    /// reads it back. Never throws: every failure is logged and returned.
    /// </summary>
    public static ObsRecordingFormatResult Apply(
        IObsRecordFormatSession session,
        string configured,
        ILogger logger,
        int? replayId = null
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logger);
        string format = Resolve(configured);
        if (format == null)
        {
            logger.LogError(
                "OBS recording format was not set for replay {ReplayId}. {Error}",
                replayId,
                Validate(configured)
            );
            return new ObsRecordingFormatResult(ObsRecordingFormatState.Invalid, configured);
        }

        string category = null;
        string before = null;
        try
        {
            category = Category(ObsProfileInfo.Parameter(session, "Output", "Mode"));
            before = ObsProfileInfo.Parameter(session, category, ParameterName);
            if (Same(before, format))
            {
                logger.LogDebug(
                    "OBS already records {Format} ({Category}/{Name}) for replay {ReplayId}.",
                    format,
                    category,
                    ParameterName,
                    replayId
                );
                return new ObsRecordingFormatResult(
                    ObsRecordingFormatState.Unchanged,
                    format,
                    category,
                    before,
                    before
                );
            }

            session.SetRecordingFormat(category, format);
            string readBack = ObsProfileInfo.Parameter(session, category, ParameterName);
            if (!Same(readBack, format))
            {
                logger.LogWarning(
                    "OBS did not keep the recording format for replay {ReplayId}: {Category}/{Name} reads {ReadBack} after it was set to {Format}. The replay records as OBS has it.",
                    replayId,
                    category,
                    ParameterName,
                    readBack ?? "nothing",
                    format
                );
                return new ObsRecordingFormatResult(
                    ObsRecordingFormatState.Mismatch,
                    format,
                    category,
                    before,
                    readBack
                );
            }

            logger.LogInformation(
                "OBS recording format for replay {ReplayId} is {Format} ({Category}/{Name}, was {Before}).{Restart}",
                replayId,
                format,
                category,
                ParameterName,
                before ?? "not set",
                ChangesMuxer(before, format)
                    ? " OBS picks its Hybrid MP4 muxer only when it starts, so this applies fully after OBS restarts."
                    : string.Empty
            );
            return new ObsRecordingFormatResult(
                ObsRecordingFormatState.Set,
                format,
                category,
                before,
                readBack
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not set the OBS recording format to {Format} for replay {ReplayId}. The replay records as the profile has it.",
                format,
                replayId
            );
            return new ObsRecordingFormatResult(
                ObsRecordingFormatState.Failed,
                format,
                category,
                before
            );
        }
    }

    private static bool Same(string profile, string format) =>
        string.Equals(profile?.Trim(), format, StringComparison.OrdinalIgnoreCase);

    /// <summary>OBS's own Hybrid MP4 muxer is chosen when OBS creates its outputs, not per recording.</summary>
    private static bool ChangesMuxer(string before, string after) =>
        IsHybrid(before) != IsHybrid(after);

    private static bool IsHybrid(string format) =>
        format?.Trim().StartsWith("hybrid_", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// The spectator's own connection, seen as the read-only Gets of <see cref="IObsReadSession"/>
/// plus the one profile write the recording format needs. It can change nothing else.
/// </summary>
public interface IObsRecordFormatSession : IObsReadSession
{
    /// <summary>SetProfileParameter <paramref name="category"/>/<c>RecFormat2</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="category"/> is not <c>SimpleOutput</c> or <c>AdvOut</c>.</exception>
    /// <exception cref="ObsRequestException">OBS refused the request.</exception>
    void SetRecordingFormat(string category, string format);
}

/// <summary>
/// <see cref="IObsRecordFormatSession"/> over the spectator's one connection per replay.
/// Disposing it leaves the connection open.
/// </summary>
internal sealed class ObsBorrowedRecordFormatSession : IObsRecordFormatSession
{
    private readonly OBSWebsocket obs;
    private readonly ObsBorrowedReadSession read;

    public ObsBorrowedRecordFormatSession(OBSWebsocket obs)
    {
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        read = new ObsBorrowedReadSession(obs);
    }

    public JObject Get(string requestType, JObject requestData = null) =>
        read.Get(requestType, requestData);

    public void SetRecordingFormat(string category, string format)
    {
        if (
            category != ObsRecordingFormat.SimpleCategory
            && category != ObsRecordingFormat.AdvancedCategory
        )
        {
            throw new ArgumentException(
                "Only SimpleOutput and AdvOut hold a recording format.",
                nameof(category)
            );
        }

        try
        {
            obs.SetProfileParameter(category, ObsRecordingFormat.ParameterName, format);
        }
        catch (ErrorResponseException e)
        {
            throw new ObsRequestException("SetProfileParameter", e.ErrorCode, e.Message);
        }
    }

    public void Dispose()
    {
        // The spectator's session owns the connection.
    }
}
