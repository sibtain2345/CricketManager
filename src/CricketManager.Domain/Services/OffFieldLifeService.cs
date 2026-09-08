using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 14 (§18.4): off-field life events - a child born, a bereavement, a visa or legal issue.
/// Rare, real, and they touch a player where the cricket cannot: a short unavailability
/// (<see cref="Player.PersonalLeaveUntil"/> + <see cref="UnavailabilityReason.PersonalLeave"/>), a
/// morale/form ripple, and occasionally a lasting nudge to temperament (a new parent often steadies).
///
/// Reviewed quarterly. Deliberately low-probability - most quarters, nothing happens to anyone.
/// </summary>
public sealed class OffFieldLifeService
{
    public IEnumerable<GameEvent> ReviewQuarterly(WorldState world, DateOnly date, Random random)
    {
        var events = new List<GameEvent>();

        // Clear expired leave (deterministic, first).
        foreach (var p in world.Players.Where(p => p.PersonalLeaveUntil is { } d && d <= date))
        {
            p.PersonalLeaveUntil = null;
            if (p.NonInjuryUnavailability == UnavailabilityReason.PersonalLeave)
                p.NonInjuryUnavailability = UnavailabilityReason.Available;
        }

        // Iterate world.Players list order (deterministic); a small per-player chance.
        foreach (var p in world.Players)
        {
            if (p.IsRetired || p.AcademyTeamId is not null) continue;
            if (p.PersonalLeaveUntil is not null) continue;
            if (random.NextDouble() >= 0.010) continue; // ~1% per quarter

            double roll = random.NextDouble();
            if (roll < 0.5 && p.Age(date) is >= 24 and <= 36)
            {
                // A child - a short paternity absence, then a steadier player.
                p.PersonalLeaveUntil = date.AddDays(random.Next(10, 22));
                if (p.NonInjuryUnavailability is UnavailabilityReason.Available)
                    p.NonInjuryUnavailability = UnavailabilityReason.PersonalLeave;
                p.Mental.Professionalism = Math.Min(20, p.Mental.Professionalism + 1);
                p.Morale.Adjust(6);
                events.Add(new GameEvent(date, GameEventType.OffFieldEvent,
                    $"{p.FullName} takes a short break for the birth of his child.", p.Id, p.CurrentTeamId));
            }
            else if (roll < 0.8)
            {
                // A bereavement - a form/morale dip that recovers.
                p.Morale.Adjust(-12);
                p.Form.RecordPerformance(-6);
                events.Add(new GameEvent(date, GameEventType.OffFieldEvent,
                    $"{p.FullName} is dealing with a family bereavement - the club has offered its full support.", p.Id, p.CurrentTeamId));
            }
            else
            {
                // A visa / legal / administrative issue - an unavailability, no lasting mark.
                p.PersonalLeaveUntil = date.AddDays(random.Next(20, 45));
                if (p.NonInjuryUnavailability is UnavailabilityReason.Available)
                    p.NonInjuryUnavailability = UnavailabilityReason.PersonalLeave;
                events.Add(new GameEvent(date, GameEventType.OffFieldEvent,
                    $"{p.FullName} is unavailable while a visa issue is resolved.", p.Id, p.CurrentTeamId));
            }
        }

        return events;
    }
}
