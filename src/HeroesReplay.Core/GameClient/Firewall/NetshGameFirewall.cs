using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.GameClient.Firewall;

/// <summary>
/// Reading rules needs no elevation. Adding one does, so an unelevated process only reports
/// a missing rule; <c>heroesreplay client firewall</c> from an elevated shell adds it.
/// Any enabled inbound Allow rule for the exe counts, whatever its name, including the
/// <c>Query User{...}</c> rules Windows adds when someone answers its prompt (#284).
/// </summary>
public sealed class NetshGameFirewall : IGameFirewall
{
    /// <summary>
    /// Every inbound rule. Only verbose output names the program, and the rules that allow an
    /// exe may have any name, so the check reads them all once per call.
    /// </summary>
    internal const string ShowInboundArguments =
        "advfirewall firewall show rule name=all dir=in verbose";

    private static readonly string[] EveryProfile = { "Domain", "Private", "Public" };

    private readonly ILogger<NetshGameFirewall> logger;
    private readonly Func<bool> isElevated;
    private readonly Func<string, bool, NetshResult> run;

    // Each exe is reported once per process: a launch checks every installed client.
    private readonly ConcurrentDictionary<string, bool> reported = new(
        StringComparer.OrdinalIgnoreCase
    );

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
        IReadOnlyList<HeroesFirewallRule> rules = HeroesFirewallConsent.ForClients(exePaths);
        if (rules.Count == 0)
        {
            return outcomes;
        }

        bool elevated = isElevated();
        IReadOnlyList<ShownRule> inbound = null;
        Exception readFailure = null;
        try
        {
            inbound = ReadInboundRules();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            readFailure = e;
        }

        foreach (HeroesFirewallRule rule in rules)
        {
            if (readFailure != null)
            {
                LogFailure(rule, readFailure);
                outcomes.Add(new FirewallRuleOutcome(rule.ProgramPath, FirewallRuleState.Failed));
                continue;
            }

            try
            {
                outcomes.Add(AllowOne(rule, inbound, elevated));
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            {
                LogFailure(rule, e);
                outcomes.Add(new FirewallRuleOutcome(rule.ProgramPath, FirewallRuleState.Failed));
            }
        }

        return outcomes;
    }

    private FirewallRuleOutcome AllowOne(
        HeroesFirewallRule rule,
        IReadOnlyList<ShownRule> inbound,
        bool elevated
    )
    {
        IReadOnlyList<ShownRule> allowing = AllowingRules(inbound, rule.ProgramPath);
        int copies = allowing.Count(shown =>
            string.Equals(shown.Name, rule.Name, StringComparison.OrdinalIgnoreCase)
        );
        if (elevated && copies > 1)
        {
            // Older builds added a copy on every launch because the check never matched.
            run(DeleteArguments(rule), false);
            logger.LogInformation(
                "Removed {Count} copies of the inbound rule for {Program} before adding one.",
                copies,
                rule.ProgramPath
            );
            return Add(rule);
        }

        if (allowing.Count > 0)
        {
            string allowedBy = Describe(allowing);
            LogAllowed(rule.ProgramPath, allowing, allowedBy);
            return new FirewallRuleOutcome(
                rule.ProgramPath,
                FirewallRuleState.AlreadyAllowed,
                allowedBy
            );
        }

        if (elevated)
        {
            return Add(rule);
        }

        if (FirstReport("missing", rule.ProgramPath))
        {
            logger.LogWarning(
                "No inbound firewall rule for {Program}. Windows may ask to allow it the first time it listens. Run `heroesreplay client firewall` once from an elevated shell to add it.",
                rule.ProgramPath
            );
        }
        else
        {
            logger.LogDebug("Still no inbound firewall rule for {Program}.", rule.ProgramPath);
        }

        return new FirewallRuleOutcome(rule.ProgramPath, FirewallRuleState.MissingNeedsElevation);
    }

    private FirewallRuleOutcome Add(HeroesFirewallRule rule)
    {
        int code = run(AddArguments(rule), false).Code;
        if (code != 0)
        {
            throw new InvalidOperationException("netsh could not add the allow rule. Exit " + code);
        }

        logger.LogInformation(
            "Allowed inbound network access for {Program}. Windows Firewall will not ask for this Heroes client.",
            rule.ProgramPath
        );
        return new FirewallRuleOutcome(rule.ProgramPath, FirewallRuleState.Added);
    }

    private void LogAllowed(string program, IReadOnlyList<ShownRule> allowing, string allowedBy)
    {
        string[] covered = CoveredProfiles(allowing);
        LogLevel level = FirstReport("allowed", program) ? LogLevel.Information : LogLevel.Debug;
        if (covered.Length == EveryProfile.Length)
        {
            logger.Log(
                level,
                "Inbound access for {Program} is already allowed by {Rules}.",
                program,
                allowedBy
            );
            return;
        }

        // Still allowed: Windows asks only on a network profile no rule covers.
        logger.Log(
            level,
            "Inbound access for {Program} is allowed by {Rules}, which cover only these profiles: {Profiles}.",
            program,
            allowedBy,
            covered.Length == 0 ? "none" : string.Join(",", covered)
        );
    }

    private bool FirstReport(string kind, string program)
    {
        return reported.TryAdd(kind + "|" + NormalizeProgramPath(program), true);
    }

