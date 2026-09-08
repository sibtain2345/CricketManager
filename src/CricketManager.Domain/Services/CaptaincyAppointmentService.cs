using CricketManager.Domain.Common;
using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>One appointment: who captains, who deputises, and why.</summary>
public sealed record CaptaincyChoice(Guid CaptainId, string CaptainName, Guid? ViceCaptainId, double Merit, string Reasoning);

/// <summary>
/// Post-Phase-6, section D: appointing captains.
///
/// Four things the brief is explicit about:
/// 1. **Format-specific, four patterns.** A team can have one captain across all formats, a
///    red-ball/white-ball split, a long-form (Test+ODI)/T20 split, or three separate captains.
///    <see cref="CaptaincyPattern"/> models all four; <see cref="SuggestPattern"/> reads the
///    squad's leadership landscape and picks the one that fits.
/// 2. **It is the Head Coach's decision** (AI or human), coloured by how good a judge of a
///    leader he is (his own tactical/man-management attributes).
/// 3. **Merit-gated.** A strong leader who does not merit an XI place on playing ability alone
///    cannot captain the side - the candidate pool is the players good enough to be picked.
/// 4. **International captaincy carries higher scrutiny** - the merit bar and the leadership bar
///    are both stricter for a national side, because a national captain under pressure is a
///    bigger story than a domestic one (this feeds the existing board-pressure machinery via
///    the caller, it does not build a parallel one).
///
/// The captain also has genuine input into squad selection - see <see cref="CaptainSquadInput"/>.
/// </summary>
public sealed class CaptaincyAppointmentService
{
    private readonly PlayerSelectionEvaluator _evaluator = new();
    private readonly DressingRoomService _room = new();

    /// <summary>
    /// How well suited a player is to captain in this format, 0-100. Blends what makes a leader
    /// (innate leadership, the experience a CaptaincyProfile carries, dressing-room standing,
    /// tactical judgement) with the hard requirement that he is worth his place with bat or ball.
    /// </summary>
    public double CaptaincyScore(Player p, MatchFormat format, CaptaincyProfile? profile, DateOnly asOf, bool international)
    {
        double innate = AbilityScale.AttributeToHundred(p.Mental.Leadership);
        double judgement = (AbilityScale.AttributeToHundred(p.Mental.GameAwareness)
                            + AbilityScale.AttributeToHundred(p.Mental.DecisionMaking)
                            + AbilityScale.AttributeToHundred(p.Mental.PressureHandling)) / 3.0;
        double experience = profile is null ? 0 : (1 - Math.Exp(-profile.MatchesCaptained / 30.0)) * 100;
        double standing = _room.Standing(p, asOf);

        double leaderCore = innate * 0.34 + judgement * 0.30 + experience * 0.18 + standing * 0.18;
        if (p.Personality.HasFlag(PersonalityTrait.Leader)) leaderCore += 6;
        if (p.Personality.HasFlag(PersonalityTrait.Inconsistent)) leaderCore -= 5;

        // Playing merit - a fair read for either discipline.
        double batMerit = _evaluator.Evaluate(p, format).TotalScore;
        double bowlMerit = p.BowlingRole != BowlingRoleType.NotABowler ? _evaluator.Evaluate(p, format, forBowling: true).TotalScore : 0;
        double merit = Math.Max(batMerit, bowlMerit);

        // A national captaincy is a heavier weight of expectation - it leans harder on the
        // leadership qualities and the candidate has to be a genuinely secure pick.
        double leaderWeight = international ? 0.62 : 0.55;
        return Math.Clamp(leaderCore * leaderWeight + merit * (1 - leaderWeight), 0, 100);
    }

    /// <summary>
    /// The players good enough to captain: they must merit a place (be in roughly the top of the
    /// squad by playing score) AND clear a minimum leadership bar. International sides apply both
    /// filters more strictly.
    /// </summary>
    private List<Player> EligibleCandidates(IReadOnlyList<Player> squad, MatchFormat format, DateOnly asOf, bool international)
    {
        var ranked = squad
            .Where(p => !p.IsRetired && !p.RetiredFormats.Contains(format))
            .Select(p => (Player: p, Score: Math.Max(
                _evaluator.Evaluate(p, format).TotalScore,
                p.BowlingRole != BowlingRoleType.NotABowler ? _evaluator.Evaluate(p, format, forBowling: true).TotalScore : 0)))
            .OrderByDescending(x => x.Score)
            .ToList();

        int meritCutoff = international ? 11 : 13;
        double leadershipBar = international ? 52 : 45;

        return ranked.Take(meritCutoff)
            .Where(x => AbilityScale.AttributeToHundred(x.Player.Mental.Leadership) >= leadershipBar
                        || x.Player.Personality.HasFlag(PersonalityTrait.Leader))
            .Select(x => x.Player)
            .ToList();
    }

