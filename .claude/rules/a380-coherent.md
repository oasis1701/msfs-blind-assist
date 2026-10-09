---
paths:
  - "MSFSBlindAssist/SimConnect/CoherentDebuggerClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentDisplayClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentEWDClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentFwsFailureClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentNDClient.cs"
  - "MSFSBlindAssist/Resources/coherent-a380*.js"
  - "MSFSBlindAssist/Resources/coherent-oans-agent.js"
  - "MSFSBlindAssist/Resources/coherent-rmp-agent.js"
  - "MSFSBlindAssist/Resources/coherent-ewd-agent.js"
  - "MSFSBlindAssist/Resources/coherent-ecl-agent.js"
  - "MSFSBlindAssist/Resources/coherent-display-agent.js"
  - "MSFSBlindAssist/Forms/FBWA380/**"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Rmp.cs"
---
# FlyByWire A380X Coherent clients, OANS and RMP rules

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/a380-coherent.md.

- [A380C-1] Never assume a Coherent UI element needs the KCCU cursor or a DOM click: check what the real cockpit input drives (the MFD uses `InputField` keypress, the ECL ECP L:var pulses). Full: docs/invariants/a380-coherent.md#a380c-1
- [A380C-4] Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists; the SD Upper-E/WD fallback goes through the one always-on monitor socket. Full: docs/invariants/a380-coherent.md#a380c-4
- [A380C-5] The ECL must share the EWD monitor's Coherent socket, never open its own: a second inspector connection to the same page is rejected. Full: docs/invariants/a380-coherent.md#a380c-5
- [A380C-6] Every form marshaling a background bridge push to the UI thread must wrap `BeginInvoke` in try/catch(InvalidOperationException) (`SafeBeginInvoke`); an `IsHandleCreated` check alone races handle destruction. Full: docs/invariants/a380-coherent.md#a380c-6
- [A380C-7] Never re-add the OANS forced-render/zoom/visibility DOM scrape (it froze the whole machine); OANS stays data-only: JS instance reads and `btvUtils` calls, never a canvas draw or scrape. Full: docs/invariants/a380-coherent.md#a380c-7
- [A380C-8] Never loop `armExit`/`armRunway` over the full OANS exit list (each arm redraws the canvas and a sweep exhausts memory); arm only the 1-2 same-named candidate features. Full: docs/invariants/a380-coherent.md#a380c-8
- [A380C-9] Call OANS `loadAirportMap(icao)` directly, never only via the `oans_display_airport` bus event: under perf-hide mode that handler sets the ICAO and skips the load. Full: docs/invariants/a380-coherent.md#a380c-9
- [A380C-10] KCCU H-events must be published on the MFD's own msfs-sdk EventBus (`bus.pub('hEvent', ...)`); `Coherent.trigger`/`SimVar.SetSimVarValue` from the external debugger do not reach the MFD. Full: docs/invariants/a380-coherent.md#a380c-10
- [A380C-11] `fireKey` must fire each KCCU H-event once, never via both `Coherent.trigger` and `SimVar.SetSimVarValue`, which caused double/erratic F-PLN paging. Full: docs/invariants/a380-coherent.md#a380c-11
- [A380C-17] Never write `SetStoredData` for metric weight and expect it to propagate; only the real EFB "US Units" toggle changes the aircraft. MSFSBA's Units button is a local read-out preference, kept separate from the aircraft's value. Full: docs/invariants/a380-coherent.md#a380c-17
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
- [A380C-19] Compute the RMP VHF standby readback from the typed digit entry, never a single polled scrape: FBW's autocomplete settles over several frames after the last keystroke. Full: docs/invariants/a380-coherent.md#a380c-19
- [A380C-20] RMP row selection is authoritative from the manual `Ctrl+1/2/3` pick, synced from the scrape only on the first poll; a per-poll sync resets a fresh selection back to row 0. Full: docs/invariants/a380-coherent.md#a380c-20
- [A380C-21] Set the RMP squawk via the stock `XPNDR_SET` event regardless of the displayed RMP page; the page-switch + keypad + auto-validate chain is unreliable to drive externally. Full: docs/invariants/a380-coherent.md#a380c-21
- [A380C-22] The RMP announce (`Apply`) must marshal to the UI thread: `_announcer.Announce` silently fails off it while the dedup key still updates, masking later announcements. Full: docs/invariants/a380-coherent.md#a380c-22
- [A380C-23] Dispose every A380 form holding a Coherent client or the def in `SwitchAircraft`'s cleanup; a hide-on-close form (RMP) tears down in `Dispose(bool)`, since `Close()` is cancelled and `Form.Dispose()` skips `OnFormClosed`. Full: docs/invariants/a380-coherent.md#a380c-23
- [A380C-24] Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown), or seat/slider motor timers keep writing L:vars into the new aircraft. Full: docs/invariants/a380-coherent.md#a380c-24
- [A380C-25] The A380 EWD scrape must baseline silently on first connect; only failures appearing after connect are announced, as with every other MSFSBA monitor. Full: docs/invariants/a380-coherent.md#a380c-25

Mirrored from troubleshooting.md (it governs the A380 forms that build Coherent clients, such as FBWA380RmpForm's `CoherentDisplayClient` per RMP; change it there and here together):
- [DBG-9] Coherent GT allows only ONE inspector socket per page for ANY aircraft using it — never open a second client against a view another client already holds; share the connection. Full: docs/invariants/troubleshooting.md#dbg-9
