using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>The kind of call being made, because who owns each one differs.</summary>
public enum InMatchDecision
{
    /// <summary>Who bowls the next over. A shared call in practice, but the captain has the ball in his hand.</summary>
    BowlingChange,
    /// <summary>Where the fielders stand. Shifts constantly in the middle and is very largely the captain's.</summary>
    FieldPlacement,
    /// <summary>The plan for a bowler to a batter. Set by the coach, adapted by the captain.</summary>
    BowlingPlan,
    /// <summary>Batting intent. Mostly the coach's from the sidelines, but a captain batting can override.</summary>
    BattingApproach,
    /// <summary>Bat or bowl. The captain's alone, at the toss, in front of everybody.</summary>
    Toss,
    /// <summary>Declare, enforce the follow-on, take the new ball. Big strategic calls, usually agreed.</summary>
    Strategic
}

/// <summary>
/// Decides who makes each in-match call and how good it is.
///
/// This is the layer the design brief asks for: the coach must not silently make every decision,
/// the captain must not be a dummy, and the quality of both must matter. Two principles run
/// through it:
///
/// 1. **Ownership varies by decision.** Nobody but the captain calls the toss. Field placements
///    shift ball by ball in the middle and are overwhelmingly his. Bowling plans are the coach's
///    work, adapted out there. Batting approach is mostly the coach's from the sidelines. A model
///    where the coach owns everything is as wrong as one where the captain does.
///
/// 2. **Quality compounds.** A strong coach and a strong captain produce better calls than either
///    would alone, because a good plan meets someone who can adapt it. Two average men produce
///    worse ones than either would alone, because a mediocre plan meets someone who misreads it
///    and neither catches the other's mistake. That asymmetry is the whole point - it is why
///    getting both right matters more than getting one right twice.
/// </summary>
public sealed class CaptaincyService
{
    // Situational pattern memory (planning-brief Section D follow-up): reuses Player.Matchups via
    // MatchupConfidenceService rather than a new, unpersisted field on CaptaincyProfile -
    // CaptaincyProfile itself has no repository/persistence path anywhere in this codebase yet
    // (confirmed by grep before adding this - MatchesCaptained/DressingRoomBacking are already in
    // the same boat, ephemeral unless a caller manages them), whereas Player IS a real persisted
    // entity and this is exactly the shape PartnershipChemistryService already proved out for a
    // different pairing (batting partners instead of match situations).
    private readonly MatchupConfidenceService _situationMemory = new();

    /// <summary>
    /// A coarse, reusable key for "have I actually been in a situation like THIS before" - format,
    /// phase, chasing-or-defending, and a pressure tier bucketed from PressureLevel rather than the
    /// raw number, so two situations that are practically identical (pressure 61 vs 64) share a key
    /// instead of the memory being sliced too thin to ever accumulate a real sample. Deliberately
    /// this coarse: a captain "remembering" a specific ball-by-ball sequence would be a lookup-table
    /// win-button, not the slow, bounded pattern recognition the brief actually asks for.
    /// </summary>
    public static string BuildSituationKey(MatchFormat format, MatchPhase phase, bool chasing, double pressureLevel)
    {
        string tier = pressureLevel switch { >= 70 => "high", >= 40 => "medium", _ => "low" };
        return $"{format}:{phase}:{(chasing ? "chase" : "defend")}:{tier}";
    }

    /// <summary>
    /// How much a captain's read of THIS kind of situation is coloured by having been here before -
    /// deliberately a much smaller swing than an ordinary matchup (MatchupConfidenceService's own
    /// 0.85-1.15 range damped further to roughly 0.955-1.045), because this nudges confidence in a
    /// recurring situation, not raw tactical ability, and the brief is explicit that this kind of
    /// learning "must be slow, bounded, and must never escape a ceiling."
    /// </summary>
    public double SituationConfidenceMultiplier(Player captain, string situationKey)
    {
        double raw = _situationMemory.GetMatchupMultiplier(captain, MatchupKey.ForSituation(situationKey));
        return 1 + (raw - 1) * 0.3;
    }

