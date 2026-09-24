using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Heroes.ReplayParser;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis;

public sealed class MatchClipEntry
{
    public int? ReplayId { get; set; }
    public string Kind { get; set; }
    public string Hero { get; set; }
    public string Description { get; set; }
    public int HudStartSecond { get; set; }
    public int HudEndSecond { get; set; }
    public double FileStartSeconds { get; set; }
    public double FileEndSeconds { get; set; }
    public string File { get; set; }
}

public static class TeamKillDeaths
{
    public static IReadOnlyList<TeamKillDeath> FromHeroUnits(IEnumerable<Unit> units)
    {
        var deaths = new List<TeamKillDeath>();
        if (units == null)
        {
            return deaths;
        }

        foreach (Unit unit in units)
        {
            if (unit?.TimeSpanDied == null || unit.PlayerControlledBy == null)
            {
                continue;
            }

            deaths.Add(
                new TeamKillDeath(
                    unit.TimeSpanDied.Value.FloorSeconds(),
                    unit.PlayerKilledBy?.Character,
                    unit.PlayerControlledBy.Character
                )
            );
        }

        return deaths;
    }
}

public static class MatchClipList
{
    public const string FileName = "clips.json";

    public static IReadOnlyList<MatchClipEntry> Ready(
        int? replayId,
        IReadOnlyList<TeamKillClip> clips,
        RecordingClock clock
    )
    {
        var ready = new List<MatchClipEntry>();
        if (clips == null || clock == null)
        {
            return ready;
        }

        foreach (TeamKillClip clip in clips)
        {
            if (
                !clock.TryFileSeconds(clip.HudStartSecond, out double start)
                || !clock.TryFileSeconds(clip.HudEndSecond, out double end)
                || end <= start
            )
            {
                continue;
            }

            ready.Add(
                new MatchClipEntry
                {
                    ReplayId = replayId,
                    Kind = clip.Kind,
                    Hero = clip.Hero,
                    Description = clip.Description,
                    HudStartSecond = clip.HudStartSecond,
                    HudEndSecond = clip.HudEndSecond,
                    FileStartSeconds = start,
                    FileEndSeconds = end,
                    File = ClipFileName(clip),
                }
            );
        }

        return ready;
    }

    public static string ClipFileName(TeamKillClip clip)
    {
        string hero = string.IsNullOrWhiteSpace(clip.Hero) ? "team" : clip.Hero;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            hero = hero.Replace(invalid, '-');
        }

        return clip.Kind
            + "-"
            + hero
            + "-"
            + clip.FirstDeathSecond.ToString(CultureInfo.InvariantCulture)
            + ".mp4";
    }

    public static void Write(string path, IReadOnlyList<MatchClipEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(entries ?? Array.Empty<MatchClipEntry>(), WriteOptions())
        );
    }

    public static IReadOnlyList<MatchClipEntry> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Array.Empty<MatchClipEntry>();
        }

        List<MatchClipEntry> entries = JsonSerializer.Deserialize<List<MatchClipEntry>>(
            File.ReadAllText(path)
        );
        return entries ?? new List<MatchClipEntry>();
    }

    private static JsonSerializerOptions WriteOptions() => new() { WriteIndented = true };
}
