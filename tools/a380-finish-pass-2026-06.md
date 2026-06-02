# A380 "finish-line" pass — 2026-06 (overnight autonomous)

Goal: make the FBW A380X a finished product — every display complete, every panel
audited, high/low-value controls added, all live-verified, code cleaned/hardened.
Doors are intentionally EXCLUDED this pass (noted as a possible A320-parity task).
Each item below was: implemented → built (0 errors) → every new SimVar/L:var
live-verified via SimConnect MCP against a running A380X → committed (NOT pushed).

Method: 5 parallel source-grounded audit agents (one per display) produced the gap
lists; a panel-by-panel agent sweep produced the control gaps. All var names cited
back to FBW A380 source (`fbw-aircraft/fbw-a380x/src/systems/instruments/src`).

---

## ISIS (standby instruments) — DONE

**Correctness fixes** (the form was reading the PFD/baro air-data source, not the
standby ADM the real ISIS uses):
- Standby **altitude** now reads `INDICATED ALTITUDE:3` (was the non-indexed PFD source).
- Standby **baro** now reads `KOHLSMAN SETTING MB:3`, shown in **both hPa and inHg**
  (the real ISIS shows the inHg pair). Registered with explicit units (millibars + inHg)
  because the generic auto-register forces "number" (which returns Pascals for Kohlsman).

**Additions** (high value, all live-verified):
- **Slip/skid ball** — `ACCELERATION BODY X` (G), clamped ±0.3 G → "% left/right / centred".
- **LS overlay deviations** — when `A32NX_ISIS_LS_ACTIVE`, surface LOC + G/S deviation in
  dots (÷0.4, full-scale 2 dots) gated by `A32NX_RADIO_RECEIVER_{LOC,GS}_IS_VALID`. Makes
  the existing "LS overlay: On" line actionable on a standby approach.

**Deferred (low value for a blind pilot, noted not done):** ISIS pilot-set SPD/ALT
reference bugs (`A32NX_ISIS_BUGS_*`, verified readable), metric-altitude / inHg-unit
persistent-property flags, and the display-unit power/self-test state.

Files: `Aircraft/FlyByWireA380Definition.cs` (ISIS Stock registrations),
`Forms/FBWA380/FBWA380ISISForm.cs` (Vars + Render + DotsPhrase helper).
