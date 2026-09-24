using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HeroesReplay.CLI;

public sealed record AspireCliInvocation(
    string FileName,
    string Arguments,
    string WorkingDirectory
);

public sealed record AspireProcessStart(int? Pid, string Error, string LogPath);

public sealed record DashboardEnsureResult(bool Listening, bool Launched, string Detail);

public static class AspireDashboardHost
{
    public const string UiUrl = "http://127.0.0.1:18888";
    public const string OtlpGrpcEndpoint = "http://127.0.0.1:4317";
    public const string OtlpHttpEndpoint = "http://127.0.0.1:4318";
    public const int UiPort = 18888;
    public const int OtlpGrpcPort = 4317;
    public const int OtlpHttpPort = 4318;
    public const string RunArguments =
        "dashboard run --allow-anonymous --non-interactive --nologo --frontend-url "
        + UiUrl
        + " --otlp-grpc-url "
        + OtlpGrpcEndpoint
        + " --otlp-http-url "
        + OtlpHttpEndpoint;

    public static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(30);

    public static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "logs"
        );

    public static string StdoutLogPath => Path.Combine(LogDirectory, "aspire-dashboard.log");

    public static string StderrLogPath => Path.Combine(LogDirectory, "aspire-dashboard.err.log");

    public static string PidPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "aspire-dashboard.pid"
        );

    public static void ApplyExporterEnvironment()
    {
        ApplyExporterEnvironment(
            name => Environment.GetEnvironmentVariable(name),
            (name, value) => Environment.SetEnvironmentVariable(name, value)
        );
    }

    public static void ApplyExporterEnvironment(
        Func<string, string> getVariable,
        Action<string, string> setVariable
    )
    {
        ArgumentNullException.ThrowIfNull(getVariable);
        ArgumentNullException.ThrowIfNull(setVariable);
        if (string.IsNullOrWhiteSpace(getVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
        {
            setVariable("OTEL_EXPORTER_OTLP_ENDPOINT", OtlpGrpcEndpoint);
        }

        if (string.IsNullOrWhiteSpace(getVariable("OTEL_EXPORTER_OTLP_PROTOCOL")))
        {
            setVariable("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
        }
    }

    public static bool EnsureRunning()
    {
        try
        {
            ApplyExporterEnvironment();
            using var mutex = new Mutex(false, @"Local\HeroesReplay.AspireDashboard");
            bool locked = false;
            try
            {
                locked = mutex.WaitOne(StartupBudget + TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                locked = true;
            }

            if (!locked)
            {
                if (IsPortListening(OtlpGrpcPort))
                {
                    Console.WriteLine(
                        $"Aspire dashboard is already listening. UI {UiUrl}. OTLP gRPC {OtlpGrpcEndpoint}."
                    );
                    return true;
                }

                Console.Error.WriteLine(
                    "Aspire dashboard lock is held and OTLP is not listening yet. Continuing without the dashboard."
                );
                return false;
            }

            try
            {
                DashboardEnsureResult result = Ensure(
                    () => IsPortListening(OtlpGrpcPort),
                    Launch,
                    ProcessAlive,
                    SearchStarts(),
                    ManifestDeclaresAspire,
                    IsCommandOnPath("aspire", Environment.GetEnvironmentVariable("PATH")),
                    Directory.GetCurrentDirectory(),
                    Log,
                    Thread.Sleep,
                    StartupBudget
                );
                return result.Listening;
            }
            finally
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException) { }
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(
                $"Aspire dashboard was not started. Continuing without the dashboard. {e.Message}"
            );
            return false;
        }
    }

    public static DashboardEnsureResult Ensure(
        Func<bool> otlpListening,
        Func<AspireCliInvocation, AspireProcessStart> start,
        Func<int, bool> processAlive,
        IEnumerable<string> searchStarts,
        Func<string, bool> manifestDeclaresAspire,
        bool aspireOnPath,
        string pathFallbackDirectory,
        Action<string> log,
        Action<TimeSpan> wait,
        TimeSpan startupBudget
    )
    {
        ArgumentNullException.ThrowIfNull(otlpListening);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(wait);

        if (otlpListening())
        {
            string already =
                $"Aspire dashboard is already listening. UI {UiUrl}. OTLP gRPC {OtlpGrpcEndpoint}.";
            log(already);
            return new DashboardEnsureResult(true, false, already);
        }

        AspireCliInvocation invocation = Resolve(
            searchStarts,
            manifestDeclaresAspire,
            aspireOnPath,
            pathFallbackDirectory
        );
        if (invocation == null)
        {
            string missing =
                "Aspire dashboard was not started: the Aspire CLI was not found. "
                + "Run `dotnet tool restore` so the local Aspire.Cli tool is available, or install `aspire` on PATH. "
                + "Continuing without the dashboard.";
            log(missing);
            return new DashboardEnsureResult(false, false, missing);
        }

        log(
            $"Starting Aspire dashboard with `{invocation.FileName} {invocation.Arguments}` in {invocation.WorkingDirectory}."
        );
        AspireProcessStart started = start(invocation);
        if (started == null || started.Pid is not int pid || pid <= 0)
        {
            string reason = string.IsNullOrWhiteSpace(started?.Error)
                ? "process did not start"
                : started.Error;
            string failed =
                $"Aspire dashboard was not started ({reason}). Continuing without the dashboard.";
            log(failed);
            return new DashboardEnsureResult(false, false, failed);
        }

        log($"Aspire dashboard launcher pid {pid}. Waiting for {OtlpGrpcEndpoint}.");
        DateTimeOffset deadline = DateTimeOffset.UtcNow + startupBudget;
        while (true)
        {
            if (otlpListening())
            {
                string up =
                    $"Aspire dashboard is up. UI {UiUrl}. OTLP gRPC {OtlpGrpcEndpoint}. "
                    + "CLI processes export logs, metrics, and traces there.";
                log(up);
                return new DashboardEnsureResult(true, true, up);
            }

            if (processAlive != null && !processAlive(pid))
            {
                string tail = Tail(started.LogPath);
                string exited =
                    $"Aspire dashboard process {pid} exited before {OtlpGrpcEndpoint} was listening. "
                    + "Continuing without the dashboard.";
                if (!string.IsNullOrWhiteSpace(tail))
                {
                    exited += " " + tail;
                }

                log(exited);
                return new DashboardEnsureResult(false, true, exited);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                break;
            }

            wait(TimeSpan.FromMilliseconds(200));
        }

        string pending =
            $"Aspire dashboard launcher pid {pid} is running but {OtlpGrpcEndpoint} is not listening yet. "
            + "Continuing without waiting. CLI export stays pointed at that endpoint.";
        log(pending);
        return new DashboardEnsureResult(false, true, pending);
    }

    public static AspireCliInvocation Resolve(
        IEnumerable<string> searchStarts,
        Func<string, bool> manifestDeclaresAspire,
        bool aspireOnPath,
        string pathFallbackDirectory
    )
    {
        if (searchStarts != null && manifestDeclaresAspire != null)
        {
            foreach (string start in searchStarts)
            {
                string directory = FindToolDirectory(start, manifestDeclaresAspire);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return new AspireCliInvocation("dotnet", "aspire " + RunArguments, directory);
                }
            }
        }

        if (!aspireOnPath)
        {
            return null;
        }

        string working = string.IsNullOrWhiteSpace(pathFallbackDirectory)
            ? Directory.GetCurrentDirectory()
            : pathFallbackDirectory;
        return new AspireCliInvocation("aspire", RunArguments, working);
    }

    public static string FindToolDirectory(string start, Func<string, bool> manifestDeclaresAspire)
    {
        if (string.IsNullOrWhiteSpace(start) || manifestDeclaresAspire == null)
        {
            return null;
        }

        DirectoryInfo dir;
        try
        {
            dir = new DirectoryInfo(start);
        }
        catch (Exception)
        {
            return null;
        }

        while (dir != null)
        {
            string manifest = Path.Combine(dir.FullName, ".config", "dotnet-tools.json");
            try
            {
                if (manifestDeclaresAspire(manifest))
                {
                    return dir.FullName;
                }
            }
            catch (Exception)
            {
                // Keep walking if a manifest cannot be read.
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static bool ManifestDeclaresAspire(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        string json = File.ReadAllText(path);
        return json.Contains("\"aspire\"", StringComparison.Ordinal);
    }

    public static bool IsCommandOnPath(string name, string pathEnv)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pathEnv))
        {
            return false;
        }

        foreach (
            string part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            string dir = part.Trim();
            if (dir.Length == 0)
            {
                continue;
            }

            if (
                File.Exists(Path.Combine(dir, name))
                || File.Exists(Path.Combine(dir, name + ".exe"))
                || File.Exists(Path.Combine(dir, name + ".cmd"))
            )
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsDashboardLauncher(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;
        return name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || name.Equals("aspire", StringComparison.OrdinalIgnoreCase);
    }

    public static int StopRecorded(
        string pidText,
        Func<int, string> processName,
        Action<int> kill,
        Action<string> log
    )
    {
        ArgumentNullException.ThrowIfNull(processName);
        ArgumentNullException.ThrowIfNull(kill);
        ArgumentNullException.ThrowIfNull(log);
        if (!int.TryParse(pidText?.Trim(), out int pid) || pid <= 0)
        {
            log("Aspire dashboard pid file is empty. Nothing to stop.");
            return 0;
        }

        string name;
        try
        {
            name = processName(pid);
        }
        catch (Exception e)
        {
            log($"Could not inspect Aspire dashboard pid {pid}: {e.Message}");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            log($"Aspire dashboard pid {pid} is not running.");
            return 0;
        }

        if (!IsDashboardLauncher(name))
        {
            log($"Pid {pid} is {name}, not the Aspire CLI. Leaving it running.");
            return 1;
        }

        try
        {
            kill(pid);
            log($"Stopped Aspire dashboard pid {pid}.");
            return 0;
        }
        catch (Exception e)
        {
            log($"Could not stop Aspire dashboard pid {pid}: {e.Message}");
            return 1;
        }
    }

    public static int StopRunning()
    {
        try
        {
            if (!File.Exists(PidPath))
            {
                Console.WriteLine(
                    IsPortListening(OtlpGrpcPort)
                        ? $"No pid file. {OtlpGrpcEndpoint} is listening, but this CLI did not start it."
                        : "Aspire dashboard is not running."
                );
                return 0;
            }

            string text = File.ReadAllText(PidPath);
            int code = StopRecorded(text, ProcessNameOrNull, KillTree, Console.WriteLine);
            if (code == 0)
            {
                TryDelete(PidPath);
            }

            return code;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not stop the Aspire dashboard. {e.Message}");
            return 1;
        }
    }

    public static int PrintStatus()
    {
        bool otlp = IsPortListening(OtlpGrpcPort);
        bool ui = IsPortListening(UiPort);
        Console.WriteLine($"Dashboard UI: {UiUrl} ({(ui ? "listening" : "not listening")})");
        Console.WriteLine(
            $"OTLP gRPC: {OtlpGrpcEndpoint} ({(otlp ? "listening" : "not listening")})"
        );
        string export = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        Console.WriteLine(
            "CLI export: "
                + (
                    string.IsNullOrWhiteSpace(export)
                        ? OtlpGrpcEndpoint + " (appsettings or code default)"
                        : export
                )
        );
        if (File.Exists(PidPath) && int.TryParse(File.ReadAllText(PidPath).Trim(), out int pid))
        {
            string name = ProcessNameOrNull(pid);
            Console.WriteLine(
                name == null
                    ? $"Recorded pid {pid} is not running."
                    : $"Recorded pid {pid} ({name})."
            );
        }
        else
        {
            Console.WriteLine("Recorded pid: none.");
        }

        AspireCliInvocation invocation = Resolve(
            SearchStarts(),
            ManifestDeclaresAspire,
            IsCommandOnPath("aspire", Environment.GetEnvironmentVariable("PATH")),
            Directory.GetCurrentDirectory()
        );
        Console.WriteLine(
            invocation == null
                ? "Aspire CLI: not found. Run `dotnet tool restore`."
                : $"Aspire CLI: {invocation.FileName} {invocation.Arguments.Split(' ')[0]} ({invocation.WorkingDirectory})"
        );
        return otlp ? 0 : 1;
    }

    public static string PowerShellStartCommand(
        string fileName,
        string arguments,
        string workingDirectory,
        string stdoutLog,
        string stderrLog,
        string pidFile
    )
    {
        string argList = string.Join(
            ",",
            (arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(PsQuote)
        );
        return "$ProgressPreference = 'SilentlyContinue'; $p = Start-Process -FilePath "
            + PsQuote(fileName)
            + " -ArgumentList "
            + argList
            + " -WorkingDirectory "
            + PsQuote(workingDirectory)
            + " -WindowStyle Hidden -RedirectStandardOutput "
            + PsQuote(stdoutLog)
            + " -RedirectStandardError "
            + PsQuote(stderrLog)
            + " -PassThru; Set-Content -LiteralPath "
            + PsQuote(pidFile)
            + " -Value $p.Id -NoNewline";
    }

    public static AspireProcessStart Launch(AspireCliInvocation invocation)
    {
        if (invocation == null || string.IsNullOrWhiteSpace(invocation.FileName))
        {
            return new AspireProcessStart(null, "missing Aspire CLI launch", null);
        }

        try
        {
            Directory.CreateDirectory(LogDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(PidPath));
            TryDelete(StdoutLogPath);
            TryDelete(StderrLogPath);
            TryDelete(PidPath);
            string script = PowerShellStartCommand(
                invocation.FileName,
                invocation.Arguments,
                invocation.WorkingDirectory,
                StdoutLogPath,
                StderrLogPath,
                PidPath
            );
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using Process process = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            );
            if (process == null)
            {
                return new AspireProcessStart(null, "powershell did not start", StderrLogPath);
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => AppendLine(stdout, e.Data);
            process.ErrorDataReceived += (_, e) => AppendLine(stderr, e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception) { }

                return new AspireProcessStart(
                    null,
                    "powershell timed out starting the Aspire CLI",
                    StderrLogPath
                );
            }

            process.WaitForExit();
            int? pid = File.Exists(PidPath)
                ? Commands.Services.ServicesCommand.ParseProcessId(File.ReadAllText(PidPath))
                : null;
            if (process.ExitCode != 0 || pid == null)
            {
                string error = stderr.Length > 0 ? stderr.ToString() : stdout.ToString();
                return new AspireProcessStart(null, TrimError(error), StderrLogPath);
            }

            return new AspireProcessStart(pid, null, StderrLogPath);
        }
        catch (Exception e)
        {
            return new AspireProcessStart(null, e.Message, StderrLogPath);
        }
    }

    public static bool IsPortListening(int port)
    {
        try
        {
            using var client = new TcpClient();
            Task connect = client.ConnectAsync(IPAddress.Loopback, port);
            if (!connect.Wait(TimeSpan.FromSeconds(2)))
            {
                return false;
            }

            return client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string> SearchStarts()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
        string processDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(processDir))
        {
            yield return processDir;
        }
    }

    private static void Log(string message)
    {
        if (
            message.Contains("was not started", StringComparison.Ordinal)
            || message.Contains("not listening", StringComparison.Ordinal)
            || message.Contains("exited before", StringComparison.Ordinal)
            || message.Contains("Continuing without", StringComparison.Ordinal)
        )
        {
            Console.Error.WriteLine(message);
            return;
        }

        Console.WriteLine(message);
    }

    private static bool ProcessAlive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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

    private static void KillTree(int pid)
    {
        using Process process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }

    private static void AppendLine(StringBuilder builder, string line)
    {
        if (line == null)
        {
            return;
        }

        lock (builder)
        {
            builder.AppendLine(line);
        }
    }

    private static string Tail(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            string[] lines = File.ReadAllText(path)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return null;
            }

            int take = Math.Min(8, lines.Length);
            string text = string.Join(" ", lines[^take..]);
            return text.Length <= 500 ? text : text[^500..];
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string TrimError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Aspire CLI exited without a pid";
        }

        string trimmed = error.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[^500..];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception) { }
    }

    private static string PsQuote(string value) => "'" + (value ?? "").Replace("'", "''") + "'";
}
