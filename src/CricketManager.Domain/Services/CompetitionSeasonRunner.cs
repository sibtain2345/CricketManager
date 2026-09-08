using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 6, Slice 6.3: the competition-season lifecycle. FixturePlayService plays the individual
/// matches; this is what turns a played-out fixture list into a finished season and then the next
/// one - the loop that makes a seeded world keep running year after year instead of going quiet
/// after one campaign.
///
/// Three jobs:
/// - **Draw the knockout stage** once a league/group stage is complete - qualifiers by the same
///   points/NRR tiebreak CompetitionProgressionService already uses, then a bracket via
///   PlayoffBracketService (straight knockout or IPL-style, per Competition.PlayoffFormat).
/// - **Finish the season** once every fixture is played: decide the champion, pay out prize money
///   (CompetitionRevenueService) into each club's budget, and name the player of the series from
///   the ratings FixturePlayService accumulated match by match.
/// - **Create next year's season**: carry the participants forward, apply promotion/relegation
///   between a linked two-tier pair, and generate its fixtures so it is playable in turn.
/// </summary>
public sealed class CompetitionSeasonRunner
{
    private readonly CompetitionProgressionService _progression = new();
    private readonly CompetitionRevenueService _revenue = new();
    private readonly PlayoffBracketService _bracket = new();
    private readonly FixtureGenerationService _scheduler = new();
    private readonly CompetitionCalendarService _calendar = new();
    private readonly PlayerMoraleService _playerMorale = new();
    private readonly BroadcastRevenueService _broadcast = new();          // Phase 7, Slice 7.2
    private readonly FranchiseFinanceService _franchiseFinance = new();   // follow-up: loss-proof franchise economy
    private readonly CompetitionReputationService _competitionReputation = new(); // Phase 7, Slice 7.2
    private readonly LeaderboardService _leaderboards = new();                    // Phase 12: team of the tournament

    private const int PlayoffLeadDays = 7;

    /// <summary>Slice 6.6: how many times a fixture is re-fixtured before the competition gives up on it.</summary>
    private const int MaxRescheduleAttempts = 3;

