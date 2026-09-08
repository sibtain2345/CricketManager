using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>Who actually makes a given in-match call. The answer is not always the coach, and that is the point.</summary>
public enum DecisionOwner
{
    /// <summary>The coach set it before the match or from the sidelines - a plan, a field, an instruction.</summary>
    Coach,
    /// <summary>The captain's own call in the middle. He is the one out there and some decisions are only his.</summary>
    Captain,
    /// <summary>Both agreed - the best case, and where a strong partnership shows.</summary>
    Agreed
}

/// <summary>A single in-match decision, with who made it and how well.</summary>
public sealed record CaptaincyDecision(
    DecisionOwner Owner,
    /// <summary>0-100. How good the call was, before luck decides whether it works.</summary>
    double Quality,
    /// <summary>True when the captain overrode or quietly ignored the coach's instruction. Never true under CoachHasFinalSay.</summary>
    bool DepartedFromPlan,
    string Explanation)
{
    /// <summary>
    /// What the player would have preferred, when he wanted something different but the coach's
    /// instruction stood. This is how a captain gets a voice without getting a veto: the coach sees
    /// what his men want and decides whether to change his mind.
    /// </summary>
    public MatchSuggestion? Suggestion { get; init; }
}

/// <summary>
/// A player asking the coach for something in the middle - an extra slip, a change of ends, to be
/// taken off, to keep going. It carries the reasoning and how strongly he feels, so a coach can
/// weigh it rather than just see a notification.
/// </summary>
public sealed record MatchSuggestion(
    Guid PlayerId,
    string PlayerName,
    SuggestionKind Kind,
    /// <summary>0-100. How strongly he feels about it - a senior captain who is certain is worth listening to.</summary>
    double Strength,
    string Request)
{
    /// <summary>Set when the coach's instruction and the player's view differ on something concrete, so a UI can offer a one-click switch.</summary>
    public object? ProposedValue { get; init; }
}

/// <summary>
/// A captain's standing in his own dressing room, and how he handles the job.
///
/// Leadership is deliberately NOT a decorative number here. It drives three separate things:
/// how good the captain's own in-match calls are, how much authority he carries when he departs
/// from the coach's plan, and how much his presence lifts the players around him.
///
/// Crucially it is partly innate and partly earned. A born leader with no experience makes
/// enthusiastic mistakes; a modest leader with 80 matches of captaincy behind him has seen most
/// situations before. That is why `EffectiveLeadership` blends the attribute with matches captained
/// - and why it keeps improving for years rather than being fixed at debut.
/// </summary>
public sealed class CaptaincyProfile
{
    public Guid PlayerId { get; init; }

    /// <summary>Matches captained. The polish on top of the innate attribute.</summary>
    public int MatchesCaptained { get; set; }

    /// <summary>How much the dressing room backs him, 0-100. Built by results and by how he handles people.</summary>
    public double DressingRoomBacking { get; set; } = 50;

    /// <summary>
    /// Innate leadership plus what experience has taught him, 0-100.
    ///
    /// The experience term saturates - the difference between 5 and 40 matches as captain is
    /// enormous, between 120 and 160 almost nothing. Same shape as playing experience, for the
    /// same reason: most of what the job teaches is learned early.
    /// </summary>
    public double EffectiveLeadership(Player player)
    {
        double innate = AbilityScale.AttributeToHundred(player.Mental.Leadership);
        // Divisor 30: about thirty matches in charge teaches most of what the job teaches, which
        // matches how quickly captains are said to grow into it.
        double experience = (1 - Math.Exp(-MatchesCaptained / 30.0)) * 100;

        // Innate dominates, but experience is worth a genuine chunk - and it is the half a coach
        // can actually develop.
        double core = innate * 0.62 + experience * 0.38;

        // A captain the dressing room does not back cannot lead it, however good he is on paper.
        double backing = 0.85 + Math.Clamp(DressingRoomBacking, 0, 100) / 100.0 * 0.3;

        return Math.Clamp(core * backing, 0, 100);
    }

