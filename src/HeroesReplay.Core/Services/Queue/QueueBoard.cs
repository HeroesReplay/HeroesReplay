using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Queue;

public static class QueueBoard
{
    public const string FileName = "queue.html";

    public static void Write(string path, IReadOnlyList<RewardQueueItem> items)
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
        html.Append("h1{font-size:64px;margin:40px 56px 12px;}");
        html.Append("p.lead{font-size:28px;margin:0 56px 28px;color:#9aabc0;}");
        html.Append("ol{font-size:36px;line-height:1.45;margin:0 72px 48px;}");
        html.Append(
            ".who{color:#f2d38a;} .map{color:#e8eef7;} .meta{color:#9aabc0;font-size:28px;}"
        );
        html.Append("</style></head><body><h1>Replay queue</h1>");
        int count = items?.Count ?? 0;
        html.Append("<p class=\"lead\">")
            .Append(count)
            .Append(count == 1 ? " request" : " requests")
            .Append(" waiting</p>");
        if (count == 0)
        {
            html.Append(
                "<p class=\"lead\">The queue is empty. Redeem a replay with channel points.</p>"
            );
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

    private static string Encode(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : WebUtility.HtmlEncode(value.Trim());
    }
}
