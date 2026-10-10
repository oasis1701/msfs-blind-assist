---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs"
  - "MSFSBlindAssist/Aircraft/A380*.cs"
  - "MSFSBlindAssist/Forms/FBWA380/**"
  - "tests/MSFSBlindAssist.Tests/**/*A380*.cs"
  - "MSFSBlindAssist/Services/TcasRaGuidance.cs"
  - "MSFSBlindAssist/Aircraft/WiperPosition.cs"
---
# FlyByWire A380X systems and panels rules

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/a380-systems.md.

- [A380-1] The LT-TEST knob is render-only: never narrate what the bulbs would show; announce only the knob's own position and let real per-system fault lights announce genuine faults. Full: docs/invariants/a380-systems.md#a380-1
- [A380-4] Never treat the Surveillance pedestal panel as working (FBW has not implemented it); transponder AUTO mode and squawk are the only real controls, via the MFD SURV page. Full: docs/invariants/a380-systems.md#a380-4
- [A380-5] Before declaring an announced ARINC var untestable by injection, check for a per-frame writer: only vars the writer leaves alone are injectable; writer-owned vars need a live scenario. Full: docs/invariants/a380-systems.md#a380-5
- [A380-7] Every A380 RMP calc-path write must be unique per call with a `{seq} 0 *` prefix (MobiFlight coalesces identical consecutive strings, dropping a repeated digit or a double LSK/ADK press), and a key tap is ONE call carrying press AND release, the opposite shape to [A320-14]'s spaced L:var pulse: never harmonise them. Full: docs/invariants/a380-systems.md#a380-7
- [A380-9] Seat-motor writes need a per-frame unique calc string (`<seq> 0 *` prefix); MobiFlight fires identical strings once, so the motor ticks once instead of running. Full: docs/invariants/a380-systems.md#a380-9
- [A380-11] Every FBW unit/feature with an observable effect must be wired into MSFSBA's own read-outs; MSFSBA bypasses the cockpit displays, so a display-only conversion never reaches the pilot. Full: docs/invariants/a380-systems.md#a380-11
- [A380-14] When FBW moves a subsystem, diff removed tokens per commit against var `Name`s from the BUILT assembly, never source text; also check constant writers, changed writers, live reads (clear only) and stock names. (more: see full) Full: docs/invariants/a380-systems.md#a380-14
- [A380-15] The FBW-drift intersection must cover input EVENTS too (`Type == SimVarType.Event` and every `H:` literal): a deleted event fails silently. Pinned by `FlyByWireA380EventContractTests`. Full: docs/invariants/a380-systems.md#a380-15
- [A380-20] Strip annunciators from panel DISPLAY variable sets; keep numeric/analog and 3+-state status fields. Full: docs/invariants/a380-systems.md#a380-20
