---
paths:
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
---
# Stand naming rules for the taxi graph's parking pass

MIRRORS: copied word for word from gsx-stands.md, whose globs leave TaxiGraph.cs out. Their code: `Build`'s parking pass, which marks nodes `Parking` and names them with `FormatParkingDisplayName` from `ParkingSpot.Name`. Change a rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [DCK-8] `ParkingSpot.Name` is the CONCOURSE LETTER on every path, never terminal prose; `uiTerminalName` goes in `TerminalName`, rendered only when `TerminalNameDisambiguates`; split identity with `StandId.Parse`, never a local regex (more: see full). Full: docs/invariants/gsx-stands.md#dck-8
- [DCK-9] `GsxConcourseLetterFiller` borrows a missing concourse letter right after the reader: NAME-ONLY, never overwriting a letter GSX supplied, and `Name = ""` stays a supported shape. Full: docs/invariants/gsx-stands.md#dck-9
- [DCK-40] A stand has ONE name app-wide: `GetSelectableGates` to ACT on a stand, `GetNamedSpots` to name one and for every `TaxiGraph.Build` given parking; never build a pilot-heard list from `GetParkingSpots`, nor call the supplier per position update (more: see full). Full: docs/invariants/gsx-stands.md#dck-40
- [DCK-41] Never feed `TaxiGraph.Build` a spot list other than navdata's own set: its parking pass sets `TaxiNodeType.Parking` and can MOVE A HOLD-SHORT; the exceptions are builds given no parking at all: the runway-rows-only ones and the briefing's `OsmPlanningGraph`. Full: docs/invariants/gsx-stands.md#dck-41
