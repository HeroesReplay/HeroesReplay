﻿using System;
using System.IO;


namespace HeroesReplay.Shared
{
    using Heroes.ReplayParser;

    /// <summary>
    /// The StormReplay is a wrapper which links a raw replay file on disk to an in-memory parsed version of that file
    /// </summary>
    public class StormReplay
    {
        public string Path { get; }

        public Replay Replay { get; }

        public StormReplay(string path, Replay replay)
        {
            Replay = replay;
            Path = path;
        }
    }
}