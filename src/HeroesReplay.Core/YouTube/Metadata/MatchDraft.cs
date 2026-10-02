using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// Every unusual-draft note of a match: each team's role note (<see cref="HeroDraft"/>) and its
/// unusual composition labels (<see cref="TeamComposition"/>). A note both teams share is
/// written once without a team, so <c>Blue no tank, Red double healer, Double soak, Red dive</c>.
/// </summary>
public sealed class MatchDraft
{
    private MatchDraft(string line, string title, IReadOnlyList<string> labels)
    {
        Line = line;
        Title = title;
        Labels = labels;
    }

    /// <summary>The description's <c>Draft:</c> note, or null for a usual draft.</summary>
    public string Line { get; }

    /// <summary>
    /// The title's draft slot: the role notes, as before composition labels, or the rarest
    /// composition label when its corpus frequency is lower than every role note's. One
    /// composition label at most. Null for a usual draft.
    /// </summary>
    public string Title { get; }

    /// <summary>The composition labels without a team (<c>Double soak</c>), each once, for tags.</summary>
    public IReadOnlyList<string> Labels { get; }

    public static MatchDraft Read(
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        YouTubeTitleSettings titles = null
    )
    {
        titles ??= new YouTubeTitleSettings();
        TeamCompositionSettings settings = titles.Compositions ?? new TeamCompositionSettings();
        List<Entry> roles = RoleEntries(
            HeroDraft.TeamNote(catalog, roster, titles, team: 0),
            HeroDraft.TeamNote(catalog, roster, titles, team: 1)
        );
        List<Entry> compositions = CompositionEntries(
            TeamComposition.Unusual(catalog, roster, team: 0, titles),
            TeamComposition.Unusual(catalog, roster, team: 1, titles)
        );

        var shown = new List<string>();
        var labels = new List<string>();
        foreach (Entry entry in roles)
        {
            shown.Add(entry.Shown);
        }

        foreach (Entry entry in compositions)
        {
            shown.Add(entry.Shown);
            if (!labels.Contains(entry.Note.Text))
            {
                labels.Add(entry.Note.Text);
            }
        }

        string roleTitle =
            roles.Count == 0 ? null : string.Join(", ", roles.Select(entry => entry.Shown));
        Entry rarest = Rarest(compositions, settings);
        string title = roleTitle;
        if (rarest != null && (roleTitle == null || Rarer(rarest, roles, settings)))
        {
            title = rarest.Shown;
        }

        return new MatchDraft(
            shown.Count == 0 ? null : string.Join(", ", shown),
            title,
            labels.ToArray()
        );
    }

    /// <summary>The same order as <see cref="HeroDraft.Phrase"/>: shared, else Blue then Red.</summary>
    private static List<Entry> RoleEntries(DraftNote blue, DraftNote red)
    {
        var entries = new List<Entry>();
        if (
            blue != null
            && red != null
            && string.Equals(blue.Text, red.Text, StringComparison.Ordinal)
        )
        {
            entries.Add(new Entry(blue, blue.Text));
            return entries;
        }

        if (blue != null)
        {
            entries.Add(new Entry(blue, HeroDraft.WithTeam("Blue", blue.Text)));
        }

        if (red != null)
        {
            entries.Add(new Entry(red, HeroDraft.WithTeam("Red", red.Text)));
        }

        return entries;
    }

    /// <summary>In rule order. A label both teams have, with the same words, is written once.</summary>
    private static List<Entry> CompositionEntries(
        IReadOnlyList<DraftNote> blue,
        IReadOnlyList<DraftNote> red
    )
    {
        var entries = new List<Entry>();
        foreach (string key in TeamComposition.Keys)
        {
            DraftNote left = Find(blue, key);
            DraftNote right = Find(red, key);
            if (
                left != null
                && right != null
                && string.Equals(left.Text, right.Text, StringComparison.Ordinal)
            )
            {
                entries.Add(new Entry(left, left.Text));
                continue;
            }

            if (left != null)
            {
                entries.Add(new Entry(left, HeroDraft.WithTeam("Blue", left.Text)));
            }

            if (right != null)
            {
                entries.Add(new Entry(right, HeroDraft.WithTeam("Red", right.Text)));
            }
        }

        return entries;
    }

    private static DraftNote Find(IReadOnlyList<DraftNote> notes, string key)
    {
        if (notes == null)
        {
            return null;
        }

        foreach (DraftNote note in notes)
        {
            if (note != null && string.Equals(note.Key, key, StringComparison.Ordinal))
            {
                return note;
            }
        }

        return null;
    }

    /// <summary>The composition entry with the lowest corpus frequency. The first wins a tie.</summary>
    private static Entry Rarest(List<Entry> entries, TeamCompositionSettings settings)
    {
        Entry best = null;
        double bestFrequency = double.MaxValue;
        foreach (Entry entry in entries)
        {
            double frequency = settings.Frequency(entry.Note.Key) ?? 0;
            if (best == null || frequency < bestFrequency)
            {
                best = entry;
                bestFrequency = frequency;
            }
        }

        return best;
    }

    /// <summary>True when the label is rarer than every role note, so it takes the title slot.</summary>
    private static bool Rarer(Entry label, List<Entry> roles, TeamCompositionSettings settings)
    {
        double frequency = settings.Frequency(label.Note.Key) ?? 0;
        foreach (Entry role in roles)
        {
            if ((settings.Frequency(role.Note.Key) ?? 0) <= frequency)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record Entry(DraftNote Note, string Shown);
}
