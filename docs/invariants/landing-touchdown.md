# Landing assist, touchdown and go-around — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/landing-touchdown.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## ROL-1

- The manual landing assist hands its rollout tone to taxi guidance SILENTLY on the frame taxi guidance leaves a landing rollout for anything but a route reload or a stop (`LandingFlareAssistManager.StepTaxiHandover`, fed from `StateChanged` and yielded after the taxi position update) — never by widening `IsLandingExitTaxiSteering` to count backtracking, which ended the two-tone overlap but let the assist's interrupting "Rollout guidance complete" cut "End of runway … Turn around" off within a frame. A Taxi Stop is not a takeover (the assist is independent of taxi guidance), and the assist's own speed or turn end is QUEUED whenever taxi guidance ran during the rollout, so it never clips a countdown, "Runway vacated…" or closure sentence. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from landing-rollout.md (split for the rule budget).

## ROL-7

- A go-around or touch-and-go after touchdown ENDS landing-exit guidance and keeps the plan (`Services/LandingExitGoAround`): while KNOWN airborne the rollout is held (`HoldsRollout`, unknown counts as the ground, never the other way); MainForm arms a one-shot check on the liftoff edge while landing-exit guidance runs (`Arms`: `LandingRollout` or landing-exit `Taxiing`), a touchdown stops it as a bounce, and after `ConfirmMs` (5 s) a FRESH position read decides (`Ends`), never the 1 Hz cache — the liftoff handoff's pattern, token-guarded against a response landing after a touchdown, disconnect or aircraft switch. It stops guidance as `StopGuidance` does, re-arms the planner (`RearmAfterGoAround`) and speaks ONE sentence ("Exit guidance off, plan kept."). Before it, nothing ended the rollout at liftoff: exit callouts spoke into the climb-out and the next approach had no exit guidance. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from landing-rollout.md (split for the rule budget).
