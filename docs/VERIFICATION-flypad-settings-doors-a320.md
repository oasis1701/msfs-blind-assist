# A320 Verification — flyPad Settings accessibility + door open/closed

> **⚠️ TEMPORARY DOC — delete after the A320 verification passes.** This was written so a
> collaborator (and their Claude) can independently confirm, on the FlyByWire **A32NX**,
> the flyPad work that was built and verified on the **A380X** in the 2026-06-06 session.
> Once the checklist below is green on the A320, delete this file (and the now-stale
> verification notes in `tools/_probe/settings-accessibility-audit.md`).

---

## 0. TL;DR — what to verify and why it should "just work"

Three accessibility changes were made to **one shared file**,
`MSFSBlindAssist/Resources/coherent-flypad-agent.js` (the in-page DOM agent that the
flyPad EFB window scrapes through). The agent is **shared by the A380X and the A32NX** —
there is no per-aircraft branch in it. All three changes key on the **shared `fbw-common`
React components** (`SettingItem`, `SelectGroup`, `Toggle`, `SimpleInput`,
`GroundServiceButton`), so they *should* behave identically on the A320. They were only
ever run live on the A380, so the A320 needs a confirming pass.

| # | Change | Where | A320 risk |
|---|---|---|---|
| 1 | **Settings pages** read cleanly (name + tight options, labeled inputs, no doubled page label, no false "(selected)", Audio rc-slider phantoms gone, About preserved) | `buildSettingsLines` + `pageLabel` + settings-index `(selected)` gate | Low — shared components; watch the deferred items in §5 |
| 2 | **flyPad doors** read **open / closed / in transit** (from the tile colour) instead of "active"/silence | `doorOpenState` + door block in `enumerate` | Low — same `ServiceButtonState` colours |
| 3 | **Doors keep their precise name + grouping in flight** (when the tile is a disabled heading, not a button) | fiber-aware `doorIdentity` + `orderGroundServices` heading-doors | Medium — fiber path is live-only, never unit-tested |

The full design + audit live in (gitignored, local to the original machine)
`docs/superpowers/specs/2026-06-05-flypad-settings-accessibility-design.md` and
`docs/superpowers/plans/2026-06-05-flypad-settings-accessibility.md`. You do **not** need
them to verify — this doc is self-contained.

---

## 1. Mental model (read this first, Claude)

- **Transport.** The flyPad runs in MSFS's **Coherent GT** (Chromium 49) view, exposed by
  the sim's remote debugger on `http://127.0.0.1:19999`. The app's `CoherentEFBClient.cs`
  connects to the `- EFB` view, injects `coherent-flypad-agent.js` once, then calls
  `scrape()` / `clickElement(idx)` / `setValue(idx,text)`. The `FBWA380EFBForm` WebView2
  window renders the scraped element list as an accessible HTML document. The **same form
  + agent serve the A320** (dispatched by `AircraftCode == "A320"`).
- **ES5 only.** The agent runs in Chromium 49: `var`, no arrow functions, no
  `String.includes` (use `indexOf`), top-level try/catch. A scraper bug returns an error
  string; it must never throw into the flyPad.