    /// <summary>Advances every in-progress season one step where it is ready to - called each day after FixturePlayService.</summary>
    public IEnumerable<GameEvent> Advance(WorldState world, GameCalendar calendar, DateOnly date)
    {
        var events = new List<GameEvent>();

        // Slice 6.6: a fixture whose date has passed without being played (a knockout slot whose
        // feeder slipped, a side that could not field an XI, a cancellation) is re-fixtured rather
        // than dropped - a truncated season is the worse failure. After a few failed attempts it
        // is abandoned (Cancelled and left) so the competition is not chased forever.
        var allFixtures = world.Fixtures.ToList();
        var missedCompNames = world.Competitions.ToDictionary(c => c.Id, c => c.Name);
        foreach (var missed in world.Fixtures
                     .Where(f => f.Status == FixtureStatus.Scheduled && f.ScheduledDate < date)
                     .OrderBy(f => f.SequenceNumber)
                     .ThenBy(f => f.ScheduledDate)
                     .ThenBy(f => missedCompNames.GetValueOrDefault(f.CompetitionId, string.Empty), StringComparer.Ordinal)
                     .ToList())
        {
            // Pass 1 bug 2: a knockout fixture whose feeder has not produced a result YET is not
            // "missed" - it was never playable. Push its date without counting the attempt, so a
            // slow-resolving bracket (a feeder that itself needed rescheduling) can never abandon
            // the fixture that depends on it.
            //
            // But only while the feeders are still LIVE. If a feeder has itself been cancelled, the
            // slot is never going to fill on its own - salvage what we can (advance the surviving
            // feeder's winner as a walkover) or, failing that, let this fixture fall into normal
            // missed handling so the season is not blocked forever.
            if (missed.AwaitingFeederResult)
            {
                if (FeedersStillLive(missed, allFixtures))
                {
                    missed.Reschedule(date.AddDays(3), countsAsAttempt: false);
                    continue;
                }

                if (SalvageFromDeadFeeder(missed, allFixtures))
                {
                    // A slot was filled from the surviving side - re-fixture it normally now.
                    missed.Reschedule(date.AddDays(4));
                    events.Add(new GameEvent(date, GameEventType.FixturePostponed,
                        $"A {missed.Stage} fixture is re-fixtured for {missed.ScheduledDate:yyyy-MM-dd} after a walkover.", missed.CompetitionId));
                    continue;
                }
                // fall through: no feeder can ever resolve this - treat as an ordinary miss
            }

            if (missed.RescheduleCount >= MaxRescheduleAttempts)
            {
                missed.Cancel();
                events.Add(new GameEvent(date, GameEventType.FixturePostponed,
                    $"A {missed.Stage} fixture has been abandoned after repeated postponements.", missed.CompetitionId));
                continue;
            }

            int restDays = 3 + missed.RescheduleCount * 2;
            missed.Reschedule(date.AddDays(restDays));
            events.Add(new GameEvent(date, GameEventType.FixturePostponed,
                $"A {missed.Stage} fixture is re-fixtured for {missed.ScheduledDate:yyyy-MM-dd}.", missed.CompetitionId));
        }

        foreach (var competition in world.Competitions.OrderBy(c => c.Name))
        {
            foreach (var season in world.CompetitionSeasons
                         .Where(s => s.CompetitionId == competition.Id && !s.IsCompleted)
                         .OrderBy(s => s.Year).ToList())
            {
                var fixtures = world.Fixtures.Where(f => f.SeasonId == season.Id).ToList();
                if (fixtures.Count == 0) continue;

                var groupStage = fixtures.Where(f => !IsKnockoutStage(f.Stage)).ToList();
                var knockout = fixtures.Where(f => IsKnockoutStage(f.Stage)).ToList();

                bool groupStageDone = groupStage.Count > 0 && groupStage.All(f => f.Status != FixtureStatus.Scheduled);
                if (!groupStageDone) continue;

                // 1. Draw the knockout stage.
                if (knockout.Count == 0 && NeedsPlayoffs(competition))
                {
                    var startDate = groupStage.Max(f => f.ScheduledDate).AddDays(PlayoffLeadDays);
                    var bracketFixtures = GeneratePlayoffs(world, competition, season, startDate);
                    if (bracketFixtures.Count > 0)
                    {
                        foreach (var f in bracketFixtures) world.Fixtures.Add(f);
                        events.Add(new GameEvent(date, GameEventType.CompetitionStageAdvanced,
                            $"The {competition.Name} playoffs are set.", competition.Id));
                    }
                    continue;
                }

                // 2. Finish the season.
                bool knockoutDone = knockout.Count == 0 || knockout.All(f => f.Status != FixtureStatus.Scheduled);
                if (!knockoutDone) continue;

                events.AddRange(FinishSeason(world, calendar, date, competition, season, knockout));
            }
        }

        return events;
    }

