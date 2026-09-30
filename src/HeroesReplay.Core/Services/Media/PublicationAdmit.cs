using System;
using HeroesReplay.Core.Services.YouTube;

namespace HeroesReplay.Core.Services.Media;

public readonly record struct PublicationAdmitResult(bool Allow, string Reason);

/// <summary>
/// Curated ordinary matches stay local. An ordinary match older than three days
/// stays local even in AllEligible. Notable and high-skill matches are admitted
/// only while the rolling day is under the publication cap. A requested match stays eligible.
/// </summary>
public static class PublicationAdmit
{
    public static PublicationAdmitResult Decide(
        ReplayMediaDecision decision,
        int alreadyPublishedInWindow
    )
    {
        if (decision == null || !decision.PublicationCandidate)
        {
            return Withhold(decision == null ? null : decision.PublicationReason);
        }

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
            if (OrdinaryIsOlderThanThreeDays(decision))
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

        if (alreadyPublishedInWindow >= PublicationSchedule.MaxPublicPerDay)
        {
            return Withhold(ReplayMediaReason.NotSelected);
        }

        return Admit(decision.PublicationReason);
    }

    private static bool OrdinaryIsOlderThanThreeDays(ReplayMediaDecision decision)
    {
        if (
            decision.Priority != ReplayMediaPriority.Ordinary
            || decision.GameDateUtc == null
        )
        {
            return false;
        }

        DateTime evaluated = decision.EvaluatedAtUtc;
        DateTime played = decision.GameDateUtc.Value;
        if (evaluated == default || evaluated < played)
        {
            return false;
        }

        return evaluated - played > PublicationSchedule.OrdinaryMaxAge;
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
