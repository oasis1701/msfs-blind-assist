# iFly 737 MAX 8 — AI Display Reading (PFD / ND / ISFD / EICAS)

**Date:** 2026-07-26
**Status:** Approved design, ready for implementation plan
**Branch context:** `feat/ifly-737-max8`

## Summary

Add AI vision-based display reading to the iFly 737 MAX 8, mirroring the existing
PMDG 737 feature. Four screen-reader hotkeys each capture the MSFS window and ask the
selected AI provider (Gemini or Claude) to read one cockpit display into plain text:

| Hotkey | Display | `DisplayType` (new) |
|--------|---------|---------------------|
| Alt+P | PFD (Primary Flight Display)            | `PFDiFly`   |
| Alt+N | ND (Navigation Display)                 | `NDiFly`    |
| Alt+I | ISFD (Integrated Standby Flight Display)| `ISFDiFly`  |
| Alt+E | Engine / crew-alert display ("EICAS")   | `EICASiFly` |

(Bindings verified in `HotkeyManager.cs:735-739` `RegisterHotKey` calls: Alt+P=0x50,
Alt+N=0x4E, Alt+I=0x49, Alt+E=0x45. The PMDG 737 code *comment* says "Shift+N" but the
actual registration is Alt+N — the registration is authoritative.)

This is a small, low-risk change: the capture pipeline, result window, error handling,
hotkey routing, and provider abstraction are all already shared. The only genuinely new
work is four `DisplayType` enum values, four tailored prompts, and four `case` blocks in
the iFly's hotkey handler.

## Background — the existing (PMDG) pattern

The feature already exists for the PMDG 737/777 and HorizonSim 787. It works entirely
through shared infrastructure:

- **`BaseAircraftDefinition.ReadDisplay(DisplayType, displayName, announcer, parentForm)`**
  (`Aircraft/BaseAircraftDefinition.cs:680`) — checks the MSFS window is present, captures
  the **whole window** via `ScreenshotService.CaptureAsync()`, calls
  `aiProvider.AnalyzeDisplayAsync(screenshot, displayType)`, shows the result text in a
  `DisplayReadingResultForm`, and announces `"{displayName} analysis ready."`. It handles
  the missing-API-key and generic-error paths (announce + message box). **No camera
  switching, no cropping** — it screenshots whatever is on screen and the prompt tells the
  AI which display to read.
- **`IAiProvider.AnalyzeDisplayAsync(byte[], GeminiService.DisplayType)`** — implemented by
  both `GeminiService` and `ClaudeService`; the active one is chosen by
  `AiProviderFactory.Create()`. **Both providers share the prompt**: `ClaudeService`
  calls `GeminiService.GetPromptForDisplay(displayType)` (`Services/ClaudeService.cs:88`),
  so each prompt is authored once, in `GeminiService`.
- **Hotkeys are global.** Pressing the physical combo fires
  `TriggerHotkey(HotkeyAction.ReadDisplayPFD/ND/ISIS/UpperECAM)` regardless of the loaded
  aircraft (`Hotkeys/HotkeyManager.cs:450-467`; command IDs `9069/9071/9072/9073`). An
  aircraft **opts in simply by handling the action** in `HandleHotkeyAction`. The iFly
  currently handles none of them (`Aircraft/IFly737MAXDefinition.cs:2135`), so an unhandled
  press falls through to the base and is a no-op today.
- **PMDG 737 reference** (`Aircraft/PMDG737Definition.cs:5613-5631`): maps the four
  `ReadDisplay*` actions to `ReadDisplay(DisplayType.PFD737/ND737/ISFD737/EICAS737, …)` and
  returns `false` for `ReadDisplayLowerECAM` (lower/system DU out of scope).

## Goals

- Full PMDG-parity display reading for the iFly: PFD, ND, ISFD, and the engine/crew-alert
  display, on the same hotkeys.
- Prompts tuned to the **737 MAX** display layout, capturing the information that is *not*
  already available through the iFly SDK — above all the ND map picture (weather radar,
  terrain, traffic, route) and the EICAS **caution/warning message text**.

## Non-goals (explicit scope guardrails)

- **No camera switching.** Reliable per-display camera framing is unavailable in this
  setup (see Appendix A) and unnecessary — the normal forward cockpit view already shows
  all four displays legibly, and `ReadDisplay` captures the whole window. This matches how
  PMDG works. The user must simply have the target display visible when pressing the key.
- **No SDK writes.** Read-only (screenshot + AI). Consistent with the iFly transport rules
  (SDK shared memory + WM_COPYDATA; no MobiFlight/L:var writes) — this feature touches
  neither.
