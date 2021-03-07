using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analysis;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Context
{
    public class ContextManager : IContextManager
    {
        private readonly ILogger<ContextManager> logger;
        private readonly IReplayContext context;
        private readonly IReplayAnalyzer replayAnalyzer;
        private readonly IOptions<AppSettings> settings;

        public ContextManager(ILogger<ContextManager> logger, IReplayContext replayContext, IOptions<AppSettings> settings, IReplayAnalyzer replayAnalyzer)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.context = replayContext;
            this.replayAnalyzer = replayAnalyzer ?? throw new ArgumentNullException(nameof(replayAnalyzer));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task SetContextAsync(LoadedReplay loadedReplay)
        {
            if (loadedReplay == null)
                throw new ArgumentNullException(nameof(loadedReplay));

            context.Previous = context.Current;

            Replay replay = loadedReplay.Replay;

            var players = replayAnalyzer.GetPlayers(replay);
            var panels = replayAnalyzer.GetPanels(replay);
            var end = replayAnalyzer.GetEnd(replay);
            var isCarried = replayAnalyzer.GetIsCarriedObjective(replay);
            var start = replayAnalyzer.GetStart(replay);
            var payloads = replayAnalyzer.GetPayloads(replay);
            var teamBans = replayAnalyzer.GetTeamBans(replay);
            var directory = Directory.CreateDirectory(settings.Value.ContextsDirectory).CreateSubdirectory($"{loadedReplay.ReplayId}");

            context.Current = new ContextData
            {
                LoadedReplay = loadedReplay,
                Payloads = payloads,
                Players = players,
                Panels = panels,
                GatesOpen = start,
                CoreKilled = end,
                IsCarriedObjectiveMap = isCarried,
                Timeloaded = DateTime.Now,
                Directory = directory,
                TeamBans = teamBans
            };

            await WriteContextFilesAsync();
        }

        public async Task WriteContextFilesAsync()
        {
            CopyReplayToContext();

            await WriteYoutubeEntryAsync();
            await WriteObsEntryAsync();
        }

        private async Task WriteObsEntryAsync()
        {
            try
            {
                ObsEntry entry = new ObsEntry
                {
                    Map = context.Current.LoadedReplay.HeroesProfileReplay.Map,
                    GameType = context.Current.LoadedReplay.HeroesProfileReplay.GameType,
                    Rank = context.Current.LoadedReplay.HeroesProfileReplay.Rank,
                    TwitchLogin = context.Current.LoadedReplay.RewardQueueItem?.Request?.Login,
                    TeamBans = GetTeamBans(),
                    RecordingDirectory = context.Current.Directory.FullName
                };

                string file = Path.Combine(context.Current.Directory.FullName, settings.Value.Obs.EntryFileName);
                string json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllTextAsync(file, json);
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not write OBS info file.");
            }
        }

        private string GetTeamBans()
        {
            IEnumerable<string> bans = Enumerable.Empty<string>();

            if (context.Current.TeamBans.Values.Any(teamBans => teamBans.Any()))
            {
                bans = bans.Append("Bans:");

                if (context.Current.TeamBans[0].Any())
                {
                    bans = bans.Concat(context.Current.TeamBans[0].Select(ban => $"T1: {ban}"));
                }

                if (context.Current.TeamBans[1].Any())
                {
                    bans = bans.Concat(context.Current.TeamBans[1].Select(ban => $"T2: {ban}"));
                }
            }

            return string.Join(Environment.NewLine, bans);
        }

        private void CopyReplayToContext()
        {
            try
            {
                if (context.Current.LoadedReplay.FileInfo.Exists)
                {
                    context.Current.LoadedReplay.FileInfo.CopyTo(Path.Combine(context.Current.Directory.FullName, context.Current.LoadedReplay.FileInfo.Name));
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

        private async Task WriteYoutubeEntryAsync()
        {
            if (settings.Value.YouTube.Enabled)
            {
                try
                {
                    var heroesProfileReplay = context.Current.LoadedReplay.HeroesProfileReplay;

                    var entry = new YouTubeEntry()
                    {
                        Title = string.Join(" - ", new[] { $"{heroesProfileReplay.Id}", heroesProfileReplay.Map, heroesProfileReplay.GameType ?? string.Empty, heroesProfileReplay.Rank }),
                        PrivacyStatus = "public",
                        CategoryId = settings.Value.YouTube.CategoryId,
                        DescriptionLines = new[] { $"Twitch: http://twitch.tv/saltysadism", $"Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID={heroesProfileReplay.Id}" },
                        Tags = new[]
                        {
                            heroesProfileReplay.GameType,
                            heroesProfileReplay.Map,
                            heroesProfileReplay.Rank
                        }.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
                    };

                    string file = Path.Combine(context.Current.Directory.FullName, settings.Value.YouTube.EntryFileName);
                    string json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });

                    await File.WriteAllTextAsync(file, json);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not write youtube info file.");
                }
            }
        }
    }
}