    /// <summary>Which split fits this squad's leadership - if one man is clearly the best leader across all three formats, keep it unified; if a T20 specialist is the standout white-ball leader, split.</summary>
    public CaptaincyPattern SuggestPattern(IReadOnlyList<Player> squad, DateOnly asOf, IReadOnlyDictionary<Guid, CaptaincyProfile> profiles, bool international)
    {
        Guid? Best(MatchFormat f)
        {
            var c = EligibleCandidates(squad, f, asOf, international);
            return c.Count == 0 ? null
                : c.OrderByDescending(p => CaptaincyScore(p, f, profiles.GetValueOrDefault(p.Id), asOf, international)).First().Id;
        }

        var test = Best(MatchFormat.Test);
        var odi = Best(MatchFormat.ODI);
        var t20 = Best(MatchFormat.T20);

        if (test is null || odi is null || t20 is null) return CaptaincyPattern.Unified;
        if (test == odi && odi == t20) return CaptaincyPattern.Unified;
        if (test == odi && odi != t20) return CaptaincyPattern.LongFormShortForm;
        if (odi == t20 && test != odi) return CaptaincyPattern.RedBallWhiteBall;
        return CaptaincyPattern.ThreeSeparate;
    }

    /// <summary>
    /// Appoints captains for the whole team according to its <see cref="Team.CaptaincyPattern"/>.
    /// The coach's judgement colours it: a poor judge of a leader sometimes takes the second-best
    /// name. Mutates the team's captain/vice-captain maps and returns the choices made.
    /// </summary>
    public IReadOnlyList<(MatchFormat Format, CaptaincyChoice Choice)> AppointAll(
        Team team, IReadOnlyList<Player> squad, DateOnly asOf, Coach? coach,
        IDictionary<Guid, CaptaincyProfile> profiles, Random random)
    {
        bool international = team.IsNational;
        double coachJudge = coach is null
            ? 0.5
            : Math.Clamp((AbilityScale.AttributeToHundred(coach.Attributes.DecisionMaking)
                          + AbilityScale.AttributeToHundred(coach.Attributes.ManManagement)) / 200.0, 0.2, 0.95);

        var groups = team.CaptaincyPattern switch
        {
            CaptaincyPattern.Unified => new[] { new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 } },
            CaptaincyPattern.RedBallWhiteBall => new[] { new[] { MatchFormat.Test }, new[] { MatchFormat.ODI, MatchFormat.T20 } },
            CaptaincyPattern.LongFormShortForm => new[] { new[] { MatchFormat.Test, MatchFormat.ODI }, new[] { MatchFormat.T20 } },
            _ => new[] { new[] { MatchFormat.Test }, new[] { MatchFormat.ODI }, new[] { MatchFormat.T20 } }
        };

        var results = new List<(MatchFormat, CaptaincyChoice)>();
        CaptaincyProfile? Profile(Guid id) => profiles.TryGetValue(id, out var p) ? p : null;

