using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>One coach's season, assessed - what it did to his standing, and whether it cost him the job.</summary>
/// <summary>Job-market follow-up: the board's renewal call on an expiring contract - see CoachCareerService.EvaluateRenewal.</summary>
public sealed record RenewalDecision(bool Renewed, string Reason);

public sealed record CoachSeasonReview(
    /// <summary>-1 (disastrous underachievement) .. +1 (genuine overachievement) against what the team's own Strength/Reputation predicted.</summary>
    double PerformanceScore,
    double BoardTrustDelta,
    bool Dismissed,
    string Reason);

/// <summary>
/// Section D: a coach's own standing and job security. Before this, Coach.Reputation was set
/// once at creation (SeedAttributesFromHistory) and never moved again; BoardTrust and
/// CareerSatisfaction were initialised and never written by anything at all (confirmed by a
/// full-codebase grep before touching this - the same "dead attribute" bug class this project's
/// history already names for RoleTraitDeriver staleness); Authority was READ by
/// CaptaincyService.Decide but had no writer either. ContractStatus.Terminated existed as an
/// enum value with no code path that ever produced it - a contract only ever expired on its own
/// end date, however the season actually went, and there was no such thing as being sacked.
///
/// Deliberately does NOT attempt to evaluate CoachingContract.ObjectivesByYear's free text
/// ("Finish top 4") - that is prose written for a human to read, not a structured target this
/// code could honestly check off without guessing at what satisfies it, and guessing would be
/// exactly the "looks like insight and isn't" trap this project's own post-match-analysis slice
/// explicitly declined to fall into for a similar reason. Instead this reads the same real,
/// measurable signal the rest of the codebase already leans on for "did this side do what it
/// should have": table position and win rate against what the team's own Strength/Reputation
/// predicted - the same expectation-vs-actual shape MatchResultContextService's upset detection
/// already uses, so a rich club's coach is not credited with his squad's talent and a threadbare
/// one is not blamed for his squad's weakness.
/// </summary>
public sealed class CoachCareerService
{
    private readonly CompetitionProgressionService _progression = new();

    /// <summary>Below this, a coach's job is genuinely at risk - not fired instantly, but the clock has started. Wave 7: this is the BASE floor; EffectiveDismissalFloor lowers it for a coach with a genuine track record.</summary>
    public const double DismissalTrustFloor = 22;

    /// <summary>Below this, a coach is genuinely unhappy enough that a real, probabilistic chance of resignation applies.</summary>
    public const double ResignationSatisfactionFloor = 20;

    /// <summary>
    /// A -1..1 read of one season: how the team's actual win rate compared to what its own
    /// Strength/Reputation would predict. Zero when nothing was played this year - no verdict to
    /// reach, not a neutral judgement being made.
    /// </summary>
    public double ScoreSeason(Team team, IReadOnlyList<CompetitionStanding> standingsThisYear)
    {
        double totalPlayed = standingsThisYear.Sum(s => s.Played);
        if (totalPlayed <= 0) return 0;

        double actualWinRate = standingsThisYear.Sum(s => s.Won) / totalPlayed;

        // What a side of this Strength/Reputation should win roughly as often in a competitive
        // field - gentle on purpose: even a genuinely dominant side (Strength 90+) is only
        // expected to win a bit more than half its matches, because cricket has no sure things.
        // Strength and Reputation.Domestic are both already on the 0-100 scale, so this is a
        // plain weighted average, not a conversion - AbilityScale exists for the 1-20/1-200
        // scales, neither of which applies here.
        double standing = (team.Strength + team.Reputation.Domestic * 2) / 3;
        double expectedWinRate = 0.30 + Math.Clamp(standing, 0, 100) / 100.0 * 0.40; // 0.30 - 0.70

        return Math.Clamp((actualWinRate - expectedWinRate) / 0.35, -1, 1);
    }

