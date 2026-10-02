namespace HeroesReplay.Core.GameClient;

public interface IReplayOpener
{
    void RequestAuthenticatedClient();

    void Open(string replayPath);

    void OpenMatching(string exePath, string replayPath);
}
