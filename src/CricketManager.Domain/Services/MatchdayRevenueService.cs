using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>One match's crowd and gate outcome. FillRate is kept alongside Attendance because a near-sellout at a small ground is a different commercial story from a half-empty large one.</summary>
public sealed record MatchdayResult(int Attendance, double FillRate, double Revenue);

/// <summary>
/// Attendance is derived, never hand-set. Previously Ground.Capacity and
/// TeamFinances.MatchdayIncomeRate both existed with nothing reading them, which is exactly
/// the "meaningless database field" the Phase 3 brief said not to create.
///
/// What actually drives a crowd here: how big a deal the competition is (structural
/// prestige blended with its current earned reputation), how much of a draw the venue
/// itself is, and how good the two sides are. Revenue is then attendance x per-head yield,
/// where the ground's corporate/hospitality quality moves the yield - the same 40,000
/// people are worth more at a venue that can actually sell them hospitality.
/// </summary>
public sealed class MatchdayRevenueService
{
    /// <summary>
    /// Expected attendance for one match at this ground. Returns 0 for a ground with no
    /// capacity set rather than inventing a crowd.
    /// </summary>
    /// <param name="homeFanSentiment">
    /// Phase 7, Slice 7.3: the home fanbase's mood (ClubBoard.FanSentiment), 0-100, 50 neutral
    /// and the default (every pre-Phase-7 caller). An unhappy fanbase stays away; a buzzing one
    /// fills the ground - a small multiplier (0.85x-1.15x) on the fill rate, which is what makes
    /// the board -> fans -> attendance -> revenue -> board loop actually close.
    /// </param>
    public MatchdayResult CalculateMatchday(
        Ground ground,
        Competition competition,
        double homeTeamStrength,
        double awayTeamStrength,
        double matchdayIncomeBaseline,
        double homeFanSentiment = 50)
    {
        if (ground.Capacity <= 0) return new MatchdayResult(0, 0, 0);

        // Prestige is the stable structural signal; reputation is the current media profile.
        double competitionStanding = Math.Clamp(competition.Prestige, 0, 100) * 0.6
                                   + Math.Clamp(competition.Reputation, 0, 100) * 0.4;

        double venueDraw = Math.Clamp(ground.Reputation, 0, 100);
        double contestQuality = (Math.Clamp(homeTeamStrength, 0, 100) + Math.Clamp(awayTeamStrength, 0, 100)) / 2.0;

        // Competition standing is a MULTIPLICATIVE gate, not just one additive term among
        // three: a second-XI fixture does not half-fill a Test venue simply because the
        // ground is famous and both sides are decent. How big the occasion is sets the
        // ceiling; venue draw and contest quality then move the crowd within that ceiling.
        double competitionPull = 0.15 + competitionStanding / 100.0 * 0.85;   // 0.15x - 1.0x
        double venueAndContest = 0.6 + (venueDraw * 0.4 + contestQuality * 0.6) / 100.0 * 0.5; // 0.6x - 1.1x

        // Phase 7: the home fanbase's mood scales turnout - angry fans stay away, a buzz fills grounds.
        double fanFactor = 0.85 + Math.Clamp(homeFanSentiment, 0, 100) / 100.0 * 0.3; // 0.85x-1.15x, neutral at 50

        // Even a low-appeal fixture draws a small core support; even the biggest fixture
        // rarely reports a literal 100% gate.
        double fillRate = Math.Clamp(competitionPull * venueAndContest * fanFactor, 0.02, 0.98);
        int attendance = (int)Math.Round(ground.Capacity * fillRate);

        double hospitalityYield = 0.8 + Math.Clamp(ground.Facilities.CorporateHospitality, 0, 100) / 100.0 * 0.6; // 0.8x - 1.4x
        const double BaseYieldPerAttendee = 4.0;
        double revenue = matchdayIncomeBaseline + attendance * BaseYieldPerAttendee * hospitalityYield;

        return new MatchdayResult(attendance, Math.Round(fillRate, 3), Math.Round(revenue, 0));
    }
}
