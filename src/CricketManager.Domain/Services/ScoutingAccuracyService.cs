namespace CricketManager.Domain.Services;

/// <summary>
/// The functional point of ScoutingQuality: a team's ESTIMATE of a player's true
/// PotentialAbility is not the same as the true value unless scouting is excellent. A
/// weak-scouting team can badly misjudge (over or under) an unfamiliar player's ceiling;
/// elite scouting gets close to the real number. This is what will drive recruitment/
/// auction misjudgments in later phases (a team overpaying for a player they've overrated,
/// or missing a bargain they underrated) instead of every team having perfect information.
/// </summary>
public sealed class ScoutingAccuracyService
{
    private readonly Random _random;

    public ScoutingAccuracyService(Random? random = null) => _random = random ?? new Random();

    /// <summary>
    /// Returns a scouting ESTIMATE of PotentialAbility (1-200 scale), not the true value.
    /// Error margin shrinks as scoutingQuality rises: 0 quality -> up to +/-40, 100 quality -> ~0.
    /// </summary>
    public int EstimatePotentialAbility(int truePotentialAbility, int scoutingQuality)
    {
        double quality = Math.Clamp(scoutingQuality, 0, 100) / 100.0;
        double maxErrorMargin = 40 * (1 - quality);
        double error = (_random.NextDouble() * 2 - 1) * maxErrorMargin;
        return Math.Clamp((int)Math.Round(truePotentialAbility + error), 1, 200);
    }
}
