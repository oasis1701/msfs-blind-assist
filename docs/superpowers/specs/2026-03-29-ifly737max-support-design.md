# Design: iFly Boeing 737 MAX 8 Support

**Date:** 2026-03-29
**Status:** Phase 1 ready to implement — Phase 2 requires aircraft install
**Prerequisites:** MobiFlight WASM module, FLYINGTOMO "Add Lvar for iFly 737 Max8" addon

---

## Overview

Add full accessibility support for the iFly Boeing 737 MAX 8 in MSFS, at parity with the existing Fenix A320 CEO implementation. Covers cockpit panel controls, MCP (Mode Control Panel) readout and manipulation, background state monitoring with screen reader announcements, and CDU/FMC display access.

iFly does not expose an SDK like PMDG. All cockpit state is accessed via L:VARs through MobiFlight WASM, H: events through HubHop/MobiFlight, and a CDU WebSocket server the aircraft natively exposes at `ws://localhost:8320/winwing/cdu-captain`.

---

## Prerequisites

Two community addons must be installed alongside MobiFlight:

1. **MobiFlight WASM module** — already required by existing aircraft (FBW, Fenix)
2. **FLYINGTOMO "Add Lvar for iFly 737 Max8"** (flightsim.to/file/89774) — bridges the iFly SDK's internal byte-level variables into readable L:VARs for MCP values and FMC annunciator lights. Without this, MCP readback (speed/heading/altitude/VS/baro) is unavailable.

---

## New Files

```
MSFSBlindAssist/
  Aircraft/
    IFly737MaxDefinition.cs          ← aircraft definition
  Services/
    IFly737CDUService.cs             ← WebSocket CDU service (port 8320)
  Forms/
    IFly737/
      IFly737CDUForm.cs              ← CDU display and interaction form
      IFly737SpeedWindow.cs          ← MCP speed inc/dec dialog
      IFly737HeadingWindow.cs        ← MCP heading inc/dec dialog
      IFly737AltitudeWindow.cs       ← MCP altitude inc/dec dialog
      IFly737VSWindow.cs             ← MCP VS inc/dec dialog
      IFly737BaroWindow.cs           ← MCP baro dialog (hPa/inHg + STD)
      IFly737AutopilotWindow.cs      ← Autopilot engage dialog (CMD A/B, A/T ARM)
```

**Modified files:** `MainForm.cs`, `MainForm.Designer.cs`

---

## Aircraft Definition — `IFly737MaxDefinition.cs`

Inherits `BaseAircraftDefinition`. All MCP controls use `FCUControlType.IncrementDecrement` (same as Fenix).

```csharp
public override string AircraftName => "iFly Boeing 737 MAX 8";
public override string AircraftCode => "IFLY_737MAX8";

public override FCUControlType GetAltitudeControlType()  => FCUControlType.IncrementDecrement;
public override FCUControlType GetHeadingControlType()   => FCUControlType.IncrementDecrement;
public override FCUControlType GetSpeedControlType()     => FCUControlType.IncrementDecrement;
public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;
```

### Variables (`GetVariables`)

#### MCP Readback L:VARs — FLYINGTOMO addon (confirmed names)

| Key | L:VAR | Description |
|---|---|---|
| `add_iFly_spd_value` | `L:add_iFly_spd_value` | MCP speed display (999 = dashes) |
| `add_iFly_vs_value` | `L:add_iFly_vs_value` | MCP VS display |
| `add_iFly_vs_blank` | `L:add_iFly_vs_blank` | VS blank/dash flag (9999 = dashes) |
| `add_iFly_baro_status` | `L:add_iFly_baro_status` | Baro mode status |
| `add_iFly_std_status` | `L:add_iFly_std_status` | STD baro engaged |
| `add_iFly_QNH_HG_value` | `L:add_iFly_QNH_HG_value` | Baro setting in inHg |
| `add_iFly_QNH_MB_value` | `L:add_iFly_QNH_MB_value` | Baro setting in hPa |
| `add_iFly_bank_value` | `L:add_iFly_bank_value` | Bank angle limit |

