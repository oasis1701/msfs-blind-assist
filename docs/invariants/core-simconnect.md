# Core SimConnect and MainForm — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/core-simconnect.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## SIM-1

- The SimConnect data-definition budget is 1000 per connection (resets per aircraft switch) — never register a var as BOTH an individual def AND a batch-covered def; a Continuous+IsAnnounced var must skip its individual registration and read from the shared batch cache. → [architecture.md](../architecture.md)

## SIM-14

- The status-display auto-refresh repaint must be a LEADING-edge one-shot coalesce, never a restart-per-push trailing debounce — a trailing debounce starves under high-frequency PFD/ISIS streams (hand-fly posts per SIM_FRAME). → [architecture.md](../architecture.md)

## SIM-15

- `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first — relying on stale cached `displayValues` alone goes stale for any def that returns `true` from `ProcessSimVarUpdate` (A32NX COM freqs, A380 EFIS baro, HS787 flight data all skip the generic write). → [architecture.md](../architecture.md)

## SIM-16

- The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary on every call. → [architecture.md](../architecture.md)

## SIM-20

- `SimConnectManager.SendEvent` (`SimConnect/SimConnectManager.EventSend.cs`) is safe on the UI thread only. On its stock-event path it maps every event name it has not sent before (`if (!eventIds.ContainsKey(eventName)) { uint eventId = nextEventId++; eventIds[eventName] = eventId; … MapClientEventToSimEvent … }`) and then reads the id back for `TransmitClientEvent`. `eventIds` is a plain `Dictionary<string, uint>` with no lock, `nextEventId++` is not atomic, and the UI thread also writes the map (`RegisterClientEvents`, from `Connect`, after `SetupEvents`) and clears it (`Disconnect`). So never call `SendEvent` from a pool thread: not inside `Task.Run`, not from a `System.Threading.Timer` or `System.Timers.Timer` callback, and not in a continuation after `ConfigureAwait(false)`. To send after a delay, start a plain `async` method or local function on the UI thread and `await Task.Delay(...)` with no `ConfigureAwait(false)` (the PMDG 777 emergency lights, [P777-13]; the iFly flap walk), or marshal with `BeginInvoke`. H: events and dotted events on a verified calc path leave before the map (`FireCalcEvent`, whose `calcEventSeq` is `Interlocked`), and the pending-event queue is locked; but a stock event always reaches the map, and a dotted event reaches it once the calc-path probe has concluded unverified, or when the MobiFlight module object is absent (the calc branch needs `mobiFlightWasm != null`, and `InitializeMobiFlight` nulls it when initialization fails), so the rule covers every caller.

Added 2026-10-09 from a caller audit (`git grep -n "SendEvent(" -- MSFSBlindAssist`, leaving out the PMDG data managers' own `SendEvent`, a different method). Every caller but two runs on the UI thread: MainForm's panel controls (`MainForm.PanelBuilder.cs`: WinForms events and `System.Windows.Forms.Timer` ticks); every definition's `HandleUIVariableSet`, which only the panel builder, the aircraft windows (the Fenix FCU windows' `SetFCUVariable`, the A380 baro window's `ApplyUIVariable`) and the PMDG autopilot window call; `HandleHotkeyAction`, reached from `WM_HOTKEY` through `HotkeyManager.ProcessWindowMessage`; the FBW and HS787 FCU, autopilot and baro windows and dialogs, directly or through the definitions' helpers (`SetFCU*Value`, `FireFCUButton`, `SetEfisBaro*`, `SetAltIncrement`, `SetTrkFpaMode`, `ToggleMetricAltitude`, `ToggleFlightDirectors`, `ShowFCUInputDialog`); the PMDG 737 and iFly NAV radio dialogs and the iFly baro dialog; and the async methods that resume on the UI thread (the iFly flap walk and `TuneNavActiveAsync`, the PMDG 777 emergency lights, `MainForm.TuneCom1FromSurroundings`, and the MD-11's `SetComStandby` and `SwapCom`, which send before their first await). `FlushPendingCalcEvents` re-enters `SendEvent` from `MainForm.BridgeProbeTimer_Tick`, a WinForms timer.

Known race, not fixed (found 2026-10-09): `FlyByWireA320Definition.DeferReadback` runs its action inside `Task.Run` after a `Task.Delay`, so two writes it defers reach `SendEvent` from a pool thread. (1) `COM{n}_RADIO_SWAP`, 100 ms after a COM active-frequency set in `HandleUIVariableSet`: a stock event, so it always reads the map, and writes it the first time that name is sent. (2) `A32NX.FCU_ALT_SET`, 50 ms after the 100 ft increment when the target of `SetFCUAltitudeValue` is not a multiple of 1000 (`SetAltitudeAndAnnounce`; a multiple of 1000 runs it synchronously): a dotted event, so it reaches the map only after the probe concluded unverified, or when the MobiFlight module object is absent. The Headwind A330 inherits both from `FlyByWireA320Definition`. A write to the map on another thread at the same moment (a UI-thread `SendEvent` of a name not sent before, `RegisterClientEvents` on a reconnect, the `Clear` in `Disconnect`, or the swaps of two radios set within 100 ms of each other) can corrupt the map or give two names one id. Fixing it means deferring those two writes on the UI thread; that is a code change for its own PR.

Corrected 2026-10-09: the writers of `eventIds` are `RegisterClientEvents` (`SimConnectManager.Setup.cs`, called only from `Connect()` in `SimConnectManager.cs`, after `SetupEvents()`), the new-name mapping in `SendEvent` (`SimConnectManager.EventSend.cs`) and the `Clear` in `Disconnect`. `SetupEvents` and `ReregisterAllVariables`, the aircraft-switch path, never touch the map; an aircraft switch re-registers variables, not events.

## SIM-21

- The pause and slew announcements (`SimPauseSlewTracker`, fed by `SimConnectManager.SimState.cs`) read the "Pause_EX1" system event's flags one bit at a time: `SimPauseFlag` (8) is the Esc menu pause, `ActivePauseFlag` (4) is active pause. `FullPauseFlag` (1) is never spoken, the "Sim" system event gates speech (a change while it reports 0 is tracked silently), and the first delivery of each signal on a connection only seeds it.

Measured live on 2026-10-17 with a throwaway SimConnect probe on MSFS 2024 (build 282174, the FS2020 key layout), a parked aircraft:

- Esc fires the key events `PAUSE_TOGGLE` and `PAUSE_OFF`, then `Paused`, `Pause` = 1 and `Pause_EX1` = 8. Closing the menu: `Unpaused`, `Pause` = 0, `Pause_EX1` = 0. The sim clock (`SIMULATION TIME`) stops.
- Active pause (Pause/Break) fires no key event: `Paused`, `Pause` = 1, `Pause_EX1` = 4, and 0 when it ends. The sim clock keeps running. None of `ACTIVE_PAUSE_TOGGLE`, `TOGGLE_ACTIVE_PAUSE`, `ACTIVE_PAUSE_ON`/`OFF`/`SET` is a known event name, and no simvar for it was found (`IS PAUSED`, `IS ACTIVE PAUSE`, `ACTIVE PAUSE`, `SIM PAUSED` are unrecognized).
- Esc during active pause reports 8, not 12, and closing the menu reports 0: the menu ENDS active pause (a second run showed it staying off until the pilot pressed the key again). So that one delivery is both "Sim paused" and "Active pause off".
- The full-pause bit came only from the sim: two 13 ms 1-then-0 blips while a flight loaded, and a 1 about a second after entering the cockpit from the walkaround, held until the next Esc while the sim clock kept running. Esc on top of it went 0, 9, 8, then 0. Speaking bit 1 would have said "Sim paused" with the sim running; reading the whole value instead of bits would have mis-read the 9.
- Leaving for the main menu sends nothing beyond the Esc that got there (the menu pause stays 8, "Sim" stays 1). The next load sends `SimStop` ("Sim" 0), `PositionChanged`, the 8-to-0 unpause, then `SimStart` ("Sim" 1), all within one second, then the full-pause blips; "Ready to fly" sends nothing. The "Sim" gate is what keeps that unpause silent.
- Slew: `SLEW_TOGGLE` fires and `IS SLEW ACTIVE` flips 0/1 within 3 ms on every toggle; the sim clock stops while slewing. SIM_FRAME requests kept delivering through the Esc pause, active pause and slew.
- Each subscription (`Pause_EX1`, `Sim`, and the CHANGED slew request) delivers its current state at once, which is why the first delivery only seeds: connecting to an already-paused sim stays silent.

Not measured: MSFS 2020, where a pause may report only bit 1 and so go unspoken; re-measure there before widening the bits spoken.
