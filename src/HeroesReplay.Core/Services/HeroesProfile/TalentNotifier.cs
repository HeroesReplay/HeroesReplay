using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class TalentNotifier : ITalentNotifier
    {
        private readonly ILogger<TalentNotifier> logger;
        private readonly ISessionHolder sessionHolder;
        private readonly IHeroesProfileService heroesProfileService;

        private SessionData Data => this.sessionHolder.SessionData;
        private string SessionId { get; set; }
        public bool SessionCreated => !string.IsNullOrWhiteSpace(SessionId);

        public TalentNotifier(ILogger<TalentNotifier> logger, ISessionHolder sessionHolder, IHeroesProfileService heroesProfileService)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.sessionHolder = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.heroesProfileService = heroesProfileService ?? throw new ArgumentNullException(nameof(heroesProfileService));
        }

        public async Task SendCurrentTalentsAsync(TimeSpan timer)
        {
            if (this.Data.Payloads.Create.Any())
            {
                await SendCreatePayloadsAsync().ConfigureAwait(false);
            }
            else if (this.Data.Payloads.Update.Any() && !string.IsNullOrWhiteSpace(SessionId))
            {
                await SendUpdatePayloadsAsync().ConfigureAwait(false);
            }
            else
            {
                await UpdateTalentsAsync(timer).ConfigureAwait(false);
            }
        }

        private async Task SendCreatePayloadsAsync()
        {
            var createReplayPayload = this.Data.Payloads.Create.Find(p => p.Step == HeroesProfileTwitchExtensionStep.CreateReplayData);

            if (createReplayPayload != null)
            {
                var session = await heroesProfileService.CreateReplaySessionAsync(createReplayPayload).ConfigureAwait(false);
                var success = !string.IsNullOrWhiteSpace(session);

                if (success)
                {
                    this.Data.Payloads.Create.Remove(createReplayPayload);
                    SessionId = session;
                }
            }
            else
            {
                var createPlayerPayload = this.Data.Payloads.Create.Find(p => p.Step == HeroesProfileTwitchExtensionStep.CreatePlayerData);

                if (createPlayerPayload != null)
                {
                    bool success = await heroesProfileService.CreatePlayerDataAsync(createPlayerPayload, SessionId).ConfigureAwait(false);

                    if (success)
                    {
                        this.Data.Payloads.Create.Remove(createPlayerPayload);
                    }
                }
            }
        }

        private async Task SendUpdatePayloadsAsync()
        {
            var updateReplayPayload = this.Data.Payloads.Update.Find(p => p.Step == HeroesProfileTwitchExtensionStep.UpdateReplayData);

            if (updateReplayPayload != null)
            {
                bool success = await heroesProfileService.UpdateReplayDataAsync(updateReplayPayload, SessionId).ConfigureAwait(false);

                if (success)
                {
                    this.Data.Payloads.Update.Remove(updateReplayPayload);
                }
            }
            else
            {
                var updatePlayerPayload = this.Data.Payloads.Update.Find(p => p.Step == HeroesProfileTwitchExtensionStep.UpdatePlayerData);

                if (updatePlayerPayload != null)
                {
                    bool success = await heroesProfileService.UpdatePlayerDataAsync(updatePlayerPayload, SessionId).ConfigureAwait(false);

                    if (success)
                    {
                        this.Data.Payloads.Update.Remove(updatePlayerPayload);
                    }
                }
            }
        }

        private async Task UpdateTalentsAsync(TimeSpan timer)
        {
            foreach (TimeSpan talentTime in Data.Payloads.Talents.Keys)
            {
                if (talentTime <= timer)
                {
                    logger.LogInformation($"Talents at {talentTime} was found during timer: {timer}");

                    bool success = await heroesProfileService.UpdatePlayerTalentsAsync(Data.Payloads.Talents[talentTime], SessionId).ConfigureAwait(false);

                    if (success)
                    {
                        if (Data.Payloads.Talents.Remove(talentTime))
                        {
                            logger.LogInformation($"Removed talents key: {talentTime}");
                        }

                        await heroesProfileService.NotifyTwitchAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public void ClearSession()
        {
            SessionId = null;
        }
    }
}