using CricketManager.Domain.Entities;
using CricketManager.Domain.Enums;
using CricketManager.Domain.ValueObjects;

namespace CricketManager.Domain.Services;

/// <summary>
/// Phase 7, Slice 7.3: the board as a persistent actor.
///
/// Before this, Team.BoardConfidence moved on results and nothing else. This gives the board a
/// real profile (ClubBoard: ambition, patience, wealth, ownership, fan sentiment) and three jobs
/// that run at the annual rollover:
/// - <b>Set the coach a season budget</b> from wealth, last season's finances and ambition -
///   the number Phase 9's real transfer market will spend against.
/// - <b>Move fan sentiment</b> on results, trophies and finances - a slower number than
///   reputation that feeds patience and takeover interest.
/// - <b>Consider a takeover</b> - a club underachieving its market, or one with a restless owner,
///   can change hands, and the new owner's wealth / ambition / patience reshape what the coach
///   is judged against (its AmbitionTier can genuinely shift).
///
/// BoardRelationshipService (Slice 6.5) still owns the MONTHLY confidence movement; this folds a
/// financial-health term into that via <see cref="FinancialHealthFactor"/>.
/// </summary>
public sealed class BoardService
{
    private readonly CoachCareerService _coachCareer = new();

    /// <summary>
    /// The board sets (or revises) the coach's budget for the coming season. Driven by wealth,
    /// how the books actually look, and ambition - a wealthy, ambitious board backs a rebuild; a
    /// stretched one tightens the belt. Sets Board.SeasonBudget and returns a one-line summary.
    /// </summary>
    public string SetSeasonBudget(Team team, double closingBudget, double lastNetResult)
    {
        var board = team.Board;

        // A base allowance scaled by wealth, plus a slice of any surplus, minus a haircut on a loss.
        double baseAllowance = 150_000 + board.Wealth * 6_000;
        double surplusSlice = Math.Max(0, lastNetResult) * 0.5;
        double lossHaircut = Math.Max(0, -lastNetResult) * 0.7;
        double reserveFloor = Math.Min(0, closingBudget) * 0.8; // if the club is already in the red, the budget shrinks further

        // Phase 13 (§10.4): a club that TRADES well is trusted with more - a positive player-trading
        // P&L this year is added to the pot (and confidence); a poor one costs it. Then reset.
        double tradingBonus = Math.Clamp(team.PlayerTradingPnL, -2_000_000, 4_000_000) * 0.35;
        if (Math.Abs(team.PlayerTradingPnL) > 500_000)
            team.BoardConfidence = Math.Clamp(team.BoardConfidence + Math.Sign(team.PlayerTradingPnL) * 3, 0, 100);
        team.PlayerTradingPnL = 0;

        double budget = Math.Max(20_000, baseAllowance + surplusSlice - lossHaircut + reserveFloor + tradingBonus);

        // Phase 12 (§13.1/§13.2): the chairman's agenda and the owner's trajectory reshape the pot.
        double agendaMult = board.Agenda switch
        {
            ChairmanAgenda.WinNow => 1.30,
            ChairmanAgenda.Prestige => 1.15,
            ChairmanAgenda.Austerity => 0.72,
            _ => 1.0
        };
        double trajectoryMult = board.Trajectory switch
        {
            OwnerTrajectory.InvestingHeavily => 1.25,
            OwnerTrajectory.WindingDown => 0.80,
            OwnerTrajectory.SeekingSale => 0.88,
            _ => 1.0
        };
        // A divided board can't agree on a number - it swings around the mean.
        double unityMult = 0.85 + board.BoardUnity / 100.0 * 0.15;
        budget *= agendaMult * trajectoryMult * unityMult;

        // Ambition tilts the split toward the market; a youth agenda pulls it back toward wages/development.
        double share = 0.30 + board.Ambition / 100.0 * 0.35;
        if (board.Agenda == ChairmanAgenda.YouthAndAcademy) share -= 0.12;
        if (board.Agenda == ChairmanAgenda.Prestige) share += 0.10;
        board.TransferBudgetShare = Math.Clamp(share, 0.20, 0.75);
        board.SeasonBudget = Math.Round(Math.Max(20_000, budget), 0);

        return $"{team.Name}'s board set a season budget of {board.SeasonBudget:N0} " +
               $"({board.TransferBudgetShare:P0} for recruitment).";
    }

