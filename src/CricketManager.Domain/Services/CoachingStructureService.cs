using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Seven-suggestions pass (S2): a national board splitting - or reunifying - its HEAD-COACH job
/// across formats. The direct mirror of <see cref="CaptaincyAppointmentService"/>'s captaincy-pattern
/// work.
///
/// Real and cyclical (verified): England split after Chris Silverwood in Feb 2022 - Brendon
/// McCullum (Test) + Matthew Mott (white-ball) - then REUNIFIED under McCullum from January 2025
/// because "constant clashes between formats" made the split hard to run. So this models both
/// directions: a split relieves format-specific pressure and lets a specialist run his own show,
/// but it accrues real <see cref="Team.CoachingCoordinationFriction"/> (squad planning, workload,
/// a consistent message) that, sustained, tips the board back to one man.
///
/// Runs once a year for national teams. Its own date-seeded Random - it does not perturb the
/// shared annual stream.
/// </summary>
public sealed class CoachingStructureService
{
    private readonly CoachJobMarketService _coachMarket = new();
    private readonly CoachRecruitmentService _recruitment = new();

    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date)
    {
        var random = new Random(date.Year * 617 + 29);
        var events = new List<GameEvent>();

        foreach (var team in world.Teams.Values.Where(t => t.IsNational).OrderBy(t => t.Name).ToList())
        {
            if (team.CoachingStructure == CoachingStructure.Unified)
            {
                // --- friction eases while unified ---
                team.CoachingCoordinationFriction = Math.Max(0, team.CoachingCoordinationFriction - 15);

                var unified = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
                if (unified is null) continue;

                double politics = team.NationalBoard?.Politicisation ?? 45;
                double media = world.ProfileFor(team.Country).MediaPressureFactor;
                bool underPressure = unified.BoardTrust < 46 && (politics >= 52 || media >= 1.15);
                if (!underPressure) continue;

                // A genuine white-ball specialist to bring in for the shorter formats.
                var specialist = world.Coaches
                    .Where(c => !c.IsRetired && c.CurrentTeamId is null && c.FranchiseCoachingTeamId is null
                                && c.FormatFocus == FormatSpecialisation.WhiteBall && c.Reputation.Domestic >= 45)
                    .OrderByDescending(c => c.Reputation.Domestic + c.CircuitReputation * 0.5)
                    .ThenBy(c => c.LastName).ThenBy(c => c.FirstName)
                    .FirstOrDefault();

                double splitChance = 0.16 + (politics - 50) / 200.0 + (media - 1.0) * 0.25;
                if (random.NextDouble() >= Math.Clamp(splitChance, 0.05, 0.5)) continue;

                var whiteBallCoach = specialist ?? MakeSpecialist(world, date, random, FormatSpecialisation.WhiteBall);

                // Keep the incumbent on Test; the specialist takes ODI + T20.
                team.CoachingStructure = CoachingStructure.RedBallWhiteBall;
                team.FormatCoachIds[MatchFormat.Test] = unified.Id;
                team.FormatCoachIds[MatchFormat.ODI] = whiteBallCoach.Id;
                team.FormatCoachIds[MatchFormat.T20] = whiteBallCoach.Id;

                double salary = Math.Round(90_000 + team.Reputation.Domestic * 2_400, 0);
                var contract = _coachMarket.Hire(whiteBallCoach, team, date, salary, contractYears: 3, compensationIfTerminated: salary);
                world.CoachingContracts.Add(contract);
                // Hire() overwrites CurrentCoachId - restore the incumbent as the "primary" (Test) coach.
                team.CurrentCoachId = unified.Id;
                whiteBallCoach.CurrentTeamId = team.Id;

                team.CoachingCoordinationFriction = 20;
                events.Add(new GameEvent(date, GameEventType.CoachingStructureChanged,
                    $"{team.Name} split the head-coach job - {unified.FullName} stays on for the Test side, {whiteBallCoach.FullName} takes charge of the white-ball teams.",
                    team.Id));
            }
            else
            {
                // --- split: friction accrues, and a sustained level (or a clear performance gap) tips it back ---
                team.CoachingCoordinationFriction = Math.Min(100, team.CoachingCoordinationFriction + 8);

                var testCoach = world.Coaches.FirstOrDefault(c => c.Id == team.CoachForFormat(MatchFormat.Test));
                var whiteCoach = world.Coaches.FirstOrDefault(c => c.Id == team.CoachForFormat(MatchFormat.ODI));
                if (testCoach is null || whiteCoach is null)
                {
                    // A coach was lost some other way - collapse back to whichever remains.
                    Reunify(world, team, date, testCoach ?? whiteCoach, events);
                    continue;
                }

                double trustGap = Math.Abs(testCoach.BoardTrust - whiteCoach.BoardTrust);
                bool frictionHigh = team.CoachingCoordinationFriction >= 58;
                bool clearWinner = trustGap >= 26 && Math.Min(testCoach.BoardTrust, whiteCoach.BoardTrust) < 45;

                if (!frictionHigh && !clearWinner) continue;
                double reunifyChance = frictionHigh ? 0.34 : 0.22;
                if (random.NextDouble() >= reunifyChance) continue;

                var keep = testCoach.BoardTrust >= whiteCoach.BoardTrust ? testCoach : whiteCoach;
                Reunify(world, team, date, keep, events);
            }
        }

        return events;
    }

    private void Reunify(WorldState world, Team team, DateOnly date, Coach? keep, List<GameEvent> events)
    {
        var testCoach = world.Coaches.FirstOrDefault(c => c.Id == team.CoachForFormat(MatchFormat.Test));
        var whiteCoach = world.Coaches.FirstOrDefault(c => c.Id == team.CoachForFormat(MatchFormat.ODI));
        keep ??= testCoach ?? whiteCoach;

        foreach (var other in new[] { testCoach, whiteCoach })
        {
            if (other is null || other.Id == keep?.Id) continue;
            var deal = world.CoachingContracts.FirstOrDefault(k => k.CoachId == other.Id && k.TeamId == team.Id && k.Status == ContractStatus.Active);
            if (deal is not null)
            {
                deal.Status = ContractStatus.Terminated;
                deal.EndDate = date;
                team.Finances.Budget -= deal.CompensationIfTerminated;
            }
            other.CurrentTeamId = null;
            other.CurrentContractId = null;
        }

        team.CoachingStructure = CoachingStructure.Unified;
        team.FormatCoachIds.Clear();
        team.CoachingCoordinationFriction = 30;
        if (keep is not null)
        {
            team.CurrentCoachId = keep.Id;
            keep.CurrentTeamId = team.Id;
            events.Add(new GameEvent(date, GameEventType.CoachingStructureChanged,
                $"{team.Name} reunify the head-coach job under {keep.FullName} - the split had become a coordination headache.",
                team.Id));
        }
    }

    private Coach MakeSpecialist(WorldState world, DateOnly date, Random random, FormatSpecialisation focus)
    {
        var coach = _recruitment.GenerateShortlist(date, random, count: 1)[0];
        coach.FormatFocus = focus;
        world.Coaches.Add(coach);
        return coach;
    }
}
