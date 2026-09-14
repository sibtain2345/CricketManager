# Delivery-Type × Shot-Type Visual Taxonomy — Design Spec

**Scope**: `docs/ui-mockup/cover-point-mockup.html` (Cover Point — Match Day Live, the
per-ball "delivery focus" animation built earlier in this same session). Architectural
scope per the brainstorming skill's own classification — a new visual subsystem layered
onto the existing bezier-curve delivery-focus engine, not a bounded tweak.

## 1. Problem statement

The delivery-focus animation (bowler runs in, ball flies on a curved path, batter reacts)
was just rebuilt this session to be genuinely smooth. The user's next, larger ask: every
delivery **type** (outswinger, inswinger, bouncer, yorker, slower ball; off-break,
leg-break, googly, arm-ball) should visibly affect the flight, and every real **shot**
(cover drive, pull, hook, sweep, defensive block, leave, edge, ...) should have its own
distinct visual trajectory — not one generic curve reused for every four and every six.
Additionally: bowler **pace tier** (Fast/Fast-Medium/Medium-Fast/Medium) should be a
visible speed distinction, and bowling-arm/batting-hand should mirror correctly.

## 2. What CC14's own extracted data actually offers here (checked directly, not assumed)

Re-checked this session, specifically for this ask, before designing anything:
- `match_sprites/`: plain colored position-marker dots + a 4-frame ball-rotation
  checkerboard. No curve, no swing/turn shape, no shot-trajectory data.
- `gui/std/Panels/sidebar_bat.png` / `sidebar_bowl.png`: one static bowler-delivery
  silhouette, reused identically in both contexts.
- `.dbj` database string extractions: real position *names* and plan *names/descriptions*
  ("Pace Attacking", "Ultra defensive spin field setting") — no coordinates, no physics,
  no shot-shape data. The binary numeric portions of these tables were confirmed
  (`extracted_combined/GUIDE.md`) to be undecoded proprietary structure — genuinely
  unavailable, not merely unchecked.

**Conclusion, stated plainly**: CC14 (a 2010s text/stats management sim) never rendered
swing-curve, turn-curve, or shot-trajectory shape — there is nothing further to extract
for *this specific ask*. It remains the source for pixel *finish* (colours, chrome), not
this behaviour. The authoritative source for the behaviour itself is this project's own
C# domain: `BowlingStyle`, `DeliveryVariation` (which already names Outswinger/Inswinger/
SlowerBall/Bouncer/Yorker/Googly/ArmBall etc.), `ShotZone`'s 8-segment wagon wheel, and
`DeliveryEffectService`'s real line/length/variation → outcome composition logic. This
design translates that domain model into a 2D visual grammar; it does not invent one.

## 3. Architecture — Approach A (parametric composition), confirmed with the user

Two small, real data tables — `DELIVERY_TYPES` and `SHOT_TYPES` — each entry a set of
**visual parameters**, not a hand-drawn path. At animation time the two are **composed**
into the same multi-leg bezier structure the existing engine (`buildLegs`/`tweenLegs`,
built earlier this session) already animates, then one mirror transform is applied. This
mirrors how `DeliveryEffectService` itself reasons (compose line/length/variation effects)
rather than a hardcoded delivery×shot combination matrix (~160+ entries to hand-author
and keep consistent — rejected as Approach B).

```
pickDelivery(bowler)         -> DeliveryType record
pickShot(deliveryType)       -> ShotType record (weighted, delivery-appropriate)
composeLegs(delivery, shot, pitchOffset, paceTier) -> legs[] (the SAME buildLegs/tweenLegs
                                                       engine already built this session)
mirror(legs, bowlingArm, battingHand) -> legs'[]
```

## 4. Data — Delivery types (9 records, `DeliveryVariation`-grounded)

| id | Family | leg1 (release→pitch) bow | leg2 (pitch→bat) bow | pitch depth (pitchPt.y) | Notes |
|---|---|---|---|---|---|
| `outswinger` | Pace | +7 (toward off) | +4 | standard (300) | classic shape moved by the extracted profile |
| `inswinger` | Pace | -7 (toward leg) | -4 | standard (300) | mirror of outswinger |
| `bouncer` | Pace | +3 | +2 | short (**340**, pulled toward bowler) | bounce-pulse ring scaled 1.6x at impact |
| `yorker` | Pace | +2 | +1 | full (**252**, pulled toward bat — pitch/bat nearly merge) | minimal leg2 length |
| `slower_ball` | Pace | +3 | +2 | standard (300) | **duration multiplier 1.35x** on leg2 only — the deception is timing, not shape |
| `off_break` | Spin | +4 | +15 (off→leg) | standard (300) | matches the existing spin-bow value already shipped |
| `leg_break` | Spin | +4 | -15 (leg→off) | standard (300) | mirror of off-break |
| `googly` | Spin | +4 | +15 (same direction as off-break — the disguised ball) | standard (300) | only reachable once a leg-spinner exists (see §7) |
| `arm_ball` | Spin | +4 | +2 (≈ straight — "the one that doesn't turn") | standard (300) | |

