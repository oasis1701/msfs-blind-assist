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

---

## PFD (Primary Flight Display) — DONE

Already present (kept through the doors/PFD revert): FMA modes/armed, autothrust,
approach capability, AP1/2, attitude/heading/IAS/altitude, PFD messages, GW + CG %MAC,
V1/VR/V2, Mach, Track, ILS freq/DME, and the FAC speed-protection bugs Vmax/VLS/
Vαprot/Vαmax/Vsw. **Additions this pass (all live-verified):**
- **Radio altitude** `A32NX_RA_1_RADIO_ALTITUDE` (ARINC429, feet) — live-verified Normal
  Operation. Highest-value missing number on short final.
- **Vertical speed** `A32NX_ADIRS_IR_1_VERTICAL_SPEED` — live testing revealed this is an
  ARINC429 word (the audit mis-called it "plain"); decoded ft/min, Normal Operation.
- **Transition altitude** `A32NX_FM1_TRANS_ALT` (ARINC429, feet; "not available" when unset).
- **Characteristic speed bugs** (FAC ARINC429, knots): green dot `_V_MAN`, F-speed `_V_3`,
  S-speed `_V_4`, VFE-next `_V_FE_NEXT`.
- **Marker beacon** `MARKER BEACON STATE` (stock enum → None/Outer/Middle/Inner).
- **ILS/LS course** `A32NX_FM_LS_COURSE` (plain L:var; -1 = no course set) — completes the
  ILS block alongside the existing freq/DME.

Used a new `ArincUnit(key,name,display,unit)` helper (RA/VS/trans-alt) mirroring the
existing `ArincKt`. All ARINC vars are OnRequest (no monitoring-batch / detection impact).

**Deferred (documented):** ILS ident (`NAV IDENT:3`, string simvar — numeric pipeline
can't read; via scrape), FCU selected ALT/HDG readouts (respecting "don't touch the FCU"),
transition level (needs FL formatting), LOC/GS deviation (already in ND + ISIS), DH (already
in Minimums), managed/preselect target speeds, FPV/FPA/drift, beta target, TCAS RA band.

Files: `Aircraft/FlyByWireA380Definition.cs` (registrations + d["PFD"] + LS-course decode).

---

## SD pages — DONE

The SD decode (`A380SdRows`) was already near-complete (pages 1/2/3/7/8/9/10/12 had no
gaps). Genuine additions this pass (live-verified):
- **PRESS page**: pressurization **AUTO/MAN mode** (`A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO`)
  — the page's prominent mode label.
- **ENGINE page**: per-engine **starter (cranking) valve** (`A32NX_PNEU_ENG_n_STARTER_VALVE_OPEN`)
  — open while motoring/starting; useful start context for a blind pilot.

Audit "bug fixes" that were **false positives** (verified, correctly NOT changed):
- *Pitch-trim* — the SD row's `ELEVATOR_TRIM` key is already registered to the stock
  `ELEVATOR TRIM POSITION` in **degrees** (def line ~1800); the audit only saw the key name.
- *Landing-elevation ARINC* — `A32NX_FM1_LANDING_ELEVATION` reads a plain `0.0` live (not the
  ~4.29e9 an ARINC NCD word shows), so the existing "not set (auto)" plain decode is correct.
- *EXT PWR available* — already surfaced as "GPU n Available" under Ground Services; not
  duplicated into the SD ELEC AC rows.

Files: `Aircraft/FlyByWireA380Definition.cs` (`A380SdRows` cases 0 + 4).

---

## EWD (Engine/Warning Display) — DONE

The EWD's numeric set is small and already well-covered: per-engine N1, **N1-command**
(`A32NX_AUTOTHRUST_N1_COMMANDED:n`), EGT, thrust-rating mode + FLEX, reverser-deployed
(eng 2/3), plus the live-scrape memos/warnings/status boxes. (N2/N3/FF are SD-ENG-page
data, not EWD, and are already exposed.) **Addition this pass (live-verified = 84.9%):**
- **Thrust limit N1 %** `A32NX_AUTOTHRUST_THRUST_LIMIT` — the green max-N1 the current
  thrust-rating mode allows (complements the already-announced limit TYPE).

**Deferred (documented):** the computed **THR %** gauge headline (derivable from N1 +
idle/max limits — N1 is already exposed), per-lever **TLA_N1** (≈ N1-command live, a
near-duplicate), reverser **deploying/selected** transitional states (deployed already
shown), the **BLEED supply** line (CPIOM AGS ARINC bits), and the **IDLE** memo. THR LK /
A.FLOOR are FG-EventBus-only → available via the EWD live scrape, not a SimVar.

Files: `Aircraft/FlyByWireA380Definition.cs` (autothrust readout + d["Thrust Levers"] + decode).
