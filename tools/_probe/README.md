# `tools/_probe/` — live A380X / flyPad Coherent probe scripts

Small, self-contained JavaScript snippets that run inside a live Coherent GT view
via [`../coherent-eval.ps1`](../coherent-eval.ps1). They are how almost every A380
feature in MSFSBA was discovered, verified, and debugged **against the running
sim** — read/write any L:var, scrape/click any cockpit DOM, walk the MCDU/flyPad,
all independent of the SimConnect MCP (which goes stale on focus loss).

This directory is intentionally kept small: it holds a **reference set** of probes
(the ones other tools, the jsdom test suites, and CLAUDE.md point at), not the
hundreds of throwaway one-offs that were used during development. Throwaway probes
are fine to create locally — just don't commit them (a curated cleanup removed the
old ad-hoc pile; see PR #85).

## How to run one

```powershell
# From the repo's tools/ directory, with MSFS running + the aircraft loaded.
# Read an L:var directly (no agent needed):
./coherent-eval.ps1 -Title A380X_MFD -Expr "SimVar.GetSimVarValue('L:A32NX_BTV_STATE','number')"

# Inject the in-page agent first when the probe calls __MSFSBA_A380 / __MSFSBA_FLYPAD:
./coherent-eval.ps1 -Title "- EFB" `
  -PreFile ..\MSFSBlindAssist\Resources\coherent-flypad-agent.js `
  -ExprFile _probe\_settings_scrape.js
```

`-Title` resolves the view by substring from `/pagelist.json` (ids shuffle every
session — never hardcode them). The view title-needles are listed in the header of
`coherent-eval.ps1`. `-PreFile` evaluates a file (usually the agent) before the
probe; `-ExprFile` / `-Expr` is the probe itself. The probe's return value is
printed.

## The probe pattern

Each probe is an IIFE that returns a string (JSON or plain text):

```js
(function(){ try {
  var A = window.__MSFSBA_A380;            // the injected MCDU/MFD agent
  A.navigateUri('atccom/msg-record');      // UIService nav (cross-system)
  var o = JSON.parse(A.scrape());          // structured page scrape
  return o.title + ' n=' + o.elements.length;
} catch(e){ return 'ERR ' + e + ' ' + (e && e.stack); } })()
```

## What's kept here

| File | View / used by | Purpose |
|------|----------------|---------|
| `_settings_scrape.js` | "- EFB" (flypad agent) | scrape the flyPad Settings pages — the reference flyPad probe |
| `_pmdg_settings_dump.js` | "VCockpit09/10 - PMDGTablet" (pmdg-efb agent) | dump the whole PMDG tablet scrape as readable one-line-per-element — the reference PMDG EFB probe (Settings/Dashboard/chrome tours) |
| `_door_fiber.js` | "- EFB" (flypad agent) | read flyPad door identities from the React fiber (precise door names in flight) |
| `_click_by_text.js` | any (agent) | generic click-an-element-by-its-text helper |
| `_capture_fixture.js` | bakes `tools/perf-builder-test` fixtures | stamp `data-rect`/`data-vis` onto a live MFD scrape so `enumerateLines` runs offline under jsdom |
| `_capture_flypad_fixture.js` | bakes `tools/flypad-settings-test` fixtures | same, for the flyPad Settings fixtures |
| `a380_engmon.js` / `a380_engmon_full.js` | A380X_SYSTEMSHOST, via `../a380_engine_monitor.ps1` | live variable monitor (caught the ENGINE_COUNT=2 ignition-fan-out bug — CLAUDE.md "Engines 3 & 4 motor but never light") |
| `flypad_tour.js` + `drive_tour.ps1` | "- EFB" | tour every flyPad EFB tab (ping/scrape/click/setValue) |
| `fbw_lvars_clean.txt` | reference data | a dump of every L:var in the running A380X — grep it for a system + its synonyms |
| `captures/mcdu_captures.md`, `captures/mfd_pages_recon.md` | reference notes | per-page MCDU/MFD scrape recon |

**Write-stick testing rule (CLAUDE.md #103):** to decide whether an FBW L:var write
sticks, ALWAYS write via the calculator path — `SimVar.SetSimVarValue('L:VAR','number',v)`
in a Coherent view, or `(>L:VAR)` via `execute_calculator_code`. NEVER use the MCP
`set_lvar` (native data-def) — it silently fails for many FBW L:vars and produces false
"reverts / uncontrollable" conclusions.

Copy one, tweak the var/page, and run it to probe whatever you're working on — but
keep new throwaways out of version control.
