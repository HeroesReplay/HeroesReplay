namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public sealed record ExtensionPlayer(
    string Name,
    int BattleTag,
    int Region,
    int Team,
    string Hero,
    string HeroAttribute,
    bool Ai
);
