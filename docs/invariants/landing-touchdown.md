# Landing assist, touchdown and go-around — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/landing-touchdown.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## ROL-1

- The manual landing assist hands its rollout tone to taxi guidance SILENTLY on the frame taxi guidance leaves a landing rollout for anything but a route reload or a stop (`LandingFlareAssistManager.StepTaxiHandover`, fed from `StateChanged` and yielded after the taxi position update) — never by widening `IsLandingExitTaxiSteering` to count backtracking, which ended the two-tone overlap but let the assist's interrupting "Rollout guidance complete" cut "End of runway … Turn around" off within a frame. A Taxi Stop is not a takeover (the assist is independent of taxi guidance), and the assist's own speed or turn end is QUEUED whenever taxi guidance ran during the rollout, so it never clips a countdown, "Runway vacated…" or closure sentence. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from landing-rollout.md (split for the rule budget).

## ROL-7

- A go-around or touch-and-go after touchdown ENDS landing-exit guidance and keeps the plan (`Services/LandingExitGoAround`): while KNOWN airborne the rollout is held (`HoldsRollout`, unknown counts as the ground, never the other way); MainForm arms a one-shot check on the liftoff edge while landing-exit guidance runs (`Arms`: `LandingRollout` or landing-exit `Taxiing`), a touchdown stops it as a bounce, and after `ConfirmMs` (5 s) a FRESH position read decides (`Ends`), never the 1 Hz cache — the liftoff handoff's pattern, token-guarded against a response landing after a touchdown, disconnect or aircraft switch. It stops guidance as `StopGuidance` does, re-arms the planner (`RearmAfterGoAround`) and speaks ONE sentence ("Exit guidance off, plan kept."). Before it, nothing ended the rollout at liftoff: exit callouts spoke into the climb-out and the next approach had no exit guidance. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from landing-rollout.md (split for the rule budget).

## ROL-32

- **"Flare" is the one word the manual landing assist adds to its tones, and it belongs to the aircraft.** The assist engages at 50 ft gear height ("Flare guidance, runway 28R."), but the moment a pilot starts the flare is lower and differs by type: A320 FCTM 30 ft, Boeing 737 ~20 ft, the PMDG 777's autoland 40 ft, a light single 10-20 ft (FAA Airplane Flying Handbook). So the call fires at `VisualGuidanceProfile.FlareTriggerWheelHeightFt`, the height visual guidance already flares at, 0.3 s early at the current sink rate (`LandingGuidanceLaws.FlareCalloutDue`, `FLARE_CALLOUT_LEAD_SEC`) so the word is heard at the height, not a screen reader's latency below it. It interrupts, once per engagement: a bounce back into the flare does not repeat it, and under a silent flare visual guidance speaks its own "Flare" at the same height. The flare's sink-rate law (`FlareSinkTauSec`, `FlareTouchdownSinkFpm`, `FlareHoldsEntrySink`) and the rollout tone's end speed (`RolloutGuidanceEndKnots`) come from the same profile: as constants they were a jet's (6 s, 120 fpm, 40 kt), which told a DA40 on a normal 380 fpm approach that it was floating from 50 ft down and ended its rollout tone a few seconds after a 55 kt touchdown. The jets' defaults are unchanged (added 2026-10-12, PR #242). → [taxi-guidance.md](../taxi-guidance.md)
