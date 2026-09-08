using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>Everything a completed match produced, in the shape the rest of the game already stores.</summary>
public sealed record MatchRecords
{
    public List<BattingInningsRecord> Batting { get; } = new();
    public List<BowlingSpellRecord> Bowling { get; } = new();
    public List<FieldingRecord> Fielding { get; } = new();
    public List<TeamInningsRecord> TeamInnings { get; } = new();
    public List<PartnershipRecord> Partnerships { get; } = new();
    public List<GameEvent> Events { get; } = new();
}

/// <summary>
/// The integration layer. The match engine produces a scorecard; this is what makes the rest of
/// the world react to it.
///
/// Before this existed, Phases 1-3 had built a large amount of machinery that was correct,
/// tested, and completely inert because nothing played cricket: ground records and honour
/// boards had no innings to read, standings had no results, net run rate could not be computed,
/// form and reputation had no performances to consume, matchup confidence had no head-to-heads,
/// player career stats had no source, fielding records had no producer, and injuries could be
/// applied but never occurred. Every one of those is wired here.
///
/// Deliberately a separate service from MatchSimulator: simulating a match and recording its
/// consequences are different jobs, and keeping them apart means a match can be simulated for
/// prediction, replay or "what if" analysis WITHOUT permanently altering the world - which the
/// AI will need when it evaluates its own decisions.
/// </summary>
public sealed class MatchRecorder
{
    private readonly PerformanceRecordingService _performance = new();
    private readonly PlayerAvailabilityService _availability = new();
    private readonly MedicalEffectivenessService _medical = new();
    private readonly PlayerMoraleService _playerMorale = new();
    private readonly TeamMoraleService _teamMorale = new();
    private readonly MatchResultContextService _resultContext = new();
    private readonly PartnershipChemistryService _chemistry = new();
    private readonly BowlingPairSynergyService _pairSynergy = new();

    /// <summary>Post-Phase-5 rectification pass, Wave 2: the "development BY playing" channel - format-aware learning-by-doing plus the big-match pressure/redemption arc. Runs alongside training, not instead of it.</summary>
    private readonly MatchDevelopmentService _matchDev = new();

    /// <summary>Wave 8: ICC-style rankings, updated only when the caller supplies a ranking list.</summary>
    private readonly RankingService _ranking = new();

    /// <summary>
    /// Turns a completed match into records and applies every consequence.
    /// </summary>
    /// <param name="players">Every player who took part, so career effects can be applied by id.</param>
    /// <param name="season">Optional - when supplied, standings and net run rate are updated.</param>
    /// <param name="world">Optional - when supplied, appearances are logged for the retirement/playing-time loop and injuries are indexed on the clock.</param>
    /// <param name="teams">
    /// Optional - when supplied, TEAM morale reacts to the result (see TeamMoraleService/
    /// MatchResultContextService), and the batting/bowling side's morale scales the value each
    /// player's performance is recorded with. PLAYER morale-from-personal-performance
    /// (PlayerMoraleService.AdjustForMatchPerformance) is unconditional and needs no Team
    /// entity at all - the same way Form recording has always been unconditional - so it still
    /// applies with teams left null; only the team-side effects need this parameter.
    /// </param>
    public MatchRecords Record(
        MatchResult match,
        IReadOnlyDictionary<Guid, Player> players,
        CompetitionSeason? season = null,
        WorldState? world = null,
        Random? random = null,
        IReadOnlyDictionary<Guid, Team>? teams = null,
        IList<TeamRanking>? rankings = null)
    {
        var records = new MatchRecords();
        var rng = random ?? new Random(match.Setup.MatchDate.DayNumber);

        RecordInnings(match, match.FirstInnings, players, records, rng, world, teams);
        RecordInnings(match, match.SecondInnings, players, records, rng, world, teams);

        UpdateGroundAggregate(match, records);
        UpdateStandings(match, season);
        ApplyMatchInjuries(match, players, records, rng, world);
        ApplyTeamMorale(match, teams);
        ApplyRankings(match, teams, rankings);

        records.Events.Add(new GameEvent(match.Setup.MatchDate, GameEventType.MatchCompleted, match.Summary, match.WinningTeamId));

        return records;
    }

