
using System;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    [Serializable]
    public class ReplayNotFoundException : Exception
    {
        public ReplayNotFoundException(string message) : base(message) { }
    }
}