    /// <summary>
    /// How good his tactical calls are in the middle - reading the game, not commanding the room.
    /// Deliberately a different blend from EffectiveLeadership: plenty of inspirational captains
    /// are tactically ordinary, and plenty of shrewd ones command no respect at all.
    /// </summary>
    public double TacticalJudgement(Player player)
    {
        double awareness = AbilityScale.AttributeToHundred(player.Mental.GameAwareness);
        double decisions = AbilityScale.AttributeToHundred(player.Mental.DecisionMaking);
        double composure = AbilityScale.AttributeToHundred(player.Mental.PressureHandling);
        double experience = (1 - Math.Exp(-MatchesCaptained / 30.0)) * 100;

        return Math.Clamp(awareness * 0.32 + decisions * 0.30 + composure * 0.15 + experience * 0.23, 0, 100);
    }

    /// <summary>
    /// The lift a captain gives the men around him, as a multiplier on their effectiveness.
    /// Small on purpose - a great captain is worth a few percent across eleven players, which is
    /// a match over a season and never a match on its own.
    /// </summary>
    public double TeamLift(Player player)
    {
        double leadership = EffectiveLeadership(player);
        return 0.97 + leadership / 100.0 * 0.06; // 0.97x - 1.03x
    }

    public void RecordMatch(bool won, bool handledWell)
    {
        MatchesCaptained++;

        double delta = won ? 2.0 : -1.2;
        if (handledWell) delta += 1.0;

        DressingRoomBacking = Math.Clamp(DressingRoomBacking + delta, 0, 100);
    }

    // ---- Wave 4: the per-match decision-quality accumulator ----

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: running sum of the per-match decision-quality
    /// reading (from MatchSimulator/MultiDayMatchSimulator.RecordCaptaincyOutcomes) since the last
    /// monthly crystallisation. A captain makes dozens of live, attributable calls a match, so the
    /// learning SIGNAL is match-grain - but a slow-moving attribute like Leadership should not
    /// jump per match, so it is banked here and turned into real attribute movement monthly by
    /// CaptaincyGrowthService.Crystallise.
    /// </summary>
    public double DecisionQualityAccumulator { get; private set; }

    /// <summary>Matches banked into the accumulator this crystallisation period - the evidence weight, reset alongside the sum.</summary>
    public int AccumulatedMatches { get; private set; }

    /// <summary>
    /// Adds one match's decision-quality reading (roughly -70..70, the same scale
    /// RecordCaptaincyOutcomes already produces). weight below 1 is for a VICE-captain, who
    /// deputises and watches without owning the calls - real vice-captains visibly develop
    /// leadership before ever getting the top job, so they bank a smaller fraction of the same signal.
    /// </summary>
    public void AccumulateDecisionQuality(double matchRating, double weight = 1.0)
    {
        DecisionQualityAccumulator += matchRating * weight;
        AccumulatedMatches++;
    }

    /// <summary>Clears the accumulator - called by CaptaincyGrowthService.Crystallise once it has read it.</summary>
    public void ResetAccumulator()
    {
        DecisionQualityAccumulator = 0;
        AccumulatedMatches = 0;
    }

    // ---- Wave 6 (suggestion): the captain's own redemption arc ----

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 6 (suggestion): the same shape as a player's
    /// PressureMoment (Wave 2) but for a captain - a public failure in a match his side was
    /// expected to win hangs over him until he answers it. Set by RecordCaptaincyOutcomes on a
    /// heavy/collapse loss as the favourite; cleared, with a real DecisionMaking/PressureHandling
    /// lift, on a subsequent dominant/comeback/upset win - personality-gated on his own composure.
    /// Null the vast majority of the time.
    /// </summary>
    public PressureMoment? PendingBoldFailure { get; private set; }

    public void RecordBoldFailure(DateOnly date, double severity) =>
        PendingBoldFailure = new PressureMoment(date, Math.Clamp(severity, 0, 100),
            (PendingBoldFailure?.SubsequentAttempts ?? -1) + 1);

    public void ClearBoldFailure() => PendingBoldFailure = null;
}

/// <summary>
/// The working relationship between the coach and his captain.
///
/// The design brief is explicit that the captain must not be a dummy while the coach makes every
/// call. This models the actual dynamic: a coach sets plans, a captain executes them in the middle,
/// and how faithfully depends on whether the two men are aligned, how much authority the captain
/// carries, and how good the coach's plan actually looked from out there.
///
/// The consequences run both ways, which is what makes it a relationship rather than a hierarchy:
/// - A strong coach and a strong captain compound. Their calls are better than either alone,
///   because the plan is good AND the man in the middle adapts it intelligently.
/// - Two average men compound in the other direction, and make more mistakes than either would
///   alone - a mediocre plan executed by someone who reads the game poorly.
/// - A great captain rescues a poor coach's plan more often than not.
/// - A great coach's plan is wasted on a captain who neither understands nor backs it.
/// </summary>
public sealed class CoachCaptainRelationship
{
    public Guid CoachId { get; init; }
    public Guid CaptainId { get; init; }

