using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon;
using Amazon.Auth.AccessControlPolicy;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Heroes.ReplayParser;
using HeroesReplay.Replays.HotsApi;
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using static Heroes.ReplayParser.DataParser;
using HotsApiReplay = HeroesReplay.Replays.HotsApi.Replay;

namespace HeroesReplay.Replays
{
    public class StormReplayHotsApiProvider : IStormReplayProvider
    {
        private const string GAME_TYPE_STORM_LEAGUE = "StormLeague";
        private const string GAME_TYPE_QUICK_MATCH = "QuickMatch";
        private const string GAME_TYPE_UNRANKED = "UnrankedDraft";

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (configuration.GetValue<int>(Constants.ConfigKeys.MinReplayId) != Constants.REPLAY_ID_UNSET)
                    {
                        MinReplayId = configuration.GetValue<int>(Constants.ConfigKeys.MinReplayId);
                    }
                    else if (TempReplaysDirectory.GetFiles(Constants.STORM_REPLAY_WILDCARD).Any())
                    {
                        FileInfo? latest = TempReplaysDirectory.GetFiles(Constants.STORM_REPLAY_WILDCARD).OrderByDescending(f => f.CreationTime).FirstOrDefault();

                        MinReplayId = int.Parse(latest.Name.Split(Constants.STORM_REPLAY_CACHED_FILE_NAME_SPLITTER)[0]);
                    }
                    else
                    {
                        MinReplayId = Constants.REPLAY_ID_ZEMILL_BASE_LINE;
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
                DirectoryInfo cache = new DirectoryInfo(Constants.STORM_REPLAY_CACHE_PATH);
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }

        private string AwsAccessKey => configuration.GetValue<string>(Constants.ConfigKeys.AwsAccessKey);

        private string AwsSecretKey => configuration.GetValue<string>(Constants.ConfigKeys.AwsSecretKey);

        private readonly CancellationTokenProvider provider;
        private readonly PlayerBlackListChecker blackListChecker;
        private readonly IConfiguration configuration;
        private readonly ILogger<StormReplayHotsApiProvider> logger;

        private int minReplayId;

        public StormReplayHotsApiProvider(CancellationTokenProvider provider, PlayerBlackListChecker blackListChecker, IConfiguration configuration, ILogger<StormReplayHotsApiProvider> logger)
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
            logger.LogInformation($"[HOTSAPI][PARSING][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{cacheStormReplay.FullName}]");

            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await File.ReadAllBytesAsync(cacheStormReplay.FullName), ParseOptions.FullParsing);

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

        private FileInfo CreateFile(HotsApiReplay replay) => new FileInfo(Path.Combine(TempReplaysDirectory.FullName, $"{replay.Id}{Constants.STORM_REPLAY_CACHED_FILE_NAME_SPLITTER}{replay.Filename}{Constants.STORM_REPLAY_EXTENSION}"));

        private async Task<HotsApiReplay?> GetNextReplayAsync()
        {
            try
            {
                logger.LogInformation($"[MIN REPLAY ID][{MinReplayId}]");

                using (HttpClient client = new HttpClient())
                {
                    HotsApiClient hotsApiClient = new HotsApiClient(client);

                    return await Polly.Policy
                        .Handle<Exception>()
                        .OrResult<HotsApiReplay?>(replay => replay == null)
                        .WaitAndRetryAsync(30, (retry) => TimeSpan.FromSeconds(10))
                        .ExecuteAsync(async (token) =>
                        {
                            HotsApiResponse<ICollection<HotsApiReplay>> response = await hotsApiClient.ListReplaysAllAsync(min_id: MinReplayId, existing: true, with_players: false, token);

                            if (response.StatusCode == (int)HttpStatusCode.OK)
                            {
                                HotsApiReplay replay = response.Result
                                    .FirstOrDefault(r =>
                                        r.Id > MinReplayId &&
                                        r.Deleted == false &&
                                        Version.Parse(r.Game_version) >= Constants.MIN_VERSION_SUPPORTED &&
                                        (r.Game_type == GAME_TYPE_STORM_LEAGUE || r.Game_type == GAME_TYPE_UNRANKED || r.Game_type == GAME_TYPE_QUICK_MATCH));

                                if (replay != null)
                                {
                                    logger.LogInformation($"[REPLAY][FOUND][{replay.Id}][{replay.Url}]");
                                    return replay;
                                }

                                logger.LogInformation($"[REPLAY][ERROR][Could not find suitable replay]");
                                return null;
                            }

                            logger.LogInformation($"[HOTSAPI][HTTP_STATUS][{response.StatusCode}]");
                            return null;

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