    /// <summary>Wave 8: updates the two teams' ICC-style ranking rows - only when the caller supplied both a teams dictionary and a rankings list.</summary>
    private void ApplyRankings(MatchResult match, IReadOnlyDictionary<Guid, Team>? teams, IList<TeamRanking>? rankings)
    {
        if (rankings is null || teams is null) return;
        if (!teams.TryGetValue(match.Setup.Home.TeamId, out var home) || !teams.TryGetValue(match.Setup.Away.TeamId, out var away)) return;
        _ranking.RecordMatch(rankings, home, away, match.Setup.Format, match.WinningTeamId, home.Strength, away.Strength);
    }

    /// <summary>Classifies the result for both sides and applies the real, differentiated TeamMorale delta - see MatchResultContextService/TeamMoraleService. A no-op when teams weren't supplied.</summary>
    private void ApplyTeamMorale(MatchResult match, IReadOnlyDictionary<Guid, Team>? teams)
    {
        if (teams is null) return;

        foreach (var teamId in new[] { match.Setup.Home.TeamId, match.Setup.Away.TeamId })
        {
            if (!teams.TryGetValue(teamId, out var team)) continue;
            var opponentId = teamId == match.Setup.Home.TeamId ? match.Setup.Away.TeamId : match.Setup.Home.TeamId;
            double opponentStrength = teams.TryGetValue(opponentId, out var opponent) ? opponent.Strength : 50;

            var story = _resultContext.Classify(match, teamId, team.Strength, opponentStrength);
            _teamMorale.ApplyMatchResult(team, story);
        }
    }

    // ---------------- per-innings ----------------

