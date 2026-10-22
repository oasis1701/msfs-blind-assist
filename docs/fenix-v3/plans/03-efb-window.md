# Fenix v3 EFB Window Implementation Plan (plan 03)

**Claimed by:** nobody yet · **Status:** not started

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Fenix v3 EFB usable with a screen reader in our "Fenix EFB" window (input mode, Shift+T): every app reachable by keyboard, every switch, box and list named, every selection and open state spoken, the two dead ends (MCDU, FAP) caught, and the window on the EFB's keyboard-friendly target at 127.0.0.1 with a tabbable status message.

**Architecture:** The window keeps hosting Fenix's own page in WebView2 and adds a layer, as `IFlyEfbForm` already does for the iFly EFB. One script, `MSFSBlindAssist/Resources/fenix-efb-accessibility.js`, is injected on document creation (the window already injects its Escape listener that way). It stamps roles, names, states and Tab stops on the page's own elements, kept current by a MutationObserver, and never changes what a press does: Enter and Space call the element's own `click()`. The window itself catches navigation to the MCDU and FAP pages (`CoreWebView2.SourceChanged`, decided by a pure `FenixEfbRoutes` class), goes back to the EFB home and says where to go instead. The script is tested offline with jsdom and `dom-accessibility-api` against trimmed live captures (`tools/fenix-efb-test`, like `tools/md11-efb-test`); the C# parts with xUnit.

**Tech Stack:** C# 13 / .NET 10 WinForms, WebView2 (already referenced), plain JavaScript (no build step), Node 18+ with jsdom and dom-accessibility-api for the offline tests, xUnit.

**Spec:** `docs/fenix-v3/spec.md` section 8; every app's findings: `docs/fenix-v3/evidence/efb-tour.md`. Index, file ownership and workflow: `docs/fenix-v3/plans/00-index.md`. Hotkeys and the Fenix windows list: `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions

The owner asked for this plan on 2026-10-21 ("towards the beginning", its own plan). Calls 1-3 moved here from the displays plan (plan 04, decisions 5-7) with the window; the rest are my calls, each one small to change:

1. **EFB target `external`, explicitly.** Today's URL has no `target`, and the EFB's own `detectTargetDevice` falls back to `external` for any browser that is not Chrome 49 (Coherent GT) and has no `_capt` in the URL, so WebView2 already gets `external`. Pinning it removes that reliance. What `external` means, read in the v3 bundle: Tab works and PageUp/PageDown/Home/End reach the page (a sim target swallows Tab unless its on-screen keyboard is open, blocks those four keys on a non-editable focus, and releases focus 2 s after a click: `AUTO_UNFOCUS_DELAY_MS=2e3`, `isSimTablet()`); no simulated battery or sleep; the physical keyboard, no on-screen one; its own `chart_theme_external_type` / `map_style_external_type` settings, so it never changes the cockpit tablets' themes; never touches `fenix.efb.hidden_*`/`connected_*`; charts come from Navigraph or the moving map only; and, as in any browser, the page reports its script errors to Fenix's error tracker (a sim target does not).
2. **127.0.0.1, not `localhost`** (A320-40). WebView2 races IPv4 against the refused `::1` (Chromium starts IPv4 300 ms after IPv6; from Chromium's source, not measured in WebView2), so the cost is up to 0.3 s per new connection, not 2 s; a plain client pays the full refusal (measured: 2,002-2,005 ms, three tries; 127.0.0.1 connects in 0.3-10 ms). The page builds every gateway address from its own origin (`getHostUrl`, `backendUrl`), so the choice reaches its GraphQL socket and REST calls too. One-time cost: the EFB's only per-origin state, `localStorage["fenix.efb.background-image-state"]` (the custom-background flag), starts empty.
3. **The "not reachable" message becomes a read-only TextBox** (VAT-13), as the iFly EFB window already does (`Forms/IFly737/IFlyEfbForm.cs:316-328`).
4. **Fix the page in place, not a native rewrite.** It keeps everything a sighted pilot has (owner principle) and needs no data-service plumbing. The risk is a Fenix update changing the page; the captures and the "every control named" sweep (Task 3) catch it, re-run after each Fenix update.
5. **Names come from the page's own visible text** (whitespace and `<br>` folded to single spaces, the page's capitals kept). Only controls with no text get names written here, exactly: home button "Home"; switcher icons the tile titles in order; pushback arrows "Pushback left", "Pushback straight", "Pushback right"; Settings' "⋮" buttons "More options for <setting>"; map zoom "Zoom in", "Zoom out"; quick settings slider "Brightness" and its airplane button "Airplane mode"; tab-bar arrows "Previous tabs", "Next tabs"; the Panel States pop-up "Panel states". A page's own `aria-label` is never overwritten.
6. **Never press anything, never change behaviour.** The layer only adds `role`, `aria-*`, `tabindex` and key handlers that call the page's own `click()`. No network calls, no element removed or hidden, no value written.
7. **Alt+Home goes to the EFB home from anywhere** (the iFly window's key). It sets `location.hash = '#/efb'`, the route the tour used.
8. **The MCDU and FAP tiles are caught by the window, not hidden.** On navigation to `#/mcdu…` or `#/fap…` the window loads the EFB home again and speaks one line (CORE-7: the screen reader says nothing useful there):
   - MCDU: "The EFB's MCDU can't be read with a screen reader. Use the MCDU window instead: input mode, then Shift+M."
   - FAP: "The EFB's FAP can't be read with a screen reader. Use the Fenix windows list instead: input mode, then Control+Shift+F."
9. **Loading notices speak through the screen reader:** the page's own notice and progress bar become a polite live region and a progress bar with its value; the app itself speaks nothing more.
10. **Settings, Documents, Failures and the FAP keep their own windows** (plans 11, 07, 10, 08): the layer fixes the EFB's versions too because it costs little, but those windows add what the page cannot (read-backs, confirmations, the FAP's screensaver).

