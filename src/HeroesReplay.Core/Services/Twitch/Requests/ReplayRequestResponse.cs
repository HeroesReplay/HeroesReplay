namespace HeroesReplay.Core.Services.Twitch
{
    public class ReplayRequestResponse
    {
        public bool Success { get; }
        public string Message { get; }

        public ReplayRequestResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }
}
