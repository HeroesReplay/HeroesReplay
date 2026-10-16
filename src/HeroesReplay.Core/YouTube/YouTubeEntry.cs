using System;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.YouTube;

public class YouTubeEntry
{
    public string Title { get; set; }
    public string TemplateVersion { get; set; }
    public int? ReplayId { get; set; }
    public string VideoId { get; set; }
    public string Map { get; set; }
    public string GameType { get; set; }
    public string Rank { get; set; }
    public string GameVersion { get; set; }
    public string[] DescriptionLines { get; set; }
    public string[] Tags { get; set; }

    /*
     *  {
          "kind": "youtube#videoCategory",
          "etag": "0srcLUqQzO7-NGLF7QnhdVzJQmY",
          "id": "24",
          "snippet": {
            "title": "Entertainment",
            "assignable": true,
            "channelId": "UCBR8-60-B28hp2BmDPdntcQ"
          }
        },
    */
    public string CategoryId { get; set; }
    public string PrivacyStatus { get; set; } // unlisted, private, public
    public string DesiredPrivacyStatus { get; set; }
    public string ActualPrivacyStatus { get; set; }
    public bool Requested { get; set; }
    public string Hero { get; set; }
    public string[] Heroes { get; set; }
    public DateTimeOffset? RecordedAtUtc { get; set; }
    public DateTimeOffset? PublishAtUtc { get; set; }

    /// <summary>
    /// The hero the cycle followed for the whole match (#433), or null for any other video. A
    /// focus video is sent right after the viewer requests.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public YouTubeHeroFocus HeroFocus { get; set; }
}

/// <summary>A focus-hero video's hero (catalog English name), its role, and its guide build.</summary>
public sealed class YouTubeHeroFocus
{
    public string Hero { get; set; }
    public string Role { get; set; }
    public string Build { get; set; }
}
