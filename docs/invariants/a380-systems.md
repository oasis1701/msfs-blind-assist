# FlyByWire A380X systems and panels — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/a380-systems.md`, which Claude Code loads when it reads matching code. Background: [a380x.md](../a380x.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## A380-1

- The annunciator/integral LT-TEST knob is render-only — never synthesize a spoken narration of what the bulbs would show; only announce the knob's own position and let real per-system fault lights announce genuine faults. → [a380x.md](../a380x.md)

## A380-2

- Every A380 panel control must render as a COMBO, not a hardware button, EXCEPT true one-shot momentary actions (ECAM-CP keys, chrono, calls, ATC ack) and the seat-motor toggles A380-10 prescribes (`RenderAsButton`: a press starts the motor, a second press stops it) — a control that must show ongoing state must never be a plain button (the reverted APU-start-PB and Fire-Test cases). → [a380x.md](../a380x.md)

Corrected 2026-10-08: the exceptions now include the seat-motor `RenderAsButton` toggles, which A380-10 requires and this rule forbade. Evidence: `FlyByWireA380Definition`'s `SeatBtn` helper (`RenderAsButton = true`, handled by `ToggleSeatMotor`).

## A380-3

- Never implement the FBW flyPad pushback controls — Robin's team uses GSX for pushback; this is a permanent decision. → [a380x.md](../a380x.md)

## A380-4

- Never treat the Surveillance pedestal panel as a working feature — FBW's own docs say it's not yet implemented; transponder AUTO-mode and squawk are the only real controls, reachable via the MFD SURV page. → [a380x.md](../a380x.md)

## A380-5

- Before assuming an announced ARINC var can't be injection-tested, check whether it has a per-frame writer (Rust systems/rendering instrument) — only vars the writer leaves alone are injectable; real verification of writer-owned vars needs a live scenario. → [a380x.md](../a380x.md)

## A380-6

- FBW named AC/DC buses and batteries publish as `A32NX_ELEC_{rawBusName}_BUS_IS_POWERED` with the raw bus id — never invent a descriptive name (e.g. `_AC_EHA_...`); confirm the id in the Rust source. → [a380x.md](../a380x.md)

## A380-7

- Every A380 RMP calc-path write must be made unique per call with a `{seq} 0 *` prefix — MobiFlight's command channel coalesces two consecutive IDENTICAL calc strings, silently dropping a repeated-digit keystroke or a double-press of the same LSK/ADK. → [a380x.md](../a380x.md)

## A380-8

- Every A32NX DCDU H-event fire (`FlyByWireDcduForm.FireDcduEvent`) must be similarly sequence-uniquified — the WILCO→SEND two-step press on the same slot would otherwise silently drop the second press to the coalescing bug. The A380 has no DCDU: its CPDLC lives on the MFD ATCCOM pages. → [a380x.md](../a380x.md)

Corrected 2026-10-08: names the A32NX DCDU, the only DCDU there is. Evidence: `FlyByWireDcduForm.FireDcduEvent` (`Forms/FlyByWireA320/FlyByWireDcduForm.cs`), and the "A380 has NO DCDU instrument" comment in `FlyByWireA380Definition`.

## A380-9

- Seat-motor writes must use a per-frame UNIQUE calc string (`<seq> 0 *` prefix) — identical strings are registered+fired only once by MobiFlight, which is why a naive write only ticked the seat motor once instead of sustaining it. → [a380x.md](../a380x.md)

## A380-10

- Never poll a combo's backing var and snap its displayed value on a periodic re-read for a synthetic motor var that idles at 0 — that causes a spurious restart/stop/announce loop; use a `RenderAsButton` toggle instead so state only changes on the click edge. → [a380x.md](../a380x.md)

## A380-11

- Every FBW unit/feature with an observable effect must be wired into MSFSBA's OWN read-outs — MSFSBA bypasses the cockpit displays, so a display-only conversion never reaches the blind pilot unless MSFSBA applies it itself. → [a380x.md](../a380x.md)