- **SINGLE CONNECTION.** Coherent allows **one** debugger socket per view. The dev tool
  `tools/coherent-eval.ps1` and the app's `CoherentEFBClient` **cannot both be connected
  to `- EFB` at once.** To drive the page with the tool, **close the app's flyPad form
  first** (Escape). To test through the app's own window, close the tool. The human's
  NVDA pass uses the app window; your (Claude's) live pass uses the tool.
- **Two ways to verify**, both needed:
  1. **Offline jsdom harness** — `tools/flypad-settings-test/` replays *captured* live DOM
     (geometry baked into `data-rect`/`data-vis`) so the scrape logic runs without the
     sim. Fast, deterministic. **Cannot test the fiber-based door naming** (jsdom has no
     React fibers) — that part is live-only.
  2. **Live scrape** — `tools/coherent-eval.ps1` against the running A320.

---

## 2. Files changed this session (the review scope)

- `MSFSBlindAssist/Resources/coherent-flypad-agent.js` — the only product code change.
  New: `buildSettingsLines`, `settingsContentRoot`, `settingsBackLink`, `settingUnits`,
  `settingUnitHasControl`, `emitSettingsRows`, `emitSettingInput`, `selectGroupOptions`,
  `toggleNode`, `emitSettingsControl`, `emitAudioRow`, `emitSettingsLink`, `textItem`,
  `doorOpenState`. Edited: `doorIdentity` (now reads the React fiber), the door block +
  `(selected)` gate + `pageLabel` in `enumerate`, and `orderGroundServices` (heading-doors).
- `tools/flypad-settings-test/` — jsdom harness + tests + 11 captured A380 fixtures (NEW).
- `tools/_probe/_capture_flypad_fixture.js`, `_settings_scrape.js`, `_click_by_text.js`,
  `_settings_click.js`, `_door_inspect.js`, `_door_fiber.js` — probe scripts used here.
- `tools/_probe/settings-accessibility-audit.md` — the original A380 audit (also temporary).

The exe was NOT recompiled by these changes (only a JS resource changed). To run the app
with the new agent, copy the file into the build output and **restart the app** (see §6).

---

## 3. STEP 1 — Offline confidence (no sim needed)

```bash
cd tools/flypad-settings-test
npm install        # first time only (jsdom)
node --test        # expect: pass 23, fail 0
```

This replays the **A380** fixtures and asserts the cleaned output. Green here means the
scrape logic is intact. It does **not** prove the A320 — for that, §4.

To eyeball any A380 page's cleaned output: `node run.js aircraft` (or `simoptions`,
`realism`, `thirdparty`, `atsu_aoc`, `audio`, `flypad`, `about`, `index`, `ground`,
`calibrate`).

---

## 4. STEP 2 — Live A320 verification

**Prerequisites:** MSFS running, **A32NX loaded**, flyPad powered on. For the tool pass,
**close the app's flyPad form** (the single-connection rule).

### 4a. Sanity: can the tool see the A320 flyPad?

```bash
cd tools/_probe
powershell -ExecutionPolicy Bypass -File ../coherent-eval.ps1 -Title "- EFB" \
  -PreFile ../../MSFSBlindAssist/Resources/coherent-flypad-agent.js \
  -ExprFile _settings_scrape.js
```

Expect a JSON blob starting `{"ok":true,"page":"...","elements":[...]}`. If you get
`NO_PAGE_MATCHING '- EFB'`, the flyPad view isn't up (open the EFB in the cockpit) or the
app's flyPad form is holding the socket (close it).

### 4b. Settings — navigate all categories and read the cleaned output

The Settings page is an **index of category links**; each opens a detail sub-page whose
top-left title is the **back** link. To tour them with the tool, use the text-clicker
helper (it re-scrapes, then clicks the first element whose text matches):

```bash
# in tools/_probe — define once
CE="../coherent-eval.ps1"; AG="../../MSFSBlindAssist/Resources/coherent-flypad-agent.js"
clickt(){ sed "s/NEEDLE_PLACEHOLDER/$1/" _click_by_text.js > _ct.js; \
  powershell -ExecutionPolicy Bypass -File "$CE" -Title "- EFB" -PreFile "$AG" -ExprFile _ct.js; }
dump(){ powershell -ExecutionPolicy Bypass -File "$CE" -Title "- EFB" -PreFile "$AG" \
  -ExprFile _settings_scrape.js; }

clickt "Settings"          # nav-rail Settings (NOTE: also matches a back-link "Settings - X")
clickt "settings (current" # safest way to reach the INDEX from a detail page
clickt "aircraft options"  # open a category by its visible text
dump                       # read its cleaned lines
```

**What CORRECT looks like (apply to every category):**
- Page label is **not** doubled — `"Settings - <Category>"`, never `"Settings: Settings - <Category>"`.
- First line is **"Back to Settings"** (a link).
- Each setting reads as a **name line then its option buttons**, e.g.
  `text "ADIRS Align Time"` then `button "Instant"`, `button "Fast (selected)"`,
  `button "Real"` — exactly **one** option marked `(selected)`.
- **Toggles** read `toggle "<name>" = true|false` (controlType checkbox).
- **Inputs** read `input "<name>" = <value>` — the name comes from the setting's OWN row,
  and the value is **not** duplicated inside the label.
