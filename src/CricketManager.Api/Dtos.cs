namespace CricketManager.Api;

// ==================== Game lifecycle ====================

public sealed record NewGameRequest(
    string SaveDirectory, int? Seed, DateOnly? StartDate,
    IReadOnlyList<string>? Countries, int TeamsPerCountry = 4, int SquadSize = 14, bool Force = false);

public sealed record LoadGameRequest(string SaveDirectory);

public sealed record AdvanceRequest(int? Days, int? Weeks, DateOnly? To);

public sealed record GameSummaryDto(
    Guid SessionId, DateOnly CurrentDate, int WorldSeed,
    int TeamCount, int ActivePlayerCount, int RetiredPlayerCount,
    int CompetitionCount, int SeasonsCompleted);

public sealed record AdvanceResponseDto(
    DateOnly FromDate, DateOnly ToDate, int TotalEvents,
    IReadOnlyList<string> Headlines);

// ==================== Teams / fixtures ====================

public sealed record TeamSummaryDto(Guid Id, string Name, string Country, bool IsNational, bool IsFranchise);

// ==================== Global search ====================

/// <summary>One matched entity for the persistent top-bar search - a player or a team, so far (staff can be added the same way once a Staff screen exists to open into).</summary>
public sealed record SearchResultDto(Guid Id, string Kind, string Label, string SubLabel);

public sealed record FixtureSummaryDto(
    Guid Id, string HomeTeamName, string AwayTeamName, DateOnly Date,
    string CompetitionName, string Format, string Status);

// ==================== Squad ====================

public sealed record SquadRowDto(
    Guid PlayerId, string FullName, string Role, string BattingHand, string? BowlingStyle,
    int Age, string SquadStatusLabel,
    string AbilityTier, int AbilityBarPercent,
    string FormTier, int FormBarPercent,
    string SharpnessTier, int SharpnessBarPercent,
    bool IsInjured, string? InjuryNote);

// ==================== Player profile ====================

public sealed record AttributeBarDto(string Name, int Value1To20, string Tier);

public sealed record PotentialBandDto(int Stars1To5, string Note);

public sealed record CareerStatsRowDto(
    string BucketLabel, int Matches, int Innings, int Runs, double BattingAverage, double StrikeRate,
    int Hundreds, int Fifties, double BowlingOvers, int Wickets, double BowlingAverage, double Economy);

public sealed record ContractSummaryDto(
    double WeeklyWage, DateOnly EndDate, double MonthsRemaining, bool IsHomegrown, bool HasReleaseClause);

public sealed record MatchupNoteDto(string Label, string Tier);

public sealed record PlayerProfileDto(
    Guid Id, string FullName, string Role, string BattingHand, string? BowlingStyle,
    DateOnly DateOfBirth, int Age, string Nationality, string SquadStatusLabel,
    string AbilityTier,
    PotentialBandDto Potential,
    IReadOnlyList<AttributeBarDto> BattingAttributes,
    IReadOnlyList<AttributeBarDto> BowlingAttributes,
    IReadOnlyList<AttributeBarDto> FieldingAttributes,
    IReadOnlyList<AttributeBarDto> MentalAttributes,
    IReadOnlyList<AttributeBarDto> PhysicalAttributes,
    IReadOnlyList<string> PersonalityTags,
    string FormTier,
    IReadOnlyList<double> FormSparkline,
    IReadOnlyList<CareerStatsRowDto> CareerStats,
    ContractSummaryDto? Contract,
    IReadOnlyList<MatchupNoteDto> NotableMatchups);