    private void RecordInnings(
        MatchResult match, InningsState innings,
        IReadOnlyDictionary<Guid, Player> players, MatchRecords records, Random rng, WorldState? world,
        IReadOnlyDictionary<Guid, Team>? teams = null)
    {
        var setup = match.Setup;
        double prestige = setup.Competition?.Prestige ?? 50;

        // Opposition strength drives contextual performance valuation - 100 against a strong
        // attack is worth more than 100 against a weak one, which PerformanceValuationService
        // already knew how to weigh and had no live source for.
        //
        // FIX (tech-debt item 11, confirmed by a real test before this was touched - see
        // HANDOFF section 3.3 / CLAUDE.md): this used to read EstimateBowlingStrength on the
        // OTHER innings, i.e. opposingInnings.BowlerCards - but that innings' bowlers are the
        // batting side's OWN attack (bowling in the innings where the roles are reversed), not
        // the attack these batters actually faced. InningsState.BowlerCards already holds the
        // bowling figures of whoever bowled AT this SAME innings' batters, so no pairing is
        // needed at all - the same correction MultiDayMatchRecorder (Slice 14) already used.
        double oppositionStrength = EstimateBowlingStrength(innings, players);

        MatchContext ContextFor(Guid opponentTeamId, string opponentName) => new()
        {
            MatchId = match.MatchId,
            MatchDate = setup.MatchDate,
            GroundId = setup.Ground?.Id ?? Guid.Empty,
            Ground = setup.Ground?.Name ?? string.Empty,
            OpponentTeamId = opponentTeamId,
            OpponentName = opponentName,
            Format = setup.Format,
            Season = setup.Season,
            OppositionStrength = oppositionStrength,
            Scope = setup.Competition?.Scope ?? CompetitionScope.DomesticT20,
            Tournament = setup.Competition?.Name ?? string.Empty
        };

        // --- batting ---
        foreach (var card in innings.BatterCards.Where(c => c.HasBatted))
        {
            var context = ContextFor(innings.BowlingTeamId, innings.BowlingTeamName);

            var record = new BattingInningsRecord
            {
                PlayerId = card.PlayerId,
                Runs = card.Runs,
                BallsFaced = card.BallsFaced,
                Fours = card.Fours,
                Sixes = card.Sixes,
                NotOut = !card.IsOut,
                Context = context,
                Dismissal = card.Dismissal,
                DismissedByBowlerId = card.DismissedByBowlerId,
                FielderId = card.FielderId
            };
            records.Batting.Add(record);

            if (!players.TryGetValue(card.PlayerId, out var player)) continue;

            // One call updates form, career stats, matchup confidence and reputation, all from
            // the SAME contextually valued rating - see PerformanceRecordingService.
            var battingTeam = teams?.GetValueOrDefault(innings.BattingTeamId);
            var battingResult = _performance.RecordBattingPerformance(player, record, competitionPrestige: prestige, battingTeam: battingTeam,
                situation: BuildSituation(innings, match, setup, batting: true));
            _playerMorale.AdjustForMatchPerformance(player, battingResult.ValuedRating,
                match.WinningTeamId is null ? null : match.WinningTeamId == innings.BattingTeamId);

            // Wave 2: he also develops from having played - format-aware, youth-gated, and the
            // big-match pressure/redemption arc. A separate channel from training and from ageing.
            var battingDev = _matchDev.ApplyMatchExperience(player, setup.Format, bowling: false,
                battingResult.ValuedRating, setup.BaseImportance, rng, setup.MatchDate, world);
            if (battingDev.Redeemed || battingDev.FrozeAgain)
                records.Events.Add(new GameEvent(setup.MatchDate, GameEventType.PlayerDeveloped, battingDev.Summary!, player.Id));

            player.Experience.RecordAppearance(setup.Format, setup.MatchDate,
                isInternational: setup.Competition?.Scope == CompetitionScope.International,
                isBigMatch: setup.BaseImportance >= 80);

            world?.RecordAppearance(card.PlayerId, setup.Format);
        }

        // --- bowling ---
        foreach (var card in innings.BowlerCards.Where(c => c.LegalBallsBowled > 0))
        {
            var context = ContextFor(innings.BattingTeamId, innings.BattingTeamName);

            var spell = new BowlingSpellRecord
            {
                PlayerId = card.PlayerId,
                OversBowled = card.LegalBallsBowled / 6.0,
                RunsConceded = card.RunsConceded,
                Wickets = card.Wickets,
                Context = context,
                RoleUsedAs = players.TryGetValue(card.PlayerId, out var bowler) ? bowler.BowlingRole : BowlingRoleType.NotABowler
            };
            records.Bowling.Add(spell);

            if (bowler is null) continue;
            var bowlingTeam = teams?.GetValueOrDefault(innings.BowlingTeamId);
            var bowlingResult = _performance.RecordBowlingPerformance(bowler, spell, competitionPrestige: prestige, bowlingTeam: bowlingTeam,
                situation: BuildSituation(innings, match, setup, batting: false));
            _playerMorale.AdjustForMatchPerformance(bowler, bowlingResult.ValuedRating,
                match.WinningTeamId is null ? null : match.WinningTeamId == innings.BowlingTeamId);

            var bowlingDev = _matchDev.ApplyMatchExperience(bowler, setup.Format, bowling: true,
                bowlingResult.ValuedRating, setup.BaseImportance, rng, setup.MatchDate, world);
            if (bowlingDev.Redeemed || bowlingDev.FrozeAgain)
                records.Events.Add(new GameEvent(setup.MatchDate, GameEventType.PlayerDeveloped, bowlingDev.Summary!, bowler.Id));
        }

        // Section Q/W follow-up: the new-ball pair, identified the same way InningsSimulator itself
        // identifies it live (the first two distinct bowlers used inside the powerplay window) - so
        // BowlingPairSynergyService.RecordNewBallPairing gets a real, consistent pairing to record.
        RecordNewBallPairing(innings, players, _pairSynergy);

        // --- fielding ---
        // Built by walking the deliveries rather than kept as a running tally, so the fielding
        // record can never disagree with the ball-by-ball data it came from.
        var fieldingByPlayer = new Dictionary<Guid, (int Catches, int RunOuts, int Stumpings)>();

        foreach (var delivery in innings.Deliveries.Where(d => d.Outcome.FielderId is not null))
        {
            var fielderId = delivery.Outcome.FielderId!.Value;
            (int Catches, int RunOuts, int Stumpings) tally =
                fieldingByPlayer.TryGetValue(fielderId, out var existing) ? existing : (0, 0, 0);

            tally = delivery.Outcome.Dismissal switch
            {
                DismissalType.Caught or DismissalType.CaughtBehind or DismissalType.CaughtAndBowled
                    => (tally.Catches + 1, tally.RunOuts, tally.Stumpings),
                DismissalType.RunOut => (tally.Catches, tally.RunOuts + 1, tally.Stumpings),
                DismissalType.Stumped => (tally.Catches, tally.RunOuts, tally.Stumpings + 1),
                _ => tally
            };

            fieldingByPlayer[fielderId] = tally;
        }

        foreach (var (fielderId, tally) in fieldingByPlayer)
        {
            records.Fielding.Add(new FieldingRecord
            {
                PlayerId = fielderId,
                Context = ContextFor(innings.BattingTeamId, innings.BattingTeamName),
                Catches = tally.Catches,
                RunOuts = tally.RunOuts,
                Stumpings = tally.Stumpings,
                KeptWicket = tally.Stumpings > 0
            });
        }

        // --- partnerships ---
        // Every stand InningsState already tracked, converted to the persisted shape. The final
        // entry needs special care - see PartnershipRecord's class doc for why an UNBROKEN stand's
        // wicket number is off by one from what Partnership.WicketNumber holds directly.
        //
        // BUG FIX: "unbroken" used to mean "!innings.IsAllOut" - wrong whenever the innings ends
        // (overs, target, declaration) on the SAME ball the final wicket also falls: fewer than
        // ten wickets down reads as "not all out", but that last partnership was genuinely closed
        // by a real, counted dismissal, not left hanging. The correct signal is arithmetic, not a
        // proxy: ClosePartnership only ever produces MORE partnerships than wickets fell (exactly
        // one more) when the last pair was still batting when the innings ended without being
        // separated - every other case, the counts match exactly, including a genuine all-out
        // innings. Found by a seed that put the final wicket on the innings' literal last ball,
        // which the pre-existing 20-seed test suite had simply never happened to hit before.
        bool anyPartnershipUnbroken = innings.Partnerships.Count > innings.FallOfWickets.Count;
        for (int p = 0; p < innings.Partnerships.Count; p++)
        {
            var partnership = innings.Partnerships[p];
            bool isLastEntry = p == innings.Partnerships.Count - 1;
            bool unbroken = isLastEntry && anyPartnershipUnbroken;
            int wicketNumber = unbroken ? partnership.WicketNumber + 1 : partnership.WicketNumber;

            var batterA = innings.BatterCards.FirstOrDefault(c => c.PlayerId == partnership.BatterAId);
            var batterB = innings.BatterCards.FirstOrDefault(c => c.PlayerId == partnership.BatterBId);

            records.Partnerships.Add(new PartnershipRecord
            {
                MatchId = match.MatchId,
                MatchDate = setup.MatchDate,
                GroundId = setup.Ground?.Id ?? Guid.Empty,
                GroundName = setup.Ground?.Name ?? string.Empty,
                BattingTeamId = innings.BattingTeamId,
                BattingTeamName = innings.BattingTeamName,
                BowlingTeamId = innings.BowlingTeamId,
                BowlingTeamName = innings.BowlingTeamName,
                Format = setup.Format,
                Season = setup.Season,
                InningsNumber = innings.InningsNumber,
                WicketNumber = wicketNumber,
                BatterAId = partnership.BatterAId,
                BatterAName = batterA?.PlayerName ?? string.Empty,
                BatterBId = partnership.BatterBId,
                BatterBName = batterB?.PlayerName ?? string.Empty,
                Runs = partnership.Runs,
                Balls = partnership.Balls,
                Unbroken = unbroken
            });

            // Section Q/W: how this specific pair ran together, folded into their shared history
            // for next time - a run-out ending the stand is the one direct, honest signal of a
            // mix-up between these two players, independent of how well they'd been scoring.
            if (players.TryGetValue(partnership.BatterAId, out var chemA) && players.TryGetValue(partnership.BatterBId, out var chemB))
            {
                bool endedInRunOut = !unbroken && p < innings.FallOfWickets.Count
                    && innings.BatterCards.FirstOrDefault(c => c.PlayerId == innings.FallOfWickets[p].BatterOutId)?.Dismissal == DismissalType.RunOut;
                _chemistry.RecordPartnership(chemA, chemB, partnership.Runs, partnership.Balls, setup.Format, endedInRunOut);
            }
        }

        // --- team innings ---
        records.TeamInnings.Add(new TeamInningsRecord
        {
            MatchId = match.MatchId,
            MatchDate = setup.MatchDate,
            GroundId = setup.Ground?.Id ?? Guid.Empty,
            GroundName = setup.Ground?.Name ?? string.Empty,
            BattingTeamId = innings.BattingTeamId,
            BattingTeamName = innings.BattingTeamName,
            BowlingTeamId = innings.BowlingTeamId,
            BowlingTeamName = innings.BowlingTeamName,
            Format = setup.Format,
            Scope = setup.Competition?.Scope ?? CompetitionScope.DomesticT20,
            CompetitionId = setup.Competition?.Id ?? Guid.Empty,
            CompetitionName = setup.Competition?.Name ?? string.Empty,
            Season = setup.Season,
            InningsNumber = innings.InningsNumber,
            Runs = innings.Runs,
            Wickets = innings.Wickets,
            Overs = innings.LegalBalls / 6.0,
            Extras = innings.Extras,
            MatchResultForBattingTeam =
                match.WinningTeamId == innings.BattingTeamId ? MatchOutcome.Win
                : match.WinningTeamId is null ? MatchOutcome.Tie
                : MatchOutcome.Loss
        });
    }

