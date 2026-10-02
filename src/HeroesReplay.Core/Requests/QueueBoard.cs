using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using HeroesReplay.Core.Twitch.Rewards;

namespace HeroesReplay.Core.Requests;

public static class QueueBoard
{
    public const string FileName = "queue.html";
    public const string ReplayIdExample = "12345678";

    public static void Write(
        string path,
        IReadOnlyList<RewardQueueItem> items,
        IReadOnlyList<SupportedReward> rewards = null
    )
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var html = new StringBuilder();
        html.Append(
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Replay queue</title><style>"
        );
        html.Append(
            "html,body{margin:0;background:#0b0e14;color:#e8eef7;font-family:Segoe UI,sans-serif;}"
        );
        html.Append("h1{font-size:64px;margin:28px 56px 8px;}");
        html.Append("p.lead{font-size:28px;margin:0 56px 16px;color:#9aabc0;}");
        html.Append("p.help{font-size:26px;line-height:1.35;margin:0 56px 12px;color:#c5d2e0;}");
        html.Append("span.label{color:#f2d38a;}");
        html.Append("ol{font-size:32px;line-height:1.4;margin:8px 72px 24px;}");
        html.Append(
            ".who{color:#f2d38a;} .map{color:#e8eef7;} .meta{color:#9aabc0;font-size:26px;}"
        );
        html.Append("div.slots{display:flex;gap:24px;margin:4px 56px 10px;}");
        html.Append(
            "div.team{display:flex;gap:8px;align-items:center;padding:8px 14px;border-radius:10px;}"
        );
        html.Append("div.blue{background:#16407a;} div.red{background:#7a1c1c;}");
        html.Append(
            "div.team b{width:44px;height:44px;line-height:44px;text-align:center;font-size:30px;background:#0b0e14;border-radius:6px;}"
        );
        html.Append("div.team i{font-style:normal;font-size:24px;margin:0 6px;}");
        html.Append("</style></head><body><h1>Replay queue</h1>");
        int count = items?.Count ?? 0;
        html.Append("<p class=\"lead\">")
            .Append(count)
            .Append(count == 1 ? " request" : " requests")
            .Append(" waiting</p>");
        AppendHowTo(html, rewards);
        if (count == 0)
        {
            html.Append("<p class=\"lead\">The queue is empty.</p>");
        }
        else
        {
            html.Append("<ol>");
            for (int index = 0; index < items.Count; index++)
            {
                RewardQueueItem item = items[index];
                string who = Encode(item?.Request?.Login);
                string title = Encode(item?.Request?.RewardTitle);
                string map = Encode(item?.HeroesProfileReplay?.Map);
                string rank = Encode(item?.HeroesProfileReplay?.Rank);
                string id = item?.HeroesProfileReplay?.Id.ToString() ?? "";
                html.Append("<li><span class=\"who\">")
                    .Append(string.IsNullOrWhiteSpace(who) ? "viewer" : who)
                    .Append("</span> — <span class=\"map\">")
                    .Append(string.IsNullOrWhiteSpace(map) ? title : map)
                    .Append("</span> <span class=\"meta\">");
                if (!string.IsNullOrWhiteSpace(rank))
                {
                    html.Append(rank).Append(" · ");
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    html.Append(title).Append(" · ");
                }

                if (ReplayRequestKind.ViewerEnteredReplayId(item))
                {
                    html.Append(id);
                }

                html.Append("</span></li>");
            }

            html.Append("</ol>");
        }

