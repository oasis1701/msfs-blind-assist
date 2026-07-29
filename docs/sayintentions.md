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

Speaks the most recent **ATC** transmission. Two things are filtered out, for different
reasons. SayIntentions mixes cabin PA and crew intercom lines into the same message
stream, so pressing this during taxi gives you the ground controller, not the purser.

And **your own transmissions are never returned**. A readback is normally the newest
thing on the frequency at exactly the moment you press the key, so ordering by timestamp
announced the pilot their own words back, prefixed "Pilot:". Preferring the ATC call only
*within* one record — as far as the first fix went — did not help, because the readback
arrives in a later record than the clearance it repeats. A `Pilot`-speaker transmission
is now dropped outright.

A transmission with **no direction at all** still counts. It comes from the bare
`message` fallback, which carries nothing identifying it as the pilot, so excluding it
would be a guess. The failure modes are not symmetric: dropping it leaves a payload shape
we cannot classify silent, and for a readout whose whole job is to say what was heard,
silence is the worse failure — while including it risks at worst an unlabelled line,
which with no speaker is prefixed with nothing and so can never be mistaken for you.

When the history holds nothing but your own calls you hear *"No ATC transmission yet.
Only your own calls so far."* That is a different answer from nothing found: you did hear
traffic, none of it from the controller, and saying so stops you pressing again for a call
that has not come.

The taxi import inherits the filter for free. A clearance can now only ever come from the
controller, never from your readback of one — which is exactly the transmission the
hold-short masking exists to survive.

### Flight information

Opens a **read-only window** rather than speaking. Each section of the report is its own
list: Tab moves between sections, the arrow keys move within one, typing a letter jumps to
the next item starting with it, and Escape closes and hands the foreground back to the
simulator. Focus lands on the first section with its first item selected, so tabbing in
announces the section, its leading value and how many items it holds in one utterance.

Lists rather than a box of text, for two reasons. The window is a **lookup surface** — you
open it to find one value, so the structure has to be something you can jump around rather
than a run you arrow through from the top. And a list item is a discrete object, so it
**brailles as one unit** and the reader announces its position ("3 of 7"); a multi-line
text box can only braille the caret line, and its line boundaries are a rendering
artefact. It is the same reasoning that put the A32NX DCDU in a ListBox, and the same
`DisplayListBox` the Weather Radar window uses, so the reading behaviour carries over.

A section with no data is **omitted entirely** — no heading with nothing under it, and no
empty list to tab into and find nothing in.

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

**The airport you are AT is reported first.** The two blocks used to go out
departure-then-arrival unconditionally, so an arrival opened this window on the field
the aircraft had left: the live LMML → EDDF capture, on the ground at EDDF, led with
LMML's runway picture and LMML's altimeter — 1300 nm behind the aircraft, and 0.12 inHg
from the setting about to be used, which is roughly 120 ft. The departure block now
leads only when it names `current_airport` **and** the arrival block does not.
Everything else — airborne, `current_airport` empty, parked at neither field — leads
with the **arrival**: a destination is what you plan for, and the field you left is not.
A block carrying no airport name matches nothing (not even a blank `current_airport`)
and keeps its role, `Departure`/`Arrival`, as its heading. When both blocks name the
same field — a circuit, a return-to-field — it is printed **once**, from the arrival
block, and a block with nothing under it never claims the heading away from the one
that has the data.

The **ATIS letter** (`current`) is parsed but not shown. It is the one field in the
block you genuinely cannot restate without having listened — but it is not runway
information, and this section is the runway information. It is a one-line change if it
should come back.

Two formatting rules exist for the screen reader rather than the eye. Runway lists are
respaced from `22L,22R` to `22L, 22R`, because without the space the reader runs the two
designators into one word. Aviation numbers are formatted invariant, so the altimeter
reads `29.73` on a machine whose locale would otherwise write `29,73` — a comma there
makes a screen reader say a different number, not an obvious typo.

**The altimeter is given in both units**, `Altimeter: 30.12 inches (1020 hPa)`.
SayIntentions publishes it numerically in inHg only and half the world flies the hPa
number, so both are printed and neither pilot converts in their head off a spoken line.
The conversion is checked against the airports themselves rather than taken on trust:
the live capture read 30 at LMML and 30.12 at EDDF, and 30 × 33.86389 = 1016,
30.12 × 33.86389 = 1020 — exactly the Q1016 and QNH 1020 those two fields were passing
at the time. inHg is fixed at two decimals: whole values used to drop theirs, so one
window read `Altimeter: 30 inches` a few lines above `Altimeter: 30.12 inches`. It says
"inches", not "inHg", because the line is read aloud.

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

There is no SayIntentions settings tab. The one option lives on **Settings → Taxi
Guidance**, under a SayIntentions heading at the foot of that tab:

