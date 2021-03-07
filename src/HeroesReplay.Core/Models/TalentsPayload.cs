using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public class TalentsPayload
    {
        public ExtensionStep Step { get; set; }
        public List<Dictionary<string, string>> Content { get; set; }
    }
}