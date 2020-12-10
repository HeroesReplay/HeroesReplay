using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record HeroesToolChestSettings
    {
        public string HeroesDataPath { get; init; }

        public Uri HeroesDataReleaseUri { get; init; }

        public IEnumerable<string> IgnoreUnits { get; init; }
    }
}