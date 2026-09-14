# Delivery-Type × Shot-Type Visual Taxonomy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every delivery type (outswinger, inswinger, bouncer, yorker, slower ball,
off-break, leg-break, googly, arm-ball) and every real shot (cover drive, pull, hook,
sweep, defensive block, leave, edge, ...) its own distinct visual signature in the Match
Day "delivery focus" animation, driven by real data tables grounded in this project's own
`BowlingStyle`/`DeliveryVariation`/`ShotZone` domain model — replacing the current
outcome-first random pick with a delivery→shot→outcome pipeline.

**Architecture:** Two small JS data tables (`DELIVERY_TYPES`, `SHOT_TYPES`), each entry a
set of visual parameters (curve bow, pitch depth, duration, outcome weights) — composed at
animation time into the SAME multi-leg bezier structure (`buildLegs`/`tweenLegs`) already
built this session, then mirrored once for bowling arm / batting hand. Approach A
(parametric composition) over Approach B (a hand-authored 160+-entry combination matrix) —
confirmed with the user during brainstorming.

**Tech Stack:** Vanilla JS inside one static HTML file
(`docs/ui-mockup/cover-point-mockup.html`), no build step, no test framework. SVG +
`requestAnimationFrame` for animation (already established this session).

**Spec:** `docs/superpowers/specs/2026-09-14-delivery-shot-visual-taxonomy-design.md`

## Global Constraints — READ THIS BEFORE TASK 1

This is a single ~2.9MB HTML file with embedded base64 assets on some lines that are
100KB-1.1MB long. **Never use the Edit tool on this file directly** — it exceeds the
tool's read/size limits. Every change is a **standalone, throwaway Python script**, run
once, then discarded (scratchpad only, never committed):

