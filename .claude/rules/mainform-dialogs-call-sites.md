---
paths:
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
---
# Rules whose code MainForm.Dialogs.cs holds

MIRRORS: copied word for word from gsx-docking.md, whose globs leave this partial out: `OpenTaxiForm` hands `AircraftPosition.HeadingMagnetic` to the taxi window as it is. Change both together (ClaudeContextBudgetTests checks).

- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/gsx-docking.md#dck-34
