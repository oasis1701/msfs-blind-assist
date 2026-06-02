# `tools/` — developer & debug tooling

Index of the dev/debug tools in this directory. **Full guide: [`../docs/tooling.md`](../docs/tooling.md)** — read it for the shared transport (the MSFS Coherent GT remote debugger on `:19999`), how to run each tool, and crash diagnosis.

For the *methodology* of proving a control works (calculator-path write-stick test, the write-mechanism decision tree, the case studies), see the **VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK** in [`../CLAUDE.md`](../CLAUDE.md).

---

## Start here

| Tool | Use it for |
|---|---|
| **[`coherent-eval.ps1`](coherent-eval.ps1)** | **The canonical entry point.** Read/write any live L:var and scrape/click any cockpit DOM from inside a Coherent view. `-Title <needle>` resolves the view by title (never hardcode ids); `-PreFile` injects an agent; `-Expr`/`-ExprFile` is the JS. |
| **[`_probe/`](_probe/README.md)** | Worked catalogue of ~42 live probe snippets — copy one, tweak, run via `coherent-eval.ps1`. How almost every A380 feature was discovered/verified. Includes `fbw_lvars_clean.txt` (live L:var dump) + catalog-diff artefacts. |

## Convenience drivers (thin wrappers over the same transport)

| Tool | View | Job |
|---|---|---|
| [`mcdu_tour.ps1`](mcdu_tour.ps1) / [`mcdu_run.ps1`](mcdu_run.ps1) / `mcdu_scrape.js` | A380X_MFD | Tour every MFD page / run an expr after nav / one-line scrape |
| [`fp_tour.ps1`](fp_tour.ps1) / [`fp_run.ps1`](fp_run.ps1) / `fp_scrape.js` / `fp_inspect.js` | `- EFB` | Tour flyPad pages / run an expr / scrape / deep element dump |
| [`mfd_import_and_scrape.ps1`](mfd_import_and_scrape.ps1) | A380X_MFD | Drive MFD INIT → type a city pair → re-scrape (worked `sendToField` example) |
| [`sd-page-tour.ps1`](sd-page-tour.ps1) | A380X_SDv2 | Scrape the decoded SD page (drive the page from the app/MCP first) |
| [`fcu/`](fcu/README.md) | A380X_FCU | FCU verification: `fcu-read.ps1` (state), `fcu-roundtrip.ps1`, `fcu-set.ps1` |

## Standalone Node projects (own transport + tests)

| Project | Job |
|---|---|
| [`fbw-mcdu-probe/`](fbw-mcdu-probe/README.md) | Inspect/drive/capture the **A32NX** MCDU over the SimBridge websocket (no C# app). `mcdu-format.js` = authoritative decode ref (sync with `Services/FbwMcduFormat.cs`). |
| [`flypad-shell-test/`](flypad-shell-test/) | Canonical keyed-DOM-reconcile spec for the flyPad WebView2 shell (jsdom). |
| [`efb-dom-tool.js`](efb-dom-tool.js) | Node CDP scraper/clicker for the **A32NX** EFB. |

## Reference catalogs (`*.md`)

Static research notes mined from FBW source (var names, page-index maps): `a380-simvars-catalog.md`, `a380-fcu-vars.md`, `a380-engine-start.md`, `a380-fault-status-vars.md`, `a380-sd-pages.md`, `fbw-a380-mfd-source-findings.md`, `ecam-display-readout-notes.md`, `a320-panel-structure.md`.

## Early bootstrap probes — superseded, kept

`probe-coherent-debugger.ps1`, `test-coherent-ws.ps1`, `prove-coherent-scrape.ps1`, `probe-efb-frames.ps1`, `probe-flypad-elements.ps1` — the first scripts that discovered the Coherent architecture. Superseded by `coherent-eval.ps1` + `_probe/`; their "Developer Mode ON" headers are **obsolete** (Dev Mode is **not** required). Kept as historical record / quick "is the port up" smoke tests. Prefer `coherent-eval.ps1` for new work.

## Pre-existing — do NOT modify

`PMDGDispatchTester/` and `CDUTest/` predate the FBW work and are independent PMDG console apps (not Coherent tooling). See [`../CLAUDE.md`](../CLAUDE.md) → Build Commands. Leave them untouched.
