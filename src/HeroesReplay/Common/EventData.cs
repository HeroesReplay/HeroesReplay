using Heroes.ReplayParser;
using System;

namespace HeroesReplay
{
    public class GameEvent<T> : EventArgs
    {
        public Game Game { get; }
        public T Data { get; }
        public string Message { get; }

        public GameEvent(Game game, T data, string message)
        {
            Game = game;
            Message = message;
            Data = data;
        }
    }
}