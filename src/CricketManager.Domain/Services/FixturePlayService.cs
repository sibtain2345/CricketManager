using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What playing the fixtures due on one day produced.</summary>
public sealed record FixturePlayReport(int FixturesPlayed, int FixturesSkipped, IReadOnlyList<GameEvent> Events)
{
    public static readonly FixturePlayReport Empty = new(0, 0, Array.Empty<GameEvent>());
}

/// <summary>
/// Phase 6, Slice 6.1: the season engine core. This is the piece that was missing for the whole
/// life of the project - CLAUDE.md's own Slice-15 note: "actually PLAYING a fixture (turning it
/// into a MatchSetup - choosing an XI, resolving weather, etc.) is Phase 6 territory".
///
/// On each day the world clock advances, this finds the fixtures scheduled for that day and plays
/// them: it resolves the two sides (including a knockout slot that only became concrete when its
/// feeder was played), generates the weather, works out how big an occasion it is (competition
/// prestige, the stage, any rivalry), picks each side's XI, assembles the setup, runs the match
/// engine and the recorder (so standings, records, rankings, form, reputation and injuries all
/// move exactly as they do for a hand-run match), marks the fixture complete and advances any
/// bracket that feeds from it.
///
/// Deliberately separate from the match engine and the recorder, the same separation of concerns
/// they already keep from each other: building the schedule, playing a match, and recording its
/// consequences are three different jobs.
///
/// Determinism: every fixture gets its own RNG stream from GameCalendar.RandomForFixture(date,
/// index), where index is the fixture's position in the day's DETERMINISTICALLY SORTED list -
/// never anything derived from a Guid, per this project's determinism history.
/// </summary>
public sealed class FixturePlayService
{
    private readonly MatchSimulator _matchSim = new();
    private readonly MultiDayMatchSimulator _multiDaySim = new();
    private readonly MatchRecorder _recorder = new();
    private readonly MultiDayMatchRecorder _multiDayRecorder = new();
    private readonly MatchWeatherService _weather = new();
    private readonly XiSelectionService _xi = new();
    private readonly PlayoffBracketService _bracket = new();
    private readonly MatchdayRevenueService _matchday = new();
    private readonly PostMatchAnalysisService _analysis = new();
    private readonly PreMatchReportService _preMatch = new();          // post-Phase-9 wiring pass: pre-match conditions/head-to-head news
    private readonly AiTacticalPlanner _planner = new();               // post-Phase-9 wiring pass: a real TacticalPlan for each AI side
    private readonly CommentaryService _commentary = new();            // post-Phase-9 wiring pass: match highlight lines
    private readonly UmpireService _umpireService = new();   // Phase 7, Slice 7.9
    private readonly Phase7MatchHooks _phase7 = new();        // Phase 7: per-match finance/awards/discipline/records/umpiring
    private readonly OverseasRegistrationService _overseas = new(); // Phase 9, Slice 9.5: overseas-player cap for franchise fixtures
    private readonly PitchDoctoringService _doctoring = new();       // Phase 15, §19.3: home side prepares the pitch to suit itself

    private static List<Player> SquadPlayers(Team team, IReadOnlyDictionary<Guid, Player> playersById) =>
        team.SquadPlayerIds.Select(id => playersById.TryGetValue(id, out var p) ? p : null)
            .Where(p => p is not null).Select(p => p!).ToList();