    /// <summary>
    /// Records how one situation actually turned out for this captain, so the next time he faces
    /// something like it his read carries a little more (or less) conviction. Called once per match
    /// per side that had a real MatchLeadership, from the point the result is known.
    /// </summary>
    public void RecordSituationOutcome(Player captain, string situationKey, double outcomeRating) =>
        _situationMemory.RecordOutcome(captain, MatchupKey.ForSituation(situationKey), outcomeRating);

    /// <summary>
    /// Post-Phase-6 carry-forward: moves each XI player's trust in the captain a little, on how
    /// his calls went this match (roughly -70..70) and the result. Small and bounded - one match
    /// nudges it a few points; a real relationship (or its collapse) takes a run of them.
    /// </summary>
    public static void ApplyCaptainTrust(IReadOnlyList<Player> side, double matchDecisionRating, bool won)
    {
        double delta = matchDecisionRating / 22.0 + (won ? 1.0 : -0.6);
        foreach (var p in side)
            p.CaptainTrust = Math.Clamp(p.CaptainTrust + delta, 0, 100);
    }

    /// <summary>The squad's average trust in whoever is captain right now - what "does the room back him" reads off, alongside DressingRoomBacking.</summary>
    public static double SquadCaptainTrust(IReadOnlyList<Player> squad) =>
        squad.Count == 0 ? 50 : squad.Average(p => p.CaptainTrust);

    /// <summary>
    /// How much of this decision belongs to the captain rather than the coach, 0-1. These are the
    /// realistic splits: a coach cannot call a toss, and cannot move a fielder ten yards squarer
    /// between deliveries either.
    /// </summary>
    public double CaptainOwnership(InMatchDecision decision) => decision switch
    {
        InMatchDecision.Toss => 1.00,
        InMatchDecision.FieldPlacement => 0.75,
        InMatchDecision.BowlingChange => 0.60,
        InMatchDecision.Strategic => 0.45,
        InMatchDecision.BowlingPlan => 0.35,
        InMatchDecision.BattingApproach => 0.25,
        _ => 0.5
    };

