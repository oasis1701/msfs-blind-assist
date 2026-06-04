# A380 MFD pages recon (live, on ground)

> **The page captures below are PRE-sweep (raw comma-soup).** They're kept as the raw
> material that drove the cleanup. See the sweep status next for the current behaviour.

## SWEEP STATUS (2026-06-04) — MFD pages tidy-up

Four shipped increments (commits 97e3c80 → bb87993), each live-verified + jsdom-tested
(`tools/perf-builder-test`, 8 tests green); selectability invariant preserved everywhere
(every input/combo/radio/subtab/button/surv keeps its stamped idx):

1. **buildLabeledFields** (all pages) — one clean `LABEL: value unit` per
   `.mfd-label-value-container` with an inner label (GW/CG/FOB, EPU, GPIRS POSITION, …)
   + the label-colon join (`ACTIVE ATC: XXXX`, `IRS ALIGNED ON GPS POS: …`).
2. **Label-aware Y-merge composer** — sibling label/value/unit data rows compose as
   `T.TRK: 134.4 °T, T.HDG: 134.4 °T`, `GND SPD: 0.0 KT, MAG HDG: 122.9 °`, `IRS 1: NAV`,
   `IRS1: 0.0 NM`, `WAYPOINTS: 00, ROUTES: 00`, `NAV DATABASE: MS26050001`,
   `TRIP: 108.7 KLB, 04:32`. Number/time cells can't act as labels (real-label guard).
   + D-ATIS dropdown uses spacedText.
3. **buildSurvControls** — SURV CONTROLS radios/toggles header-prefixed by column:
   `XPDR: AUTO`, `TCAS: TA/RA`, `WXR: AUTO`, `MODE: WX`, `TERR SYS: OFF: ON`,
   `ELEVN/TILT: AUTO`. (TCAS ABV/BLW/NORM range column has no DOM header → bare.)
4. **STATUS & SWITCHING** — `.mfd-surv-status-item` cells never row-merge; each on its
   own line, no false heading-bind.

Per-page state now:
- CLEAN: ATC CONNECT / MSG RECORD, DATA/AIRPORT, SEC INDEX, INIT, IRS, MONITOR,
  SURV CONTROLS, STATUS & SWITCHING, PERF (T.O/CLB/CRZ/DES/APPR/GA), F-PLN (per-wpt).
- MOSTLY CLEAN: DATA/STATUS (a few neutral column-header rows: IDLE/PERF, ACTIVE/SECOND,
  the DB-cycle dates); FUEL&LOAD (top fields clean; the DEST/ALTN read-only prediction
  GRID still reads as comma rows); NAVAIDS (CLASS / SLOPE read as header then value).
- LEFT AS-IS (low value / FBW-WIP): ACCURACY drops its split "FT" unit span (value
  still reads); PERF composite residuals (CLB SPD LIM, DEST);
  ATC REQUEST/REPORT/EMER + DATA WAYPOINT/NAVAID/ROUTE/PRINTER + POSITION REPORT/GNSS/TIME
  (unreachable / "ERROR 404" in the FBW dev build).

Follow-up increments (commits 3c90987 → d917b94), live-verified on the ground (KLAX→KJFK):
5. **D-ATIS** (once populated): function selector reads "UPDATE OR PRINT" (was absorbing
   the whole ATIS report via comboSelectedValue + losing spacing); ATIS reads once.
6. **F-PLN**: dropped the stray "at * feet" managed-constraint marker (real constraints
   decode: "at or above 640 feet"); **STEP ALTs** WPT/ALT → "Step waypoint"/"Step
   altitude" (scoped to that subtab); leading "FL" binds its level ("FROM CRZ: FL 390").

F-PLN sub-pages + per-page sub-tabs SWEPT (all read acceptably; no further code needed):
- LAT REV menu (DIR TO/DEPARTURE/ARRIVAL/HOLD/AIRWAYS/STEP ALTs/CONSTRAINTS, "(N/A)"
  disabled) — clean under its opener.
- DEPARTURE (RWY/SID/TRANS show selection + summary), ARRIVAL (same widget/path),
  HOLD (INBOUND CRS / TURN / TIME-DIST / LEG PARAM), AIRWAYS (FROM / VIA / TO),
  CONSTRAINTS=VERT REV/ALT (ALT CSTR AT + AT/ABOVE/BELOW), VERT REV/SPD (SPD CSTR +
  CLB SPD LIMIT). NOTE: HOLD/AIRWAYS/DEPARTURE-ARRIVAL-selection/SPD create a TMPY on
  interaction — erase after (ERASE TMPY on F-PLN).
