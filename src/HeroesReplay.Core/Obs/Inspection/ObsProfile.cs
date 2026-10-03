using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>A source filter by name and kind (<c>crop_filter</c>, <c>sharpness_filter</c>).</summary>
public sealed record ObsFilterInfo(string Name, string Kind);

/// <summary>
/// The profile settings that decide what HeroesReplay can do with an output, read with
/// GetProfileParameter. The encoders are the machine's choice and are only reported.
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

    public bool RecordsMp4 =>
        RecordingFormat != null && Mp4Formats.Contains(RecordingFormat.Trim().TrimStart('.'));

    /// <exception cref="ObsRequestException">OBS refused GetProfileParameter.</exception>
    public static ObsProfileInfo Read(IObsReadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string mode = Parameter(session, "Output", "Mode") ?? "Simple";
        if (!string.Equals(mode, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            return new ObsProfileInfo(
                mode,
                Parameter(session, "SimpleOutput", "RecFormat2")
                    ?? Parameter(session, "SimpleOutput", "RecFormat"),
                Parameter(session, "SimpleOutput", "RecEncoder"),
                Parameter(session, "SimpleOutput", "StreamEncoder")
            );
        }

        // Advanced output: a Standard recording uses the container; a Custom Output (FFmpeg) the extension.
        bool ffmpeg = string.Equals(
            Parameter(session, "AdvOut", "RecType"),
            "FFmpeg",
            StringComparison.OrdinalIgnoreCase
        );
        return new ObsProfileInfo(
            mode,
            ffmpeg
                ? Parameter(session, "AdvOut", "FFExtension")
                : Parameter(session, "AdvOut", "RecFormat2")
                    ?? Parameter(session, "AdvOut", "RecFormat"),
            Parameter(session, "AdvOut", "RecEncoder"),
            Parameter(session, "AdvOut", "Encoder")
        );
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

    private static string NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