    private IReadOnlyList<ShownRule> ReadInboundRules()
    {
        NetshResult shown = run(ShowInboundArguments, true);

        // netsh exits 1 with "No rules match the specified criteria." when there are none.
        return shown.Code == 0 ? ParseRules(shown.Text) : Array.Empty<ShownRule>();
    }

    /// <summary>
    /// The enabled inbound Allow rules for this program, whatever their name. Paths are compared
    /// case-insensitively after environment variables are expanded and the path is made full:
    /// Windows writes its own rules in lower case.
    /// </summary>
    internal static IReadOnlyList<ShownRule> AllowingRules(
        IEnumerable<ShownRule> rules,
        string programPath
    )
    {
        var allowing = new List<ShownRule>();
        string program = NormalizeProgramPath(programPath);
        if (program.Length == 0 || rules == null)
        {
            return allowing;
        }

        foreach (ShownRule rule in rules)
        {
            if (
                rule.AllowsInbound
                && string.Equals(
                    NormalizeProgramPath(rule.Program),
                    program,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                allowing.Add(rule);
            }
        }

        return allowing;
    }

    internal static string NormalizeProgramPath(string path)
    {
        string trimmed = (path ?? string.Empty).Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        string expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (!Path.IsPathFullyQualified(expanded))
        {
            // "System" or "Any": never an exe path.
            return expanded;
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            return expanded;
        }
    }

    /// <summary>
    /// Rules from <c>netsh advfirewall firewall show rule ... verbose</c>. Each rule starts with
    /// <c>Rule Name:</c> and is one <c>Key: value</c> line per field. netsh can wrap a long
    /// program path onto the next line, so a line that is not a field continues the path.
    /// </summary>
    internal static IReadOnlyList<ShownRule> ParseRules(string verboseText)
    {
        var rules = new List<ShownRule>();
        Dictionary<string, string> fields = null;
        string key = null;
        foreach (string raw in (verboseText ?? string.Empty).Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.All(c => c == '-'))
            {
                key = null;
                continue;
            }

            if (TryField(line, out string name, out string value))
            {
                if (string.Equals(name, "Rule Name", StringComparison.OrdinalIgnoreCase))
                {
                    AddRule(rules, fields);
                    fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                key = name;
                if (fields != null)
                {
                    fields[name] = value;
                }

                continue;
            }

            // Only the program path is read from a wrapped line. "Ok." ends the output, and
            // ICMP rules list their types under the protocol.
            if (fields != null && string.Equals(key, "Program", StringComparison.OrdinalIgnoreCase))
            {
                fields[key] += line.TrimStart();
            }
        }

        AddRule(rules, fields);
        return rules;
    }

    private static bool TryField(string line, out string name, out string value)
    {
        name = null;
        value = null;
        int colon = line.IndexOf(':');

        // A field name is words, at least two letters long, so a wrapped "C:\..." is not one.
        if (colon < 2 || !char.IsLetter(line[0]))
        {
            return false;
        }

        for (int i = 0; i < colon; i++)
        {
            if (!char.IsLetter(line[i]) && line[i] != ' ')
            {
                return false;
            }
        }

        name = line[..colon].Trim();

        // Keep a trailing space: it can be the space before the wrapped part of a path.
        value = line[(colon + 1)..].TrimStart();
        return true;
    }

    private static void AddRule(List<ShownRule> rules, Dictionary<string, string> fields)
    {
        if (fields == null)
        {
            return;
        }

        rules.Add(
            new ShownRule(
                Field(fields, "Rule Name"),
                Field(fields, "Enabled"),
                Field(fields, "Direction"),
                Field(fields, "Profiles"),
                Field(fields, "Action"),
                Field(fields, "Program")
            )
        );
    }

    private static string Field(Dictionary<string, string> fields, string name)
    {
        return fields.TryGetValue(name, out string value) ? value.Trim() : string.Empty;
    }

    private static string Describe(IEnumerable<ShownRule> rules)
    {
        return string.Join(
            ", ",
            rules
                .Select(rule => "\"" + rule.Name + "\" (" + rule.Profiles + ")")
                .Distinct(StringComparer.OrdinalIgnoreCase)
        );
    }

    private static string[] CoveredProfiles(IEnumerable<ShownRule> rules)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ShownRule rule in rules)
        {
            foreach (
                string profile in rule.Profiles.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (string.Equals(profile, "Any", StringComparison.OrdinalIgnoreCase))
                {
                    named.UnionWith(EveryProfile);
                }
                else
                {
                    named.Add(profile);
                }
            }
        }

        return EveryProfile.Where(named.Contains).ToArray();
    }

    private void LogFailure(HeroesFirewallRule rule, Exception e)
    {
        logger.LogWarning(
            e,
            "Could not allow inbound access for {Program}. Windows may ask before Heroes can use the network.",
            rule.ProgramPath
        );
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

    /// <summary>One rule as <c>netsh ... verbose</c> shows it. Values are netsh's English text.</summary>
    internal sealed record ShownRule(
        string Name,
        string Enabled,
        string Direction,
        string Profiles,
        string Action,
        string Program
    )
    {
        public bool AllowsInbound =>
            string.Equals(Enabled, "Yes", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Direction, "In", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Action, "Allow", StringComparison.OrdinalIgnoreCase);
    }
}