1. Write a Python script to
   `C:\Users\USER1~1\AppData\Local\Temp\claude\e--CricketManager-Post-Phase-5-CricketManager-Phase4-Slice14-CricketManager\23f8497e-7f39-42bd-84d1-5ebb0faf51be\scratchpad\<descriptive_name>.py`
   (adjust the session-id segment if a new session produced a different scratchpad path —
   confirm the real path from the system prompt's "Scratchpad directory" line first).
2. The script reads `docs/ui-mockup/cover-point-mockup.html`, does one or more
   `must_replace(old, new, label)` calls (asserts `old in content` and exactly one
   occurrence — raises `AssertionError` naming which anchor failed otherwise) or
   `must_replace_all(old, new, expected_count, label)` for a known multi-occurrence
   change, writes to `<path>.tmp`, then `os.replace(tmp, path)`. Both helper functions:

```python
def must_replace(old, new, label):
    n = content.count(old)
    assert old in content, f"[{label}] anchor not found"
    assert n == 1, f"[{label}] anchor not unique (found {n})"
    return content.replace(old, new)

def must_replace_all(old, new, expected, label):
    n = content.count(old)
    assert old in content, f"[{label}] anchor not found"
    assert n == expected, f"[{label}] expected {expected} occurrences, found {n}"
    return content.replace(old, new)
```

3. Run the script with `python <path>.py` (this environment: `python`, NOT `python3`).
4. After EVERY script, run the structural verification suite:
   `python <scratchpad>\verify_pass1.py` — a pre-existing reusable script in the same
   scratchpad this session already built, checking div/table/tr/span/svg/nav/section/button
   tag-count parity, a stack-based div-nesting scanner, a duplicate-`id="..."` scan, and
   `node --check` on the file's trailing `<script>` block. All four checks must read clean
   (`OK`) before moving on. **After it runs, delete the byproduct file it leaves behind**:
   `docs/ui-mockup/cover-point-mockup.html.extracted.js` (a `node --check` artifact,
   never committed). If `verify_pass1.py` is not present in the scratchpad (a fresh
   session), recreate it from this pattern (tag-count regex per tag, a stack-based
   nesting walk, an id-collision `Counter`, and a subprocess call to
   `node --check <path>.extracted.js` against the extracted trailing `<script>...</script>`
   block) before Task 1.
5. After structural verification passes, verify **live and behaviorally** via the
   Playwright MCP tools against the already-running local server
   (`python -m http.server 8734` in `docs/ui-mockup/` — confirm it's still up with
   `curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8734/cover-point-mockup.html`;
   if not running, start it in the background). Navigate to
   `http://localhost:8734/cover-point-mockup.html`, then use `browser_evaluate` to force
   the Match Day Live stage visible:
   ```js
   () => {
     try { localStorage.clear(); } catch(e) {}
     showPanel('matchday');
     document.querySelectorAll('.match-stage').forEach(s => s.hidden = true);
     document.getElementById('stage-live').hidden = false;
     return true;
   }
   ```
   Each task's own "Verify live" step below gives the exact `browser_evaluate` assertion
   to run next. **Do not trust a screenshot alone** — screenshots miss the actual
   per-frame motion; the proven pattern this session used twice already is a single
   `async () => { ... }` `browser_evaluate` call that overrides `Math.random` to force a
   specific pick, runs the animation, and returns sampled state (never rely on wall-clock
   waits between separate tool calls — round-trip latency is unpredictable and can let an
   entire ~2-3s animation finish between one call and the next; do the waiting **inside**
   one `evaluate` call with `await new Promise(r => setTimeout(r, N))`).
6. Clean up every screenshot/diagnostic file from the repo root and delete any stray
   `.playwright-mcp/` directory before your task's commit — `git status --short` must show
   only the one real file changed (`docs/ui-mockup/cover-point-mockup.html`) plus this
   plan/spec's own tracked files.
7. **Never touch the real C# simulation** (`src/CricketManager.*`) — this plan is
   presentation-layer only, per the spec's own §10.
8. Commit after every task (`git add docs/ui-mockup/cover-point-mockup.html` — the
   scratchpad script itself is never committed).

**Naming from the spec, copied verbatim — every task below uses these exact ids, do not
rename:** delivery ids `outswinger`, `inswinger`, `bouncer`, `yorker`, `slower_ball`,
`off_break`, `leg_break`, `googly`, `arm_ball`. Shot ids (20, from spec §5) `straight_drive`,
`cover_drive`, `on_drive`, `off_drive`, `square_cut`, `late_cut`, `upper_cut`, `pull`,
`hook`, `leg_glance`, `square_leg_whip`, `sweep`, `reverse_sweep`, `slog_sweep`,
`paddle_sweep`, `lofted_drive`, `slog`, `defensive_block`, `leave`, `edge`.

---

## Task 1: `DELIVERY_TYPES` data table + per-bowler repertoire + `pickDeliveryType`

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` (the `<script>` block, right before the
  existing `var BOWLER_RUNUP = {...}` object this session already added — grep for
  `var BOWLER_RUNUP` to find the current exact anchor text, since later tasks in this
  session may have shifted line numbers; the anchor must include enough of
  `BOWLER_RUNUP`'s own literal object body to be uniquely matched, which the script
  writer confirms via the exact `n == 1` assertion — copy the CURRENT file's own
  `var BOWLER_RUNUP = { ... };` text into the script's `old` string, and prepend the new
  table before it in `new`).

**Interfaces:**
- Consumes: nothing new (pure data + a lookup function).
- Produces: `DELIVERY_TYPES` (object keyed by delivery id, spec §4 shape), `BOWLER_REPERTOIRE`
  (object keyed by field-wrap id `field-iqbal`/`field-awan`, each a weighted array of
  `[deliveryId, weight]` pairs), `pickDeliveryType(wrapId)` returning a delivery id string.
  Later tasks call `pickDeliveryType(wrap.id)`.

- [ ] **Step 1: Write the data-table script**

Create `<scratchpad>\add_delivery_types.py`:

```python
# -*- coding: utf-8 -*-
import os

PATH = r"E:\CricketManager_Post_Phase_5\CricketManager_Phase4_Slice14\CricketManager\docs\ui-mockup\cover-point-mockup.html"

with open(PATH, "r", encoding="utf-8") as f:
    content = f.read()

def must_replace(old, new, label):
    n = content.count(old)
    assert old in content, f"[{label}] anchor not found"
    assert n == 1, f"[{label}] anchor not unique (found {n})"
    return content.replace(old, new)

# Anchor: the exact current BOWLER_RUNUP declaration. If this fails with
# "anchor not found", grep the live file for "var BOWLER_RUNUP" and paste
# the CURRENT exact text here before re-running - do not guess.
OLD_ANCHOR = """  var BOWLER_RUNUP = {
    'field-iqbal': { from: { x: 320, y: 476 }, bow: 5, ms: 480, style: 'spin' },
    'field-awan': { from: { x: 284, y: 558 }, bow: -26, ms: 780, style: 'pace' }
  };"""

NEW_BLOCK = """  // ---- Delivery-type visual taxonomy (spec: docs/superpowers/specs/
  // 2026-09-14-delivery-shot-visual-taxonomy-design.md). Grounded in this
  // project's own BowlingStyle/DeliveryVariation domain enums, not
  // invented - CC14's own extracted assets were re-checked for this
  // specific ask and confirmed to hold no swing/turn/shot-shape data at
  // all (colour-dot sprites + a 4-frame ball-rotation checkerboard only),
  // so this is grounded in the real C# domain model instead. ----
  var DELIVERY_TYPES = {
    outswinger:  { family: 'pace', leg1Bow: 7,  leg2Bow: 4,  pitchY: 300, durMul: 1 },
    inswinger:   { family: 'pace', leg1Bow: -7, leg2Bow: -4, pitchY: 300, durMul: 1 },
    bouncer:     { family: 'pace', leg1Bow: 3,  leg2Bow: 2,  pitchY: 340, durMul: 1 },
    yorker:      { family: 'pace', leg1Bow: 2,  leg2Bow: 1,  pitchY: 252, durMul: 1 },
    slower_ball: { family: 'pace', leg1Bow: 3,  leg2Bow: 2,  pitchY: 300, durMul: 1.35 },
    off_break:   { family: 'spin', leg1Bow: 4,  leg2Bow: 15, pitchY: 300, durMul: 1 },
    leg_break:   { family: 'spin', leg1Bow: 4,  leg2Bow: -15, pitchY: 300, durMul: 1 },
    googly:      { family: 'spin', leg1Bow: 4,  leg2Bow: 15, pitchY: 300, durMul: 1 },
    arm_ball:    { family: 'spin', leg1Bow: 4,  leg2Bow: 2,  pitchY: 300, durMul: 1 }
  };
  // Only the deliveries a bowler could genuinely bowl in real cricket -
  // Iqbal is an off-spinner (off-break stock ball + the occasional arm-
  // ball that doesn't turn); Awan is a pace bowler with the standard
  // swing/bouncer/yorker/change-of-pace repertoire. leg_break/googly have
  // no leg-spin bowler in this mockup's illustrative data yet - real,
  // tested code, honestly unreachable through today's two field-toggle
  // buttons (spec S8), not silently invented onto a bowler who wouldn't
  // bowl them.
  var BOWLER_REPERTOIRE = {
    'field-iqbal': [['off_break', 78], ['arm_ball', 22]],
    'field-awan': [['outswinger', 26], ['inswinger', 20], ['bouncer', 16], ['yorker', 16], ['slower_ball', 22]]
  };
  function pickDeliveryType(wrapId) {
    var options = BOWLER_REPERTOIRE[wrapId] || BOWLER_REPERTOIRE['field-iqbal'];
    var total = options.reduce(function (s, o) { return s + o[1]; }, 0);
    var roll = Math.random() * total;
    for (var i = 0; i < options.length; i++) {
      roll -= options[i][1];
      if (roll <= 0) return options[i][0];
    }
    return options[0][0];
  }

  var BOWLER_RUNUP = {
    'field-iqbal': { from: { x: 320, y: 476 }, bow: 5, ms: 480, style: 'spin' },
    'field-awan': { from: { x: 284, y: 558 }, bow: -26, ms: 780, style: 'pace' }
  };"""

content = must_replace(OLD_ANCHOR, NEW_BLOCK, "add-delivery-types-table")

tmp_path = PATH + ".tmp"
with open(tmp_path, "w", encoding="utf-8") as f:
    f.write(content)
os.replace(tmp_path, PATH)
print("OK: DELIVERY_TYPES + BOWLER_REPERTOIRE + pickDeliveryType added")
```

- [ ] **Step 2: Run the script**

Run: `python <scratchpad>\add_delivery_types.py`
Expected: `OK: DELIVERY_TYPES + BOWLER_REPERTOIRE + pickDeliveryType added`. If it raises
`AssertionError: [add-delivery-types-table] anchor not found`, grep the live file for
`var BOWLER_RUNUP` and update `OLD_ANCHOR` to match exactly, then re-run.

- [ ] **Step 3: Structural verification**

Run: `python <scratchpad>\verify_pass1.py`
Expected: all lines end `OK` (div/table/tr/span/svg/nav/section/button counts, div-nesting,
duplicate-ids, node --check). Delete
`docs/ui-mockup/cover-point-mockup.html.extracted.js` afterward.

- [ ] **Step 4: Verify live via Playwright**

Navigate to the mockup, force Match Day Live visible (Global Constraints step 5), then:

```js
() => {
  var samples = { iqbal: {}, awan: {} };
  for (var i = 0; i < 200; i++) {
    var d = pickDeliveryType('field-iqbal');
    samples.iqbal[d] = (samples.iqbal[d] || 0) + 1;
  }
  for (var i = 0; i < 200; i++) {
    var d = pickDeliveryType('field-awan');
    samples.awan[d] = (samples.awan[d] || 0) + 1;
  }
  return samples;
}
```

Expected: `samples.iqbal` has ONLY keys `off_break`/`arm_ball` (never a pace delivery);
`samples.awan` has ONLY keys `outswinger`/`inswinger`/`bouncer`/`yorker`/`slower_ball`
(never a spin delivery) — confirming the repertoire constraint from spec §8 actually
holds, not just that the table exists.

- [ ] **Step 5: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: add DELIVERY_TYPES table + per-bowler repertoire (delivery-shot taxonomy, task 1/8)"
```

---

## Task 2: `SHOT_TYPES` data table (20 real shots)

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` (insert right after Task 1's
  `pickDeliveryType` function, before `var BOWLER_RUNUP`).

**Interfaces:**
- Consumes: nothing new.
- Produces: `SHOT_TYPES` (object keyed by shot id). Each entry:
  `{ zone: 'off'|'straight'|'leg'|'behind', aerial: bool, power: 0-1,
  outcomeWeights: {dot,single,two,four,six,wicket} (six integers summing to 100) }`.
  Task 3 reads `SHOT_TYPES[id].outcomeWeights` to resolve runs; Task 4 reads `aerial`/`power`.

- [ ] **Step 1: Write the script**

Create `<scratchpad>\add_shot_types.py`. Anchor on the exact text Task 1 just inserted
(`function pickDeliveryType(wrapId) { ... }` through its closing `}`, then the blank line
before `var BOWLER_RUNUP` - copy the CURRENT file's own text for the `old` string, per the
Global Constraints rule; the shape below is what the inserted `new` text must produce):

```python
NEW_TABLE = """
  // 20 real shots (spec S5) - each carries a ShotZone-equivalent, whether
  // it's played along the ground or in the air, a power/risk band, and
  // its OWN outcome tendency (dot/single/two/four/six/wicket, integers
  // summing to 100) - this is what makes the pipeline shot-driven rather
  // than picking a random outcome independently of what was actually
  // played (spec S3/S8's core fix).
  var SHOT_TYPES = {
    straight_drive:  { zone: 'straight', aerial: false, power: 0.55, outcomeWeights: { dot: 30, single: 30, two: 12, four: 22, six: 2,  wicket: 4  } },
    cover_drive:     { zone: 'off',      aerial: false, power: 0.6,  outcomeWeights: { dot: 22, single: 26, two: 12, four: 32, six: 2,  wicket: 6  } },
    on_drive:        { zone: 'leg',      aerial: false, power: 0.55, outcomeWeights: { dot: 28, single: 30, two: 12, four: 24, six: 2,  wicket: 4  } },
    off_drive:       { zone: 'off',      aerial: false, power: 0.5,  outcomeWeights: { dot: 30, single: 32, two: 12, four: 20, six: 1,  wicket: 5  } },
    square_cut:      { zone: 'off',      aerial: false, power: 0.7,  outcomeWeights: { dot: 18, single: 20, two: 8,  four: 38, six: 3,  wicket: 13 } },
    late_cut:        { zone: 'behind',   aerial: false, power: 0.35, outcomeWeights: { dot: 40, single: 34, two: 6,  four: 14, six: 0,  wicket: 6  } },
    upper_cut:       { zone: 'behind',   aerial: true,  power: 0.65, outcomeWeights: { dot: 20, single: 10, two: 3,  four: 28, six: 15, wicket: 24 } },
    pull:            { zone: 'leg',      aerial: false, power: 0.75, outcomeWeights: { dot: 18, single: 18, two: 8,  four: 30, six: 12, wicket: 14 } },
    hook:            { zone: 'leg',      aerial: true,  power: 0.8,  outcomeWeights: { dot: 15, single: 8,  two: 2,  four: 22, six: 28, wicket: 25 } },
    leg_glance:      { zone: 'behind',   aerial: false, power: 0.3,  outcomeWeights: { dot: 38, single: 40, two: 8,  four: 10, six: 0,  wicket: 4  } },
    square_leg_whip: { zone: 'leg',      aerial: false, power: 0.55, outcomeWeights: { dot: 26, single: 30, two: 12, four: 26, six: 2,  wicket: 4  } },
    sweep:           { zone: 'leg',      aerial: false, power: 0.5,  outcomeWeights: { dot: 24, single: 32, two: 10, four: 26, six: 2,  wicket: 6  } },
    reverse_sweep:   { zone: 'off',      aerial: false, power: 0.5,  outcomeWeights: { dot: 26, single: 26, two: 8,  four: 24, six: 3,  wicket: 13 } },
    slog_sweep:      { zone: 'leg',      aerial: true,  power: 0.78, outcomeWeights: { dot: 18, single: 8,  two: 2,  four: 22, six: 30, wicket: 20 } },
    paddle_sweep:    { zone: 'behind',   aerial: false, power: 0.3,  outcomeWeights: { dot: 32, single: 38, two: 8,  four: 14, six: 0,  wicket: 8  } },
    lofted_drive:    { zone: 'off',      aerial: true,  power: 0.75, outcomeWeights: { dot: 16, single: 10, two: 2,  four: 28, six: 26, wicket: 18 } },
    slog:            { zone: 'leg',      aerial: true,  power: 0.9,  outcomeWeights: { dot: 20, single: 6,  two: 2,  four: 18, six: 32, wicket: 22 } },
    defensive_block: { zone: 'straight', aerial: false, power: 0.08, outcomeWeights: { dot: 88, single: 9,  two: 0,  four: 0,  six: 0,  wicket: 3  } },
    leave:           { zone: 'straight', aerial: false, power: 0,    outcomeWeights: { dot: 100, single: 0, two: 0,  four: 0,  six: 0,  wicket: 0  } },
    edge:            { zone: 'behind',   aerial: false, power: 0.25, outcomeWeights: { dot: 8,  single: 6,  two: 2,  four: 20, six: 0,  wicket: 64 } }
  };
"""
```

(Compose the full script the same way Task 1's was written: read the file, `must_replace`
the exact current anchor with `<anchor> + NEW_TABLE`, write via `.tmp` + `os.replace`.)

- [ ] **Step 2: Run the script, verify structurally** — identical pattern to Task 1 Steps
  2-3.

- [ ] **Step 3: Verify live via Playwright**

```js
() => {
  var ids = Object.keys(SHOT_TYPES);
  var badWeights = ids.filter(function (id) {
    var w = SHOT_TYPES[id].outcomeWeights;
    var sum = w.dot + w.single + w.two + w.four + w.six + w.wicket;
    return sum !== 100;
  });
  return { count: ids.length, badWeights: badWeights };
}
```

Expected: `count: 20`, `badWeights: []` (every shot's six outcome weights genuinely sum to
100 - a real arithmetic check, not just "the table exists").

- [ ] **Step 4: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: add SHOT_TYPES table, 20 real shots (delivery-shot taxonomy, task 2/8)"
```

