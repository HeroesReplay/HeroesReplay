using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Heroes.ReplayParser;

using HeroesReplay.Core.Providers.HotsApi;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using Polly;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using static Heroes.ReplayParser.DataParser;

using HotsApiReplay = HeroesReplay.Core.Providers.HotsApi.Replay;

namespace HeroesReplay.Core.Providers
{
    public class HotsApiProvider : IReplayProvider
    {
        private readonly Settings settings;
        private readonly CancellationTokenProvider provider;
        private readonly ILogger<HotsApiProvider> logger;
        private int minReplayId;

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (settings.HotsApi.MinReplayId != settings.HotsApi.ReplayIdUnset)
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
                        MinReplayId = settings.HotsApi.ReplayIdBaseline;
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



        public HotsApiProvider(CancellationTokenProvider provider, Settings settings, ILogger<HotsApiProvider> logger)
        {
            this.provider = provider;
            this.settings = settings;
            this.logger = logger;
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            try
            {
                HotsApiReplay? hotsApiReplay = await GetNextReplayAsync();

                if (hotsApiReplay != null)
                {
                    FileInfo cacheStormReplay = CreateFile(hotsApiReplay);

                    if (!cacheStormReplay.Exists)
                    {
                        await DownloadStormReplay(hotsApiReplay, cacheStormReplay);
                    }

                    StormReplay? stormReplay = await TryLoadReplay(hotsApiReplay, cacheStormReplay);

                    if (stormReplay != null)
                    {
                        MinReplayId = (int)hotsApiReplay.Id;
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

        private async Task<StormReplay?> TryLoadReplay(HotsApiReplay hotsApiReplay, FileInfo cacheStormReplay)
        {
            logger.LogDebug("id: {0}, url: {1}, path: {2}", hotsApiReplay.Id, hotsApiReplay.Url, cacheStormReplay.FullName);

            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await File.ReadAllBytesAsync(cacheStormReplay.FullName), ParseOptions.FullParsing);

            logger.LogDebug("result: {0}, path: {1}", result, cacheStormReplay.FullName);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                return new StormReplay(cacheStormReplay.FullName, replay);
            }

            return null;
        }

        private async Task DownloadStormReplay(HotsApiReplay hotsApiReplay, FileInfo cacheStormReplay)
        {
            using (AmazonS3Client s3Client = new AmazonS3Client(new BasicAWSCredentials(settings.HotsApi.AwsAccessKey, settings.HotsApi.AwsSecretKey), RegionEndpoint.EUWest1))
            {
                if (Uri.TryCreate(hotsApiReplay.Url, UriKind.Absolute, out Uri? uri))
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
                            logger.LogDebug($"id: {hotsApiReplay.Id}");
                            logger.LogDebug($"s3: {hotsApiReplay.Url}");
                            logger.LogDebug($"path: {cacheStormReplay.FullName}");
                        }
                    }
                }
            }
        }

        private FileInfo CreateFile(HotsApiReplay replay)
        {
            return new FileInfo(Path.Combine(TempReplaysDirectory.FullName, $"{replay.Id}{settings.HotsApi.CachedFileNameSplitter}{replay.Filename}{settings.StormReplay.FileExtension}"));
        }

        private async Task<HotsApiReplay?> GetNextReplayAsync()
        {
            try
            {
                logger.LogInformation($"MinReplayId: {MinReplayId}");

                using (HttpClient client = new HttpClient())
                {
                    HotsApiClient hotsApiClient = new HotsApiClient(client);

                    return await Policy
                        .Handle<Exception>()
                        .OrResult<HotsApiReplay?>(replay => replay == null)
                        .WaitAndRetryAsync(60, retry => TimeSpan.FromSeconds(5))
                        .ExecuteAsync(async token =>
                        {
                            HotsApiResponse<ICollection<HotsApiReplay>> response = await hotsApiClient.ListReplaysAllAsync(min_id: MinReplayId, existing: true, with_players: null, token);

                            HotsApiReplay? replay = null;

                            if (response.StatusCode == (int)HttpStatusCode.OK)
                            {
                                replay = response.Result.FirstOrDefault(r => r.Id > MinReplayId &&
                                                                             r.Deleted == false &&
                                                                             Version.Parse(r.Game_version) >= settings.Spectate.MinVersionSupported &&
                                                                             settings.HotsApi.GameTypes.Contains(r.Game_type, StringComparer.CurrentCultureIgnoreCase));

                                MinReplayId = (int)response.Result.Max(x => x.Id);
                                logger.LogInformation($"MinReplayId: {MinReplayId}");
                            }

                            logger.LogDebug($"hotsapi response code: {response.StatusCode}");

                            return replay;

                        }, provider.Token);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Could not get the next replay file.");
            }

            return null;
        }
    }
}
