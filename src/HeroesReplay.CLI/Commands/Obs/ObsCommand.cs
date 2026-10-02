using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;

namespace HeroesReplay.CLI.Commands.Obs;

public class ObsCommand : Command
{
    public ObsCommand()
        : base(
            "obs",
            "Machine-local OBS controls. Twitch ingest starts only when OBS:StreamingEnabled is true and this machine is armed."
        )
    {
        Subcommands.Add(ArmCommand());
        Subcommands.Add(DisarmCommand());
        Subcommands.Add(StatusCommand());
    }

    private static Command ArmCommand()
    {
        var command = new Command(
            "arm",
            "Allow this machine to start Twitch ingest. Writes %LOCALAPPDATA%\\HeroesReplay\\stream-armed. Run it only on the stream PC; the spectator still needs OBS:StreamingEnabled (prod)."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Arm(new ObsStreamArm()));
            }
        );
        return command;
    }

    private static Command DisarmCommand()
    {
        var command = new Command(
            "disarm",
            "Stop this machine from starting Twitch ingest. Deletes the arm file. A stream that is already live keeps running until services stop or OBS stops it."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Disarm(new ObsStreamArm()));
            }
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Print the stream arm, OBS:StreamingEnabled, and the expected OBS profile and scene collection. Does not connect to OBS."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(Status(new ObsStreamArm()));
            }
        );
        return command;
    }

    private static int Arm(ObsStreamArm arm)
    {
        try
        {
            arm.Arm("Armed by heroesreplay obs arm.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not arm this machine. {e.Message}");
            return 1;
        }

        Console.WriteLine($"Armed: {arm.FilePath}");
        Console.WriteLine(
            "The spectator starts Twitch ingest on its next reconcile when OBS:StreamingEnabled is true."
        );
        return 0;
    }

    private static int Disarm(ObsStreamArm arm)
    {
        bool removed;
        try
        {
            removed = arm.Disarm();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not disarm this machine. {e.Message}");
            return 1;
        }

        Console.WriteLine(
            removed ? $"Disarmed: removed {arm.FilePath}" : $"Not armed: {arm.FilePath} is absent."
        );
        Console.WriteLine(
            "No new stream starts. A live stream keeps running until services stop or OBS stops it."
        );
        return 0;
    }

    private static int Status(ObsStreamArm arm)
    {
        bool armed = arm.IsArmed();
        Console.WriteLine($"Stream arm: {(armed ? "armed" : "not armed")} ({arm.FilePath})");
        OBSSettings obs;
        try
        {
            obs = ServiceCollectionExtensions.LoadObsSettings();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Settings could not be loaded. {e.Message}");
            return 1;
        }

        bool streaming = SessionMedia.ShouldStream(obs);
        Console.WriteLine($"OBS:StreamingEnabled: {streaming}");
        Console.WriteLine($"OBS profile: {ObsNames.Profile(obs)} (OBS:ProfileName)");
        Console.WriteLine(
            $"OBS scene collection: {ObsNames.SceneCollection(obs)} (OBS:SceneCollectionName)"
        );
        string blocked = TwitchIngestGuard.BlockedBy(streaming, armed);
        Console.WriteLine(
            streaming && armed
                ? "Twitch ingest: may start (the profile and scene collection are checked first)."
                : "Twitch ingest: off. "
                    + (blocked != null ? blocked + ". " : string.Empty)
                    + TwitchIngestGuard.Refusal(streaming, armed)
        );
        return 0;
    }
}
