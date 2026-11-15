---
paths:
  - "MSFSBlindAssist/Navigation/TurnShape.cs"
  - "MSFSBlindAssist/Navigation/OffChartLeg.cs"
  - "MSFSBlindAssist/Navigation/TurnPick*.cs"
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TurnShape*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OffChartLeg*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TurnPick*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*KbosHcJunction*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiRouterBlockedNodes*.cs"
---

# Taxi Turn picks and the off-chart leg rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-turn-picks.md.

- [TURN-1] Turn picks are applied AFTER the default route (`TurnPickApplier.Apply`) and never change it when absent: never thread a pick into `FindConstrainedPathCore`, and `TaxiRouter.BlockedNodes` stays empty unless a pick is applied or was made. Full: docs/invariants/taxi-turn-picks.md#turn-1
- [TURN-2] Every gate of `OffChartLeg.Judge` is load-bearing (150 m, the turn limits, the mapped-pavement gate at every sample, no runway the leg does not start on): the pavement gate alone refuses the KBOS diagonal across the corner, the runway gate alone keeps a leg from re-entering 04R. Full: docs/invariants/taxi-turn-picks.md#turn-2
- [TURN-3] A recalculation (`TryRecalculateRoute`) never re-judges a Turn pick from the aircraft: one ahead keeps the loaded turn and tail (`TurnPickCarryOver.TailFrom`) or is said; one in the turn is missed; neither is a no-op; a made junction stays blocked (`BlocksToKeep` drops none); blocks change only on adoption. Full: docs/invariants/taxi-turn-picks.md#turn-3
- [TURN-4] A pick is honored only by a route that ITSELF makes the turn (`BuiltRouteMakesTheTurn`, along the row's own taxiway): never one that doubles back within 80 m, uses a node twice (`VisitsANodeTwice`) or passes its junction again (`ComesBackPastTheJunction`). VirtualPilot flew each as a loop ending the charted way. Full: docs/invariants/taxi-turn-picks.md#turn-4
- [TURN-5] A blocked node never switches gap chaining off for its whole taxiway (`AStarSearchWithGapChaining`): a navdata gap elsewhere on it is still chained, and only a hop that would step over the block is refused (`HopStepsOverABlock`). Refusing every gap refused the KBOS left turn onto C outright. Full: docs/invariants/taxi-turn-picks.md#turn-5
- [TURN-6] The rest of a picked route is searched with more than the junction closed: nodes already taxied, the charted way it declined, and the taxiway's other arm (`OtherArm`; the owner's ruling, the destination stays open). Closed at load only, for later picks too, never in the pick's own blocked nodes: a recalculation keeps only the junction. Full: docs/invariants/taxi-turn-picks.md#turn-6
- [TURN-7] A pick never ADDS a stretch along a runway (`LongestRunAlongARunwayMeters`: over 100 m on one runway and 50 m more than the charted route has): the hold passes have no good stop for a route driven along a runway, and OMDB "left onto N" went onto 12L/30R unheld and unwarned. Full: docs/invariants/taxi-turn-picks.md#turn-7
