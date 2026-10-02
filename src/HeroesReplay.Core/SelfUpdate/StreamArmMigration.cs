using System;
using System.IO;
using HeroesReplay.Core.Obs;

namespace HeroesReplay.Core.SelfUpdate;

public enum StreamArmMigrationOutcome
{
    /// <summary>The migration ran on an earlier update. The arm is left as the operator set it.</summary>
    AlreadyMigrated,

    /// <summary>The replaced install streamed, so this machine was armed once.</summary>
    Armed,

    /// <summary>The replaced install streamed and this machine was already armed.</summary>
    AlreadyArmed,

    /// <summary>The replaced install did not stream. This machine was not armed.</summary>
    StreamingDisabled,
}

public sealed record StreamArmMigrationResult(StreamArmMigrationOutcome Outcome, string Message);

/// <summary>
/// One-time handoff for the update that introduces the stream arm. Before this release,
/// <c>OBS:StreamingEnabled</c> alone started ingest. When the install being replaced has it
/// true, the machine is armed so the stream does not go dark. A marker next to the arm makes
/// this run once: a later <c>obs disarm</c> is not undone by the next update. A machine whose
/// settings do not stream is never armed.
/// </summary>
public static class StreamArmMigration
{
    public const string MarkerFileName = "stream-arm.migrated";

    public static string MarkerPathFor(ObsStreamArm arm) =>
        Path.Combine(Path.GetDirectoryName(arm.FilePath) ?? string.Empty, MarkerFileName);

    public static StreamArmMigrationOutcome Decide(bool migrated, bool streamingEnabled, bool armed)
    {
        if (migrated)
        {
            return StreamArmMigrationOutcome.AlreadyMigrated;
        }

        if (!streamingEnabled)
        {
            return StreamArmMigrationOutcome.StreamingDisabled;
        }

        return armed ? StreamArmMigrationOutcome.AlreadyArmed : StreamArmMigrationOutcome.Armed;
    }

    public static StreamArmMigrationResult Run(
        ObsStreamArm arm,
        bool streamingEnabled,
        string previousInstall
    )
    {
        if (arm == null)
        {
            throw new ArgumentNullException(nameof(arm));
        }

        string marker = MarkerPathFor(arm);
        StreamArmMigrationOutcome outcome = Decide(
            File.Exists(marker),
            streamingEnabled,
            arm.IsArmed()
        );
        if (outcome == StreamArmMigrationOutcome.AlreadyMigrated)
        {
            return new StreamArmMigrationResult(
                outcome,
                "Stream arm migration already ran on this machine. The arm was left as it is ("
                    + (arm.IsArmed() ? "armed" : "not armed")
                    + ")."
            );
        }

        string message;
        if (outcome == StreamArmMigrationOutcome.Armed)
        {
            arm.Arm(
                "One-time release migration: OBS:StreamingEnabled was true in "
                    + previousInstall
                    + "."
            );
            message =
                "OBS:StreamingEnabled was true in "
                + previousInstall
                + ". This machine was armed once for Twitch ingest ("
                + arm.FilePath
                + "). `heroesreplay obs disarm` turns it off; later updates do not arm it again.";
        }
        else if (outcome == StreamArmMigrationOutcome.AlreadyArmed)
        {
            message = "This machine was already armed for Twitch ingest (" + arm.FilePath + ").";
        }
        else
        {
            message =
                "OBS:StreamingEnabled is false in "
                + previousInstall
                + ". This machine was not armed for Twitch ingest.";
        }

        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(
            marker,
            outcome + " " + DateTimeOffset.UtcNow.ToString("O") + Environment.NewLine
        );
        return new StreamArmMigrationResult(outcome, message);
    }
}
