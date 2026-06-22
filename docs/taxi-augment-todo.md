# Taxi-Data Augmentation — deferred / next items

Tracked follow-ups for the `feat/taxi-data-augmentation` feature (online taxiway/gate-name
enrichment from OpenStreetMap + X-Plane apt.dat). Do one at a time; delete each when done.
The core feature is COMPLETE and shipped on this branch (see CLAUDE.md "Taxi-data augmentation"
+ `docs/taxi-guidance.md`). These are enhancements.

## 1. Parking / gate ALIAS in the gate dropdown  ← the Toronto/Montreal fix
**Problem:** Some sceneries + the navdata SQLite + GSX use **spot numbers** for stands, which do
NOT match the real **gate numbers** ATC/the airport use (e.g. Canada: ATC "gate A14 / 47" but
navdata calls it "GN 3" / a spot code). The pilot needs to pick the REAL gate number from the
dropdown.

**Empirical check (done 2026-06):**
- **CYUL (Montreal, MK Studios):** OSM HAS the real gate numbers (1–89, C1/E1/N1.., A/B suffixes),
  distinct from navdata's spot codes (G/GN/GS/GW). ⇒ alias-from-OSM WOULD fix it.
- **CYYZ (Toronto, FlightAmpa):** OSM is essentially EMPTY for parking (one ref, "B16"). Neither
  navdata nor OSM has FlightAmpa's gate numbers. ⇒ NO free-source fix; would need the scenery's
  own data or a manual map. (Check whether the X-Plane Gateway CYYZ apt.dat carries real gates —
  if so, apt.dat could cover it.)

**Design (mirror the taxiway alias, already shipped):**
- Add `List<string> Aliases` to `ParkingSpot` (in-memory only, like `TaxiPath.Aliases`).
- Extend `AugmentingAirportDataProvider.GetParkingSpots`: today it FILLS empty navdata names from
  online (≤30 m). ALSO collect online gate names that DIFFER from a non-empty navdata name (after
  a name normalize) as `ParkingSpot.Aliases`.
- Surface in the gate dropdowns — `TaxiAssistForm` (parking destination) AND `GateTeleportForm`:
  add a selectable entry per alias that maps back to the same `ParkingSpot` (so picking "47" routes
  to the navdata spot at that location). Mirror the taxiway pattern: a real-gate collision guard
  (never remap a name that's already a real navdata gate).
- Risk: both gate forms are large + untested; the combo population + selection→spot resolution must
  be changed carefully. That's why it's deferred from the marathon session, not because it's low
  value — it directly fixes CYUL-class airports.

## 2. In-dialog enable checkbox + visible attribution
- The `TaxiAugmentEnabled` setting works now (default on, editable in settings.json; `decorator.Enabled`
  reads it). Add the actual checkbox to the Taxi Guidance Options dialog ("Online taxiway names
  (OpenStreetMap + X-Plane)") — needs a small layout shift (move OK/Cancel down / resize the form;
  set tab order). A half-built version was reverted to avoid dead code.
- Add a VISIBLE attribution line (ODbL requires it): "Taxiway/gate names: © OpenStreetMap
  contributors (ODbL) + X-Plane Scenery Gateway." (Currently only in docs/CLAUDE.md.)

## 3. OSM holding_position → sharpen hold-shorts
- OSM `node[aeroway=holding_position]` carries real hold-short positions (+ sometimes the runway in
  `ref`). The OSM source already could fetch them; feed them into the hold-short derivation to
  improve hold-short placement at airports where navdata geometry is sparse. Keep navdata
  authoritative; treat OSM holds as a fill, same safety rule (names/positions onto navdata geometry).

## 4. apt.dat as a gate-number source for CYYZ-class airports
- For airports where OSM has no parking but the X-Plane Gateway apt.dat carries `1300` ramp starts
  with real gate names, the apt.dat parser (already extracts `Parking`) could feed item 1's alias.
  Verify per-airport coverage before relying on it.
