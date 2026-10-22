---
paths:
  - "MSFSBlindAssist/Services/Fenix*.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320*.cs"
  - "MSFSBlindAssist/Aircraft/FenixVariant*.cs"
  - "MSFSBlindAssist/Forms/Fenix/**"
  - "MSFSBlindAssist/FirstOfficer/Fenix/**"
  - "tests/MSFSBlindAssist.Tests/**/*Fenix*.cs"
---
# Fenix data service rules

Loaded when Claude reads matching code. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fenix-data.md.

- [FNX-1] A Fenix data-service read of an unknown name returns `""`, not null (v3): `FenixDataValue.FromJson` maps it to `EmptyOrUnknown`, and no caller may treat that as 0, false, off or closed; a JSON null is unknown too. Full: docs/invariants/fenix-data.md#fnx-1
- [FNX-2] `FenixDataService` subscription callbacks run on a thread-pool thread, never the UI thread: a form marshals to its own thread with BeginInvoke inside try/catch ([A380C-6]), and the service itself never speaks or touches a control. Full: docs/invariants/fenix-data.md#fnx-2
- [FNX-3] Take Fenix data-service names only from open sources (the gateway's own EFB/FAP bundles, names already in the app, HubHop); never guess a name, and never introspect the schema to find one (ruled out on purpose). Full: docs/invariants/fenix-data.md#fnx-3
