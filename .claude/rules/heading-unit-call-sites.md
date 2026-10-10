---
paths:
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/Services/GroundTrafficMonitor.cs"
  - "MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Displays.cs"
  - "MSFSBlindAssist/Forms/TcasForm.cs"
  - "MSFSBlindAssist/Services/TcasService.cs"
  - "MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs"
  - "MSFSBlindAssist/Services/LandingFlareAssistManager.cs"
  - "MSFSBlindAssist/Services/RouteAdvisoryLocator.cs"
  - "MSFSBlindAssist/Services/TakeoffAssistManager.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "tests/MSFSBlindAssist.Tests/GroundTrafficMonitorHarness.cs"
  - "tests/MSFSBlindAssist.Tests/TcasFormParsingTests.cs"
---
# Heading-unit rule for files that read a heading SimConnect delivered

MIRRORS: copied word for word from simconnect-data.md, whose globs leave these files out; change both together (ClaudeContextBudgetTests checks).

- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/simconnect-data.md#dck-34
