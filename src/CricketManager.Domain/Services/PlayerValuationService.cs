using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9, Slice 9.3: what a player is worth on the transfer market - the fee a buying club has
/// to find, and the number the selling club weighs an offer against.
///
/// The factors, and why each is here:
/// - <b>Ability</b> drives the bulk of it, on a steep curve - a world-class player is worth many
///   times a solid squad one, not linearly more.
/// - <b>Potential premium</b> - a 21-year-old with a high ceiling and real headroom carries a
///   premium a 29-year-old at the same current ability does not.
/// - <b>Age curve</b> - value peaks in the mid-20s and falls away after 30; a 34-year-old is worth
///   a fraction of his 27-year-old self at identical ability.
/// - <b>Reputation</b> - a commercial premium: a household name sells shirts.
/// - <b>Contract length remaining</b> - THE lever that makes a market a market. A player with a
///   year left has almost no fee (the selling club has no leverage - he can leave for nothing
///   next summer); a player on a fresh four-year deal costs a fortune.
/// - <b>Form</b> - a mild multiplier; a purple patch inflates a price, a slump deflates it.
/// - <b>Market index</b> - a world-wide inflation factor (Slice 9.3) so a 2045 fee is not a 2026 fee.
/// - <b>Transfer request / listed</b> - a player who wants out, or whom the club has made
///   available, goes for less.
/// </summary>
public sealed class PlayerValuationService
{
    /// <summary>The fee an absolute-peak player (ability ~100/100, mid-20s, on a long deal, in form) commands at market index 1.0. Everything scales down from here.</summary>
    private const double PeakValue = 9_000_000;

    public double EstimateValue(Player player, PlayerContract? contract, DateOnly asOf, double marketIndex = 1.0)
    {
        double ability = AbilityScale.CompositeAbilityToHundred(player.CurrentAbility);
        double baseValue = Math.Pow(Math.Clamp(ability, 0, 100) / 100.0, 2.3) * PeakValue;

        // Potential premium - only for a genuinely young player who still has room to grow into it.
        int age = player.Age(asOf);
        double headroom = Math.Clamp((player.PotentialAbility - player.CurrentAbility) / 40.0, 0, 1);
        double potentialPremium = age <= 24 ? headroom * baseValue * 0.6
            : age <= 27 ? headroom * baseValue * 0.3
            : 0;

        // Age curve.
        double ageFactor = age switch
        {
            <= 20 => 0.85,
            <= 23 => 1.0,
            <= 27 => 1.05,
            <= 29 => 0.95,
            <= 31 => 0.78,
            <= 33 => 0.55,
            <= 35 => 0.32,
            _ => 0.15
        };

        double reputationPremium = (player.Reputation.Domestic * 0.4 + player.Reputation.Continental * 0.35 + player.Reputation.Worldwide * 0.25)
                                   / 100.0 * baseValue * 0.35;

        double gross = (baseValue + potentialPremium) * ageFactor + reputationPremium;

        // Contract length remaining - the decisive lever.
        double monthsLeft = contract?.MonthsRemaining(asOf) ?? 12;
        double contractFactor = Math.Clamp(0.28 + monthsLeft / 48.0 * 0.72, 0.18, 1.0);
        gross *= contractFactor;

        // Form - a mild swing.
        gross *= 1 + Math.Clamp(player.Form.CurrentForm / 100.0, -1, 1) * 0.15;

        // Market inflation.
        gross *= Math.Clamp(marketIndex, 0.5, 6.0);

        // A player agitating to leave, or one the club has listed, is a weaker negotiating position.
        if (player.TransferRequested) gross *= 0.68;
        else if (player.TransferListed) gross *= 0.85;

        // A release clause, when set, is a HARD ceiling on what any club must pay.
        if (contract?.ReleaseClauseValue is { } clause)
            gross = Math.Min(gross, clause);

        return Math.Round(Math.Max(0, gross), 0);
    }

    /// <summary>
    /// A fair annual wage for a player of this quality, at the current market index - what a new
    /// contract offer is benchmarked against. Reuses WageBillService's own per-player shape (the
    /// Phase 7 stub is kept precisely as this valuation helper) and inflates it by the index.
    /// </summary>
    private readonly WageBillService _wages = new();

    public double MarketWage(Player player, double marketIndex = 1.0) =>
        Math.Round(_wages.PlayerWage(player) * Math.Clamp(marketIndex, 0.5, 6.0), 0);
}
