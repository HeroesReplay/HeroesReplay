namespace HeroesReplay.Core.Configuration
{
    public class TrackerEventOptions
    {
        public string GatesOpen { get; } = nameof(GatesOpen);
        public string TalentChosen { get; } = nameof(TalentChosen);
        public string JungleCampCapture { get; } = nameof(JungleCampCapture);
    }
}