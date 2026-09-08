using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>What the conditions say, ahead of a match. Toss-independent by design - it describes, it does not decide.</summary>
public sealed record PreMatchConditionsReport(
    string GroundName,
    string PitchSummary,
    string WeatherSummary,
    string WhatItFavours,
    int? ParScore,
    bool ParFromHistory,
    IReadOnlyList<string> Lines);

/// <summary>
/// Post-Phase-5 rectification pass, Wave 8 (point 14): a toss-independent pre-match conditions
/// report.
///
/// The existing toss logic (MatchSimulator.DecideToBat / MultiDayMatchSimulator.DecideToBat)
/// reads the conditions and produces a DECISION. This produces a DESCRIPTION - what the surface
/// looks like, what the weather is doing, what it all favours - and deliberately stops short of
/// "bat first". That call belongs to the captain and coach; the report just gives them (and a
/// human player, and a future UI/news layer) the same read the AI captain works from.
///
/// Reuses GroundConditionsService for the par score and spin projection rather than
/// re-deriving them.
/// </summary>
public sealed class PreMatchReportService
{
    private readonly GroundConditionsService _conditions = new();

    /// <param name="headToHead">
    /// Post-Phase-6 carry-forward: recent completed meetings between the two sides, most recent
    /// first, each as (winner id, or null for a no-result). Supplied by
    /// <see cref="HeadToHead"/>. When present, a "these two have met N times lately" line is added -
    /// pure presentation off data the fixture/result records already carry.
    /// </param>
    public PreMatchConditionsReport Build(
        Ground ground, MatchFormat format, MatchWeather? weather,
        IEnumerable<TeamInningsRecord>? groundHistory = null, int dayOfMatch = 1, bool eveningSession = false,
        PitchPreparation preparation = PitchPreparation.Neutral,
        (string HomeName, string AwayName, Guid HomeId, Guid AwayId, IReadOnlyList<Guid?> RecentMeetings)? headToHead = null,
        IReadOnlyList<string>? keyDuels = null)
    {
        var lines = new List<string>();

        // --- pitch ---
        double pace = ground.PitchPaceRating, spin = ground.PitchSpinRating, bounce = ground.PitchBounceRating;
        double batFriendly = ground.PitchBattingFriendliness;
        double projectedSpin = _conditions.EstimateSpinAssistance(ground, format, dayOfMatch, eveningSession);

        string pitchSummary = Describe(batFriendly, pace, spin, bounce);
        lines.Add($"Pitch: {pitchSummary}.");
        if (format == MatchFormat.Test && projectedSpin > spin + 8)
            lines.Add($"It is expected to take significant turn as the match wears on (day-{dayOfMatch} read: {projectedSpin:F0}/100 spin).");
        if (preparation != PitchPreparation.Neutral)
            lines.Add($"The groundstaff have prepared it {preparation.ToString().ToLowerInvariant()}.");
        if (ground.SquareBoundaryMetres <= 62 || ground.StraightBoundaryMetres <= 66)
            lines.Add("The boundaries are on the shorter side - a small ground where mishits carry.");
        else if (ground.SquareBoundaryMetres >= 72 && ground.StraightBoundaryMetres >= 78)
            lines.Add("A big outfield - runs will have to be worked, not slogged.");

        // --- weather ---
        string weatherSummary;
        if (weather is { } w)
        {
            weatherSummary = $"{w.TemperatureC:F0}°C, {(w.CloudCover >= 70 ? "heavy cloud" : w.CloudCover >= 40 ? "some cloud" : "clear skies")}, humidity {w.Humidity:F0}%";
            if (w.RainRisk >= 40) { weatherSummary += $", rain a real threat ({w.RainRisk:F0}%)"; lines.Add("Rain is forecast - time could be lost."); }
            if (w.CloudCover >= 65 && w.Humidity >= 55) lines.Add("Overcast and muggy - the ball should swing, especially early.");
            if (w.TemperatureC >= 33 && w.Humidity >= 60) lines.Add("Hot and humid - fielding sides will tire, and a long bat in this is hard work.");
            if (eveningSession && ground.HasFloodlights && ground.DewTendency >= 55)
                lines.Add("Dew is likely under lights - gripping the ball will get harder as the evening goes on.");
        }
        else
        {
            weatherSummary = "conditions unremarkable";
        }
        lines.Insert(1, $"Weather: {weatherSummary}.");

        // --- head to head (still not a toss call, just context) ---
        if (headToHead is { RecentMeetings.Count: > 0 } h2h)
        {
            int homeWins = h2h.RecentMeetings.Count(w => w == h2h.HomeId);
            int awayWins = h2h.RecentMeetings.Count(w => w == h2h.AwayId);
            int noResults = h2h.RecentMeetings.Count(w => w is null);
            string tail = noResults > 0 ? $", {noResults} without a result" : "";
            string edge = homeWins > awayWins ? $"{h2h.HomeName} have had the better of it"
                : awayWins > homeWins ? $"{h2h.AwayName} have had the better of it"
                : "honours have been even";
            lines.Add($"Head to head: their last {h2h.RecentMeetings.Count} meetings - {h2h.HomeName} {homeWins}, {h2h.AwayName} {awayWins}{tail}. {edge}.");
        }

        // --- Phase 16 (§15.4): the key personal duels + ground hoodoos ---
        if (keyDuels is { Count: > 0 })
            foreach (var duel in keyDuels.Take(3))
                lines.Add($"Watch: {duel}.");

        // --- what it favours (still not a toss call) ---
        string favours = FavoursText(batFriendly, pace, projectedSpin, bounce, weather, format);
        lines.Add($"On balance: {favours}.");

        // --- par ---
        int? par = null;
        bool parFromHistory = false;
        if (groundHistory is not null)
        {
            var estimate = _conditions.EstimateParScore(ground, format, groundHistory);
            par = estimate.ParScore;
            parFromHistory = estimate.FromHistoricalData;
            lines.Add($"Par first-innings score here: about {par}{(parFromHistory ? $" (from {estimate.InningsSampled} innings of history)" : " (estimated from the pitch profile)")}.");
        }

        return new PreMatchConditionsReport(ground.Name, pitchSummary, weatherSummary, favours, par, parFromHistory, lines);
    }

