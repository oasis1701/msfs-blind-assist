---
paths:
  - "MSFSBlindAssist/SimConnect/Arinc429Word.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWire*.cs"
  - "MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs"
  - "MSFSBlindAssist/Resources/coherent-oans-agent.js"
  - "tests/MSFSBlindAssist.Tests/**/*Arinc*.cs"
---
# FlyByWire ARINC 429 words rules

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/fbw-arinc.md.

- [ARINC-1] Read an FBW ARINC discrete bit from `Arinc429Word.DiscreteBits` (`BitValueOr`), NEVER the raw low 32 bits: the bitfield is the float's VALUE. `BitValue` (no SSM gate) is only for a word whose writer never sets its SSM; tests pack words via `BitConverter.SingleToUInt32Bits((float)bitfield)`. Full: docs/invariants/fbw-arinc.md#arinc-1
- [ARINC-2] Any ANNOUNCED enum var whose raw value is ARINC-large (≥2^32) must be decoded via `Arinc429Word` before comparing to its `ValueDescriptions`, or the announcer speaks the raw multi-billion word. Full: docs/invariants/fbw-arinc.md#arinc-2
- [ARINC-3] The generic ARINC429 auto-decoder in `MainForm.UpdateDisplayText` must run AFTER `TryGetDisplayOverride` and only for vars `ProcessSimVarUpdate` didn't handle: ad-hoc per-var decoders win, and no var is decoded twice. Full: docs/invariants/fbw-arinc.md#arinc-3
