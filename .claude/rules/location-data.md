---
paths:
  - "MSFSBlindAssist/Services/Geography/**"
  - "MSFSBlindAssist/Models/LocationData.cs"
  - "MSFSBlindAssist/Forms/LocationInfoForm.cs"
  - "MSFSBlindAssist/Forms/Settings/GeoNamesPanel.cs"
  - "tests/MSFSBlindAssist.Tests/Geography/**"
---
# Offline location data rules

Loaded when Claude reads matching code. Background: docs/location-data.md. Full text of each rule: docs/invariants/location-data.md.

- [LOC-1] Output C and Location Info answer only from the local database (`OfflineLocationService`); the one piece of network code is `LocationDataDownloader`, run from the GeoNames tab. Never add an online lookup at key-press time: the retired GeoNames web service cost 40 requests and up to 60 s of silence per press. Full: docs/invariants/location-data.md#loc-1
- [LOC-2] Every connection to the location data uses `Pooling=False` (`LocationDataStore.ConnectionString`) and is disposed; the live file is replaced only by `LocationDataStore.TryPromote` under the store's write lock, after `LocationDataBuilder` wrote `complete=1` last. A failed or cancelled build never touches the live file. Full: docs/invariants/location-data.md#loc-2
- [LOC-3] Every number read from a download parses with the invariant culture (`GeoNamesDumpLine`, `LocationDataStore.ReadInfo`), and everything spoken or shown is invariant (`LocationPhrases`, `LocationDataStatusText`, the model `ToString`s): a German Windows still hears "5.4 miles". Full: docs/invariants/location-data.md#loc-3
- [LOC-4] Resume the GeoNames dump only with the validator saved beside the `.part` file, sent as `If-Range`, and accept a 206 only as `ResumeDecision.Decide` allows: GeoNames rebuilds the dump daily, so a resume without it splices two different files. Full: docs/invariants/location-data.md#loc-4
- [LOC-5] Download speech is milestones every 25 percent (`DownloadProgressGate`), phase changes, stalls, retries, resumed, cancelled, ready and errors, never per chunk; queued except the press confirmation, errors and ready. The status box shows the job's LATEST status, rewritten in place, per phase or whole percent. Full: docs/invariants/location-data.md#loc-5
- [LOC-6] `OfflineLocationService` classifies lake first, then land (a state polygon), then sea: Natural Earth's states cover the Great Lakes. At sea the edge is the nearest land; over a lake, its shore (its own outline, never the sea coast); over land, the sea coast within 50 miles or 80 km (`LocationPhrases.CoastCloseOverLand`), named by its sea. Full: docs/invariants/location-data.md#loc-6
- [LOC-7] Never renumber `LocationCategory` or `NaturalEarthLayer`, nor change `LocationGrid.CellDegrees` or `LocationGrid.OffsetScale`, without bumping `LocationDataStore.SchemaVersion`: a built database encodes them, and the bump turns a stale one into "update needed" instead of wrong answers. Full: docs/invariants/location-data.md#loc-7
- [LOC-8] The C sentence and the "Where you are" box are composed only by `LocationPhrases` (water first over water, the owner's choice of 2026-10-10); the Location Info window speaks nothing on opening, since its focused first box is read, so never re-add a summary announcement. Full: docs/invariants/location-data.md#loc-8
- [LOC-9] The area line ("In:" over land, "Near:" over water) comes from GeoNames: the nearest town inside the containing state outline (`OfflineLocationService`), else the nearest town. Never use Natural Earth's `gn_a1_code`: its French department numbers collide with GeoNames' region numbers, so Paris read as "New Aquitaine". Full: docs/invariants/location-data.md#loc-9