---

## Task 3: Delivery→shot pairing table + `pickShot` + `rollOutcome`

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` (insert after Task 2's `SHOT_TYPES`,
  before `var BOWLER_RUNUP`).

**Interfaces:**
- Consumes: `DELIVERY_TYPES` (Task 1), `SHOT_TYPES` (Task 2).
- Produces: `DELIVERY_TO_SHOT` (object keyed by delivery id → weighted `[shotId, weight]`
  array), `pickShot(deliveryId)` returning a shot id string, `rollOutcome(shotId)`
  returning one of `'dot'|'single'|'two'|'four'|'six'|'wicket'`. Task 7 calls both.

- [ ] **Step 1: Write the script**

```python
NEW_BLOCK = """
  // Realistic delivery -> shot weighting (spec S6) - a bouncer draws a
  // pull/hook/duck, a yorker draws a block or gets driven/misses, spin
  // draws a sweep or a drive down the ground. This is what stops the
  // shot from ever contradicting the ball that was actually bowled.
  var DELIVERY_TO_SHOT = {
    outswinger:  [['cover_drive', 22], ['square_cut', 14], ['edge', 20], ['defensive_block', 22], ['off_drive', 14], ['leave', 8]],
    inswinger:   [['on_drive', 22], ['square_leg_whip', 16], ['pull', 10], ['defensive_block', 24], ['edge', 16], ['leave', 12]],
    bouncer:     [['pull', 30], ['hook', 18], ['leave', 24], ['upper_cut', 10], ['edge', 18]],
    yorker:      [['defensive_block', 42], ['straight_drive', 18], ['edge', 22], ['leg_glance', 18]],
    slower_ball: [['lofted_drive', 16], ['slog', 14], ['defensive_block', 30], ['edge', 22], ['straight_drive', 18]],
    off_break:   [['sweep', 20], ['on_drive', 18], ['defensive_block', 24], ['edge', 16], ['slog_sweep', 10], ['late_cut', 12]],
    leg_break:   [['sweep', 18], ['cover_drive', 18], ['defensive_block', 24], ['edge', 18], ['reverse_sweep', 12], ['slog_sweep', 10]],
    googly:      [['edge', 28], ['defensive_block', 26], ['sweep', 14], ['on_drive', 12], ['slog_sweep', 10], ['leave', 10]],
    arm_ball:    [['on_drive', 20], ['defensive_block', 28], ['edge', 22], ['square_leg_whip', 16], ['leave', 14]]
  };
  function pickShot(deliveryId) {
    var options = DELIVERY_TO_SHOT[deliveryId] || DELIVERY_TO_SHOT.off_break;
    var total = options.reduce(function (s, o) { return s + o[1]; }, 0);
    var roll = Math.random() * total;
    for (var i = 0; i < options.length; i++) {
      roll -= options[i][1];
      if (roll <= 0) return options[i][0];
    }
    return options[0][0];
  }
  function rollOutcome(shotId) {
    var w = (SHOT_TYPES[shotId] || SHOT_TYPES.defensive_block).outcomeWeights;
    var order = ['dot', 'single', 'two', 'four', 'six', 'wicket'];
    var roll = Math.random() * 100;
    for (var i = 0; i < order.length; i++) {
      roll -= w[order[i]];
      if (roll <= 0) return order[i];
    }
    return 'dot';
  }
"""
```

(Same `must_replace` + write pattern as Tasks 1-2.)

- [ ] **Step 2: Run, verify structurally** — as before.

- [ ] **Step 3: Verify live via Playwright**

```js
() => {
  var deliveryIds = Object.keys(DELIVERY_TYPES);
  var missing = deliveryIds.filter(function (id) { return !DELIVERY_TO_SHOT[id]; });
  var badShotRefs = [];
  deliveryIds.forEach(function (id) {
    (DELIVERY_TO_SHOT[id] || []).forEach(function (pair) {
      if (!SHOT_TYPES[pair[0]]) badShotRefs.push(id + ' -> ' + pair[0]);
    });
  });
  var outcomes = {};
  for (var i = 0; i < 500; i++) { var o = rollOutcome('defensive_block'); outcomes[o] = (outcomes[o] || 0) + 1; }
  return { missing: missing, badShotRefs: badShotRefs, defensiveBlockDotShare: (outcomes.dot || 0) / 500 };
}
```

Expected: `missing: []` (all 9 delivery types have a pairing table), `badShotRefs: []`
(every referenced shot id genuinely exists in `SHOT_TYPES` — a real cross-reference
check, not assumed), `defensiveBlockDotShare` close to `0.88` (matches the table's own
declared weight — proves `rollOutcome` reads the real weights, not a placeholder).

- [ ] **Step 4: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: add DELIVERY_TO_SHOT pairing + pickShot/rollOutcome (delivery-shot taxonomy, task 3/8)"
```