## A380-12

- Never regress the A380 ROW/ROP and BTV rollout distance call-outs — they're safety call-outs for a blind pilot during landing rollout; they can only be verified in a real scenario since their writers are per-frame and injection is unreliable. `A32NX_ROW_ROP_WORD_1` is laid out as FBW #10699 (2026-07-04) left it — 11 BRAKE MAX BRAKING, 12 SET MAX REVERSE, 13 KEEP MAX REVERSE, 14/15 too short wet/dry, "inoperative" in the SSM — the same map the PFD's `AttitudeIndicatorWarnings` and the FWS aurals read (`RowRopWord1Bits`, pinned by `A380RowRopCalloutTests`); MSFSBA carried the pre-#10699 map, which the decoder bug below hid until 2026-09-25. → [a380x.md](../a380x.md)

## A380-13

- **A380 ECAM control-panel keys (`L:A32NX_BTN_*`) must be held `A380EcpKeyPulse.HoldMs` (250 ms) and left released `ReleaseMs` (250 ms) between presses.** FBW #10934 (a380x `27a73e219`, 2026-09-19) moved the FWS's 125 ms `UpdateThrottler` check ahead of the block that reads them, so a key is sampled once per FWS cycle, not every frame: the checklist window's old 45 ms press registered about one time in three, silently. Never shorten them back; re-derive both if FBW changes the throttle (`A380EcpKeyPulseTests` pins it). The release time is enforced on EVERY press, on ONE clock the whole app shares (`A380EcpKeyPulse.Shared`): a press RESERVES its slot when it STARTS, recording when it WILL be released, so a press begun while another is still held waits for it — the checklist window (`PressEcpKey` and its close-time C/L) and the ECAM Control Panel's buttons (the A380's `PulseEcpKey`, every `A32NX_BTN_*` key) all press on it. Never go back to a guard per caller (a press queued during a burst's closing scrape followed the release by only 85 ms plus the scrape) or to recording the release after it happens (Escape during the opening C/L's hold sent the close-time C/L on top of it, and the overlay stayed up). → [a380x.md](../a380x.md)

## A380-14

