# HorizonSim 787-9 FMC Bridge — FS2024 Resolution Log

The HS787 FMC bridge works in **both MSFS 2020 and MSFS 2024** as of `BridgeVersion 18`. The two sims use different install architectures, picked at runtime in `HS787ModPackageManager` by `IsFs2024(communityFolderPath)` (`path.Contains("Limitless")`).

This doc captures the diagnosis trail that got us there, what was tried and why it failed, and the final fix — so future maintainers don't waste cycles re-running ruled-out experiments.

## TL;DR

FS2024's bridge needed two simultaneous fixes:

1. **Switch from override-package to in-place patching of `horizonsim-aircraft-787-9`.** FS2024's VFS silently refuses community-on-community html_ui overrides under the `Airliners/` namespace, so the `zzz-hs787-accessibility` override package's modified HTML was never loaded.
2. **Use `import-script` injection instead of `<script src>`.** FS2024's Coherent GT 2.x doesn't execute standalone `<script src>` tags appended at the end of HTML that's loaded via the MSFS template machinery — only `<script type="text/html" import-script="...">`-style tags fire there.

Either fix alone is insufficient. Both are required.

FS2020 still uses the original architecture (override package + `<script src>`) because its Coherent GT 1.x is lenient and its VFS prioritizes by alphabetical prefix (`zzz-` wins). Do not modify the FS2020 path.

## Confirmed Context

- FS2020 uses Coherent GT 1.x. FS2024 uses Coherent GT 2.x (newer Chromium base, stricter loading behavior — confirmed by ES6 `class` syntax in `MFD789.GE.js`).
- FS2024 also has a new VFS with explicit override declarations (`globally_overriden_base_sim_files`) for some content. Asobo's typo is preserved (single 'd').
- The PMDG EFB bridge (`zzz-pmdg-efb-accessibility`) works on FS2024 because its override target is at `html_ui/Pages/VCockpit/Instruments/PMDGTablet/...`, **outside** the `Airliners/` protected namespace.
- `panel-raas` works on FS2024 because it overrides `\html_ui\pages\VCockpit\Core\VCockpit.html` (an Asobo base sim file) via the `globally_overriden_base_sim_files` field — that field is base-sim-files-only, will not accept community-package paths.
- Stage 0 (`L:MSFSBA_787_STAGE`) does NOT distinguish "script blocked by sandbox" from "script tag never made it into the loaded DOM." The deciding diagnostic is the Coherent debugger console: `document.documentElement.outerHTML.indexOf('hs787-mfd-bridge')`.

## Final Fix (BridgeVersion 18)

`HS787ModPackageManager.cs` branches on `IsFs2024()`:

