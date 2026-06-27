# A380 MFD — STEP ALTs + PERF accessibility: findings & plan

Live in-flight investigation (cruise FL380, VCBI→KLAX). Raw captures:
`tools/_probe/captures/mcdu_captures.md`. Probes: `tools/_probe/_perf_dom*.js`,
`_mach_probe.js`, `_capture.js`.

## STATUS (2026-06-04): PERF builder IMPLEMENTED + tested
`A.buildPerfLines` + `A.isPerfPage` + `A.insidePerf` are in `coherent-a380-agent.js`,
gated like `buildFplnLines` (4 wiring points in `enumerateLines`). Built LIVE on the A380
(on the ground, active-field state) and verified across all 6 tabs, plus the cruise/inactive
state validated offline against `tools/perf-builder-test/fixtures/perf_clb.html`. Fixed:
- comma-soup for labeled fields → one clean "LABEL: value unit" line (OPT/REC MAX/EO MAX,
  F/S/VREF/VLS/VAPP/VLS, HD/CROSS, LW, etc.)
- **APPR F-speed no longer fused into the wind line** (each is its own container)
- **green-dot speed labeled** ("green dot: 208 KT" — was a bare value)
- **speed-prediction grids reconstructed** (CLB/CRZ/DES: "ECON: SPD 292 KT, MACH .84",
  "LRC: MACH .84") via `br`-row + column-header pairing
