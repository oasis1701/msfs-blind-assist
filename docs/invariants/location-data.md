# Offline location data — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/location-data.md`, which Claude Code loads when it reads matching code. Background: [location-data.md](../location-data.md).

## LOC-1

- Output C and Location Info answer only from the local database (`OfflineLocationService`); the one piece of network code is `LocationDataDownloader`, started only by the pilot (GeoNames tab, startup data check). Never add an online lookup at key-press time. → [location-data.md](../location-data.md)

Why. Until 2026-10 both features called the free GeoNames web service. One press sent 40 requests in six back-to-back waits, each with a 10 s timeout; on the owner's flights of 2026-10-08 and 2026-10-09 the server answered `canceling statement due to statement timeout` or not at all, and C stayed silent for 60 s. Each press also cost about 143 of the account's 1,000 hourly credits. The local database answers in 5 to 25 ms with no account, and the data it holds never changes during a flight. The downloader is the only code that may reach the network, and only when the pilot asks for it in Settings.

## LOC-2

- Every connection to the location data uses `Pooling=False` (`LocationDataStore.ConnectionString`) and is disposed; the live file is replaced only by `LocationDataStore.TryPromote` under the store's write lock, after `LocationDataBuilder` wrote `complete=1` last. A failed or cancelled build never touches the live file. → [location-data.md](../location-data.md)

Why. SQLite on Windows opens its file without delete sharing, so a pooled connection, or a command or reader left undisposed, keeps a handle that makes the swap's rename fail (the navdata build hit the same trap). Lookups run under the read side of one `ReaderWriterLockSlim` and the swap under the write side, so a lookup never sees the file vanish. The build order is: unsorted `.staging` file, `VACUUM INTO` a packed `.building` copy, a smoke test, `complete=1` written last, rename to `.ready`, then the swap with about 10 s of retries for an antivirus scan of the new file. The back-off sleeps outside the write lock: held through it, a lookup on the UI thread (C, Shift+L, the GeoNames tab) froze the app and its SimConnect pump for up to 12 s. If the swap still fails, `LocationDataStore.Sweep` promotes the `.ready` file at the next start. This is NAV-4's rule from the navdata build, applied here.

## LOC-3

- Every number read from a download parses with the invariant culture (`GeoNamesDumpLine`, `LocationDataStore.ReadInfo`), and everything spoken or shown is invariant (`LocationPhrases`, `LocationDataStatusText`, the model `ToString`s). → [location-data.md](../location-data.md)

Why. The GeoNames dump writes "38.55179" whatever the pilot's region is; parsed with a comma-decimal culture it fails or misplaces every row. On the output side, a sentence formatted with the current culture says "5,4 miles" on a German or Swedish Windows. The tests run every spoken string and the dump parser under de-DE and sv-SE, because CI runs under en-US and would not catch a culture leak otherwise.

## LOC-4

- Resume the GeoNames dump only with the validator saved beside the `.part` file, sent as `If-Range`, and accept a 206 only as `ResumeDecision.Decide` allows. → [location-data.md](../location-data.md)

Why. `allCountries.zip` (about 400 MB) is rebuilt every day. A partial download resumed the next day with a bare `Range` header would append the tail of a different file and produce a damaged zip. The `.part.json` sidecar keeps the ETag (preferred) or Last-Modified of the first response; the resumed request carries it as `If-Range`, so a server whose file changed answers 200 with the whole new file, and the downloader restarts and says so. A 206 is accepted only when its `Content-Range` starts exactly at the partial file's length and the total matches. Any other 206, or a 416 that disagrees about the size, is `ResumeOutcome.DiscardPartial`: its body is a tail from the wrong offset or an error page, so none of it is written; the partial file and its sidecar go and the next request carries no `Range` (a 206 or 416 to that request is a plain error, so a misbehaving server cannot loop). Only a 200 ever becomes the whole file. A partial file without a sidecar is deleted, not resumed. The build also checks the zip entry's length and CRC-32, and a whole dump the build's own checks rejected (a damaged zip, no `allCountries.txt`, ended early, a CRC mismatch, below the row floors) is deleted with its validator: kept for reuse, it would fail the same way on every press for three days. Only those checks throw `GeoNamesDumpRejectedException`, the one failure that deletes it; a cancel, a full disk, a lost connection or any other build failure (Natural Earth, the packed copy's checks) keeps it.

## LOC-5

- Download speech is milestones every 25 percent (`DownloadProgressGate`), phase changes, stalls, retries, resumed, cancelled, ready and errors, never per chunk; queued except the press confirmation, errors and ready. The status box shows the job's LATEST status, rewritten in place, per phase or whole percent. → [location-data.md](../location-data.md)

Why. The owner asked to hear what the download is doing and whether it is stuck (2026-10-10), and chose 25 percent steps. The pilot may be flying while it runs, so progress and stall notices are queued and never interrupt a callout; the press confirmation (it carries the size, which the screen reader cannot say, CORE-7), errors and "ready" are immediate. A stalled read never returns, so a watchdog speaks the stall at 20 s and abandons the attempt at 60 s; only attempts that made no progress count against the 5 retries. The GeoNames tab updates its read-only box only when the phase or the whole percent changes, rewriting just the changed text so NVDA keeps its line, and always reads the downloader's current status rather than the posted one, so a late progress post can never overwrite "Ready".

## LOC-6

- `OfflineLocationService` classifies lake first, then land (a state polygon), then sea: Natural Earth's states cover the Great Lakes. At sea the edge is the nearest land; over a lake, its shore (its own outline, never the sea coast); over land, the sea coast within the Coast Range (`LocationQuerySettings.CoastOverLandKm`), named by its sea. → [location-data.md](../location-data.md)

