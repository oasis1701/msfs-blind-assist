---
paths:
  - "MSFSBlindAssist/Services/TcasRaGuidance.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TcasRa*.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320*.cs"
  - "MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs"
  - "MSFSBlindAssist/Aircraft/A380*.cs"
  - "MSFSBlindAssist/Aircraft/Fcu*.cs"
  - "MSFSBlindAssist/Aircraft/ArmedAltitudeMode.cs"
  - "MSFSBlindAssist/Aircraft/WiperPosition.cs"
  - "MSFSBlindAssist/Services/FbwMcdu*.cs"
  - "MSFSBlindAssist/Services/Fenix*.cs"
  - "MSFSBlindAssist/Services/FlyByWire*.cs"
  - "MSFSBlindAssist/SimConnect/CoherentA32nxMcduClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentEvalClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentLinkState.cs"
  - "MSFSBlindAssist/SimConnect/CoherentViewOwnership.cs"
  - "MSFSBlindAssist/Forms/FBWA320/**"
  - "MSFSBlindAssist/Forms/FlyByWireA320/**"
  - "MSFSBlindAssist/Forms/Fenix*/**"
  - "MSFSBlindAssist/Forms/FBWA380/**"
  - "MSFSBlindAssist/Resources/coherent-a32nx-*.js"
  - "tests/MSFSBlindAssist.Tests/**/*A32nx*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A380*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Fenix*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwMcdu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Fcu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FlyByWireMCDU*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwAutothrust*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwVSpeed*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CoherentLinkState*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CoherentViewOwnership*.cs"
---
# TCAS RA guidance rules (A32NX and A380)

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/tcas-ra.md.

- [A380-17] Register the TCAS RA-guidance V/S bands as the `:1`/`:2` indexed L:vars, never only the unindexed names, which FBW never writes. Full: docs/invariants/tcas-ra.md#a380-17
- [A380-18] Defer the TCAS RA-guidance compose (~800 ms), never synchronous off the state edge: FBW resets the V/S band vars only in STBY, so RA onset can speak the previous RA's sense. Full: docs/invariants/tcas-ra.md#a380-18
- [A380-19] Register the TCAS `VSPEED_GREEN/RED:1/:2` and `RA_RATE_TO_MAINTAIN` L:vars with `Units="number"`, never a velocity unit: they are already fpm, and a velocity unit multiplies them by 196.85. Full: docs/invariants/tcas-ra.md#a380-19