    /// <summary>Plays every Scheduled fixture dated <paramref name="date"/>. Safe to call on a day with no fixtures.</summary>
    public FixturePlayReport PlayDueFixtures(WorldState world, GameCalendar calendar, DateOnly date)
    {
        // Deterministic, Guid-INDEPENDENT ordering. SequenceNumber is assigned per COMPETITION
        // starting from 0, so when several competitions run in the same window (a domestic
        // promotion/relegation pyramid, say) many same-day fixtures share a SequenceNumber - and
        // (SequenceNumber, RoundNumber, Stage) alone then ties, leaving the order to a stable sort
        // over world.Fixtures. Competition NAME (unique, seed-stable) is the final tiebreak, so the
        // day's fixtures play in exactly the same order across two runs of the same seed.
        var compNames = world.Competitions.ToDictionary(c => c.Id, c => c.Name);
        var due = world.Fixtures
            .Where(f => f.Status == FixtureStatus.Scheduled && f.ScheduledDate == date)
            .OrderBy(f => f.SequenceNumber)
            .ThenBy(f => f.RoundNumber)
            .ThenBy(f => f.Stage, StringComparer.Ordinal)
            .ThenBy(f => compNames.GetValueOrDefault(f.CompetitionId, string.Empty), StringComparer.Ordinal)
            .ToList();

        if (due.Count == 0) return FixturePlayReport.Empty;

        var events = new List<GameEvent>();
        int played = 0, skipped = 0;
        var playersById = world.Players.ToDictionary(p => p.Id);

        for (int i = 0; i < due.Count; i++)
        {
            var fixture = due[i];
            // Key the fixture's RNG on its stable SequenceNumber when it has one (generated
            // fixtures), falling back to the day-position for a hand-built fixture with none.
            var rng = calendar.RandomForFixture(date, fixture.SequenceNumber != 0 ? fixture.SequenceNumber : i);

            if (fixture.HomeTeamId is not { } homeId || fixture.AwayTeamId is not { } awayId
                || !world.Teams.TryGetValue(homeId, out var home) || !world.Teams.TryGetValue(awayId, out var away))
            {
                // A knockout slot whose feeder has not been played yet, or a dangling reference.
                // Nothing to do - it will be playable once the feeder resolves (or never, if the
                // schedule is broken, which is a generation bug, not this service's to paper over).
                skipped++;
                continue;
            }

            var competition = world.Competitions.FirstOrDefault(c => c.Id == fixture.CompetitionId);
            var season = world.CompetitionSeasons.FirstOrDefault(s => s.Id == fixture.SeasonId)
                         ?? world.CompetitionSeasons.FirstOrDefault(s => competition is not null && s.CompetitionId == competition.Id && s.Year == date.Year);
            var ground = ResolveGround(world, fixture, home);
            var format = competition?.Format ?? MatchFormat.T20;

            var rivalry = world.Rivalries.FirstOrDefault(r => r.Match(homeId, awayId));
            double importance = ComputeImportance(competition, fixture, rivalry);
            double homeAdvantage = ComputeHomeAdvantage(home, ground, competition, away, rivalry, fixture, rng);

            var weather = _weather.Generate(ground, date, rng, dayOfMatch: 1,
                rainRiskMultiplier: world.ProfileFor(ground?.Country).RainRiskMultiplier);

            // Phase 7, Slice 7.9: assign the match officials. Null when the world has no umpire
            // panel (every pre-Phase-7 world) - byte-identical to before. OutBiasFor is the
            // deterministic ball-model multiplier; the panel list carries identity for the review.
            IReadOnlyList<Umpire>? umpires = null;
            double umpireOutBias = 1.0;
            if (world.Umpires.Count >= 2)
            {
                bool isInternational = competition?.Scope == CompetitionScope.International;
                bool isKnockout = fixture.Stage.Length > 0 && (fixture.Stage.Contains("Final") || fixture.Stage.Contains("Semi") || fixture.Stage.Contains("Qualifier") || fixture.Stage.Contains("Eliminator"));
                umpires = _umpireService.AssignPanel(world.Umpires.ToList(), isInternational, isKnockout, importance,
                    home.Country, away.Country, rng, date);
                umpireOutBias = _umpireService.OutBiasFor(umpires, importance, homeAdvantage);
            }

            // Slice 6.6 + section F: a dead rubber is played with lower stakes. Whom to rest is NOT
            // decided by the dead-rubber flag alone - it weighs each player's actual condition and
            // recent load and the real fixture calendar (WorkloadRotationService). A rested player
            // is genuinely out of THIS XI.
            bool homeDead = IsDeadRubberFor(world, competition, season, fixture, homeId);
            bool awayDead = IsDeadRubberFor(world, competition, season, fixture, awayId);
            if (homeDead && awayDead) importance *= 0.55;

            var homeRested = _rotation.PlayersToRest(world, home, format, date, homeDead, importance);
            var awayRested = _rotation.PlayersToRest(world, away, format, date, awayDead, importance);

            var homeXi = SelectXi(world, home, format, date, playersById, rested: homeRested, opposition: away);
            var awayXi = SelectXi(world, away, format, date, playersById, rested: awayRested, opposition: home);
            if (homeXi is null || awayXi is null)
            {
                skipped++;
                continue;
            }

            // Phase 9, Slice 9.5: a franchise league caps overseas (non-host-nation) players in the
            // XI. OverseasRegistrationService swaps the surplus out and flags them Unregistered for
            // this fixture; the flags are cleared straight after the match.
            List<Player> flaggedThisFixture = new();
            // A FRANCHISE league caps overseas players in the XI at Competition.OverseasPlayerLimit
            // (4). A DOMESTIC competition uses the much tighter host-country rule from its
            // CountryProfile - at most 2 overseas players in the matchday XI (the user's "every
            // country benefits from every other" principle; the squad-level 4/>=1-associate rule is
            // enforced when a club signs a foreign player, not here).
            int foreignLimit = competition?.OverseasPlayerLimit ?? 0;
            string hostNation = competition?.Country ?? string.Empty;
            if (foreignLimit == 0 && competition is not null && competition.Scope != CompetitionScope.International && !string.IsNullOrEmpty(competition.Country))
            {
                var prof = world.ProfileFor(competition.Country);
                foreignLimit = prof.AllowsForeignDomesticPlayers ? prof.DomesticXiOverseasLimit : 0;
            }
            if (foreignLimit > 0 && !string.IsNullOrEmpty(hostNation))
            {
                var homeSquad = SquadPlayers(home, playersById);
                var awaySquad = SquadPlayers(away, playersById);
                homeXi = _overseas.EnforceLimit(homeXi, homeSquad, hostNation, foreignLimit, format);
                awayXi = _overseas.EnforceLimit(awayXi, awaySquad, hostNation, foreignLimit, format);
                flaggedThisFixture.AddRange(homeSquad.Concat(awaySquad).Where(p => p.NonInjuryUnavailability == UnavailabilityReason.Unregistered));
            }

            var result = format == MatchFormat.Test
                ? PlayMultiDay(world, calendar, fixture, home, away, homeXi, awayXi, ground, competition, season, weather, date, importance, homeAdvantage, umpires, umpireOutBias, rng, playersById, events)
                : PlayLimitedOvers(world, calendar, fixture, home, away, homeXi, awayXi, ground, competition, season, weather, date, format, importance, homeAdvantage, umpires, umpireOutBias, rng, playersById, events);

            // Bracket advancement + fixture completion. A no-result in a knockout falls back to
            // the nominal home team so the bracket can still progress (a rare edge - the match
            // engine breaks a limited-overs tie itself in almost every case).
            var bracket = world.Fixtures.Where(f => f.SeasonId == fixture.SeasonId).ToList();
            bool feedsForward = fixture.HomeFeederFixtureId is not null || fixture.AwayFeederFixtureId is not null
                                || bracket.Any(x => x.HomeFeederFixtureId == fixture.Id || x.AwayFeederFixtureId == fixture.Id);

            if (result.WinnerId is { } winnerId)
            {
                _bracket.RecordFixtureResult(fixture, winnerId, result.MatchId, bracket);
            }
            else if (feedsForward)
            {
                _bracket.RecordFixtureResult(fixture, homeId, result.MatchId, bracket);
                events.Add(new GameEvent(date, GameEventType.MatchCompleted,
                    $"{home.Name} advance over {away.Name} (no result on the day).", homeId, awayId));
            }
            else
            {
                fixture.CompleteNoResult(result.MatchId);
            }

            if (rivalry is not null) ApplyRivalryAftermath(rivalry, fixture, result, home, away, date);

            // Phase 9, Slice 9.5: release the transient overseas-cap flags now the fixture is done.
            OverseasRegistrationService.ClearFixtureFlags(flaggedThisFixture);

            events.Add(new GameEvent(date, GameEventType.MatchCompleted, result.Summary, result.WinnerId ?? homeId,
                result.WinnerId is null ? null : (result.WinnerId == homeId ? awayId : homeId)));
            played++;
        }

        return new FixturePlayReport(played, skipped, events);
    }