- When FBW moves a subsystem, diff EVERY `A32NX_`/`A380X_` token in their tree before vs after the commit and intersect with the def's own references — a spot check of the obvious vars found 6 of the 14 casualties in #10855. **⚠️ But do NOT run that intersection against SOURCE TEXT: a full sweep did, and got 137 A380 + 135 A320 "unresolved" tokens of which ZERO were real.** Four false-positive sources, all of which must be handled: a KEY is not a NAME (`A32NX_FCU_LEFT_EIS_BARO_IS_STD`'s Name is `A32NX_FCU_EFIS_L_DISPLAY_BARO_IS_STD`) — enumerate `GetVariables()` from the BUILT ASSEMBLY and check `Name`, which drops comments and concatenation fragments too; BOTH sides build names dynamically (FBW's Rust indexed families never appear literally); EVENTS are registered in TWO places (`SimConnectInterface.cpp` AND the Rust systems WASM — checking one reports five live A32NX autobrake events as missing); and `SimVar.GetRegisteredId` is NOT an existence oracle (per-view, returns a fresh id for a WASM-written var, polluted by your own probes). SCOPE the diff to a COMMIT ("removed and never restored") — dynamic names never appear as removals. Run four passes a name diff structurally cannot do: CONSTANT WRITERS (`idFoo->set(literal)` or a local only ever assigned one — extend to CONTRIBUTORS of a composed write, which is the only way the A32NX's dead `tcasArmed` bit shows up), CHANGED WRITERS (a surviving name with a new writer can change UNITS/ENCODING silently — this is how V/S announced 500 fpm as "98400"), a LIVE batch read through the Coherent debugger, which may only CLEAR a candidate (non-zero proves existence; zero proves nothing, most vars are legitimately zero in the cruise), and STOCK NAMES — every stock SimVar the def reads and stock K-event it sends, which a token diff cannot see and which never disappear: is the event now MASKED and re-meant, is its input still consumed, does anything still write the var (the #10855 flight-director casualty went unseen for five weeks this way). → [a380x.md](../a380x.md)

## A380-15

- That intersection must cover INPUT EVENTS as well as variables (`Type == SimVarType.Event`, plus every `H:` literal) — a deleted event has NO failure signal at all (the sim silently discards an unregistered K-event while the combo keeps reporting the right state from a still-live read var), which is how #10855's first pass shipped 7 dead A380 controls: heading push/pull, V/S pull and both baro STD/QNH pairs. Pinned by `FlyByWireA380EventContractTests`. → [a380x.md](../a380x.md)

## A380-16

- A readout-only var (a decoder in `TryGetDisplayOverride`, no hotkey/window/auto-announce) must be listed in a panel display list or it is DEAD — never requested, decoder never runs, nothing logged. `FMA_CRUISE_ALT_MODE` shipped that way. Pinned by `FlyByWireA380DisplayReachabilityTests`. → [a380x.md](../a380x.md)

## A380-17

- Never leave only the unindexed TCAS RA-guidance vars registered — the fly-to/avoid V/S bands exist ONLY as the `:1`/`:2` indexed L:vars; the unindexed names are never written by FBW. → [a380x.md](../a380x.md)

## A380-18

- The TCAS RA-guidance compose must be DEFERRED (~800ms), never synchronous off the state edge — FBW resets the V/S band vars only in TCAS STBY (not on clear-of-conflict), so a synchronous compose at RA-onset can speak the previous RA's stale sense. → [a380x.md](../a380x.md)

## A380-19

- The TCAS `VSPEED_GREEN/RED:1/:2` + `RA_RATE_TO_MAINTAIN` L:vars MUST be registered `Units="number"`, NEVER a velocity unit — FBW writes them unitless-but-already-in-fpm, so a "feet per minute" data-def read multiplies by 196.85 (assumes native m/s) and speaks garbage RA numbers (1500 → "295276"); the fpm label is hardcoded in the compose/display, not derived from Units. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## A380-20

- Annunciators must be stripped from panel DISPLAY variable sets — a pilot navigates a panel to operate controls, not to scan "X Fault: Normal" rows; numeric/analog and 3+-state status fields are kept. → [a380x.md](../a380x.md)

## A380-21

- When a status has BOTH a PB-light L:var and an ECAM memo for the same condition, the L:var must be `ReadEnumQuiet` and the memo must be the single call-out — never double-announce the same condition from two sources. → [a380x.md](../a380x.md)

## A380-22

- Master Warning/Caution acknowledge must pulse the EXACT L:var name the glareshield XML uses — the A380's is misspelled `MASTERAWARN` (extra A); using the plausible-but-wrong spelling is a silent no-op that never clears the aural. → [a380x.md](../a380x.md)

## A380-23

- #103 breakthrough: A380 overhead PBs (PACK/HOT AIR/ENGINE BLEED/CABIN+AIR-EXTRACT FANS/HYD engine+electric pumps/ELEC bus-tie/galley/HYD PTU/emergency-exit sign) ARE all settable via the calculator path — the earlier "computed outputs that revert" verdict was WRONG (an artifact of testing with the unreliable data-def `set_lvar` write); `XMLVAR_` sign combos now route through the OVHD calc catch-all too. Always test an FBW L:var write with the calculator path, never `set_lvar`. → [a380x.md](../a380x.md)

## A380-24

- Multi-position cockpit switches stay MULTI-position combos, never split into easier On/Off controls: the Nose light is one 3-position T.O./Taxi/Off combo (state = `LIGHTING_LANDING_1`, actuated by indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET`), and Seat Belts is 3-position ON/AUTO/OFF (`XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position` — On/Off drive the stock `CABIN SEATBELTS ALERT SWITCH` via its toggle, AUTO is left to the FBW 500 ms Update). A blind pilot gets the same access a sighted pilot has. → [a380x.md](../a380x.md)

Corrected 2026-10-08: the one-line form named the two L:vars as if they were the controls; they hold the position and drive nothing, as this text says. Evidence: `FlyByWireA380Definition.HandleUIVariableSet`'s `NOSE_LIGHT` (indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET`) and `SEATBELT_SIGN` (`CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE`) branches in `FlyByWireA380Definition.UiVariableSet.cs`.

## A380-25

- **A380 wing anti-ice is the STOCK switch — `(>K:TOGGLE_STRUCTURAL_DEICE)` with state `A:STRUCTURAL DEICE SWITCH` — NOT the A32NX's `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION`, and an earlier bullet here said the opposite.** The two FBW packages ship DIFFERENT bodies for the identically-named `FBW_Airbus_AntiIce_Wing` template, so a 2026-07 change that assumed shared wiring left the control dead in icing. It passed review because the L:var is a DEAD MIRROR — the write sticks and the read-back looks perfect, the playbook's Golden Rule 2 inverse trap. NEVER infer one FBW airframe's wiring from the other's, even behind the same template name, and check `tools/a380-simvars-catalog.md` (which had the right answer throughout) before deriving one. The write is a TOGGLE routed through `A380ToggleCommand.ShouldFire` — fire only when the pick differs, treat an UNKNOWN live value as differing so a cold cache still actuates, and force-read when nothing is sent — over `SendEvent`, NOT the calculator path: a plain stock K-event needs no MobiFlight WASM module, and `TransmitClientEvent` does not coalesce, so the `…Unique` question never arises. The A380 wing anti-ice PNEUMATIC is still not modelled (real switch ON leaves `_SYSTEM_ON` at 0) — a separate FBW gap the read-only "Wing Anti-Ice Flowing" status reports honestly, and NOT the bug above. Engine anti-ice (`ANTI_ICE_SET_ENGn`) and probe heat (auto) both work. → [a380x.md](../a380x.md)

## A380-26

- SD-page row var registration classifies by FBW PREFIX, not by space/colon: a colon-INDEXED FBW L:var (`A32NX_FUEL_USED:n`) is a real L:var, not a stock SimVar — the old "any colon = SimVar" rule registered it as a nonexistent stock SimVar that read 0, blanking the SD Fuel/Cruise Engine fuel-used rows. Stock names (no FBW prefix) still register as SimVar. → [a380x.md](../a380x.md)

## A380-27

- Frequency readouts need explicit formatting: the RMP panel's stock `COM ACTIVE/STANDBY FREQUENCY:n` (MHz) needs a "0.000 MHz" display override or a whole-MHz freq drops its fraction to a bare "137"; ND ADF/VOR need kHz/MHz unit labels (an ADF "890" is a correct 890 kHz, just ambiguous). → [a380x.md](../a380x.md)

## A380-28

- "Passengers on Board" sums the per-station `A32NX_PAX_<st>_DESIRED` seat bitmasks (the planned/target load the flyPad headline + GSX `FSDT_GSX_NUMPASSENGERS` report), NOT the boarded `A32NX_PAX_<st>` set — the boarded bitmask lags and, under GSX-driven boarding, settles below target and stays there (popcount is exact, max 50 seats/station < 2⁵³; it's the wrong quantity, not a math bug). → [a380x.md](../a380x.md)

## A380-29

- Wipers are 3-position OFF/SLOW/FAST per side (Capt = circuit 141, F/O = 143, independent): OFF = `CIRCUIT SWITCH ON` off; SLOW = switch on + `CIRCUIT POWER SETTING` 75%; FAST = switch on + power 100%. The power setting PERSISTS at its default (100%) while the switch is off, so the position needs BOTH vars (switch-first — power alone would misread a cold-start OFF as FAST); expose as a synthetic 3-position combo (not On/Off). → [a380x.md](../a380x.md)
