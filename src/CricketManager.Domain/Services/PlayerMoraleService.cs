using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// What actually moves PlayerMorale, and the one place CoachTrust is adjusted. Two distinct
/// triggers, deliberately kept separate because they answer different questions:
/// - How is he being TREATED (squad decisions, promotion) - see AdjustForSquadDecision/
///   AdjustForPromotion. This is Section P (coach-player interaction) made real: a Roadmap or
///   Rest costs less trust than a Drop, and both cost less than a Drop with no path back,
///   because the player can tell the difference between being managed and being discarded.
/// - How did he actually DO (personal performance in the context of the team's result) - see
///   AdjustForMatchPerformance.
///
/// The read side lives in BallOutcomeModel (see PlayerMorale's own doc comment) as a small,
/// capped multiplicative term right next to Form's - real, but deliberately never the dominant
/// factor a player's own current form and ability already are.
/// </summary>
public sealed class PlayerMoraleService
{
    public void AdjustForSquadDecision(Player player, SquadDecisionType decision)
    {
        switch (decision)
        {
            case SquadDecisionType.Drop:
                // Losing a place you believed was yours stings more than losing one you never
                // really had - the same "judged against his own standing" idea Section A's
                // selection leniency already applies, mirrored here for how it FEELS rather
                // than how he's ranked.
                bool wasEstablished = player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice or SquadStatus.ReturningFromInjury;
                player.Morale.Adjust(wasEstablished ? -18 : -10);
                player.CoachTrust = Math.Clamp(player.CoachTrust - 6, 0, 100);
                break;

            case SquadDecisionType.Roadmap:
                // A real hit - he is out of the side - softened by a genuine path back rather
                // than being discarded outright.
                player.Morale.Adjust(-8);
                player.CoachTrust = Math.Clamp(player.CoachTrust + 3, 0, 100);
                break;

            case SquadDecisionType.Rest:
                // Framed as care, not punishment - the brief's own Kohli-style "given time away,
                // then a genuine return" case - and trusted coaches earn MORE trust for handling
                // a decline this way rather than reaching straight for a drop.
                player.Morale.Adjust(-3);
                player.CoachTrust = Math.Clamp(player.CoachTrust + 5, 0, 100);
                break;
        }
    }

    /// <summary>A young player, or one who has been out of favour, given a real chance - the "backing pays off" half of the brief's Section M.</summary>
    public void AdjustForPromotion(Player player)
    {
        player.Morale.Adjust(10);
        player.CoachTrust = Math.Clamp(player.CoachTrust + 4, 0, 100);
    }

    /// <summary>
    /// Post-Phase-6 carry-forward: an individual honour - player of the match, player of the
    /// series - lifts a player a little and nudges his reputation up a touch, which feeds his
    /// standing in the dressing room (DressingRoomService.Standing reads reputation). Deliberately
    /// small and bounded: a good day is a good day, not a transformation, and the effect washes
    /// out through morale's own decay if it is not backed up. `weight` scales it - 1.0 for a
    /// match award, higher for a series/tournament one.
    /// </summary>
    public void AdjustForIndividualHonour(Player player, double weight = 1.0)
    {
        weight = Math.Clamp(weight, 0.5, 3.0);
        player.Morale.Adjust(4.0 * weight);
        player.Reputation.Adjust(domesticDelta: 0.8 * weight, continentalDelta: 0.3 * weight, worldwideDelta: 0.1 * weight);
    }

    /// <summary>
    /// Personal performance in the context of the team's result - his own showing matters far
    /// more than what his team-mates did, which is why the team-result term is a small addition
    /// on top rather than an equal partner.
    /// </summary>
    /// <summary>teamWon is null for a draw/tie/no-result - none of those are a "loss" for morale purposes, so the small win/loss term is skipped entirely rather than guessed at.</summary>
    public void AdjustForMatchPerformance(Player player, double oppositionAdjustedRating, bool? teamWon)
    {
        double delta = oppositionAdjustedRating / 12.0; // rating is roughly -100..100, so this is typically -8..+8
        if (teamWon is { } won) delta += won ? 1.5 : -1.0;
        player.Morale.Adjust(delta);
    }
}