#### MCP Readback L:VARs — names to verify on install (Phase 2)

| Key | L:VAR (tentative) | Description |
|---|---|---|
| `add_iFly_hdg_value` | `L:add_iFly_hdg_value` | MCP heading display |
| `add_iFly_alt_value` | `L:add_iFly_alt_value` | MCP altitude display |
| `add_iFly_crs_l_value` | `L:add_iFly_crs_l_value` | Course selector left |
| `add_iFly_crs_r_value` | `L:add_iFly_crs_r_value` | Course selector right |
| `add_iFly_at_arm_status` | `L:add_iFly_at_arm_status` | Autothrottle ARM status |
| `add_iFly_cmd_a_status` | `L:add_iFly_cmd_a_status` | CMD A engaged |
| `add_iFly_cmd_b_status` | `L:add_iFly_cmd_b_status` | CMD B engaged |
| `add_iFly_fd_l_status` | `L:add_iFly_fd_l_status` | Captain flight director |
| `add_iFly_fd_r_status` | `L:add_iFly_fd_r_status` | FO flight director |
| `add_iFly_vnav_status` | `L:add_iFly_vnav_status` | VNAV mode engaged |
| `add_iFly_lnav_status` | `L:add_iFly_lnav_status` | LNAV mode engaged |

#### FMC Annunciator Lights — background monitoring (FLYINGTOMO addon, confirmed)

All `UpdateFrequency.Continuous`, `IsAnnounced = true`:

| Key | L:VAR | Announced as |
|---|---|---|
| `add_iFly_EXEC_status` | `L:add_iFly_EXEC_status` | "FMC Execute" / cleared |
| `add_iFly_MSG_status` | `L:add_iFly_MSG_status` | "FMC Message" / cleared |
| `add_iFly_FAIL_status` | `L:add_iFly_FAIL_status` | "FMC Fail" / cleared |
| `add_iFly_CALL_status` | `L:add_iFly_CALL_status` | "FMC Call" / cleared |
| `add_iFly_OFST_status` | `L:add_iFly_OFST_status` | "FMC Offset" / cleared |

#### Overhead & Pedestal L:VARs — Phase 2

All variables in the Overhead and Pedestal panels are `// PLACEHOLDER` stubs. Names are discovered post-install using the MobiFlight Variable Viewer and community HubHop profiles. Naming convention follows the iFly SDK (lowercase, e.g., `FD_left_Switches_Status`).

### Panel Structure (`GetPanelStructure`)

```
Overhead:
  - Electrical
  - IRS
  - Hydraulic
  - Pneumatics & Bleed Air
  - Air Conditioning & Pressurization
  - Anti-Ice
  - Fire
  - Fuel
  - APU
  - Engine Start & Ignition
  - Signs & Lighting
  - Oxygen
  - Miscellaneous

Pedestal:
  - Engine Levers & Speedbrake
  - Fuel Crossfeed
  - Radio Management
  - ATC & TCAS
  - GPWS / EGPWS
  - Weather Radar
  - Parking Brake

Main Instrument Panel:
  - Landing Gear
  - Auto Brakes
  - Instruments & Lighting
  - Audio

Glareshield / MCP:
  - Flight Director
  - Autothrottle
  - Speed
  - Heading
  - Altitude
  - Vertical Speed
  - Course Selectors
  - Mode Buttons (VNAV, LNAV, APP, HDG SEL, LVL CHG, ALT HOLD, VS)
  - Autopilot Engage (CMD A, CMD B)
```

### Hotkey Mapping (`HandleHotkeyAction`)

Reuses all existing `HotkeyAction` enum values. No new enum values needed.

