# Location Info window redesign (proposal)

Status: proposal, second round. The owner's review notes of 2026-11-05 are worked in. Nothing is
built yet.

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
  aircraft's. Searching around another point therefore needs no new database code.
- The `place` table is clustered on a 0.25° grid (`LocationGrid`), so searching around a point is
  one range seek per grid row: fast.
- Range ceiling: 310 miles or 500 km (`LocationQuerySettings.MaxRangeMiles/MaxRangeKm`), set
  because a query's cost grows with the area it covers.
- Categories in the data (`LocationCategory`): Town, Village, Airport, Terrain, Water, Landmark,
  and Undersea (stored, never shown). Each stored place also keeps its exact GeoNames feature code
  (`place.code`: ISL island, MT mountain, LK lake, and so on). `LocationFeatureCatalog` maps codes
  to categories; a code not in it (RK rock, for example) is not stored at all.
- GeoNames itself has two levels: a feature class (one letter: H water, T terrain, P populated
  place, S spot/building, ...) and a feature code under it. BA's categories are its own grouping
  of codes, not GeoNames' classes.
- There is no index on place names. Measured on the 2026-10-21 database (8.36 million places),
  reading the file from Python: an exact name about 0.63 s, starts-with about 0.65 s, contains
  about 0.86 s. Every one reads the whole table.
- `FindTowns` (Airport Lookup's city search on MSFS 2024) matches only exact folded names of
  towns, in 170 to 340 ms by its own comment.
- `WaypointTracker` stores a slot as an ident plus latitude and longitude, filled from a
  `WaypointFix`, and reads it out as "<ident>, N nautical miles, N degrees". A place can fill a
  slot through a `WaypointFix` with its name as the ident.
- The Airports box lists GeoNames airports (category Airport), not airports from BA's navigation
  database, so it has no ICAO codes.
- Tree check boxes (`NativeAccessibleTreeView.CheckboxStateImages`, CORE-18) have two states,
  ticked and unticked. There is no "partly ticked" image today.

## The map position

The window works around one point, the **map position**. It starts at the aircraft. "Set as map
position" on a result moves it to that place, and everything measured "from the map position" is
then measured from there. "Reset to aircraft position" moves it back.

## Proposed layout

From top to bottom, in tab order. The looking-around parts come first; the filters are hidden
until asked for, so they don't stand in the way.

1. **Where you are**: read-only, focused on opening, so Shift+L still answers "where am I" straight
   away (LOC-8: the window speaks nothing itself). First line: the aircraft, the same sentence as
   today (`LocationPhrases`). Second line, only when the map position is not the aircraft: the map
   position, e.g. "Map position: Krokholmen, island, Västra Götaland, 23 nm northwest of the
   aircraft".
2. **Results**: a table (list view with columns), sorted by distance from the map position,
   nearest first. Columns:
   - Name
   - Type: the GeoNames feature type (island, mountain, lake, airport, ...)
   - Region
   - From map position: distance and direction
   - From aircraft: distance and direction (the same as the column above while the map position
     is the aircraft; hidden then, or kept, to decide when built)
   - Details: what this type has. Height for terrain, population for towns, depth for water if
     the data has it, ICAO code and "in simulator" for airports. A height says its source
     (database or simulator, see Elevation).
   - Coordinates: decimal degrees (57.93333, 11.66667). Converting to a CDU's format is left to
     the pilot.
3. **Show filters and search** / **Hide filters and search**: one button that shows or hides
   everything below it. Its state is remembered.
4. **Search**: text box. Empty means everything in range. Text filters by name (folded the same
   way as `SearchText.Fold`: case and accents ignored).
5. **Scope**: combo box. "Around the map position" or "Whole world, by name".
6. **Range**: numeric field, in the GeoNames tab's distance unit, up to the ceiling (310 miles or
   500 km).
7. **Direction**: combo box. All, N, NE, E, SE, S, SW, W, NW (45° sectors, true bearing from the
   map position).
