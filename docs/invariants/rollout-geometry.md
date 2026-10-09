# Rollout geometry: the implicit-exit override and the tolerance tripwire — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/rollout-geometry.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## ROL-11

- Both override guards (apron forward-direction AND `apronAngle > currentAngleFwd`) are required together in the implicit-exit shallow-angle override — dropping either regresses the exit bearing. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from landing-rollout.md (split for the rule budget).

## ROL-19

- **THE DERIVED-CONSTANT TRIPWIRE — re-derive all five (`RolloutExitGate.VacatedShortAlongTrackFeet`, `EarlyVacateMaxPassedFeet`, `HandoffReachDefaultHalfWidthM`, `RunwayClearMarginM` and `DefaultRunwayWidthFeet`, the rows of the tripwire table in docs/taxi-guidance.md) before changing any tolerance in the runway/rollout area, and say so in the commit message.** `RolloutExitGate.VacatedShortAlongTrackFeet` (350), `EarlyVacateMaxPassedFeet` (1400) and the 25 m corridor clamp (`HandoffReachDefaultHalfWidthM`) are all arithmetic consequences of the exact **5 m** gap between the exit-node corridor (`halfWidth + HandoffReachMarginM`, 15 m) and the pavement boundary (`halfWidth + RunwayClearMarginM`, 10 m) — `halfWidth` cancels; `RunwayClearMarginM` (10) is the codebase's ONE definition of "off the runway"; `DefaultRunwayWidthFeet` (200) is a fallback half-width DIFFERENT from `RunwayShape.DefaultHalfWidthMeters` (75 ft). Nothing in the code, the compiler or the tests links them, there is no compile error when they stop being derived, and the boundary tests keep passing because they pin the OLD arithmetic. `RunwayVacateResolver` keeps its own 75 ft copy and its own `SameRunwayLateralM` (30.0), calibrated against the residual scatter `TaxiGraph.SnapStartToRunwayCenterline` leaves — so loosening the snap invalidates that too. Since 2026-09 the same constants also drive the landing-exit branch measurement and the off-pavement alert: `RunwayClearMarginM` is `ExitBranch`'s clear line (every measured branch ends there, so it decides every measured exit's angle), `PavementMap` reads `RunwayClearMarginM` for a runway (its taxiway reach is `PavementTolerance.ForWidthFeet`, the off-route detector's own), and `TurnWindowFeetFor` falls back to `DefaultRunwayWidthFeet` and is floored at `TurnNowFeet` (150 ft); `RunwayAxis.CorridorMarginMetres` IS `HandoffReachMarginM` (linked 2026-09-26, no longer a copy). A change there also moves exit angles, the off-pavement line and the turn window: re-run `tools/LandingExitSweep` too. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: "all five" now names its five, the rows of the tripwire table in docs/taxi-guidance.md, in both this text and the one-line form. Evidence: the five constants in `RolloutExitGate.cs`.

Moved 2026-10-10 from landing-rollout.md (split for the rule budget).
