# Cricket Coaching Management Simulator

A Football Manager-depth cricket coaching/management sim (2D match presentation later,
deep simulation underneath), built in phases per the living spec. See `CLAUDE.md` for
architecture decisions, phase status, and session-continuity notes - read that first if
you're picking this project up in a new session.

## Stack

- **.NET 10**, C#
- **Domain-driven layering**: `CricketManager.Domain` (entities, value objects, pure game
  logic - zero external dependencies) → `CricketManager.Data` (persistence, seeding) →
  future UI layer (not built yet)
- **Persistence**: SQLite (`Microsoft.Data.Sqlite`), one `game.db` per save directory,
  behind `IRepository<T>` - see "Why JSON instead of SQLite" below for the migration and
  the storage shape actually used (JSON-column hybrid, not full relational mapping).
- **Tests**: a small dependency-free custom test runner (`tests/CricketManager.Tests`),
  NOT xUnit/NUnit - see "Why not xUnit" below.

## Where the project is

| Phase | Scope | Status |
|---|---|---|
| 1 | Core architecture, domain model, persistence | Complete |
| 2 | Player database, attributes, form, stats, records | Complete |
| 3 | Teams, competitions, grounds, facilities, finances | Complete |
| 3b | Ground statistics, honour boards, conditions | Complete |
| 3c | Experience, ageing curves, retirement, world clock | Complete |
| 4 | Match simulation | **Complete** - slices 1-15 |
| 5 | Coaching systems | **Complete** - training, backroom staff hiring/market/renewal, board objectives, plus the full 8-wave post-Phase-5 rectification pass, and a four-level ICC code of conduct + campaign-based franchise coaching in the post-7/8/9 pass (see below) |
| 6 | AI managers & world simulation | **Complete** - slices 6.1-6.7 (season engine, rivalries, home advantage, AI club brain, competition lifecycle + promotion/relegation, in-match tactical AI, in-season board relationship, fixture disruption/dead-rubbers, international selection & national teams). See `CLAUDE.md` "PHASE 6" |
| Post-6 | Rectifications: two-directional job market, staff-role roster, format-specific captaincy, national pools, workload-driven rotation | **Complete** - see `CLAUDE.md` "POST-PHASE-6 RECTIFICATIONS" |
| 7 | Board, Media & Finance depth | **First pass complete** - slices 7.1-7.9 (running finance loop, broadcast money + competition-reputation movement, the board as a persistent actor with takeovers, financial fair play, fan-driven attendance, press conferences + pundits, the news/inbox layer, milestones & awards, discipline/bans/over-rate fines, a records-progression database + Hall of Fame, a national-board layer, and umpiring with full umpire careers - **no DRS**). See `CLAUDE.md` "PHASE 7" |
| 8 | Training & Player Development | **Complete** - slices 8.0-8.7 (the youth academy pipeline: annual intake scaled by youth facilities + a nation-talent skew, scouting-driven promotion/release with real misjudgement; youth-accelerated development; individual peak-timing variation + emergent age-driven role drift + a permanent cost from a career-threatening injury; the weekly training-calendar modulation; the ecosystem loops - retiring players become coaches, sustained overperformance raises a ceiling, coaching-badge progression, development loans). Closes tech-debt items 1 and 5. See `CLAUDE.md` "PHASE 8" |
| 9 | Auction / Contracts / Market | **Complete** - slices 9.0-9.8 (a real `PlayerContract` model + real wages into the finance loop, contract renewal/expiry + free agency, the transfer market with valuation driven by contract length, transfer windows, board sanction and release clauses, transfer requests + unsettled players + a light agent layer, franchise leagues + a turn-by-turn auction + overseas-player slots, a proper loan market with fees / wage split / option + obligation to buy, contract depth - loyalty bonuses + testimonials, a world market-index inflating fees and wages over a long sim, and the three-tier training camps). See `CLAUDE.md` "PHASE 9" |
| Post-7/8/9 | Combined rectification / tuning pass + follow-up | **Complete** - a real 4-level ICC code of conduct (fines as a % of the match fee, a 4-demerit-point rolling-24-month suspension, coaches chargeable at press conferences) + richer umpire careers; per-country administrative/economic profiles (board-controlled vs club-membership ownership, hemisphere-keyed transfer windows, cross-border domestic transfers, an economic-scale multiplier on fees); **five** franchise leagues (IPL/PSL/CPL/SA20/BBL, non-overlapping windows) with the auction rebuilt as a genuine strategic fight (interest-voting shortlist, fixed base-price brackets a player self-selects, ordered sets, a strict bid staircase, pre-auction plans + adaptive purse-pressure bidding, retention + Right-to-Match + a reserve list + a mega-vs-mini cadence, a transparency read-model, auction previews / round-ups / press conferences); **loss-proof** franchise finances + prize money; campaign-based franchise coaching. Plus the follow-up realism items - the corrected DOMESTIC overseas rule (4 in the squad / 2 in the XI / >=1 associate, with seeded associate nations), medical & analyst staff-quality wiring, regional weather, player-agent bidding wars, a national-coach career track, and emerging-player-of-the-tournament awards. See `CLAUDE.md` -> "POST-PHASE-7/8/9 RECTIFICATIONS" + "FOLLOW-UP PASS". |
| NOW | Post-Phase-9 Wiring & Tech-Debt Pass | **Complete (588 tests)** - `AiTacticalPlanner` + the analyst / pre-match / post-match / commentary layer wired into the live match (`MatchPreview` / `MatchStory` news, own fixture-keyed RNG); the `Rate*` rating functions rewritten with a `MatchSituation` layer (occasion, clutch, game-impact, result); `CareerStats` two-store split documented; `TestRunner --filter` / `--trace`; recorder RNG seeded from the match date; head-coach salaries into the wage bill; session-level in-match momentum. See `CLAUDE.md`. |
| 10 | World Simulation & International Cricket | **Complete** - a `Country` talent / home-conditions layer on `CountryProfile` (drives academy cohorts + pitch character); a WTC-style Test Championship + ODI Championship + six named bilateral trophy series (the Ashes, Border-Gavaskar, …) with `Rivalry` trophies that change hands; central-contract tiers (A/B/C) + NOC denial before a clashing franchise auction (**international always wins the clash**); tour acclimatisation; the national-coach market + a `NationalBoardVerdictService`; a 25-year longevity test; **a real 3-tier promotion/relegation chain, a 3-format domestic season per country, and associate nations as real teams with a qualifier**. See `CLAUDE.md`. |
| 11 | Depth: Planning, Tactics & Selection | **Core complete** - the unified `DelegationProfile` authority model (`DoItMyself` / `Consult` / `Delegate` per decision area; the human coach always has final say, delegation is explicit) + staff recommendations; the selection MEETING (`SelectionMeetingService` - a per-pick rationale, a surprise pick, a big omission, the captain's comment, a dissent note when the panel is weak) surfaced as a `SquadAnnounced` media event; the fuller XI combination problem (new-ball pair / death specialist / viable leader / left-right mix); `AiTacticalPlanner` full version (relative-strength read shaping Test + white-ball plans); `Mental.RunningCalling` → a run-out-risk floor. **A dedicated Match-Engine Tactical Pass then closed roughly half of the ~20 in-match micro-tactics (§2.x)** - a spinner turning away from the bat draws a real slip, captain quality scales the declaration/follow-on decisions, a sustained leg-theory barrage carries a real conduct cost, and more; a short, honestly-scoped remainder stays deferred. See `CLAUDE.md`. |
| 12 | Media, Narrative & Board/Coach Depth | **Complete** - per-nation media market (amplifies board-trust swings); `NarrativeService` (breakout star / captain-under-fire / crisis / dominant-run / redemption storylines feeding pressure moments, plus a personal ground hoodoo becoming a live storyline); live leaderboards + team of the tournament; pundit conflicts of interest; player media personas; coach worldwide earnings ledger + circuit reputation + franchise campaign fees; coaching-philosophy drift; `AllrounderLeaning`; the Director of Cricket; **the chairman's own agenda + the owner's trajectory shaping the season budget; coach-player relationships; a board objective beyond results; franchise-circuit assistant coaches and format-specialist assistants**. |
| 13 | Auction & Market Depth | **Complete** - franchise auction archetypes (Moneyball / Star-hunter / Youth-builder / Balanced); name-recognition overvaluation filtered by scouting-department quality; dynasty tracking; inter-franchise trades; sell-on / buy-back clauses; a player's dream club + `AmbitionDirection` colouring a move; forced sales on financial distress; player-trading P&L as a board KPI; transfer deadline day; **multi-year sponsorship deals and persistent player agents who push a client to several clubs and take a real cut; a real retention-negotiation round and a genuine accelerated-round value phase**. |
| 14 | Player Life, Relationships & Development Depth | **Core complete** - a player-to-player relationship graph (friendship / mentorship / feud → dressing-room harmony, "he'll sign if his mate is there"); personality development arcs; off-field life events; `AmbitionDirection`; confidence contagion; technical flaws + remediation (in-match exploitation frequency now real, closed by the Match-Engine Tactical Pass); skill regression from a one-format diet; early-career burnout. |
| 15 | Match Officiating, Conditions & Rare Events | **Complete** - **DRS** (`DecisionReviewService` - a batting-side review of a marginal LBW/caught-behind, on/off by competition scope, a weak panel's calls come back; a deterministic fielding-DRS clawback); per-competition `PlayingConditions` (DRS reviews, over-rate penalty severity, tie-break rule); the **Super Over**; umpire fatigue + a decider-howler board query; pitch doctoring as an AI choice with a backfire risk; drop-in pitches + a re-used strip worn from ball one; wind → swing and boundary asymmetry; the **concussion protocol** (a mandatory 6-day stand-down); **retired hurt (not a dismissal); a TV umpire without full DRS; the franchise Impact Player as a real 12-deep roster addition; post-rain per-over transition; footmark rough split by bowling arm**. "Timed out" removed from scope per the user. |
| 16 | Records, Awards & Historical Flavour | **Complete** - more record categories (most sixes, fastest 50/100, best economy, most catches in a match, most ducks); milestone ceremonies (`CeremonyService` - a 100th-cap guard of honour); head-to-head personal duels in the pre-match report and live commentary; an all-time / **team-of-the-era XI** every 5 years, now naming the dominant team and the era's defining storyline too; regen quality variance by nation and era (golden generations / fallow decades); a farewell / testimonial tour for a retiring one-club great; match referees running the code-of-conduct hearing. |
| Post-16 | Completion Pass (all remaining Phase 1-16 tails) | **Complete (627 tests)** - tech debt #6/#9 closed; contract holdouts; personal ground hoodoos; `NationalBoard.Ambition` → the coach verdict; central-contract retainer/NOC finance lines; day-night Tests + neutral-venue tours; the franchise circuit draining a domestic competition's reputation; multi-year `BroadcastDeal` objects; `FanReactionService` (+ protests); squad culture/churn; bench match-sharpness; membership + image-rights revenue; a coach's dream job; a salary cap; format comeback from retirement; nationality switches; academy poaching; all-ages burnout; a leadership pipeline; **match referees** running the hearing; most-catches-in-a-match / most-ducks records; head-to-head in live commentary; **feud → run-out** in-match; technical-flaw dismissal shape. See `CLAUDE.md` → "POST-PHASE-16 COMPLETION PASS". |
| Post-16-B | Deferred-Items Completion Sweep | **Complete** - the user's directive: nothing deferred against Phases 10-16 (including "15-adjacent", the Match-Engine Tactical Pass) carries into Phase 17. Six passes: Phase 10 domestic depth, Phase 12 board/coach/media, Phase 13 market depth, Phase 15 conditions & rare events, the Match-Engine Tactical Pass itself (roughly half the ~20 §2.x micro-tactics closed with real, tested mechanics - see `CLAUDE.md` for the honest short remainder), and Phase 16 historical flavour. Includes a real regression found, diagnosed and fixed (a situational field-aggression mechanic diluted the established "poor captaincy costs runs" signal even fully decorrelated from captain quality - reverted to an unwired, documented building block) and a real crash found and fixed (a `ToDictionary` merge across both XIs throwing on the rare case of a shared player). See `CLAUDE.md` → "PHASE 10-16 DEFERRED-ITEMS COMPLETION SWEEP". |
| Follow-up (post-sweep) | Closing the remaining Phase 10-16 deferred items, a dedicated 4-pass roadmap | **All four passes complete (661 tests)** - Pass 1 (coach/board authority & governance depth): the selection panel now genuinely favours reputation over merit when it is weak, a formal panel vote surfaces a real outvote, a pre-series planning conversation is surfaced ahead of a head-to-head series, and a boardroom coup is a distinct governance event that draws a real fan protest. Pass 2 (player development & squad depth): `Player.AssignedRole` role clarity, a standing cross-format workload plan, a "next in line" successor signal, training load as its own soft-tissue injury axis distinct from match-load burnout, and a club's real medical investment raising how much workload a player can carry. Pass 3 (market transparency): confirmed the live in-auction rival-purse-pressure bidding and the domestic prize-money news line were both already built. Pass 4 (historical depth + the match-engine tactical remainder): a deeper Team-of-the-Era model (more than one notable storyline, a rivalry of the era); confirmed the bowling-side "part-timer when collared" decision already emerges from the existing bowler-selection scoring; a genuinely new within-over bowling trap ("set the over up, finish it differently"); a finer T20 acceleration band ahead of the death overs; and "hiding the bunny" - a batting-order shuffle that protects a batter with a real, evidenced weak record against the bowler currently operating. Eight of the roadmap's twenty-odd items across all four passes turned out to be already built (register staleness, not missing functionality) - see `CLAUDE.md` -> "FOLLOW-UP PASSES (POST-SWEEP)". |
| Meeting Ticket | Meeting-Driven Selection/Auction, Franchise Identity Evolution, Cleanup | **Complete (673 tests)** - a large external directive worked through a mandatory Research → Plan → Implement gate. Stage 1: the Director of Cricket and the Impact Player rule both removed entirely (both turned out cheap and clean); the national selection panel restructured as staff under the coach, never an independent authority - new `Selector`/`ChiefSelector` staff roles, a `NationalPoolMeetingService` narrating the pool refresh as a genuine meeting, the per-series selection meeting made genuinely discretionary, and a repeated outvoted panel decision now costing real trust and raising a live storyline. Stage 2: franchise identity evolution (`FranchiseArchetype` drifts from results and from a new coach's philosophy); first-hand-knowledge auction weighting (`CareerTeamIds` - a coach/captain/core that has shared a dressing room with a player backs him harder); the pre-auction war-room + EOI-conversion + post-auction-review meeting trio; no fixed squad templates (archetype/identity reshapes role emphasis on top of the depth floor); needs-priority bidding order; and the generic N-tier promotion/relegation domestic-pyramid generator (built and unit-tested - **wiring it into the live seeded world was reverted and deferred**: it amplified a latent non-determinism over a long sim that could not be fully root-caused). A confirmed pre-existing determinism bug (`JobMarketService.GatherApplications` ordering candidates by Guid) was found and **fixed** along the way, along with a seed-stable tiebreak on the fixture-play sort. See `CLAUDE.md` → "MEETING-DRIVEN SELECTION TICKET". |
| Meeting Ticket — Corrections | Four corrections + a bug fix on the Meeting Ticket, plus a Section-H revisit | **Complete (680 tests, four consecutive clean runs)** - all worked through the same Research → Plan → Implement gate. (1) Auction set order is purely role/tier-driven again (the Stage-2 franchise-preference queue-jump was a misreading, reverted); a lost genuine must-have now promotes that role's plan-B for the rest of the auction. (2) A franchise builds its squad conditions-first: `RoleEmphasis` folds home-ground character + the previous campaign's weak point into one combined read on top of the depth floor; a new per-`Competition` `HomePitchInfluence` (≈0 for ICC events and franchise leagues, full for domestic first-class) gates home-pitch shaping - and a genuinely aggressive prep on a poorly-resourced square can now be rated poor (a quality floor, not a % cap). (3) **Franchise head coaches corrected to genuine year-round, multi-year `CoachingContract` employees** (verified: Dravid → RR) - hired *before* the auction so they are in the pre-auction war room, re-hired immediately on a sack, available across any domestic role but franchise-over-domestic on a genuine clash and exclusive for an international coach except a league in his own country. The Director of Cricket stays out **on purpose, not by omission**. (4) A national coach can call his own pool/selection meeting at any time, and a specific auto-triggered meeting can be skipped one-shot (`SkipNextSelectionMeetingFor`), distinct from the blanket toggle. Bug fix: `FirstHandKnowledgeService.ReadStrength` takes a player dictionary instead of a linear scan. See `CLAUDE.md` → "MEETING-DRIVEN SELECTION TICKET — CORRECTIONS PASS". |
| Seven-Suggestions | 7 review-process features + a cricket-personnel-careers subsystem | **Complete (691 tests)** - all seven built plus four user expansions, through the Research → Plan → Implement gate. **S5** an ICC annual revenue distribution (~$600M pool, the majors flattened, a growing associate pool) + a light national finance loop; **S7** an associate earns Test status on a sustained record - telegraphed, then granted, and **irrevocable** (ICC Article 2.7); **S6** opportunity- and bitterness-driven changes of international allegiance with the real 3-year stand-down and personality-shaped outcomes; **NEW-C** a retired player's second career - head coach / specialist coach / scout / national selector / mentor (legends only) / out of the game, routed by his playing pedigree and what he wants; **NEW-B** national selectors are now ex-cricketers only (a real caps threshold, retired 5+ years, the most-capped is chairman); **NEW-D** a skilled, willing head coach at a small side doubles up as his own batting or bowling coach; **S2** a national board splits its head-coach job across formats under clash pressure and reunifies when the coordination friction bites (the England 2022→2025 arc); **NEW-A** real year-round multi-league franchise staff contracts; **S1** multi-league franchise ownership groups; **S3** individual player commercial appeal; **S4** a match-fitness gate before an international recall. See `CLAUDE.md` → "SEVEN-SUGGESTIONS PASS". |
| 17 | Application, Persistence & UI | **Planned** - `WorldStateStore` (a save currently loses ~19 `WorldState` collections), a headless `CricketManager.App` game loop, build hygiene, then a graphical UI. |
| 18 | Real-World Data Import | **Planned** - real players/teams/competitions + the ICC FTP + Cricsheet history as a data swap; also the domestic draft league type. |

The full plan and a maintained deferred-items register live in `CLAUDE.md` -> "POST-PHASE-9 PLAN".
The underlying analysis is `Full_Project_Review_Gaps_And_Suggestions.md`.

**Phase 5 + post-Phase-5 rectification pass delivered:** a real training system (per-attribute
focus, staff/facility-driven quality, role conversion, an all-round development cap, overtraining
risk); individual backroom staff as people (recruitment shortlists, hiring, dismissal, tenure,
contracts, satisfaction and resignation); a coach job market with contract renewal decided on the
coach's own track record; structured, checkable board objectives per contract year; and then an
eight-wave rectification pass that moved almost every player/coach/staff/relationship system off
the once-a-year rollover onto the cadence it actually belongs on - training and match-experience
development monthly, form/morale/confidence/matchup decay monthly (and frozen while a player is
genuinely unavailable), partnership chemistry on its own faster clock, squad management quarterly,
a coach's tactical growth on campaign milestones, a captain's on a per-match accumulator - plus
dressing-room hierarchy and mentoring groups, in-match and team-form momentum, a multi-year
context-aware board-judgment layer (achievement leniency, the "one tenure, no title" clock,
team-appropriate ambition), and a presentation layer (toss-independent conditions report,
template news engine with a weekly digest, opposition-adjusted ICC-style rankings).

**Phase 4 slices delivered:** ball outcome model, innings simulation, full limited-overs match
with a real chase, world integration (records, standings, net run rate, form, reputation,
injuries), fielding positions and field settings, individual fielder quality (range vs hands,
impossible catches, boundary saving, misfields), coach-facing tactics, bowling plans with bowler
intelligence plus manual field placement, set-ness/confidence/fatigue and plan-fit scoring,
captaincy and the coach-captain relationship, decision authority (the human coach's final say),
analyst staff, weather and in-field recovery, multi-day cricket (four innings, declarations,
follow-on, a wearing pitch), the match-day clock (over rates, bad light, free hits,
nightwatchmen), session-by-session strategy and points-driven ambition, rain and Duckworth-Lewis-
Stern, making up time lost to rain, ball-by-ball commentary, post-match analysis, partnership
records, bonus points, and - closing out the phase - fixture generation (round-robin scheduling,
group stages) and playoff brackets (straight knockout and IPL-style qualifiers).

**Phase 6 is complete** (`CLAUDE.md` -> "PHASE 6 - PROGRESS"). Built: the season engine
(`FixturePlayService`) that plays a generated fixture list day by day as the clock advances -
weather, XI selection, the match engine, the recorder, standings, records and rankings all move
exactly as for a hand-run match; team rivalries and a crowd-driven home-field edge; an AI
club-manager brain (squad announcement per competition, coach and staff hiring, captaincy
succession) with `Team.ManagerPreferences` so a human club can delegate as much or as little as
it likes; the competition-season lifecycle (`CompetitionSeasonRunner`) - playoff bracket
generation once the league stage ends, champion, prize money into budgets, player of the series,
then next season's fixtures generated automatically so a world keeps playing season after
season; two-tier domestic promotion and relegation; in-match tactical AI (`InMatchTacticalAI`) -
a pinch-hitter promoted up the order when quick runs are needed, gated by the captain's read and
the coach's authority; and the in-season board relationship - `BoardConfidence` tracking the
current campaign, mid-season sackings, and job offers to the human coach.

Also built: fixture disruption (a missed or cancelled fixture is re-fixtured, or abandoned after
repeated postponements), dead-rubber detection (a game that can no longer change either side's
qualification is played with lower stakes and a rotated XI), and international cricket -
`WorldSeeder.GenerateInternationalWorld` builds a small multi-country world with national teams
(a pool of each country's best players), a World T20 Championship across them, and real
club-vs-country tension: a player named in a national squad for an active international window is
unavailable to his club (but not to his country).

**A post-Phase-6 rectification pass** then made the coaching world two-directional and cricket-real.
Any coach or staff member - employed or not - can now apply to an advertised vacancy, weighing it on
genuine career benefit rather than raw reputation: a batting coach applying for a head-coach job at a
smaller club is a real step up because the ROLE is. Boards weigh applicants on role fit and how their
philosophy sits with the club's identity; a candidate low on adaptability weighs a move abroad more
cautiously. The head coach - not the board - owns every non-head-coach appointment, and can delegate
that to a general manager or back to the board. The backroom roster is real work now: a batting or
bowling coach is assigned individual sessions with specific young players (feeding the training
system week by week), scouts recommend signings and national-pool additions, and a club mentor
amplifies player-to-player mentoring. Captaincy is format-specific - one captain, a red-ball/white-ball
split, a Test+ODI / T20 split, or three separate captains - appointed by the head coach on leadership
AND playing merit (a leader who does not merit an XI place cannot captain), with international
captaincy carrying higher scrutiny, and the captain has a genuine visible say in squad selection.
National teams pick from a per-format pool of ~38 players built for real role and condition coverage
- multiple backups at every position, a keeper battle, sensible bowling variety - that evolves
continuously off form and never permanently includes anyone. Rotation and rest are driven by real
workload management (condition, recent load, the fixture calendar), never a dead-rubber flag alone.
Plus a set of long-standing carry-forwards: player-of-the-match feeding morale and dressing-room
standing, quarterly attribute-cluster development snapshots, personality-moderated matchup recency,
trust in the captain as a distinct number from trust in the coach, head-to-head history in the
pre-match report, and a templated explanation of the toss call.

**Phase 7 (Board, Media & Finance depth) is complete** (`CLAUDE.md` -> "PHASE 7"). The season
finance loop now actually runs: sponsorship, matchday gate money, facility upkeep and a (stub)
wage bill move a real budget every year, and a club can spend itself into the red. Competitions
distribute broadcast money to their clubs and their standing rises or falls on crowds,
competitiveness and the calibre of the field. The board is a persistent actor - ambition,
patience, wealth, an ownership model and a fanbase mood - that sets the coach a season budget and
can be taken over by a new owner who changes what the coach is judged against. Coaches face the
media (press conferences keyed to how well they handle it, and a pundit layer reacting to the big
stories), the world produces news (a filterable archive and a weekly digest), players reach career
milestones and win player-of-the-month / player-of-the-year / team-of-the-season awards, and the
code of conduct bites - over-rate fines, dissent charges, and real bans. An all-time records book
tracks who held each record and for how long, and genuine greats are inducted into the Hall of
Fame on retirement. National teams have a selection panel and a chairman of selectors whose
quality shapes how well the pool tracks form. And there are umpires - real people with real
careers, assigned to matches by tier and neutrality, whose quality genuinely affects the marginal
LBWs and caught-behinds (there is **no DRS** - an on-field decision stands - by deliberate choice,
to be revisited later).

**Phase 8 (Training & Player Development) is complete** (`CLAUDE.md` -> "PHASE 8"). Every club now
runs a youth academy: a cohort of teenagers comes in each year, sized and shaped by the club's
youth facilities and a light nation-talent skew (subcontinent leans spin, the pace nations lean
quick bowling), and develops on a youth-accelerated curve through the same training tick everyone
uses. The academy graduates its best into the senior squad and releases the ones who plateau - and
it acts on a *scouted estimate* of a prospect's ceiling, not the truth, so a club with a weak
scouting department genuinely lets a good one go and over-promotes a dud. Careers now have
individual arcs: players peak at different ages (a real late developer, a teenage prodigy), a
fast bowler who has lost his pace visibly drifts into a medium-pace role, and a career-threatening
injury leaves a permanent mark on pace and mobility when it heals. A weekly training-calendar
layer means a young player makes real gains in pre-season and the off-season and almost none in a
match week. And the ecosystem loops close: a retiring international becomes a rookie coach on the
market (the same person, a starting badge, his cricket brain intact), coaches earn higher coaching
badges over a career, a young player who consistently outperforms expectation has his ceiling
genuinely raised, and buried youngsters go out on development loans and come back better for the
game time.

**Phase 9 (Auction / Contracts / Market) is complete** (`CLAUDE.md` -> "PHASE 9"). Every player now
has a real contract - a wage, a length, a status - and those wages are the largest line in a
club's season finances (the Phase 7 stub is gone). Contracts renew or run down: a valued young
first-choice on a fair offer re-signs, an ageing fringe player is let go and leaves for nothing
on a Bosman. Free agents are picked up by clubs with a gap. The transfer market runs in windows -
a buying club identifies its weakest position and a target, the fee is driven above all by how
much contract the target has left (a player in his last year costs a fraction of one on a fresh
four-year deal), the selling club decides, the buying club's board sanctions a marquee outlay, the
player agrees personal terms, and a **release clause** lets a rich club force a deal a smaller one
would have refused. Buried, ambitious players hand in transfer requests; a star whose reputation
has outgrown his club gets his head turned. There is a franchise league assembled by a
**turn-by-turn auction** from the whole world's players, with an overseas-player cap on the XI.
Loans carry real terms now - a fee, a wage split, an option or an obligation to buy. One-club
servants get loyalty bonuses and testimonials. A world market index inflates fees and wages a few
percent a year so a multi-decade sim stays economically coherent. And the three-tier training
camps finally exist - a player under a franchise contract is excused from his national camp when
the windows clash.

**The combined post-Phase-7/8/9 rectification/tuning pass is complete** (`CLAUDE.md` ->
"POST-PHASE-7/8/9 RECTIFICATIONS"). Discipline is a real four-level ICC code now - fines as a
percentage of the match fee, demerit points that age out of a rolling 24-month window, a
suspension at four points (the actual ICC figure - the old code used eight), and a head coach who
can be charged for a badly-handled confrontational press conference. Umpires have player-adjacent
careers - matches by format, a howler rate, controversies, a panel history - and move between
panels on accuracy, not age. Every country has an administrative profile: some domestic sides are
board-controlled regional teams (un-takeover-able), others club-membership counties; the transfer
window is keyed to the hemisphere; domestic cricket is open to foreign players under a
data-driven quota; and a fee is denominated against the buying league's cricket economy. There
are four franchise leagues on non-overlapping windows, and the auction is now a genuine strategic
fight rather than a price-only mechanism - franchises register interest to build a shortlist,
players self-select a base price from fixed brackets (a fading star picks lower to avoid going
unsold), the bidding follows a strict staircase, and each franchise works a pre-auction plan that
adapts live: a lost target falls back to a plan B, a filled role stops drawing spend, and
escalation gets sharper when a rival with a deep purse also needs that role. Every third year is a
mega auction with up to six retentions and Right-to-Match cards; the years between are smaller
top-up auctions. Franchise coaches are hired for a campaign, not a career - a coach can hold a
franchise job and a domestic one at once if the windows do not clash.

**A follow-up pass then added the ecosystem around all of that.** There is a fifth franchise
league (BBL), and the auction now comes with a build-up: a preview of each franchise's needs and
purse, the marquee names in the pool, a pre-auction press conference, and afterwards a round-up
naming the biggest buy, the bargain of the day and the players who went unsold, plus a
per-franchise verdict. Franchise finances are real but loss-proof by construction - a guaranteed
central pool that always covers the auction spend, prize money by finishing position, local
revenue that rises and falls with the fan base, and a hard reserve floor underneath it all.
Domestic cricket has its own overseas rule, distinct from the franchise leagues and built on the
principle that every country should benefit from every other: four overseas players in a squad, at
least one of them from an associate nation, and no more than two in the eleven - with a pool of
Dutch, Nepali, Scottish and Emirati players seeded for clubs to sign into that associate slot.
Medical and analysis departments a board pays for now genuinely matter (fewer injuries, sharper
reports). Weather is regional - an English venue loses far more time to rain than an Australian
one. A listed player's agent takes him to several clubs at once and turns their interest into a
bidding war, then takes his cut. National coaching is its own career track, judged on tournaments.
And a franchise league hands out an emerging-player-of-the-tournament award.

**Phases 10 through 16 are now done, including their deferred items.** A `Country` talent layer,
a WTC-style Test Championship + ODI Championship + named bilateral trophies, central contracts and
international-always-wins-the-clash, a real 3-tier promotion/relegation chain and a 3-format
domestic season, associate nations as real teams with a qualifier; the unified coach/player
delegation model, the selection meeting, the fuller XI combination problem; the media/board/
narrative layer (a chairman's own agenda, coach-player relationships, franchise-circuit assistant
coaches); multi-year sponsor deals and persistent player agents; DRS, the Super Over, pitch
doctoring, drop-in and re-used strips, wind and heat, the concussion protocol, retired hurt, and
the franchise Impact Player as a real roster addition; more record categories, milestone
ceremonies, a Team of the Era with a dominant side and a defining storyline. On top of all of
that, a dedicated Match-Engine Tactical Pass closed roughly half of the ~20 in-match micro-tactics
named as their own future pass since Phase 4 - a spinner turning away from the bat draws a real
slip, captain quality now genuinely scales the declaration and follow-on decisions, a sustained
leg-theory barrage carries a real conduct cost, and more. See `CLAUDE.md` -> "PHASE 10-16
DEFERRED-ITEMS COMPLETION SWEEP" for the full six-pass writeup, including a real regression that
was found, diagnosed and fixed along the way. Real ICC Future Tours Programme / Cricsheet data
import remains Phase 18 - unstarted, and always was going to be a data-layer job, not an engine one.

## What actually works today

You can seed a world, advance a real-world calendar through it day by day, and watch careers
happen: players age on attribute-group-specific curves, get injured and recover, retire for
individual reasons, and build reputations from performances weighted by opposition and
competition prestige. You can simulate a limited-overs match ball by ball with fields set to the
laws, and every scorecard feeds ground records, honour boards, points tables and net run rate.
Fielders matter individually: an outstanding one takes catches nobody else reaches and saves
boundaries, a poor one drops chances and fumbles, and the same save always replays identically.
A coach can set batting and bowling intent, target or see off individual bowlers, farm the strike
to protect the tail, force bowling changes, and set field aggression - and can lose a match by
getting any of it wrong. Bowling plans are set in a coach's language - a line, a length, a go-to
variation - and the bowler executes them with his own judgement: he can miss his mark, change his
line when he is being milked, spot a weakness the coach didn't, or ignore a working plan if his
discipline is poor. Batters get set - and a set batter sees it like a football without ever
becoming unbeatable - while confidence, fitness and the difficulty of the surface decide whether he
ever feels in at all. Plans are scored against both the batter's weakness and the bowler's ability
to bowl them, so a plan the bowler cannot execute is correctly a bad plan. The captain is a real
participant rather than a name on a team sheet: he calls the toss, sets the field in the middle,
makes the bowling changes, and can back his own read against his coach - rightly or wrongly. A good
coach and a good captain compound; two ordinary ones make more mistakes than either would alone.

For a human coach the final say is always his. The captain and the bowlers tell him what they want -
an extra slip, a different bowler, a change of line - and he decides whether to act on it. He can
also hand any decision, or any individual player, over entirely: "bowl how you like" is an
instruction he chooses to give, not one the players take for themselves.

Backroom staff matter. An analyst produces a pre-match report on the opposition - estimated scoring
areas, identified weaknesses, and which of your bowlers should attack whom - and the report is only
as good as he is. A poor analyst misses real weaknesses and invents ones that are not there, and
following him is worse than having no plan at all.

Conditions are felt by the players. Heat and humidity compound into genuinely punishing work while
cool overcast air is easier than a mild day, bowlers feel it more than batters, and a bowler taken
off recovers while he fields rather than waiting for the interval. Captains pick bowlers on the
matchup, the pitch, the weather and who is on top - not by taking turns.

Multi-day cricket is its own game: four innings, declarations, the follow-on, a pitch that wears
and turns across the days, sides batting out time to save a match, and the draw as a real result
worth real points. A day of play runs on a clock rather than a fixed allocation: quick bowlers take
longer over their overs than spinners and cost their side overs, the light fades in the evening and
allows spin only, nightwatchmen are sent near the close, and a no-ball in limited overs earns a free
hit the batter cannot be dismissed on. Sides also play for a result rather than simply playing:
competition points shape how a match is approached before a ball is bowled, and both dressing rooms
reassess at every break - a side three hundred behind is surviving, and if it turns the match around
the win comes back into view. Rain behaves as it should in both formats: limited-overs matches lose
overs and get a target recalculated on resources, while multi-day matches lose time - which is what
turns a winning position into a draw. Lost time is then made up the way the playing conditions
allow: the days after a washout start early, run late and bowl more than ninety overs until the
arrears are cleared - though a completely lost day, and time lost on the final day, are gone for good.
A finished multi-day match feeds the world exactly like a limited-overs one does now: batting,
bowling, fielding and partnership records, first-class points including batting/bowling bonus
points, and career/form/reputation effects, all from the same match that could previously only be
simulated and read, never recorded.

Every ball can be turned into readable commentary on demand - not baked into the simulation, but
generated afterward from the same delivery log a scorecard reads from. It reacts to what actually
happened: milestones as they're reached, a trait-flavoured line when a power hitter clears the
ropes or a containment bowler locks a batter down, a nod to matchup history on a wicket, and the
required rate spelled out when a boundary or a wicket lands in the death overs of a tight chase.

A finished match can also produce a post-match report: who stood out (batting and bowling ratings
combined, so a genuine all-rounder's match is credited as one performance rather than losing to a
single bigger innings elsewhere), the notable passages of play - a batting collapse, the biggest
partnership, how hard a chase's required rate peaked, a Test match's turning point in the
dressing room's own words - and factual notes on anything that shaped the match: rain, a DLS
revision, an enforced follow-on. It deliberately stops short of "what the coach could have done
differently" - a real counterfactual needs an AI that can evaluate alternative decisions, which
belongs with the AI managers phase, not a report reading a finished match.

Every partnership is kept as its own record, not just folded into a scorecard - so "the best 3rd-
wicket stand at this ground" or "the highest partnership these two players have put together" are
real, queryable answers. First-class cricket can also earn batting and bowling bonus points on top
of the result (modelled on the Ranji Trophy system), and a limited-overs competition can opt in to
an extra point for a big win - both stay off by default so today's points tables are unaffected
unless a competition specifically turns them on.

Players develop between matches, not just at a season's end. A coach picks a training focus - "work
on his death bowling", "his game against spin" - or leaves it to the specialist coach to name the
weakness, and the work lands in twelve small monthly increments a year, faster for a young player
with headroom and a good coach. Playing develops him too, format-aware: a Test innings sharpens
technique and concentration, a T20 one power and death-hitting. A big public failure in a match
that mattered hangs over a player until he answers it - and whether he does is a matter of
temperament. Form, confidence, morale and matchup edges all fade gradually across a season now
rather than jumping once a year, and they FREEZE while a player is injured or rested - he does not
lose form he had no chance to defend. Coming back from a long lay-off, his belief is read from
where it was before the injury, not from whatever a hot replacement did to the team while he was
out.

The dressing room has a hierarchy. Senior pros' read of the coach carries the room; the juniors
take their cue from the seniors, and a squad split down the middle among its senior players is a
fractured one whatever the results say. Senior players mentor juniors - a committed mentor and a
keen mentee genuinely accelerate the young player's development and steady his temperament; a
clash of personalities barely helps and the pairing simply is not made.

Momentum is real and shiftable. A run of boundaries or a burst of wickets swings an innings, it
decays over by over because it is fleeting, and on a break a composed side holds what it built
while a fragile one hands it back - and it works inside a Test match, across a day's play, not
only in white-ball cricket. A team on a genuine upswing plays with a little more belief than its
raw mood alone; a spiral is its own drag.

Backroom staff and coaches have careers of their own. A specialist builds a reputation from
whether his own players actually improve, not just from years served; an assistant coach can step
up as interim when the top job falls vacant, and be made permanent after a real run. A rival club
can come calling for a sitting coach, and the board either fights to keep him with improved terms
or lets him walk. And the board judges a coach on more than last season: titles in the bank buy
real rope, an elite club that has gone a whole tenure without a trophy applies real pressure
however respectable the finishes look, and what counts as success is team-appropriate - a
developing side reaching a World Cup quarter-final is a triumph, the same run for a powerhouse is
a failure.

The world produces news. Every event the simulation raises - a signing, a sacking, an injury, a
title, a board verdict - is classified into a category with a prominence and rolled into a weekly
digest that leads with what matters. There is a toss-independent pre-match conditions report that
describes the pitch and weather and what they favour without ever making the captain's call for
him, and ICC-style team rankings that move on a rating-difference model after every match and
feed a team's reputation over time.

There is no graphical UI yet. The test suite is the interface - `dotnet run` on the test project
exercises every system end to end, commentary and analysis included.

## Project layout

```
CricketManager.sln
src/
  CricketManager.Domain/     - entities, value objects, domain services (pure logic)
  CricketManager.Data/       - SQLite repositories, GameDataContext, world seeding
tests/
  CricketManager.Tests/      - console-app test runner, run via `dotnet run`
```

## Build & test

```bash
dotnet build CricketManager.sln
dotnet run --project tests/CricketManager.Tests/CricketManager.Tests.csproj

# 673 tests, all passing, zero build warnings as of the current build (verified on real
# dotnet build/run, two consecutive clean runs - see CLAUDE.md's "MEETING-DRIVEN SELECTION TICKET" section)
```

A passing run ends with `Results: N passed, 0 failed (of N)`.

## Why not xUnit

The development sandbox this project was originally built in had no access to nuget.org
(only a fixed allowlist of domains), so `tests/CricketManager.Tests` became a plain
console app with a ~40-line hand-rolled `TestRunner` (see `TestRunner.cs`) instead of
xUnit/NUnit. That constraint no longer applies (the project now builds with real NuGet
access), but nobody has asked for the xUnit migration and it isn't required just because
it's now possible - the test logic itself doesn't depend on the runner, only the
`TestRunner.Run(name, () => {...})` wrapper syntax would need converting to `[Fact]`
methods, if that's ever wanted.

## Why JSON instead of SQLite

Historical - **this has been migrated.** The original sandbox had no NuGet access, so
`CricketManager.Data` used a hand-rolled `JsonRepository<T>` behind `IRepository<T>`.
Once real NuGet access existed, `SqliteRepository<T>` (`Microsoft.Data.Sqlite`) replaced
it as `GameDataContext`'s backing store - `JsonRepository<T>` is kept in the tree as a
fallback/reference, not deleted.

The storage shape is a deliberate **JSON-column hybrid**, not a full relational mapping:
each entity type gets one SQLite table, `(Id TEXT PRIMARY KEY, Json TEXT NOT NULL)`,
reusing the same `System.Text.Json` serialization JsonRepository always used. This was a
considered choice, not a shortcut - full relational mapping of every nested value object
(`BattingAttributes`, `Reputation`, `FormState`, `Matchups`, `InjuryHistory`, ...) across
16 entities would have been a much larger, much riskier undertaking for a payoff nothing
in the codebase currently needs: every query filters in C# over already-loaded
candidates, never with a SQL `WHERE` on a nested field. If that ever changes, that's the
trigger to promote to real relational mapping - see `CLAUDE.md` tech-debt item 2 for the
full writeup, including a Windows connection-pooling bug found and fixed during the
migration (`Pooling=False` - Microsoft.Data.Sqlite otherwise keeps the file handle open
across `Dispose()`, which broke every test that deletes its save directory right after use).
