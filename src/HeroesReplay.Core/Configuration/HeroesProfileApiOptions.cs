
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public class HeroesProfileApiOptions
    {
        public Uri BaseUri { get; set; }
        public Uri TwitchBaseUri { get; set; }
        public string ApiKey { get; set; }
        public string AwsAccessKey { get; set; }
        public string AwsSecretKey { get; set; }
        public IEnumerable<string> GameTypes { get; set; }
        public string S3Bucket { get; set; }
        public string S3Region { get; set; }
        public int MinReplayId { get; set; }
        public int FallbackMaxReplayId { get; set; }
        public int ApiMaxReturnedReplays { get; set; }
        public bool EnableMMR { get; set; }
        public TimeSpan APIRetryWaitTime { get; set; }
        public string StandardCacheDirectoryName { get; set; }
        public string RequestsCacheDirectoryName { get; set; }
    }
}