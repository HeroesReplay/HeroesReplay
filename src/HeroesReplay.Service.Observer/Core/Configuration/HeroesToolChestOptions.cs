using System;
using System.Collections.Generic;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class HeroesToolChestOptions
    {
        public Uri HeroesDataReleaseUri { get; set; }
        public IEnumerable<string> IgnoreUnits { get; set; }
        public IEnumerable<string> ObjectiveContains { get; set; }
        public IEnumerable<string> CaptureContains { get; set; }
        public string CoreScalingLinkId { get; set; }
        public IEnumerable<string> VehicleScalingLinkIds { get; set; }
    }
}