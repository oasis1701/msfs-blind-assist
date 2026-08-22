# Landing-exit rollout: early-turn handoff, drift tone, and vacate retargeting

Date: 2026-08-21
Status: approved, ready for implementation
Origin: KSEA ILS 34L arrival, 2026-08-21 19:37–19:41 UTC (build `f385b688`)

## The defect

On a 34L landing with taxiway Z selected in the Landing Exit Planner, the aircraft
decelerated normally and drifted 15.1° **left** of the runway heading at 19.7 kt,
2,232 ft short of Z. Every mapped exit on 34L is to the **right**.

`turnBegun` tests `hdgDeltaAbs >= 15°` — an absolute value, with no reference to where
the exit is or how far away it lies — so the leftward drift read as "the pilot has begun
the exit turn" and handed off to Taxiing.

The handoff then re-routed from the live position to the planned exit's node. The route
start snapped to the nearest graph node, on taxiway J's diagonal 54 m to the right and
17.8 m **outside** the runway's east edge. The steering tone, silent until that instant
(it only arms within 300 ft of the exit), resumed and panned hard right at 79°,
saturating at 91.7° within three seconds. The pilot followed it across roughly 60 m of
unmapped ground — 28.3 m past the runway edge with 38.6 m still to run to taxiway J's
edge — before reaching J's pavement.

Because the destination stayed pinned to Z's off-runway node, and the taxi graph carries
no runway edges, A\* produced the only route that exists between J and Z: up the
east-side parallel taxiway T and back down Z toward the runway. 1,678 m, of which
~1,400 m was still showing when the aircraft stopped.

### Three independent faults

1. `turnBegun` is direction-blind and distance-blind.
2. The rollout steering tone is silent outside 300 ft of the exit, so a drift toward the
   runway edge has no cue at all — and the tone's first utterance was a hard pan.
3. The handoff re-route always targets the planned exit, even when the aircraft
   demonstrably left the runway somewhere else.

A fourth, latent: nothing checks that the tone's target is a surface the aircraft can
actually reach.

## Design

### New pure module: `MSFSBlindAssist/Navigation/RolloutExitGate.cs`

All logic lands as pure static functions with no SimConnect, form or graph dependency,
following the established pattern of `LandingExitDestination` and `RunwayVacateResolver`,
so the xUnit suite can pin every rule.

#### `RolloutToneMode SelectToneMode(double groundSpeedKts, double distToExitFeet)`

| Condition | Mode |
|---|---|
| `gs > ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS` (50) | `Silent` |
| `distToExitFeet <= ROLLOUT_EXIT_TONE_ARM_FT` (300) | `ExitBearing` |
| otherwise | `DriftCorrection` |

`Silent` and `ExitBearing` reproduce today's behaviour exactly. `DriftCorrection` is new
and fills the gap that was silent.

#### `bool IsExitTurnBegun(hdgDeltaSignedDeg, groundSpeedKts, distToExitFeet, pastExit, exitRelBearingDeg)`

All of:

- `Math.Abs(hdgDeltaSignedDeg) >= ROLLOUT_TURN_BEGAN_HDG_DEG` (15)
- `groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS` (90)
- `distToExitFeet <= ROLLOUT_TURN_WINDOW_FT` (1000) **or** `pastExit`
- direction agrees: if `Math.Abs(exitRelBearingDeg) < 3.0` the exit side is unknown and
  no direction constraint applies; otherwise `Math.Sign(hdgDeltaSignedDeg)` must equal
  `Math.Sign(exitRelBearingDeg)`

`exitRelBearingDeg` is `NormalizeAngle(exit.ExitBearingTrue - runwayHeadingTrue)`, and
`ExitBearingTrue == 0.0` is the existing "unknown" sentinel, which normalises to a
relative bearing under 3° and therefore disables the direction test — the intended
degradation.

The 3° floor matches `alignedWithExit`'s existing `ExitAngleDegrees >= 3.0` gate: below
that an exit is geometrically indistinguishable from straight ahead and has no side.

#### `LandingExit? MatchEarlyVacateExit(allExits, plannedExit, signedAlongPastFeetByExit, aircraftLateralSignedMetres)`

Answers "which exit did the pilot actually turn onto?" when the handoff fires away from
the planned exit.

- Candidates are exits whose `ExitSide` matches the sign of the aircraft's lateral
  displacement from the runway axis. An exit with `ExitSide == ""` is not excluded — it
  is ranked on distance alone.
- A candidate must satisfy `signedAlongPast >= -EARLY_VACATE_FORWARD_SLACK_FT` (−600):
  at or past the exit, with slack, because a hold-short-marker exit node can read forward
  of the pavement junction the pilot actually turned at (see "Derivations").
- Among candidates, pick the **smallest positive** `signedAlongPast` — the last exit
  actually passed. You cannot vacate at an exit you have not reached.
- Reject if that value exceeds `EXIT_COVERAGE_GAP_FT` (1400) or if the winner is the
  planned exit.
