using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public record HeroesToolChestSettings
    {
        public Uri HeroesDataReleaseUri { get; init; }

        public IEnumerable<string> IgnoreUnits { get; init; }

        public IEnumerable<string> ObjectiveContains { get; init; }

        public IEnumerable<string> CaptureContains { get; init; }

        public string ScalingLinkId { get; init; }
    }
}