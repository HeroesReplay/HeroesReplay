
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record HeroesProfileApiSettings
    {
        // HeroesProfile API
        public Uri BaseUri { get; init; }
        public Uri OpenApiBaseUri { get; init; }
        public string ApiKey { get; init; }

        // HeroesProfile Replays S3 Bucket Requestor Pays
        public string AwsAccessKey { get; init; }
        public string AwsSecretKey { get; init; }

        // Game Types to return from the Replays API endpoint
        public IEnumerable<string> GameTypes { get; init; }
        public int ReplayIdBaseline { get; init; }
        public int MinReplayId { get; init; }
        public int ReplayIdUnset { get; init; }
    }
}