    /// <summary>
    /// Resolves one in-match decision: who made it, how good it was, and whether the captain went
    /// against his instructions.
    /// </summary>
    /// <param name="authority">
    /// Who has the final say. Under CoachHasFinalSay the captain CANNOT depart from the
    /// instruction - he raises a suggestion instead, which the coach can act on between overs.
    /// That is the whole point of a management game: the decisions are the coach's, and a captain
    /// who quietly overrules him takes the game away from the player.
    /// </param>
    public CaptaincyDecision Decide(
        InMatchDecision decision,
        Player captain,
        CaptaincyProfile captaincy,
        Coach? coach,
        CoachCaptainRelationship? relationship,
        bool coachHasAPlan,
        Random random,
        DecisionAuthority authority = DecisionAuthority.Delegated,
        // Situational pattern memory: null (every pre-existing call site) leaves behaviour exactly
        // as it was. Supplied, it nudges TacticalJudgement by how this captain's recollection of
        // similar situations has gone - a single injection point so every downstream branch
        // (pressure noise, compliance, the final quality figure) benefits without each needing its
        // own read.
        string? situationKey = null)
    {
        double captainJudgement = captaincy.TacticalJudgement(captain)
            * (situationKey is null ? 1.0 : SituationConfidenceMultiplier(captain, situationKey));
        double coachJudgement = coach is null ? 0 : CoachTacticalQuality(coach);

        // No coach at all, or no plan for this decision - it is the captain's, alone.
        if (coach is null || !coachHasAPlan)
        {
            double quality = ApplyPressureNoise(captainJudgement, captain, random);
            return new CaptaincyDecision(DecisionOwner.Captain, quality, false,
                $"{captain.FullName} makes the call himself.");
        }

        double ownership = CaptainOwnership(decision);

        // The coach has the final say. The instruction is carried out - but if the captain would
        // have done something else, he says so, and the coach decides what to do about it.
        if (authority is DecisionAuthority.CoachHasFinalSay or DecisionAuthority.Consult)
        {
            double executionQuality = ApplyPressureNoise(
                coachJudgement * (0.75 + captainJudgement / 100.0 * 0.45), captain, random);

            var followed = new CaptaincyDecision(DecisionOwner.Coach, executionQuality, false,
                $"{captain.FullName} carries out the coach's instruction.");

            // Would he have preferred something else? A captain who reads the game better than the
            // plan does will often want to - and the stronger his own read, the harder he pushes.
            if (captainJudgement > coachJudgement + 8)
            {
                double strength = Math.Clamp(captainJudgement - coachJudgement, 0, 100);
                return followed with
                {
                    Explanation = $"{captain.FullName} carries out the instruction, but he wants a change.",
                    Suggestion = new MatchSuggestion(captain.Id, captain.FullName,
                        SuggestionFor(decision), strength,
                        DescribeRequest(decision, captain))
                };
            }

            return followed;
        }

        // Delegated. Does he go with the instruction, or back his own read?
        double compliance = relationship?.ComplianceProbability(
            captainJudgement, coachJudgement, coach.Authority) ?? 0.7;

        // Ownership shifts it: nobody follows a coach's instruction on a toss call, and almost
        // everybody follows one on batting approach.
        compliance = Math.Clamp(compliance - (ownership - 0.5) * 0.45, 0.05, 0.97);

        bool follows = random.NextDouble() < compliance;

        if (follows)
        {
            // The plan is the coach's, but it is executed out there by the captain. A sharp captain
            // adds to a good plan; a poor one loses some of it. THIS is where two good men compound.
            double executionFactor = 0.75 + captainJudgement / 100.0 * 0.45; // 0.75x - 1.20x
            double quality = ApplyPressureNoise(coachJudgement * executionFactor, captain, random);

            var owner = captainJudgement >= 60 && coachJudgement >= 60 ? DecisionOwner.Agreed : DecisionOwner.Coach;

            return new CaptaincyDecision(owner, quality, false,
                owner == DecisionOwner.Agreed
                    ? $"{captain.FullName} and the coach are of one mind on this."
                    : $"{captain.FullName} goes with the coach's instruction.");
        }

        // He backs himself. Whether that is wisdom or arrogance depends entirely on whether he is
        // the better judge - and the simulation does not protect him from being wrong.
        double ownQuality = ApplyPressureNoise(captainJudgement, captain, random);
        relationship?.RecordDeparture(vindicated: captainJudgement > coachJudgement);

        return new CaptaincyDecision(DecisionOwner.Captain, ownQuality, true,
            captainJudgement > coachJudgement
                ? $"{captain.FullName} backs his own read - and he sees it better than the plan did."
                : $"{captain.FullName} goes against the plan, and it is not his best moment.");
    }

    private static SuggestionKind SuggestionFor(InMatchDecision decision) => decision switch
    {
        InMatchDecision.FieldPlacement => SuggestionKind.FieldChange,
        InMatchDecision.BowlingChange => SuggestionKind.BowlingChange,
        InMatchDecision.BowlingPlan => SuggestionKind.BowlingPlanChange,
        InMatchDecision.BattingApproach => SuggestionKind.BattingApproachChange,
        InMatchDecision.Toss => SuggestionKind.TossDecision,
        _ => SuggestionKind.FieldChange
    };

    private static string DescribeRequest(InMatchDecision decision, Player captain) => decision switch
    {
        InMatchDecision.FieldPlacement => $"{captain.FullName} wants to move the field.",
        InMatchDecision.BowlingChange => $"{captain.FullName} wants a different bowler on.",
        InMatchDecision.BowlingPlan => $"{captain.FullName} thinks a different plan would work better here.",
        InMatchDecision.BattingApproach => $"{captain.FullName} wants to change the approach.",
        InMatchDecision.Toss => $"{captain.FullName} would call it the other way.",
        _ => $"{captain.FullName} would do this differently."
    };

    /// <summary>
    /// A coach's tactical quality for in-match purposes. Deliberately weighted towards reading a
    /// live game rather than knowing cricket in general - plenty of deeply knowledgeable coaches
    /// are poor in the moment.
    /// </summary>
    public double CoachTacticalQuality(Coach coach)
    {
        var a = coach.Attributes;
        return Math.Clamp(
            AbilityScale.AttributeToHundred(a.TacticalKnowledge) * 0.30
            + AbilityScale.AttributeToHundred(a.MatchReading) * 0.28
            + AbilityScale.AttributeToHundred(a.TacticalAnalysis) * 0.18
            + AbilityScale.AttributeToHundred(a.DecisionMaking) * 0.14
            + AbilityScale.AttributeToHundred(a.PressureHandling) * 0.10, 0, 100);
    }

