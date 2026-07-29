# SayIntentions Integration

MSFS Blind Assist reads the active [SayIntentions.ai](https://sayintentions.ai) flight
so a blind pilot can hear the last radio call, check their assigned gate and runway,
and turn a spoken taxi clearance into a Taxi Guidance route without transcribing it
by hand.

## Hotkeys

| Mode | Key | Action |
| --- | --- | --- |
| Output | `Ctrl+S` | Read the last SayIntentions transmission |
| Output | `Ctrl+Shift+S` | Open the flight information window (gate, runway configuration, altimeter) |
| Input | `Alt+Shift+S` | Build a taxi route from the current clearance |

The two readouts work without a simulator connection — they only read the local
flight file and the SayIntentions API. Building a taxi route needs an aircraft
position, so it requires a connected sim, and the clearance is not in the local
file, so it also requires a reachable SayIntentions API.

### Last transmission

Speaks the most recent **radio** transmission. SayIntentions mixes cabin PA and crew
intercom lines into the same message stream; those are filtered out, so pressing this
during taxi gives you the ground controller, not the purser.

### Flight information

Opens a **read-only window** rather than speaking. Arrow keys move a line at a time,
Control+Home and Control+End jump to the ends, Escape closes and hands the foreground
back to the simulator.

What it shows, sections omitted entirely when the data is absent:

| Section | Contents |
| --- | --- |
| Flight | current airport, origin, destination, aircraft type, callsign, filed route |
| Gate and runway | assigned arrival gate, whether you are parked at it, departure runway, cleared-to-land or arrival runway |
| *Airport* airport | landing runways, departing runways, preferred runway, runway flow, altimeter |

**It is deliberately short, and the rule for keeping it short is: nothing a pilot can
get by listening to the ATIS or opening the METAR window.** `departure_wx` also carries
the decoded ATIS, the METAR, the TAF, wind, visibility and density altitude, and this
window briefly showed all of it. That was wrong. Every one of those is already
available — the ATIS from SayIntentions itself, the METAR from `Shift+M` — so repeating
them here made the pilot arrow through twenty lines they had already heard to reach the
handful they had not, which is exactly the wall the window exists to remove.

What earns its place is the **runway picture**: which runways are landing, which are
departing, the preferred one, and the field's flow. That is the part worth having
cached so you do not have to sit through the ATIS a second time to recover it, and
structured it is one line instead of a sentence to pick out of prose. The altimeter
stays with it as the one number worth a keypress.

The **ATIS letter** (`current`) is parsed but not shown. It is the one field in the
block you genuinely cannot restate without having listened — but it is not runway
information, and this section is the runway information. It is a one-line change if it
should come back.

Two formatting rules exist for the screen reader rather than the eye. Runway lists are
respaced from `22L,22R` to `22L, 22R`, because without the space the reader runs the two
designators into one word. Aviation numbers are formatted invariant, so the altimeter
reads `29.73` on a machine whose locale would otherwise write `29,73`.

SI also publishes a `phonetic` variant of the ATIS ("two-two-left", "one-six-zero at
eight") for its own speech synthesis. It is deliberately **not** used: the screen
reader does its own pronunciation, and pre-spelt text reads worse through it, not
better.

`SayIntentionsAirportWeather` still parses the fields the report does not show — they
are plain scalar reads off a documented block, and they are what any future weather
work would start from.

When SayIntentions is not running there is nothing to show, and that is **spoken**
rather than shown — a window the pilot has to focus, read and dismiss to learn what one
sentence says is a worse answer than the sentence.

The departure runway is **ground information** and is dropped once airborne. It
answers "which runway am I taxiing to", and the moment the wheels leave it answers
nothing — left in, it was the last thing the readout said for the entire cruise, a
stale ground fact sitting in front of the arrival gate and arrival runway. It is also
dropped at the destination, where both fields it comes from have gone stale (see
[flight_plan_departing_runway goes stale](#observed-wire-format)).

The assigned gate is always an **arrival** gate at your filed destination — see
[The assigned gate is an arrival gate](#the-assigned-gate-is-an-arrival-gate) — so it
is announced that way from the moment you push back, not just once you get there.

Only once you are actually at the destination does the readout also compare the gate
against where you are parked: within 100 m of a stand it reports whether that stand
*is* the assigned gate, which catches a mis-set arrival position. At any other airport
that comparison is meaningless and is not made.

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
destination-resolution chain, so a gate that fails to resolve falls through to it: at
an airport that happens to have a runway with the previous leg's designator, the pilot
is sent to a runway instead of their stand. EDDF has no 05, so this capture would have
fallen one further, to the arrival runway — 07L, the one just landed on. Either way an
arriving aircraft gets routed at a runway. The cascade is blocked at the gate step now
(the full-label fix above), but the stale field is still there, and any future change
to the candidate order has to assume it is wrong.

### The assigned gate is an arrival gate

**Provenance: an SayIntentions developer, relayed 2026-07-28 — not measured.**
SayIntentions does not assign a departure gate at all. `assigned_gate` therefore always
names a stand at `flight_destination`, whatever airport the aircraft happens to be
sitting at when you read it.

The live capture could not have told us this. It was taken at EDDF, the destination,
where `current_airport` and `flight_destination` are the same string — every reading of
the field agrees there. Two things had been built on the other reading:

- **The status readout inferred the gate's role from position.** Standing at the
  origin, it announced the arrival stand as "Departure gate J1 at LMML" — the wrong
  role, and a stand attached to an airport it does not belong to, spoken as if it were
  under the aircraft's wheels. It is now always "Arrival gate ... at `<destination>`".
- **The gate appeared twice in the destination-resolution chain**, the second time as
  an unconditional fallback behind the departure runway. That is only safe if the gate
  belongs to wherever the aircraft is standing. At the departure airport it would have
  matched whatever local stand happened to share the name — and short stand names like
  `A9` recur across airports often enough that it would usually find one, select it,
  and report nothing unusual. The gate now appears once, behind a check that the
  airport being routed at *is* the destination.

The proximity comparison in the readout is gated the same way. Comparing an arrival
stand against the stands of the airport you are departing from compares two unrelated
things: it announced "not assigned gate J1" about a gate that was never meant to be
there, and could equally have announced a meaningless match.

The airport check uses the ICAO the route is actually being built for, not
`current_airport` — flight.json can omit that field, in which case the airport is
resolved from position, and keying off the empty field would refuse the gate at the
very airport it names.

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

### Second capture: KBOS, on the ground, no flight plan

**Measured 2026-07-28 — aircraft parked at KBOS, SayIntentions running, no flight plan
filed.** Deliberately the case the EDDF capture could not cover, and it settles four
things.

**`assigned_gate` is EMPTY at the departure airport** — along with `assigned_gate_lat`
and `assigned_gate_lon`, which we do not read. This is the stronger form of "SI does
not assign a departure gate": the field is not populated outbound at all, rather than
holding the arrival stand early. The arrival-gate handling is correct either way, but
the line only appears once an arrival is under way.

**`flight_plan_departing_runway` was empty while `flight_details.runway` held `22L`.**
The top-level field is the live one here, so the third fallback in the departure-runway
chain is not a rarity — on this session it was the only source.

**`departure_wx` exists and is the richest block in the file**: `atis` (decoded prose),
`current` (the ATIS letter), `active_runways_arriving`, `active_runways_departing`,
`preferred_runway`, `currently_operating`, `wind_direction`, `wind_speed`,
`wind_gusting`, `visibility`, `altimeter`, `density_altitude`, `runway_heading`,
`metar`, `taf`, `phonetic`. Nothing read any of it before. There is also an
`atis_airports` list (`KBOS,KOWD,KBED,KBVY,KLWM,KGHG,K1B9`). No `arrival_wx` appeared —
plausibly because no flight plan was filed — so it is read defensively.

**`callsign_icao` is not an ICAO callsign.** It was `Skyhawk-One-Two-Three-Alpha-Zulu`,
identical to `callsign` and already spelt out with hyphens for SI's own speech
synthesis. Anything that speaks it must strip the hyphens, which a screen reader
otherwise reads aloud.

Also present and unread: `on_ground` (as `1`/`0`, not a JSON boolean),
`aircraft_icao`, `flight_id`, the traffic-injection settings (`traffic_enabled`,
`traffic_density`, `ga_traffic`, `traffic_radius`, `max_aircraft`), and
`flight_plan_origin_hold_point_{lat,lon,heading}` /
`flight_plan_origin_runway_entry_{lat,lon,heading}` — SI's own hold-short and
runway-entry geometry. `taxi_path` was 83 × `{point, heading}`, corroborating the EDDF
geometry finding at a second airport.

**The file contains personal data.** `Email`, `displayname` and `userid` are in
`flight_details` in plain text. Nothing reads them, the debug log writes only
airport/gate/clearance-present, and no raw dump of this file may go into a log or a
committed fixture.

### What flight.json holds AIRBORNE is unknown

Every field above was read from an aircraft **stopped on the ground at the
destination**. There is no airborne capture, so nothing here says what SI writes at
1,000 ft or in the cruise. Do not design an airborne readout against this table.

The specific open questions, any of which a single mid-cruise copy of the file would
settle:

- **`current_airport` in flight** — the last airport, the nearest one, or empty? The
  status readout opens with it, so if it holds a departure airport for four hours it
  is telling the pilot something false about where they are.
- **`assigned_gate` before arrival** — is it published from the start of the flight, or
  only once approach assigns it? It is the most useful airborne field there is if the
  former.
- **`runway` in flight** — departure, expected arrival, or last-used?
- **`flight_plan_route` and `callsign_icao`** — both are already parsed into
  `SayIntentionsFlightContext` and **never spoken**. Neither appears in the captured
  table, so whether SI populates them at all is unverified. They are the obvious
  candidates for an en-route readout *if* a capture shows them present.
- **The five clearance fields the reader accepts** — `cleared_for_takeoff`,
  `cleared_for_landing`, `clearance`, `last_clearance`, `taxi_clearance` — were all
  **absent** from the capture. They came from the first version of this integration and
  have never been observed to exist. Two of them (`cleared_for_takeoff`,
  `cleared_for_landing`) sit in the destination-resolution chain and the status
  readout; treat any behaviour that depends on them as untested against real SI.

`getCommsHistory` is unaffected by all of this and works the same airborne as on the
ground — en route it returns centre and approach rather than ground, and the last-
transmission hotkey needs no changes to be useful in the air.

To take a capture: SayIntentions rewrites `%LOCALAPPDATA%\SayIntentionsAI\flight.json`
continuously, so copying it mid-cruise is enough. No tooling is needed.

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
