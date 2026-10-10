---
paths:
  - "MSFSBlindAssist/Navigation/RunwayPavement.cs"
---
# Stand-bridge rule for the runway pavement test

MIRRORS: copied word for word from taxi-graph.md, whose globs leave RunwayPavement.cs out: its `IsOnPavement` and `SegmentTouchesPavement` are the runway test `BridgeOrphanParkingIslands` refuses a bridge on. Change the rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [RTE-2] Bridge a stranded stand (`BridgeOrphanParkingIslands`) ONLY when its island is all navdata `P` lead-ins within 50 m: never on/across runway pavement, onto a hold-short, a stand or another lead-in chain, never an island carrying a taxiway; runway-exit logic skips `IsStandBridge` edges (more: see full). Full: docs/invariants/taxi-graph.md#rte-2