    private IEnumerable<GameEvent> FinishSeason(
        WorldState world, GameCalendar calendar, DateOnly date,
        Competition competition, CompetitionSeason season, IReadOnlyList<Fixture> knockout)
    {
        var (champion, runnerUp) = DecideChampion(competition, season, knockout);
        if (champion is null) yield break;

        _progression.CompleteSeason(season, champion.Value, runnerUp);

        // Prize money into each club's budget.
        var payouts = _revenue.DistributeSeasonRevenue(competition, season).ToList();
        foreach (var earnings in payouts)
            if (world.Teams.TryGetValue(earnings.TeamId, out var team))
            {
                team.Finances.Budget += earnings.Total;
                if (competition.IsFranchiseAuctionLeague && earnings.PrizeMoney > 0)
                    yield return new GameEvent(date, GameEventType.FranchisePrizeMoney,
                        $"{team.Name} bank {earnings.PrizeMoney:N0} in {competition.Name} prize money ({earnings.Description}).",
                        team.Id, competition.Id);
            }

        // §10.6: domestic / international prize money surfaced as news - one line naming the two
        // biggest earners, so a title win reads as a financial event too (not just franchise
        // leagues, which already had their own line above).
        if (!competition.IsFranchiseAuctionLeague)
        {
            var top = payouts.Where(p => p.PrizeMoney > 0).OrderByDescending(p => p.PrizeMoney).Take(2)
                .Select(p => world.Teams.TryGetValue(p.TeamId, out var t) ? $"{t.Name} ({p.PrizeMoney:N0})" : null)
                .Where(s => s is not null).ToList();
            if (top.Count > 0)
                yield return new GameEvent(date, GameEventType.FranchisePrizeMoney,
                    $"{competition.Name} prize money is paid out - {string.Join(", ", top)} the biggest cheques.",
                    competition.Id);
        }

        // Phase 7, Slice 7.2: broadcast / central-distribution money - the biggest revenue leg,
        // and it was entirely absent. Paid to every club that took part, less equally than prize
        // money (a club's market pull matters).
        var participants = season.ParticipatingTeamIds
            .Select(id => world.Teams.TryGetValue(id, out var t) ? t : null)
            .Where(t => t is not null).Select(t => t!).ToList();
        // §10.3: renegotiate the broadcast-rights deal when it has run out (or sign the first one),
        // against what the competition commands now - so a competition that has grown banks the
        // upside and one that has slipped takes the hit, in a lumpy multi-year way.
        if (_broadcast.RenegotiateDeal(competition, participants, season.Year, new Random(season.Year * 31 + competition.Name.Length)) is { } dealNews)
            yield return dealNews;

        double broadcastTotal = 0;
        foreach (var payout in _broadcast.Distribute(competition, participants))
            if (world.Teams.TryGetValue(payout.TeamId, out var t))
            {
                t.Finances.Budget += payout.Total;
                broadcastTotal += payout.Total;
            }
        if (broadcastTotal > 0)
            yield return new GameEvent(date, GameEventType.BroadcastRevenuePaid,
                $"The {competition.Name} distributes {broadcastTotal:N0} in broadcast revenue to its {participants.Count} clubs.", competition.Id);

        // Follow-up: the franchise-league economy - a guaranteed central pool + prize money +
        // local revenue against the auction spend + operating costs, with a hard reserve FLOOR so
        // NO franchise ever closes a season at a loss (the user's explicit constraint).
        if (competition.IsFranchiseAuctionLeague)
            foreach (var (_, ev) in _franchiseFinance.SettleSeason(world, competition, season, date))
                yield return ev;

        // Phase 7, Slice 7.2 (closes tech-debt item 7): move the competition's earned reputation
        // on crowds, competitiveness and the calibre of the field this season.
        if (world.SeasonCrowdFill.TryGetValue(season.Id, out var fills))
        {
            var change = _competitionReputation.ReviewSeason(competition, season, fills, world.Teams, world);
            world.SeasonCrowdFill.Remove(season.Id);
            if (Math.Abs(change.Delta) >= 0.5)
                yield return new GameEvent(date, GameEventType.CompetitionReputationShift, change.Reason, competition.Id);
        }

        string championName = world.Teams.TryGetValue(champion.Value, out var champTeam) ? champTeam.Name : "The champions";
        yield return new GameEvent(date, GameEventType.SeasonCompleted,
            $"{championName} win the {competition.Name} {season.Year}.", competition.Id, champion);

        // Phase 10: a 2-team international series plays for a named trophy - transfer it (a drawn
        // series retains it with the previous holder).
        if (competition.Scope == CompetitionScope.International && season.ParticipatingTeamIds.Count == 2)
        {
            var a = season.ParticipatingTeamIds[0];
            var b = season.ParticipatingTeamIds[1];

            // §12.5: a bilateral series carries stakes beyond the trophy - winning it lifts the
            // winner's ranking a touch on top of the per-match points, and a marquee series
            // (a genuine rivalry, or a major-adjacent one) matters more.
            Guid? overallWinner = ResolveSeriesWinner(season, champion);
            if (overallWinner is { } sw && world.TeamRankings.Count > 0)
            {
                var rankRow = world.TeamRankings.FirstOrDefault(r => r.TeamId == sw && r.Format == competition.Format);
                if (rankRow is not null)
                {
                    bool marquee = world.Rivalries.Any(r => r.Match(a, b) && r.Intensity >= 55);
                    rankRow.Points = Math.Clamp(rankRow.Points + (marquee ? 1.6 : 0.8), 0, 200);
                }
            }

            var rivalry = world.Rivalries.FirstOrDefault(r => r.Match(a, b) && r.TrophyName is not null);
            if (rivalry is not null)
            {
                Guid? seriesWinner = overallWinner;
                var heldBefore = rivalry.TrophyHolderId;
                rivalry.ContestTrophy(seriesWinner, season.Year, date);
                string holderName = rivalry.TrophyHolderId is { } h && world.Teams.TryGetValue(h, out var ht) ? ht.Name : "the holders";
                yield return new GameEvent(date, GameEventType.TrophyContested,
                    seriesWinner is null
                        ? $"The {season.Year} {rivalry.TrophyName} series is drawn - {holderName} retain the trophy."
                        : rivalry.TrophyHolderId == heldBefore
                            ? $"{holderName} retain the {rivalry.TrophyName}."
                            : $"{holderName} win the {rivalry.TrophyName}.",
                    rivalry.TrophyHolderId, competition.Id);
            }
        }

        // Promotion/relegation resolves the moment BOTH divisions of a linked pair have finished.
        foreach (var ev in TryResolvePromotionRelegation(world, date, competition, season.Year))
            yield return ev;

        // Player of the series, from the ratings FixturePlayService accumulated.
        if (world.SeasonContributions.TryGetValue(season.Id, out var contributions) && contributions.Count > 0)
        {
            // Phase 12: a team of the tournament from the season's contributions.
            if (_leaderboards.TeamOfTheTournament(world, competition, season) is { } tott)
                yield return tott;

            var best = contributions.OrderByDescending(kv => kv.Value.Rating).ThenBy(kv => kv.Value.Name).First();
            var player = world.Players.FirstOrDefault(p => p.Id == best.Key);
            if (player is not null)
            {
                // Post-Phase-6 carry-forward: route through the shared individual-honour effect
                // (morale + reputation), weighted up for a whole tournament and again for an
                // international one, so a series award and a match award move a player the same way.
                double weight = competition.Scope == CompetitionScope.International ? 3.0 : 2.0;
                _playerMorale.AdjustForIndividualHonour(player, weight);
                yield return new GameEvent(date, GameEventType.PlayerOfTheSeriesAwarded,
                    $"{best.Value.Name} is named player of the {competition.Name} {season.Year}.", player.Id, competition.Id);
            }

            // Follow-up: a franchise league also awards an EMERGING player of the tournament - the
            // best young player of the season (IPL's Emerging Player award).
            if (competition.IsFranchiseAuctionLeague)
            {
                var emerging = contributions
                    .Select(kv => (kv.Key, kv.Value, Player: world.Players.FirstOrDefault(p => p.Id == kv.Key)))
                    .Where(x => x.Player is { IsRetired: false } && x.Player.Age(date) <= 23)
                    .OrderByDescending(x => x.Value.Rating)
                    .FirstOrDefault();
                if (emerging.Player is not null)
                {
                    _playerMorale.AdjustForIndividualHonour(emerging.Player, 1.5);
                    yield return new GameEvent(date, GameEventType.TournamentAward,
                        $"{emerging.Value.Name} ({emerging.Player.Age(date)}) is the emerging player of the {competition.Name} {season.Year}.",
                        emerging.Player.Id, competition.Id);
                }
            }

            world.SeasonContributions.Remove(season.Id);
        }
    }

