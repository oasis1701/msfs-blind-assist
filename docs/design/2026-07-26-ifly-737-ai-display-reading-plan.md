# iFly 737 MAX 8 — AI Display Reading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the iFly 737 MAX 8 the same AI display-reading hotkeys the PMDG 737 has — Alt+P/Alt+N/Alt+I/Alt+E read the PFD, ND, ISFD, and engine/EICAS display aloud via the selected AI provider.

**Architecture:** Reuse the shared `BaseAircraftDefinition.ReadDisplay(...)` pipeline (whole-window screenshot → `IAiProvider.AnalyzeDisplayAsync` → `DisplayReadingResultForm` + announce). The only new code is four `DisplayType` enum values with MAX-tuned prompts in `GeminiService`, four `case` blocks in the iFly's `HandleHotkeyAction`, and four lines in the iFly hotkey guide. No camera switching, no SDK writes.

**Tech Stack:** C# 13 / .NET 10, Windows Forms. Existing `GeminiService`/`ClaudeService` (shared prompt via `GeminiService.GetPromptForDisplay`), `AiProviderFactory`, `ScreenshotService`, `HotkeyManager` global hotkeys.

**Spec:** `docs/design/2026-07-26-ifly-737-ai-display-reading-design.md`

## Global Constraints

- **Build the solution, never the bare csproj:** `dotnet build MSFSBlindAssist.sln -c Debug` (maps to Debug|x64). A bare `dotnet build` on the .csproj defaults to AnyCPU and writes to a *different* folder than the run path. Verify the build landed by checking `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` (or the DLL) has a fresh timestamp.
- **The exe is file-locked while MSFSBA runs** (MSB3021) — close the app before building.
- **No unit tests for this change** (declarative + sim-facing; `GetPromptForDisplay` is `internal`, tests are public-API only — see spec). Verification = build succeeds + in-sim test plan (Task 3).
- **Prompt style (match existing arms):** screen-reader phrasing, skip normal green/white and call out only amber/red, no markdown, no explanations, fixed reporting order, tell the model the image may contain several displays and to describe only the target.
- **Read-only feature:** no `SetLVar`, no SimConnect writes, no camera changes.
- **`main` is protected** — work stays on `feat/ifly-737-max8`; do not commit to main.

---

### Task 1: Add iFly `DisplayType` values and MAX-tuned prompts

**Files:**
- Modify: `MSFSBlindAssist/Services/GeminiService.cs` (enum at `:204`, `GetPromptForDisplay` switch at `:310`)

**Interfaces:**
- Consumes: nothing (leaf change).
- Produces: enum values `GeminiService.DisplayType.PFDiFly`, `.NDiFly`, `.ISFDiFly`, `.EICASiFly`; and the prompts returned for them by `internal static string GeminiService.GetPromptForDisplay(DisplayType)` (used by both `GeminiService` and `ClaudeService`).

- [ ] **Step 1: Add the four enum values**

In `GeminiService.cs`, in the `DisplayType` enum, add after the last member `EICAS737` (change the trailing comma on `EICAS737` accordingly):

```csharp
        EICAS737,      // Upper Engine Display / "EICAS-equivalent" / DU3 (Boeing 737 NG3)
        PFDiFly,       // Primary Flight Display (iFly 737 MAX 8)
        NDiFly,        // Navigation Display (iFly 737 MAX 8)
        ISFDiFly,      // Integrated Standby Flight Display (iFly 737 MAX 8)
        EICASiFly      // Engine indications + crew alerts, "EICAS-equivalent" (iFly 737 MAX 8)
```

- [ ] **Step 2: Add the four prompt arms**

In `GetPromptForDisplay`, insert these four arms immediately **before** the `_ =>` fallback arm (the one returning `"Report what you see on this display..."`):