    /// <summary>
    /// Identifies the new-ball pair for one innings the same way InningsSimulator identifies it
    /// live - the first two distinct bowlers used inside the powerplay window (LegalBalls &lt; 36,
    /// six overs, the same threshold the field-setting "newBall" flag already uses) - and records
    /// how they contained that phase together. Internal AND static, same reuse pattern as
    /// EstimateBowlingStrength below (a pure function taking the service it needs, rather than an
    /// instance method), so MultiDayMatchRecorder can call it without needing a MatchRecorder of
    /// its own.
    /// </summary>
    internal static void RecordNewBallPairing(InningsState innings, IReadOnlyDictionary<Guid, Player> players, BowlingPairSynergyService pairSynergy)
    {
        var newBallBowlerIds = innings.Deliveries
            .Where(d => d.OverNumber <= 6) // the first six overs - the same threshold InningsSimulator's own "newBall" flag uses
            .Select(d => d.BowlerId)
            .Distinct()
            .Take(2)
            .ToList();

        if (newBallBowlerIds.Count != 2) return;
        if (!players.TryGetValue(newBallBowlerIds[0], out var bowlerA) || !players.TryGetValue(newBallBowlerIds[1], out var bowlerB)) return;

        var cardA = innings.BowlerCards.FirstOrDefault(c => c.PlayerId == newBallBowlerIds[0]);
        var cardB = innings.BowlerCards.FirstOrDefault(c => c.PlayerId == newBallBowlerIds[1]);
        if (cardA is null || cardB is null) return;

        int combinedBalls = cardA.LegalBallsBowled + cardB.LegalBallsBowled;
        if (combinedBalls == 0) return;

        double combinedEconomy = (cardA.RunsConceded + cardB.RunsConceded) / (combinedBalls / 6.0);
        pairSynergy.RecordNewBallPairing(bowlerA, bowlerB, combinedEconomy, innings.Format, cardA.Wickets + cardB.Wickets);
    }