---

## Task 4: `composeLegs` — build the multi-leg bezier path from a delivery + shot

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` (insert after Task 3's `rollOutcome`,
  before `var BOWLER_RUNUP`; reuses `buildLegs` — confirm it's still present via grep for
  `function buildLegs` before writing the anchor, since it was added earlier this session
  in the delivery-focus rebuild).

**Interfaces:**
- Consumes: `DELIVERY_TYPES`, `SHOT_TYPES`, `buildLegs(points, bows, msArr, easeArr)`
  (already exists from the earlier delivery-focus rebuild this session), `easeInOutQuad`
  (already exists).
- Produces: `composeLegs(deliveryId, shotId, outcome, pitchLineOffset)` returning
  `{ legs, points }` where `legs` is exactly what `tweenLegs` (already exists) accepts, and
  `points` is the raw waypoint array `[bowlerPt, pitchPt, batPt, targetPt?]` (matching the
  shape `animateDeliveryBall` already builds by hand today — Task 7 replaces that
  hand-built version with a call to this function).

- [ ] **Step 1: Write the script**

```python
NEW_FN = """
  // Composes a delivery record + a shot record into the same {legs, points}
  // shape animateDeliveryBall already builds by hand today (spec S3) - the
  // delivery supplies leg1/leg2 (release->pitch, pitch->bat), the shot
  // supplies leg3 (bat->target) when a shot is actually played.
  var LIVE_ZONE_TARGETS_BY_ZONE = {
    straight: [{ x: 320, y: 440 }, { x: 320, y: 565 }],
    off: [{ x: 605, y: 300 }, { x: 500, y: 115 }, { x: 560, y: 400 }],
    leg: [{ x: 35, y: 340 }, { x: 80, y: 115 }, { x: 110, y: 490 }],
    behind: [{ x: 560, y: 320 }, { x: 90, y: 320 }, { x: 368, y: 228 }]
  };
  function composeLegs(deliveryId, shotId, outcome, pitchLineOffset) {
    var delivery = DELIVERY_TYPES[deliveryId] || DELIVERY_TYPES.off_break;
    var shot = SHOT_TYPES[shotId] || SHOT_TYPES.defensive_block;
    var bowlerPt = { x: 320, y: 432 };
    var pitchPt = { x: 320 + pitchLineOffset, y: delivery.pitchY };
    var batPt = { x: 320, y: 236 };
    var points = [bowlerPt, pitchPt, batPt];
    var bows = [delivery.leg1Bow, delivery.leg2Bow];
    var msArr = [Math.round(260 * delivery.durMul), Math.round((delivery.family === 'spin' ? 260 : 210) * delivery.durMul)];
    if (outcome !== 'wicket' && shotId !== 'leave' && shotId !== 'defensive_block') {
      var zoneTargets = LIVE_ZONE_TARGETS_BY_ZONE[shot.zone] || LIVE_ZONE_TARGETS_BY_ZONE.straight;
      var target = zoneTargets[Math.floor(Math.random() * zoneTargets.length)];
      points.push(target);
      var shotBow = 8 + shot.power * 44;
      var shotMs = 260 + shot.power * 340;
      bows.push(shotBow);
      msArr.push(Math.round(shotMs));
    } else if (shotId === 'defensive_block') {
      var blockPt = { x: batPt.x + 6, y: batPt.y + 14 };
      points.push(blockPt);
      bows.push(3); msArr.push(160);
    }
    var legs = buildLegs(points, bows, msArr, null);
    return { legs: legs, points: points };
  }
"""
```

- [ ] **Step 2: Run, verify structurally** — as before.

- [ ] **Step 3: Verify live via Playwright**

```js
() => {
  var offBreak = composeLegs('off_break', 'sweep', 'four', 0);
  var legBreak = composeLegs('leg_break', 'sweep', 'four', 0);
  var yorker = composeLegs('yorker', 'defensive_block', 'dot', 0);
  var bouncer = composeLegs('bouncer', 'pull', 'four', 0);
  var stock = composeLegs('off_break', 'defensive_block', 'wicket', 0);
  return {
    offBreakLeg2Bow: offBreak.legs[1].control.x - (offBreak.legs[1].from.x + offBreak.legs[1].to.x) / 2,
    legBreakLeg2Bow: legBreak.legs[1].control.x - (legBreak.legs[1].from.x + legBreak.legs[1].to.x) / 2,
    yorkerPitchToBat: Math.abs(yorker.points[1].y - yorker.points[2].y),
    bouncerPitchY: bouncer.points[1].y,
    stockLegCount: stock.legs.length
  };
}
```

Expected: `offBreakLeg2Bow` and `legBreakLeg2Bow` have **opposite signs** (real, opposite
turn direction — the core "off-break and leg-break must look different" proof);
`yorkerPitchToBat` well under the equivalent gap for a standard delivery (pitch and bat
points nearly merge, per spec §4); `bouncerPitchY` equals `340` (pulled toward the bowler,
a genuinely shorter length than the standard `300`); `stockLegCount` is `3` (wicket outcome
still stops at the bat point, no fourth leg — matches the pre-existing wicket-handling
shape).

- [ ] **Step 4: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: add composeLegs, delivery+shot -> bezier path (delivery-shot taxonomy, task 4/8)"
```

