namespace CricketManager.Domain.ValueObjects;

/// <summary>Section 82: every coach (player-controlled or AI) shares this same profile shape.</summary>
public sealed class CoachAttributes
{
    // Coaching
    public int BattingCoaching { get; set; } = 10;
    public int BowlingCoaching { get; set; } = 10;
    public int FieldingCoaching { get; set; } = 10;
    public int FitnessCoaching { get; set; } = 10;
    public int TacticalKnowledge { get; set; } = 10;
    public int MatchPreparation { get; set; } = 10;
    public int PlayerDevelopment { get; set; } = 10;
    public int YouthDevelopment { get; set; } = 10;
    public int TechnicalKnowledge { get; set; } = 10;

    // Mental / Management
    public int Leadership { get; set; } = 10;
    public int Motivation { get; set; } = 10;
    public int Discipline { get; set; } = 10;
    public int PlayerManagement { get; set; } = 10;
    public int MediaHandling { get; set; } = 10;
    public int ManManagement { get; set; } = 10;
    public int Adaptability { get; set; } = 10;
    public int PressureHandling { get; set; } = 10;
    public int DecisionMaking { get; set; } = 10;

    // Analytical
    public int OppositionAnalysis { get; set; } = 10;
    public int Statistics { get; set; } = 10;
    public int Scouting { get; set; } = 10;
    public int TacticalAnalysis { get; set; } = 10;
    public int MatchReading { get; set; } = 10;

    // Professional
    public int Professionalism { get; set; } = 10;
    public int Ambition { get; set; } = 10;
    public int Loyalty { get; set; } = 10;
    public int WorkEthic { get; set; } = 10;
}
