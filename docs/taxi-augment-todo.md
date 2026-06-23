# Taxi-Data Augmentation — deferred / next items

Tracked follow-ups for the `feat/taxi-data-augmentation` feature. The core feature + most
enhancements are DONE on this branch (see CLAUDE.md "Taxi-data augmentation" + `docs/taxi-guidance.md`).

## DONE (2026-06)
- ✅ **Gate/parking aliases** — `ParkingSpot.Aliases`; the public `AugmentingAirportDataProvider.AugmentParking(icao, spots)`
  assigns online stands to navdata spots **1:1, nearest-pair-first, ≤50 m** (each online stand used
  once → no two gates get the same name), normalized-dedup; surfaced as labeled, selectable entries
  in TaxiAssistForm ("47 (GN 3 - …)" → same spot) and in the gate-teleport listbox
  (`ParkingSpot.ToString()` appends "(also 47)"). Fixes the CYUL/CYYZ-class case — **X-Plane apt.dat
  supplies real gate numbers** (CYYZ "Gate 131", KATL "A12") that navdata + OSM both lack.
  `AugmentParking` is public + called on the **GSX** gate list too (GSX bypasses `GetParkingSpots`).
  (`Describe()` vs `ToString()` split keeps labels clean.)
- ✅ **Self-describing taxiway alias labels** — dropdown shows "B (HAWKER)" (was bare "B"); exact
  resolution via `TaxiGraph.AliasDisplayToCanonical`. Tested in `tools/ProgressiveTaxiProbe`.
- ✅ **In-dialog enable checkbox + visible ODbL + X-Plane attribution** — Taxi Guidance Options.
- ✅ **Manual "Refresh Taxiway Names" announces the count** — `GetLastCoverage(icao)` →
  "Taxiway names refreshed for X: N added" / "No new names found".
- ✅ **In-memory cache only** (no disk) — fresh every session; dep/dest force-fresh.
- ✅ **apt.dat as a gate source** — SUBSUMED: the apt.dat parser already feeds `AirportTaxiData.Parking`
  (ramp starts), so the parking fill/alias already uses apt.dat gate names alongside OSM.
- ✅ **Deep-review fixes (5-pass audit, 2026-06)** — 1:1 gate matching (no duplicate names),
  empty-ref OSM parking skip, removed dead `MergeParking`; X-Plane Gateway field-casing + live fetch
  verified. No Critical issues; all safety invariants (navdata authoritative, online-only geometry
  ignored, alias never remaps a real name) confirmed holding.

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
