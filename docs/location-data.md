# Offline location data (Output C and Location Info)

Output > C (the nearest place) and Output > Shift+L (the Location Info window) answer from a local
database each pilot downloads once, from Settings → GeoNames → Download location data. Nothing is
looked up online when a pilot presses a key. Rules: `.claude/rules/location-data.md` (LOC-n).

## Why it is offline

Until 2026-10 both features called the free GeoNames web service: 40 requests per press in six
back-to-back waits of up to 10 s each, about 143 of the account's 1,000 hourly credits, and a
server that, on the owner's flights, timed out its own database queries or did not answer (C
stayed silent for 60 s). The weather call had a misspelled endpoint and 404'd on every press; the
time zone was fetched and never shown; the ocean was coarse ("North Atlantic Ocean" over
Chesapeake Bay) and never reached the window. The local database answers in 5 to 25 ms, needs no
account, and adds what the web service could not give: the sea or lake you are over, the distance
and direction to the nearest land or the named sea coast, the mountain range or desert, and which
state or province you are in, decided by a real outline.

## Where the data comes from

- **GeoNames** (CC BY 4.0, credited in the GeoNames tab): the full world dump
  `https://download.geonames.org/export/dump/allCountries.zip` (about 400 MB, 13.5 million rows,
  rebuilt daily), with `countryInfo.txt` and `admin1CodesASCII.txt` for country and state names.
- **Natural Earth** (public domain), release v5.1.2 at 1:10 million scale, as GeoJSON from the
  project's GitHub tag (gzip in transit, about 20 MB; 63 MB on disk): named oceans, seas, bays and
  straits (`ne_10m_geography_marine_polys`), the sea coastline, lakes, regions (mountain ranges,
  deserts, plateaus; continents and "geoareas" are skipped) and states/provinces. Each file's
  SHA-256 is pinned in `LocationDataDownloader.NaturalEarthSha256`.

The 1:10m coastline is accurate to roughly a kilometre: right for "coast 5 miles west" in flight,
not for "the shore is 300 m away" on a coastal apron. Its river lines are not used (only the major
rivers, and they stop at their estuaries).

## What is stored

About 8.4 million GeoNames rows (`LocationFeatureCatalog`): every populated place except
historical and abandoned ones, split into Town (500+ people, or a seat of administration) and
Village; airports; terrain (mountains, hills, valleys, capes, peninsulas, islands); water (lakes,
reservoirs, bays, streams, beaches, gulfs, straits, sounds, coves, reefs); tourist landmarks; and
the undersea class, stored for later use. The finished database is about 330 MB at
`%APPDATA%\MSFSBlindAssist\databases\location-data.sqlite`
(`DatabasePathResolver.GetLocationDataPath`).

- `place` is a `WITHOUT ROWID` table keyed `(cat, cell, id)`: `LocationGrid` files every point in a
  0.25° cell (rows south to north, columns west to east) with a two-byte offset from the cell
  corner, so a radius search is one range seek per row of cells, two where the search crosses ±180°.
- `admin` maps each place's country and state codes to GeoNames' names.
- `shape` holds the Natural Earth shapes as float32 blobs, one row per polygon part and coastlines
  cut into 64-point chunks, with an R*Tree on their bounding boxes (`NaturalEarthShapes`).
- `meta` holds the schema version, the GeoNames file date, the Natural Earth release, the place
  count and `complete=1`, written last.

## Downloading and building

`LocationDataDownloader` (owned by MainForm, started from `GeoNamesPanel`):