    /// <summary>
    /// Board-objectives follow-up: whether each STRUCTURED target for this contract year was
    /// actually met, read from the real standings data - the honest, checkable counterpart to
    /// ObjectivesByYear's free text this class's own doc comment explains why it never evaluates.
    /// `seasonsThisYear` is every CompetitionSeason that concluded this year (not filtered to this
    /// team - GetPosition/ChampionTeamId/PlayoffQualifiedTeamIds all need the FULL table), while
    /// `standingsThisYear` is this team's own rows across them (the same pre-filtered shape
    /// ScoreSeason already takes), needed only for MinimumWinRate, the one type with no specific
    /// competition to look up.
    /// </summary>
    public IReadOnlyList<BoardObjectiveResult> EvaluateObjectives(
        IReadOnlyList<BoardObjective> objectives, Team team,
        IReadOnlyList<CompetitionSeason> seasonsThisYear, IReadOnlyList<CompetitionStanding> standingsThisYear,
        int youngPlayersBlooded = 0, double? netFinancialResult = null)
    {
        var results = new List<BoardObjectiveResult>();

        foreach (var objective in objectives)
        {
            // Phase 12 (§13.4): objectives beyond results.
            if (objective.Type is BoardObjectiveType.DevelopYouth or BoardObjectiveType.BalanceTheBooks or BoardObjectiveType.GrowFanbase)
            {
                var (metX, reasonX) = objective.Type switch
                {
                    BoardObjectiveType.DevelopYouth => (youngPlayersBlooded >= objective.TargetValue,
                        $"{objective.Describe()} - {youngPlayersBlooded} played senior cricket this year."),
                    BoardObjectiveType.BalanceTheBooks => (netFinancialResult is >= 0 || (netFinancialResult is null && team.Finances.Budget >= 0),
                        $"{objective.Describe()} - net result {(netFinancialResult ?? team.Finances.Budget):N0}."),
                    _ => (team.Board.FanSentiment >= objective.TargetValue,
                        $"{objective.Describe()} - fan sentiment is {team.Board.FanSentiment:F0}."),
                };
                results.Add(new BoardObjectiveResult(objective, metX, reasonX));
                continue;
            }

            if (objective.Type == BoardObjectiveType.MinimumWinRate)
            {
                double played = standingsThisYear.Sum(s => s.Played);
                double won = standingsThisYear.Sum(s => s.Won);
                bool metRate = played > 0 && won / played * 100 >= objective.TargetValue;
                string rateReason = played <= 0
                    ? $"{objective.Describe()} - no matches were played this year, so this cannot honestly be judged met."
                    : $"{objective.Describe()} - actually won {won / played * 100:F0}%.";
                results.Add(new BoardObjectiveResult(objective, metRate, rateReason));
                continue;
            }

            var season = objective.CompetitionId is { } competitionId
                ? seasonsThisYear.FirstOrDefault(s => s.CompetitionId == competitionId)
                : null;

            if (season is null)
            {
                results.Add(new BoardObjectiveResult(objective, false, $"{objective.Describe()} - the competition this targeted was not played this year."));
                continue;
            }

            int? position = _progression.GetPosition(season, team.Id);
            bool met = objective.Type switch
            {
                BoardObjectiveType.FinishTopN => position is { } p && p <= objective.TargetValue,
                BoardObjectiveType.AvoidBottomN => position is { } p && p <= season.Standings.Count - objective.TargetValue,
                BoardObjectiveType.WinCompetition => season.IsCompleted && season.ChampionTeamId == team.Id,
                // A completed knockout stage records real qualifiers; before that has happened,
                // fall back to reading the position a top-N cutoff would need.
                BoardObjectiveType.ReachPlayoffs => season.PlayoffQualifiedTeamIds.Count > 0
                    ? season.PlayoffQualifiedTeamIds.Contains(team.Id)
                    : position is { } p2 && p2 <= objective.TargetValue,
                _ => false
            };

            string reason = position is { } pos
                ? $"{objective.Describe()} - finished {Ordinal(pos)}."
                : $"{objective.Describe()} - did not feature in the final standings.";

            results.Add(new BoardObjectiveResult(objective, met, reason));
        }

        return results;
    }

    private static string Ordinal(int n) => (n % 100 is >= 11 and <= 13) ? $"{n}th" : (n % 10) switch
    {
        1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th"
    };

