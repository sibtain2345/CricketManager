using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Suggests an initial SquadStatus from a player's actual ability/reputation/age/potential -
/// the same "derive a real value instead of leaving a bare enum default" discipline
/// RoleTraitDeriver already established for BattingTraits/BowlingTraits (see that class's own
/// doc comment, and CLAUDE.md's "standing hazard" note: this codebase has shipped the same
/// unpopulated-default-read-as-a-negative-signal bug five times). Called once at seed time -
/// WorldSeeder calls it right after a player's Reputation is assigned, mirroring exactly
/// where RoleTraitDeriver.ApplyTo() already runs for the same reason.
///
/// A human coach (or an AI one making a deliberate call) can always override the suggestion
/// directly - Player.SquadStatus is a plain settable property. This service only supplies a
/// sensible starting point, not a standing rule.
///
/// Deliberately does NOT try to derive EmergencyReplacement or ReturningFromInjury - both are
/// inherently EVENT-driven states (called up for one match through necessity; just back from
/// a layoff that used to be a first-team place) that no static ability/reputation/age snapshot
/// can honestly produce. PlayerAvailabilityService.MarkRecovered is where ReturningFromInjury
/// actually gets set, for the one player it happens to matter for - protecting an incumbent's
/// status against being permanently but silently displaced by an in-form stand-in, per the
/// planning brief's own example.
/// </summary>
public sealed class SquadStatusService
{
    public SquadStatus SuggestStatus(Player player, DateOnly asOf)
    {
        double ability = Common.AbilityScale.CompositeAbilityToHundred(player.CurrentAbility);
        double potential = Common.AbilityScale.CompositeAbilityToHundred(player.PotentialAbility);
        double standing = Math.Max(player.Reputation.Domestic, player.Reputation.Continental);
        int age = player.Age(asOf);

        // Real headroom (not just "potential slightly exceeds current", which is true of
        // almost every young player) and genuinely young - a development story, not yet a
        // finished product.
        bool meaningfulHeadroom = potential - ability >= 15 && age <= 25;
        if (meaningfulHeadroom)
            return ability >= 55 ? SquadStatus.LongTermProject : SquadStatus.DevelopmentProspect;

        if (ability >= 70 || standing >= 60) return SquadStatus.FirstChoice;
        if (ability >= 55 || standing >= 35) return SquadStatus.SecondChoice;
        if (ability >= 40) return SquadStatus.Backup;
        return SquadStatus.Fringe;
    }
}
