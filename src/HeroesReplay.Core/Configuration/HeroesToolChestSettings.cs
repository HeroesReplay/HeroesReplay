using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record HeroesToolChestSettings
    {
        public Uri HeroesDataReleaseUri { get; init; }

        public IEnumerable<string> IgnoreUnits { get; init; }

        public IEnumerable<string> ObjectivesContains { get; init; }

        public IEnumerable<string> CaptureNames { get; init; }

        public string ScalingLinkId { get; init; }
    }
}