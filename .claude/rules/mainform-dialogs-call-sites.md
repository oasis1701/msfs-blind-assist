---
paths:
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
---
# Rules whose code MainForm.Dialogs.cs and MainForm.SayIntentions.cs hold

MIRRORS: copied word for word from simconnect-data.md, surroundings.md and location-data.md, whose globs leave these partials out: `OpenTaxiForm` and the SayIntentions import hand `AircraftPosition.HeadingMagnetic` to the taxi window as it is; Dialogs claims `_augmentPrefetched` prefetches and opens Location Info. Change both together (ClaudeContextBudgetTests checks).

- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/simconnect-data.md#dck-34
- [SUR-5] `AirportWarmUp` readies online taxiway names and the surroundings catalog before the pilot asks (current airport on the ground only, Shift+D destination anywhere); name prefetches share `_augmentPrefetched`, and `PrefetchAsync` returns at once while online taxi data is off. Full: docs/invariants/surroundings.md#sur-5
- [LOC-1] Output C and Location Info answer only from the local database (`OfflineLocationService`); the one piece of network code is `LocationDataDownloader`, started only by the pilot (GeoNames tab, startup data check). Never add an online lookup at key-press time: the retired GeoNames web service cost 40 requests and up to 60 s of silence per press. Full: docs/invariants/location-data.md#loc-1
- [LOC-8] The C sentence and the "Where you are" box are composed only by `LocationPhrases` (water first over water, the owner's choice of 2026-10-10); the Location Info window speaks nothing on opening, since its focused first box is read, so never re-add a summary announcement. Full: docs/invariants/location-data.md#loc-8
