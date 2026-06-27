# FBW A380X MFD/flyPad — source-verified findings (2026-05-29)

Source: local clone `C:\Users\franc\Documents\development\fbw-aircraft` @ `0995706`
(origin flybywiresim/aircraft master). Pulled today; only new commit was an
MFD PERF QNH inHg fix (no DOM/selector impact).

## 1. Reading the current MFD page — USE A SIMVAR, not the title scrape

`MFD.tsx` `activeUriChanged()` sets, on every page change:

    L:A380X_MFD_<L|R>_ACTIVE_PAGE   (string)  = the full page URI, e.g. "fms/active/perf"

- L = Captain, R = First Officer.
- This is the single most reliable page-change signal. Read it via SimConnect /
  MobiFlight WASM (string simvar) OR via Runtime.evaluate
  `SimVar.GetSimVarValue('L:A380X_MFD_L_ACTIVE_PAGE','string')`.
- Far better than hashing the title-bar text; updates atomically on navigation.

## 2. Navigation — THREE mechanisms, in order of reliability

### (a) Click the dropdown menu item by STABLE ID  ← BEST, full coverage
`PageSelectorDropdownMenu.tsx`: each menu item is rendered as
`<span id="{idPrefix}_{idx}" class="mfd-dropdown-menu-element">LABEL</span>`
with a click listener added in `onAfterRender`. The listener calls
`val.action()` (which calls `uiService.navigateTo(uri)`) — and a programmatic
`element.click()` fires it **even though the dropdown is display:none**.
So we can navigate directly without opening the dropdown first.

idPrefix = `${captOrFo}_MFD_pageSelector<Tab>` where captOrFo is `CAPT` or `FO`.
From `FmsHeader.tsx` the FMS header tabs + items (index → label → URI):

  CAPT_MFD_pageSelectorActive_0   F-PLN      fms/active/f-pln (activeFlightPlanPageUri)
  CAPT_MFD_pageSelectorActive_1   PERF       fms/active/perf
  CAPT_MFD_pageSelectorActive_2   FUEL&LOAD  fms/active/fuel-load (activeFlightPlanFuelAndLoadUri)
  CAPT_MFD_pageSelectorActive_3   WIND       (disabled)
  CAPT_MFD_pageSelectorActive_4   INIT       fms/active/init
  CAPT_MFD_pageSelectorPosition_0 MONITOR    fms/position/monitor
  CAPT_MFD_pageSelectorPosition_1 REPORT     (disabled)
  CAPT_MFD_pageSelectorPosition_2 NAVAIDS    fms/position/navaids
  CAPT_MFD_pageSelectorPosition_3 IRS        fms/position/irs
  CAPT_MFD_pageSelectorPosition_4 GNSS       (disabled)
  CAPT_MFD_pageSelectorPosition_5 TIME       (disabled)
  CAPT_MFD_pageSelectorSecIndex_0 SEC 1      fms/sec/index/1
  CAPT_MFD_pageSelectorSecIndex_1 SEC 2      fms/sec/index/2
  CAPT_MFD_pageSelectorSecIndex_2 SEC 3      fms/sec/index/3
  CAPT_MFD_pageSelectorData_0     STATUS     fms/data/status (dataStatusUri)
  CAPT_MFD_pageSelectorData_1     WAYPOINT   (disabled)
  CAPT_MFD_pageSelectorData_2     NAVAID     (disabled)
  CAPT_MFD_pageSelectorData_3     ROUTE      (disabled)
  CAPT_MFD_pageSelectorData_4     AIRPORT    fms/data/airport
  CAPT_MFD_pageSelectorData_5     PRINTER    (disabled)

(FO_ prefix for the right MFD. ATCCOM/SURV/FCUBKUP have their own headers:
AtccomHeader.tsx, SurvHeader.tsx, FcuBkupHeader.tsx — same pattern, different idPrefix.)

### (b) KCCU H-events  ← works, but only the keys with a case in MFD.tsx
`MFD.tsx` onAfterRender subscribes to bus `hEvent`. For events starting with
`A32NX_KCCU_L` (capt) / `A32NX_KCCU_R` (fo), it takes `key = eventName.substring(13)`
and switch-navigates. Confirmed working keys → URIs:

  DIR     -> fms/active/f-pln-direct-to
  PERF    -> fms/active/perf
  INIT    -> fms/active/init
  NAVAID  -> fms/position/navaids
  FPLN    -> fms/active/f-pln/top
  DEST    -> fms/active/f-pln/dest
  SECINDEX-> fms/sec/index
  SURV    -> surv/controls
  ATCCOM  -> atccom/connect
  CLRINFO -> clears latest FMS error message (not navigation)
  MAILBOX/ND -> cursor moves only (no-op for us)

