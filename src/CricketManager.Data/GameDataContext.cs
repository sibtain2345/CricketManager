using CricketManager.Data.Persistence;
using CricketManager.Data.Repositories;
using CricketManager.Domain.Entities;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Data;

/// <summary>
/// Bundles all repositories for one save game. Backed by <see cref="SqliteRepository{T}"/>
/// (one SQLite database, "game.db", inside the save directory) since the migration off
/// JsonRepository - see that class's doc comment for the storage shape and why. JsonRepository
/// itself is kept in the tree as a fallback/reference, not deleted, per the migration plan.
/// </summary>
public sealed class GameDataContext
{
    public IRepository<Player> Players { get; }
    public IRepository<Coach> Coaches { get; }
    public IRepository<Team> Teams { get; }
    public IRepository<CoachingContract> CoachingContracts { get; }
    /// <summary>
    /// NOT the live career-stats store. The running game writes and reads
    /// <c>WorldState.CareerStats</c> (a <c>Dictionary&lt;string, PlayerCareerStats&gt;</c> keyed
    /// "playerId:format") via <see cref="CricketManager.Domain.Services.CareerStatsService"/> -
    /// nothing syncs that dictionary to this repository. This repo exists as a serialization-test
    /// fixture (the private-setter / <c>[JsonInclude]</c> round-trip guard). Phase 17's
    /// <c>WorldStateStore</c> persists the dictionary as part of the whole <c>WorldState</c>
    /// document; when that lands this repo can be removed. Review §0.3.
    /// </summary>
    public IRepository<PlayerCareerStats> CareerStats { get; }
    public IRepository<BattingInningsRecord> BattingInnings { get; }
    public IRepository<BowlingSpellRecord> BowlingSpells { get; }
    public IRepository<Competition> Competitions { get; }
    public IRepository<CompetitionSeason> CompetitionSeasons { get; }
    public IRepository<Ground> Grounds { get; }
    public IRepository<TeamInningsRecord> TeamInnings { get; }
    public IRepository<FieldingRecord> FieldingRecords { get; }

    /// <summary>
    /// Added during the SQLite migration. PartnershipRecord (Phase 4, Slice 13) had an entity
    /// and a query service (PartnershipRecordsService) but was never given a repository here,
    /// so nothing could actually persist a partnership across a save/reload - the same
    /// "built but not wired up" shape as the mystery-files/dead-code lesson in CLAUDE.md.
    /// Found while adding the other repositories to this migration, not a new feature.
    /// </summary>
    public IRepository<PartnershipRecord> PartnershipRecords { get; }

    public IRepository<InfrastructureProject> InfrastructureProjects { get; }
    public IRepository<GameCalendar> Calendars { get; }
    public IRepository<StaffMember> Staff { get; }

    /// <summary>Added for fixture generation (Phase 4's last item). FixtureGenerationService/PlayoffBracketService produce these; nothing else in the game reads or writes them.</summary>
    public IRepository<Fixture> Fixtures { get; }

    /// <summary>Added for the squad-selection-and-playing-XI spec (post-Phase-4). SquadSelectionService/SquadSelectionAuthorityService/XiSelectionService produce and read these.</summary>
    public IRepository<SquadAnnouncement> SquadAnnouncements { get; }

    /// <summary>Phase 5 Part 2: the employment contract behind a hired backroom StaffMember - StaffMember itself has had a repository since Phase 4, but nothing tied one to a team with a real, persisted contract until now.</summary>
    public IRepository<StaffContract> StaffContracts { get; }

    /// <summary>Post-Phase-5 rectification pass, Wave 8: ICC-style ranking rows (one per team per format). RankingService produces and reads these.</summary>
    public IRepository<TeamRanking> TeamRankings { get; }

    /// <summary>Phase 6, Slice 6.1: team-to-team rivalries. Seeded (geographic/historical) and formed dynamically; read by FixturePlayService.</summary>
    public IRepository<Rivalry> Rivalries { get; }

    /// <summary>Post-Phase-6 (section A): open/closed job adverts. JobMarketService produces and resolves these.</summary>
    public IRepository<VacancyAdvert> Vacancies { get; }

    /// <summary>Post-Phase-6 (section E): national teams' per-format player pools. NationalPoolService produces and evolves these.</summary>
    public IRepository<NationalPool> NationalPools { get; }

