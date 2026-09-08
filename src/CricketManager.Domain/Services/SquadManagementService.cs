using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>A recommended squad-management response, with the reasoning a news/inbox system can quote.</summary>
public sealed record SquadDecisionAssessment(Guid PlayerId, SquadDecisionType Type, string Reason);

/// <summary>
/// The response to a player's decline that RetirementService's binary (keep picking him /
/// his career is over) can't express - see the post-Phase-4 planning brief, Section A. A
/// genuinely prolonged decline in an established player should produce a real management
/// decision (a domestic roadmap back, or a deliberate break to reset - the real-world "a
/// month away, then back in form" case), never a flat drop and never indefinite selection on
/// reputation alone. A short bad patch should produce no decision at all - that is the entire
/// point of PlayerSelectionEvaluator's separate established-player leniency, which already
/// protects a short slump without needing this service to intervene.
///
/// Deliberately separate from RetirementService: this decides whether a player keeps his
/// first-team PLACE, not whether his CAREER is over. A caller should check retirement first
/// (WorldClockService's annual rollover does exactly that) - a player about to retire has no
/// use for a roadmap back into the side.
///
/// "Prolonged" is deliberately the SAME threshold (8 consecutive sub-threshold performances)
/// PlayerSelectionEvaluator uses to fade established-player protection to zero - the two
/// systems have to agree on what "prolonged" means, or a player could lose selection
/// protection while this service still insists nothing is wrong, or vice versa.
/// </summary>
public sealed class SquadManagementService
{
    private readonly PlayerMoraleService _morale = new();

    private const double ProlongedDeclineThreshold = -25;
    private const int ProlongedStreakLength = 8;

    /// <summary>
    /// FormState.ConsecutivePoorPerformances can never exceed 10 - FormState's own rolling
    /// window is capped at 10 recent performances (see FormState.RecordPerformance). A real
    /// bug caught by a test that should have triggered and never did: the first version of
    /// this threshold was ProlongedStreakLength * 2 = 16, which is mathematically unreachable
    /// and meant a DevelopmentProspect could NEVER get a decline response, however long his
    /// run of failures actually ran. "Backed roughly twice as long" within a hard 10-entry
    /// ceiling honestly means "backed until his entire visible recent history is poor" - still
    /// a real, meaningfully higher bar than an established player's 8, just not a literal
    /// doubling, because a literal doubling isn't expressible by the data this reads.
    /// </summary>
    private const int ProlongedStreakLengthForDevelopment = 10;

    /// <summary>
    /// Null means no intervention is warranted - the common case, and the correct outcome for
    /// a short bad patch on anyone. Only established players (FirstChoice/SecondChoice/
    /// LongTermProject/ReturningFromInjury) get Roadmap/Rest, and only once the decline is
    /// genuinely prolonged; a Backup/Fringe/EmergencyReplacement player has no reputation
    /// cushion to spend and is Dropped far sooner - "a much lower bar for replacement", per
    /// the brief. A DevelopmentProspect is backed through a materially longer run of failures
    /// before anything is recommended at all (see ProlongedStreakLengthForDevelopment) -
    /// losing his chance after a few bad games is exactly the failure mode a development
    /// pathway exists to avoid - and is never straight-Dropped by this method at all.
    ///
    /// Crossing the streak threshold doesn't trigger a decision on the spot - that would be
    /// exactly the "knee-jerk trigger" the planning brief explicitly warns against (its
    /// wording is aimed at coach/captain removal, but the same failure mode - a single data
    /// point flipping a switch - applies here too, and this codebase already avoids it
    /// elsewhere: see the ground-deterioration cliff-edge discussion in CLAUDE.md). Probability
    /// instead climbs gradually the further the streak runs past the threshold, rolled against
    /// `random` - the same "probability, not a flag" discipline RetirementService already uses,
    /// deliberately mirrored rather than reinvented.
    /// </summary>
    public SquadDecisionAssessment? AssessDeclineResponse(Player player, Random random)
    {
        if (player.IsRetired || player.CurrentSquadDecision is not null) return null;

        int poorStreak = player.Form.ConsecutivePoorPerformances(ProlongedDeclineThreshold);
        double standing = Math.Max(player.Reputation.Domestic, player.Reputation.Continental);

        bool isDevelopmentProspect = player.SquadStatus == SquadStatus.DevelopmentProspect;
        bool isEstablished = player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice
            or SquadStatus.LongTermProject or SquadStatus.ReturningFromInjury;

        int softMinimum = isDevelopmentProspect ? ProlongedStreakLengthForDevelopment
                         : isEstablished ? ProlongedStreakLength
                         : ProlongedStreakLength / 2; // Backup/Fringe/EmergencyReplacement

        if (poorStreak < softMinimum) return null;

        double probability = Math.Clamp((poorStreak - softMinimum + 1) * 0.15, 0, 0.9);
        if (random.NextDouble() >= probability) return null;

        if (isDevelopmentProspect)
            return new SquadDecisionAssessment(player.Id, SquadDecisionType.Roadmap,
                $"{player.FullName} keeps his development chance but is sent back to domestic cricket to work on his game.");

        if (isEstablished)
        {
            // Reputation alone cannot protect forever - an established player with nothing
            // left to point to besides his name is exactly the case the brief says should
            // eventually be droppable.
            if (standing < 40)
                return new SquadDecisionAssessment(player.Id, SquadDecisionType.Drop,
                    $"{player.FullName} loses his place after a prolonged loss of form, with no wider reputation left to fall back on.");

            // Whether a structured return path or simply stepping away suits him better is a
            // temperament question, not a form question - both are genuine second chances,
            // never a flat drop, at this stage.
            bool prefersStructuredReturn = player.Personality.HasFlag(PersonalityTrait.Professional)
                                         || player.Personality.HasFlag(PersonalityTrait.Ambitious);
            var type = prefersStructuredReturn ? SquadDecisionType.Roadmap : SquadDecisionType.Rest;
            string reason = type == SquadDecisionType.Roadmap
                ? $"{player.FullName} is given a domestic roadmap back into the side after a prolonged loss of form."
                : $"{player.FullName} is given time away from the game to reset before an expected return.";
            return new SquadDecisionAssessment(player.Id, type, reason);
        }

        return new SquadDecisionAssessment(player.Id, SquadDecisionType.Drop,
            $"{player.FullName} is dropped after repeated failures with no strong claim to a place.");
    }

