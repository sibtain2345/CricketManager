using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Turns a MatchResultStory into an actual, differentiated TeamMorale delta - the point of
/// classifying the result at all. A dominant win and a narrow win both feel like wins, but not
/// the same amount; a collapse loss and a heavy-but-even loss both feel like losses, but a
/// collapse stings more, because it reads as self-inflicted.
///
/// Also owns Team.CurrentStreak, since streak state and its morale consequence are the same
/// piece of bookkeeping - losing after a long winning streak hurts MORE than the same loss cold,
/// and snapping a long losing streak is a genuine relief, which is exactly the kind of streak
/// sensitivity Section O asks for without needing a separate "how long was the streak" lookup.
/// </summary>
public sealed class TeamMoraleService
{
    private static readonly Dictionary<MatchResultStory, double> BaseDelta = new()
    {
        [MatchResultStory.DominantWin] = 8,
        [MatchResultStory.OrdinaryWin] = 5,
        [MatchResultStory.NarrowWin] = 4,
        [MatchResultStory.ComebackWin] = 9,   // fighting back and winning is the biggest builder
        [MatchResultStory.UpsetWin] = 10,     // beating a genuinely stronger side
        [MatchResultStory.OrdinaryLoss] = -5,
        [MatchResultStory.CloseLoss] = -3,    // came close - a softer hit than an even defeat
        [MatchResultStory.HeavyLoss] = -8,
        [MatchResultStory.CollapseLoss] = -9, // reads as self-inflicted, stings more than heavy
        [MatchResultStory.Draw] = 0,
        [MatchResultStory.NoResult] = 0
    };

    public void ApplyMatchResult(Team team, MatchResultStory story)
    {
        double delta = BaseDelta.GetValueOrDefault(story, 0);
        bool won = delta > 0;
        bool lost = delta < 0;

        // Falling from a real high hurts more than an ordinary loss would; snapping a real
        // slump is a bigger relief than an ordinary win. Deliberately small adjustments on top
        // of the base delta, not a separate mechanic.
        if (lost && team.CurrentStreak >= 5) delta -= 2;
        if (won && team.CurrentStreak <= -5) delta += 3;

        team.Morale.Adjust(delta);

        // Wave 6: team-form momentum moves further and faster than morale - a win pushes it up
        // hard, a loss down hard, and it decays back on the monthly tick. A result AGAINST the
        // current momentum (a loss while flying, a win while spiralling) is a bigger jolt.
        double momentumDelta = won ? 14 : lost ? -14 : -team.FormMomentum * 0.3; // a draw bleeds it toward neutral
        if (won && team.FormMomentum < -20) momentumDelta += 8;   // arresting a spiral
        if (lost && team.FormMomentum > 20) momentumDelta -= 8;   // a jolt off a high
        team.FormMomentum = Math.Clamp(team.FormMomentum + momentumDelta, -100, 100);

        if (won) team.CurrentStreak = team.CurrentStreak > 0 ? team.CurrentStreak + 1 : 1;
        else if (lost) team.CurrentStreak = team.CurrentStreak < 0 ? team.CurrentStreak - 1 : -1;
        else team.CurrentStreak = 0; // a draw/no-result/tie breaks a streak without starting the opposite one
    }

    /// <summary>
    /// Wave 6: team-form momentum decays toward neutral - deliberately faster than morale's own
    /// monthly decay, because momentum is a trajectory that only lasts while it is being fed.
    /// </summary>
    public void DecayFormMomentum(Team team, double amount)
    {
        if (team.FormMomentum > 0) team.FormMomentum = Math.Max(0, team.FormMomentum - amount);
        else if (team.FormMomentum < 0) team.FormMomentum = Math.Min(0, team.FormMomentum + amount);
    }

    /// <summary>
    /// 0.93-1.07: real, but deliberately small - the planning brief's Section N is explicit that
    /// a winning streak must never make a team unbeatable and a losing one must never make
    /// recovery impossible, so this is capped well short of anything that could dominate a
    /// player's own ability and form. Wave 6 folds a small team-form-momentum term in on top of
    /// morale - a side genuinely on the up plays with a touch more belief than its raw mood alone.
    /// </summary>
    public double PerformanceMultiplier(Team? team) =>
        team is null ? 1.0
        : Math.Clamp(1.0 + (team.Morale.Level - 50) / 833.0 + team.FormMomentum / 100.0 * 0.02, 0.93, 1.07);
}
