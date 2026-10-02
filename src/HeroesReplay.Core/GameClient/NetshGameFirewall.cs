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
        if (AlreadyAllows(rule))
        {
            logger.LogDebug("Inbound access for {Program} is already allowed.", rule.ProgramPath);
            return FirewallRuleState.AlreadyAllowed;
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

    private bool AlreadyAllows(HeroesFirewallRule rule)
    {
        NetshResult shown = run(ShowArguments(rule), true);
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

    internal readonly record struct NetshResult(int Code, string Text);
}