    /// <summary>
    /// Applies an assessment - the explicit event, same pattern as RetirementService.Retire
    /// and PlayerAvailabilityService.ApplyInjury. A Drop is a one-time event (SquadStatus
    /// falls to Fringe, nothing ongoing to track); Roadmap/Rest carry a real unavailability
    /// period, recorded on Player.CurrentSquadDecision and reviewed later by ReviewDecision.
    /// </summary>
    public void Apply(Player player, SquadDecisionAssessment assessment, DateOnly decidedDate)
    {
        _morale.AdjustForSquadDecision(player, assessment.Type);

        if (assessment.Type == SquadDecisionType.Drop)
        {
            player.SquadStatus = SquadStatus.Fringe;
            return;
        }

        // Season-scale reviews, deliberately: a roadmap is "the rest of a domestic season",
        // a rest is "a couple of months away", matching the real-world timeframes the brief's
        // own example describes rather than a week-by-week check nothing in this codebase
        // currently drives anyway (annual rollover is the only caller today - see
        // WorldClockService).
        var reviewDate = assessment.Type == SquadDecisionType.Roadmap ? decidedDate.AddMonths(6) : decidedDate.AddMonths(2);

        player.CurrentSquadDecision = new SquadDecision
        {
            Type = assessment.Type,
            Reason = assessment.Reason,
            DecidedDate = decidedDate,
            ReviewDate = reviewDate
        };

        player.NonInjuryUnavailability = assessment.Type == SquadDecisionType.Roadmap
            ? UnavailabilityReason.OnDevelopmentAssignment
            : UnavailabilityReason.Resting;
    }

    /// <summary>
    /// Reviews an active Roadmap/Rest once its review date has passed - clears it and restores
    /// availability. Explicit and caller-driven rather than implicit-on-date, the same
    /// "a return is a real event other systems can react to" reasoning as
    /// PlayerAvailabilityService.MarkRecovered. Safe to call on every player every rollover -
    /// it is a no-op unless a decision is both present and due.
    /// </summary>
    public void ReviewDecision(Player player, DateOnly asOf)
    {
        var decision = player.CurrentSquadDecision;
        if (decision?.ReviewDate is null || asOf < decision.ReviewDate) return;

        if (player.NonInjuryUnavailability is UnavailabilityReason.OnDevelopmentAssignment or UnavailabilityReason.Resting)
            player.NonInjuryUnavailability = UnavailabilityReason.Available;

        player.CurrentSquadDecision = null;
    }
}
