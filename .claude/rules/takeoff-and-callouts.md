---
paths:
  - "MSFSBlindAssist/Services/TakeoffAssistManager.cs"
  - "MSFSBlindAssist/Services/GroundSpeedAnnouncer.cs"
  - "MSFSBlindAssist/Services/AltitudeCalloutAnnouncer.cs"
  - "MSFSBlindAssist/Aircraft/TakeoffVSpeedCallouts.cs"
  - "MSFSBlindAssist/Aircraft/TakeoffCalloutKeys.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Takeoff*.cs"
---
# Takeoff assist and flight callouts rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/takeoff-and-callouts.md.

- [TKO-1] Takeoff Assist's `Toggle(off)` must unconditionally clear the runway reference, or a turnaround flight silently reuses the previous flight's runway; the teleport dialog path still sets it unconditionally so teleport always wins. Full: docs/invariants/takeoff-and-callouts.md#tko-1
- [TKO-2] Auto-activate-Takeoff-Assist-on-lineup is a one-shot latch (`_autoActivateFired`) that must NOT reset on lineup drift-out; re-engaging after a deliberate manual deactivation would surprise the pilot. Full: docs/invariants/takeoff-and-callouts.md#tko-2
- [TKO-3] `GroundSpeedAnnouncer` is mode-independent (taxi, takeoff roll, landing rollout); never move it back into a per-mode manager like `TaxiGuidanceManager`, where it stopped the moment takeoff assist or touchdown took over. Full: docs/invariants/takeoff-and-callouts.md#tko-3
- [TKO-4] `AltitudeCalloutAnnouncer` fires AT the 1,000-ft boundary and names the thousand CROSSED; re-crossing the last-announced thousand is ALWAYS silent with no distance window, and the ground/teleport reset must clear the announced-thousand latch too. Full: docs/invariants/takeoff-and-callouts.md#tko-4
- [TKO-5] Take-off calls are ONE `AnnounceImmediate` per sample via `TakeoffVSpeedCallouts.Compose`, never one per call; every definition drops the ARM, never the speeds, on reconnect AND `OnSimContextReset`, and names keys through the one `TakeoffCalloutKeys` (more: see full). Full: docs/invariants/takeoff-and-callouts.md#tko-5
