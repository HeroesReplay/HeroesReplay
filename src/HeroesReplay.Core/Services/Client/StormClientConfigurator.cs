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
        string existing = File.Exists(variablesPath)
            ? File.ReadAllText(variablesPath)
            : string.Empty;
        string updated = StormVariablesEditor.Apply(existing, client.VariablesPreset);
        File.WriteAllText(variablesPath, updated);

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

        string variablesPath = Path.Combine(AppSettings.UserGameFolderPath, "Variables.txt");
        Dictionary<string, string> actual = File.Exists(variablesPath)
            ? StormVariablesEditor.Parse(File.ReadAllText(variablesPath))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<string>();
        foreach (KeyValuePair<string, string> pair in client.VariablesPreset)
        {
            if (
                !actual.TryGetValue(pair.Key, out string value)
                || !string.Equals(value, pair.Value, StringComparison.Ordinal)
            )
            {
                mismatches.Add($"{pair.Key}={value ?? "(missing)"} (want {pair.Value})");
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
