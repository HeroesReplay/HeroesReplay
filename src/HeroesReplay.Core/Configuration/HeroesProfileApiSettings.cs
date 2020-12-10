
using System;

namespace HeroesReplay.Core.Shared
{
    public record HeroesProfileApiSettings
    {
        public Uri BaseUri { get; init; }
        public string ApiKey { get; init; }
    }
}