## Global Constraints

- Fenix 3.0.0.7426. EFB URL, exact: `http://127.0.0.1:8083/?target=external#/efb` (the target in the query, before `#`). Never `localhost`; never introspect the schema; never call `/fenix/documents/OpenDocumentsFolder`.
- Files owned (no other plan touches them): `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs`; new `MSFSBlindAssist/Forms/Fenix/FenixEfbRoutes.cs`; new `MSFSBlindAssist/Resources/fenix-efb-accessibility.js`; in `MSFSBlindAssist/MSFSBlindAssist.csproj` ONE new `<None Update="Resources\fenix-efb-accessibility.js">` entry beside the other `Resources\*.js` entries (no other plan edits the project file); tests `tests/MSFSBlindAssist.Tests/*FenixEfb*.cs`; new `tools/fenix-efb-test/**` (Node harness, fixtures). Anything else is a handoff.
- Plan 01's rule file `.claude/rules/fenix-data.md` globs `MSFSBlindAssist/Forms/Fenix/**` but not yet `MSFSBlindAssist/Resources/fenix-*.js` (a glob cannot exist before a file matches it: `ClaudeContextBudgetTests`). Add that glob to its `paths:` in the same commit that creates `fenix-efb-accessibility.js`: the one `.claude/rules/**` edit this plan makes (00-index ownership table). Read the script and the form with the Read tool so the rules load (CORE-16).
- The script: one IIFE, guarded by `window.__msfsbaFenixEfb`; plain ES2017, no modules, no dependencies; no `fetch`, `XMLHttpRequest`, `WebSocket`, `.remove(`, `removeChild`, `innerHTML =` or `.value =`. Every element it has handled carries `data-msfsba="1"` so a second pass adds no second listener.
- Fixtures: trimmed captures of the page's markup only (Task 3's snippet strips scripts, styles, pictures, the seat map and map tiles, and every `value` attribute); never a Fenix bundle, never a whole document set; before committing a Settings capture, search it for `hoppie`, `simbrief`, `navigraph`, `pushover` values and drop any.
- Speech (CORE-7): the window speaks only the two dead-end lines and the not-reachable message. Everything else the screen reader reads from the page.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`; Node: `cd tools/fenix-efb-test && npm install && node --test`. Build: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`.

## Review Focus

- **The page re-renders** (Angular swaps a view, opens a chapter, adds a dialog): every new element gets its fix and none gets a second listener (Task 4 `Second_pass_changes_nothing`, `Late_elements_are_fixed`).
- **A Fenix update changes a class:** the script must never throw into the page; an unknown structure is left as it is (Task 4 `Unknown_page_is_left_alone`), and the sweep (Task 3) shows what regressed.
- **Keys:** Enter and Space on a stamped element press it and do not scroll; typing in a text box is never caught; Alt+Home works from inside a text box (Task 4 `Keys_press_buttons_but_not_text_boxes`).
- **A state changes after a press** (a runway condition picked, a choice made, a chapter opened): the spoken state follows the page's own class (Tasks 6-7, each `…_follows_the_page` test).
- **Dead-end routes:** `#/mcdu?cdu=1&fullscreen=false` and `#/fap/screen-saver` are caught; `#/efb/…` routes and an unrelated page never are (Task 2 tests).

