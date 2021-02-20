using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using static Heroes.ReplayParser.DataParser;

namespace HeroesReplay.Core.Providers
{
    public class HeroesProfileProvider : IReplayProvider
    {
        private readonly AppSettings settings;
        private readonly CancellationTokenProvider provider;
        private readonly IReplayHelper replayHelper;
        private readonly ILogger<HeroesProfileProvider> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly IReplayRequestQueue requestQueue;
        private int minReplayId;

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (StandardReplaysDirectory.GetFiles(settings.StormReplay.WildCard).Any())
                    {
                        FileInfo latest = StandardReplaysDirectory.GetFiles(settings.StormReplay.WildCard).OrderByDescending(f => f.CreationTime).FirstOrDefault();

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

        private DirectoryInfo StandardReplaysDirectory
        {
            get
            {
                DirectoryInfo cache = new DirectoryInfo(settings.ReplayCachePath);
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }

        private DirectoryInfo RequestedReplaysDirectory
        {
            get
            {
                DirectoryInfo cache = new DirectoryInfo(settings.RequestedReplayCachePath);
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }

        public HeroesProfileProvider(ILogger<HeroesProfileProvider> logger, IReplayRequestQueue requestQueue, IHeroesProfileService heroesProfileService, CancellationTokenProvider provider, IReplayHelper replayHelper, AppSettings settings)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.replayHelper = replayHelper ?? throw new ArgumentNullException(nameof(replayHelper));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.requestQueue = requestQueue ?? throw new ArgumentNullException(nameof(requestQueue));
            this.heroesProfileService = heroesProfileService ?? throw new ArgumentNullException(nameof(heroesProfileService));
        }

        public async Task<StormReplay> TryLoadReplayAsync()
        {
            if (settings.Twitch.EnableReplayRequests)
            {
                ReplayRequest request = await requestQueue.GetNextRequestAsync();

                if (request != null && request.ReplayId.HasValue)
                {
                    await GetNextRequestedReplayAsync(request.ReplayId.Value);
                }
            }

            return await GetNextStandardReplayAsync();
        }

        private async Task<StormReplay> GetNextRequestedReplayAsync(int replayId)
        {
            try
            {
                HeroesProfileReplay replay = await GetSpecificReplayAsync(replayId).ConfigureAwait(false);

                if (replay != null)
                {
                    FileInfo cacheStormReplay = GetFileInfo(RequestedReplaysDirectory, replay);

                    if (!cacheStormReplay.Exists)
                    {
                        await DownloadStormReplay(replay, cacheStormReplay).ConfigureAwait(false);
                    }

                    StormReplay stormReplay = await TryLoadReplay(replay, cacheStormReplay).ConfigureAwait(false);

                    return stormReplay;
                }
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Could not provide a Replay file using HeroesProfile API.");
            }

            return null;
        }

        private async Task<StormReplay> GetNextStandardReplayAsync()
        {
            try
            {
                HeroesProfileReplay replay = await GetNextReplayAsync().ConfigureAwait(false);

                if (replay != null)
                {
                    FileInfo cacheStormReplay = GetFileInfo(StandardReplaysDirectory, replay);

                    if (!cacheStormReplay.Exists)
                    {
                        await DownloadStormReplay(replay, cacheStormReplay).ConfigureAwait(false);
                    }

                    StormReplay stormReplay = await TryLoadReplay(replay, cacheStormReplay).ConfigureAwait(false);

                    if (stormReplay != null)
                    {
                        MinReplayId = replay.Id;
                    }

                    return stormReplay;
                }
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Could not provide a Replay file using HeroesProfile API.");
            }

            return null;
        }

        private async Task<HeroesProfileReplay> GetSpecificReplayAsync(int replayId)
        {
            Version supportedVersion = settings.Spectate.VersionSupported;

            try
            {
                logger.LogInformation($"Requested ReplayId: {replayId}");

                return await Policy
                       .Handle<Exception>()
                       .WaitAndRetryAsync(5, retry => settings.HeroesProfileApi.APIRetryWaitTime)
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> replays = await heroesProfileService.ListReplaysAllAsync(replayId).ConfigureAwait(false);

                           if (replays != null && replays.Any())
                           {
                               logger.LogInformation($"Finding replay that = {replayId}.");

                               HeroesProfileReplay found = (from replay in replays
                                                            where replay.Url.Host.Contains(settings.HeroesProfileApi.S3Bucket)
                                                            where replay.Valid == 1
                                                            where replay.Id == replayId
                                                            let version = Version.Parse(replay.GameVersion)
                                                            where supportedVersion.Major == version.Major &&
                                                                  supportedVersion.Minor == version.Minor &&
                                                                  supportedVersion.Build == version.Build &&
                                                                  supportedVersion.Revision == version.Revision
                                                            select replay)
                                         .OrderBy(x => x.Id)
                                         .FirstOrDefault();

                               if (found == null)
                               {
                                   logger.LogWarning($"Requested ReplayId {replayId} not found.");
                               }
                               else
                               {
                                   logger.LogInformation($"Requested ReplayId found.");
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

        private async Task<StormReplay> TryLoadReplay(HeroesProfileReplay heroesProfileReplay, FileInfo file)
        {
            logger.LogDebug("id: {0}, url: {1}, path: {2}", heroesProfileReplay.Id, heroesProfileReplay.Url, file.FullName);

            var options = new ParseOptions
            {
                ShouldParseEvents = settings.ParseOptions.ShouldParseEvents,
                AllowPTR = false,
                IgnoreErrors = true,
                ShouldParseMessageEvents = settings.ParseOptions.ShouldParseEvents,
                ShouldParseStatistics = settings.ParseOptions.ShouldParseStatistics,
                ShouldParseMouseEvents = settings.ParseOptions.ShouldParseMouseEvents,
                ShouldParseUnits = settings.ParseOptions.ShouldParseUnits,
                ShouldParseDetailedBattleLobby = settings.ParseOptions.ShouldParseDetailedBattleLobby
            };

            (ReplayParseResult result, Replay replay) = ParseReplay(await File.ReadAllBytesAsync(file.FullName).ConfigureAwait(false), options);

            logger.LogDebug("result: {0}, path: {1}", result, file.FullName);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                return new StormReplay(file.FullName, replay, heroesProfileReplay.Id, heroesProfileReplay.GameType);
            }

            return null;
        }

        private async Task DownloadStormReplay(HeroesProfileReplay replay, FileInfo cachedReplay)
        {
            var credentials = new BasicAWSCredentials(settings.HeroesProfileApi.AwsAccessKey, settings.HeroesProfileApi.AwsSecretKey);

            using (AmazonS3Client s3Client = new AmazonS3Client(credentials, RegionEndpoint.GetBySystemName(settings.HeroesProfileApi.S3Region)))
            {
                GetObjectRequest request = new GetObjectRequest
                {
                    RequestPayer = RequestPayer.Requester,
                    BucketName = replay.Url.Host.Split('.')[0],
                    Key = replay.Url.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)
                };

                using (GetObjectResponse response = await s3Client.GetObjectAsync(request, provider.Token).ConfigureAwait(false))
                {
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        await response.ResponseStream.CopyToAsync(memoryStream, provider.Token).ConfigureAwait(false);

                        using (var stream = cachedReplay.OpenWrite())
                        {
                            await stream.WriteAsync(memoryStream.ToArray(), provider.Token).ConfigureAwait(false);
                            await stream.FlushAsync(provider.Token).ConfigureAwait(false);
                        }

                        logger.LogInformation($"downloaded heroesprofile replay.");
                        logger.LogDebug($"id: {replay.Id}");
                        logger.LogDebug($"s3: {replay.Url}");
                        logger.LogDebug($"path: {cachedReplay.FullName}");
                    }
                }
            }
        }

        private FileInfo GetFileInfo(DirectoryInfo directory, HeroesProfileReplay replay)
        {
            var path = directory.FullName;
            var seperator = settings.StormReplay.Seperator;
            var name = $"{replay.Id}{seperator}{replay.GameType}{seperator}{replay.Fingerprint}{settings.StormReplay.FileExtension}";
            return new FileInfo(Path.Combine(path, name));
        }

        private async Task<HeroesProfileReplay> GetNextReplayAsync()
        {
            Version supportedVersion = settings.Spectate.VersionSupported;

            try
            {
                logger.LogInformation($"MinReplayId: {MinReplayId}");

                return await Policy
                       .Handle<Exception>()
                       .OrResult<HeroesProfileReplay>(replay => replay == null)
                       .WaitAndRetryAsync(60, retry => settings.HeroesProfileApi.APIRetryWaitTime)
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> replays = await heroesProfileService.ListReplaysAllAsync(MinReplayId).ConfigureAwait(false);

                           if (replays != null && replays.Any())
                           {
                               logger.LogInformation("Finding replay that fits criteria.");

                               HeroesProfileReplay found = (from replay in replays
                                                            where replay.Url.Host.Contains(settings.HeroesProfileApi.S3Bucket)
                                                            where replay.Valid == 1
                                                            where replay.Id > MinReplayId
                                                            let version = Version.Parse(replay.GameVersion)
                                                            where supportedVersion.Major == version.Major &&
                                                                  supportedVersion.Minor == version.Minor &&
                                                                  supportedVersion.Build == version.Build &&
                                                                  supportedVersion.Revision == version.Revision
                                                            where settings.HeroesProfileApi.GameTypes.Contains(replay.GameType, StringComparer.CurrentCultureIgnoreCase)
                                                            select replay)
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
