---
paths:
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/Services/GroundTrafficMonitor.cs"
---
# SimConnect heading-unit rule for the taxi window and the ground-traffic monitor

MIRRORS: copied word for word from gsx-docking.md, whose globs leave these files out: the taxi window reads `AircraftPosition.HeadingMagnetic` and takes it from MainForm's hand-offs as degrees, and the monitor adds magnetic variation to that heading and to the AI-traffic one. Change both together (ClaudeContextBudgetTests checks).

- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/gsx-docking.md#dck-34