    /// <summary>
    /// Moves BoardTrust, Authority and Reputation on the season's own merit, and - only once
    /// trust has genuinely collapsed - rolls a real, probabilistic chance of the sack. Probability
    /// rather than a boolean cliff-edge, the same discipline RetirementService and
    /// SquadManagementService already use: a coach ten points under the floor is at real risk, not
    /// automatically gone the moment he crosses it.
    /// </summary>
    /// <param name="objectiveResults">
    /// Board-objectives follow-up: the STRUCTURED targets actually set for this contract year,
    /// already evaluated by EvaluateObjectives - null/empty (every pre-existing call site) leaves
    /// behaviour exactly as it was, judged only by the implicit performanceScore reading. When
    /// supplied, meeting or missing an EXPLICITLY stated target moves trust further than an
    /// implicit "about what was expected" reading would - the board is not merely disappointed,
    /// it was told what it wanted and did not get it (or did).
    /// </param>
    public CoachSeasonReview EvaluateSeason(
        Coach coach, CoachingContract contract, Team team, double performanceScore, int seasonsAtClub, Random random,
        IReadOnlyList<BoardObjectiveResult>? objectiveResults = null)
    {
        // Wave 4: a high-reputation coach at a big club is under a magnifying glass - his trust
        // swings are amplified in both directions.
        double scrutiny = ScrutinyFactor(coach, team);

        double trustDelta = performanceScore * 9 * scrutiny; // gradual - a single season should never swing this alone
        coach.BoardTrust = Math.Clamp(coach.BoardTrust + trustDelta, 0, 100);

        if (objectiveResults is { Count: > 0 })
        {
            double objectiveDelta = objectiveResults.Average(r => r.Met ? 6.0 : -8.0) * scrutiny;
            coach.BoardTrust = Math.Clamp(coach.BoardTrust + objectiveDelta, 0, 100);
            trustDelta += objectiveDelta;
        }

        // Authority "grows with reputation + tenure" per its own doc comment on Coach - this is
        // its first actual writer. Eased toward a target rather than jumped, so one big season
        // does not hand a new arrival the standing of somebody who has earned it over years.
        double tenureFactor = Math.Clamp(seasonsAtClub / 6.0, 0, 1);
        double authorityTarget = 20 + coach.BoardTrust * 0.5 + tenureFactor * 25;
        coach.Authority = Math.Clamp(coach.Authority + (authorityTarget - coach.Authority) * 0.25, 0, 100);

        // Reputation moves only on genuinely notable outcomes - a mid-table finish is not news,
        // matching how PerformanceRecordingService only moves a PLAYER's reputation on clearly
        // above-average performances rather than on every match played.
        if (performanceScore > 0.5) coach.Reputation.Adjust(domesticDelta: 3.0, continentalDelta: 0.5);
        else if (performanceScore < -0.5) coach.Reputation.Adjust(domesticDelta: -2.5);

        string reason = performanceScore switch
        {
            > 0.35 => $"{coach.FullName} has overseen a genuinely strong season at {team.Name}.",
            < -0.35 => $"{coach.FullName} has overseen a poor season at {team.Name}.",
            _ => $"{coach.FullName}'s season at {team.Name} was about what was expected."
        };

        // A stated, checkable target read as legible board news beats the implicit
        // performance-vs-expectation line above - the board was not merely disappointed, it named
        // what it wanted and either got it or did not.
        if (objectiveResults is { Count: > 0 })
        {
            int metCount = objectiveResults.Count(r => r.Met);
            reason = metCount == objectiveResults.Count
                ? $"{coach.FullName} met every target the board set for {team.Name} this year."
                : metCount == 0
                    ? $"{coach.FullName} failed to meet any of the board's targets for {team.Name} this year."
                    : $"{coach.FullName} met {metCount} of {objectiveResults.Count} targets the board set for {team.Name} this year.";
        }

        // Wave 7: a big club that has gone a whole tenure without a trophy applies real pressure,
        // however respectable the individual seasons looked - the "one tenure, no title" clock.
        // The board of a powerhouse hires a coach to win things; second place, year after year, is
        // a failure by their standard whatever the raw win rate says.
        if (AmbitionTier(team) == TeamAmbitionTier.Elite && coach.CareerRecord.SeasonsSinceLastTitle >= 4)
        {
            double noTitleErosion = Math.Min((coach.CareerRecord.SeasonsSinceLastTitle - 3) * 3.5, 14) * scrutiny;
            coach.BoardTrust = Math.Clamp(coach.BoardTrust - noTitleErosion, 0, 100);
            trustDelta -= noTitleErosion;
        }

        bool dismissed = false;
        double effectiveFloor = EffectiveDismissalFloor(coach); // Wave 7: achievement leniency
        if (coach.BoardTrust < effectiveFloor)
        {
            double shortfall = (effectiveFloor - coach.BoardTrust) / Math.Max(1, effectiveFloor);
            double dismissalChance = Math.Clamp(shortfall * 0.55, 0, 0.75);

            // Wave 7: second-chance. A genuinely decorated coach - a major title, or two trophies -
            // whose last trophy is recent gets an ultimatum, not the sack: the board halves the odds
            // and lets him have one more season to put it right.
            var r = coach.CareerRecord;
            if ((r.MajorTitlesWon > 0 || r.TitlesWon >= 2) && r.SeasonsSinceLastTitle <= 3)
                dismissalChance *= 0.5;

            if (random.NextDouble() < dismissalChance)
            {
                dismissed = true;
                contract.Status = ContractStatus.Terminated;

                // CompensationIfTerminated has existed on CoachingContract since it was first
                // written and had no consumer anywhere - this is its first.
                if (contract.CompensationIfTerminated > 0)
                    team.Finances.Budget -= contract.CompensationIfTerminated;

                coach.CurrentTeamId = null;
                coach.CurrentContractId = null;
                // Job-market follow-up: a real, pre-existing gap closed here - this dismissal path
                // never cleared Team.CurrentCoachId, so a sacked coach's own team record still
                // pointed at him even after he was no longer attached to it from his own side. Not
                // just a stylistic nicety once the job market can HIRE a coach: a hiring flow that
                // reads Team.CurrentCoachId to check the job is actually vacant would wrongly see
                // it filled by a coach who has already been let go.
                if (team.CurrentCoachId == coach.Id) team.CurrentCoachId = null;
                reason = $"{team.Name} have parted ways with {coach.FullName} after a run of results the board could no longer back.";
            }
        }

        return new CoachSeasonReview(performanceScore, trustDelta, dismissed, reason);
    }

