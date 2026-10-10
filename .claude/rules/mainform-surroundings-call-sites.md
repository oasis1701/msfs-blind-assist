---
paths:
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.MenuHandlers.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
---
# Surroundings rules for MainForm's airport lookups and resets

MIRRORS: copied word for word from surroundings.md, whose globs leave MainForm out; change both together (ClaudeContextBudgetTests checks).

- [SUR-4] "Which airport am I at" is always `CurrentAirport.Resolve` -> `CurrentAirportResolver.Pick` (four true-distance passes, order load-bearing), never `GetNearbyAirportICAOs`; the catalog is never built on the UI thread (`SurroundingsCatalogCache.GetAsync` only) (more: see full). Full: docs/invariants/surroundings.md#sur-4
- [SUR-15] A database switch (`RefreshDatabaseProvider`) clears `surroundingsCache`, `onlineFeatures`, `ClearWhereAmICache()` and `groundTrafficMonitor.ClearRunwayCache()` and calls `surroundingsMonitor.Reset()`, which also runs on reconnect, aircraft switch and turnaround liftoff: no staleness token moves on a switch. Full: docs/invariants/surroundings.md#sur-15