- **Lower / system-page DU is out of scope** (matches PMDG's `ReadDisplayLowerECAM →
  false`). Can be added later if a useful system synoptic is identified.
- **Hybrid SDK-injection is deferred** (see Future Enhancements).

## Design

### 1. `DisplayType` enum additions — `Services/GeminiService.cs`

Add four values to the `DisplayType` enum (`GeminiService.cs:204`), following the existing
per-aircraft-family naming (`PFD737`, `PFD777`, …). The `iFly` suffix scopes them to this
add-on and avoids collision with a hypothetical future PMDG 737 MAX:

```csharp
PFDiFly,    // Primary Flight Display (iFly 737 MAX 8)
NDiFly,     // Navigation Display (iFly 737 MAX 8)
ISFDiFly,   // Integrated Standby Flight Display (iFly 737 MAX 8)
EICASiFly   // Engine / crew-alert display, "EICAS-equivalent" (iFly 737 MAX 8)
```

### 2. Prompts — `GeminiService.GetPromptForDisplay`

Add four `case` arms modeled on `EICAS737` (`GeminiService.cs:474`), written from the
real MAX layout captured in-sim. Each prompt: states the aircraft and the single target
display; instructs the model that the image may contain several displays and to describe
**only** the target; uses screen-reader phrasing; skips normal green/white and calls out
only amber/red; forbids markdown/explanation; fixes the reporting order.

Key MAX-specific facts baked into the prompts (verified from the captured screenshots):

- The **engine strip shows N1, N2, EGT, FF, and oil together** on the inboard display unit
  — unlike the 737 NG, where N2 is on a separate lower display. The `EICASiFly` prompt must
  report N2 (the `EICAS737` prompt explicitly tells the model N2 is absent — that would be
  wrong here).
- The **ND and the engine strip can share one physical display unit** (engine strip to the
  right of the map). `NDiFly` must describe only the map/navigation portion; `EICASiFly`
  only the engine/alerts portion.
- The **ISFD** is the small standby instrument on the right of the main panel.

Initial prompt drafts (to be refined against live output during in-sim testing):

- **`PFDiFly`** — report order: FMA bar left→right (autothrottle / roll / pitch modes +
  FD/CMD, e.g. `FMC SPD` / `LNAV` / `VNAV PTH` / `CMD`); airspeed + selected speed + bugs;
  attitude (pitch/bank if unusual); altitude + selected altitude + baro (e.g. `29.71 IN` /
  `1013 HPA`); vertical speed; heading/track + selected heading; radio altitude if shown;
  localizer/glideslope deviation if shown; any flags/amber/red annunciations.
- **`NDiFly`** — mode + range (e.g. `MAP 5`, `VOR`, `PLAN`, `APP`); track/heading; ground
  speed/TAS; active waypoint name + distance + ETA (e.g. `TIKNI 51.8 NM`); next route
  waypoints; wind; **weather-radar returns (presence, intensity, bearing)**; **terrain
  shading**; **TCAS traffic (relative position + altitude)**; VOR/ADF pointers; RNP/ANP and
  nav flags. *(Highest-value read — this content is not in the SDK.)*
- **`ISFDiFly`** — airspeed; attitude (pitch/bank if unusual); altitude; baro; mode
  annunciations (e.g. `APP`, `ILS`) and any flags.
- **`EICASiFly`** — thrust mode (`TO`/`R-TO`/`CLB`/`CLB1`/`CLB2`/`CON`/`CRZ`/`GA`); TAT/SAT
  if shown; per engine (ENG 1 / ENG 2): N1 + N1 reference bug, EGT, N2, fuel flow, oil
  pressure/temp/quantity, vibration; flap/gear position if shown; fuel quantities + total;
  then — **the priority** — every **caution (amber) and warning (red) crew-alert message,
  read top to bottom exactly as shown** (or "No alerts" if none).

The generic fallback arm (`_ =>`) is unchanged; the switch must return a specific prompt
for each new value (see test T1).

### 3. Hotkey wiring — `Aircraft/IFly737MAXDefinition.cs`

Add four `case` blocks to `HandleHotkeyAction` (the switch at `IFly737MAXDefinition.cs:2135`),
directly paralleling PMDG 737:

```csharp
case HotkeyAction.ReadDisplayPFD:
    ReadDisplay(Services.GeminiService.DisplayType.PFDiFly, "PFD", announcer, parentForm);
    return true;
case HotkeyAction.ReadDisplayND:
    ReadDisplay(Services.GeminiService.DisplayType.NDiFly, "ND", announcer, parentForm);
    return true;
case HotkeyAction.ReadDisplayISIS:
    ReadDisplay(Services.GeminiService.DisplayType.ISFDiFly, "ISFD", announcer, parentForm);
    return true;
case HotkeyAction.ReadDisplayUpperECAM:
    ReadDisplay(Services.GeminiService.DisplayType.EICASiFly, "EICAS", announcer, parentForm);
    return true;
// ReadDisplayLowerECAM intentionally not handled (out of scope; falls through to base → no-op).
```

Physical bindings (inherited, unchanged): **Alt+P** (PFD), **Shift+N** (ND), **Alt+I**
(ISFD), **Alt+E** (EICAS) — per the PMDG 737 reference. Verify the exact combos in
`HotkeyManager` during implementation.

### 4. Hotkey discoverability

Verify the four display-read hotkeys surface for the iFly in the hotkey list/help
(`Forms/HotkeyListForm.cs`) the same way they do for the PMDG 737. If that list is filtered
per aircraft by advertised support, add the iFly to it; if the display hotkeys are shown
globally, no change is needed. **This is the one open wiring question to confirm during
implementation.**

### 5. Reused infrastructure (no changes)

- `ScreenshotService`, `ReadDisplay`, `DisplayReadingResultForm`, `AiProviderFactory`,
  `IAiProvider`, and the API-key/error handling are all reused unchanged.
- Provider-agnostic by construction: whichever provider is selected in Settings (Gemini or
  Claude) is used, via the shared prompt.

## Files changed

1. `MSFSBlindAssist/Services/GeminiService.cs` — 4 enum values + 4 prompt `case` arms.
2. `MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs` — 4 `case` blocks in `HandleHotkeyAction`.
3. *(Verify only, possibly no change)* `MSFSBlindAssist/Forms/HotkeyListForm.cs` — ensure the
   four hotkeys are listed for the iFly.

## Testing

**No unit test.** The change is declarative — four `DisplayType` values, four static prompt
strings, four switch arms, four hotkey-dispatch cases, one doc file — not pure logic, and its
value is entirely sim-facing (the AI reading a live display). `GetPromptForDisplay` is also
`internal`, and the test project (`tests/MSFSBlindAssist.Tests`) tests only public API (no
`InternalsVisibleTo`). This matches the PMDG 737 display-capture feature, which added no
prompt test, and CLAUDE.md's rule that sim-facing paths are validated in-sim, not unit-tested.
The build succeeding + the in-sim plan below is the verification.

**In-sim test (owner-run; describe in PR).** Load the iFly 737 MAX 8, forward cockpit view:
- Press **Alt+P / Shift+N / Alt+I / Alt+E**; confirm each result window reads the correct
  display, and that values roughly match the SDK-driven readouts (e.g. Alt+P altitude vs the
  altimeter hotkey).
- **ND**: confirm the map narrative (active waypoint, range, and — where present — weather
  radar / terrain / traffic) is captured.
- **EICAS**: with an active caution/warning present, confirm the alert message text is read
  out. Confirm N2 is reported (MAX-specific).
- Repeat with the other AI provider selected in Settings to confirm provider-agnostic output.
- Prompt wording will likely need one or two refinement passes against live output; that is
  expected and can only be done in-sim.

## Future enhancements (deferred, not in scope)

- **Hybrid SDK-injection.** Pass the SDK's known numeric values (altitude, speed, N1/N2/EGT,
  FMA modes, …) into the prompt as ground truth so the model can't misread numbers and can
  focus on the graphical/non-SDK content. More accurate but couples the feature to specific
  SDK fields; revisit only if pure-vision accuracy proves insufficient.
- **Lower / system-page DU** read, if a useful iFly system synoptic is identified.

---

## Appendix A — Camera-view investigation (why no camera switching)

Recorded so this isn't re-litigated:

- The iFly `cameras.cfg` (identical across all four airframe variants) defines **9 cockpit
  "Instrument" views** in order: `PFD`, `MCP_EFIS`, `EFB`, `FMC`, `Overhead`, `OverheadAFT`,
  `Throttle`, `Radio`, `HUD` (`PANEL_CAMERA_INSTRUMENTS_01…09`), plus four "Pilot" views and
  the quickview/external cameras.
- **`CTRL+1` selects the forward cockpit view** that shows PFD, ND, engine strip and ISFD
  together — this is the view to be in when reading a display, and it is now noted in the
  iFly hotkey guide (mirroring the PMDG guide's cockpit-view note). An earlier live test where
  Ctrl+1 appeared to do nothing was simply because the sim was already on that view (confirmed
  with the owner), not because the key is unbound.
- **Programmatic per-display framing is still avoided:** setting `CAMERA_VIEW_TYPE_AND_INDEX`
  via SimConnect is unreliable — writes landed on external/porthole views, not the requested
  instrument view (verified live) — so the app never switches cameras itself.
- The **normal forward cockpit view shows PFD, ND, engine strip, ISFD, and the CDU all
  legibly at once** (verified via in-sim screenshot at 4K). This is why whole-window capture
  (the PMDG approach) is sufficient and camera framing is unnecessary.

## Appendix B — SimConnect MCP read bug (tooling note)

During verification, the `simconnect-mcp` server's single/bulk variable **read** path failed
with `cannot import name 'DATATYPE_FLOAT64' from 'SimConnect.Constants'`; only the fixed
`get_aircraft_state` block and **writes** worked. This is a bug in that external MCP tool
(under `C:\Users\robin\Downloads\simconnect-mcp`), not in MSFSBA, and is unrelated to this
feature. It only limited live probing during design and is worth a separate fix so future
in-sim probing works.
