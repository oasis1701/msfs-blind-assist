# FCU dial callouts (A32NX, A330, A380) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/fcu-callouts.md`, which Claude Code loads when it reads matching code. Background: [a32nx.md](../a32nx.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A320-22

- **The FCU hardware-dial callouts (A32NX, Headwind A330, A380) may only listen to sources that say ON THEIR OWN whether the FCU window shows a selection** — the heading/speed shims `A32NX_AUTOPILOT_{HEADING,SPEED}_SELECTED` (-1 while dashed), the ARINC429 words that are Normal Operation only for a displayed selection (A32NX `A32NX_FCU_SELECTED_{ALTITUDE,VERTICAL_SPEED,FPA}`, A380 `A32NX_PRIM_1_SELECTED_{VERTICAL_SPEED,FPA}`) and the A380's stock FCU altitude. NEVER the `A32NX_FCU_AFS_DISPLAY_*_VALUE` display values: while dashed the A32NX FCU copies the LIVE heading, airspeed and vertical speed into them, and announcing them read the heading out as the aircraft turned on the ground (a failed FCU also zeroes the altitude one). Never pair a value with a separate dashes flag either (two deliveries, racing order), and never the `A32NX_AUTOPILOT_{VS,FPA}_SELECTED` shims (0 while dashed on the A380, the live value on the A32NX). A change is STAGED, never spoken on delivery, and released by `OnContinuousBatchDelivered` once its whole batch has finished dispatching (it is judged against the FCU health var of the same sample — except the A380's PRIM 1 V/S and FPA words, which sort so late they ride the NEXT batch and are judged against the health var delivered one batch earlier, still safe because nothing is spoken until batch-end) — that release runs OUTSIDE MainForm's `announcer.Suppressed` wrap, so each airframe's own `ProcessSimVarUpdate`/flush must check its own Ctrl+M mute set itself. A callout is released only while the FCU is AVAILABLE — `A32NX_FCU_HEALTHY` (A32NX) / `A32NX_FCU_AFS_CP_ACTIVE` (A380), which rides the SAME batch as the heading/speed shims (`FcuHealthBatchMembershipTests`) but sorts after them and before the `FCU_SELECTED_*`/PRIM words; the batch-end release makes either order safe — a drop clears what was staged and blocks later staging, a return starts a settle. A failed ARINC word composes `FcuValuePhrases.Unavailable` on either airframe, and on the A380 so do the speed shim's and `FCU_ALT_VALUE`'s own impossible zeros (the FCU never selects 0 kt/Mach or below 100 ft) — but NOT the heading shim's zero: `FcuValuePhrases.Heading`/`HeadingDegrees` has no zero case (0° is a real heading), so a dead A380 FCU's zero heading composes an ordinary "Heading 000 degrees" and is kept silent only by the health-var gate above, not by `Unavailable`. A power-DOWN is silent, a power-UP starts a settle — the A380 zeroes every output rather than dashing it, so the FCU coming back otherwise reads as every knob turning at once. Every MSFSBA-origin write arms its echo from the ONE table `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` BEFORE the send (`OnPanelButtonFiring`; the confirmation kind says what else already speaks the write — mode feedback, a value readout, or neither) — bar the calculator-code V/S set (`SetFCUVSValue`), which arms the same two keys, vertical speed and flight path angle, directly — and a dotted A32NX event `SendEvent` queued behind the calc-path probe re-arms its echo when `FlushPendingCalcEvents` finally sends it (`QueuedEventDispatched`) — the echo armed at the call site can be long expired by then. V/S push/pull are muted only when a readout is about to speak the value; nothing else confirms a V/S level-off. `FcuValueAnnouncer` compares PHRASES (null = dashes, recorded not spoken), records muted and readout-pending deliveries, and settles (absorbing changes until the aircraft has published and gone quiet, baselines KEPT never wiped) on a flight load or reconnect (`OnSimContextReset` / `OnVariableCacheCleared` — a reconnect's re-fire of every var IS the evidence, ending in about 5-6 batches; a load waits for an aircraft-published source to move, then five quiet batches, or thirty regardless) and on an Aircraft-menu switch made within 60 s of `AircraftLoaded` (a judgement value, not a measurement). → [a32nx.md](../a32nx.md)

Corrected 2026-10-09: "every MSFSBA-origin write arms its echo from the ONE table" now names its one exception, the calculator-code V/S set, which arms the same two keys directly. Evidence: `SetFCUVSValue` in `FlyByWireA320Definition.cs` and its A380 counterpart in `FlyByWireA380Definition.HotkeysAndMotion.cs`, both calling `SuppressFcuValueChangeEcho(Fcu.VerticalSpeed, Fcu.FlightPathAngle)`; the A380 definition's own comment says the same.

Split out on 2026-10-09: A320-36 (V/S and FPA from the ARINC words, never the shims), A320-37 (the release outside the `Suppressed` wrap), A320-38 (every write arms its echo first, and a queued event re-arms) and A320-39 (the aircraft switch's settle). This text keeps them as written; each has its own section below.

Moved 2026-10-10 from a32nx-fenix.md (split for the rule budget).

## A320-36

- The FCU V/S and FPA dial callouts read the ARINC words (`FcuSources`: on the A32NX and A330 `A32NX_FCU_SELECTED_VERTICAL_SPEED` and `A32NX_FCU_SELECTED_FPA`, on the A380 `A32NX_PRIM_1_SELECTED_VERTICAL_SPEED` and `_FPA`), never the `A32NX_AUTOPILOT_{VS,FPA}_SELECTED` shims: unlike the heading and speed shims, they never read -1 while the window is dashed (the A32NX shim carries the LIVE value, the A380's reads 0), so a callout fed from them speaks while the window shows dashes. On the A380 `FbwFcuDialAnnounceTests.A380_vs_and_fpa_shims_are_not_announce_sources` pins it; on the A32NX, replacing the word source fails a test but adding a shim as an extra source fails nothing. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

Moved 2026-10-10 from a32nx-fenix.md (split for the rule budget).

## A320-37

- FCU dial callouts are released at batch end by `BaseAircraftDefinition.OnContinuousBatchDelivered`, OUTSIDE MainForm's `announcer.Suppressed` wrap, so the wrap mutes none of them: every `AnnounceFcuValue` caller passes `muted:` itself, from its own Ctrl+M set (`A32NXDisabledMonitorVariablesSet`, `A380DisabledMonitorVariablesSet`) or a readout pending for the same value. Never rely on the wrap. A320-9 states it for the A32NX and A330 flushes; nothing pins the A380's or the base class's release. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

Moved 2026-10-10 from a32nx-fenix.md (split for the rule budget).

## A320-38

- Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, with the keys from the ONE table `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`; `OnPanelButtonFiring` for panel buttons; the confirmation kind says what else already speaks the write: mode feedback, a value readout, or neither). The one write that arms directly is the calculator-code V/S set (`SetFCUVSValue`), with the same two keys, vertical speed and flight path angle. A dotted A32NX event `SendEvent` queued behind the calc-path probe re-arms its echo when `FlushPendingCalcEvents` finally sends it (`QueuedEventDispatched`, which MainForm forwards to the definition): the echo armed at the call site can be long expired by then. `FcuEchoKeysTests` pin the table's content and `FcuValueAnnouncerTests` the re-arm itself; the event and its forwarding are untested. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

Moved 2026-10-10 from a32nx-fenix.md (split for the rule budget).

## A320-39

- `MainForm.SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement value, not a measurement) of `AircraftLoaded`: without it, an Aircraft-menu switch made while a flight loads speaks the flight's first published FCU values as knob turns. The settles on a flight load and on a reconnect live in the definition (A320-22); this one is the switch's own call, and nothing tests it. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

Moved 2026-10-10 from a32nx-fenix.md (split for the rule budget).
