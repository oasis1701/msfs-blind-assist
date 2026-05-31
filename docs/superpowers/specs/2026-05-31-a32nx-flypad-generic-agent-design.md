# A32NX flyPad — Generic CDP Agent Rewrite

**Date:** 2026-05-31
**Status:** Approved design, pre-implementation
**Branch (current):** feature/fbw-a32nx-update

## Goal

Rewrite the FBW A32NX flyPad EFB accessibility integration to match the
architecture already used for the A380 (`coherent-flypad-agent.js`): a single
**generic** in-page DOM scraper/clicker installed at runtime over the Coherent
GT debugger (CDP), driven by a **request/response** C# client, rendered as a
**generic live form** that exposes whatever flyPad page is currently open.

This replaces the current A32NX approach (a feature-specific bridge JS that
knows about SimBrief / Ground Services / Navigraph by hardcoded selectors,
pushes state over an HTTP server, and is rendered as curated per-feature tabs).

## Decisions (locked during brainstorming)

1. **Full replacement**, scoped to the flyPad EFB. No curated tabs, no SimVar
   shortcut panel — the user navigates the real flyPad through the generic list.
2. **Transport:** Approach A — a persistent-WebSocket CDP client with
   request/response correlation by CDP message id, plus reconnect/reinstall on
   page reload.
3. **Sync model:** ~1s auto-poll + manual refresh (F5), with a short delayed
   re-scrape after each click/setValue.
4. **No wake button.** `powerOn()` runs automatically on connect. A
   **status / self-test diagnostic** lets the user confirm the pipeline works.
5. **Single-connection constraint.** Coherent GT's debugger accepts only ONE
   devtools connection at a time. `CoherentEFBClient` is the sole owner of that
   one connection; everything (install/scrape/click/setValue/ping/powerOn/
   heartbeat) multiplexes over it via CDP message-id correlation. No other
   component may hold a competing connection while it is active.

## Architecture

```
┌─ MSFS: FBW A32NX flyPad (React, Coherent GT, devtools :19999) ─┐
│   window.__MSFSBA_FLYPAD  ← generic in-page agent              │
└───────────────▲───────────────────────────────────────────────┘
                │ CDP Runtime.evaluate (persistent WebSocket)
                │   install once → scrape()/clickElement()/setValue()/ping()/powerOn()
┌───────────────┴───────────────────────────────────────────────┐
│ CoherentEFBClient.cs   (request/response, reconnect/reinstall)  │
└───────────────▲───────────────────────────────────────────────┘
                │ FlypadElement[] (parsed JSON) / click+set calls
┌───────────────┴───────────────────────────────────────────────┐
│ A32NXEFBForm.cs (generic)  — renders flat element list as       │
│   native accessible controls; ~1s auto-poll + F5 manual refresh │
└─────────────────────────────────────────────────────────────────┘
```

### Component inventory

**ADD**
- `Resources/coherent-a32nx-flypad-agent.js` — generic scraper/clicker, adapted
  from the A380 `coherent-flypad-agent.js` and verified against the live A32NX
  DOM. Stamps a stable index `data-fbwa32nx-efb-idx`; double-load guard reuses
  the `_a32nx_efb_bridge_loaded` lineage but the public global is
  `window.__MSFSBA_FLYPAD`.
- `SimConnect/CoherentEFBClient.cs` — persistent-WebSocket CDP client.

**REPLACE/REWRITE**
- `Forms/A32NX/A32NXEFBForm.cs` — gutted of curated tabs; becomes the generic
  live-flyPad renderer.

**REMOVE (A32NX-only)**
- `Resources/a32nx-efb-accessibility-bridge.js` (feature-specific HTTP bridge).
- `SimConnect/CoherentGTInjector.cs` — its WebSocket handshake + frame-building
  helpers are lifted into `CoherentEFBClient`, then the file is retired.
- The A32NX's wiring to the HTTP `EFBBridgeServer` (port 19777). **The
  `EFBBridgeServer` class itself stays** — it is still used by the PMDG EFB and
  HS787 bridges. Only the A32NX consumer is removed.