- Returns `null` when nothing qualifies; the caller then concludes guidance.

**`signedAlongPast` is measured per exit**, via the existing
`SignedAlongRunwayMeters(lat, lon, exit.Latitude, exit.Longitude, runwayHeadingTrue)`.
This is deliberate and load-bearing: it needs no threshold reference, so it is immune to
displaced thresholds. `LandingExit.DistanceFromThresholdFeet` is measured by
`GetLandingExits` from the **landing** threshold including `ThresholdOffset` (KJFK 13R
2,055 ft, KJFK 22R 3,438 ft, EGLL 27R 1,004 ft), and the natural way to compute an
aircraft's along-runway position measures from the physical runway start. Comparing the
two silently picks the wrong exit at every displaced-threshold runway. Do not reintroduce
a comparison against `DistanceFromThresholdFeet` here.

#### `bool IsHandoffRouteReachable(bool aircraftOffRunway, double crossTrackToFirstSegmentM, double firstSegmentPathWidthFeet)`

- `!aircraftOffRunway` → `true`. A handoff taken while still on the runway is the normal
  case and is never refused.
- Otherwise `crossTrackToFirstSegmentM <= halfWidthM + HANDOFF_REACH_MARGIN_M` (15).
- `firstSegmentPathWidthFeet <= 0` → `halfWidthM = 25.0`, a deliberately generous
  fallback. The guard *ends* guidance, so missing navdata width must never cause a false
  refusal.

`aircraftOffRunway` comes from the existing `IsWithinRolloutRunwayLaterally`.

This tests proximity to the target taxiway, **not** the presence of pavement. Navdata
carries only runway and `taxi_path` polygons and cannot prove there is asphalt underfoot.
What the guard does guarantee is that the steering tone is never pointed at a taxiway the
aircraft is not essentially already on.

### Wiring

`MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`:

- **Tone block (~806–892).** The two-branch speed/distance gate is replaced by
  `SelectToneMode`. `DriftCorrection` uses `desiredHeading = _rolloutRunwayHeadingTrue`
  and calls the existing `UpdateHeadingErrorWithThresholds` at 2.0 / 3.0 / 15.0°. The
  heading-error smoother resets on any mode change, extending the existing
  `_rolloutExitToneArmed` reset pattern so a `DriftCorrection → ExitBearing` transition
  starts from a clean filter.
- **`turnBegun` (~314)** delegates to `IsExitTurnBegun`, passing the **signed** heading
  delta (today only the absolute value is retained) and the exit's relative bearing.
- **Handoff block (~439–539)** gains two steps ahead of the existing re-route:
  1. **Early-vacate retarget**, entered only when *both* hold:
     - the aircraft is laterally off the runway (`!IsWithinRolloutRunwayLaterally`), and
     - `distToExitFeet > ROLLOUT_TURN_WINDOW_FT && !pastExit`.

     Both gates are load-bearing. The lateral gate is what "vacated" physically means: a
     `trulyStopped` handoff on the centreline 2,000 ft short of the exit is a pilot who
     braked early, not one who turned off, and must keep the planned exit so they can
     taxi to it. The distance gate reuses the **same 1,000 ft window** as
     `IsExitTurnBegun` — using `ROLLOUT_NEAR_EXIT_FT` (500) instead would classify a
     legitimate turn begun 800 ft out as an early vacate.

     When entered, call `MatchEarlyVacateExit`. A match replaces `_rolloutExit`, so the
     destination, the post-handoff overshoot monitor and the arrival callout all name the
     taxiway the pilot is actually on. No match concludes guidance.
  2. After `LoadRoute` succeeds, evaluate `IsHandoffRouteReachable` against segment 0.
     Unreachable concludes guidance.
