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
        private int MinId { get; set; }

        private string AwsAccessKey => configuration.GetValue<string>("awsAccessKey");

        private string AwsSecretKey => configuration.GetValue<string>("awsSecretKey");

        private readonly CancellationTokenProvider provider;
        private readonly IConfiguration configuration;
        private readonly ILogger<StormReplayHotsApiProvider> logger;

        public StormReplayHotsApiProvider(CancellationTokenProvider provider, IConfiguration configuration, ILogger<StormReplayHotsApiProvider> logger)
        {
            this.provider = provider;
            this.configuration = configuration;
            this.logger = logger;

            MinId = configuration.GetValue<int>("minReplayId");
        }

        public async Task<StormReplay?> TryLoadReplayAsync()
        {
            try
            {
                HotsApiReplay? hotsApiReplay = await GetNextReplayAsync(MinId);

                if (hotsApiReplay != null)
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

                            string stormReplay = Path.Combine(Path.GetTempPath(), request.Key);

                            if (!File.Exists(stormReplay))
                            {
                                using (GetObjectResponse response = await s3Client.GetObjectAsync(request, provider.Token))
                                {
                                    await using (MemoryStream memoryStream = new MemoryStream())
                                    {
                                        await response.ResponseStream.CopyToAsync(memoryStream);
                                        logger.LogInformation($"[HOTSAPI][SAVING][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{stormReplay}]");
                                        await File.WriteAllBytesAsync(stormReplay, memoryStream.ToArray());
                                        logger.LogInformation($"[HOTSAPI][SAVED][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{stormReplay}]");
                                    }
                                }
                            }

                            logger.LogInformation($"[HOTSAPI][PARSING][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{stormReplay}]");

                            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(await File.ReadAllBytesAsync(stormReplay), ignoreErrors: true, allowPTRRegion: false);

                            logger.LogInformation($"[HOTSAPI][PARSED][{result}][{hotsApiReplay.Id}][{hotsApiReplay.Url}][{stormReplay}]");

                            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
                            {
                                MinId = Convert.ToInt32(hotsApiReplay.Id);
                                return new StormReplay(stormReplay, replay);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Could not provide a Replay file using the HotsAPI.");
            }

            return null;
        }

        private async Task<HotsApiReplay?> GetNextReplayAsync(int previous)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    HotsApiClient hotsApiClient = new HotsApiClient(client);

                    HotsApiResponse<ICollection<HotsApiReplay>> response = await hotsApiClient.ListReplaysAllAsync(min_id: previous, existing: true, with_players: false);

                    if (response.StatusCode == 200)
                    {
                        HotsApiReplay? replay = response.Result.FirstOrDefault(replay => replay.Id > previous && replay.Deleted != true && replay.Game_type == "StormLeague" || replay.Game_type == "QuickMatch");

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