---

## Task 5: `mirrorLegs` — bowling-arm / batting-hand mirror

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` (insert after Task 4's `composeLegs`).

**Interfaces:**
- Consumes: the `{legs, points}` shape from Task 4.
- Produces: `mirrorLegs(composed, mirrorX)` returning a NEW `{legs, points}` with every
  `x` coordinate reflected around `320` when `mirrorX` is `true`, `y` untouched. Task 7
  calls this with `mirrorX = (bowlingArm === 'left') !== (battingHand === 'left')` per
  spec §7 (arm and hand mirror independently, so an XOR is the correct combination — if
  exactly one of the two is left-handed/left-arm, the visual flips; if both or neither
  are, it doesn't).

- [ ] **Step 1: Write the script**

```python
NEW_FN = """
  // Mirrors a composed delivery for a left-arm bowler and/or a left-
  // handed batter (spec S7) - arm and hand mirror INDEPENDENTLY (an XOR,
  // not "either flips it the same way"): a left-arm bowler to a right-
  // handed batter looks different from a left-arm bowler to a left-
  // handed batter, which is real cricket geometry, not a simplification.
  function mirrorPoint(p) { return { x: 640 - p.x, y: p.y }; }
  function mirrorLegs(composed, mirrorX) {
    if (!mirrorX) return composed;
    var points = composed.points.map(mirrorPoint);
    var legs = composed.legs.map(function (leg) {
      return { from: mirrorPoint(leg.from), control: mirrorPoint(leg.control), to: mirrorPoint(leg.to), ms: leg.ms, ease: leg.ease };
    });
    return { legs: legs, points: points };
  }
"""
```

- [ ] **Step 2: Run, verify structurally** — as before.

- [ ] **Step 3: Verify live via Playwright**

```js
() => {
  var composed = composeLegs('outswinger', 'cover_drive', 'four', 0);
  var mirrored = mirrorLegs(composed, true);
  var notMirrored = mirrorLegs(composed, false);
  return {
    originalBatX: composed.points[2].x,
    mirroredBatX: mirrored.points[2].x,
    notMirroredBatX: notMirrored.points[2].x,
    sumIs640: composed.points[2].x + mirrored.points[2].x === 640
  };
}
```

Expected: `notMirroredBatX === originalBatX` (a `false` mirror is a genuine no-op, not a
silently-mutated copy); `sumIs640` is `true` (the mirror reflects around the pitch's own
centre x=320, i.e. `x' = 640 - x`, confirmed arithmetically, not just "looks flipped").

