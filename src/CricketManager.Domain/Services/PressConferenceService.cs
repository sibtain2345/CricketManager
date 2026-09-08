using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.4: press conferences, keyed to how well a coach handles the media.
///
/// Coach.Attributes.MediaHandling has existed as a real attribute since Phase 1 and was barely
/// consumed. This makes it matter: a notable event (a losing run, a shock defeat, a board vote
/// of no-confidence, a contentious selection) raises a storyline the coach must address; he picks
/// a <see cref="PressTone"/> - straight bat, bullish, shield the players, or have a go at someone -
/// and how it lands depends on his MediaHandling and PressureHandling, the heat of the room, and
/// whether results back him up.
///
/// A well-handled conference can steady the board and lift the dressing room; a botched one
/// (especially a confrontational one from a low-MediaHandling coach) costs board confidence and
/// the room's goodwill. For a human coach the storyline is surfaced and the headless default is
/// the safe, diplomatic answer.
/// </summary>
public sealed class PressConferenceService
{
    private readonly DisciplineService _discipline = new();

    /// <summary>
    /// Looks at the events raised recently (this tick's) and, for a few of the most charged ones,
    /// puts a storyline on the relevant coach's desk. Deliberately not every event - most cricket
    /// does not generate a press storm.
    /// </summary>
    public void RaiseStorylines(WorldState world, DateOnly date, IReadOnlyList<GameEvent> recentEvents)
    {
        foreach (var team in world.Teams.Values.OrderBy(t => t.Name))
        {
            var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
            if (coach is null || world.PendingPressStories.ContainsKey(coach.Id)) continue;

            // A genuine losing run is the classic press storyline.
            if (team.CurrentStreak <= -4)
            {
                world.PendingPressStories[coach.Id] = new PressStoryline(date,
                    $"{team.Name} have lost {-team.CurrentStreak} in a row - the questions are about {coach.FullName}'s future.",
                    Heat: Math.Clamp(45 + -team.CurrentStreak * 6, 45, 90), AboutSelection: false);
                continue;
            }

            // The board publicly wobbling is a media event in itself.
            if (recentEvents.Any(e => e.Type == GameEventType.BoardConfidenceShift && e.SubjectId == coach.Id
                                      && e.Headline.Contains("losing patience")))
            {
                world.PendingPressStories[coach.Id] = new PressStoryline(date,
                    $"{team.Name}'s board have gone public with their doubts - {coach.FullName} is asked to respond.",
                    Heat: 78, AboutSelection: false);
                continue;
            }

            // A contentious selection call.
            if (recentEvents.Any(e => e.Type == GameEventType.SquadDecisionMade && e.SecondarySubjectId == team.Id))
            {
                world.PendingPressStories[coach.Id] = new PressStoryline(date,
                    $"{coach.FullName} is pressed on a selection call that has divided opinion.",
                    Heat: 52, AboutSelection: true);
            }
        }
    }

    /// <summary>
    /// Every coach with a pending storyline faces the media. Consumes the caller's shared RNG at a
    /// fixed point (deterministic). Returns the press-conference events; clears the storylines.
    /// </summary>
    public IEnumerable<GameEvent> HoldPendingConferences(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        // Deterministic order - iterate the coach list, not the dictionary keys (Guids differ
        // between two loads of the same seed).
        foreach (var coach in world.Coaches.OrderBy(c => c.LastName).ThenBy(c => c.FirstName))
        {
            if (!world.PendingPressStories.TryGetValue(coach.Id, out var story)) continue;
            world.PendingPressStories.Remove(coach.Id);

            if (coach.CurrentTeamId is not { } tid || !world.Teams.TryGetValue(tid, out var team)) continue;

            var tone = ChooseTone(coach, story, random);
            double media = AbilityScale.AttributeToHundred(coach.Attributes.MediaHandling);
            double execution = Math.Clamp(media / 100.0 + (random.NextDouble() - 0.5) * 0.3, 0, 1);
            var (confidenceDelta, satisfactionDelta, harmonyDelta, reputationDelta, line) =
                Resolve(coach, team, story, tone, execution);

            team.BoardConfidence = Math.Clamp(team.BoardConfidence + confidenceDelta, 0, 100);
            coach.CareerSatisfaction = Math.Clamp(coach.CareerSatisfaction + satisfactionDelta, 0, 100);
            team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony + harmonyDelta, 0, 100);
            if (Math.Abs(reputationDelta) > 0.01)
                coach.Reputation.Adjust(domesticDelta: reputationDelta, continentalDelta: reputationDelta * 0.3);

            events.Add(new GameEvent(date, GameEventType.PressConference, line, coach.Id, team.Id));

            // Section A: a confrontational answer from a poor media-handler can cross the line -
            // team management is covered by the ICC Code of Conduct, and this is where a coach
            // charge actually happens (not on the field). Heat of the room and a low execution
            // raise the risk; a genuine below-the-belt swipe at an official or the board is a
            // Level 2 charge.
            if (tone is PressTone.Confrontational or PressTone.Bullish && execution < 0.4)
            {
                double chargeChance = Math.Clamp((0.4 - execution) * (story.Heat / 100.0) * (tone == PressTone.Confrontational ? 0.9 : 0.35), 0, 0.4);
                if (random.NextDouble() < chargeChance)
                {
                    int level = execution < 0.22 && story.Heat >= 70 ? 2 : 1;
                    string reason = tone == PressTone.Confrontational
                        ? "publicly criticising the officials" : "an ill-judged public outburst";
                    var charge = _discipline.ChargeCoach(coach, team, date, level, reason, random);
                    if (charge is not null) events.Add(charge);
                }
            }
        }

