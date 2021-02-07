using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class HeroesProfileExtensionPayloadsBuilder : IHeroesProfileExtensionPayloadsBuilder
    {
        public const string PLACEHOLDER_SESSION_ID = nameof(PLACEHOLDER_SESSION_ID);

        private readonly ILogger<HeroesProfileExtensionPayloadsBuilder> logger;
        private readonly AppSettings settings;

        public HeroesProfileExtensionPayloadsBuilder(ILogger<HeroesProfileExtensionPayloadsBuilder> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public TalentExtensionPayloads CreatePayloads(Replay replay)
        {
            if (settings.TwitchExtension.Enabled)
            {
                TalentExtensionPayloads payloads = new();

                payloads.Create.Add(CreateReplay(replay));
                payloads.Create.Add(CreatePlayer(replay));

                payloads.Update.Add(UpdateReplay(replay));
                payloads.Update.Add(UpdatePlayer(replay));

                payloads.Talents = CreateTalents(replay);

                return payloads;
            }

            return null;
        }

        private Dictionary<TimeSpan, List<HeroesProfileTwitchPayload>> CreateTalents(Replay replay)
        {
            Dictionary<TimeSpan, List<HeroesProfileTwitchPayload>> talentEvents = new Dictionary<TimeSpan, List<HeroesProfileTwitchPayload>>();

            if (replay.TrackerEvents != null)
            {
                for (int i = 0; i < replay.TrackerEvents.Count; i++)
                {
                    var trackerEvent = replay.TrackerEvents[i];

                    if (trackerEvent.Data.dictionary[0].blobText == settings.TrackerEvents.TalentChosen)
                    {
                        string talentName = trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                        TimeSpan timeSpan = trackerEvent.TimeSpan;
                        int playerID = Convert.ToInt32(replay.TrackerEvents[i].Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value);
                        Player player = replay.Players[playerID - 1];

                        if (!talentEvents.ContainsKey(timeSpan))
                        {
                            talentEvents[timeSpan] = new List<HeroesProfileTwitchPayload>();
                        }

                        var talents = talentEvents[timeSpan];

                        talents.Add(new HeroesProfileTwitchPayload()
                        {
                            Step = HeroesProfileTwitchExtensionStep.SaveTalentData,
                            Content = new List<Dictionary<string, string>>()
                            {
                                new ()
                                {
                                    { settings.TwitchExtension.TwitchApiKey , settings.TwitchExtension.ApiKey },
                                    { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                                    { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                                    { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId },
                                    { settings.TwitchExtension.ReplayIdKey, PLACEHOLDER_SESSION_ID },
                                    { settings.TwitchExtension.BlizzIdKey, player.BattleNetId.ToString() },
                                    { settings.TwitchExtension.BattleTagKey, player.Name },
                                    { settings.TwitchExtension.RegionKey, player.BattleNetRegionId.ToString() },
                                    { settings.TwitchExtension.TalentKey, talentName },
                                    { settings.TwitchExtension.HeroKey, player.Character },
                                }
                            }
                        });
                    }
                }

                var payloads = talentEvents.Sum(x => x.Value.Count);

                logger.LogInformation($"Total talent payloads: {payloads}");
            }

            return talentEvents;
        }

        private HeroesProfileTwitchPayload UpdateReplay(Replay replay)
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.UpdateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new ()
                    {
                        { settings.TwitchExtension.TwitchApiKey , settings.TwitchExtension.ApiKey },
                        { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                        { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                        { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId },
                        { settings.TwitchExtension.ReplayIdKey, PLACEHOLDER_SESSION_ID },
                        { settings.TwitchExtension.GameTypeKey, replay.GameMode.ToString() },
                        { settings.TwitchExtension.GameMapKey, replay.Map },
                        { settings.TwitchExtension.GameVersionKey, replay.ReplayVersion },
                        { settings.TwitchExtension.RegionKey, replay.Players[0].BattleNetRegionId.ToString() },
                    }
                }
            };
        }

        private HeroesProfileTwitchPayload UpdatePlayer(Replay replay)
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.UpdatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>()
                {
                    { settings.TwitchExtension.TwitchApiKey , settings.TwitchExtension.ApiKey },
                    { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                    { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                    { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId },
                    { settings.TwitchExtension.BattleTagKey, player.Name },
                    { settings.TwitchExtension.ReplayIdKey, PLACEHOLDER_SESSION_ID },
                    { settings.TwitchExtension.BlizzIdKey, player.BattleNetId.ToString() },
                    { settings.TwitchExtension.TeamKey, player.Team.ToString() },
                    { settings.TwitchExtension.HeroKey, player.Character },
                    { settings.TwitchExtension.RegionKey, player.BattleNetRegionId.ToString() }
                }).ToList()
            };
        }

        private HeroesProfileTwitchPayload CreatePlayer(Replay replay)
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.CreatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>()
                {
                    { settings.TwitchExtension.TwitchApiKey , settings.TwitchExtension.ApiKey },
                    { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                    { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                    { settings.TwitchExtension.ReplayIdKey, PLACEHOLDER_SESSION_ID },
                    { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId },
                    { settings.TwitchExtension.BattleTagKey, player.Name },
                    { settings.TwitchExtension.TeamKey, player.Team.ToString() },
                }).ToList()
            };
        }

        private HeroesProfileTwitchPayload CreateReplay(Replay replay)
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.CreateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new ()
                    {
                        { settings.TwitchExtension.TwitchApiKey , settings.TwitchExtension.ApiKey },
                        { settings.TwitchExtension.TwitchEmailKey, settings.TwitchExtension.ApiEmail },
                        { settings.TwitchExtension.TwitchUserNameKey, settings.TwitchExtension.TwitchUserName },
                        { settings.TwitchExtension.UserIdKey, settings.TwitchExtension.ApiUserId },
                        { settings.TwitchExtension.GameDateKey, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    }
                }
            };
        }
    }
}