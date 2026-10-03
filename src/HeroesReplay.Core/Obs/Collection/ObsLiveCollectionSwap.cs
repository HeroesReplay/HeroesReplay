using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

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

    /// <summary>How many times a switch back to the main collection is tried.</summary>
    public const int ReturnAttempts = 3;

    public static ObsLiveSwapResult Run(
        IObsCollectionSwitch obs,
        ObsCollectionReplacement replacement,
        string collection,
        ObsManagedFiles managed,
        DateTime utcNow,
        Action<int> wait = null
    )
    {
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(managed);
        string main = ObsNames.Pick(collection);
        string spare = SpareName(main);

        ObsLiveSwapResult recovered = Recover(obs, main, wait);
        if (recovered?.Stranded == true)
        {
            return recovered;
        }

        IReadOnlyList<string> collections = obs.Collections(out string current);
        if (!string.Equals(current, main, StringComparison.Ordinal))
        {
            return new ObsLiveSwapResult(
                false,
                $"OBS has collection '{current}' active, not '{main}'. The collection is replaced the next time HeroesReplay finds OBS closed."
            );
        }

        string scenes = Path.GetDirectoryName(Path.GetFullPath(replacement.DestinationPath));
        string backup = null;
        bool replaced = false;
        bool leftMain = false;
        string scene = null;
        string written;
        try
        {
            if (!collections.Contains(spare, StringComparer.Ordinal))
            {
                // OBS lists collection files only when it starts, so the spare is created through
                // OBS once. That shows an empty collection on program until the switch back.
                leftMain = true;
                obs.Create(spare);
                obs.Select(main);
                leftMain = false;
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

            scene = obs.ProgramScene();
            leftMain = true;
            obs.Select(spare);

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
        }
        catch (Exception e) when (!leftMain)
        {
            return new ObsLiveSwapResult(
                false,
                $"OBS stayed on '{main}'. The collection was not swapped: {e.Message}"
            );
        }
        catch (Exception e)
        {
            // OBS may be on the spare, or on an empty one it just created. Never leave it there (#214).
            written = $"The swap failed after OBS left '{main}': {e.Message}";
        }

        string failure = ReturnToMain(obs, main, wait);
        if (failure != null)
        {
            return new ObsLiveSwapResult(
                false,
                $"{written} OBS did not switch back to '{main}' after {ReturnAttempts} tries and stays on '{spare}'. HeroesReplay tries again before the next replay. {failure}",
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

    /// <summary>
    /// Before each replay: OBS left on the spare collection by an earlier swap goes back to the
    /// main one, whatever the template record says (#214). Null when OBS is not on the spare.
    /// </summary>
    public static ObsLiveSwapResult Recover(
        IObsCollectionSwitch obs,
        string collection,
        Action<int> wait = null
    )
    {
        ArgumentNullException.ThrowIfNull(obs);
        string main = ObsNames.Pick(collection);
        string spare = SpareName(main);
        obs.Collections(out string current);
        if (!string.Equals(current, spare, StringComparison.Ordinal))
        {
            return null;
        }

        string failure = ReturnToMain(obs, main, wait);
        return failure == null
            ? new ObsLiveSwapResult(false, $"OBS was left on '{spare}'. It is back on '{main}'.")
            : new ObsLiveSwapResult(
                false,
                $"OBS is on '{spare}' and did not switch back to '{main}' after {ReturnAttempts} tries. {failure}",
                Stranded: true
            );
    }

    /// <summary>Null when OBS is on <paramref name="main"/>, otherwise the last failure.</summary>
    private static string ReturnToMain(IObsCollectionSwitch obs, string main, Action<int> wait)
    {
        Action<int> pause = wait ?? (attempt => Thread.Sleep(TimeSpan.FromSeconds(attempt)));
        string failure = null;
        for (int attempt = 1; attempt <= ReturnAttempts; attempt++)
        {
            try
            {
                obs.Select(main);
                return null;
            }
            catch (Exception e)
            {
                failure = e.Message;
                if (attempt < ReturnAttempts)
                {
                    pause(attempt);
                }
            }
        }

        return failure;
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
