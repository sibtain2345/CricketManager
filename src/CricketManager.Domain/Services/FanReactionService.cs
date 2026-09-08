using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>
/// Post-Phase-16 completion pass (§14.9 / §13.3): the supporters' voice. Turns the month's most
/// charged CLUB events into a fan-reaction news item and moves that club's
/// <see cref="ValueObjects.ClubBoard.FanSentiment"/> - and, when sentiment is already on the
/// floor and results are bad, a fan PROTEST that piles real pressure on the board and the coach.
///
/// Deliberately hard-capped (a couple a month, only genuinely big events) - the Phase 16
/// lesson: flooding the news archive shifts the whole world. Runs on the monthly tick right after
/// the pundits, reading that month's accumulated events.
/// </summary>
public sealed class FanReactionService
{
    private const int MaxPerMonth = 3;

    public IEnumerable<GameEvent> React(IReadOnlyList<GameEvent> monthEvents, WorldState world, DateOnly date)
    {
        var results = new List<GameEvent>();

        foreach (var e in monthEvents.OrderByDescending(e => Weight(e.Type)))
        {
            if (results.Count >= MaxPerMonth) break;
            if (Weight(e.Type) == 0) continue;

            // The team is usually the SubjectId; for SeasonCompleted it is the champion, which
            // lands in SecondarySubjectId (SubjectId there is the competition).
            Team? team = null;
            if (e.SubjectId is { } s1 && world.Teams.TryGetValue(s1, out var t1)) team = t1;
            else if (e.SecondarySubjectId is { } s2 && world.Teams.TryGetValue(s2, out var t2)) team = t2;
            if (team is null || team.IsNational || team.IsFranchise) continue;
            var board = team.Board;

            (string text, double sentiment) = e.Type switch
            {
                GameEventType.CoachDismissed => ("The fans are split on the sacking - some relieved, some furious at the board.", -1),
                GameEventType.TeamRelegated => ("Relegation has left the supporters seething - questions are being asked of everyone at the club.", -8),
                GameEventType.TeamPromoted => ("The city is buzzing - promotion has the fans dreaming again.", +7),
                GameEventType.SeasonCompleted => ("The title win has sent the fanbase into raptures.", +9),
                GameEventType.ClubTakeover => ("The takeover has the fans cautiously optimistic - and wary.", +2),
                GameEventType.ForcedSale => ("Selling the club's best asset has gone down badly with the supporters.", -6),
                GameEventType.PlayerTransferred => ("Losing a favourite has stung the supporters.", -3),
                // §13.2/§13.6: a genuine governance crisis - the fans notice the club being run
                // badly, distinct from an ordinary result-driven mood swing.
                GameEventType.BoardroomCoup => ("Supporters are furious at the chaos in the boardroom - a club run like this has no business asking for patience on the pitch.", -5),
                _ => ("", 0)
            };
            if (sentiment == 0 && text.Length == 0) continue;

            board.MoveFanSentiment(sentiment);
            results.Add(new GameEvent(date, GameEventType.FanReaction, $"{team.Name}: {text}", team.Id));

            // §13.3: a genuine protest - only when sentiment has collapsed AND the club is having a
            // bad time. It bites the board's confidence in the coach.
            if (board.FanSentiment <= 22 && sentiment < 0 && results.Count < MaxPerMonth)
            {
                board.MoveFanSentiment(-3);
                team.BoardConfidence = Math.Clamp(team.BoardConfidence - 4, 0, 100);
                results.Add(new GameEvent(date, GameEventType.FanReaction,
                    $"{team.Name} supporters stage a protest outside the ground - the board is under real pressure now.", team.Id));
            }
        }

        return results;
    }

    private static int Weight(GameEventType t) => t switch
    {
        GameEventType.TeamRelegated => 90,
        GameEventType.SeasonCompleted => 80,
        GameEventType.TeamPromoted => 75,
        GameEventType.ForcedSale => 70,
        GameEventType.ClubTakeover => 65,
        GameEventType.BoardroomCoup => 62,
        GameEventType.CoachDismissed => 60,
        GameEventType.PlayerTransferred => 40,
        _ => 0
    };
}
