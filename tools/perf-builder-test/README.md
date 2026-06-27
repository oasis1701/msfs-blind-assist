# perf-builder-test — offline harness for the A380 MFD PERF / STEP ALTs scrape

Develops + regression-tests the structure-aware PERF/STEP-ALTs cleanup of
`MSFSBlindAssist/Resources/coherent-a380-agent.js` **without the sim**. Fixtures are
real in-flight DOM captures with live visibility (`data-vis`) and geometry
(`data-rect`) baked in, so `A.enumerateLines` runs offline exactly as it does live.

## Setup
```
cd tools/perf-builder-test
npm install            # jsdom
```

## Use
```
node run.js perf_clb   # print the scraped lines for a fixture
npm test               # run the regression tests (once *.test.js exist)
```
Fixtures: `fixtures/*.html` (fpln, perf_to, perf_clb, perf_crz, perf_des, perf_appr,
perf_ga, stepalts). Current output snapshots: `baselines/*.txt`.

Proven faithful: `node run.js perf_clb` reproduces the live CLB scrape line-for-line
(same `MACH: FL 380`, same comma-soup, same idx) — see `baselines/perf_clb.txt`.

## ⚠️ Known fixture limitation — multi-visited-tab leakage
The PERF tabs share one page; FBW keeps a just-visited tab mounted+visible for a moment
after you switch away (it `display:none`s it only later). The fixture capture
(`tools/_probe/_capture_fixture.js`) stamps `data-vis` at capture time, so a tab fixture
captured AFTER visiting other tabs over-includes those tabs' inputs (e.g.
`fixtures/perf_appr.html` leaks T.O/CLB/CRZ/DES inputs as the first ~35 lines of
`baselines/perf_appr.txt`). **Clean, single-context fixtures: `perf_clb`, `stepalts`,
`fpln`.** The authoritative CLEAN per-tab live output for ALL tabs is in
`tools/_probe/captures/mcdu_captures.md` (first-walk scrapes) — use THAT as the expected
"input" reference when writing tests.

**To regenerate clean multi-tab fixtures (later session, needs sim):** capture each tab
immediately after a longer settle, or capture the active-phase tab fresh after a
nav-away/nav-back, or filter inactive TopTabNavigator panels structurally in the harness
(identify the active panel; skip siblings). Until then, develop against `perf_clb` +
`stepalts` + the clean text in `mcdu_captures.md`.

## What to build (the task)
Implement `A.buildPerfLines` + `A.isPerfPage` + `A.insidePerf` in the agent, gated like
`buildFplnLines`, to emit ONE clean line per logical field/row. Full design, the wiring
points (4 sites in `enumerateLines`), the container-type inventory, and the per-tab
target output are in `docs/a380-mcdu-perf-stepalts-findings.md`. Tests must assert (a) the
clean per-tab output and (b) the **selectability invariant**: every input/combobox/radio/
subtab/button still has a `data-fbwa380-mcdu-idx`. Also assert `fpln` is UNCHANGED
(no regression to non-PERF pages — the builder is gated by `isPerfPage`).
