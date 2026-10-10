---
paths:
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
  - "MSFSBlindAssist/MainForm.MenuHandlers.cs"
---
# SayIntentions clearance and fresh-position rules for MainForm

MIRRORS: copied word for word from sayintentions-clearance.md and surroundings.md, whose globs leave these partials out; change both together (ClaudeContextBudgetTests checks).

- [SIC-8] A CLEARANCE route's announcement must NAME both lost kinds: a taxiway the dialog could not seat AND one the airport lacks (`ScanTaxiways`'s `Unresolved`); a GEOMETRY route drops the second on purpose: never restore it, keep its `notAtAirport=[…]` log. Full: docs/invariants/sayintentions-clearance.md#sic-8
- [SUR-16] The Settings taxiway-name refresh resolves its airport from a position asked of the simulator at the press (`GetFreshAircraftPositionAsync`; `LastKnownPosition`, stale in quiet cruise, is only its 1.5 s fallback), via `CurrentAirport.Resolve` off the UI thread. Full: docs/invariants/surroundings.md#sur-16
