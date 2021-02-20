
using System;


namespace HeroesReplay.Core.Services.HeroesProfile
{
    [Serializable]
    public class ReplayVersionNotSupportedException : Exception
    {
        public ReplayVersionNotSupportedException(string message): base(message)
        {

        }
    }
}