    /// <summary>
    /// Creates the seasons for a given year, generating their fixtures. Participants come from the
    /// PlannedRosters entry when promotion/relegation has produced one, otherwise carried forward
    /// from the previous season. Called from the annual rollover in place of the bare
    /// "new CompetitionSeason".
    /// </summary>
    public IEnumerable<GameEvent> CreateSeasonsForYear(WorldState world, DateOnly asOf, int year)
    {
        var events = new List<GameEvent>();

        foreach (var competition in world.Competitions.OrderBy(c => c.Name))
        {
            if (_calendar.GetStaging(competition, year) is null) continue;
            if (world.CompetitionSeasons.Any(s => s.CompetitionId == competition.Id && s.Year == year)) continue;

            var participants = world.PlannedRosters.TryGetValue((competition.Id, year), out var planned) && planned.Count >= 2
                ? planned
                : CarryForwardParticipants(world, competition);
            world.PlannedRosters.Remove((competition.Id, year));

            // A season row is created every staged year regardless (this is the pre-6.3 behaviour
            // that multi-season history depends on). Fixtures are only generated when there is a
            // real participant list to schedule - a world with no teams yet just gets the row.
            var season = NewSeason(world, competition, year, participants);
            if (participants.Count >= 2) ScheduleSeason(world, competition, season, year);

            events.Add(new GameEvent(asOf, GameEventType.CompetitionSeasonCreated,
                $"The {year} edition of the {competition.Name} has been scheduled.", competition.Id));
        }

        return events;
    }

