using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// Reading rules needs no elevation. Adding one does, so an unelevated process only reports
/// a missing rule; <c>heroesreplay client firewall</c> from an elevated shell adds it.
/// </summary>
public sealed class NetshGameFirewall : IGameFirewall
{
    private readonly ILogger<NetshGameFirewall> logger;
    private readonly Func<bool> isElevated;
    private readonly Func<string, bool, NetshResult> run;

    public NetshGameFirewall(ILogger<NetshGameFirewall> logger)
        : this(logger, MediumIntegrityProcess.IsCurrentProcessElevated, Run) { }

    internal NetshGameFirewall(
        ILogger<NetshGameFirewall> logger,
        Func<bool> isElevated,
        Func<string, bool, NetshResult> run
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.isElevated = isElevated ?? throw new ArgumentNullException(nameof(isElevated));
        this.run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public IReadOnlyList<FirewallRuleOutcome> AllowInboundClients(IReadOnlyList<string> exePaths)
    {
        var outcomes = new List<FirewallRuleOutcome>();
        bool elevated = isElevated();
        foreach (HeroesFirewallRule rule in HeroesFirewallConsent.ForClients(exePaths))
        {
            FirewallRuleState state;
            try
            {
                state = AllowOne(rule, elevated);
            }
            catch (Win32Exception e)
            {
                LogFailure(rule, e);
                state = FirewallRuleState.Failed;
            }
            catch (InvalidOperationException e)
            {
                LogFailure(rule, e);
                state = FirewallRuleState.Failed;
            }

            outcomes.Add(new FirewallRuleOutcome(rule.ProgramPath, state));
        }

        return outcomes;
    }

    private FirewallRuleState AllowOne(HeroesFirewallRule rule, bool elevated)
    {
        int existing = CountAllowRules(rule);
        if (existing == 1 || (existing > 1 && !elevated))
        {
            logger.LogDebug("Inbound access for {Program} is already allowed.", rule.ProgramPath);
            return FirewallRuleState.AlreadyAllowed;
        }

        if (existing > 1)
        {
            // Older builds added a copy on every launch because the check never matched.
            run(DeleteArguments(rule), false);
            logger.LogInformation(
                "Removed {Count} copies of the inbound rule for {Program} before adding one.",
                existing,
                rule.ProgramPath
            );
        }

        if (!elevated)
        {
            logger.LogWarning(
                "No inbound firewall rule for {Program}. Windows may ask to allow it the first time it listens. Run `heroesreplay client firewall` once from an elevated shell to add it.",
                rule.ProgramPath
            );
            return FirewallRuleState.MissingNeedsElevation;
        }

        int code = run(AddArguments(rule), false).Code;
        if (code != 0)
        {
            throw new InvalidOperationException("netsh could not add the allow rule. Exit " + code);
        }

        logger.LogInformation(
            "Allowed inbound network access for {Program}. Windows Firewall will not ask for this Heroes client.",
            rule.ProgramPath
        );
        return FirewallRuleState.Added;
    }

    /// <summary>
    /// Allow rules with this name for this program. Only verbose output names the program, and
    /// netsh wraps a long path onto the next line, so line breaks are removed before matching.
    /// </summary>
    private int CountAllowRules(HeroesFirewallRule rule)
    {
        NetshResult shown = run(ShowArguments(rule), true);
        if (shown.Code != 0)
        {
            return 0;
        }

        return CountAllowRules(shown.Text, rule.ProgramPath);
    }

    internal static int CountAllowRules(string verboseText, string programPath)
    {
        string flat = (verboseText ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
        if (flat.IndexOf(programPath, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return 0;
        }

        int count = 0;
        int at = 0;
        while ((at = flat.IndexOf("Action:", at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            at += "Action:".Length;
            if (flat.AsSpan(at).TrimStart().StartsWith("Allow", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
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
        return "advfirewall firewall show rule name=" + Quote(rule.Name) + " verbose";
    }

    internal static string DeleteArguments(HeroesFirewallRule rule)
    {
        return "advfirewall firewall delete rule name=" + Quote(rule.Name);
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

    internal readonly record struct NetshResult(int Code, string Text);
}
