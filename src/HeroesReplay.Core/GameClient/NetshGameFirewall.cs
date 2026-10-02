using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.GameClient;

public sealed class NetshGameFirewall : IGameFirewall
{
    private readonly ILogger<NetshGameFirewall> logger;

    public NetshGameFirewall(ILogger<NetshGameFirewall> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void AllowInboundClients(IReadOnlyList<string> exePaths)
    {
        foreach (HeroesFirewallRule rule in HeroesFirewallConsent.ForClients(exePaths))
        {
            try
            {
                AllowOne(rule);
            }
            catch (Win32Exception e)
            {
                LogFailure(rule, e);
            }
            catch (InvalidOperationException e)
            {
                LogFailure(rule, e);
            }
        }
    }

    private void AllowOne(HeroesFirewallRule rule)
    {
        if (AlreadyAllows(rule))
        {
            logger.LogDebug("Inbound access for {Program} is already allowed.", rule.ProgramPath);
            return;
        }

        int code = Run(AddArguments(rule), capture: false).Code;
        if (code != 0)
        {
            throw new InvalidOperationException("netsh could not add the allow rule. Exit " + code);
        }

        logger.LogInformation(
            "Allowed inbound network access for {Program}. Windows Firewall will not ask for this Heroes client.",
            rule.ProgramPath
        );
    }

    private static bool AlreadyAllows(HeroesFirewallRule rule)
    {
        NetshResult shown = Run(ShowArguments(rule), capture: true);
        return shown.Code == 0
            && shown.Text.IndexOf(rule.ProgramPath, StringComparison.OrdinalIgnoreCase) >= 0
            && shown.Text.IndexOf("Allow", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void LogFailure(HeroesFirewallRule rule, Exception e)
    {
        logger.LogWarning(
            e,
            "Could not allow inbound access for {Program}. Windows may ask before Heroes can use the network.",
            rule.ProgramPath
        );
    }

    internal static string ShowArguments(HeroesFirewallRule rule)
    {
        return "advfirewall firewall show rule name=" + Quote(rule.Name);
    }

    internal static string AddArguments(HeroesFirewallRule rule)
    {
        return "advfirewall firewall add rule name="
            + Quote(rule.Name)
            + " dir=in action=allow program="
            + Quote(rule.ProgramPath)
            + " enable=yes profile=any";
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "", StringComparison.Ordinal) + "\"";
    }

    private static NetshResult Run(string arguments, bool capture)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("netsh did not start.");
        }

        Task<string> stdout = capture
            ? process.StandardOutput.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        Task<string> stderr = capture
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);
        if (!process.WaitForExit(15000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }

            throw new InvalidOperationException("netsh did not finish.");
        }

        string text = stdout.GetAwaiter().GetResult();
        stderr.GetAwaiter().GetResult();
        return new NetshResult(process.ExitCode, text ?? string.Empty);
    }

    private readonly record struct NetshResult(int Code, string Text);
}
