# Offline location data — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/location-data.md`, which Claude Code loads when it reads matching code. Background: [location-data.md](../location-data.md).

## LOC-1

- Output C and Location Info answer only from the local database (`OfflineLocationService`); the one piece of network code is `LocationDataDownloader`, run from the GeoNames tab. Never add an online lookup at key-press time. → [location-data.md](../location-data.md)

Why. Until 2026-10 both features called the free GeoNames web service. One press sent 40 requests in six back-to-back waits, each with a 10 s timeout; on the owner's flights of 2026-10-08 and 2026-10-09 the server answered `canceling statement due to statement timeout` or not at all, and C stayed silent for 60 s. Each press also cost about 143 of the account's 1,000 hourly credits. The local database answers in 5 to 25 ms with no account, and the data it holds never changes during a flight. The downloader is the only code that may reach the network, and only when the pilot asks for it in Settings.

## LOC-2

- Every connection to the location data uses `Pooling=False` (`LocationDataStore.ConnectionString`) and is disposed; the live file is replaced only by `LocationDataStore.TryPromote` under the store's write lock, after `LocationDataBuilder` wrote `complete=1` last. A failed or cancelled build never touches the live file. → [location-data.md](../location-data.md)

Why. SQLite on Windows opens its file without delete sharing, so a pooled connection, or a command or reader left undisposed, keeps a handle that makes the swap's rename fail (the navdata build hit the same trap). Lookups run under the read side of one `ReaderWriterLockSlim` and the swap under the write side, so a lookup never sees the file vanish. The build order is: unsorted `.staging` file, `VACUUM INTO` a packed `.building` copy, a smoke test, `complete=1` written last, rename to `.ready`, then the swap with about 10 s of retries for an antivirus scan of the new file. If the swap still fails, `LocationDataStore.Sweep` promotes the `.ready` file at the next start. This is NAV-4's rule from the navdata build, applied here.

## LOC-3

- Every number read from a download parses with the invariant culture (`GeoNamesDumpLine`, `LocationDataStore.ReadInfo`), and everything spoken or shown is invariant (`LocationPhrases`, `LocationDataStatusText`, the model `ToString`s). → [location-data.md](../location-data.md)

Why. The GeoNames dump writes "38.55179" whatever the pilot's region is; parsed with a comma-decimal culture it fails or misplaces every row. On the output side, a sentence formatted with the current culture says "5,4 miles" on a German or Swedish Windows. The tests run every spoken string and the dump parser under de-DE and sv-SE, because CI runs under en-US and would not catch a culture leak otherwise.

## LOC-4

- Resume the GeoNames dump only with the validator saved beside the `.part` file, sent as `If-Range`, and accept a 206 only as `ResumeDecision.Decide` allows. → [location-data.md](../location-data.md)

Why. `allCountries.zip` (about 400 MB) is rebuilt every day. A partial download resumed the next day with a bare `Range` header would append the tail of a different file and produce a damaged zip. The `.part.json` sidecar keeps the ETag (preferred) or Last-Modified of the first response; the resumed request carries it as `If-Range`, so a server whose file changed answers 200 with the whole new file, and the downloader restarts and says so. A 206 is accepted only when its `Content-Range` starts exactly at the partial file's length and the total matches. A partial file without a sidecar is deleted, not resumed. The build also checks the zip entry's length and CRC-32.

## LOC-5

- Download speech is milestones every 25 percent (`DownloadProgressGate`), phase changes, stalls, retries, resumed, cancelled, ready and errors, never per chunk; queued except the press confirmation, errors and ready. The status box shows the job's LATEST status, rewritten in place, per phase or whole percent. → [location-data.md](../location-data.md)

Why. The owner asked to hear what the download is doing and whether it is stuck (2026-10-10), and chose 25 percent steps. The pilot may be flying while it runs, so progress and stall notices are queued and never interrupt a callout; the press confirmation (it carries the size, which the screen reader cannot say, CORE-7), errors and "ready" are immediate. A stalled read never returns, so a watchdog speaks the stall at 20 s and abandons the attempt at 60 s; only attempts that made no progress count against the 5 retries. The GeoNames tab updates its read-only box only when the phase or the whole percent changes, rewriting just the changed text so NVDA keeps its line, and always reads the downloader's current status rather than the posted one, so a late progress post can never overwrite "Ready".

## LOC-6

- `OfflineLocationService` classifies lake first, then land (a state polygon), then sea: Natural Earth's states cover the Great Lakes. Over a lake the edge is its shore, never the sea coast; over land the coast is said only within 50 miles or 80 km (`LocationPhrases.CoastCloseOverLand`). → [location-data.md](../location-data.md)

Why. Natural Earth's US state polygons extend over the Great Lakes, so a land-first check would call a point mid-lake "land". Natural Earth's coastline is the sea coast only: over Lake Michigan its nearest point is on the Atlantic, hundreds of miles away, so a lake's own outline gives the "shore". Over land the owner chose to hear the coast only when it is within 50 miles (2026-10-10). Natural Earth's states stop at the shore of a bay, so over Chesapeake Bay no state polygon contains the point; the state then comes from the nearest place and is labelled "Nearest state". The named seas cover almost all open water: 1 point in 1,500 random ones fell in neither a sea nor a state.

## LOC-7

- Never renumber `LocationCategory` or `NaturalEarthLayer`, nor change `LocationGrid.CellDegrees` or `LocationGrid.OffsetScale`, without bumping `LocationDataStore.SchemaVersion`. → [location-data.md](../location-data.md)

Why. A built database stores the category and layer as numbers and every place as a grid cell plus an offset. Changing any of them and reading an old database silently files places under the wrong category or puts them in the wrong place. The schema version is written to the database's `meta` table; a mismatch makes `LocationDataStore.IsAvailable` false and `NeedsUpdate` true, so C says "Location data needs an update" and the GeoNames tab offers the update.

## LOC-8

- The C sentence and the "Where you are" box are composed only by `LocationPhrases` (water first over water, the owner's choice of 2026-10-10); the Location Info window speaks nothing on opening, since its focused first box is read, so never re-add a summary announcement. → [location-data.md](../location-data.md)

Why. The owner approved the wording on 2026-10-10: over water "Over Chesapeake Bay, coast 5.2 miles west. Near Dares Beach, Maryland, United States, 5.4 miles west"; over land the familiar "Near …" sentence, plus the coast within 50 miles. One composer keeps C and the window in step and lets the wording be tested. The old window announced a "Near …" summary when its data arrived; the window now opens already filled with the "Where you are" box focused, which NVDA reads, so a summary would repeat it (CORE-7).
