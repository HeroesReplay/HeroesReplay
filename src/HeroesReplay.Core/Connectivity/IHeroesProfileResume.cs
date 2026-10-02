namespace HeroesReplay.Core.Connectivity;

public interface IHeroesProfileResume
{
    bool IsPending { get; }
    void Arm();
    bool Consume();
}