    /// <summary>0-100. How well the two work together. Built by shared success and by the coach's man-management.</summary>
    public double Alignment { get; set; } = 50;

    /// <summary>Times the captain has gone against the coach's plan. Tracked because a pattern of it is itself a story.</summary>
    public int Departures { get; private set; }

    /// <summary>Times a departure turned out to be right. A captain who keeps being proved correct earns the right to keep doing it.</summary>
    public int VindicatedDepartures { get; private set; }

    public void RecordDeparture(bool vindicated)
    {
        Departures++;
        if (vindicated)
        {
            VindicatedDepartures++;
            Alignment = Math.Clamp(Alignment + 0.6, 0, 100); // being right builds trust, oddly enough
        }
        else
        {
            Alignment = Math.Clamp(Alignment - 1.2, 0, 100);
        }
    }

    public void RecordSharedSuccess() => Alignment = Math.Clamp(Alignment + 1.5, 0, 100);
    public void RecordFriction() => Alignment = Math.Clamp(Alignment - 2.0, 0, 100);

    /// <summary>
    /// How likely the captain is to follow a coach instruction rather than back his own read.
    ///
    /// High alignment means he follows even a plan he doubts. Low alignment plus high personal
    /// authority means he does what he thinks is right - which is correct when he is the better
    /// judge and disastrous when he is not.
    /// </summary>
    public double ComplianceProbability(double captainTacticalJudgement, double coachTacticalKnowledge, double coachAuthority)
    {
        // A coach whose plans are visibly good gets followed. So does one with authority.
        double coachStanding = coachTacticalKnowledge * 0.6 + coachAuthority * 0.4;

        // A captain who rates his own read backs it.
        double selfBelief = captainTacticalJudgement;

        double baseline = 0.5 + (coachStanding - selfBelief) / 200.0;   // 0.0 - 1.0 range
        double alignmentEffect = (Alignment - 50) / 100.0 * 0.35;

        return Math.Clamp(baseline + alignmentEffect, 0.15, 0.95);
    }
}

/// <summary>
/// One side's leadership for a match: the captain, his profile, his coach, and how the two of them
/// work together. Passed into the simulation so a side's calls are made by its own people.
///
/// Null throughout is legitimate and means "no modelled leadership" - every existing caller and
/// every AI-versus-AI fixture behaves exactly as it did before, which is what keeps the world
/// simulation cheap.
/// </summary>
public sealed class MatchLeadership
{
    public required Player Captain { get; init; }
    public required CaptaincyProfile Profile { get; init; }
    public Entities.Coach? Coach { get; init; }
    public CoachCaptainRelationship? Relationship { get; init; }

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4 (suggestion): the vice-captain, when one is
    /// modelled. Null throughout is fine (every existing caller). When both this and
    /// ViceCaptainProfile are set, the vice-captain banks a smaller fraction of the same
    /// per-match decision-quality signal the captain does - a real succession pipeline, so the
    /// next captain is not starting from a blank slate the day the job changes hands.
    /// </summary>
    public Player? ViceCaptain { get; init; }
    public CaptaincyProfile? ViceCaptainProfile { get; init; }

    /// <summary>Resolves one in-match call through the captaincy service.</summary>
    public CaptaincyDecision Decide(Services.CaptaincyService service, Services.InMatchDecision decision, bool coachHasAPlan, Random random,
        Enums.DecisionAuthority authority = Enums.DecisionAuthority.Delegated, string? situationKey = null) =>
        service.Decide(decision, Captain, Profile, Coach, Relationship, coachHasAPlan, random, authority, situationKey);

    /// <summary>The lift this captain gives his team-mates. Small, but present on every ball.</summary>
    public double TeamLift => Profile.TeamLift(Captain);

    /// <summary>
    /// Everything the players have asked the coach for during this match, in order. A human coach
    /// reads these between overs and decides what, if anything, to change.
    /// </summary>
    public List<MatchSuggestion> Suggestions { get; } = new();
}
