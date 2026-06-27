# A380 MFD live captures — STEP ALTs (Vertical Revision) + PERF (all tabs)

Captured live in flight (cruise FL380, VCBI→KLAX, ILS24R) via coherent-eval.ps1 on
the `A380X_MFD` Coherent view, Captain side (mcdu 1). For each page:
- **emitted** = what OUR agent (`coherent-a380-agent.js` `A.scrape()` → `enumerateLines`/`buildFplnLines`)
  produces, i.e. what the screen reader hears in `FBWA380MCDUForm`. Format: `kind|text`.
- **rawText** = the page root's raw `innerText` (the true on-screen layout, newline-separated),
  so you can see the real column structure the FBW page renders.

FO side (mcdu 2) confirmed INDEPENDENT (it was on DATA/STATUS while Capt was on PERF) — same
rendering + enumeration code path, so any fix applies to both sides automatically.

---

## STEP ALTs page — empty (ACTIVE/F-PLN/VERT REV, STEP ALTs subtab)
Reached by: F-PLN → click a waypoint → revision menu → **STEP ALTs**.

emitted:
- subtab|RTA
- subtab|SPD
- subtab|CMS
- subtab|ALT
- subtab|STEP ALTs (active)
- text|STEP ALTs FROM CRZ, FL, 380
- text|DIST, UTC
- input|WPT: ------- (combobox)
- input|ALT: -----
- text|OPTIMUM STEP POINT
- text|TO, FL, ---
- text|NO OPTIMUM STEP FOUND
- button|RETURN
- button|MSG LIST

rawText:
```
ACTIVE/F-PLN/VERT REV
RTA  SPD  CMS  ALT  STEP ALTs
STEP ALTs FROM CRZFL380
WPT
ALT
DIST
UTC
-------
-----
FT
OPTIMUM STEP POINT
TO
FL
---
NO OPTIMUM STEP FOUND
RETURN  MSG LIST
```

## STEP ALTs page — WPT combobox OPENED (the "select a waypoint" interaction)
emitted (the dropdown options render as `menu` kind = role option; downstream waypoints):
- input|WPT (combobox, now open)
- menu|MCI, menu|JUDGE, menu|SLN, menu|RYLIE, menu|LAA, menu|ALS, menu|RSK, menu|COCAN,
  menu|RHYSS, menu|TBC, menu|JASSE, menu|AREAF, menu|ESGEE, menu|DNERO, menu|SLLRS, menu|FLOJO,
  menu|SALYY, menu|GLESN, menu|ANJLL, menu|CAANN, menu|BOYEL, menu|CRCUS, menu|SKOLL, menu|DECOR,
  menu|BREEA, menu|PALAC, menu|LIVVN, menu|BROUK, menu|MERCE, menu|KOBEE, menu|KLAX24R, menu|RAFFS
- input|ALT: -----
- text|OPTIMUM STEP POINT / TO / FL --- / NO OPTIMUM STEP FOUND
- button|RETURN, button|MSG LIST

rawText (tail): `... WPT [list of all downstream wpts] ----- FT OPTIMUM STEP POINT TO FL --- NO OPTIMUM STEP FOUND ...`

NOTE: a step altitude was deliberately NOT committed to the live plan. The WPT combo selects
the waypoint where the step occurs; ALT is the target altitude (FT) after the step; DIST/UTC are
predicted outputs to that step point; OPTIMUM STEP POINT is the FMS-recommended step.

---

## PERF — CLB tab (ACTIVE/PERF, CLB active)
emitted:
- input|CRZ: 380
- text|OPT, FL, 405, REC MAX, FL, 425
- text|EO MAX, FL, 340
- subtab|T.O, subtab|CLB (active), subtab|CRZ, subtab|DES, subtab|APPR, subtab|GA
- input|ECON (combobox)
- input|CI: 15
- input|DERATED CLB: NONE (combobox)
- text|PRED TO
- text|MODE, SPD
- input|MACH: 380
- text|MANAGED, 290, KT, .84
- input|THR RED: 1510
- input|ACCEL: 1510
- text|CLB SPD LIM, 250, KT, /, FL, 100
- input|TRANS: 18000
- button|ACTIVATE APPR *, button|RETURN, button|POS MONITOR, button|MSG LIST

