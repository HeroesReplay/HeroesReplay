using System;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.MediaPolicy;

public readonly record struct PublicationAdmitResult(bool Allow, string Reason);

/// <summary>
/// Curated ordinary matches stay local. An ordinary match older than the configured
/// ordinary max age stays local even in AllEligible. Notable and high-skill matches
/// are admitted and scheduled for a later slot when the day is full; with
/// MaxPublishAhead 0 they are admitted only while the day is under the cap. A requested
/// match stays eligible for a later send.
/// </summary>
public static class PublicationAdmit
{
    public static PublicationAdmitResult Decide(
        ReplayMediaDecision decision,
        int alreadyPublishedInWindow
    )
    {
        return Decide(decision, alreadyPublishedInWindow, null);
    }

    public static PublicationAdmitResult Decide(
        ReplayMediaDecision decision,
        int alreadyPublishedInWindow,
        ReplayMediaPolicySettings settings
    )
    {
        if (decision == null || !decision.PublicationCandidate)
        {
            return Withhold(decision == null ? null : decision.PublicationReason);
        }

        if (settings != null && !PublicationSchedule.ConfigurationAllowsSend(settings))
        {
            return Withhold(ReplayMediaReason.ConfigurationInvalid);
        }

        int dayCap =
            settings == null ? PublicationSchedule.MaxPublicPerDay : settings.MaxPublicPerDay;
        TimeSpan ordinaryAge =
            settings == null
                ? PublicationSchedule.OrdinaryMaxAge
                : settings.OrdinaryCandidateMaxAge;

        if (decision.PublicationMode == ReplayPublicationMode.Disabled)
        {
            return Withhold(ReplayMediaReason.PublicationDisabled);
        }

        if (decision.Priority == ReplayMediaPriority.Requested)
        {
            return Admit(decision.PublicationReason);
        }

        if (decision.PublicationMode == ReplayPublicationMode.AllEligible)
        {
            if (OrdinaryIsOlderThan(decision, ordinaryAge))
            {
                return Withhold(ReplayMediaReason.Expired);
            }

            return Admit(decision.PublicationReason);
        }

        if (decision.PublicationMode == ReplayPublicationMode.RequestedOnly)
        {
            return Withhold(ReplayMediaReason.NotRequested);
        }

        if (decision.Priority == ReplayMediaPriority.Ordinary)
        {
            return Withhold(ReplayMediaReason.NotSelected);
        }

        if (
            decision.Priority != ReplayMediaPriority.Notable
            && decision.Priority != ReplayMediaPriority.HighSkill
        )
        {
            return Withhold(ReplayMediaReason.NotSelected);
        }

        // With a publish horizon the uploader schedules a full day onto a later slot (#136),
        // so a notable or high-skill match is only dropped when scheduling is off.
        TimeSpan publishAhead =
            settings == null ? PublicationSchedule.PublishAhead : settings.MaxPublishAhead;
        if (publishAhead <= TimeSpan.Zero && alreadyPublishedInWindow >= dayCap)
        {
            return Withhold(ReplayMediaReason.NotSelected);
        }

        return Admit(decision.PublicationReason);
    }

    private static bool OrdinaryIsOlderThan(ReplayMediaDecision decision, TimeSpan maximumAge)
    {
        if (decision.Priority != ReplayMediaPriority.Ordinary || decision.GameDateUtc == null)
        {
            return false;
        }

        DateTime evaluated = decision.EvaluatedAtUtc;
        DateTime played = decision.GameDateUtc.Value;
        if (evaluated == default || evaluated < played)
        {
            return false;
        }

        return evaluated - played > maximumAge;
    }

    private static PublicationAdmitResult Admit(string reason)
    {
        return new PublicationAdmitResult(true, reason ?? ReplayMediaReason.EligibleAll);
    }

    private static PublicationAdmitResult Withhold(string reason)
    {
        return new PublicationAdmitResult(false, reason ?? ReplayMediaReason.NotSelected);
    }
}
