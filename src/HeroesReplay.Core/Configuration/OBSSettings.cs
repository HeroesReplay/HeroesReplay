﻿using HeroesReplay.Core.Models;

using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public record OBSSettings
    {
        public bool Enabled { get; init; }
        public bool RecordingEnabled { get; init; }
        public string RecordingFolderDirectory { get; init; }
        public string WebSocketEndpoint { get; init; }
        public string GameSceneName { get; init; }
        public string WaitingSceneName { get; init; }
        public IEnumerable<ReportScene> ReportScenes { get; init; }
        public IEnumerable<string> RankImagesSourceNames { get; init; }
    }
}