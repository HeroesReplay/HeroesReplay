namespace HeroesReplay.Core.Services.Observer;

public interface IReplayOpener
{
    void RequestAuthenticatedClient();

    void Open(string replayPath);
}
