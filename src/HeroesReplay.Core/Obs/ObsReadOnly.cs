using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The obs-websocket requests the agent inspection tools may send. Every one is a Get: none
/// selects a scene, changes an input, switches a profile, or starts or stops an output.
/// <see cref="IObsReadSession"/> calls <see cref="Require"/> before each request, so a Set,
/// Start, Stop, Create, or Remove never reaches OBS through it.
/// </summary>
public static class ObsReadOnly
{
    public static readonly IReadOnlySet<string> Requests = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "GetVersion",
        "GetStats",
        "GetProfileList",
        "GetSceneCollectionList",
        "GetVideoSettings",
        "GetCurrentProgramScene",
        "GetSceneList",
        "GetSceneItemList",
        "GetInputList",
        "GetInputSettings",
        "GetInputMute",
        "GetInputVolume",
        "GetSpecialInputs",
        "GetStreamStatus",
        "GetRecordStatus",
        "GetStreamServiceSettings",
        "GetSourceScreenshot",
    };

    public static bool IsAllowed(string requestType) =>
        requestType != null
        && requestType.StartsWith("Get", StringComparison.Ordinal)
        && Requests.Contains(requestType);

    public static void Require(string requestType)
    {
        if (!IsAllowed(requestType))
        {
            throw new InvalidOperationException(
                "'"
                    + requestType
                    + "' is not a read-only OBS request. The inspection session sends only "
                    + string.Join(", ", Requests.Order(StringComparer.Ordinal))
                    + "."
            );
        }
    }
}
