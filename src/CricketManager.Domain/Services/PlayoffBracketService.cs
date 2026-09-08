using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>
/// Generates and progresses the knockout stage - the part of a season a table alone can't
/// answer. CompetitionProgressionService already decides WHO qualifies (top N by points/NRR);
/// this decides what they then play, and updates the bracket as each result comes in.
///
/// Two shapes are supported deliberately, not just one: a straight single-elimination
/// bracket (works for any qualifier count via byes at the top seeds), and the IPL-style
/// top-4 format most major T20 leagues actually use. They're genuinely different games, not
/// a cosmetic variant of each other - a straight knockout eliminates the loser of Qualifier
/// 1 immediately; the IPL-style format gives that team, having finished 1st or 2nd over a
/// whole season, a second chance via Qualifier 2 rather than ending its season on one bad
/// day against the team it beat into 2nd/1st place. Competition.PlayoffFormat is what
/// chooses between them.
/// </summary>
public sealed class PlayoffBracketService
{
    /// <summary>
    /// Standard tournament bracket seeding (seed 1 vs the bottom seed, seed 2 vs the
    /// second-bottom, ...) recursively built so seed 1 and seed 2 can only meet in the
    /// final, seeds 1-4 can only meet from the semi-finals on, and so on - the same
    /// property real single-elimination brackets are seeded to guarantee. Returns a list
    /// of length `size` (a power of two) of 1-based seed numbers in bracket-slot order;
    /// slots beyond the real qualifier count are byes.
    /// </summary>
    private static List<int> BuildSeedOrder(int size)
    {
        if (size == 1) return new List<int> { 1 };

        var previous = BuildSeedOrder(size / 2);
        var result = new List<int>();
        foreach (var seed in previous)
        {
            result.Add(seed);
            result.Add(size + 1 - seed);
        }
        return result;
    }

    private static int NextPowerOfTwo(int n)
    {
        int size = 1;
        while (size < n) size *= 2;
        return size;
    }

    private static string RoundName(int bracketSize, int roundIndex)
    {
        int teamsLeftAtStartOfRound = bracketSize >> roundIndex;
        return teamsLeftAtStartOfRound switch
        {
            2 => "Final",
            4 => "Semi-Final",
            8 => "Quarter-Final",
            _ => $"Round of {teamsLeftAtStartOfRound}"
        };
    }

    /// <summary>
    /// A straight single-elimination bracket from seeded qualifiers (index 0 = top seed).
    /// Non-power-of-two qualifier counts are padded with byes at the bottom seeds, seeded
    /// using the standard bracket order so byes fall on the weakest seeds, not randomly -
    /// exactly how a real tournament committee would place them. A bye fixture is completed
    /// immediately (no match played) so bracket progression never needs a special case for it.
    /// </summary>
    public List<Fixture> GenerateStraightKnockout(
        Guid competitionId, Guid seasonId, IReadOnlyList<Guid> seededTeamIds,
        DateOnly startDate, int daysBetweenRounds, IReadOnlyDictionary<Guid, Team> teams)
    {
        if (seededTeamIds.Count < 2) return new List<Fixture>();

        int bracketSize = NextPowerOfTwo(seededTeamIds.Count);
        var seedOrder = BuildSeedOrder(bracketSize);

        Guid? TeamForSeed(int seed) => seed <= seededTeamIds.Count ? seededTeamIds[seed - 1] : null;

        var allFixtures = new List<Fixture>();
        var currentRoundFixtures = new List<Fixture>();

        // Round 1: pair adjacent entries in seed order.
        for (int i = 0; i < seedOrder.Count; i += 2)
        {
            var homeTeam = TeamForSeed(seedOrder[i]);
            var awayTeam = TeamForSeed(seedOrder[i + 1]);

            var fixture = new Fixture
            {
                CompetitionId = competitionId,
                SeasonId = seasonId,
                Stage = RoundName(bracketSize, 0),
                HomeTeamId = homeTeam,
                AwayTeamId = awayTeam,
                ScheduledDate = startDate,
                GroundId = homeTeam is not null && teams.TryGetValue(homeTeam.Value, out var ht) ? ht.HomeGroundId : null
            };

            // A bye: exactly one side is a real team. Resolve it immediately so the next
            // round can be built without waiting on a match that was never going to happen.
            if (homeTeam is not null && awayTeam is null) fixture.CompleteAsBye(homeTeam.Value);
            else if (homeTeam is null && awayTeam is not null) fixture.CompleteAsBye(awayTeam.Value);

            allFixtures.Add(fixture);
            currentRoundFixtures.Add(fixture);
        }

        int roundIndex = 1;
        var date = startDate;
        while (currentRoundFixtures.Count > 1)
        {
            date = date.AddDays(daysBetweenRounds);
            var nextRoundFixtures = new List<Fixture>();

            for (int i = 0; i < currentRoundFixtures.Count; i += 2)
            {
                var homeFeeder = currentRoundFixtures[i];
                var awayFeeder = currentRoundFixtures[i + 1];

                var fixture = new Fixture
                {
                    CompetitionId = competitionId,
                    SeasonId = seasonId,
                    Stage = RoundName(bracketSize, roundIndex),
                    ScheduledDate = date,
                    HomeFeederFixtureId = homeFeeder.Id,
                    AwayFeederFixtureId = awayFeeder.Id,
                    HomeTeamPlaceholder = $"Winner of {homeFeeder.Stage}",
                    AwayTeamPlaceholder = $"Winner of {awayFeeder.Stage}"
                };

                // A feeder that was already decided by a bye can be filled in immediately.
                ResolveIfFeederAlreadyComplete(fixture, homeFeeder, awayFeeder, teams);

                allFixtures.Add(fixture);
                nextRoundFixtures.Add(fixture);
            }

            currentRoundFixtures = nextRoundFixtures;
            roundIndex++;
        }

        AssignSequenceNumbers(allFixtures);
        return allFixtures;
    }

