using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// A chance offered to the field. Difficulty is the key number: 0 is a dolly straight to hand,
/// 1 is the sort of chance that only an exceptional fielder even reaches.
/// </summary>
public sealed record FieldingChance(ShotZone Zone, double Difficulty, bool IsAerial);

/// <summary>What actually happened when a fielder went for it.</summary>
public sealed record FieldingOutcome(
    Guid? FielderId,
    FieldingPosition? Position,
    bool Reached,
    bool Held,
    int RunsConceded,
    bool Misfield,
    string Description);

/// <summary>
/// The individual fielder. This is what separates an exceptional one from a competent one, and
/// it is deliberately modelled as two SEPARATE questions, because they are two different skills:
///
/// 1. **Did he get there?** Range - ground covered, anticipation, speed. An outstanding fielder
///    at point reaches balls a normal one never touches, and that is why some catches look
///    impossible: they were impossible for almost everyone else.
/// 2. **Did he hold it once he got there?** Hands - catching and reflexes. A player can have
///    enormous range and ordinary hands, or be immobile with a safe pair.
///
/// Collapsing those into one "fielding" number is what makes a simulation feel flat: it cannot
/// express a fielder who saves twenty runs an innings but shells a regulation chance, or a slip
/// who never moves but never drops. Both exist, and both are common.
///
/// Two properties the curves guarantee on purpose:
/// - **Even the best drop.** A world-class fielder still puts down a routine chance a few percent
///   of the time. A model where elite fielders never err reads as fake immediately.
/// - **The worst still take catches.** A poor fielder holds most dollies; he just drops far more
///   of anything harder, and leaks runs through misfields the good one doesn't.
/// </summary>
public sealed class FielderPerformanceService
{
    private static double Scale(int attribute) => Common.AbilityScale.AttributeToHundred(attribute);

    /// <summary>
    /// How much ground this fielder covers at this position, 0-100.
    ///
    /// The weighting changes with the position because the job changes: a boundary rider is an
    /// athlete covering grass, a slip barely moves and lives on reaction, a ring fielder is
    /// anticipation plus a quick first step.
    /// </summary>
    public double GetRange(Player fielder, FieldingPosition position)
    {
        var f = fielder.Fielding;
        var info = FieldingPositions.Info(position);

        double range = info.SkillRequired switch
        {
            FieldingSkillType.SlipCatching or FieldingSkillType.Keeping =>
                Scale(f.Reflexes) * 0.60 + Scale(f.Positioning) * 0.30 + Scale(fielder.Physical.Speed) * 0.10,

            FieldingSkillType.CloseCatching =>
                Scale(f.Reflexes) * 0.55 + Scale(f.Positioning) * 0.25 + Scale(fielder.Physical.Speed) * 0.20,

            FieldingSkillType.BoundaryRiding =>
                Scale(fielder.Physical.Speed) * 0.35 + Scale(f.BoundaryFielding) * 0.30
                + Scale(f.Positioning) * 0.20 + Scale(f.GroundFielding) * 0.15,

            _ => Scale(fielder.Physical.Speed) * 0.30 + Scale(f.GroundFielding) * 0.30
                 + Scale(f.Positioning) * 0.25 + Scale(f.Throwing) * 0.15
        };

        // Stamina tells over a long innings in the field, but only at the margins.
        range *= 0.96 + Scale(fielder.Physical.Stamina) / 100.0 * 0.08;

        return Math.Clamp(range, 0, 100);
    }

    /// <summary>Pure catching reliability, 0-100 - separate from range on purpose.</summary>
    public double GetHands(Player fielder) =>
        Math.Clamp(Scale(fielder.Fielding.Catching) * 0.60 + Scale(fielder.Fielding.Reflexes) * 0.40, 0, 100);

    /// <summary>
    /// Whether he even gets a hand to it.
    ///
    /// This is where range does its work and where the gap between an exceptional fielder and an
    /// ordinary one is widest. On a regulation chance almost anyone gets there; on a very hard
    /// one, an elite fielder reaches roughly a third of them and an average one essentially none.
    /// That difference IS the "impossible catch" - it was only impossible for everyone else.
    /// </summary>
    public double GetReachProbability(double range, double difficulty)
    {
        difficulty = Math.Clamp(difficulty, 0, 1);
        double reach = 1.15 - difficulty * (1.30 - range / 100.0 * 0.85);
        return Math.Clamp(reach, 0.02, 0.995);
    }

    /// <summary>
    /// Whether he holds it, having got there.
    ///
    /// The hands penalty SCALES WITH DIFFICULTY, and that is the important part. A flat penalty
    /// said that good and bad hands differ by the same margin on a dolly as on a screamer, which
    /// is plainly wrong: almost anyone catches a regulation chance, and the gap between a safe
    /// pair of hands and a poor one opens up precisely as the chance gets harder. With a flat
    /// penalty an elite fielder was only about 1.8x better than an ordinary one on the hardest
    /// chances; weighting it properly puts that where it belongs.
    ///
    /// Note the ceiling below 1: an elite fielder still shells a routine chance a few percent of
    /// the time, which is exactly what real cricket does.
    /// </summary>
    public double GetHoldProbability(double hands, double difficulty)
    {
        difficulty = Math.Clamp(difficulty, 0, 1);
        double handsPenalty = (100 - hands) / 100.0 * (0.10 + difficulty * 0.30);
        double hold = 0.99 - difficulty * 0.45 - handsPenalty;
        return Math.Clamp(hold, 0.10, 0.97);
    }

