using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using HeroesReplay.CLI;
using Xunit;

namespace HeroesReplay.Tests.Unit.Telemetry;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class AspireDashboardHostTests
{
    [Fact]
    public void Endpoints_MatchTheLocalAspireDashboard()
    {
        Assert.Equal(18888, new Uri(AspireDashboardHost.UiUrl).Port);
        Assert.Equal(4317, new Uri(AspireDashboardHost.OtlpGrpcEndpoint).Port);
        Assert.Equal(4318, new Uri(AspireDashboardHost.OtlpHttpEndpoint).Port);
        Assert.Equal(
            AspireDashboardHost.OtlpGrpcEndpoint,
            HeroesReplayOpenTelemetry.DefaultOtlpEndpoint
        );
        Assert.DoesNotContain("docker", AspireDashboardHost.RunArguments);
        Assert.DoesNotContain("compose", AspireDashboardHost.RunArguments);
        Assert.Contains("--allow-anonymous", AspireDashboardHost.RunArguments);
        Assert.Contains(AspireDashboardHost.UiUrl, AspireDashboardHost.RunArguments);
        Assert.Contains(AspireDashboardHost.OtlpGrpcEndpoint, AspireDashboardHost.RunArguments);
    }

    [Fact]
    public void Ensure_WhenOtlpIsListening_DoesNotStartAProcess()
    {
        int starts = 0;
        DashboardEnsureResult result = AspireDashboardHost.Ensure(
            () => true,
            invocation =>
            {
                starts++;
                return new AspireProcessStart(1, null, null);
            },
            pid => true,
            Array.Empty<string>(),
            path => false,
            aspireOnPath: false,
            pathFallbackDirectory: null,
            log: _ => { },
            wait: _ => { },
            startupBudget: TimeSpan.Zero
        );

        Assert.True(result.Listening);
        Assert.False(result.Launched);
        Assert.Equal(0, starts);
        Assert.Contains("already listening", result.Detail);
    }