    /// <summary>Moves the fanbase's mood on how the season went and how the club is run. Slow - a few points a season.</summary>
    public void ReviewFanSentiment(Team team, double performanceScore, bool wonTitle, double netResult)
    {
        var board = team.Board;
        double delta = performanceScore * 5.0;                       // overachieving lifts the fans, underachieving sours them
        if (wonTitle) delta += 8;
        if (netResult < -200_000) delta -= 4;                        // a club visibly losing money worries its fans
        else if (netResult > 300_000) delta += 1.5;

        // Fans of an elite club are harder to please and quicker to turn.
        if (_coachCareer.AmbitionTier(team) == CoachCareerService.TeamAmbitionTier.Elite && delta < 0)
            delta *= 1.4;

        board.MoveFanSentiment(delta);
    }

    /// <summary>
    /// A -1..~0.4 factor BoardRelationshipService folds into its monthly confidence target: a
    /// club haemorrhaging money loses the board's confidence in the coach regardless of results;
    /// a healthy one gets a small benefit of the doubt.
    /// </summary>
    public double FinancialHealthFactor(Team team)
    {
        double budget = team.Finances.Budget;
        if (budget < -500_000) return -0.35;
        if (budget < 0) return -0.15;
        if (budget > 2_000_000) return 0.10;
        return 0;
    }

    /// <summary>
    /// Considers whether this club changes hands this year. Rare by design. A takeover is most
    /// likely for a club with a genuine market (domestic reputation) that is underachieving it
    /// (low board confidence / fan sentiment) and is actually for sale (ownership model). It
    /// reshapes the board - usually wealthier and more ambitious, always less patient - and gives
    /// the club a small reputation bump (a takeover is a statement of intent).
    /// </summary>
    public GameEvent? ConsiderTakeover(Team team, DateOnly date, Random random)
    {
        var board = team.Board;

        // Member-owned clubs and associations are not for sale.
        if (board.Ownership is OwnershipModel.MemberOwned or OwnershipModel.Association) return null;

        double marketAppeal = Math.Clamp(team.Reputation.Domestic, 0, 100) / 100.0;      // a bigger name is a more attractive buy
        double underachievement = Math.Clamp((60 - team.BoardConfidence) / 60.0, 0, 1);   // the board is failing to realise the club's potential
        double restlessFans = Math.Clamp((50 - board.FanSentiment) / 50.0, 0, 1);

        double chance = Math.Clamp(0.015 + marketAppeal * underachievement * 0.10 + restlessFans * 0.04, 0, 0.16);
        if (random.NextDouble() >= chance) return null;

        var oldOwnership = board.Ownership;
        // A wealthier, more ambitious, less patient new owner - occasionally a corporate buyer.
        board.Ownership = random.NextDouble() < 0.4 ? OwnershipModel.Corporate : OwnershipModel.PrivateOwner;
        board.Wealth = Math.Clamp(board.Wealth + 15 + random.NextDouble() * 25, 0, 100);
        board.Ambition = Math.Clamp(board.Ambition + 10 + random.NextDouble() * 20, 0, 100);
        board.Patience = Math.Clamp(board.Patience - 8 - random.NextDouble() * 12, 10, 100);
        board.MoveFanSentiment(12); // the initial bounce of new investment
        team.Reputation.Adjust(domesticDelta: 3 + random.NextDouble() * 4, continentalDelta: 1.5);

        return new GameEvent(date, GameEventType.ClubTakeover,
            $"{team.Name} have been taken over - a {Describe(board.Ownership)} steps in, promising real investment.",
            team.Id);
    }

    private static string Describe(OwnershipModel model) => model switch
    {
        OwnershipModel.Corporate => "corporate group",
        OwnershipModel.PrivateOwner => "wealthy private owner",
        OwnershipModel.Association => "governing association",
        _ => "supporters' trust"
    };