    /// <summary>Slice 6.1: stable, generation-order sequence numbers, offset past a round-robin's range so a LeagueWithPlayoffs season's fixtures sort in play order overall.</summary>
    private static void AssignSequenceNumbers(IReadOnlyList<Fixture> fixtures)
    {
        for (int k = 0; k < fixtures.Count; k++)
            fixtures[k].SequenceNumber = 1000 + k;
    }

    /// <summary>
    /// The IPL-style top-4 playoff: Qualifier 1 (seed 1 v seed 2), Eliminator (seed 3 v seed
    /// 4), Qualifier 2 (loser of Qualifier 1 v winner of Eliminator), Final (winner of
    /// Qualifier 1 v winner of Qualifier 2). Requires exactly 4 qualifiers - this format
    /// doesn't generalise to other bracket sizes the way a straight knockout does, which is
    /// exactly why it's a separate method rather than a parameter on GenerateStraightKnockout.
    /// </summary>
    public List<Fixture> GenerateIplStylePlayoff(
        Guid competitionId, Guid seasonId, IReadOnlyList<Guid> top4SeededTeamIds,
        DateOnly startDate, int daysBetweenRounds, IReadOnlyDictionary<Guid, Team> teams)
    {
        if (top4SeededTeamIds.Count != 4)
            throw new ArgumentException("IPL-style playoff needs exactly 4 qualified teams.", nameof(top4SeededTeamIds));

        Guid? GroundFor(Guid teamId) => teams.TryGetValue(teamId, out var t) ? t.HomeGroundId : null;

        var qualifier1 = new Fixture
        {
            CompetitionId = competitionId, SeasonId = seasonId, Stage = "Qualifier 1",
            HomeTeamId = top4SeededTeamIds[0], AwayTeamId = top4SeededTeamIds[1],
            ScheduledDate = startDate, GroundId = GroundFor(top4SeededTeamIds[0])
        };

        var eliminator = new Fixture
        {
            CompetitionId = competitionId, SeasonId = seasonId, Stage = "Eliminator",
            HomeTeamId = top4SeededTeamIds[2], AwayTeamId = top4SeededTeamIds[3],
            ScheduledDate = startDate, GroundId = GroundFor(top4SeededTeamIds[2])
        };

        var qualifier2Date = startDate.AddDays(daysBetweenRounds);
        var qualifier2 = new Fixture
        {
            CompetitionId = competitionId, SeasonId = seasonId, Stage = "Qualifier 2",
            ScheduledDate = qualifier2Date,
            HomeFeederFixtureId = qualifier1.Id, HomeFeederIsLoserSlot = true,
            HomeTeamPlaceholder = "Loser of Qualifier 1",
            AwayFeederFixtureId = eliminator.Id, AwayFeederIsLoserSlot = false,
            AwayTeamPlaceholder = "Winner of Eliminator"
        };

        var finalDate = qualifier2Date.AddDays(daysBetweenRounds);
        var final = new Fixture
        {
            CompetitionId = competitionId, SeasonId = seasonId, Stage = "Final",
            ScheduledDate = finalDate,
            HomeFeederFixtureId = qualifier1.Id, HomeFeederIsLoserSlot = false,
            HomeTeamPlaceholder = "Winner of Qualifier 1",
            AwayFeederFixtureId = qualifier2.Id, AwayFeederIsLoserSlot = false,
            AwayTeamPlaceholder = "Winner of Qualifier 2"
        };

        var ipl = new List<Fixture> { qualifier1, eliminator, qualifier2, final };
        AssignSequenceNumbers(ipl);
        return ipl;
    }

    /// <summary>
    /// Records a real result and advances the bracket: any fixture in the bracket that feeds
    /// from this one has its placeholder resolved to a concrete team. This is the only place
    /// a "TBD" slot ever gets filled in - generation only ever creates the placeholder.
    /// </summary>
    public void RecordFixtureResult(Fixture fixture, Guid winningTeamId, Guid matchId, IReadOnlyList<Fixture> bracket)
    {
        fixture.Complete(matchId, winningTeamId);
        PropagateResult(fixture, bracket);
    }

    private static void PropagateResult(Fixture completed, IReadOnlyList<Fixture> bracket)
    {
        foreach (var dependent in bracket)
        {
            if (dependent.HomeFeederFixtureId == completed.Id)
            {
                dependent.HomeTeamId = dependent.HomeFeederIsLoserSlot ? completed.LosingTeamId : completed.WinningTeamId;
                if (dependent.HomeTeamId is not null) dependent.HomeTeamPlaceholder = null;
            }
            if (dependent.AwayFeederFixtureId == completed.Id)
            {
                dependent.AwayTeamId = dependent.AwayFeederIsLoserSlot ? completed.LosingTeamId : completed.WinningTeamId;
                if (dependent.AwayTeamId is not null) dependent.AwayTeamPlaceholder = null;
            }
        }
    }

    private static void ResolveIfFeederAlreadyComplete(Fixture fixture, Fixture homeFeeder, Fixture awayFeeder, IReadOnlyDictionary<Guid, Team> teams)
    {
        if (homeFeeder.WinningTeamId is { } homeWinner)
        {
            fixture.HomeTeamId = homeWinner;
            fixture.HomeTeamPlaceholder = null;
            fixture.GroundId ??= teams.TryGetValue(homeWinner, out var ht) ? ht.HomeGroundId : null;
        }
        if (awayFeeder.WinningTeamId is { } awayWinner)
        {
            fixture.AwayTeamId = awayWinner;
            fixture.AwayTeamPlaceholder = null;
        }
    }
}