    // ---------------- Wave 7: multi-year, context-aware board judgment ----------------

    /// <summary>How high the bar sits for a team, and therefore what counts as success and how quick the board is to lose patience.</summary>
    public enum TeamAmbitionTier { Developing, Established, Elite }

    /// <summary>
    /// Wave 7: a team like Bangladesh is not judged against a World Cup final the way India is.
    /// Derived from a team's own standing - strength blended with domestic and continental
    /// reputation.
    /// </summary>
    public TeamAmbitionTier AmbitionTier(Team team)
    {
        double standing = (team.Strength + team.Reputation.Domestic * 2 + team.Reputation.Continental) / 4;
        return standing >= 72 ? TeamAmbitionTier.Elite
            : standing >= 45 ? TeamAmbitionTier.Established
            : TeamAmbitionTier.Developing;
    }

    /// <summary>
    /// Wave 7: the dismissal floor for THIS coach - the base floor lowered by his track record.
    /// Titles and sustained overperformance buy real rope: a coach who has won things is given
    /// time a first-timer with the same trust level would not get. Never drops below 8 - even a
    /// legend can be sacked if it goes badly enough for long enough.
    /// </summary>
    public double EffectiveDismissalFloor(Coach coach)
    {
        var r = coach.CareerRecord;
        double leniency = Math.Min(r.TitlesWon * 2.5 + r.MajorTitlesWon * 4.0, 12)
                          + Math.Clamp(r.CumulativeOverperformance, 0, 6);
        return Math.Max(8, DismissalTrustFloor - leniency);
    }

    /// <summary>
    /// Wave 7: folds one completed season into the coach's multi-year record, and returns a
    /// small, tier-aware reputation adjustment - a developing side's coach who overachieves gets
    /// NOTICED, an elite side's coach who merely meets par does not build a name from it, and a
    /// major title is a career-defining move regardless of tier.
    /// </summary>
    public double UpdateCareerRecord(Coach coach, Team team, double performanceScore, bool wonTitle, bool wonMajorTitle, bool reachedMajorFinal)
    {
        coach.CareerRecord.RecordSeason(performanceScore, wonTitle, wonMajorTitle, reachedMajorFinal, sameClubAsLastSeason: true);

        double repDelta = 0;
        var tier = AmbitionTier(team);

        if (performanceScore > 0.35)
            repDelta += performanceScore * (tier == TeamAmbitionTier.Developing ? 4.0 : tier == TeamAmbitionTier.Established ? 2.5 : 1.5);
        else if (performanceScore < -0.35)
            repDelta += performanceScore * (tier == TeamAmbitionTier.Elite ? 3.5 : 2.0);
        else if (tier == TeamAmbitionTier.Elite)
            repDelta -= 0.6; // a powerhouse coasting to a par finish quietly loses lustre

        if (wonMajorTitle) repDelta += 12;
        else if (wonTitle) repDelta += 5;
        else if (reachedMajorFinal) repDelta += 4;

        coach.Reputation.Adjust(domesticDelta: repDelta, continentalDelta: repDelta * 0.4, worldwideDelta: repDelta * 0.15);
        return repDelta;
    }

