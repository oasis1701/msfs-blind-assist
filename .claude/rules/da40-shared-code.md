---
paths:
  - "MSFSBlindAssist/Aircraft/DA40/**"
  - "tests/MSFSBlindAssist.Tests/**/CowsDA40SettleExceedsBatchTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/CowsDA40VarNameCollisionTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/CowsDA40HotkeyCacheTests.cs"
---
# Rules the DA40 work found in shared code

Rules about shared code that the DA40 work measured. A line guarding a shared file is MIRRORED word for word into the rule file that loads that file; change it here and in its mirrors together. Full text of each rule: docs/invariants/da40-shared-code.md. DA40S-3, DA40S-4 and DA40S-6 govern every aircraft definition, so they live in variable-definitions.md, which loads on this aircraft too.

- [DA40S-1] `GetCachedVariableValue` is keyed by the VARIABLE KEY, never the SimVar name, and holds a variable when it is `Continuous && IsAnnounced` (`ExcludeFromBatch` vars too); never turn a cache miss into 0 (`EveryCacheLookupNamesACachedVariableKey`). Full: docs/invariants/da40-shared-code.md#da40s-1
- [DA40S-2] A settle timer outlasts the sample period of what feeds it: over 1000 ms for a batch-fed value; a SIM_FRAME-fed value carries a `FAST-SAMPLED` marker (`CowsDA40SettleExceedsBatchTests`); an own-write grace outlasts the settle. Full: docs/invariants/da40-shared-code.md#da40s-2
- [DA40S-5] `SimVarDefinition.Format` defaults to "F0" and `ValueDescriptions` is never null (test `Count`, not null). Full: docs/invariants/da40-shared-code.md#da40s-5
- [DA40S-7] A background monitor starts in BOTH `MainForm_Load` and `SwitchAircraft`, or it never runs for the aircraft the app opens with. Full: docs/invariants/da40-shared-code.md#da40s-7
- [DA40S-8] A `CoherentDisplayClient` agent returns a string containing `MSFSBA_DISP_INSTALLED`; the client reports a wrong answer once per socket, and that report stays. Full: docs/invariants/da40-shared-code.md#da40s-8
- [DA40S-9] `NativeChecklistReader` renders the aircraft's own `Checklist/*.xml`, walking each page's children IN ORDER including one level of `<Block>`; it is pinned against the installed package, and packages resolve from UserCfg.opt's active key. Full: docs/invariants/da40-shared-code.md#da40s-9
- [DA40S-10] Use `tools/coherent-coverage.js` before claiming any Coherent display is fully read: its visibility test walks the ancestor chain and chrome is reported once; it reports candidates, not verdicts. Full: docs/invariants/da40-shared-code.md#da40s-10
- [DA40S-11] FBW mirrors the LEFT aileron only: `A380SurfaceDeflection.DescribeMirrored` is for the left aileron and `Describe` for the right aileron and both elevators, or a droop reads as a roll. Full: docs/invariants/da40-shared-code.md#da40s-11
- [DA40S-12] Take-off-roll speed calls are decided only by `TakeoffRollCallouts` and applied for the CURRENT aircraft on every path that configures `TakeoffAssistManager` (aircraft switch AND Settings save); a profile is never applied without being taken back. Full: docs/invariants/da40-shared-code.md#da40s-12
- [DA40S-13] `UnusualAttitudeMonitor` INTERRUPTS beyond 45 degrees of bank with hysteresis and announces the recovery at 20 degrees, a deliberate exception to the silent-clear rule; never turn it into a query and never silence the recovery. Full: docs/invariants/da40-shared-code.md#da40s-13
- [DA40S-14] `NativeChecklistReader` opens every file through `LoadDocument`, behind a static constructor that registers the code-page provider: Asobo checklists declare Windows-1252, which .NET reads only with one, and without it every load threw and read as "no checklist". A real-package test skips only when the file is not found. Full: docs/invariants/da40-shared-code.md#da40s-14