        html.Append("</body></html>");
        string temp = path + ".tmp";
        File.WriteAllText(temp, html.ToString());
        File.Move(temp, path, overwrite: true);
    }

    private static void AppendHowTo(StringBuilder html, IReadOnlyList<SupportedReward> rewards)
    {
        List<SupportedReward> known =
            rewards?.Where(reward => !string.IsNullOrWhiteSpace(reward?.Title)).ToList()
            ?? new List<SupportedReward>();
        List<string> randoms = known
            .Where(reward =>
                reward.RewardType != RewardType.ReplayId && string.IsNullOrWhiteSpace(reward.Map)
            )
            .Select(reward => reward.Title)
            .ToList();
        List<SupportedReward> maps = known
            .Where(reward =>
                reward.RewardType != RewardType.ReplayId && !string.IsNullOrWhiteSpace(reward.Map)
            )
            .ToList();
        string mapExample = maps.FirstOrDefault(reward => !IsRankReward(reward))?.Title;
        string rankExample = maps.FirstOrDefault(IsRankReward)?.Title;

        html.Append(
            "<p class=\"help\"><span class=\"label\">Check out the channel-point rewards.</span> There are several rewards to claim."
        );
        if (randoms.Count > 0)
        {
            html.Append(' ').Append(Join(randoms)).Append(" play a random match.");
        }

        if (mapExample != null)
        {
            html.Append(" Map rewards such as ").Append(Quote(mapExample)).Append(" pick the map.");
        }

        if (rankExample != null)
        {
            html.Append(" Rank rewards such as ")
                .Append(Quote(rankExample))
                .Append(" also ask for a rank.");
        }

        if (randoms.Count == 0 && mapExample == null && rankExample == null)
        {
            html.Append(" Pick a random match, a map, or a rank.");
        }

        html.Append("</p>");

        List<SupportedReward> replayIds = known
            .Where(reward => reward.RewardType == RewardType.ReplayId)
            .ToList();
        string replayId = replayIds.FirstOrDefault(reward => !reward.RecordAndUpload)?.Title;
        string replayUpload = replayIds.FirstOrDefault(reward => reward.RecordAndUpload)?.Title;
        html.Append(
                "<p class=\"help\"><span class=\"label\">Request a specific replay.</span> Redeem "
            )
            .Append(replayId == null ? "the replay reward" : Quote(replayId))
            .Append(" and enter its Heroes Profile replay ID, for example ")
            .Append(ReplayIdExample)
            .Append(". Replays must be from a recent patch.");
        if (replayUpload != null)
        {
            html.Append(' ')
                .Append(Quote(replayUpload))
                .Append(" also records the match and uploads it to YouTube.");
        }

        html.Append("</p>");
        html.Append(
                "<p class=\"help\"><span class=\"label\">Follow one hero.</span> Add a comma and the hero number: "
            )
            .Append(ReplayIdExample)
            .Append(
                ",3 follows hero 3. Send it before that match starts. The camera stays on that hero while they are alive and uses the normal view while they are dead.</p>"
            );
        AppendHeroNumbers(html);
    }

    private static void AppendHeroNumbers(StringBuilder html)
    {
        html.Append("<div class=\"slots\"><div class=\"team blue\"><i>Blue</i>");
        for (int index = 0; index < 5; index++)
        {
            html.Append("<b>").Append(PlayerPriorityRequest.Digit(index)).Append("</b>");
        }

        html.Append("</div><div class=\"team red\">");
        for (int index = 5; index < 10; index++)
        {
            html.Append("<b>").Append(PlayerPriorityRequest.Digit(index)).Append("</b>");
        }

        html.Append("<i>Red</i></div></div>");
        html.Append(
            "<p class=\"help\"><span class=\"label\">Hero numbers.</span> The numbers 1 to 0 sit above each hero portrait at the top of the game screen. 1 to 5 are the Blue team on the left. 6 to 9 and 0 are the Red team on the right, so 0 is the tenth hero.</p>"
        );
    }

    private static bool IsRankReward(SupportedReward reward)
    {
        return reward.Title.Contains("(Rank ", StringComparison.Ordinal);
    }

    private static string Quote(string title)
    {
        return "<span class=\"label\">" + Encode(title) + "</span>";
    }

    private static string Join(IReadOnlyList<string> titles)
    {
        List<string> quoted = titles.Select(Quote).ToList();
        if (quoted.Count == 1)
        {
            return quoted[0];
        }

        return string.Join(", ", quoted.Take(quoted.Count - 1)) + " and " + quoted[^1];
    }

    private static string Encode(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : WebUtility.HtmlEncode(value.Trim());
    }
}
