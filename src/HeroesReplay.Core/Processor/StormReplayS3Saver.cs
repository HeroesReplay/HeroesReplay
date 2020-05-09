using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Replays
{
    public class StormReplayS3Saver : IStormReplaySaver
    {
        private readonly ILogger<StormReplayS3Saver> logger;
        private readonly IConfiguration configuration;
        private readonly CancellationTokenProvider tokenProvider;

        private string AwsAccessKey => configuration.GetValue<string>(Constants.ConfigKeys.AwsAccessKey);

        private string AwsSecretKey => configuration.GetValue<string>(Constants.ConfigKeys.AwsSecretKey);

        private string Bucket => configuration.GetValue<string>(Constants.ConfigKeys.ReplayDestination);


        public StormReplayS3Saver(ILogger<StormReplayS3Saver> logger, IConfiguration configuration, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.configuration = configuration;
            this.tokenProvider = tokenProvider;
        }

        public async Task<StormReplay> SaveReplayAsync(StormReplay stormReplay)
        {
            throw new NotImplementedException();

            //using (AmazonS3Client s3Client = new AmazonS3Client(new BasicAWSCredentials(AwsAccessKey, AwsSecretKey), RegionEndpoint.EUWest1))
            //{
            //    if (Uri.TryCreate(stormReplay.Path, UriKind.Absolute, out Uri? uri))
            //    {
            //        string key = Path.GetFileName(uri.GetComponents(UriComponents.Path, UriFormat.Unescaped));

            //        PutObjectRequest request = new PutObjectRequest
            //        {
            //            BucketName = Bucket,
            //            Key = Path.GetFileName(uri.GetComponents(UriComponents.Path, UriFormat.Unescaped))
            //        };

            //        PutObjectResponse response = await s3Client.PutObjectAsync(request, tokenProvider.Token);

            //        if (response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            //        {
            //            logger.LogInformation($"uploaded replay to: {Bucket}/{key}");
            //        }

            //        return new StormReplay($"{Bucket}/{key}", stormReplay.Replay);
            //    }
            //}
        }
    }
}