    /// <summary>
    /// Phase 12 (§13.1/§13.2/§13.6): the annual boardroom review - the arc a board is on, not just
    /// its mood this month. It moves BoardUnity on how the season went and how the books look,
    /// drifts the owner's Wealth along the Trajectory (a private/corporate owner only), and can
    /// produce a governance EVENT: a boardroom coup that installs a new chairman with a different
    /// agenda when the board has been badly split, a fresh cash injection when an owner steps up
    /// investment, or a move onto the market when interest is genuinely waning. RNG for the events;
    /// the drift is deterministic.
    /// </summary>
    public IEnumerable<GameEvent> ReviewBoardroom(Team team, DateOnly date, double performanceScore, double netResult, Random random)
    {
        var board = team.Board;
        if (board.Ownership is OwnershipModel.MemberOwned or OwnershipModel.Association)
        {
            // A members' club still has a committee that can fall out - but never an owner arc.
            board.BoardUnity = Math.Clamp(board.BoardUnity + performanceScore * 6 + (netResult < -300_000 ? -4 : 0), 15, 100);
            yield break;
        }

        // Unity: results and finances pull it, and a low-patience owner is quicker to fracture.
        double unityDelta = performanceScore * 8 - board.Impatience() * 1.5;
        if (netResult < -400_000) unityDelta -= 6;
        else if (netResult > 400_000) unityDelta += 3;
        board.BoardUnity = Math.Clamp(board.BoardUnity + unityDelta, 10, 100);

        // Owner trajectory: the wealth number drifts, and the trajectory itself can flip.
        double drift = board.Trajectory switch
        {
            OwnerTrajectory.InvestingHeavily => 4.5,
            OwnerTrajectory.WindingDown => -4.0,
            OwnerTrajectory.SeekingSale => -2.0,
            _ => 0
        };
        board.Wealth = Math.Clamp(board.Wealth + drift, 5, 100);

        // A sustained fall in results/interest turns a stable owner toward the exit; a good run and
        // a wealthy owner turns him toward investing.
        if (board.Trajectory == OwnerTrajectory.Stable)
        {
            if (performanceScore < -0.2 && board.FanSentiment < 42 && random.NextDouble() < 0.18)
            {
                board.Trajectory = OwnerTrajectory.WindingDown;
                yield return new GameEvent(date, GameEventType.BoardConfidenceShift,
                    $"{team.Name}'s owner is understood to have cooled on the project - investment is being scaled back.", team.Id);
            }
            else if (performanceScore > 0.25 && board.Wealth > 60 && random.NextDouble() < 0.14)
            {
                board.Trajectory = OwnerTrajectory.InvestingHeavily;
                yield return new GameEvent(date, GameEventType.ClubTakeover,
                    $"{team.Name}'s owner has committed fresh money - a real push is coming.", team.Id);
            }
        }
        else if (board.Trajectory == OwnerTrajectory.WindingDown && random.NextDouble() < 0.30)
        {
            board.Trajectory = OwnerTrajectory.SeekingSale;
            yield return new GameEvent(date, GameEventType.BoardConfidenceShift,
                $"{team.Name} are quietly on the market - the owner wants out.", team.Id);
        }

        // A boardroom coup: a badly split board changes its chairman, and the new one brings a
        // different agenda. Only once things are genuinely fractured.
        if (board.BoardUnity <= 24 && random.NextDouble() < 0.35)
        {
            var oldAgenda = board.Agenda;
            var agendas = new[] { ChairmanAgenda.Balanced, ChairmanAgenda.WinNow, ChairmanAgenda.YouthAndAcademy, ChairmanAgenda.Austerity, ChairmanAgenda.Prestige };
            board.Agenda = agendas[random.Next(agendas.Length)];
            board.BoardUnity = 55;
            board.Patience = Math.Clamp(board.Patience + random.Next(-10, 12), 15, 100);
            if (board.Agenda != oldAgenda)
                yield return new GameEvent(date, GameEventType.BoardroomCoup,
                    $"{team.Name} have a new chairman after a boardroom split - the mandate now is {AgendaWord(board.Agenda)}.", team.Id);
        }
    }

    private static string AgendaWord(ChairmanAgenda a) => a switch
    {
        ChairmanAgenda.WinNow => "trophies, and quickly",
        ChairmanAgenda.YouthAndAcademy => "building from the academy up",
        ChairmanAgenda.Austerity => "balancing the books",
        ChairmanAgenda.Prestige => "profile, marquee names and a bigger fanbase",
        _ => "a steady hand"
    };
}
