using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.Services;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Tests;

/// <summary>Builders for match-engine tests: repeatable sides at a chosen quality level, so a test can state "good batting versus weak bowling" and assert on what should follow.</summary>
public static class MatchTestData
{
    /// <summary>A player whose every attribute sits at the given 1-20 level, with role-appropriate emphasis. Uniform by design - a test comparing two skill levels shouldn't be perturbed by attribute noise.</summary>
    public static Player MakePlayer(string name, int level, PlayerRole role = PlayerRole.Batsman, BowlingRoleType bowlingRole = BowlingRoleType.NotABowler)
    {
        int l = Math.Clamp(level, 1, 20);
        var p = new Player
        {
            FirstName = name, LastName = "P",
            DateOfBirth = new DateOnly(1997, 1, 1),
            PrimaryRole = role,
            BowlingRole = bowlingRole,
            CurrentAbility = l * 10, PotentialAbility = l * 10,
            Batting = new BattingAttributes
            {
                Technique = l, Timing = l, ShotSelection = l, DefensiveAbility = l, Aggression = l,
                AgainstPace = l, AgainstSpin = l, ShortBallAbility = l, SwingHandling = l, SeamHandling = l,
                SpinHandling = l, DeathOverBatting = l, PowerHitting = l, StrikeRotation = l,
                BoundaryHitting = l, RiskManagement = l
            },
            Bowling = new BowlingAttributes
            {
                Pace = l, Accuracy = l, Swing = l, Seam = l, Spin = bowlingRole == BowlingRoleType.SpecialistSpinner ? l : 4,
                Variation = l, Yorker = l, Bouncer = l, SlowerBall = l, DeathBowling = l,
                NewBallBowling = l, MiddleOverBowling = l, Containment = l, AttackingAbility = l
            },
            Fielding = new FieldingAttributes { Catching = l, Reflexes = l, Throwing = l, GroundFielding = l, Positioning = l, BoundaryFielding = l },
            Mental = new MentalAttributes
            {
                Composure = l, Concentration = l, Confidence = l, Determination = l, Leadership = l,
                PressureHandling = l, DecisionMaking = l, Adaptability = l, Professionalism = l,
                Consistency = l, GameAwareness = l, RunningCalling = l, ReviewJudgement = l
            },
            Physical = new PhysicalAttributes { Fitness = l, Stamina = l, Strength = l, Speed = l, InjuryProneness = 10, Recovery = l }
        };

        if (bowlingRole == BowlingRoleType.SpecialistSpinner) p.Bowling.Pace = 4;
        p.RecalculateFormatSuitability();
        return p;
    }

    /// <summary>An eleven at a given level: seven batters, a keeper, and five bowling options including a spinner.</summary>
    public static (List<Player> BattingOrder, List<Player> Bowlers) MakeEleven(string prefix, int battingLevel, int bowlingLevel)
    {
        var order = new List<Player>();
        for (int i = 1; i <= 6; i++) order.Add(MakePlayer($"{prefix}Bat{i}", battingLevel));
        order.Add(MakePlayer($"{prefix}Keeper", battingLevel, PlayerRole.WicketKeeper));

        var allrounder = MakePlayer($"{prefix}AR", battingLevel, PlayerRole.BowlingAllrounder, BowlingRoleType.FirstChange);
        var spinner = MakePlayer($"{prefix}Spin", Math.Max(1, battingLevel - 6), PlayerRole.Bowler, BowlingRoleType.SpecialistSpinner);
        var seam1 = MakePlayer($"{prefix}Seam1", Math.Max(1, battingLevel - 7), PlayerRole.Bowler, BowlingRoleType.OpeningBowler);
        var seam2 = MakePlayer($"{prefix}Seam2", Math.Max(1, battingLevel - 7), PlayerRole.Bowler, BowlingRoleType.OpeningBowler);

        foreach (var bowler in new[] { allrounder, spinner, seam1, seam2 })
            SetBowlingLevel(bowler, bowlingLevel);

        order.AddRange(new[] { allrounder, spinner, seam1, seam2 });

        var bowlers = new List<Player> { seam1, seam2, spinner, allrounder, order[5] };
        SetBowlingLevel(order[5], Math.Max(1, bowlingLevel - 5)); // part-timer

        return (order, bowlers);
    }

    private static void SetBowlingLevel(Player p, int level)
    {
        int l = Math.Clamp(level, 1, 20);
        bool spinner = p.BowlingRole == BowlingRoleType.SpecialistSpinner;
        p.Bowling.Accuracy = l; p.Bowling.Variation = l; p.Bowling.Containment = l;
        p.Bowling.AttackingAbility = l; p.Bowling.DeathBowling = l; p.Bowling.NewBallBowling = l;
        p.Bowling.MiddleOverBowling = l; p.Bowling.Yorker = l; p.Bowling.SlowerBall = l;
        if (spinner) { p.Bowling.Spin = l; p.Bowling.Pace = 4; }
        else { p.Bowling.Pace = l; p.Bowling.Swing = l; p.Bowling.Seam = l; p.Bowling.Spin = 4; }
    }

    public static InningsSetup Setup(int battingLevel, int bowlingLevel, string prefix = "A")
    {
        var (order, bowlers) = MakeEleven(prefix, battingLevel, bowlingLevel);
        return new InningsSetup(order, order, bowlers);
    }
}
