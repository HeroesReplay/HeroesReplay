using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace HeroesReplay.Core.Services.Twitch;

public sealed class PredictionStreakBook
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string LastPredictionId { get; set; }

    public Dictionary<string, int> Streaks { get; set; } = new Dictionary<string, int>();

    public static PredictionStreakBook Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new PredictionStreakBook();
        }

        try
        {
            PredictionStreakBook book = JsonSerializer.Deserialize<PredictionStreakBook>(
                File.ReadAllText(path),
                JsonOptions
            );
            if (book == null)
            {
                return new PredictionStreakBook();
            }

            book.Streaks ??= new Dictionary<string, int>();
            return book;
        }
        catch (JsonException)
        {
            return new PredictionStreakBook();
        }
    }

    public void Save(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public int Apply(string predictionId, string userId, bool won)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        Streaks.TryGetValue(userId, out int current);
        if (string.Equals(LastPredictionId, predictionId, StringComparison.Ordinal))
        {
            return current;
        }

        int next = won ? current + 1 : 0;
        Streaks[userId] = next;
        return next;
    }

    public void Remember(string predictionId)
    {
        if (!string.IsNullOrWhiteSpace(predictionId))
        {
            LastPredictionId = predictionId;
        }
    }
}
