using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using HeroesReplay.Replays.HotsApi;
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Heroes.ReplayParser.DataParser;
using HotsApiReplay = HeroesReplay.Replays.HotsApi.Replay;

namespace HeroesReplay.Replays
{
    public class StormReplayHotsApiProvider : IStormReplayProvider
    {
        private int MinReplayId
        {
            get
            {
                try
                {
                    int minReplayId;

                    FileInfo? latest = TempReplaysDirectory.GetFiles("*.StormReplay").OrderByDescending(f => f.CreationTime).FirstOrDefault();

                    if (latest != null)
                    {
                        minReplayId = int.Parse(latest.Name.Split("_")[0]);
                    }
                    else if (configuration.GetValue<int>("minReplayId") != -1)
                    {
                        minReplayId = configuration.GetValue<int>("minReplayId");
                    }
                    else
                    {
                        minReplayId = Constants.ZEMILL_BASE_LINE_HOTS_API_REPLAY_ID;
                    }

                    logger.LogInformation($"[MIN REPLAY ID][{minReplayId}]");

                    return minReplayId;
                }
                catch (Exception e)
                {
                    logger.LogError("Could not calculate starting replay id. Falling back to Zemill.");

                    return Constants.ZEMILL_BASE_LINE_HOTS_API_REPLAY_ID;
                }
            }
        }

        private DirectoryInfo TempReplaysDirectory
        {
            get
            {
                DirectoryInfo cache = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "HeroesReplay"));
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }

        private string AwsAccessKey => configuration.GetValue<string>("awsAccessKey");

        private string AwsSecretKey => configuration.GetValue<string>("awsSecretKey");

        private readonly CancellationTokenProvider provider;
        private readonly PlayerBlackListChecker blackListChecker;
        private readonly IConfiguration configuration;
        private readonly ILogger<StormReplayHotsApiProvider> logger;

        public StormReplayHotsApiProvider(CancellationTokenProvider provider, PlayerBlackListChecker blackListChecker,  IConfiguration configuration, ILogger<StormReplayHotsApiProvider> logger)
        {
            this.provider = provider;
            this.blackListChecker = blackListChecker;
            this.configuration = configuration;
            this.logger = logger;
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            try
            {
                HotsApiReplay? hotsApiReplay = await GetNextReplayAsync();

                if (hotsApiReplay != null && blackListChecker.IsUsable(hotsApiReplay))
                {
                    FileInfo cacheStormReplay = CreateFile(hotsApiReplay);

                    if (!cacheStormReplay.Exists)
                    {
                        await DownloadStormReplay(hotsApiReplay, cacheStormReplay);
                    }

                    return await TryLoadReplay(hotsApiReplay, cacheStormReplay);
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
            logger.LogInformation($"[HOTSAPI][PARSING][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{cacheStormReplay.FullName}]");

            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await File.ReadAllBytesAsync(cacheStormReplay.FullName), true);

            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
            {
                logger.LogInformation("Parse Success: " + cacheStormReplay.FullName);
                return new StormReplay(cacheStormReplay.FullName, replay);
            }

            return null;
        }

        private async Task DownloadStormReplay(HotsApiReplay hotsApiReplay, FileInfo cacheStormReplay)
        {
            using (AmazonS3Client s3Client = new AmazonS3Client(new BasicAWSCredentials(AwsAccessKey, AwsSecretKey), RegionEndpoint.EUWest1))
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

                            logger.LogInformation($"[HOTSAPI][SAVING][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{cacheStormReplay.FullName}]");

                            await using (var stream = cacheStormReplay.OpenWrite())
                            {
                                await stream.WriteAsync(memoryStream.ToArray());
                                await stream.FlushAsync();
                            }

                            logger.LogInformation($"[HOTSAPI][SAVED][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{cacheStormReplay.FullName}]");
                        }
                    }
                }
            }
        }

        private FileInfo CreateFile(HotsApiReplay replay) => new FileInfo(Path.Combine(TempReplaysDirectory.FullName, $"{replay.Id}_{replay.Filename}.StormReplay"));

        private async Task<HotsApiReplay?> GetNextReplayAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HotsApiClient hotsApiClient = new HotsApiClient(client);

                    HotsApiResponse<ICollection<HotsApiReplay>> response = await hotsApiClient.ListReplaysAllAsync(min_id: MinReplayId, existing: true, with_players: false);

                    if (response.StatusCode == 200)
                    {
                        HotsApiReplay? replay = response.Result.Where(replay => replay.Id > MinReplayId).FirstOrDefault(replay => replay.Deleted != true && replay.Game_type == "StormLeague" || replay.Game_type == "QuickMatch");

                        if (replay != null)
                        {
                            logger.LogInformation($"[REPLAY][FOUND][{replay.Id}][{replay.Url}]");
                            return replay;
                        }
                    }

                    if (response.StatusCode != 200)
                    {
                        // Why?
                    }


                    logger.LogInformation($"[REPLAY][ERROR][Could not find suitable replay]");
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
