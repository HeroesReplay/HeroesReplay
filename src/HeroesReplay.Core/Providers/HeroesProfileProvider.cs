using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
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
        private readonly AppSettings settings;
        private readonly CancellationTokenProvider provider;
        private readonly ReplayHelper replayHelper;
        private readonly ILogger<HeroesProfileProvider> logger;
        private readonly IHeroesProfileService heroesProfileService;
        private int minReplayId;

        private int MinReplayId
        {
            get
            {
                if (minReplayId == default)
                {
                    if (ReplaysDirectory.GetFiles(settings.StormReplay.WildCard).Any())
                    {
                        FileInfo? latest = ReplaysDirectory.GetFiles(settings.StormReplay.WildCard).OrderByDescending(f => f.CreationTime).FirstOrDefault();

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

        private DirectoryInfo ReplaysDirectory
        {
            get
            {
                DirectoryInfo cache = new DirectoryInfo(settings.ReplayCachePath);
                if (!cache.Exists) cache.Create();
                return cache;
            }
        }

        public HeroesProfileProvider(ILogger<HeroesProfileProvider> logger, IHeroesProfileService heroesProfileService, CancellationTokenProvider provider, ReplayHelper replayHelper, AppSettings settings)
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

        private async Task<StormReplay?> TryLoadReplay(HeroesProfileReplay heroesProfileReplay, FileInfo file)
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

            (ReplayParseResult result, Replay replay) = ParseReplay(await File.ReadAllBytesAsync(file.FullName), options);
            
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
                if (Uri.TryCreate(replay.Url, UriKind.Absolute, out Uri? uri))
                {
                    GetObjectRequest request = new GetObjectRequest
                    {
                        RequestPayer = RequestPayer.Requester,
                        BucketName = uri.Host.Split('.')[0],
                        Key = uri.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)
                    };

                    using (GetObjectResponse response = await s3Client.GetObjectAsync(request, provider.Token))
                    {
                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            await response.ResponseStream.CopyToAsync(memoryStream);

                            using (var stream = cachedReplay.OpenWrite())
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
            var name = $"{replay.Id}{settings.StormReplay.Seperator}{replay.GameType}{settings.StormReplay.Seperator}{replay.Fingerprint}{settings.StormReplay.FileExtension}";
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
                       .WaitAndRetryAsync(60, retry => settings.HeroesProfileApi.APIRetryWaitTime)
                       .ExecuteAsync(async token =>
                       {
                           IEnumerable<HeroesProfileReplay> response = await heroesProfileService.ListReplaysAllAsync(MinReplayId).ConfigureAwait(false);

                           HeroesProfileReplay? replay = null;

                           if (response != null && response.Any())
                           {
                               replay = (from r in response
                                         where r.Url.Contains(settings.HeroesProfileApi.S3Bucket)
                                         where r.Valid == 1
                                         where r.Id > MinReplayId
                                         let version = Version.Parse(r.GameVersion)
                                         where version.Major == minVersion.Major && 
                                               version.Minor == minVersion.Minor && 
                                               version.Build == minVersion.Build && 
                                               version.Revision == minVersion.Revision
                                         where settings.HeroesProfileApi.GameTypes.Contains(r.GameType, StringComparer.CurrentCultureIgnoreCase)
                                         select r).OrderBy(x => x.Id).FirstOrDefault();

                               if (replay == null)
                               {
                                   MinReplayId = response.Max(x => x.Id);
                               }

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
