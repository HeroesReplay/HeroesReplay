using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public class PayloadsBuilder : IPayloadsBuilder
    {
        public const string PLACEHOLDER_SESSION_ID = nameof(PLACEHOLDER_SESSION_ID);

        private readonly ILogger<PayloadsBuilder> logger;
        private readonly IOptions<AppSettings> settings;
        private readonly List<KeyValuePair<string, string>> sharedFormData;

        public PayloadsBuilder(ILogger<PayloadsBuilder> logger, IOptions<AppSettings> settings)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            sharedFormData = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(FormKeys.TwitchKey, settings.Value.TwitchExtension.ApiKey),
                new KeyValuePair<string, string>(FormKeys.Email, settings.Value.TwitchExtension.ApiEmail),
                new KeyValuePair<string, string>(FormKeys.TwitchUserName, settings.Value.TwitchExtension.TwitchUserName),
                new KeyValuePair<string, string>(FormKeys.UserId, settings.Value.TwitchExtension.ApiUserId),
            };
        }

        public TalentPayloads CreatePayloads(Replay replay)
        {
            if (settings.Value.TwitchExtension.Enabled)
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

        private Dictionary<TimeSpan, List<TalentsPayload>> CreateTalents(Replay replay)
        {
            Dictionary<TimeSpan, List<TalentsPayload>> talentEvents = new Dictionary<TimeSpan, List<TalentsPayload>>();

            if (replay.TrackerEvents != null)
            {
                foreach (var trackerEvent in replay.TrackerEvents)
                {
                    if (trackerEvent.Data.dictionary[0].blobText == settings.Value.TrackerEvents.TalentChosen)
                    {
                        string talentName = trackerEvent.Data.dictionary[1].optionalData.array[0].dictionary[1].blobText;
                        TimeSpan timeSpan = trackerEvent.TimeSpan;
                        int playerID = Convert.ToInt32(trackerEvent.Data.dictionary[2].optionalData.array[0].dictionary[1].vInt.Value);
                        Player player = replay.Players[playerID - 1];

                        if (!talentEvents.ContainsKey(timeSpan))
                        {
                            talentEvents[timeSpan] = new List<TalentsPayload>();
                        }

                        var talents = talentEvents[timeSpan];

                        talents.Add(new TalentsPayload()
                        {
                            Step = ExtensionStep.SaveTalentData,
                            Content = new List<Dictionary<string, string>>()
                            {
                                new (sharedFormData)
                                {
                                    { FormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                                    { FormKeys.BlizzId, player.BattleNetId.ToString() },
                                    { FormKeys.BattleTag, $"{player.Name}#{player.BattleTag}" },
                                    { FormKeys.Region, player.BattleNetRegionId.ToString() },
                                    { FormKeys.Talent, talentName },
                                    { FormKeys.Hero, player.Character },
                                    { FormKeys.HeroId, player.HeroId },
                                    { FormKeys.HeroAttributeId, player.HeroAttributeId }
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

        private TalentsPayload UpdateReplay(Replay replay)
        {
            return new TalentsPayload
            {
                Step = ExtensionStep.UpdateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new (sharedFormData)
                    {
                        { FormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                        { FormKeys.GameType, replay.GameMode.ToString() },
                        { FormKeys.GameMap, replay.Map },
                        { FormKeys.GameVersion, replay.ReplayVersion },
                        { FormKeys.Region, replay.Players[0].BattleNetRegionId.ToString() },
                    }
                }
            };
        }

        private TalentsPayload UpdatePlayer(Replay replay)
        {
            return new TalentsPayload
            {
                Step = ExtensionStep.UpdatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>(sharedFormData)
                {
                    { FormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                    { FormKeys.BlizzId, player.BattleNetId.ToString() },
                    { FormKeys.BattleTag, $"{player.Name}#{player.BattleTag}" },
                    { FormKeys.Hero, player.Character },
                    { FormKeys.HeroId, player.HeroId },
                    { FormKeys.HeroAttributeId, player.HeroAttributeId },
                    { FormKeys.Team, player.Team.ToString() },
                    { FormKeys.Region, player.BattleNetRegionId.ToString() }
                }).ToList()
            };
        }

        private TalentsPayload CreatePlayer(Replay replay)
        {
            return new TalentsPayload
            {
                Step = ExtensionStep.CreatePlayerData,
                Content = replay.Players.Select(player => new Dictionary<string, string>(sharedFormData)
                {
                    { FormKeys.SessionId, PLACEHOLDER_SESSION_ID },
                    { FormKeys.BattleTag, $"{player.Name}#{player.BattleTag}" },
                    { FormKeys.Team, player.Team.ToString() },
                }).ToList()
            };
        }

        private TalentsPayload CreateSession()
        {
            return new TalentsPayload
            {
                Step = ExtensionStep.CreateReplayData,
                Content = new List<Dictionary<string, string>>()
                {
                    new (sharedFormData)
                    {
                        { FormKeys.GameDate, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    }
                }
            };
        }
    }
}