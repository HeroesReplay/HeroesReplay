using System;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Spectator
{
    public class GameEventArgs<T> : EventArgs
    {
        public StormReplay StormReplay { get; }
        public T Data { get; }
        public string Message { get; }
        public TimeSpan Timer { get;  }

        public GameEventArgs(StormReplay stormReplay, T data, TimeSpan timer, string message)
        {
            Timer = timer;
            StormReplay = stormReplay;
            Message = message;
            Data = data;
        }
    }
}