    private readonly record struct PlayedMatch(Guid MatchId, Guid? WinnerId, string Summary);

    // ---------------- limited overs ----------------

    private PlayedMatch PlayLimitedOvers(
        WorldState world, GameCalendar calendar, Fixture fixture, Team home, Team away,
        XiSelectionResult homeXi, XiSelectionResult awayXi, Ground? ground, Competition? competition,
        CompetitionSeason? season, MatchWeather weather, DateOnly date, MatchFormat format,
        double importance, double homeAdvantage, IReadOnlyList<Umpire>? umpires, double umpireOutBias,
        Random rng, IReadOnlyDictionary<Guid, Player> playersById, List<GameEvent> events)
    {
        int overs = format == MatchFormat.ODI ? 50 : 20;

        // A separate, fixture-keyed stream for the presentation/planning layer so wiring it in does
        // NOT shift the match simulation's own rng position - match outcomes still change (an AI
        // side now plays to a real plan, read deterministically from the setup), but the shift is
        // the plan, not incidental rng churn.
        var presRng = PresentationRng(calendar, fixture, date);
        EmitPreMatchReport(world, home, away, ground, format, weather, date, events, homeXi.BattingOrder, awayXi.BattingOrder);
        var homePlan = _planner.BuildPlan(world, home, homeXi.BattingOrder, away, awayXi.BattingOrder, ground, format, importance, presRng);
        var awayPlan = _planner.BuildPlan(world, away, awayXi.BattingOrder, home, homeXi.BattingOrder, ground, format, importance, presRng);

        var homeSide = ToSide(home, homeXi);
        var awaySide = ToSide(away, awayXi);

        var setup = new MatchSetup
        {
            Home = homeSide,
            Away = awaySide,
            Format = format,
            OversPerInnings = overs,
            Ground = ground,
            Competition = competition,
            MatchDate = date,
            Season = date.Year,
            BaseImportance = importance,
            HomeAdvantage = homeAdvantage,
            Umpires = umpires,
            UmpireOutBias = umpireOutBias,
            DrsReviewsPerInnings = competition?.EffectiveConditions.DrsReviewsPerInnings ?? 0,
            ResolveTiesWithSuperOver = (competition?.EffectiveConditions.LimitedOversTiebreak ?? LimitedOversTiebreak.SuperOver) == LimitedOversTiebreak.SuperOver
                                       && (IsKnockout(fixture) || competition?.Scope is CompetitionScope.International or CompetitionScope.FranchiseLeague),
            Weather = weather,
            HomePlan = homePlan,
            AwayPlan = awayPlan,
            FeudingPairs = FeudingPairsIn(world, homeXi, awayXi),
            // §16.6: a TV umpire for line calls - international / franchise cricket, or any
            // competition that runs DRS.
            HasThirdUmpire = competition?.Scope is CompetitionScope.International or CompetitionScope.FranchiseLeague
                || (competition?.EffectiveConditions.DrsReviewsPerInnings ?? 0) > 0,
            HomeLeadership = BuildLeadership(world, home, homeXi, format),
            AwayLeadership = BuildLeadership(world, away, awayXi, format)
        };

        var result = _matchSim.Simulate(setup, rng);
        var teamsById = world.Teams.ToDictionary(kv => kv.Key, kv => kv.Value);
        var records = _recorder.Record(result, playersById, season, world, rng, teamsById, world.TeamRankings);

        if (season is not null)
            AccumulateContributions(world, season.Id, _analysis.PlayerContributions(result));

        var report = _analysis.AnalyzeMatch(result);
        ApplyPlayerOfTheMatch(report.PlayerOfTheMatch, playersById, date, events);
        EmitMatchStory(report, result.FirstInnings, result.SecondInnings, home, away, playersById, date, events, presRng);

        RunPhase7Hooks(world, records, result.FirstInnings, result.SecondInnings, format, home, away, homeXi, awayXi,
            ground, competition, season, importance, homeAdvantage, umpires,
            TightFinish: result.WinMargin is { } m && (result.WonByWickets ? m <= 2 : m <= 15),
            date, rng, events);

        return new PlayedMatch(result.MatchId, result.NoResult ? null : result.WinningTeamId, result.Summary);
    }

    // ---------------- multi-day ----------------

