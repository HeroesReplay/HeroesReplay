using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class ExtensionPayloadBuilder : IExtensionPayloadsBuilder
    {
        public const string PLACEHOLDER_SESSION_ID = nameof(PLACEHOLDER_SESSION_ID);

        private readonly ILogger<ExtensionPayloadBuilder> logger;
        private readonly AppSettings settings;
        private readonly List<KeyValuePair<string, string>> sharedFormData;

        public ExtensionPayloadBuilder(ILogger<ExtensionPayloadBuilder> logger, AppSettings settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            sharedFormData = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(ExtensionFormKeys.TwitchKey, settings.TwitchExtension.ApiKey),
                new KeyValuePair<string, string>(ExtensionFormKeys.Email, settings.TwitchExtension.ApiEmail),
                new KeyValuePair<string, string>(ExtensionFormKeys.TwitchUserName, settings.TwitchExtension.TwitchUserName),
                new KeyValuePair<string, string>(ExtensionFormKeys.UserId, settings.TwitchExtension.ApiUserId),
            };
        }

        public TalentPayloads CreatePayloads(Replay replay)
        {
            if (settings.TwitchExtension.Enabled)
            {
                TalentPayloads payloads = new();

                payloads.Create.Add(CreateSession());
                payloads.Create.Add(CreatePlayer(replay));

                payloads.Update.Add(UpdateReplay(replay));
                payloads.Update.Add(UpdatePlayer(replay));

                payloads.Talents = CreateTalents(replay);

                return payloads;
            }

            return null;
        }

        private Dictionary<TimeSpan, List<ExtensionPayload>> CreateTalents(Replay replay)
        {
            Dictionary<TimeSpan, List<ExtensionPayload>> talentEvents = new Dictionary<TimeSpan, List<ExtensionPayload>>();

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
                            talentEvents[timeSpan] = new List<ExtensionPayload>();
                        }

                        var talents = talentEvents[timeSpan];

                        talents.Add(new ExtensionPayload()
                        {
                            Step = ExtensionStep.SaveTalentData,
                            Content = new List<Dictionary<string, string>>()
                            {
                                new (sharedFormData)
                                {
                                    { ExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                                    { ExtensionFormKeys.BlizzId, player.BattleNetId.ToString() },
                                    { ExtensionFormKeys.BattleTag, $"{player.Name}#{player.BattleTag}" },
                                    { ExtensionFormKeys.Region, player.BattleNetRegionId.ToString() },
                                    { ExtensionFormKeys.Talent, talentName },
                                    { ExtensionFormKeys.Hero, player.Character },
                                    { ExtensionFormKeys.HeroId, player.HeroId },
                                    { ExtensionFormKeys.HeroAttributeId, player.HeroAttributeId }
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

        private ExtensionPayload UpdateReplay(Replay replay)
        {
            return new ExtensionPayload
            {
                Step = ExtensionStep.UpdateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new (sharedFormData)
                    {
                        { ExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                        { ExtensionFormKeys.GameType, replay.GameMode.ToString() },
                        { ExtensionFormKeys.GameMap, replay.Map },
                        { ExtensionFormKeys.GameVersion, replay.ReplayVersion },
                        { ExtensionFormKeys.Region, replay.Players[0].BattleNetRegionId.ToString() },
                    }
                }
            };
        }

        private ExtensionPayload UpdatePlayer(Replay replay)
        {
            return new ExtensionPayload
            {
                Step = ExtensionStep.UpdatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>(sharedFormData)
                {
                    { ExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                    { ExtensionFormKeys.BlizzId, player.BattleNetId.ToString() },
                    { ExtensionFormKeys.BattleTag, $"{player.Name}#{player.BattleTag}" },
                    { ExtensionFormKeys.Hero, player.Character },
                    { ExtensionFormKeys.HeroId, player.HeroId },
                    { ExtensionFormKeys.HeroAttributeId, player.HeroAttributeId },
                    { ExtensionFormKeys.Team, player.Team.ToString() },
                    { ExtensionFormKeys.Region, player.BattleNetRegionId.ToString() }
                }).ToList()
            };
        }

        private ExtensionPayload CreatePlayer(Replay replay)
        {
            return new ExtensionPayload
            {
                Step = ExtensionStep.CreatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>(sharedFormData)
                {
                    { ExtensionFormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                    { ExtensionFormKeys.BattleTag, $"{player.Name}#{player.BattleTag}" },
                    { ExtensionFormKeys.Team, player.Team.ToString() },
                }).ToList()
            };
        }

        private ExtensionPayload CreateSession()
        {
            return new ExtensionPayload
            {
                Step = ExtensionStep.CreateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new (sharedFormData)
                    {
                        { ExtensionFormKeys.GameDate, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    }
                }
            };
        }
    }
}