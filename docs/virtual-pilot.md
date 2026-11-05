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
| `approach` | visual guidance (Output Ctrl+V): 159 simulated approaches through the real `VisualGuidanceManager`, flown by a pilot who follows the two tones and the bank callouts, on two synthetic runways (no navdata database needed). See [Approaches](#approaches-visual-guidance) |
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

## Approaches (visual guidance)

The `approach` mode flies visual guidance the way the other modes fly taxi guidance: through the
real `VisualGuidanceManager`, with simulated time and no audio, steered only by what a blind pilot
gets. It was built on 2026-11-02 as the first stage of the visual guidance redesign, to record how
today's guidance behaves before any of it changes. Its files are in `tools/VirtualPilot/Approach/`.

### Hooks

All of them are inert in the app (VP-1):

- `VisualGuidanceManager.HeadlessForHarness`, an instance `init` flag: `Initialize` builds no tones
  and `ProcessUpdate` runs the guidance without them.
- `DesiredPitchForHarness` and `DesiredBankForHarness`: the pitch (nose up positive) and bank
  (right positive) commanded on the last update, before the tones saturate. `PhaseForHarness` names
  the guidance phase.
- `PitchDegreesFromSimConnect`, `BankDegreesFromSimConnect` and `AoaDegreesFromSimConnect`, which
  MainForm's `VISUAL_GUIDANCE_*` handlers also call, so the harness converts SimConnect's attitude
  exactly as the app does.
- Every clock read in `VisualGuidanceManager` goes through `SimClock.UtcNow`;
  `VisualGuidanceHarnessHookTests` pins it, and pins left and right on two runways.

### The runways, the aircraft and the pilot

Two synthetic runways replace the navdata database. Both have a 3° glide path and are 2 nm long.

| runway | threshold | true course | variation | magnetic course | elevation |
|---|---|---|---|---|---|
| A | 47.0 N, 8.0 E | 004 | 9° E | 355 (wraps through north) | 1,400 ft |
| B | 40.0 N, 74.0 W | 268 | 13° W | 281 | 20 ft |

The aircraft is a point mass. Bank sets the turn rate (g·tan φ / V), and wings level flies a great
circle. The flight path follows the pitch minus a fixed trim angle of attack through a first-order
lag. Speed is held, as autothrust would hold it, the wind is constant, and the terrain is flat at
the threshold's elevation. There is no ground effect.

| kind | TAS | trim angle of attack | flight-path lag | pilot roll rate | pilot pitch rate |
|---|---|---|---|---|---|
| Jet | 140 kt | 5° | 2.5 s | 5°/s | 2°/s |
| Light | 65 kt | 4° | 1.5 s | 10°/s | 4°/s |

The pilot (`TonePilot`) hears the two tones, each pinned at the profile's range (±5° of bank,
±6° of pitch by default). Inside the range the pilot flies the commanded value. Past it, both pans
pin and sound alike, so for bank the pilot uses the latest callout from the last 2 s ("3 right",
"matched"); with none, they roll until the current tone pins too, then hold. The pilot acts after
a reaction delay of 0.5 or 1.0 s, cannot hear a difference under 0.5° of bank or 0.1° of pitch,
and closes the rest with a time constant of 0.5 s (bank) and 0.7 s (pitch), capped at the roll
and pitch rates above.

