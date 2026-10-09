---
paths:
  - "MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs"
  - "MSFSBlindAssist/Navigation/RunwayRouteClassifier.cs"
  - "MSFSBlindAssist/Navigation/RunwayShape*.cs"
  - "MSFSBlindAssist/Navigation/RunwayPavement.cs"
  - "MSFSBlindAssist/Navigation/*Hold*.cs"
  - "MSFSBlindAssist/Navigation/Progressive*.cs"
  - "MSFSBlindAssist/Services/RunwayIncursionWatch.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Hold*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Incursion*.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager*.cs"
  - "MSFSBlindAssist/Database/Models/TaxiRoute.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayShape*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayPavement*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayMembership*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayRowShapes*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayCenterlinePairing*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayReach*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DestinationStripCrossing*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayEventDescription*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiMathUtils*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayRouteClassifier*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteRunwayCrossings*.cs"
---
# Runway hold-shorts, crossings and runway shape rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/runway-holds.md.

- [HLD-1] Hold-short-to-runway association must be by nearest runway CENTERLINE, never threshold distance, which mislabels crossings far from either threshold with the taxiway name instead of the runway. Full: docs/invariants/runway-holds.md#hld-1
- [HLD-2] A user "end of taxiway" hold-short label must never be touched or overwritten by the crossing-label self-heal logic. Full: docs/invariants/runway-holds.md#hld-2
- [HLD-3] Route-less incursion warnings are gated by `RunwayIncursionWatch` on HAVING THE GRAPH, never a hand-listed state; "approaching" comes from own motion and the map (`IsApproaching`, `IsApproachingAlongAPath`, `IsApproachingWhileTurning`), never proximity alone or the route/off-route verdict (more: see full). Full: docs/invariants/runway-holds.md#hld-3
- [HLD-4] Never disable auto-inserted runway-crossing hold-shorts: `ApplyAutoHoldShortPasses` runs only from `AdoptRoute`, the one place a route is adopted; never place a stop on any runway's pavement, and a `StartGuidance` caller that stays in `Taxiing` or `HoldShort` must speak `ConsumeStartHoldCue()` (more: see full). Full: docs/invariants/runway-holds.md#hld-4
- [HLD-5] `ApplyUserRunwayHoldShorts` must bind a pick to the FIRST run of the named taxiway and judge it against the resolved, reciprocal-aware `RunwayCenterline`; an honoured pick records its own `TaxiRouteRunwayEvent` (merged by `AdoptRoute`), and never widen the destination-strip skip. Full: docs/invariants/runway-holds.md#hld-5
- [HLD-6] Measured, deliberately left alone: never widen the NARROW runway lateral band (no headroom without claiming the adjacent taxiway), and `RouteRunwayCrossings.RouteProgressMeters` returning 0.0 for both "at route start" and "not near this route" is not a bug (more: see full). Full: docs/invariants/runway-holds.md#hld-6
- [HLD-7] Read runway geometry through the ONE `RunwayShape.For` (classifier, holds, Where-Am-I, takeoff, vacate, reach walk), bar `MatchHoldShortRunwayName` (start rows) and four callers with their own geometry; never read `Pavement*` directly or test membership on `Lat1..Lon2` alone; repair an outboard start ROW, never cap the extent (more: see full). Full: docs/invariants/runway-holds.md#hld-7
- [HLD-8] Never tune `NamedHoldingPointResolver`'s snap radii: don't widen `DESIGNATED_SNAP_M` (15 m) toward `MAX_SNAP_M` (30 m), don't require a designated node for runway/ILS kinds, and don't add a "never snap runway-ward" guard; all three were probed and are worse. Full: docs/invariants/runway-holds.md#hld-8
- [HLD-9] `CheckRunwayIncursion` builds each hold node's paths (`CollectPathsIntoNode`: taxiway side only, never a chain onto runway pavement) and `GuardedRunway` once per graph, never per frame, and a new graph resets node memory; only the node spoken about is latched, so a withheld node (logged once) is judged again every frame. Full: docs/invariants/runway-holds.md#hld-9
- [HLD-10] Whether a route STARTS HELD is the pass's call from the aircraft (`AircraftPosition`: not within `RunwayWithinClearMargin` of any runway, not >10 m along) plus `MayStartHeld`, false only from `TryRecalculateRoute` (`recalculation: true`); never a ground-speed gate, an `allowStartHold` flag or the `landingRolloutRoute` log label. Full: docs/invariants/runway-holds.md#hld-10
- [HLD-11] A stop guarding several runways takes one Continue per runway (`RunwayHoldStages`, `ContinuePastHoldShort`): a press with a runway outstanding answers `ComposeAdvance` WITHOUT moving (state stays `HoldShort`, tone paused); never stage the destination hold (`_holdShortAtDestination`), whose Continue is a lineup clearance. Full: docs/invariants/runway-holds.md#hld-11

Mirrored from landing-rollout.md (it governs the `RunwayShape` tolerances; change it there and here together):
- [ROL-19] Derived-constant tripwire: re-derive all five (`VacatedShortAlongTrackFeet`, `EarlyVacateMaxPassedFeet`, `HandoffReachDefaultHalfWidthM`, `RunwayClearMarginM`, `DefaultRunwayWidthFeet`; three hang on `HandoffReachMarginM`) before any runway/rollout tolerance change, say so in the commit, re-run `tools/LandingExitSweep` (more: see full). Full: docs/invariants/landing-rollout.md#rol-19

Mirrored from ground-traffic.md and surroundings.md (they govern the held-runway label and the runway probe in the TaxiGuidanceManager partials; change them there and here together):
- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
- [SUR-24] The runway probe (`TaxiGuidanceManager.IsOnRunwayPavement`) answers only from geometry in hand and NEVER builds a graph; its warm-up reads runway rows only (`PrepareRunwayShapeWarmUp`, never `GetTaxiPaths`); `_graphGeneration` is stamped only when a NEW `_graph` instance is installed, never when a re-route hands the same graph back. Full: docs/invariants/surroundings.md#sur-24
