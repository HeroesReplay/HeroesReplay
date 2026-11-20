using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Xml.Linq;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Win32;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// Reads what <see cref="AutoStartCheck"/> needs from this machine: the logon task's command
/// (<c>schtasks /Query /XML</c>) and the Winlogon auto-logon values. Read-only; it never reads
/// or prints a password.
/// </summary>
public static class AutoStartProbe
{
    private const string Winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

    public static AutoStartReport Check(string taskName = ServiceLogonTask.DefaultName) =>
        AutoStartCheck.Evaluate(Read(taskName), taskName);

    public static AutoStartFacts Read(string taskName)
    {
        string autoAdminLogon = null;
        string user = null;
        int? count = null;
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(Winlogon))
        {
            autoAdminLogon = key?.GetValue("AutoAdminLogon")?.ToString();
            user = key?.GetValue("DefaultUserName")?.ToString();
            object raw = key?.GetValue("AutoLogonCount");
            if (raw != null && int.TryParse(raw.ToString(), out int parsed))
            {
                count = parsed;
            }
        }

        return new AutoStartFacts
        {
            CurrentUser = WindowsIdentity.GetCurrent().Name,
            Executable = Environment.ProcessPath,
            TaskCommand = TaskCommand(taskName),
            AutoAdminLogon = string.Equals(autoAdminLogon?.Trim(), "1", StringComparison.Ordinal),
            AutoLogonUser = user,
            AutoLogonCount = count,
        };
    }

    /// <summary>The task's first Exec command and arguments, or null when the task does not exist.</summary>
    public static string TaskCommand(string taskName)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("/Query");
        start.ArgumentList.Add("/TN");
        start.ArgumentList.Add(taskName);
        start.ArgumentList.Add("/XML");
        try
        {
            using Process process = Process.Start(start);
            var stderr = process.StandardError.ReadToEndAsync();
            string xml = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderr.Result;
            return process.ExitCode == 0 ? ParseTaskCommand(xml) : null;
        }
        catch (Exception e)
            when (e
                    is IOException
                        or InvalidOperationException
                        or System.ComponentModel.Win32Exception
            )
        {
            return null;
        }
    }

    /// <summary>The first Exec's command and arguments from a task definition, or null.</summary>
    public static string ParseTaskCommand(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        try
        {
            XElement exec = XDocument
                .Parse(xml)
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Exec");
            string command = exec
                ?.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Command")
                ?.Value;
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            string arguments = exec.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "Arguments")
                ?.Value;
            return string.IsNullOrWhiteSpace(arguments) ? command : command + " " + arguments;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