    /// <summary>
    /// How good the attack the batters actually faced was. Weighted by overs bowled, so a strong
    /// bowler who sent down two overs does not make the whole attack look formidable.
    /// Internal rather than private so MultiDayMatchRecorder can reuse it rather than duplicating
    /// the same logic - a pure visibility widen, no behaviour change to this method.
    /// </summary>
    internal static double EstimateBowlingStrength(InningsState bowlingInnings, IReadOnlyDictionary<Guid, Player> players)
    {
        var contributions = bowlingInnings.BowlerCards
            .Where(c => c.LegalBallsBowled > 0 && players.ContainsKey(c.PlayerId))
            .Select(c => (Weight: (double)c.LegalBallsBowled,
                          Strength: Common.AbilityScale.CompositeAbilityToHundred(players[c.PlayerId].CurrentAbility)))
            .ToList();

        if (contributions.Count == 0) return 50;

        double totalWeight = contributions.Sum(c => c.Weight);
        return Math.Round(contributions.Sum(c => c.Strength * c.Weight) / totalWeight, 1);
    }

    // ---------------- ground and competition ----------------

    /// <summary>
    /// Ground records need no explicit update: GroundRecordsService derives everything from the
    /// TeamInningsRecord/BattingInningsRecord/BowlingSpellRecord rows this recorder produces.
    /// That was the whole point of removing the cached aggregates from Ground in Phase 3b - one
    /// source of truth, and honour boards that can never drift from the scorecards behind them.
    /// This method exists only to note that, and to keep the ball-age/match count on the pitch
    /// side honest.
    /// </summary>
    private static void UpdateGroundAggregate(MatchResult match, MatchRecords records)
    {
        if (match.Setup.Ground is null) return;
        // Intentionally empty - see the summary. Left as a named step so the next person looking
        // for "where do ground records get written" finds this explanation instead of assuming
        // it was forgotten.
    }

