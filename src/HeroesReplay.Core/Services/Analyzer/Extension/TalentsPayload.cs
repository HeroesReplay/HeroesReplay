using System.Collections.Generic;
using HeroesReplay.Service.Spectator.Core.HeroesProfileExtension;

namespace HeroesReplay.Core.Services.Analyzer
{
    public class TalentsPayload
    {
        public ExtensionStep Step { get; set; }
        public List<Dictionary<string, string>> Content { get; set; }
    }
}