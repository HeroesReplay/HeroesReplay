using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs.Inspection;

namespace HeroesReplay.CLI.Commands.Obs;

/// <summary>
/// <c>obs inspect</c> and <c>obs validate</c>: the read-only <c>obs_inspect</c> and
/// <c>obs_validate</c> MCP tools as commands. Each opens one short session that sends only
/// <see cref="ObsReadOnly"/> Get requests and never prints the stream key.
/// </summary>
public static class ObsLiveCommands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static Command InspectCommand() =>
        Create(
            "inspect",
            "Read live OBS without changing it: versions, active profile and scene collection, canvas and FPS, output mode, recording format, encoders and bitrates, the record directory, scenes, inputs and global audio, stream and record status, stats, the stream service (never the key), and the stream arm. Exit 1 when OBS cannot be read.",
            json =>
                Inspect(
                    new ObsWebsocketReadSessionFactory(),
                    ServiceCollectionExtensions.LoadObsInspectionSettings,
                    json,
                    Console.Out
                )
        );

    public static Command ValidateCommand() =>
        Create(
            "validate",
            "Check the collection OBS has loaded against obs/Default.json and this install's settings without changing it: the install's OBS files against obs/bundle.manifest, the websocket requests HeroesReplay sends, profile and collection, scenes, sources and filters, where each driven item and the game capture are placed, asset paths, Mic/Aux, canvas 1920x1080 and FPS, the recording format (.mp4), the stream and recording bitrate floor, and the stream service when OBS:StreamingEnabled. Findings have stable codes. Exit 0 when there is no error finding, 1 otherwise or when OBS cannot be read.",
            json =>
                Validate(
                    new ObsWebsocketReadSessionFactory(),
                    ServiceCollectionExtensions.LoadObsInspectionSettings,
                    json,
                    Console.Out
                )
        );

    public static int Inspect(
        IObsReadSessionFactory sessions,
        Func<ObsInspectionSettings> settings,
        bool json,
        TextWriter output
    )
    {
        ObsLiveReadResult<ObsInspection> run = ObsLiveRead.Run(
            sessions,
            settings,
            ObsInspector.Inspect
        );
        ObsInspection inspection =
            run.Value ?? ObsInspector.Unavailable(run.Settings, run.Code, run.Message);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(inspection, Json));
        }
        else
        {
            WriteText(inspection, output);
        }

        return inspection.Ok ? 0 : 1;
    }

    public static int Validate(
        IObsReadSessionFactory sessions,
        Func<ObsInspectionSettings> settings,
        bool json,
        TextWriter output
    )
    {
        ObsLiveReadResult<ObsValidation> run = ObsLiveRead.Run(
            sessions,
            settings,
            ObsValidator.Validate
        );
        ObsValidation validation =
            run.Value ?? ObsValidator.Unavailable(run.Settings, run.Code, run.Message);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(validation, Json));
        }
        else
        {
            WriteText(validation, output);
        }

        return validation.Ok ? 0 : 1;
    }

    private static Command Create(string name, string description, Func<bool, int> run)
    {
        var command = new Command(name, description);
        var format = new Option<string>("--output")
        {
            Description =
                "text (default) or json. JSON is the same object the MCP tool returns: schemaVersion, ok, code, and the details.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "json");
        format.Aliases.Add("-o");
        command.Options.Add(format);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    run(
                        string.Equals(
                            parseResult.GetValue(format),
                            "json",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                );
            }
        );
        return command;
    }

    private static void WriteText(ObsValidation validation, TextWriter output)
    {
        if (validation.Code != null && validation.Findings.Count == 0)
        {
            output.WriteLine($"OBS was not validated ({validation.Code}). {validation.Error}");
            return;
        }

        output.WriteLine(
            $"OBS at {validation.Endpoint}: {(validation.Ok ? "ok" : "not ok")}, {validation.Errors} error(s), {validation.Warnings} warning(s)."
        );
        foreach (ObsFinding finding in validation.Findings)
        {
            output.WriteLine(
                $"{finding.Severity} {finding.Code} {finding.Subject}: {finding.Message}"
            );
        }
    }

    private static void WriteText(ObsInspection inspection, TextWriter output)
    {
        if (!inspection.Ok)
        {
            output.WriteLine($"OBS was not read ({inspection.Code}). {inspection.Error}");
            WriteArm(inspection.StreamArm, output);
            return;
        }

        ObsVersionInfo version = inspection.Version;
        if (version != null)
        {
            output.WriteLine(
                $"OBS {version.ObsVersion}, obs-websocket {version.WebsocketVersion} at {inspection.Endpoint}, {version.AvailableRequestCount} requests."
            );
        }

        ObsSelectionInfo selection = inspection.Selection;
        if (selection != null)
        {
            output.WriteLine(
                $"Profile {selection.ActiveProfile} (expected {selection.ExpectedProfile}), scene collection {selection.ActiveCollection} (expected {selection.ExpectedCollection}): {(selection.Ok ? "ok" : selection.Reason)}."
            );
        }

        ObsVideoInfo video = inspection.Video;
        if (video != null)
        {
            output.WriteLine(
                $"Canvas {video.BaseWidth}x{video.BaseHeight}, output {video.OutputWidth}x{video.OutputHeight}, {video.Fps} FPS."
            );
        }

        ObsProfileInfo profile = inspection.Profile;
        if (profile != null)
        {
            output.WriteLine(
                $"{profile.OutputMode} output: records {profile.RecordingFormat} with {profile.RecordingEncoder}, streams with {profile.StreamEncoder}."
            );
            output.WriteLine(
                $"Bitrate: stream {Kbps(profile.StreamBitrateKbps, profile.StreamRateControl)}, recording {(profile.RecordingQuality != null ? profile.RecordingQuality + " quality, " : "")}{Kbps(profile.RecordingBitrateKbps, profile.RecordingRateControl)}."
            );
        }

        if (inspection.RecordDirectory != null)
        {
            output.WriteLine($"Record directory: {inspection.RecordDirectory}.");
        }

        output.WriteLine($"Program scene: {inspection.ProgramScene}.");
        if (inspection.Scenes != null)
        {
            output.WriteLine(
                $"Scenes ({inspection.Scenes.Count}): {string.Join(", ", inspection.Scenes.Select(scene => scene.Name))}."
            );
        }

        if (inspection.Inputs != null)
        {
            foreach (
                ObsInputInfo input in inspection.Inputs.Where(input => input.GlobalAudio != null)
            )
            {
                output.WriteLine(
                    $"Global audio {input.GlobalAudio}: {input.Name}, {(input.Muted == true ? "muted" : "not muted")}."
                );
            }
        }

        if (inspection.Stream != null)
        {
            output.WriteLine(
                $"Stream: {(inspection.Stream.Active == true ? "active " + inspection.Stream.Timecode : "inactive")}, dropped {inspection.Stream.DroppedFrames} of {inspection.Stream.TotalFrames} frames."
            );
        }

        if (inspection.Record != null)
        {
            output.WriteLine(
                $"Recording: {(inspection.Record.Active == true ? "active " + inspection.Record.Timecode : "inactive")}."
            );
        }

        if (inspection.StreamService != null)
        {
            output.WriteLine(
                $"Stream service: {inspection.StreamService.Service ?? inspection.StreamService.Type}, key {(inspection.StreamService.KeySet ? "set" : "not set")}."
            );
        }

        WriteArm(inspection.StreamArm, output);
        foreach (string unread in inspection.Unread)
        {
            output.WriteLine($"Not read: {unread}");
        }
    }

    private static string Kbps(long? kbps, string rateControl) =>
        kbps == null
            ? "not in the profile parameters"
            : kbps + " kbps" + (rateControl != null ? " " + rateControl : "");

    private static void WriteArm(ObsStreamArmInfo arm, TextWriter output)
    {
        if (arm != null)
        {
            output.WriteLine(
                $"Stream arm: {(arm.Armed ? "armed" : "not armed")}, OBS:StreamingEnabled {arm.StreamingEnabled}{(arm.BlockedBy != null ? ", blocked by " + arm.BlockedBy : "")}."
            );
        }
    }
}