| HotkeyAction | 737 Behavior |
|---|---|
| `ReadSpeed` | Read `L:add_iFly_spd_value`, announce with mode context |
| `ReadHeading` | Read `L:add_iFly_hdg_value`, announce heading |
| `ReadAltitude` | Read `L:add_iFly_alt_value`, announce altitude |
| `ReadFCUVerticalSpeedFPA` | Read `L:add_iFly_vs_value` / `vs_blank`, announce VS or "dashes" |
| `ReadAltimeter` | Read QNH HG + MB + STD status, announce baro |
| `ReadFuelQuantity` | Read fuel quantity L:VAR (Phase 2 name) |
| `ReadGrossWeightKg` | Read gross weight L:VAR (Phase 2 name) |
| `ReadWaypointInfo` | Repurposed → gross weight lbs |
| `ShowFuelPayloadWindow` | Repurposed → fuel quantity kg |
| `ReadFlaps` | Read flap position L:VAR (Phase 2 name) |
| `ReadGear` | Read gear position |
| `FCUHeadingPush` | H: HDG SEL button toggle |
| `FCUHeadingPull` | H: heading knob pull |
| `FCUAltitudePush` | H: altitude 1000ft step decrement |
| `FCUAltitudePull` | H: altitude 1000ft step increment |
| `FCUSpeedPush` | H: SPD intervention toggle |
| `FCUSpeedPull` | H: SPD knob pull |
| `FCUVSPush` | H: VS mode engage |
| `FCUVSPull` | H: VS knob pull |
| `FCUSetHeading` | Show `IFly737HeadingWindow` |
| `FCUSetAltitude` | Show `IFly737AltitudeWindow` |
| `FCUSetSpeed` | Show `IFly737SpeedWindow` |
| `FCUSetVS` | Show `IFly737VSWindow` |
| `FCUSetBaro` | Show `IFly737BaroWindow` |
| `FCUSetAutopilot` | Show `IFly737AutopilotWindow` |
| `MonitorManager` | Show monitor manager (universal) |
| `ReadDisplayPFD` | Gemini screenshot → PFD |
| `ReadDisplayND` | Gemini screenshot → ND |
| `ReadDisplayUpperECAM` | Gemini screenshot → Upper EICAS |
| `ReadDisplayLowerECAM` | Gemini screenshot → Lower EICAS/SD |

All H: event names for MCP knobs/buttons are `// PLACEHOLDER` in Phase 1, discovered via HubHop and iFly key assignment list post-install.

---

## CDU Service — `IFly737CDUService.cs`

Independent from `FenixMCDUService`. Same structural pattern: async WebSocket connection loop, exponential backoff reconnect, `DisplayUpdated` and `ConnectionStatusChanged` events.

**Connection target:** `ws://localhost:8320/winwing/cdu-captain`

**Phase 1:** Connection management only. `ParseDisplayMessage(string json)` is a stub that returns an empty `CDUDisplayData`. `SendButtonPress(string buttonName)` is a stub that logs the call.

**Phase 2:** After install, connect a raw WebSocket client to port 8320, log all incoming messages, and implement the parser. Expected format is JSON with 14 lines of content + scratchpad (Boeing CDU: 14 rows × 24 chars), similar structure to the Fenix XML format but different encoding.

**Data model:** Reuses `MCDUDisplayData` and `MCDULinePair` types from `FenixMCDUService` — these are generic enough to represent any CDU layout. No new types needed.

---

## CDU Form — `IFly737CDUForm.cs`

Independent from `FenixMCDUForm`. Identical UX structure: ListBox display, scratchpad TextBox, page buttons. Same accessibility, same keyboard shortcuts.

**Boeing CDU page buttons:**

| Row | Buttons (text → event name) |
|---|---|
| 1 | Init Ref → `INIT_REF`, Rte → `RTE`, Clb → `CLB`, Crz → `CRZ`, Des → `DES` |
| 2 | Legs → `LEGS`, Dep Arr → `DEP_ARR`, Hold → `HOLD`, Prog → `PROG`, N1 Limit → `N1_LIMIT` |
| 3 | Fix → `FIX`, Prev Page → `PREV_PAGE`, Next Page → `NEXT_PAGE` |

