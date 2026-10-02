using System;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// Whether OBS has the configured profile and scene collection active. <see cref="Reason"/>
/// is a stable code for status and logs; <see cref="Detail"/> says what to change.
/// </summary>
public sealed record ObsSelectionResult(bool Ok, string Reason, string Detail);

/// <summary>
/// Checked over the websocket before every StartStream and StartRecord. A mismatch, or an
/// OBS that does not answer, fails closed: the output is not started.
/// </summary>
public static class ObsSelection
{
    public const string ProfileMismatch = "obs.profile_mismatch";
    public const string CollectionMismatch = "obs.collection_mismatch";
    public const string Unreadable = "obs.selection_unreadable";

    public static ObsSelectionResult Check(
        string expectedProfile,
        string expectedCollection,
        string activeProfile,
        string activeCollection
    )
    {
        string profile = ObsNames.Pick(expectedProfile);
        string collection = ObsNames.Pick(expectedCollection);
        if (string.IsNullOrWhiteSpace(activeProfile) || string.IsNullOrWhiteSpace(activeCollection))
        {
            return NotRead("OBS did not report the active profile and scene collection.");
        }

        // Exact match: OBS keys the profile folder and collection file on these names.
        bool profileOk = string.Equals(activeProfile, profile, StringComparison.Ordinal);
        bool collectionOk = string.Equals(activeCollection, collection, StringComparison.Ordinal);
        if (profileOk && collectionOk)
        {
            return new ObsSelectionResult(
                true,
                null,
                "OBS profile '"
                    + profile
                    + "' and scene collection '"
                    + collection
                    + "' are active."
            );
        }

        string profileText = profileOk
            ? null
            : "OBS profile is '"
                + activeProfile
                + "', expected '"
                + profile
                + "' (OBS:ProfileName). Select Profile > "
                + profile
                + " in OBS, or change OBS:ProfileName.";
        string collectionText = collectionOk
            ? null
            : "OBS scene collection is '"
                + activeCollection
                + "', expected '"
                + collection
                + "' (OBS:SceneCollectionName). Select Scene Collection > "
                + collection
                + " in OBS, or change OBS:SceneCollectionName.";
        string detail = (profileText + " " + collectionText).Trim();
        return new ObsSelectionResult(
            false,
            profileOk ? CollectionMismatch : ProfileMismatch,
            detail + " Streaming and recording were not started."
        );
    }

    public static ObsSelectionResult NotRead(string error) =>
        new(
            false,
            Unreadable,
            "Could not read the active OBS profile and scene collection. Streaming and recording were not started. "
                + error
        );
}