    private PlayedMatch PlayMultiDay(
        WorldState world, GameCalendar calendar, Fixture fixture, Team home, Team away,
        XiSelectionResult homeXi, XiSelectionResult awayXi, Ground? ground, Competition? competition,
        CompetitionSeason? season, MatchWeather weather, DateOnly date,
        double importance, double homeAdvantage, IReadOnlyList<Umpire>? umpires, double umpireOutBias,
        Random rng, IReadOnlyDictionary<Guid, Player> playersById, List<GameEvent> events)
    {
        bool isInternational = competition?.Scope == CompetitionScope.International;
        int days = isInternational ? 5 : 4;

        var presRng = PresentationRng(calendar, fixture, date);
        EmitPreMatchReport(world, home, away, ground, MatchFormat.Test, weather, date, events, homeXi.BattingOrder, awayXi.BattingOrder);
        var homePlan = _planner.BuildPlan(world, home, homeXi.BattingOrder, away, awayXi.BattingOrder, ground, MatchFormat.Test, importance, presRng);
        var awayPlan = _planner.BuildPlan(world, away, awayXi.BattingOrder, home, homeXi.BattingOrder, ground, MatchFormat.Test, importance, presRng);

        var setup = new MultiDayMatchSetup
        {
            Home = ToSide(home, homeXi),
            Away = ToSide(away, awayXi),
            Days = days,
            Ground = ground,
            Competition = competition,
            Weather = weather,
            MatchDate = date,
            Season = date.Year,
            BaseImportance = importance,
            HomeAdvantage = homeAdvantage,
            Umpires = umpires,
            UmpireOutBias = umpireOutBias,
            DrsReviewsPerInnings = competition?.EffectiveConditions.DrsReviewsPerInnings ?? 0,
            HomePitchPreparation = _doctoring.Decide(home, homeXi.BattingOrder, awayXi.BattingOrder, ground, presRng,
                competition?.EffectiveHomePitchInfluence ?? 1.0),
            // §11.4: a day-night pink-ball Test - occasionally, for an international Test at a
            // floodlit ground. Deterministic from the presentation stream.
            DayNight = isInternational && ground?.HasFloodlights == true && presRng.NextDouble() < 0.28,
            Points = CompetitionPointsSystem.FirstClass,
            HomePlan = homePlan,
            AwayPlan = awayPlan,
            // §19.4: a re-used strip if this ground hosted a completed match in the last 12 days.
            PitchIsReused = ground is not null && world.Fixtures.Any(f => f.Status == FixtureStatus.Completed
                && (f.HomeTeamId == home.Id || f.AwayTeamId == home.Id)
                && f.ScheduledDate < date && f.ScheduledDate >= date.AddDays(-12)),
            HasThirdUmpire = true, // first-class cricket always has a TV umpire
            FeudingPairs = FeudingPairsIn(world, homeXi, awayXi),
            HomeLeadership = BuildLeadership(world, home, homeXi, MatchFormat.Test),
            AwayLeadership = BuildLeadership(world, away, awayXi, MatchFormat.Test)
        };

        // Correction 2: the quality FLOOR on home-pitch shaping. A genuine bilateral / domestic
        // push on a poorly-resourced square can produce a surface rated poor - a real, occasional
        // bad outcome (not a soft cap that quietly prevents anything going wrong), with a
        // demerit-style cost to the board, the venue and the fanbase.
        if (setup.HomePitchPreparation != PitchPreparation.Neutral && ground is not null
            && PitchDoctoringService.OverPreparedPoorly(setup.HomePitchPreparation, ground,
                   competition?.EffectiveHomePitchInfluence ?? 1.0, presRng))
        {
            ground.Reputation = Math.Clamp(ground.Reputation - 4, 0, 100);
            home.Reputation.Adjust(-2);
            home.Board.FanSentiment = Math.Clamp(home.Board.FanSentiment - 4, 0, 100);
            events.Add(new GameEvent(date, GameEventType.PitchRatedPoor,
                $"The {ground.Name} surface prepared for {home.Name} is rated below standard - {home.Name}'s board takes a demerit and the venue's standing suffers.",
                home.Id, competition?.Id ?? Guid.Empty));
        }

        var result = _multiDaySim.Simulate(setup, rng);
        var teamsById = world.Teams.ToDictionary(kv => kv.Key, kv => kv.Value);
        var records = _multiDayRecorder.Record(result, playersById, season, world, rng, teamsById, world.TeamRankings);

        if (season is not null)
            AccumulateContributions(world, season.Id, _analysis.PlayerContributions(result));

        var report = _analysis.AnalyzeMultiDayMatch(result);
        ApplyPlayerOfTheMatch(report.PlayerOfTheMatch, playersById, date, events);

        var firstInnings = result.Innings.Count > 0 ? result.Innings[0] : null;
        var secondInnings = result.Innings.Count > 1 ? result.Innings[1] : firstInnings;
        EmitMatchStory(report, firstInnings, secondInnings, home, away, playersById, date, events, presRng);
        RunPhase7Hooks(world, records, firstInnings, secondInnings, MatchFormat.Test, home, away, homeXi, awayXi,
            ground, competition, season, importance, homeAdvantage, umpires,
            TightFinish: result.WinMargin is { } m && (result.WonByWickets ? m <= 3 : m <= 60),
            date, rng, events);

        return new PlayedMatch(result.MatchId, result.WinningTeamId, result.Summary);
    }

    /// <summary>Phase 7: the per-match consequences (career stats, milestones, records, discipline, umpiring review, matchday income, awards tallies).</summary>
    private void RunPhase7Hooks(
        WorldState world, MatchRecords records, InningsState? first, InningsState? second, MatchFormat format,
        Team home, Team away, XiSelectionResult homeXi, XiSelectionResult awayXi,
        Ground? ground, Competition? competition, CompetitionSeason? season,
        double importance, double homeAdvantage, IReadOnlyList<Umpire>? umpires, bool TightFinish, DateOnly date,
        Random rng, List<GameEvent> events)
    {
        var dismissals = new Dictionary<DismissalType, int>();
        // §2.15: how many short, into-the-body deliveries each bowler sent down this match - the
        // real, observable signal DisciplineService.ReviewIntimidatoryBowling judges on.
        var legTheoryByBowler = new Dictionary<Guid, int>();
        foreach (var innings in new[] { first, second }.Where(i => i is not null))
        {
            foreach (var d in innings!.Deliveries.Where(x => x.Outcome.IsWicket))
                dismissals[d.Outcome.Dismissal] = dismissals.GetValueOrDefault(d.Outcome.Dismissal) + 1;
            foreach (var d in innings!.Deliveries.Where(x => x.Line is BowlingLine.IntoTheBody or BowlingLine.LegStump && x.Length == BowlingLength.Short))
                legTheoryByBowler[d.BowlerId] = legTheoryByBowler.GetValueOrDefault(d.BowlerId) + 1;
        }

        var ctx = new Phase7MatchHooks.MatchContext(
            records, format, home, away,
            homeXi.BattingOrder, awayXi.BattingOrder, homeXi.Bowlers, awayXi.Bowlers,
            home.GetCaptain(format), away.GetCaptain(format),
            dismissals, TightFinish, importance, homeAdvantage, umpires,
            ground, competition, season?.Id, date, legTheoryByBowler);

        _phase7.Process(world, ctx, rng, events);
    }

