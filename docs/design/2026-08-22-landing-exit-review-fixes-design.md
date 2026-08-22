# Landing-exit rollout: review follow-up fixes

**Date:** 2026-08-22
**Follows:** [2026-08-21-landing-exit-early-turn-design.md](2026-08-21-landing-exit-early-turn-design.md) (PR #204)
**Status:** design approved, ready for implementation

## Why

A high-effort review of PR #204 found that two of the PR's own new safeguards — the
early-vacate retarget and the handoff reachability guard — can both be bypassed on the
most common early-vacate path, and that the guard itself can be defeated by dirty
navdata. Four correctness defects were confirmed, along with three duplications that are
the mechanism by which this class of defect keeps recurring.

Every one of the four correctness findings has the same shape: **a rule that exists once
in the pure `RolloutExitGate` module is also expressed, slightly differently, in
`TaxiGuidanceManager`.** The two copies then disagree. The fix is to make the pure module
the single authority in each case, not to nudge a threshold until the reported symptom
goes away.

## The four defects

### D1 — the lateral dead band

`exitedLaterally` trips at `halfRunwayWidthFt + 30.0` ft (9.144 m).
`IsWithinRolloutRunwayLaterally` reports the aircraft as still on the runway up to
`halfWidthM + RUNWAY_CLEAR_MARGIN_M` (10.0 m). Same reference point, same projection,
two numbers — leaving a 0.856 m band in which the handoff fires while the aircraft still
reads as *on the runway*.

On a far early vacate `exitedLaterally` is the only reachable trigger (`turnBegun` is
proximity-gated to the 1,000 ft window, `pastExit` is false), so the handoff fires on the
first frame lateral distance crosses 9.144 m. Whenever the per-frame lateral step is under
0.856 m — the common case at typical update rates — that frame lands inside the band.
`offRunwayAtHandoff` then reads false, which skips `MatchEarlyVacateExit` entirely *and*
makes `IsHandoffRouteReachable` return true through its `if (!aircraftOffRunway) return
true;` early exit. `LoadRoute` re-routes to the **planned** exit: the KSEA long-way-round
this PR exists to eliminate, delivered with no announcement.

### D2 — the failed re-route resumes on a route to the wrong exit

When `MatchEarlyVacateExit` matches a substitute and `_rolloutExit` is swapped, the pilot
hears *"Left the runway short of taxiway Z. Now following taxiway J."* Every `LoadRoute`
failure path returns before `_route = route;`, so on failure `_route` is still the
touchdown route targeting **Z**. `handoffRerouted` is false, so the reachability guard —
gated on `handoffRerouted` — never runs, and the fallback re-anchors the segment cursor on
that stale route and resumes the tone. The pilot is steered toward the exit they just
vacated short of, seconds after being told otherwise.

CLAUDE.md states the invariant plainly: *"After an early vacate the handoff must never
re-route to the PLANNED exit… Retarget or conclude."* and *"`IsHandoffRouteReachable` must
gate **every** landing-exit handoff re-route."* Neither holds on this path.

### D3 — the guard trusts uncapped navdata width

`IsHandoffRouteReachable` derives its acceptance corridor straight from
`firstSegmentPathWidthFeet`. The codebase's own off-route logic caps the same field at
`OFF_ROUTE_PERP_WIDTH_CAP_FT` (300 ft) because navdata reports widths of thousands of feet
on apron-tagged rows. A 4,000 ft width yields a ~625 m corridor, so the guard passes at
essentially any cross-track — worst on exactly the airports with the dirtiest navdata.
Even a modest 260 ft mis-tag would have passed the original KSEA 53.9 m case the guard was
written for.

### D4 — one closure speaks for two different situations

The guard sets `_landingExitVacatedEarly` unconditionally, including when no early vacate
occurred (`offRunwayAtHandoff && !farFromPlannedExit`, reachable with `pastExit` true).
`HandleArrival` then falls back to `_route.DestinationName` and announces *"You have left
the runway short of Taxiway X"* to a pilot standing at or beyond X. A blind pilot told they
are somewhere they are not may manoeuvre to correct a position that was fine.

## The design

### 1. One definition of "off the runway"  *(D1)*

`RolloutExitGate` gains the canonical margin and a pure predicate:

```csharp
public const double RunwayClearMarginM = 10.0;

public static bool IsLaterallyClearOfRunway(double absLateralMetres, double runwayWidthFeet);
```

`TaxiGuidanceManager.RUNWAY_CLEAR_MARGIN_M` initialises from
`RolloutExitGate.RunwayClearMarginM` — the same one-source-of-truth pattern PR #204
already used for the `ROLLOUT_*` constants. `IsWithinRolloutRunwayLaterally` delegates to
the pure predicate, and `exitedLaterally` drops its hand-rolled `halfRunwayWidthFt + 30.0`
term in favour of the same call.

The two can then never disagree, because there is only one comparison left. The handoff
trigger moves 0.856 m later (9.144 m → 10 m), which is the conservative direction and
therefore cannot reintroduce the over-eager EDDB 24L → M3 case that the combined
`distToExit <= 250 || hdgDeltaAbs >= 8 || pastExit` gate fixed.

`lateralFromCenterlineFt` is still computed — the diagnostics and the overshoot gate both
read it. Only the threshold comparison is replaced.

**Rejected:** lowering `RUNWAY_CLEAR_MARGIN_M` to 9.144 m so the guards match the trigger.
CLAUDE.md makes `IsWithinRolloutRunwayLaterally` the shared authority for the
post-high-speed-exit pan floor as well, so re-tuning it changes unrelated tone behaviour.

**Rejected:** computing `offRunwayAtHandoff` as `exitedLaterally || !IsWithinRolloutRunwayLaterally(...)`.
That closes the gap but layers a special case over the inconsistency; the next reader still
finds two definitions of "off the runway".

### 2. Cap the width the guard trusts  *(D3)*

`RolloutExitGate` gains `MaxTrustedPathWidthFeet = 300.0`;
`OFF_ROUTE_PERP_WIDTH_CAP_FT` initialises from it. `IsHandoffRouteReachable` clamps
`firstSegmentPathWidthFeet` to that cap before deriving the half-width. A mis-tagged apron
row can no longer buy an unbounded corridor.

The clamp is one-sided. A *narrow* width still yields a narrow corridor, and a missing
width still falls back to the deliberately generous `HandoffReachDefaultHalfWidthM` — the
guard ends guidance, so thin navdata must never cause a false refusal.

### 3. Guard the route guidance actually resumes on  *(D2)*

Two changes in the handoff block:

- Track whether the early-vacate branch swapped the exit (`earlyVacateSwapped`). If the
  swap happened and the re-route then failed, **conclude** rather than resume — the
  surviving route targets the exit the pilot left short of.

  The closure can name the planned exit without any new plumbing: `LoadRoute`'s
  fresh-route reset block (`Routing.cs:449-465`, which nulls
  `_landingExitVacatedEarlyPlannedName`) sits *after* every failure exit, so a failed
  `LoadRoute` leaves the captured name untouched. The existing
  `vacatedEarlyPlannedNameAtHandoff` carry-across is needed only on the success path,
  which is exactly where it is already applied — do not add a second restore for the
  failure path.
- Move the reachability guard below the fallback re-anchor and point it at
  `_route.Segments[_currentSegmentIndex]` — *the segment the tone is about to steer at* —
  rather than at `_route.Segments[0]` of a rerouted route only. For a successful re-route
  the cursor is 0, so behaviour there is unchanged; for the fallback the guard now covers a
  path that previously had none.

This restores the invariant as stated: every handoff re-route is gated, on every path.

### 4. Two distinct closure reasons  *(D4)*

A new `_landingExitRouteUnreachable` flag, reset alongside `_landingExitVacatedEarly` at
the same sites (`LoadRoute`'s fresh-route reset and `StopGuidance`).

The guard chooses between them by what it can actually prove:

- `_landingExitVacatedEarlyPlannedName != null` — an early vacate genuinely preceded this
  refusal, so *"left the runway short of X"* is true. Set `_landingExitVacatedEarly`.
- Otherwise — the aircraft is off the runway but nothing established *where* along the
  runway. Set `_landingExitRouteUnreachable`, whose closure makes no positional claim:

  > "Exit guidance ended: no usable route from here. Stop and hold position, then open the
  > taxi planner to set a route to your gate."

`HandleArrival` gains the branch ahead of `_landingExitOffPavement`, whose "Off the runway
at X" wording implies a successful vacate and must not absorb this case.

### 5. Reuse cleanups

- `AbsLateralFromRunwayMeters` becomes `Math.Abs(SignedLateralFromRunwayMeters(...))`.
  The bodies are currently byte-identical apart from the `Math.Abs`, and they feed gates
  that must agree about the same aircraft position.
- The 1400 ft early-vacate value becomes canonical as
  `RolloutExitGate.EarlyVacateMaxPassedFeet`, with `TaxiGraph.GetLandingExits`'s local
  `EXIT_COVERAGE_GAP_FT` initialising from it. This direction — rather than hoisting
  TaxiGraph's const and referencing it from the gate — keeps the pure module free of a
  dependency *on* the graph, which its class doc promises. Both sides get a comment naming
  the other. Drift becomes a compile-time impossibility, so the "keep the two in step"
  instruction can be deleted rather than restated.
- A new `RolloutExitGate.ExitRelativeBearingDeg(exitBearingTrue, runwayHeadingTrue)` owns
  the `ExitBearingTrue == 0.0` sentinel decode; both call sites
  (`TaxiGuidanceManager.Rollout.cs` and `TaxiGuidanceManager.cs`) use it.

  The three doc comments claiming the sentinel *"normalises into"* / *"lands inside"* the
  `ExitSideMinBearingDeg` band are **false** and get corrected: the formula they prescribe
  yields `NormalizeAngle(-runwayHeadingTrue)` for the sentinel — −20° on a 020° runway, 90°
  on a 270° runway — so `HasKnownExitSide` returns true and the direction test compares
  against a fabricated side. Only the callers' undocumented `!= 0.0` guard makes the
  degradation work today.

### 6. Doc accuracy

The `changelog.d/204-landing-exit-early-turn.fix.md` fragment currently promises a steady
centreline tone through the whole rollout. `SelectToneMode` deliberately returns `Silent`
for a same-side deviation at or above 2° within the 1,000 ft window, so the last stretch
before a known-side exit is intentionally quiet. The fragment is amended to match, and the
design doc's `SelectToneMode` table gains the toward-exit silent window that was added
after it was written.

The fragment is amended in place rather than replaced: it belongs to this same unreleased
PR, so no new fragment is created.

## Testing

Every rule above is pure and gets a failing test before its implementation.

| Test | Pins |
|---|---|
| `RolloutLateralClearanceTests` | The shared predicate: not clear at half-width + 9.5 m, clear beyond half-width + 10 m, and the boundary itself. |
| `HandoffRouteReachabilityTests` (extend) | A 4,000 ft width yields the same corridor as 300 ft; narrow and missing widths are unchanged. |
| `ExitRelativeBearingTests` | Sentinel → 0.0 on 020°, 270° and 344° runways; a real bearing → the normalised difference. |
| `TaxiMathUtilsTests` (extend) | `Abs == \|Signed\|` across a spread of positions and headings. |

Hoisting the 1400 ft constant makes divergence a compile error, so it needs no sync test.

**Not unit-testable.** The manager-level sequencing — conclude-on-failed-reroute, the
relocated guard, and the closure wording — is sim-facing. Per CLAUDE.md it gets a written
in-sim test plan in the PR, which the repo owner runs:

1. **Early vacate, matched substitute** — land, vacate at an unplanned exit well over
   1,000 ft short of the planned one. Expect the substitute announcement and guidance that
   follows the taxiway actually taken, never a route back toward the planned exit.
2. **Early vacate, no match** — vacate onto pavement with no mapped exit nearby. Expect the
   "left the runway short of X" closure and silence, not a re-route.
3. **Vacate at the planned exit with an offset first segment** — expect the new neutral
   closure, and specifically *not* "left the runway short of X".
4. **Normal exit** — confirm the 0.856 m trigger shift changed nothing perceptible: the
   handoff still fires at the same point in the turn and the tone behaves as before.

## Risks

- **The handoff trigger moves.** 0.856 m later, in the conservative direction. Scenario 4
  above is the check.
- **The relocated guard covers more paths.** It can now refuse a fallback route it
  previously ignored, which ends guidance where guidance used to continue. That is the
  intended behaviour — the alternative is the tone pointed at pavement the aircraft is not
  on — but it makes scenarios 2 and 3 the ones to watch.
- **Constant hoisting touches `TaxiGraph.GetLandingExits`**, which is exercised on every
  route build. The change is a const initialiser with no behavioural component; the existing
  suite covers the surrounding logic.
