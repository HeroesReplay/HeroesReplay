using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroFocus.Cards;

/// <summary>
/// One JSON file per replay under <c>Data\Focus\cards</c> (#433). The download role is the only
/// writer. Any reader, the spectator included, reads with sharing on and treats a file it cannot
/// read as missing: it never sets a card aside, because the writer may be mid-write.
/// </summary>
public sealed class FocusCardStore
{
    public const string FolderName = "cards";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public FocusCardStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Folder = directory;
    }

    public string Folder { get; }

    /// <summary><c>Data\Focus\cards</c> under the data directory.</summary>
    public static string DirectoryFor(string dataDirectory) =>
        Path.Combine(dataDirectory, HeroFocusPaths.FolderName, FolderName);

    public string PathFor(string key) => Path.Combine(Folder, FileNameFor(key));

    public void Write(FocusCard card)
    {
        if (string.IsNullOrWhiteSpace(card?.Key))
        {
            throw new ArgumentException("A card needs a key.", nameof(card));
        }

        DurableFile.Replace(PathFor(card.Key), JsonSerializer.Serialize(card, Json));
    }

    /// <summary>The card, or null when it is missing or cannot be read right now.</summary>
    public FocusCard Read(string key) =>
        string.IsNullOrWhiteSpace(key) ? null : ReadFile(PathFor(key));

    /// <summary>Every card that reads, in key order.</summary>
    public IReadOnlyList<FocusCard> ReadAll() =>
        Files()
            .Select(ReadFile)
            .Where(card => card != null)
            .OrderBy(card => card.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>The keys of the cards on disk, read or not.</summary>
    public IReadOnlyList<string> Keys() =>
        Files().Select(Path.GetFileNameWithoutExtension).OrderBy(key => key).ToList();

    public bool Delete(string key)
    {
        string path = string.IsNullOrWhiteSpace(key) ? null : PathFor(key);
        if (path == null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>A key as a file name: characters a path cannot hold become <c>_</c>.</summary>
    public static string FileNameFor(string key)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string name = new(key.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return name + ".json";
    }

    private IEnumerable<string> Files() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*.json")
            : Enumerable.Empty<string>();

    internal static FocusCard ReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            FocusCard card = JsonSerializer.Deserialize<FocusCard>(stream, Json);
            return string.IsNullOrWhiteSpace(card?.Key) ? null : card;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