**UPDATE**
- `tools/efb-dom-tool.js` — `inject`/`state` commands retargeted from the old
  bridge globals to `window.__MSFSBA_FLYPAD`. The tool remains as the live
  verification harness.

## The in-page agent (`coherent-a32nx-flypad-agent.js`)

ES5 / Coherent-GT-safe (`var`, no arrow funcs, `indexOf` not `includes`,
top-level try/catch).

**Public contract (called by C# via `Runtime.evaluate`):**
- `__MSFSBA_FLYPAD.ping()` → `"MSFSBA_FLYPAD_OK"`
- `__MSFSBA_FLYPAD.scrape()` → JSON `{ ok, page, elements:[…] }`
- `__MSFSBA_FLYPAD.clickElement(idx)` → `"ok" | "missing"`
- `__MSFSBA_FLYPAD.setValue(idx, text)` → `"ok" | "missing"`
- `__MSFSBA_FLYPAD.powerOn()` → sets `L:A32NX_EFB_TURNED_ON=1` + synthetic
  interaction to resume the dormant render loop. Idempotent.

**Each scraped element carries:** `idx` (stable, stamped on the node),
`kind` (link/button/tab/toggle/checkbox/input/select/slider/heading/checkitem/
text), `text` (label derived from aria/title → own text → router href →
nearest heading), `value`, `controlType` (text/select/checkbox/""),
`clickable`, `level` (heading 1-6), `live` (aria-live politeness),
`disabled`, `options` (for selects). Reading order = nav-rail-last,
row-clustered, left-to-right, de-duplicated; Dashboard gets column-first order.

**A32NX divergence risk vs. the A380** (confirmed live, not assumed): (a) Toggle
component shape classes (e.g. pill width token), (b) nav-rail width threshold
(the `leftRel < 100` heuristic), (c) segmented / displaced setting controls.
Findings get baked into the agent.

## The C# client (`CoherentEFBClient.cs`)

Persistent WebSocket, request/response by CDP message id.

```
States: Disconnected → Discovering → Connecting → Installing → Ready
                            ▲                                    │
                            └──── page reload / socket drop ─────┘
```

**Responsibilities:**
- **Discovery:** poll `:19999/pagelist.json` for the EFB page (title suffix
  `- EFB`). Reuse `CoherentGTInjector.FindEfbPageIdAsync` logic.
- **Connect:** hand-rolled HTTP→WS upgrade (Coherent's non-standard
  `Connection` header defeats `ClientWebSocket`). One long-lived socket.
- **Install-once:** on connect, eval the agent file → `ping()` → `powerOn()`.
- **Request/response:** single send/receive pump; each call gets a monotonic
  `id`; a `TaskCompletionSource` keyed by `id` completes on the matching
  response frame. **Multi-frame reassembly across TCP packets** — scrape JSON is
  many KB and will not fit a single frame/read; this is the principal new code
  (the old per-call reader was sized for ~70-byte bool replies and is
  insufficient). Per-call timeout (~4s) faults the TCS.
- **Reconnect/reinstall:** heartbeat (`ping()` every few seconds, or a failed
  `scrape`) detects page reload (agent global gone) or socket death → tear
  down, rediscover, reconnect, reinstall.
- **Public API:** `Task<FlypadScrape> ScrapeAsync()`, `Task<bool> ClickAsync(int idx)`,
  `Task<bool> SetValueAsync(int idx, string text)`, `event Connected/Disconnected`,
  `bool IsReady`. JSON parsed into `FlypadElement` records via `System.Text.Json`.
- **Threading:** receive pump on a background task; captured
  `SynchronizationContext` marshals events/continuations to the UI thread.
- **Single connection / lifecycle:** Coherent GT allows only one devtools
  connection at a time, so this client holds THE one connection and is the only
  CDP consumer for the flyPad page. The connection is opened when the EFB form
  opens and **closed when the form closes**, releasing the single slot so other
  consumers (the `efb-dom-tool.js` verification harness, ad-hoc debugging) can
  use it when the form is not open. No per-call short-lived sockets — that model
  is retired with `CoherentGTInjector`.

## The generic form (`A32NXEFBForm.cs`)

Renders `FlypadElement[]` as native accessible controls in reading order. No
feature knowledge.

| `kind` / `controlType` | Rendered as |
|---|---|
| heading (level 1-6) | `Label`, `AccessibleRole = Heading` (H-key nav) |
| link / button / tab / clickable | `Button` → `ClickAsync(idx)` |
| toggle / checkbox / checkitem | `CheckBox` (from `value`) → `SetValueAsync(idx,"true"/"false")` |
| input / slider (text) | labeled `TextBox`; commit on Enter/leave → `SetValueAsync` |
| select | `ComboBox` from `options` → `SetValueAsync(idx, value)` |
| text | read-only `Label` |
| `disabled` | control disabled; `live` regions mirrored to an aria-live announcer |

**Sync model:** ~1s `System.Windows.Forms.Timer` → `ScrapeAsync()`; F5 forces
immediate re-scrape; ~300ms delayed re-scrape after a click/setValue.

**Focus-preserving diff-render (centerpiece UX):** keep a signature of the
rendered list (page label + per-element idx/kind/text/value/disabled). If
unchanged, do nothing — a naive full rebuild every second would steal
screen-reader focus constantly. On change, rebuild and **restore focus** to the
control whose `idx` matches the previously focused element (or nearest).

**Page label** (from `scrape().page`) shown and announced when it changes.

**Lifecycle:** open → ensure `CoherentEFBClient` started + `powerOn()`. Opened
via the existing Shift+T input-mode hotkey.

**Diagnostics (replaces the wake button):**
- Always-visible **status line**: `Connecting… → Installing agent → Live:
  "<page label>", N controls` + last-refresh time; announced on transitions.
- A **Status / self-test action** (button at top of form + hotkey) announces a
  full diagnostic on demand: connection state, agent present (`ping()`), current
  page label, control count, last successful scrape time. If `scrape()` returns
  `ok:false`, it announces the reason verbatim.

## Error handling

- **Sim not running / not FBW A32NX:** no `- EFB` page → status "flyPad not
  detected"; silent retry loop, no exceptions surfaced.
- **flyPad powered off / dormant:** `scrape()` returns
  `{ok:false, error:"flyPad root not found (powered up?)"}` → `powerOn()`
  retried with backoff; status "flyPad off — powering on…".
- **Page reload:** agent global vanishes → `ping()` fails → reconnect+reinstall
  within one heartbeat; status briefly "Reinstalling…".
- **CDP eval timeout / malformed JSON:** per-call timeout faults gracefully; the
  poll skips that cycle (keeps last good render) rather than blanking the form.
  Repeated failures flip status to Disconnected and trigger reconnect.
- **Agent never breaks the flyPad:** top-level try/catch in the JS — a scraper
  bug returns an error string, never throws into the EFB.

## Testing / live verification

No automated tests (repo convention for this SimConnect/UI app). Verification is
live and incremental, with the sim up, via `efb-dom-tool.js`.

**Single-connection caveat:** the verification tool and the app's
`CoherentEFBClient` cannot both be connected at once. During agent development,
run the tool with the app's EFB form closed (client disconnected) and the old
`CoherentGTInjector` polling stopped. When testing in-app, close the tool first.

Steps:

1. **Agent unit-walk:** `scrape()` each key page (Dashboard, Ground, Payload,
   Settings/3rd-Party, Navigraph, Checklists); confirm labels/kinds/values;
   patch classifiers for A32NX divergence.
2. **Click/setValue round-trips:** click a nav link → page label changes; toggle
   a switch → `value` flips; set PAX/cargo input → value committed.
3. **Resilience:** reload the EFB page → auto-reinstall; power flyPad off/on →
   `powerOn()` recovery.
4. **In-app:** build, open form via Shift+T with NVDA; confirm focus-preserving
   polling, heading nav, and the status/self-test diagnostic.

The PR will carry this as the in-sim test plan for the repo owner to run.

## Out of scope

- Other A380 agents (display/ecl/ewd/oans) and the MCDU — flyPad only.
- Changes to `EFBBridgeServer` or the PMDG/HS787 bridges.
- Any Community-folder patching (the whole point is runtime CDP install).
