---
paths:
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
---
# Rules whose code MainForm.Dialogs.cs and MainForm.SayIntentions.cs hold

MIRRORS: copied word for word from simconnect-data.md, whose globs leave these partials out: `OpenTaxiForm` (Dialogs) and the SayIntentions import (`LoadAirportForExternalRouteAsync`) hand `AircraftPosition.HeadingMagnetic` to the taxi window as it is. Change both together (ClaudeContextBudgetTests checks).

- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/simconnect-data.md#dck-34
