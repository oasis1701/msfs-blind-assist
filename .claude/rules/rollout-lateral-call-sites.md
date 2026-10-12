---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs"
---
# Rollout lateral-test rules for TaxiGuidanceManager.MathUtils.cs

MIRRORS: copied word for word from landing-rollout.md, whose globs leave this partial out: it declares `IsWithinRolloutRunwayLaterally`, the pan floor's lateral release and the base of the `exitedLaterally` trigger. Change both together (ClaudeContextBudgetTests checks).

- [ROL-10] The post-high-speed-exit `ExitBearingTrue` pan floor has exactly TWO releases: the opposite-sign test on the pavement and lateral clearance (`IsWithinRolloutRunwayLaterally`); never magnitude-vs-floor, never the unconditional `Math.Max/Min` clamp. Full: docs/invariants/landing-rollout.md#rol-10
- [ROL-12] The `exitedLaterally` handoff trigger must use the combined gate (lateral + dist/hdgDelta/pastExit), never bare lateral distance, which fires before the tone's exit-bearing phase engages. Full: docs/invariants/landing-rollout.md#rol-12
