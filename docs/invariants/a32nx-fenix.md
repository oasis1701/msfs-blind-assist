# FlyByWire A32NX and Fenix A320 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/a32nx-fenix.md`, which Claude Code loads when it reads matching code. Background: [a32nx.md](../a32nx.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A320-1

- Do NOT build an A32NX circuit-breaker panel: only ONE breaker (49VU E12, the ECAM Control Panel's own power) is modelled as clickable; the 196 monitored positions in the `A32NX_CB_*_TRIPPED_*` bitmasks are SDAC monitoring inputs with no 3-D object or name. → [a32nx.md](../a32nx.md)

## A320-2

- The A32NX C/B TRIPPED cautions need a monitored bit AND flight phase 1/2/6 AND a full 60-second confirm — don't declare them broken after a short wait. → [a32nx.md](../a32nx.md)

## A320-3

- Every Fenix panel pushbutton must go through a full PRESS-RELEASE pulse (0→1→0), never press-only — a press-only pulse leaves the button held down for the whole session (root cause of the stuck TO CONFIG / stuck ECAM STATUS bug that re-fired the takeoff-config test after touchdown). → [a32nx.md](../a32nx.md)

## A320-4

- Do not revert `ExecuteButtonTransition` to the press-only form — the release is safe/correct for all ~150 Fenix buttons (systems latch on the 0→1 rising edge into a separate indicator var; the release never loses state). → [a32nx.md](../a32nx.md)

## A320-6

- The A32NX Flight Director controls are the `A32NX.FCU_EFIS_{L,R}_FD_PUSH` events, and their state is `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` (`A32NX_FCU_EFIS_{L,R}_FD_ACTIVE` does not exist in FBW), NOT `TOGGLE_FLIGHT_DIRECTOR`/`A320_Neo_FCU_FD_n_PUSH`/`A380X_EFIS_L_FD_BUTTON_IS_ON` — those alternates genuinely fail; don't re-test the wrong vars and re-conclude "uncontrollable." → [a32nx.md](../a32nx.md)

Corrected 2026-10-08: the rule named `A32NX_FCU_EFIS_{L,R}_FD_ACTIVE`, which FBW does not have; the controls are the push events and the light. Evidence: `FlyByWireA320Definition`'s `A32NX.FCU_EFIS_{L,R}_FD_PUSH` and `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` registrations and its `FdLeftLightVar`/`FdRightLightVar`, and the comment in `Forms/FBWA320/FBWA320AutopilotWindow.cs` that the `_FD_ACTIVE` names do not exist.

## A320-7

- Never write A32NX overhead L:vars via the unreliable data-def `SetLVar` path when testing — the earlier "PACK/HOT AIR/BLEED/etc. are computed outputs that revert" verdict was an artifact of testing with the wrong write path; the calculator path sticks for all of them. → [a32nx.md](../a32nx.md)

## A320-8

- The RMP audio/volume `A32NX_RMP_{L,R}_VHF{n}_VOLUME` L:vars do not exist in dev FBW — do not re-add the ACP volume combos; the physical ACP is unmodeled. → [a32nx.md](../a32nx.md)

## A320-9

- **The A32NX armed-ALT constraint qualifier is the SSM of `A32NX_FMGC_{1,2}_FM_ALTITUDE_CONSTRAINT`, NOT a discrete bit** — `FmgcComputer.cpp:4898` sets that word's SSM to Normal Operation exactly when `alt_cstr_applicable` is true and No Computed Data otherwise, so it is read for VALIDITY and its number is never used (Normal Operation ONLY — the accessor its own PFD uses for a value word's validity, where the A380 reads a discrete bit and uses `bitValueOr`; each mirrors its own aircraft, neither is stricter, do not "harmonize" them). ⚠️ Do NOT port the A380 rule by assumption: only the final `verticalArmed = altArmed | (clbArmed << 2) | …` expression is identical between the two shims — the A32NX reads FMGC A-bus discrete word 3 bits **12/22/23/24/25** (not PRIM FG word 2's 11/13/14/15/16/18) and has NO ALT CRZ branch at all. Read BOTH FMGCs and OR them: the armed bitmask follows `fmgcPriorityIndex`, so an FMGC-1-only read goes quiet whenever FMGC 2 holds priority. The PFD's extra `!clbArmed` term is deliberately not reproduced on either airframe (it picks which single label to draw, not whether a constraint exists; MSFSBA announces each newly-armed mode separately). Bit 2 was REMOVED from `_vertArmedBits` (structurally absent — `base_fmgc_armed_modes` has no constraint member) but bit 64/TCAS was KEPT (the shim hardcodes `bool tcasArmed = false;` where its siblings read a bit — not-yet-wired, not not-modelled). Source-verified, not sim-verified. ⚠️ **Anything this def speaks from a CALLBACK rather than from `ProcessSimVarUpdate` — a timer tick, or the `OnDeferredFlushBatchDelivered` batch hook — must check `A32NXDisabledMonitorVariablesSet` ITSELF** — the A32NX is muted CENTRALLY (MainForm wraps `announcer.Suppressed` around `ProcessSimVarUpdate`, because vars like the A320's EFIS baro readouts — and the Headwind A330's stock-Kohlsman altimeter, which shares the wrap — announce from INSIDE it and return true, exiting before the generic `A32NXDisabledMonitorVariables` gate, so a Ctrl+M un-tick never muted them) and such a callback runs OUTSIDE that wrap, so a muted var would still be spoken; the armed-ALT flush (`OnDeferredFlushBatchDelivered`, see the A380 bullet above for the hold it belongs to) is the live example, and the A380 has no such trap because its armed branch checks its own disabled set locally. → [a32nx.md](../a32nx.md)

## A320-10

- The A32NX FMS's own post-takeoff clear of V1/VR/V2 is consumed SILENTLY once `A32NX_FMGC_FLIGHT_PHASE` is TAKEOFF **or later** — never "past TAKEOFF": the phase rides continuous batch 1 and the V-speeds batch 2 (separate once-a-second requests), and the FMS clears on its own 1-second throttle, so the clear can reach MSFSBA before the CLIMB phase does; it can never arrive with anything below TAKEOFF. A clear before takeoff thrust (a runway change) is still spoken. `SimVarDefinition.IsNotSet` is the ONE "not set" test the readout and the status box share. → [a32nx.md](../a32nx.md)

## A320-11

- The A32NX predicted-takeoff-pitch-trim sign is INVERTED vs the A380 (`-ths`, negative = nose up) — never copy a sign convention between the two FMSes without checking the writer. → [a32nx.md](../a32nx.md)

## A320-12

- FUEL MODE SEL junction options are 1-based (`t+1`, not `t`) — sending the raw toggle value selects the wrong option despite the L:var/light appearing to follow. → [a32nx.md](../a32nx.md)

## A320-13

- The evacuation-horn shut-off L:var write is ONE-WAY — never pulse it back to 0, that resumes the horn. → [a32nx.md](../a32nx.md)

## A320-14

- Momentary FBW L:var button pulses must be sent as TWO SEPARATE calc calls, never a single same-frame `1 (>L:X) 0 (>L:X)` string — the Rust sampler doesn't see a same-tick pulse. → [a32nx.md](../a32nx.md)

Corrected 2026-10-09: "two separate calc calls" was not enough; two calls issued back to back would fail too. MobiFlight's command channel is one shared buffer it reads once per frame, so back-to-back `ExecuteCalculatorCode` calls land in the same frame and the second overwrites the first (the A380 RMP comment records this). The working helper writes `1 (>L:X)`, waits 250 ms, then writes `0 (>L:X)`, so the sampler sees the pressed state on its own tick; it also speaks "<name> pressed" once. The A380's ECP keys do not use it: they hold for the same 250 ms (`A380EcpKeyPulse.HoldMs`), but also keep a 250 ms `ReleaseMs` gap between presses on the one shared `A380EcpKeyPulse` clock ([A380-13]). Do not confuse this with the A380 RMP keys ([A380-7]), which are H-events sent as ONE call carrying press and release; the two shapes are opposite and must not be harmonised. Evidence: `BaseAircraftDefinition.PulseMomentaryLVar`; the blue e-pump override comment in `FlyByWireA320Definition` ("separated press/release toggles the latch; a same-frame 1->0 pulse is NOT seen by the Rust sampler"); `FlyByWireA380Definition.SendRmpKey`'s CRITICAL #1 comment (`FlyByWireA380Definition.Rmp.cs`).

## A320-15

- The A32NX DCDU display must be a ListBox, not a multiline TextBox — a right-aligned key label read on a separate braille line from its leading key number in a TextBox. → [a32nx.md](../a32nx.md)

## A320-16

- DCDU soft-key slot must be mapped by POSITION (a Y-threshold), never by simple L/R order — an empty-state key can be alone on a side yet still live on the second slot. → [a32nx.md](../a32nx.md)

## A320-17

- DCDU page-scroll direction must be DOWN=forward everywhere — the answer keys stay inactive until the pilot has paged to the end of a multi-page uplink, so an inverted direction silently blocks answering. → [a32nx.md](../a32nx.md)

## A320-18

- Unmodified PageUp/PageDown must scroll WITHIN the displayed DCDU message (`POEMINUS`/`POEPLUS`), with Ctrl+PageUp/Down stepping between messages — never the reverse: every other CDU window (A320/A380 MCDU, PMDG 737/777, HS787, iFly 737) binds plain PageUp/Down to scrolling the content being read, and within-message paging is the load-bearing one (it unlocks the answer keys), so it must not sit behind a modifier. → [a32nx.md](../a32nx.md)

## A320-19

- Per-DCDU-key ACTIVE flags must be checked before firing — an inactive key must never falsely confirm a press. → [a32nx.md](../a32nx.md)

## A320-20

- An inactive DCDU key must NOT dead-end on "read to the end of the message first" — FBW loses the per-message `reachedEndOfMessage` flag whenever a new message's page count matches the last one rendered (it is only raised on MessageVisualization's page-count TRANSITION or a page key), leaving every answer key permanently dead. Re-assert with `POEPLUS` and retry the key, but ONLY when the scraped page counter shows nothing left to read — never page past unread text on the pilot's behalf. → [a32nx.md](../a32nx.md)