rawText:
```
ACTIVE/PERF
CRZ FL 380   OPT FL 405   REC MAX FL 425   EO MAX FL 340
T.O CLB CRZ DES APPR GA
ECON   CI 15   DERATED CLB NONE
MODE | SPD            PRED TO
MACH                  FL 380
MANAGED 290 KT .84    null null
THR RED 1510 FT
ACCEL 1510 FT
CLB SPD LIM 250 KT / FL 100
TRANS 18000 FT
RETURN  ACTIVATE APPR *  POS MONITOR  MSG LIST
```
BUG: `input|MACH: 380` — 380 is actually the **PRED TO FL** (right column), not a MACH entry value.
Units **FT** dropped from THR RED / ACCEL / TRANS (raw has FT).

## PERF — T.O tab
emitted:
- input|CRZ: 380; text|OPT, FL, 405, REC MAX, FL, 425; text|EO MAX, FL, 340
- (subtabs … T.O active)
- input|T.O SHIFT: ----
- text|RWY, 04L
- input|V1: 139
- text|F, 156, KT
- radio|TOGA
- input|VR: 144
- radio|FLEX (selected)
- input|+65 °C
- text|S, 183, KT
- input|V2: 150
- text|246, KT
- radio|DERATED
- input|FLAPS: 1 (combobox)
- input|THS FOR: 40.3
- input|PACKS: ON (combobox)
- input|ANTI ICE: OFF (combobox)
- input|THR RED: 1510
- input|ACCEL: 1510
- input|TRANS: 18000
- input|EO ACCEL: 1510
- button|ACTIVATE APPR *, RETURN, POS MONITOR, MSG LIST

rawText:
```
RWY 04L
T.O SHIFT ---- FT
V1 139 KT
F 156 KT
VR 144 KT
S 183 KT
V2 150 KT
246 KT            (green-dot / O speed — UNLABELLED in emission)
TOGA  FLEX  DERATED   +65 °C
FLAPS  THS FOR  PACKS  ANTI ICE
1  40.3 %  ON  OFF
THR RED 1510 FT  ACCEL 1510 FT  TRANS 18000 FT  EO ACCEL 1510 FT
```
NOTE: `246, KT` is unlabelled (green-dot/O). FLEX temp `+65 °C` has its unit (good). FT dropped on inputs.

## PERF — CRZ tab
emitted:
- input|CRZ: 380; text|OPT, FL, 405, REC MAX, FL, 425; text|EO MAX, FL, 340
- input|ECON (combobox); input|CI: 15
- text|MODE, MACH, PRED TO, T/D
- input|PRESEL: .--
- input|SPD: ---
- text|MANAGED, .85, ---, KT, --:-- ----, NM
- text|LRC, .84, ---
- text|MAX TURB, .85, ---
- button|CMS, button|STEP ALTs
- text|DEST, KLAX, 19:54, 29.6, KLB
- button|ACTIVATE APPR *, RETURN, POS MONITOR, MSG LIST

rawText:
```
ECON  CI 15
MODE | MACH    SPD     PRED TO     T/D
PRESEL .--  --- KT
MANAGED .85 --- KT  --:-- ----  NM   null null null
LRC .84 ---
MAX TURB .85 ---
DEST KLAX 19:54 29.6 KLB
CMS  STEP ALTs
```
NOTE: STEP ALTs also reachable here (button). DEST prediction (ETA 19:54, 29.6 KLB fuel) comma-merged.

## PERF — DES tab
emitted:
- input|CRZ: 380; text|OPT…; text|EO MAX…
- input|ECON (combobox); input|CI: 15
- input|DES CABIN RATE: -350
- text|PRED TO
- text|MODE, MACH
- input|SPD: -----
- input|MANAGED: .84
- input|288 KT
- text|--:-- ----
- button|SPD CSTR
- text|DEST, KLAX, 19:54, 29.6, KLB
- button|ACTIVATE APPR *, RETURN, POS MONITOR, MSG LIST

