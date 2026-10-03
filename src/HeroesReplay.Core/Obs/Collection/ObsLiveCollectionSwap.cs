using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HeroesReplay.Core.Obs.Collection;

/// <summary>The OBS requests a live collection swap sends.</summary>
public interface IObsCollectionSwitch
{
    /// <summary>The scene collections OBS lists, and the active one.</summary>
    IReadOnlyList<string> Collections(out string current);

    /// <summary>Creates an empty collection. OBS makes it active.</summary>
    void Create(string name);

    /// <summary>Makes a collection active. OBS saves the one it leaves to its file first.</summary>
    void Select(string name);

    /// <summary>The scene on program, or null.</summary>
    string ProgramScene();

    void ShowScene(string name);
}

/// <param name="Swapped">OBS has the new collection active under its own name.</param>
/// <param name="Message">What happened, for the log.</param>
/// <param name="Backup">The copy of the collection file taken before the write.</param>
/// <param name="Stranded">OBS was left on the spare collection, because the switch back failed.</param>
public sealed record ObsLiveSwapResult(
    bool Swapped,
    string Message,
    string Backup = null,
    bool Stranded = false
);

/// <summary>
/// Puts a new collection template into a running OBS without stopping its outputs. OBS reads a
/// collection file when it switches to it, and saves the collection it leaves. So the new layout
/// is written to a spare collection, OBS switches to it, the main file (no longer active) is
/// rewritten, and OBS switches back to the main name. The stream and the recording stay
/// connected across both switches (ASA-SERVER, OBS 32.2.2).
/// </summary>
public static class ObsLiveCollectionSwap
{
    public const string SpareSuffix = "-next";

    public static string SpareName(string collection) => ObsNames.Pick(collection) + SpareSuffix;

    public static ObsLiveSwapResult Run(
        IObsCollectionSwitch obs,
        ObsCollectionReplacement replacement,
        string collection,
        ObsManagedFiles managed,
        DateTime utcNow
    )
    {
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(managed);
        string main = ObsNames.Pick(collection);
        string spare = SpareName(main);

        IReadOnlyList<string> collections = obs.Collections(out string current);
        if (!string.Equals(current, main, StringComparison.Ordinal))
        {
            return new ObsLiveSwapResult(
                false,
                $"OBS has collection '{current}' active, not '{main}'. The collection is replaced the next time HeroesReplay finds OBS closed."
            );
        }

        string scenes = Path.GetDirectoryName(Path.GetFullPath(replacement.DestinationPath));
        if (!collections.Contains(spare, StringComparer.Ordinal))
        {
            // OBS lists collection files only when it starts, so the spare is created through
            // OBS once. That shows an empty collection on program until the switch back.
            obs.Create(spare);
            obs.Select(main);
        }

        string spareFile = CollectionFile(scenes, spare);
        if (spareFile == null)
        {
            return new ObsLiveSwapResult(
                false,
                $"OBS lists '{spare}' but no collection file in {scenes} has that name. The collection is replaced the next time HeroesReplay finds OBS closed."
            );
        }

        ObsFileTransaction.Write(
            spareFile,
            ObsNames.WithCollectionName(replacement.Contents, spare),
            managed.BackupDirectory,
            utcNow
        );

        string scene = obs.ProgramScene();
        obs.Select(spare);

        string backup = null;
        bool replaced = false;
        string written;
        try
        {
            backup = ObsCollectionPatcher.WriteReplacement(replacement, managed, utcNow);
            replaced = true;
            written = "Replaced the live OBS collection while OBS runs.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            written =
                "The new layout showed from the spare collection, but the collection file was not written, so OBS is back on the old one. "
                + e.Message;
        }

        try
        {
            obs.Select(main);
        }
        catch (Exception e)
        {
            return new ObsLiveSwapResult(
                false,
                $"{written} OBS did not switch back to '{main}' and stays on '{spare}'. {e.Message}",
                backup,
                Stranded: true
            );
        }

        if (!string.IsNullOrWhiteSpace(scene))
        {
            try
            {
                obs.ShowScene(scene);
            }
            catch (Exception)
            {
                // The new layout no longer has that scene. The spectator selects its own next.
            }
        }

        return new ObsLiveSwapResult(
            replaced,
            written + (backup == null ? "" : $" The previous collection was saved to {backup}."),
            backup
        );
    }

    /// <summary>The file in <paramref name="scenes"/> whose top-level <c>name</c> is <paramref name="collection"/>.</summary>
    public static string CollectionFile(string scenes, string collection)
    {
        if (!Directory.Exists(scenes))
        {
            return null;
        }

        foreach (string file in Directory.EnumerateFiles(scenes, "*.json"))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
                if (
                    document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && string.Equals(name.GetString(), collection, StringComparison.Ordinal)
                )
                {
                    return file;
                }
            }
            catch (Exception e)
                when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                // Not a collection file this can read.
            }
        }

        return null;
    }
}