- **Start taxi guidance immediately** — off by default (see above).

It sits there because it decides what happens to a *taxi route*, which is that tab's
subject, and because it was the only option left once the API key was retired.

There is no API key field. SayIntentions publishes the key in `flight.json`
(`flight_details.api_key`) whenever a flight is active, confirmed in both live captures,
so a hand-entered copy could only duplicate it — or go stale and quietly override it with
something wrong. Removing the setting also retired the last error string that sent a pilot
looking for it: when there is no key and nothing in the file, the honest reason is that
SayIntentions is not running, and that is what is now spoken.

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
`label` or `id` member anywhere in it. `point.lat`/`point.lon` ARE read, into
`TaxiPathPoints`; no name-ish member ever is — see
[Reading `taxi_path`: coordinates only, never names](#reading-taxi_path-coordinates-only-never-names).
Each snapshot's own generation time comes from the sibling `flight_details.timestamp` —
a raw Unix epoch in **seconds**, fractional (e.g. `1785357161.40969`), not a date
string — covered in the same section.

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

### Clearance delivery is not a taxi clearance

`LooksLikeTaxiClearance` gates the "fall back to the last radio transmission" path. It
accepts **taxi** or a bare **via**, because an abbreviated clearance can omit the verb
("Runway 15L via Bravo, Charlie") — so the exclusion list is what carries the weight.

A live KBOS capture, 2026-07-29, is why there is one beyond the original landing
clearance:

```
Cleared to Miami via the SSOXS7 departure. Then as filed. Climb and maintain 5,000.
Expect FL360 one-zero minutes after departure. Departure on 133.0. Squawk 6422.
```

That passed on the strength of its "via". Imported, it matched no taxiways, fell back to
shortest path to the departure runway, and announced itself as a SayIntentions route —
with nothing to tell the pilot it had not come from a taxi clearance at all. The pilot's
**readback** is published as a transmission too, and is the newest thing on the frequency
at exactly the moment someone might press the import key.

Excluded on `cleared to land`, `climb and maintain`, `squawk NNNN` and `as filed`. Each
belongs to clearance delivery and to nothing a ground controller says while taxiing you,
so excluding on them costs no real taxi clearance.

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

**A single letter can arrive as its compass word.** Palma Ground, live: *"Taxi to holding
point runway 24R via LE, E, North, H2."* LEPA's navdata calls that taxiway `N` — and
SayIntentions rendered the bare letter as the plain English word, not the NATO
"November". It cost the route a leg twice over: the pattern stopped at the trailing
"orth", and the phonetic-only unresolved scan had no branch for it either, so the pilot
heard a three-taxiway route with nothing to say a leg had gone missing. The taxi router
caught it downstream — *"No intersection between 'E' and 'H2'"* — which is not the
import's job.

`NORTH`/`SOUTH`/`EAST`/`WEST`/`CENTER`/`CENTRE` are therefore spoken forms of N/S/E/W/C,
merged into the same table `ALPHA` comes from by `SpokenForms`, so the match and the
report pick them up from one place and cannot diverge. They compose with everything else
unchanged: longest-match-first still prefers a hypothetical `NE` over `N`, and the digit
words still bind — "North One" → `N1`, and at an airport with `N` but no `N2`, "North Two"
is reported as `N2` rather than quietly resolved to `N`.

#### The one thing a compass word costs

Nobody writes "alpha" in prose. People write "north" constantly, and it can sit after
`via`: *"taxi north on Bravo"*, *"to the north side"*, *"to runway 24 Center"*. Both
failure modes are real and they are mirror images — where the airport HAS the letter,
prose silently adds a leg ATC never cleared; where it does not, prose is announced as
"could not apply North", and a false report teaches the pilot to distrust the whole
announcement. `IsDirectionProse` is the price, and it is applied to BOTH scans from one
helper, or the announcement contradicts itself from one airport to the next.

A compass word is a direction rather than a taxiway when:

- **a direction phrase leads into it** — `the` ("to the north end"; a taxiway is never
  given an article, ATC says "via Alpha" and never "via the Alpha"), or a runway number
  ("to runway 24 Center" — hold-short and crossing runways are already blanked by the
  mask, a destination runway named after `via` is not); or
- **the very next word is English** rather than the next designator in the list. A comma,
  a full stop, the end of the route, `and`/`then`, or another taxiway all leave it a
  taxiway. "Immediately" means within three separators, so a blanked-out hold-short span
  reads as "nothing follows" — which is what it is — instead of reaching across it for the
  first word on the far side and dropping the last taxiway of the clearance.

That the lowercase prose after a direction can be tested against the designator list at
all is the case asymmetry paying off again: "north apron" cannot see taxiway `A` in
"apron", because the literal branch is uppercase-only.

**Capitalization is deliberately not the signal.** SayIntentions' text is generated, and
"North" being capitalized in one live clearance is not a contract.

**Known residual:** *"proceed north then LE"* still reads `north` as taxiway `N`, because
`then` is exactly what joins two taxiways in a list ("LE, North and H2") and the guard
cannot tell the two apart. It is ambiguous to a human reader too, no live capture contains
prose after `via` at all, and the router's own sanity check catches the resulting route.
Closing it needs a whitelist of what may PRECEDE a compass word, whose failure mode is the
silent dropped leg this change exists to remove.

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

The word list has since gained the five compass words, which ARE ordinary English — the
one widening this rule ever took, and bounded the same way: a closed list of whole words,
no bare designators. What English costs is paid by `IsDirectionProse`, not by loosening
the pattern.

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

### Reading `taxi_path`: coordinates only, never names

`SayIntentionsService.ReadTaxiPathPoints` reads `current_flight.taxi_path` into
`SayIntentionsFlightContext.TaxiPathPoints` — but ONLY `point.lat`/`point.lon` from
each entry. No `taxiway`, `name`, `label` or `id` member is ever read. An entry missing
either coordinate is skipped outright rather than defaulted to `(0, 0)`, which would
snap to nothing useful at best and to some other airport's pavement at worst
(`MalformedTaxiPathEntriesAreSkippedNotZeroed` pins the skip;
`TheTaxiPathIsReadAsCoordinatesOnly` pins the coordinates themselves).

That boundary is narrower than it looks, and it is deliberate. An earlier version of
this integration had a reader that turned `taxi_path` into a taxiway sequence by
reading an object's `taxiway`, `name`, `label` **or `id`** member, plus a
`MatchKnownTaxiways` branch that preferred that sequence over the spoken clearance.
Both were deleted in 2026-07 rather than left dormant: they had been written against a
guessed schema, the live capture showed the field is geometry with no name anywhere in
it, and `id` — one of the four members the old reader accepted — is precisely what a
geometry array is most likely to grow next. Had SayIntentions added one, ~200 point ids
would have become "taxiway names" on their own, the dormant branch would have armed
itself, the route would have silently stopped coming from the clearance, and the pilot
would have heard a shortest-path route plus "Could not apply" followed by two hundred
numbers — with nothing in the announcement to reveal that the switch had even happened.
So the boundary is enforced at the reader itself, coordinates in and nothing else, no
matter what a future capture appears to add: see the doc comment on
`ReadTaxiPathPoints` and the CLAUDE.md invariant under "SayIntentions integration" for
the same rule stated at the code site.

Turning coordinates into a route is a separate, already-built concern:
`SayIntentionsTaxiPathSnapper.Snap` snaps each point to the nearest edge of the
airport's own named taxiway graph — never to anything SI publishes as a name. A live
LSZH arrival snapped `taxi_path` to exactly the taxiways ("E4, E, C") Zurich Ground had
just cleared. Nothing wires that sequence into route building yet, and when something
does it must only ever OVERRIDE the clearance-derived route, never author one SI never
cleared: the geometry is SayIntentions' own rendering of *a* plan, which need not be
the plan the controller most recently spoke.

That is what `flight_details.timestamp` is for.
`SayIntentionsService.ReadTaxiPathStampUtc` reads it into `TaxiPathStampUtc` as this
snapshot's generation time, in UTC. It is a raw Unix epoch in **seconds**, fractional —
e.g. `1785357161.40969` → `2026-07-29T20:32:41.409Z` — NOT the ISO-ish `stamp_zulu`
date-string shape used for transmissions elsewhere in this same file (see
`ParseZuluStamp`); confirmed against ten real wire captures (LSZH and EGLL,
2026-07-29/30). A value that is not a plausible epoch-seconds instant — zero, negative,
or large enough to overflow `DateTime` outright, which is exactly what a future
migration to millisecond or microsecond epochs would publish — is treated the same as
an absent field: `TaxiPathStampUtc` falls back to the flight.json file's own
last-write time, a later answer than SI's generation time but still an honest one,
rather than an unhandled exception on the pilot's Ctrl+S/Ctrl+Shift+S/Alt+Shift+S
hotkeys (an unguarded conversion of a live millisecond-shape value took down all three
at once before this range check existed).

A future task is expected to gate any clearance override on this stamp being provably
NEWER than the clearance it would replace — a capture taken before the clearance is
SayIntentions' own plan, not a correction to what the pilot was actually told.
`MapHoldShortsToTaxiways` already tolerates an applied sequence that differs from the
spoken one, so the hold-short side of a future override still holds.

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

The key comes from `flight.json` and from nowhere else. The SAPI hostname comes from the
same file, which this app does not own.
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
