using Heroes.ReplayParser;

using System;

namespace HeroesReplay
{
    public class EventData<T> : EventArgs
    {
        public Replay Replay { get; }
        public T Data { get; }
        public string Message { get; }

        public EventData(Replay replay, T data, string message)
        {
            Replay = replay;
            Message = message;
            Data = data;
        }
    }
}