    /// <summary>
    /// Once BOTH divisions of a linked pair have a completed season for the same year, work out
    /// who swaps and stash the two next-year rosters in PlannedRosters. Called from either
    /// division's FinishSeason - whichever finishes second is the one that triggers it.
    /// </summary>
    private IEnumerable<GameEvent> TryResolvePromotionRelegation(WorldState world, DateOnly date, Competition justFinished, int year)
    {
        // Phase 10: a domestic PYRAMID is a CHAIN of linked pairs (Div1 -> Div2 -> Div3 -> ...).
        // Walk the whole chain the just-finished division belongs to and resolve every adjacent
        // pair whose BOTH seasons have completed - a middle division is the "top" of one pair and
        // the "bottom" of another, so each of its two swaps is applied exactly once (guarded by
        // world.PromotionRelegationResolved).
        var chain = BuildChain(world, justFinished);
        for (int i = 0; i < chain.Count - 1; i++)
        {
            var top = chain[i];
            var bottom = chain[i + 1];
            if (top.PromotionRelegationCount <= 0) continue;

            string key = $"{top.Id}|{year}";
            if (world.PromotionRelegationResolved.Contains(key)) continue;

            var topSeason = world.CompetitionSeasons.FirstOrDefault(s => s.CompetitionId == top.Id && s.Year == year && s.IsCompleted);
            var bottomSeason = world.CompetitionSeasons.FirstOrDefault(s => s.CompetitionId == bottom.Id && s.Year == year && s.IsCompleted);
            if (topSeason is null || bottomSeason is null) continue;

            int swap = Math.Min(top.PromotionRelegationCount, Math.Min(topSeason.Standings.Count, bottomSeason.Standings.Count) - 1);
            if (swap <= 0) continue;

            // Phase 10 (§17.5): a thriving league expands (an extra promotion) / a fading one
            // contracts (an extra relegation). Consumed once.
            int expand = top.PendingExpansion;
            top.PendingExpansion = 0;
            int nUp = Math.Min(swap + Math.Max(0, expand), bottomSeason.Standings.Count - 1);
            int nDown = Math.Min(swap + Math.Max(0, -expand), topSeason.Standings.Count - 1);

            var relegated = BottomTeams(topSeason, nDown);
            var promoted = TopTeams(bottomSeason, nUp);

            // Build on the roster P/R with the OTHER neighbour may already have planned, so a middle
            // division's two swaps compose rather than overwrite each other.
            var topBase = world.PlannedRosters.TryGetValue((top.Id, year + 1), out var pt) ? pt : topSeason.ParticipatingTeamIds.ToList();
            var bottomBase = world.PlannedRosters.TryGetValue((bottom.Id, year + 1), out var pb) ? pb : bottomSeason.ParticipatingTeamIds.ToList();

            world.PlannedRosters[(top.Id, year + 1)] = topBase.Except(relegated).Concat(promoted).ToList();
            world.PlannedRosters[(bottom.Id, year + 1)] = bottomBase.Except(promoted).Concat(relegated).ToList();
            world.PromotionRelegationResolved.Add(key);

            foreach (var id in promoted)
                yield return new GameEvent(date, GameEventType.TeamPromoted,
                    $"{Name(world, id)} are promoted to the {top.Name} for {year + 1}.", id, top.Id);
            foreach (var id in relegated)
                yield return new GameEvent(date, GameEventType.TeamRelegated,
                    $"{Name(world, id)} are relegated to the {bottom.Name} for {year + 1}.", id, bottom.Id);
        }
    }

    /// <summary>The full ordered division chain (top to bottom) that <paramref name="member"/> belongs to.</summary>
    private static List<Competition> BuildChain(WorldState world, Competition member)
    {
        // Walk up to the head of the chain.
        var head = member;
        while (world.Competitions.FirstOrDefault(c => c.SecondTierCompetitionId == head.Id) is { } above)
            head = above;

        var chain = new List<Competition> { head };
        while (chain[^1].SecondTierCompetitionId is { } nextId
               && world.Competitions.FirstOrDefault(c => c.Id == nextId) is { } next
               && !chain.Contains(next))
            chain.Add(next);
        return chain;
    }

