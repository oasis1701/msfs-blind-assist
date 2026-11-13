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
  - "tests/MSFSBlindAssist.Tests/**/*SimStateTracker*.cs"
---
# Core SimConnect and MainForm rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/core-simconnect.md.

- [SIM-1] The SimConnect data-definition budget is 1000 per connection; never register a var as BOTH an individual def AND a batch-covered def — a Continuous+IsAnnounced var skips its individual registration and reads the shared batch cache. Full: docs/invariants/core-simconnect.md#sim-1
- [SIM-14] The status-display auto-refresh repaint must be a LEADING-edge one-shot coalesce, never a restart-per-push trailing debounce, which starves under high-frequency PFD/ISIS streams. Full: docs/invariants/core-simconnect.md#sim-14
- [SIM-15] `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first; cached `displayValues` alone go stale for any def whose `ProcessSimVarUpdate` returns `true`. Full: docs/invariants/core-simconnect.md#sim-15
- [SIM-16] The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary every call. Full: docs/invariants/core-simconnect.md#sim-16
- [SIM-20] Never add a `SimConnectManager.SendEvent` caller off the UI thread (`Task.Run`, a pool timer, after `ConfigureAwait(false)`): it maps each new event name into the unlocked `eventIds` Dictionary, bumping a non-atomic `nextEventId`. Marshal first, or `await` the delay on the UI thread. The FBW A320's `DeferReadback` sends are a known race. Full: docs/invariants/core-simconnect.md#sim-20
- [SIM-21] Pause announcements read Pause_EX1 bit by bit (`SimStateTracker`): 8 is the Esc menu pause, 4 active pause. Never speak the full-pause bit 1, which MSFS 2024 raises on its own while loading and after entering the cockpit; speak nothing while the "Sim" event says no flight runs; seed silently on every connection. Full: docs/invariants/core-simconnect.md#sim-21
- [SIM-23] A status-display var repaints the list in BOTH branches of `OnSimVarUpdated` (a def-handled row too); with a combo focused the tick holds back only that combo's var, unless `StatusRefreshPausesWhileComboFocused` (an `OnDisplayPanelShown` override: SD-page aircraft) keeps the whole pause; Ctrl+3 and GotFocus repaint from the cache first. Full: docs/invariants/core-simconnect.md#sim-23

Mirrored from da40-shared-code.md (they govern SimConnectManager, SimVarDefinition and CoherentDisplayClient code; change them there and here together):
- [DA40S-1] `GetCachedVariableValue` is keyed by the VARIABLE KEY, never the SimVar name, and holds a variable when it is `Continuous && IsAnnounced` (`ExcludeFromBatch` vars too); never turn a cache miss into 0 (`EveryCacheLookupNamesACachedVariableKey`). Full: docs/invariants/da40-shared-code.md#da40s-1
- [DA40S-5] `SimVarDefinition.Format` defaults to "F0" and `ValueDescriptions` is never null (test `Count`, not null). Full: docs/invariants/da40-shared-code.md#da40s-5
- [DA40S-8] A `CoherentDisplayClient` agent returns a string containing `MSFSBA_DISP_INSTALLED`; the client reports a wrong answer once per socket, and that report stays. Full: docs/invariants/da40-shared-code.md#da40s-8
