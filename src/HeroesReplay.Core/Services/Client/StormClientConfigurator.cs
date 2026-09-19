using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Services.Client;

public sealed class StormClientConfigurator
{
    private readonly AppSettings settings;

    public StormClientConfigurator(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public ClientConfigureResult Configure()
    {
        ClientSettings client =
            settings.Client
            ?? throw new InvalidOperationException("Client settings are not bound.");

        string gameFolder = AppSettings.UserGameFolderPath;
        Directory.CreateDirectory(gameFolder);
        Directory.CreateDirectory(AppSettings.UserStormInterfacePath);

        string interfaceSource = FindInterfaceFile(client.InterfaceFileName);
        string interfaceDest = Path.Combine(
            AppSettings.UserStormInterfacePath,
            client.InterfaceFileName
        );
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
            ApplyVariables(accountVariables, client.InterfacePreset);
        }

        bool hotSRunning = IsHeroesRunning();
        var warnings = new List<string>();
        if (interfaceSource == null)
        {
            warnings.Add(
                $"Could not find {client.InterfaceFileName}. Place it under obs/interfaces or Assets/Interfaces."
            );
        }

        if (hotSRunning)
        {
            warnings.Add(
                "Heroes of the Storm is running. Restart the client so windowed 1080p and AhliObs take effect."
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

    public ClientStatusResult GetStatus()
    {
        ClientSettings client =
            settings.Client
            ?? throw new InvalidOperationException("Client settings are not bound.");

        string gameFolder = AppSettings.UserGameFolderPath;
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
                CollectMismatches(accountVariables, client.InterfacePreset, mismatches);
            }
        }

        string interfaceDest = Path.Combine(
            AppSettings.UserStormInterfacePath,
            client.InterfaceFileName
        );
        bool interfaceInstalled = File.Exists(interfaceDest);
        if (!interfaceInstalled)
        {
            mismatches.Add($"interface file missing: {interfaceDest}");
        }

        return new ClientStatusResult(
            variablesPath,
            interfaceInstalled,
            mismatches.Count == 0,
            mismatches,
            IsHeroesRunning()
        );
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
                || !string.Equals(value, pair.Value, StringComparison.Ordinal)
            )
            {
                mismatches.Add($"{path}: {pair.Key}={value ?? "(missing)"} (want {pair.Value})");
            }
        }
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
