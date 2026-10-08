using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>A source filter by name and kind (<c>crop_filter</c>, <c>sharpness_filter</c>).</summary>
public sealed record ObsFilterInfo(string Name, string Kind);

/// <summary>
/// The profile settings that decide what HeroesReplay can do with an output, read with
/// GetProfileParameter. The encoders are the machine's choice and are only reported. The
/// bitrates are checked against <see cref="ObsBitratePolicy"/>.
/// </summary>
public sealed record ObsProfileInfo(
    string OutputMode,
    string RecordingFormat,
    string RecordingEncoder,
    string StreamEncoder
)
{
    /// <summary>
    /// Recording formats OBS writes as a <c>.mp4</c> file. The uploader, the pentakill clips, and
    /// retention look only for <c>*.mp4</c> in <c>Data\Contexts</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> Mp4Formats = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "mp4",
        "hybrid_mp4",
        "fragmented_mp4",
    };

    /// <summary>
    /// The stream's video bitrate in kbps (Simple output <c>VBitrate</c>). Null when the profile
    /// parameters do not hold it: Advanced output keeps it in <c>streamEncoder.json</c>, which
    /// obs-websocket does not read.
    /// </summary>
    public long? StreamBitrateKbps { get; init; }

    /// <summary><c>CBR</c> for Simple output, which always streams at a constant bitrate. Null when not known.</summary>
    public string StreamRateControl { get; init; }

    /// <summary>Simple output's recording quality: <c>Stream</c> (the stream's encoder and bitrate), <c>Small</c>, <c>HQ</c>, or <c>Lossless</c>.</summary>
    public string RecordingQuality { get; init; }

    /// <summary>
    /// The recording's video bitrate in kbps: the stream's when Simple output records at
    /// <c>Stream</c> quality, <c>FFVBitrate</c> for an Advanced custom (FFmpeg) recording. Null
    /// for a quality-based recording or when the profile parameters do not hold it.
    /// </summary>
    public long? RecordingBitrateKbps { get; init; }

    /// <summary><c>CBR</c> when the recording shares the stream's encoder; otherwise null.</summary>
    public string RecordingRateControl { get; init; }

    /// <summary>The recording is the stream's encoder output, so its bitrate is the stream's.</summary>
    public bool RecordingSharesStreamEncoder { get; init; }

    public bool RecordsMp4 =>
        RecordingFormat != null && Mp4Formats.Contains(RecordingFormat.Trim().TrimStart('.'));

    /// <exception cref="ObsRequestException">OBS refused GetProfileParameter.</exception>
    public static ObsProfileInfo Read(IObsReadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string mode = Parameter(session, "Output", "Mode") ?? "Simple";
        if (!string.Equals(mode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            long? stream = Kbps(Parameter(session, "SimpleOutput", "VBitrate"));
            string quality = Parameter(session, "SimpleOutput", "RecQuality");
            bool shared = string.Equals(quality, "Stream", StringComparison.OrdinalIgnoreCase);
            return new ObsProfileInfo(
                mode,
                Parameter(session, "SimpleOutput", "RecFormat2")
                    ?? Parameter(session, "SimpleOutput", "RecFormat"),
                Parameter(session, "SimpleOutput", "RecEncoder"),
                Parameter(session, "SimpleOutput", "StreamEncoder")
            )
            {
                StreamBitrateKbps = stream,
                StreamRateControl = stream.HasValue ? "CBR" : null,
                RecordingQuality = quality,
                RecordingBitrateKbps = shared ? stream : null,
                RecordingRateControl = shared && stream.HasValue ? "CBR" : null,
                RecordingSharesStreamEncoder = shared,
            };
        }

        // Advanced output: a Standard recording uses the container; a Custom Output (FFmpeg) the extension.
        bool ffmpeg = string.Equals(
            Parameter(session, "AdvOut", "RecType"),
            "FFmpeg",
            StringComparison.OrdinalIgnoreCase
        );
        string recordingEncoder = Parameter(session, "AdvOut", "RecEncoder");
        return new ObsProfileInfo(
            mode,
            ffmpeg
                ? Parameter(session, "AdvOut", "FFExtension")
                : Parameter(session, "AdvOut", "RecFormat2")
                    ?? Parameter(session, "AdvOut", "RecFormat"),
            recordingEncoder,
            Parameter(session, "AdvOut", "Encoder")
        )
        {
            // The encoder bitrates are in streamEncoder.json and recordEncoder.json, not here.
            RecordingBitrateKbps = ffmpeg ? Kbps(Parameter(session, "AdvOut", "FFVBitrate")) : null,
            RecordingSharesStreamEncoder =
                !ffmpeg
                && string.Equals(recordingEncoder, "none", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>The profile's value, or OBS's default when the profile does not set it.</summary>
    private static string Parameter(IObsReadSession session, string category, string name)
    {
        JObject response = session.Get(
            "GetProfileParameter",
            new JObject { ["parameterCategory"] = category, ["parameterName"] = name }
        );
        string value = ObsResponse.String(response, "parameterValue");
        return string.IsNullOrWhiteSpace(value)
            ? NullIfBlank(ObsResponse.String(response, "defaultParameterValue"))
            : value;
    }

    private static long? Kbps(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long kbps)
        && kbps > 0
            ? kbps
            : null;

    private static string NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
/// The lowest video bitrate the machine profile policy accepts for the output size and FPS.
/// Below it the game's motion breaks into blocks on stream and in the YouTube upload. The
/// floors are three quarters of Twitch's guidance (6000 kbps at 1080p60, 4500 at 1080p30 or
/// 720p60, 3000 at 720p30), rounded down to a multiple of 500. A bitrate under the floor is a
/// warning; it never
/// blocks a stream or a recording.
/// </summary>
public static class ObsBitratePolicy
{
    public const long Floor1080p60Kbps = 4500;
    public const long Floor1080p30Kbps = 3000;
    public const long FloorLowerKbps = 2000;

    /// <summary>The floor for the output (scaled) resolution and FPS, or null when either is unknown.</summary>
    public static long? FloorKbps(long? outputHeight, double? fps)
    {
        if (outputHeight is not > 0 || fps is not > 0)
        {
            return null;
        }

        bool high = fps > 31;
        if (outputHeight >= 1080)
        {
            return high ? Floor1080p60Kbps : Floor1080p30Kbps;
        }

        return outputHeight >= 720 && high ? Floor1080p30Kbps : FloorLowerKbps;
    }
}