Fire via `Coherent.trigger('H:A32NX_KCCU_L_PERF')` (+ the SimVar pulse pattern we
already use). NOTE: this CONTRADICTS the earlier "KCCU page-nav doesn't work"
assumption — it does route to navigateTo. If it failed live before, suspect the
event wasn't reaching the `hEvent` bus or keyboard mode wasn't the issue
(navigation keys don't require KBD_ON). Worth retesting; but (a) is more complete.

### (c) uiService.navigateTo(uri) directly  ← cleanest but instance not exposed
`MfdUiService.navigateTo(uri)` sets the `activeUri` Subject AND publishes
`mfd_active_uri` on the bus. BUT the page swap is driven by the Subject inside the
live MfdComponent's PRIVATE `#uiService` — publishing `mfd_active_uri` on the bus
alone does NOT swap the page. No global handle to the instance is exported, so we
can't easily call this from Runtime.evaluate. Prefer (a).

URIs accepted: `sys/category/page[/extra]`; `back` pops the nav stack.
`Coherent.trigger('UNFOCUS_INPUT_FIELD')` is fired before every navigation.

## 3. MFD DOM anchors (confirmed)
- Root custom element: `<a380x-mfd url="...">` ; `getDisplayIndex()` reads last char of url.
- `.mfd-main` (top), header mounted in a bare div, page in `.mfd-navigator-container`.
- `#MFD_CONTENT` exists (used as outside-click target).
- Dropdown: `.mfd-dropdown-container` > `.mfd-page-selector-outer`
  (`.mfd-page-selector-label`) + `.mfd-dropdown-menu` > `.mfd-dropdown-menu-element`
  (with `.disabled` / `.separator` modifiers).

## 4. Interaction mode (KCCU vs touchscreen)
`MFD.tsx`: `interactionMode` = Kccu when `L:A32NX_KCCU_<L|R>_KBD_ON_OFF` (bus
`kccuOnL`/`kccuOnR`) else Touchscreen. Touchscreen clicks (our `.click()`) work
regardless — we don't need KBD_ON for clicking, only for raw KCCU key entry.

## Web research validation (deep-research, 2026-05-29, 103 agents, 22/25 claims confirmed)
Corroborates all of the above against official FBW docs + GitHub:
- **Navigation: DOM-click, NOT KCCU H-events.** FBW's own issue #9348 ("Refactor
  KCCU keys from H-Events to B-Events") states the H-event implementation makes
  KCCU "practically unusable (or at least very uncomfortable) for external tools
  like SPAD." → our stable-element-ID `.click()` path (§2a) is the right call;
  KCCU H-events (§2b) are a best-effort fallback only.
- `A380X_MFD_{side}_ACTIVE_PAGE` is officially documented (a380-simvars.md) as a
  String SimVar = "URI of active page (e.g. fms/active/init)". Confirmed read path.
- **No A380X SimBridge remote MCDU.** SimBridge Remote MCDU (ws port 8380,
  /interfaces/mcdu, bidirectional) is A32NX-ONLY. The broad "SimBridge remote
  access to flyPad/EFB" claim was REFUTED 0-3. So the Coherent GT debugger DOM
  approach is the only viable path for the A380X — which is what we built.
- flyPad = React/TypeScript/Vite/Tailwind, root `Efb.tsx` in fbw-common/.../EFB.
  Inspect by the coui:// path / title substring, NOT a hardcoded VCockpit index
  (the index is load-order dependent). We resolve by title needle "- EFB". ✓
- Coherent GT = Chromium 49 / ES5-only is the standing engineering assumption
  (well-known; not separately re-verified by the research). Keep agents ES5.
- **Caveat: master moves.** CSS class names / URIs can change between A380X dev
  builds. We pin understanding to the local clone @ 0995706; re-scrape on FBW bumps.

## Recommendation for MSFSBA
1. Page-change detection: read `L:A380X_MFD_<L|R>_ACTIVE_PAGE` each poll; hash on it.
2. Navigation buttons: click `<captOrFo>_MFD_pageSelector<Tab>_<idx>` by stable ID
   (add a `navigateById` agent entry). Keep KCCU H-event as a fallback.
3. captOrFo: CAPT when activeMcdu==1, FO when ==2.
