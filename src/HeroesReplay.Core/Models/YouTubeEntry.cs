namespace HeroesReplay.Core.Models
{

    public class YouTubeEntry
    {
        public string Title { get; set; }
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
    }
}
