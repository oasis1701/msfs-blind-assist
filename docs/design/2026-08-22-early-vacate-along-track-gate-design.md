# Early-vacate gate: measure along-track, not straight-line

**Date:** 2026-08-22
**Follows:** [2026-08-22-landing-exit-review-fixes-design.md](2026-08-22-landing-exit-review-fixes-design.md) (PR #204)
**Status:** design approved, ready for implementation

## Why

A review of PR #204 raised two residual findings. This spec covers one of them and
records the decision on the other.

**Finding 6 (this spec).** The landing-rollout handoff runs its early-vacate retarget only
when `offRunwayAtHandoff && farFromPlannedExit`, where

```csharp
bool farFromPlannedExit = !pastExit
    && distToExitFeet > Navigation.RolloutExitGate.TurnWindowFeet;   // 1,000 ft, straight-line
```

So a pilot who vacates onto a **different** exit lying within 1,000 ft of the planned one —
common where exits sit ~800 ft apart on parallel-taxiway layouts — is treated as having
turned onto the planned exit. The handoff then re-routes to the **planned** exit. Because
`LoadRoute` snaps the route start to the node underfoot, the reachability guard passes, and
the route can run up a parallel taxiway and back toward the runway.

That is a direct instance of the behaviour CLAUDE.md forbids — *"After an early vacate the
handoff must never re-route to the PLANNED exit… Retarget or conclude"* — occurring inside a
window where the code does not recognise the vacate as a vacate. Neither existing safeguard
catches it: the reachability guard measures cross-track to the **first** segment only and
says nothing about the shape of the remaining route, and the post-handoff overshoot monitor
clears itself via `exitedLaterallyPH` on the next frame.

**Finding 5 (recorded, no change).** `HasKnownExitSide` rejects relative bearings near 0° but
has no equivalent band near ±180°, so for a turnaround/backtaxi exit the left-or-right answer
is effectively a coin flip. Investigation established the blast radius precisely: it can only
affect `ExitType == "End"` exits with `ExitAngleDegrees == 130.0`; every downstream harm is
caught by an existing gate (`speedNearExitHandoff`, the mutually-exclusive overshoot heading
gate, the early-vacate retarget, `IsHandoffRouteReachable`); and the residual is a delayed
handoff, a drift cue that goes quiet on one side, and occasionally guidance concluding earlier
than it needed to. **No wrong steering is produced.** The repo owner's decision on 2026-08-22
was to leave it alone. This paragraph is the record so it is not re-raised as new.

## The rule

Replace the straight-line distance gate with an **along-track** one, expressed as a pure
predicate on `RolloutExitGate`:

```csharp
public const double VacatedShortAlongTrackFeet = 350.0;

public static bool IsVacateAwayFromPlannedExit(
    bool pastPlannedExit,
    double signedAlongPastPlannedFeet,
    double distToPlannedExitFeet);
```

returning

```
!pastPlannedExit
&& ( distToPlannedExitFeet > TurnWindowFeet
     || -signedAlongPastPlannedFeet > VacatedShortAlongTrackFeet )
```

The caller keeps its `offRunwayAtHandoff &&` conjunct unchanged. That conjunct is not
incidental — it is what makes the 350 ft derivation valid, and it is independently what
protects the on-runway case discussed under "Why 1,000 ft stays" below.

Both inputs are already computed in `UpdateLandingRollout` before the handoff block:
`distToExitFeet` and `signedAlongPastFt`. Nothing new is measured.

### Why 350 ft — derived, not fitted

`TaxiGraph.GetLandingExits` refuses any candidate node whose lateral offset exceeds
`halfWidthM + 15.0`. `IsLaterallyClearOfRunway` puts the pavement boundary at
`halfWidthM + RunwayClearMarginM` (10.0). Both read the same `halfWidth` from the same
`Runway.Width`, so **the exit-node corridor extends exactly 5 m beyond the clear boundary.**

An aircraft that is laterally clear, whose own track leaves the axis at local angle θ, gains
lateral offset at `tan θ` per unit of along-track. Its own exit's node cannot be further ahead
than the point at which that node would leave the corridor:

```
alongTrackShortOfOwnNode  ≤  (halfW + 15 − (halfW + 10)) / tan θ  =  5 m / tan θ
```

θ here is the **aircraft's own track angle** away from the axis as it leaves — not an
exit-angle constant. `GetLandingExits` enforces no minimum exit angle for HS/IHS nodes, and
`ExitSideMinBearingDeg` (3°) is a side-*knowability* floor, not a geometric one; it appears
below only as the shallowest track worth tabulating.

| θ (aircraft's own track off the axis) | Max along-track you can be short of **your own** exit while off the pavement |
|---|---|
| 3° | 95.4 m = **313 ft** |
| 5° | 187 ft |
| 7° (the EDDB 24L M3 / LGAV D8 shallow-stub cases) | 134 ft |
| 15° (shallowest deviation `IsExitTurnBegun` can fire for) | 61 ft |
| 90° | 16 ft |

313 ft is the worst case across the whole practical angle range; 350 rounds it up, and that
derivation alone is what carries the threshold.

**No empirical spacing floor between distinct exits is claimed.** `TaxiGraph`'s coverage-gap
sweep (266 runway directions across 39 airports) measures the distance from a gap-fill
candidate to an exit *already in the list* — the far ends of RET arcs, i.e. the **same**
physical turnoff — so its figures say nothing about how far apart two *different* turnoffs
sit, and real spacing at large airports routinely exceeds 1,000 ft (which is exactly why the
straight-line clause is still needed). The one real datum on close-together distinct exits is
the closest same-name pair kept on the hold-short path, **EGLL 09R S4E at 433 ft**. Do not
raise 350 toward it on the strength of a floor that was never measured.

Degenerate case, checked: when `Runway.Width == 0` the two paths use *different* fallbacks —
75 ft half-width for the node corridor, 200 ft for the clear test — giving negative slack. A
laterally-clear aircraft is then already outside the node corridor entirely, so the bound
holds a fortiori.

### Why the 1,000 ft window stays untouched

`TurnWindowFeet`'s own derivation is `lateralTolerance / tan(exitAngle)` = 558 ft, computed
for an aircraft **on the centreline** — which is where `IsExitTurnBegun` fires. The
early-vacate branch fires only when the aircraft is **off the pavement**, which has already
consumed all but 5 m of that same corridor. Two gates, two different lateral states, two
different correct numbers.

This also disposes of the objection that a 500 ft tightening was already tried and rejected.
That rejection reasoned about *a turn begun on the runway 800 ft out* — and such a turn cannot
enter this branch at all, because `offRunwayAtHandoff` is false and the planned exit is kept.
The lateral conjunct protects that case independently of the distance number.

### Why the straight-line clause is kept, not dropped

`distToPlannedExitFeet > TurnWindowFeet` is **not** subsumed by the new clause. It is
subsumed only while lateral offset is small: at 1,000 ft straight-line with a modest lateral,
along-track short is ~988 ft, well past 350. But the branch places no upper bound on lateral
offset, so an aircraft that has driven a long way off the side can read a small along-track
distance while being nowhere near the exit. Keeping the clause covers that; dropping it
would be a real, if narrow, regression.

## What the pilot hears

| Situation | Today | After |
|---|---|---|
| Vacate 800 ft short onto a mapped exit | Silently routed to the exit you skipped, possibly the long way round | *"Left the runway short of taxiway Z. Now following taxiway J."* |
| Vacate 400 ft short onto pavement it can't identify | Silently routed to the planned exit | *"You have left the runway short of Taxiway X. Exit guidance ended. Stop and hold position, then open the taxi planner…"* |
| Normal turn onto the planned exit | Planned exit kept | **Unchanged** — once clear of the pavement the exit is ≤61 ft ahead at 15°, far inside 350 |
| Vacate >1,000 ft short | Retarget or conclude | **Unchanged** |

The second row is a real behaviour change and belongs in the PR's in-sim plan rather than
being discovered in the air. The repo owner chose it explicitly on 2026-08-22: ending
guidance and saying so beats a silent route to the skipped exit.

## Testing

The predicate is pure and gets characterization tests beside `EarlyVacateExitMatcherTests`:

| Test | Pins |
|---|---|
| Boundary both sides | 350 ft exactly is not a vacate; the next representable double above it is |
| Geometric realism | A 15° exit at 61 ft short is **not** a vacate; the same exit at 400 ft short **is** |
| Straight-line clause survives | `distToPlannedExitFeet > 1000` fires regardless of along-track |
| Past-exit short-circuit | `pastPlannedExit == true` returns false whatever the distances |
| Sign convention | A **positive** `signedAlongPastPlannedFeet` (past the exit) never reads as short |

Not unit-testable: the wiring in `UpdateLandingRollout` is sim-facing. It gets the in-sim
plan above, added to the PR body.

## Docs

`CLAUDE.md`'s taxi-guidance invariant list and `docs/taxi-guidance.md` both describe the
early-vacate branch's entry condition; both need the along-track gate.

The existing `changelog.d/204-landing-exit-early-turn.fix.md` needs **no change**. It already
promises that coming off at a different taxiway gets you either a retarget or a plain "it has
ended" instead of being quietly routed the long way round. Inside 1,000 ft that was not
actually true. This fix makes the existing sentence honest rather than adding a new claim.

## Risks

- **The branch widens, so the "no match → conclude" path widens with it.** That is the
  intended outcome per the invariant and the owner's decision, but it is the row of the table
  above most likely to surprise, and the reason scenario 2 below exists.
- **`MatchEarlyVacateExit` must not be run bare inside the window.** It ranks candidates by
  `Math.Abs(passed)` with no reference to the planned exit, so on a normal turn onto the
  planned exit it would return the *previous* exit (≈ +100 ft behind) and fire a wrong
  retarget on essentially every landing. The along-track gate is what keeps the matcher from
  ever being consulted in that case; the two must land together.
- **Not addressed here, recorded as a follow-up:** nothing rejects the long-way-round route
  *shape*, whichever exit is chosen — the reachability guard is first-segment-only by design.
  `TryRecalculateRoute` already carries a length-blow-up OR backwards-bearing gate that could
  be applied to the handoff route. Separate change, separate risk surface.

## In-sim test plan

1. **Vacate ~800 ft short onto a mapped neighbouring exit** (EGLL 09R S5W/N5E is a mapped
   ~900 ft pair). Expect the retarget announcement naming both taxiways, then guidance along
   the taxiway actually taken — never a route back toward the planned exit.
2. **Vacate ~400 ft short onto pavement carrying no mapped exit.** Expect the guidance-ended
   closure. This is the new behaviour; confirm it is not startling in the air.
3. **Normal turn onto the planned exit.** Expect no change at all — no retarget, no closure.
4. **KSEA 34L, the original case (>1,000 ft short).** Expect unchanged behaviour.
