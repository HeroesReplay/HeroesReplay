using System;

namespace HeroesReplay
{
    public class GameEventArgs<T> : EventArgs
    {
        public Game Game { get; }
        public T Data { get; }
        public string Message { get; }

        public GameEventArgs(Game game, T data, string message)
        {
            Game = game;
            Message = message;
            Data = data;
        }
    }
}