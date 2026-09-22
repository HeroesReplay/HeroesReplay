using System.Net;
using System.Text;

namespace HeroesReplay.Core.Services.Twitch;

public static class PredictionReportPage
{
    public static string ToHtml(PredictionReport report)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>");
        html.Append(Encode(report?.Title ?? "Prediction"));
        html.Append("</title><style>");
        html.Append(
            "body{margin:0;background:#0b0e14;color:#f4f7fb;font:24px Segoe UI,sans-serif;}"
        );
        html.Append("main{padding:48px 64px;} h1{font-size:48px;margin:0 0 8px;}");
        html.Append("p{color:#9aa7b5;margin:0 0 28px;}");
        html.Append(".cols{display:flex;gap:32px;} section{flex:1;}");
        html.Append("h2{font-size:28px;margin:0 0 12px;}");
        html.Append("table{width:100%;border-collapse:collapse;}");
        html.Append("th,td{text-align:left;padding:10px 12px;border-bottom:1px solid #243041;}");
        html.Append(
            "th{color:#9aa7b5;font-weight:600;} .win h2{color:#7dcea0;} .lose h2{color:#f0a3a3;}"
        );
        html.Append("</style></head><body><main>");
        html.Append("<h1>");
        html.Append(Encode(string.IsNullOrWhiteSpace(report?.Title) ? "Prediction" : report.Title));
        html.Append("</h1><p>");
        if (string.IsNullOrWhiteSpace(report?.WinningOutcome))
        {
            html.Append("No winning outcome.");
        }
        else
        {
            html.Append(Encode(report.WinningOutcome));
            html.Append(" won. Twitch only returns the top predictors, not every viewer.");
        }

        html.Append("</p><div class=\"cols\">");
        AppendTable(html, "Winners", "win", report?.Winners, true);
        AppendTable(html, "Losers", "lose", report?.Losers, false);
        html.Append("</div></main></body></html>");
        return html.ToString();
    }

    private static void AppendTable(
        StringBuilder html,
        string heading,
        string css,
        System.Collections.Generic.IReadOnlyList<PredictionParticipant> rows,
        bool winners
    )
    {
        html.Append("<section class=\"");
        html.Append(css);
        html.Append("\"><h2>");
        html.Append(heading);
        html.Append("</h2><table><thead><tr><th>Viewer</th><th>");
        html.Append(winners ? "Won" : "Spent");
        html.Append("</th><th>Streak</th></tr></thead><tbody>");
        if (rows == null || rows.Count == 0)
        {
            html.Append("<tr><td colspan=\"3\">None in the top predictors.</td></tr>");
        }
        else
        {
            foreach (PredictionParticipant row in rows)
            {
                html.Append("<tr><td>");
                html.Append(Encode(row.DisplayName));
                html.Append("</td><td>");
                html.Append(winners ? row.PointsWon : row.PointsUsed);
                html.Append("</td><td>");
                html.Append(row.Streak);
                html.Append("</td></tr>");
            }
        }

        html.Append("</tbody></table></section>");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
