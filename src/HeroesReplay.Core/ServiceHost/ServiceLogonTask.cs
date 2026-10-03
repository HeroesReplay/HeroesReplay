using System;
using System.IO;
using System.Security;
using System.Text;

namespace HeroesReplay.Core.ServiceHost;

/// <summary>
/// The Windows scheduled task that starts the supervised stack when this user logs on
/// (<c>services install-task</c>). <c>apply-release.ps1</c> restarts the stack through a task of
/// this name when it exists, so a release update keeps the stack supervised. The task runs as
/// the user who installed it, only while that user is logged on (interactive, not elevated), so
/// the role consoles and the supervisor console open on that desktop. Registering it needs no
/// administrator rights.
/// </summary>
public static class ServiceLogonTask
{
    public const string DefaultName = "HeroesReplay-live";

    /// <summary>The logon trigger waits this long, so OBS and the network are up first.</summary>
    public const string LogonDelay = "PT30S";

    /// <summary>The command line the task runs, after <c>HEROES_REPLAY_ENV</c> when one is set.</summary>
    public static string Arguments(string roles) =>
        "services start --supervise"
        + (string.IsNullOrWhiteSpace(roles) ? string.Empty : " --roles " + roles.Trim());

    /// <summary>The task definition for <c>schtasks /Create /XML</c> (saved as UTF-16).</summary>
    public static string Xml(string userId, string executable, string environment, string roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        string directory = Path.GetDirectoryName(Path.GetFullPath(executable));
        string command;
        string arguments;
        if (string.IsNullOrWhiteSpace(environment))
        {
            command = executable;
            arguments = Arguments(roles);
        }
        else
        {
            // A task cannot set an environment variable, so cmd sets it for this start only.
            command = "cmd.exe";
            arguments =
                "/d /c \"set HEROES_REPLAY_ENV="
                + environment.Trim()
                + "&& \"" // no space before &&: it would end up in the value
                + executable
                + "\" "
                + Arguments(roles)
                + "\"";
        }

        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        xml.AppendLine(
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
        );
        xml.AppendLine("  <RegistrationInfo>");
        xml.AppendLine(
            "    <Description>Starts the HeroesReplay stack supervised when "
                + Escape(userId)
                + " logs on (heroesreplay services install-task). apply-release.ps1 restarts the stack through it.</Description>"
        );
        xml.AppendLine("  </RegistrationInfo>");
        xml.AppendLine("  <Triggers>");
        xml.AppendLine("    <LogonTrigger>");
        xml.AppendLine("      <Enabled>true</Enabled>");
        xml.AppendLine("      <UserId>" + Escape(userId) + "</UserId>");
        xml.AppendLine("      <Delay>" + LogonDelay + "</Delay>");
        xml.AppendLine("    </LogonTrigger>");
        xml.AppendLine("  </Triggers>");
        xml.AppendLine("  <Principals>");
        xml.AppendLine("    <Principal id=\"Author\">");
        xml.AppendLine("      <UserId>" + Escape(userId) + "</UserId>");
        xml.AppendLine("      <LogonType>InteractiveToken</LogonType>");
        xml.AppendLine("      <RunLevel>LeastPrivilege</RunLevel>");
        xml.AppendLine("    </Principal>");
        xml.AppendLine("  </Principals>");
        xml.AppendLine("  <Settings>");
        // A second start while the stack runs is refused by services start anyway.
        xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
        xml.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        xml.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        xml.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
        xml.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        xml.AppendLine("    <Enabled>true</Enabled>");
        xml.AppendLine("  </Settings>");
        xml.AppendLine("  <Actions Context=\"Author\">");
        xml.AppendLine("    <Exec>");
        xml.AppendLine("      <Command>" + Escape(command) + "</Command>");
        xml.AppendLine("      <Arguments>" + Escape(arguments) + "</Arguments>");
        xml.AppendLine("      <WorkingDirectory>" + Escape(directory) + "</WorkingDirectory>");
        xml.AppendLine("    </Exec>");
        xml.AppendLine("  </Actions>");
        xml.AppendLine("</Task>");
        return xml.ToString();
    }

    private static string Escape(string value) => SecurityElement.Escape(value);
}