## A320-21

- A32NX FCU baro STD/QNH polarity is PULL=STD/PUSH=QNH — the opposite of the A380's PUSH=STD/PULL=QNH; never harmonize them. → [a32nx.md](../a32nx.md)

## A320-22

- **The FCU hardware-dial callouts (A32NX, Headwind A330, A380) may only listen to sources that say ON THEIR OWN whether the FCU window shows a selection** — the heading/speed shims `A32NX_AUTOPILOT_{HEADING,SPEED}_SELECTED` (-1 while dashed), the ARINC429 words that are Normal Operation only for a displayed selection (A32NX `A32NX_FCU_SELECTED_{ALTITUDE,VERTICAL_SPEED,FPA}`, A380 `A32NX_PRIM_1_SELECTED_{VERTICAL_SPEED,FPA}`) and the A380's stock FCU altitude. NEVER the `A32NX_FCU_AFS_DISPLAY_*_VALUE` display values: while dashed the A32NX FCU copies the LIVE heading, airspeed and vertical speed into them, and announcing them read the heading out as the aircraft turned on the ground (a failed FCU also zeroes the altitude one). Never pair a value with a separate dashes flag either (two deliveries, racing order), and never the `A32NX_AUTOPILOT_{VS,FPA}_SELECTED` shims (0 while dashed on the A380, the live value on the A32NX). A change is STAGED, never spoken on delivery, and released by `OnContinuousBatchDelivered` once its whole batch has finished dispatching (it is judged against the FCU health var of the same sample — except the A380's PRIM 1 V/S and FPA words, which sort so late they ride the NEXT batch and are judged against the health var delivered one batch earlier, still safe because nothing is spoken until batch-end) — that release runs OUTSIDE MainForm's `announcer.Suppressed` wrap, so each airframe's own `ProcessSimVarUpdate`/flush must check its own Ctrl+M mute set itself. A callout is released only while the FCU is AVAILABLE — `A32NX_FCU_HEALTHY` (A32NX) / `A32NX_FCU_AFS_CP_ACTIVE` (A380), which rides the SAME batch as the heading/speed shims (`FcuHealthBatchMembershipTests`) but sorts after them and before the `FCU_SELECTED_*`/PRIM words; the batch-end release makes either order safe — a drop clears what was staged and blocks later staging, a return starts a settle. A failed ARINC word composes `FcuValuePhrases.Unavailable` on either airframe, and on the A380 so do the speed shim's and `FCU_ALT_VALUE`'s own impossible zeros (the FCU never selects 0 kt/Mach or below 100 ft) — but NOT the heading shim's zero: `FcuValuePhrases.Heading`/`HeadingDegrees` has no zero case (0° is a real heading), so a dead A380 FCU's zero heading composes an ordinary "Heading 000 degrees" and is kept silent only by the health-var gate above, not by `Unavailable`. A power-DOWN is silent, a power-UP starts a settle — the A380 zeroes every output rather than dashing it, so the FCU coming back otherwise reads as every knob turning at once. Every MSFSBA-origin write arms its echo from the ONE table `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` BEFORE the send (`OnPanelButtonFiring`; the confirmation kind says what else already speaks the write — mode feedback, a value readout, or neither) — bar the calculator-code V/S set (`SetFCUVSValue`), which arms the same two keys, vertical speed and flight path angle, directly — and a dotted A32NX event `SendEvent` queued behind the calc-path probe re-arms its echo when `FlushPendingCalcEvents` finally sends it (`QueuedEventDispatched`) — the echo armed at the call site can be long expired by then. V/S push/pull are muted only when a readout is about to speak the value; nothing else confirms a V/S level-off. `FcuValueAnnouncer` compares PHRASES (null = dashes, recorded not spoken), records muted and readout-pending deliveries, and settles (absorbing changes until the aircraft has published and gone quiet, baselines KEPT never wiped) on a flight load or reconnect (`OnSimContextReset` / `OnVariableCacheCleared` — a reconnect's re-fire of every var IS the evidence, ending in about 5-6 batches; a load waits for an aircraft-published source to move, then five quiet batches, or thirty regardless) and on an Aircraft-menu switch made within 60 s of `AircraftLoaded` (a judgement value, not a measurement). → [a32nx.md](../a32nx.md)

Corrected 2026-10-09: "every MSFSBA-origin write arms its echo from the ONE table" now names its one exception, the calculator-code V/S set, which arms the same two keys directly. Evidence: `SetFCUVSValue` in `FlyByWireA320Definition.cs` and its A380 counterpart in `FlyByWireA380Definition.HotkeysAndMotion.cs`, both calling `SuppressFcuValueChangeEcho(Fcu.VerticalSpeed, Fcu.FlightPathAngle)`; the A380 definition's own comment says the same.

Split out on 2026-10-09: A320-36 (V/S and FPA from the ARINC words, never the shims), A320-37 (the release outside the `Suppressed` wrap), A320-38 (every write arms its echo first, and a queued event re-arms) and A320-39 (the aircraft switch's settle). This text keeps them as written; each has its own section below.

## A320-23

- The A32NX autobrake set must use the MobiFlight calculator path, never `SetLVar` (data-def write is unreliable for this FBW L:var). → [a32nx.md](../a32nx.md)

## A320-24

- Never re-fold the A380's metric-ALTITUDE (MTRS) feature into the A32NX — the real A320 has no MTRS button (A330+/A380 only). → [a32nx.md](../a32nx.md)

## A320-25

- A32NX approach minimums read the plain-feet `AIRLINER_*` L:vars, never the `A32NX_FM1_*` ARINC words — those are NCD until near-destination, so a gate-entered MDA/DH read "Not set" (same bug + fix as the A380). → [a32nx.md](../a32nx.md)

## A320-26

- A32NX wing anti-ice writes `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION` (the Rust input), never `_SYSTEM_SELECTED` — that is a Rust per-frame OUTPUT and any write reverts (<2 s, any phase; the old "holds in flight" note was a mis-test). → [a32nx.md](../a32nx.md)

## A320-27

- A32NX nose/landing lights are driven by the indexed stock events in the FBW template's verbatim RPN form `<value> <index> r (>K:2:LANDING_LIGHTS_SET/TAXI_LIGHTS_SET)` — the `LIGHTING_LANDING_x` L:vars drive nothing (nose holds-but-dead, wing template-owned/reverts). (RPN `r` swaps the top two stack entries, so this form is stack-equivalent to index-first no-`r`; the old "index-first is a NO-OP" claim was a test confound — keep the template-verbatim form, don't rewrite others without live re-verification.) → [a32nx.md](../a32nx.md)

## A320-28

- A32NX wipers are circuits 77 (Capt) / 80 (F/O) — not the A380's 141/143 — and the live OFF/SLOW/FAST position needs BOTH circuit switch AND power (power rests at 100% while off; the switch bool alone can't read back FAST); `XMLVAR_A320_WiperSwitch_*` does not exist in FBW. → [a32nx.md](../a32nx.md)

