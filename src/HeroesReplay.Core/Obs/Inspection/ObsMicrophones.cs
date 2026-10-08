using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HeroesReplay.Core.Obs.Inspection;

/// <summary>
/// A microphone OBS captures: a global Mic/Aux device (<see cref="GlobalAudio"/> is <c>mic1</c>
/// to <c>mic4</c>) or an audio input capture source (<see cref="GlobalAudio"/> is null).
/// <see cref="Kind"/> is the unversioned input kind when GetInputList names it.
/// </summary>
public sealed record ObsMicrophone(string Name, string Kind, string GlobalAudio)
{
    /// <summary>The slot of a global device, else the input kind.</summary>
    public string Source => GlobalAudio ?? Kind ?? "audio input";
}

/// <summary>
/// Every microphone input OBS has (#314): the enabled global Mic/Aux devices from
/// GetSpecialInputs (<c>mic1</c> to <c>mic4</c>; a disabled device has no name there), and every
/// input whose kind captures an audio input device (<see cref="InputCaptureKinds"/>). Desktop
/// Audio (an output capture), media, browser, and every other source are never in the list.
/// <c>obs validate</c> reports these, and the spectator mutes them (<see cref="ObsMicrophoneMute"/>).
/// </summary>
public static class ObsMicrophones
{
    /// <summary>
    /// OBS's audio input capture kinds: <c>wasapi_input_capture</c> on Windows (Audio Input
    /// Capture), and the macOS and Linux equivalents. A video capture device (<c>dshow_input</c>)
    /// is a camera or capture card, not a microphone, and is not here.
    /// </summary>
    public static readonly IReadOnlySet<string> InputCaptureKinds = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "wasapi_input_capture",
        "coreaudio_input_capture",
        "pulse_input_capture",
        "alsa_input_capture",
    };

    private static readonly string[] MicSlots = { "mic1", "mic2", "mic3", "mic4" };

    /// <summary>Reads GetSpecialInputs and GetInputList: Get requests only.</summary>
    public static IReadOnlyList<ObsMicrophone> Find(IObsReadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        IReadOnlyDictionary<string, string> global = ObsInspector.GlobalAudio(
            session.Get("GetSpecialInputs")
        );
        return Find(global, ObsResponse.Objects(session.Get("GetInputList"), "inputs"));
    }

    /// <param name="global">Input name to slot, from <see cref="ObsInspector.GlobalAudio"/>.</param>
    /// <param name="inputs">GetInputList <c>inputs</c>.</param>
    internal static IReadOnlyList<ObsMicrophone> Find(
        IReadOnlyDictionary<string, string> global,
        IEnumerable<JObject> inputs
    )
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JObject input in inputs ?? [])
        {
            string name = ObsResponse.String(input, "inputName");
            if (!string.IsNullOrWhiteSpace(name))
            {
                kinds.TryAdd(
                    name,
                    ObsResponse.String(input, "unversionedInputKind")
                        ?? ObsResponse.String(input, "inputKind")
                );
            }
        }

        global ??= new Dictionary<string, string>();
        var microphones = global
            .Where(entry => MicSlots.Contains(entry.Value, StringComparer.Ordinal))
            .OrderBy(entry => entry.Value, StringComparer.Ordinal)
            .Select(entry => new ObsMicrophone(
                entry.Key,
                kinds.TryGetValue(entry.Key, out string kind) ? kind : null,
                entry.Value
            ))
            .ToList();
        microphones.AddRange(
            kinds
                .Where(entry =>
                    InputCaptureKinds.Contains(entry.Value ?? string.Empty)
                    && !global.ContainsKey(entry.Key)
                )
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ObsMicrophone(entry.Key, entry.Value, null))
        );
        return microphones;
    }
}
