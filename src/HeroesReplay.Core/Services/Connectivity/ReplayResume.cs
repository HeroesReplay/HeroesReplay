using System;
using System.IO;
using System.Text.Json;

namespace HeroesReplay.Core.Services.Connectivity;

public interface IReplayResume
{
    void Request(int replayId, string replayPath);

    bool TryTake(out int replayId, out string replayPath);
}

public static class ReplayResumeRules
{
    public static bool ShouldReplay(
        bool gameRunning,
        int? replayId,
        int? completedReplayId,
        string replayPath
    )
    {
        if (gameRunning || replayId == null || replayId.Value <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(replayPath) || !File.Exists(replayPath))
        {
            return false;
        }

        return completedReplayId != replayId;
    }
}

public sealed class ReplayResumeFile : IReplayResume
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SharedPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HeroesReplay",
            "replay-resume.json"
        );

    private readonly string path;
    private readonly object gate = new();

    public ReplayResumeFile(string path)
    {
        this.path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public void Request(int replayId, string replayPath)
    {
        if (replayId <= 0 || string.IsNullOrWhiteSpace(replayPath))
        {
            return;
        }

        var request = new ReplayResumeRequest { ReplayId = replayId, ReplayPath = replayPath };
        lock (gate)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(request, JsonOptions));
        }
    }

    public bool TryTake(out int replayId, out string replayPath)
    {
        replayId = 0;
        replayPath = null;
        lock (gate)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                ReplayResumeRequest request = JsonSerializer.Deserialize<ReplayResumeRequest>(
                    File.ReadAllText(path),
                    JsonOptions
                );
                File.Delete(path);
                if (
                    request == null
                    || request.ReplayId <= 0
                    || string.IsNullOrWhiteSpace(request.ReplayPath)
                )
                {
                    return false;
                }

                replayId = request.ReplayId;
                replayPath = request.ReplayPath;
                return true;
            }
            catch (JsonException)
            {
                File.Delete(path);
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    private sealed class ReplayResumeRequest
    {
        public int ReplayId { get; set; }

        public string ReplayPath { get; set; }
    }
}
