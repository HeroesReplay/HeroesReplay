using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.TwitchExtension;

public sealed record ExtensionSnapshotPlayer(
    string Name,
    [property: JsonPropertyName("battletag")] int BattleTag,
    int Region,
    int Team,
    string Hero,
    string HeroAttribute,
    IReadOnlyList<string> Talents,
    bool Ai
);
