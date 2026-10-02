using System;
using System.IO;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// Machine-local consent for Twitch ingest. HeroesReplay starts a stream only when
/// <c>OBS:StreamingEnabled</c> is true and this file exists. It sits with the other machine
/// state in <c>%LOCALAPPDATA%\HeroesReplay</c>, never in the repo or the release zip, and no
/// setting can move it, so an appsettings overlay or environment variable alone cannot arm a
/// machine. <c>heroesreplay obs arm</c> and <c>obs disarm</c> write and delete it. Recording
/// does not need it.
/// </summary>
public sealed class ObsStreamArm
{
    public const string FileName = "stream-armed";
    public const string NotArmedReason = "obs.stream_not_armed";

    public ObsStreamArm()
        : this(DefaultPath()) { }

    public ObsStreamArm(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("The arm file path is required.", nameof(filePath));
        }

        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            FileName
        );

    public bool IsArmed() => File.Exists(FilePath);

    public void Arm(string note)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(
            FilePath,
            "Armed "
                + DateTimeOffset.UtcNow.ToString("O")
                + " on "
                + Environment.MachineName
                + ". "
                + (note ?? string.Empty)
                + Environment.NewLine
        );
    }

    /// <summary>Returns false when the machine was not armed.</summary>
    public bool Disarm()
    {
        if (!File.Exists(FilePath))
        {
            return false;
        }

        File.Delete(FilePath);
        return true;
    }
}
