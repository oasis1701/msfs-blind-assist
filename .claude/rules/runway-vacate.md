---
paths:
  - "MSFSBlindAssist/Navigation/RunwayVacateResolver.cs"
  - "MSFSBlindAssist/Navigation/LandingExitDestination.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayVacateResolver*.cs"
---
# Vacate stop after a landing exit rules

Loaded when Claude reads matching code; the rollout's own rules are in landing-rollout.md. Background: docs/taxi-guidance.md, docs/virtual-pilot.md. Full text of each rule: docs/invariants/runway-vacate.md.

- [VAC-1] The vacate stop goes past the LANDED runway's own hold line where the exit curves (`RunwayVacateResolver.ExtendPastOwnHoldAhead`); keep every measured gate, and drop it for an exit whose path turns back. Full: docs/invariants/runway-vacate.md#vac-1
- [VAC-2] The vacate walk never steps onto or past a hold line guarding ANOTHER runway, but only when name AND geometry agree (`HoldGuardsAnotherRunway`); a result that cannot clear the pavement degrades to the legacy answer. Full: docs/invariants/runway-vacate.md#vac-2
