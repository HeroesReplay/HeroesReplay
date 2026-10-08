using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Dependencies;

/// <summary>One file <c>deps install</c> moved into a tool folder.</summary>
public sealed record DependencyInstalledFile(string Name, long Size, string Sha256);

/// <summary>
/// <c>installed.json</c> in a tool folder: which pinned build is there. It is written only after
/// every file is in place and deleted before any file is replaced, so a partial install is never
/// taken for a complete one.
/// </summary>
public sealed record DependencyInstallRecord(
    string Name,
    string Version,
    string Sha256,
    string Url,
    IReadOnlyList<DependencyInstalledFile> Files,
    DateTimeOffset InstalledAt
)
{
    public const string FileName = "installed.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string PathIn(string toolDirectory) => Path.Combine(toolDirectory, FileName);

    /// <summary>The record in <paramref name="toolDirectory"/>, or null when it is missing or unreadable.</summary>
    public static DependencyInstallRecord Read(string toolDirectory)
    {
        string text = DurableFile.ReadOrAside(PathIn(toolDirectory));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DependencyInstallRecord>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(string toolDirectory) =>
        DurableFile.Replace(PathIn(toolDirectory), JsonSerializer.Serialize(this, Json));

    public DependencyInstalledFile FindFile(string name) =>
        Files?.FirstOrDefault(file =>
            string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)
        );
}
