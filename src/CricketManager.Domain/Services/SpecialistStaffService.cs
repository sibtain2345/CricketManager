using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-6 (section C): what the backroom roles actually DO in game, beyond a title.
///
/// - **Batting / bowling / fielding / mental coaches** can be assigned to a specific player for
///   extra individual work (<see cref="AssignIndividualWork"/>), which adds a weekly development
///   channel on top of the normal monthly training and shows up in the coach's own quarterly
///   effectiveness signal (StaffCareerService.GrowFromDevelopmentSignal already reads
///   RecentDevelopmentByPlayer).
/// - **Chief Scout / Scout** produce recommendations - a real named player who fills a gap, with
///   reasoning - for a club signing, or for a national-pool addition.
/// - **Head Physio / Physio** raise the team's effective medical quality
///   (<see cref="MedicalQualityBoost"/>), which the injury system reads.
/// - **Mentor** amplifies the player-to-player mentoring MentoringService models
///   (<see cref="MentoringStrengthBoost"/>).
/// - **Data Analyst / Sports Scientist** feed the existing analysis/conditioning pathways -
///   a hired one lifts <see cref="AnalysisQualityBoost"/> / conditioning, no parallel system.
/// </summary>
public sealed class SpecialistStaffService
{
    private static readonly StaffRole[] IndividualWorkRoles =
        { StaffRole.BattingCoach, StaffRole.BowlingCoach, StaffRole.FieldingCoach, StaffRole.MentalPerformanceCoach };

    // ---------------- individual coaching ----------------

    /// <summary>
    /// Assigns each specialist coach at a team to up to two players who most need individual work:
    /// young players with real headroom, and established players in a genuine dip. A player already
    /// getting individual attention keeps his coach. Idempotent - safe to call every month.
    /// </summary>
    public void AssignIndividualWork(Team team, IReadOnlyList<Player> squad, IReadOnlyList<StaffMember> teamStaff)
    {
        foreach (var role in IndividualWorkRoles)
        {
            var coach = teamStaff.FirstOrDefault(s => s.Role == role);
            if (coach is null) continue;

            var eligible = squad
                .Where(p => !p.IsRetired && p.CurrentTeamId == team.Id && RoleMatches(role, p))
                .Where(p => (p.PotentialAbility - p.CurrentAbility >= 15) || p.Form.CurrentForm < -20)
                .OrderByDescending(p => (p.PotentialAbility - p.CurrentAbility) + Math.Max(0, -p.Form.CurrentForm))
                .ToList();

            int assigned = squad.Count(p => p.AssignedSpecialistCoachId == coach.Id);
            foreach (var p in eligible)
            {
                if (assigned >= 2) break;
                if (p.AssignedSpecialistCoachId is null) { p.AssignedSpecialistCoachId = coach.Id; assigned++; }
            }
        }

        // Drop assignments whose coach has left.
        var validIds = teamStaff.Select(s => s.Id).ToHashSet();
        foreach (var p in squad.Where(p => p.AssignedSpecialistCoachId is { } id && !validIds.Contains(id)))
            p.AssignedSpecialistCoachId = null;
    }

    /// <summary>
    /// One week of individual work for a player who has an assigned coach. A small, headroom-gated
    /// development roll in the coach's own cluster, scaled by the coach's effectiveness. Feeds
    /// RecentDevelopmentByPlayer so the coach's quarterly signal reflects it.
    /// </summary>
    public bool ApplyIndividualWorkWeek(Player player, StaffMember coach, Random random, WorldState? world = null)
    {
        if (player.IsRetired || player.AssignedSpecialistCoachId != coach.Id) return false;
        double headroom = Math.Clamp((player.PotentialAbility - player.CurrentAbility) / 40.0, 0, 1);
        if (headroom <= 0 && player.Form.CurrentForm >= -10) return false;

        double youth = player.Age(DateOnly.FromDayNumber(0)) <= 24 ? 1.0 : 0.55; // rough - callers pass real age via the cluster targeting; this is the weekly roll
        double chance = 0.05 * (coach.EffectiveWithExperience / 100.0) * (0.3 + headroom) * youth;
        if (random.NextDouble() >= chance) return false;

        NudgeCluster(coach.Role, player, random);
        player.CurrentAbility = Math.Clamp(player.CurrentAbility + 1, 1, player.PotentialAbility);
        player.RecalculateFormatSuitability();
        if (world is not null)
            world.RecentDevelopmentByPlayer[player.Id] =
                world.RecentDevelopmentByPlayer.TryGetValue(player.Id, out var acc) ? acc + 0.6 : 0.6;
        return true;
    }