- **New arrival wording.** A third flag beside `_landingExitMissed`, rendered in
  `HandleArrival`'s `_isLandingExitRoute` branch:
  > "You have left the runway short of {exitName}. Exit guidance ended. Stop and hold
  > position, then open the taxi planner to set a route to your gate."

  It must not reuse the `_landingExitMissed` wording ("You have passed the … vacate
  point"), which describes the opposite failure.

`MSFSBlindAssist/Services/TaxiGuidanceManager.cs`:

- **`turnBegunPH` (~1637)**, the post-handoff overshoot monitor, gains the **direction**
  test only — not the proximity test, which would be wrong for a check that runs when the
  aircraft is already near or past the exit. A wrong-way turn clearing the overshoot
  monitor is the same hole one level down.

### Constants

| Constant | Value | Source |
|---|---|---|
| `ROLLOUT_TURN_WINDOW_FT` | 1000.0 | derived below |
| `ROLLOUT_DRIFT_TONE_SILENT_DEG` | 2.0 | existing `alignedWithExit` floor |
| `ROLLOUT_DRIFT_TONE_ACTIVATION_DEG` | 3.0 | one degree above the floor |
| `ROLLOUT_DRIFT_TONE_MAX_PAN_DEG` | 15.0 | matches every other tone in the file |
| `EARLY_VACATE_FORWARD_SLACK_FT` | 600.0 | derived below |
| `HANDOFF_REACH_MARGIN_M` | 15.0 | reuses `lateralToleranceM`'s buffer |
| `EXIT_COVERAGE_GAP_FT` | 1400.0 | **existing**, reused, not duplicated |

### Derivations

Every threshold traces to geometry or to a constant already in this codebase. None is
fitted to the KSEA flight.

**`ROLLOUT_TURN_WINDOW_FT` = 1,000 ft.** An exit node may sit forward of its actual
pavement junction by up to `lateralTolerance / tan(exitAngle)`, where `lateralTolerance`
is `halfWidth + 15 m` (`TaxiGraph.GetLandingExits`). `turnBegun` can only fire for an
exit the aircraft can deviate 15° onto, so `exitAngle >= 15°`. The worst case is a 200 ft
runway: `(30.5 + 15) / tan(15°)` = 170 m = **558 ft**. Add the app's own notion of "at the
exit" — the 300 ft tone-arm distance plus the 150 ft "turn now" cue — for 1,008 ft, rounded
to 1,000. A tighter 500 ft would block legitimate turns at shallow-RET airports whose
exits derive from hold-short nodes.

**`EARLY_VACATE_FORWARD_SLACK_FT` = 600 ft.** The same 558 ft node-displacement figure,
rounded, applied in the opposite direction so a hold-short-marker node reading forward of
the junction the pilot turned at still qualifies as "an exit already passed".

**`EXIT_COVERAGE_GAP_FT` = 1,400 ft** is reused rather than paralleled. Its existing
comment records it as measured across 266 runway directions at 39 airports as the
distance beyond which two nodes stop describing the same physical turnoff — exactly the
question `MatchEarlyVacateExit` is asking.

**Drift deadband 2° / 3°.** 2° is the codebase's existing floor for a meaningful heading
deviation (`Math.Max(2.0, exitAngle * 0.7)` in `alignedWithExit`). As a cross-check
against the incident data, the KSEA rollout ran at 0.4–1.7° throughout its normal phase
and the drift episode read 6.1° then 14.4°: the tone stays silent through normal rollout
and starts panning about 3.5 s before the old handoff fired.

## Behaviour across airports

| Case | Behaviour |
|---|---|
| Exits on both sides of the runway | The direction test uses the *planned* exit's side. A genuine opposite-side vacate is caught by `exitedLaterally`, which is position-based and side-agnostic, then matched by the aircraft's own displacement side. |
| `ExitBearingTrue == 0.0` | Direction test disabled; the proximity window still applies. |
| `ExitSide == ""` | Side filter skipped for that candidate; ranked on distance alone. |
| Displaced threshold | No threshold reference is used anywhere in the matcher. |
| `PathWidth == 0` | Generous 25 m half-width fallback; biased toward accepting. |
| No-graph rollout (`BeginLandingRolloutNoGraph`) | *Gains* the drift tone, which it never had. Handoff paths are unchanged: the re-route already cannot run without a graph. |
| Runway-end countdown, backtrack departure | Untouched. Both already own their own tones and their own state machines. |
| Very shallow RET (< 15°) | `turnBegun` never fired for these. `exitedLaterally` and `alignedWithExit` still own them, unchanged. |
| High-speed exit | `TryEarlyExitHandoff` is unchanged and still fires first, at ≤ 300 ft. |

## Testing

Four new xUnit files under `tests/MSFSBlindAssist.Tests`:

- `RolloutToneModeTests` — the three modes and their boundaries (50 kt, 300 ft).
- `RolloutExitTurnGateTests` — the 15°, 90 kt and 1,000 ft gates; direction agreement;
  the unknown-bearing and sub-3° degradations; `pastExit` bypassing the window. Carries
  the KSEA case (`hdgDelta −15.1°`, exit bearing `+13.6°`, 2,232 ft) as a named
  regression.
- `EarlyVacateExitMatcherTests` — last-exit-passed selection, the forward slack, the
  1,400 ft rejection, side filtering, both-sides runways, blank `ExitSide`, and the
  planned-exit rejection. Carries the KSEA case (J at +810 ft wins over E at −800 ft),
  and the "braked on the centreline" case, which must not match because the caller's
  lateral gate never lets it in.
- `HandoffRouteReachabilityTests` — the on-runway bypass, the half-width + margin
  boundary, and the `PathWidth == 0` fallback. Carries the KSEA case (53.9 m cross-track
  to an 82 ft segment → refused).

Sim-facing behaviour cannot be unit-tested; the PR carries an in-sim test plan for the
repository owner.

## Out of scope

`LandingExitDestination` and `RunwayVacateResolver` are untouched. The 1,678 m loop
disappears as a consequence of the handoff no longer targeting the planned exit after an
early vacate, not by changing how a destination is resolved.
