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

---

## ND (Navigation Display) — DONE

**Bug fix:** ND range readout was wrong for the A380. `A32NX_EFIS_L_ND_RANGE` is an
INDEX into the A380 array `[-1(ZOOM),10,20,40,80,160,320,640]`, but the form used the
A320's 6-entry `10..320` array — so every range read one step high and 640/ZOOM were
impossible. Fixed to the 8-entry A380 array (index 0 = ZOOM).

**Additions** (all live-verified against the running A380X):
- **Ground speed** `A32NX_ADIRS_IR_1_GROUND_SPEED`, **True airspeed**
  `A32NX_ADIRS_ADR_1_TRUE_AIRSPEED`, **Wind** dir/speed
  `A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR` / `_WIND_SPEED_BNR` — all ARINC429 BNR words
  decoded via `Arinc429Word`; render "---" when not in Normal Operation (e.g. TAS/wind
  are No-Computed-Data on the ground).
- **TRUE REF awareness** `A32NX_FMGC_TRUE_REF` — shows "Reference: TRUE" and switches the
  TO-WPT bearing to `A32NX_EFIS_L_TO_WPT_TRUE_BEARING` (degrees) instead of the magnetic
  bearing when true-north reference is selected.
- **Approach capability** (e.g. "CAT3 DUAL") from `A32NX_EFIS_L_APPR_MSG_0/_1` (packed
  6-bit, decoded with the existing ident unpacker).

**Deferred (string-simvar limitation, documented):** the VOR1/2 + ADF1/2 nav-source
block needs `NAV IDENT:n` / `ADF IDENT:n` (string simvars) which MSFSBA's numeric
SimVar pipeline can't read; these idents remain available via the ND **F6 live scrape**.
TCAS message state and ROSE-VOR/ROSE-ILS tuned-station blocks also deferred (mode-specific,
lower value). RWY AHEAD already covered by ROW/ROP auto-announce.

Files: `Forms/FBWA380/FBWA380NavDisplayForm.cs` (NdRanges array + Vars + Render).
