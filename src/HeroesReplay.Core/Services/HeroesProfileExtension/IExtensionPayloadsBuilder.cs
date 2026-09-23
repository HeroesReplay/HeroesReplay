using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.HeroesProfileExtension;

public interface IExtensionPayloadsBuilder
{
    ExtensionGame CreatePayloads(Replay replay);
}
