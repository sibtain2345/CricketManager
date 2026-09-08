using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 6, Slice 6.5: the in-season board relationship - the half of a coach's job security that
/// moves DURING a campaign rather than only at the annual review CoachCareerService.EvaluateSeason
/// already owns.
///
/// Three things, on the monthly tick:
/// - **Team.BoardConfidence tracks the current season** - a club overperforming its
///   Strength/Reputation par gains board confidence month by month; one drifting below par loses
///   it. Tier-adjusted: an elite club expects to beat par, so par alone slowly erodes confidence.
/// - **Mid-season dismissal** - if confidence collapses, the board acts before the season ends
///   rather than waiting for the annual review. Probabilistic (never a hard cliff), scaled by the
///   scrutiny a marquee coach at a big club is under. Applies to a human coach too - being sacked
///   is a real career state.
/// - **Job offers to the human coach** - when a club's chair is vacant and the human's standing
///   fits, the board approaches him. An unemployed human takes the first suitable job (the
///   headless default); an employed one gets the offer surfaced as an event and decides himself.
/// </summary>
public sealed class BoardRelationshipService
{
    private readonly CoachCareerService _coachCareer = new();
    private readonly CoachJobMarketService _jobMarket = new();
    private readonly WorkloadRotationService _rotation = new();          // §17.7: fixture-congestion complaint

    private const double DismissalConfidenceFloor = 18;
    private const int MinGamesBeforeJudging = 2;

    public IEnumerable<GameEvent> ReviewMonthly(WorldState world, GameCalendar calendar, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        foreach (var coach in world.Coaches
                     .Where(c => c.CurrentTeamId is not null && c.CurrentContractId is not null && !c.IsRetired)
                     .OrderBy(c => world.Teams.TryGetValue(c.CurrentTeamId!.Value, out var t) ? t.Name : string.Empty)
                     .ToList())
        {
            var contract = world.CoachingContracts.FirstOrDefault(c => c.Id == coach.CurrentContractId && c.Status == ContractStatus.Active);
            if (contract is null || !world.Teams.TryGetValue(coach.CurrentTeamId!.Value, out var team)) continue;

            var standings = world.CompetitionSeasons
                .Where(s => s.Year == date.Year)
                .SelectMany(s => s.Standings)
                .Where(s => s.TeamId == team.Id)
                .ToList();

            double gamesPlayed = standings.Sum(s => s.Played);
            if (gamesPlayed < MinGamesBeforeJudging) continue;

            double score = _coachCareer.ScoreSeason(team, standings); // -1..1
            double tierExpectation = _coachCareer.AmbitionTier(team) switch
            {
                CoachCareerService.TeamAmbitionTier.Elite => 0.15,
                CoachCareerService.TeamAmbitionTier.Established => 0.0,
                _ => -0.10
            };
            double target = Math.Clamp(50 + (score - tierExpectation) * 45, 5, 95);

            // Phase 12: a volatile media market moves the confidence needle faster - a hero one
            // week, a villain the next - so the swing toward the target is sharper.
            double mediaRate = 0.25 * Math.Clamp(world.ProfileFor(team.Country).MediaPressureFactor, 0.8, 1.4);

            double before = team.BoardConfidence;
            team.BoardConfidence = Math.Clamp(team.BoardConfidence + (target - team.BoardConfidence) * mediaRate, 0, 100);
            // A slow echo into the annual measure, so EvaluateSeason sees the accumulated in-season mood.
            coach.BoardTrust = Math.Clamp(coach.BoardTrust + (team.BoardConfidence - coach.BoardTrust) * 0.06, 0, 100);

            if (Math.Abs(team.BoardConfidence - before) > 12)
                events.Add(new GameEvent(date, GameEventType.BoardConfidenceShift,
                    team.BoardConfidence > before
                        ? $"{team.Name}'s board publicly back {coach.FullName} after a strong run."
                        : $"{team.Name}'s board are losing patience with {coach.FullName}.",
                    coach.Id, team.Id));

            // §17.7: fixture congestion as an explicit board complaint. When the calendar is
            // genuinely packed AND several of the side's best players are currently sidelined
            // (a soft-tissue toll - the sign of a coach not managing the load), the board says so.
            if (!team.IsFranchise && _rotation.IsCalendarCongested(world, team, date))
            {
                var topInjured = world.Players
                    .Where(p => p.CurrentTeamId == team.Id && p.CurrentInjury is { } inj
                                && inj.IsActiveOn(date) && inj.Severity >= InjurySeverity.Minor
                                && p.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice)
                    .Count();
                if (topInjured >= 3 && random.NextDouble() < 0.5)
                {
                    team.BoardConfidence = Math.Clamp(team.BoardConfidence - 4, 0, 100);
                    events.Add(new GameEvent(date, GameEventType.BoardConfidenceShift,
                        $"{team.Name}'s board question {coach.FullName}'s workload management - the treatment room is full during a congested run of fixtures.",
                        coach.Id, team.Id));
                }
            }

            // Post-Phase-7/8/9 rectification (Section L): a FRANCHISE coach is judged at the end of
            // the campaign, not mid-season - a franchise almost never sacks its coach mid-tournament
            // (the window is only weeks long). The board-confidence number still tracks so
            // EvaluateSeason sees it; only the mid-season trigger is suppressed.

            if (team.BoardConfidence < DismissalConfidenceFloor && !team.IsFranchise)
            {
                double scrutiny = _coachCareer.ScrutinyFactor(coach, team);
                double chance = Math.Clamp((DismissalConfidenceFloor - team.BoardConfidence) / DismissalConfidenceFloor * 0.22 * scrutiny, 0, 0.5);
                if (random.NextDouble() < chance)
                {
                    contract.Status = ContractStatus.Terminated;
                    if (contract.CompensationIfTerminated > 0)
                        team.Finances.Budget -= contract.CompensationIfTerminated;
                    coach.CurrentTeamId = null;
                    coach.CurrentContractId = null;
                    team.CurrentCoachId = null;
                    coach.CareerSatisfaction = Math.Clamp(coach.CareerSatisfaction - 15, 0, 100);

                    events.Add(team.IsNational
                        ? new GameEvent(date, GameEventType.NationalCoachDismissed,
                            $"{team.Name}'s board part company with {coach.FullName} after a run of results the country will not accept.", coach.Id, team.Id)
                        : new GameEvent(date, GameEventType.CoachDismissed,
                            $"{team.Name} sack {coach.FullName} mid-season with the board's confidence gone.", coach.Id, team.Id));
                }
            }
        }

        events.AddRange(OfferJobsToHumanCoach(world, date, random));
        return events;
    }

