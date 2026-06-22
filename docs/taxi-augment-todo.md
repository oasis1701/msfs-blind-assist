# Taxi-Data Augmentation — deferred / next items

Tracked follow-ups for the `feat/taxi-data-augmentation` feature. The core feature + most
enhancements are DONE on this branch (see CLAUDE.md "Taxi-data augmentation" + `docs/taxi-guidance.md`).

## DONE (2026-06)
- ✅ **Gate/parking aliases** — `ParkingSpot.Aliases`; `AugmentingAirportDataProvider.GetParkingSpots`
  collects online gate names that differ from a navdata spot name (≤30 m, normalized-dedup);
  surfaced as labeled, selectable entries in TaxiAssistForm ("47 (GN 3 - …)" → same spot) and in
  the gate-teleport listbox (`ParkingSpot.ToString()` appends "(also 47)"). Fixes the CYUL-class
  case where OSM has the real gate numbers. (`Describe()` vs `ToString()` split keeps labels clean.)
- ✅ **Self-describing taxiway alias labels** — dropdown shows "B (HAWKER)" (was bare "B"); exact
  resolution via `TaxiGraph.AliasDisplayToCanonical`. Tested in `tools/ProgressiveTaxiProbe`.
- ✅ **In-dialog enable checkbox + visible ODbL attribution** — Taxi Guidance Options.
- ✅ **apt.dat as a gate source** — SUBSUMED: the apt.dat parser already feeds `AirportTaxiData.Parking`
  (ramp starts), so `GetParkingSpots` fill/alias already uses apt.dat gate names alongside OSM.

## REMAINING
### OSM holding_position → sharpen hold-shorts (the one risky item)
- OSM `node[aeroway=holding_position]` carries real hold-short positions (+ often the runway in
  `ref`). Plan: fetch them in `OsmTaxiSource`, then feed as a navdata-AUTHORITATIVE fill into the
  hold-short derivation (`TaxiGuidanceManager.InsertRunwayCrossingHoldShorts` / the
  `MatchHoldShortRunwayName` association).
- **Why deferred:** the hold-short pipeline is heavily tuned (see the many hold-short bullets in
  CLAUDE.md — KBOS/OMDB/EHAM edge cases). A fill that interferes with the geometric derivation could
  regress those. Needs a careful design + IN-SIM verification (a real airport where navdata
  hold-shorts are sparse but OSM has them) before shipping. Do as a focused session, not blind.
- Same safety rule as the rest: navdata authoritative; names/positions only attach to navdata
  geometry; never steer on an offset online line.

### GateTeleportnote
- Gate aliases there are shown via `ParkingSpot.ToString()` "(also 47)" (single entry). If a
  separate selectable "47" entry is wanted in the teleport listbox too, it needs a small
  display-wrapper around `ParkingSpot` (the listbox holds objects, not strings).