    /// <summary>
    /// Points table and net run rate. NRR is the standard formula - runs per over scored minus
    /// runs per over conceded - and a side bowled out is charged the FULL quota of overs, not
    /// the overs it actually faced, which is the rule people most often get wrong.
    /// </summary>
    private static void UpdateStandings(MatchResult match, CompetitionSeason? season)
    {
        if (season is null) return;

        // Tech-debt item 9: a competition can now genuinely opt in to a limited-overs margin
        // bonus (some domestic / franchise T20 leagues award a bonus point for a big win). Default
        // is still the plain system, so every competition that hasn't set the flag is unchanged.
        var pointsSystem = match.Setup.Format == MatchFormat.Test
            ? CompetitionPointsSystem.FirstClass
            : match.Setup.Competition?.UsesLimitedOversMarginBonus == true
                ? CompetitionPointsSystem.LimitedOversWithMarginBonus
                : CompetitionPointsSystem.LimitedOvers;
        var bonusPoints = new BonusPointsService();

        foreach (var innings in new[] { match.FirstInnings, match.SecondInnings })
        {
            var standing = season.GetOrCreateStanding(innings.BattingTeamId);

            MatchOutcome outcome =
                match.WinningTeamId is null ? MatchOutcome.Tie
                : match.WinningTeamId == innings.BattingTeamId ? MatchOutcome.Win
                : MatchOutcome.Loss;

            // Inert unless the caller's own points system opted in - see CompetitionPointsSystem.
            // MarginBonusEnabled's doc comment. Neither of the two systems selected above turns
            // it on, so this is a no-op against today's actual standings.
            int bonus = pointsSystem.MarginBonusEnabled
                ? bonusPoints.LimitedOversMarginBonus(match, innings.BattingTeamId)
                : 0;

            standing.RecordResult(outcome, pointsSystem, bonus);
        }

        UpdateNetRunRate(match, season, match.FirstInnings, match.SecondInnings);
        UpdateNetRunRate(match, season, match.SecondInnings, match.FirstInnings);
    }

    private static void UpdateNetRunRate(MatchResult match, CompetitionSeason season, InningsState own, InningsState against)
    {
        var standing = season.GetOrCreateStanding(own.BattingTeamId);

        double fullQuota = match.Setup.OversPerInnings;

        // A side dismissed inside its overs is treated as having used them all - otherwise being
        // bowled out for 60 in 12 overs would flatter a team's run rate instead of punishing it.
        double oversFaced = own.IsAllOut ? fullQuota : own.LegalBalls / 6.0;
        double oversBowled = against.IsAllOut ? fullQuota : against.LegalBalls / 6.0;

        if (oversFaced <= 0 || oversBowled <= 0) return;

        double nrr = own.Runs / oversFaced - against.Runs / oversBowled;

        // Blended across matches played so a single result doesn't overwrite a season's NRR.
        int played = Math.Max(1, standing.Played);
        double blended = (standing.NetRunRate * (played - 1) + nrr) / played;
        standing.SetNetRunRate(Math.Round(blended, 3));
    }

