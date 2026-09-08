using System.Text.Json.Serialization;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// A player's psychological state - deliberately separate from FormState (how well he's
/// actually playing) and Reputation (his standing). Driven by how he's TREATED - selection/
/// squad decisions, being backed vs overlooked, personal performance in context - not just
/// results. See PlayerMoraleService for what actually moves it.
///
/// 0-100, 50 = neutral. Decays toward neutral like FormState does, because a good or bad
/// mood the club forgot about six months ago shouldn't still be live.
/// </summary>
public sealed class PlayerMorale
{
    [JsonInclude] public double Level { get; private set; } = 50;

    public void Adjust(double delta) => Level = Math.Clamp(Level + delta, 0, 100);

    public void DecayTowardNeutral(double amount = 1.5)
    {
        if (Level > 50) Level = Math.Max(50, Level - amount);
        else if (Level < 50) Level = Math.Min(50, Level + amount);
    }
}

/// <summary>
/// A squad's collective mood - a separate concept from individual PlayerMorale, driven by
/// results (with CONTEXT - see MatchResultContextService/MatchResultStory) and streaks
/// rather than any one player's personal treatment. Deliberately capped in how much it can
/// move a performance (see TeamMoraleService) so a winning streak never becomes an
/// unrealistic global multiplier and a losing one never makes recovery impossible - stated
/// explicitly because that is exactly the failure mode the planning brief's Section N warns
/// against by name.
/// </summary>
public sealed class TeamMorale
{
    [JsonInclude] public double Level { get; private set; } = 50;

    public void Adjust(double delta) => Level = Math.Clamp(Level + delta, 0, 100);

    public void DecayTowardNeutral(double amount = 1.0)
    {
        if (Level > 50) Level = Math.Max(50, Level - amount);
        else if (Level < 50) Level = Math.Min(50, Level + amount);
    }
}