    /// <summary>
    /// Section D follow-up: CareerSatisfaction (Section 97) - initialised on Coach and never
    /// written by anything, confirmed by the same full-codebase grep this class's own doc comment
    /// already describes for BoardTrust/Authority before EvaluateSeason gave them a writer. This
    /// is a genuinely different signal from BoardTrust: BoardTrust is whether the BOARD still backs
    /// him; CareerSatisfaction is whether HE still wants the job. A coach can be perfectly secure
    /// (high trust) and still restless - a strong record at a small club is the classic case, and
    /// a coach who has coasted through many stagnant seasons is another.
    ///
    /// Deliberately reads the same expectation-vs-actual "standing" shape ScoreSeason already uses
    /// (team.Strength/Reputation), rather than inventing a second measure of "how big is this job" -
    /// consistent with this class's own stated principle of reusing real, measurable signals rather
    /// than guessing at ambition from nothing.
    /// </summary>
    public double EvaluateSatisfaction(Coach coach, Team team, double performanceScore, int seasonsAtClub)
    {
        double clubStanding = (team.Strength + team.Reputation.Domestic * 2) / 3;

        // Overachieving at a small job builds a genuine itch to leave for a bigger one - the
        // Mauricio-Pochettino-at-a-mid-table-club pattern. Only fires when both halves are true:
        // genuinely doing well (>0.4) AND the job itself is genuinely modest (<55) - success at a
        // big club, or a modest season at a small one, earns no such pull.
        double ambitionPressure = performanceScore > 0.4 && clubStanding < 55
            ? (performanceScore - 0.4) * (55 - clubStanding) * 0.6
            : 0;

        // A board that has stopped backing him erodes satisfaction too, independent of dismissal -
        // job insecurity is itself demoralising, before it ever reaches the sacking threshold.
        double trustErosion = coach.BoardTrust < 40 ? (40 - coach.BoardTrust) * 0.15 : 0;

        // Wave 4: the same magnifying-glass effect as EvaluateSeason - a marquee coach at a big
        // club feels every swing more sharply.
        double scrutiny = ScrutinyFactor(coach, team);
        double delta = (performanceScore * 4 - ambitionPressure - trustErosion) * scrutiny;

        // Long tenure with nothing genuinely moving - not a poor season, just a flat, unchallenged
        // one - is its own quiet drain. A coach in real crisis is already caught by BoardTrust; this
        // is specifically the stagnation case that dismissal never touches.
        if (seasonsAtClub > 8 && Math.Abs(performanceScore) < 0.15) delta -= 2.5;

        // §7.3: a coach who insists on doing everything himself (few areas delegated) burns out -
        // the workload is real, and a human coach who never hands anything off pays for it in
        // satisfaction. Only bites when he is genuinely hoarding control (5+ areas DoItMyself).
        int doItMyself = team.ManagerPreferences.Delegation.Modes.Values.Count(m => m == Enums.DelegationMode.DoItMyself);
        if (doItMyself >= 5) delta -= (doItMyself - 4) * 0.9;

        // §7.3, the other side: a coach who delegates almost EVERYTHING loses his grip on the
        // group - his own Authority drifts down and the dressing room reads him as disengaged.
        int delegated = team.ManagerPreferences.Delegation.Modes.Values.Count(m => m == Enums.DelegationMode.Delegate);
        if (delegated >= 8)
        {
            coach.Authority = Math.Clamp(coach.Authority - (delegated - 7) * 0.8, 0, 100);
            team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony - (delegated - 7) * 0.4, 0, 100);
        }

