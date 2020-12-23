using Heroes.ReplayParser;

using System;

namespace HeroesReplay.Core
{
    public record Focus(Type Calculator, Unit Unit, Player Target, float Points, string Description, int Index = 0);
}