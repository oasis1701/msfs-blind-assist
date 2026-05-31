# `tools/_probe/` — live A380X Coherent probe scripts

Small, self-contained JavaScript snippets that run inside a live A380X Coherent GT
view via [`../coherent-eval.ps1`](../coherent-eval.ps1). They are how almost every
A380 feature in MSFSBA was discovered, verified, and debugged **against the running
sim** — read/write any L:var, scrape/click any cockpit DOM, walk the MCDU/flyPad,
all independent of the SimConnect MCP (which goes stale on focus loss).

## How to run one

```powershell
# From the repo's tools/ directory, with MSFS running + the A380X loaded:
./coherent-eval.ps1 -Title A380X_MFD -ExprFile _probe\fpln_sanity.js
# Inject the in-page agent first when the probe calls __MSFSBA_A380 / __MSFSBA_FLYPAD:
./coherent-eval.ps1 -Title A380X_MFD `
  -PreFile ..\MSFSBlindAssist\Resources\coherent-a380-agent.js `
  -ExprFile _probe\surv_read.js
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

Read an L:var directly (no agent needed):

```js
(function(){ return SimVar.GetSimVarValue('L:A32NX_BTV_STATE','number'); })()
```

## What's here (worked catalogue)

| Probe | View | Demonstrates |
|-------|------|--------------|
| `uisvc.js` | A380X_MFD | resolve the MFD `uiService` (the cross-system `navigateTo` key) |
| `atccom_nav.js` / `atccom_all.js` / `atccom_read.js` / `atccom_scrape.js` | A380X_MFD | navigate + scrape the ATC COM / CPDLC + D-ATIS pages |
| `sec_read.js` | A380X_MFD | the secondary (SEC) flight plans read like the active plan |
| `surv_read.js` / `surv_click.js` / `surv_click2.js` / `radio_diag.js` | A380X_MFD | SURV pages; RadioButtonGroup enabled/disabled + click-actuation |
| `btv_dist.js` | A380X_MFD | BTV predicted dry/wet/stop-bar distances (plain-metres L:vars) |
| `rudtrim.js` | A380X_MFD | rudder-trim ARINC429 word + status, vs the stock percent |
| `metric.js` / `metric_toggle.js` | A380X_MFD / "- EFB" | the metric/imperial EFB persistent store (`GetStoredData`/`SetStoredData`) |
| `fpln_sanity.js` | A380X_MFD | sanity-scrape the active F-PLN with the agent injected |
| `efb_scrape.js` / `efb_failnav.js` / `efb_ata.js` / `efb_dash.js` | "- EFB" | flyPad scrape + navigate (Failures app → ATA chapter → per-failure buttons) |
| `pack_write.js` / `ovhd_flip.js` / `ovhd_flip2.js` / `ovhd_check.js` | A380X_MFD | **write-stick test**: flip overhead PB L:vars via the calculator path and read back after a delay (proved the "computed-output" PBs are actually settable — see CLAUDE.md #103) |
| `ecl_diag.js` / `ecl_scrape.js` | A380X_EWD | ECL `.EclLine` presence + the agent's structured row scrape |

**Write-stick testing rule (CLAUDE.md #103):** to decide whether an FBW L:var write
sticks, ALWAYS write via the calculator path — `SimVar.SetSimVarValue('L:VAR','number',v)`
in a Coherent view, or `(>L:VAR)` via `execute_calculator_code`. NEVER use the MCP
`set_lvar` (native data-def) — it silently fails for many FBW L:vars and produces false
"reverts / uncontrollable" conclusions.

These are **reference examples**, not a test suite — copy one, tweak the var/page,
and run it to probe whatever you're working on. Throwaway one-offs are fine; just
name them descriptively if you keep them.