    // ---------------- setup helpers ----------------

    private static MatchSide ToSide(Team team, XiSelectionResult xi) =>
        new(team.Id, team.Name, xi.BattingOrder, xi.Bowlers);

    /// <summary>
    /// Phase 14 (§18.1, in-match): the genuine feuds within each XI - a strong Feud edge between two
    /// team-mates who are both playing. Fed to the setup so their run-out risk carries a small extra
    /// multiplier whenever they are batting together. Cross-team feuds are irrelevant here (they
    /// never bat as a pair).
    /// </summary>
    private static IReadOnlyCollection<(Guid, Guid)> FeudingPairsIn(WorldState world, XiSelectionResult homeXi, XiSelectionResult awayXi)
    {
        if (world.PlayerRelationships.Count == 0) return Array.Empty<(Guid, Guid)>();
        var pairs = new List<(Guid, Guid)>();
        foreach (var xi in new[] { homeXi, awayXi })
        {
            var ids = xi.BattingOrder.Select(p => p.Id).ToHashSet();
            foreach (var r in world.PlayerRelationships)
                if (r.Kind == RelationshipKind.Feud && r.Strength >= 55 && ids.Contains(r.PlayerAId) && ids.Contains(r.PlayerBId))
                    pairs.Add((r.PlayerAId, r.PlayerBId));
        }
        return pairs;
    }

    private readonly PlayerMoraleService _playerMorale = new();

    /// <summary>
    /// Post-Phase-6 carry-forward: a player-of-the-match award is a small, bounded lift - morale
    /// and a touch of reputation (which feeds his standing in the room). Was proposed in the
    /// Post-Phase-5 plan and never wired; FixturePlayService is the natural place, since it is the
    /// only thing that plays a match and then has the world in hand to react.
    /// </summary>
    private void ApplyPlayerOfTheMatch(PerformanceHighlight? potm, IReadOnlyDictionary<Guid, Player> playersById, DateOnly date, List<GameEvent> events)
    {
        if (potm is null || !playersById.TryGetValue(potm.PlayerId, out var player)) return;
        _playerMorale.AdjustForIndividualHonour(player, weight: 1.0);
        events.Add(new GameEvent(date, GameEventType.MatchCompleted,
            $"{player.FullName} is named player of the match.", player.Id));
    }

    /// <summary>Fixture-keyed stream for the presentation/planning layer - see the call site.</summary>
    private static Random PresentationRng(GameCalendar calendar, Fixture fixture, DateOnly date) =>
        calendar.RandomForFixture(date, (fixture.SequenceNumber != 0 ? fixture.SequenceNumber : 1) + 90_000);

    /// <summary>
    /// Post-Phase-9 wiring pass: a toss-independent pre-match preview (pitch, weather, what it
    /// favours, recent head-to-head) as a news item. Reads `PreMatchReportService`, which was built
    /// (Wave 8) and never called.
    /// </summary>
    private void EmitPreMatchReport(WorldState world, Team home, Team away, Ground? ground,
        MatchFormat format, MatchWeather weather, DateOnly date, List<GameEvent> events,
        IReadOnlyList<Player>? homeXi = null, IReadOnlyList<Player>? awayXi = null)
    {
        if (ground is null) return;
        var h2h = PreMatchReportService.HeadToHead(world.Fixtures, home.Id, away.Id);
        var duels = homeXi is not null && awayXi is not null
            ? PreMatchReportService.KeyDuels(homeXi, awayXi)
            : null;
        if (homeXi is not null && awayXi is not null)
        {
            var hoodoos = PreMatchReportService.GroundHoodoos(homeXi.Concat(awayXi).ToList(), ground.Id, ground.Name, take: 2);
            if (hoodoos.Count > 0)
                duels = (duels ?? Array.Empty<string>()).Concat(hoodoos).ToList();
        }
        var report = _preMatch.Build(ground, format, weather, groundHistory: null, dayOfMatch: 1,
            headToHead: (home.Name, away.Name, home.Id, away.Id, h2h), keyDuels: duels);

        string lead = $"PREVIEW - {home.Name} v {away.Name} at {report.GroundName}: {report.WhatItFavours}";
        var extra = report.Lines.Take(2).ToList();
        if (extra.Count > 0) lead += " " + string.Join(" ", extra);
        events.Add(new GameEvent(date, GameEventType.MatchPreview, lead, home.Id, away.Id));
    }

    /// <summary>
    /// Post-Phase-9 wiring pass: the full post-match report (only `.PlayerOfTheMatch` was read
    /// before - the headline, key moments and session narrative were computed and thrown away) plus
    /// a couple of `CommentaryService` highlight lines, as news. `CommentaryService` and the report
    /// body had no consumer.
    /// </summary>
    private void EmitMatchStory(MatchAnalysisReport report, InningsState? first, InningsState? second,
        Team home, Team away, IReadOnlyDictionary<Guid, Player> playersById, DateOnly date, List<GameEvent> events, Random rng)
    {
        var moment = report.KeyMoments.FirstOrDefault();
        if (moment is not null)
            events.Add(new GameEvent(date, GameEventType.MatchStory,
                $"{home.Name} v {away.Name}: {moment.Description}", home.Id, away.Id));

        // Phase 15 (§1.7): a match where DRS genuinely swung things.
        int overturns = new[] { first, second }.Where(i => i is not null).Sum(i => i!.DrsOverturns);
        if (overturns >= 2)
            events.Add(new GameEvent(date, GameEventType.MatchStory,
                $"{home.Name} v {away.Name}: {overturns} on-field decisions were overturned on review - DRS had a real say in this one.", home.Id, away.Id));

        // A couple of highlight lines from the innings with the most going on.
        var innings = new[] { first, second }.Where(i => i is not null).Select(i => i!)
            .OrderByDescending(i => i.Deliveries.Count).FirstOrDefault();
        if (innings is not null)
        {
            var comm = _commentary.GenerateInningsCommentary(innings, playersById, rng);
            var highlights = comm
                .Where(c => c.Tags.Contains(CommentaryTag.Wicket) || c.Tags.Contains(CommentaryTag.Six) || c.Tags.Contains(CommentaryTag.Milestone))
                .Reverse().Take(3).Reverse().ToList();
            if (highlights.Count > 0)
                events.Add(new GameEvent(date, GameEventType.MatchStory,
                    "Highlights: " + string.Join(" | ", highlights.Select(h => h.Text)), home.Id, away.Id));
        }
    }

