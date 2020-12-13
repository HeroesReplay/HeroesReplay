using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Heroes.ReplayParser;

using HeroesReplay.Core.Services;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.HotsApi;
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
                        MinReplayId = settings.HotsApi.MinReplayId;
                    }
                    else if (TempReplaysDirectory.GetFiles(settings.StormReplay.WildCard).Any())
                    {
                        FileInfo? latest = TempReplaysDirectory.GetFiles(settings.StormReplay.WildCard).OrderByDescending(f => f.CreationTime).FirstOrDefault();

                        MinReplayId = int.Parse(latest.Name.Split(settings.HotsApi.CachedFileNameSplitter)[0]);
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

        private DirectoryInfo TempReplaysDirectory
        {
            get
            {
                DirectoryInfo cache = new DirectoryInfo(settings.StormReplayHotsApiCache);
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }


        public HeroesProfileProvider(ILogger<HeroesProfileProvider> logger, HeroesProfileService heroesProfileService, CancellationTokenProvider provider, Settings settings)
        {
            this.provider = provider;
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

            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await File.ReadAllBytesAsync(cacheStormReplay.FullName), ParseOptions.FullParsing);

            logger.LogDebug("result: {0}, path: {1}", result, cacheStormReplay.FullName);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                return new StormReplay(cacheStormReplay.FullName, replay);
            }

            return null;
        }

        private async Task DownloadStormReplay(HeroesProfileReplay heroesProfileReplay, FileInfo cacheStormReplay)
        {
            using (AmazonS3Client s3Client = new AmazonS3Client(new BasicAWSCredentials(settings.HotsApi.AwsAccessKey, settings.HotsApi.AwsSecretKey), RegionEndpoint.EUWest1))
            {
                if (Uri.TryCreate(heroesProfileReplay.Url, UriKind.Absolute, out Uri? uri))
                {
                    GetObjectRequest request = new GetObjectRequest
                    {
                        RequestPayer = RequestPayer.Requester,
                        BucketName = uri.Host.Split('.')[0],
                        Key = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)
                    };

                    using (GetObjectResponse response = await s3Client.GetObjectAsync(request, provider.Token))
                    {
                        await using (MemoryStream memoryStream = new MemoryStream())
                        {
                            await response.ResponseStream.CopyToAsync(memoryStream);

                            await using (var stream = cacheStormReplay.OpenWrite())
                            {
                                await stream.WriteAsync(memoryStream.ToArray());
                                await stream.FlushAsync();
                            }

                            logger.LogInformation($"downloaded hotsapi replay.");
                            logger.LogDebug($"id: {heroesProfileReplay.Id}");
                            logger.LogDebug($"s3: {heroesProfileReplay.Url}");
                            logger.LogDebug($"path: {cacheStormReplay.FullName}");
                        }
                    }
                }
            }
        }

        private FileInfo CreateFile(HeroesProfileReplay replay)
        {
            return new FileInfo(
                Path.Combine(
                    TempReplaysDirectory.FullName, 
                    $"{replay.Id}{settings.HotsApi.CachedFileNameSplitter}{replay.Fingerprint}{settings.StormReplay.FileExtension}"));
        }

        private async Task<HeroesProfileReplay?> GetNextReplayAsync()
        {
            try
            {
                logger.LogInformation($"MinReplayId: {MinReplayId}");

                return await Policy
                       .Handle<Exception>()
                       .OrResult<HeroesProfileReplay?>(replay => replay == null)
                       .WaitAndRetryAsync(60, retry => TimeSpan.FromSeconds(5))
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> response = await heroesProfileService.ListReplaysAllAsync(MinReplayId).ConfigureAwait(false);

                           HeroesProfileReplay? replay = null;

                           if (response != null && response.Any())
                           {
                               replay = response.FirstOrDefault(r => r.Id > MinReplayId &&
                                                                            (r.Deleted == null || r.Deleted == 0) &&
                                                                            Version.Parse(r.GameVersion) >= settings.Spectate.MinVersionSupported &&
                                                                            settings.HeroesProfileApi.GameTypes.Contains(r.GameType, StringComparer.CurrentCultureIgnoreCase));

                               MinReplayId = (int)response.Max(x => x.Id);

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
