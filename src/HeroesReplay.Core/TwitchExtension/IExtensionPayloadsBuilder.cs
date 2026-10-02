using Heroes.ReplayParser;

namespace HeroesReplay.Core.TwitchExtension;

public interface IExtensionPayloadsBuilder
{
    ExtensionGame CreatePayloads(Replay replay);
}