    /// <summary>
    /// Post-Phase-6 carry-forward: recent completed meetings between two teams, most recent first,
    /// as the winner id per match (null = no result). Built from the fixtures the world already
    /// has - no new tracking.
    /// </summary>
    public static IReadOnlyList<Guid?> HeadToHead(IEnumerable<Fixture> allFixtures, Guid teamA, Guid teamB, int take = 8)
    {
        return allFixtures
            .Where(f => f.Status == FixtureStatus.Completed
                        && ((f.HomeTeamId == teamA && f.AwayTeamId == teamB) || (f.HomeTeamId == teamB && f.AwayTeamId == teamA)))
            .OrderByDescending(f => f.ScheduledDate)
            .Take(take)
            .Select(f => f.WinningTeamId)
            .ToList();
    }

    /// <summary>
    /// Phase 16 (§15.4): the marquee personal duels for a fixture - a bowler who has a batter's
    /// number, read off the batter's own matchup history (bowler:{id} keys, a real sample, a
    /// clearly negative confidence). At most a couple, most in-form batter vs most threatening
    /// bowler first. Pure presentation off data the matchup system already keeps.
    /// </summary>
    /// <summary>
    /// Post-Phase-16 completion pass (§18.7): a batter with a genuine hoodoo at THIS ground - a
    /// real, sampled, clearly-negative record - read off his own `ground:{id}` matchup history.
    /// A narrative sub-arc, surfaced as a pre-match line; at most a couple.
    /// </summary>
    public static IReadOnlyList<string> GroundHoodoos(IReadOnlyList<Player> xi, Guid groundId, string groundName, int take = 2)
    {
        var hoodoos = new List<(double Severity, string Text)>();
        string key = $"ground:{groundId}";
        foreach (var p in xi)
        {
            if (!p.Matchups.TryGetValue(key, out var m) || m.SampleCount < 4) continue;
            double c = m.BlendedConfidence;
            if (c > -20) continue;
            hoodoos.Add((-c + m.SampleCount, $"{p.FullName} has never enjoyed {groundName} - {m.SampleCount} visits and rarely a score"));
        }
        return hoodoos.OrderByDescending(h => h.Severity).Select(h => h.Text).Take(take).ToList();
    }

