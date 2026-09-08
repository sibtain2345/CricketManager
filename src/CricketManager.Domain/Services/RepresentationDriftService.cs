using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Seven-suggestions pass (S6): opportunity-driven changes of international allegiance.
///
/// Real rules (verified against the ICC Player Eligibility Regulations): a player qualifies for a
/// nation by BIRTH, CITIZENSHIP, or 3 consecutive years' RESIDENCY. After his last International
/// Match for federation X he must serve a **3-year stand-down** before he can qualify for
/// federation Y - EXCEPT a player moving from an ASSOCIATE federation to a FULL MEMBER, who serves
/// **zero** stand-down. Real examples: Moises Henriques (Australia -> Portugal), Joe Burns
/// (Australia -> Italy), Boyd Rankin (Ireland -> England -> Ireland, once Ireland gained Test
/// status - a genuine reversal).
///
/// This absorbs the thin nationality-switch that used to live in SkillRegressionService. It is
/// RARE and produces a NAMED-storyline-flavoured news line. Two trigger paths:
/// <list type="bullet">
/// <item><b>Opportunity</b> - a player good enough for international cricket who is genuinely out
///       of his home nation's plans, and has an eligible bigger (or any) stage elsewhere.</item>
/// <item><b>Bitterness</b> - a proven player dropped despite real recent form; an <c>Ambitious</c>
///       or aggrieved temperament reaches a breaking point.</item>
/// </list>
/// Personality shapes the outcome: <c>Loyal</c> players often retire uncapped rather than switch
/// (a legitimate, poignant ending); <c>Ambitious</c> players chase the opportunity hardest.
/// A switch is REVERSIBLE only in the specific Rankin case - a player whose former nation later
/// gains Test status (S7) and who is not established at his current nation.
///
/// Uses its own date-seeded Random, like SkillRegressionService, so it never perturbs the shared
/// annual RNG stream.
/// </summary>
public sealed class RepresentationDriftService
{
    public const int StandDownYears = 3;