    [Fact]
    public void Ensure_StartsTheLocalDotnetToolAgainstTheDashboardEndpoints()
    {
        string root = NewManifestRoot();
        try
        {
            AspireCliInvocation seen = null;
            DashboardEnsureResult result = AspireDashboardHost.Ensure(
                () => seen != null,
                invocation =>
                {
                    seen = invocation;
                    return new AspireProcessStart(42, null, null);
                },
                pid => true,
                new[] { Path.Combine(root, "src", "bin") },
                AspireDashboardHost.ManifestDeclaresAspire,
                aspireOnPath: true,
                pathFallbackDirectory: root,
                log: _ => { },
                wait: _ => throw new InvalidOperationException("should not wait"),
                startupBudget: TimeSpan.FromMinutes(5)
            );

            Assert.True(result.Listening);
            Assert.True(result.Launched);
            Assert.Equal("dotnet", seen.FileName);
            Assert.Equal(root, seen.WorkingDirectory);
            Assert.StartsWith("aspire dashboard run", seen.Arguments);
            Assert.Contains("--allow-anonymous", seen.Arguments);
            Assert.Contains("http://127.0.0.1:18888", seen.Arguments);
            Assert.Contains("http://127.0.0.1:4317", seen.Arguments);
            Assert.Contains("http://127.0.0.1:4318", seen.Arguments);
            Assert.DoesNotContain("docker", seen.Arguments);
            Assert.Contains("logs, metrics, and traces", result.Detail);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Ensure_FallsBackToAspireOnPath()
    {
        AspireCliInvocation seen = null;
        AspireDashboardHost.Ensure(
            () => seen != null,
            invocation =>
            {
                seen = invocation;
                return new AspireProcessStart(9, null, null);
            },
            pid => true,
            new[] { Path.GetTempPath() },
            path => false,
            aspireOnPath: true,
            pathFallbackDirectory: @"C:\aspire-cli",
            log: _ => { },
            wait: _ => { },
            startupBudget: TimeSpan.Zero
        );

        Assert.Equal("aspire", seen.FileName);
        Assert.Equal(@"C:\aspire-cli", seen.WorkingDirectory);
        Assert.StartsWith("dashboard run", seen.Arguments);
        Assert.DoesNotContain("docker", seen.Arguments);
    }

    [Fact]
    public void Ensure_MissingCli_LogsAndDoesNotThrow()
    {
        var lines = new List<string>();
        DashboardEnsureResult result = AspireDashboardHost.Ensure(
            () => false,
            invocation => throw new InvalidOperationException("should not start"),
            pid => true,
            Array.Empty<string>(),
            path => false,
            aspireOnPath: false,
            pathFallbackDirectory: null,
            lines.Add,
            wait: _ => { },
            startupBudget: TimeSpan.Zero
        );

        Assert.False(result.Listening);
        Assert.False(result.Launched);
        Assert.Contains(lines, line => line.Contains("was not started"));
        Assert.Contains(lines, line => line.Contains("dotnet tool restore"));
        Assert.Contains(lines, line => line.Contains("Continuing without the dashboard"));
    }

    [Fact]
    public void Ensure_StartFailure_ContinuesWithoutTheDashboard()
    {
        var lines = new List<string>();
        DashboardEnsureResult result = AspireDashboardHost.Ensure(
            () => false,
            invocation => new AspireProcessStart(null, "restore failed", null),
            pid => true,
            Array.Empty<string>(),
            path => false,
            aspireOnPath: true,
            pathFallbackDirectory: @"C:\repo",
            lines.Add,
            wait: _ => { },
            startupBudget: TimeSpan.Zero
        );

        Assert.False(result.Listening);
        Assert.False(result.Launched);
        Assert.Contains(lines, line => line.Contains("restore failed"));
        Assert.Contains(lines, line => line.Contains("Continuing without the dashboard"));
    }

    [Fact]
    public void Ensure_ProcessExitsBeforePortOpens_IncludesLogTail()
    {
        string log = Path.Combine(Path.GetTempPath(), $"aspire-dashboard-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(
                log,
                "Run 'dotnet tool restore' to make the aspire command available."
            );
            DashboardEnsureResult result = AspireDashboardHost.Ensure(
                () => false,
                invocation => new AspireProcessStart(7, null, log),
                pid => false,
                Array.Empty<string>(),
                path => false,
                aspireOnPath: true,
                pathFallbackDirectory: @"C:\repo",
                log: _ => { },
                wait: _ => throw new InvalidOperationException("should not wait"),
                startupBudget: TimeSpan.FromMinutes(5)
            );

            Assert.False(result.Listening);
            Assert.True(result.Launched);
            Assert.Contains("exited before", result.Detail);
            Assert.Contains("dotnet tool restore", result.Detail);
            Assert.Contains("Continuing without the dashboard", result.Detail);
        }
        finally
        {
            File.Delete(log);
        }
    }

    [Fact]
    public void ApplyExporterEnvironment_SetsGrpcEndpointWithoutOverwriting()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        AspireDashboardHost.ApplyExporterEnvironment(
            name => values.TryGetValue(name, out string value) ? value : null,
            (name, value) => values[name] = value
        );

        Assert.Equal(AspireDashboardHost.OtlpGrpcEndpoint, values["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("grpc", values["OTEL_EXPORTER_OTLP_PROTOCOL"]);

        values["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://10.0.0.8:4317";
        AspireDashboardHost.ApplyExporterEnvironment(
            name => values.TryGetValue(name, out string value) ? value : null,
            (name, value) => values[name] = value
        );
        Assert.Equal("http://10.0.0.8:4317", values["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }

    [Fact]
    public void PowerShellStartCommand_HidesTheDashboardAndDoesNotUseDocker()
    {
        string script = AspireDashboardHost.PowerShellStartCommand(
            "dotnet",
            "aspire " + AspireDashboardHost.RunArguments,
            @"C:\heroesreplay\worktrees\develop",
            @"C:\logs\aspire-dashboard.log",
            @"C:\logs\aspire-dashboard.err.log",
            @"C:\logs\aspire-dashboard.pid"
        );

        Assert.Contains("-WindowStyle Hidden", script);
        Assert.Contains("-FilePath 'dotnet'", script);
        Assert.Contains("'aspire'", script);
        Assert.Contains("'dashboard'", script);
        Assert.Contains("'--allow-anonymous'", script);
        Assert.Contains("'http://127.0.0.1:4317'", script);
        Assert.Contains(@"-WorkingDirectory 'C:\heroesreplay\worktrees\develop'", script);
        Assert.DoesNotContain("docker", script);
        Assert.DoesNotContain("compose", script);
        Assert.DoesNotContain("-WindowStyle Normal", script);
    }

    [Fact]
    public void IsCommandOnPath_FindsAspireExe()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"aspire-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "aspire.exe");
        try
        {
            File.WriteAllText(exe, "");
            Assert.True(AspireDashboardHost.IsCommandOnPath("aspire", dir + ";C:\\Windows"));
            Assert.False(AspireDashboardHost.IsCommandOnPath("missing-tool", dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("dotnet", true)]
    [InlineData("dotnet.exe", true)]
    [InlineData("aspire", true)]
    [InlineData("Aspire.EXE", true)]
    [InlineData("docker", false)]
    [InlineData("heroesreplay", false)]
    [InlineData(null, false)]
    public void IsDashboardLauncher_OnlyMatchesTheCli(string name, bool expected)
    {
        Assert.Equal(expected, AspireDashboardHost.IsDashboardLauncher(name));
    }

    [Fact]
    public void StopRecorded_KillsOnlyTheAspireLauncher()
    {
        var killed = new List<int>();
        var lines = new List<string>();
        int code = AspireDashboardHost.StopRecorded("15", pid => "dotnet", killed.Add, lines.Add);

        Assert.Equal(0, code);
        Assert.Equal(new[] { 15 }, killed);
        Assert.Contains(lines, line => line.Contains("Stopped Aspire dashboard pid 15"));

        killed.Clear();
        int left = AspireDashboardHost.StopRecorded("15", pid => "notepad", killed.Add, lines.Add);
        Assert.Equal(1, left);
        Assert.Empty(killed);

        int empty = AspireDashboardHost.StopRecorded("", pid => "dotnet", killed.Add, lines.Add);
        Assert.Equal(0, empty);
        int gone = AspireDashboardHost.StopRecorded("15", pid => null, killed.Add, lines.Add);
        Assert.Equal(0, gone);
    }

    [Fact]
    public void IsPortListening_SeesALoopbackListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(AspireDashboardHost.IsPortListening(port));
        }
        finally
        {
            listener.Stop();
        }

        Assert.False(AspireDashboardHost.IsPortListening(port));
    }

    [Fact]
    public void Repo_UsesTheAspireToolAndNotCompose()
    {
        string root = AspireDashboardHost.FindToolDirectory(
            AppContext.BaseDirectory,
            AspireDashboardHost.ManifestDeclaresAspire
        );

        Assert.False(string.IsNullOrWhiteSpace(root));
        string manifest = File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json"));
        Assert.Contains("\"aspire.cli\"", manifest);
        Assert.Contains("\"aspire\"", manifest);
        Assert.False(File.Exists(Path.Combine(root, "deploy", "aspire", "docker-compose.yml")));
    }

    private static string NewManifestRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aspire-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".config"));
        Directory.CreateDirectory(Path.Combine(root, "src", "bin"));
        File.WriteAllText(
            Path.Combine(root, ".config", "dotnet-tools.json"),
            """
            { "tools": { "aspire.cli": { "commands": [ "aspire" ] } } }
            """
        );
        return root;
    }
}