    /// <summary>
    /// Resolves a catching chance against a specific fielder. Returns the full story, because
    /// "dropped at point" and "nowhere near it" are different events that commentary, analysis
    /// and the player's own fielding record should all be able to tell apart.
    /// </summary>
    public FieldingOutcome ResolveCatchChance(Player fielder, FieldingPosition position, FieldingChance chance, Random random)
    {
        double range = GetRange(fielder, position);
        double hands = GetHands(fielder);

        var info = FieldingPositions.Info(position);

        // A position's inherent difficulty stacks with the chance's own difficulty - a hard chance
        // at slip is harder than the same chance at mid-off.
        double effectiveDifficulty = Math.Clamp(chance.Difficulty * 0.85 + info.CatchDifficulty * 0.20, 0, 1);

        if (random.NextDouble() >= GetReachProbability(range, effectiveDifficulty))
        {
            // Never got there. Hard chances that beat a fielder usually cost runs.
            int runs = chance.Difficulty > 0.6 ? (random.NextDouble() < 0.55 ? 4 : 2) : (random.NextDouble() < 0.3 ? 4 : 1);
            return new FieldingOutcome(fielder.Id, position, false, false, runs, false,
                $"{fielder.FullName} could not get to it at {position}.");
        }

        if (random.NextDouble() < GetHoldProbability(hands, effectiveDifficulty))
            return new FieldingOutcome(fielder.Id, position, true, true, 0, false,
                effectiveDifficulty > 0.55
                    ? $"Outstanding catch by {fielder.FullName} at {position}!"
                    : $"Caught by {fielder.FullName} at {position}.");

        // Reached it and put it down. In the deep a spilled chance often still runs away.
        int spilled = !info.InsideCircle && random.NextDouble() < 0.45 ? 2 : 0;
        return new FieldingOutcome(fielder.Id, position, true, false, spilled, false,
            $"Dropped by {fielder.FullName} at {position}!");
    }

    /// <summary>
    /// A shot heading for the rope, and a fielder out there trying to cut it off.
    ///
    /// This is the other half of what an exceptional fielder is worth, and it is worth a lot: a
    /// boundary saved is three runs, and over an innings a great outfielder saves more runs than
    /// he takes catches. Shot power matters - nobody chases down a genuine flat six - and so does
    /// how far the fielder has to go, which is what makes placement and athleticism interact.
    /// </summary>
    public FieldingOutcome ResolveBoundaryAttempt(Player fielder, FieldingPosition position, double shotPower, Random random)
    {
        double range = GetRange(fielder, position);

        // 0.02 - 0.75. An elite outfielder cuts off a meaningful share of what would otherwise be
        // four; an immobile one almost none.
        double save = Math.Clamp(range / 100.0 * 0.55 - Math.Clamp(shotPower, 0, 1) * 0.20 + 0.15, 0.02, 0.75);

        if (random.NextDouble() >= save)
            return new FieldingOutcome(fielder.Id, position, false, false, 4, false,
                $"Beats {fielder.FullName} at {position} - four.");

        // Saved it. How many they get back depends on the arm and the ground fielding.
        double cleanliness = Scale(fielder.Fielding.GroundFielding) * 0.6 + Scale(fielder.Fielding.Throwing) * 0.4;
        int conceded = random.NextDouble() < 0.35 + cleanliness / 100.0 * 0.35 ? 2 : 3;

        return new FieldingOutcome(fielder.Id, position, true, false, conceded, false,
            $"Brilliantly cut off by {fielder.FullName} at {position} - saves the boundary.");
    }

    /// <summary>
    /// Chance of fumbling a routine ball and giving away an extra run.
    ///
    /// This is the quiet cost of a poor fielding side and the quiet value of a good one: no single
    /// misfield decides a match, but a side that leaks an extra run every few overs loses fifteen
    /// or twenty in an innings without anything memorable ever happening.
    /// </summary>
    public double GetMisfieldProbability(Player fielder)
    {
        double cleanliness = Scale(fielder.Fielding.GroundFielding) * 0.7 + Scale(fielder.Fielding.Positioning) * 0.3;
        return Math.Clamp(0.13 - cleanliness / 100.0 * 0.115, 0.012, 0.14);
    }

    /// <summary>Resolves an ordinary fielded ball - mostly nothing happens, occasionally a fumble costs a run.</summary>
    public FieldingOutcome ResolveGroundFielding(Player fielder, FieldingPosition position, int runsRun, Random random)
    {
        if (random.NextDouble() >= GetMisfieldProbability(fielder))
            return new FieldingOutcome(fielder.Id, position, true, false, runsRun, false, string.Empty);

        return new FieldingOutcome(fielder.Id, position, true, false, runsRun + 1, true,
            $"Misfield by {fielder.FullName} at {position} - an extra run.");
    }

    /// <summary>
    /// How much this whole side is worth in the field, 0-100 - the number a coach or scout would
    /// quote. Weighted towards the specialists actually placed rather than a flat average, because
    /// one outstanding fielder at point changes more than a marginally better man at fine leg.
    /// </summary>
    public double GetSideFieldingQuality(FieldSetting field, IReadOnlyDictionary<Guid, Player> players)
    {
        var rated = field.Placements
            .Where(p => players.ContainsKey(p.PlayerId))
            .Select(p => new
            {
                Range = GetRange(players[p.PlayerId], p.Position),
                Hands = GetHands(players[p.PlayerId]),
                Weight = FieldingPositions.Info(p.Position).IsCatchingPosition ? 1.4 : 1.0
            })
            .ToList();

        if (rated.Count == 0) return 50;

        double totalWeight = rated.Sum(r => r.Weight);
        return Math.Round(rated.Sum(r => (r.Range * 0.55 + r.Hands * 0.45) * r.Weight) / totalWeight, 1);
    }
}