    public IEnumerable<GameEvent> ReviewAnnually(WorldState world, DateOnly date)
    {
        var random = new Random(date.Year * 733 + 41);
        var events = new List<GameEvent>();

        var nationalByCountry = world.Teams.Values
            .Where(t => t.IsNational && !string.IsNullOrEmpty(t.Country))
            .GroupBy(t => t.Country, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
        if (nationalByCountry.Count == 0) return events;

        // playerId -> the set of countries whose national pool he currently sits in.
        var inPool = new Dictionary<Guid, HashSet<string>>();
        foreach (var pool in world.NationalPools)
        {
            var country = nationalByCountry.FirstOrDefault(kv => kv.Value.Id == pool.NationalTeamId).Key;
            if (country is null) continue;
            foreach (var e in pool.Entries)
            {
                if (!inPool.TryGetValue(e.PlayerId, out var set))
                    inPool[e.PlayerId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(country);
            }
        }

        MembershipStatus MembershipOf(string nation) =>
            world.CountryProfiles.TryGetValue(nation, out var p) ? p.Membership : MembershipStatus.FullMember;

        // Stable order (never by Guid) - the date-seeded Random is consumed per player.
        foreach (var p in world.Players
                     .OrderBy(p => p.LastName, StringComparer.Ordinal).ThenBy(p => p.FirstName, StringComparer.Ordinal).ThenBy(p => p.DateOfBirth))
        {
            if (p.IsRetired || p.AcademyTeamId is not null) continue;
            int age = p.Age(date);
            if (age is < 21 or > 35) continue;

            string home = p.Nationality;
            if (!nationalByCountry.ContainsKey(home)) continue;

            bool inHomePlans = inPool.TryGetValue(p.Id, out var poolCountries) && poolCountries.Contains(home);
            double continental = p.Reputation.Continental;
            bool goodEnough = continental >= 40 || p.Reputation.Domestic >= 68;

            // --- eligible targets ---
            var targets = new List<string>();
            var candidateNations = new List<string>(p.HeritageNations);
            // residency route: a long career at a club in another country.
            if (p.CurrentTeamId is { } tid && world.Teams.TryGetValue(tid, out var club)
                && !string.IsNullOrEmpty(club.Country)
                && !string.Equals(club.Country, home, StringComparison.OrdinalIgnoreCase))
                candidateNations.Add(club.Country);
            // the Rankin reversal: a former nation that has since become a Test nation.
            if (p.FormerNationality is { } former && MembershipOf(former) == MembershipStatus.FullMember)
                candidateNations.Add(former);

            bool capped = p.Experience.InternationalMatches > 0;
            var lastIntl = p.Experience.LastInternationalDate;
            // Stand-down: if capped and either the date is unknown (assume recent) or within 3 years,
            // he cannot switch yet - UNLESS home is an Associate and the target is a Full Member.
            bool servedStandDown = !capped
                || (lastIntl is { } d && d <= date.AddYears(-StandDownYears));

            foreach (var nation in candidateNations.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(nation, home, StringComparison.OrdinalIgnoreCase)) continue;
                if (!nationalByCountry.ContainsKey(nation)) continue;
                bool zeroStandDown = MembershipOf(home) == MembershipStatus.Associate && MembershipOf(nation) == MembershipStatus.FullMember;
                if (servedStandDown || zeroStandDown) targets.Add(nation);
            }
            if (targets.Count == 0) continue;

            // --- trigger ---
            bool ambitious = p.Personality.HasFlag(PersonalityTrait.Ambitious);
            bool loyal = p.Personality.HasFlag(PersonalityTrait.Loyal);
            bool aggrieved = p.Personality.HasFlag(PersonalityTrait.Aggressive) || p.Mental.Professionalism < 9;

            double chance = 0;
            string flavour;

            // Path A - opportunity.
            if (goodEnough && !inHomePlans)
            {
                chance = 0.04;
                if (ambitious) chance += 0.05;
                // a bigger stage: an Associate player with a Full-Member target.
                bool biggerStage = targets.Any(t => MembershipOf(t) == MembershipStatus.FullMember)
                                   && MembershipOf(home) == MembershipStatus.Associate;
                if (biggerStage) chance += 0.10;
                flavour = biggerStage
                    ? "a genuine shot at Test cricket he was never going to get at home"
                    : "regular international cricket he cannot get in his own country's set-up";
            }
            else
            {
                flavour = "";
            }

            // Path B - bitterness. A proven player dropped despite real form.
            bool bitternessCase = capped && p.Experience.InternationalMatches >= 5
                                  && p.Form.CurrentForm >= 62 && !inHomePlans && aggrieved;
            if (bitternessCase)
            {
                chance = Math.Max(chance, 0.11);
                flavour = "a breaking point - overlooked despite the runs, and done waiting";
            }

            if (loyal) chance *= 0.35;
            if (chance <= 0 || random.NextDouble() >= chance) continue;

            var target = targets[random.Next(targets.Count)];

            // --- outcome ---
            if (loyal && !bitternessCase && random.NextDouble() < 0.45)
            {
                // Loyalty wins - he ends his career rather than represent another nation.
                p.IsRetired = true;
                p.RetirementDate = date;
                events.Add(new GameEvent(date, GameEventType.PlayerRetired,
                    $"{p.FullName} retires rather than switch allegiance - eligible for {target}, out of favour at home, but he will only ever have played for {home}.",
                    p.Id));
                continue;
            }

            bool isReversal = string.Equals(target, p.FormerNationality, StringComparison.OrdinalIgnoreCase);
            if (!p.HeritageNations.Contains(home, StringComparer.OrdinalIgnoreCase)) p.HeritageNations.Add(home);
            p.FormerNationality = home;
            p.Nationality = target;
            events.Add(new GameEvent(date, GameEventType.RepresentationSwitch,
                isReversal
                    ? $"{p.FullName} switches his allegiance BACK to {target} now that they are a Test nation - the Rankin move in reverse."
                    : $"{p.FullName} switches his international allegiance from {home} to {target} - {flavour}.",
                p.Id));
        }

        return events;
    }
}
