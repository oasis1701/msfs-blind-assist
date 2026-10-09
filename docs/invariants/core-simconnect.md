# Core SimConnect and MainForm — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/core-simconnect.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## SIM-1

- The SimConnect data-definition budget is 1000 per connection (resets per aircraft switch) — never register a var as BOTH an individual def AND a batch-covered def; a Continuous+IsAnnounced var must skip its individual registration and read from the shared batch cache. → [architecture.md](../architecture.md)

## SIM-2

- `SetupDataDefinitions` must register bulk/batch vars LAST, after the fixed/critical defs (AIRCRAFT_INFO/ATC/position) — so a def-count overflow degrades gracefully instead of stranding aircraft detection. → [architecture.md](../architecture.md)

## SIM-3

- Watch `registration.log`'s `approxTotalDefs` — never let a new var addition push a connection's total definitions near/over 1000 without splitting to a second SimConnect connection. → [architecture.md](../architecture.md)

## SIM-4

- `RequestVariable(key, forceUpdate:true)` must also work for batch-covered (no-individual-def) vars — `ProcessContinuousBatch` must consult `forceUpdateVariables` too, or a forced re-read of an unchanged batch value silently no-ops. → [architecture.md](../architecture.md)

## SIM-5

- `SimConnectManager.RequestVariable` may be called from ANY thread and must stay safe there: the MD-11's walks and read-backs reach `ReadFreshAsync` from thread-pool threads (`ConfigureAwait(false)` chains). The manager captures the UI thread's WinForms `SynchronizationContext` and thread id at construction (`UiThreadGate`; any other kind of context leaves the gate inert, because a context that runs its posts on a pool thread would re-post forever), and `RequestVariable` called off that thread POSTS itself there and re-checks the connection when it runs — a post refused at shutdown is dropped, never run inline. `FreshReadWaiters.WaitAsync` registers the waiter BEFORE invoking the issue callback, so a posted request loses nothing, and `continuousVariableIndexMap` is a `ConcurrentDictionary`. Never read that map or issue `RequestDataOnSimObject` from a pool thread directly. The CEVENT pump's calculator writes still run on the pump thread — `SendMFCommand` is a pure send with no shared state — a recorded residual, not a licence. → [md11.md](../md11.md)

## SIM-6

- Request ids 505–508 are the hand-numbered guidance frames (`(DATA_REQUESTS)505..508`: visual guidance, takeoff assist, taxi-guidance position, flare assist — `SimConnectManager.Monitoring.cs`/`Dispatch.cs`) and 600–607 are the ground-traffic sweeps' rotating range (`REQUEST_GROUND_TRAFFIC`, `GroundTrafficRequestIdCount`) — never assign an id in either range to anything else, and grep for raw `(DATA_REQUESTS)` casts before choosing a new one: SimConnect treats a request issued under an existing id as a REPLACEMENT, so a sweep planned at 501–508 would have cancelled taxi guidance's own position stream (the enum alone does not show those four; an earlier review missed them exactly that way). Pinned by `GroundTrafficRequestIdTests`, which also scans every `*.cs` under `MSFSBlindAssist/` for a raw `(DATA_REQUESTS)NNN` cast, so a future hand-numbered id is caught too. → [architecture.md](../architecture.md)

Added 2026-10-09: 341–348 (camera reads: `REQUEST_CAMERA_VIEW` + `CameraReadIdCount`, each display read on its own id, AI-8), below `INDIVIDUAL_VARIABLE_BASE` and pinned by `CameraReadWaitersTests.TheManagersCameraRange_IsBelowTheIndividualBase_AndClearOfEveryOtherRequestId`. The range was stated only in AI-3's full text, whose rule file loads on no `SimConnectManager` partial but the camera one.

## SIM-7

- A delivered value is a CHANGE (fires `SimVarUpdated`) only when it moves by more than its definition's `SimVarDefinition.ChangeTolerance`, or the shared `SimConnectManager.ChangeTolerance` (0.001) when that is null — ONE rule, `SimConnectManager.IsValueChange`, for both delivery paths. Widen it only for a var whose readers need a coarse line and whose ripple costs work: the MD-11's `MD11_DC_BUS_VOLTAGE` is 0.5 V (its power gate needs only the 20 V line, and each ripple re-composed every stateful row of the open panel once a second). The cache still takes every delivery, so a drift slower than the tolerance per sample never fires — never widen a var whose reader acts on a small cumulative change, and never one `Md11SeedGate` counts (it compares at the shared constant). → [md11.md](../md11.md)