rawText:
```
ECON  CI 15
DES CABIN RATE -350 FT/MN
MODE | MACH    SPD     PRED TO
MANAGED .84  288 KT   ----- FT
--:-- ----   null null
DEST KLAX 19:54 29.6 KLB
SPD CSTR
```
BUG: `DES CABIN RATE: -350` drops **FT/MN**. `input|288 KT` is the managed-descent SPD value rendered as an input.

## PERF — APPR tab
emitted:
- input|CRZ: 380; text|OPT…; text|EO MAX…
- text|ILS24R, KLAX, 800.3
- text|APPR, LW, KLB
- input|MAG WIND: ---
- input|--- KT
- text|194, KT
- text|S, 177, KT
- text|HD, ---, KT, CROSS, ---, KT, 143
- text|F, KT
- text|VREF, 131, KT
- input|OAT: blank
- input|QNH: blank
- radio|CONF 3
- radio|FULL (selected)
- text|MINIMUM
- text|VLS, 131, KT
- input|BARO: -----
- input|VAPP: 136
- input|RADIO: -----
- input|TRANS: 180
- text|VERT DEV, +-----
- button|ACTIVATE APPR *, RETURN, POS MONITOR, MSG LIST

rawText:
```
APPR
ILS24R KLAX  LW 800.3 KLB
MAG WIND --- ° --- KT
HD --- KT  CROSS --- KT
OAT ▯▯▯ °C   QNH ▯▯▯▯
MINIMUM
BARO ----- FT   RADIO ----- FT
194 KT
S 177 KT
F 143 KT
VREF 131 KT
CONF 3  FULL
VLS 131 KT  VAPP 136 KT
TRANS FL 180
VERT DEV +-----
```
BUG (clear): the **F-speed value 143** got misassociated into the wind line
(`HD, ---, KT, CROSS, ---, KT, 143`), leaving `F, KT` with no value (raw clearly has `F 143 KT`).
BUG: `ILS24R, KLAX, 800.3` + `APPR, LW, KLB` fragments the landing-weight `LW 800.3 KLB`.
BUG: `TRANS: 180` drops `FL` (raw: TRANS FL 180). `194, KT` unlabelled (green-dot/O).

## PERF — GA tab
emitted:
- input|CRZ: 380; text|OPT…; text|EO MAX…
- text|F, 143, KT
- text|S, 177, KT
- text|194, KT
- input|THR RED: 1590
- input|ACCEL: 1590
- input|EO ACCEL: 1590
- text|TRANS, 18000, FT
- button|ACTIVATE APPR *, RETURN, POS MONITOR, MSG LIST

rawText:
```
F 143 KT
S 177 KT
194 KT             (green-dot/O — unlabelled)
THR RED 1590 FT  ACCEL 1590 FT  EO ACCEL 1590 FT
TRANS 18000 FT
```
NOTE: here `TRANS 18000 FT` is a TEXT line and keeps FT; on CLB/T.O the SAME field is an INPUT and
DROPS FT. So the unit-dropping is tied to input-cell rendering, not the field.

---

## Cross-cutting observations (apply to BOTH STEP ALTs and PERF, and likely all grid pages)
1. **Units dropped on input cells**: input fields render `LABEL: value` but omit the trailing unit
   span (FT / KT / °C / FT/MN) that sits in an adjacent cell. Text cells sometimes keep the unit.
2. **Comma-merged multi-field rows**: the generic row-merge joins all same-row static cells with
   ", ", producing "OPT, FL, 405, REC MAX, FL, 425" / "MANAGED, 290, KT, .84" / "DEST, KLAX, 19:54,
   29.6, KLB" — multiple logical fields fused into one comma-soup line.
3. **Column misassociation**: where a page has TWO side-by-side column blocks at the same Y, values
   from the right block leak into the left (CLB `MACH:380`←PRED TO FL; APPR `143`←F-speed into wind line).
4. **Unlabelled green-dot/O speed**: `246, KT` / `194, KT` render with no label.
5. **Headers split from data**: `MODE, SPD` / `PRED TO` / `DIST, UTC` emitted as standalone header
   lines, disconnected from the value rows they head.