    private static void AccumulateContributions(WorldState world, Guid seasonId, IEnumerable<PostMatchAnalysisService.MatchPlayerContribution> contributions)
    {
        if (!world.SeasonContributions.TryGetValue(seasonId, out var byPlayer))
            world.SeasonContributions[seasonId] = byPlayer = new Dictionary<Guid, (string, double)>();

        foreach (var c in contributions)
        {
            double running = byPlayer.TryGetValue(c.PlayerId, out var existing) ? existing.Rating : 0;
            byPlayer[c.PlayerId] = (c.PlayerName, running + c.Rating);
        }
    }

    private static Ground? ResolveGround(WorldState world, Fixture fixture, Team home)
    {
        if (fixture.GroundId is { } gid && world.Grounds.TryGetValue(gid, out var g)) return g;
        if (home.HomeGroundId is { } hg && world.Grounds.TryGetValue(hg, out var homeGround)) return homeGround;
        return null;
    }

    private readonly WorkloadRotationService _rotation = new();

    private XiSelectionResult? SelectXi(WorldState world, Team team, MatchFormat format, DateOnly date, IReadOnlyDictionary<Guid, Player> playersById, IReadOnlySet<Guid>? rested = null, Team? opposition = null)
    {
        var squad = team.SquadPlayerIds
            .Select(id => playersById.TryGetValue(id, out var p) ? p : null)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        // §3.3: a cheap proxy for the opposition's likely top order - their own squad's best
        // recognised batters by ability, not their actual (not-yet-selected) XI. Avoids a
        // chicken-and-egg dependency between the two sides' selections.
        var oppositionTopOrder = opposition?.SquadPlayerIds
            .Select(id => playersById.TryGetValue(id, out var p) ? p : null)
            .Where(p => p is { IsRetired: false } && p.PrimaryRole != PlayerRole.Bowler)
            .Select(p => p!)
            .OrderByDescending(p => p.CurrentAbility)
            .Take(6)
            .ToList();

        var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;

        // Section F: rested players are out of this XI, provided the rest of the squad can still
        // field a legal side.
        if (rested is { Count: > 0 } && squad.Count - rested.Count >= 12)
            squad = squad.Where(p => !rested.Contains(p.Id)).ToList();

        if (squad.Count < 11) return null;

        var announcement = FindAnnouncedSquad(world, team, date);

        var honourAlways = team.ManagerPreferences.AlwaysInclude;
        var xi = _xi.SelectXi(squad, format, date, coach, /*ground*/ null, announcement, nationalSelection: team.IsNational,
            oppositionBattingOrder: oppositionTopOrder);

        // A genuine can't-field-a-side situation (a squad gutted by unavailability). The fixture
        // is skipped and 6.6 re-fixtures it. A side merely short of eleven still plays - short-
        // handed, exactly as XiSelectionService itself is built to allow.
        if (xi.BattingOrder.Count < 7) return null;

        // The nominal team captain, and the AlwaysInclude preference, are not guaranteed to be in
        // a purely merit-ranked XI. That is a selection-realism question for 6.2's AI brain to own
        // fully; here we just make sure the honoured players are not silently left out.
        return EnsureIncluded(xi, squad, honourAlways, team.GetCaptain(format), format);
    }

    /// <summary>
    /// Pass 1 bug 1: forces the captain and any AlwaysInclude players into the XI - but at a
    /// batting position that matches their real role (a middle-order captain does not go in at
    /// eleven), and dropping the player whose loss does the LEAST damage: a pure batsman before a
    /// bowler, and never a bowler if that would take the attack below the format's legal depth.
    /// The old version appended the forced player last and dropped whoever happened to be last -
    /// often a frontline bowler - which both mis-ordered the innings and could strand the attack
    /// short (the exact bowler-depth failure the Post-Phase-5 pass fixed from a different angle).
    /// </summary>
    internal static XiSelectionResult EnsureIncluded(
        XiSelectionResult xi, IReadOnlyList<Player> squad, IReadOnlyList<Guid> alwaysInclude, Guid? captainId, MatchFormat format)
    {
        var order = xi.BattingOrder.ToList();
        var mustHave = new List<Guid>(alwaysInclude);
        if (captainId is { } cid && !mustHave.Contains(cid)) mustHave.Add(cid);

        int bowlingFloor = XiSelectionService.BowlingFloor(format);

        foreach (var id in mustHave)
        {
            if (order.Any(p => p.Id == id)) continue;
            var incoming = squad.FirstOrDefault(p => p.Id == id);
            if (incoming is null || order.Count == 0) continue;

            int bowlersNow = order.Count(p => p.BowlingRole != BowlingRoleType.NotABowler);
            bool incomingBowls = incoming.BowlingRole != BowlingRoleType.NotABowler;

            // Candidates to make room, weakest batting first. Never the specialist keeper, never a
            // must-have. Prefer dropping a non-bowler; only drop a bowler when the attack can spare
            // one (or the incoming player is himself a bowler, so depth is unchanged).
            var droppable = order
                .Where(p => p.Id != xi.Wicketkeeper?.Id && !mustHave.Contains(p.Id))
                .OrderBy(p => p.BowlingRole != BowlingRoleType.NotABowler ? 1 : 0)   // non-bowlers first
                .ThenBy(p => BattingDepthRank(p))                                    // then lower-order roles
                .ThenBy(p => BattingStrength(p))                                     // then the genuinely weakest bat
                .ToList();

            Player? toDrop = droppable.FirstOrDefault(p =>
            {
                bool dropsABowler = p.BowlingRole != BowlingRoleType.NotABowler;
                if (!dropsABowler || incomingBowls) return true;
                return bowlersNow - 1 >= bowlingFloor;
            }) ?? droppable.FirstOrDefault();

            if (toDrop is null) continue;

            order.Remove(toDrop);

            // Insert at a role-appropriate slot rather than appending.
            int slot = RoleSlot(incoming.BattingRole, order.Count);
            order.Insert(Math.Clamp(slot, 0, order.Count), incoming);
        }

        if (order.SequenceEqual(xi.BattingOrder)) return xi;

        var bowlers = order.Where(p => p.BowlingRole != BowlingRoleType.NotABowler).ToList();
        return xi with { BattingOrder = order, Bowlers = bowlers.Count >= 1 ? bowlers : xi.Bowlers };
    }

