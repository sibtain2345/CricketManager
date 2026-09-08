using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// The scheduler CompetitionWindow's own doc comment has been pointing at since Phase 3:
/// fills a window with actual fixtures. Deliberately the "realistic generated patterns"
/// half of the decided direction in CLAUDE.md, not the "real ICC Future Tours Programme
/// data" half - this project's seeded world (WorldSeeder) produces fictional domestic
/// teams within one generated country, not real international boards with a real tour
/// calendar to import, so there is no real FTP data this slice could honestly plug in yet.
/// That half stays exactly where CLAUDE.md already placed it: a future external-data-layer
/// import, once real team/board data exists to hang it on. What this service builds is the
/// generic algorithm every competition - real or seeded - needs regardless: a round-robin
/// schedule, balanced home and away, spaced across the time actually available.
/// </summary>
public sealed class FixtureGenerationService
{
    /// <summary>
    /// Standard "circle method" round-robin: fix one team, rotate the rest through the
    /// remaining seats each round. An odd team count gets a phantom bye seat that rotates
    /// through exactly like a real team, so the SAME team is never left out twice before
    /// everyone else has sat out once.
    ///
    /// Home/away is assigned GREEDILY - whichever of the two teams is currently more
    /// away-heavy gets this match at home - not by a fixed (round, seat) parity rule. A
    /// parity rule looks balanced on paper but isn't: traced by hand for a 4-team round
    /// robin, the naive "alternate by round and seat index" rule stranded one specific team
    /// at 0 home / 3 away for the whole single leg, because that team happened to rotate
    /// through seats whose parity always resolved to "away" that season - a real bug caught
    /// before it shipped, not a hypothetical one. The greedy version keeps every team within
    /// one match of an even split, which is the best any round-robin with an odd number of
    /// rounds can do (n-1 rounds is odd whenever n is even, so someone is always 1 up).
    /// </summary>
    public List<(Guid Home, Guid Away, int Round)> GenerateRoundRobinPairings(
        IReadOnlyList<Guid> teamIds, bool doubleRoundRobin)
    {
        if (teamIds.Count < 2)
            return new List<(Guid, Guid, int)>();

        var seats = new List<Guid?>(teamIds.Select(id => (Guid?)id));
        bool hasBye = seats.Count % 2 != 0;
        if (hasBye) seats.Add(null);

        int n = seats.Count;
        int roundsInSingleLeg = n - 1;
        int half = n / 2;

        var rawPairs = new List<(Guid TeamA, Guid TeamB, int Round)>();

        for (int round = 0; round < roundsInSingleLeg; round++)
        {
            for (int i = 0; i < half; i++)
            {
                var a = seats[i];
                var b = seats[n - 1 - i];
                if (a is null || b is null) continue; // one of them is the bye this round

                rawPairs.Add((a.Value, b.Value, round + 1));
            }

            // Rotate every seat but the first one clockwise.
            var fixedSeat = seats[0];
            var rotating = seats.Skip(1).ToList();
            rotating.Insert(0, rotating[^1]);
            rotating.RemoveAt(rotating.Count - 1);
            seats = new List<Guid?> { fixedSeat }.Concat(rotating).ToList();
        }

        var homeCount = new Dictionary<Guid, int>();
        var awayCount = new Dictionary<Guid, int>();
        int Balance(Guid teamId)
        {
            homeCount.TryAdd(teamId, 0);
            awayCount.TryAdd(teamId, 0);
            return homeCount[teamId] - awayCount[teamId];
        }

        var pairings = new List<(Guid Home, Guid Away, int Round)>();
        foreach (var (teamA, teamB, round) in rawPairs)
        {
            int balanceA = Balance(teamA);
            int balanceB = Balance(teamB);

            // The more away-heavy team gets home; an exact tie is broken by round parity so
            // the choice is deterministic (same input always produces the same schedule).
            bool aIsHome = balanceA < balanceB || (balanceA == balanceB && round % 2 == 0);
            var (home, away) = aIsHome ? (teamA, teamB) : (teamB, teamA);

            homeCount[home]++;
            awayCount[away]++;
            pairings.Add((home, away, round));
        }

        if (!doubleRoundRobin) return pairings;

        // Second leg: same pairings, home and away mirrored, appended as later rounds.
        var secondLeg = pairings
            .Select(p => (Home: p.Away, Away: p.Home, Round: p.Round + roundsInSingleLeg))
            .ToList();

        pairings.AddRange(secondLeg);
        return pairings;
    }

