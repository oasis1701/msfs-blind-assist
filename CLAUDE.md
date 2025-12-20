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