## Evidence (2026-10-21, Fenix 3.0.0.7426, A320-214 CFM, read only)

Structures the layer keys on, read live in the same Chromium engine (the per-app findings are in `evidence/efb-tour.md`):

| Where | Structure | Fix |
|---|---|---|
| Home | `#appContainer > div.appIcon` holding `img.app-img`, `<br>`, `span.appTitle` "Fenix" … "FAP" | button named by `.appTitle`, Tab stop |
| Every app | `div#homeButtonFree.home-button` (draggable, empty); top bar `div.popup-arrow` opens a switcher of `img.app-img` without `alt` | "Home" button; switcher icons named by tile titles in order |
| Ground Services | `div.slide-container > mat-slide-toggle … input[role=switch]` + sibling `div.text` "GPU" (14 times); `div.left-arrow`/`down-arrow`/`right-arrow` | switch named by its `div.text`; arrows named (decision 5) |
| Mass and Balance | section title text, then `table` with header row (Planned, Boarded…) and a row holding `input.planned-input[placeholder="Max 180"]`; buttons with `<br>` ("Resend<br>Loadsheet") | input named "<section> <column>, <placeholder>" ("Passengers Planned, Max 180"); button names with a space |
| Panel States | `mat-dialog-container` with four buttons, no title | named "Panel states" |
| Takeoff, Landing | `div.col-2` / `div.section-item` holding `div.departure-label` + `app-keyboard-input input[placeholder="----"]` or `mat-select[role=combobox]`; results in `div.mcdu-screen` as label rows over value rows (`small-yellow-box` spans = not computed) | input or combobox named by its `.departure-label`; each value cell named "<label above>: <value>" |
| Landing | `div.section-item[tabindex]` "6<br>DRY" … with `section-selected`; `div.one-click-change` (text + up-down picture); `div.btn.arrival-procedure-item` | radio group "Runway condition" with `aria-checked` from `section-selected`; buttons |
| Settings | `li.category` (`category--active`); each option an `article` with a heading ("A/C ESS Auto Switching" plus a "Default"/"Livery" badge), `label.toggle > input[type=checkbox]`, `button.option` (`option--active`), `button.overflow-button` | categories buttons with `aria-current`; checkbox named by the heading without its badge; options `aria-pressed`; "⋮" named |
| Failures | `div.manual-failure-ata > div.manual-failure-ata-title` (chevron), `div.manual-failure-group > div.manual-failure-group-title` "CPC" + `button.manual-failure-item` "1" | chapter title button with `aria-expanded`; item named "CPC 1" |
| Documents | `div.folder-name`, `div.file` | buttons, `aria-expanded` on folders |
| Charts | `div.navigraph-sidebar-item`; `i.fa-plus-circle`/`fa-minus-circle`; `div.map-control-item` holding an `img` with a title | buttons named from text, decision 5, or the picture's title |
| Quick settings | `mat-slider.brightness-popup-slider`; `button.quick-settings-btn` holding `img[src*=airplane]` | named (decision 5) |
| Notices | `.loading-notification .notification-content` with `.progress-bar[value]`; `fap-notification`, `no-data-notification` | `aria-live="polite"`; `role=progressbar` with `aria-valuenow` |
| MCDU, FAP | `#/mcdu` (two canvases, title "FenixSim MCDU"); `#/fap/screen-saver` (title "FenixSim FAP") | caught by the window (decision 8) |

## File structure

| File | Responsibility |
|---|---|
| `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs` | The window: address, status box, loads and injects the layer, catches dead ends |
| `MSFSBlindAssist/Forms/Fenix/FenixEfbRoutes.cs` | Pure: which EFB route a URL is, and what to say at a dead end |
| `MSFSBlindAssist/Resources/fenix-efb-accessibility.js` | The layer |
| `tools/fenix-efb-test/` | `package.json`, `load.js` (fixture + layer in jsdom), `capture.md` (the capture snippet), `fixtures/*.html`, `*.test.js` |
| `tests/MSFSBlindAssist.Tests/FenixEfbAddressTests.cs`, `FenixEfbRoutesTests.cs`, `FenixEfbLayerTests.cs` | xUnit for the C# side and the script's guardrails (CI runs these; it does not run Node) |

