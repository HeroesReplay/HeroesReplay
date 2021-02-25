using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using Polly;

namespace HeroesReplay.Core.Services.Providers
{
    public class HeroesProfileProvider : IReplayProvider
    {
        private readonly AppSettings settings;
        private readonly ConsoleTokenProvider provider;
        private readonly ILogger<HeroesProfileProvider> logger;
        private readonly IReplayLoader replayLoader;
        private readonly IReplayHelper replayHelper;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly IRequestQueue requestQueue;
        private int minReplayId;

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (StandardDirectory.GetFiles(settings.StormReplay.WildCard).Any())
                    {
                        FileInfo latest = StandardDirectory.GetFiles(settings.StormReplay.WildCard).OrderByDescending(f => f.CreationTime).FirstOrDefault();

                        if (replayHelper.TryGetReplayId(latest.Name, out int replayId))
                        {
                            MinReplayId = replayId;
                        }
                    }
                    else
                    {
                        MinReplayId = settings.HeroesProfileApi.MinReplayId;
                    }
                }

                return minReplayId;
            }
            set => minReplayId = value;
        }

        private DirectoryInfo StandardDirectory
        {
            get
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(settings.ReplayCachePath);
                if (!directoryInfo.Exists) directoryInfo.Create();
                return directoryInfo;
            }
        }

        private DirectoryInfo RequestsDirectory
        {
            get
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(settings.RequestedReplayCachePath);
                if (!directoryInfo.Exists) directoryInfo.Create();
                return directoryInfo;
            }
        }

        public HeroesProfileProvider(ILogger<HeroesProfileProvider> logger, IReplayLoader replayLoader, IReplayHelper replayHelper, IRequestQueue requestQueue, IHeroesProfileService heroesProfileService, ConsoleTokenProvider provider, AppSettings settings)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.replayLoader = replayLoader ?? throw new ArgumentNullException(nameof(replayLoader));
            this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
            this.requestQueue = requestQueue ?? throw new ArgumentNullException(nameof(requestQueue));
            this.heroesProfileService = heroesProfileService ?? throw new ArgumentNullException(nameof(heroesProfileService));
        }

        public async Task<LoadedReplay> TryLoadNextReplayAsync()
        {
            if (settings.Twitch.EnableRequests)
            {
                RewardQueueItem item = await requestQueue.DequeueItemAsync();

                if (item != null)
                {
                    logger.LogInformation("Reward request item found, loading...");

                    return await GetNextRequestedReplayAsync(item);
                }
            }

            return await GetNextStandardReplayAsync();
        }

        private async Task<LoadedReplay> GetNextRequestedReplayAsync(RewardQueueItem item)
        {
            try
            {
                if (item != null)
                {
                    FileInfo fileInfo = GetFileInfo(RequestsDirectory, item.HeroesProfileReplay);

                    if (!fileInfo.Exists)
                    {
                        await DownloadReplayAsync(item.HeroesProfileReplay, fileInfo).ConfigureAwait(false);
                    }

                    Replay replay = await replayLoader.LoadAsync(fileInfo.FullName).ConfigureAwait(false);

                    return new LoadedReplay
                    {
                        ReplayId = item.HeroesProfileReplay.Id,
                        RewardQueueItem = item,
                        HeroesProfileReplay = item.HeroesProfileReplay,
                        FileInfo = fileInfo,
                        Replay = replay
                    };
                }
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Could not provide a Replay file using HeroesProfile API.");
            }

            return null;
        }

        private async Task<LoadedReplay> GetNextStandardReplayAsync()
        {
            try
            {
                HeroesProfileReplay heroesProfileReplay = await GetNextReplayAsync().ConfigureAwait(false);

                if (heroesProfileReplay != null)
                {
                    FileInfo fileInfo = GetFileInfo(StandardDirectory, heroesProfileReplay);

                    if (!fileInfo.Exists)
                    {
                        await DownloadReplayAsync(heroesProfileReplay, fileInfo).ConfigureAwait(false);
                    }

                    Replay replay = await replayLoader.LoadAsync(fileInfo.FullName).ConfigureAwait(false);

                    return new LoadedReplay
                    {
                        ReplayId = heroesProfileReplay.Id,
                        HeroesProfileReplay = heroesProfileReplay,
                        FileInfo = fileInfo,
                        Replay = replay,
                        RewardQueueItem = null
                    };
                }
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Could not provide a Replay file using HeroesProfile API.");
            }

            return null;
        }

        private async Task DownloadReplayAsync(HeroesProfileReplay replay, FileInfo fileInfo)
        {
            var credentials = new BasicAWSCredentials(settings.HeroesProfileApi.AwsAccessKey, settings.HeroesProfileApi.AwsSecretKey);

            using (AmazonS3Client s3Client = new AmazonS3Client(credentials, RegionEndpoint.GetBySystemName(settings.HeroesProfileApi.S3Region)))
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    RequestPayer = RequestPayer.Requester,
                    BucketName = settings.HeroesProfileApi.S3Bucket,
                    Key = replay.Url.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)
                };

                using (GetObjectResponse response = await s3Client.GetObjectAsync(request, provider.Token).ConfigureAwait(false))
                {
                    await using (MemoryStream memoryStream = new MemoryStream())
                    {
                        await response.ResponseStream.CopyToAsync(memoryStream, provider.Token).ConfigureAwait(false);

                        await using (var stream = fileInfo.OpenWrite())
                        {
                            await stream.WriteAsync(memoryStream.ToArray(), provider.Token).ConfigureAwait(false);
                            await stream.FlushAsync(provider.Token).ConfigureAwait(false);
                        }

                        logger.LogInformation($"downloaded heroesprofile replay.");
                    }
                }
            }
        }

        private FileInfo GetFileInfo(DirectoryInfo directory, HeroesProfileReplay replay)
        {
            var path = directory.FullName;

            var segments = new string[]
            {
                $"{replay.Id}",
                replay.GameType,
                replay.Rank ?? "Unknown",
                replay.Map,
                replay.Fingerprint,
                settings.StormReplay.FileExtension
            };

            var name = string.Join(settings.StormReplay.Seperator, segments);
            return new FileInfo(Path.Combine(path, name));
        }

        private async Task<HeroesProfileReplay> GetNextReplayAsync()
        {
            try
            {
                return await Policy
                       .Handle<Exception>()
                       .OrResult<HeroesProfileReplay>(replay => replay == null)
                       .WaitAndRetryAsync(60, retry => settings.HeroesProfileApi.APIRetryWaitTime)
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> replays = await heroesProfileService.GetReplaysByMinId(MinReplayId).ConfigureAwait(false);

                           if (replays != null && replays.Any())
                           {
                               logger.LogInformation("Finding replay that fits criteria.");

                               HeroesProfileReplay found = replays
                                        .Where(r => r.Id > MinReplayId && r.Rank != null && settings.HeroesProfileApi.GameTypes.Contains(r.GameType, StringComparer.CurrentCultureIgnoreCase))
                                        .OrderBy(x => x.Id)
                                        .FirstOrDefault();

                               if (found == null)
                               {
                                   logger.LogWarning($"Replay not found with criteria. MinReplayId = {MinReplayId}");
                                   MinReplayId = replays.Max(x => x.Id);
                               }
                               else
                               {
                                   logger.LogInformation($"Replay found. MinReplayId = {MinReplayId}");
                                   MinReplayId = found.Id;
                                   return found;
                               }
                           }

                           return null;

                       }, provider.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get the next replay file.");
            }

            return null;
        }
    }
}