## A320-29

- A32NX seat belts is genuinely 2-position ON/OFF in the FBW model (no AUTO — unlike the A380); don't "fix" it to 3-position. → [a32nx.md](../a32nx.md)

## A320-30

- A32NX "Passengers on Board" sums the `A32NX_PAX_{A..D}_DESIRED` planned bitmasks, not the lagging boarded set (same lesson as the A380 pax fix). → [a32nx.md](../a32nx.md)

## A320-31

- The Fenix MCDU marks a selected option with cyan AND large font — never gate the accessible `*` marker on green alone (that left CONFIG > FAILURES with no indication of NONE/MINOR/ALL), and never "fix" it by broadening the colour test to cyan: cyan is used for entry fields, brackets and the leading `←` cycle arrow, so it asterisks nearly every line. Selection detection belongs in `FenixMcduFormat`'s size rule (large-among-small within a `/`-separated group), which must stay conservative — ≥2 options, each uniformly one size, exactly one large. The colour rule must run BEFORE the size rule and the size rule must skip a token the colour rule already marked — the shared `HashSet<int>` only collapses an identical index, and the two rules anchor differently (green-segment start vs first letter/digit), so a green option starting on `(`/`[`/`←` double-marks as `A/*(*B)` without the per-token overlap check. `currentLarge = true` is load-bearing, not a fallback (the live NONE-selected capture has no size code before NONE), and `SpecialChars` must keep its `\uXXXX` escapes — the Latin-1 keys mangle at runtime, with no compile error, if the BOM-less file is ever re-saved as CP1252. → [a32nx.md](../a32nx.md)