    public static IReadOnlyList<string> KeyDuels(
        IReadOnlyList<Player> sideAxi, IReadOnlyList<Player> sideBxi, int take = 2)
    {
        var duels = new List<(double Severity, string Text)>();

        void Scan(IReadOnlyList<Player> batting, IReadOnlyList<Player> bowling)
        {
            foreach (var bat in batting)
                foreach (var bowl in bowling.Where(b => b.BowlingRole != Enums.BowlingRoleType.NotABowler))
                {
                    if (!bat.Matchups.TryGetValue($"bowler:{bowl.Id}", out var m) || m.SampleCount < 4) continue;
                    double c = m.BlendedConfidence;
                    if (c > -18) continue; // only a genuine hoodoo
                    duels.Add((-c + m.SampleCount, $"{bowl.FullName} has been {bat.FullName}'s bogey bowler - {m.SampleCount} meetings and rarely comfortable"));
                }
        }

        Scan(sideAxi, sideBxi);
        Scan(sideBxi, sideAxi);

        return duels.OrderByDescending(d => d.Severity).Select(d => d.Text).Take(take).ToList();
    }

    /// <summary>
    /// Post-Phase-6 carry-forward: a one-line, templated explanation of an AI captain's bat/bowl
    /// decision - the same signals MatchSimulator.DecideToBat / MultiDayMatchSimulator.DecideToBat
    /// weigh, put into words. Same lightweight templating the news engine uses.
    /// </summary>
    public string ExplainTossChoice(Ground ground, MatchFormat format, MatchWeather? weather, bool choseToBat, int dayOfMatch = 1)
    {
        double projectedSpin = _conditions.EstimateSpinAssistance(ground, format, dayOfMatch, eveningSession: false);
        bool deteriorating = format == MatchFormat.Test && projectedSpin > ground.PitchSpinRating + 8;
        bool dewRisk = ground.HasFloodlights && ground.DewTendency >= 55 && format != MatchFormat.Test;
        bool green = weather is { CloudCover: >= 65, Humidity: >= 55 } || ground.PitchPaceRating >= 68;

        if (choseToBat)
        {
            if (deteriorating) return "We'll bat - this surface will only get harder, so we want first use of it.";
            if (ground.PitchBattingFriendliness >= 66) return "We'll have a bat - it looks a good one and we want runs on the board.";
            return "We'll bat first and put a total in front of them.";
        }

        if (dewRisk) return "We'll bowl - the dew later means chasing should be the easier half of the day.";
        if (green) return "We'll have a bowl - there's enough in the surface and the air first up to make it worth it.";
        return "We'll bowl first and see how it behaves before we have to bat.";
    }

    private static string Describe(double batFriendly, double pace, double spin, double bounce)
    {
        string surface = batFriendly >= 68 ? "a road - excellent for batting"
            : batFriendly >= 52 ? "a good surface with something for everyone"
            : batFriendly >= 38 ? "a tricky one - the bowlers will fancy it"
            : "a minefield - survival will be an achievement";

        string extra = spin >= 65 ? ", and it is a real turner"
            : pace >= 65 && bounce >= 60 ? ", quick and bouncy"
            : pace <= 38 ? ", slow and low"
            : "";

        return surface + extra;
    }

    private static string FavoursText(double batFriendly, double pace, double projectedSpin, double bounce, MatchWeather? weather, MatchFormat format)
    {
        bool swinging = weather is { } w && w.CloudCover >= 65 && w.Humidity >= 55;
        if (projectedSpin >= 68) return "the spinners - a side with two good slow bowlers has a real edge";
        if (pace >= 65 && bounce >= 62) return "the quicks - genuine pace will be a weapon";
        if (swinging) return "the new-ball bowlers - the first hour could be decisive";
        if (batFriendly >= 66) return "the batters - big runs are on offer for whoever applies themselves";
        return "a balanced contest - the toss matters less than usual";
    }
}
