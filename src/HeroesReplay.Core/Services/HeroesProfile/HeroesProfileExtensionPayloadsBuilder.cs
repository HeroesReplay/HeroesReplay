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
        private readonly List<KeyValuePair<string, string>> sharedFormData;

        public HeroesProfileExtensionPayloadsBuilder(ILogger<HeroesProfileExtensionPayloadsBuilder> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            this.sharedFormData = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(TwitchExtensionFormKeys.TwitchKey, settings.TwitchExtension.ApiKey),
                new KeyValuePair<string, string>(TwitchExtensionFormKeys.Email, settings.TwitchExtension.ApiEmail),
                new KeyValuePair<string, string>(TwitchExtensionFormKeys.TwitchUserName, settings.TwitchExtension.TwitchUserName),
                new KeyValuePair<string, string>(TwitchExtensionFormKeys.UserId, settings.TwitchExtension.ApiUserId),
            };
        }

        public TalentExtensionPayloads CreatePayloads(Replay replay)
        {
            if (settings.TwitchExtension.Enabled)
            {
                TalentExtensionPayloads payloads = new();

                payloads.Create.Add(CreateSession());
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
                foreach (var trackerEvent in replay.TrackerEvents) 
                {
                    if (trackerEvent.Data.dictionary[0].blobText == settings.TrackerEvents.TalentChosen)
                    {
                        string talentName = trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                        TimeSpan timeSpan = trackerEvent.TimeSpan;
                        int playerID = Convert.ToInt32(trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value);
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
                                new (sharedFormData)
                                {
                                    { TwitchExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                                    { TwitchExtensionFormKeys.BlizzId, player.BattleNetId.ToString() },
                                    { TwitchExtensionFormKeys.BattleTag, player.Name },
                                    { TwitchExtensionFormKeys.BattleNetId, player.BattleNetId.ToString() },
                                    { TwitchExtensionFormKeys.Region, player.BattleNetRegionId.ToString() },
                                    { TwitchExtensionFormKeys.Talent, talentName },
                                    { TwitchExtensionFormKeys.Hero, player.Character },
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
                    new (sharedFormData)
                    {
                        { TwitchExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                        { TwitchExtensionFormKeys.GameType, replay.GameMode.ToString() },
                        { TwitchExtensionFormKeys.GameMap, replay.Map },
                        { TwitchExtensionFormKeys.GameVersion, replay.ReplayVersion },
                        { TwitchExtensionFormKeys.Region, replay.Players[0].BattleNetRegionId.ToString() },
                    }
                }
            };
        }

        private HeroesProfileTwitchPayload UpdatePlayer(Replay replay)
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.UpdatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>(sharedFormData)
                {
                    { TwitchExtensionFormKeys.BattleTag, player.Name },
                    { TwitchExtensionFormKeys.BattleNetId, player.BattleNetId.ToString() },
                    { TwitchExtensionFormKeys.HeroId, player.HeroId },
                    { TwitchExtensionFormKeys.HeroAttributeId, player.HeroAttributeId },
                    { TwitchExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                    { TwitchExtensionFormKeys.BlizzId, player.BattleNetId.ToString() },
                    { TwitchExtensionFormKeys.Team, player.Team.ToString() },
                    { TwitchExtensionFormKeys.Hero, player.Character },
                    { TwitchExtensionFormKeys.Region, player.BattleNetRegionId.ToString() }
                }).ToList()
            };
        }

        private HeroesProfileTwitchPayload CreatePlayer(Replay replay)
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.CreatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>(sharedFormData)
                {
                    { TwitchExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                    { TwitchExtensionFormKeys.BattleTag, player.Name },
                    { TwitchExtensionFormKeys.Team, player.Team.ToString() },
                }).ToList()
            };
        }

        private HeroesProfileTwitchPayload CreateSession()
        {
            return new HeroesProfileTwitchPayload
            {
                Step = HeroesProfileTwitchExtensionStep.CreateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new (sharedFormData)
                    {
                        { TwitchExtensionFormKeys.GameDate, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    }
                }
            };
        }
    }
}