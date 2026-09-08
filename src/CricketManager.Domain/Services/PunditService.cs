using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.4: the pundit / former-player opinion layer on top of NewsEngine.
///
/// NewsEngine turns an event into a factual headline ("X sack Y"). This adds the reaction piece -
/// the ex-player column, the TV panel take - that a real cricket news cycle carries: opinionated,
/// grounded in the event that just happened, never inventing a new fact. Deliberately capped so a
/// busy week does not drown in hot takes.
/// </summary>
public sealed class PunditService
{
    private const int MaxOpinionsPerBatch = 4;

    /// <summary>Turns the most charged events of a batch into pundit-opinion events.</summary>
    public IEnumerable<GameEvent> Opine(IReadOnlyList<GameEvent> events, DateOnly date, IReadOnlyList<Pundit>? pundits = null)
    {
        var worthy = events
            .Where(e => Opinion(e.Type) is not null)
            .OrderByDescending(e => Weight(e.Type))
            .Take(MaxOpinionsPerBatch)
            .ToList();

        int i = 0;
        foreach (var e in worthy)
        {
            var take = Opinion(e.Type)!;

            // Phase 12: a pundit has a conflict of interest. Pick one deterministically for this
            // event and, if it concerns his old side or his rival, colour the take.
            string bias = "";
            if (pundits is { Count: > 0 })
            {
                // A stable index (string.GetHashCode is randomised per process - never use it).
                int key = i;
                foreach (char c in e.Headline) key = unchecked(key * 31 + c);
                var pundit = pundits[Math.Abs(key) % pundits.Count];
                var involves = e.SubjectId is { } s ? s : (Guid?)null;
                var involves2 = e.SecondarySubjectId;
                if (pundit.FormerTeamId is { } ft && (ft == involves || ft == involves2) && pundit.BiasStrength > 0.35)
                    bias = $" — though {pundit.Name}, who played for them, is careful not to be too harsh";
                else if (pundit.RivalTeamId is { } rt && (rt == involves || rt == involves2) && pundit.BiasStrength > 0.35)
                    bias = $" — {pundit.Name} has never hidden what he thinks of that club, mind";
            }

            yield return new GameEvent(date, GameEventType.PunditOpinion,
                $"{take}{bias} — on: “{e.Headline}”", e.SubjectId, e.SecondarySubjectId);
            i++;
        }
    }

    private static int Weight(GameEventType type) => type switch
    {
        GameEventType.CoachDismissed => 90,
        GameEventType.CoachResigned => 78,
        GameEventType.ClubTakeover => 74,
        GameEventType.PlayerRetired => 70,
        GameEventType.HallOfFameInduction => 68,
        GameEventType.UmpiringControversy => 66,
        GameEventType.TeamPromoted or GameEventType.TeamRelegated => 60,
        GameEventType.BoardObjectivesReviewed => 55,
        GameEventType.SeasonAward => 50,
        GameEventType.PlayerMilestone => 48,
        GameEventType.BoardConfidenceShift => 45,
        _ => 0
    };

    private static string? Opinion(GameEventType type) => type switch
    {
        GameEventType.CoachDismissed => "“Harsh, and the board have to look at themselves here”, says the panel",
        GameEventType.CoachResigned => "“You can hardly blame him for walking”, one former captain reckons",
        GameEventType.ClubTakeover => "“Money talks, but it does not pick a batting order”, warns a veteran writer",
        GameEventType.PlayerRetired => "“A giant of the game — they do not make them like that any more”",
        GameEventType.HallOfFameInduction => "“Richly deserved. First name on the team sheet for a generation”",
        GameEventType.UmpiringControversy => "“We have to have a proper conversation about officiating standards”",
        GameEventType.TeamPromoted => "“They have earned this — now the hard part starts”",
        GameEventType.TeamRelegated => "“A club that size should never be down there”",
        GameEventType.BoardObjectivesReviewed => "“The pressure is on now, no question”, says the studio",
        GameEventType.SeasonAward => "“No arguments — he has been the standout all year”",
        GameEventType.PlayerMilestone => "“What a servant to the game. Take a bow”",
        GameEventType.BoardConfidenceShift => "“Votes of confidence are usually the beginning of the end”",
        _ => null
    };
}
