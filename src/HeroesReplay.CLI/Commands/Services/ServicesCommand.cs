using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.Processes;
using HeroesReplay.Core.Services.Status;

namespace HeroesReplay.CLI.Commands.Services;

public class ServicesCommand : Command
{
    public ServicesCommand()
        : base(
            "services",
            "Start, stop, or inspect the spectator, Twitch, downloader, and YouTube processes."
        )
    {
        Subcommands.Add(StartCommand());
        Subcommands.Add(StopCommand());
        Subcommands.Add(StatusCommand());
    }

    private static Command StartCommand()
    {
        var command = new Command(
            "start",
            "Start spectate, twitch connect, heroesprofile download, and youtube uploader. Does not start Twitch ingest."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                string exe = Environment.ProcessPath;
                int code = ServiceSupervisor.Start(
                    ServiceLockStore.DefaultPath,
                    exe,
                    ProcessNameOrNull,
                    (name, arguments) => StartProcess(exe, arguments),
                    () => ServiceStopFile.Clear()
                );
                return Task.FromResult(code);
            }
        );
        return command;
    }

    private static Command StopCommand()
    {
        var command = new Command(
            "stop",
            "Ask the recorded processes to shut down, kill any still running after 20 seconds, and close Heroes of the Storm."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                int code = ServiceSupervisor.Stop(
                    ServiceLockStore.DefaultPath,
                    ProcessNameOrNull,
                    Kill,
                    () => ServiceStopFile.Request(),
                    TimeSpan.FromSeconds(20),
                    Thread.Sleep,
                    () => ServiceStopFile.Clear(),
                    StopSpectatedGame
                );
                return Task.FromResult(code);
            }
        );
        return command;
    }

    private static Command StatusCommand()
    {
        var command = new Command(
            "status",
            "Show which service processes are still running, plus the spectator status file."
        );
        command.SetAction(
            (parseResult, cancellationToken) =>
            {
                int code = ServiceSupervisor.Status(
                    ServiceLockStore.DefaultPath,
                    ProcessNameOrNull,
                    new SpectatorStatusStore().TryReadShared()
                );
                return Task.FromResult(code);
            }
        );
        return command;
    }

    private static string ProcessNameOrNull(int pid)
    {
        try
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static string PowerShellStartCommand(
        string exe,
        string arguments,
        string pidFile
    )
    {
        string argList = string.Join(
            ",",
            arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(PsQuote)
        );
        return "$ProgressPreference = 'SilentlyContinue'; $p = Start-Process -FilePath "
            + PsQuote(exe)
            + " -ArgumentList "
            + argList
            + " -WorkingDirectory "
            + PsQuote(Path.GetDirectoryName(exe))
            + " -WindowStyle Normal -PassThru; Set-Content -LiteralPath "
            + PsQuote(pidFile)
            + " -Value $p.Id -NoNewline";
    }

    private static int? StartProcess(string exe, string arguments)
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "logs"
        );
        Directory.CreateDirectory(logDir);
        string slug = string.Join("-", arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        string pidFile = Path.Combine(logDir, slug + ".pid");
        if (File.Exists(pidFile))
        {
            File.Delete(pidFile);
        }

        string script = PowerShellStartCommand(exe, arguments, pidFile);
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using Process process = Process.Start(
            new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        );
        if (process == null)
        {
            return null;
        }

        if (!process.WaitForExit(15000))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException) { }
            return null;
        }

        int? pid = File.Exists(pidFile) ? ParseProcessId(File.ReadAllText(pidFile)) : null;
        if (process.ExitCode != 0 || pid == null)
        {
            return null;
        }

        Console.WriteLine($"Console open for {arguments} (pid file {pidFile}).");
        return pid;
    }

    public static int? ParseProcessId(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(lines[i].Trim(), out int pid) && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    private static string PsQuote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

    private static void StopSpectatedGame()
    {
        foreach (Process game in Process.GetProcessesByName("HeroesOfTheStorm_x64"))
        {
            try
            {
                game.Kill(entireProcessTree: true);
                Console.WriteLine($"Stopped Heroes of the Storm pid {game.Id}.");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(
                    $"Could not stop Heroes of the Storm pid {game.Id}: {e.Message}"
                );
            }
        }
    }

    private static void Kill(int pid)
    {
        using Process process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }
}
