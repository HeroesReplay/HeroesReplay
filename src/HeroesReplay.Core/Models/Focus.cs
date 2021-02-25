using Heroes.ReplayParser;

using System;

namespace HeroesReplay.Core.Models
{
    public class Focus
    {
        public Type Calculator { get; set; }
        public Unit Unit { get; set; }
        public Player Target { get; set; }
        public float Points { get; set; }
        public string Description { get; set; }

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