        return events;
    }

    private static PressTone ChooseTone(Coach coach, PressStoryline story, Random random)
    {
        double media = AbilityScale.AttributeToHundred(coach.Attributes.MediaHandling);
        double pressure = AbilityScale.AttributeToHundred(coach.Attributes.PressureHandling);

        // A human coach's headless default is the safe answer.
        if (coach.IsHumanControlled) return PressTone.Diplomatic;

        // Weight the four tones.
        double diplomatic = 0.35 + media / 100.0 * 0.4;
        double bullish = 0.20 + (coach.Philosophy is CoachingPhilosophy.Aggressive or CoachingPhilosophy.ShortTermResults ? 0.15 : 0)
                              + Math.Clamp((pressure - 40) / 60.0, 0, 1) * 0.15;
        double shield = 0.20 + Math.Clamp((coach.Attributes.ManManagement - 10) / 10.0, 0, 1) * 0.15;
        double confrontational = Math.Clamp(0.10 + (story.Heat - 50) / 50.0 * 0.15
                                            + (40 - Math.Min(40, media)) / 40.0 * 0.20
                                            - Math.Clamp((pressure - 50) / 50.0, 0, 1) * 0.12, 0.01, 0.5);

        double total = diplomatic + bullish + shield + confrontational;
        double roll = random.NextDouble() * total;
        if ((roll -= diplomatic) < 0) return PressTone.Diplomatic;
        if ((roll -= bullish) < 0) return PressTone.Bullish;
        if ((roll -= shield) < 0) return PressTone.ShieldsThePlayers;
        return PressTone.Confrontational;
    }

    private static (double Confidence, double Satisfaction, double Harmony, double Reputation, string Line) Resolve(
        Coach coach, Team team, PressStoryline story, PressTone tone, double execution)
    {
        double heat = story.Heat / 100.0;

        return tone switch
        {
            PressTone.Diplomatic => (
                Confidence: (execution - 0.4) * 3,
                Satisfaction: 0,
                Harmony: 0,
                Reputation: (execution - 0.5) * 1.5,
                Line: $"{coach.FullName} gives a measured, straight-bat press conference."),

            PressTone.Bullish => (
                Confidence: (execution - 0.45) * 8 * heat,
                Satisfaction: 1.5,
                Harmony: 2.5 * execution,
                Reputation: (execution - 0.5) * 3,
                Line: execution >= 0.5
                    ? $"{coach.FullName} comes out fighting and backs his players in public."
                    : $"{coach.FullName} talks a big game, but nobody in the room looks convinced."),

            PressTone.ShieldsThePlayers => (
                Confidence: -2 - heat * 3,
                Satisfaction: -1,
                Harmony: 3 + execution * 3,
                Reputation: execution * 1.0,
                Line: $"{coach.FullName} takes the heat off his players and puts it squarely on himself."),

            _ => ( // Confrontational
                Confidence: -4 - heat * 6 + execution * 4,
                Satisfaction: -3 + execution * 4,
                Harmony: execution >= 0.6 ? 1.0 : -3.0,
                Reputation: execution >= 0.65 ? 1.5 : -4.0,
                Line: execution >= 0.65
                    ? $"{coach.FullName} has a pointed go at the critics - and just about carries it off."
                    : $"{coach.FullName} loses his cool with a reporter - it will not read well."),
        };
    }
}