The speech-led pilot (`bankBySpeech`, the GUST family's `speech` runs) stands for a pilot who
leans on the callouts: its bank target is always the latest bank callout of the last 2 s, and with
none it holds its bank, even inside the tone's range. Its pitch still follows the tone. A gust
(`TonePilot.Knock`) rolls the aircraft at once, but the pilot learns of it only through what it
hears after it, a reaction delay later: until then it steers the bank it perceives, the actual bank
less the knock it has not yet heard. Without a knock the pilot's arithmetic is the same as before
gusts existed, to the bit.

A scripted turn (`ApproachScriptedTurns`, a scenario's `Turns`) stands for a pilot obeying ATC: from
each turn's time the pilot ignores the tones and turns to the heading course + the turn's relative
heading (relative to the centreline's direction abeam the aircraft), at a bank of twice the heading
error, at most 20°, at its usual roll rate and with no reaction delay, since it turns by its own
heading. Once within 1° the turn is over: the pilot rolls level and follows the tones again, acting
on what it heard while turning. Turns run one at a time, in order. The tones and callouts still
reach the measures during a turn. A scenario's `StartBankDeg` starts the aircraft and the pilot in
that bank (a press of Ctrl+V in a turn); with no scripted turn the pilot then follows the tones.

Each frame the harness feeds visual guidance in MainForm's order (AGL last, which calls
`ProcessUpdate`), records the aircraft state with the command it produced, then steps the pilot
and the aircraft.

### Scenarios

Headings are relative to the centreline's direction where the aircraft starts. "Cross" is positive
right of the extended centreline, facing the runway. A start on the course is already descending
on the 3° path, except the vectoring and turning starts, which fly parallel to it level; the others
start level. Unless a row says otherwise, the frame rate is 30 a second and the reaction delay 0.5
and 1.0 s. The light runs fly with a minimum final (`MinimumFinalNm`) of 1.0 nm, the circuit-sized
final the redesign's spec gives light aircraft, set by reflection where the app has the field.

| family | name pattern | start | count |
|---|---|---|---|
| Final | `FINAL-{A,B}-{JET,LIGHT}-x{0,0.2R,0.2L}-h{0,+200,-200}-d{0.5,1.0}` | jet 8 nm out, light 4 nm; on the course, offset sideways and above or below the path | 72 |
| Intercept | `INT-{A,B}-{JET,LIGHT}-{L,R}{30,45,90}-d{…}` | jet 12 nm out and 3 nm to the side, light 6 nm and 1.5 nm; cutting toward the line at the angle given; at the path's height | 48 |
| Downwind | `DW-{A,B}-{JET,LIGHT}-{L,R}-d{…}` | abeam the middle of the runway (1 nm past the threshold), jet 3 nm to the side and 2,000 ft above the field, light 1.5 nm and 1,000 ft; flying the reciprocal. Not abeam the threshold itself: there, guidance's behind-the-runway test is decided by rounding | 16 |
| Wind | `WIND-XW-A-JET-d0.5`, `WIND-TW-A-JET-d0.5` | a jet final with 15 kt from the right; a jet on a 90° base from the right with the same wind behind it | 2 |
| Frame rate | `FPS-A-JET-f{20,30,60}` | the jet final on the centreline and the path, at 20, 30 and 60 frames a second | 3 |
| Step | `STEP-A-{JET,LIGHT}` | 6 nm out on the path; at 40 s the pilot holds 1° below their pitch for 10 s | 2 |
| Speed | `SPEED-A-{JET160,LIGHT90}-{R45,x0.2R-h+200}-d0.5` | the 45° intercept and an offset final at 160 kt (jet) and 90 kt (light) | 4 |
| Noise | `NOISE-A-{JET,LIGHT}-f{30,60}` | the `FINAL-A-…-x0.2R-h+200` start with the 0.5 s pilot, at 30 and 60 frames a second, with sensor noise on what visual guidance reads | 4 |
| Gust | `GUST-A-{JET,LIGHT}-g{2,4}-{tone,speech}` | the `FINAL-A-…-x0-h0` start with the 0.5 s pilot; at 40, 80 and 120 s a gust knocks the aircraft 2° or 4° left, right, left, each spread over 0.3 s of frames; `tone` is the tone-following pilot, `speech` the speech-led one | 8 |
| Vectoring | `VEC-{A,B}-{JET,LIGHT}-{L,R}-{par,away}-d0.5` | jet 10 nm out and 2 nm to the side, light 5 nm and 1 nm; flying parallel inbound (`par`) or outbound (`away`); level at the path's height | 16 |
| Far side | `FAR-{A,B}-{JET,LIGHT}-{L,R}-d0.5` | past the runway's far end (jet 4 nm, light 3 nm past the threshold), jet 1 nm and light 0.5 nm to the side, flying back toward the field, jet 2,000 ft and light 1,000 ft above it | 8 |
| Too close | `CLOSE-A-{JET,LIGHT}-{L,R}-d0.5` | jet 7.5 nm out and 2 nm to the side, light 1.8 nm and 1 nm; cutting toward the line at 45°, so crossing it at 5.5 and 0.8 nm, inside the minimum final; at the path's height | 4 |
| Turning | `TURN-A-{JET,LIGHT}-{L,R}-d0.5` | the intercept start, flying parallel, level, banked 20° toward the line, with a scripted turn at 0 s to a 30° cut (Ctrl+V pressed in the turn ATC gave) | 4 |
| Heading change | `NOCROSS-A-{JET,LIGHT}-{L,R}-d0.5` | the 30° intercept start; scripted turns parallel to the course at 20 s and to a 45° cut at 60 s (the jet's new track meets final at about 7.2 nm, outside the 6 nm minimum) | 4 |
| Shallow | `SHALLOW-{A,B}-{JET,LIGHT}-{L,R}-d0.5` | jet 20 nm out and 3 nm to the side, light 10 nm and 1.5 nm; cutting toward the line at 15°, crossing it at about 8.8 and 4.4 nm; at the path's height | 8 |

The noise (`ApproachSensorNoise`) adds independent Gaussian noise every frame to the vertical speed
(σ 30 fpm), the angle of attack (σ 0.2°) and the ground track (σ 0.2°), from one fixed seed per run,
so a noisy run repeats exactly. These spreads are assumed for a stress case; nobody has measured
them in the sim. The noise reaches only what visual guidance reads: the aircraft, the measures and
the trace keep the true values.

A run ends when the wheels touch, which is the profile's `FlareAltitudeBiasFt` below the datum, as
visual guidance itself places them. The landing is `touchdown` on the runway (2 nm by 150 ft), or
`landedShort`, `landedLong` or `offRunway`. A run also ends when the aircraft passes the runway's
far end still airborne (`pastRunwayEnd`), when visual guidance switches itself off (`vgOff`), after
900 simulated seconds (`timeout`), or 10 s after a step (`stepDone`). A far-side start begins past
the far end, so the far end ends a run only once the aircraft has been over or before the runway.

### Measures

Each scenario writes one line to `<out>.results.txt`: `VG|<name>|key=value|...`, numbers in invariant
culture, `NA` where a measure does not apply.

| key | meaning |
|---|---|
| `endReason`, `durationS` | how and when the run ended |
| `touchdownAlongFt`, `touchdownXteFt` | the touchdown point: feet before the threshold (negative = past it), and feet right of the centreline |
| `touchdownVsFpm` | the sink rate at wheel contact, feet per minute down; `NA` for a run that does not land. The model has no ground effect, so compare it between runs, not with the sim |
| `established`, `timeToEstablishS` | whether, and when, the aircraft had been within 0.1 nm of the line and 5° of the course for 3 s |
| `overshootFt` | for starts more than 0.15 nm off the line: the farthest past it, after first crossing, before established |
| `finalXteRmsFt`, `finalXteMaxFt` | cross-track on the final segment (jet 5.0 to 0.5 nm, light 3.0 to 0.3 nm), counted once established |
| `pathRmsFt`, `pathMaxHighFt`, `pathMaxLowFt` | height against the 3° path (with the profile's glidepath bias) on the same segment |
| `pitchJitterDeg`, `bankJitterDeg` | RMS of the frame-to-frame change in the commanded pitch and bank on that segment |
| `pitchRateRmsDegS`, `bankRateRmsDegS` | RMS of the commanded pitch's (bank's) rate of change on the final segment, in degrees per second. Unlike the jitter keys it does not depend on the frame rate for a smooth command |
| `maxDescentFpm` | the largest descent rate of the run |
| `bankCalloutsPerMin`, `centrelineCalloutsPerMin`, `otherCallouts` | spoken lines by kind |
| `cutOffCallouts` | lines followed by an interrupting line before their estimated end (words / 2.5 + 0.2 s; a measure only, never an app timing, VG-1) |
| `gustCalloutDelayS` | gust runs only: the mean over the knocks of the time from a knock's start to the first bank callout after it; a knock with none within 10 s counts 10 |
| `gustRecoveryS` | gust runs only: the mean over the knocks of the time from a knock's start until the aircraft's bank has stayed within 1° of the commanded bank for 1 s, counted at the start of that second; 30 if it never does within 30 s. The band is twice the pilot's 0.5° deadband: at 0.5° the pilot settles just inside the band's edge, the slowly moving command pushes it out before the second is up, and the measure read 11 s for a jet back level 2 s after a 2° knock |
| `gustXteMaxFt` | gust runs only: the largest cross-track within 30 s after any knock |
| `selfFeedback3s`, `selfFeedback10s` | step runs only: how far the command moved, 3 and 10 s after the step, per degree the pilot pitched, against its mean over the 2 s before. Positive means the command followed the pilot down; negative means it moved the other way |
| `decided` | the first decision visual guidance made (`OnFinal`, `Joining`, `Vectoring`, `Pattern`; its `DecisionForHarness`, read by reflection) |
| `decidedBankDeg` | the aircraft's bank, either way, at the first frame with a decision |
| `establishedAlongNm` | how far out (nm along the course) the aircraft was when established, by the harness's own criteria above; measured on any app |
| `joinHoldBankMaxDeg` | the largest commanded bank, either way, while the phase is `Joining` and before "Turn onto final" is spoken, outside scripted turns |
| `countdown30ErrS`, `countdown15ErrS` | the time from "Turn onto final in 30 seconds" (15) to "Turn onto final", less 30 (15) |
| `turnToEstablishedS` | from "Turn onto final" to established |
| `maxBankCmdDeg` | the largest commanded bank of the run, either way; measured on any app |
| `notCrossingCalls` | how many times "Heading does not cross final" was spoken |
| `leftJoining` | True if the phase ever went from `Joining` to `Vectoring` |

The joining keys are on every line, a value or `NA`. Against an app without a decision (before stage 4
of the redesign) every one of them is `NA` except `establishedAlongNm` and `maxBankCmdDeg`; on a
stage-4 app a timing key is `NA` when its line was not spoken.

### Running and comparing

```
VirtualPilot.exe approach --selftest
VirtualPilot.exe approach [--scenario TEXT] [--out file]
VirtualPilot.exe approach --trace NAME
VirtualPilot.exe approach --trace NAME --record FILE
VirtualPilot.exe approach --scenario TEXT --record FILE
VirtualPilot.exe approach --capture-gain K [--out file]
```

`--record FILE` writes the flight recording (see "Flight recordings" below) of the traced
scenario, or of every scenario `--scenario` picks, one after another. `--capture-gain K` sets visual
guidance's capture gain (deg·kt/nm, its `CaptureGainForHarness`) for every run; it is set by
reflection and refuses to run against an app from before stage 4. Stage 4's sweep also had
`--roll-in S` for a profile field `RollInTimeS`; the sweep picked 0 s and both were removed.

`--selftest` runs 33 checks of the harness itself: turn rate and direction, the 3° descent rate,
wind drift, the SimConnect signs, the north wrap, the runway geometry, great-circle flight, the
pilot's delay, roll rate, deadband and saturation, speech timestamps, the frame-rate runs, results
under a Swedish culture, the matrix size, the step measures, wheel contact, the run ending past the
runway, mirror-image downwinds, the landing classes, the rate measure reading the same at 20, 30
and 60 Hz, the noise touching only its three values with the stated spreads, a noisy run
repeating exactly, the touchdown sink rate written for a landing and `NA` otherwise, a knock that
the pilot has not yet heard staying on the bank, the speech-led pilot holding its bank without a
callout and following "3 right", the gust measures written for a gust run and `NA` otherwise, and
the flight recording reaching the harness (`record-hook`: the hook is set by reflection, so this
check is what catches a rename; against an app from before stage 3 it fails, as expected), a
scripted turn ending on its heading and the pilot rolling level after it, a start bank reaching
visual guidance on the first frame, and every results line carrying the ten joining keys. The
far-end check also flies a far-side start, which must not end where it starts. The
downwind mirror check counts passing the runway's far end and touching down within 100 ft of it as
the same ending, since which line the last frame crosses first is a coincidence of that frame. `--scenario` runs every scenario
whose name contains the text. `--trace` prints one scenario every half simulated second (phase,
position, path error, commanded and actual bank and pitch) with each spoken line.

`python tools/VirtualPilot/compare_approach.py BEFORE.results.txt AFTER.results.txt` compares two
runs scenario by scenario and prints `BETTER` and `WORSE` counts with up to six examples each. A
change counts as worse when `established` goes from True to False, `endReason` leaves `touchdown`,
`finalXteRmsFt` or `pathRmsFt` rises by more than 10 % and 15 ft, `overshootFt` by 100 ft,
`maxDescentFpm` by 200, `touchdownVsFpm` by 100, `pitchJitterDeg`, `pitchRateRmsDegS`, `bankRateRmsDegS` or
`bankCalloutsPerMin` by 20 %, `|selfFeedback10s|` by
0.1, `selfFeedback10s` changes sign, the touchdown point moves more than 25 ft farther from the
centreline, `gustCalloutDelayS` rises by more than 0.3 s, `gustRecoveryS` by 20 %, or
`gustXteMaxFt` by 10 % and 15 ft, or `establishedAlongNm` falls by more than 0.5 nm to below the
minimum final (6.0 nm, 1.0 for a run whose name contains `LIGHT`). Better is the mirror of each,
except that a sign change of `selfFeedback10s` is always worse when the new value is more than 0.1
from zero; within 0.1 it is noise, since the vertical path (stage 5) aims at near zero. A third list,
`CHANGED`, has the moves with no single right direction: the touchdown point moving more than
300 ft along the runway, the end reason changing between two outcomes neither of which is a
touchdown, and `decided` changing. The harness is deterministic: two runs of one build compare as `BETTER {}`, `WORSE {}`
and `CHANGED {}`.

### Flight recordings

Visual guidance records every approach, so a flight in the sim can be measured with the harness's
own measures instead of judged by feel (VP-2, added in stage 3 of the redesign, 2026-11-04). In the
app the lines go to `%APPDATA%\MSFSBlindAssist\logs\visual_guidance_flight.log`, through the log
channel `visual_guidance_flight`, behind the usual time stamp and tags. The file rotates at 5 MB
into `visual_guidance_flight.1.log` to `.3.log`; a frame line is about 300 bytes, so at 30 frames
a second that is about every 10 minutes of guidance (5 at 60), and a long approach can span two files. The harness writes the same lines,
without the log's prefix, with `--record FILE`.

Each line starts `VGREC` and holds space-separated `key=value` fields in the invariant culture:

| line | when | fields |
|---|---|---|
| `VGREC start` | Ctrl+V (`Initialize`) | `apt`, `rwy`, threshold `lat` and `lon`, course `crs` (true) and `crsMag`, threshold `elev`, glide path `gp`, `gsBias` and `flareBias` (the profile's glideslope and flare altitude biases) |
| `VGREC f` | every update | `t` (seconds since Ctrl+V), `ph` (the phase), `lat`, `lon`, `alt` (MSL), `agl`, `gs`, `vs`, `trk` and `hdg` (magnetic), `pitch` (nose up +), `bank` (right +), `aoa`, the commands `dPitch` and `dBank`, the vertical law's target `tFpm` and height error `altErr` (+ high), cross-track `xte` (nm, + right of the centreline facing the runway) and straight-line `dist` (nm) to the threshold |
| `VGREC say` | everything visual guidance speaks | `t`, `imm` (1: it interrupted), `text` (last, to the end of the line) |
| `VGREC stop` | visual guidance off | `t` |

Latitude and longitude have 7 decimals, `t` 3, the angles (`pitch`, `bank`, `aoa`, `dPitch`,
`dBank`) 4, `xte` and `dist` 5, everything else 2. The record holds what visual guidance read, so in
the harness's NOISE runs `vs`, `aoa` and `trk` carry the noise.

`python tools/VirtualPilot/analyze_vg_flight.py RECORDING [--segment FROM,TO]` reads the file (with
its rotated older parts first), splits it into approaches at each `start` line and prints for each:
the runway, the duration and last phase; when it was established (within 0.1 nm and 5° of the course
for 3 s, as the harness counts it); and on the final segment from then (5.0 to 0.5 nm along the
course by default; use `--segment 3.0,0.3` for a light aircraft, as the harness does):

- `finalXteRmsFt`, `finalXteMaxFt` and `pathRmsFt`, as in the results line (the path from `altErr`);
- `pitchRateRmsDegS` and `bankRateRmsDegS`, how fast the commands moved;
- `followPitchRmsDeg` and `followBankRmsDeg`, the RMS of command minus actual: how closely the pilot,
  or the autopilot, flew the tones;
- the sensor noise: the standard deviation of `vs`, `aoa` and `trk` around their centred 1 s moving
  average, which is how the NOISE family's assumed spreads can be checked against the sim;

and for the whole approach `touchdownVsFpm`, the descent rate at the first frame whose wheels
(`alt − elev − flareBias`) are at or below the runway. The harness stops a run at wheel contact and
records no frame there, so when the last frame is above the runway but the next would reach it, that
next frame is extrapolated from the last two; an approach that never got that low reads NA. Last,
bank callouts a minute.

Checked against the harness on 2026-11-04, a recording of `--trace` against the results line the
same run prints:

| run | measure | script | harness |
|---|---|---|---|
| `FINAL-A-JET-x0.2R-h+200-d0.5` | `finalXteRmsFt` | 20.7 | 20.7 |
| | `pathRmsFt` | 18.0 | 18.0 |
| | `pitchRateRmsDegS` | 0.0085 | 0.0084 |
| | `bankRateRmsDegS` | 0.0662 | 0.0662 |
| | `touchdownVsFpm` | 388 | 388 |
| `NOISE-A-JET-f30` | noise in `vs`, `aoa`, `trk` | 29.5 fpm, 0.197°, 0.198° | assumed 30 fpm, 0.2°, 0.2° |

The pitch rate differs by 1.2 % because the commands are recorded to 4 decimals. A NOISE run's
touchdown sink from the record (262 fpm) is not the harness's (366): the record has the noisy
vertical speed visual guidance read, the harness the true one.

### Baseline: today's visual guidance (2026-11-02)

Measured at 0463330e9, before any change to the guidance (the app's code is the same from f4a129b48,
which added the hooks, to the end of stage 1). Later stages of the redesign compare their own run
against it with `compare_approach.py`. While the harness is unchanged, the baseline is the run of
the harness built at 0463330e9. Once the harness changes (a fix, a new measure or scenario), the
baseline has to be flown again with the new harness against the stage-1 app: make a worktree at
0463330e9, copy the current `tools/VirtualPilot` over its own, build and run it there. That only
works while the harness needs no app hooks the stage-1 app lacks.

- Final tracking, jet: 0 ft RMS from a start on the centreline, about 21 ft from a start 0.2 nm off
  (24 runs). The median over every jet run with a final segment is 21.0 ft.
- Final tracking, light aircraft: median 368 ft. From 0.2 nm (1,215 ft) off at 4 nm, today's arc
  capture asks for about 3° of intercept near the line and is still about 590 ft off at 3 nm. It
  counts as established anyway, because 0.1 nm is 608 ft, so `established` is not a tracking
  result on its own. The light intercepts that land (the 1.0 s pilot) put the wheels down about
  350 ft beside the runway (`offRunway`, 12 runs, and one 90 kt run).
- Jet intercepts: established after about 172 s, no overshoot, final cross-track median 70 ft
  because the capture is still closing at 5 nm.
- Path: median `pathRmsFt` 10.0 ft over 130 runs (jet 5.5 ft, light 10.3 ft).
- Self-feedback: the jet's command moved 0.67° the other way per degree the pilot pitched after 3 s
  and 1.52° after 10 s (`selfFeedback` −0.671 and −1.524); the light aircraft's −0.415 and −0.741.
  The calculation from the code predicted about −1.2 and −0.6.
- Frame rate: `pitchJitterDeg` 0.0076, 0.0092 and 0.0127 at 20, 30 and 60 frames a second. A
  smooth command changes less per frame as the frame rate rises; this one changes more.
- Speech: median 15.0 bank callouts a minute (jet 26.5, light 12.4) and a median of 52 lines per run
  cut off by the next. Bank callouts can come one frame apart: "3 right", "6 right", "9 right"
  within 0.07 s.
- End reasons: 86 touchdowns, 46 past the runway's end, 13 beside the runway, 2 step runs.
- Light aircraft flare: 38 light runs float past the runway's far end. The flare commands a fixed
  6° of pitch against a 4° trim angle of attack, so the aircraft climbs out of the flare, falls back
  to final and repeats. Whether it lands at all depends on reaction timing: 34 of the 38 are the
  0.5 s pilot. The other 4 are the 1.0 s pilot's downwinds.
- Touchdown: the jets that reached final touched down 761 to 1,299 ft past the threshold (median
  1,147 ft, 67 runs); the light aircraft that landed, 1,780 to 2,844 ft past it.
- Downwinds: none of the 16 is established. Today's guidance says "Extending", flies them outbound,
  turns them in once they pass abeam the threshold, and says "Established on centerline" while they
  are still 3 nm to the side. They reach the runway's far end in the flare, wheels under 30 ft, 1,750
  to 1,960 ft beside it on their own side. The jets descend at up to 1,661 fpm on the way.
- Two quirks in the traces: "5 miles" is spoken on activation when guidance starts inside 5 nm, and
  the activation sentence is cut off by "On commanded path" in the same frame.

Stage 2 added the NOISE family and the two rate measures, so the baseline was flown again with the
extended harness against the same stage-1 app, at the commit "test(virtualpilot): sensor noise and a
frame-rate-independent smoothness measure" (`approach_baseline_v2`). Its 147 old lines are
identical to the stage-1 baseline's; `compare_approach.py` prints `BETTER {}`, `WORSE {}` and
`CHANGED {}` with 4 scenarios only in the new run. The 4 NOISE runs add 2 touchdowns (the jets) and
2 runs past the runway's end (the light aircraft). Stage 2's review then added `touchdownVsFpm`, so
the baseline was flown once more the same way (`approach_baseline_v3`, at b6f2b03e6's harness): its
lines are identical to v2's apart from the new key. Sink at touchdown: jet median 377 fpm (69
landings, at most 564), light median 80 fpm (19 landings).

Stage 3 added the GUST family and the three gust measures, so the baseline was flown once more
(`approach_baseline_v4`): a worktree at c2d7510df, whose app code is the stage-1 app's, with the
stage-3 harness copied in. With the three gust keys left out, its 151 old lines are textually
identical to v3's; the 8 GUST lines are new. The stage-2 code was flown with the same harness
(`approach_stage2_v2`), and its 151 old lines are identical to `approach_stage2`'s the same way.
Later stages compare against v4.

Stage 4 added scripted turns, a start bank, the six joining families and the ten joining keys, so
both were flown again: `approach_baseline_v5` (a worktree at c2d7510df, the stage-1 app, with the
stage-4 harness copied in) and `approach_stage3_v2` (a worktree at ca728302f, the stage-3 code).
With the ten joining keys left out, the 159 old lines of each are textually identical to
`approach_baseline_v4`'s and `approach_stage3_final`'s; on the old lines the joining keys are `NA`
apart from `establishedAlongNm` and `maxBankCmdDeg`. Against the stage-1 app the `record-hook` check
fails, as before. Stage 4 compares against both.

What today's guidance (the stage-3 code) does in the new families:

- Vectoring: all 16 established, but where today's laws happen to deliver them, not where a final
  would start. The jets flying parallel are established 4.3 nm out, inside the 6 nm minimum; those
  flying away are turned through about 150° and established 8.2 nm out. The light aircraft are
  established 0.3 nm (parallel) and 1.7 nm (away) out, and all 8 float past the runway's end.
- Far side: "Extending" flies the aircraft back toward the field (descending at up to 1,900 fpm on
  the jets), turns it in once it passes abeam the threshold, and says "On vertical profile" while it
  is still 1 nm to the side. The jets land 3,400 ft short of the threshold and 1,350 ft beside the
  centreline on their own side; the light aircraft count as established 1.4 nm past the threshold,
  over the runway, and float past its end. None is established before the threshold.
- Too close: the jets are established 2.5 nm out and land; the light aircraft are never established
  and float past the runway's end.
- Turning: today's guidance takes over during the scripted turn; the jets are established 5.8 nm out,
  the light aircraft 0.7 nm.
- Heading change: the jets are established 5.0 nm out, the light aircraft 0.6 nm; nothing is said
  when the heading stops crossing final.
- Shallow: more than 2 nm off, today's intercept law turns the 15° cut into its own 60° one (the
  owner's first complaint), so the jets are established 14 nm out instead of joining at 8.8 nm; the
  light aircraft at 4.7 nm.
- The largest commanded bank is 25° on the jets and 15° on the light aircraft.

- `pitchRateRmsDegS` on the clean jet final: 0.153, 0.275 and 0.761°/s at 20, 30 and 60 frames a
  second. A command that ran on real time would read about the same at all three.
- With noise, the pitch command moves at 27.6°/s RMS at 30 frames a second and 90.1°/s at 60 (jet;
  the light aircraft 27.8 and 90.2). The same final without noise reads 0.14°/s (jet) and 1.56°/s
  (light). Today's damping term divides the vertical speed's change by the frame time, so noise on
  the vertical speed reaches the tone almost undamped.
- Tracking with noise, at 30 and 60 frames a second: jet `finalXteRmsFt` 26.0 and 27.2 ft and
  `pathRmsFt` 23.1 and 23.0 ft (20.3 and 17.8 ft without noise); light 336.7 and 334.0 ft, and 43.9
  and 44.1 ft (368.5 and 41.0 ft without noise).

The model is simpler than the sim: a point mass, speed held, a fixed trim angle of attack, no ground
effect, flat terrain, a constant wind, no turbulence, and sensor noise only in the NOISE runs. A real
light aircraft at 6° of pitch with the power off would slow down and sink, so the float is larger
here than in the sim. It is still the finding it is: the fixed-pitch flare is what the redesign
replaces.

### Stage 2: real-time limits (2026-11-04)

Every rate limit, smoothing filter and derivative in visual guidance now runs on the measured frame
time (VG-20), with the numbers it was designed with at one update a second, except the flare's pitch
rate (10°/s instead of 1.5, see "The flare" below). Measured at a6092d4c3 against
`approach_baseline_v3` (`approach_stage2`): `BETTER` pitchJitterDeg down 111, pitchRateRmsDegS down
118, now touches down 7, bankRateRmsDegS down 40, |touchdownXteFt| down 12, bankCalloutsPerMin down 4,
|selfFeedback10s| down 1; `WORSE` bankRateRmsDegS up 7, maxDescentFpm up 9; `CHANGED`
touchdownAlongFt moved 31, endReason 5.

The plan's three acceptance checks:

- **A, lateral: passed.** No `finalXteRmsFt`, `overshootFt` or "no longer established" line, and no
  touchdown farther from the centreline. Jet `finalXteRmsFt` moved by −3.6 to +2.7 ft (median +0.2)
  over 70 runs; the median is 21.8 ft against the baseline's 21.2. On the 24 jet finals started
  0.2 nm off it rose by 0.1 to 0.8 ft, and their worst point on the final segment by up to 20.5 ft
  (runway A, 0.2 nm left, 1.0 s pilot: 47.3 to 67.8 ft). The largest rise is a noise run:
  `NOISE-A-JET-f30` 26.0 to 28.7 ft (+10.4 %, under the compare rule's 15 ft); its 60 frames a
  second twin improved, 27.2 to 23.6 ft.
- **B, frame rate: five of six checks passed, one failed.** On `FPS-A-JET-f{20,30,60}`,
  `bankRateRmsDegS` is 0 at all three, `finalXteRmsFt` 0.0 and `pathRmsFt` 2.4 ft at all three, and
  the noisy jet's `pitchRateRmsDegS` is 0.1073 and 0.1072 at 30 and 60. The failure:
  `pitchRateRmsDegS` on the clean finals is 0.0185, 0.0226 and 0.0313°/s (max/min 1.69, limit 1.10).
  A per-frame dump shows all of it is two steps at 1 nm, where the vertical law switches to holding
  the natural descent rate (`GLIDESLOPE_LOCK_DISTANCE_NM`): +0.043° entering the lock and −0.012°
  one second later, when that step leaves the 1 s derivative window. The steps are the same size at
  every frame rate; a fixed step adds to an RMS rate as the square root of the frame rate (√3 = 1.73,
  measured 1.69), and without the two steps the measure is 0.0002°/s at all three. Stage 4 removes
  the lock. A 0.04° step is about 2 Hz on the tone.
- **C, calmer: passed.** `pitchRateRmsDegS` with noise fell from 27.6 to 0.107°/s (jet, 30 frames a
  second), 90.1 to 0.107 (jet, 60), 27.8 to 0.135 (light, 30) and 90.2 to 0.136 (light, 60). The
  bank command with noise also calmed: `bankRateRmsDegS` 8.5 and 17.0 to 2.6 and 2.8°/s (jet), 6.6
  and 13.0 to 2.5 and 2.8 (light).

Since A passed at the design values (S0), the plan chose S0 and needed no sweep. The sweep was flown
anyway, for information, with the bank rate in the default profile and the two cross-track
constants changed. It was flown before the flare fix; its columns measure the final segment, which
ends before the flare.

| id | bank rate (°/s) | cross-track rate window (s) | its smoothing per second | median jet `finalXteRmsFt` | jet 0.2 nm finals: RMS change | worst-point change | noisy jet `bankRateRmsDegS` f30 / f60 |
|---|---|---|---|---|---|---|---|
| baseline | 3 (per update) | 5 samples | 0.85 per update | 21.2 | | | 8.49 / 16.97 |
| **S0** (chosen) | 3 | 4.0 | 0.85 | 21.8 | +0.1 to +0.8 | −0.1 to +20.5 | 2.64 / 2.80 |
| S1 | 3 | 1.0 | 0.85 | 21.8 | +0.2 to +1.0 | +0.2 to +21.1 | 2.64 / 2.80 |
| S2 | 3 | 1.0 | 0.5 | 21.4 | +0.1 to +0.4 | +0.2 to +7.7 | 2.64 / 2.80 |
| S3 | 6 | 1.0 | 0.5 | 21.2 | −0.1 to +0.6 | 0 to +3.0 | 4.64 / 5.27 |
| S4 | 10 | 1.0 | 0.5 | 21.1 | −0.2 to +0.6 | −0.9 to +1.3 | 6.48 / 8.03 |
| S5 | 10 | 0.5 | 0.2 | 21.2 | −0.1 to +0.7 | 0 to +14.3 | 6.48 / 8.03 |
| S6 | 90 | 0.15 | 0.0076 | 21.2 | −0.1 to 0 | 0 | 8.49 / 16.97 |

Every row passes A and C and fails B the same way. S6 reproduces today's lateral behaviour at 30
frames a second exactly, as expected. S2 keeps the profiles' bank rates and tracks the 0.2 nm finals
a little tighter than S0 (on the run that moved most, runway A, 0.2 nm left, 1.0 s pilot, its worst
point is 54.2 ft against S0's 67.8 and the baseline's 47.3), but speaks more bank callouts on
`FINAL-B-JET-x0.2R-*-d1.0` (13.43 a minute against S0's 11.15 and the baseline's 12.85). S3 to S5
double or more every aircraft's bank rate and make the bank command under noise 1.7 to 3 times
rougher.

**The flare.** Stage 2's review found that the flare's pitch limit, 1.5°/s in the 1 Hz design, had
become real: a jet passes the 30 ft flare height about 2.4 s before touchdown, and the cue needed
2.7 s to rise from about 2° to the 6° flare pitch, so it finished after the wheels touched. Before,
it jumped there in two frames. With `touchdownVsFpm` added, the effect measured, against
`approach_baseline_v3`:

| flare pitch rate (°/s) | landings > 100 fpm firmer | jet sink at touchdown, median / max (fpm) | light sink, median (fpm) | light landings |
|---|---|---|---|---|
| baseline (a jump in two frames) | | 377 / 564 | 80 | 19 |
| 1.5 (the design, run on real time) | 101 | 608 / 748 | 360 | 39 |
| 5 | 30 | 426 / 605 | 249 | 39 |
| **10** (chosen) | 0 | 396 / 576 | 91 | 26 |
| 999 (no limit) | 0 | 377 / 556 | 145 | 19 |

10°/s is the slowest rate tried with no landing more than 100 fpm firmer than the baseline: the jets
are 2 to 56 fpm firmer (median +28), the light aircraft 58 fpm softer to 18 fpm firmer (median +9).
The cue now reaches the flare pitch in about 0.4 s. `The_flare_cue_reaches_the_flare_pitch_within_half_a_second`
pins it. Stage 6 replaces this flare.

Every other `WORSE` and `CHANGED` line, explained:

- `bankRateRmsDegS` up (7 light intercepts): 0.0012–0.0021 to 0.0016–0.0028°/s, the 20 % rule on
  values near zero.
- `maxDescentFpm` up (the 8 jet downwinds, 1,533 and 1,661 to 1,902 and 1,993 fpm, and the 160 kt
  intercept, 1,079 to 1,340): both start level while the vertical law asks for a descent at once (the
  downwinds 2,258 ft above the path, target 1,500 fpm; the intercept about 850 fpm). The command is
  built from the smoothed vertical speed, which now has its designed 3 s smoothing instead of about
  0.1 s, so it keeps asking for more descent until the smoothed rate catches up, and the pilot passes
  the target. This is the vertical-speed law stage 4 replaces, and on the downwinds the Extending
  phase stage 4 replaces too.
- Now touching down (7: the 6 light finals starting 200 ft low with the 0.5 s pilot, and
  `NOISE-A-LIGHT-f60`): they balloon out of the fixed 6° flare as before, and now land on a later
  cycle, 6,030 to 6,640 ft past the threshold, at 249 to 274 fpm, where before they floated past the
  end.
- `endReason` pastRunwayEnd to offRunway (5 jet downwinds): they now touch down at about 190 to
  260 fpm, 1,640 to 2,060 ft beside the runway near its far end, where before they passed the end in
  the flare about as far beside it: today's Extending, which stage 4 replaces.
- `touchdownAlongFt` moved (31): the light aircraft with the 1.0 s pilot (18 finals, 12 intercepts)
  balloon out of the fixed 6° flare one more time before the wheels catch, and land about 1,800 ft
  further along (for example 2,234 to 4,077 ft past the threshold), still on the runway and at about
  the same sink; one 90 kt light run lands 400 ft further along. Jets touch down 839 to 1,197 ft past
  the threshold (761 to 1,299 before).
- `|touchdownXteFt|` down (12 light intercepts with the 1.0 s pilot): landing later on the same
  slowly converging track, they touch down about 35 ft closer to the centreline (for example 369 to
  332 ft beside it).
- Self-feedback: the jet's command moves 0.18° the other way per degree pitched after 3 s (0.67
  before) and 1.37° after 10 s (1.52); the light aircraft's 0.09 and 0.69 (0.42 and 0.74). The
  vertical-speed law still runs away from the pilot over 10 s; stage 4 replaces it.

### Gusts with today's callouts (2026-11-04)

The GUST runs flown on the stage-2 code (`approach_stage2_v2`), before stage 3 changes when bank
callouts are spoken. These are the numbers stage 3's callout gate is measured against.

| run | first callout after a knock (s) | recovery (s) | worst cross-track after a knock (ft) | bank callouts a minute | lines cut off |
|---|---|---|---|---|---|
| jet, 2°, tone | 0.09 | 1.10 | 26.8 | 29.4 | 85 |
| jet, 2°, speech | 0.08 | 2.08 | 57.5 | 11.7 | 27 |
| jet, 4°, tone | 0.07 | 1.57 | 34.0 | 26.0 | 75 |
| jet, 4°, speech | 0.04 | 10.91 | 50.3 | 14.6 | 38 |
| light, 2°, tone | 0.12 | 1.13 | 26.1 | 13.9 | 57 |
| light, 2°, speech | 0.07 | 1.13 | 30.9 | 10.7 | 39 |
| light, 4°, tone | 0.04 | 1.56 | 25.3 | 19.7 | 83 |
| light, 4°, speech | 0.04 | 11.28 | 29.2 | 16.8 | 64 |

Today's callouts answer a knock within about a tenth of a second, while it is still building: a 4°
knock is spoken as "1 right", "2 right", "3 right", "4 right" within 0.27 s, each line cutting off
the one before. The speech-led pilot takes about 11 s to recover from a 4° knock because of what
today's callouts leave out: once they have said "1 right", they stay silent while the error rounds
to the same 1, so in `GUST-A-JET-g4-speech` the pilot held about 1.4° short of the command from
41.4 to 46.6 s, until "2 right". The light runs end past the runway's far end, as their `FINAL-A`
twins do (the fixed flare's balloon).

### Stage 3: callouts, F and the recording (2026-11-04)

Bank callouts now speak through `VisualGuidanceBankCalloutGate` (VG-21), each of visual guidance's
own lines holds them back for its measured speaking time, the centreline callouts are gone, F reads
the status (VG-22), and every update is recorded (VP-2). Measured on the final code
(`approach_stage3_final`) against the stage-2 code with the same harness (`approach_stage2_v2`):
`BETTER` bankCalloutsPerMin down 129, bankRateRmsDegS down 6, |touchdownXteFt| down 4, gustRecoveryS
down 3, gustXteMaxFt down 1; `WORSE` bankRateRmsDegS up 10, gustCalloutDelayS up 8, pitchRateRmsDegS
up 7, pitchJitterDeg up 6, gustXteMaxFt up 2, finalXteRmsFt up 1, gustRecoveryS up 1,
|touchdownXteFt| up 1; `CHANGED` touchdownAlongFt moved 5, endReason 1.

How it got there. The gate as the spec and plan had it (`approach_stage3`) failed checks A and C on
the speech-led gust runs, so the stage stopped for the owner. A 3 s repeat of an unchanged callout was
then built and measured (`approach_stage3_r3`; limited to errors of 1° or more, `approach_stage3_r3b`),
and the owner removed it: "I just want the degree to be announced as soon as plane goes off course
while in final." The gate already does that: a degree off on final is spoken a quarter of a second
after the error reaches 1°. The stage's review then found the bank callouts, which interrupt, cutting
off visual guidance's own queued lines ("Visual guidance active", "Extending, turn to heading 175",
"Flare", "Touchdown", the mile calls), so each of those lines now holds the bank callouts for its
measured speaking time (VG-21).

The plan's three acceptance checks, on the final code:

- **A, final tracking: passed except for one speech-led gust run, accepted by the owner.** No
  `finalXteRmsFt`, `overshootFt` or "no longer established" line in any FINAL, FPS, NOISE, WIND-XW or
  offset SPEED run, or in any tone-led GUST run. `GUST-A-LIGHT-g2-speech` went from 14.5 to 30.9 ft: its
  pilot steers by speech alone, hears no tones, and after "1 right" sat 0.6 to 0.8° off, which the
  callouts no longer repeat. A real pilot hears that error in the tones.
- **B, quieter: passed.** No centreline callouts in any run. The median bank callouts a minute fell
  from 14.98 to 1.37, and the lines cut off by the next from 11,133 over the 159 runs (median 50) to 1
  (median 0).
- **C, gusts heard quickly: five runs passed; the three that did not are accepted.** In five GUST runs
  a knock is spoken 0.50 to 0.56 s after it starts on average (a 2° knock once, about 0.25 s after it
  settles). Three average 0.78 to 0.80 s, each from one knock in three: the light tone pilot rolls back
  so fast after its 0.5 s reaction that the error passes 4, 3 and 2 quicker than the 0.25 s hold (it
  costs that pilot nothing: recovery and tracking identical), and in the two 4° speech runs the knock
  landed on a standing error and made more than 5°, past the tone range, where the owner's rule waits
  for the bank to settle. The speech-led pilot recovers from a 4° knock far faster than with today's
  callouts (11 s to 2.5 and 2.8 s), but drifts farther in two runs (jet 4°: 50.3 to 73.8 ft; light 2°:
  30.9 to 59.6 ft), for the reason under A.

The GUST runs, today's callouts / now:

| run | first callout (s) | recovery (s) | worst cross-track after a knock (ft) | final RMS (ft) | bank callouts a minute | lines cut off |
|---|---|---|---|---|---|---|
| jet, 2°, tone | 0.09 / 0.50 | 1.10 / 1.10 | 26.8 / 26.8 | 19.2 / 19.2 | 29.4 / 2.9 | 85 / 0 |
| jet, 2°, speech | 0.08 / 0.52 | 2.08 / 1.36 | 57.5 / 41.3 | 27.6 / 19.8 | 11.7 / 9.1 | 27 / 0 |
| jet, 4°, tone | 0.07 / 0.56 | 1.57 / 1.57 | 34.0 / 34.0 | 19.8 / 19.8 | 26.0 / 2.9 | 75 / 0 |
| jet, 4°, speech | 0.04 / 0.78 | 10.91 / 2.80 | 50.3 / 73.8 | 28.5 / 32.1 | 14.6 / 8.6 | 38 / 0 |
| light, 2°, tone | 0.12 / 0.53 | 1.13 / 1.13 | 26.1 / 26.1 | 15.6 / 15.6 | 13.9 / 1.8 | 57 / 1 |
| light, 2°, speech | 0.07 / 0.51 | 1.13 / 2.09 | 30.9 / 59.6 | 14.5 / 30.9 | 10.7 / 10.1 | 39 / 0 |
| light, 4°, tone | 0.04 / 0.80 | 1.56 / 1.56 | 25.3 / 25.3 | 13.5 / 13.5 | 19.7 / 1.4 | 83 / 0 |
| light, 4°, speech | 0.04 / 0.79 | 11.28 / 2.50 | 29.2 / 28.0 | 14.5 / 18.1 | 16.8 / 10.3 | 64 / 0 |

With the repeat, the speech-led runs had tracked better than today's in every case (final RMS 16.2,
13.8, 6.2 and 15.7 ft), at a median of 5.7 bank callouts a minute and "1 right" every 3 s for about
340 s in the light 90° intercepts, where the tone pilot hovers half a degree off. The tone-led runs
fly exactly as before in every version; only what they hear changes.

Every other `WORSE` and `CHANGED` line, explained:

- `bankRateRmsDegS` up on the jet intercepts on runway B (`INT-B-JET-R30-d0.5` 0.177 to 0.316,
  `-R30-d1.0` 0.189 to 0.376, `-R45-d1.0` 0.120 to 0.145°/s): the first big callout now waits for the
  activation sentence and "Intercepting final, 3.0 miles to final" (4.4 s; "10 left" at 4.4 s instead
  of 1.0 s, while the tone already sits fully left), and in the capture turn, past the tone range, the
  pilot steers by callouts, so the path shifts. These intercepts are still closing on the centreline at
  5 nm (the baseline's finding); final cross-track 77.6 to 86.2 ft RMS on the largest, under the compare
  rule. Stage 4 replaces the capture.
- `pitchRateRmsDegS` and `pitchJitterDeg` up on the light 90° intercepts (from 0.0000 to at most 0.067°/s):
  the slow light capture is counted as established when the aircraft comes within 0.1 nm (608 ft), now
  1 to 1.4 s earlier (`INT-B-LIGHT-R90-d0.5` 300.3 to 298.9 s), so the final segment now starts just
  before 1 nm and includes the 0.04° step of the old 1 nm lock (stage 2's finding), which stage 4
  removes.
- `bankRateRmsDegS` up on light intercepts (0.0013 to 0.0027°/s): the 20 % rule on values near zero.
- The jet downwinds (`endReason` 1, `touchdownAlongFt` 5, `|touchdownXteFt|` 4 down and 1 up): today's
  Extending phase turns them past the tone range, where the pilot steers by callouts, so the new timing
  and the holds move their path. `DW-B-JET-L-d0.5` now touches down beside the runway near its far end
  instead of passing the end in the flare; four touch down 340 to 450 ft farther along and 70 to 100 ft
  nearer the centreline; `DW-B-JET-R-d0.5` lands about 1,190 ft earlier and 225 ft farther out (1,638
  to 1,863 ft). Stage 4 replaces Extending.
- `gustCalloutDelayS` up (all 8): by design. Today's callouts answer while the knock is still building
  and cut each other off; the gate speaks once, after the error has held a quarter of a second.

### Stage 4: joining final (2026-11-04)

Visual guidance now decides, once the wings are nearly level after Ctrl+V, whether the aircraft is on
final, joining (its heading crosses final at least the minimum final out: the heading is held until
the turn onto final, VG-23), vectoring (BA picks the cut), or needs the pattern (flown as "Unable" until
the pattern is built). One capture law with a speed-scaled gain turns the aircraft onto final; the
precision track within 0.05 nm is unchanged.

**The sweep.** The capture gain at 4200 (the old 30°/nm at 140 kt), 7000 and 10259 deg·kt/nm
(FlyByWire's), each with a roll-in time of 0, 2 and 4 s, against `approach_stage3_v2`, on the plan's
checks: A, final tracking (no `finalXteRmsFt` up, `overshootFt` up or "no longer established" on any
FINAL, FPS, NOISE, WIND-XW, STEP, GUST or offset SPEED run); B, joining holds the heading (every INT,
SHALLOW, TURN, NOCROSS, WIND-TW and SPEED R45 run decides Joining, commands under 0.5° of bank before
"Turn onto final", hears it, is established, never leaves Joining; NOCROSS hears "Heading does not cross
final" once; TURN decides under 5° of bank); C, vectoring reaches final (VEC and the light CLOSE runs
established at least the minimum final less 0.5 nm, jet, or 0.3 nm, light; no overshoot over 600 ft
outside the pattern runs; jet intercepts established 5 nm out or more; the light 90° intercepts
established sooner than at stage 3); D, the countdown (|error| median ≤ 3 s and worst ≤ 5 s at 30 s,
2 and 3 s at 15 s); E, no commanded bank over 25°. First round, with BA picking its cut as if the
aircraft already flew it:

| gain | roll-in | A | B | C | D | E | median time to establish |
|---|---|---|---|---|---|---|---|
| 4200 | 0 / 2 / 4 s | fail 1 / 6 / 27 | fail 1 | fail 18 | fail | pass | 174 / 181 / 188 s |
| 7000 | 0 / 2 / 4 s | pass | pass | fail 2 / 2 / 3 | fail (30 s call 6.6 to 8.6 s off) | pass | 139 / 147 / 144 s |
| 10259 | 0 s | pass | pass | fail 4 | pass | pass | 129 s |
| 10259 | 2 s | pass | pass | fail 2 | pass | pass | 129 s |
| 10259 | 4 s | pass | pass | fail 6 | pass | pass | 132 s |

None passed. At 10259 with no roll-in only the four jets vectored from 2 nm beside a 10 nm final,
flying parallel, fell short: established 5.43 to 5.46 nm out against 5.5. BA picked its 30° cut from a
sum (`AlongToEstablishNm`) that assumes the aircraft is already on the cut, and from parallel the turn
onto it took about 20 s and 0.7 nm along final while closing 0.13 nm. At 4200 the jets on 30° cuts were
established 4.6 nm out; at 7000 the light aircraft starting too close were never established. The
stage stopped for the owner, who chose (2026-11-04) to have BA count the turn onto its cut, from the
aircraft's own speed and bank settings (`VisualGuidanceJoin.TurnOntoCut`: rolling in and out at the
profile's bank rate to at most its largest approach bank). Second round:

| gain | roll-in | A | B | C | D | E | median time to establish |
|---|---|---|---|---|---|---|---|
| 4200 | 0 / 2 / 4 s | fail 1 / 6 / 27 | fail 1 | fail 14 | fail | pass | 170 / 176 / 184 s |
| 7000 | 0 / 2 / 4 s | pass | pass | fail 2 | fail (5.3 to 7.2 s) | pass | 139 / 147 / 144 s |
| 10259 | 0 s | **pass** | **pass** | **pass** | **pass** | **pass** | **128 s** |
| 10259 | 2 s | pass | pass | fail 2 | pass | pass | 129 s |
| 10259 | 4 s | pass | pass | fail 2 | pass | pass | 132 s |

**The pick: FlyByWire's gain, 10259, with no roll-in**, the only combination that passes all five.
The roll-in time was removed with its profile field, `--roll-in` with it (plan decision 7). Two
defects in the first round were fixed before it, each with a failing test: joining's wings-level
command followed the aircraft's bank down at the rate limit after a press in a turn, and a vectored
aircraft's countdown was timed on the few degrees it had while still turning onto its cut, which said
"30 seconds" up to 20 s late on the light 0.2 nm finals.

**The final run** (`approach_stage4`, the same as the pick's sweep run):

- A: no line. Final tracking is far tighter where the old arc capture was slow: the offset finals' median
  `finalXteRmsFt` 189.9 to 28.9 ft (`FINAL-A-LIGHT-x0.2R-h0-d0.5` 362 to 41 ft), the intercepts' 316.8
  to 19.3 ft.
- B: all 67 runs decide Joining; the largest commanded bank before "Turn onto final" is 0.00°; from
  "Turn onto final" to established a median 35.9 s, at most 56 s; every NOCROSS run hears "Heading does
  not cross final" once; the TURN runs decide at 4.93 to 5.00° (printed to two decimals; the app decides
  only under 5°).
- C: the vectored jets are established 5.64 to 8.53 nm out, the light ones 2.93 to 3.63 nm, the light
  aircraft starting too close 0.73 nm (need 5.5, 0.7 and 0.7); no overshoot outside the pattern runs; the
  jet intercepts are established 6.16 nm out or more; the light 90° intercepts in 107 s instead of 300.
- D: "Turn onto final in 30 seconds" a median 0.07 s off, worst 3.0 s (85 runs); 15 s 0.03, worst 1.23 s.
- E: the largest commanded bank in any run is 25.0°.

**After the checkpoint review** (a fresh reviewer, the same day) four things were fixed, each with a
failing test first: the turn onto final now comes whenever a joining track still meets final ahead of
the threshold, even inside the minimum final (before, the cue waited until 0.05 nm and then asked for a
sudden steep turn); "Unable" sits the pitch tone on the aircraft's own pitch, as joining does (before, it
asked for a dive toward the 3° path far below a far-side start); the mile calls are armed again once the
distance has gone 0.3 nm back above a mark; and while joining, banked 5° or more for a turn the pilot
makes on purpose, the bank callouts stay quiet. A to E are unchanged; against the run before the fixes
(`approach_stage4_precheckpoint`) only the pattern runs moved: flying "Unable" level, the 24 downwind and
far-side runs now fly on to the 900 s limit instead of meeting the ground, with fewer callouts and no
descent.

Against `approach_stage3_v2`: `BETTER` finalXteRmsFt down 94, bankRateRmsDegS down 88, bankCalloutsPerMin
down 56, establishedAlongNm up 38, maxDescentFpm down 26, pitchJitterDeg down 21, pitchRateRmsDegS down 19,
now touches down 11, |touchdownXteFt| down 9, now established 2; `WORSE` bankCalloutsPerMin up 80,
pitchJitterDeg up 76, pitchRateRmsDegS up 76, maxDescentFpm up 59, pathRmsFt up 50, bankRateRmsDegS up 36,
no longer touches down 19, no longer established 6, overshootFt up 2, |touchdownXteFt| up 1; `CHANGED`
endReason 28, touchdownAlongFt moved 17. Against `approach_baseline_v5`, the stage-1 app: `BETTER`
bankCalloutsPerMin down 173, pitchJitterDeg and pitchRateRmsDegS down 142, finalXteRmsFt down 94,
bankRateRmsDegS down 92, establishedAlongNm up 40, now touches down 17; `WORSE` maxDescentFpm up 65,
pathRmsFt up 50, bankRateRmsDegS up 39, pitchJitterDeg and pitchRateRmsDegS up 32, no longer touches down
18, gustCalloutDelayS up 8 (stage 3's, by design), finalXteRmsFt up 1 (stage 3's speech-led gust run).

Every `WORSE` and `CHANGED` line against stage 3, explained:

- **The vertical side of joining** (pathRmsFt up 50, maxDescentFpm up 59, no longer touches down 16 of the
  19, pitchJitterDeg and pitchRateRmsDegS up on the INT, TURN, NOCROSS, SHALLOW and SPEED R45 runs): by
  design. While joining, the altitude is ATC's or the pilot's, so the pitch tone sits on the aircraft's
  own pitch, and the harness's pilot, with no ATC to clear a descent, stays at the height it started:
  the path's height at 12 nm (jet) or 6 nm (light). A jet joining a 30° cut reaches final about 6.8 nm
  out some 1,900 ft above the path; today's vertical law then descends at up to 1,500 fpm and the jet
  floats past the runway (the 8 jet 30° intercepts, the jet TURN, NOCROSS and SHALLOW runs); the jets
  joining at 45° land about 3,500 ft past the threshold instead of 1,200 (touchdownAlongFt moved 17, all
  joining runs). The pitch
  measures rise for the same reason: the final segment starts while the law is still bringing the
  aircraft down. Stage 5's vertical path (the level hold while joining, the descent planned when
  vectoring, "You'll be 600 feet high at final") is the answer; the plan reports these, not judges them.
- **More bank callouts** (80 runs up; the median over all runs 1.6 to about 2 a minute, against 16 before
  stage 3): mostly the offset finals (48 runs, 1.2 to 2.3 a minute), which used to drift onto the line at
  the old arc's 30°/nm and are now vectored onto it with a real turn, and track far tighter for it (A);
  and the light 45° and 90° intercepts, whose capture is now a turn, not a drift. The most outside the
  pattern runs: 4.8 a minute on the short light runs starting too close (a steep turn in under 4
  minutes) and 3.6 on the jets vectored back from heading away. 56 runs went down, the pattern runs
  among them.
- **Lines cut off**: 33 runs count one line each "cut off" (stage 3: one run). None is: the measure
  estimates a line's length as words / 2.5 + 0.2 s, and "Turn onto final" (17), "Turn onto final in 30
  seconds" (4), "3 miles out" (1) and "1 mile out" (2) are followed by a bank callout exactly when their
  measured hold ends, which is the estimate or shorter (measured 1.11, 2.13, 0.99 and 0.85 s). The other
  5 are "On commanded path" followed by the next callout 1.0 to 1.2 s later, stage 3's fixed 1 s gap
  between callouts (VG-21), seen once at stage 3.
- `bankRateRmsDegS` up 36: the light intercepts and vectored light runs, from about 0.002 to 0.014°/s:
  the speed-scaled gain asks 158° of intercept per nm at 65 kt, so the light capture is quicker and its
  bank moves more near the line; the 20 % rule on near-zero values. The offset light finals went down
  from 0.30 to 0.005°/s.
- `pitchJitterDeg` up on the offset finals (jet 0.0002 to 0.0004°, light 0.0002 to 0.0015°): the 20 % rule
  on near-zero values.
- **The pattern runs** (DW, FAR, CLOSE jet: endReason 24 of the 28, no longer established 6, overshootFt
  up 2, no longer touches down 2): reported, not judged, until the pattern exists.
  They fly "Unable", heading and altitude held: the downwinds and far-side runs fly on beside or away
  from the field to the 900 s limit (`timeout`); the jets starting too close cut across final and fly on
  past the runway. Before the checkpoint review "Unable" followed the vertical law, which aimed at the
  path far below, and the downwinds met the ground about 8 nm out.
- `endReason` on the light 30° intercepts with the 1.0 s pilot (4, offRunway to pastRunwayEnd): they now
  reach final at their start height, as above, and float in the fixed flare (stage 6).
- `NOISE-A-LIGHT-f60` no longer touches down (Vectoring): a light run landing or floating in the fixed
  flare depends on timing; its tracking passed A.
- `WIND-TW-A-JET-d0.5` |touchdownXteFt| 25 to 54 ft: the 90° join with 15 kt behind it, turned onto final
  by the new law, touches down 54 ft left of the centreline (half the runway's width is 75 ft); established
  and tracked under A.

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