    // ---------------- helpers ----------------

    /// <summary>
    /// True while every feeder this fixture is still waiting on can still produce a result - it is
    /// Scheduled (the bracket is just catching up) or Completed (propagation is about to fill the
    /// slot). Only a CANCELLED feeder makes the wait pointless.
    /// </summary>
    private static bool FeedersStillLive(Fixture f, IReadOnlyList<Fixture> all)
    {
        bool FeederOk(Guid? feederId, Guid? filledSlot)
        {
            if (feederId is null || filledSlot is not null) return true; // no feeder, or slot already filled
            var feeder = all.FirstOrDefault(x => x.Id == feederId.Value);
            return feeder is not null && feeder.Status != FixtureStatus.Cancelled;
        }
        return FeederOk(f.HomeFeederFixtureId, f.HomeTeamId) && FeederOk(f.AwayFeederFixtureId, f.AwayTeamId);
    }

    /// <summary>
    /// A feeder has been cancelled and will never produce a result. If the OTHER feeder did
    /// complete, walk its winner (or loser, for a loser-slot) into the empty slot so the bracket
    /// can still progress. Returns true if a slot was filled.
    /// </summary>
    private static bool SalvageFromDeadFeeder(Fixture f, IReadOnlyList<Fixture> all)
    {
        bool filled = false;
        Guid? FromFeeder(Guid? feederId, bool loserSlot)
        {
            var feeder = feederId is null ? null : all.FirstOrDefault(x => x.Id == feederId.Value);
            if (feeder is not { Status: FixtureStatus.Completed }) return null;
            return loserSlot ? feeder.LosingTeamId : feeder.WinningTeamId;
        }

        if (f.HomeTeamId is null && FromFeeder(f.HomeFeederFixtureId, f.HomeFeederIsLoserSlot) is { } h)
        {
            f.HomeTeamId = h; f.HomeTeamPlaceholder = null; filled = true;
        }
        if (f.AwayTeamId is null && FromFeeder(f.AwayFeederFixtureId, f.AwayFeederIsLoserSlot) is { } a)
        {
            f.AwayTeamId = a; f.AwayTeamPlaceholder = null; filled = true;
        }
        return filled;
    }

    private static bool IsKnockoutStage(string stage)
    {
        var s = stage.ToLowerInvariant();
        return s.Contains("final") || s.Contains("semi") || s.Contains("qualifier")
               || s.Contains("eliminator") || s.Contains("quarter") || s.Contains("round of");
    }

    private static bool NeedsPlayoffs(Competition c) =>
        c.StructureType is CompetitionStructureType.LeagueWithPlayoffs
            or CompetitionStructureType.GroupStageKnockout
            or CompetitionStructureType.Knockout;

    private List<Fixture> GeneratePlayoffs(WorldState world, Competition competition, CompetitionSeason season, DateOnly startDate)
    {
        var teams = world.Teams.ToDictionary(kv => kv.Key, kv => kv.Value);

        if (competition.StructureType == CompetitionStructureType.GroupStageKnockout)
        {
            var groups = season.Standings.Select(s => s.GroupName).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g).ToList();
            if (groups.Count == 0) return new List<Fixture>();
            int perGroup = competition.PlayoffQualifierCount > 0 ? competition.PlayoffQualifierCount : 2;
            var rankedGroups = groups.Select(g => (IReadOnlyList<Guid>)_progression.GetGroupQualifiers(season, g, perGroup).ToList()).ToList();
            var seeded = _scheduler.BuildGroupCrossoverSeeding(rankedGroups, perGroup);
            return _bracket.GenerateStraightKnockout(competition.Id, season.Id, seeded, startDate, daysBetweenRounds: PlayoffLeadDays, teams);
        }

        int qualifiers = competition.PlayoffQualifierCount > 0
            ? competition.PlayoffQualifierCount
            : (competition.StructureType == CompetitionStructureType.Knockout ? season.ParticipatingTeamIds.Count : 4);
        qualifiers = Math.Min(qualifiers, season.Standings.Count);
        if (qualifiers < 2) return new List<Fixture>();