---

### Task 1: Window address and status message (moved from the displays plan)

**Files:**
- Modify: `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs` (class comment 8-21; `EfbUrl` line 24; `_errorLabel` 33, 90-96, 104; `NotReachableMessage` 109-111; `ShowError` 186)
- Create: `tests/MSFSBlindAssist.Tests/FenixEfbAddressTests.cs`

**Interfaces:** Produces `internal const string EfbUrl` and `internal const string NotReachableMessage` on `FenixEFBForm`.

- [ ] **Step 1: Write the failing tests.**
  - `The_efb_page_is_the_external_target_on_ipv4`: `Assert.Equal("http://127.0.0.1:8083/?target=external#/efb", FenixEFBForm.EfbUrl)`; `var u = new Uri(FenixEFBForm.EfbUrl);` `u.Host == "127.0.0.1"`, `u.Query == "?target=external"`, `u.Fragment == "#/efb"`.
  - `The_not_reachable_message_names_the_address_it_tried`: `Assert.Equal("Fenix EFB is not reachable at http://127.0.0.1:8083. Make sure the Fenix A320 is fully loaded, then press Retry.", FenixEFBForm.NotReachableMessage)`.
  - `The_window_never_addresses_the_gateway_by_name_and_shows_no_label`: source scan of `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs` (via `ClaudeContextBudgetTests.RepoRoot()`, as `FenixDataServiceAddressTests` does): no match for `(?i)(ws|http)s?://localhost`, no `new Label`, and no "already screen-reader accessible".
- [ ] **Step 2: Run them; they fail.** `--filter FenixEfbAddressTests`.
- [ ] **Step 3: Implement.** The two constants as `internal const` (comment: decisions 1 and 2). Replace `_errorLabel` with `_errorBox`, a read-only multiline `TextBox` built like `IFlyEfbForm`'s (`ReadOnly = true`, `TabStop = true`, `Multiline = true`, `ScrollBars = Vertical`, `AccessibleName = "Fenix EFB status message"`, location 12,12, size 700×100), Retry below it; `ShowError` sets `_errorBox.Text`; focus and speech unchanged. Rewrite the class comment: the v3 page is NOT accessible on its own (spec 8); this window adds the layer and catches the dead ends.
- [ ] **Step 4: Run them and the whole suite; all pass. Build the solution.**
- [ ] **Step 5: Commit.** `fix(fenix): EFB window loads the external target on 127.0.0.1; status text tabbable`

### Task 2: Catch the MCDU and FAP dead ends

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixEfbRoutes.cs`, `tests/MSFSBlindAssist.Tests/FenixEfbRoutesTests.cs`
- Modify: `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs` (`InitWebViewAsync`: subscribe `SourceChanged`)

**Interfaces:**
- Produces: `internal enum FenixEfbRoute { Efb, Mcdu, Fap, Other }`; `internal static class FenixEfbRoutes` with `FenixEfbRoute Classify(string? url)` (by the fragment's first segment: `#/mcdu…` Mcdu, `#/fap…` Fap, `#/efb…` or an empty fragment on the gateway's origin Efb, anything else Other; host compare ordinal-ignore-case) and `string? DeadEndMessage(FenixEfbRoute route)` (decision 8's two lines exactly; null for Efb and Other).

