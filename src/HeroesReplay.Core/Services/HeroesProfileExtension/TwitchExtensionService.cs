using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Shared;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public class TwitchExtensionService : ITwitchExtensionService
    {
        public const string ExtensionApiClient = nameof(ExtensionApiClient);

        private readonly FormUrlEncodedContent notifyContent;
        private readonly ILogger<TwitchExtensionService> logger;
        private readonly HttpClient httpClient;
        private readonly ConsoleTokenProvider tokenProvider;

        private const string SaveReplayUrl = @"save/replay";
        private const string UpdateReplayUrl = @"update/replay/";
        private const string SavePlayerUrl = @"save/player";
        private const string UpdatePlayerUrl = @"update/player";
        private const string SaveTalentUrl = @"save/talent";
        private const string NotifyTalentUpdate = @"notify/talent/update";

        public TwitchExtensionService(ILogger<TwitchExtensionService> logger, HttpClient httpClient, ConsoleTokenProvider tokenProvider, AppSettings settings)
        {
            notifyContent = new(new Dictionary<string, string>
            {
                { ExtensionFormKeys.TwitchKey, settings.TwitchExtension.ApiKey },
                { ExtensionFormKeys.Email, settings.TwitchExtension.ApiEmail },
                { ExtensionFormKeys.TwitchUserName, settings.TwitchExtension.TwitchUserName },
                { ExtensionFormKeys.UserId, settings.TwitchExtension.ApiUserId }
            });

            this.logger = logger;
            this.httpClient = httpClient;
            this.tokenProvider = tokenProvider;

            this.httpClient.BaseAddress = settings.HeroesProfileApi.TwitchBaseUri;
        }

        public async Task<string> CreateReplaySessionAsync(ExtensionPayload payload, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            try
            {
                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    HttpResponseMessage response = await Policy
                               .Handle<Exception>()
                               .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                               .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                               .ExecuteAsync(async (context, token) => await httpClient.PostAsync(SaveReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), cancellationSource.Token), new Context(), tokenProvider.Token)
                               .ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync();

                        if (int.TryParse(result, out _))
                        {
                            return result;
                        }
                    }
                    else
                    {
                        logger.LogError($"{response.StatusCode} ({response.ReasonPhrase})");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogWarning("Could not create twitch extension replay session because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not create session.");
            }

            return null;
        }

        public async Task<bool> CreatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                payload.SetGameSessionReplayId(sessionId);

                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    foreach (var content in payload.Content)
                    {
                        HttpResponseMessage response = await Policy
                            .Handle<Exception>()
                            .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                            .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                            .ExecuteAsync(async (context, token) => await httpClient.PostAsync(SavePlayerUrl, new FormUrlEncodedContent(content), token), new Context(), cancellationSource.Token)
                            .ConfigureAwait(false);

                        responses.Add(response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Could not create twitch extension player data because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not create player data.");
            }

            return responses.All(response => response.IsSuccessStatusCode);
        }

        public async Task<bool> UpdateReplayDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            try
            {
                payload.SetGameSessionReplayId(sessionId);

                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    HttpResponseMessage response = await Policy
                               .Handle<Exception>()
                               .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                               .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                               .ExecuteAsync(async (context, token) => await httpClient.PostAsync(UpdateReplayUrl, new FormUrlEncodedContent(payload.Content.Single()), token), new Context(), cancellationSource.Token)
                               .ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Could not update twitch extension replay data because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update replay data.");
            }

            return false;
        }

        public async Task<bool> UpdatePlayerDataAsync(ExtensionPayload payload, string sessionId, CancellationToken token)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                payload.SetGameSessionReplayId(sessionId);

                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    foreach (var playerContent in payload.Content)
                    {
                        HttpResponseMessage response = await Policy
                                 .Handle<Exception>()
                                 .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                 .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                                 .ExecuteAsync(async (context, t) => await httpClient.PostAsync(UpdatePlayerUrl, new FormUrlEncodedContent(playerContent), t), new Context(), cancellationSource.Token)
                                 .ConfigureAwait(false);

                        responses.Add(response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Could not update twitch extension player data because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not update player data.");
            }

            return responses.All(x => x.IsSuccessStatusCode);
        }

        public async Task<bool> UpdatePlayerTalentsAsync(List<ExtensionPayload> talentPayloads, string sessionId, CancellationToken token)
        {
            if (talentPayloads == null)
                throw new ArgumentNullException(nameof(talentPayloads));

            if (sessionId == null)
                throw new ArgumentNullException(nameof(sessionId));

            List<HttpResponseMessage> responses = new List<HttpResponseMessage>();

            try
            {
                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    foreach (var talentPayload in talentPayloads)
                    {
                        talentPayload.SetGameSessionReplayId(sessionId);

                        foreach (var content in talentPayload.Content)
                        {
                            HttpResponseMessage response = await Policy
                                .Handle<Exception>()
                                .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                .WaitAndRetryAsync(retryCount: 10, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                                .ExecuteAsync(async (context, token) => await httpClient.PostAsync(SaveTalentUrl, new FormUrlEncodedContent(content), token), new Context(), cancellationSource.Token)
                                .ConfigureAwait(false);

                            responses.Add(response);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Could not update twitch extension talents because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, $"Could not update talents.");
            }

            return responses.All(response => response.IsSuccessStatusCode);
        }

        public async Task<bool> NotifyTwitchAsync(CancellationToken token)
        {
            try
            {
                using (CancellationTokenSource cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, tokenProvider.Token))
                {
                    HttpResponseMessage response = await Policy
                                     .Handle<Exception>()
                                     .OrResult<HttpResponseMessage>(msg => !msg.IsSuccessStatusCode)
                                     .WaitAndRetryAsync(retryCount: 5, sleepDurationProvider: this.GetSleepDuration, onRetry: this.OnRetry)
                                     .ExecuteAsync(async (context, token) => await httpClient.PostAsync(NotifyTalentUpdate, notifyContent, token), new Context(), cancellationSource.Token)
                                     .ConfigureAwait(false);

                    return response.IsSuccessStatusCode;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Could not notify twitch extension because the task was cancelled.");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not notify.");
            }

            return false;
        }

        private TimeSpan GetSleepDuration(int retry, Context context)
        {
            if (context.ContainsKey("retry-after"))
            {
                var retryAfter = (TimeSpan) context["retry-after"];
                logger.LogInformation($"getting sleep duration for retry attempt {retry}: {retryAfter}");
                return retryAfter;
            }

            return TimeSpan.FromSeconds(1);
        }

        private void OnRetry(DelegateResult<HttpResponseMessage> wrappedResponse, TimeSpan timeSpan, int retryAttempt, Context context)
        {
            if (wrappedResponse.Exception != null)
            {
                logger.LogError(wrappedResponse.Exception, "Error with Heroes Profile Service");
            }

            if (wrappedResponse.Result != null)
            {
                logger.LogDebug($"retry attempt {retryAttempt}: {wrappedResponse.Result.StatusCode}: {wrappedResponse.Result.ReasonPhrase}");
            }

            if (wrappedResponse?.Result?.Headers?.RetryAfter != null)
            {
                TimeSpan retryAfter = wrappedResponse.Result.Headers.RetryAfter.Delta.Value;
                logger.LogWarning($"Setting retry-after to {retryAfter}");
                context["retry-after"] = retryAfter;
            }
        }

    }
}