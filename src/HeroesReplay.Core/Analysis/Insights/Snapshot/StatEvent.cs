using System;
using System.Collections.Generic;
using Heroes.ReplayParser.MPQFiles;

namespace HeroesReplay.Core.Analysis.Insights.Snapshot;

/// <summary>
/// A tracker <c>StatGameEvent</c> read into its named fields (#412):
/// <c>{"JungleCampCapture", [{{"CampType"}, "Siege Camp"}], [{{"CampID"}, 3}], [{{"TeamID"}, 1}]}</c>
/// becomes <c>CampType</c>, <c>CampID</c> and <c>TeamID</c>. A field can repeat
/// (<c>KillingPlayer</c>), so every value is kept.
/// </summary>
public sealed class StatEvent
{
    private readonly Dictionary<string, List<object>> fields;

    private StatEvent(string name, TimeSpan time, Dictionary<string, List<object>> fields)
    {
        Name = name;
        Time = time;
        this.fields = fields;
    }

    public string Name { get; }

    public TimeSpan Time { get; }

    /// <summary>The event read from <paramref name="trackerEvent"/>, or null when it is not a stat event.</summary>
    public static StatEvent From(TrackerEvent trackerEvent)
    {
        if (
            trackerEvent?.TrackerEventType != ReplayTrackerEvents.TrackerEventType.StatGameEvent
            || trackerEvent.Data?.dictionary == null
            || !trackerEvent.Data.dictionary.TryGetValue(0, out TrackerEventStructure name)
            || string.IsNullOrEmpty(name?.blobText)
        )
        {
            return null;
        }

        var fields = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (KeyValuePair<int, TrackerEventStructure> section in trackerEvent.Data.dictionary)
        {
            if (section.Key == 0)
            {
                continue;
            }

            TrackerEventStructure[] items =
                section.Value?.array ?? section.Value?.optionalData?.array;
            foreach (TrackerEventStructure item in items ?? Array.Empty<TrackerEventStructure>())
            {
                string key = Key(item);
                if (
                    key == null
                    || item.dictionary == null
                    || !item.dictionary.TryGetValue(1, out TrackerEventStructure value)
                )
                {
                    continue;
                }

                object read = value?.blobText ?? (object)value?.vInt;
                if (read == null)
                {
                    continue;
                }

                if (!fields.TryGetValue(key, out List<object> values))
                {
                    values = new List<object>();
                    fields.Add(key, values);
                }

                values.Add(read);
            }
        }

        return new StatEvent(name.blobText, trackerEvent.TimeSpan, fields);
    }

    /// <summary>The first integer value of <paramref name="key"/>, or null.</summary>
    public int? Int(string key) =>
        fields.TryGetValue(key, out List<object> values)
        && values.Count > 0
        && values[0] is long value
            ? (int)value
            : null;

    /// <summary>The first text value of <paramref name="key"/>, or null.</summary>
    public string Text(string key) =>
        fields.TryGetValue(key, out List<object> values) && values.Count > 0
            ? values[0] as string
            : null;

    /// <summary>Every integer value of <paramref name="key"/>, such as each <c>KillingPlayer</c>.</summary>
    public IReadOnlyList<int> Ints(string key)
    {
        var ints = new List<int>();
        if (fields.TryGetValue(key, out List<object> values))
        {
            foreach (object value in values)
            {
                if (value is long number)
                {
                    ints.Add((int)number);
                }
            }
        }

        return ints;
    }

    private static string Key(TrackerEventStructure item)
    {
        if (
            item?.dictionary == null
            || !item.dictionary.TryGetValue(0, out TrackerEventStructure label)
        )
        {
            return null;
        }

        return
            label?.dictionary != null
            && label.dictionary.TryGetValue(0, out TrackerEventStructure text)
            ? text?.blobText
            : null;
    }
}
