using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Heroes.ReplayParser;

using HeroesReplay.Core.Services.HeroesProfile;
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
        private readonly Settings settings;
        private readonly CancellationTokenProvider provider;
        private readonly ReplayHelper replayHelper;
        private readonly ILogger<HeroesProfileProvider> logger;
        private readonly HeroesProfileService heroesProfileService;
        private int minReplayId;

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (settings.HeroesProfileApi.MinReplayId != settings.HeroesProfileApi.ReplayIdUnset)
                    {
                        MinReplayId = settings.HeroesProfileApi.MinReplayId;
                    }
                    else if (ReplaysDirectory.GetFiles(settings.StormReplay.WildCard).Any())
                    {
                        FileInfo? latest = ReplaysDirectory.GetFiles(settings.StormReplay.WildCard).OrderByDescending(f => f.CreationTime).FirstOrDefault();

                        if (replayHelper.TryGetReplayId(latest.Name, out int replayId))
                        {
                            MinReplayId = replayId;
                        }
                    }
                    else
                    {
                        MinReplayId = settings.HeroesProfileApi.ReplayIdBaseline;
                    }
                }

                return minReplayId;
            }
            set => minReplayId = value;
        }

        private DirectoryInfo ReplaysDirectory
        {
            get
            {
                DirectoryInfo cache = new DirectoryInfo(settings.ReplayCachePath);
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }

        public HeroesProfileProvider(ILogger<HeroesProfileProvider> logger, HeroesProfileService heroesProfileService, CancellationTokenProvider provider, ReplayHelper replayHelper, Settings settings)
        {
            this.provider = provider;
            this.replayHelper = replayHelper;
            this.settings = settings;
            this.logger = logger;
            this.heroesProfileService = heroesProfileService;
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            try
            {
                HeroesProfileReplay? replay = await GetNextReplayAsync();

                if (replay != null)
                {
                    FileInfo cacheStormReplay = CreateFile(replay);

                    if (!cacheStormReplay.Exists)
                    {
                        await DownloadStormReplay(replay, cacheStormReplay);
                    }

                    StormReplay? stormReplay = await TryLoadReplay(replay, cacheStormReplay);

                    if (stormReplay != null)
                    {
                        MinReplayId = (int)replay.Id;
                    }

                    return stormReplay;
                }
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Could not provide a Replay file using the HotsAPI.");
            }

            return null;
        }

        private async Task<StormReplay?> TryLoadReplay(HeroesProfileReplay heroesProfileReplay, FileInfo cacheStormReplay)
        {
            logger.LogDebug("id: {0}, url: {1}, path: {2}", heroesProfileReplay.Id, heroesProfileReplay.Url, cacheStormReplay.FullName);

            (ReplayParseResult result, Replay replay) = ParseReplay(await File.ReadAllBytesAsync(cacheStormReplay.FullName), ParseOptions.FullParsing);

            logger.LogDebug("result: {0}, path: {1}", result, cacheStormReplay.FullName);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                return new StormReplay(cacheStormReplay.FullName, replay);
            }

            return null;
        }

        private async Task DownloadStormReplay(HeroesProfileReplay replay, FileInfo cachedReplay)
        {
            using (AmazonS3Client s3Client = new AmazonS3Client(new BasicAWSCredentials(settings.HeroesProfileApi.AwsAccessKey, settings.HeroesProfileApi.AwsSecretKey), RegionEndpoint.USEast1))
            {
                if (Uri.TryCreate(replay.Url, UriKind.Absolute, out Uri? uri))
                {
                    GetObjectRequest request = new GetObjectRequest
                    {
                        RequestPayer = RequestPayer.Requester,
                        BucketName = uri.Host.Split('.')[0],
                        Key = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped),

                    };

                    using (GetObjectResponse response = await s3Client.GetObjectAsync(request, provider.Token))
                    {
                        await using (MemoryStream memoryStream = new MemoryStream())
                        {
                            await response.ResponseStream.CopyToAsync(memoryStream);

                            await using (var stream = cachedReplay.OpenWrite())
                            {
                                await stream.WriteAsync(memoryStream.ToArray());
                                await stream.FlushAsync();
                            }

                            logger.LogInformation($"downloaded heroesprofile replay.");
                            logger.LogDebug($"id: {replay.Id}");
                            logger.LogDebug($"s3: {replay.Url}");
                            logger.LogDebug($"path: {cachedReplay.FullName}");
                        }
                    }
                }
            }
        }

        private FileInfo CreateFile(HeroesProfileReplay replay)
        {
            var path = ReplaysDirectory.FullName;
            var name = $"{replay.Id}{settings.StormReplay.CachedFileNameSplitter}{replay.Fingerprint}{settings.StormReplay.FileExtension}";
            return new FileInfo(Path.Combine(path, name));
        }

        private async Task<HeroesProfileReplay?> GetNextReplayAsync()
        {
            Version minVersion = settings.Spectate.MinVersionSupported;

            try
            {
                logger.LogInformation($"MinReplayId: {MinReplayId}");

                return await Policy
                       .Handle<Exception>()
                       .OrResult<HeroesProfileReplay?>(replay => replay == null)
                       .WaitAndRetryAsync(60, retry => TimeSpan.FromSeconds(10))
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> response = await heroesProfileService.ListReplaysAllAsync(MinReplayId).ConfigureAwait(false);

                           HeroesProfileReplay? replay = null;

                           if (response != null && response.Any())
                           {
                               replay = (from r in response
                                         where r.Id >= minReplayId
                                         let version = Version.Parse(r.GameVersion)
                                         where version.Major == minVersion.Major && 
                                               version.Minor == minVersion.Minor && 
                                               version.Build == minVersion.Build && 
                                               version.Revision == minVersion.Revision
                                         where settings.HeroesProfileApi.GameTypes.Contains(r.GameType, StringComparer.CurrentCultureIgnoreCase)
                                         select r).FirstOrDefault();

                               MinReplayId = response.Max(x => x.Id);

                               logger.LogInformation($"MinReplayId: {MinReplayId}");
                           }

                           return replay;

                       }, provider.Token);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get the next replay file.");
            }

            return null;
        }
    }
}
