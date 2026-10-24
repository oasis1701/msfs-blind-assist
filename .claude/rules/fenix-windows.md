---
paths:
  - "MSFSBlindAssist/Forms/Fenix/**"
  - "MSFSBlindAssist/Services/FenixCabin*.cs"
  - "MSFSBlindAssist/Services/FenixDocument*.cs"
  - "MSFSBlindAssist/Services/FenixEngine*.cs"
  - "MSFSBlindAssist/Services/FenixFailure*.cs"
  - "MSFSBlindAssist/Services/FenixOption*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixCabin*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixDocument*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixEngine*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixFailure*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixOption*.cs"
---
# Fenix windows rules: cabin, documents, engines, failures, options

Loaded when Claude reads matching code, on top of fenix-data.md. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fenix-windows.md.

- [FNX-12] Fenix documents are read with GETs only (`FenixDocumentEndpoints`: list, search, content); no Fenix code calls /fenix/documents/OpenDocumentsFolder or the upload POST to /fenix/documents, which act on the pilot's PC. The one write is the pinned_documents setting, in the EFB's own JSON (`FenixDocumentPins.Serialize`). Full: docs/invariants/fenix-windows.md#fnx-12
- [FNX-14] Every Fenix cabin write is built by `FenixCabinWrites` (name, kind, value as the EFB and FAP write them) or `FenixCabinPram` (the FAP bundle's mutation text verbatim, its `success` checked); never `doors.control.*` 0 (Armed), which the EFB never writes, and never a door or cargo door above 100 ft or with that height unknown (the EFB's gate). Full: docs/invariants/fenix-windows.md#fnx-14
- [FNX-15] The Fenix cabin window and announcements use only the doors, slides and lavatories `FenixCabinLayout.Resolve` gives (settings `emergency_exits_type` and `aircraft_variant` first, then the aircraft type): the data service answers for doors an airframe lacks (the A321 reads overwing slides armed), so a known value never proves a part exists. Full: docs/invariants/fenix-windows.md#fnx-15
- [FNX-17] A Fenix failure write in `FenixFailuresForm` (fail now, arm, clear) posts only after a Yes/No box defaulting to No and speaks the state the Fenix's reply carries (`FenixFailureRequests.ParseSaved`), never the requested one; no timer, search, open or close ever posts. Full: docs/invariants/fenix-windows.md#fnx-17
- [FNX-18] The Fenix failure catalog depends on the loaded variant (A320 549 failures, A321 IAE 555): never hard-code a failure id, count or chapter; `FenixFailureText.Changes` speaks only ids present in both reads. Full: docs/invariants/fenix-windows.md#fnx-18
