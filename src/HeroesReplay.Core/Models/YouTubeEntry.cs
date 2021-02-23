namespace HeroesReplay.Core.Services.Twitch
{
    public class YouTubeEntry
    {
        public string Title { get; init; }
        public string[] DescriptionLines { get; init; }
        public string[] Tags { get; init; }

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
        public string CategoryId { get; init; }
        public string PrivacyStatus { get; init; } // unlisted, private, public
    }
}
