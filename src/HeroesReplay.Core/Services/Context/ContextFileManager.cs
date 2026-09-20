using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Context;

public class ContextFileManager : IContextFileManager
{
    private readonly ILogger<ContextFileManager> logger;
    private readonly IGameData gameData;
    private readonly AppSettings settings;

    public ContextFileManager(
        ILogger<ContextFileManager> logger,
        AppSettings settings,
        IGameData gameData
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task WriteContextFilesAsync(ContextData contextData)
    {
        CopyReplayToContext(contextData);
        await WriteYoutubeEntryAsync(contextData);
        await WriteObsFileAsync(contextData);
    }

    private void CopyReplayToContext(ContextData contextData)
    {
        try
        {
            if (contextData.LoadedReplay.FileInfo.Exists)
            {
                contextData.LoadedReplay.FileInfo.CopyTo(
                    Path.Combine(
                        contextData.Directory.FullName,
                        contextData.LoadedReplay.FileInfo.Name
                    ),
                    overwrite: true
                );
            }
            else
            {
                logger.LogWarning("Could not copy replay file into Context directory");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not copy replay file to context directory");
        }
    }

    private async Task WriteObsFileAsync(ContextData contextData)
    {
        if (settings.ReplayDetailsWriter.Enabled)
        {
            try
            {
                Replay replay = contextData.LoadedReplay.Replay;
                string requestor = contextData.LoadedReplay.RewardQueueItem?.Request?.Login;
                string gameType = contextData.LoadedReplay.HeroesProfileReplay?.GameType;
                IEnumerable<string> bans = Enumerable.Empty<string>();

                if (contextData.TeamBans.Values.Any(teamBans => teamBans.Any()))
                {
                    bans = bans.Append("Bans:");

                    if (contextData.TeamBans[0].Any())
                    {
                        bans = bans.Concat(contextData.TeamBans[0].Select(ban => $"T1: {ban}"));
                    }

                    if (contextData.TeamBans[1].Any())
                    {
                        bans = bans.Concat(contextData.TeamBans[1].Select(ban => $"T2: {ban}"));
                    }
                }

                var lines = new[]
                {
                    settings.ReplayDetailsWriter.Requestor
                        ? requestor != null
                            ? $"Requestor: {requestor}"
                            : string.Empty
                        : string.Empty,
                    settings.ReplayDetailsWriter.GameType ? gameType ?? string.Empty : string.Empty,
                }.Concat(settings.ReplayDetailsWriter.Bans ? bans : Enumerable.Empty<string>()).Where(line => !string.IsNullOrWhiteSpace(line));

                string file = Path.Combine(
                    contextData.Directory.FullName,
                    settings.OBS.InfoFileName
                );
                logger.LogInformation($"writing replay details to: {file}");
                await File.WriteAllLinesAsync(file, lines, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not write OBS info file.");
            }
        }
    }

    private async Task WriteYoutubeEntryAsync(ContextData contextData)
    {
        LoadedReplay loaded = contextData.LoadedReplay;
        if (!SessionMedia.ShouldWriteYouTubeEntry(settings.YouTube, loaded))
        {
            return;
        }

        try
        {
            var heroesProfileReplay = loaded.HeroesProfileReplay;
            string requestor = loaded.RewardQueueItem?.Request?.Login;
            string map = heroesProfileReplay?.Map ?? loaded.Replay?.Map;
            string gameType = heroesProfileReplay?.GameType;
            string rank = heroesProfileReplay?.Rank;
            string id = heroesProfileReplay?.Id.ToString();

            var descriptionLines = new[]
            {
                "Twitch: http://twitch.tv/saltysadism",
                heroesProfileReplay != null
                    ? $"Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID={heroesProfileReplay.Id}"
                    : string.Empty,
                gameType != null ? $"Game type: {gameType}" : string.Empty,
                !string.IsNullOrWhiteSpace(rank) ? $"Rank: {rank}" : string.Empty,
                !string.IsNullOrWhiteSpace(requestor) ? $"Requested by: {requestor}" : string.Empty,
                "Hashtags: #HeroesOfTheStorm #SaltySadism",
            }
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            var entry = new YouTubeEntry()
            {
                Title = string.Join(
                    " - ",
                    new[] { id, map, gameType, rank }.Where(part =>
                        !string.IsNullOrWhiteSpace(part)
                    )
                ),
                PrivacyStatus = settings.YouTube.PrivacyStatus ?? "private",
                CategoryId = settings.YouTube.CategoryId,
                DescriptionLines = descriptionLines,
                Tags = new[] { gameType, map }.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray(),
            };

            string file = Path.Combine(
                contextData.Directory.FullName,
                settings.YouTube.EntryFileName
            );
            string json = JsonSerializer.Serialize(
                entry,
                new JsonSerializerOptions { WriteIndented = true }
            );

            await File.WriteAllTextAsync(file, json);
            logger.LogInformation("Wrote YouTube entry {File}.", file);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not write youtube info file.");
        }
    }
}
