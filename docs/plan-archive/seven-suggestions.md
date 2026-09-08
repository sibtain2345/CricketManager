# Review-process suggestions pass — 7 features + a cricket-personnel-careers subsystem

> **STATUS: COMPLETE — 691/691, 0 build warnings.** All 11 slices built (7 suggestions + NEW-A/B/C/D),
> logic-then-tests, 11 net new tests (680 → 691). Two pre-existing tests updated for legitimate
> behaviour changes; one transient failure of the fragile "extended world deterministic" exact-count
> check did not reproduce across 4 isolated + subsequent full runs (no shared mutable static
> introduced; all new RNG consumption is deterministic — two Guid-ordering bugs were caught and
> fixed before the test run). Full writeup: `CLAUDE.md` → "SEVEN-SUGGESTIONS PASS + CRICKET-PERSONNEL-CAREERS
> SUBSYSTEM". README + phase-status table updated.
>
> **STATUS (earlier): APPROVED — IN PROGRESS.** User's call: **do everything in this pass**; all four open
> questions resolved per my recommendation (all seven built; S7 stays irrevocable; S5 gets a
> light national finance loop; S2 seeds every nation `Unified`). PLUS four expansions the user
> added: (NEW-A) staff franchise contracts are real, same logic/conditionality as the head coach;
> (NEW-B) national selectors must be ex-cricketers; (NEW-C) a retiring player has real, varied
> future roles weighted by his playing career and driven by his own preference; (NEW-D) a head
> coach can hold multiple roles (head coach + bowling/batting coach + selector, even triple)
> gated on his skills and willingness. Baseline: build clean, suite **680/680** (four consecutive
> clean runs).

## Additional research (the personnel-careers expansion)

