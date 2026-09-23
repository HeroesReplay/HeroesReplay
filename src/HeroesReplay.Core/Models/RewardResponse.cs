namespace HeroesReplay.Core.Models;

public class RewardResponse
{
    public bool Success { get; }
    public string Message { get; }
    public bool Duplicate { get; }

    public RewardResponse(bool success, string message, bool duplicate = false)
    {
        Success = success;
        Message = message;
        Duplicate = duplicate;
    }
}