    /// <summary>
    /// The combined quality of a coach-captain pair over a match, which is what a season actually
    /// turns on.
    ///
    /// Superlinear at the top and subadditive at the bottom, on purpose. Two good men reinforce
    /// each other; two ordinary ones miss what the other misses. This is the exact effect the
    /// design brief describes - a good coach AND a good captain are both needed for success, and
    /// average ones make more mistakes than the average of their parts.
    /// </summary>
    public double PartnershipQuality(Player captain, CaptaincyProfile captaincy, Coach? coach, CoachCaptainRelationship? relationship)
    {
        double captainQuality = captaincy.TacticalJudgement(captain) * 0.6
                                + captaincy.EffectiveLeadership(captain) * 0.4;

        if (coach is null) return captainQuality * 0.85; // a side with no coach leans entirely on its captain

        double coachQuality = CoachTacticalQuality(coach);
        double average = (captainQuality + coachQuality) / 2;

        // The compounding term. Positive when both are good, negative when both are ordinary.
        double both = (captainQuality / 100.0) * (coachQuality / 100.0);
        double compounding = (both - 0.25) * 24; // zero at 50/50, +18 at 100/100, -6 at 0/0

        double alignment = relationship is null ? 0 : (relationship.Alignment - 50) / 100.0 * 8;

        return Math.Clamp(average + compounding + alignment, 0, 100);
    }

    /// <summary>
    /// How often a coach-captain pair simply gets a decision wrong over a match. The brief asks for
    /// average pairs to make MORE mistakes, and this is where that lives: the curve is steep at the
    /// bottom, so the gap between a poor pair and an average one is larger than between an average
    /// pair and a good one.
    /// </summary>
    public double MistakeRate(double partnershipQuality)
    {
        double quality = Math.Clamp(partnershipQuality, 0, 100) / 100.0;
        return Math.Clamp(0.34 * Math.Pow(1 - quality, 1.5) + 0.03, 0.03, 0.40);
    }

    /// <summary>
    /// Pressure and temperament noise on a decision. A captain who handles pressure badly makes
    /// worse calls when it matters, which is exactly when calls matter.
    /// </summary>
    private static double ApplyPressureNoise(double baseQuality, Player captain, Random random)
    {
        double composure = AbilityScale.AttributeToHundred(captain.Mental.PressureHandling);
        double spread = 18 - composure / 100.0 * 10; // +/-8 for the unflappable, +/-18 for the fragile

        double noise = (random.NextDouble() * 2 - 1) * spread;
        return Math.Clamp(baseQuality + noise, 0, 100);
    }

    /// <summary>
    /// Picks a captain from a squad the way a selection panel would: leadership first, but weighted
    /// by whether he is worth his place and how much he has led before. A brilliant leader who
    /// cannot get in the side is not the captain.
    /// </summary>
    public Player? SelectCaptain(IEnumerable<Player> squad, IReadOnlyDictionary<Guid, CaptaincyProfile>? profiles = null) =>
        squad.Where(p => !p.IsRetired)
            .OrderByDescending(p =>
            {
                double leadership = AbilityScale.AttributeToHundred(p.Mental.Leadership);
                double judgement = AbilityScale.AttributeToHundred(p.Mental.GameAwareness) * 0.5
                                   + AbilityScale.AttributeToHundred(p.Mental.DecisionMaking) * 0.5;
                double standing = AbilityScale.CompositeAbilityToHundred(p.CurrentAbility);

                double experience = profiles is not null && profiles.TryGetValue(p.Id, out var profile)
                    ? (1 - Math.Exp(-profile.MatchesCaptained / 30.0)) * 100
                    : p.Experience.Level;

                return leadership * 0.42 + judgement * 0.22 + standing * 0.21 + experience * 0.15;
            })
            .FirstOrDefault();
}
