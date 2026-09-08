using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 10: the national board's verdict on its head coach immediately after a global event or a
/// marquee bilateral series - the "how did we do at the thing that actually matters" moment, on
/// top of the ordinary season-by-season <see cref="CoachCareerService.EvaluateSeason"/> and the
/// multi-year <see cref="ValueObjects.CoachCareerRecord"/>.
///
/// A national board is judged by its public on World Cups, WTC finals and the Ashes - not on a
/// domestic table - and it is far more political about it (<see cref="ValueObjects.NationalBoard.Politicisation"/>).
/// Winning a major buys a coach real rope; an early exit from one, at a political board, costs him
/// hard and can tip the existing dismissal machinery over on the next monthly review.
///
/// This only moves <see cref="Coach.BoardTrust"/> and raises news - it never sacks anyone directly
/// (BoardRelationshipService owns that) and never touches an international fixture.
/// </summary>
public sealed class NationalBoardVerdictService
{
    /// <summary>
    /// Scans the events just produced this tick for a completed International competition and, for
    /// each, delivers the board verdict on every participating national side's coach.
    /// </summary>
    public IEnumerable<GameEvent> ReviewCompletedInternationals(WorldState world, DateOnly date, IReadOnlyList<GameEvent> tickEvents)
    {
        var results = new List<GameEvent>();

        var completedCompIds = tickEvents
            .Where(e => e.Type == GameEventType.SeasonCompleted && e.SubjectId is not null)
            .Select(e => e.SubjectId!.Value)
            .Distinct()
            .ToList();

        foreach (var compId in completedCompIds)
        {
            var competition = world.Competitions.FirstOrDefault(c => c.Id == compId);
            if (competition is null || competition.Scope != CompetitionScope.International) continue;

            var season = world.CompetitionSeasons
                .Where(s => s.CompetitionId == compId && s.IsCompleted)
                .OrderByDescending(s => s.Year)
                .FirstOrDefault();
            if (season is null) continue;

            bool major = competition.IsMajor;
            bool bilateral = season.ParticipatingTeamIds.Count == 2;

            // Rank the field by final standing for the "early exit / bottom half" read.
            var ordered = season.Standings
                .OrderByDescending(s => s.Points).ThenByDescending(s => s.NetRunRate)
                .Select((s, i) => (s.TeamId, Rank: i))
                .ToDictionary(x => x.TeamId, x => x.Rank);
            int field = Math.Max(1, ordered.Count);

            foreach (var teamId in season.ParticipatingTeamIds)
            {
                if (!world.Teams.TryGetValue(teamId, out var team) || !team.IsNational) continue;
                // S2: with a split coaching structure the board judges the coach responsible for THIS format only.
                var coach = team.CoachForFormat(competition.Format) is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
                if (coach is null) continue;

                double politics = team.NationalBoard?.Politicisation ?? 45;
                // Phase 12: a loud, volatile media market amplifies the verdict.
                double media = world.ProfileFor(team.Country).MediaPressureFactor;
                double scrutiny = (0.7 + politics / 100.0 * 0.9) * Math.Clamp(media, 0.8, 1.4);  // 0.56 .. 2.24
                double weight = major ? 1.0 : bilateral ? 0.55 : 0.35;

                bool champion = season.ChampionTeamId == teamId;
                int rank = ordered.TryGetValue(teamId, out var r) ? r : field - 1;
                double placing = 1.0 - rank / (double)Math.Max(1, field - 1); // 1 = won, 0 = last

                // §11.3: at a MAJOR the board judges against its own STRUCTURED ambition - a
                // powerhouse that only reaches the semis has failed its objective; a developing
                // side that does the same has met it. The bar scales with NationalBoard.Ambition.
                double expected = major ? (team.NationalBoard?.ExpectedMajorPlacing ?? 0.55) : 0.5;
                string target = major && team.NationalBoard is not null ? $" (the board wanted them to {team.NationalBoard.MajorTargetDescription})" : "";

                double delta;
                string verdict;
                if (champion)
                {
                    delta = 10 * weight;
                    verdict = major
                        ? $"{team.Name}'s board hails {coach.FullName} after the {competition.Name} triumph - his position is rock solid."
                        : $"{team.Name}'s board is delighted with {coach.FullName} after winning the {competition.Name}.";
                }
                else if (placing >= expected)
                {
                    delta = 3 * weight;
                    verdict = $"{team.Name}'s board is broadly satisfied with {coach.FullName}'s {competition.Name} campaign - objective met{target}.";
                }
                else if (placing >= expected - 0.25)
                {
                    delta = -2 * weight * scrutiny;
                    verdict = $"{team.Name}'s board wanted more from {coach.FullName} at the {competition.Name}{target}.";
                }
                else
                {
                    delta = -9 * weight * scrutiny;
                    verdict = major
                        ? $"{team.Name}'s board is scathing about {coach.FullName} after a chastening {competition.Name} - his job is on the line{target}."
                        : $"{team.Name}'s board is unhappy with {coach.FullName} after a poor {competition.Name}.";
                }

                // Trophy status - a bilateral trophy changing hands is its own line on top.
                if (bilateral)
                {
                    var rivalry = world.Rivalries.FirstOrDefault(rv => rv.Involves(teamId) && rv.TrophyName is not null
                        && season.ParticipatingTeamIds.All(id => rv.Involves(id)));
                    if (rivalry is not null && rivalry.LastContestedYear == season.Year)
                    {
                        if (rivalry.TrophyHolderId == teamId && !champion) { /* retained on a draw - neutral */ }
                        else if (rivalry.TrophyHolderId == teamId && champion) delta += 3;
                        else delta -= 3 * scrutiny;
                    }
                }

                coach.BoardTrust = Math.Clamp(coach.BoardTrust + delta, 0, 100);
                results.Add(new GameEvent(date, GameEventType.NationalBoardVerdict, verdict, coach.Id, team.Id));
            }

            // Phase 10 (§12.5): every completed International competition carries World-Cup
            // qualification stakes - a finishing-position award into the running tally that
            // determines seeding / direct entry to the next global event. The bigger the
            // competition, the more it is worth. RNG-free.
            double compWeight = major ? 6.0 : bilateral ? 2.0 : competition.Name.Contains("Championship") ? 4.0 : 1.5;
            foreach (var (teamId, rankIx) in ordered)
            {
                double positionShare = 1.0 - rankIx / (double)Math.Max(1, field - 1);
                double pts = compWeight * (0.3 + positionShare * 1.4);
                world.WorldCupQualificationPoints[teamId] =
                    world.WorldCupQualificationPoints.GetValueOrDefault(teamId) + Math.Round(pts, 2);
            }
        }

        return results;
    }
}
