using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.GameClient;

public sealed class StormClientConfigurator
{
    private readonly AppSettings settings;
    private readonly string gameFolder;

    public StormClientConfigurator(AppSettings settings)
        : this(settings, AppSettings.UserGameFolderPath) { }

    /// <summary>Tests point <paramref name="gameFolder"/> at a temp folder.</summary>
    internal StormClientConfigurator(AppSettings settings, string gameFolder)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.gameFolder = gameFolder ?? throw new ArgumentNullException(nameof(gameFolder));
    }

    private string InterfacesFolder => Path.Combine(gameFolder, "Interfaces");

    /// <summary>
    /// Installs the interface and writes the preset into the root and every account
    /// Variables.txt. <paramref name="blind"/> installs the blind copy of AhliObs (hero names, no
    /// chat) under the AhliObs file name for a Blind Rating replay (#411); otherwise AhliObs
    /// itself. Heroes must be closed: it holds the installed file open.
    /// </summary>
    public ClientConfigureResult Configure(bool blind = false)
    {
        ClientSettings client =
            settings.Client
            ?? throw new InvalidOperationException("Client settings are not bound.");

        Directory.CreateDirectory(gameFolder);
        Directory.CreateDirectory(InterfacesFolder);

        string interfaceSource = InterfaceSourceFor(client, blind, out bool blindHud);
        string interfaceDest = Path.Combine(InterfacesFolder, client.InterfaceFileName);
        bool copied = false;
        if (interfaceSource != null)
        {
            File.Copy(interfaceSource, interfaceDest, overwrite: true);
            copied = true;
        }

        string variablesPath = Path.Combine(gameFolder, "Variables.txt");
        ApplyVariables(variablesPath, client.VariablesPreset);
        foreach (string accountVariables in EnumerateAccountVariables(gameFolder))
        {
            ApplyVariables(accountVariables, client.VariablesPreset);
        }

        bool hotSRunning = IsHeroesRunning();
        var warnings = new List<string>();
        if (interfaceSource == null)
        {
            warnings.Add(
                $"Could not find {client.InterfaceFileName}. Place it under obs/interfaces or Assets/Interfaces."
            );
        }

        if (blind && !blindHud)
        {
            warnings.Add(
                $"Could not find {client.BlindInterfaceFileName}. This Blind Rating replay shows player names and chat."
            );
        }

        if (hotSRunning)
        {
            warnings.Add(
                "Heroes of the Storm is running. Restart the client so windowed 1080p, background audio, and AhliObs take effect."
            );
        }

        return new ClientConfigureResult(
            variablesPath,
            interfaceDest,
            interfaceSource,
            copied,
            hotSRunning,
            warnings
        );
    }

    public ReplayInterfaceValues ReadReplayInterfaces()
    {
        string root = ReadReplayInterface(Path.Combine(gameFolder, "Variables.txt"));
        string account = null;
        foreach (string accountVariables in EnumerateAccountVariables(gameFolder))
        {
            string value = ReadReplayInterface(accountVariables);
            if (!string.IsNullOrEmpty(value))
            {
                account = value;
                break;
            }
        }

        return new ReplayInterfaceValues(root, account);
    }

    /// <summary>
    /// True when the installed interface holds the archive this replay needs: the blind copy for
    /// a Blind Rating replay, else AhliObs. It reads the file while Heroes holds it open, and the
    /// client keeps the archive it opened, so this is what a running client shows. A file that
    /// cannot be read, or no archive in the build to compare with, counts as a match.
    /// </summary>
    public bool InstalledInterfaceIs(bool blind)
    {
        ClientSettings client = settings.Client ?? new ClientSettings();
        string source = InterfaceSourceFor(client, blind, out _);
        string installed = Path.Combine(InterfacesFolder, client.InterfaceFileName);
        return source == null || !File.Exists(installed) || SameContent(source, installed) != false;
    }

    public ClientStatusResult GetStatus(bool blind = false)
    {
        ClientSettings client =
            settings.Client
            ?? throw new InvalidOperationException("Client settings are not bound.");

        string variablesPath = Path.Combine(gameFolder, "Variables.txt");
        var mismatches = new List<string>();
        CollectMismatches(variablesPath, client.VariablesPreset, mismatches);

        string[] accountFiles = EnumerateAccountVariables(gameFolder);
        if (accountFiles.Length == 0)
        {
            mismatches.Add(
                "No Accounts\\*\\Variables.txt yet; log into Battle.net once so HotS creates the account file."
            );
        }
        else
        {
            foreach (string accountVariables in accountFiles)
            {
                CollectMismatches(accountVariables, client.VariablesPreset, mismatches);
            }
        }

        string interfaceDest = Path.Combine(InterfacesFolder, client.InterfaceFileName);
        bool interfaceInstalled = File.Exists(interfaceDest);
        if (!interfaceInstalled)
        {
            mismatches.Add($"interface file missing: {interfaceDest}");
        }
        else
        {
            // After a Blind Rating replay the blind copy is still installed, and the next replay
            // must get AhliObs back (#411).
            string source = InterfaceSourceFor(client, blind, out _);
            if (source != null && SameContent(source, interfaceDest) == false)
            {
                mismatches.Add($"interface file differs from {source}: {interfaceDest}");
            }
        }

        return new ClientStatusResult(
            variablesPath,
            interfaceInstalled,
            mismatches.Count == 0,
            mismatches,
            IsHeroesRunning()
        );
    }

    /// <summary>
    /// The archive installed as <see cref="ClientSettings.InterfaceFileName"/>: the blind copy for
    /// a Blind Rating replay when the build carries it, else AhliObs. Null when neither is found.
    /// </summary>
    private static string InterfaceSourceFor(ClientSettings client, bool blind, out bool blindHud)
    {
        string blindSource =
            blind && !string.IsNullOrWhiteSpace(client.BlindInterfaceFileName)
                ? FindInterfaceFile(client.BlindInterfaceFileName)
                : null;
        blindHud = blindSource != null;
        return blindSource ?? FindInterfaceFile(client.InterfaceFileName);
    }

    /// <summary>Null when either file cannot be read.</summary>
    private static bool? SameContent(string left, string right)
    {
        try
        {
            using FileStream a = new(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream b = new(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (a.Length != b.Length)
            {
                return false;
            }

            return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void ApplyVariables(string path, IReadOnlyDictionary<string, string> updates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        File.WriteAllText(path, StormVariablesEditor.Apply(existing, updates));
    }

    private static void CollectMismatches(
        string path,
        IReadOnlyDictionary<string, string> expected,
        List<string> mismatches
    )
    {
        Dictionary<string, string> actual = File.Exists(path)
            ? StormVariablesEditor.Parse(File.ReadAllText(path))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, string> pair in expected)
        {
            if (
                !actual.TryGetValue(pair.Key, out string value)
                || !ValuesEqual(pair.Key, pair.Value, value)
            )
            {
                mismatches.Add($"{path}: {pair.Key}={value ?? "(missing)"} (want {pair.Value})");
            }
        }
    }

    private static bool ValuesEqual(string key, string expected, string actual)
    {
        if (
            key.Equals("observerinterface", StringComparison.OrdinalIgnoreCase)
            || key.Equals("replayinterface", StringComparison.OrdinalIgnoreCase)
        )
        {
            return StormVariablesEditor.InterfaceNameEquals(expected, actual);
        }

        if (
            key.Equals("windowwidth", StringComparison.OrdinalIgnoreCase)
            || key.Equals("windowheight", StringComparison.OrdinalIgnoreCase)
        )
        {
            return StormVariablesEditor.WindowEdgeEquals(expected, actual);
        }

        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadReplayInterface(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        Dictionary<string, string> values = StormVariablesEditor.Parse(File.ReadAllText(path));
        values.TryGetValue("replayinterface", out string value);
        return value;
    }

    private static string[] EnumerateAccountVariables(string gameFolder)
    {
        string accounts = Path.Combine(gameFolder, "Accounts");
        if (!Directory.Exists(accounts))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(accounts, "Variables.txt", SearchOption.AllDirectories);
    }

    public static string FindInterfaceFile(string fileName)
    {
        foreach (string candidate in InterfaceCandidates(fileName))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> InterfaceCandidates(string fileName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "Interfaces", fileName);
        yield return Path.Combine(
            Directory.GetCurrentDirectory(),
            "Assets",
            "Interfaces",
            fileName
        );

        DirectoryInfo dir = new(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            yield return Path.Combine(dir.FullName, "obs", "interfaces", fileName);
            dir = dir.Parent;
        }
    }

    private bool IsHeroesRunning()
    {
        string name = settings.Process?.HeroesOfTheStorm ?? "HeroesOfTheStorm_x64";
        Process[] processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}

public readonly record struct ReplayInterfaceValues(string Root, string Account);

public sealed record ClientConfigureResult(
    string VariablesPath,
    string InterfaceDestination,
    string InterfaceSource,
    bool InterfaceCopied,
    bool HotSRunning,
    IReadOnlyList<string> Warnings
);

public sealed record ClientStatusResult(
    string VariablesPath,
    bool InterfaceInstalled,
    bool MatchesPreset,
    IReadOnlyList<string> Mismatches,
    bool HotSRunning
);
