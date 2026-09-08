using System.Text.Json.Serialization;
using CricketManager.Domain.Enums;

namespace CricketManager.Domain.ValueObjects;

/// <summary>
/// What a player has actually been through, as opposed to how old they are or how good they
/// are. These are three genuinely different things and the game was previously modelling only
/// two of them.
///
/// This is the gap behind "a young player with high current ability and high potential still
/// struggles in certain situations, while a 35-year-old who is past his physical peak can
/// still deliver": ability says what he can do on a good day, experience says whether he has
/// been here before. A 30-year-old with twenty first-class games is NOT experienced, and no
/// age-derived shortcut can express that - which is exactly why this is counted, not computed
/// from a birthday.
///
/// Weighted deliberately: an international appearance is worth more than a domestic one, and a
/// knockout/final is worth more again, because that is where the pressure that builds a player
/// actually lives.
/// </summary>
public sealed class PlayerExperience
{
    [JsonInclude] public Dictionary<MatchFormat, int> MatchesByFormat { get; private set; } = new();
    [JsonInclude] public int InternationalMatches { get; private set; }

    /// <summary>Finals, knockouts, World Cup matches - the games where inexperience actually shows.</summary>
    [JsonInclude] public int BigMatchAppearances { get; private set; }

    [JsonInclude] public int SeasonsPlayed { get; private set; }
    [JsonInclude] public DateOnly? DebutDate { get; private set; }

    /// <summary>Seven-suggestions pass (S6): the date of the player's most recent INTERNATIONAL appearance - the clock the 3-year ICC stand-down for a change of allegiance is measured from. Null = never capped.</summary>
    [JsonInclude] public DateOnly? LastInternationalDate { get; private set; }

    [JsonIgnore] public int TotalMatches => MatchesByFormat.Values.Sum();

    public int MatchesIn(MatchFormat format) => MatchesByFormat.TryGetValue(format, out var n) ? n : 0;

    /// <summary>
    /// 0-100. Deliberately a saturating curve, not linear: the difference between 5 and 40
    /// career matches is enormous, the difference between 200 and 240 is almost nothing. A
    /// linear count would make a 15-year veteran twice as "experienced" as a 7-year one, which
    /// is not how cricket works - most of what experience teaches is learned early.
    /// </summary>
    [JsonIgnore]
    public double Level
    {
        get
        {
            double weighted = TotalMatches + InternationalMatches * 2.0 + BigMatchAppearances * 3.0;
            return Math.Round((1 - Math.Exp(-weighted / 120.0)) * 100, 1);
        }
    }

    /// <summary>Format-specific experience. A player with 80 Tests and 3 T20s is a novice in T20 regardless of his overall standing, and selection should be able to see that.</summary>
    public double LevelInFormat(MatchFormat format)
    {
        double weighted = MatchesIn(format) + InternationalMatches * 0.5 + BigMatchAppearances * 1.5;
        return Math.Round((1 - Math.Exp(-weighted / 60.0)) * 100, 1);
    }

    public void RecordAppearance(MatchFormat format, DateOnly date, bool isInternational = false, bool isBigMatch = false)
    {
        MatchesByFormat[format] = MatchesIn(format) + 1;
        if (isInternational) { InternationalMatches++; LastInternationalDate = date; }
        if (isBigMatch) BigMatchAppearances++;
        DebutDate ??= date;
    }

    public void RecordSeasonPlayed() => SeasonsPlayed++;

    /// <summary>
    /// Sets a career already in progress. This is how a real-world snapshot is loaded: a player
    /// imported at the June 2026 start date arrives with the career he has actually had, not as
    /// a blank slate who happens to be 33.
    /// </summary>
    public static PlayerExperience FromExistingCareer(
        IDictionary<MatchFormat, int> matchesByFormat,
        int internationalMatches = 0,
        int bigMatchAppearances = 0,
        int seasonsPlayed = 0,
        DateOnly? debutDate = null) => new()
        {
            MatchesByFormat = new Dictionary<MatchFormat, int>(matchesByFormat),
            InternationalMatches = internationalMatches,
            BigMatchAppearances = bigMatchAppearances,
            SeasonsPlayed = seasonsPlayed,
            DebutDate = debutDate
        };
}
