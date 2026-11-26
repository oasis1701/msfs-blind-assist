---
paths:
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/RunwayReachGate.cs"
---
# Entry-stub rule for the router and the reach gate

MIRRORS: copied word for word from taxi-steering.md, whose globs leave these files out: `RunwayReachGate.cs` holds the rule's 100 m figure and `TaxiRouter.cs` its second reader (`DestinationRunways`). Change the rule there, in steering-routing-call-sites.md and here together; ClaudeContextBudgetTests fails if they differ.

- [STR-20] An entry stub ending on the destination centreline up to `ENTRY_STUB_BEYOND_END_M` (100 m, `RunwayReachGate.StubBeyondEndMeters`) past the recorded end still reaches the runway (`RunwaysOnEntryStub` fallback; `TaxiRouter.DestinationRunways` reads a destination node the same way); never widen either lateral bound past the runway's half-width. Full: docs/invariants/taxi-steering.md#str-20
