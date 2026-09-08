using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// The multi-day counterpart to <see cref="MatchRecorder"/> - closes tech-debt item 10, found
/// while wiring bonus points in Slice 13: a completed <see cref="MultiDayMatchResult"/> produced
/// no records of any kind, updated no standings, and fed no career/form/reputation system,
/// because nothing existed that turned one into permanent world state. Everything this class
/// calls (`PerformanceRecordingService`, `MedicalEffectivenessService`, `PartnershipRecord`,
/// `BonusPointsService`) already existed and worked; this is the wiring `MatchRecorder` already
/// is for limited overs, generalised to 2-4 innings and first-class scoring.
///
/// A separate class rather than an overload on `MatchRecorder`, mirroring the existing split
/// between `MatchSimulator`/`MultiDayMatchSimulator` - the two formats already don't share a
/// simulator, and forcing them to share a recorder would mean every method taking either a
/// `MatchResult` or a `MultiDayMatchResult` by convention rather than by type. The two small
/// pieces of logic that don't depend on which type of match it was
/// (`MatchRecorder.EstimateBowlingStrength`, `MatchRecorder.RollBowlerInjury`) are reused
/// directly rather than duplicated - both were widened from private to internal for exactly this,
/// a pure visibility change with no behaviour change to `MatchRecorder` itself.
///
/// **One deliberate difference from `MatchRecorder`'s calling convention, worth reading:**
/// `MatchRecorder.RecordInnings` calls `EstimateBowlingStrength` on the OTHER innings of the
/// pair (`opposingInnings`), not the one being recorded. Tracing through what that actually reads
/// - `InningsState.BowlerCards` is already the bowling figures of whoever bowled AT this specific
/// innings' batters, recorded on the SAME `InningsState`, not a paired one - it looks like that
/// passes the wrong side's bowling figures as "the attack these batters faced". This class passes
/// the SAME innings instead (correct by inspection, and side-steps needing to know the follow-on
/// battting order to find the "right" pair across up to four innings). Flagged for the single-day
/// version rather than changed there in the same pass - that's shipped, tested code valuing
/// upstream reputation/form effects, and changing it deserves its own look rather than riding
/// along on this one.
/// </summary>
public sealed class MultiDayMatchRecorder
{
    private readonly PerformanceRecordingService _performance = new();
    private readonly PlayerAvailabilityService _availability = new();
    private readonly MedicalEffectivenessService _medical = new();
    private readonly BonusPointsService _bonus = new();
    private readonly PartnershipChemistryService _chemistry = new();
    private readonly BowlingPairSynergyService _pairSynergy = new();

    /// <summary>Wave 2: the multi-day counterpart to MatchRecorder's own MatchDevelopmentService wiring - a Test innings develops Test-relevant attributes.</summary>
    private readonly MatchDevelopmentService _matchDev = new();

    /// <summary>Wave 8: ICC-style Test rankings, updated only when the caller supplies both a teams dictionary and a rankings list.</summary>
    private readonly RankingService _ranking = new();

    public MatchRecords Record(
        MultiDayMatchResult match,
        IReadOnlyDictionary<Guid, Player> players,
        CompetitionSeason? season = null,
        WorldState? world = null,
        Random? random = null,
        IReadOnlyDictionary<Guid, Team>? teams = null,
        IList<TeamRanking>? rankings = null)
    {
        var records = new MatchRecords();
        var rng = random ?? new Random(match.Setup.MatchDate.DayNumber);

        foreach (var innings in match.Innings)
            RecordInnings(match, innings, players, records, rng, world);

        UpdateStandings(match, season);
        ApplyMatchInjuries(match, players, records, rng, world);

        if (rankings is not null && teams is not null
            && teams.TryGetValue(match.Setup.Home.TeamId, out var homeTeam)
            && teams.TryGetValue(match.Setup.Away.TeamId, out var awayTeam))
        {
            _ranking.RecordMatch(rankings, homeTeam, awayTeam, MatchFormat.Test, match.WinningTeamId, homeTeam.Strength, awayTeam.Strength);
        }

        records.Events.Add(new GameEvent(match.Setup.MatchDate, GameEventType.MatchCompleted, match.Summary, match.WinningTeamId));

        return records;
    }

    // ---------------- per-innings ----------------