    private static bool RoleMatches(StaffRole role, Player p) => role switch
    {
        StaffRole.BattingCoach => p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder or PlayerRole.WicketKeeper,
        StaffRole.BowlingCoach => p.BowlingRole != BowlingRoleType.NotABowler,
        _ => true
    };

    private static void NudgeCluster(StaffRole role, Player p, Random random)
    {
        int C(int v) => Math.Clamp(v + 1, 1, 20);
        switch (role)
        {
            case StaffRole.BattingCoach:
                if (random.Next(2) == 0) p.Batting.Technique = C(p.Batting.Technique);
                else p.Batting.ShotSelection = C(p.Batting.ShotSelection);
                break;
            case StaffRole.BowlingCoach:
                if (random.Next(2) == 0) p.Bowling.Accuracy = C(p.Bowling.Accuracy);
                else p.Bowling.Variation = C(p.Bowling.Variation);
                break;
            case StaffRole.FieldingCoach:
                if (random.Next(2) == 0) p.Fielding.Catching = C(p.Fielding.Catching);
                else p.Fielding.GroundFielding = C(p.Fielding.GroundFielding);
                break;
            case StaffRole.MentalPerformanceCoach:
                if (random.Next(2) == 0) p.Mental.Composure = C(p.Mental.Composure);
                else p.Mental.PressureHandling = C(p.Mental.PressureHandling);
                break;
        }
    }

    // ---------------- effect helpers other systems read ----------------

    /// <summary>How much a hired physio / head physio lifts the team's effective medical quality, 0..~15.</summary>
    public double MedicalQualityBoost(IReadOnlyList<StaffMember> teamStaff)
    {
        var lead = teamStaff.FirstOrDefault(s => s.Role == StaffRole.HeadPhysiotherapist);
        var physio = teamStaff.FirstOrDefault(s => s.Role == StaffRole.Physiotherapist);
        double v = 0;
        if (lead is not null) v += lead.EffectiveWithExperience / 100.0 * 10;
        if (physio is not null) v += physio.EffectiveWithExperience / 100.0 * 6;
        var sciSc = teamStaff.FirstOrDefault(s => s.Role == StaffRole.SportsScientist);
        if (sciSc is not null) v += sciSc.EffectiveWithExperience / 100.0 * 4;
        return Math.Clamp(v, 0, 18);
    }

    /// <summary>How much a hired analyst / data analyst lifts the team's analysis quality, 0..~14.</summary>
    public double AnalysisQualityBoost(IReadOnlyList<StaffMember> teamStaff)
    {
        double v = 0;
        foreach (var s in teamStaff.Where(s => s.Role is StaffRole.Analyst or StaffRole.DataAnalyst))
            v += s.EffectiveWithExperience / 100.0 * 8;
        return Math.Clamp(v, 0, 14);
    }

    /// <summary>
    /// A hired club Mentor amplifies player-to-player mentoring: it raises pairing strength and,
    /// crucially, lets a promising junior with NO suitable senior team-mate still be developed
    /// (the mentor fills that gap). Returns a strength bonus 0..~18.
    /// </summary>
    public double MentoringStrengthBoost(IReadOnlyList<StaffMember> teamStaff)
    {
        var mentor = teamStaff.FirstOrDefault(s => s.Role == StaffRole.Mentor);
        return mentor is null ? 0 : Math.Clamp(mentor.EffectiveWithExperience / 100.0 * 18, 0, 18);
    }

    // ---------------- scouting output ----------------

