using Heroes.ReplayParser;

using System;

namespace HeroesReplay.Core.Models
{
    public class Focus
    {
        public Type Calculator { get; init; }
        public Unit Unit { get; init; }
        public Player Target { get; init; }
        public float Points { get; init; }
        public string Description { get; init; }

        public Focus(Type calculator, Unit unit, Player target, float points, string description, int index = 0)
        {
            Calculator = calculator;
            Unit = unit;
            Target = target;
            Points = points;
            Description = description;
            Index = index;
        }

        public int Index { get; set; }
    }
}