    private void RecordInnings(
        MultiDayMatchResult match, InningsState innings,
        IReadOnlyDictionary<Guid, Player> players, MatchRecords records, Random rng, WorldState? world)
    {
        var setup = match.Setup;
        double prestige = setup.Competition?.Prestige ?? 50;

        double oppositionStrength = MatchRecorder.EstimateBowlingStrength(innings, players);

        MatchContext ContextFor(Guid opponentTeamId, string opponentName) => new()
        {
            MatchId = match.MatchId,
            MatchDate = setup.MatchDate,
            GroundId = setup.Ground?.Id ?? Guid.Empty,
            Ground = setup.Ground?.Name ?? string.Empty,
            OpponentTeamId = opponentTeamId,
            OpponentName = opponentName,
            Format = MatchFormat.Test,
            Season = setup.Season,
            OppositionStrength = oppositionStrength,
            Scope = setup.Competition?.Scope ?? CompetitionScope.DomesticFirstClass,
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

            var battingResult = _performance.RecordBattingPerformance(player, record, competitionPrestige: prestige,
                situation: BuildSituation(match, innings, batting: true));

            // Wave 2: develop from playing, Test-format-aware (technique/concentration weighted).
            var battingDev = _matchDev.ApplyMatchExperience(player, MatchFormat.Test, bowling: false,
                battingResult.ValuedRating, setup.BaseImportance, rng, setup.MatchDate, world);
            if (battingDev.Redeemed || battingDev.FrozeAgain)
                records.Events.Add(new GameEvent(setup.MatchDate, GameEventType.PlayerDeveloped, battingDev.Summary!, player.Id));

            player.Experience.RecordAppearance(MatchFormat.Test, setup.MatchDate,
                isInternational: setup.Competition?.Scope == CompetitionScope.International,
                isBigMatch: setup.BaseImportance >= 80);

            world?.RecordAppearance(card.PlayerId, MatchFormat.Test);
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
            var bowlingResult = _performance.RecordBowlingPerformance(bowler, spell, competitionPrestige: prestige,
                situation: BuildSituation(match, innings, batting: false));

            var bowlingDev = _matchDev.ApplyMatchExperience(bowler, MatchFormat.Test, bowling: true,
                bowlingResult.ValuedRating, setup.BaseImportance, rng, setup.MatchDate, world);
            if (bowlingDev.Redeemed || bowlingDev.FrozeAgain)
                records.Events.Add(new GameEvent(setup.MatchDate, GameEventType.PlayerDeveloped, bowlingDev.Summary!, bowler.Id));
        }

        // Section Q/W follow-up: same new-ball pairing identification MatchRecorder uses.
        MatchRecorder.RecordNewBallPairing(innings, players, _pairSynergy);

        // --- fielding ---
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
        // Same +1-for-an-unbroken-final-stand rule as MatchRecorder - see PartnershipRecord's
        // class doc for the full reasoning, and MatchRecorder's own comment on this exact line
        // for the bug fix: "unbroken" is the arithmetic comparison against FallOfWickets.Count,
        // not "!IsAllOut" - the latter is wrong whenever the innings ends on the same ball the
        // final wicket also falls.
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
                Format = MatchFormat.Test,
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

            // Section Q/W - see MatchRecorder's identical block for the full reasoning.
            if (players.TryGetValue(partnership.BatterAId, out var chemA) && players.TryGetValue(partnership.BatterBId, out var chemB))
            {
                bool endedInRunOut = !unbroken && p < innings.FallOfWickets.Count
                    && innings.BatterCards.FirstOrDefault(c => c.PlayerId == innings.FallOfWickets[p].BatterOutId)?.Dismissal == DismissalType.RunOut;
                _chemistry.RecordPartnership(chemA, chemB, partnership.Runs, partnership.Balls, MatchFormat.Test, endedInRunOut);
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
            Format = MatchFormat.Test,
            Scope = setup.Competition?.Scope ?? CompetitionScope.DomesticFirstClass,
            CompetitionId = setup.Competition?.Id ?? Guid.Empty,
            CompetitionName = setup.Competition?.Name ?? string.Empty,
            Season = setup.Season,
            InningsNumber = innings.InningsNumber,
            Runs = innings.Runs,
            Wickets = innings.Wickets,
            Overs = innings.LegalBalls / 6.0,
            Extras = innings.Extras,
            Declared = innings.IsDeclared,
            MatchResultForBattingTeam = ResultFor(match, innings.BattingTeamId)
        });
    }

    /// <summary>
    /// The match result from one specific team's perspective. Reads `ResultForHome` (already
    /// correctly computed by the simulator - Win/Loss/Draw/Tie/NoResult, not re-derivable from
    /// `WinningTeamId` alone since a Draw and a NoResult both leave it null) and flips Win/Loss
    /// for whichever side isn't home. Draw/Tie/NoResult are symmetric - both sides share the same
    /// result by definition, unlike a win.
    /// </summary>
    /// <summary>Post-Phase-9 wiring pass (tech-debt item 4): the match SITUATION for a Test performance.</summary>
    private static MatchSituation BuildSituation(MultiDayMatchResult match, InningsState innings, bool batting)
    {
        var teamId = batting ? innings.BattingTeamId : innings.BowlingTeamId;
        var outcome = ResultFor(match, teamId);
        bool? won = outcome switch { MatchOutcome.Win => true, MatchOutcome.Loss => false, _ => (bool?)null };
        return new MatchSituation(
            BaseImportance: match.Setup.BaseImportance,
            WasChase: innings.InningsNumber >= 4 || innings.Target is not null,
            TeamTotal: innings.Runs,
            TeamWicketsInHand: Math.Max(0, 10 - innings.Wickets),
            WicketsTakenByTeam: innings.Wickets,
            TeamWon: won,
            MatchDecided: outcome is MatchOutcome.Win or MatchOutcome.Loss,
            MomentumForTeam: batting ? innings.Momentum.Value : -innings.Momentum.Value);
    }

    private static MatchOutcome ResultFor(MultiDayMatchResult match, Guid teamId)
    {
        bool isHome = teamId == match.Setup.Home.TeamId;
        if (isHome) return match.ResultForHome;

        return match.ResultForHome switch
        {
            MatchOutcome.Win => MatchOutcome.Loss,
            MatchOutcome.Loss => MatchOutcome.Win,
            var symmetric => symmetric
        };
    }

    // ---------------- standings and bonus points ----------------

    private void UpdateStandings(MultiDayMatchResult match, CompetitionSeason? season)
    {
        if (season is null) return;

        var pointsSystem = match.Setup.Points ?? CompetitionPointsSystem.FirstClass;
        var home = match.Setup.Home;
        var away = match.Setup.Away;

        int homeBonus = 0, awayBonus = 0;

        // FC batting/bowling bonus points, from each side's own FIRST innings only - real
        // first-class rules never award them for a second innings. Innings[0] and Innings[1] are
        // always each side's respective first innings regardless of follow-on order (a follow-on
        // changes who bats THIRD, never who bats first or second) - see MultiDayMatchSimulator's
        // innings-construction order for why that holds in every case, enforced or not.
        if (pointsSystem.BattingBowlingBonusEnabled && match.Innings.Count >= 2)
        {
            bool firstBattingIsHome = match.Innings[0].BattingTeamId == home.TeamId;
            var homeFirstInnings = firstBattingIsHome ? match.Innings[0] : match.Innings[1];
            var awayFirstInnings = firstBattingIsHome ? match.Innings[1] : match.Innings[0];

            homeBonus = _bonus.BattingBonusPoints(homeFirstInnings) + _bonus.BowlingBonusPoints(awayFirstInnings);
            awayBonus = _bonus.BattingBonusPoints(awayFirstInnings) + _bonus.BowlingBonusPoints(homeFirstInnings);
        }

        season.GetOrCreateStanding(home.TeamId).RecordResult(ResultFor(match, home.TeamId), pointsSystem, homeBonus);
        season.GetOrCreateStanding(away.TeamId).RecordResult(ResultFor(match, away.TeamId), pointsSystem, awayBonus);

        // Deliberately no net run rate update. First-class standings are points-based, not
        // NRR-based - NRR stays at its "no data yet" default for a team that has only played
        // multi-day cricket, which is the correct answer, not a fabricated zero.
    }

    // ---------------- injuries ----------------

    /// <summary>Same model as MatchRecorder.ApplyMatchInjuries, generalised to however many innings the match actually had.</summary>
    private void ApplyMatchInjuries(
        MultiDayMatchResult match, IReadOnlyDictionary<Guid, Player> players, MatchRecords records, Random rng, WorldState? world)
    {
        foreach (var innings in match.Innings)
        {
            foreach (var card in innings.BowlerCards.Where(c => c.LegalBallsBowled > 0))
            {
                if (!players.TryGetValue(card.PlayerId, out var player) || player.CurrentInjury is not null) continue;

                int teamMedical = MatchRecorder.TeamMedicalQuality(world, player.CurrentTeamId);
                var groundFacilities = match.Setup.Ground?.Facilities;

                double baseRisk = _medical.GetEffectiveInjuryRisk(player.Physical.InjuryProneness, teamMedical, groundFacilities);

                double workload = card.LegalBallsBowled / 24.0;
                double probability = Math.Clamp(baseRisk / 100.0 * 0.02 * workload, 0, 0.08);

                if (rng.NextDouble() >= probability) continue;

                var (type, severity) = MatchRecorder.RollBowlerInjury(player, rng);

                var injury = world is not null
                    ? world.ApplyInjury(player, type, severity, match.Setup.MatchDate, teamMedical, groundFacilities, rng)
                    : _availability.ApplyInjury(player, type, severity, match.Setup.MatchDate, teamMedical, groundFacilities, rng);

                records.Events.Add(new GameEvent(match.Setup.MatchDate, GameEventType.InjuryOccurred,
                    $"{player.FullName} has been ruled out with a {severity} {type}, expected back around {injury.ExpectedReturnDate:d MMM yyyy}.",
                    player.Id));
            }

            MatchRecorder.MaybeApplyBatterConcussion(innings, players, records, match.Setup.Ground, match.Setup.MatchDate, rng, world, _availability);
        }
    }
}
