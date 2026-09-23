using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubeEntryBuilder
{
    public static YouTubeEntry Create(LoadedReplay loaded, YouTubeSettings youtube)
    {
        HeroesProfileReplay heroesProfileReplay = loaded?.HeroesProfileReplay;
        string requestor = loaded?.RewardQueueItem?.Request?.Login;
        string map = heroesProfileReplay?.Map ?? loaded?.Replay?.Map;
        string gameType = heroesProfileReplay?.GameType;
        string rank = heroesProfileReplay?.Rank;
        int? replayId = heroesProfileReplay?.Id ?? loaded?.ReplayId;
        string id = replayId is > 0 ? replayId.Value.ToString() : null;

        var descriptionLines = new[]
        {
            "Twitch: http://twitch.tv/saltysadism",
            id != null
                ? $"Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID={id}"
                : string.Empty,
            gameType != null ? $"Game type: {gameType}" : string.Empty,
            !string.IsNullOrWhiteSpace(rank) ? $"Rank: {rank}" : string.Empty,
            !string.IsNullOrWhiteSpace(requestor) ? $"Requested by: {requestor}" : string.Empty,
            "Hashtags: #HeroesOfTheStorm #SaltySadism",
        }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return new YouTubeEntry
        {
            Title = string.Join(
                " - ",
                new[] { map, id, gameType, rank }.Where(part => !string.IsNullOrWhiteSpace(part))
            ),
            PrivacyStatus = youtube?.PrivacyStatus ?? "public",
            CategoryId = youtube?.CategoryId,
            DescriptionLines = descriptionLines,
            Tags = new[] { gameType, map }.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray(),
        };
    }
}