```csharp
            DisplayType.PFDiFly => @"You are reading the Primary Flight Display (PFD) of an iFly Boeing 737 MAX 8 for a screen reader user. The image may contain several displays. ONLY describe the PFD — the display showing the artificial-horizon attitude indicator with a speed tape on its left and an altitude tape on its right. Ignore the navigation display, the engine indications, the ISFD standby, the flight-information/data page, and the CDU.
Report in this order:
Flight Mode Annunciator (FMA) across the top, left to right: autothrottle mode (e.g. N1, RETARD, ARM, FMC SPD), roll mode (e.g. LNAV, HDG SEL, VOR/LOC, LOC), pitch mode (e.g. VNAV PTH, VNAV SPD, ALT, V/S, G/S, FLARE), and AFDS status (FD, CMD, or single/dual channel).
Airspeed: current indicated airspeed, the selected/MCP speed if shown, and any relevant speed bugs.
Attitude: pitch and bank only if unusual (otherwise say wings level).
Altitude: current altitude, the selected altitude, and the barometric setting (e.g. 29.71 IN, 1013 HPA, or STD).
Vertical speed if shown.
Heading and track at the bottom, plus the selected heading.
Radio altitude if displayed.
Localizer and glideslope deviation if an approach is shown.
Any flags, failure flags, or amber/red annunciations.
Skip normal colors (green, white, magenta); only call out amber and red. Put each parameter on its own line. Do not use markdown. Do not explain what things mean. Just state the data.",

            DisplayType.NDiFly => @"You are reading the Navigation Display (ND) of an iFly Boeing 737 MAX 8 for a screen reader user. The image may contain several displays. ONLY describe the ND — the display showing the compass arc or map with the aircraft symbol. Engine indications may appear on the same physical display unit, to the right of the map; do NOT report engine data — describe only the navigation/map portion.
Report in this order:
Mode and range (e.g. MAP 5, VOR, PLAN, APP; range in nautical miles).
Current track and heading (e.g. TRK 008 MAG); ground speed and true airspeed if shown.
Active waypoint: name, distance, and time or ETA (e.g. TIKNI 51.8 NM).
Next waypoints along the magenta route line, in order, if legible.
Wind: direction and speed.
Weather radar returns: whether any are painted, their intensity (green, amber/yellow, red), and rough bearing and distance.
Terrain: any terrain shading and its color.
Traffic (TCAS): any traffic symbols, with relative bearing, range, and relative altitude if shown.
VOR/ADF pointers and tuned stations if shown.
RNP/ANP figures and any navigation flags or messages.
Skip normal colors; only call out amber and red. Put each item on its own line. Do not use markdown. Do not explain. Just state the data.",

            DisplayType.ISFDiFly => @"You are reading the Integrated Standby Flight Display (ISFD) of an iFly Boeing 737 MAX 8 for a screen reader user — the small standby instrument on the right side of the main panel, a compact attitude indicator with its own speed and altitude readouts. The image may contain several displays. ONLY describe the ISFD; ignore the main PFD, the ND, the engine display, and the CDU.
Report: airspeed; attitude (pitch and bank only if unusual); altitude; barometric setting (e.g. 1013 HPA, 29.71 IN, or STD); and any mode annunciations (e.g. APP, ILS) or flags.
Skip normal colors; only call out amber and red. Put each parameter on its own line. Do not use markdown. Do not explain. Just state the data.",

            DisplayType.EICASiFly => @"You are reading the engine indications and crew-alert messages of an iFly Boeing 737 MAX 8 for a screen reader user. On the 737 MAX these appear on the inboard display unit, to the right of the navigation display. The image may contain several displays. Describe ONLY the engine indications and the crew-alert message text; ignore the PFD attitude, the ND map, the ISFD, and the CDU.
Important: on the 737 MAX the engine display shows N1, N2, EGT, fuel flow, and oil indications together (unlike the 737 NG, where N2 is on a separate lower display). Report all of them.
Report in this order:
Thrust mode label if shown (e.g. TO, R-TO, CLB, CLB1, CLB2, CON, CRZ, GA).
TAT and SAT if shown, in degrees Celsius.
For each engine, ENG 1 then ENG 2 (left then right):
  N1 percent, and the N1 reference/limit bug value if shown.
  EGT in degrees Celsius.
  N2 percent.
  Fuel flow (e.g. 2.26 meaning 2260 pounds per hour).
  Oil pressure, oil temperature, oil quantity, and vibration if shown.
Flap position and landing-gear indications if shown on this display.
Fuel quantity: left, center, right, and total if shown, in thousands of pounds.
Then, most important, the crew alert messages: read every caution (amber) and warning (red) message line exactly as written, top to bottom. If there are none, say ""No alerts"".
Skip normal colors (green, white); call out amber and red. Put the thrust mode first, then TAT/SAT, then each engine on its own line, then flaps/gear, then fuel, then the alerts. Do not use markdown. Do not explain. Just state the data.",
```

