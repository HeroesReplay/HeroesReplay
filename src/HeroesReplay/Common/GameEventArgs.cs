using System;

namespace HeroesReplay
{
    public class GameEventArgs<T> : EventArgs
    {
        public StormReplay StormReplay { get; }
        public T Data { get; }
        public string Message { get; }

        public GameEventArgs(StormReplay stormReplay, T data, string message)
        {
            StormReplay = stormReplay;
            Message = message;
            Data = data;
        }
    }
}