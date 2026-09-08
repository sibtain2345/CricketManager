using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 9, Slice 9.4: the player-side pressure on the market - transfer requests, unsettled
/// players, and a light agent layer.
///
/// - <b>Transfer request</b>: a buried fringe player who wants regular cricket - young enough to
///   still be building a career, Ambitious by temperament, and not getting a game - hands in a
///   request. It raises his market availability and cuts his fee (PlayerValuationService reads
///   Player.TransferRequested). A well-managed club (a strong man-manager as head coach, a player
///   who trusts the setup) can talk him round; a badly-run one cannot, and refusing the request
///   dents the dressing room.
/// - <b>Unsettled</b>: a genuine first-team player at a smaller club whose reputation has outgrown
///   it, with concrete interest from a bigger one, has his head turned - a morale/form drag
///   (applied on the monthly tick) until it resolves (he moves, or the interest fades).
/// - <b>Agent</b>: a MoneyFocused player being paid well below his market rate is pushed by his
///   agent toward a request - the same mechanism, a different trigger.
///
/// Runs on the quarterly tick (a real series/tour cadence), at the tail of ProcessQuarterlyTick.
/// </summary>
public sealed class TransferRequestService
{
    public IEnumerable<GameEvent> RunQuarterly(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        foreach (var player in world.Players.Where(Eligible).ToList())
        {
            var team = world.Teams[player.CurrentTeamId!.Value];
            int age = player.Age(date);
            int games = world.MatchesThisSeason.GetValueOrDefault(player.Id, world.MatchesThisSeason.Count == 0 ? 5 : 0);

            var contract = PlayerContractService.ActiveDomesticContract(world, player.Id);
            double marketWage = new PlayerValuationService().MarketWage(player, world.MarketIndex);
            bool underpaid = contract is not null && contract.AnnualWage < marketWage * 0.7;

            // --- transfer request ---
            if (!player.TransferRequested && player.SquadStatus is SquadStatus.Fringe or SquadStatus.Backup && age <= 29)
            {
                double frustration = 0.04
                    + (games <= 2 ? 0.10 : games <= 5 ? 0.04 : 0)
                    + (player.Personality.HasFlag(PersonalityTrait.Ambitious) ? 0.08 : 0)
                    + (player.Personality.HasFlag(PersonalityTrait.MoneyFocused) && underpaid ? 0.08 : 0)
                    + (player.Morale.Level < 40 ? 0.05 : 0);

                if (random.NextDouble() < frustration)
                {
                    // Can the club talk him round?
                    var coach = team.CurrentCoachId is { } cid ? world.Coaches.FirstOrDefault(c => c.Id == cid) : null;
                    double talkRound = 0.25
                        + (coach is not null ? AbilityScale.AttributeToHundred(coach.Attributes.ManManagement) / 100.0 * 0.4 : 0)
                        + (player.CoachTrust - 50) / 100.0 * 0.3;
                    if (random.NextDouble() < Math.Clamp(talkRound, 0.05, 0.85))
                    {
                        player.Morale.Adjust(4);
                        continue;
                    }

                    player.TransferRequested = true;
                    player.TransferListed = true;
                    team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony - 3, 0, 100);
                    events.Add(new GameEvent(date, GameEventType.TransferRequestFiled,
                        $"{player.FullName} has handed in a transfer request at {team.Name} - he wants regular cricket.",
                        player.Id, team.Id));
                    continue;
                }
            }

            // --- contract holdout (§9.2) - distinct from a transfer request. A genuine first-team
            // player, MoneyFocused, paid a long way under his market rate, downs tools: he refuses
            // to play until the club renegotiates. The club ends it by improving his deal (Slice
            // 9.1's EvaluateRenewal, which moves the wage toward market, clears the flag); until
            // then he is unavailable, exactly like a suspension.
            if (!player.HoldingOut && !player.TransferRequested
                && player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice
                && player.Personality.HasFlag(PersonalityTrait.MoneyFocused)
                && contract is not null && contract.AnnualWage < marketWage * 0.55
                && player.Reputation.Domestic >= 45)
            {
                double holdout = 0.10 + (0.55 - contract.AnnualWage / Math.Max(1, marketWage)) * 0.8
                                 + (player.Personality.HasFlag(PersonalityTrait.Ambitious) ? 0.05 : 0);
                if (random.NextDouble() < Math.Clamp(holdout, 0, 0.5))
                {
                    player.HoldingOut = true;
                    player.NonInjuryUnavailability = UnavailabilityReason.ContractHoldout;
                    team.DressingRoomHarmony = Math.Clamp(team.DressingRoomHarmony - 4, 0, 100);
                    events.Add(new GameEvent(date, GameEventType.ContractHoldout,
                        $"{player.FullName} is refusing to play for {team.Name} until his contract is renegotiated - his agent says the deal is well below his market value.",
                        player.Id, team.Id));
                    continue;
                }
            }

            // --- unsettled by a bigger club ---
            if (player.UnsettledUntil is null
                && player.SquadStatus is SquadStatus.FirstChoice or SquadStatus.SecondChoice
                && player.Personality.HasFlag(PersonalityTrait.Ambitious))
            {
                double standing = Math.Max(player.Reputation.Domestic, player.Reputation.Continental);
                bool outgrownClub = standing > team.Reputation.Domestic + 12;
                bool aBiggerClubExists = world.Teams.Values.Any(t => !t.IsNational && !t.IsFranchise
                    && t.Id != team.Id && t.Reputation.Domestic > team.Reputation.Domestic + 15 && t.Board.Ambition >= 60);

                // S3: a genuine commercial name stuck at a club that cannot showcase him is
                // measurably more restless - the bright-lights pull is real.
                double appealNudge = player.CommercialAppeal >= 55 && team.Reputation.Domestic < 60 ? 0.06 : 0;
                if (outgrownClub && aBiggerClubExists && random.NextDouble() < 0.12 + appealNudge)
                {
                    player.UnsettledUntil = date.AddMonths(4);
                    events.Add(new GameEvent(date, GameEventType.PlayerUnsettled,
                        $"{player.FullName}'s future at {team.Name} is in doubt amid interest from bigger clubs.",
                        player.Id, team.Id));
                }
            }
        }

        return events;
    }

    private static bool Eligible(Player p) =>
        !p.IsRetired && p.AcademyTeamId is null && p.LoanReturnDate is null && p.CurrentTeamId is not null;
}