    /// <summary>
    /// Splits seeded teams into groups using a snake draft (1,2,3,4 | 4,3,2,1 | ...) rather
    /// than a plain contiguous split, so a group stage doesn't accidentally load every strong
    /// side into the same group - the same seeding convention real World Cup groups use.
    /// Caller passes teamIds already ordered strongest-first.
    /// </summary>
    public List<List<Guid>> SplitIntoGroups(IReadOnlyList<Guid> seededTeamIds, int numberOfGroups)
    {
        var groups = new List<List<Guid>>();
        for (int g = 0; g < numberOfGroups; g++) groups.Add(new List<Guid>());

        int direction = 1;
        int groupIndex = 0;
        foreach (var teamId in seededTeamIds)
        {
            groups[groupIndex].Add(teamId);
            groupIndex += direction;
            if (groupIndex == numberOfGroups) { groupIndex = numberOfGroups - 1; direction = -1; }
            else if (groupIndex < 0) { groupIndex = 0; direction = 1; }
        }

        return groups;
    }

    /// <summary>
    /// Builds a knockout seed order from several groups' own qualifiers - the missing link
    /// between CompetitionProgressionService.GetGroupQualifiers (ranks ONE group) and
    /// PlayoffBracketService.GenerateStraightKnockout (needs ONE combined seeded list).
    ///
    /// Interleaves by rank-within-group rather than concatenating group by group (every
    /// group's winner, then every group's runner-up, ...) specifically so the standard
    /// bracket seeding in GenerateStraightKnockout keeps first-round opponents from
    /// different groups. Traced through by hand for the classic 2-group, top-2-each shape
    /// before trusting it: seeding [A1, B1, A2, B2] pairs seed1-vs-seed4 = A1 vs B2 and
    /// seed2-vs-seed3 = B1 vs A2 - group winners face the OTHER group's runner-up, never
    /// their own, which is exactly the "typical World Cup shape" the structure type is
    /// named for. With more than two groups the same interleaving still guarantees no
    /// side meets its OWN group in round one; it does not guarantee every possible pairing
    /// avoids a repeat of a group-stage opponent further into the bracket - a real, stated
    /// limitation rather than a claim this generalises perfectly for every group count.
    /// </summary>
    public List<Guid> BuildGroupCrossoverSeeding(IReadOnlyList<IReadOnlyList<Guid>> rankedGroups, int qualifiersPerGroup)
    {
        var seeding = new List<Guid>();
        for (int rank = 0; rank < qualifiersPerGroup; rank++)
        {
            foreach (var group in rankedGroups)
            {
                if (rank < group.Count) seeding.Add(group[rank]);
            }
        }
        return seeding;
    }

    /// <summary>Minimum realistic days of rest between a side's matches, by format - a Test/first-class side cannot be scheduled back into another match the way a T20 side can.</summary>
    public static int MinimumRestDays(MatchFormat format) => format switch
    {
        MatchFormat.T20 => 2,
        MatchFormat.ODI => 3,
        MatchFormat.Test => 5,
        _ => 3
    };

    /// <summary>Realistic day-to-day spacing when the calendar has room to spread out - what a schedule looks like when it isn't under time pressure.</summary>
    private static int ComfortableRestDays(MatchFormat format) => format switch
    {
        MatchFormat.T20 => 3,
        MatchFormat.ODI => 4,
        MatchFormat.Test => 7,
        _ => 4
    };

    /// <summary>
    /// Turns round-robin pairings into dated, grounded Fixtures. Each round is treated as one
    /// "matchday" - every fixture in it gets the same date, spaced from the next round by
    /// however many days the format and the available window can support. Compresses toward
    /// the format's MINIMUM rest days if the window is tight, and honestly lets the schedule
    /// run past the nominal window end if even minimum spacing doesn't fit - a silently
    /// truncated season (dropped fixtures) would be a worse failure than a schedule that
    /// overruns and says so.
    /// </summary>
    public List<Fixture> ScheduleRoundRobin(
        Guid competitionId, Guid seasonId,
        List<(Guid Home, Guid Away, int Round)> pairings,
        DateOnly windowStart, DateOnly windowEnd, MatchFormat format,
        IReadOnlyDictionary<Guid, Team> teams,
        string stagePrefix = "Round")
    {
        if (pairings.Count == 0) return new List<Fixture>();

        int totalRounds = pairings.Max(p => p.Round);
        int comfortable = ComfortableRestDays(format);
        int minimum = MinimumRestDays(format);

        int availableDays = Math.Max(0, windowEnd.DayNumber - windowStart.DayNumber);
        int spacing = totalRounds <= 1 ? comfortable : Math.Max(minimum, availableDays / totalRounds);

        var fixtures = new List<Fixture>();
        int seq = 0;
        foreach (var (home, away, round) in pairings)
        {
            var date = windowStart.AddDays((round - 1) * spacing);
            teams.TryGetValue(home, out var homeTeam);

            fixtures.Add(new Fixture
            {
                CompetitionId = competitionId,
                SeasonId = seasonId,
                Stage = $"{stagePrefix} {round}",
                RoundNumber = round,
                SequenceNumber = seq++,
                HomeTeamId = home,
                AwayTeamId = away,
                ScheduledDate = date,
                GroundId = homeTeam?.HomeGroundId
            });
        }

        return fixtures;
    }
}