Why. Natural Earth's US state polygons extend over the Great Lakes, so a land-first check would call a point mid-lake "land". Natural Earth's coastline is the sea coast only: over Lake Michigan its nearest point is on the Atlantic, hundreds of miles away, so a lake's own outline (its islands included) gives its shore. The named seas cover almost all open water: 1 point in 1,500 random ones fell in neither a sea nor a state.

The shore band. A "sea" position within 2 km of the coastline is ashore (`OfflineLocationService.ShoreBandKm`): the land sentence, its coast named by the sea shape it lies in. Natural Earth's simplified seas overlap the shore and its state outlines leave out many islands and coastal airports; measured on the real data (2026-10-11), Auckland, St Maarten, Madeira, Ponta Delgada, Diego Garcia, Funafuti and Majuro lie within 0.8 km of the coastline inside a sea shape, Hong Kong's reclaimed island 1.75 km off it, and all of them were "Over <sea>" at the gate. Baltra (Galápagos) is missing from the coastline itself and still reads as sea. The reverse, water inside neither a sea nor a state (SFO's 28L final over the bay), still gets the land sentence: telling it apart needs Natural Earth's land polygons.

The Coast Range. Over land the owner first chose to hear the coast only within 50 miles, then made it the GeoNames tab's Coast Range (2026-10-10): `UserSettings.CoastRange`, default 50, 1 to 1,000, in the tab's own units like its other ranges (so a kilometre user's default is 50 km, not the earlier fixed 80 km). The service searches the coast over land only that far, so a coast beyond the range is never found and the phrases never filter by distance themselves. The range never limits what is said over water: at sea the nearest land and over a lake its shore are always said (the owner's choice).

Wording, changed by the owner on 2026-10-10 after "Coast: 0.2 miles northeast" said neither coast of what nor to what: at sea the distance is "nearest land" (box and C), over a lake it stays "shore" (the owner's choice: a lake's edge is plainly its shore), and over land the coast is named by the Natural Earth marine shape found by stepping 1 to 25 km past the coast point, away from the aircraft ("English Channel coast 1.2 miles south", "Coast: English Channel, 1.2 miles south"). Where no named sea lies past the coast (San Francisco Bay and the Øresund are not in Natural Earth) the coast stays unnamed. Measured at 12 coastal places: English Channel (Brighton, Dover), Mediterranean Sea (Nice), North Atlantic Ocean (Lisbon), Chesapeake Bay (Annapolis), Massachusetts Bay (Boston Logan), Straits of Florida (Miami), Tasman Sea (Sydney), Adriatic Sea (Venice), Skagerrak (Oslo); unnamed at San Francisco and Copenhagen.

## LOC-7

- Never renumber `LocationCategory` or `NaturalEarthLayer`, nor change `LocationGrid.CellDegrees` or `LocationGrid.OffsetScale`, without bumping `LocationDataStore.SchemaVersion`. → [location-data.md](../location-data.md)

Why. A built database stores the category and layer as numbers and every place as a grid cell plus an offset. Changing any of them and reading an old database silently files places under the wrong category or puts them in the wrong place. The schema version is written to the database's `meta` table; a mismatch makes `LocationDataStore.IsAvailable` false and `NeedsUpdate` true, so C says "Location data needs an update" and the GeoNames tab offers the update.

## LOC-8

- The C sentence and the "Where you are" box are composed only by `LocationPhrases` (water first over water, the owner's choice of 2026-10-10); the Location Info window speaks nothing on opening, since its focused first box is read, so never re-add a summary announcement. → [location-data.md](../location-data.md)

Why. The owner approved the wording on 2026-10-10: over water "Over Chesapeake Bay, nearest land 5.2 miles west. Near Dares Beach, Maryland, United States, 5.4 miles west" (first approved with "coast 5.2 miles west"; changed to "nearest land" the same day, see LOC-6); over land the familiar "Near …" sentence, plus the named coast within the Coast Range. One composer keeps C and the window in step and lets the wording be tested. The old window announced a "Near …" summary when its data arrived; the window now opens already filled with the "Where you are" box focused, which NVDA reads, so a summary would repeat it (CORE-7).

## LOC-9

- The area line ("In:" over land, "Near:" over water) comes from GeoNames: the nearest town inside the containing state outline (`OfflineLocationService`), else the nearest town. Never use Natural Earth's `gn_a1_code`: its French department numbers collide with GeoNames' region numbers, so Paris read as "New Aquitaine". → [location-data.md](../location-data.md)

Why. Natural Earth's state layer is at a different level in each country: US states and German states, but UK boroughs ("Westminster"), French departments, Italian provinces, Irish and Norwegian counties. Its `gn_a1_code` looks like a GeoNames first-level code but is not one for every country: France's department 75 (Paris) carries "FR.75", which in GeoNames is the region Nouvelle-Aquitaine, and 13 French departments collide that way; London's polygon had no usable code and showed "Westminster" while C said "Near London, England". GeoNames' own first-level division of a town is what the C sentence already says, so the box takes it from the nearest town that lies inside the same state outline as the aircraft. A town across a border can be nearer, but it never names the area the aircraft is in. Checked on 15 European capitals and cities (2026-10-10): England, Scotland, Île-de-France, Auvergne-Rhône-Alpes, Bavaria, State of Berlin, Lazio, Madrid, North Holland, Zurich, Leinster, Oslo, Mazovia, Vienna. Only a spot with no town inside its outline within the nearby range falls back to the outline's own name. The label is "In:" over land and "Near:" over water: "State:" fitted few countries outside the US (owner, 2026-10-10).