**Keyboard shortcuts (identical to Fenix MCDU form):**
- **Ctrl+1–6** → LSK1L–LSK6L
- **Alt+1–6** → LSK1R–LSK6R
- **Backspace** → CLR
- **PageUp** → PREV PAGE
- **PageDown** → NEXT PAGE
- **Alt+S** → focus scratchpad
- **Alt+Home** → focus display
- **Enter** (in scratchpad) → send text character by character

**Title announcement:** page title changes trigger immediate screen reader announcement and reset focus to first content line.

---

## MCP Dialogs — `Forms/IFly737/`

Six dialogs, all following the Fenix increment/decrement window pattern:

| Form | Inc H: event | Dec H: event | Read L:VAR |
|---|---|---|---|
| `IFly737SpeedWindow` | Phase 2 | Phase 2 | `L:add_iFly_spd_value` |
| `IFly737HeadingWindow` | Phase 2 | Phase 2 | `L:add_iFly_hdg_value` |
| `IFly737AltitudeWindow` | Phase 2 (100ft / 1000ft steps) | Phase 2 | `L:add_iFly_alt_value` |
| `IFly737VSWindow` | Phase 2 | Phase 2 | `L:add_iFly_vs_value` |
| `IFly737BaroWindow` | Phase 2 | Phase 2 | `L:add_iFly_QNH_HG_value` + MB + STD |
| `IFly737AutopilotWindow` | — | — | CMD A/B, A/T ARM buttons |

`IFly737BaroWindow` adds a "STD" toggle button (sets standard pressure).
`IFly737AutopilotWindow` has discrete buttons for CMD A, CMD B, A/T ARM rather than knob inc/dec.

---

## MainForm Integration

**Menu item:** `Aircraft → iFly Boeing 737 MAX 8` (alongside existing FBW A320, Fenix A320)

**`LoadAircraftFromCode` switch:** add `case "IFLY_737MAX8": return new IFly737MaxDefinition();`

**CDU form lifecycle:** same pattern as Fenix — `IFly737CDUForm` is created once, shown/hidden on demand, never disposed until app exits.

---

## Two-Phase Execution Plan

### Phase 1 — Build skeleton (before purchase)
1. Create `IFly737MaxDefinition.cs` with confirmed L:VARs, placeholder H: events, full panel structure stubs
2. Create `IFly737CDUService.cs` with connection skeleton, stub parser
3. Create `IFly737CDUForm.cs` with full Boeing CDU UI
4. Create 6 MCP dialog forms (stubs for inc/dec events, readback wired where L:VARs are confirmed)
5. Wire `MainForm.cs` menu and `LoadAircraftFromCode`
6. Verify builds and aircraft appears in menu

### Phase 2 — Populate variables (after install)
1. Load aircraft in MSFS, open MobiFlight Variable Viewer — capture all L:VAR names for overhead and pedestal systems
2. Open HubHop, filter by iFly 737 MAX — capture all H: event names for MCP buttons/knobs
3. Open iFly key assignment dialog — capture `KEY_COMMAND_*` names for any events not in HubHop
4. Connect raw WebSocket client to `ws://localhost:8320/winwing/cdu-captain` — log messages, implement `ParseDisplayMessage` and `SendButtonPress`
5. Fill all `// PLACEHOLDER` stubs in `IFly737MaxDefinition.cs`
6. Fill H: event names in all 6 MCP dialog forms
7. Test all panels, monitoring announcements, MCP dialogs, and CDU form end-to-end

---

## Out of Scope

- Shared `ICDUService` interface between Fenix and iFly services (decided: keep independent)
- MSFS 2024 compatibility testing (iFly 737 MAX 8 has experimental 2024 support as of mid-2025 — test in Phase 2)
- Second CDU (FO side at `ws://localhost:8320/winwing/cdu-fo`) — not planned
- Shared FMC monitoring window (monitor manager handles state announcements)
