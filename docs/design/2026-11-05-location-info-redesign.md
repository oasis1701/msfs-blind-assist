# Location Info window redesign (proposal)

Status: proposal, for review. Nothing is built yet.

## Why

The Location Info window (Output Shift+L, `Forms/LocationInfoForm.cs`) is eight read-only text
boxes, one per category: Where you are, Major Cities, Tourist Landmarks, Terrain, Nearby Places,
Cardinal Directions, Airports, Water Bodies. To find one thing you tab through every box and arrow
through each one. You can't search, filter, sort or see coordinates, and the results are always
around the aircraft.

What prompted this: finding a small island (Krokholmen, a sailing camp) and the airport nearest to
it. The location data has the island (57.93333, 11.66667), but the window has no way to get to it.

## What exists today (checked in the code)

- `OfflineLocationService.GetLocationInfo(lat, lon, settings)` takes any position, not only the
  aircraft's. Searching around a place therefore needs no new database code.
- The `place` table is clustered on a 0.25° grid (`LocationGrid`), so searching around a point is
  one range seek per grid row: fast.
- Range ceilings: 310 miles / 500 km (`LocationQuerySettings.MaxRangeMiles/MaxRangeKm`), set
  because a query's cost grows with the area it covers.
- Categories in the data (`LocationCategory`): Town, Village, Airport, Terrain, Water, Landmark,
  and Undersea (stored, never shown).
- There is no index on place names. Measured on the 2026-10-21 database (8.36 million places),
  reading the file from Python: an exact name about 0.63 s, starts-with about 0.65 s, contains
  about 0.86 s. Every one reads the whole table.
- `FindTowns` (Airport Lookup's city search on MSFS 2024) matches only exact folded names of
  towns, in 170 to 340 ms by its own comment.
- `WaypointTracker` stores a slot as an ident plus latitude and longitude, filled from a
  `WaypointFix`, and reads it out as "<ident>, N nautical miles, N degrees". A place from the
  location data can fill a slot through a `WaypointFix` with its name as the ident.
- The Airports box lists GeoNames airports (category Airport), not airports from BA's navigation
  database, so it has no ICAO codes.

## Proposed layout

From top to bottom, in tab order:

1. **Where you are**: the same line as today (`LocationPhrases`), read-only, focused on opening, so
   Shift+L still answers "where am I" straight away (LOC-8: the window speaks nothing itself).
2. **Search around**: combo box. "The aircraft" by default. After you pick "Search around this
   place" on a result, it also holds that place's name, and the list is measured from there.
3. **Search**: text box. Empty means everything in range. Text filters by name (folded the same
   way as `SearchText.Fold`: case and accents ignored).
4. **Scope**: combo box. "Within range" (around the point above) or "Whole world, by name".
5. **Category**: combo box. All, Towns, Villages, Airports, Terrain, Water, Landmarks.
6. **Direction**: combo box. All, N, NE, E, SE, S, SW, W, NW (45° sectors, true bearing from the
   search point).
7. **Range**: numeric field, in the GeoNames tab's distance unit, up to the existing ceiling.
   Starts at the tab's saved value; changing it here doesn't change the saved setting.
8. **Show coordinates**: check box.
9. **Results**: one list, sorted by distance, nearest first. Each line:
   "Krokholmen, island, Västra Götaland, 23 nm northwest". With coordinates on:
   "..., N57°56.0′ E011°40.0′". Coordinates are in degrees and decimal minutes, the format a CDU
   takes.
10. Context menu on a result (Applications key or Shift+F10):
    - Track in slot 1 to 5
    - Search around this place
    - Copy coordinates, in a submenu: Boeing CDU (N5756.0E01140.0), Airbus MCDU
      (5756.0N/01140.0E), decimal degrees (57.93333, 11.66667)

Changing any filter updates the list at once. The count of results is spoken only when you
press Enter in the search box, not on every filter change (CORE-7: the screen reader already says
the new combo value).

## Performance

- Around a point: uses the existing grid search, so it stays as fast as today's window.
- Whole world by name: about 0.6 to 0.9 s today, because there's no name index. It has to run off
  the UI thread, with the list showing "Searching..." until it's done. Optional: add a name index
  when the database is built (faster exact and starts-with searches, bigger file, and every
  existing database needs rebuilding: a schema bump, LOC-7). Proposal: start without the index
  and measure in the window first.

## Not changing

- Output C and the automatic nearest-city announcement stay as they are.
- Offline only (LOC-1): no web lookup when a key is pressed.
- The GeoNames tab's saved ranges and counts stay the defaults.

## Open questions

- Should the Airports category use BA's navigation database (ICAO codes, runways, can go
  straight to Airport Lookup or the destination) instead of GeoNames' airport list, or as well?
- Major Cities and Cardinal Directions: drop them as categories (the list with a Towns filter and
  a direction filter covers both), or keep them as presets?
- Should the window remember the filters between openings?
- What a tracked place says when read with Output 1 to 5: the plain name, or name and category?
- List or tree: this proposal uses one flat list, because sorting by distance works across
  categories. A tree grouped by category (`NativeAccessibleTreeView`, CORE-12) is the alternative.
