# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

MSFS Blind Assist - C# Windows Forms accessibility application for Microsoft Flight Simulator. Multi-aircraft support (FlyByWire A320, Fenix A320, extensible). SimConnect integration, screen reader optimized (NVDA/JAWS). .NET 9, Windows Forms, SQLite.

## Build Commands

```bash
dotnet build MSFSBlindAssist.sln -c Debug
dotnet build MSFSBlindAssist.sln -c Release
```

**Output:** `MSFSBlindAssist\bin\x64\{Debug|Release}\net9.0-windows\win-x64\`

**Prerequisites:** MSFS_SDK environment variable, .NET 9 SDK

## CRITICAL Rules (Always Follow)

### Screen Reader Announcements

**CRITICAL:** Screen readers automatically announce ALL UI control interactions.

**NEVER announce:**
- Button presses in panel controls
- Combo box/dropdown value changes
- Any direct user interaction with UI elements

**ONLY announce:**
- Numeric input confirmations (user needs exact value feedback)
- Error conditions (validation failures)
- Background state changes (not directly triggered by user)

**Why:** Screen readers already announce UI interactions. Redundant announcements = poor UX.

### SimConnect Connection Timing

**CRITICAL:** In SimConnectManager.cs, set `IsConnected = true` BEFORE calling `SetupDataDefinitions()`. Required for `StartContinuousMonitoring()` to execute properly (has guard clause requiring `IsConnected == true`). See SimConnectManager.cs:251

### Multi-Aircraft Architecture

**Core interfaces:**
- **IAircraftDefinition** - Contract for all aircraft
- **BaseAircraftDefinition** - Recommended base class (provides hotkey routing, caching, helpers)
- **FlyByWireA320Definition** - Reference implementation

**Each aircraft defines:**
- `GetVariables()` - All simulator variables
- `GetPanelStructure()` - Section/panel hierarchy
- `BuildPanelControls()` - Panel-to-variables mapping (cached automatically by base class)
- `GetHotkeyVariableMap()` - Simple hotkey action → event name mappings
- `HandleHotkeyAction()` - Custom hotkey logic (optional override)

## Quick Reference

### Adding Panel Control
1. Add to aircraft's `GetVariables()` with `UpdateFrequency.OnRequest`
2. Add variable key to `BuildPanelControls()` under appropriate panel
3. Test - automatic registration and UI generation

### Adding Background Monitoring
1. Add to `GetVariables()` with `UpdateFrequency.Continuous` + `IsAnnounced = true`
2. Do NOT add to `BuildPanelControls()` - batched monitoring is automatic
3. Change detection and announcements are automatic (supports 1000 variables)

### Adding New Aircraft
1. Create class inheriting `BaseAircraftDefinition`
2. Override: `GetVariables()`, `GetPanelStructure()`, `BuildPanelControls()`
3. Add menu item in `MainForm.Designer.cs` + click handler
4. Add to `LoadAircraftFromCode()` switch statement
5. Use `FlyByWireA320Definition.cs` as template

### Variable Types
- **K:EVENT** - Standard MSFS events (via SimConnect TransmitClientEvent)
- **L:VARIABLE** - Local variables (reading aircraft state)
- **H:EVENT** - Hardware events (via MobiFlight WASM module)
- **PMDGVar** - PMDG SDK variables (read via Client Data Area broadcast)

### PMDG 777 Specific Patterns

**Switch control:** Use CDA (SetClientData) with direct position values for most switches.
- Two-position toggles: `SendPMDGEvent(eventName, eventId, targetPosition)` where targetPosition is 0 or 1
- Multi-position selectors: same, with the target position index
- Momentary buttons: `SendPMDGEvent(eventName, eventId, 1)` — parameter 1 = pressed, 0 = no-op
- Continuous knobs (brightness, temperature, EFIS baro/mins): **cannot be controlled via SDK** — do not add to panels
- **Fuel control levers:** Exception — use CDA with **inverted** parameter (1=Cutoff, 0=Run). See special case in HandleUIVariableSet.
- **Ground power switches (ELEC_ExtPwr):** Momentary push buttons — send parameter 1 regardless of target. See special case in HandleUIVariableSet.

**Radio frequencies and transponder:** Use standard SimConnect events (not PMDG SDK):
- `COM_STBY_RADIO_SET_HZ` / `COM2_STBY_RADIO_SET_HZ` for setting standby freqs
- `COM_STBY_RADIO_SWAP` / `COM2_RADIO_SWAP` for swapping active/standby
- `XPNDR_SET` for squawk code (BCD16 encoded)

**CDU interaction:** CDU buttons must send parameter 1 (pressed) via CDA; parameter 0 also registers as a press (not a release). Text entry sends one character at a time with 350ms delay; repeated characters need an extra 400ms for the CDU to distinguish separate presses. CDU display uses color and font-size data to detect toggle selections (non-white color or non-small font = selected, marked with `X`). Toggle detection only applies to rows with adjacent `<>` (mapped from 0xA1/0xA2 arrow symbols). Scratchpad announcements are suppressed during text entry and clearing (`_typingInProgress`/`_clearingInProgress` flags); `_previousScratchpad` is only updated when the announcement actually fires. CLR uses `_clearingInProgress` to suppress intermediate states and only announces "Cleared" once the scratchpad is empty.

**MCP dialogs:** Use `ValueInputForm` with `ToggleButtonDef` for mode toggles. Opened non-modal (`Show()`, not `ShowDialog()`) with `ShowCancelButton = false` so other windows remain accessible. Dialogs stay open after value entry (callback pattern). `MCP_IASBlank` indicates FMC-controlled speed.

**Announcements:** Use `Announce()` (queued) in ProcessSimVarUpdate, `AnnounceImmediate()` only in HandleHotkeyAction. `IsAnnounced = true` is required for continuous monitoring registration. Suppress button push state (_Sw_Pushed) announcements via RenderAsButton check. Annunciator lights announce both on and off states. For variables needing cache but no auto-announcement, set `IsAnnounced = true` and return `true` from ProcessSimVarUpdate to suppress.

## Detailed Documentation

**Claude: Read these docs only when the task specifically requires them.**

**When to read detailed docs:**
- **Adding complex features or workflows** → [Adding Features](docs/adding-features.md), [Quick Reference](docs/QUICK-REFERENCE.md)
- **Implementing new aircraft** → [Architecture](docs/architecture.md), [Adding Features](docs/adding-features.md)
- **Working with FCU/MCP/display systems** → [Architecture](docs/architecture.md)
- **Adding or modifying hotkeys** → [Hotkey System](docs/hotkey-system.md)
- **Fenix rotary encoders (RMP, FCU)** → [Fenix Increment/Decrement](docs/fenix-increment-decrement.md)
- **Tuning visual guidance PID controller** → [Visual Guidance](docs/visual-guidance.md)
- **Understanding variable patterns** → [Variable System](docs/variable-system.md)
- **API reference** → [Aircraft Definitions](docs/aircraft-definitions.md)
- **Dependencies and key files** → [Development](docs/development.md)

**Available documentation:**
- **[Quick Reference](docs/QUICK-REFERENCE.md)** - Common patterns and workflows (read first for most tasks)
- **[Architecture](docs/architecture.md)** - Core components, multi-aircraft system, FCU architecture
- **[Adding Features](docs/adding-features.md)** - Step-by-step workflows for common development tasks
- **[Variable System](docs/variable-system.md)** - Three patterns for managing variables (Panel, Monitoring, Hotkey)
- **[Fenix Increment/Decrement](docs/fenix-increment-decrement.md)** - Counter-based pattern for Fenix rotary encoders
- **[Visual Guidance](docs/visual-guidance.md)** - PID controller tuning and ground track monitoring
- **[Aircraft Definitions](docs/aircraft-definitions.md)** - Multi-aircraft dictionary system API reference
- **[Hotkey System](docs/hotkey-system.md)** - Dual-mode hotkeys and multi-aircraft routing
- **[Development](docs/development.md)** - Dependencies, key files, development notes

## Technology Stack

.NET 9 (C# 13), Windows Forms, SimConnect SDK (MSFS), SQLite, NVDA/Tolk (screen readers)