## SIM-8

- The calc-path probe MUST report its verdict — `CalcPathVerdict.LogLine` on both outcomes, and `PilotWarning` spoken when an aircraft that registers the probe target (`MSFSBA_BRIDGE_PROBE` — the FBW defs, the Headwind A330 by inheritance, and the TFDi MD-11) concludes UNVERIFIED. Every other aircraft concludes at once: the log line is still written, but `PilotWarning` returns null for it, so nothing is spoken, by design, since it has nothing to verify and nothing to warn about. MainForm's timer gates on THAT REGISTRATION, never on a type list: a type list is how the MD-11 — every control write a calculator-path CEVENT — sat outside the probe with no module and no warning, while every load logged a false `NOT available after 0 attempt(s)`. It reported NOTHING before, which is the sole reason a broken probe degraded every generic L:var write and dotted FBW event for ten weeks unnoticed. Never make the verdict silent again. → [architecture.md](../architecture.md)

Corrected 2026-10-08: the one-line form had dropped "an aircraft that registers the probe target", so it read as a spoken warning on every aircraft; this text now also says what the others do. Evidence: `CalcPathVerdict.PilotWarning`, `SimConnectManager.MarkCalcPathProbeConcluded` (`aircraftNeedsCalcPath` defaults to false) and `MainForm.BridgeProbeTimer_Tick`; which aircraft register the target is pinned by `CalcPathProbeOptInTests`.

## SIM-9

- The `MSFSBA_BRIDGE_PROBE` read-back LAGS ITS WRITE BY ONE ROUND (the data-def request is issued right after the calc write, so the sim answers with the pre-write value) — the match must accept the PREVIOUS nonce as well (`SimConnect.BridgeProbe.IsEcho`). Comparing only the current nonce misses by exactly one every round, and since each miss picks a fresh nonce the probe NEVER converges: measured live with the nonce past 861 having never once verified. The cost is silent and huge — `CalcPathVerified` stays false, so `SetLVar` falls back to the data-def write that is unreliable for FBW L:vars AND every dotted FBW event falls back to a transport the FCU ignores ("the FCU won't accept"). → [architecture.md](../architecture.md)

## SIM-10

- **A380** FCU events (`A32NX.FCU_*`) must NEVER wait on that probe (`SimConnectManager.IsFbwFcuEvent`) — the A380X FCU consumes them strictly as calculator K-events, so a probe false negative does not degrade them, it kills them. `FireFCUButton` always bypassed the gate by calling `ExecuteCalculatorCode` directly, which is exactly why the knob buttons worked while the combos silently did nothing — that inconsistency hid the fault. The predicate is gated on `AircraftCode == "FBW_A380"` and that half is load-bearing: the **A32NX shares the event NAMES** but is reached fine by `MapClientEventToSimEvent` + `TransmitClientEvent`, so applying the bypass there deletes a working fallback instead of rescuing a broken one — a pilot without the MobiFlight WASM module (a supported, degraded configuration, the one `CalcPathVerdict.PilotWarning` announces) would lose `FCU_HDG/SPD/ALT_SET`, the `FCU_EFIS_{L,R}_BARO_{SET,PUSH,PULL}` writes and every A320 FCU panel button outright. → [architecture.md](../architecture.md)

## SIM-11

- `SetLVar`'s MobiFlight calc-path routing must gate on `CalcPathVerified`, never on bare `IsMobiFlightConnected` — that flag is true even with no WASM module installed. → [architecture.md](../architecture.md)

## SIM-12

- `SetLVar` keeps stock SimVar names (`TRANSPONDER STATE:1`) and every name containing a space on the data-def write path — never route one through the L:var calc path. Today it sends every name with a colon there too, but the shape decides only the write route, never whether a name is a stock SimVar: an add-on's real L:vars can carry a colon index or a space (`B787_IRS_Knob_State:1`, which the HS787 writes through `SetLVar`; VAR-2). Such an L:var gets the native data-def write, not the calculator path, so read the write back before trusting it (DBG-1, DBG-4). For a colon-indexed add-on L:var that route is today's behaviour, never measured, not a rule: if an in-sim read-back shows its data-def write reverting, moving colon-indexed add-on L:vars to the calculator path does not break this rule. → [architecture.md](../architecture.md)

