# FlyByWire A32NX Update Design

**Date:** 2026-03-29
**Branch:** `feature/fbw-a32nx-update`
**Status:** Approved

## Overview

Bring the FlyByWire A32NX aircraft definition up to full parity with the Fenix A320 in terms of panel coverage, MCDU access, hotkey completeness, and variable accuracy. Work is sequenced in four phases: variable audit → panel completion → MCDU → hotkey/fuel cleanup.

---

## Phase 1 — Variable Audit

**Goal:** Fix broken/outdated variables before adding anything new.

**Source of truth:** `D:\Claude\fbw\aircraft\fbw-a32nx\src\`

Every existing variable in `FlyByWireA320Definition.cs` `GetVariables()` is cross-referenced against the FBW source. Any LVar that has been renamed or removed gets corrected. No new variables are added in this phase — audit only.

**Scope:** All ~367 variables in `GetVariables()` and their corresponding `BuildPanelControls()` keys.

**Outcome:** All existing panels function correctly against current FBW A32NX before new work begins.

---

## Phase 2 — Panel Completion

**Goal:** Match Fenix panel coverage across all cockpit sections.

**Variable sourcing:** All new LVar names sourced from `D:\Claude\fbw\aircraft\fbw-a32nx\src\`. No variables are invented or guessed. If no FBW equivalent exists for a Fenix panel, that panel is skipped and noted.

**Naming convention:** FBW-style panel names are kept (not renamed to match Fenix). We match panel *coverage*, not panel *names*.

### Panels to Add

**Overhead Forward** (currently 11 → target ~19):

| Panel | Notes |
|-------|-------|
| Fire | Engine/APU fire detection and extinguisher controls |
| Hydraulic | System G/B/Y pump states and pressure |
| Interior Lighting | Dome, map lights, flood panels |
| Voice Recorder | CVR test and erase |
| Cockpit Door | Door lock state and video controls |
| Evacuation | EVAC command switch |
| Wipers | Captain/FO wiper speed selectors |
| Cargo Smoke | Smoke detection and extinguisher |
| Engine (Maintenance) | Engine master and mode selector states |

**Pedestal** (currently 7 → target ~10):

| Panel | Notes |
|-------|-------|
| Flight Controls | Speedbrake, flap lever position readback |
| DCDU | Datalink control and display unit |
| Audio Control Panel (ACP) | COM/NAV audio selection |

**Instrument Panel** (currently 1 → target ~8):

| Panel | Notes |
|-------|-------|
| ISIS | Integrated standby instrument system |
| Console Floor Lights | Flood lighting |
| GPWS/Terrain | GPWS mode switches and terrain display |
| Warnings/Messages | Master caution/warning states |
| Autoland | Land 2/3 capability and autoland states |
| Instrument Lights | Panel lighting controls |
| Audio | Audio management unit |

---

## Phase 3 — MCDU

**Goal:** Full interactive MCDU access for FBW A32NX via SimBridge, matching Fenix MCDU parity.

### Architecture

**New files:**
- `Services/FBWMCDUService.cs` — WebSocket client, message parsing, reconnect logic
- `Forms/A32NX/A32NXMCDUForm.cs` — MCDU UI form
- `IAircraftCapabilities.cs` — add `ISupportsMCDU` marker interface
- `FlyByWireA320Definition.cs` — implement `ISupportsMCDU`, wire hotkey handler

**Pattern:** Mirrors `FenixMCDUService` / `FenixMCDUForm` architecture exactly, adapted for the FBW protocol.

### Protocol

| Direction | Message Format |
|-----------|---------------|
| Sim → Client | `update:{json}` — full screen state |
| Client → Sim | `event:left:BUTTON_NAME` — key press |
| Client → Sim | `requestUpdate` — force refresh |
| Sim → Client | `mcduConnected` — connection confirmation |

**WebSocket URL:** `ws://localhost:8380/interfaces/v1/mcdu`

**Display data structure:**
```
{
  left: {
    lines: [label0, line0, label1, line1, ..., label5, line5],  // 12 elements
    scratchpad: "{color}text{end}",
    title: "PAGE TITLE",
    page: "{small}1/3{end}",
    arrows: [...],
    displayBrightness: 0.8
  },
  right: { ... }  // mirrored structure
}
```