        var seededTeams = _progression.GetPlayoffQualifiers(season, qualifiers);

        return competition.PlayoffFormat == PlayoffFormat.IplStyle && qualifiers == 4
            ? _bracket.GenerateIplStylePlayoff(competition.Id, season.Id, seededTeams, startDate, daysBetweenRounds: PlayoffLeadDays, teams)
            : _bracket.GenerateStraightKnockout(competition.Id, season.Id, seededTeams, startDate, daysBetweenRounds: PlayoffLeadDays, teams);
    }

    private (Guid? Champion, Guid? RunnerUp) DecideChampion(Competition competition, CompetitionSeason season, IReadOnlyList<Fixture> knockout)
    {
        if (knockout.Count > 0)
        {
            var final = knockout
                .Where(f => f.Stage.Contains("Final") && !f.Stage.Contains("Semi") && !f.Stage.Contains("Quarter"))
                .OrderByDescending(f => f.ScheduledDate)
                .FirstOrDefault();
            if (final?.WinningTeamId is { } winner)
                return (winner, final.LosingTeamId);
        }

        // Pure league, or a knockout that produced no clean final result.
        var topper = _progression.GetTableTopper(season);
        var second = season.Standings
            .OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate)
            .Skip(1).Select(s => (Guid?)s.TeamId).FirstOrDefault();
        return (topper, second);
    }

    /// <summary>
    /// Phase 10: who won a 2-team international series - the side with more points, or null when
    /// the series is genuinely level (a win each, or all draws). Not just the table topper, which
    /// DecideChampion returns even on a tie.
    /// </summary>
    private static Guid? ResolveSeriesWinner(CompetitionSeason season, Guid? tableTopper)
    {
        if (season.Standings.Count != 2) return tableTopper;
        var ordered = season.Standings.OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).ToList();
        if (ordered[0].Points == ordered[1].Points && Math.Abs(ordered[0].NetRunRate - ordered[1].NetRunRate) < 0.001)
            return null;
        return ordered[0].TeamId;
    }

    private static List<Guid> CarryForwardParticipants(WorldState world, Competition competition)
    {
        var prior = world.CompetitionSeasons
            .Where(s => s.CompetitionId == competition.Id && s.ParticipatingTeamIds.Count >= 2)
            .OrderByDescending(s => s.Year)
            .FirstOrDefault();
        return prior?.ParticipatingTeamIds.ToList() ?? new List<Guid>();
    }

    private static List<Guid> BottomTeams(CompetitionSeason season, int n) =>
        season.Standings.OrderBy(s => s.Points).ThenBy(s => s.NetRunRate).Take(n).Select(s => s.TeamId).ToList();

    private static List<Guid> TopTeams(CompetitionSeason season, int n) =>
        season.Standings.OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate).Take(n).Select(s => s.TeamId).ToList();

    private static CompetitionSeason NewSeason(WorldState world, Competition competition, int year, IReadOnlyList<Guid> participants)
    {
        var season = new CompetitionSeason { CompetitionId = competition.Id, Year = year };
        foreach (var id in participants)
        {
            season.ParticipatingTeamIds.Add(id);
            season.GetOrCreateStanding(id);
        }
        world.CompetitionSeasons.Add(season);
        return season;
    }

    private void ScheduleSeason(WorldState world, Competition competition, CompetitionSeason season, int year)
    {
        var staging = _calendar.GetStaging(competition, year);
        var start = staging?.StartDate ?? new DateOnly(year, competition.Window.StartMonth, competition.Window.StartDay);
        var end = staging?.EndDate ?? start.AddMonths(5);

        bool doubleRound = competition.StructureType is CompetitionStructureType.League or CompetitionStructureType.LeagueWithPlayoffs;
        var pairings = _scheduler.GenerateRoundRobinPairings(season.ParticipatingTeamIds, doubleRound);
        var teams = world.Teams.ToDictionary(kv => kv.Key, kv => kv.Value);
        var fixtures = _scheduler.ScheduleRoundRobin(competition.Id, season.Id, pairings, start, end, competition.Format, teams);
        foreach (var f in fixtures) world.Fixtures.Add(f);
    }

    private static string Name(WorldState world, Guid teamId) =>
        world.Teams.TryGetValue(teamId, out var t) ? t.Name : "A team";
}