8. **Categories**: a tree with check boxes (`NativeAccessibleTreeView`, CORE-12/18). Built from
   `LocationFeatureCatalog` at run time, never a typed-in list:
   - All (expanded and ticked by default)
     - BA's categories: Towns, Villages, Airports, Terrain, Water, Landmarks
       - The feature types in each: under Terrain, island, islet, mountain, peak, hill, cape,
         peninsula, and so on.

   Ticking or unticking an item does the same to everything under it. Only the bottom level (the
   feature types) decides what the list shows; an upper item's box is just a switch for
   everything below it, and nothing else reads it. So its own state is only what it shows: ticked
   when everything under it is ticked, unticked otherwise. Unticking "mountain" therefore unticks
   Terrain and All, and ticking Terrain again ticks every type in it.

   Context menu on a category: **Use for Output C**. Output C then names the nearest place of the
   ticked types instead of its usual choice: "All" gives the nearest place of any kind, Towns only
   towns, island only islands.
9. **Reset filters** and **Reset to aircraft position**: two buttons at the bottom.

Changing a filter updates the list at once. The count of results is spoken only when you press
Enter in the search box, not on every filter change (CORE-7: the screen reader already says the
new value).

The window remembers its filters, range, scope, search text and the show/hide state between
openings. The map position resets to the aircraft each time the window opens (to decide; see Open
questions).

## Context menu on a result

Applications key or Shift+F10:

- Track in slot 1 to 5. The slot holds the place's plain name, read out as tracked fixes are today.
- Set as map position
- Copy coordinates (decimal degrees)

## Airports

The Airports category stays GeoNames' airport list, with two columns filled from BA's navigation
database where the airport is matched there: its ICAO code, and whether it is in the simulator.
How a GeoNames airport is matched to a navigation-database airport (by distance, by name, or both)
is to work out when built.

## Elevation (to test)

Heights for terrain results, especially mountains, hills and islands. Two possible sources, neither
checked yet:

- **GeoNames.** The dump has `elevation` (often empty for small places) and `dem` (a height
  modelled from satellite terrain data, coarse, so a small summit comes out lower).
  `LocationDataBuilder` drops both today. Keeping them is a new column and a schema bump (LOC-7).
  Measure first how many terrain places in the dump have a value and how far off `dem` is for known
  summits.
- **The simulator.** SimConnect gives only the ground height under the aircraft (`GROUND
  ALTITUDE`); there's no call for the height at a chosen position. Idea to try: create an invisible
  object at the position (`AICreateSimulatedObject`) and read its `GROUND ALTITUDE`. Unknowns:
  whether terrain loads that far from the aircraft, how long it takes, and whether the mesh
  flattens small summits. If it works, it gives the height the pilot will actually fly over.

The Details column says which source a height came from. Other per-type details (population,
water depth) follow the same column; population is already stored, depth is not checked.

## Performance

- Around a point: uses the existing grid search, so it stays as fast as today's window.
- Whole world by name: about 0.6 to 0.9 s today, because there's no name index. It runs off the
  UI thread, with the list showing "Searching..." until it's done. Start without a name index and
  measure in the window; add one (a schema bump, LOC-7) only if it's too slow.

## Not changing

- The automatic nearest-city announcement.
- Output C, except the optional "Use for Output C" filter above.
- Offline only (LOC-1): no web lookup when a key is pressed.
- Major Cities stays as it is for now (`MajorCityPopulationThreshold`, default 50,000, and
  `MaxMajorCitiesToShow`, default 10, on the GeoNames tab).

## Open questions

- The GeoNames tab's saved ranges: once the window remembers its own range, is the tab's
  Location Info range still needed, or only as the first-time default?
- Map position on opening: back to the aircraft each time, or kept from last time?
- Readable names for feature types (ISL → "island"): check whether the database or the download
  has GeoNames' feature-code names, or whether they need adding.
- Owner's own town missing from Major Cities with the 50,000 threshold: to look up (which town,
  its GeoNames population, the 10-city cap, the range).
