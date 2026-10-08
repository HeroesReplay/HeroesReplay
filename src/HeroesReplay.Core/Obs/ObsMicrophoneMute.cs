using System;
using System.Collections.Generic;
using HeroesReplay.Core.Obs.Inspection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The spectator's microphone mute (#314): the read-only Get requests of
/// <see cref="IObsReadSession"/>, plus SetInputMute to muted. Muting is the only change it can
/// make. It has no way to unmute an input.
/// </summary>
public interface IObsMicrophoneSession : IObsReadSession
{
    /// <summary>Sends SetInputMute with <c>inputMuted</c> true.</summary>
    /// <exception cref="ObsRequestException">OBS refused, for example an input without audio.</exception>
    void Mute(string inputName);
}

/// <summary>
/// <see cref="IObsMicrophoneSession"/> on the spectator's one connection per replay. The Get
/// guard is <see cref="ObsReadOnly"/>, and disposing it leaves the connection open.
/// </summary>
internal sealed class ObsBorrowedMicrophoneSession : IObsMicrophoneSession
{
    private readonly OBSWebsocket obs;
    private readonly ObsBorrowedReadSession read;

    public ObsBorrowedMicrophoneSession(OBSWebsocket obs)
    {
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        read = new ObsBorrowedReadSession(obs);
    }

    public JObject Get(string requestType, JObject requestData = null) =>
        read.Get(requestType, requestData);

    public void Mute(string inputName)
    {
        try
        {
            obs.SetInputMute(inputName, true);
        }
        catch (ErrorResponseException e)
        {
            throw new ObsRequestException("SetInputMute", e.ErrorCode, e.Message);
        }
    }

    public void Dispose()
    {
        // The owner disconnects.
    }
}

/// <summary>
/// Mutes every microphone OBS has (<see cref="ObsMicrophones"/>): the global Mic/Aux devices and
/// any audio input capture source. Desktop Audio, media, and browser sources are never touched.
/// An automated machine should have no microphone at all, so this is the backstop for one that
/// does. The owner's decision was mute, not block (#314): a mute that fails is a warning, and the
/// caller goes on. Each input's mute is logged once per session.
/// </summary>
internal sealed class ObsMicrophoneMute
{
    public const string AtSessionStart = "at session start";
    public const string BeforeStartStream = "before StartStream";

    private const string ReadKey = "\0read";

    private readonly ILogger logger;
    private readonly IObsMicrophoneSession session;
    private readonly object gate = new();
    private readonly HashSet<string> logged = new(StringComparer.Ordinal);

    public ObsMicrophoneMute(ILogger logger, IObsMicrophoneSession session)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>A new replay session: each mute and each failure is logged again, once.</summary>
    public void NewSession()
    {
        lock (gate)
        {
            logged.Clear();
        }
    }

    /// <summary>
    /// Mutes each microphone that is not muted. Never throws: a read or a mute that fails is
    /// logged as a warning.
    /// </summary>
    /// <param name="moment"><see cref="AtSessionStart"/> or <see cref="BeforeStartStream"/>.</param>
    /// <returns>The inputs this call muted.</returns>
    public IReadOnlyList<string> MuteAll(string moment)
    {
        // The watchdog starts the stream on its own thread while the spectator begins a session.
        lock (gate)
        {
            IReadOnlyList<ObsMicrophone> microphones;
            try
            {
                microphones = ObsMicrophones.Find(session);
            }
            catch (Exception e)
            {
                if (logged.Add(ReadKey))
                {
                    logger.LogWarning(
                        e,
                        "Could not read the OBS microphones {Moment}, so none was muted. HeroesReplay goes on.",
                        moment
                    );
                }

                return [];
            }

            var muted = new List<string>();
            foreach (ObsMicrophone microphone in microphones)
            {
                if (Mute(microphone, moment))
                {
                    muted.Add(microphone.Name);
                }
            }

            return muted;
        }
    }

    private bool Mute(ObsMicrophone microphone, string moment)
    {
        try
        {
            if (ObsInspector.ReadMuted(session, microphone.Name) == true)
            {
                return false;
            }

            session.Mute(microphone.Name);
        }
        catch (Exception e)
        {
            if (logged.Add("\0failed:" + microphone.Name))
            {
                logger.LogWarning(
                    e,
                    "Could not mute OBS microphone {Input} ({Source}) {Moment}. It may still be live. The stream and the recording go on.",
                    microphone.Name,
                    microphone.Source,
                    moment
                );
            }

            return false;
        }

        if (logged.Add(microphone.Name))
        {
            logger.LogWarning(
                "OBS microphone {Input} ({Source}) was not muted. HeroesReplay muted it {Moment} (OBS:MuteMicrophones). {Fix}",
                microphone.Name,
                microphone.Source,
                moment,
                Fix(microphone)
            );
        }
        else
        {
            logger.LogDebug(
                "OBS microphone {Input} was unmuted again. HeroesReplay muted it {Moment}.",
                microphone.Name,
                moment
            );
        }

        return true;
    }

    /// <summary>What removes the microphone for good, so nothing is left to mute.</summary>
    internal static string Fix(ObsMicrophone microphone) =>
        microphone.GlobalAudio != null
            ? "An automated machine has no microphone: set Settings > Audio > Global Audio Devices > Mic/Auxiliary Audio to Disabled."
            : "An automated machine has no microphone: remove this audio input capture source from the collection.";
}
