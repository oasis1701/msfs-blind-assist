# Location Info window redesign (proposal)

Status: agreed 2026-11-05, after three review rounds with the owner. Nothing is built yet.

**Done when:** everything under Proposed layout, The map position, Refreshing, Filters, Context
menu on a result and Airports is built, committed, and checked live (by the owner in the sim or
over the dev server), with Cardinal Directions dropped. Heights, water depth and any other new
per-type detail are not part of it: each is its own entry. Population, already stored, stays in
the Details column.

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

The window works around one point, the **map position**. A check box, **Map position follows the
aircraft**, ticked by default, ties the two together: while it's ticked the map position is the
aircraft. "Set as map position" on a result moves the map position to that place and unticks the
box; ticking it again brings the map position back to the aircraft. Whether the map position is
kept between openings is then the pilot's choice: the box is remembered with the other filters.

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
   - From aircraft: distance and direction. Hidden while the map position follows the aircraft,
     since it would repeat the column above.
   - Details: what this type has. Population for towns, ICAO code and "in simulator" for
     airports. Heights and depths come later (see Later: heights).
   - Coordinates: decimal degrees (57.93333, 11.66667). Converting to a CDU's format is left to
     the pilot.
3. **Show filters and search** / **Hide filters and search**: one button that shows or hides
   everything below it. Its state is remembered.
4. **Search**: text box. Empty means everything in range. Text filters by name (folded the same
   way as `SearchText.Fold`: case and accents ignored).
5. **Scope**: combo box. "Around the map position" or "Whole world, by name".
6. **Range**: one numeric field for every category, in the GeoNames tab's distance unit, up to
   the ceiling (310 miles or 500 km). Remembered like the filters. Per-category ranges or saved
   filter sets are a later idea, only if one range turns out not to be enough.
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
9. **Map position follows the aircraft**: check box (see The map position).
10. **Reset filters** and **Refresh** (F5): buttons at the bottom.

## Refreshing

Today's window is a snapshot taken when it opens; nothing in it updates. The new window keeps that:
no automatic refresh, because a list that re-sorts while you read it moves you off your line.
F5, or the Refresh button, takes the aircraft's position again and redoes the list, keeping the
selected place selected if it's still in the list.

## Filters

Changing a filter updates the list at once. The count of results is spoken only when you press
Enter in the search box, not on every filter change (CORE-7: the screen reader already says the
new value).

The window remembers its filters, range, scope, search text, the show/hide state and the
follow-the-aircraft box between openings and between sessions (saved with the other settings).

## Context menu on a result

Applications key or Shift+F10:

- Track in slot 1 to 5. The slot holds the place's plain name, read out as tracked fixes are today.
- Set as map position
- Copy coordinates (decimal degrees)

## Airports

The Airports category stays GeoNames' airport list, with two columns filled from BA's navigation
database where the airport is matched there: its ICAO code, and whether it is in the simulator.
A match is shown as likely, not certain.

Matching, measured 2026-11-05 (GeoNames dump of that day against the MSFS 2020 navigation
database, 41,934 airports). Ground truth: GeoNames airports whose alternate names hold a 4-letter
code that is a navigation-database ident within 5 km of the GeoNames point: 10,378 airports.
(Another 313 rows list a code belonging to an airport far away, so GeoNames' codes are not
reliable on their own.)

- Distance only, nearest within 5 km: 0.58% wrong, none unmatched.
- Within 5 km and sharing a name word (generic words like "airport" and "international" ignored),
  main name only: 0.54% wrong, 7.2% unmatched.
- The same, with GeoNames' alternate names as well: 0.55% wrong, 3.4% unmatched.

Most of the remaining "wrong" ones are the same airport listed twice in the navigation database
under an old and a new ident (Kuwait OKKK and OKBK, Dourados SBDO and SSDO, Mangshi ZPMS and
ZPLX). A few are really another field close by (Brussels matched Melsbroek, Orly matched the
Orly vertiport).

Rule: if GeoNames lists a code that is a navigation-database ident within 5 km, use it. Otherwise,
the nearest airport within 5 km that shares a name word, alternate names included. Distance alone
is not used: it matches every GeoNames airfield to whatever is nearest, about 4,800 more matches
the name check turns away. Coverage measured with 5 km and the main name only (the rule above
matches a few more): 17,824 of 23,574 GeoNames airports, 6,175 of 13,466 airfields, 430 of 5,002
heliports.

This needs the alternate names (or at least the codes and names among them), which the database
drops today: a new column and a schema bump (LOC-7), the same rebuild as the feature-type names. The owner accepted the one-time rebuild (2026-11-05).

## Later: heights (not part of this work)

Worth checking, but each is its own entry after this window is done. Heights for terrain results, especially mountains, hills and islands. Two possible sources, neither
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

## Dropped

- Cardinal Directions. It shows the nearest town or village roughly north, east, south and west
  (within 45°, inside the Regional range), which the Towns and Villages filters plus the Direction
  filter cover.

## Open questions

- Readable names for feature types (ISL → "island"). Checked: the database keeps only the codes
  (`feature_code` table: id and code), and the download fetches `allCountries.zip`,
  `countryInfo.txt` and `admin1CodesASCII.txt`, not GeoNames' `featureCodes_en.txt`. Fetching that
  file with the others and storing the names is a schema bump (LOC-7), so every database is rebuilt.

## Settled

- The GeoNames tab's ranges: Nearby range (Output C, the area line, Volanta's traffic places) and
  Major Cities range (Major Cities, Volanta) stay. Regional range goes with Cardinal Directions,
  its only user. Landmarks range, used only by today's window boxes, is replaced by the window's
  own range field.
