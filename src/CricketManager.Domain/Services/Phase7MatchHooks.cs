using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7: the per-match consequences that the finance, awards, discipline, records and umpiring
/// slices all need, bundled into one call FixturePlayService makes after recording a match.
///
/// Deliberately a thin orchestrator over the real services (CareerStatsService, MilestoneService,
/// RecordProgressionService, DisciplineService, UmpireService) - the same separation
/// FixturePlayService already keeps from MatchSimulator / MatchRecorder.
/// </summary>
public sealed class Phase7MatchHooks
{
    private readonly CareerStatsService _careerStats = new();
    private readonly MilestoneService _milestones = new();
    private readonly RecordProgressionService _records = new();
    private readonly CeremonyService _ceremony = new();   // Phase 16 (§15.3): milestone ceremonies
    private readonly DisciplineService _discipline = new();
    private readonly UmpireService _umpires = new();
    private readonly MatchdayRevenueService _matchday = new();

    public sealed record MatchContext(
        MatchRecords Records,
        MatchFormat Format,
        Team HomeTeam, Team AwayTeam,
        IReadOnlyList<Player> HomeXi, IReadOnlyList<Player> AwayXi,
        IReadOnlyList<Player> HomeBowlers, IReadOnlyList<Player> AwayBowlers,
        Guid? HomeCaptainId, Guid? AwayCaptainId,
        IReadOnlyDictionary<DismissalType, int> DismissalsByType,
        bool TightFinish,
        double Importance,
        double HomeAdvantage,
        IReadOnlyList<Umpire>? Umpires,
        Ground? Ground,
        Competition? Competition,
        Guid? SeasonId,
        DateOnly Date,
        /// <summary>§2.15: how many short, into-the-body deliveries each bowler sent down THIS
        /// match, by bowler id - null/empty (every caller before this field existed) means no
        /// leg-theory review happens. FixturePlayService computes it from the raw delivery log.</summary>
        IReadOnlyDictionary<Guid, int>? LegTheoryDeliveriesByBowler = null);

    public void Process(WorldState world, MatchContext ctx, Random random, List<GameEvent> events)
    {
        var playersById = world.Players.ToDictionary(p => p.Id);

        // --- appearances: everyone who took the field, for the match count ---
        var appearances = ctx.HomeXi.Select(p => p.Id).Concat(ctx.AwayXi.Select(p => p.Id)).ToHashSet();

        // §5.10: playing a match tops match sharpness back up (a big jump for a rusty returnee,
        // little for a regular who is already sharp).
        foreach (var id in appearances)
            if (playersById.TryGetValue(id, out var appeared))
                appeared.MatchSharpness = Math.Min(100, appeared.MatchSharpness + (100 - appeared.MatchSharpness) * 0.35 + 4);

        // --- career stats + milestones ---
        var before = _careerStats.Snapshot(world, appearances, ctx.Format);
        _careerStats.ApplyMatch(world, ctx.Records, ctx.Format, appearances);
        var milestones = _milestones.Detect(world, ctx.Records, playersById, ctx.Format, ctx.Date, before);
        events.AddRange(milestones);

        // Phase 16 (§15.3): the big landmarks get a ceremony - a guard of honour, a presentation -
        // with a real, bounded lift for the player and a touch of warmth for his side.
        events.AddRange(_ceremony.Process(world, milestones, ctx.Date));

        // --- all-time records ---
        events.AddRange(_records.ReviewMatch(world, ctx.Records, ctx.Date));

        // --- discipline ---
        double overRateSeverity = ctx.Competition?.EffectiveConditions.OverRatePenaltySeverity ?? 1.0;
        // §16.2: the match referee runs the code-of-conduct hearing - a distinct official from the
        // on-field panel, whose firmness shapes how the sanctions land.
        var (refName, refConsistency) = world.Umpires is { Count: > 0 } wp
            ? _umpires.AssignReferee(wp, ctx.Umpires ?? Array.Empty<CricketManager.Domain.Entities.Umpire>(),
                ctx.Competition?.Scope == CricketManager.Domain.Enums.CompetitionScope.International)
            : ("the match referee", 0.6);
        events.AddRange(_discipline.ReviewMatch(world, ctx.Date, ctx.Format,
            ctx.HomeTeam, ctx.AwayTeam, ctx.HomeBowlers, ctx.AwayBowlers, ctx.HomeXi, ctx.AwayXi,
            ctx.HomeCaptainId, ctx.AwayCaptainId, ctx.Importance, random, overRateSeverity, refConsistency, refName));

        // §2.15: a sustained leg-theory barrage carries a real conduct cost. Deterministic - see
        // DisciplineService.ReviewIntimidatoryBowling's own doc comment - so it is safe to run
        // unconditionally without touching the RNG stream anything else here consumes.
        if (ctx.LegTheoryDeliveriesByBowler is { Count: > 0 } legTheory)
        {
            // A player could in principle appear in both XIs (a rare loan/transfer edge case) -
            // GroupBy/First rather than ToDictionary so that never crashes a real simulated match
            // over what is a minor disciplinary side-note.
            var allXi = ctx.HomeXi.Concat(ctx.AwayXi).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var (bowlerId, count) in legTheory)
            {
                if (!allXi.TryGetValue(bowlerId, out var bowler)) continue;
                var team = ctx.HomeXi.Any(p => p.Id == bowlerId) ? ctx.HomeTeam : ctx.AwayTeam;
                var charge = _discipline.ReviewIntimidatoryBowling(world, ctx.Date, ctx.Format, team, bowler, count);
                if (charge is not null) events.Add(charge);
            }
        }

