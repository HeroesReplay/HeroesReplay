
using System;


namespace HeroesReplay.Core.Services.HeroesProfile
{
    [Serializable]
    public class ReplayDeletedException : Exception
    {
        public ReplayDeletedException(string message) : base(message)
        {

        }
    }
}