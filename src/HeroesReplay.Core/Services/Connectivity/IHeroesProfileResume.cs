namespace HeroesReplay.Core.Services.Connectivity;

public interface IHeroesProfileResume
{
    bool IsPending { get; }
    void Arm();
    bool Consume();
}
