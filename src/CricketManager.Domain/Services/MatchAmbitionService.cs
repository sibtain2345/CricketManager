using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What a side is actually playing for at this moment. It is not fixed at the toss - it moves with the match.</summary>
public enum MatchAmbition
{
    /// <summary>Going for the win, and accepting risk to get it.</summary>
    PressForVictory,

    /// <summary>Playing properly, taking what comes, ready to push if the game opens up.</summary>
    PlayItStraight,

    /// <summary>The win has gone. Batting or bowling to secure the draw.</summary>
    SecureTheDraw,

    /// <summary>Under real pressure. Survival first, and nothing else.</summary>
    SaveTheMatch
}

/// <summary>A side's assessment at a session break, with the reasoning a dressing room would actually use.</summary>
public sealed record SessionAssessment(
    Guid TeamId,
    int Day,
    SessionType Session,
    MatchAmbition Ambition,
    /// <summary>0-100 chance of winning, as this side sees it.</summary>
    double WinChance,
    double DrawChance,
    double LossChance,
    string Reasoning);

/// <summary>
/// What result a side is playing for, and how that changes across a multi-day match.
///
/// This is the layer that makes Test cricket a strategic game rather than a long one. Two things
/// the design brief is explicit about:
///
/// 1. **A side reassesses session by session.** Where am I in this match, how many wickets are
///    left, who is still to bat, and what is realistically achievable from here? A side losing
///    heavily plays for the draw first - and if the situation improves enough that a win comes back
///    into view, it goes for it. That reassessment is the whole game.
/// 2. **Competition points shape ambition before a ball is bowled.** Two evenly matched sides in a
///    World Test Championship cycle both go for the win, because a draw is worth a fraction of a
///    victory and the table demands it. A side that needs points chases them; one already safe can
///    afford to be careful. The same match played in a dead rubber is a different match.
///
/// The assessment is deliberately a SIDE'S OWN view rather than an oracle. It reads its own
/// strength, the opposition's, the state of the game and the time left - and a side that rates
/// itself wrongly plays for the wrong result, which is a mistake a coach gets to watch.
/// </summary>
public sealed class MatchAmbitionService
{
    /// <summary>
    /// The ambition a side takes into the match, before a ball is bowled. Driven by the points on
    /// offer and by how the two sides compare - which is why a WTC fixture between equals is a
    /// different proposition from a dead rubber against a much stronger opponent.
    /// </summary>
    public MatchAmbition InitialAmbition(
        double ownStrength, double oppositionStrength, CompetitionPointsSystem points, double competitionImportance = 50)
    {
        // How much a win is worth compared with a draw. In a first-class competition that ratio is
        // typically three to one, and it is precisely why sides take risks for a result.
        double winPremium = points.Draw <= 0 ? 4.0 : (double)points.Win / points.Draw;

        double gap = ownStrength - oppositionStrength;

        // A side markedly weaker than its opponent starts by making sure it does not lose.
        if (gap < -22) return MatchAmbition.SecureTheDraw;

        // A big win premium plus real importance pushes even a slight underdog to go for it - the
        // table is asking for a result, and a draw does not pay.
        if (winPremium >= 2.5 && competitionImportance >= 45) return MatchAmbition.PressForVictory;

        if (gap > 15) return MatchAmbition.PressForVictory;

        return MatchAmbition.PlayItStraight;
    }

    /// <summary>
    /// Reassess at a session break. This is the heart of it: the same side can go into lunch playing
    /// for a draw and come out after tea going for the win, because the match changed.
    /// </summary>
    public SessionAssessment Assess(
        Guid teamId,
        MultiDayMatchPosition position,
        double ownStrength,
        double oppositionStrength,
        MatchAmbition currentAmbition,
        int day,
        SessionType session)
    {
        var (win, draw, loss) = EstimateOutcomes(position, ownStrength, oppositionStrength);

        MatchAmbition ambition;
        string reasoning;

        if (win >= 45)
        {
            ambition = MatchAmbition.PressForVictory;
            reasoning = "We are on top and there is time - go and win it.";
        }
        else if (loss >= 55)
        {
            // Under real pressure: survival first. But if a side has been here and dug out before,
            // that is exactly the situation the draw is for.
            ambition = position.TimeRemainingFraction < 0.25 ? MatchAmbition.SecureTheDraw : MatchAmbition.SaveTheMatch;
            reasoning = ambition == MatchAmbition.SaveTheMatch
                ? "We are in trouble and there is a long way to go - survival first."
                : "We cannot win from here, but we can bat out the time.";
        }
        else if (win >= 25 && position.TimeRemainingFraction > 0.30)
        {
            ambition = MatchAmbition.PlayItStraight;
            reasoning = "Evenly poised - play properly and see where we are in a session's time.";
        }
        else if (draw >= 55)
        {
            ambition = MatchAmbition.SecureTheDraw;
            reasoning = "The win has gone and so has theirs - make sure of the draw.";
        }
        else
        {
            ambition = MatchAmbition.PlayItStraight;
            reasoning = "Nothing decided yet.";
        }

        // A side already pressing for victory does not abandon it on one poor session - but it does
        // if the match has genuinely turned. Momentum matters, stubbornness does not.
        if (currentAmbition == MatchAmbition.PressForVictory && ambition == MatchAmbition.PlayItStraight && win >= 30)
        {
            ambition = MatchAmbition.PressForVictory;
            reasoning = "One session has not changed our minds - we are still going for this.";
        }

        return new SessionAssessment(teamId, day, session, ambition,
            Math.Round(win, 1), Math.Round(draw, 1), Math.Round(loss, 1), reasoning);
    }