(Note: `""No alerts""` uses doubled quotes — correct escaping inside a C# `@"..."` verbatim string.)

- [ ] **Step 3: Build and verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded.` with 0 errors. (If MSFSBA is running, close it first — the exe is locked.)

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Services/GeminiService.cs
git commit -m "feat(ifly): add PFD/ND/ISFD/EICAS DisplayType values and MAX-tuned prompts

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Wire the four hotkeys into the iFly and document them

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs` (`HandleHotkeyAction` switch, `:2135`)
- Modify: `MSFSBlindAssist/HotkeyGuides/iFly_737MAX8_Hotkeys.txt`

**Interfaces:**
- Consumes: `GeminiService.DisplayType.PFDiFly / .NDiFly / .ISFDiFly / .EICASiFly` (Task 1); the inherited `protected async void ReadDisplay(GeminiService.DisplayType, string displayName, ScreenReaderAnnouncer, Form)` from `BaseAircraftDefinition`; the `HotkeyAction.ReadDisplayPFD / ReadDisplayND / ReadDisplayISIS / ReadDisplayUpperECAM` values.
- Produces: working Alt+P / Alt+N / Alt+I / Alt+E display reads for the iFly (no code consumed downstream).

Background (already true, no change needed): the physical keys are registered globally in `HotkeyManager.cs:735-739` (`Alt+P`=0x50 PFD, `Alt+N`=0x4E ND, `Alt+I`=0x49 ISIS, `Alt+E`=0x45 Upper ECAM) and routed to these `HotkeyAction`s for every aircraft. An aircraft opts in purely by handling the action; the iFly currently handles none, so today they're no-ops.

- [ ] **Step 1: Add the four dispatch cases**

In `IFly737MAXDefinition.HandleHotkeyAction`, add these cases immediately **before** the `default:` label (which currently does `return base.HandleHotkeyAction(...)`):

```csharp
            // ------------------------------------------------------------------
            // AI display reading — Alt+P / Alt+N / Alt+I / Alt+E (mirrors PMDG 737)
            // ------------------------------------------------------------------
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

            // Lower system display (Alt+S / ReadDisplayLowerECAM) intentionally not handled
            // (out of scope — matches PMDG 737/777); it falls through to base as a no-op.
```

- [ ] **Step 2: Document the hotkeys in the iFly guide**

In `MSFSBlindAssist/HotkeyGuides/iFly_737MAX8_Hotkeys.txt`, insert this block immediately **before** the `Checklists and EFB:` section:

```text
AI Display Reading (needs an AI provider configured in Settings > AI tab):
  Alt+P      Describe the PFD (Primary Flight Display)
  Alt+N      Describe the ND (Navigation Display)
  Alt+I      Describe the ISFD (standby instrument)
  Alt+E      Describe the engine display and crew alerts (EICAS)
  Each captures the simulator window and reads the chosen display aloud in a
  result window, so have that display visible when you press the key — the
  normal forward cockpit view shows all of them at once.

```

- [ ] **Step 3: Build and verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded.` with 0 errors. Confirm `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` has a current timestamp, and that `HotkeyGuides\iFly_737MAX8_Hotkeys.txt` in that output folder contains the new "AI Display Reading" block (the build copies the guide to the output tree).

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs MSFSBlindAssist/HotkeyGuides/iFly_737MAX8_Hotkeys.txt
git commit -m "feat(ifly): wire Alt+P/N/I/E AI display reads and document them

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: In-sim verification (owner-run)

**Files:** none (manual test; no automated test — see Global Constraints).

Not an editing task — this is the acceptance test the repo owner runs against a live sim, to be described in the PR. A subagent cannot perform it; mark it done only after the owner confirms.

- [ ] **Step 1: Configure an AI provider.** In MSFSBA, File > Settings > AI tab, select Gemini or Claude and confirm the API key is set.

- [ ] **Step 2: Load the iFly 737 MAX 8** in MSFS, in the normal forward cockpit view (PFD, ND, engine strip, ISFD all visible).

- [ ] **Step 3: Read each display.** Press **Alt+P**, **Alt+N**, **Alt+I**, **Alt+E** in turn. For each, confirm the result window opens and reads the correct display, and that the values roughly agree with the SDK-driven readouts (e.g. Alt+P altitude vs. the `A`/altimeter hotkeys).

- [ ] **Step 4: Check the high-value content.** Confirm **ND** captures the map narrative (active waypoint, range, and — where present — weather radar / terrain / traffic), and that **EICAS** reports **N2** (MAX-specific) and, with an active caution/warning present, reads the alert message text.

- [ ] **Step 5: Provider parity.** Switch the AI provider in Settings and re-run one or two reads to confirm provider-agnostic output.

- [ ] **Step 6: Discoverability.** Open the iFly hotkey list (the app's hotkey guide) and confirm the four AI Display Reading entries appear.

- [ ] **Step 7: Refine prompts if needed.** Prompt wording will likely need one or two passes against live output (e.g. an FMA term the model misreads). Adjust the strings in `GeminiService.cs` (Task 1, Step 2), rebuild, and re-test. Commit any refinement:

```bash
git add MSFSBlindAssist/Services/GeminiService.cs
git commit -m "fix(ifly): refine display-reading prompts from in-sim output

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage** (each spec section → task):
- Hotkeys (Alt+P/N/I/E, handled in `HandleHotkeyAction`) → Task 2, Step 1. ✓
- `DisplayType` additions → Task 1, Step 1. ✓
- Four MAX-tuned prompts (PFD/ND/ISFD/EICAS, incl. N2-on-strip and ND non-SDK content) → Task 1, Step 2. ✓
- Capture + result + error handling reused (`ReadDisplay`) → no code; consumed in Task 2. ✓
- Provider-agnostic (shared `GetPromptForDisplay`) → Task 1 (prompt), Task 3 Step 5 (verified). ✓
- Hotkey discoverability (`iFly_737MAX8_Hotkeys.txt`) → Task 2, Step 2; verified Task 3, Step 6. ✓
- Non-goals (no camera switch, no SDK writes, lower DU out of scope) → honored; lower-ECAM left unhandled (Task 2, Step 1 comment). ✓
- Testing = build + in-sim → Global Constraints + Task 3. ✓

**Placeholder scan:** No TBD/TODO. All four prompts are written in full; all edit anchors are concrete (`EICAS737` enum member, `_ =>` arm, `default:` label, `Checklists and EFB:` line). ✓

**Type consistency:** Enum values `PFDiFly / NDiFly / ISFDiFly / EICASiFly` are defined in Task 1 Step 1 and consumed with identical spelling in Task 1 Step 2 (prompt arms) and Task 2 Step 1 (dispatch). Hotkey actions `ReadDisplayPFD / ReadDisplayND / ReadDisplayISIS / ReadDisplayUpperECAM` and the `ReadDisplay(DisplayType, string, ScreenReaderAnnouncer, Form)` signature match the existing PMDG 737 usage. ✓
