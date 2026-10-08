using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Obs;

/// <summary><c>obs backup</c> and <c>obs restore</c> (#307): the live scene collection's backups.</summary>
public static class ObsBackupCommands
{
    public static Command BackupCommand()
    {
        var command = new Command(
            "backup",
            "Copy the live OBS scene collection (OBS:SceneCollectionName) into %LOCALAPPDATA%\\HeroesReplay\\obs\\backups, the folder every HeroesReplay write backs it up to (the newest 10 are kept), and list its backups with their time, size and SHA-256. Only reads the collection, so it is safe while OBS runs. --list lists without copying. Exit 1 when there is no collection or the copy failed."
        );
        Option<bool> list = new("--list") { Description = "List the backups only." };
        Option<string> format = OutputOption();
        command.Options.Add(list);
        command.Options.Add(format);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    Run(
                        parseResult.GetValue(format),
                        collection =>
                            parseResult.GetValue(list)
                                ? ObsCollectionBackups.List(
                                    ObsManagedFiles.ForThisUser(),
                                    collection
                                )
                                : ObsCollectionBackups.Take(
                                    ObsManagedFiles.ForThisUser(),
                                    collection,
                                    DateTime.UtcNow
                                )
                    )
                );
            }
        );
        return command;
    }

    public static Command RestoreCommand()
    {
        var command = new Command(
            "restore",
            "Write a backup of the live OBS scene collection back over it, byte for byte, while OBS is closed. The collection it replaces is backed up first (so a restore can be undone with the next backup), the write is atomic, and a release rollback that waited for OBS is cleared. managed-collections.json is not changed. Refused while OBS runs (obs.restore_obs_running), and for a file that is not a backup of this collection (obs.backup_other_file) or not a scene collection (obs.backup_invalid). Exit 1 when nothing was restored."
        );
        Argument<string> backup = new("backup")
        {
            Description =
                "The backup: a path, or a file name in %LOCALAPPDATA%\\HeroesReplay\\obs\\backups (`obs backup --list` shows them).",
        };
        Option<string> format = OutputOption();
        command.Arguments.Add(backup);
        command.Options.Add(format);
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    Run(
                        parseResult.GetValue(format),
                        collection =>
                            ObsCollectionBackups.Restore(
                                ObsManagedFiles.ForThisUser(),
                                collection,
                                parseResult.GetValue(backup),
                                NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
                                DateTime.UtcNow
                            )
                    )
                );
            }
        );
        return command;
    }

    private static Option<string> OutputOption()
    {
        var format = new Option<string>("--output")
        {
            Description =
                "text (default) or json: schemaVersion, ok, code, message, collection, backup, saved, backups.",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "json");
        format.Aliases.Add("-o");
        return format;
    }

    private static int Run(string format, Func<string, ObsBackupResult> run)
    {
        string collection;
        try
        {
            collection = ObsNames.CollectionFile(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ObsNames.SceneCollection(ServiceCollectionExtensions.LoadObsSettings())
            );
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Settings could not be loaded. {e.Message}");
            return 1;
        }

        ObsBackupResult result = run(collection);
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(result.ToJson());
        }
        else
        {
            WriteText(result, result.Ok ? Console.Out : Console.Error);
        }

        return result.Ok ? 0 : 1;
    }

    private static void WriteText(ObsBackupResult result, TextWriter output)
    {
        output.WriteLine($"{result.Message} [{result.Code}]");
        foreach (ObsBackupInfo backup in result.Backups)
        {
            string sha = backup.Sha256 == null ? "unreadable" : backup.Sha256[..12];
            output.WriteLine(
                $"  {backup.TakenAtUtc:u}  {backup.Bytes, 9} bytes  {sha}  {Path.GetFileName(backup.Path)}"
            );
        }
    }
}
