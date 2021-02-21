namespace HeroesReplay.Core.Services.Twitch
{
    public class RewardResponse
    {
        public bool Success { get; }
        public string Message { get; }

        public RewardResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }
}