    /// <summary>Phase 7, Slice 7.9: the world's umpire panel. UmpireService runs their careers; FixturePlayService assigns them to fixtures.</summary>
    public IRepository<Umpire> Umpires { get; }

    /// <summary>Phase 9, Slice 9.0: every player's employment contract (domestic + franchise). PlayerContractService / the transfer &amp; free-agent markets produce and read these; SeasonFinanceService sums the wages.</summary>
    public IRepository<PlayerContract> PlayerContracts { get; }

    /// <summary>Post-Phase-7/8/9 rectification (Sections B + C): per-country administrative + economic profile.</summary>
    public IRepository<CountryProfile> CountryProfiles { get; }

    public string SaveDirectory { get; }

    public GameDataContext(string saveDirectory)
    {
        SaveDirectory = saveDirectory;
        var dbPath = Path.Combine(saveDirectory, "game.db");

        Players = new SqliteRepository<Player>(dbPath);
        Coaches = new SqliteRepository<Coach>(dbPath);
        Teams = new SqliteRepository<Team>(dbPath);
        CoachingContracts = new SqliteRepository<CoachingContract>(dbPath);
        CareerStats = new SqliteRepository<PlayerCareerStats>(dbPath);
        BattingInnings = new SqliteRepository<BattingInningsRecord>(dbPath);
        BowlingSpells = new SqliteRepository<BowlingSpellRecord>(dbPath);
        Competitions = new SqliteRepository<Competition>(dbPath);
        CompetitionSeasons = new SqliteRepository<CompetitionSeason>(dbPath);
        Grounds = new SqliteRepository<Ground>(dbPath);
        TeamInnings = new SqliteRepository<TeamInningsRecord>(dbPath);
        FieldingRecords = new SqliteRepository<FieldingRecord>(dbPath);
        PartnershipRecords = new SqliteRepository<PartnershipRecord>(dbPath);
        InfrastructureProjects = new SqliteRepository<InfrastructureProject>(dbPath);
        Calendars = new SqliteRepository<GameCalendar>(dbPath);
        Staff = new SqliteRepository<StaffMember>(dbPath);
        Fixtures = new SqliteRepository<Fixture>(dbPath);
        SquadAnnouncements = new SqliteRepository<SquadAnnouncement>(dbPath);
        StaffContracts = new SqliteRepository<StaffContract>(dbPath);
        TeamRankings = new SqliteRepository<TeamRanking>(dbPath);
        Rivalries = new SqliteRepository<Rivalry>(dbPath);
        Vacancies = new SqliteRepository<VacancyAdvert>(dbPath);
        NationalPools = new SqliteRepository<NationalPool>(dbPath);
        Umpires = new SqliteRepository<Umpire>(dbPath);
        PlayerContracts = new SqliteRepository<PlayerContract>(dbPath);
        CountryProfiles = new SqliteRepository<CountryProfile>(dbPath);
    }

    public async Task SaveAllAsync()
    {
        await Players.SaveChangesAsync();
        await Coaches.SaveChangesAsync();
        await Teams.SaveChangesAsync();
        await CoachingContracts.SaveChangesAsync();
        await CareerStats.SaveChangesAsync();
        await BattingInnings.SaveChangesAsync();
        await BowlingSpells.SaveChangesAsync();
        await Competitions.SaveChangesAsync();
        await CompetitionSeasons.SaveChangesAsync();
        await Grounds.SaveChangesAsync();
        await TeamInnings.SaveChangesAsync();
        await FieldingRecords.SaveChangesAsync();
        await PartnershipRecords.SaveChangesAsync();
        await InfrastructureProjects.SaveChangesAsync();
        await Calendars.SaveChangesAsync();
        await Staff.SaveChangesAsync();
        await Fixtures.SaveChangesAsync();
        await SquadAnnouncements.SaveChangesAsync();
        await StaffContracts.SaveChangesAsync();
        await TeamRankings.SaveChangesAsync();
        await Rivalries.SaveChangesAsync();
        await Vacancies.SaveChangesAsync();
        await NationalPools.SaveChangesAsync();
        await Umpires.SaveChangesAsync();
        await PlayerContracts.SaveChangesAsync();
        await CountryProfiles.SaveChangesAsync();
    }
}
