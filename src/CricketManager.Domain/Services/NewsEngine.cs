using CricketManager.Domain.Enums;

namespace CricketManager.Domain.Services;

/// <summary>Where a piece of news belongs - a UI/inbox can route and a digest can group by this.</summary>
public enum NewsCategory
{
    Result,
    Transfer,      // contracts, hirings, dismissals, resignations, renewals
    Injury,
    Board,         // objectives, retention offers, board judgment
    Development,   // training breakthroughs, redemptions, mentoring
    Competition,   // windows opening/closing, seasons scheduled, new grounds
    Rankings,
    Finance,       // Phase 7: season accounts, broadcast money, takeovers, budgets
    Media,         // Phase 7: press conferences, pundit opinion
    Milestones,    // Phase 7: career landmarks, awards, hall of fame, records
    Discipline,    // Phase 7: fines, bans, over-rate penalties
    Other
}

/// <summary>One rendered news item - a GameEvent turned into something a reader sees.</summary>
public sealed record NewsItem(
    DateOnly Date,
    NewsCategory Category,
    /// <summary>0-100 - how prominently this should feature. A dismissal or a title outranks a contract renewal.</summary>
    int Prominence,
    string Kicker,
    string Headline);

/// <summary>The week's news, grouped and ordered.</summary>
public sealed record WeeklyDigest(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    IReadOnlyList<NewsItem> Lead,                              // the handful that lead the bulletin
    IReadOnlyDictionary<NewsCategory, IReadOnlyList<NewsItem>> ByCategory,
    int TotalItems)
{
    public bool IsQuietWeek => TotalItems == 0;
}

/// <summary>
/// Post-Phase-5 rectification pass, Wave 8 (points 17-18): a template-based news engine, and the
/// weekly inbox digest that aggregates over it.
///
/// Every GameEvent this codebase already produces carries a human sentence (`Headline`). This
/// layer classifies each one, assigns it a category and a prominence, and wraps it with a short
/// kicker - the difference between a raw event log and something that reads like a bulletin.
/// Deliberately does NOT invent new facts or rewrite the sentences the events already carry -
/// the same "grounded, not manufactured" discipline PostMatchAnalysisService follows.
/// </summary>
public sealed class NewsEngine
{
    public NewsItem Render(GameEvent ev)
    {
        var (category, prominence, kicker) = Classify(ev.Type);
        return new NewsItem(ev.Date, category, prominence, kicker, ev.Headline);
    }

    /// <summary>Phase 7, Slice 7.5: renders an event for the persisted archive, keeping the subject ids so a reader can filter "news about my club / my players".</summary>
    public ValueObjects.NewsArchiveItem RenderArchive(GameEvent ev)
    {
        var (category, prominence, kicker) = Classify(ev.Type);
        return new ValueObjects.NewsArchiveItem(ev.Date, category, prominence, kicker, ev.Headline, ev.SubjectId, ev.SecondarySubjectId);
    }

    public IReadOnlyList<NewsItem> Render(IEnumerable<GameEvent> events) =>
        events.Select(Render).OrderByDescending(n => n.Prominence).ThenByDescending(n => n.Date).ToList();

