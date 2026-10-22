using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.ServiceHost.Logs;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ServiceStatePrunersTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const int LivePid = 4321;
    private const int GonePid = 9876;

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-service-state-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose() => TestTemp.Delete(root);

    private string Logs => Path.Combine(root, "logs");
    private string Ready => Path.Combine(root, "ready");

    /// <summary>
    /// The stream PC on 2026-10-22: download and youtube were started by hand, wrote no role log,
    /// and never pruned their files from 2026-10-06. Housekeeping prunes every role's logs,
    /// whether or not the role runs, and the files nothing else retires.
    /// </summary>
    [Fact]
    public void PlanLogs_PrunesEveryRolesOldLogs_AndTheLeftoversNothingElseRetires()
    {
        string oldDownload = Write(Logs, "download-2026-10-06.log", Now.AddDays(-16));
        string recentDownload = Write(Logs, "download-2026-10-19.log", Now.AddDays(-3));
        string oldYouTube = Write(Logs, "youtube-2026-10-06.1.log", Now.AddDays(-16));
        string onlyOld = Write(Logs, "twitch-2026-09-30.log", Now.AddDays(-22));
        var spectate = new List<string>();
        for (int day = 0; day < 4; day++)
        {
            spectate.Add(Write(Logs, $"spectate-2026-10-{22 - day:D2}.log", Now.AddDays(-day)));
        }

        string console = Write(Logs, "services-start-console.log", Now.AddDays(-20));
        string freshConsole = Write(Logs, "services-start-console.1.log", Now.AddHours(-1));
        string legacy = Write(Logs, "legacy-2026-09.zip", Now.AddDays(-30));
        string release = Write(Logs, "apply-release.20261001-120000.log", Now.AddDays(-21));
        string dashboard = Write(Logs, "aspire-dashboard.err.log", Now.AddDays(-30));
        string deadPid = Write(
            Logs,
            "heroesprofile-download.pid",
            Now.AddDays(-2),
            GonePid.ToString()
        );
        string livePid = Write(Logs, "youtube-uploader.pid", Now.AddDays(-2), LivePid.ToString());
        string launching = Write(
            Logs,
            "spectate-heroesprofile.pid",
            Now.AddMinutes(-1),
            GonePid.ToString()
        );
        string garbled = Write(Logs, "twitch-connect.pid", Now.AddDays(-2), "not a pid");
        string rootPid = Write(root, "aspire-dashboard.pid", Now.AddDays(-2), GonePid.ToString());

        PrunePlan plan = ServiceStatePruners.PlanLogs(
            Logs,
            root,
            new ServiceLogSettings { RetainedDays = 14, MaxFilesPerRole = 3 },
            Facts(),
            Now
        );

        Assert.Equal(
            new[]
            {
                oldDownload,
                oldYouTube,
                onlyOld,
                spectate[3],
                console,
                legacy,
                deadPid,
                garbled,
                rootPid,
            }.Order(),
            plan.Items.Select(item => item.Path).Order()
        );
        // No role log outlives RetainedDays, not even a role's only file: twitch has not run since.
        Assert.DoesNotContain(
            plan.Items,
            item =>
                item.Path == recentDownload
                || item.Path == freshConsole
                || item.Path == release
                || item.Path == dashboard
                || item.Path == livePid
                || item.Path == launching
        );
        Assert.Contains("RetainedDays", plan.Items.Single(item => item.Path == oldDownload).Reason);
        Assert.Contains(
            "MaxFilesPerRole",
            plan.Items.Single(item => item.Path == spectate[3]).Reason
        );
    }

    [Fact]
    public void PlanReadyFiles_RemovesOnlyFilesOfProcessesThatAreGone_OrLongSilent()
    {
        Beat("tracked", GonePid, Now.AddDays(-3));
        Beat("crashed", GonePid, Now.AddHours(-2));
        Beat("justexited", GonePid, Now.AddMinutes(-2));
        Beat("running", LivePid, Now.AddSeconds(-10));
        Beat("reused", LivePid, Now.AddDays(-2));
        string garbled = Write(Ready, "garbled.json", Now.AddHours(-1), "{");

        PrunePlan plan = ServiceStatePruners.PlanReadyFiles(
            Ready,
            new ServiceHealthSettings(),
            Facts(tracked: "tracked"),
            Now
        );

        Assert.Equal(
            new[] { "crashed.json", "garbled.json", "reused.json" },
            plan.Items.Select(item => Path.GetFileName(item.Path)).Order()
        );
        Assert.Contains(
            "is gone",
            plan.Items.Single(item => item.Path.EndsWith("crashed.json")).Reason
        );
        Assert.True(File.Exists(garbled));
    }

    private static ServiceStateFacts Facts(params string[] tracked) =>
        new(new HashSet<int> { LivePid }, tracked.ToHashSet(StringComparer.Ordinal));

    private void Beat(string nonce, int pid, DateTimeOffset at)
    {
        ServiceReadyFile.Report(
            new ServiceReadyReport
            {
                Role = "download",
                Nonce = nonce,
                Pid = pid,
                ReadyAt = at,
                HeartbeatAt = at,
                HeartbeatIntervalSeconds = 15,
            },
            Ready
        );
    }

    private static string Write(
        string folder,
        string name,
        DateTimeOffset written,
        string text = "x"
    )
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