Reworded 2026-10-07. The original text, verbatim from CLAUDE.md as of `1f37801a`, was: "A name containing a space or colon (e.g. `TRANSPONDER STATE:1`) is a stock SimVar shape and must stay on the data-def write path — never route it through the L:var calc path." Its "is a stock SimVar shape" contradicted VAR-2 as narrowed the same day; the routing it guards is unchanged. The colon-indexed add-on L:var was left out of the "never" the same day: nothing had measured its write on either route.

Corrected 2026-10-08: the one-line form restores this text's condition, which it had shortened to "change it after an in-sim read-back": move colon-indexed add-on L:vars to the calculator path only if the read-back shows the data-def write reverting. Evidence: this rule's 2026-10-07 text above; the route itself is `SimConnectManager.SetLVar` in `SimConnectManager.EventSend.cs`.

## SIM-13

- H: events must always go to the MobiFlight channel whenever `IsMobiFlightConnected`; dotted events must wait for `CalcPathVerified` (queued, bounded, flushed on verify or probe-conclude) — never fire a dotted event before the probe concludes, except the A380's `A32NX.FCU_*` events, which SIM-10 sends straight to the calculator path without waiting (`SimConnectManager.IsFbwFcuEvent`). → [architecture.md](../architecture.md)

Corrected 2026-10-08: names SIM-10's exception, which this rule's "never" contradicted. Evidence: `SimConnectManager.SendEvent` in `SimConnectManager.EventSend.cs`, which fires the calculator event when `CalcPathVerified || IsFbwFcuEvent(...)` holds, before it queues anything.

## SIM-14

- The status-display auto-refresh repaint must be a LEADING-edge one-shot coalesce, never a restart-per-push trailing debounce — a trailing debounce starves under high-frequency PFD/ISIS streams (hand-fly posts per SIM_FRAME). → [architecture.md](../architecture.md)

## SIM-15

