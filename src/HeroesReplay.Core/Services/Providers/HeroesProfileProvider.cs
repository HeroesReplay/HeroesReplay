using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Queue;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;

namespace HeroesReplay.Core.Services.Providers
{
    public class HeroesProfileProvider : IReplayProvider
    {
        private readonly IOptions<AppSettings> settings;
        private readonly CancellationTokenSource cts;
        private readonly ILogger<HeroesProfileProvider> logger;
        private readonly IReplayLoader loader;
        private readonly IReplayHelper helper;
        private readonly IHeroesProfileService hpService;
        private readonly IRequestQueueDequeuer queue;
        private int minReplayId;

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (StandardDirectory.GetFiles(settings.Value.StormReplay.WildCard).Any())
                    {
                        FileInfo latest = StandardDirectory
                            .GetFiles(settings.Value.StormReplay.WildCard)
                            .OrderByDescending(f => int.Parse(Path.GetFileName(f.FullName).Split(settings.Value.StormReplay.Seperator)[0]))
                            .FirstOrDefault();

                        if (helper.TryGetReplayId(latest.Name, out int replayId))
                        {
                            MinReplayId = replayId;
                        }
                    }
                    else
                    {
                        MinReplayId = settings.Value.HeroesProfileApi.MinReplayId;
                    }
                }

                return minReplayId;
            }
            set => minReplayId = value;
        }

        private DirectoryInfo StandardDirectory => Directory.CreateDirectory(settings.Value.StandardReplayCachePath);

        private DirectoryInfo RequestsDirectory => Directory.CreateDirectory(settings.Value.RequestedReplayCachePath);

        public HeroesProfileProvider(ILogger<HeroesProfileProvider> logger, IOptions<AppSettings> settings, IReplayLoader loader, IReplayHelper helper, IRequestQueueDequeuer queue, IHeroesProfileService hpService, CancellationTokenSource cts)
        {
            this.cts = cts ?? throw new ArgumentNullException(nameof(cts));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.helper = helper ?? throw new ArgumentNullException(nameof(helper));
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.hpService = hpService ?? throw new ArgumentNullException(nameof(hpService));
        }

        public async Task<LoadedReplay> TryLoadNextReplayAsync()
        {
            RewardQueueItem item = await queue.DequeueItemAsync();

            if (item != null)
            {
                logger.LogInformation("Reward request item found, loading...");
                return await GetNextRequestedReplayAsync(item);
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

                    fileInfo.Refresh();

                    Replay replay = await loader.LoadAsync(fileInfo.FullName).ConfigureAwait(false);

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

                    fileInfo.Refresh();

                    Replay replay = await loader.LoadAsync(fileInfo.FullName).ConfigureAwait(false);

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
            var credentials = new BasicAWSCredentials(settings.Value.HeroesProfileApi.AwsAccessKey, settings.Value.HeroesProfileApi.AwsSecretKey);

            using (AmazonS3Client s3Client = new AmazonS3Client(credentials, RegionEndpoint.GetBySystemName(settings.Value.HeroesProfileApi.S3Region)))
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    RequestPayer = RequestPayer.Requester,
                    BucketName = settings.Value.HeroesProfileApi.S3Bucket,
                    Key = replay.Url.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)
                };

                using (GetObjectResponse response = await s3Client.GetObjectAsync(request, cts.Token).ConfigureAwait(false))
                {
                    await using (MemoryStream memoryStream = new MemoryStream())
                    {
                        await response.ResponseStream.CopyToAsync(memoryStream, cts.Token).ConfigureAwait(false);

                        await using (var stream = fileInfo.OpenWrite())
                        {
                            await stream.WriteAsync(memoryStream.ToArray(), cts.Token).ConfigureAwait(false);
                            await stream.FlushAsync(cts.Token).ConfigureAwait(false);
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
                settings.Value.StormReplay.FileExtension
            };

            var name = string.Join(settings.Value.StormReplay.Seperator, segments);
            return new FileInfo(Path.Combine(path, name));
        }

        private async Task<HeroesProfileReplay> GetNextReplayAsync()
        {
            try
            {
                return await Policy
                       .Handle<Exception>()
                       .OrResult<HeroesProfileReplay>(replay => replay == null)
                       .WaitAndRetryAsync(60, retry => settings.Value.HeroesProfileApi.APIRetryWaitTime)
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> replays = await hpService.GetReplaysByMinId(MinReplayId).ConfigureAwait(false);

                           if (replays != null && replays.Any())
                           {
                               logger.LogInformation("Finding replay that fits criteria.");

                               HeroesProfileReplay found = replays
                                        .Where(r => r.Id > MinReplayId && r.Rank != null && settings.Value.HeroesProfileApi.GameTypes.Contains(r.GameType, StringComparer.CurrentCultureIgnoreCase))
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

                       }, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get the next replay file.");
            }

            return null;
        }
    }
}