    /// <summary>
    /// A club's scouting department recommends a signing: the best player at another club who
    /// fills a genuine gap in this squad, with reasoning. Null when the squad has no obvious gap
    /// or nobody suitable is out there.
    /// </summary>
    public (Guid PlayerId, string Reasoning)? RecommendSigning(
        Team team, IReadOnlyList<Player> ownSquad, IEnumerable<Player> otherClubsPlayers,
        IReadOnlyList<StaffMember> teamStaff, MatchFormat format)
    {
        var scout = teamStaff.FirstOrDefault(s => s.Role is StaffRole.ChiefScout or StaffRole.Scout);
        if (scout is null) return null;

        // The squad's thinnest role group.
        var gap = ThinnestGroup(ownSquad);
        if (gap is null) return null;

        var evaluator = new PlayerSelectionEvaluator();
        var target = otherClubsPlayers
            .Where(p => !p.IsRetired && gap.Value.Matches(p))
            .OrderByDescending(p => Math.Max(
                evaluator.Evaluate(p, format).TotalScore,
                p.BowlingRole != BowlingRoleType.NotABowler ? evaluator.Evaluate(p, format, forBowling: true).TotalScore : 0)
                + AbilityScale.CompositeAbilityToHundred(p.PotentialAbility) * (scout.Role == StaffRole.ChiefScout ? 0.15 : 0.08))
            .FirstOrDefault();

        return target is null ? null
            : (target.Id, $"{scout.FullName} recommends {target.FullName} - {team.Name} are light on {gap.Value.Label}.");
    }

    /// <summary>A national scout's recommendation: a player who deserves a look in the pool, by form and conditions fit, with reasoning.</summary>
    public (Guid PlayerId, string Reasoning)? RecommendPoolAddition(
        Team nationalTeam, IEnumerable<Player> countrymen, IReadOnlyList<StaffMember> teamStaff, MatchFormat format)
    {
        var scout = teamStaff.FirstOrDefault(s => s.Role is StaffRole.ChiefScout or StaffRole.Scout);
        if (scout is null) return null;

        var contender = countrymen
            .Where(p => !p.IsRetired && !p.RetiredFormats.Contains(format)
                        && string.Equals(p.Nationality, nationalTeam.Country, StringComparison.OrdinalIgnoreCase)
                        && p.Form.CurrentForm > 30)
            .OrderByDescending(p => p.Form.CurrentForm + AbilityScale.CompositeAbilityToHundred(p.CurrentAbility))
            .FirstOrDefault();

        return contender is null ? null
            : (contender.Id, $"{scout.FullName} flags {contender.FullName} for the {format} pool - {(int)contender.Form.CurrentForm} form and the game to back it.");
    }

    private readonly record struct RoleGroup(string Label, Func<Player, bool> Matches);

    private static RoleGroup? ThinnestGroup(IReadOnlyList<Player> squad)
    {
        var groups = new[]
        {
            new RoleGroup("front-line pace", p => p.PrimaryRole == PlayerRole.Bowler && p.BowlingStyle is
                BowlingStyle.RightArmFast or BowlingStyle.RightArmFastMedium or BowlingStyle.LeftArmFast or BowlingStyle.LeftArmFastMedium),
            new RoleGroup("front-line spin", p => p.PrimaryRole == PlayerRole.Bowler && p.BowlingStyle is
                BowlingStyle.RightArmOffSpin or BowlingStyle.RightArmLegSpin or BowlingStyle.LeftArmOrthodox or BowlingStyle.LeftArmChinaman),
            new RoleGroup("top-order batting", p => p.BattingRole is BattingRole.Opener or BattingRole.TopOrder
                && p.PrimaryRole is PlayerRole.Batsman or PlayerRole.BattingAllrounder),
            new RoleGroup("all-round balance", p => p.PrimaryRole is PlayerRole.BattingAllrounder or PlayerRole.BowlingAllrounder),
            new RoleGroup("wicketkeeping cover", p => p.PrimaryRole == PlayerRole.WicketKeeper),
        };
        var thinnest = groups.OrderBy(g => squad.Count(g.Matches)).First();
        int have = squad.Count(thinnest.Matches);
        int need = thinnest.Label == "wicketkeeping cover" ? 2 : 3;
        return have < need ? thinnest : null;
    }
}