- `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first — relying on stale cached `displayValues` alone goes stale for any def that returns `true` from `ProcessSimVarUpdate` (A32NX COM freqs, A380 EFIS baro, HS787 flight data all skip the generic write). → [architecture.md](../architecture.md)

## SIM-16

- The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary on every call. → [architecture.md](../architecture.md)

## SIM-17

- Every calc-path event string must be unique per call — `SimConnectManager.BuildCalcEventCode` prefixes `{seq} 0 *` centrally, because MobiFlight dedups byte-identical consecutive commands and a TOGGLE fires the SAME event for on and off, so without it a control could be switched on and never off. The dedup keys on TEXT, not elapsed time: "presses are seconds apart" is NOT a mitigation. → [a380x.md](../a380x.md)

## SIM-18

- The SimConnect "Frame" system event (`SYSTEM_EVENT_ID.Frame`, `SimConnectManager.FrameRate.cs`) is subscribed only while a consumer holds a `StartFrameRateMonitoring` request, released with the last `StopFrameRateMonitoring`, and re-armed by `SetupEvents` on a new connection only when a request is still held. Never hoist the `SubscribeToSystemEvent` into `SetupEvents` unconditionally: the sim raises it once per rendered frame (30 to 120 times a second), each one a `ReceiveMessage` dispatch on the UI thread next to the per-frame SIM_FRAME data the taxi, landing and guidance paths already consume, and the only reader is the File > Sim Performance window. The handler itself only stores a value in `FrameRateMeter`; the window averages it on its own one-second timer. → [architecture.md](../architecture.md)

## SIM-19

- A SIM_FRAME + CHANGED own subscription delivers only on a change, so it is SEEDED by a ONCE request on `FreshReadPolicy.SeedRequestId` (the definition id + 900000, with no request-map entry), and only once `OnRecvSimobjectData` is attached: `SeedSimFrameSubscriptions` runs after `SetupEvents`, and again after a re-registration, because `SetupDataDefinitions`' `DoEvents` drains any answer that arrives before the handler exists. `RequestVariable` also seeds on an empty or forced cache. A seed's answer fills the cache and completes NO fresh-read waiter. The ordering is untestable, and it is SimConnect-wide rather than MD-11-only, which is why this rule lives here. → [architecture.md](../architecture.md)

Split from MD11-8 on 2026-10-09: one mechanism per ID.

## SIM-20

- `SimConnectManager.SendEvent` (`SimConnect/SimConnectManager.EventSend.cs`) is safe on the UI thread only. On its stock-event path it maps every event name it has not sent before (`if (!eventIds.ContainsKey(eventName)) { uint eventId = nextEventId++; eventIds[eventName] = eventId; … MapClientEventToSimEvent … }`) and then reads the id back for `TransmitClientEvent`. `eventIds` is a plain `Dictionary<string, uint>` with no lock, `nextEventId++` is not atomic, and the UI thread also writes the map (`RegisterClientEvents`, from `SetupEvents` and `ReregisterAllVariables`) and clears it (`Disconnect`). So never call `SendEvent` from a pool thread: not inside `Task.Run`, not from a `System.Threading.Timer` or `System.Timers.Timer` callback, and not in a continuation after `ConfigureAwait(false)`. To send after a delay, start a plain `async` method or local function on the UI thread and `await Task.Delay(...)` with no `ConfigureAwait(false)` (the PMDG 777 emergency lights, [P777-13]; the iFly flap walk), or marshal with `BeginInvoke`. H: events and dotted events on a verified calc path leave before the map (`FireCalcEvent`, whose `calcEventSeq` is `Interlocked`), and the pending-event queue is locked; but a stock event always reaches the map, and a dotted event reaches it once the calc-path probe has concluded unverified, or when the MobiFlight module object is absent (the calc branch needs `mobiFlightWasm != null`, and `InitializeMobiFlight` nulls it when initialization fails), so the rule covers every caller.

Added 2026-10-09 from a caller audit (`git grep -n "SendEvent(" -- MSFSBlindAssist`, leaving out the PMDG data managers' own `SendEvent`, a different method). Every caller but two runs on the UI thread: MainForm's panel controls (`MainForm.PanelBuilder.cs`: WinForms events and `System.Windows.Forms.Timer` ticks); every definition's `HandleUIVariableSet`, which only the panel builder, the aircraft windows (the Fenix FCU windows' `SetFCUVariable`, the A380 baro window's `ApplyUIVariable`) and the PMDG autopilot window call; `HandleHotkeyAction`, reached from `WM_HOTKEY` through `HotkeyManager.ProcessWindowMessage`; the FBW and HS787 FCU, autopilot and baro windows and dialogs, directly or through the definitions' helpers (`SetFCU*Value`, `FireFCUButton`, `SetEfisBaro*`, `SetAltIncrement`, `SetTrkFpaMode`, `ToggleMetricAltitude`, `ToggleFlightDirectors`, `ShowFCUInputDialog`); the PMDG 737 and iFly NAV radio dialogs and the iFly baro dialog; and the async methods that resume on the UI thread (the iFly flap walk and `TuneNavActiveAsync`, the PMDG 777 emergency lights, `MainForm.TuneCom1FromSurroundings`, and the MD-11's `SetComStandby` and `SwapCom`, which send before their first await). `FlushPendingCalcEvents` re-enters `SendEvent` from `MainForm.BridgeProbeTimer_Tick`, a WinForms timer.

Known race, not fixed (found 2026-10-09): `FlyByWireA320Definition.DeferReadback` runs its action inside `Task.Run` after a `Task.Delay`, so two writes it defers reach `SendEvent` from a pool thread. (1) `COM{n}_RADIO_SWAP`, 100 ms after a COM active-frequency set in `HandleUIVariableSet`: a stock event, so it always reads the map, and writes it the first time that name is sent. (2) `A32NX.FCU_ALT_SET`, 50 ms after the 100 ft increment when the target of `SetFCUAltitudeValue` is not a multiple of 1000 (`SetAltitudeAndAnnounce`; a multiple of 1000 runs it synchronously): a dotted event, so it reaches the map only after the probe concluded unverified, or when the MobiFlight module object is absent. The Headwind A330 inherits both from `FlyByWireA320Definition`. A write to the map on another thread at the same moment (a UI-thread `SendEvent` of a name not sent before, `RegisterClientEvents` on a reconnect or aircraft switch, the `Clear` in `Disconnect`, or the swaps of two radios set within 100 ms of each other) can corrupt the map or give two names one id. Fixing it means deferring those two writes on the UI thread; that is a code change for its own PR.