- Sub-tabs: DATA→FMS P/N (empty), NAVAIDS→SELECTED FOR FMS NAV (navaid selectors +
  RADIO NAV MODE/POSITION; minor: a SLOPE value's "°" orphans to the next row),
  DATA/AIRPORT→PILOT STORED RWYs (empty).

---

## atccom_connect  ->  CONNECT / CONNECT  (14 els)
- text | CONNECT
- input | NOTIFY TO ATC: ---- (combobox)
- button | NOTIFY
- text | ACTIVE ATC :, XXXX
- button | DISCONNECT ALL
- text | NEXT ATC :, XXXX
- button | MODIFY MAX UPLINK DELAY
- text | ADS-C, ADS-C EMERGENCY
- text | NOT YET IMPLEMENTED
- adsc | ADS-C OFF, toggle, other option ARMED
- adsc | ADS-C EMERGENCY OFF, toggle, other option ARMED
- text | ADS-C CONNECTED GROUND STATIONS :, NONE
- button | CLEAR INFO
- text | NAV PRIMARY

## atccom_datis_list  ->  D-ATIS / D-ATIS/LIST  (12 els)
- dropdown | UPDATEOR PRINT, AUTOUPDATE
- input | D-ATIS/LIST: KLAX
- input | DEP (combobox)
- dropdown | UPDATEOR PRINT, AUTOUPDATE
- input | KJFK
- input | ARR (combobox)
- input | KPHL
- dropdown | UPDATEOR PRINT, AUTOUPDATE
- input | ARR (combobox)
- button | PRINT ALL
- button | UPDATE ALL
- button | CLEAR INFO

## atccom_emer  ->  D-ATIS / D-ATIS/LIST  (12 els)
- dropdown | UPDATEOR PRINT, AUTOUPDATE
- input | D-ATIS/LIST: KLAX
- input | DEP (combobox)
- dropdown | UPDATEOR PRINT, AUTOUPDATE
- input | KJFK
- input | ARR (combobox)
- input | KPHL
- dropdown | UPDATEOR PRINT, AUTOUPDATE
- input | ARR (combobox)
- button | PRINT ALL
- button | UPDATE ALL
- button | CLEAR INFO

## atccom_msgrecord  ->  MSG RECORD / MSG RECORD  (5 els)
- text | MSG RECORD
- button | ALL MSG
- button | MONITORED MSG
- button | CLEAR INFO
- text | NAV PRIMARY

## atccom_reportmodify_position  ->  MSG RECORD / MSG RECORD  (5 els)
- text | MSG RECORD
- button | ALL MSG
- button | MONITORED MSG
- button | CLEAR INFO
- text | NAV PRIMARY

## atccom_request  ->  MSG RECORD / MSG RECORD  (5 els)
- text | MSG RECORD
- button | ALL MSG
- button | MONITORED MSG
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_active_fuelload  ->  ACTIVE / ACTIVE/FUEL&LOAD  (29 els)
- text | ACTIVE/FUEL&LOAD
- text | GW, ---.-, KLB, CG, --.-, %, FOB, ---.-, KLB
- input | ZFW: 765.3 KLB
- input | ZFWCG: 35.2 %
- button | FUEL PLANNING *
- input | BLOCK: 141.2 KLB
- input | TAXI: 3.3 KLB
- input | PAX NBR: 453
- input | ECON (combobox)
- text | TRIP, 108.7, KLB, 04:32
- input | CI: 200
- input | RTE RSV: 5.4 KLB
- input | MODE: 5.0 %
- input | JTSN GW: ---.- KLB
- input | ALTN: 14.3 KLB
- text | --:--
- text | 903.2, KLB
- input | FINAL: 13.2 KLB
- input | TOW: 00:30
- text | LW, 794.5, KLB
- text | TIME, EFOB
- input | MIN FUEL AT DEST: 27.6 KLB
- text | DEST, KJFK, 04:32, 32.5, KLB
- text | 0.0
- text | ALTN, KPHL, --:--, KLB, EXTRA
- text | -3.8, KLB, --:--
- button | RETURN
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_active_init  ->  ACTIVE / ACTIVE/INIT  (28 els)
- text | ACTIVE/INIT
- button | CPNY F-PLN REQUEST
- input | FLT NBR: BVI2GP
- button | ACFT STATUS
- input | FROM: KLAX
- input | TO: KJFK
- input | ALTN: KPHL
- input | CPNY RTE: NONE
- button | RTE SEL
- input | ALTN RTE: NONE
- button | ALTN RTE SEL
- input | CRZ FL: FL 390
- input | CRZ TEMP: -56 °C
- input | MODE: ECON (combobox)
- input | TROPO: 44790 FT
- input | CI: 200
- button | CPNY WIND REQUEST
- input | TRIP WIND: TL 023
- button | WIND
- button | IRS
- button | DEPARTURE
- button | RTE SUMMARY
- button | NAVAIDS
- button | FUEL&LOAD
- button | T.O. PERF
- button | CPNY T.O. REQUEST
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_data_airport  ->  DATA / DATA/AIRPORT  (7 els)
- text | DATA/AIRPORT
- subtab | DATABASE ARPTs (active)
- subtab | PILOT STORED RWYs
- input | ARPT IDENT: blank
- button | RETURN
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_data_navaid  ->  DATA / DATA/AIRPORT  (7 els)
- text | DATA/AIRPORT
- subtab | DATABASE ARPTs (active)
- subtab | PILOT STORED RWYs
- input | ARPT IDENT: blank
- button | RETURN
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_data_printer  ->  DATA / DATA/AIRPORT  (7 els)
- text | DATA/AIRPORT
- subtab | DATABASE ARPTs (active)
- subtab | PILOT STORED RWYs
- input | ARPT IDENT: blank
- button | RETURN
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_data_route  ->  DATA / DATA/AIRPORT  (7 els)
- text | DATA/AIRPORT
- subtab | DATABASE ARPTs (active)
- subtab | PILOT STORED RWYs
- input | ARPT IDENT: blank
- button | RETURN
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_data_status  ->  DATA / DATA/STATUS  (18 els)
- text | DATA/STATUS
- subtab | ACFT STATUS (active)
- subtab | FMS P/N
- text | A380-800 / TRENT 972
- text | +0.0, +0.0
- button | MODIFY
- text | IDLE, PERF
- input | FUEL PENALTY: +000.0 %
- text | NAV DATABASE, MS26050001
- text | ACTIVE, SECOND
- button | SWAP *
- text | 14MAY-11JUN, 14MAY-11JUN
- text | PILOT STORED ELEMENTS
- text | WAYPOINTS, 00, ROUTES, 00
- button | DELETE ALL *
- text | NAVAIDS, 00, RUNWAYS, 00
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_data_waypoint  ->  DATA / DATA/STATUS  (18 els)
- text | DATA/STATUS
- subtab | ACFT STATUS (active)
- subtab | FMS P/N
- text | A380-800 / TRENT 972
- text | +0.0, +0.0
- button | MODIFY
- text | IDLE, PERF
- input | FUEL PENALTY: +000.0 %
- text | NAV DATABASE, MS26050001
- text | ACTIVE, SECOND
- button | SWAP *
- text | 14MAY-11JUN, 14MAY-11JUN
- text | PILOT STORED ELEMENTS
- text | WAYPOINTS, 00, ROUTES, 00
- button | DELETE ALL *
- text | NAVAIDS, 00, RUNWAYS, 00
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_position_gnss  ->  DATA / DATA/STATUS  (18 els)
- text | DATA/STATUS
- subtab | ACFT STATUS (active)
- subtab | FMS P/N
- text | A380-800 / TRENT 972
- text | +0.0, +0.0
- button | MODIFY
- text | IDLE, PERF
- input | FUEL PENALTY: +000.0 %
- text | NAV DATABASE, MS26050001
- text | ACTIVE, SECOND
- button | SWAP *
- text | 14MAY-11JUN, 14MAY-11JUN
- text | PILOT STORED ELEMENTS
- text | WAYPOINTS, 00, ROUTES, 00
- button | DELETE ALL *
- text | NAVAIDS, 00, RUNWAYS, 00
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_position_irs  ->  POSITION / POSITION/IRS  (21 els)
- text | POSITION/IRS
- text | IRS ALIGNED ON GPS POS:, 33°56.5N/118°22.2W
- button | ALIGN ON OTHER REF
- text | IRS 1, NAV
- text | IRS 2, NAV
- text | IRS 3, NAV
- button | IRS1
- button | IRS2
- button | IRS3
- button | FREEZE ALL IRS
- text | 33°56.5N/118°22.2W
- text | POSITION
- text | T.TRK, 134.4, °T, T.HDG, 134.4, °T
- text | GND SPD, 0.0, KT, MAG HDG, 122.9, °
- text | 0.0, /0.0, 11.5
- text | T.WIND, °, KT, MAG VAR, °W
- text | GPIRS POSITION, 33°56.5N/118°22.2W
- text | ACCURACY, 0, FT
- button | RETURN
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_position_monitor  ->  POSITION / POSITION/MONITOR  (22 els)
- text | POSITION/MONITOR
- text | NAV PRIMARY
- input | RNP: 1.00 NM
- text | EPU, 0.04, NM
- text | POS1, 33°56.5N/118°22.2W
- text | 33°56.5N/118°22.2W
- text | POS2
- text | DEVIATION FROM POS1
- text | IRS1, 0.0, NM
- text | IRS2, 0.0, NM
- text | IRS3, 0.0, NM
- button | DISPLAY POS SENSORS
- button | FREEZE POS DATA *
- input | BRG / DIST TO: -------
- button | POSITION UPDATE
- text | /
- text | ---, ----.-
- button | NAVAIDS
- button | GNSS
- button | IRS
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_position_navaids  ->  POSITION / POSITION/NAVAIDS  (23 els)
- text | POSITION/NAVAIDS
- subtab | TUNED FOR DISPLAY (active)
- subtab | SELECTED FOR FMS NAV
- text | VOR2
- input | VOR1: LAX
- input | IDENT: LAX
- input | 113.60
- input | FREQ: 113.60
- input | --- °
- input | CRS: --- °
- text | CLASS
- text | VOR/DME, VOR/DME
- text | LS
- input | IDENT: IHQB
- input | FREQ/CHAN: 111.70
- button | DESELECT GLIDE *
- input | CRS: F251 °
- text | SLOPE
- text | -3.0, °
- text | CLASS
- text | ILS/DME
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_position_report  ->  POSITION / POSITION/NAVAIDS  (23 els)
- text | POSITION/NAVAIDS
- subtab | TUNED FOR DISPLAY (active)
- subtab | SELECTED FOR FMS NAV
- text | VOR2
- input | VOR1: LAX
- input | IDENT: LAX
- input | 113.60
- input | FREQ: 113.60
- input | --- °
- input | CRS: --- °
- text | CLASS
- text | VOR/DME, VOR/DME
- text | LS
- input | IDENT: IHQB
- input | FREQ/CHAN: 111.70
- button | DESELECT GLIDE *
- input | CRS: F251 °
- text | SLOPE
- text | -3.0, °
- text | CLASS
- text | ILS/DME
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_position_time  ->  POSITION / POSITION/NAVAIDS  (23 els)
- text | POSITION/NAVAIDS
- subtab | TUNED FOR DISPLAY (active)
- subtab | SELECTED FOR FMS NAV
- text | VOR2
- input | VOR1: LAX
- input | IDENT: LAX
- input | 113.60
- input | FREQ: 113.60
- input | --- °
- input | CRS: --- °
- text | CLASS
- text | VOR/DME, VOR/DME
- text | LS
- input | IDENT: IHQB
- input | FREQ/CHAN: 111.70
- button | DESELECT GLIDE *
- input | CRS: F251 °
- text | SLOPE
- text | -3.0, °
- text | CLASS
- text | ILS/DME
- button | CLEAR INFO
- text | NAV PRIMARY

## fms_sec_index  ->  SEC INDEX / SEC/SEC INDEX  (17 els)
- text | SEC/SEC INDEX
- subtab | SEC 1 (active)
- subtab | SEC 2
- subtab | SEC 3
- dropdown | IMPORT
- button | CPNY F-PLN REQUEST
- button | F-PLN
- button | PERF
- button | WIND
- button | FUEL&LOAD
- button | INIT
- button | WHAT IF
- button | DELETE *
- button | XFR TO MAILBOX
- button | PRINT *
- button | CLEAR INFO
- text | NAV PRIMARY

## surv_controls  ->  CONTROLS / CONTROLS  (32 els)
- text | CONTROLS
- text | XPDR, TCAS
- input | SQWK: 2000
- radio | TA/RA
- radio | NORM (selected)
- radio | AUTO
- button | IDENT
- radio | TA ONLY
- radio | ABV
- radio | STBY (selected)
- radio | BLW
- text | ALT RPTG
- radio | STBY (selected)
- surv | ON
- text | WXR
- text | WXR, PRED W/S, TURB
- surv | AUTO
- text | ELEVN/TILT
- radio | AUTO (selected)
- radio | ELEVN
- text | GAIN, MODE, WX ON VD
- surv | AUTO
- surv | WX
- surv | AUTO
- radio | TILT
- text | TAWS
- text | TERR SYS, G/S MODE, FLAP MODE
- text | GPWS, SURV
- surv | OFF: ON
- button | DEFAULT SETTINGS
- button | CLEAR INFO
- text | NAV PRIMARY

## surv_statusswitching  ->  STATUS & SWITCHING / STATUS & SWITCHING  (15 els)
- text | STATUS & SWITCHING
- survstatus | SYS 1
- survstatus | SYS 2
- text | WXR
- text | WXR DISPLAY 1, WXR DISPLAY OFF
- text | TURB 1, TURB OFF
- text | PRED W/S 1, PRED W/S OFF
- text | TERR SYS 1, TAWS, TERR SYS OFF
- text | GPWS 1, GPWS OFF
- survstatus | SYS 1
- survstatus | SYS 2
- text | XPDR 1, XPDR, XPDR OFF
- text | TCAS 1, TCAS, TCAS OFF
- button | CLEAR INFO
- text | NAV PRIMARY

