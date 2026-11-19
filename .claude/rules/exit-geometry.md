---
paths:
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitCrossingSide*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitOwnBank*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitOwnPavementBank*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitStubSide*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitAxisNoisePick*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitTurnPoint*.cs"
---
# Landing exits: side, bank and turn-point geometry rules

Loaded when Claude reads TaxiGraph.cs, which builds the exit list. Background: docs/taxi-guidance.md, docs/virtual-pilot.md. Full text of each rule: docs/invariants/exit-geometry.md.

- [EXG-1] A crossing exit's name and `ExitBearingTrue` sit on the bank its own `ApronNodeId` is on (`TaxiGraph.ReconcileExitSideWithHandoff`); keep ALL its gates, taking the bank from the apron node's lateral OFFSET, never the bearing to it. Full: docs/invariants/exit-geometry.md#exg-1
- [EXG-2] An exit's corridor walk must not come back on ANOTHER exit's pavement (`TaxiGraph.RewalkCorridorOnExitBank`); all four gates are load-bearing, and it applies at all four handoff-node sites, never one alone. Full: docs/invariants/exit-geometry.md#exg-2
- [EXG-3] Where the edge bearing cannot name a bank, the exit's OWN NAMED pavement does (`RewalkOnOwnNamedPavement` + `FollowOwnNamedPavement`): clear the CORRIDOR, never just the pavement; re-run all three sweeps, occurrence-keyed, before touching a gate. Full: docs/invariants/exit-geometry.md#exg-3
- [EXG-4] `ExitSide` never comes from a bearing below `SIDE_MIN_LATERAL` (`TaxiGraph.ResolveExitSide`): use the apron node's lateral OFFSET, else leave it EMPTY, never guess; `SIDE_MIN_LATERAL` is ONE constant across the crossing test, re-pick, bank check and side resolver. Full: docs/invariants/exit-geometry.md#exg-4
- [EXG-5] A bearing above the noise floor can still be a 2-8 m stub's: `ResolveExitSide` overrules the label (`OwnNamedPavementBank`) only when the apron node is clear on the other bank AND the exit's own-name pavement reaches only that bank; never change `ExitBearingTrue` for it. Full: docs/invariants/exit-geometry.md#exg-5
- [EXG-6] `RewalkOnOwnNamedPavement` stands aside at a both-banks crossing ONLY when the exit's own bearing is ≥ `SIDE_MIN_LATERAL`; with no own-pavement node 10° off the axis, `FollowOwnNamedPavement` returns the first clear one (owner ruling). Full: docs/invariants/exit-geometry.md#exg-6
- [EXG-7] A taxiway drawn ALONG the centreline is not a turnoff where its name starts (`TaxiGraph.OnAxisDepartureShiftM`): a forward run ≥ 20 m becomes `TurnPointOffsetFeet` (Normal exits only), never a relocated exit; a backward run ≥ 150 m drops the candidate. Full: docs/invariants/exit-geometry.md#exg-7
- [EXG-8] A High-speed exit must agree with the route its handoff steers (`CorroborateHighSpeed`, after `RefineForPlanner`): move `ApronNodeId` onto the exit's own rapid-exit branch, else demote to Normal when the junction→apron route turns over 50° on a leg ≥ 5 m. Full: docs/invariants/exit-geometry.md#exg-8
