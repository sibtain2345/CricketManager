using CricketManager.Domain.Entities;

namespace CricketManager.Domain.Services;

/// <summary>
/// Meeting-driven-selection ticket, requirement A: the OTHER selection cadence, distinct from
/// <see cref="SelectionMeetingService"/>'s per-series squad meeting - the chairman of selectors and
/// his panel sit down to build/evolve the BROADER national pool (who's in the wider frame, what
/// gaps exist, what a coming tournament needs), not to pick one squad for one series.
///
/// This wraps the existing pool-building computation (<see cref="NationalPoolService.BuildPool"/> /
/// <see cref="NationalPoolService.ReviewPool"/>, run via <see cref="NationalSelectionService.RefreshPool"/>)
/// with a genuine meeting narrative rather than a silent refresh - reusing
/// <see cref="SelectionMeetingService.PanelQuality"/> for the SAME rigour reading a squad meeting
/// already uses, so a weak/political panel's pool meeting reads exactly as thin as its squad
/// meetings already do. It never re-derives the pool itself - the panel discusses what the refresh
/// already found, it doesn't invent a second computation.
///
/// Two cadences (both call <see cref="Hold"/>, distinguished by <c>preTournament</c>):
/// - the existing ANNUAL refresh, every national team, every year (WorldClockService.ProcessAnnualRollover);
/// - an EXTRA meeting once ahead of a major tournament (Competition.IsMajor), on the quarterly
///   tick, deduped per CompetitionSeason via WorldState.PreTournamentPoolMeetingsHeld so it never
///   fires more than once for the same tournament instance.
/// </summary>
public sealed class NationalPoolMeetingService
{
    public NationalPoolMeetingReport Hold(
        Team team, IReadOnlyList<string> refreshNotes, Coach? coach, IReadOnlyList<StaffMember> selectors,
        DateOnly date, bool preTournament = false, string? tournamentName = null)
    {
        double quality = SelectionMeetingService.PanelQuality(team, coach);

        var attendees = new List<string>();
        var chief = selectors.FirstOrDefault(s => s.Role == Enums.StaffRole.ChiefSelector);
        if (chief is not null) attendees.Add($"{chief.FullName} (chief selector)");
        attendees.AddRange(selectors.Where(s => s.Role == Enums.StaffRole.Selector).Select(s => s.FullName));
        if (coach is not null) attendees.Add($"{coach.FullName} (head coach)");

        string headline = preTournament
            ? $"{team.Name}'s selectors meet to shape the squad frame ahead of the {tournamentName ?? "tournament"}."
            : $"{team.Name}'s selection panel sits down to review the national pool.";

        // A weak/political panel's findings read thin even when real changes were made - the same
        // "picked on reputation and recent runs" honesty SelectionMeetingService already applies to
        // an individual pick, applied here to the panel's OWN account of its own work.
        var findings = quality >= 50 || refreshNotes.Count == 0
            ? refreshNotes
            : new List<string> { "The panel struggled to explain its own reasoning beyond reputation and recent runs." };

        // A genuinely fractious pool meeting - several changes made with no real panel consensus.
        string? dissent = quality < 40 && refreshNotes.Count >= 3
            ? "More than one selector is understood to have wanted a different shape to the pool."
            : null;

        return new NationalPoolMeetingReport(headline, attendees, findings, dissent, Math.Round(quality, 0), preTournament);
    }
}

/// <summary>Meeting-driven-selection ticket: the output of a national pool meeting - what SelectionMeetingReport already is, for the OTHER cadence.</summary>
public sealed record NationalPoolMeetingReport(
    string Headline,
    IReadOnlyList<string> Attendees,
    IReadOnlyList<string> Findings,
    string? DissentNote,
    double PanelQuality,
    bool PreTournament);