    /// <summary>Roughly where a batting role bats, as an index into an order of the given length.</summary>
    private static int RoleSlot(BattingRole role, int currentLength) => role switch
    {
        BattingRole.Opener => 0,
        BattingRole.TopOrder => Math.Min(2, currentLength),
        BattingRole.MiddleOrder => Math.Min(4, currentLength),
        BattingRole.LowerOrder => Math.Min(7, currentLength),
        _ => currentLength // Tailender - the end
    };

    /// <summary>Lower = a lower-order role, so it sorts to the front of the "drop this one" list.</summary>
    private static int BattingDepthRank(Player p) => p.BattingRole switch
    {
        BattingRole.Tailender => 0,
        BattingRole.LowerOrder => 1,
        BattingRole.MiddleOrder => 2,
        BattingRole.TopOrder => 3,
        _ => 4
    };

    /// <summary>A rough batting-strength proxy, lower = weaker with the bat, used to pick the least costly player to drop within a role tier.</summary>
    private static double BattingStrength(Player p) =>
        (p.Batting.Technique + p.Batting.Timing + p.Batting.ShotSelection + p.Batting.DefensiveAbility
         + p.Batting.AgainstPace + p.Batting.AgainstSpin) / 6.0;

    private static SquadAnnouncement? FindAnnouncedSquad(WorldState world, Team team, DateOnly date)
    {
        return world.SquadAnnouncements
            .Where(a => a.TeamId == team.Id && a.AnnouncedDate <= date && a.PlayerIds.Count >= 11)
            .OrderByDescending(a => a.AnnouncedDate)
            .FirstOrDefault();
    }

    private static MatchLeadership? BuildLeadership(WorldState world, Team team, XiSelectionResult xi, MatchFormat format)
    {
        if (xi.BattingOrder.Count == 0) return null;

        var nominalCaptainId = team.GetCaptain(format);
        var captain = xi.BattingOrder.FirstOrDefault(p => p.Id == nominalCaptainId)
                      ?? xi.BattingOrder.OrderByDescending(p => p.Mental.Leadership).First();

        var profile = world.CaptaincyProfiles.TryGetValue(captain.Id, out var existing)
            ? existing
            : world.CaptaincyProfiles[captain.Id] = new CaptaincyProfile { PlayerId = captain.Id };

        // S2: a national side with a split coaching structure fields the head coach responsible for this format.
        var coach = team.CoachForFormat(format) is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;

        // Phase 12 (§4.6): a FORMAT-SPECIALIST ASSISTANT runs the side in his format - if this club
        // has one whose FormatFocus matches, and he is the better man for this format, he takes the
        // reins for the match (CaptaincyService/InningsSimulator read MatchLeadership.Coach).
        bool whiteBall = format is MatchFormat.T20 or MatchFormat.ODI;
        var specialist = world.Coaches.FirstOrDefault(c => c.AssistantToTeamId == team.Id && !c.IsRetired
            && ((whiteBall && c.FormatFocus == FormatSpecialisation.WhiteBall)
                || (!whiteBall && c.FormatFocus == FormatSpecialisation.RedBall)));
        if (specialist is not null && (coach is null || specialist.Attributes.TacticalKnowledge >= coach.Attributes.TacticalKnowledge - 1))
            coach = specialist;

        var viceCaptain = xi.BattingOrder.Where(p => p.Id != captain.Id).OrderByDescending(p => p.Mental.Leadership).FirstOrDefault();
        CaptaincyProfile? vcProfile = null;
        if (viceCaptain is not null)
            vcProfile = world.CaptaincyProfiles.TryGetValue(viceCaptain.Id, out var vc)
                ? vc
                : world.CaptaincyProfiles[viceCaptain.Id] = new CaptaincyProfile { PlayerId = viceCaptain.Id };

        return new MatchLeadership
        {
            Captain = captain,
            Profile = profile,
            Coach = coach,
            ViceCaptain = viceCaptain,
            ViceCaptainProfile = vcProfile
        };
    }

    // ---------------- occasion + home advantage ----------------

    /// <summary>
    /// Slice 6.6: is this fixture a dead rubber FOR the given team - i.e. can its result no longer
    /// change whether that team makes the playoffs? Only meaningful for a LeagueWithPlayoffs group
    /// stage, and only once enough of it has been played that "clinched" / "eliminated" is real.
    /// A points-for-a-win model (2) is assumed - conservative, so a genuine live game is never
    /// misread as dead.
    /// </summary>
    private static bool IsDeadRubberFor(WorldState world, Competition? competition, CompetitionSeason? season, Fixture fixture, Guid teamId)
    {
        if (competition is null || season is null) return false;
        if (competition.StructureType != CompetitionStructureType.LeagueWithPlayoffs) return false;
        if (IsKnockout(fixture)) return false;

        int qualifiers = competition.PlayoffQualifierCount > 0 ? competition.PlayoffQualifierCount : 4;
        if (season.Standings.Count <= qualifiers) return false;

        var ranked = season.Standings.OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).ToList();
        int totalGamesEach = (season.ParticipatingTeamIds.Count - 1) * 2; // double round-robin
        double playedFraction = season.Standings.Sum(s => s.Played) / (double)Math.Max(1, totalGamesEach * season.Standings.Count);
        if (playedFraction < 0.65) return false;

