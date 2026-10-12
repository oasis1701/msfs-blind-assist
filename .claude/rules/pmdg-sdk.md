---
paths:
  - "MSFSBlindAssist/Services/PMDG/**"
  - "MSFSBlindAssist/Forms/PMDG/PMDGSdkSetupDialog.cs"
  - "tests/MSFSBlindAssist.Tests/PMDGOptionsIni*Tests.cs"
  - "tests/MSFSBlindAssist.Tests/PMDGSdkSetup*Tests.cs"
---
# PMDG SDK setup check rules

Loaded when Claude reads matching code. Background: docs/pmdg-737.md and docs/pmdg-777.md (Options.ini requirement). Full text of each rule: docs/invariants/pmdg-sdk.md.

- [PSDK-1] MSFSBA never writes, backs up or patches a PMDG options file (737_Options.ini, 777_Options.ini): `PMDGSdkSetupCheck` only reads it, shared for write and delete so PMDG's own write never fails, and `PMDGSdkSetupDialog` shows the pilot the lines to add. Full: docs/invariants/pmdg-sdk.md#psdk-1
- [PSDK-2] The PMDG SDK setup check runs only on an Aircraft-menu pick of the PMDG 737 or 777 (`OfferPmdgSdkSetup`), never at connect, where its dialog opens behind the simulator unheard; it shows on every pick while a file lacks the lines, never remembering a dismissal. Full: docs/invariants/pmdg-sdk.md#psdk-2
