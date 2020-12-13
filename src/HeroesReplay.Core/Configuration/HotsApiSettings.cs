
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record HotsApiSettings
    {
        public string AwsAccessKey { get; init; }
        public string AwsSecretKey { get; init; }

        public int ReplayIdUnset { get; init; }
        public int MinReplayId { get; init; }
        public int ReplayIdBaseline { get; init; }
        public Uri BaseUri { get; init; }
        public string CachedFileNameSplitter { get; init; }
        public IEnumerable<string> GameTypes { get; init; }
    }
}