- **National selectors are ex-cricketers.** BCCI senior selection committee eligibility: **min 7
  Tests OR 30 first-class matches OR 10 ODIs + 20 first-class matches**; **retired ≥ 5 years**;
  not on a BCCI cricket committee for a cumulative 5 years (a conflict rule). The chief selector
  is conventionally the panel member with the most international caps (Agarkar, MSK Prasad).
  Sources: [BCCI — selector applications & criteria](https://www.bcci.tv/articles/2025/news/55556251/bcci-invites-applications-for-national-selector-positions), [The SportsRush — applicant list & criteria](https://thesportsrush.com/cricket-news-bcci-selection-committee-list-list-of-applicants-for-bcci-new-senior-mens-selector-post/).
- **Post-retirement career paths.** Coaching (head, or a specialist — batting / pace / spin /
  fielding / wicketkeeping), **mentor** (reserved for legends / renowned names — Dhoni, Gambhir,
  Dravid, Ponting; short-term, prestige-based, often a national T20-World-Cup gig), scouting /
  talent-ID, age-group / academy coaching, selection, commentary/punditry, administration — and
  some simply leave the game. Coaching needs accreditation (a licence). Sources: [Mystery Cricket — what cricketers do after retirement](https://mysterycricket.com/blogs/cricket/what-do-cricketers-do-after-retirement), [Career in Cricket — options beyond playing](https://careerincricket.com/blog/cricket-career-options-beyond-playing).
- **Multi-role personnel are real.** Dinesh Karthik — **mentor AND batting coach** at London
  Spirit. Gautam Gambhir — mentor (LSG 2022-23, KKR 2024, title) then India head coach. Player-
  coaches historically. So one person legitimately holds two or three cricket jobs at once when
  windows and workload allow. Sources: [ESPNcricinfo — Karthik mentor + batting coach, London Spirit](https://africa.espn.com/cricket/story/_/id/47263913/the-hundred-former-india-cricketer-dinesh-karthik-appointed-london-spirit-mentor-batting-coach), [ESPNcricinfo — Gambhir returns to KKR as mentor](https://africa.espn.com/cricket/story/_/id/38956708/ipl-2024-gautam-gambhir-returns-kolkata-knight-riders-team-mentor).

The user supplied a review-process suggestion prompt with 7 optional features. Per its own
instruction and the standing gate: verify each research claim, verify against the actual codebase
(file:line), write a plan, get it reviewed, THEN implement — and "these are suggestions to
prioritise/discuss, not an automatic go-ahead to build all seven in one pass."

---

## Research findings (all verified against real sources this pass)

**S5 — ICC revenue.** 2024-27 model: total pool ≈ **US$600M/year**. BCCI **38.5%** (≈$230M).
No other Full Member in double digits: **ECB $41.3M (6.89%)**, **CA $37.5M (6.25%)**, **PCB
$34.5M (5.75%)**. The 12 Full Members take **88.81%** ($532.8M); the 90+ Associates share
**11.19%** ($67M, ≈$700k each). Allocation criteria: cricket history; performance in ICC events
over the last 16 years; contribution to ICC commercial revenue; equal weightage for Full-Member
status. ICC event prize money is real and tiered: **WTC 2025** winner $3.6M / runner-up $2.1M
(≈$12M pool); **ODI World Cup 2023** $40k per league-stage win + placement money, winner $4M.
Sources: [ESPNcricinfo — BCCI ~40% of ICC net earnings](https://www.espncricinfo.com/story/bcci-set-to-get-nearly-40-of-icc-s-annual-net-earnings-in-new-revenue-distribution-model-1387167), [SportsPro — BCCI 38.5%](https://www.sportspro.com/news/bcci-icc-revenue-earnings-distribution-model-2024-27/), [Wisden — per-board breakdown](https://www.wisden.com/cricket-news/icc-revenue-share-model-2024-27-each-board-bcci-ecb-ca-pcb), [ICC — WTC25 winners $3.6M](https://www.icc-cricket.com/media-releases/icc-world-test-champions-to-bag-3-6-million-purse), [ESPNcricinfo — ODI WC 2023 winner $4M](https://www.espncricinfo.com/story/odi-world-cup-2023-winner-to-receive-usd-4-million-in-prize-money-1399559).

**S6 — player eligibility / switch.** A player qualifies for a nation by any one of: **born
there**; **citizen**; **resident for 3 consecutive years** (primary/permanent home, a close
credible link). **3-year stand-down** after the player's last International Match for the original
federation before he can qualify for another. **Zero stand-down** for a player moving from an
**Associate** federation to a **Full Member**. Sources: [ICC Player Eligibility Regulations (PDF)](https://images.icc-cricket.com/image/upload/prd/o6gtuccut4pumbxmbzgu.pdf), [USA Cricket — ICC PER amended 2021 (PDF)](https://usacricket.org/wp-content/uploads/2021/10/ICC-Player-Eligibility-Regulations-amended-effective-12-April-2021-.pdf).

**S7 — Test / Full Member status.** Article 2.1: six criteria areas (general, governance,
performance, participation & domestic structures, infrastructure, development programmes).
Ireland + Afghanistan granted Full Member + Test status on **22 June 2017** by a **unanimous** ICC
Council vote (bringing Full Members to 12). **Article 2.7: Full Member status is IRREVOCABLE** —
there is no demotion mechanism; voting rights and historical entitlements survive any performance
decline. The suggestion's own correction is confirmed. Sources: [Wisden — Full Member criteria](https://www.wisden.com/series/icc-mens-t20-world-cup-2024/cricket-news/what-are-the-iccs-requirements-for-full-member-status), [Sky Sports — Ireland & Afghanistan Full Members](https://www.skysports.com/cricket/news/12123/10923896/icc-grant-full-member-status-to-ireland-and-afghanistan), [Grokipedia — ICC members, Article 2.7 irrevocability](https://grokipedia.com/page/List_of_International_Cricket_Council_members).

**S2 — split coaching.** Real but **cyclical**: England split after Silverwood (Feb 2022) →
Brendon McCullum (Test) + Matthew Mott (white-ball) → Mott sacked July 2024 after two failed
World Cup defences → **reunified under McCullum from Jan 2025** because "constant clashes between
formats" made the split hard to run. So the game should model both structures with a real
coordination cost to the split that can drive reunification. Sources: [Sky Sports — McCullum takes white-ball too](https://www.skysports.com/cricket/news/12123/13209015/brendon-mccullum-appointed-england-white-ball-head-coach-in-senior-restructure), [ESPNcricinfo — McCullum adds white-ball role](https://www.espncricinfo.com/story/brendon-mccullum-becomes-england-new-white-ball-coach-adding-to-test-role-1449785).

**S1 — sister-franchise groups.** Well-established, no deep research needed: Reliance (Mumbai
Indians / MI Cape Town / MI New York / MI Emirates), Sunrisers (Hyderabad / Eastern Cape),
GMR-JSW (Delhi Capitals / Pretoria Capitals), Royals (Rajasthan / Barbados / Paarl). Group-wide
scouting, player-development pipelines and coach/analyst movement within the group are real.

**S4 — return-to-play through domestic cricket.** Standard practice: a player back from a long
injury plays domestic / 'A' / county cricket for match fitness before an international recall
(Ben Stokes, Jasprit Bumrah, Rishabh Pant have all done exactly this). No specific numbers to
verify — it is a well-known convention.

**S3 — player commercial value.** Real: image rights, individual sponsorship and brand value are
distinct from a player's match-fee/contract wage and shape where a player wants to be seen. No
specific figures to verify.

---

## Codebase verification (exact refs, this pass)

- **`CountryProfile`** (`ValueObjects/CountryProfile.cs`): `Membership` (`MembershipStatus.FullMember`/
  `Associate`), `BoardYouthInvestment` (0-100), `EconomicScale` (1.0 ref), `TalentProduction`,
  `PoliticalStability`, `MediaIntensity`/`MediaVolatility`/`MediaPressureFactor`. **No ICC-revenue
  field, no "cricket history / seniority" field.**
- **`NationalBoard`** (`ValueObjects/NationalBoard.cs`): `ChairmanOfSelectorsQuality`,
  `Politicisation`, `Ambition`/`ExpectedMajorPlacing`, `CoachScrutinyMultiplier()`,
  `ConsecutiveOutvotes`. **No revenue / budget / investment lever of its own** — it is a
  selection-and-scrutiny object only.
- **`SeasonFinanceService.cs:45`** and **`FinancialFairPlayService.cs:45`** both iterate
  `world.Teams.Values.Where(t => !t.IsNational && !t.IsFranchise)` — **national boards have NO
  running finance loop at all.** A national `Team.Finances.Budget` exists but nothing feeds or
  spends it.
- **`SeasonFinanceService.cs:71-77`**: club image-rights revenue IS already derived from
  `Player.MediaPersona` (`Marketable` → `320_000 + Reputation.Worldwide * 9_000`, etc.). There is
  **no player-level commercial-value scalar** and nothing feeds a player's own morale / transfer
  wish from it.
- **`Player.MediaPersona`** (`Player.cs:296`): computed (Guarded/Balanced/Marketable/Outspoken)
  from personality, not stored. **No `CommercialAppeal` / `CareerSatisfaction`** on `Player`
  (`CareerSatisfaction` is a `Coach` field). `Player` has `Morale`, `Form`, `MatchSharpness`
  (Post-16 bench-sharpness, `Player.cs`), `ConcussionStandDownUntil`.
- **Nationality switch** — `SkillRegressionService.cs:28-42`: a **thin** switch already exists —
  an **uncapped** player (`Experience.InternationalMatches == 0`), age 27-33, whole career in
  another country (`club.Country != p.Nationality`), `Reputation.Domestic >= 62`, **6%/year** →
  `p.Nationality = club.Country`, a `GameEventType.OffFieldEvent` "switch of allegiance" line.
  **No stand-down clock, no `LastInternationalAppearance` date, no heritage/Pathway-A, no
  coach-conversation trigger, no personality shaping, no reversibility.** `PlayerExperience`
  tracks `InternationalMatches` (a count) but **not the date of the last one**.
- **`CaptaincyPattern`** (`Enums.cs:132`): `Unified` / `RedBallWhiteBall` / `LongFormShortForm` /
  `ThreeSeparate` — on `Team`, appointed by `CaptaincyAppointmentService`. **This is the exact
  template S2 asks coaches to mirror.**
- **`FormatSpecialisation`** (`Enums.cs:726`): `AllFormats` / `RedBall` / `WhiteBall`.
  `Coach.FormatFocus` uses it. A **format-specialist ASSISTANT** already exists for a domestic
  club (`AiClubManagementService.cs:413-426` — `Coach.AssistantToTeamId` + `FormatFocus.WhiteBall`,
  `FixturePlayService` hands him the white-ball reins). **NOT for national teams, and NOT as a
  separate HEAD coach with his own `CoachingContract` / `NationalBoardVerdictService` judgement.**
- **`FillCoachVacancy`** (`AiClubManagementService.cs:389-406`): hires exactly ONE head coach per
  team; a national job is a 4-year contract at 1.6× salary, emits `NationalCoachAppointed`. No
  format split for the head role.
- **Associate nations** — `WorldSeeder.cs:678-713`: already **real national teams** with their
  own player pools, contesting an **"ICC Associate Qualifier"** (Phase 10 §11.7). `AssociateNations`
  seed list (Netherlands / Nepal / Scotland / UAE). This is the infrastructure S7 promotes FROM.
- **`FranchiseCoachService`** (just rewritten in the corrections pass): franchise HEAD coaches now
  hold a real multi-year `CoachingContract` and `FranchiseCoachingTeamId`. Staff
  (`world.Staff` / `StaffContract`) have **no franchise equivalent** — a `StaffMember` cannot hold
  a franchise contract at all today. `RoleFitService.RelocationComfort` (Post-Phase-6 §G) is the
  reusable "how willing is this person to move" lever S1 names.
- **`Team.FranchiseArchetype`** + **`FranchiseIdentityService`** (meeting ticket Stage 2) + a
  `Team.OwnershipModel` (`ClubBoard.Ownership`) exist. **No `OwnershipGroupId` / cross-team
  ownership link.**
- **`RetirementService.TryComebackFromFormat`** (`RetirementService.cs:274`) — a genuine name in
  his 30s can be coaxed back into a format he retired from. The natural neighbour for S4/S6's
  Scenario D.
- New `GameEventType`s needed: none of `IccRevenue*`, `FullMembership*`, `TestStatus*`,
  `AllegianceSwitch*`, `CoachingStructure*` exist.

---

## Per-suggestion recommendation

| # | Feature | Grounding | Codebase readiness | Scope | Recommend |
|---|---|---|---|---|---|
| S5 | ICC / board revenue flow | **Strong** (exact numbers verified) | New subsystem — no ICC pool, no national finance loop; `CountryProfile`/`NationalBoard` are the levers | **Medium** | **BUILD — first.** Highest ecosystem value: makes the associate trajectory real over decades and is the prerequisite for S7 meaning anything. |
| S7 | Test / Full Member status grant | **Strong** (Article 2.1 + 2.7 verified) | Sits directly on the Post-10 associate-qualifier infra; keep **irrevocable** | **Small-medium** | **BUILD — second (after S5).** A rare, telegraphed long-sim milestone; gives S5's Full-Member revenue tier something to promote INTO. |
| S6 | Dual-nationality / representation drift | **Strong** (3 routes, 3-yr stand-down, zero for Associate→Full) | A thin switch exists; needs a stand-down clock, `LastInternationalAppearance`, the coach-conversation trigger, personality shaping | **Medium** | **BUILD — third (after S7).** Scenario D (reversible switch) depends on Test-status changes existing. |
| S2 | Format-specific international head coaches | **Strong** but cyclical (split→reunify) | `CaptaincyPattern` is the exact template; format-specialist assistant already exists for clubs | **Medium** | **BUILD — self-contained.** Mirrors an existing pattern; fits the "coaching / selection depth is a first-class priority" steer. Model the split's coordination cost so reunification is a real outcome. |
| S3 | Player commercial value | **Adequate** | `MediaPersona` computed; club image-rights already derived; no player-level scalar | **Small** | **BUILD — low-risk flavour.** A computed `Player.CommercialAppeal` feeding a bounded morale/satisfaction term + a transfer-wish nudge + the existing club image-rights line. |
| S1 | Sister-franchise ownership groups | **Strong** (real groups) | No ownership-group link; `FirstHandKnowledgeService` / `RoleFitService.RelocationComfort` reusable | **Medium** (scoped) | **BUILD — scoped.** `Team.OwnershipGroupId`; sister-franchise first-hand-knowledge; a soft group-retention bias; within-group coach/staff movement. **Part (f) — staff can hold multiple simultaneous franchise contracts — is small and independently useful; do it alongside.** Defer cross-group scouting (lower value). |
| S4 | Rehab through domestic cricket | **Adequate** (well-known convention) | `MarkRecovered` / `MatchSharpness` / `MatchDevelopmentService` / `LoanService` are the parts; no "must play domestic before recall" gate | **Small-medium** | **BUILD — self-contained.** A `NeedsMatchFitnessUntil` gate after a Serious/CareerThreatening layoff; national selection won't pick him until N domestic/A appearances or the flag lapses; surfaced as news / a `StaffRecommendation`. |

**Nothing is recommended for outright rejection.** All seven are real and grounded. The question
is how many to take in this pass.

---

## FINAL SLICE ORDER (approved — build all, logic-then-tests, two clean runs at the end)

1. **S5 — `IccRevenueService` + a light national finance loop.** `CountryProfile.CricketHistoryWeight`
   (seeded seniority/heritage proxy). Annual: a global pool (scaled by `WorldState.MarketIndex`),
   split by weighted formula (history + recent ICC-event performance + commercial draw from
   `MediaIntensity`×reputation + equal Full-Member share), **deviations as named tunable
   constants** (India-largest-but-flatten-the-majors; a generous *growing* associate development
   pool). Money → national `Team.Finances` (a light loop: national-coach salary + a few ICC costs
   draw against it) **and** lifts `CountryProfile.BoardYouthInvestment` toward a funded target.
   ICC-event prize money (WTC / ODI-champ / World-T20) paid to participating boards on completion,
   tiered by finish + a per-win league bonus. **Men's events only.**
2. **S7 — `FullMembershipService` (irrevocable).** An associate that sustains a real record
   (qualifier wins + bilateral results vs Full Members + `BoardYouthInvestment`/reputation
   threshold + financial stability from S5) earns a rare, **telegraphed** promotion: a
   `IccFullMembershipGranted` event, `Membership → FullMember`, WTC/ODI-Championship admission,
   move onto the Full-Member revenue tier. A "candidacy" storyline builds a season or two first.
3. **S6 — `RepresentationDriftService`.** `Player.LastInternationalAppearanceDate` +
   `Player.HeritageNations` (0-2, seeded rarely) + `EligibleNationsNow` applying the 3-year
   stand-down (and **zero** for Associate→Full). Triggered by being genuinely out of his nation's
   plans (pool membership + coach relationship) or a bitterness spike (dropped despite proven
   form); personality-shaped (`Ambitious` chases it, `Loyal` may retire uncapped, an aggrieved
   player switches on a grievance). Rare **named storyline**. Reversible when a target nation
   gains Test status (S7). Absorbs the thin `SkillRegressionService` switch.
4. **NEW-C — `PlayerRetirementCareerService`.** On retirement, a player pursues a destination
   role driven by (a) his playing career weight (caps, reputation, longevity, role) and (b) a
   computed **post-career ambition** from personality: **head coach** (pedigreed + `Ambitious`),
   a **specialist** `StaffMember` (batting / bowling / fielding coach, or scout — mid-tier),
   **selector** (past the 5-yr cooldown, min caps), **mentor** (legends only — worldwide
   reputation ≥ a high bar), **scout/talent-ID** (journeyman FC players), or **leaves the game**.
   Expands the existing `CoachRecruitmentService.CreateFromRetiredPlayer`. Each new coach/staff
   member carries a `PlayingCareerWeight` that scales his starting attributes and standing, and a
   `RolePreference` (what he wants to become long-term).
5. **NEW-B — selectors are ex-cricketers only.** `AiClubManagementService.FillSelectionPanel`
   draws `Selector`/`ChiefSelector` from the pool of retired-player-derived staff (NEW-C) with
   `Role == Selector`, never a fabricated person. Enforce: retired ≥ 5 years, a min-caps
   threshold (7 Tests OR 30 FC OR 10 ODI+20 FC, read from `PlayerExperience`). Chief = most
   international caps on the panel. If the pool is thin, the panel runs short (a real state) rather
   than inventing someone.
6. **NEW-D — multi-role personnel (`Coach.AdditionalRoles`).** A `List<CoachRole>` (`(TeamId,
   StaffRole)`) a head coach also fills — bowling/batting/fielding coach, or selector — when the
   team cannot afford / does not need a separate specialist AND the coach's matching attribute is
   high AND he is willing (a `WillDoubleUp` read from ambition/workEthic). Max 2 additional. The
   training / specialist-boost / selection reads honour it. A small workload cost. Also covers a
   retired-player mentor who is *also* a batting coach (Karthik).
7. **S2 — national `CoachingStructure`.** Enum on a national `Team` (`Unified` / `RedBallWhiteBall`
   / `LongFormShortForm` / `ThreeSeparate`) mirroring `CaptaincyPattern`; every nation seeds
   `Unified`. Each head coach on his own `CoachingContract`, judged by `NationalBoardVerdictService`
   **on his formats only**, philosophy drift scoped to those formats. The split carries a
   **coordination cost** (squad-planning / workload friction) that, sustained, triggers a board
   **reunification** — the England 2022→2025 arc.
8. **NEW-A — staff franchise contracts.** `StaffMember.FranchiseStaffTeamId` + a real
   `StaffContract` for franchises, mirroring the corrections-pass `FranchiseCoachService` rewrite:
   **same availability rules** (national-team staff → host-country franchise only; domestic-club
   staff → any country, franchise wins a clash; a clashing OTHER franchise role → unavailable),
   **multi-league year-round** contracts. `FranchiseCoachService` (or a sibling
   `FranchiseStaffService`) fills a franchise's specialist chairs on real contracts.
9. **S1 — ownership groups.** `Team.OwnershipGroupId` + an `OwnershipGroup` registry seeded from
   the franchise leagues (MI / Sunrisers / Capitals / Royals shape). `FirstHandKnowledgeService`
   counts a sister-franchise's shared history (at a discount). A **soft** group-retention bias in
   the auction (never a guaranteed pipeline). Within-group coach/staff movement gets a
   `RelocationComfort`-style lift. Cross-group scouting: deferred.
10. **S3 — `Player.CommercialAppeal`.** Computed (worldwide reputation × marketability from
    `MediaPersona` × a small recent-form term), bounded. Feeds a morale term when a club under-uses
    his profile, a `TransferRequestService` nudge when a bigger-market club is interested, and a
    more precise `SeasonFinanceService` image-rights line.
11. **S4 — match-fitness gate.** `Player.NeedsMatchFitnessUntil` set on recovery from a Serious /
    CareerThreatening layoff; national selection excludes him until N domestic/'A' appearances
    (tracked) or the flag lapses; a `StaffRecommendation` / news line. Composes with
    `MatchDevelopmentService`.

## Superseded — original tiered recommendation (kept for the reasoning)

The tiered order below was the pre-approval recommendation; the user chose to build everything.

Built as reviewed slices, logic-then-tests, determinism discipline throughout (no `Guid`-ordered
iteration that gates RNG; new RNG consumers at tail positions on their existing per-cadence
streams; two consecutive clean full-suite runs before a slice is done).

1. **S5 — `IccRevenueService`.** A new `CountryProfile.CricketHistoryWeight` (seeded, a
   seniority/heritage proxy) + `IccRevenueService.DistributeAnnually` on the annual rollover: a
   global pool scaled by `WorldState.MarketIndex`, split by a weighted formula (history +
   recent-ICC-event performance from `NationalBoardVerdictService` / rankings + commercial draw
   from `MediaIntensity` × reputation + an equal Full-Member share), with the **deliberate
   deviations as named tunable constants** — India-largest-but-flatten-the-other-majors, and a
   deliberately generous, *growing* associate development pool. The money establishes a **light
   national finance loop** (feeds national `Team.Finances`, and lifts `CountryProfile.BoardYouthInvestment`
   toward a funded target — which `AcademyService` already reads). **Men's events only** (no
   women's concept exists anywhere and none is added). ICC-event prize money (WTC / ODI-champ /
   World-T20) paid to the participating boards on those competitions' completion, tiered by
   finish, with a per-win bonus for the league stage.
2. **S7 — `FullMembershipService`.** An associate that sustains a real record — qualifier wins +
   bilateral results vs Full Members + a `BoardYouthInvestment` / reputation threshold + (from S5)
   financial stability — earns a rare, **telegraphed** promotion: an `IccFullMembershipGranted`
   event, `CountryProfile.Membership → FullMember`, admission to the WTC / ODI Championship, and
   (with S5) a move onto the Full-Member revenue tier. **Irrevocable** — a promoted nation stays
   promoted; the roster of Test nations only grows. A "candidacy" storyline builds for a season or
   two before the grant so it never comes from nowhere.
3. **S6 — `RepresentationDriftService`.** New `Player.LastInternationalAppearanceDate` +
   `Player.HeritageNations` (0-2, seeded rarely) + a computed `EligibleNationsNow(player, date)`
   applying the 3-year stand-down (and the **zero** stand-down for an Associate→Full move). The
   switch is triggered by (a) being genuinely out of his nation's plans — read from `NationalPool`
   membership + the `CoachPlayerRelationshipService` standing — or (b) a **bitterness spike**
   (dropped despite proven recent form). Outcome shaped by personality: `Ambitious` chases the
   opportunity, `Loyal` may retire uncapped rather than switch (a legitimate ending),
   a low-professionalism / aggrieved player switches on a grievance. Surfaced as a rare **named
   storyline**. **Reversibility** (Scenario D) when a target nation gains Test status (S7). The
   existing thin `SkillRegressionService` switch is absorbed into this.
4. **S2 — national `CoachingStructure`.** A `CoachingStructure` enum on a national `Team`
   (`Unified` / `RedBallWhiteBall` / `LongFormShortForm` / `ThreeSeparate`) mirroring
   `CaptaincyPattern`; each head coach on his own `CoachingContract`, judged by
   `NationalBoardVerdictService` **on his formats only**, with philosophy drift scoped to the
   formats he runs. The split carries a real **coordination cost** (a squad-planning / player-
   workload friction term) that, sustained, can trigger a board **reunification** decision — the
   England 2022→2025 arc. `AiClubManagementService.FillCoachVacancy` and
   `NationalBoardVerdictService` are the two touch points.
5. **S4 — match-fitness gate.** `Player.NeedsMatchFitnessUntil` set on recovery from a
   Serious / CareerThreatening layoff; `NationalSelectionService` / `XiSelectionService` exclude
   him from **international** selection until he has had N domestic / 'A' appearances (tracked) or
   the flag lapses; a `StaffRecommendation` / news line makes the return a visible decision.
   Composes with `MatchDevelopmentService` (the domestic games still develop him).
6. **S3 — `Player.CommercialAppeal`.** Computed (worldwide reputation × marketability from
   `MediaPersona` × a small recent-form term), bounded. Feeds: a morale/`CareerSatisfaction`-style
   term when a player is at a club that under-uses his profile; a transfer-wish nudge in
   `TransferRequestService` when a bigger-market club is interested; and a more precise version of
   the existing `SeasonFinanceService` club image-rights line.
7. **S1 — ownership groups.** `Team.OwnershipGroupId` (nullable) + a small `OwnershipGroup`
   registry seeded from the franchise leagues (the real MI / Sunrisers / Capitals / Royals
   shape). `FirstHandKnowledgeService` extended so a sister-franchise's shared history counts
   (at a discount). A **soft** group-retention bias in the auction (never a guaranteed pipeline).
   Within-group coach / staff movement gets a `RelocationComfort`-style lift. **Part (f):** a
   `StaffMember` can hold a franchise `StaffContract` (mirroring what the corrections pass just
   did for coaches) and **more than one at once across different leagues**, year-round — lift any
   implicit single-franchise assumption on staff. Cross-group scouting: **deferred** (flagged,
   lower value).

---

## Open questions for the user

1. **How far to take this pass?** Options: (a) Tier 1 only — S5 + S7 + S6 (the international /
   representation cluster); (b) Tier 1 + S2 (add the coaching structure); (c) all seven, in the
   order above; (d) a different subset.
2. **S7 irrevocability** — confirm: keep Full Member status irrevocable (recommended — the more
   interesting long-sim choice, and it matches Article 2.7)? Or model a deliberate-deviation
   demotion path?
3. **S5 national finance loop** — a *light* one (revenue in → `BoardYouthInvestment` + a national
   `Team.Finances` number the national-coach salary and a few ICC-event costs draw against), or
   leave national `Team.Finances` untouched and route ICC money **only** into
   `CountryProfile.BoardYouthInvestment` + facilities? (Recommendation: light loop — it makes the
   money mean something and is the honest place S7's "financial stability" check reads from.)
4. **S2 default structure** — should the seeded world start every nation `Unified` (and only split
   via an AI board decision under format-clash pressure), or seed a couple of nations split from
   the start? (Recommendation: all `Unified` at seed; the split emerges.)

## Verification (every slice)

`dotnet build` (0 warnings) + targeted tests (deterministic → direct; probabilistic → paired
statistical at a real sample size). End of pass: full suite **twice consecutively** clean.
Determinism discipline throughout. CLAUDE.md + README + this file updated at the end.