    private IEnumerable<GameEvent> OfferJobsToHumanCoach(WorldState world, DateOnly date, Random random)
    {
        var human = world.Coaches.FirstOrDefault(c => c.IsHumanControlled && !c.IsRetired);
        if (human is null) yield break;

        double currentStanding = human.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var currentTeam)
            ? Standing(currentTeam)
            : -1;

        foreach (var team in world.Teams.Values
                     .Where(t => t.CurrentCoachId is null && t.InterimCoachStaffId is null && !t.IsFranchise && !t.IsNational)
                     .OrderBy(t => t.Name))
        {
            if (human.CurrentTeamId == team.Id) continue;

            double teamStanding = Standing(team);
            bool goodEnough = teamStanding <= human.Reputation.Domestic + 22;
            bool worthMoving = human.CurrentTeamId is null || teamStanding > currentStanding + 8;
            if (!goodEnough || !worthMoving) continue;

            yield return new GameEvent(date, GameEventType.CoachJobOffer,
                $"{team.Name} approach {human.FullName} about their vacant head-coach job.", human.Id, team.Id);

            // Headless default: an unemployed human takes the first job that fits. An employed one
            // is left to decide - the offer event is enough for a UI to act on.
            if (human.CurrentTeamId is null)
            {
                double salary = Math.Round(80_000 + team.Strength * 4_000 + team.Reputation.Domestic * 2_500, 0);
                var contract = _jobMarket.Hire(human, team, date, salary, contractYears: 3, compensationIfTerminated: salary);
                world.CoachingContracts.Add(contract);
                yield return new GameEvent(date, GameEventType.CoachAppointed,
                    $"{human.FullName} takes charge at {team.Name}.", human.Id, team.Id);
                yield break;
            }
        }
    }

    private static double Standing(Team team) => (team.Strength + team.Reputation.Domestic * 2) / 3;
}
