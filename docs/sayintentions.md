# SayIntentions Integration

MSFS Blind Assist reads the active [SayIntentions.ai](https://sayintentions.ai) flight
so a blind pilot can hear the last radio call, check their assigned gate and runway,
and turn a spoken taxi clearance into a Taxi Guidance route without transcribing it
by hand.

## Hotkeys

| Mode | Key | Action |
| --- | --- | --- |
| Output | `Ctrl+S` | Read the last SayIntentions transmission |
| Output | `Ctrl+Shift+S` | Read the assigned gate and runway |
| Input | `Alt+Shift+S` | Build a taxi route from the current clearance |

The two readouts work without a simulator connection — they only read the local
flight file and the SayIntentions API. Building a taxi route needs an aircraft
position, so it requires a connected sim, and the clearance is not in the local
file, so it also requires a reachable SayIntentions API.

### Last transmission

Speaks the most recent **radio** transmission. SayIntentions mixes cabin PA and crew
intercom lines into the same message stream; those are filtered out, so pressing this
during taxi gives you the ground controller, not the purser.

### Assigned gate and runway

Speaks the current airport, the assigned gate (labelled departure or arrival based on
where you are in the flight), the departure runway, and any landing clearance. When
the aircraft is within 100 m of a parking spot it also reports whether that spot *is*
the assigned gate — useful for catching a mis-set starting position.

### Build taxi route

Reads the current taxi clearance, resolves the destination and taxiways against the
airport's real taxi network, and fills in the Taxi Guidance dialog.

**This one needs the SayIntentions API to be reachable.** The local flight file does
not carry the clearance text, so every press fetches the last transmissions over the
network. With SayIntentions offline, the key rejected, or the request timing out (five
seconds), you hear why and no route is built — the two readouts above are unaffected.

By default the dialog opens with everything pre-filled so you can review it, then you
press **Calculate Route** to start guidance. Enable **Start taxi guidance immediately**
in Settings → SayIntentions to skip the review step.

An import replaces the **whole** route, including anything you had set up by hand
first — intersection departure, CAT III hold, hold-shorts. The clearance is the route.

#### What the summary tells you

It names the destination and the taxiways that were applied, then everything that did
not survive:

- **"Could not apply K."** — a taxiway the clearance named that the route does not use,
  either because this airport does not have it or because the dialog could not seat it.
  The route is still built from whatever did match.
- **"Hold short of runway 15R after N."** — a hold-short from the clearance that was
  set, on the taxiway it follows. One line per hold-short, in clearance order.
- **"Could not set hold short of runway 22."** — a hold-short that reached no row.
  Treat it as still in force: guidance will not stop you there.
- **"Destination not set. Check the destination field."** — the dialog is open but you
  have to pick the destination yourself.

Nothing that came out of the clearance is dropped in silence. A route shorter than the
one you were cleared for is not something you can see, so it is always said out loud.

## Settings

**Settings → SayIntentions** holds two options:

- **API key** — optional. Leave it blank and the integration uses the key
  SayIntentions publishes in `flight.json` during an active flight. Set it explicitly
  if you want comms history and parking lookups to work in other situations.
- **Start taxi guidance immediately** — off by default (see above).

## Troubleshooting

Diagnostics are written to `%APPDATA%\MSFSBlindAssist\logs\sayintentions.log`. It
records which fields were found in `flight.json`, and for every route import one line
holding the destination, the taxiways **applied**, the taxiways **skipped** (the airport
has them, the dialog could not seat them), the taxiways **not at this airport**, the
**hold-shorts** that were set and the ones that were **missed**. If the spoken summary
and the dialog ever disagree, that line is the record of what the import actually did.
API keys are never written to the log.

"SayIntentions flight.json not found" means no flight is active — SayIntentions writes
`%LOCALAPPDATA%\SayIntentionsAI\flight.json` only while connected to a flight.

---

## Developer internals

### Layout

Pure logic lives in `MSFSBlindAssist/Services/SayIntentions/` and is unit-tested:

| File | Responsibility |
| --- | --- |
| `SayIntentionsClearanceParser.cs` | All regex. Runway/gate/taxiway extraction from ATC speech. |
| `SayIntentionsTransmissionClassifier.cs` | Radio vs cabin/PA classification. |
| `SayIntentionsEndpoint.cs` | SAPI URL construction, host allowlist, log redaction. |
| `SayIntentionsService.cs` | I/O only — `flight.json` reads and SAPI requests. |
| `SayIntentionsModels.cs` | Context/transmission/parking/result types. |

UI wiring is `MainForm.SayIntentions.cs`; settings are a `SayIntentionsPanel` tab in
the unified `SettingsForm`.

### Observed wire format

**Provenance: measured from ONE live session on 2026-07-28 — an LMML → EDDF arrival,
aircraft on the ground at EDDF taxiing to Terminal 3 Gate J1 — not from SayIntentions
documentation.** One session, one airport, one aircraft, one flight phase. Treat it as
what the wire really carried that day, not as a specification: a later capture that
contradicts anything here should win. It is still worth more than the schema the first
version of this integration was written against, every wrong assumption in which was
caught by this one capture.

**Direction is from SayIntentions' point of view, not the pilot's.** `incoming_message`
is what SI *received* — the PILOT speaking. `outgoing_message` is what SI *sent* — ATC.
The intuitive reading is exactly backwards. Every turn pair in the capture reads
incoming "Request taxi" / outgoing "Taxi to Terminal 3 Gate J1 via …", and across 89
records `outgoing_message` carried 20 ATC-phrase hits and zero pilot-phrase hits. Read
the intuitive way, Ctrl+S announces the pilot's own readback as the controller — and
"prefer the ATC call within a record" systematically prefers the pilot.

**`assigned_gate` is the full label, not a stand id.** EDDF gave
`"Terminal 3 Gate J1"`. Navdata names that spot `J1`, so the two only meet through
`NormalizeParkingName` — see [Gate names](#gate-names) for why the stand id is
whatever follows the *last* gate/stand keyword.

**`current_flight.taxi_path` is GEOMETRY, not taxiway names.** ~200 objects shaped
`{"heading": 93.92, "point": {"lon": 8.52, "lat": 50.04}}` — no `taxiway`, `name`,
`label` or `id` member anywhere in it. Nothing reads it; see
[Why taxi_path is not parsed](#why-taxi_path-is-not-parsed).

**flight.json carries no clearance text and no comms.** None of `cleared_for_takeoff`,
`cleared_for_landing`, `clearance`, `last_clearance` or `taxi_clearance` were present
in `flight_details`, there was no comms array, and the string `incoming_message` did
not appear in the file at all. So `ClearanceText` from flight.json is always null in
practice and the taxi import always depends on a live `getCommsHistory` round-trip, on
the five-second `ApiTimeoutSeconds` critical path. The API key itself IS in the file
(`flight_details.api_key`) — that part of the design holds; it is the clearance that
is missing.

**`flight_plan_departing_runway` goes stale, and it is load-bearing.** At EDDF, after
landing, it still read `"5"` — left over from the LMML departure. It sits in the
destination-resolution chain ahead of the second gate attempt, so a gate that fails to
resolve falls through to it: at an airport that happens to have a runway with the
previous leg's designator, the pilot is sent to a runway instead of their stand. EDDF
has no 05, so this capture would have fallen one further, to the arrival runway — 07L,
the one just landed on. Either way an arriving aircraft gets routed at a runway. The
cascade is blocked at the gate step now (the full-label fix above), but the stale field
is still there, and any future change to the candidate order has to assume it is wrong.

The field values as captured:

| Field | Value | Note |
| --- | --- | --- |
| `flight_details.hostname` | `https://apipri.sayintentions.ai` | matches the documented default |
| `flight_details.api_key` | *(present)* | never logged, never committed |
| `flight_details.current_airport` | `EDDF` | |
| `flight_details.runway` | `7L` | |
| `current_flight.assigned_gate` | `Terminal 3 Gate J1` | full label |
| `current_flight.flight_plan_departing_runway` | `5` | **stale** — the LMML leg |
| `current_flight.flight_plan_arriving_runway` | `7L` | |
| `current_flight.taxi_path` | ~200 × `{heading, point}` | geometry |

`SayIntentionsLiveClearanceTests` pins the captured clearance verbatim;
`SayIntentionsLiveFlightJsonTests` pins the file shape and these field values.

### Hold-short masking (safety-critical)

A taxi clearance to a **gate** routinely ends "hold short of runway NN", and a
clearance to a **runway** routinely contains a crossing. Extracting the destination
with a leftmost `Regex.Match` for "runway NN" therefore made the *hold-short* runway
the destination — routing a blind pilot at an active runway they had just been told
to stop before.

`ParseDestinationRunway` runs against a copy of the clearance with every hold-short
and crossing span replaced by spaces (`MaskHoldShortAndCrossings`, length-preserving).
The two extractions can no longer collide. `ParseHoldShortRunway` reads the original
text.

**Every phrasing of the hold is masked**, not just the exact "hold short of": *holding
short*, *hold-short*, *hold short of the*, *remain short of*, and the ICAO *holding
point*. The mask and the capture share ONE `HoldPrefix` constant deliberately — the
first version spelled the two separately, handled `CROSS(ING)` but only bare "hold
short", so a pilot readback ("holding short of runway 15", which is exactly what
SayIntentions publishes as the newest transmission) still made 15 the taxi destination.
Two regexes for one concept will drift; keep them as one const.

The same masking is why the taxiway scan does **not** truncate at `cross`/`then`: a
clearance legitimately continues, and reuses taxiways, across a runway crossing (the
KBOS pattern in [taxi-guidance.md](taxi-guidance.md)). It stops only at a genuine
terminator — `contact`, `monitor`, `squawk`, `remain`, `report`, `give way`, `follow`,
`information`. `information` is there because the ATIS letter is spoken phonetically
("advise you have information Sierra"): read as route text it silently appends a real
taxiway S to the clearance, or claims the airport is missing one.

### Taxiway matching case asymmetry

`BuildTaxiwayPattern` emits `(?:A|(?i:ALPHA))` per character: the literal designator
matches **case-sensitively** (uppercase only) while the NATO word does not. That
asymmetry is the only thing stopping the English article "a" being read as taxiway A,
and the preposition "at" as taxiway AT. Callers must never pass
`RegexOptions.IgnoreCase` to this pattern.

Overlapping candidates resolve longest-first, so "Alpha-Tango" reads as `AT` rather
than `A` followed by `T`.

**Digits carry spoken forms too**, exactly like letters. Without them "Bravo Four"
decayed to taxiway B — a real taxiway at most airports, so the wrong route was delivered
with full confidence and never reported as missing. Affects every airport with
alphanumeric taxiways (KJFK, EGLL…).

### Reporting what did not survive

Three things can go missing between the clearance and the route. All three are spoken.

| Lost | Detected by | Reported as |
| --- | --- | --- |
| A taxiway this airport does not have | `ScanTaxiways` → `Unresolved` | `Could not apply …` |
| A taxiway the dialog could not seat | `ApplyExternalRoute` → `SkippedTaxiways` | `Could not apply …` |
| A hold-short that reached no row | `ApplyExternalRoute` → `SkippedHoldShortRunways` | `Could not set hold short of runway …` |

The first row could not exist before: `ParseTaxiways` returns only names the graph
knows, so a taxiway the airport lacked evaporated between the clearance and the
announcement. `ScanTaxiways` returns `(Resolved, Unresolved)` and `ParseTaxiways` is now
a thin wrapper over its `Resolved` half — the old signature has callers and tests, and
keeps working. The two "could not apply" sources share one spoken line: the pilot needs
the same thing from both, the name of the leg the route is not taking.

**Unknown-taxiway detection is PHONETIC-ONLY, deliberately.** A token counts as missing
when it is a whole NATO word, optionally with a digit ("Kilo", "Bravo Four"), that
overlaps none of the names that did resolve. Bare designators are **not** scanned:
matching uppercase letters in prose false-positives on ordinary abbreviations, and a
wrong "could not apply K" teaches the pilot to distrust the whole announcement. A miss is
the better failure here, so a clearance written with bare designators can still lose one
quietly. There is no structured second source to catch it: `taxi_path` is geometry.

Two guards keep the report quiet when it should be, and both are load-bearing:

- **A phonetic word overlapping a resolved name is skipped.** Both words of
  "Alpha-Tango" sit inside the `AT` that already matched, and an airport can have AT
  without having A or T — without this, a perfectly resolved route reports two missing
  taxiways.
- **A token whose designator IS a known taxiway is skipped.** `BuildTaxiwayPattern` has
  no phonetic branch for a name containing a space, so "Bravo Four" cannot match a graph
  that spells it `B 4`. That is a matching gap, not a missing taxiway.

### Hold-shorts belong to their own taxiway

A clearance carries several ("via Alpha, hold short of 15, Bravo, hold short of 04,
Charlie") and each belongs to the taxiway it **follows**. Pinning them all to the last
taxiway of the clearance put the stop at the wrong crossing, and only the first survived
at all.

`ParseClearanceTaxiPlan` cuts the clearance at the spans the parser masks — hold-shorts
AND crossings — and resolves each piece on its own, so where each hold-short falls in
the sequence survives. Cutting on the parser's own mask is what keeps a second copy of
the hold-short phrasing out of `MainForm.SayIntentions.cs`; two copies would drift.

A taxiway repeated across a hold-short is **kept** (the KBOS "N, hold short 15R, N"
pattern): the form carries one hold-short per row, so collapsing the repeat throws the
second one away. A repeat across a plain crossing still collapses.

`MapHoldShortsToTaxiways` then turns each hold-short's taxiway NAME into a position in
the sequence being applied. A name that sequence does not carry maps to `-1` and gets
reported, never hung on whatever row happens to be last — the case a clearance produces
by naming a hold-short before it names any taxiway.

### Why `taxi_path` is not parsed

The reader that turned `current_flight.taxi_path` into a taxiway sequence, and the
`MatchKnownTaxiways` branch that preferred that sequence over the spoken clearance, are
**deleted**. They were written against a guessed schema, and the live capture shows the
field is geometry: no name to read, so the branch had never once run against real
SayIntentions traffic.

Deleting it rather than leaving it dormant is deliberate, and the reason is not tidiness.
The reader accepted an object's `taxiway`, `name`, `label` **or `id`** member — and `id`
is precisely what a geometry array is most likely to grow. Had that happened, ~200 point
ids would have become "taxiway names", the branch would have activated on its own, the
route would have silently stopped coming from the clearance, and the pilot would have
heard a shortest-path route plus "Could not apply" followed by two hundred numbers. A
dormant path that arms itself on someone else's schema change, with no announcement to
reveal the switch, is worse than no path.

If SayIntentions ever does publish taxiway names, this is perfectly good work to redo —
from a capture that shows them, not from a guess. `MapHoldShortsToTaxiways` already
tolerates an applied sequence that differs from the spoken one, so the hold-short side
of it still holds.

### Gate names

`ParseDestinationGate`'s capture admits a **hyphen** as well as a space, so "gate A-9"
reaches stand A9. Normalizing `A-9` → `A9` afterwards was not enough while the capture
itself stopped at the bare letter: that routed the pilot to stand "A" — or, with no such
stand, fell through to the departure RUNWAY as the destination.

`NormalizeParkingName` strips a descriptor tail only when the dash is **spaced**
("A9 - Terminal 1"). A bare hyphen is part of the stand name.

### An import owns the whole route

`ApplyExternalRoute` calls `ResetRouteShapingControls` first. `OnDestTypeChanged` only
clears the runway-only boxes when the destination TYPE changes, so a runway route
imported over a hand-built runway route otherwise keeps the old intersection departure
and CAT III hold — a different lineup point, with nothing in the announcement to reveal
it. `chkFitFilter` is deliberately exempt: it describes the aircraft's wingspan rather
than the route, and forcing it either way could hide the very gate the clearance names.

### One graph build per keypress

`MainForm` never builds a `TaxiGraph`. `TaxiAssistForm.LoadAirportForExternalRouteAsync`
loads the airport once and returns the taxiway names its graph knows; the clearance is
resolved against that list, and destinations resolve through
`TaxiAssistForm.TryResolveExternalDestination`, which searches the already-populated
destination combo. The form owns its own label formats — callers pass a normalized
identifier (`"15L"`, `"A9"`), never a constructed `"Runway 15L"` string.

### API key handling

The SAPI hostname comes from `flight.json`, a file this app does not own.
`SayIntentionsEndpoint.Build` requires **https on `sayintentions.ai`** before attaching
the key and silently falls back to the documented default host otherwise, so a
tampered or corrupt `flight.json` cannot redirect the credential. Request URLs go
through `SayIntentionsEndpoint.Redact` before reaching the log.

The key remains a query parameter because that is how SAPI documents its auth. Moving
it to a header is a possible follow-up but cannot be verified without live
credentials.

### Request coalescing

Comms history and parking are cached (5 s / 10 s). A request that arrives while one is
already in flight **joins** it rather than starting a second — and the cache commits
in a `finally`, after completion. Stamping the cache time before awaiting made a second
hotkey press during a slow request hit a populated-but-empty cache and speak "no
transmission available", which is exactly when a pilot presses again because they
heard nothing.