        coach.CareerSatisfaction = Math.Clamp(coach.CareerSatisfaction + delta, 0, 100);
        return coach.CareerSatisfaction;
    }

    /// <summary>
    /// A real, probabilistic chance the coach walks away on his own terms - the mirror of
    /// EvaluateSeason's dismissal check, but coach-initiated rather than board-initiated, and
    /// driven by CareerSatisfaction rather than BoardTrust. Same "probability, not a boolean
    /// cliff-edge" discipline as everywhere else in this codebase that ends a tenure.
    ///
    /// Deliberately no CompensationIfTerminated payout here - that clause exists for the CLUB
    /// ending the contract early, not for a coach choosing to leave. CareerSatisfaction is reset to
    /// a fresh-start value on resignation (a coach who has walked away for something new is not
    /// carrying the same restlessness into a job he has not taken yet) - provisional, pending a
    /// real hiring/job-market system that would otherwise own this reset.
    /// </summary>
    public bool TryResign(Coach coach, CoachingContract contract, Team team, Random random, out string reason)
    {
        reason = string.Empty;
        if (coach.CareerSatisfaction >= ResignationSatisfactionFloor) return false;

        double shortfall = (ResignationSatisfactionFloor - coach.CareerSatisfaction) / ResignationSatisfactionFloor;
        double resignationChance = Math.Clamp(shortfall * 0.45, 0, 0.6);

        if (random.NextDouble() >= resignationChance) return false;

        contract.Status = ContractStatus.Resigned;
        coach.CurrentTeamId = null;
        coach.CurrentContractId = null;
        if (team.CurrentCoachId == coach.Id) team.CurrentCoachId = null; // same fix as EvaluateSeason's dismissal path, see its own comment
        coach.CareerSatisfaction = 65;
        reason = $"{coach.FullName} has resigned as head coach of {team.Name}.";
        return true;
    }

    /// <summary>
    /// Job-market follow-up: contract renewal, decided by the board on the coach's own TENURE
    /// performance - which is exactly what BoardTrust already IS. BoardTrust is not recomputed
    /// here from scratch; it is the running tally EvaluateSeason has already accumulated, season
    /// by season, across the whole contract - reusing it is the honest way to read "how has this
    /// spell actually gone", rather than building a second parallel performance measure.
    /// Reputation gets a small, secondary say - a genuinely well-regarded coach gets a touch more
    /// benefit of the doubt than a raw trust reading alone would give him, matching how a
    /// big-name manager's reputation buys real patience in the real game.
    ///
    /// Probability, not a boolean cliff-edge, the same discipline every other career-ending
    /// decision in this class already uses. A renewal is a real pay rise when trust is genuinely
    /// high - success is rewarded, not just tolerated - and never a pay CUT, since a club offering
    /// worse terms is not really renewing him, it is inviting him to leave, which the "he does not
    /// get renewed" branch already covers on its own honest terms.
    /// </summary>
    public RenewalDecision EvaluateRenewal(Coach coach, CoachingContract contract, Team team, Random random, int renewalYears = 2)
    {
        double chance = Math.Clamp((coach.BoardTrust - 30) / 70.0, 0.05, 0.92);
        chance = Math.Clamp(chance + (coach.Reputation.Domestic - 50) / 400.0, 0.05, 0.95);

        if (random.NextDouble() >= chance)
            return new RenewalDecision(false, $"{team.Name} have decided not to renew {coach.FullName}'s contract.");

        double raiseFactor = coach.BoardTrust > 70 ? 1.15 : coach.BoardTrust > 50 ? 1.05 : 1.0;
        contract.EndDate = contract.EndDate.AddYears(Math.Max(1, renewalYears));
        contract.AnnualSalary *= raiseFactor;

        return new RenewalDecision(true, $"{team.Name} have extended {coach.FullName}'s contract to {contract.EndDate:yyyy}.");
    }

    /// <summary>
    /// Section D's "learning": a coach's own tactical sharpness grows slowly and probabilistically
    /// with tenure - fast in the first few seasons, negligible by year ten, the same saturating
    /// shape every other experience curve in this codebase already uses (playing experience,
    /// captaincy). Reinforced, never punished, by how the season went: succeeding teaches faster,
    /// but a bad season does not erase what he already knows - results cost him BoardTrust and
    /// job security above, not raw tactical skill, which is a genuinely different consequence.
    /// Bounded at the same 1-20 ceiling every coach attribute already respects.
    /// </summary>
    public void GrowFromTenure(Coach coach, int seasonsAtClub, double performanceScore, Random random)
    {
        double tenureFactor = Math.Exp(-Math.Max(0, seasonsAtClub) / 5.0);
        double reinforcement = 1 + Math.Max(0, performanceScore) * 0.6;
        double growthChance = Math.Clamp(0.10 * tenureFactor * reinforcement, 0, 0.35);

        if (random.NextDouble() < growthChance) coach.Attributes.TacticalKnowledge = Math.Min(20, coach.Attributes.TacticalKnowledge + 1);
        if (random.NextDouble() < growthChance) coach.Attributes.MatchReading = Math.Min(20, coach.Attributes.MatchReading + 1);
        if (random.NextDouble() < growthChance) coach.Attributes.DecisionMaking = Math.Min(20, coach.Attributes.DecisionMaking + 1);
        if (random.NextDouble() < growthChance) coach.Attributes.Adaptability = Math.Min(20, coach.Attributes.Adaptability + 1);
    }

    // ---------------- Wave 4: milestone-driven growth, and Type B (job-market-driven) departure ----------------

    /// <summary>
    /// Post-Phase-5 rectification pass, Wave 4: a head coach's tactical sharpness now grows on
    /// MILESTONES - a completed tour, a franchise season, a global event - not on the calendar.
    /// The per-role cadence decision in CLAUDE.md is explicit about why: a head coach's influence
    /// is broad and slow, a whole campaign's accumulated results settling into his tactical
    /// philosophy, not any single match (that is the captain's signal, and it has its own
    /// service). Fired from WorldClockService's CompetitionWindowClosed handler.
    ///
    /// Two Wave-4 refinements folded in:
    /// - **Context matters.** A coach at a bigger club, in a higher-prestige competition, learns
    ///   faster - more exposure, sharper opposition, more scrutiny of every call.
    /// - **Specialisation drift.** Over a long tenure a coach's profile drifts toward what his
    ///   philosophy demands - an AnalyticsDriven coach sharpens his analysis attributes, a
    ///   development-focused one his player-development ones - on top of the four in-match reads
    ///   that always grow.
    /// </summary>
    /// <summary>
    /// Returns the coach's new <see cref="CoachingPhilosophy"/> if it drifted this milestone
    /// (Phase 12, §4.5 - a coach is shaped by where he succeeds), otherwise null.
    /// </summary>
    public CoachingPhilosophy? GrowFromMilestone(Coach coach, Team team, double competitionPrestige, int seasonsAtClub, double performanceScore, Random random)
    {
        double tenureFactor = Math.Exp(-Math.Max(0, seasonsAtClub) / 5.0);
        double reinforcement = 1 + Math.Max(0, performanceScore) * 0.6;

        double clubStanding = (team.Strength + team.Reputation.Domestic * 2) / 3;
        double contextFactor = 0.7 + Math.Clamp((clubStanding + Math.Clamp(competitionPrestige, 0, 100)) / 200.0, 0, 1) * 0.7; // 0.7 - 1.4

        // Comparable per-call to GrowFromTenure's old annual figure at a mid-context job, so a side
        // playing a single annual competition still grows its coach at roughly the rate it used to;
        // a busy side with several competitions compounds it across more milestones, which is the point.
        double growthChance = Math.Clamp(0.09 * tenureFactor * reinforcement * contextFactor, 0, 0.32);

        if (random.NextDouble() < growthChance) coach.Attributes.TacticalKnowledge = Math.Min(20, coach.Attributes.TacticalKnowledge + 1);
        if (random.NextDouble() < growthChance) coach.Attributes.MatchReading = Math.Min(20, coach.Attributes.MatchReading + 1);
        if (random.NextDouble() < growthChance) coach.Attributes.DecisionMaking = Math.Min(20, coach.Attributes.DecisionMaking + 1);
        if (random.NextDouble() < growthChance) coach.Attributes.Adaptability = Math.Min(20, coach.Attributes.Adaptability + 1);

        foreach (var drift in SpecialisationDrift(coach.Philosophy))
            if (random.NextDouble() < growthChance * 1.2)
                drift(coach.Attributes);

        // Phase 12 (§4.5): a coach's PHILOSOPHY drifts, slowly, toward the identity of the club
        // where he keeps succeeding - the environment shapes him. Rare, and only after a genuine
        // run of good seasons.
        if (performanceScore > 0.2 && seasonsAtClub >= 3
            && team.CulturalIdentity != coach.Philosophy
            && random.NextDouble() < growthChance * 0.18)
        {
            coach.Philosophy = team.CulturalIdentity;
            return coach.Philosophy;
        }
        return null;
    }

    /// <summary>
    /// Phase 8, Slice 8.6: coaching-badge progression. Coach.License was static after creation for
    /// the whole life of the project - a coach could work for twenty years and never earn a higher
    /// qualification. This advances it a rung at a time, slowly, gated on genuine experience
    /// (seasons in the job) and helped by a real track record and standing - the coaching-education
    /// ladder a working coach actually climbs. Returns the new licence if it moved this year,
    /// otherwise null. Called once a year for an employed coach.
    /// </summary>
    public CoachingLicense? ProgressLicence(Coach coach, int totalSeasonsCoached, Random random)
    {
        if (coach.License >= CoachingLicense.InternationalElite) return null;

        // Each rung needs more time than the last - a Basic -> Level1 badge is a season or two,
        // Advanced -> InternationalElite is most of a career.
        int rung = (int)coach.License; // 1 = Basic .. 5 = Advanced (None handled above by the seed path giving Basic)
        int seasonsNeeded = 1 + rung * 2;
        if (totalSeasonsCoached < seasonsNeeded) return null;

        double trackRecord = coach.CareerRecord.TitlesWon * 0.05 + Math.Max(0, coach.CareerRecord.CumulativeOverperformance) * 0.15;
        double standing = coach.Reputation.Domestic / 100.0 * 0.25;
        double chance = Math.Clamp(0.10 + trackRecord + standing, 0.05, 0.6);

        if (random.NextDouble() >= chance) return null;

        coach.License = (CoachingLicense)Math.Min((int)CoachingLicense.InternationalElite, (int)coach.License + 1);

        // A new badge is real, applied knowledge - a small, one-off bump to the tactical/technical/
        // development attributes it certifies, the same shape Coach.SeedAttributesFromHistory uses.
        int B(int v) => Math.Min(20, v + 1);
        coach.Attributes.TechnicalKnowledge = B(coach.Attributes.TechnicalKnowledge);
        coach.Attributes.PlayerDevelopment = B(coach.Attributes.PlayerDevelopment);
        coach.Attributes.MatchPreparation = B(coach.Attributes.MatchPreparation);
        return coach.License;
    }

    private static IEnumerable<Action<ValueObjects.CoachAttributes>> SpecialisationDrift(CoachingPhilosophy philosophy) => philosophy switch
    {
        CoachingPhilosophy.YouthDevelopment or CoachingPhilosophy.LongTermDevelopment => new Action<ValueObjects.CoachAttributes>[]
        {
            a => a.PlayerDevelopment = Math.Min(20, a.PlayerDevelopment + 1),
            a => a.YouthDevelopment = Math.Min(20, a.YouthDevelopment + 1)
        },
        CoachingPhilosophy.AnalyticsDriven => new Action<ValueObjects.CoachAttributes>[]
        {
            a => a.TacticalAnalysis = Math.Min(20, a.TacticalAnalysis + 1),
            a => a.OppositionAnalysis = Math.Min(20, a.OppositionAnalysis + 1),
            a => a.Statistics = Math.Min(20, a.Statistics + 1)
        },
        CoachingPhilosophy.Aggressive or CoachingPhilosophy.Defensive or CoachingPhilosophy.TacticalFlexibility => new Action<ValueObjects.CoachAttributes>[]
        {
            a => a.TacticalKnowledge = Math.Min(20, a.TacticalKnowledge + 1),
            a => a.MatchReading = Math.Min(20, a.MatchReading + 1)
        },
        CoachingPhilosophy.FitnessFocused => new Action<ValueObjects.CoachAttributes>[]
        {
            a => a.FitnessCoaching = Math.Min(20, a.FitnessCoaching + 1)
        },
        _ => new Action<ValueObjects.CoachAttributes>[]
        {
            a => a.ManManagement = Math.Min(20, a.ManManagement + 1)
        }
    };

    /// <summary>
    /// How amplified this coach's BoardTrust / satisfaction swings are - a high-reputation coach
    /// at a big club is under a magnifying glass, so a bad season costs him more and a good one
    /// buys him less "surprise" credit. ~0.8 (a low-profile coach at a small club) to ~1.5 (a
    /// big name at a big job).
    /// </summary>
    public double ScrutinyFactor(Coach coach, Team team)
    {
        double clubStanding = (team.Strength + team.Reputation.Domestic * 2) / 3;
        double factor = Math.Clamp(1
            + (coach.Reputation.Domestic - 50) / 50.0 * 0.3
            + (clubStanding - 50) / 50.0 * 0.2, 0.8, 1.5);

        // Phase 7, Slice 7.8: a national coaching job carries far heavier scrutiny than a domestic
        // one for the same results - every match is a headline and the board is political.
        if (team.NationalBoard is { } nb)
            factor = Math.Clamp(factor * nb.CoachScrutinyMultiplier(), 0.8, 2.6);

        return factor;
    }

    /// <summary>
    /// Wave 4, Type B: whether a rival is genuinely interested in this sitting coach AND he is
    /// open to the idea - the job-market-driven departure, event-triggered, distinct from the
    /// slow-burn board/environment-driven Type A that TryResign already models. Driven by
    /// ambition, by overachieving at a job smaller than his own standing, and by a satisfaction
    /// that is soft rather than rock-bottom (a genuinely miserable coach is Type A's business).
    /// </summary>
    public bool EvaluateRivalInterest(Coach coach, Team currentTeam, double lastPerformanceScore, Random random)
    {
        // A coach the board has already lost faith in is not being headhunted - that is Type A /
        // dismissal territory, and mixing the two muddies which mechanism owns a departure. And a
        // coach the wider game does not rate yet is not a target either.
        if (coach.BoardTrust < 35 || coach.Reputation.Domestic < 45) return false;

        double ambition = Common.AbilityScale.AttributeToHundred(coach.Attributes.Ambition);
        double clubStanding = (currentTeam.Strength + currentTeam.Reputation.Domestic * 2) / 3;

        double overachievement = coach.Reputation.Domestic - clubStanding; // positive = he has outgrown the job
        double pull = Math.Clamp(
            ambition / 100.0 * 0.5
            + Math.Clamp(overachievement / 60.0, 0, 1) * 0.35
            + Math.Clamp(lastPerformanceScore, 0, 1) * 0.2
            - Math.Clamp((coach.CareerSatisfaction - 55) / 45.0, 0, 1) * 0.3, 0, 0.9);

        // Rare per check - this is a quarterly roll, and a coach changes jobs every few years at most.
        return random.NextDouble() < pull * 0.15;
    }

    /// <summary>
    /// Wave 4, Type B: the board's retention response when a rival comes calling. A board that
    /// rates him (BoardTrust) fights to keep him with a real pay rise and an extension, and it
    /// works often enough to matter; a board that has cooled on him lets him walk. Returns
    /// (retained, reason). On a failed retention the coach leaves on his own terms - contract
    /// Resigned, both sides detached, the same shape TryResign already uses.
    /// </summary>
    public (bool Retained, string Reason) OfferRetention(Coach coach, CoachingContract contract, Team team, Random random)
    {
        double willFight = Math.Clamp((coach.BoardTrust - 35) / 65.0, 0, 0.9);

        if (random.NextDouble() < willFight)
        {
            contract.EndDate = contract.EndDate.AddYears(2);
            contract.AnnualSalary *= 1.2;
            coach.CareerSatisfaction = Math.Clamp(coach.CareerSatisfaction + 15, 0, 100);
            return (true, $"{team.Name} have moved quickly to fend off interest in {coach.FullName}, handing him improved terms.");
        }

        contract.Status = ContractStatus.Resigned;
        coach.CurrentTeamId = null;
        coach.CurrentContractId = null;
        if (team.CurrentCoachId == coach.Id) team.CurrentCoachId = null;
        coach.CareerSatisfaction = 65;
        return (false, $"{coach.FullName} has left {team.Name} for another opportunity, with the club unable to match it.");
    }
}
