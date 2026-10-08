using System.Globalization;
using System.Net;
using System.Text;

namespace HeroesReplay.Core.Twitch.Predictions;

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
        html.Append("p.verdict{color:#d9c8ff;font-size:30px;font-style:italic;margin:0 0 12px;}");
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
        html.Append("</h1>");
        if (!string.IsNullOrWhiteSpace(report?.Verdict))
        {
            html.Append("<p class=\"verdict\">");
            html.Append(Encode(report.Verdict));
            html.Append("</p>");
        }

        html.Append("<p>");
        if (string.IsNullOrWhiteSpace(report?.WinningOutcome))
        {
            html.Append("Voting is open.");
        }
        else
        {
            html.Append(Encode(report.WinningOutcome));
            html.Append(" won.");
            AppendSide(html, report.WinningOutcome, report.WinnerVoters, report.WinnerPoints);
            AppendSide(html, report.LosingOutcome, report.LoserVoters, report.LoserPoints);
            html.Append(" Twitch only returns the top predictors, not every viewer.");
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

    private static void AppendSide(StringBuilder html, string outcome, int voters, int points)
    {
        if (string.IsNullOrWhiteSpace(outcome) || (voters <= 0 && points <= 0))
        {
            return;
        }

        html.Append(' ');
        html.Append(Encode(outcome));
        html.Append(": ");
        html.Append(voters.ToString("N0", CultureInfo.InvariantCulture));
        html.Append(voters == 1 ? " viewer, " : " viewers, ");
        html.Append(points.ToString("N0", CultureInfo.InvariantCulture));
        html.Append(" points.");
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
