using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// The hero statistics a replay's title may use: the download role's file for the replay's major
/// patch and game type. Spectate only reads it. Null when stat hooks are off, or the file is
/// missing, unreadable, or older than <c>HeroesProfileApi:HeroStats:MaxAge</c>; the title then
/// keeps its usual form.
/// </summary>
public static class StatHookSource
{
    public static HeroStatsSnapshot For(
        AppSettings settings,
        LoadedReplay loaded,
        DateTimeOffset now
    )
    {
        if (settings?.YouTube?.Titles?.StatHooks?.Enabled != true || loaded == null)
        {
            return null;
        }

        string dataDirectory = settings.Location?.DataDirectory;
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        string version =
            Text(loaded.Replay?.ReplayVersion) ?? Text(loaded.HeroesProfileReplay?.GameVersion);
        string gameType =
            Text(loaded.HeroesProfileReplay?.GameType) ?? Text(loaded.Replay?.GameMode.ToString());
        TimeSpan maxAge = (settings.HeroesProfileApi?.HeroStats ?? new HeroStatsSettings()).MaxAge;
        try
        {
            return new HeroStatsStore(dataDirectory).ReadFresh(version, gameType, now, maxAge);
        }
        catch (Exception e)
            when (e is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Text(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
