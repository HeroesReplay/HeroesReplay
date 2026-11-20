using System;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// What decides whether the stack comes back by itself after Windows restarts (<c>check
/// autostart</c>): the <see cref="ServiceLogonTask"/> starts it only when its user logs on, so
/// an unattended restart (Windows Update restarts at about 03:30 on Patch Tuesday) also needs
/// Windows to log that user on by itself (Winlogon <c>AutoAdminLogon</c>).
/// </summary>
public sealed record AutoStartFacts
{
    /// <summary>The user this process runs as, <c>DOMAIN\name</c> or <c>name</c>.</summary>
    public string CurrentUser { get; init; }

    /// <summary>This exe, which the task should start.</summary>
    public string Executable { get; init; }

    /// <summary>The task's command and arguments joined, or null when the task does not exist.</summary>
    public string TaskCommand { get; init; }

    /// <summary>Winlogon <c>AutoAdminLogon</c> is <c>1</c>.</summary>
    public bool AutoAdminLogon { get; init; }

    /// <summary>Winlogon <c>DefaultUserName</c>: the user auto-logon signs in.</summary>
    public string AutoLogonUser { get; init; }

    /// <summary>Winlogon <c>AutoLogonCount</c>: auto-logon turns itself off after this many logons. Null when unset.</summary>
    public int? AutoLogonCount { get; init; }
}

/// <summary><see cref="AutoStartCheck.Evaluate"/>: null <see cref="Reason"/> means the stack starts unattended.</summary>
public sealed record AutoStartReport(string Reason, string Detail)
{
    public bool Ready => Reason == null;
}

public static class AutoStartCheck
{
    public const string NoTask = "no_task";
    public const string TaskOtherInstall = "task_other_install";
    public const string NoAutoLogon = "no_auto_logon";
    public const string AutoLogonOtherUser = "auto_logon_other_user";
    public const string AutoLogonCounted = "auto_logon_counted";

    public const string AutologonUrl =
        "https://learn.microsoft.com/sysinternals/downloads/autologon";

    public static AutoStartReport Evaluate(AutoStartFacts facts, string taskName)
    {
        ArgumentNullException.ThrowIfNull(facts);
        string task = string.IsNullOrWhiteSpace(taskName) ? ServiceLogonTask.DefaultName : taskName;
        string user = ShortName(facts.CurrentUser);
        if (string.IsNullOrWhiteSpace(facts.TaskCommand))
        {
            return new AutoStartReport(
                NoTask,
                $"No scheduled task {task}, so nothing starts the stack after Windows restarts. Run `heroesreplay services install-task --environment prod` from the release install as {user}."
            );
        }

        if (
            !string.IsNullOrWhiteSpace(facts.Executable)
            && facts.TaskCommand.IndexOf(facts.Executable, StringComparison.OrdinalIgnoreCase) < 0
        )
        {
            return new AutoStartReport(
                TaskOtherInstall,
                $"Task {task} starts `{facts.TaskCommand}`, not this install ({facts.Executable}). Run `heroesreplay services install-task` from the install that should start after a restart."
            );
        }

        if (!facts.AutoAdminLogon)
        {
            return new AutoStartReport(
                NoAutoLogon,
                $"Task {task} starts the stack when {user} logs on, but Windows does not log on by itself (Winlogon AutoAdminLogon is not 1). After a restart (Windows Update restarts at about 03:30 on Patch Tuesday) the machine waits at the sign-in screen until someone logs on. Turn on auto-logon for {user} once with Sysinternals Autologon ({AutologonUrl}), which keeps the password encrypted in LSA."
            );
        }

        if (
            !string.IsNullOrWhiteSpace(facts.AutoLogonUser)
            && !string.Equals(
                ShortName(facts.AutoLogonUser),
                user,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return new AutoStartReport(
                AutoLogonOtherUser,
                $"Windows logs on {facts.AutoLogonUser} by itself, but task {task} runs as {user}, so the stack does not start after a restart. Run install-task as {facts.AutoLogonUser}, or point auto-logon at {user} (Sysinternals Autologon, {AutologonUrl})."
            );
        }

        if (facts.AutoLogonCount is int count)
        {
            return new AutoStartReport(
                AutoLogonCounted,
                $"Auto-logon is on for {user}, but Winlogon AutoLogonCount is {count}: Windows turns auto-logon off after that many more logons. Delete AutoLogonCount, or set it up again with Sysinternals Autologon ({AutologonUrl})."
            );
        }

        return new AutoStartReport(
            null,
            $"Windows logs on {user} by itself, and task {task} then starts `{facts.TaskCommand}`. The stack comes back after a restart with no one at the machine."
        );
    }

    /// <summary><c>name</c> from <c>DOMAIN\name</c>.</summary>
    public static string ShortName(string user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return "this user";
        }

        int slash = user.LastIndexOf('\\');
        return slash >= 0 ? user[(slash + 1)..] : user;
    }
}
