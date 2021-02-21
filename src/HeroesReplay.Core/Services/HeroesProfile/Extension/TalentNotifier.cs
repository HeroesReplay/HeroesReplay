using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class TalentNotifier : ITalentNotifier
    {
        private readonly ILogger<TalentNotifier> logger;
        private readonly ISessionHolder sessionHolder;
        private readonly IHeroesProfileService heroesProfileService;

        private SessionData Data => sessionHolder.SessionData;
        private string SessionId { get; set; }
        public bool SessionCreated => !string.IsNullOrWhiteSpace(SessionId);

        public TalentNotifier(ILogger<TalentNotifier> logger, ISessionHolder sessionHolder, IHeroesProfileService heroesProfileService)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.sessionHolder = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.heroesProfileService = heroesProfileService ?? throw new ArgumentNullException(nameof(heroesProfileService));
        }

        public async Task SendCurrentTalentsAsync(TimeSpan timer, CancellationToken token = default)
        {
            if (Data.Payloads.Create.Any())
            {
                await SendCreatePayloadsAsync(token).ConfigureAwait(false);
            }
            else if (Data.Payloads.Update.Any())
            {
                await SendUpdatePayloadsAsync(token).ConfigureAwait(false);
            }
            else
            {
                await UpdateTalentsAsync(timer, token).ConfigureAwait(false);
            }
        }

        private async Task SendCreatePayloadsAsync(CancellationToken token = default)
        {
            ExtensionPayload createReplayPayload = Data.Payloads.Create.Find(p => p.Step == ExtensionStep.CreateReplayData);

            if (createReplayPayload != null)
            {
                string session = await heroesProfileService.CreateReplaySessionAsync(createReplayPayload, token).ConfigureAwait(false);
                bool success = !string.IsNullOrWhiteSpace(session);

                if (success && Data.Payloads.Create.Remove(createReplayPayload))
                {
                    logger.LogDebug($"removed create replay payload.");
                    logger.LogDebug($"create replay session id: {SessionId}");
                }

                SessionId = session;
            }
            else
            {
                ExtensionPayload createPlayerPayload = Data.Payloads.Create.Find(p => p.Step == ExtensionStep.CreatePlayerData);

                if (createPlayerPayload != null)
                {
                    bool success = await heroesProfileService.CreatePlayerDataAsync(createPlayerPayload, SessionId, token).ConfigureAwait(false);

                    if (success && Data.Payloads.Create.Remove(createPlayerPayload))
                    {
                        logger.LogDebug("Removed create player payload.");
                    }
                }
            }
        }

        private async Task SendUpdatePayloadsAsync(CancellationToken token = default)
        {
            ExtensionPayload updateReplayPayload = Data.Payloads.Update.Find(p => p.Step == ExtensionStep.UpdateReplayData);

            if (updateReplayPayload != null)
            {
                bool success = await heroesProfileService.UpdateReplayDataAsync(updateReplayPayload, SessionId, token).ConfigureAwait(false);

                if (success && Data.Payloads.Update.Remove(updateReplayPayload))
                {
                    logger.LogDebug($"removed update replay payload.");
                }
            }
            else
            {
                ExtensionPayload updatePlayerPayload = Data.Payloads.Update.Find(p => p.Step == ExtensionStep.UpdatePlayerData);

                if (updatePlayerPayload != null)
                {
                    bool success = await heroesProfileService.UpdatePlayerDataAsync(updatePlayerPayload, SessionId, token).ConfigureAwait(false);

                    if (success)
                    {
                        Data.Payloads.Update.Remove(updatePlayerPayload);
                        logger.LogDebug($"removed update player payload.");
                    }
                }
            }
        }

        private async Task UpdateTalentsAsync(TimeSpan timer, CancellationToken token = default)
        {
            foreach (TimeSpan talentTime in Data.Payloads.Talents.Keys)
            {
                if (talentTime <= timer)
                {
                    logger.LogInformation($"Talents at {talentTime} was found during timer: {timer}");

                    bool success = await heroesProfileService.UpdatePlayerTalentsAsync(Data.Payloads.Talents[talentTime], SessionId, token).ConfigureAwait(false);

                    if (success && Data.Payloads.Talents.Remove(talentTime))
                    {
                        logger.LogInformation($"Removed talents key: {talentTime}");

                        bool notified = await heroesProfileService.NotifyTwitchAsync(token).ConfigureAwait(false);

                        if (notified)
                        {
                            logger.LogDebug($"Twitch notify sent");
                        }
                    }
                }
            }
        }

        public void ClearSession() => SessionId = null;
    }
}