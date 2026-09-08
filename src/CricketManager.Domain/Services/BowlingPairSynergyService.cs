using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// The bowling-side half of Sections Q/W - two bowlers who have shared the new ball before and
/// contained well together should feel like a genuine pairing when reunited, the same way two
/// batters with a history of running well together do (PartnershipChemistryService).
///
/// Deliberately scoped to the NEW-BALL PAIRING only, not general bowler-to-bowler synergy across
/// a whole attack - the new-ball pair is the one bowling relationship real cricket actually names
/// (an opening pair "feeding off each other"), and it is the one place a shared history has a
/// natural, well-understood consequence: building pressure together early, before the batters have
/// settled. Reuses Player.Matchups via MatchupConfidenceService rather than a parallel type, the
/// exact same call PartnershipChemistryService already made for the batting side.
/// </summary>
public sealed class BowlingPairSynergyService
{
    private readonly MatchupConfidenceService _matchups = new();

    /// <summary>
    /// Records how well two bowlers contained the new-ball phase TOGETHER in one innings - the
    /// same shared-outcome idea PartnershipChemistryService.RecordPartnership already uses for two
    /// batters, applied to the bowling side of the same pattern.
    /// </summary>
    public void RecordNewBallPairing(Player bowlerA, Player bowlerB, double combinedEconomy, MatchFormat format, int wicketsTakenTogether)
    {
        double rating = RatePairing(combinedEconomy, format, wicketsTakenTogether);
        _matchups.RecordOutcome(bowlerA, MatchupKey.ForBowlingPartner(bowlerB.Id), rating);
        _matchups.RecordOutcome(bowlerB, MatchupKey.ForBowlingPartner(bowlerA.Id), rating);
    }

    private static double RatePairing(double combinedEconomy, MatchFormat format, int wicketsTakenTogether)
    {
        double parEconomy = format switch { MatchFormat.Test => 3.0, MatchFormat.ODI => 5.0, _ => 7.8 };

        // Economy BELOW par is good for the pairing - the sign is deliberately inverted from the
        // batting side's tempo read (there, faster than par is good; here, more economical is).
        double rating = Math.Clamp((parEconomy - combinedEconomy) / parEconomy, -1, 1) * 35;
        rating += Math.Clamp(wicketsTakenTogether, 0, 4) * 8; // a wicket in the shared spell is a real, direct signal

        return Math.Clamp(rating, -100, 100);
    }

    /// <summary>
    /// The lift (or drag) this specific pairing gives a bowler during the new-ball phase, from
    /// their shared history - a small, bounded multiplier (0.94x-1.08x, deliberately smaller than
    /// PartnershipChemistryService's batting-side swing) since this is a secondary, contextual
    /// factor on top of each bowler's own skill, not a claim that pairing alone wins overs.
    /// No shared history, or a thin sample, stays neutral.
    /// </summary>
    public double GetSynergyMultiplier(Player bowler, Guid partnerBowlerId)
    {
        if (!bowler.Matchups.TryGetValue(MatchupKey.ForBowlingPartner(partnerBowlerId), out var chemistry) || chemistry.SampleCount == 0)
            return 1.0;

        double sampleWeight = Math.Clamp(chemistry.SampleCount / 5.0, 0.2, 1.0);
        double swing = (chemistry.Confidence / 100.0) * 0.08 * sampleWeight;
        return Math.Clamp(1.0 + swing, 0.94, 1.08);
    }
}
