using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using HeroesReplay.Replays.HotsApi;
using HeroesReplay.Shared;

using static Heroes.ReplayParser.DataParser;

namespace HeroesReplay.Replays
{
    public class StormReplayHotsApiProvider
    {
        // Get Last replay ID
        // Find Next ID
        // Download Replay
        // Write Temp Replay ID
        // Parse Replay
        // Launch Game
        // End Game
        // Next

        public async Task<HeroesReplay.Replays.HotsApi.Replay> GetNextReplayAsync(int previous = 18000000)
        {
            using (var client = new HttpClient())
            {
                HotsApiClient hotsApiClient = new HotsApiClient(client);

                var response = await hotsApiClient.ListReplaysAllAsync(min_id: previous, existing: true, with_players: false);

                if (response.StatusCode == 200)
                {
                    var replay = response.Result.First(replay => replay.Id > previous && replay.Deleted != true && replay.Game_type == "StormLeague" || replay.Game_type == "QuickMatch");
                    return replay;
                }

                throw new Exception("Error from HotsApi: " + response.StatusCode);
            }
        }

        public async Task<StormReplay> GetStormReplayAsync(HeroesReplay.Replays.HotsApi.Replay hotsApiReplay)
        {
            using (var client = new AmazonS3Client(RegionEndpoint.EUWest1))
            {
                if (Uri.TryCreate(hotsApiReplay.Url, UriKind.Absolute, out Uri uri))
                {
                    GetObjectRequest request = new GetObjectRequest
                    {
                        RequestPayer = RequestPayer.Requester,
                        BucketName = uri.Host.Split('.')[0],
                        Key = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)
                    };

                    using (GetObjectResponse response = await client.GetObjectAsync(request))
                    {
                        await using (MemoryStream stream = new MemoryStream(new byte[response.ResponseStream.Length]))
                        {
                            await response.ResponseStream.CopyToAsync(stream);

                            (ReplayParseResult result, Heroes.ReplayParser.Replay replay) = ParseReplay(stream.ToArray(), ignoreErrors: true, allowPTRRegion: false);

                            if (result != ReplayParseResult.Exception && result != ReplayParseResult.PreAlphaWipe && result != ReplayParseResult.Incomplete)
                            {
                                return new StormReplay(hotsApiReplay.Url, replay);
                            }
                        }
                    }
                }

                throw new Exception($"Could not parse replay: {hotsApiReplay.Id}:{hotsApiReplay.Url}");
            }
        }
    }
}
