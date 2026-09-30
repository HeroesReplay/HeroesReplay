namespace HeroesReplay.Core.Services.Observer;

public interface IReplayOpener
{
    void RequestAuthenticatedClient();

    void Open(string replayPath);

    void OpenMatching(string exePath, string replayPath);
}