        foreach (var formats in groups)
        {
            var reference = formats[0];
            var candidates = EligibleCandidates(squad, reference, asOf, international);
            if (candidates.Count == 0) continue;

            var scored = candidates
                .Select(p => (Player: p, Score: formats.Average(f => CaptaincyScore(p, f, Profile(p.Id), asOf, international))))
                .OrderByDescending(x => x.Score)
                .ToList();

            // A good judge picks the best; a poor one occasionally slips to the runner-up.
            var pick = scored.Count > 1 && random.NextDouble() > coachJudge
                ? scored[1].Player
                : scored[0].Player;

            var vice = scored.FirstOrDefault(x => x.Player.Id != pick.Id).Player;

            if (!profiles.ContainsKey(pick.Id)) profiles[pick.Id] = new CaptaincyProfile { PlayerId = pick.Id };
            if (vice is not null && !profiles.ContainsKey(vice.Id)) profiles[vice.Id] = new CaptaincyProfile { PlayerId = vice.Id };

            string scope = formats.Length == 3 ? "all formats"
                : string.Join("/", formats.Select(f => f.ToString()));
            string why = pick.Personality.HasFlag(PersonalityTrait.Leader)
                ? $"a natural leader who is worth his place - {team.Name}'s {scope} captain"
                : $"the strongest leadership option in the side - {team.Name}'s {scope} captain";
            var choice = new CaptaincyChoice(pick.Id, pick.FullName, vice?.Id, Math.Round(scored[0].Score, 1), why);

            foreach (var f in formats)
            {
                var previous = team.GetCaptain(f);
                team.SetCaptain(f, pick.Id);
                if (vice is not null) team.ViceCaptainsByFormat[f] = vice.Id;
                if (previous is { } prev && prev != pick.Id)
                    ResetCaptainTrust(squad); // section D + carry-forward: a change of captain is a fresh start
                results.Add((f, choice));
            }
        }

        return results;
    }

    /// <summary>Does the captaincy for this format need a fresh appointment - the incumbent has retired, left, is out long-term, or has led badly for a sustained run?</summary>
    public bool NeedsReview(Team team, MatchFormat format, IReadOnlyList<Player> squad, DateOnly asOf, IReadOnlyDictionary<Guid, CaptaincyProfile> profiles)
    {
        var currentId = team.GetCaptain(format);
        var current = currentId is { } id ? squad.FirstOrDefault(p => p.Id == id) : null;
        if (current is null || current.IsRetired || current.RetiredFormats.Contains(format) || current.CurrentTeamId != team.Id)
            return true;
        if (current.CurrentInjury is { } inj && inj.IsActiveOn(asOf) && inj.Severity >= InjurySeverity.Serious)
            return true;

        // A sustained run of poor leadership - low dressing-room backing AND a low squad trust.
        var profile = profiles.GetValueOrDefault(currentId!.Value);
        double backing = profile?.DressingRoomBacking ?? 50;
        double squadTrust = CaptaincyService.SquadCaptainTrust(squad.Where(p => p.CurrentTeamId == team.Id).ToList());
        return backing < 28 && squadTrust < 35;
    }

    /// <summary>
    /// The captain's genuine input into a squad: players he particularly rates - ones he has a
    /// strong shared record with (partnership / matchup history) or high-ceiling young players he
    /// wants developed. NOT a veto - the caller (Head Coach) weighs it. For a human coach it is
    /// surfaced as a view to consider; for an AI coach it is a modest bump.
    /// </summary>
    public IReadOnlyList<(Guid PlayerId, string Reason)> CaptainSquadInput(
        Player captain, IReadOnlyList<Player> squadCandidates, MatchFormat format, DateOnly asOf)
    {
        var input = new List<(Guid, string)>();
        foreach (var p in squadCandidates)
        {
            if (p.Id == captain.Id) continue;

            // A strong shared batting partnership record with the captain.
            if (captain.Matchups.TryGetValue(MatchupKey.ForPartner(p.Id), out var partner)
                && partner.SampleCount >= 3 && partner.BlendedConfidence > 25)
            {
                input.Add((p.Id, $"the captain rates his partnership with {p.FullName}"));
                continue;
            }

            // A young player the captain rates - genuine ceiling, and the captain is a senior voice.
            bool captainIsSenior = _room.RoleOf(captain, asOf) is DressingRoomRole.SeniorPro or DressingRoomRole.Established;
            if (captainIsSenior && p.Age(asOf) <= 24 && p.PotentialAbility - p.CurrentAbility >= 20
                && AbilityScale.AttributeToHundred(captain.Mental.GameAwareness) >= 55)
            {
                input.Add((p.Id, $"the captain has pushed for young {p.FullName} to be given a look"));
            }
        }
        return input.Take(3).ToList();
    }

    private static void ResetCaptainTrust(IReadOnlyList<Player> squad)
    {
        foreach (var p in squad)
            p.CaptainTrust += (50 - p.CaptainTrust) * 0.6; // a fresh start, not a total wipe
    }
}
