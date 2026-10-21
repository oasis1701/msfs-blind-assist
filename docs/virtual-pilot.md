# VirtualPilot — simulated landings and taxis against the real guidance code

`tools/VirtualPilot` flies landings and taxis through the **real** `TaxiGuidanceManager`
against the pilot's own navdata DB, steering **only by what a blind pilot gets**: the tone's
pan and the spoken callouts. It exists because unit tests only pin bugs we already know and the
DB sweeps check exit *tables*, not what happens second by second as an aircraft rolls through
handoff → miss check → retarget → taxi. Built 2026-09-18 after the YPPH 21 C9 false-miss flight.

Nothing touches the pilot's machine state:

- **Time is simulated.** Every clock read in the guidance code (`TaxiGuidanceManager*.cs`,
  `TaxiSteeringTone`) goes through `Utils/SimClock.UtcNow`; the harness installs
  `SimClock.Override`, so a landing takes ~10 ms, and every cooldown, persistence window and
  grace period still sees simulated seconds.
- **No audio.** `TaxiSteeringTone.Headless` skips the audio device; `ObservedPan` reports what the
  pilot would hear (signed pan, 0 when silent or paused).
- **No log writes.** `Log.Redirect` swallows every line (trace mode captures them for display).

All three hooks are `internal` and must stay **inert in the app** — never set any of them outside
the harness. The `Legacy…ForHarness` switches (`LegacyExitAnchorForHarness`,
`LegacyReversedEntryForHarness`) are the same kind of hook: each restores an earlier rule for A/B
measurement only.

> **On this branch.** The rounds below were measured on the owner's local build before its fixes
> were ported onto main (`feat/taxi-landing-port`); main's own designs replaced some of what they
> describe (the off-route runway warning now uses main's `RunwayIncursionWatch`; exit measurement is
> main's `ExitBranch`). Two things a fresh checkout needs: an OSM snapshot (`fetchosm`, or point
> `VP_OSMDIR` at an existing `runs/_osm`) — without one the named-holding-point (`HP`) departures are
> not flown at all and the graph has no OSM taxiway names, so the run is not comparable with one that
> had it; and the `frozen/` before-build project described under A/B is not in the repo — build the
> harness against an older checkout instead (`-p:MainApp=true` compiles the harness against plain
> main before the port).

## Running

```
dotnet build tools/VirtualPilot -c Release -p:Platform=x64
tools/VirtualPilot/bin/x64/Release/net10.0-windows/VirtualPilot.exe <mode> [options]
```

