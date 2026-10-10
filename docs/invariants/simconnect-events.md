# SimConnect events and the calc path — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/simconnect-events.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## SIM-8

- The calc-path probe MUST report its verdict — `CalcPathVerdict.LogLine` on both outcomes, and `PilotWarning` spoken when an aircraft that registers the probe target (`MSFSBA_BRIDGE_PROBE` — the FBW defs, the Headwind A330 by inheritance, and the TFDi MD-11) concludes UNVERIFIED. Every other aircraft concludes at once: the log line is still written, but `PilotWarning` returns null for it, so nothing is spoken, by design, since it has nothing to verify and nothing to warn about. MainForm's timer gates on THAT REGISTRATION, never on a type list: a type list is how the MD-11 — every control write a calculator-path CEVENT — sat outside the probe with no module and no warning, while every load logged a false `NOT available after 0 attempt(s)`. It reported NOTHING before, which is the sole reason a broken probe degraded every generic L:var write and dotted FBW event for ten weeks unnoticed. Never make the verdict silent again. → [architecture.md](../architecture.md)

Corrected 2026-10-08: the one-line form had dropped "an aircraft that registers the probe target", so it read as a spoken warning on every aircraft; this text now also says what the others do. Evidence: `CalcPathVerdict.PilotWarning`, `SimConnectManager.MarkCalcPathProbeConcluded` (`aircraftNeedsCalcPath` defaults to false) and `MainForm.BridgeProbeTimer_Tick`; which aircraft register the target is pinned by `CalcPathProbeOptInTests`.

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-9

- The `MSFSBA_BRIDGE_PROBE` read-back LAGS ITS WRITE BY ONE ROUND (the data-def request is issued right after the calc write, so the sim answers with the pre-write value) — the match must accept the PREVIOUS nonce as well (`SimConnect.BridgeProbe.IsEcho`). Comparing only the current nonce misses by exactly one every round, and since each miss picks a fresh nonce the probe NEVER converges: measured live with the nonce past 861 having never once verified. The cost is silent and huge — `CalcPathVerified` stays false, so `SetLVar` falls back to the data-def write that is unreliable for FBW L:vars AND every dotted FBW event falls back to a transport the FCU ignores ("the FCU won't accept"). → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-10

- **A380** FCU events (`A32NX.FCU_*`) must NEVER wait on that probe (`SimConnectManager.IsFbwFcuEvent`) — the A380X FCU consumes them strictly as calculator K-events, so a probe false negative does not degrade them, it kills them. `FireFCUButton` always bypassed the gate by calling `ExecuteCalculatorCode` directly, which is exactly why the knob buttons worked while the combos silently did nothing — that inconsistency hid the fault. The predicate is gated on `AircraftCode == "FBW_A380"` and that half is load-bearing: the **A32NX shares the event NAMES** but is reached fine by `MapClientEventToSimEvent` + `TransmitClientEvent`, so applying the bypass there deletes a working fallback instead of rescuing a broken one — a pilot without the MobiFlight WASM module (a supported, degraded configuration, the one `CalcPathVerdict.PilotWarning` announces) would lose `FCU_HDG/SPD/ALT_SET`, the `FCU_EFIS_{L,R}_BARO_{SET,PUSH,PULL}` writes and every A320 FCU panel button outright. → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-11

- `SetLVar`'s MobiFlight calc-path routing must gate on `CalcPathVerified`, never on bare `IsMobiFlightConnected` — that flag is true even with no WASM module installed. → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-12

- `SetLVar` keeps stock SimVar names (`TRANSPONDER STATE:1`) and every name containing a space on the data-def write path — never route one through the L:var calc path. Today it sends every name with a colon there too, but the shape decides only the write route, never whether a name is a stock SimVar: an add-on's real L:vars can carry a colon index or a space (`B787_IRS_Knob_State:1`, which the HS787 writes through `SetLVar`; VAR-2). Such an L:var gets the native data-def write, not the calculator path, so read the write back before trusting it (DBG-1, DBG-4). For a colon-indexed add-on L:var that route is today's behaviour, never measured, not a rule: if an in-sim read-back shows its data-def write reverting, moving colon-indexed add-on L:vars to the calculator path does not break this rule. → [architecture.md](../architecture.md)

Reworded 2026-10-07. The original text, verbatim from CLAUDE.md as of `1f37801a`, was: "A name containing a space or colon (e.g. `TRANSPONDER STATE:1`) is a stock SimVar shape and must stay on the data-def write path — never route it through the L:var calc path." Its "is a stock SimVar shape" contradicted VAR-2 as narrowed the same day; the routing it guards is unchanged. The colon-indexed add-on L:var was left out of the "never" the same day: nothing had measured its write on either route.

Corrected 2026-10-08: the one-line form restores this text's condition, which it had shortened to "change it after an in-sim read-back": move colon-indexed add-on L:vars to the calculator path only if the read-back shows the data-def write reverting. Evidence: this rule's 2026-10-07 text above; the route itself is `SimConnectManager.SetLVar` in `SimConnectManager.EventSend.cs`.

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-13

- H: events must always go to the MobiFlight channel whenever `IsMobiFlightConnected`; dotted events must wait for `CalcPathVerified` (queued, bounded, flushed on verify or probe-conclude) — never fire a dotted event before the probe concludes, except the A380's `A32NX.FCU_*` events, which SIM-10 sends straight to the calculator path without waiting (`SimConnectManager.IsFbwFcuEvent`). → [architecture.md](../architecture.md)

Corrected 2026-10-08: names SIM-10's exception, which this rule's "never" contradicted. Evidence: `SimConnectManager.SendEvent` in `SimConnectManager.EventSend.cs`, which fires the calculator event when `CalcPathVerified || IsFbwFcuEvent(...)` holds, before it queues anything.

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-17

- Every calc-path event string must be unique per call — `SimConnectManager.BuildCalcEventCode` prefixes `{seq} 0 *` centrally, because MobiFlight dedups byte-identical consecutive commands and a TOGGLE fires the SAME event for on and off, so without it a control could be switched on and never off. The dedup keys on TEXT, not elapsed time: "presses are seconds apart" is NOT a mitigation. → [a380x.md](../a380x.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-18

- The SimConnect "Frame" system event (`SYSTEM_EVENT_ID.Frame`, `SimConnectManager.FrameRate.cs`) is subscribed only while a consumer holds a `StartFrameRateMonitoring` request, released with the last `StopFrameRateMonitoring`, and re-armed by `SetupEvents` on a new connection only when a request is still held. Never hoist the `SubscribeToSystemEvent` into `SetupEvents` unconditionally: the sim raises it once per rendered frame (30 to 120 times a second), each one a `ReceiveMessage` dispatch on the UI thread next to the per-frame SIM_FRAME data the taxi, landing and guidance paths already consume, and the only reader is the File > Sim Performance window. The handler itself only stores a value in `FrameRateMeter`; the window averages it on its own one-second timer. → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).
