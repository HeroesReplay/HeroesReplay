using System;

namespace HeroesReplay.Core.TwitchExtension;

public sealed record ExtensionTalentPick(TimeSpan Time, int PlayerIndex, string TalentName);