- **PRED-TO no longer mislabeled "MACH: FL 380"** (owning the grid removed the bad pairing)
- `null` grid rows suppressed; the APPR approach-summary composite left to the generic pass
  (so the ILS ident isn't lost)
Selectability preserved (every input/combo/radio/subtab/button keeps its stamped idx);
F-PLN unaffected (gated). Regression tests: `tools/perf-builder-test/perf.test.js`
(`cd tools/perf-builder-test && npm test`), 5 tests green.

**Remaining minor edges (readability, not wrong values):** the CLB SPD LIM and DEST
composites (label + label-less value cells) stay as readable comma-soup; the unlabeled
PRESEL / managed-speed input fields read as bare values; a stray RADIO-minimums "-----" on
APPR. **STEP ALTs clarity** (relabel WPT→"Step waypoint", ALT→"Step altitude", drop the
orphan "DIST, UTC" header) is NOT done — the units fix already added "FT" to ALT; the
relabel is a small follow-up.

## STEP ALTs (Vertical Revision) — what the edit boxes do
Reached: F-PLN → click a waypoint → revision menu → **STEP ALTs**.
- **WPT** (combobox) — the waypoint where the step climb/descent occurs (dropdown of
  downstream waypoints). Source: `MfdFmsFplnVertRev.tsx`.
- **ALT** (input) — the target altitude *after* the step (FT/FL).
- A step is created only when **both** WPT and ALT are set; selecting a WPT alone does
  nothing (`tryAddCruiseStep` requires both).
- **DIST / UTC** — computed predictions (distance + ETA to the step point), shown once a
  step is defined. Read-only.
- **STEP ALTs FROM CRZ FL** — the cruise level the steps are measured from. Read-only.
- **OPTIMUM STEP POINT / TO FL / NO OPTIMUM STEP FOUND** — FMS recommendation. Read-only.

## What was FIXED + verified live (shipped)
**Input unit drop** — `associateInputLabels` built `LABEL: value` but never appended the
field's own unit (a sibling span), so labeled inputs lost FT/KT/°C/FT-MN, and leading
units (FL) were never read at all. Fix (`coherent-a380-agent.js`): added `inputLeadUnit`,
threaded `leadUnit` onto each input item, and emit `lead + value + trail` in both the
labeled and no-label branches. Live-verified: `THR RED: 1510 FT`, `CRZ: FL 380`,
`TRANS: 18000 FT`; STEP ALTs `ALT: ----- FT` via the same path. Applies to both PERF and
STEP ALTs, both Capt/FO. No regression (units were being dropped; nothing else changes).

## What still needs a STRUCTURE-AWARE builder (NOT yet shipped — see below)
Diagnosed + designed, but reverted after live regressions (details below). Open issues:
1. **Column misassociation = wrong values.** The generic Y-row merge is column-blind. On
   CLB the read-only **PRED TO FL 380** (an InputField with the `inactive` class =
   "rendered as static value", per `InputField.tsx`) is misclassified as an editable input
   and geometrically mislabeled **"MACH: 380"**. On APPR the **F-speed 143** leaks into the
   wind line (`HD …, CROSS …, 143`).
2. **Comma-soup rows.** `OPT, FL, 405, REC MAX, FL, 425` / `MANAGED, 290, KT, .84` /
   `CLB SPD LIM, 250, KT, /, FL, 100` / `DEST, KLAX, 19:54, 29.6, KLB`.
3. **Unlabeled green-dot speed.** `246, KT` / `194, KT` (label is an `<svg><circle>`).
4. **STEP ALTs clarity.** Relabel WPT→"Step waypoint", ALT→"Step altitude"; drop the
   orphan `DIST, UTC` header.

## Why the full builder was reverted (and what it needs)
A `buildPerfLines` (gated by `isPerfPage`, mirroring `buildFplnLines`) was implemented and
live-tested. It correctly produced clean lines for the simple `.mfd-label-value-container`
fields (`OPT: FL 405`, etc.) — but it **regressed** other fields, because the PERF DOM has
MORE container types than two:
- **`.mfd-fms-perf-to-thrred-noise-grid` cells** (THR RED/ACCEL/EO ACCEL) — not LVCs, not
  the speed grid → fell through and fragmented (`THR RED, 1510` / `FT`).
- **Composite label-less fields** — `CLB SPD LIM` = a label + a label-less speed LVC
  (`250 KT`) + a `/` + a label-less FL LVC (`FL 100`); per-LVC emission split it into 3.
- **`inactive` is phase-dependent** — THR RED/ACCEL/DERATED CLB are `inactive` (read-only)
  at cruise but EDITABLE in climb. So the read-only-vs-editable handling can't be fully
  validated at cruise (can't exercise climb-phase active fields live).

**Conclusion:** doing this correctly across all 6 tabs (T.O/CLB/CRZ/DES/APPR/GA) ×
multiple container types × flight phases is not safely verifiable by live cruise scraping
alone. It should be built with **offline jsdom tests** (like `tools/flypad-shell-test/`)
that pin each tab's expected screen-reader output against captured DOM fixtures, then
spot-checked live. The captures in `tools/_probe/captures/mcdu_captures.md` +
`_perf_dom*.js` output are the raw material for those fixtures.

### Builder design (for the tested follow-up)
- `A.isPerfPage(page)` = `page.querySelector('[class*="mfd-fms-perf"]')`.
- Own consumed regions via a `data-fbwa380-perf-owned` attr (cleared each scrape);
  `insidePerf` makes the generic static pass + Y-merge skip them; perf items carry
  `perf:true` so the Y-merge never fuses them.
- Emit one clean line per **logical field**, handling EACH container type:
  speed grids (`-clb/-crz/-des-grid`, rows split on `br` cells, pair data cells to the
  header row by column index), thrred/noise grids, flaps-packs grid, label-value
  containers, and composite label + label-less-value groups (CLB SPD LIM, wind HD/CROSS).
- Read-only `inactive` InputFields → static text (not interactive); EDITABLE inputs stay
  in step-1 with their stamped idx (selectability preserved). Green-dot: synthesize
  "green dot" when the label is an `<svg><circle>`.
- STEP ALTs: gate on `VERT REV` title; relabel WPT/ALT; drop the orphan `DIST, UTC`.
- **Selectability invariant:** every input/combobox/radio/subtab/button keeps its
  `data-fbwa380-mcdu-idx`; the builder only takes over STATIC text.

### Container-type inventory (what buildPerfLines must handle)
Confirmed live (`tools/_probe/_perf_dom*.js`):
- `.mfd-label-value-container` — label `.mfd-label` + value `.mfd-value` + unit
  (`.mfd-unit-leading`/`.mfd-unit-trailing`) siblings. The common discrete field
  (OPT/REC MAX/LW/HD/CROSS/F/S/VREF/VLS/V1/VR/V2). Green-dot speed = no `.mfd-label`,
  an `<svg><circle>` instead → synthesize "green dot".
- Speed-prediction grids `.mfd-fms-perf-clb-grid` / `-crz-grid` / `-des-grid` — cells
  `.mfd-fms-perf-speed-table-cell` (+ `.mfd-fms-perf-speed-presel-managed-table-cell`);
  a `br`-classed cell starts each row; row 0 = column headers (MODE/SPD/MACH/PRED TO —
  note CRZ swaps SPD/MACH). Pair data cells to header cells BY COLUMN INDEX.
- `.mfd-fms-perf-to-thrred-noise-grid` cells — THR RED/ACCEL/EO ACCEL (T.O/CLB/GA).
- `.mfd-fms-perf-to-flaps-packs-grid` — FLAPS/THS FOR/PACKS/ANTI ICE (T.O).
- Composite label-less groups — e.g. CLB SPD LIM = a label + label-less speed LVC
  (`250 KT`) + a `/` + label-less FL LVC (`FL 100`); must be grouped, not split.
- **Read-only fields** = an `.mfd-input-field-container.inactive` ("rendered as static
  value", `InputField.tsx`) — NOT editable. Skip from interactive (don't stamp); emit its
  value as static. **`inactive` is PHASE-DEPENDENT** (THR RED/ACCEL/DERATED are inactive
  at cruise, editable in climb) — so editable-phase behaviour must be covered by a
  fixture variant (remove the `inactive` class) AND a real climb-phase live check.

## HANDOFF — to complete + test in later sessions
**Done now:** units fix (shipped, in working tree, NOT committed); full diagnosis (this
doc); raw clean per-tab live output (`tools/_probe/captures/mcdu_captures.md`); offline
harness (`tools/perf-builder-test/`, jsdom, proven faithful on CLB); DOM fixtures
(`tools/perf-builder-test/fixtures/`, with the multi-tab leak caveat in that README).

**Later session (offline, no sim needed for most of it):**
1. `cd tools/perf-builder-test && npm install`.
2. (Optional, needs sim) Regenerate clean multi-tab fixtures — see the README "Known
   fixture limitation". `perf_clb`, `stepalts`, `fpln` are already clean.
3. Implement `A.buildPerfLines` + `A.isPerfPage` + `A.insidePerf` in
   `coherent-a380-agent.js`, gated like `buildFplnLines`. Wire at the 4 sites in
   `enumerateLines`: (a) clear `data-fbwa380-perf-owned` with the stale-idx clear,
   (b) `if (isPerf) idx = A.buildPerfLines(...)` after the `isFpln` block, (c)
   `if (isPerf && A.insidePerf(t)) continue;` in the static pass, (d) `&& !cur.perf`
   in the Y-merge guard. (An earlier attempt at exactly this is in git history — the
   commit message references the revert; reuse it as a starting point.)
4. Skip `.mfd-input-field-container.inactive` from interactive in step-1 (read-only).
5. `node run.js perf_clb` etc. to iterate; then write `*.test.js` asserting per-tab
   output + the selectability invariant + `fpln` UNCHANGED. `npm test` green.
6. Live spot-check each tab via `coherent-eval.ps1 -PreFile <edited agent>` (tests the
   source edit without restarting the app).

**What the human (you) must do:**
- Run `npm install` in `tools/perf-builder-test` once (network).
- **One climb-phase check on a departure:** confirm THR RED / ACCEL / DERATED CLB are
  SELECTABLE and read cleanly while active (the one state cruise can't exercise).
- **Deploy:** rebuild + restart MSFSBA so it loads the new agent (the running app reads
  the agent at connect; the build copies it to output).
- Commit when satisfied.
