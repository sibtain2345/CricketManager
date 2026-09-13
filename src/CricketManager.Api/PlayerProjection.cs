using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.Services;

namespace CricketManager.Api;

/// <summary>
/// Turns a <see cref="Player"/> (plus the <see cref="WorldState"/> it lives in) into the DTOs the
/// frontend actually receives - the one place a squad row or a player profile is assembled, so the
/// qualitative-projection discipline (design decision 3c) is applied consistently rather than
/// re-derived per endpoint.
/// </summary>
public static class PlayerProjection
{
    private static readonly CareerStatsService CareerStats = new();

    public static SquadRowDto ToSquadRow(Player player, WorldState world, DateOnly asOf)
    {
        var (abilityTier, abilityPercent) = QualitativeProjectionService.AbilityTier(player.CurrentAbility);
        var (formTier, formPercent) = QualitativeProjectionService.FormTier(player.Form.CurrentForm);
        var (sharpnessTier, sharpnessPercent) = QualitativeProjectionService.SharpnessTier(player.MatchSharpness);

        return new SquadRowDto(
            player.Id, player.FullName, RoleLabel(player), player.BattingHand.ToString(),
            player.BowlingStyle == BowlingStyle.None ? null : player.BowlingStyle.ToString(),
            player.Age(asOf), player.SquadStatus.ToString(),
            abilityTier, abilityPercent, formTier, formPercent, sharpnessTier, sharpnessPercent,
            player.CurrentInjury is not null, player.CurrentInjury?.Type.ToString());
    }

    public static PlayerProfileDto ToProfile(
        Player player, WorldState world, Team? team, DateOnly asOf, IReadOnlyList<PlayerContract> contracts, Random random)
    {
        var (abilityTier, _) = QualitativeProjectionService.AbilityTier(player.CurrentAbility);
        var scoutingQuality = team?.Facilities.ScoutingQuality ?? 30;
        var (stars, note) = QualitativeProjectionService.PotentialBand(player.PotentialAbility, scoutingQuality, random);
        var (formTier, _) = QualitativeProjectionService.FormTier(player.Form.CurrentForm);

        var personalityTags = Enum.GetValues<PersonalityTrait>()
            .Where(t => t != PersonalityTrait.None && player.Personality.HasFlag(t))
            .Select(t => t.ToString())
            .ToList();

        var careerRows = new[] { MatchFormat.Test, MatchFormat.ODI, MatchFormat.T20 }
            .Select(f => CareerStats.For(world, player.Id, f))
            .Where(s => s is not null && (s.Innings > 0 || s.BowlingInnings > 0))
            .Select(s => new CareerStatsRowDto(
                s!.Format.ToString(), s.Matches, s.Innings, s.Runs, s.BattingAverage, s.StrikeRate,
                s.Hundreds, s.Fifties, s.OversBowled, s.Wickets, s.BowlingAverage, s.Economy))
            .ToList();

        var activeContract = contracts
            .Where(c => c.PlayerId == player.Id && c.Status == ContractStatus.Active)
            .OrderByDescending(c => c.EndDate)
            .FirstOrDefault();
        ContractSummaryDto? contractDto = activeContract is null ? null : new ContractSummaryDto(
            activeContract.AnnualWage / 52.0, activeContract.EndDate, activeContract.MonthsRemaining(asOf),
            activeContract.IsHomegrown, activeContract.ReleaseClauseValue is not null);

        var matchupNotes = new List<MatchupNoteDto>();
        if (team?.HomeGroundId is { } groundId
            && player.Matchups.TryGetValue(MatchupKey.ForGround(groundId), out var groundForm)
            && groundForm.SampleCount >= 3)
        {
            var blended = groundForm.BlendedConfidence;
            if (blended >= 20) matchupNotes.Add(new MatchupNoteDto("At his home ground", "Thrives"));
            else if (blended <= -20) matchupNotes.Add(new MatchupNoteDto("At his home ground", "Struggles"));
        }

        return new PlayerProfileDto(
            player.Id, player.FullName, RoleLabel(player), player.BattingHand.ToString(),
            player.BowlingStyle == BowlingStyle.None ? null : player.BowlingStyle.ToString(),
            player.DateOfBirth, player.Age(asOf), player.Nationality, player.SquadStatus.ToString(),
            abilityTier,
            new PotentialBandDto(stars, note),
            AttributeBars(player.Batting), AttributeBars(player.Bowling), AttributeBars(player.Fielding),
            AttributeBars(player.Mental), AttributeBars(player.Physical),
            personalityTags, formTier,
            // No genuine per-innings form history is tracked in WorldState today (FormState exposes
            // only the current scalar) - an honest empty series rather than a fabricated trend.
            // Extending this needs a real Domain addition (a rolling recent-ratings history), not an
            // API-layer fake.
            Array.Empty<double>(),
            careerRows, contractDto, matchupNotes);
    }

    private static string RoleLabel(Player player) => player.PrimaryRole switch
    {
        PlayerRole.WicketKeeper => $"Wicketkeeper ({player.BattingRole})",
        PlayerRole.Bowler => $"{player.BowlingRole} bowler",
        PlayerRole.BowlingAllrounder => "Bowling allrounder",
        PlayerRole.BattingAllrounder => "Batting allrounder",
        _ => player.BattingRole.ToString()
    };

    private static IReadOnlyList<AttributeBarDto> AttributeBars<T>(T attributes) where T : notnull
    {
        return typeof(T).GetProperties()
            .Where(p => p.PropertyType == typeof(int))
            .Select(p =>
            {
                var value = (int)p.GetValue(attributes)!;
                return new AttributeBarDto(SplitPascalCase(p.Name), value, QualitativeProjectionService.AttributeTier(value));
            })
            .ToList();
    }

    private static string SplitPascalCase(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");
}
