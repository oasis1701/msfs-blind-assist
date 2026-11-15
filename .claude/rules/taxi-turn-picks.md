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
