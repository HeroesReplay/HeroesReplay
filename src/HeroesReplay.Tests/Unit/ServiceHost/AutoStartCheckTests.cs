using System;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Tests.Unit.Support;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class AutoStartCheckTests
{
    private const string Exe = @"C:\heroesreplay\app\heroesreplay.exe";

    private static readonly AutoStartFacts Ready = new()
    {
        CurrentUser = @"DESKTOP-8SJEK72\admin",
        Executable = Exe,
        TaskCommand =
            "cmd.exe /d /c \"set HEROES_REPLAY_ENV=prod&& \"" + Exe + "\" services start --supervise\"",
        AutoAdminLogon = true,
        AutoLogonUser = "admin",
    };

    [Fact]
    public void TaskAndAutoLogonForThisUser_IsReady()
    {
        AutoStartReport report = AutoStartCheck.Evaluate(Ready, ServiceLogonTask.DefaultName);

        Assert.True(report.Ready);
        Assert.Contains("comes back after a restart", report.Detail);
    }

    [Fact]
    public void NoTask_SaysToRunInstallTask()
    {
        AutoStartReport report = AutoStartCheck.Evaluate(
            Ready with { TaskCommand = null },
            ServiceLogonTask.DefaultName
        );

        Assert.Equal(AutoStartCheck.NoTask, report.Reason);
        Assert.Contains("services install-task", report.Detail);
    }

    [Fact]
    public void TaskForAnotherInstall_IsNamed()
    {
        AutoStartReport report = AutoStartCheck.Evaluate(
            Ready with
            {
                TaskCommand = @"C:\heroesreplay\HeroesReplay\src\bin\heroesreplay.exe services start --supervise",
            },
            ServiceLogonTask.DefaultName
        );

        Assert.Equal(AutoStartCheck.TaskOtherInstall, report.Reason);
    }

    [Fact]
    public void AutoLogonOff_PointsAtAutologon()
    {
        AutoStartReport report = AutoStartCheck.Evaluate(
            Ready with { AutoAdminLogon = false },
            ServiceLogonTask.DefaultName
        );

        Assert.Equal(AutoStartCheck.NoAutoLogon, report.Reason);
        Assert.Contains(AutoStartCheck.AutologonUrl, report.Detail);
        Assert.Contains("admin", report.Detail);
    }

    [Fact]
    public void AutoLogonForAnotherUser_IsNotReady()
    {
        AutoStartReport report = AutoStartCheck.Evaluate(
            Ready with { AutoLogonUser = "stream" },
            ServiceLogonTask.DefaultName
        );

        Assert.Equal(AutoStartCheck.AutoLogonOtherUser, report.Reason);
    }

    [Fact]
    public void AutoLogonCount_RunsOut()
    {
        AutoStartReport report = AutoStartCheck.Evaluate(
            Ready with { AutoLogonCount = 2 },
            ServiceLogonTask.DefaultName
        );

        Assert.Equal(AutoStartCheck.AutoLogonCounted, report.Reason);
    }

    [Fact]
    public void TaskCommand_IsReadFromTheTaskDefinition()
    {
        string xml = ServiceLogonTask.Xml(@"PC\admin", Exe, "prod", null);

        string command = AutoStartProbe.ParseTaskCommand(xml);

        Assert.StartsWith("cmd.exe ", command, StringComparison.Ordinal);
        Assert.Contains(Exe, command, StringComparison.Ordinal);
        Assert.Contains("services start --supervise", command, StringComparison.Ordinal);
        Assert.Null(AutoStartProbe.ParseTaskCommand("not xml"));
        Assert.Null(AutoStartProbe.ParseTaskCommand(""));
    }
}
