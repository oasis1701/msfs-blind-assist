---
paths:
  - "MSFSBlindAssist/SimConnect/*.cs"
  - "MSFSBlindAssist/MainForm*.cs"
  - "MSFSBlindAssist/Utils/ReadoutFormat.cs"
  - "MSFSBlindAssist/Utils/PanelRowRules.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CalcPath*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FreshRead*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RequestId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BridgeProbe*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimConnectId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimConnectPureLogic*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OwnAircraftFilter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DeliveryLogPolicy*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ValueChangeTolerance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*WakeableDelay*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ReadoutFormat*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PanelRowRules*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*EwdMessageLookup*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ControlStateHook*.cs"
  - "MSFSBlindAssist/Forms/SimPerformanceForm.cs"
  - "MSFSBlindAssist/Services/SimPerformance/**"
  - "tests/MSFSBlindAssist.Tests/**/*SimPerformance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CpuLoad*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FrameRateMeter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GpuCounterInstance*.cs"
---
# Core SimConnect and MainForm rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/core-simconnect.md.

- [SIM-1] The SimConnect data-definition budget is 1000 per connection; never register a var as BOTH an individual def AND a batch-covered def — a Continuous+IsAnnounced var skips its individual registration and reads the shared batch cache. Full: docs/invariants/core-simconnect.md#sim-1
- [SIM-14] The status-display auto-refresh repaint must be a LEADING-edge one-shot coalesce, never a restart-per-push trailing debounce, which starves under high-frequency PFD/ISIS streams. Full: docs/invariants/core-simconnect.md#sim-14
- [SIM-15] `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first; cached `displayValues` alone go stale for any def whose `ProcessSimVarUpdate` returns `true`. Full: docs/invariants/core-simconnect.md#sim-15
- [SIM-16] The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary every call. Full: docs/invariants/core-simconnect.md#sim-16
- [SIM-20] Never add a `SimConnectManager.SendEvent` caller off the UI thread (`Task.Run`, a pool timer, after `ConfigureAwait(false)`): it maps each new event name into the unlocked `eventIds` Dictionary, bumping a non-atomic `nextEventId`. Marshal first, or `await` the delay on the UI thread. The FBW A320's `DeferReadback` sends are a known race. Full: docs/invariants/core-simconnect.md#sim-20