    /// <summary>
    /// The side's own read of the three results. Deliberately not an oracle: it works from the
    /// scoreboard, the wickets left, the time left and its assessment of the two attacks, and a side
    /// that misjudges those plays for the wrong result.
    /// </summary>
    public (double Win, double Draw, double Loss) EstimateOutcomes(
        MultiDayMatchPosition position, double ownStrength, double oppositionStrength)
    {
        double timeLeft = Math.Clamp(position.TimeRemainingFraction, 0, 1);

        // With almost no time left, a draw is overwhelmingly likely whatever the scores.
        double drawBase = Math.Clamp(90 - timeLeft * 95, 5, 92);

        // Where the match actually stands. A lead is only worth something set against the wickets
        // in hand to build on it, which is the calculation a dressing room genuinely makes.
        double leadPressure = position.EffectiveLead / 60.0 * 12;
        double wicketsPressure = (position.OwnWicketsInHand - position.OppositionWicketsInHand) * 2.5;
        double qualityGap = (ownStrength - oppositionStrength) * 0.35;

        double advantage = leadPressure + wicketsPressure + qualityGap;

        // A side batting last against a big target is in danger however good it is.
        if (position.IsChasing && position.RunsRequired is { } required && position.OwnWicketsInHand > 0)
        {
            double runsPerWicket = required / (double)position.OwnWicketsInHand;
            if (runsPerWicket > 45) advantage -= 25;
            else if (runsPerWicket > 30) advantage -= 12;
        }

        double decisive = 100 - drawBase;
        double winShare = Math.Clamp(50 + advantage, 5, 95) / 100.0;

        double win = decisive * winShare;
        double loss = decisive * (1 - winShare);

        return (win, drawBase, loss);
    }

    /// <summary>
    /// Turns an ambition into the batting instruction that expresses it. This is what makes the
    /// assessment matter rather than being a label on a screen - a side playing to save a match
    /// actually blocks, and a side pressing for victory actually takes risks.
    /// </summary>
    public BattingIntent ToBattingIntent(MatchAmbition ambition, MultiDayMatchPosition position) => ambition switch
    {
        MatchAmbition.SaveTheMatch => BattingIntent.Blocking,
        MatchAmbition.SecureTheDraw => position.OwnWicketsInHand <= 4 ? BattingIntent.Blocking : BattingIntent.Anchoring,
        MatchAmbition.PressForVictory => position.TimeRemainingFraction < 0.35 ? BattingIntent.Attacking : BattingIntent.Normal,
        _ => BattingIntent.Normal
    };

    /// <summary>And the bowling instruction. A side chasing a result attacks; one holding on contains.</summary>
    public BowlingIntent ToBowlingIntent(MatchAmbition ambition) => ambition switch
    {
        MatchAmbition.PressForVictory => BowlingIntent.Attacking,
        MatchAmbition.SaveTheMatch or MatchAmbition.SecureTheDraw => BowlingIntent.Defensive,
        _ => BowlingIntent.Balanced
    };

    /// <summary>
    /// A field aggression that matches the ambition, so the two cannot contradict each other - a
    /// side pressing for victory with a defensive field is a side that has not made up its mind.
    /// </summary>
    public FieldAggression ToFieldAggression(MatchAmbition ambition) => ambition switch
    {
        MatchAmbition.PressForVictory => FieldAggression.Attacking,
        MatchAmbition.SaveTheMatch or MatchAmbition.SecureTheDraw => FieldAggression.Defensive,
        _ => FieldAggression.Balanced
    };
}

/// <summary>
/// Where a multi-day match stands from one side's point of view - the facts a dressing room reads
/// at a session break.
/// </summary>
public sealed record MultiDayMatchPosition
{
    /// <summary>Runs ahead (or behind, if negative) accounting for every innings so far.</summary>
    public int EffectiveLead { get; init; }

    public int OwnWicketsInHand { get; init; } = 10;
    public int OppositionWicketsInHand { get; init; } = 10;

    /// <summary>0-1. How much of the match is still to be played. The single biggest factor in whether a result is possible at all.</summary>
    public double TimeRemainingFraction { get; init; } = 1;

    public bool IsChasing { get; init; }
    public int? RunsRequired { get; init; }

    /// <summary>Which innings the match is in, 1-4.</summary>
    public int InningsNumber { get; init; } = 1;
}
