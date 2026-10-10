---
paths:
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
---
# Rules whose code MainForm.Dialogs.cs and MainForm.SayIntentions.cs hold

MIRRORS: copied word for word from simconnect-data.md and surroundings.md, whose globs leave these partials out: `OpenTaxiForm` (Dialogs) and the SayIntentions import (`LoadAirportForExternalRouteAsync`) hand `AircraftPosition.HeadingMagnetic` to the taxi window as it is; Dialogs claims `_augmentPrefetched` prefetches. Change both together (ClaudeContextBudgetTests checks).

- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/simconnect-data.md#dck-34
- [SUR-5] `AirportWarmUp` readies online taxiway names and the surroundings catalog before the pilot asks (current airport on the ground only, Shift+D destination anywhere); name prefetches share `_augmentPrefetched`, and `PrefetchAsync` returns at once while online taxi data is off. Full: docs/invariants/surroundings.md#sur-5