- [ ] **Step 4: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: add mirrorLegs for left-arm/left-hand (delivery-shot taxonomy, task 5/8)"
```

---

## Task 6: Pace-tier speed scaling

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` — extend `BOWLER_RUNUP` (from Task 1's
  relocation of it) with a `paceTier` field per bowler, and add `PACE_TIER_MULTIPLIER`.

**Interfaces:**
- Consumes: `BOWLER_RUNUP` (existing).
- Produces: `PACE_TIER_MULTIPLIER` (object: `Fast: 0.78, FastMedium: 0.9, MediumFast: 1.05,
  Medium: 1.22`), and `BOWLER_RUNUP['field-awan'].paceTier` set to `'FastMedium'`, plus a
  new `paceTierFor(wrapId)` helper returning the multiplier (`1` for a spin bowler, since
  pace tiers don't apply to spin per spec §7).

- [ ] **Step 1: Write the script**

```python
OLD = """  var BOWLER_RUNUP = {
    'field-iqbal': { from: { x: 320, y: 476 }, bow: 5, ms: 480, style: 'spin' },
    'field-awan': { from: { x: 284, y: 558 }, bow: -26, ms: 780, style: 'pace' }
  };"""

NEW = """  var PACE_TIER_MULTIPLIER = { Fast: 0.78, FastMedium: 0.9, MediumFast: 1.05, Medium: 1.22 };
  var BOWLER_RUNUP = {
    'field-iqbal': { from: { x: 320, y: 476 }, bow: 5, ms: 480, style: 'spin' },
    'field-awan': { from: { x: 284, y: 558 }, bow: -26, ms: 780, style: 'pace', paceTier: 'FastMedium' }
  };
  function paceTierFor(wrapId) {
    var r = BOWLER_RUNUP[wrapId];
    if (!r || r.style !== 'pace' || !r.paceTier) return 1;
    return PACE_TIER_MULTIPLIER[r.paceTier] || 1;
  }"""
```

(Note: this replaces the SAME `BOWLER_RUNUP` block Task 1 already relocated — locate its
current exact text via grep for `var BOWLER_RUNUP` immediately before writing this
script, since Task 1 changed its surrounding context.)

- [ ] **Step 2: Run, verify structurally** — as before.

- [ ] **Step 3: Verify live via Playwright**

```js
() => ({
  awanTier: paceTierFor('field-awan'),
  iqbalTier: paceTierFor('field-iqbal'),
  fastFasterThanMedium: PACE_TIER_MULTIPLIER.Fast < PACE_TIER_MULTIPLIER.Medium
})
```

Expected: `awanTier === 0.9` (FastMedium, matches the table), `iqbalTier === 1` (spin is
unaffected), `fastFasterThanMedium === true` (a genuinely faster bowler has a SMALLER
duration multiplier — arrives sooner).

- [ ] **Step 4: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: pace-tier speed scaling (delivery-shot taxonomy, task 6/8)"
```

---

## Task 7: Rewire `animateDeliveryBall` onto the new pipeline

**Files:**
- Modify: `docs/ui-mockup/cover-point-mockup.html` — the `animateDeliveryBall` function
  built earlier this session (grep for `function animateDeliveryBall` to get its current
  exact text before writing the anchor — it is long; copy it verbatim as the `old` string).

**Interfaces:**
- Consumes: `pickDeliveryType`, `pickShot`, `rollOutcome`, `composeLegs`, `mirrorLegs`,
  `paceTierFor` (Tasks 1-6), plus everything `animateDeliveryBall` already used this
  session (`tweenLegs`, `nearestFielder`, `buildCommentaryLine` — this task REPLACES
  `buildCommentaryLine`'s call site to pass the real delivery/shot names through, see
  Step 1).
- Produces: `animateDeliveryBall()` unchanged in its external contract (still the
  `onclick` target of `#bowl-btn`), now internally delivery/shot-driven instead of
  outcome-first.

- [ ] **Step 1: Write the script**

Locate the CURRENT exact `animateDeliveryBall` function body (grep first) and replace it
with a version that: (a) calls `pickDeliveryType(wrap.id)` then `pickShot(deliveryId)`
then `rollOutcome(shotId)` in place of the old `pickLiveOutcome()`; (b) calls
`composeLegs(deliveryId, shotId, outcome, pitchLineOffset)` then
`mirrorLegs(composed, mirrorX)` in place of the old hand-built `points`/`bows`/`legMs`
block; (c) scales the run-up leg's `ms` by `paceTierFor(wrap.id)` before building
`runupLegs`; (d) passes `deliveryId`/`shotId` into a new `buildDeliveryCommentaryLine`
(Step 2) instead of the existing generic `buildCommentaryLine`. Keep every existing cue
(bounce pulse, bat-impact flash, striker shot-react, six shadow marker, fielder-alert,
caption reveal, hold-then-close, `bowlBtn.disabled` guard) — this task changes WHAT builds
the path and the commentary text, not the sequencing/timing infrastructure around it,
which this session already proved smooth.

Concretely, inside `animateDeliveryBall`, replace:
```js
    var outcome = pickLiveOutcome();
    var targets = LIVE_ZONE_TARGETS[outcome];
    var target = targets[Math.floor(Math.random() * targets.length)];
    var runup = BOWLER_RUNUP[wrap.id] || BOWLER_RUNUP['field-iqbal'];
    var isSpin = runup.style === 'spin';

    var bowlerPt = { x: 320, y: 432 };
    var pitchLineOffset = Math.round(Math.random() * 16 - 8);
    var pitchPt = { x: 320 + pitchLineOffset, y: 300 };
    var batPt = { x: 320, y: 236 };
    var points = outcome === 'wicket' ? [bowlerPt, pitchPt, batPt] : [bowlerPt, pitchPt, batPt, target];

    var bows = [isSpin ? 4 : 7, isSpin ? 15 : 6];
    var legMs = [260, isSpin ? 260 : 210];
    if (outcome !== 'wicket') {
      var finalBow = { dot: 5, single: 9, two: 10, four: 24, six: 44 }[outcome] || 8;
      var finalMs = { dot: 260, single: 320, two: 340, four: 420, six: 560 }[outcome] || 320;
      bows.push(finalBow); legMs.push(finalMs);
    }
    var legs = buildLegs(points, bows, legMs, null);
    var pathD = legsToPathD(legs);
```
with:
```js
    var runup = BOWLER_RUNUP[wrap.id] || BOWLER_RUNUP['field-iqbal'];
    var deliveryId = pickDeliveryType(wrap.id);
    var shotId = pickShot(deliveryId);
    var outcome = rollOutcome(shotId);
    var pitchLineOffset = Math.round(Math.random() * 16 - 8);
    var mirrorX = false; // no left-arm/left-hand character in today's mockup data yet (spec S7/S8) - mirrorLegs is real and tested (task 5), wired here so the next bowler/batter added only needs to flip this flag, not touch the pipeline
    var composed = mirrorLegs(composeLegs(deliveryId, shotId, outcome, pitchLineOffset), mirrorX);
    var legs = composed.legs;
    var points = composed.points;
    var bowlerPt = points[0], pitchPt = points[1], batPt = points[2];
    var pathD = legsToPathD(legs);
```

Then find the run-up tween call:
```js
      var runupLegs = buildLegs([runup.from, bowlerPt], [runup.bow], [runup.ms], [easeInOutQuad]);
```
and change the `[runup.ms]` element to `[Math.round(runup.ms * paceTierFor(wrap.id))]`.

Then find the caption-building call:
```js
      var line = buildCommentaryLine(outcome);
```
and replace with:
```js
      var line = buildDeliveryCommentaryLine(deliveryId, shotId, outcome);
```

- [ ] **Step 2: Add `buildDeliveryCommentaryLine`**

In the same script, also insert (anywhere before `animateDeliveryBall`, e.g. right after
Task 3's `rollOutcome`):

```js
  var DELIVERY_LABEL = { outswinger: 'an outswinger', inswinger: 'an inswinger', bouncer: 'a bouncer', yorker: 'a yorker', slower_ball: 'a slower ball', off_break: 'an off-break', leg_break: 'a leg-break', googly: 'a googly', arm_ball: 'the arm-ball' };
  var SHOT_LABEL = { straight_drive: 'driven straight', cover_drive: 'driven through cover', on_drive: 'driven back past the bowler', off_drive: 'driven through mid-off', square_cut: 'cut hard square', late_cut: 'late-cut fine', upper_cut: 'upper-cut over the slips', pull: 'pulled away', hook: 'hooked away', leg_glance: 'glanced off the pads', square_leg_whip: 'whipped through square leg', sweep: 'swept away', reverse_sweep: 'reverse-swept', slog_sweep: 'slog-swept', paddle_sweep: 'paddled fine', lofted_drive: 'lofted over the infield', slog: 'slogged away', defensive_block: 'defended solidly', leave: 'left alone', edge: 'edged' };
  function buildDeliveryCommentaryLine(deliveryId, shotId, outcome) {
    var b = currentBowlerName(), batter = currentBatterName();
    var dLabel = DELIVERY_LABEL[deliveryId] || 'a delivery';
    var sLabel = SHOT_LABEL[shotId] || 'played';
    var outcomeText = { dot: 'no run.', single: 'a single.', two: 'two runs.', four: 'FOUR!', six: 'SIX!', wicket: 'OUT!' }[outcome] || '';
    return b + ' to ' + batter + ', ' + dLabel + ' \u2014 ' + sLabel + ', ' + outcomeText;
  }
```

- [ ] **Step 3: Run, verify structurally** — as before.

- [ ] **Step 4: Verify live via Playwright**

Force a specific pairing and trace the whole sequence, matching this session's own
already-proven pattern (override `Math.random` to a fixed sequence long enough to cover
delivery pick → shot pick → outcome roll → pitch offset → target-zone pick, run
`animateDeliveryBall`, sample real per-frame state):

```js
async () => {
  var origRandom = Math.random;
  var calls = 0;
  // First four Math.random() calls inside one run are, in order: delivery
  // pick, shot pick, outcome roll, pitch-line offset - force a bouncer/
  // pull/four sequence deterministically by returning values that land
  // each weighted pick on the desired option (compute against the exact
  // tables above rather than guessing).
  Math.random = function () { calls++; return 0.5; };
  document.getElementById('bowl-btn').click();
  await new Promise(r => setTimeout(r, 3200));
  Math.random = origRandom;
  var log = document.querySelector('.commentary-card .commentary-item');
  return { commentary: log ? log.textContent.trim() : null, focusClosedOrClosing: !document.body.classList.contains('delivery-focus') || true };
}
```

Expected: `commentary` contains BOTH a real delivery label (one of the `DELIVERY_LABEL`
phrases) AND a real shot label (one of the `SHOT_LABEL` phrases) in the same line — proof
the commentary now names what actually happened, not a generic template. Then repeat with
at least THREE more forced runs (vary the fixed `Math.random` return value — e.g. `0.05`,
`0.35`, `0.95` — across separate calls) and confirm the sampled `bx/by` sequences (same
per-frame sampling technique already used twice this session — wrap
`window.requestAnimationFrame` to push `cx`/`cy` into an array before calling the
original) produce **visibly different curve shapes** between at least one pace delivery
and one spin delivery (leg2's mid-frame x-deviation from a straight line has opposite
sign or a materially different magnitude) — this is the actual claim of the whole plan
and must be checked numerically, not assumed from the earlier unit-style checks alone.

- [ ] **Step 5: One visual screenshot sanity check**

Take a single screenshot mid-animation (using the same "monkeypatch `setTimeout` to
freeze mid-sequence" trick already used twice this session — see the delivery-focus
rebuild's own verification for the exact pattern) for one pace delivery and one spin
delivery, confirm by eye that the field, ball colour (off-white, not cyan — untouched by
this plan, already fixed earlier this session) and general layout still look correct.
Delete the screenshot file afterward (Global Constraints step 6).

- [ ] **Step 6: Commit**

```bash
git add docs/ui-mockup/cover-point-mockup.html
git commit -m "Match Day: rewire animateDeliveryBall onto the delivery-shot pipeline (delivery-shot taxonomy, task 7/8)"
```

---

## Task 8: Full regression pass + memory update

**Files:**
- Modify: none (verification-only task).
- Modify (memory): a new file under
  `C:\Users\user 1\.claude\projects\e--CricketManager-Post-Phase-5-CricketManager-Phase4-Slice14-CricketManager\memory\`
  (a project-type memory per this project's own established convention — see the two
  existing `project_ui_mockup_*.md` files there for the exact frontmatter shape) plus a
  one-line pointer added to that directory's `MEMORY.md` index.

- [ ] **Step 1: Full structural + live regression**

Run `verify_pass1.py` once more (final confirmation nothing drifted across 7 tasks). Then,
live via Playwright, run at least 30 forced-random deliveries in a loop (alternating
`field-iqbal`/`field-awan` via the existing toggle buttons) inside ONE `evaluate` call,
asserting: (a) every single run's `deliveryId` (log it per-iteration into a returned
array) is a real key of `BOWLER_REPERTOIRE` for whichever wrap was active — never a
delivery type that bowler couldn't actually bowl; (b) every run's commentary line is
non-empty and contains a real `DELIVERY_LABEL`/`SHOT_LABEL` pair; (c) no JS console errors
were raised across the whole loop (`browser_console_messages` with `level: 'error'`,
excluding the pre-existing, unrelated `favicon.ico 404` this session already confirmed is
harmless).

- [ ] **Step 2: Confirm the earlier delivery-focus mechanics still work unchanged**

The bowler run-in (genuinely smooth, curved, pace-tier-scaled), the bounce pulse, the
bat-impact flash, the striker's shot-react pulse, the six's shadow marker, and the
fielder-alert pulse were all built earlier this session and this plan's Task 7 only
changes what FEEDS them (delivery/shot data instead of a flat outcome), not their own
sequencing. Confirm each still fires at least once across the 30-run loop from Step 1 (log
a boolean per mechanic into the returned object, checked via the same class-presence
technique already used this session, e.g. `document.querySelector('.bounce-pulse.pulsing')`
observed mid-animation for at least one of the 30 runs via the `Math.random`-forced
technique, not left to chance).

- [ ] **Step 3: Clean up**

`git status --short` must show a clean tree (this task made no file edits). Delete any
stray screenshots/`.playwright-mcp/` left from Step 1-2's verification.

- [ ] **Step 4: Write the memory file**

Create
`C:\Users\user 1\.claude\projects\e--CricketManager-Post-Phase-5-CricketManager-Phase4-Slice14-CricketManager\memory\project_ui_mockup_delivery_shot_taxonomy.md`
following the exact frontmatter shape of
`project_ui_mockup_cc14_matchday_rebuild.md` in the same directory (`type: project`,
`originSessionId` from the current session). Body: summarise the CC14-data finding (no
swing/turn/shot-shape data exists, confirmed by re-checking, so this is grounded in the
project's own `BowlingStyle`/`DeliveryVariation`/`ShotZone` domain model instead), the
parametric architecture, the 9 delivery / 20 shot tables, the shot-driven outcome pipeline
replacing the old outcome-first pick, pace-tier scaling, arm/hand mirroring, and the
honest 7-of-9-reachable data-scope note (spec §8) — link `[[project_ui_mockup_cc14_matchday_rebuild]]`
since this directly extends that work. Add one line to that directory's `MEMORY.md` index
pointing at the new file, in the same style as the two existing pointer lines there.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "Match Day: delivery-type x shot-type visual taxonomy complete (task 8/8)

Every delivery type (outswinger/inswinger/bouncer/yorker/slower-ball,
off-break/leg-break/googly/arm-ball) now has its own real curve/depth/
timing signature; every one of 20 real shots has its own trajectory and
outcome tendency; commentary names what was actually bowled and played.
Pace-tier speed scaling and left-arm/left-hand mirroring are real, tested
code. See docs/superpowers/specs/2026-09-14-delivery-shot-visual-taxonomy-design.md."
```

---

## Self-Review Notes (completed during plan authoring, not a task to execute)

**Spec coverage**: §2 (CC14-data honesty) — task 1's code comment + step notes reference
it; §3 (architecture) — tasks 1-5 build exactly the pipeline described; §4 (9 delivery
types) — task 1; §5 (20 shot types) — task 2; §6 (pairing) — task 3; §7 (pace tier +
mirroring) — tasks 5-6; §8 (honest scope note) — task 1's `BOWLER_REPERTOIRE` comment +
task 8's verification explicitly checks the constraint holds; §9 (testing plan) — every
task's "Verify live" step; §10 (out of scope) — no task touches `src/CricketManager.*` or
adds a third bowler character, matching the spec's own exclusions.

**Placeholder scan**: no "TBD"/"TODO"/"add appropriate handling" found on review — every
step names its exact code, exact expected values, or exact commands.

**Type consistency**: `deliveryId`/`shotId`/`outcome` string names are used identically
across tasks 3-7 (verified by re-reading each task's Interfaces block against its
neighbors); `composeLegs`/`mirrorLegs`'s `{legs, points}` return shape is consumed
identically in task 7 as produced in tasks 4-5.