- **Sub-page/action links** read `"<setting>: <verb>"`, e.g. `"Automatic Call Outs: Select"`.
- On the **index**, NO category reads `(selected)` (they're a static list).

**A320 specifics (don't be alarmed):** the A320's Settings categories are NOT identical to
the A380's. A320-only / A380-only settings differ (e.g. ISIS Metric Altitude, US Units,
Auto Cabin Lighting, OANS Performance Mode are A380-only). That's *absence*, not breakage —
the **behaviour** (clean name+options, labeled inputs, no doubled label, no false
"(selected)", About bails to a plain read) must match. The throttle **Calibrate** sub-page
(Sim Options → Throttle Detents → Calibrate) must still read with its existing axis labels
("Axis N Current Value", "Axis N detent low/high") — the Settings builder explicitly bails
on it.

**Audio page specifically:** must show **one labeled control per volume** (e.g.
`input "Master Volume" = 50`) and **zero** lines reading just `"Settings - Audio"` (those
were the rc-slider phantoms; they must be gone).

### 4c. Doors — the headline A320 check

Navigate to **Ground** (nav-rail) → it opens on the **Services** sub-tab. Scrape:

```bash
clickt "Ground"
powershell -ExecutionPolicy Bypass -File "$CE" -Title "- EFB" -PreFile "$AG" \
  -ExprFile _settings_scrape.js
```

**What CORRECT looks like on the A320 (door enums map via the agent's `DOOR_NAMES`):**
- Door tiles read **`"<Door name> (closed)"`** with the precise A320 names:
  `Forward Left Door`, `Forward Right Door`, `Aft Left Door`, `Aft Right Door`,
  `Cargo Door`. (Source: `cabinleftdoor`/`cabinrightdoor`/`aftleftdoor`/`aftrightdoor`/
  `cargodoor` in `A320_251N/A320Services.tsx`, mapped in `A.DOOR_NAMES`.)
- The door tiles are **grouped contiguously**, ahead of the other services (GPU, Jet
  Bridge, Stairs, Fuel/Baggage/Catering trucks).
- **On the ground**, open a door (click the tile / use the in-cockpit door) — its line must
  flip to **`"<Door name> (open)"`** (green tile). Close it → back to `(closed)`. Amber
  mid-move reads `(in transit)`.

**This is the part most worth a careful look**, because the precise-name-while-disabled
path reads the door enum from the React **fiber** (FBW keeps the `onClick` closure as a
prop even when it removes it from the DOM in flight). It was verified on the A380 in flight;
confirm the A320's `GroundServiceButton` exposes the same fiber `onClick`. Quick probe:

```bash
powershell -ExecutionPolicy Bypass -File "$CE" -Title "- EFB" -PreFile "$AG" \
  -ExprFile _door_fiber.js
```

Expect each door to report a non-empty `fiberOnClickEnum` (e.g. `CabinLeftDoor`,
`AftRightDoor`, `CargoDoor`). If a door reports `""`, the precise name won't resolve in
flight for that door — note which one and we'll extend `DOOR_NAMES` / the fiber walk.

### 4d. Human NVDA pass (close the tool first!)

Open the app's flyPad form (**INPUT mode → Shift+T**), arrow through Settings and the
Ground page with NVDA. Confirm the readout matches §4b/§4c: clean settings, doors announce
open/closed with precise names, doors grouped. This is the real acceptance test.

---

## 5. A320-specific risks flagged in code review (verify or rule out)

These were deferred from the A380 review because they could only be confirmed on a real
A320. If §4 reads cleanly, they're fine; if something is off, start here:

1. **`selectGroupOptions` is `<span>`-only.** It collects segmented options as
   `span.cursor-pointer` leaves. If an A320 SelectGroup renders options as `div`/`button`
   instead, the group would mis-read (options dropped or the setting falls through to the
   input/link branch). **Check:** every segmented A320 setting (e.g. Default Barometer
   Unit, ATIS source, Theme, Time Displayed) shows its options as buttons with exactly one
   `(selected)`. If options are missing → widen `selectGroupOptions` to accept the actual
   tag.
2. **`settingsContentRoot` left-threshold (`rect.left > 100`).** It finds the settings rows
   wrapper (`div.divide-y-2`) to the right of the category column. If the A320 layout puts
   that wrapper at a different x, the builder could miss it (Settings would read via the
   generic pass — cluttered but not broken). **Check:** A320 Settings detail pages show
   "Back to Settings" + clean rows (builder fired), not the old cluttered output.
3. **`toggleNode` reuses `classify()`'s toggle rule** (`rounded-full + cursor-pointer +
   w-14`). If the A320 Toggle uses a different width class, toggles would be missed (read
   as a bare name). **Check:** A320 toggles read `toggle "<name>" = true|false`.
4. **Graceful degrade (safety net).** Even if 1–3 are wrong on some page,
   `buildSettingsLines` returns `null` (deferring to the generic pass) when it finds **no
   recognizable control** in the region — so a mismatch degrades to the old readable output
   rather than a blank page. Confirm no Settings page reads **blank** (only "Back to
   Settings"); if one does, that's the gate misfiring — capture a fixture (§7) and report.

---

## 6. Running the APP (not just the tool) with the new agent

The app loads the agent from its build output, once per Coherent connection. After pulling
the branch:

```powershell
# Build the SOLUTION (never the bare csproj — that goes to the AnyCPU bin\Debug the app
# doesn't run from). Close the app first if it's running (the exe is file-locked).
dotnet build MSFSBlindAssist.sln -c Debug
```

The build copies `Resources/coherent-flypad-agent.js` into
`bin\x64\Debug\net9.0-windows\win-x64\Resources\`. **Restart MSFSBlindAssist** so
`CoherentEFBClient` re-reads the agent. (Swapping aircraft or reopening the flyPad form
also re-injects it.)

---

## 7. Locking the A320 in (do this once it reads correctly)

Capture A320 fixtures into the harness so the A320 has its own regression coverage:

```bash
cd tools/_probe
CE="../coherent-eval.ps1"; AG="../../MSFSBlindAssist/Resources/coherent-flypad-agent.js"; FIX="../flypad-settings-test/fixtures"
# with the A320 flyPad on the page you want, and the app's flyPad form closed:
powershell -ExecutionPolicy Bypass -File "$CE" -Title "- EFB" -PreFile "$AG" \
  -ExprFile _capture_flypad_fixture.js | sed -n '/</,$p' > "$FIX/a320-aircraft.html"
```

`_capture_flypad_fixture.js` stamps live geometry (`data-rect`) + visibility (`data-vis`)
onto every element so the static HTML replays faithfully in jsdom. Capture each Settings
category + the Ground page (verify the title/content after each navigate — navigation can
lag; re-capture if the title is wrong). Then add A320 assertions to
`tools/flypad-settings-test/settings.test.js` / `doors.test.js` mirroring the A380 ones.
**Caveat:** the fiber-based door *naming* still can't be jsdom-tested (no fibers); the
captured A320 ground fixture will show generic FBW labels offline — verify the precise
names **live** (§4c) and keep the grouping/`(closed)` assertions for the fixture.

---

## 8. Sign-off checklist

> **✅ CLAUDE-SIDE VERIFICATION COMPLETE (2026-06-06, live on the A320neo FlyByWire at VCBI).**
> Every automated/tool box below is green; only the **human NVDA pass** remains.

- [x] `node --test` in `tools/flypad-settings-test/` → **39/39 pass** (23 A380 + 16 new A320).
- [x] A320 Settings: every category reads clean (name+options, labeled inputs, no doubled
      label, no false "(selected)"); Audio has no phantom sliders; About is readable;
      Calibrate link present/unregressed. *(Toured all 8 categories live + offline fixtures.)*
- [x] A320 doors: read `(open)` / `(closed)` / `(in transit)` with **precise A320 names**
      (Forward/Aft Left/Right Door, Cargo Door), **grouped** together. *(Live cycle verified:
      closed → open → in transit → closed; offline fixture asserts state+grouping since the
      precise names are fiber/live-only.)*
- [x] `_door_fiber.js` shows a non-empty enum for every A320 door (CabinLeftDoor, AftLeftDoor,
      CabinRightDoor, CargoDoor, AftRightDoor — 0 fiber hops).
- [ ] Human NVDA pass through Settings + Ground confirms the above.  ← **only remaining box**
- [x] A320 fixtures captured (`fixtures/a320-*.html`, live geometry baked) + A320 regression
      assertions added (`a320.test.js`).

When the NVDA box is also checked: **delete this file** and
`tools/_probe/settings-accessibility-audit.md`, and remove any A320 verification probes
you don't want to keep. (The `a320.test.js` harness + `a320-*` fixtures are permanent — keep.)
