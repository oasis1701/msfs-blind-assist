# Offline location data (Output C and Location Info)

Output > C (nearest place) and Output > Shift+L (the Location Info window) answer from a local
database each pilot downloads once, from Settings → GeoNames → Download location data. Nothing
is looked up online at the moment a pilot presses a key.

## Where the data comes from

- **GeoNames** (CC BY 4.0, credited in the GeoNames tab): the full world dump
  `https://download.geonames.org/export/dump/allCountries.zip` (about 400 MB, rebuilt daily),
  with `countryInfo.txt` and `admin1CodesASCII.txt` for country and state names.
- **Natural Earth** (public domain), release v5.1.2, 1:10 million scale: named oceans, seas and
  bays, the coastline, lakes, regions (mountain ranges, deserts, plateaus) and states/provinces.

## The code

`MSFSBlindAssist/Services/Geography/`:

- `GeoNamesDumpLine` parses one row of the dump; `LocationFeatureCatalog` decides which rows are
  kept and in which `LocationCategory` (Town, Village, Airport, Terrain, Water, Landmark, Undersea).
- `LocationGrid` is the 0.25° grid that clusters the place table; `LocationGeometry` holds the
  point-in-polygon and nearest-point-on-a-line math over Natural Earth shapes.
- `DownloadGuards` holds the download's spoken-progress gate, stall detector, retry budget,
  resume decision and disk-space estimate.
- `LocationPhrases` composes everything spoken or shown: the C sentence and the "Where you are"
  box.

This page grows with the feature; the rules live in `.claude/rules/location-data.md`.