| mode | what it flies |
|---|---|
| `landings` | every listed exit of every runway: once **following the tone**, once **rolling straight past** (skipped for End exits / exits < 600 m from the runway end) |
| `taxi` | per airport, K plans: gate → runway (lineup) and landing-exit vacate point → gate; each flown on the **shortest path** and again on an **ATC-style clearance** (the shortest route's taxiway names in order) |
| `wrongturn` | follows a gate → runway route, then deliberately turns into a branch that leads to a runway hold line not on the route; the app must say "off route" first |
| `all` | landings + taxi |
| `departures` | per runway end: named (OSM) holding-point departures (`HP`, flown free and on a clearance), intersection departures (`IX`), full-length backtracks where the runway list would say "(backtrack required)" (`BT`); per airport: gate → gate taxis across the field (`G2G`) and ATC clearances with explicit "hold short of runway X" instructions (`HSCLR`) — each also voiced as a written and a spoken-NATO clearance and fed to the SayIntentions import's own parser (`CLR_*`) |
| `fetchosm` | downloads the raw Overpass answer for each airport into `runs/_osm/ICAO.json` (one polite request at a time — Overpass rate-limits hard, expect ~1 airport a minute). Every mode then builds the app's OWN OSM-augmented graph from that snapshot (names, holding points); `VP_NOOSM=1` flies plain navdata, `VP_OSMDIR` points elsewhere. The snapshot pins the data so a before/after run measures the code, not OSM churn |

Options: `--airports N` (busiest by taxi-path count, default 100), `--icao A,B`, `--shard i/n`
(run n processes in parallel — the hooks are process-global, so parallelise by process, never by
thread), `--taxi-per-airport K`, `--out file`, and
`--trace ICAO:RWY:EXIT[:straight]` / `--trace ICAO:taxi:N[:clearance]` for one scenario frame by
frame (speech, state changes, routes, the harness's view of the runway frame).

Every run writes `<out>` (findings) and `<out>.results.txt` (one outcome line per landing).
`tools/VirtualPilot/compare.py BEFORE.results.txt AFTER.results.txt` diffs two runs landing by
landing — **run it before and after any change to rollout / exit logic; "no degrades" means the
WORSE column is explained case by case.** Run outputs go under `tools/VirtualPilot/runs/` (ignored).

Trace forms: `ICAO:RWY:EXIT[:straight]` (a landing), `ICAO:taxi:N[:clearance]` (the Nth taxi
plan), `ICAO:dep:<text>` / `ICAO:any:<text>` (every departures / every taxi-or-departures scenario
whose description contains the text — e.g. `KORD:dep:IX Y4 L 5`). `VP_SHOWCLR=1` prints the voiced
clearances, `VP_STUBSTATS=1` the destination entry-stub length and hold label of every departure.

### A/B against a frozen build

Comparing two runs only means something when both fly the SAME scenarios, and a harness change
(a new check, a different random draw) changes the scenarios. So the "before" is the current
harness compiled against the OLD app: freeze the harness output folder before touching the app,
then build `tools/VirtualPilot/frozen` against it (its csproj header has the four steps;
`OLDAPP` compiles out harness calls to app APIs the old build lacks) and run
`VP_EXE=tools/VirtualPilot/frozen/out/VirtualPilot.exe tools/VirtualPilot/runall.sh BEFORE`.
Scenario keys can still drift when the app's own output feeds scenario selection (the
intersection list's order picks which intersections are flown) — `compare_codes.py` prints the
"only before / only after" counts; read those rows as new scenarios, not regressions.

The usual loop: `tools/VirtualPilot/runall.sh NAME` (landings + taxis + wrong turns, 100 airports,
6 processes, ~10 min — it BUILDS the harness first, because the harness is not in the solution and
a solution build leaves a stale binary), then
`py tools/VirtualPilot/compare_codes.py runs/BEFORE runs/AFTER [n]`, which lists per scenario
every finding code that disappeared or appeared. Trace one scenario with
`--trace ICAO:RWY:n<nodeId>` (exit names repeat — KMSP 04 has three B junctions) or
`--trace ICAO:taxi:N --taxi-per-airport 10` (N as numbered by the run); `VP_ALLLOG=1` adds the
app's log lines, and the landing trace prints the exit path in the runway frame.

## The pilot model

`PilotModel`: 0.3 s reaction lag; yaw rate from the pan, limited by speed (0.15 g lateral, max
15°/s) and yaw acceleration; holds heading when the tone is silent; 15 kt taxi, 9 kt on a hard
pan, 10 kt after "Slow…". In **lineup** (precision tone: 1° activation / 15° full pan) it reads the
pan as an error estimate and corrects proportionally — the turn-hard-on-any-pan taxi model
limit-cycles against a precision tone, which is a harness artefact, not an app bug. It stops at
hold shorts and presses Continue after 2 s, stops on "Lined up", and follows the rollout tone at a
decelerating speed (Normal/End exits 18 kt at the junction, High-speed 35 kt).

## What it checks, and how much to trust each

| finding | meaning | confidence |
|---|---|---|
| `LANDING_FALSE_MISS` | "Missed X" / retarget while the pilot followed the tone. The detail says how far the pilot was from the route and where the route leaves the centreline | high when on-route ≤ 5 m |
| `STRAIGHT_MISS_NOT_CALLED` / `_LATE` / `STRAIGHT_ROLL_ARRIVED` | rolling straight past is not called, called > 200 m late, or reported as vacated | high |
| `LANDING_NOT_ARRIVED`, `LANDING_RAN_OFF_END` | never reached an arrival | medium — check the trace |
| `EXIT_ROUTE_HAIRPIN` | the route off the runway turns > 120° within 150 m of the handoff: an exit listed for this direction that needs a U-turn (KPHL 35 E4) | high |
| `HANDOFF_ROUTE_STARTS_BACKWARDS` | the handoff route's first leg points > 120° off the heading | high |
| `UTURN_SPOKEN_ON_RUNWAY` / `UTURN_ON_FLAGGED_TURNBACK_EXIT` | "Make a U-turn" spoken on the runway — on an unlabelled exit (a bug) or one the list labels "(sharp turn back)" (warned) | high |
| `TOUCHDOWN_NAMES_OTHER_TAXIWAY` | at touchdown the first guidance line names a taxiway that is not the chosen exit ("Taxiway A. Steering guidance active." before "…exit taxiway C9") | high (every landing) |
| `OFF_ROUTE_WARNING_ON_ROUTE` | "Warning: approaching runway X, off route" with the aircraft ≤ 15 m from its route | high |
| `WRONG_TURN_SILENT` | a deliberate wrong turn reached a runway hold line with no warning | high |
| `HOLD_STOP_ON_RUNWAY_PAVEMENT` | the pilot stopped for a hold short while standing on runway pavement (`RunwayShape`) | high |
| `RUNWAY_ENTERED_UNHELD` / `DEST_…` | runway pavement entered > 250 m after the last hold (edge flicker and lineup onto the destination runway excluded) | medium |
| `CLEARANCE_ROUTE_DETOUR` | the constrained router over the shortest route's own taxiway names is 1.3× + 200 m longer — usually unnamed connectors a clearance cannot name vs the `FindRunwayBridge` 200 m cap (PGUA "via A": 450 m of unnamed connector) | medium, often by design |
| `TAXI_RECALC_WHILE_FOLLOWING` | "Route changed" while following the tone | medium |
| `TAXI_OFF_PAVEMENT`, `LANDING_OFF_PAVEMENT` | > 12 m beyond the nearest taxi edge's half-width | **low** — the graph has no apron polygons, so open aprons read as grass |
| `LINEUP_WRONG_PLACE` | "Lined up" > 120 m along the runway from where the pilot selected (the intersection's listed distance; the full-length lineup point) | medium — a precise lineup turn at 6 kt takes 30-60 m |
| `HOLD_NAMED_OTHER_RUNWAY` / `HOLD_FAR_BEFORE_RUNWAY` / `HOLD_WITHOUT_RUNWAY` | the stop named a runway other than the one then entered; it was > 220 m before it; or a runway stop was never followed by any runway | high |
| `DEST_HOLD_FAR_FROM_ENTRY` | the destination hold is > 600 m of taxiing before the runway entry | high |
| `HOLDPICK_NOT_SET` | an explicit "hold short of runway X" the app could not set (it says so) | medium — the harness derives the picks from the SHORTEST route, the clearance route can differ |
| `HP_HELD_AWAY_FROM_POINT` / `HP_HOLD_NOT_NAMED` / `HP_ROUTE_MISSES_POINT[_WARNED]` | a named-holding-point departure held > 30 m from the painted line picked / held there without naming it / the route never passes it (`_WARNED`: the app said so) | medium — hold placement stays navdata-authoritative by design, and a painted line BEYOND the navdata hold is correctly never reached |
| `CLR_{WRITTEN,SPOKEN}_{TAXIWAYS,HOLDS,RUNWAY}_WRONG` | the voiced clearance parsed to different taxiways / hold shorts / runway | high for spellable names; navdata names ATC would never say ("N-North") are noise |
| `LINEUP_LEAVES_MAPPED_PAVEMENT`, `LINEUP_NEVER_ALIGNED` | the lineup intercept leaves every mapped edge; never "Lined up" in 400 m | **low** — many sceneries stop the entry taxiway at the hold line (KIAH SA), so the ground between hold line and runway is unmapped |

## Findings of the first run (2026-09-18, 100 busiest fs2020 airports)

4,857 landing runs, 1,960 taxis, 409 wrong turns.

**Fixed:**

1. **Centreline-stub false misses** — many sceneries draw the exit taxiway down the runway
   centreline before it turns off (KORD 27L M 80 m, ZSPD 35R B7 99 m, EGLL 27L N7 40 m after a
   3 m jog). The route tone correctly said "continue"; the miss check at junction + 100 ft said
   "Missed". `LandingExitPathFollow.OnAxisRunMetres` now extends both miss detectors by that run
   (hairpins and paths that come back across the centreline excluded). **996 → 194** false misses,
   "never arrived" 227 → 48, off-pavement 671 → 466, ran off the end 226 → 50. Per-landing diff:
   ~1,550 better; 5 changed for the worse, all on broken exit geometry (KSDF 29 F, KCVG 18C K,
   PGUA 24L B: false "missed" → a different false "missed"; KLAX 25L A7 14 m off pavement).
   The trade, accepted: rolling straight past a stub exit is called "missed" later (median 59 m,
   p90 107 m, max 201 m), because until the drawn exit leaves the centreline "rolled past" and
   "following the exit" are the same position; 101 of those retargets pick a later exit (mostly
   because the old pick was 5–75 m ahead).
2. **False "approaching runway X, off route"** — the on-route set was the REMAINING route only,
   so the hold line just crossed dropped out the moment it was passed; hold lines beside the route
   (entry stubs off a parallel taxiway) tripped it too. Now: the whole route counts, a line behind
   the aircraft is not "approaching", and a line beside the route is silent while the aircraft is
   within 8 m of its route and heading along it (±30°). **1,121 → 239** of 1,960 taxis. Wrong-turn
   A/B (408 deliberate wrong turns): 0 warnings lost, 2 gained.

## Second round (2026-09-18, same 100 airports)

Every change below was A/B-measured with `compare_codes.py`; against the start of the round,
nothing new appears except what is listed as accepted.

1. **Touchdown callout named the wrong taxiway** on every landing ("Taxiway A. Steering guidance
   active." before "…exit taxiway C9") — `StartGuidance(announceStart: false)` at touchdown.
2. **U-turns on the runway at the handoff: 41 → 1** (KPHL 27R K4, a data mismatch). The
   landing-exit anchor retries a route that starts backwards, points back on its first leg or
   hairpins within 150 m (`LoadRoute`, see [RTE-32] in `docs/invariants/taxi-routing.md` for every gate and why). Exits whose own path
   turns back are labelled "(sharp turn back)" in the list, warned at touchdown and not picked by
   default (`PathTurnsBack`). Route hairpins 272 → 106.
3. **Destination hold on runway pavement** (EFHK 22R WD, KSLC 14): the stop moves back to the
   last node clear of every runway. 6 → 0.
4. **False misses 194 → 66**: the stub allowance no longer drops for an exit that loops back once
   well out on the apron (44), plus the retry above.
5. **False "off route" 239 → 115**: the on-route tracking test looks three segments back (a pilot
   stops short of a hold line and Continue moves the index past it).
6. Harness: "ran off the end" only counts inside the runway strip (End exits run on beside it).

**Accepted, recorded:**

- 260 U-turns on exits labelled "(sharp turn back)": the pilot was told at touchdown, and the
  alternatives (loops through other taxiways that hairpin 50 m later, or an 860 m detour at
  ZGGG 20L A) are worse.
- Offset stubs (OOMS 26R A, 7 m off the centreline for 87 m): widening the 6 m band trades each
  fix for a straight roll called "missed" > 200 m late. Stubs > 200 m (KLAX 25L A7) hit the cap.
- ZGGG 20L B: the retry lets the 300 ft early handoff go ahead and the pilot misses the curve at
  48 kt — disabling the retry there costs two other exits.
- ZSSS 18L (island start, uncharted crossing, B drawn over the runway) and KPHX 25R E12 (E11/E10
  drawn 260 m along 25R): the route itself runs on runway pavement.
- A geometric "hold line further from the runway than the aircraft" silence for off-route warnings
  fixed 19 and silenced two real wrong turns at RKTP 03R — removed, do not rebuild.

**Open (not fixed — each needs a decision):** the remaining 66 false misses and 115 off-route
warnings, 71 recalcs while following (mostly a harness artefact: exit→gate taxis start facing away
from their route), lineup from a set-back hold cutting across unmapped ground, clearance detours.

## Third round — taxi and departures (2026-09-18, same 100 airports)

New scenario families (`departures` mode) and the OSM snapshot. Old build vs new, frozen-build A/B,
13,774 scenarios on navdata (landings, taxis, wrong turns, departures) plus the named-holding-point
runs at the 20 airports with an OSM snapshot. Landings and wrong-turn warnings are unchanged
(25 silent wrong turns before and after, all pre-existing).

**Fixed:**

1. **Lineup from a set-back hold cut across the grass.** After Continue the intercept law steered
   for the centreline straight from the hold line (KORD 04R Y4: 95 m out, "Turn left" — 137° — at
   the line, "Lined up" 500 m past the intersection picked). The tone now follows the cut-off entry
   stub onto the pavement first (`TaxiRoute.RunwayEntryStub`, kept only when it reaches the
   destination runway). Grass lineups **2,104 → 238**, wrong-place lineups **801 → 212**,
   never-aligned 52 → 30.
2. **The destination hold was the latest hold anywhere on the route** — another runway's hold or this
   runway's far end: WIII 06 at "runway 07L at N8M" 612-868 m out, RPLL 31 2.5 km out, OTBH 16L 6 km
   out. Now only a hold within 200 m of the entry, or within 1,000 m naming this runway, counts; else
   the synthetic back-off, which must now be 10 m clear of every runway edge (VIDP 27 via D6 stopped
   on the edge). Phantom holds 207 → 107.
3. **Holds that named the wrong runway** 32 → 2: the entry stub can roll onto an intersecting runway
   before the destination (KSLC 32/35, KCOS, KSTL, KDTW) — now named at the stop ("Hold short of
   Runway 32 and runway 35") — and the countdown used the scenery's reciprocal designator
   ("Hold short runway 22L at Y4" then "Stop. Hold short of Runway 04R").
4. **Intersection distances were overstated on angled entries** (EGLL 09L AB11, KORD 04R Y4): now
   quoted from where the taxiway reaches the pavement on the aircraft's side, forward only.
5. **False "off route" warnings** 228 → 138: the on-route tolerance is 12 m, not 8 (a pilot rounding a
   junction 8-9 m wide — KORD G/G2, RPLL F5); wrong-turn A/B 391/391 still warned.
6. **Named holding points:** OSM labels that describe a hold kind ("28C APCH", "GP HOLD LINE", "ILS")
   are no longer offered as departure entries (KORD "28R APCH" stopped the pilot on runway 04R), and
   a holding point the route cannot be pinned through is now SPOKEN instead of log-only (EDDF 18 via P8).
7. Harness: the spoken-clearance check (the SayIntentions parser recovers taxiways, hold shorts and
   runway from written and NATO-spoken clearances at every airport — it does, apart from water runways
   "04W" and navdata names no controller says); a crossing through a runway intersection is one
   crossing, not two entries (CYYC C over 35L/11).

**Measured and rejected:** handing the lineup over to the centreline intercept 12 m BEFORE the
pavement edge (to shorten the lineup turn on narrow runways — YPDN 36 overshoots a 30 m runway by
2 m): 59 new findings for 24 fixed, it cuts the corner on angled entries.

**Open after this round:** see the fourth round below.

## Fourth round — the open list (2026-09-18 evening, same 100 airports)

A/B against a frozen build of the start of the round (`tools/VirtualPilot/frozen`, which now copies
the frozen dependencies itself and only compiles `OLDAPP` out with `-p:OldApp=true`).

**Harness corrections first** — each changed what a finding means, not the app:

- Taxi-planner routes are now loaded with `announceSummary: true`, as the form does. That flag also
  runs the route-start checks, so KVAD Parking 45 (a stand joined to the network only across runway
  19R) is correctly REFUSED by the app ("reaching it means crossing runway 19R where there is no
  taxiway data") — it was an artefact, not an unheld runway entry. Refusals are recorded as
  `TAXI_ROUTE_REFUSED` (31: honest island messages).
- A wrong turn the app answers by RE-ROUTING through the line is scored on the new route:
  `WRONG_TURN_REROUTED_NO_RUNWAY` (no runway beyond the line) / `_HELD` / `_UNHELD` (the only bad one).
  Scenarios whose "junction" lies beyond the lineup are skipped (KMCI).
- Rolling from one runway's pavement straight onto another's is one crossing (CYYC C over 35L/11).

**Fixed (125 findings, 0 new on navdata; on the full OSM set — 98 airports, 3,457 departures incl.
692 named-holding-point runs — 49 fixed / 12 changed, every changed one a variant of the same
scenario's old finding or a safer route: OIII 11L via B8 no longer drives onto 11L and back out to
hold; KPDX 03 via 4E no longer takes a 4.7 km detour to pass the painted point, and says so):**

1. **Clearances that repeat a taxiway** — both readings built, the one that follows the clearance
   taken unless it loops (see [RTE-33] in `docs/invariants/taxi-routing.md`). OTBH 16L "A, C, A" 9.1 → 2.9 km, ZPPP 04L "A, B, A"
   6.2 → 3.9 km, EDDF/LEMD clearances the old router abandoned for shortest path now followed.
2. **"Crossing runway X" for a hold line the route never goes past onto a runway** → "Passing the
   runway X hold line" (KDTW F, an approach-area line).
3. A holding-point route's summary read "Route to  via …" (the pinned route lost its name).
4. Water runways: "runway 04W" parsed as 04.

**Measured and rejected** (details in [HLD-15], `docs/invariants/runway-holds.md`): a dead-ahead exception to the off-route tracking
silence (204, then 148 false warnings), a cost on taxiing along runways, pinning intersection
departures through the picked taxiway, stopping at a picked painted line behind the navdata hold.

**Still open — each is the scenery or the pilot model, not a code path left unmeasured:**

- 10 silent wrong turns: a missed turn and a wide turn are the same geometry when the hold line is
  20-30 m past the junction (YPDN A2/A3 ×3, OETF, KEWR, LEMD, KATL, KPHL, KFBG, KMCI).
- Stops on runway pavement (8) and unheld entries (9) where the ROUTE drives along a runway (KLAX
  H8/H9 down 07R, KABQ along 08/26, USSS M, OOMS A): the shortest path uses runway pavement as a
  taxiway and there is no clear node to stop at.
- YBBN 19L "A6": that intersection is reached along A4S; A6's own approach lies beyond the junction.
- Painted holding points behind the navdata hold line: the stop stays navdata-authoritative.

## Painted-line hold audit (2026-09-23)

Set `VP_AMDBDIR` to an unpacked AMDB Bridge airport cache (one folder per ICAO with
`taxiwayholdingposition.geojson`; the free X-Plane-Gateway/OSM database from
github.com/Vihaan2012-cmyk/Free-Airport-Mapping-DB) and every taxi/departure also checks each hold
stop against the painted RUNWAY hold lines (catstop 1):

- `HOLD_PAST_PAINT_NAVDATA_HOLD_UNUSED` — the stop is on the runway side of every painted line for
  that runway, and our navdata HAS a hold node within 40 m of the line: our placement logic.
- `HOLD_PAST_PAINT_NO_NAVDATA_HOLD` — same, but our navdata has no hold there: the two sceneries
  disagree.
- `HOLD_WELL_SHORT_OF_PAINT` — stopped more than 120 m before the first painted line.

X-Plane positions are ~20 m off MSFS, so every comparison carries a 30 m allowance, and only lines
off the entered runway's pavement, on the stop's side, within 250 m and nearer it than any other
runway count. Unset, the audit is off and the harness is unchanged. Result and the fix it drove:
docs/taxi-guidance.md, "Runway holds on a diagonal approach".

## Fifth round — what the pilot hears (2026-09-25, same 100 airports)

New harness checks: `SPOKEN_DIRECTION_CONTRADICTS` (opposite turn directions in one frame, or
"Curving X" over a hard opposite tone) and `VP_BURST=1` (prints every frame that speaks more than
one line — each `AnnounceImmediate` cuts the previous one off, so only the last is heard). The
taxi trace also prints the route's start node and every hold line within 150 m (`HSNEAR`, with
distance from the nearest centreline).

**Fixed (0 new findings; wrong-turn warnings 385/385, same distances):**

1. **One utterance per frame** (`SpeakNow` / `ComposeFrameUtterance`). ~12,000 frames spoke 2-4
   interrupting lines; the pilot heard only the last ("In 30 metres, slight right." → "Slight right
   now." → "Slow for turn.": the direction was never heard). Safety lines go first; an advance notice
   in the same frame as its "… now" is dropped.
2. **"Curving X" deferred while the tone asks for a turn the other way ≥ 25°** — 89 → 2
   contradictions (EGLL 27R A7/A10E/AB11: "Turn left to come around." + "Curving right.").
3. **Junction callouts wait until the pilot has come round after the initial turn cue**
   (`_initialTurnPending`) — the route summary's "Turn left to come around" was cut off by "Left."
   / "Left now." for a junction behind the aircraft.
4. **Off-route runway warning**: the LOADED route's start node counts as on-route (a stand 31 m
   short of LTFM A11A; recalcs excluded — whitelisting them lost 19 wrong-turn warnings), the pilot
   standing at it is ignored, and "approaching" needs ≥ 2 kt of closing speed on the line. False
   warnings on route 147 → 77.
5. Wording: callouts capitalised, no "continue now.", no "sharp slight right … 160 degrees".
6. Hold-short countdown speaks only the deepest tier already crossed ("in 55 metres." then
   "in 55 metres. Slow down." 0.1 s apart at LTFM B9B).
7. Post-handoff, well off to the side of a leg running beside the runway with the next turn toward
   that side, the tone steers at the leg's end (KTPA 28 N: silent tone under "Sharp right now").

Follow-up the same evening (A/B against the round's end, 0 new findings, wrong turns 385/385):

8. **Vacate stops past the landed runway's own hold line on a curving exit**
   (`RunwayVacateResolver.ExtendPastOwnHoldAhead`). EGLL 09L A1 stopped 22 m short of "A1, Runway
   27R" at 141 m because the greedy walk needs every step further out and A1 curves (120 → 123 →
   120 m). Gates, each measured: a line NAMED for the landed runway, ≤ 180 m out (1,446 → 1,332
   moves before this cap; 01G was moving to 252 m), ≤ 80 m of taxiing, never more than 10 m back
   toward the runway, a ≤ 60 m hop past the line must exist (KCVG 09 C's only onward leg is 298 m
   and the planner then started the route 250 m away), and not on an exit whose path turns back
   (`LandingExitDestination.Resolve`; RJAA 34L A8, CYYC 11 A got worse). Whole DB
   (`tools/LandingExitDestinationSweep`): 497 exits at 229 airports move, all outward (+5 to +120 m,
   median 46 m), none closer, no vacate verdict changes — Heathrow's N5E/N4W/S4E/S6/N6/A6 stopped at
   ~90 m against painted lines at ~145-150 m.
9. **Handoff route anchor follows the exit's own path** (`ExitPathStartAnchor`): KCLT 18 "D6"
   leaves via R, and the nearest D6 node was on D6's other arm beside the runway — the route opened
   with a leg that panned the tone away from the exit and the pilot heard "Missed taxiway D6". Only
   where the path beyond the junction carries no node of the exit's name, and never on a turn-back
   exit (overriding every off-path anchor fixed 59 findings and made 43).
10. **The post-handoff miss retarget uses the undershoot scan's 11 ft/kt lead** — "Retargeting taxiway
    R, 25 metres ahead" at 25 kt. A junction of the missed exit's own taxiway is exempt (KSLC 35 K8
    is rescued by it), a close exit is still taken when nothing further qualifies, and
    `RetargetLandingExit` tries it last when every further exit fails to route (KEDW 23L past C).
    314 straight-roll retargets now name an exit a median 100 m ahead instead of 35 m.

**Open:** OSM hold names like "C@10/28" (KTPA, KDFW, KONT, KABQ) are read out verbatim. EGLL 09R S6
→ Gate 223 routes back up S6 onto the runway and along it (the known "runway used as a taxiway"
residual).