Pace-family entries additionally read a **pace-tier speed multiplier** (§6) on both legs;
spin-family entries use a fixed spin-pace duration (unaffected by the pace tier table,
since spin bowlers aren't classified on that tier).

## 5. Data — Shot types (20 records, the real book)

Each record: `zone` (this project's own `ShotZone`), `aerial` (bool), `power` (radius
multiplier on the final leg + duration), `outcomeWeights` (dot/single/two/four/six/wicket
— replacing the old outcome-first pick, see §3/§8).

Straight Drive, Cover Drive, On Drive, Off Drive, Square Cut, Late Cut, Upper Cut, Pull,
Hook, Leg Glance, Square-Leg Whip, Sweep, Reverse Sweep, Slog Sweep, Paddle Sweep, Lofted
Drive, Slog/Heave, Defensive Block, Leave, Edge.

Representative entries (full table built at implementation time, following this exact
shape — not exhaustively hand-typed in the spec to avoid drift from the real code):

| id | Zone | Aerial | Power | Outcome lean |
|---|---|---|---|---|
| `defensive_block` | MidOff | no | very low | ~90% dot, small single/wicket tail |
| `leave` | — (no shot; ball continues on the delivery's own line) | no | n/a | 100% dot, zero risk |
| `edge` | ThirdMan (behind) | no | sharp/short | mostly wicket, small four (edge to boundary) |
| `pull` | MidWicket/SquareLeg | sometimes | high | spread four/six/dot, real wicket tail (mistimed pull) |
| `hook` | SquareLeg/FineLeg | yes | high | six/wicket-leaning, riskiest of the short-ball shots |
| `sweep` | SquareLeg | no | medium | four/single/dot, low wicket risk |
| `slog_sweep` | MidWicket | yes | high | six/wicket-leaning |
| `cover_drive` | Cover | no | medium-high | four/two/single, low wicket risk |
| `lofted_drive` | MidOff/Cover | yes | high | six/four/wicket (catchable) |

## 6. Delivery → shot pairing (realistic weighting)

One weighted table per delivery type, e.g.:
- `bouncer` → Pull 35, Hook 20, Leave 25, Upper Cut 10, Edge 10
- `yorker` → Defensive Block 45, straight Yorker-drive (Straight Drive, power-capped) 20, Edge/wicket 20, squeezed single (Leg Glance, low power) 15
- `off_break` → Sweep 20, Straight/On Drive 20, Defensive Block 25, Edge 15, Slog Sweep 10, Late Cut 10
- (full table for all 9 deliveries built at implementation time from this same pattern)

## 7. Pace tiers and mirroring

- **Pace tier multiplier** (on pace-family leg durations): Fast 0.78x, Fast-Medium 0.9x,
  Medium-Fast 1.05x, Medium 1.22x. Also scales `BOWLER_RUNUP`'s own `ms` (already a
  per-bowler field from this session's earlier work) by the same factor, so a genuine
  quick's run-up is visibly brisker than a medium-pacer's, not just the ball itself.
- **Mirroring**: swing/turn curve direction is a fixed physical fact of bowling arm +
  variation (computed once, independent of the batter). A shot's target zone mirrors
  independently off `battingHand` (RH/LH) via a single `mirrorX(offset)` applied to the
  shot-leg's control point and endpoint. Stated scope cut: the real coaching-book nuance
  where the *same* outswinger reads as "natural angle" to a RH batter and "into the
  left-hander" to a LH batter is not modelled — direction is per-arm, zone mirrors
  per-hand, independently. Correct and legible without that extra coupling.

## 8. Honest data-scope note (stated up front, not discovered later)

Today's mockup has exactly two illustrative bowlers: Iqbal (off-break family) and Awan
(pace family). `leg_break`/`googly`/`top_spinner` have no genuine leg-spin bowler to
attach to yet. **Decision**: build all 9 delivery records as real, working, independently
testable code — but only the 7 types Iqbal and Awan can genuinely bowl (Iqbal:
off_break, arm_ball; Awan: outswinger, inswinger, bouncer, yorker, slower_ball) are
reachable through today's two field-toggle buttons. `leg_break` and `googly` are real,
tested, correct code with no bowler in this mockup's current illustrative data to attach
them to. This is stated in the code's own comment, matching this project's established
discipline of naming a real data gap rather than papering over it with an invented third
character.

## 9. Testing / verification plan

Same discipline already used twice this session for the delivery-focus rebuild:
1. `verify_pass1.py` (tag-count parity, div-nesting, duplicate-id scan, `node --check`)
   after the implementation script, every time.
2. Live Playwright, forcing specific delivery×shot pairs via a temporary `Math.random`
   override (as already done for the six-outcome trace) and sampling real per-frame
   `cx`/`cy` through a full `evaluate` call — not screenshots alone — to prove each
   delivery type's curve genuinely differs in shape/depth/duration from the others, that
   mirroring flips correctly, and that pace-tier duration is visibly different between
   Iqbal (spin-pace) and Awan (his own pace tier).
3. At least one screenshot per family (a pace delivery + a spin delivery) for a direct
   visual sanity check, not relied on alone.

## 10. Deliberately out of scope for this pass

- A third (leg-spin) bowler character to make `leg_break`/`googly` reachable — a real,
  separate mockup-content addition (a new field-view SVG + roster entry), not part of
  this visual-engine design.
- The "natural angle vs. into the angle" batter-relative swing coupling (§7).
- Any change to the real C# `DeliveryEffectService`/domain code — this is a mockup-only,
  presentation-layer design; the C# simulation itself is untouched, per this project's
  standing session rule.