1. Checks free disk space (the peak is about 1.5 GB: the 400 MB download, a 370 MB staging file
   and the 330 MB result; an update's old database is already on disk). Says "Downloading location
   data, about N megabytes to download."
2. Fetches the two GeoNames name files and the five Natural Earth files whole (SHA-256 checked,
   3 attempts each), then the dump into `location-data-download\allCountries.zip.part`, resuming
   with `Range` + `If-Range` from the validator in `.part.json` (LOC-4). Speaks 25, 50 and 75
   percent. A watchdog speaks a stall at 20 s without data and abandons the attempt at 60 s; up to
   5 attempts without progress are retried with backoff, and "Download resumed." follows.
3. Builds on a below-normal-priority thread (`LocationDataBuilder`, about 30 s on the owner's PC):
   rows into `.staging` in key-sorted batches, `VACUUM INTO` a packed `.building` copy, a smoke
   test, `complete=1`, rename to `.ready`, then the swap under the store's write lock (LOC-2).
4. Deletes the downloads and says "Location data ready, 8.4 million places."

Cancel keeps the partial download, so the next press resumes. A build that fails or is cancelled
keeps the whole download and its validator (`allCountries.zip.json`) for 3 days, and the next press
builds from it without downloading again. Closing the app cancels silently.
At startup `LocationDataStore.Sweep` removes half-built files and promotes a `.ready` file whose
swap an antivirus scan blocked. The GeoNames tab's status box shows the job's latest state in
place (LOC-5); with no job running it describes the installed data, a kept partial download, or
what a download will take.

## Answering

`OfflineLocationService` classifies the position lake first, then land (inside a state polygon),
then sea (inside a named marine polygon) (LOC-6):

- At sea, the distance is to the **nearest land**: the coastline searched outward up to 3,000 km
  (Point Nemo gives about 1,670 miles). Over a lake it is to the **shore**, from the lake's own
  outline (islands included): "Over Lake Michigan, shore 24 miles east".
- Over land, the distance is to the **sea coast**, only within 50 miles or 80 km, and named by the
  Natural Earth sea found by stepping 1 to 25 km past the coast point ("English Channel"); unnamed
  where that water is not in Natural Earth (San Francisco Bay, the Øresund).
- The **area** is each country's first-level division (a US state, a German state, a Swiss canton,
  a French region, England or Scotland) in GeoNames' spelling, the one C uses: over land from the
  nearest town inside the same state outline, so a town across a border never names it; over
  water from the nearest town (LOC-9). Natural Earth's `gn_a1_code` is never used: French
  department numbers collide with GeoNames' region numbers.

`LocationPhrases` composes everything spoken or shown (LOC-8):

- **Output C**, water first: "Over Chesapeake Bay, nearest land 5.2 miles west. Near Dares Beach,
  Maryland, United States, 5.4 miles west." Over land: "Near Brighton, England, United Kingdom,
  0.5 miles north, English Channel coast 1.2 miles south" (the coast clause only within 50 miles).
  No town within the nearby range: "No town within 25 miles, <area>, <country>". The periodic
  announcement uses the same sentence and stays silent when no data is installed.
- **Location Info**: a "Where you are" box first, then the seven lists. Over water: "Over
  Chesapeake Bay", "Nearest land: 5.2 miles west" ("Shore: …" over a lake), "Near: Maryland,
  United States". Over land:
  "In: Bavaria, Germany", "Region: Alps, mountain range" (when inside one), "Coast: English
  Channel, 1.2 miles south" (within 50 miles). Then "Time zone: …" (the GeoTimeZone package). The
  window opens already filled and focused on that box, and speaks nothing itself. The landmark
  lists share the tab's one Landmarks Range; the Major Cities range is capped at 310 miles or 500 km.

Numbers are invariant everywhere (LOC-3).

## The code

`MSFSBlindAssist/Services/Geography/`:

- Pure, unit-tested: `GeoNamesDumpLine`, `GeoNamesNameFiles`, `LocationFeatureCatalog`,
  `LocationGrid`, `LocationGeometry`, `NaturalEarthShapes`, `DownloadGuards` (progress gate, stall
  detector, retry budget, resume decision, disk space), `LocationPhrases`, `LocationDataStatusText`.
- I/O: `LocationDataStore`, `LocationDataBuilder`, `LocationDataDownloader`, `OfflineLocationService`.
- Callers: `MainForm.AnnounceNearestCity` (Output C and its timer), `MainForm.ShowLocationInfoDialog`,
  `Forms/LocationInfoForm.cs`, `Forms/Settings/GeoNamesPanel.cs`.

Tests in `tests/MSFSBlindAssist.Tests/Geography/` build a small fixture world
(`GeographyFixtureWorld`) with the real builder and run the whole download job against a fake
server: fresh, resumed, a dump that changed on the server, a full disk, cancel and shutdown.
