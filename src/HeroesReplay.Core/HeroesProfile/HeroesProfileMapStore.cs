using System;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Heroes Profile's <c>/maps</c> list in <c>Data\HeroesProfile\maps.json</c> (#425). The reward
/// sync refreshes it (<see cref="HeroesProfileMapRefresh"/>); the Storm League map pool and the
/// queue page only read it. Without a data directory there is no file: reads return null and
/// writes do nothing.
/// </summary>
public sealed class HeroesProfileMapStore
{
    public const string FileName = "maps.json";

    public HeroesProfileMapStore(string dataDirectory)
    {
        FilePath = string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.Combine(dataDirectory, "HeroesProfile", FileName);
    }

    public string FilePath { get; }

    public static HeroesProfileMapStore For(AppSettings settings) =>
        new(settings?.Location?.DataDirectory);

    /// <summary>The saved list, whatever its age. Null when it is missing or unreadable.</summary>
    public HeroesProfileMapList Read()
    {
        if (FilePath == null || !File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            return HeroesProfileMapList.FromJson(
                File.ReadAllText(FilePath),
                File.GetLastWriteTimeUtc(FilePath)
            );
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

    public void Write(HeroesProfileMapList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        DurableFile.Replace(FilePath, list.ToJson());
    }

    /// <summary>
    /// True when <paramref name="list"/> was fetched within <paramref name="maxAge"/> of
    /// <paramref name="now"/>. False when it is missing, older, or dated in the future.
    /// </summary>
    public static bool IsFresh(HeroesProfileMapList list, DateTimeOffset now, TimeSpan maxAge)
    {
        if (list == null || maxAge <= TimeSpan.Zero)
        {
            return false;
        }

        TimeSpan age = now - list.FetchedAtUtc;
        return age >= TimeSpan.FromMinutes(-5) && age <= maxAge;
    }
}
