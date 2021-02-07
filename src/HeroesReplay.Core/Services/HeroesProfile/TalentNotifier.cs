using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
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

        public async Task TryInitializeSessionAsync()
        {
            // Creation Steps
            while (this.Data.Payloads.Create.TryDequeue(out var payload))
            {
                switch (payload.Step)
                {
                    case HeroesProfileTwitchExtensionStep.CreateReplayData:
                        {
                            SessionId = await heroesProfileService.CreateReplaySessionAsync(payload);
                            break;
                        }
                    case HeroesProfileTwitchExtensionStep.CreatePlayerData:
                        {
                            await heroesProfileService.CreatePlayerDataAsync(payload, SessionId);
                            break;
                        }
                }
            }

            // Update Steps
            while (this.Data.Payloads.Update.TryDequeue(out var payload))
            {
                switch (payload.Step)
                {
                    case HeroesProfileTwitchExtensionStep.UpdateReplayData:
                        {
                            await heroesProfileService.UpdateReplayDataAsync(payload, SessionId);
                            break;
                        }
                    case HeroesProfileTwitchExtensionStep.UpdatePlayerData:
                        {
                            await heroesProfileService.UpdatePlayerDataAsync(payload, SessionId);
                            break;
                        }
                }
            }
        }

        public async Task SendCurrentTalentsAsync(TimeSpan timer)
        {
            foreach (TimeSpan talentTime in Data.Payloads.Talents.Keys)
            {
                if (talentTime <= timer)
                {
                    logger.LogInformation($"Talents at {talentTime} was found during timer: {timer}");

                    await heroesProfileService.UpdatePlayerTalentsAsync(Data.Payloads.Talents[talentTime], SessionId);

                    if (Data.Payloads.Talents.Remove(talentTime))
                    {
                        logger.LogInformation($"Removed talents key: {talentTime}");
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