    /// <summary>
    /// Post-Phase-9 wiring pass (tech-debt item 4): the match SITUATION for a performance in a
    /// limited-overs match - built from the `InningsState` and `MatchResult` the recorder already
    /// holds so `PerformanceRecordingService.Rate*` can weigh occasion, chase pressure, team-effort
    /// share, momentum and result.
    /// </summary>
    internal static MatchSituation BuildSituation(InningsState innings, MatchResult match, MatchSetup setup, bool batting)
    {
        bool? won = match.NoResult ? null
            : match.WinningTeamId is null ? (bool?)null
            : match.WinningTeamId == (batting ? innings.BattingTeamId : innings.BowlingTeamId);
        return new MatchSituation(
            BaseImportance: setup.BaseImportance,
            WasChase: innings.InningsNumber >= 2 || innings.Target is not null,
            TeamTotal: innings.Runs,
            TeamWicketsInHand: Math.Max(0, 10 - innings.Wickets),
            WicketsTakenByTeam: innings.Wickets,
            TeamWon: won,
            MatchDecided: !match.NoResult,
            MomentumForTeam: batting ? innings.Momentum.Value : -innings.Momentum.Value);
    }

    // ---------------- injuries ----------------

    private static readonly SpecialistStaffService _specialistStaff = new();

    /// <summary>
    /// Follow-up: a team's effective medical quality = its facility MedicalQuality PLUS the boost
    /// from a hired physio / head physio / sports scientist (SpecialistStaffService.MedicalQualityBoost -
    /// built in the Post-Phase-6 pass and, until now, unwired). Falls back to 50 when there is no
    /// world/team (a hand-built match test).
    /// </summary>
    internal static int TeamMedicalQuality(WorldState? world, Guid? teamId)
    {
        if (world is null || teamId is not { } tid || !world.Teams.TryGetValue(tid, out var team)) return 50;
        var staff = world.Staff.Where(s => team.StaffIds.Contains(s.Id)).ToList();
        return (int)Math.Round(Math.Clamp(team.Facilities.MedicalQuality + _specialistStaff.MedicalQualityBoost(staff), 0, 100));
    }

    /// <summary>
    /// Injuries finally OCCUR. Everything needed was already in place and unused: injury
    /// proneness and recovery on the player, MedicalEffectivenessService to mitigate risk, the
    /// Injury value object, and PlayerAvailabilityService to apply and resolve one. The only
    /// missing piece was something that generates them, because injuries happen during play.
    ///
    /// Risk is workload-driven, which is why it lands mostly on bowlers and hardest on those who
    /// bowled long spells - and it is why the medical facilities a board pays for finally show
    /// up as fewer and shorter absences.
    /// </summary>
    private void ApplyMatchInjuries(
        MatchResult match, IReadOnlyDictionary<Guid, Player> players, MatchRecords records, Random rng, WorldState? world)
    {
        foreach (var innings in new[] { match.FirstInnings, match.SecondInnings })
        {
            foreach (var card in innings.BowlerCards.Where(c => c.LegalBallsBowled > 0))
            {
                if (!players.TryGetValue(card.PlayerId, out var player) || player.CurrentInjury is not null) continue;

                int teamMedical = TeamMedicalQuality(world, player.CurrentTeamId);
                var groundFacilities = match.Setup.Ground?.Facilities;

                double baseRisk = _medical.GetEffectiveInjuryRisk(player.Physical.InjuryProneness, teamMedical, groundFacilities);

                // Per-match probability. Workload is the multiplier: a four-over spell is routine,
                // a long spell in a Test is where bodies break down.
                double workload = card.LegalBallsBowled / 24.0;
                double probability = Math.Clamp(baseRisk / 100.0 * 0.02 * workload, 0, 0.08);

                if (rng.NextDouble() >= probability) continue;

                var (type, severity) = RollBowlerInjury(player, rng);

                var injury = world is not null
                    ? world.ApplyInjury(player, type, severity, match.Setup.MatchDate, teamMedical, groundFacilities, rng)
                    : _availability.ApplyInjury(player, type, severity, match.Setup.MatchDate, teamMedical, groundFacilities, rng);

                records.Events.Add(new GameEvent(match.Setup.MatchDate, GameEventType.InjuryOccurred,
                    $"{player.FullName} has been ruled out with a {severity} {type}, expected back around {injury.ExpectedReturnDate:d MMM yyyy}.",
                    player.Id));
            }

            MaybeApplyBatterConcussion(innings, players, records, match.Setup.Ground, match.Setup.MatchDate, rng, world, _availability);
        }
    }