### FS2024 path
1. Locate `horizonsim-aircraft-787-9` within the FS2024 Community folder.
2. For each of the 4 instrument HTMLs (`HSB789_MFD.GE/RR.html`, `HSB789_EFB.GE/RR.html`):
   - On first install, copy original to `<name>.msfsba_backup` (preserves first known-good state).
   - Re-derive the patched content from the backup (so re-installs and version bumps don't stack script tags) and append:
     ```html
     <script type="text/html" import-script="/Pages/VCockpit/Instruments/Airliners/HSB787_9/MFD/hs787-mfd-bridge.js"></script>
     ```
     (or the EFB equivalent). Path is absolute from `html_ui/` root — relative paths don't resolve in template contexts.
3. Drop `hs787-mfd-bridge.js` next to `MFD789.RR.js`, and `hs787-efb-bridge.js` next to `EFB789.RR.js`.
4. Update `horizonsim-aircraft-787-9/layout.json`:
   - Resync the 4 HTML entry sizes to actual on-disk sizes (slash-agnostic match — see "subtle bug" below).
   - Add (or replace) 2 entries for the bridge JS files. Match the slash convention of existing entries.
   - First write backs up `layout.json` to `layout.json.msfsba_backup`.
5. Write `msfsba-bridge-version.txt = 18` inside `horizonsim-aircraft-787-9/` for version tracking.
6. If `zzz-hs787-accessibility/` exists in the Community folder (leftover from older MSFSBA versions), delete it — it does nothing on FS2024.

### FS2020 path
Unchanged. The override-package architecture (`zzz-hs787-accessibility`) + `<script src>` injection works fine. Coherent GT 1.x is lenient; FS2020's VFS uses alphabetical priority.

### Uninstall (FS2024)
`RemoveFs2024`: restore each HTML from its `.msfsba_backup`, delete the bridge JS files we added, restore `layout.json` from `layout.json.msfsba_backup`, delete `msfsba-bridge-version.txt`. Also wipes any legacy `zzz-hs787-accessibility/`.

### Subtle bug to be aware of when touching layout.json patching
The auto-detection of `/` vs `\` in `UpdateHorizonsimLayoutJson` must scan all entries until it finds one with any slash — looking at only the first entry will misfire when the package's first entry is a top-level file with no path separator (e.g. `de-DE.locPak`). When this misfires:
- New bridge JS entries get the wrong slash style — Windows tolerates this, MSFS likely too, but it makes the layout.json visually inconsistent.
- More critically, the `UpdateHtmlSize` match-by-string-equality silently fails, so the 4 HTML entries keep their old sizes while the actual files are larger after patching. **MSFS validates layout.json sizes; mismatches can cause the package to load incorrectly.** Current code uses a slash-agnostic canonical form (`Canonical(p) = p.Replace('\\','/').ToLowerInvariant()`) for matching, which sidesteps the entire class of problem. Don't revert.

## Verification

- Coherent debugger (`http://127.0.0.1:19999`), in any `HSB789_MFD_*` webview's Console:
  - `window._mfd_bridge_loaded` → `true`
  - `document.documentElement.outerHTML.indexOf('hs787-mfd-bridge')` → non-negative integer
  - `typeof SimVar` → `"object"` (FS2024 VCockpit context exposes SimVar — confirmed)
- `L:MSFSBA_787_STAGE` (FS2024 dev mode behaviors or via MSFSBA's continuous monitoring) → reaches `3` (connected)
- MSFSBA's HS787 FMC form: status label hides (bridge connected), CDU rows populate, buttons (INIT REF, etc.) work.

## Failed Attempts — DO NOT RETRY

These were burned chasing the wrong root cause. Documented here so future maintainers don't repeat them.

### 1. Private Network Access headers (`Access-Control-Allow-Private-Network: true`)
**Hypothesis:** Chrome 94+ PNA policy blocks `fetch()` to localhost. **Result:** Failed. **Why irrelevant:** The script never executed at all — fetch was never even attempted in FS2024 VCockpit. PNA was a red herring.

### 2. `import-script` instead of `<script src>` in the override package
**Hypothesis:** `<script src>` is ignored by FS2024 Coherent; `import-script` is the MSFS-native loader. **Result:** Failed. **Why irrelevant:** This change is correct (and now applied), but only as half the fix. The override package itself wasn't loading on FS2024 — so the injection style inside it was irrelevant. Also: `import-script` alone, in the override package on FS2020, broke FS2020 because of how Coherent GT 1.x runs import-script in a separate JS context from `<script src>` (the double-load guard fired before the working context could initialize). FS2020 must use `<script src>`. FS2024 must use `import-script` (and in place, not in an override package).

### 3. IPv4 force (`localhost` → `127.0.0.1`) + dual loader injection
**Hypothesis:** `localhost` resolves to IPv6 `::1` in Coherent 2.x, but C# HttpListener binds IPv4 only. **Result:** Failed. **Why irrelevant:** Same as #1 — script never ran, so its choice of resolver never mattered. Keep `127.0.0.1` anyway; it's a strict improvement.

### 4. Inline `<script>` block in patched HTML (no external src)
**Hypothesis:** Coherent strips external `<script src>` tags but might honor inline blocks. **Result:** Failed (Stage 0). **Why irrelevant:** The patched HTML was never loaded in the first place. Whether the script tag was inline or external is moot if MSFS reads the original unpatched HTML.

### 5. Manifest `globally_overriden_base_sim_files` with community-package paths
**Hypothesis:** FS2024 needs an explicit override declaration for `html_ui` files; the field name suggests this is the mechanism. **Result:** Failed (Stage 0; `BRIDGE_IN_DOM=false`). **Why irrelevant:** The field is base-sim-files-only. Asobo accepts only paths to files shipped in base sim content. Adding community-package paths is silently ignored. The mechanism never has and never will accept third-party-package files. Verified by inspecting `panel-raas` (works, overrides `\html_ui\pages\VCockpit\Core\VCockpit.html`, an Asobo base file) — there's no working community-on-community example using this field anywhere.

## How the diagnosis trail actually broke open

The key moment was inspecting the Coherent debugger's loaded-resources list. `mfd789.rr.js` was visible (loaded from `horizonsim-aircraft-787-9` directly), but `document.documentElement.outerHTML.indexOf('hs787-mfd-bridge')` returned `-1` — meaning our `<script>` tag was nowhere in the rendered DOM, even with a valid override declaration in our manifest. That ruled out every "the script is loaded but blocked" theory and pointed at "the patched HTML never even reached the DOM."

The architectural conclusion: FS2024 protects the `Airliners/` namespace from community overrides. Stop trying to override; patch directly. PMDG EFB's working bridge confirmed it: same architecture, different path (`PMDGTablet/` instead of `Airliners/`), works fine.

The second half (`<script src>` failing inside template-loaded HTML) only became visible AFTER the first half was fixed and we could see the bridge HTML actually loaded but `_mfd_bridge_loaded` still undefined. That led to inspecting the original HSB789_MFD.RR.html and noticing it uses `import-script` for every script reference — switching to that style fired the bridge.

If `L:MSFSBA_787_STAGE` is ever 0 again, do this in order:
1. Open Coherent debugger, find an HSB789_MFD webview, run `document.documentElement.outerHTML.indexOf('hs787-mfd-bridge')`.
2. If `-1`: patched HTML isn't loaded. Check `IsInstalledFs2024` returns true, layout.json sizes match files on disk, no signing/checksum changes by Asobo.
3. If non-negative: patched HTML loaded but script didn't run. Check the script tag form — must be `import-script`, not `<script src>`. Check the path is absolute from `html_ui/` root.