- [ ] **Step 1: Write the failing tests.**
  - `Dead_ends_are_recognised`: `Classify("http://127.0.0.1:8083/#/mcdu")` and `…/#/mcdu?cdu=1&fullscreen=false` → Mcdu; `…/#/fap/screen-saver` and `…/#/fap/cabin-status` → Fap.
  - `Efb_routes_are_left_alone`: `…/?target=external#/efb`, `…/#/efb/fenix/ground-services`, `…/#/efb/settings`, `…/` → Efb; `https://support.fenixsim.com/#/mcdu` → Other; `null` and `""` → Other.
  - `Each_dead_end_says_where_to_go`: the two exact strings; `DeadEndMessage(Efb)` and `(Other)` null.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixEfbRoutesTests`.
- [ ] **Step 3: Implement `FenixEfbRoutes`,** then in `FenixEFBForm.InitWebViewAsync` subscribe `CoreWebView2.SourceChanged` (it fires for in-page fragment changes, `IsNewDocument` false): on Mcdu or Fap, `_webView.CoreWebView2.Navigate(EfbUrl)` and `_announcer.AnnounceImmediate(message)`. Log the catch with `Log.Debug("FenixEFB", …)` (CORE-15).
- [ ] **Step 4: Run them and the suite; all pass. Build.**
- [ ] **Step 5: Commit.** `fix(fenix): EFB window returns from the unreadable MCDU and FAP pages and says where to go`

### Task 3: The offline harness, the captures and the sweep

**Files:**
- Create: `tools/fenix-efb-test/package.json` (`"test": "node --test"`; dependencies `jsdom` `^24.1.0` as `tools/md11-efb-test`, and `dom-accessibility-api`), `tools/fenix-efb-test/load.js`, `tools/fenix-efb-test/capture.md`, `tools/fenix-efb-test/fixtures/*.html`, `tools/fenix-efb-test/sweep.test.js`
- Create: an empty layer `MSFSBlindAssist/Resources/fenix-efb-accessibility.js` (the guard and nothing else), so the sweep runs and fails

**Interfaces:** `load.js` exports `async function load(fixtureName, { layer = true } = {})` returning the jsdom `window`: reads `fixtures/<name>.html`, builds `new JSDOM(html, { runScripts: "outside-only", url: "http://127.0.0.1:8083/?target=external#/efb" })`, evaluates `../../MSFSBlindAssist/Resources/fenix-efb-accessibility.js` in it when `layer`, then awaits two macrotask turns so the observer's pass has run. Tests use `computeAccessibleName` and `getRole` from `dom-accessibility-api`.

- [ ] **Step 1: Capture the fixtures** (Fenix loaded, any variant). In any Chromium browser open `http://127.0.0.1:8083/?target=external#/efb`, go to each screen below by mouse, and in the developer console run the snippet from `capture.md`, then paste the clipboard into the fixture file:
  ```js
  copy((() => { const c = document.body.cloneNode(true);
    c.querySelectorAll('script,style,link,canvas,svg,img,#seatContainer,.leaflet-pane').forEach(n => n.remove());
    c.querySelectorAll('[value]').forEach(n => n.removeAttribute('value'));
    return '<!doctype html><html><body>' + c.innerHTML + '</body></html>'; })())
  ```
  Screens and names: `home`, `switcher` (an app open, the top-bar arrow pressed), `my-flight`, `ground-services`, `mass-balance`, `panel-states` (its pop-up open; close it with Escape afterwards, pressing nothing), `depperf`, `arrperf`, `settings` (Aircraft category), `failures` (ATA 21 open), `documents`, `charts`, `quick-settings`. The snippet removes pictures, so add back by hand, in `home` and `switcher` only, one `<img class="app-img">` per tile where it stood (the layer needs the elements, not the pictures). Search `settings.html` for credential values (Global Constraints). Write in `capture.md` the date, the Fenix version and the aircraft.
- [ ] **Step 2: Write `sweep.test.js`:** for every fixture, after `load(name)`, every element that is a `button`, `a[href]`, `input` (not hidden), `[role=switch|checkbox|radio|combobox|button|tab|slider]`, `[tabindex]` or `div.appIcon` has a computed name that is non-empty, is not only dashes, slashes and spaces, and is not `on` or `x`; the failures list names each offending element by its tag and class. Also `Without_the_layer_the_page_fails_the_sweep`: the same check with `{ layer: false }` on `ground-services` finds at least 14 offenders (proves the harness sees today's problem).
- [ ] **Step 3: Run it; the sweep fails on every fixture** (the layer is empty). `cd tools/fenix-efb-test && npm install && node --test`.
- [ ] **Step 4: Commit** (the sweep stays red until Task 7). `test(fenix): offline EFB harness, live captures and the every-control-named sweep`

### Task 4: The layer's core, navigation, and shipping it

**Files:**
- Modify: `MSFSBlindAssist/Resources/fenix-efb-accessibility.js`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` (one `<None Update="Resources\fenix-efb-accessibility.js"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`, beside the other `Resources\*.js` entries)
- Modify: `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs` (`InitWebViewAsync`: inject the layer before `Navigate`)
- Create: `tools/fenix-efb-test/core.test.js`, `tools/fenix-efb-test/navigation.test.js`, `tests/MSFSBlindAssist.Tests/FenixEfbLayerTests.cs`

**Interfaces (inside the script; later tasks add rules to `RULES`):**
- `textOf(el)`: the element's visible text with `<br>` and block boundaries as single spaces, collapsed and trimmed, skipping `[aria-hidden=true]`.
- `name(el, text)`: sets `aria-label` unless the page set a non-empty one, or `text` is empty.
- `button(el, text)`: `role=button` unless it has a role, `tabindex=0` unless focusable, `name(el, text)`, and once (`data-msfsba`) a `keydown` handler: Enter or Space → `preventDefault()` and `el.click()`, never when the event target is an `input`, `textarea` or `select`.
- `RULES`: an array of `function (root)`; `pass()` runs each in its own `try/catch` (a failing rule never stops the others or throws into the page); a `MutationObserver` on `document.body` (`childList`, `subtree`, `attributes` with `attributeFilter: ["class"]`) schedules one `pass()` per 50 ms.
- Alt+Home: a capture-phase `keydown` on `window`: `location.hash = "#/efb"`, `preventDefault()`.
- C#: the form reads `Path.Combine(AppContext.BaseDirectory, "Resources", "fenix-efb-accessibility.js")` once; missing or unreadable → `Log.Warn("FenixEFB", …)` and the page loads without it.

- [ ] **Step 1: Write the failing tests.**
  - `core.test.js`: `textOf` folds `Resend<br>Loadsheet` to "Resend Loadsheet"; `Second_pass_changes_nothing` (run `pass()` twice on `home`: same attributes, one listener per element, counted by wrapping `addEventListener`); `Late_elements_are_fixed` (append a new `div.appIcon` with a `span.appTitle` "Test" after load; after two turns it is a named button); `Unknown_page_is_left_alone` (a fixture of plain unrelated markup: no attribute added, nothing thrown); `Keys_press_buttons_but_not_text_boxes` (Enter on a tile calls its click once; Space in an `input` does not; Alt+Home from inside an `input` sets `location.hash` to `#/efb`).
  - `navigation.test.js`: on `home`, the 9 tiles are role `button` with names "Fenix", "Takeoff", "Landing", "Charts", "Documents", "Failures", "Settings", "MCDU", "FAP" and `tabIndex` 0; "Open quick settings" keeps its own name; on `switcher`, each `img.app-img` (in its clickable parent) is a button named by the tiles' titles in order and `#homeButtonFree` is a button named "Home".
  - `FenixEfbLayerTests` (xUnit, so CI guards it): the script file exists and starts its IIFE with the `__msfsbaFenixEfb` guard; it contains none of `fetch(`, `XMLHttpRequest`, `WebSocket`, `.remove(`, `removeChild`, `innerHTML =`, `.value =`; the csproj has a `None Update="Resources\fenix-efb-accessibility.js"` with `PreserveNewest`; `FenixEFBForm.cs` calls `AddScriptToExecuteOnDocumentCreatedAsync` with the text read from `"fenix-efb-accessibility.js"`.
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement the core, the home/switcher/home-button rules, Alt+Home, the csproj entry and the injection.**
- [ ] **Step 4: Run both suites; the new tests pass (the sweep still fails on the other fixtures). Build the solution.**
- [ ] **Step 5: Commit.** `feat(fenix): EFB accessibility layer: tiles, switcher and home are buttons; Alt+Home`

### Task 5: The Fenix app (My Flights, Ground Services, Mass and Balance, Panel States) and notices

**Files:** Modify the script; Create `tools/fenix-efb-test/fenix-app.test.js`.

- [ ] **Step 1: Write the failing tests.**
  - `Ground_services_switches_are_named_by_their_text`: on `ground-services`, the 14 `input[role=switch]` names are exactly, in page order, the 14 `div.text` texts ("GPU", "PCA", "ENGINE FAN HEATING", "CHOCKS & CONES", "FWD STAIRS", "AFT STAIRS", "1 left", "1 right", "2 left", "2 right", "fwd cargo", "aft cargo", "bulk", "CONNECT TUG" as captured); the three arrows are buttons "Pushback left", "Pushback straight", "Pushback right".
  - `Mass_and_balance_boxes_say_what_they_hold`: the four `input.planned-input` names are "Passengers Planned, Max 180", "Cargo Planned, Max 19,460", "Fuel Planned, Max 41,897", "Planned Weights Zero Fuel Weight, Max 134,482" (section title before its first parenthesis, column header, placeholder; use the captured numbers); buttons read "Reset All", "Resend Loadsheet", "Load Aircraft", "Send To MCDU" (page capitals kept as captured).
  - `My_flight_cards_are_buttons`: the three "TAP TO IMPORT FROM SIMBRIEF" zones are buttons named "<card title>: Tap to import from SimBrief" (card titles "A320 FLIGHT", "ROUTE", "WEATHER" as captured); the tab-bar arrows are "Previous tabs", "Next tabs".
  - `Panel_states_popup_has_a_title`: on `panel-states`, the `mat-dialog-container` has role `dialog` and name "Panel states"; its four buttons keep their text.
  - `Loading_notices_are_spoken`: in a fixture `notices.html` written by hand from the bundle's markup (`.loading-notification > .notification-content` with a `.progress-bar` `value="40"`, a `.fap-notification`, a `.no-data-notification`), each notice has `aria-live="polite"`, the bar is `role=progressbar` with `aria-valuenow="40"`, and changing `value` to 60 then a pass updates `aria-valuenow`.
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement the rules.** Section and column texts come from the page (the section's leading text node, the column header row's cell at the input's column).
- [ ] **Step 4: Run; they pass.**
- [ ] **Step 5: Commit.** `feat(fenix): EFB layer names Ground Services, Mass and Balance and Panel States`

### Task 6: Takeoff and Landing

**Files:** Modify the script; Create `tools/fenix-efb-test/performance.test.js`.

- [ ] **Step 1: Write the failing tests.**
  - `Every_takeoff_field_is_named_by_its_label`: on `depperf`, each `input` and `mat-select` is named by the `.departure-label` in its own container ("ICAO CODE", "RUNWAY", "RUNWAY SURFACE", "FLAP CONFIG", "FORCE TOGA", "ANTI- ICE" as captured, "PACKS", "TOW (LBS)", "MACTOW (%)", "WIND", "TEMPERATURE", "ALTIMETER (INHG)"); a field without its own label takes the column header above it, else its unit text, never its placeholder.
  - `Takeoff_results_read_label_and_value`: in `.mcdu-screen`, each value cell is named "<label above>: <value>" ("V1: not computed" where the value is `small-yellow-box` spans, "FLP RETR: F=---", "STOP MARGIN: ----"); the label rows are not read twice (`aria-hidden="true"` on a label cell only when its value cell carries the label).
  - `Runway_condition_is_a_radio_group_that_follows_the_page`: on `arrperf`, the six codes are role `radio` in a `radiogroup` named "Runway condition", names "6 DRY" … "1 POOR", `aria-checked` true only on `section-selected`; moving `section-selected` to "3 MED" and a pass moves `aria-checked`.
  - `Landing_options_and_procedures_are_buttons`: the five `.one-click-change` are buttons named by their text; the nine `.arrival-procedure-item` are buttons with Tab stops, names as captured.
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement the rules.**
- [ ] **Step 4: Run; they pass.**
- [ ] **Step 5: Commit.** `feat(fenix): EFB layer names the takeoff and landing calculators`

### Task 7: Settings, Failures, Documents, Charts and quick settings

**Files:** Modify the script; Create `tools/fenix-efb-test/other-apps.test.js`.

- [ ] **Step 1: Write the failing tests.**
  - `Settings_options_say_name_and_choice_and_follow_the_page`: each `article`'s checkbox is named by its heading without the badge ("A/C ESS Auto Switching", not "… Default"); each `button.option` has `aria-pressed` true only with `option--active` ("ADIRS" true, "GNADIRS" false as captured), and moving the class flips it after a pass; each "⋮" is "More options for <setting>"; the categories are buttons with Tab stops and `aria-current="page"` only on `category--active`.
  - `Failure_chapters_open_and_items_name_their_group`: on `failures`, each `.manual-failure-ata-title` is a button with `aria-expanded` true for the open chapter (ATA 21) and false for the others; items are named "<group title> <item>" ("CPC 1", "CPC 2", "Overheat Pack 1", "Regulator Fault Pack 1"), an item with no group title keeps its own text ("Lavatory/Galley fan").
  - `Documents_folders_and_files_are_buttons`: `.folder-name` and `.file` are buttons with Tab stops; folders carry `aria-expanded`.
  - `Charts_and_quick_settings_controls_are_named`: the five sidebar items are buttons named by their text; zoom "Zoom in", "Zoom out"; each `.map-control-item` named by its picture's title; the slider "Brightness"; the airplane button "Airplane mode".
- [ ] **Step 2: Run them; they fail.**
- [ ] **Step 3: Implement the rules.**
- [ ] **Step 4: Run both suites; everything passes, the sweep included, on every fixture.** Build the solution.
- [ ] **Step 5: Commit.** `feat(fenix): EFB layer names Settings, Failures, Documents, Charts and quick settings`

### Task 8: Owner in-sim checks and hand back

- [ ] **Step 1:** Build, both suites green; `git diff --name-only <base-sha>` lists only owned files.
- [ ] **Step 2:** Run the owner checks below with the owner (NVDA); record each heard phrase under "Results".
- [ ] **Step 3:** Fill in the handoff section, set the **Status** line at the top of this file to "done", push.

## Owner in-sim checks (few, prioritised)

Build from this branch; the Fenix loaded on the ground; NVDA running; app on the Fenix profile. Open the window with input mode, then Shift+T.

1. **Getting around.** Tab from the top: hear "Open quick settings, button", then "Fenix, button" through "FAP, button". Enter on "Fenix" opens it; Alt+Home returns to the tiles. The "Home" button is found in browse mode and works.
2. **Ground Services.** Fenix > Ground Services: arrowing down reads "GPU, switch, on" (or off) and every door by name. With the owner's go-ahead, Space on GPU toggles it and the screen reader says the new state; put it back.
3. **Takeoff.** Takeoff: each box and list says its name ("ICAO CODE, edit"). Type the departure airport, pick a runway, Calculate (a calculation in the EFB, nothing sent): the results read "V1: …", "VR: …", "V2: …".
4. **Settings.** Settings: an on/off option says its own name ("AP/FD TCAS, check box, checked"); on the ADIRS option, the selected picture says "pressed".
5. **Dead ends.** Press the MCDU tile: back on the tiles, hear "The EFB's MCDU can't be read with a screen reader. Use the MCDU window instead: input mode, then Shift+M." Same for FAP with its line.
6. **Status message.** Before the Fenix has loaded (main menu), open the window: hear "Fenix EFB is not reachable at http://127.0.0.1:8083. Make sure the Fenix A320 is fully loaded, then press Retry."; Shift+Tab from Retry reaches the message.

## Handoff to plan 12

**Pilot-facing changes** (guide: Fenix chapter, the EFB section; `guide/planning-your-flight.md:491`; changelog `fix`):
- The Fenix EFB window works with a screen reader again on Fenix v3: every app is a button you can Tab to, every switch, box and list says its name, selections and open chapters say their state, and Alt+Home goes back to the EFB's home screen from anywhere.
- The EFB's own MCDU and FAP pages can't be read with a screen reader: the window brings you back and tells you to use the MCDU window or the Fenix windows list.
- The Charts map stays pictures only.
- The window's "not reachable" message says http://127.0.0.1:8083 and can be reached with Tab.
- Changelog draft: "Fenix: the EFB window reads properly on Fenix v3 (switch and box names, selections, a Home key with Alt+Home), and leaves the unreadable MCDU and FAP pages for the app's own windows."

**Handoff requests:**
- H1. `docs/invariants/a32nx-fenix.md` A320-40, last bullet: replace "Not covered by this rule: the Fenix EFB window (…)" with "The Fenix EFB window uses the same address (FNX rule below)."
- H2. `guide/planning-your-flight.md:491`: the new message text (Task 1 Step 1).
- H3. `docs/a32nx.md`, the Fenix EFB section: no longer "already accessible"; the layer, the captures and how to re-take them after a Fenix update (`tools/fenix-efb-test/capture.md`); the dead ends.
- H4. `docs/tooling.md` Node line: add `tools/fenix-efb-test/` to the jsdom harnesses.

**Proposed rule lines** (`.claude/rules/fenix-data.md`, whose plan 01 globs cover `Forms/Fenix/**` and `Resources/fenix-*.js`; next free FNX numbers; full texts from this plan's decisions and Evidence):
```text
- [FNX-?] `FenixEFBForm` loads http://127.0.0.1:8083/?target=external#/efb, the target in the query before the #: a sim target (sim-capt, sim-fo, msfs2024-efb) swallows Tab and PageUp/PageDown/Home/End and drops focus 2 s after a click, and localhost pays the ::1 refusal ([A320-40]). Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] The EFB layer (fenix-efb-accessibility.js, injected by `FenixEFBForm`) only adds roles, names, states, Tab stops and Enter/Space to the page's own click: never a request, a removed element or a written value. Names come from the page's text; re-take the tools/fenix-efb-test captures after every Fenix update. Full: docs/invariants/fenix-data.md#fnx-?
```

**Follow-ups (not planned):** the top-bar status icons (signal, link) stay unnamed; a native readout of the takeoff results if the owner finds the grid slow to read.

## Results

(Filled in by the implementer: date, aircraft, each check's heard phrases, decisions taken.)