    /// <summary>
    /// Phase 15 (§1.8): a batter takes a blow to the head. Rare, and gated on a genuinely bouncy
    /// surface and a batter who is not a good player of the short ball. It always triggers the
    /// concussion protocol (a Concussion injury -> a mandatory minimum stand-down, applied in
    /// PlayerAvailabilityService), and the club names a concussion substitute. Shared by both
    /// recorders. Consumes the caller's Random, once per batter who faced a real number of balls.
    /// </summary>
    internal static void MaybeApplyBatterConcussion(
        InningsState innings, IReadOnlyDictionary<Guid, Player> players, MatchRecords records,
        Ground? ground, DateOnly matchDate, Random rng, WorldState? world, PlayerAvailabilityService availability)
    {
        double bounce = ground?.PitchBounceRating ?? 50;
        if (bounce < 52) return;

        foreach (var card in innings.BatterCards.Where(c => c.HasBatted && c.BallsFaced >= 8))
        {
            if (!players.TryGetValue(card.PlayerId, out var player) || player.CurrentInjury is not null) continue;
            if (player.ConcussionStandDownUntil is not null) continue;

            // A poor player of the short ball on a lively pitch, over a decent number of balls.
            double shortBall = player.Batting.ShortBallAbility;
            double vuln = Math.Clamp((13 - shortBall) / 13.0, 0, 1) * Math.Clamp((bounce - 52) / 40.0, 0, 1);
            double prob = Math.Clamp(0.0016 * (card.BallsFaced / 30.0) * (0.4 + vuln), 0, 0.02);
            if (rng.NextDouble() >= prob) continue;

            int teamMedical = TeamMedicalQuality(world, player.CurrentTeamId);
            var groundFacilities = ground?.Facilities;
            var injury = world is not null
                ? world.ApplyInjury(player, InjuryType.Concussion, InjurySeverity.Minor, matchDate, teamMedical, groundFacilities, rng)
                : availability.ApplyInjury(player, InjuryType.Concussion, InjurySeverity.Minor, matchDate, teamMedical, groundFacilities, rng);

            records.Events.Add(new GameEvent(matchDate, GameEventType.InjuryOccurred,
                $"{player.FullName} took a heavy blow to the helmet and has been withdrawn under the concussion protocol - a concussion substitute takes his place, and he is stood down until at least {injury.ExpectedReturnDate:d MMM yyyy}.",
                player.Id, player.CurrentTeamId));
        }
    }

    /// <summary>Bowling injuries skew towards the ones bowlers actually get - side strains, backs and hamstrings - and are usually minor rather than career-ending. Internal so MultiDayMatchRecorder can reuse it - pure visibility widen.</summary>
    internal static (InjuryType Type, InjurySeverity Severity) RollBowlerInjury(Player player, Random rng)
    {
        InjuryType type = rng.NextDouble() switch
        {
            < 0.24 => InjuryType.SideStrain,
            < 0.44 => InjuryType.BackInjury,
            < 0.62 => InjuryType.Hamstring,
            < 0.74 => InjuryType.ShoulderInjury,
            < 0.84 => InjuryType.MuscleStrain,
            < 0.92 => InjuryType.AnkleInjury,
            < 0.98 => InjuryType.KneeInjury,
            _ => InjuryType.StressFracture
        };

        // The injury-prone break down worse, not just more often.
        double severityRoll = rng.NextDouble() * (0.75 + player.Physical.InjuryProneness / 20.0 * 0.5);

        InjurySeverity severity = severityRoll switch
        {
            < 0.42 => InjurySeverity.Niggle,
            < 0.72 => InjurySeverity.Minor,
            < 0.92 => InjurySeverity.Moderate,
            _ => InjurySeverity.Serious
        };

        return (type, severity);
    }
}