**Color stripping:** `{cyan}`, `{white}`, `{green}`, `{magenta}`, `{amber}`, `{red}`, `{small}`, `{end}` tags stripped before screen reader output.

### Screen Reader Behavior

- Title changes announced automatically
- Scratchpad changes announced with debounce (identical to Fenix)
- Display lines navigable via ListBox (read on demand)
- Page buttons: INIT, DIR, FPLN, PERF, RAD NAV, FUEL PRED, SEC FPLN, ATC COM, MENU, AIRPORT, DATA

### Fallback

When SimBridge is not running:
- Status label shows: *"SimBridge not connected — enable SimBridge in the FBW EFB settings"*
- Reconnect with backoff (same delays as Fenix: 3s, 6s, 12s, 30s)
- No crash or exception surfaced to user

### Hotkey

- **Trigger:** `[` (input mode) → `Shift+M`
- **Handler:** `HotkeyAction.ShowFenixMCDU` — FBW definition routes to `A32NXMCDUForm`, Fenix definition routes to `FenixMCDUForm` (no new action needed, aircraft definition determines behavior)

---

## Phase 4 — Hotkey and Fuel Cleanup

**Goal:** Replace `FuelPayloadDisplayForm` with hotkey announcements. Align all weight/fuel hotkeys to agreed spec.

### Files Removed
- `Forms/A32NX/FuelPayloadDisplayForm.cs` — deleted entirely
- `ShowA320FuelPayloadWindow()` in `FlyByWireA320Definition.cs` — deleted

### Hotkey Mapping (Output Mode `]`)

| Key | Action | Notes |
|-----|--------|-------|
| `F` | Fuel quantity in **lbs** | Change from current kg |
| `Shift+F` | Fuel quantity in **kg** | Replace payload window |
| `L` | Flaps | No change |
| `Shift+G` | Gear handle position | No change |
| `W` | Waypoint info | No change |
| `Shift+W` | Gross weight in **both lbs and kg** | Single announcement e.g. *"150,000 lbs / 68,039 kg"* |
| `B` | Altimeter reading | No change |

### Hotkey Mapping (Input Mode `[`)

| Key | Action | Notes |
|-----|--------|-------|
| `Ctrl+B` | Set **both** captain and FO altimeters simultaneously | Currently sets one only |

### Implementation Notes

- `HOTKEY_GROSS_WEIGHT_KG` renamed to `HOTKEY_GROSS_WEIGHT` (now covers both units)
- `HOTKEY_FUEL_QUANTITY` handler updated to read lbs
- `HOTKEY_FUEL_PAYLOAD` handler updated to call fuel kg announcement (remove window)
- `Ctrl+B` baro handler updated to fire two SimConnect events (captain + FO baro set)

---

## Dev Setup

**Prerequisites:**
1. .NET 9 SDK
2. `MSFS_SDK` environment variable set
3. FBW A32NX installed and running in sim
4. SimBridge enabled (default on, confirm running on port 8380)
5. FBW source at `D:\Claude\fbw` for variable lookups
6. NVDA or JAWS for accessibility testing

**Build:**
```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

**Variable lookup workflow:** Search `D:\Claude\fbw\aircraft\fbw-a32nx\src\` for any LVar name before adding or modifying a variable. Do not guess or invent names.

---

## Release Process

1. All work on `feature/fbw-a32nx-update` branch
2. Test against live sim: variable audit coverage, all new panels, MCDU connect/interact, all hotkeys
3. Update `Checklists/FlyByWire_A32NX_Checklist.txt` to reflect new panels
4. Update `HotkeyGuides/` for `F`, `Shift+F`, `Shift+W` changes
5. Open PR against `main` with full change description
6. After merge: create GitHub Release with compiled x64 Release binary and release notes

**Release notes must call out:**
- Which variables were updated (users with broken controls will want to know)
- New panels added (so users know what's newly accessible)
- MCDU now available via `[` → `Shift+M` (requires SimBridge)
- Hotkey changes: `F` now lbs, `Shift+F` now kg announcement, `Shift+W` now combined lbs/kg
