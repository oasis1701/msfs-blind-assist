# PMDG 777 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/pmdg-777.md`, which Claude Code loads when it reads matching code. Background: [pmdg-777.md](../pmdg-777.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## P777-1

- Continuous knobs (brightness, temperature, EFIS baro/mins) cannot be controlled via the PMDG SDK event with a position parameter — do not add them to panels via that path (the cockpit-model-L:var-IS-the-input exception applies only to a few named knobs, e.g. shoulder heaters). → [pmdg-777.md](../pmdg-777.md)

## P777-2

- Fuel control levers are the inverted exception — CDA parameter 1=Cutoff/0=Run, not the usual on/off convention. → [pmdg-777.md](../pmdg-777.md)

## P777-3

- Ground power switches (`ELEC_ExtPwr`) are momentary — always send parameter 1 regardless of target state. → [pmdg-777.md](../pmdg-777.md)

## P777-4

- The PMDG 777 foot-heater combo and the Boris Audio Works hydraulic-pump-model combo drive the SAME physical knob — only expose the Boris combo; do not re-add a separate foot-heater control. → [pmdg-777.md](../pmdg-777.md)

## P777-5

- Crew seats are NOT adjustable on the PMDG 777 — no event/struct field/L:var/animation exists; don't go hunting for seat-motion vars. → [pmdg-777.md](../pmdg-777.md)

## P777-6

- CDU buttons must send parameter 1 (pressed) via CDA — parameter 0 ALSO registers as a press, not a release, so never rely on 0 to mean "no press." → [pmdg-777.md](../pmdg-777.md)

## P777-7

- The CDU array index convention is `0=Captain/1=F.O./2=Observer` for every crew-position array including CDU data areas — the dropdown is Left/Center/Right and needs the `DataCDUIndex` remap (`1→2, 2→1`); the event-prefix switch uses the raw dropdown index and must NOT be remapped. → [pmdg-777.md](../pmdg-777.md)

## P777-8

- `EVT_MCP_VS_SWITCH` is engage/disengage; `EVT_MCP_VS_FPA_SWITCH` is the VS↔FPA display toggle — the SDK names are misleading (confirmed only by live testing); don't swap them back based on the names. → [pmdg-777.md](../pmdg-777.md)

## P777-9

- PMDG-broadcast System Display reads must gate on `IPMDGDataManager.IsReady` — before the first CDA snapshot, `GetFieldValue` returns 0.0 for every field, which must render as `--`, never as "every door open"/"0 lb". → [pmdg-777.md](../pmdg-777.md)

## P777-10

- MainForm's PMDG panel-populate loop must only force-read `Type == PMDGVar` controls — force-reading a non-PMDG control (e.g. an LVar combo) via `GetFieldValue` returns the "unknown field" 0.0 sentinel and silently resets it on every panel re-show. → [pmdg-777.md](../pmdg-777.md)

## P777-11

- The 777 autobrake selector has EIGHT detents including a DISARM at index 2 (`0 RTO / 1 OFF / 2 DISARM / 3 "1" / 4 "2" / 5 "3" / 6 "4" / 7 MAX AUTO`) — never harmonize it with the 737's six (RTO/OFF/1/2/3/MAX, no DISARM); dropping DISARM shifts every label above OFF one step and silently commands one setting lower than announced. → [pmdg-777.md](../pmdg-777.md)

## P777-12

- The emergency-exit light guard MUST be driven (`EVT_OH_EMER_EXIT_LIGHT_GUARD`, 0=closed/1=open; ARMED is the guard-closed position) — the old "opening the guard leaves the switch unusable" belief was wrong, and without the guard OFF/ON are unreachable. → [pmdg-777.md](../pmdg-777.md)

## P777-13

- The emergency-lights guard→switch 80 ms settle gap is load-bearing (the two writes use DIFFERENT transports — CDA vs TransmitClientEvent — and arrive out of order without it, forcing the pilot to pick the position twice) — never drop it to zero; and the sequence must stay SERIALIZED (`_emerLightsGate`, latest-selection-wins) and fired as a plain async local function, NEVER `Task.Run` (which would race `SimConnectManager.SendEvent`'s unlocked `eventIds` dictionary from a pool thread). → [pmdg-777.md](../pmdg-777.md)

## P777-14

- `RequestVariable(key, forceUpdate: true)` is a NO-OP for any `PMDGVar` (CDA-broadcast, so in neither the data-def nor the continuous-batch map) — never use it to snap a PMDG combo back; that trick only works for the L:var-typed controls (e.g. the temp-knob variant guards). → [pmdg-777.md](../pmdg-777.md)

## P777-15

- **Speed-brake ARM is EXACT on the PMDG 737, the PMDG 777 and the iFly 737 MAX** (`PmdgLeverDetent.Tolerance`, 0.25) — never widen it back into the table's settle band. Measured 2026-09-30 with hydraulics pressurised: one step past ARM the PMDG spoilers are already 34 percent up (737 `L:switch_679_73X` 101, 777 `L:switch_498_a` 201), and the iFly's deploy in step with the lever from 34, so the old bands announced "Speed brake armed" over deployed speed brakes; past ARM speaks a percentage, never "0 percent", and the Control Stand combo seeds the nearest DEPLOYED detent, never "Armed" (`PmdgSpeedBrakeLever.NearestDetentValue`). The 777 lever must be read from `L:switch_498_a` (0-400), NEVER the SDK's `FCTL_Speedbrake_Lever` byte — a quarter of it, TRUNCATED, so 201-203 read exactly 50. Both PMDG levers ride the 1 Hz L-var batch with a 1500 ms settle, which must outlast one batch now that a lever between detents is spoken. → [pmdg-777.md](../pmdg-777.md)
