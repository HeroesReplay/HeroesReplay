using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Observer;

public interface IGameFirewall
{
    void AllowInboundClients(IReadOnlyList<string> exePaths);
}