        // --- umpiring: post-match career + controversy ---
        if (ctx.Umpires is { Count: > 0 } panel)
        {
            var report = _umpires.ReviewMatch(panel, ctx.DismissalsByType, ctx.TightFinish, ctx.Importance, random, ctx.Format, ctx.Date);
            if (report.Howlers > 0 && (report.Controversy >= 45 || ctx.Importance >= 75))
            {
                // Phase 15 (§16.4): a howler in a decider is a bigger story - the board has queried
                // the appointment, and the official is off the big matches for a spell.
                bool decider = ctx.Importance >= 75;
                string tail = decider && panel.Take(2).Any(u => u.IsUnderScrutiny(ctx.Date))
                    ? " The board has asked for an explanation, and the panel will be reviewed."
                    : "";
                events.Add(new GameEvent(ctx.Date, GameEventType.UmpiringControversy,
                    $"{ctx.HomeTeam.Name} v {ctx.AwayTeam.Name}: {report.Summary}{tail}", ctx.HomeTeam.Id, ctx.AwayTeam.Id));
            }
        }

        // --- matchday income + crowd fill for the finance / competition-reputation slices ---
        if (ctx.Ground is { } ground && ctx.Competition is { } competition)
        {
            var md = _matchday.CalculateMatchday(ground, competition, ctx.HomeTeam.Strength, ctx.AwayTeam.Strength,
                ctx.HomeTeam.Finances.MatchdayIncomeRate, ctx.HomeTeam.Board.FanSentiment);

            world.MatchdayIncomeThisSeason[ctx.HomeTeam.Id] =
                world.MatchdayIncomeThisSeason.TryGetValue(ctx.HomeTeam.Id, out var running) ? running + md.Revenue : md.Revenue;

            if (ctx.SeasonId is { } sid)
            {
                if (!world.SeasonCrowdFill.TryGetValue(sid, out var fills))
                    world.SeasonCrowdFill[sid] = fills = new List<double>();
                fills.Add(md.FillRate);
            }
        }

        // --- year / month form tallies for awards ---
        UpdateFormTally(world.YearForm, world, ctx);
        UpdateFormTally(world.MonthForm, world, ctx);
    }

    private static void UpdateFormTally(Dictionary<Guid, PlayerFormTally> store, WorldState world, MatchContext ctx)
    {
        var perPlayerRating = new Dictionary<Guid, double>();

        // Combined per-player rating: reuse the same batting + bowling ratings the recorder used,
        // approximated here from the match records (runs, wickets) so awards have a signal even
        // without the live analysis report threaded in.
        foreach (var b in ctx.Records.Batting)
            perPlayerRating[b.PlayerId] = perPlayerRating.GetValueOrDefault(b.PlayerId) + b.Runs * 0.35 - 8;
        foreach (var s in ctx.Records.Bowling)
            perPlayerRating[s.PlayerId] = perPlayerRating.GetValueOrDefault(s.PlayerId) + s.Wickets * 14 - s.RunsConceded * 0.12;

        var runsByPlayer = ctx.Records.Batting.GroupBy(b => b.PlayerId)
            .ToDictionary(g => g.Key, g => (Runs: g.Sum(x => x.Runs), Balls: g.Sum(x => x.BallsFaced)));
        var bowlByPlayer = ctx.Records.Bowling.GroupBy(b => b.PlayerId)
            .ToDictionary(g => g.Key, g => (Wickets: g.Sum(x => x.Wickets), Overs: g.Sum(x => x.OversBowled), Conceded: g.Sum(x => x.RunsConceded)));

        var involved = perPlayerRating.Keys.ToHashSet();
        foreach (var id in involved)
        {
            if (!world.Players.Any(p => p.Id == id)) continue;
            var player = world.Players.First(p => p.Id == id);

            if (!store.TryGetValue(id, out var tally))
                store[id] = tally = new PlayerFormTally
                {
                    PlayerId = id, PlayerName = player.FullName, TeamId = player.CurrentTeamId, Age = player.Age(ctx.Date)
                };

            var r = runsByPlayer.GetValueOrDefault(id);
            var w = bowlByPlayer.GetValueOrDefault(id);
            tally.AddMatch(perPlayerRating[id], r.Runs, r.Balls, w.Wickets, w.Overs, w.Conceded);
        }
    }
}