    /// <summary>Aggregates a window of events into a digest - the lead items first, then everything grouped by category.</summary>
    public WeeklyDigest Digest(IEnumerable<GameEvent> events, DateOnly weekStart, DateOnly weekEnd, int leadCount = 4)
    {
        var items = Render(events.Where(e => e.Date >= weekStart && e.Date <= weekEnd));

        var lead = items.Take(leadCount).ToList();
        var byCategory = items
            .GroupBy(i => i.Category)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NewsItem>)g.OrderByDescending(i => i.Prominence).ToList());

        return new WeeklyDigest(weekStart, weekEnd, lead, byCategory, items.Count);
    }

    private static (NewsCategory Category, int Prominence, string Kicker) Classify(GameEventType type) => type switch
    {
        GameEventType.MatchCompleted => (NewsCategory.Result, 55, "Result"),
        GameEventType.CompetitionSeasonCreated => (NewsCategory.Competition, 25, "Fixtures"),
        GameEventType.CompetitionWindowOpened => (NewsCategory.Competition, 35, "Under way"),
        GameEventType.CompetitionWindowClosed => (NewsCategory.Competition, 40, "Wrapped up"),
        GameEventType.NewGroundOpened => (NewsCategory.Competition, 45, "New venue"),
        GameEventType.InfrastructureCompleted => (NewsCategory.Competition, 20, "Facilities"),

        GameEventType.InjuryOccurred => (NewsCategory.Injury, 50, "Injury blow"),
        GameEventType.PlayerRecovered => (NewsCategory.Injury, 35, "Back fit"),

        GameEventType.PlayerRetired => (NewsCategory.Transfer, 65, "Curtain call"),
        GameEventType.ContractExpired => (NewsCategory.Transfer, 30, "Contract"),
        GameEventType.CoachContractRenewed => (NewsCategory.Transfer, 40, "Staying put"),
        GameEventType.StaffContractRenewed => (NewsCategory.Transfer, 25, "Backroom"),
        GameEventType.CoachDismissed => (NewsCategory.Transfer, 80, "Sacked"),
        GameEventType.CoachResigned => (NewsCategory.Transfer, 70, "Walks away"),
        GameEventType.StaffResigned => (NewsCategory.Transfer, 30, "Backroom exit"),
        GameEventType.CoachAppointedInterim => (NewsCategory.Transfer, 55, "In charge"),
        GameEventType.CoachPromotedToPermanent => (NewsCategory.Transfer, 60, "Gets the job"),
        GameEventType.CoachRetentionOffer => (NewsCategory.Board, 55, "Fought off"),
        GameEventType.StaffPromotedToHeadCoach => (NewsCategory.Transfer, 55, "Steps up"),

        GameEventType.SquadDecisionMade => (NewsCategory.Board, 45, "Selection"),
        GameEventType.BoardObjectivesReviewed => (NewsCategory.Board, 50, "Board verdict"),

        GameEventType.PlayerDeveloped => (NewsCategory.Development, 30, "On the rise"),

        // ---- Phase 7 ----
        GameEventType.TeamFinancesSettled => (NewsCategory.Finance, 20, "The accounts"),
        GameEventType.BroadcastRevenuePaid => (NewsCategory.Finance, 25, "TV money"),
        GameEventType.CompetitionReputationShift => (NewsCategory.Finance, 22, "Standing"),
        GameEventType.ClubTakeover => (NewsCategory.Finance, 75, "New owners"),
        GameEventType.BoardroomCoup => (NewsCategory.Finance, 62, "Boardroom split"),
        GameEventType.SeasonBudgetSet => (NewsCategory.Finance, 30, "War chest"),
        GameEventType.PressConference => (NewsCategory.Media, 35, "Facing the media"),
        GameEventType.PunditOpinion => (NewsCategory.Media, 25, "The verdict"),
        GameEventType.PlayerMilestone => (NewsCategory.Milestones, 45, "Landmark"),
        GameEventType.SeasonAward => (NewsCategory.Milestones, 55, "Awards"),
        GameEventType.HallOfFameInduction => (NewsCategory.Milestones, 70, "Immortalised"),
        GameEventType.RecordBroken => (NewsCategory.Milestones, 60, "Into the book"),
        GameEventType.DisciplinaryAction => (NewsCategory.Discipline, 50, "Code of conduct"),
        GameEventType.UmpiringControversy => (NewsCategory.Discipline, 55, "Officiating"),
        GameEventType.PitchRatedPoor => (NewsCategory.Discipline, 48, "Pitch report"),
        GameEventType.SelectionPanelNote => (NewsCategory.Board, 42, "Selectors"),

        // ---- follow-up: franchise finance, auction media, national coaching, agents, awards ----
        GameEventType.FranchiseFinancesSettled => (NewsCategory.Finance, 22, "Franchise accounts"),
        GameEventType.FranchisePrizeMoney => (NewsCategory.Finance, 28, "Prize money"),
        GameEventType.AuctionPreview => (NewsCategory.Competition, 48, "Auction build-up"),
        GameEventType.AuctionReport => (NewsCategory.Competition, 55, "Auction round-up"),
        GameEventType.AuctionPressConference => (NewsCategory.Media, 40, "Auction presser"),
        GameEventType.PreAuctionMeeting => (NewsCategory.Competition, 42, "In the war room"),
        GameEventType.EoiConversionMeeting => (NewsCategory.Competition, 44, "Setting the list"),
        GameEventType.PostAuctionReview => (NewsCategory.Competition, 40, "Taking stock"),
        GameEventType.FranchiseIdentityShift => (NewsCategory.Transfer, 46, "A change of direction"),
        GameEventType.StandingStatusDigest => (NewsCategory.Other, 26, "The state of things"),
        GameEventType.ReservePlayerRetained => (NewsCategory.Transfer, 25, "One for the future"),
        GameEventType.NationalCoachAppointed => (NewsCategory.Transfer, 72, "National job"),
        GameEventType.NationalCoachDismissed => (NewsCategory.Transfer, 82, "National sacking"),
        GameEventType.AgentBiddingWar => (NewsCategory.Transfer, 60, "Bidding war"),
        GameEventType.TournamentAward => (NewsCategory.Milestones, 50, "Tournament award"),
        GameEventType.MatchPreview => (NewsCategory.Result, 30, "Match preview"),
        GameEventType.MatchStory => (NewsCategory.Result, 42, "Match story"),
        GameEventType.TrophyContested => (NewsCategory.Result, 78, "Trophy on the line"),
        GameEventType.CentralContractAwarded => (NewsCategory.Transfer, 55, "Central contract"),
        GameEventType.NationalBoardVerdict => (NewsCategory.Result, 74, "Board verdict"),
        GameEventType.NocDenied => (NewsCategory.Transfer, 66, "NOC denied"),
        GameEventType.StaffRecommendationIssued => (NewsCategory.Other, 28, "Staff recommendation"),
        GameEventType.SquadAnnounced => (NewsCategory.Result, 50, "Squad announced"),
        GameEventType.NationalPoolMeeting => (NewsCategory.Result, 44, "Selection panel"),

        GameEventType.NarrativeUpdate => (NewsCategory.Media, 58, "The story"),
        GameEventType.Leaderboard => (NewsCategory.Result, 34, "Leaderboard"),
        GameEventType.TeamOfTheTournament => (NewsCategory.Milestones, 56, "Team of the tournament"),
        GameEventType.FanReaction => (NewsCategory.Media, 30, "Fan reaction"),
        GameEventType.CoachPhilosophyDrift => (NewsCategory.Other, 22, "A new approach"),
        GameEventType.FranchiseTrade => (NewsCategory.Transfer, 62, "Trade"),
        GameEventType.ContractHoldout => (NewsCategory.Transfer, 64, "Holdout"),
        GameEventType.ForcedSale => (NewsCategory.Transfer, 68, "Fire sale"),
        GameEventType.SellOnClausePaid => (NewsCategory.Finance, 40, "Sell-on"),
        GameEventType.TransferDeadlineDay => (NewsCategory.Transfer, 60, "Deadline day"),
        GameEventType.PlayerRelationship => (NewsCategory.Media, 26, "Dressing room"),
        GameEventType.OffFieldEvent => (NewsCategory.Media, 44, "Off the field"),
        GameEventType.PersonalityDevelopment => (NewsCategory.Other, 24, "Growing up"),
        GameEventType.TechnicalFlaw => (NewsCategory.Other, 26, "Under the microscope"),

        GameEventType.MilestoneCeremony => (NewsCategory.Milestones, 58, "A guard of honour"),
        GameEventType.AllTimeXiNamed => (NewsCategory.Milestones, 52, "Team of the era"),
        GameEventType.GoldenGeneration => (NewsCategory.Development, 46, "A golden generation"),
        GameEventType.TestimonialTour => (NewsCategory.Milestones, 62, "A fond farewell"),

        GameEventType.IccRevenueDistributed => (NewsCategory.Finance, 44, "ICC money"),
        GameEventType.IccFullMembershipGranted => (NewsCategory.Board, 88, "A new Test nation"),
        GameEventType.RepresentationSwitch => (NewsCategory.Transfer, 58, "Switching allegiance"),
        GameEventType.PlayerCareerAfterCricket => (NewsCategory.Transfer, 40, "Life after cricket"),
        GameEventType.CoachingStructureChanged => (NewsCategory.Board, 60, "Coaching shake-up"),
        GameEventType.OwnershipGroupMove => (NewsCategory.Transfer, 42, "Group move"),

        GameEventType.SeasonRollover => (NewsCategory.Other, 10, "New year"),
        _ => (NewsCategory.Other, 15, "In brief")
    };
}
