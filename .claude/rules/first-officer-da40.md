---
paths:
  - "MSFSBlindAssist/FirstOfficer/DA40/**"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/DA40/**"
---
# First Officer rules: COWS Diamond DA40 (NG and XLS)

Loaded when Claude reads matching code, on top of first-officer.md. Background: docs/first-officer.md ("COWS Diamond DA40") and docs/da40.md. Full text of each rule: docs/invariants/first-officer-da40.md.

- [FOD-1] Every DA40 First Officer write goes through `CowsDA40Definition.HandleUIVariableSet` (`DA40FoActionExecutor.Apply`), the panels' measured path; a key it does not claim is REFUSED, never written blind with `SetLVar` (most obvious DA40 variables are read-only mirrors, DA40-1). Full: docs/invariants/first-officer-da40.md#fod-1
- [FOD-2] The First Officer's step is the one voice: `Apply` mutes the definition's own confirmation (`ScreenReaderAnnouncer.OwnerMute`) and marks the echo (`CowsDA40Definition.UiEchoMark`); only `SpeakingKeys` speak (a refusal, the ECU test's verdict, a numeric setting). Full: docs/invariants/first-officer-da40.md#fod-2
- [FOD-3] The DA40 door write is a TOGGLE: the First Officer closes doors only through `CloseDoorsKey`, which writes a door only when the cache reads it definitely open; never a door key in a flow step. Full: docs/invariants/first-officer-da40.md#fod-3
- [FOD-4] The read-backs are the INSTALLED COWS pages (`DA40FoReadback.Build`, `NativeChecklistReader.LoadPages`), matched by page, block, subject and expectation; an unmatched line is a Captain line and an unknown page Informational. Never transcribe COWS text into code; reference pages are never ticked. Full: docs/invariants/first-officer-da40.md#fod-4
- [FOD-5] A DA40 phase is built ONCE into its flow and its action group (`DA40FoPhase`): never hand-write an action line without its step, and a step delivering a read-back line names it with `cl`. Full: docs/invariants/first-officer-da40.md#fod-5
- [FOD-6] The DA40 First Officer never moves the power lever or throttle except to the XLS start positions, and never sets a transponder code, a QNH, a frequency or the flight plan: a lever the page checks is a Captain item followed by a read-only check (`PowerLeverIdle`). Full: docs/invariants/first-officer-da40.md#fod-6
- [FOD-7] The crank (`CrankKey`) holds the start key until 500 RPM or `MaxCrankSeconds`, then always releases it (also on Stop and window close, `HeldPresses`); the XLS runs the mixture in as the engine fires and keeps cranking until it holds 500 RPM (`XlsCatchMs`). The prime (`PrimeKey`) always ends at cut-off. Full: docs/invariants/first-officer-da40.md#fod-7