        var mine = ranked.FirstOrDefault(s => s.TeamId == teamId);
        if (mine is null) return false;

        int myRemaining = world.Fixtures.Count(f => f.SeasonId == season.Id && f.Status == FixtureStatus.Scheduled
                                                   && (f.HomeTeamId == teamId || f.AwayTeamId == teamId));
        int myMaxGain = myRemaining * 2;
        int myPos = ranked.IndexOf(mine);

        // Clinched: even if every rival below the line wins out, they cannot catch me.
        var firstOutside = ranked.Skip(qualifiers).FirstOrDefault();
        bool clinched = myPos < qualifiers && firstOutside is not null
            && mine.Points > firstOutside.Points + RivalsMaxGain(world, season, firstOutside.TeamId);

        // Eliminated: even winning out, I cannot reach the last qualifying spot.
        var lastInside = ranked.Skip(qualifiers - 1).FirstOrDefault();
        bool eliminated = myPos >= qualifiers && lastInside is not null
            && mine.Points + myMaxGain < lastInside.Points;

        return clinched || eliminated;
    }

    private static int RivalsMaxGain(WorldState world, CompetitionSeason season, Guid teamId) =>
        world.Fixtures.Count(f => f.SeasonId == season.Id && f.Status == FixtureStatus.Scheduled
                                 && (f.HomeTeamId == teamId || f.AwayTeamId == teamId)) * 2;

    private static bool IsKnockout(Fixture f)
    {
        var s = f.Stage.ToLowerInvariant();
        return s.Contains("final") || s.Contains("semi") || s.Contains("qualifier") || s.Contains("eliminator") || s.Contains("quarter");
    }

    private static double ComputeImportance(Competition? competition, Fixture fixture, Rivalry? rivalry)
    {
        double basePrestige = competition?.Prestige ?? 50;

        // The stage of the competition. A final is a genuinely different occasion from a group game.
        string stage = fixture.Stage.ToLowerInvariant();
        double stageLift =
            stage.Contains("final") && !stage.Contains("semi") && !stage.Contains("quarter") ? 28 :
            stage.Contains("qualifier 1") ? 20 :
            stage.Contains("semi") || stage.Contains("qualifier") || stage.Contains("eliminator") ? 16 :
            stage.Contains("quarter") ? 10 :
            0;

        double rivalryLift = rivalry is null ? 0 : rivalry.Intensity * 0.22; // up to ~+22 for a max derby

        return Math.Clamp(basePrestige * 0.65 + 20 + stageLift + rivalryLift, 5, 99);
    }

    private double ComputeHomeAdvantage(Team home, Ground? ground, Competition? competition, Team away, Rivalry? rivalry, Fixture fixture, Random rng)
    {
        // A base edge just from knowing the conditions and sleeping in your own bed.
        double edge = 0.012;

        // §11.4: a genuinely NEUTRAL venue (the ground is in neither side's country) - a
        // World-Cup-in-a-third-country or a relocated series. The "own bed" edge mostly evaporates.
        if (ground is not null && !string.IsNullOrEmpty(ground.Country)
            && !string.Equals(ground.Country, home.Country, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ground.Country, away.Country, StringComparison.OrdinalIgnoreCase))
            edge = 0.004;

        // Phase 10: tour acclimatisation. A touring international side is worse early in a series -
        // unfamiliar conditions, jet lag, no rhythm - and adjusts over the following matches. Only
        // for a genuine tour (an international match at a ground in the home side's country, the
        // away side from elsewhere), and it decays across the series by RoundNumber.
        if (competition?.Scope == CompetitionScope.International && ground is not null
            && string.Equals(home.Country, ground.Country, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(away.Country, ground.Country, StringComparison.OrdinalIgnoreCase))
        {
            double tourPenalty = fixture.RoundNumber switch { <= 1 => 0.018, 2 => 0.011, 3 => 0.005, _ => 0.0 };
            edge += tourPenalty;
        }

        // Crowd. A packed house lifts the home side; a near-empty ground barely does.
        if (ground is not null && competition is not null && ground.Capacity > 0)
        {
            var gate = _matchday.CalculateMatchday(ground, competition, home.Strength, away.Strength, matchdayIncomeBaseline: 0);
            edge += gate.FillRate * 0.022;
        }

        // A united dressing room turns a home crowd into a genuine force; a fractured one wastes it.
        edge += Math.Clamp((home.DressingRoomHarmony - 50) / 50.0, -1, 1) * 0.008;

        // A derby crowd is louder and the occasion sharper.
        if (rivalry is not null) edge += rivalry.Intensity / 100.0 * 0.01;

        // A touch of match-to-match noise so it is never a fixed number.
        edge += (rng.NextDouble() - 0.5) * 0.004;

        return Math.Clamp(edge, 0, 0.05);
    }

    private static void ApplyRivalryAftermath(Rivalry rivalry, Fixture fixture, PlayedMatch result, Team home, Team away, DateOnly date)
    {
        bool bigStage = fixture.Stage.ToLowerInvariant() is var s && (s.Contains("final") || s.Contains("qualifier") || s.Contains("semi"));
        // Any meeting renews the rivalry (so it doesn't go "stale" and fade); a meeting that
        // mattered actually cranks it up.
        rivalry.Reinforce(date, amount: bigStage ? 8 : 0.8);

        // Beating your rival matters more than an ordinary win, and losing to them stings more -
        // a small extra morale swing on top of what the recorder already applied.
        if (result.WinnerId is not { } winnerId) return;
        double extra = 2.0 + rivalry.Intensity / 100.0 * 3.0;
        var winner = winnerId == home.Id ? home : away;
        var loser = winnerId == home.Id ? away : home;
        winner.Morale.Adjust(extra);
        loser.Morale.Adjust(-extra);
    }
}