## A380-8

- Every A32NX DCDU H-event fire (`FlyByWireDcduForm.FireDcduEvent`) must be similarly sequence-uniquified — the WILCO→SEND two-step press on the same slot would otherwise silently drop the second press to the coalescing bug. The A380 has no DCDU: its CPDLC lives on the MFD ATCCOM pages. → [a380x.md](../a380x.md)

Corrected 2026-10-08: names the A32NX DCDU, the only DCDU there is. Evidence: `FlyByWireDcduForm.FireDcduEvent` (`Forms/FlyByWireA320/FlyByWireDcduForm.cs`), and the "A380 has NO DCDU instrument" comment in `FlyByWireA380Definition`.

Moved 2026-10-08 from docs/invariants/a380-systems.md: the code is the A32NX DCDU form's (`FlyByWireDcduForm.FireDcduEvent`), and the A380 has no DCDU. The ID keeps its prefix.

## A320-36

- The FCU V/S and FPA dial callouts read the ARINC words (`FcuSources`: on the A32NX and A330 `A32NX_FCU_SELECTED_VERTICAL_SPEED` and `A32NX_FCU_SELECTED_FPA`, on the A380 `A32NX_PRIM_1_SELECTED_VERTICAL_SPEED` and `_FPA`), never the `A32NX_AUTOPILOT_{VS,FPA}_SELECTED` shims: unlike the heading and speed shims, they never read -1 while the window is dashed (the A32NX shim carries the LIVE value, the A380's reads 0), so a callout fed from them speaks while the window shows dashes. On the A380 `FbwFcuDialAnnounceTests.A380_vs_and_fpa_shims_are_not_announce_sources` pins it; on the A32NX, replacing the word source fails a test but adding a shim as an extra source fails nothing. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

## A320-37

- FCU dial callouts are released at batch end by `BaseAircraftDefinition.OnContinuousBatchDelivered`, OUTSIDE MainForm's `announcer.Suppressed` wrap, so the wrap mutes none of them: every `AnnounceFcuValue` caller passes `muted:` itself, from its own Ctrl+M set (`A32NXDisabledMonitorVariablesSet`, `A380DisabledMonitorVariablesSet`) or a readout pending for the same value. Never rely on the wrap. A320-9 states it for the A32NX and A330 flushes; nothing pins the A380's or the base class's release. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

## A320-38

- Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, with the keys from the ONE table `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`; `OnPanelButtonFiring` for panel buttons; the confirmation kind says what else already speaks the write: mode feedback, a value readout, or neither). The one write that arms directly is the calculator-code V/S set (`SetFCUVSValue`), with the same two keys, vertical speed and flight path angle. A dotted A32NX event `SendEvent` queued behind the calc-path probe re-arms its echo when `FlushPendingCalcEvents` finally sends it (`QueuedEventDispatched`, which MainForm forwards to the definition): the echo armed at the call site can be long expired by then. `FcuEchoKeysTests` pin the table's content and `FcuValueAnnouncerTests` the re-arm itself; the event and its forwarding are untested. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.

## A320-39

- `MainForm.SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement value, not a measurement) of `AircraftLoaded`: without it, an Aircraft-menu switch made while a flight loads speaks the flight's first published FCU values as knob turns. The settles on a flight load and on a reconnect live in the definition (A320-22); this one is the switch's own call, and nothing tests it. → [a32nx.md](../a32nx.md)

Split from A320-22 on 2026-10-09: one mechanism per ID.
