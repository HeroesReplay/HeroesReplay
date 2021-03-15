using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Analyzer;
using HeroesReplay.Service.Spectator.Core.Context;
using Microsoft.Extensions.Logging;
using ContextData = HeroesReplay.Service.Spectator.Core.Context.ContextData;

namespace HeroesReplay.Service.Spectator.Core.HeroesProfileExtension
{
    public class TalentNotifier : ITalentNotifier
    {
        private readonly ILogger<TalentNotifier> logger;
        private readonly IReplayContext context;
        private readonly ITwitchExtensionService extensionService;

        private ContextData Data => context.Current;

        private string SessionId { get; set; }

        public TalentNotifier(ILogger<TalentNotifier> logger, IReplayContext context, ITwitchExtensionService extensionService)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.extensionService = extensionService ?? throw new ArgumentNullException(nameof(extensionService));
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
            TalentsPayload createReplayPayload = Data.Payloads.Create.Find(p => p.Step == ExtensionStep.CreateReplayData);

            if (createReplayPayload != null)
            {
                string session = await extensionService.CreateReplaySessionAsync(createReplayPayload, token).ConfigureAwait(false);
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
                TalentsPayload createPlayerPayload = Data.Payloads.Create.Find(p => p.Step == ExtensionStep.CreatePlayerData);

                if (createPlayerPayload != null)
                {
                    bool success = await extensionService.CreatePlayerDataAsync(createPlayerPayload, SessionId, token).ConfigureAwait(false);

                    if (success && Data.Payloads.Create.Remove(createPlayerPayload))
                    {
                        logger.LogDebug("Removed create player payload.");
                    }
                }
            }
        }

        private async Task SendUpdatePayloadsAsync(CancellationToken token = default)
        {
            TalentsPayload updateReplayPayload = Data.Payloads.Update.Find(p => p.Step == ExtensionStep.UpdateReplayData);

            if (updateReplayPayload != null)
            {
                bool success = await extensionService.UpdateReplayDataAsync(updateReplayPayload, SessionId, token).ConfigureAwait(false);

                if (success && Data.Payloads.Update.Remove(updateReplayPayload))
                {
                    logger.LogDebug($"removed update replay payload.");
                }
            }
            else
            {
                TalentsPayload updatePlayerPayload = Data.Payloads.Update.Find(p => p.Step == ExtensionStep.UpdatePlayerData);

                if (updatePlayerPayload != null)
                {
                    bool success = await extensionService.UpdatePlayerDataAsync(updatePlayerPayload, SessionId, token).ConfigureAwait(false);

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

                    bool success = await extensionService.UpdatePlayerTalentsAsync(Data.Payloads.Talents[talentTime], SessionId, token).ConfigureAwait(false);

                    if (success && Data.Payloads.Talents.Remove(talentTime))
                    {
                        logger.LogInformation($"Removed talents key: {talentTime}");

                        bool notified = await extensionService.NotifyTwitchAsync(token).ConfigureAwait(false);

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