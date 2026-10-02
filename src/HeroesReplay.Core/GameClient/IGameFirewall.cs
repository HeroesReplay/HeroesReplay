using System.Collections.Generic;

namespace HeroesReplay.Core.GameClient;

public interface IGameFirewall
{
    void AllowInboundClients(IReadOnlyList<string> exePaths);
}
