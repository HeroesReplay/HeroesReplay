namespace HeroesReplay.Core.Services.Twitch
{
    public class ReplayRequestResponse
    {
        public bool Success { get; }
        public string Message { get; }
        public int? Number { get; }

        public ReplayRequestResponse(bool success, string message, int? number)
        {
            Success = success;
            Message = message;
            Number = number;
        }
    }
}
