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
| Input | `Ctrl+Shift+Y` | Build a taxi route from the current clearance |

The two readouts work without a simulator connection — they only read the local
flight file and the SayIntentions API. Building a taxi route needs an aircraft
position, so it requires a connected sim, and the clearance is not in the local
file, so it also requires a reachable SayIntentions API.

### Last transmission

Speaks the most recent **ATC** transmission. Two things are filtered out, for different
reasons. SayIntentions mixes cabin PA and crew intercom lines into the same message
stream, so pressing this during taxi gives you the ground controller, not the purser.

A message SHAPED like a ground instruction survives one cabin word in its text:
*"Hold position, passenger aircraft crossing left to right"* used to die on
"passenger" before the ATC speaker or COM1 channel were consulted — and a filtered
record is invisible to the clearance selector too. The override is three-keyed
(channel not a cabin channel, no cabin marker in the speaker/station/channel FIELDS,
imperative instruction shape in the message — `AtcInstructionVocabulary`), so purser
speech, which says "taxi", "runway" and "cleared to land" as prose, stays filtered.
`CLEARED TO LAND` is deliberately not an instruction shape; a real landing clearance
qualifies through its runway designator.

That instruction shape rests on two discriminators, both in
`SayIntentionsTransmissionClassifier.cs` beside `AtcInstructionVocabulary`. A shared
`NarrationGuard` lookbehind sits in front of every verb-initial leg — `hold short`,
`hold position`, `give way`, `cross`, `taxi to`, `taxi…via` (which additionally
carries its own gap blocklist, below), `continue taxi`, `line up and wait` — eight
legs in all — and refuses to match when a first-person or narrative word (WE,
WILL, PLEASE, OUR, CONTINUE…) sits immediately in front of the verb, since the
verb alone cannot tell a controller's "cross runway 27" from a captain's "we
will cross runway 27". Where the
guard would also catch a genuine ATC form, the fix is an explicit rescue leg beside
it, never a hole in the guard — `CLEARED TO CROSS` rescues what it blocks on `CROSS`,
`CONTINUE TAXI` rescues what it blocks on `CONTINUE`. Inside the `TAXI…VIA` gap a
second discriminator, a per-token noun-phrase blocklist, does the equivalent job: a
real clearance's gap is a bare destination ("the passenger terminal", "gate A-9"),
where a cabin bridge sentence puts a pronoun, modal or PLEASE instead.

Neither discriminator claims to be complete, and the file inventories six honest
residuals rather than hiding them. The two worth knowing before touching this code:
a nominal, non-imperative "taxi to the gate" inside cabin PA still passes and is
selector-reachable, because nothing at that position is a register word for the
guard to read; and PLEASE, needed to keep boarding PA filtered, also silences a real
"Please continue taxi via Alpha" if a controller ever phrases it that way. Both are
accepted trade-offs — see the lettered residuals (a)-(f) in the source for the rest
and why each was left open rather than traded for a worse failure.

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

What the import does **not** inherit is "the last one". This readout answers *what was
just said*; the import asks *where was I told to taxi*, and those stop being the same
question the moment the controller says anything after clearing you. See
[The clearance is not always the last thing said](#the-clearance-is-not-always-the-last-thing-said).

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
*is* the assigned gate, which catches a mis-set arrival position. That comparison
honors this scenery's own online aliases for the stand as well as its name — a KDTW
arrival parked at scenery stand A24A, which SayIntentions and OSM both call A24,
reports a match rather than "not assigned gate A24" at the very stand it was assigned.
At any other airport that comparison is meaningless and is not made.

### Build taxi route

Reads the current taxi clearance, works out the route against the airport's real taxi
network — from SayIntentions' own published ground track when that agrees with the
clearance, from the words otherwise — and fills in the Taxi Guidance dialog.

**This one needs the SayIntentions API to be reachable.** The local flight file does
not carry the clearance text, so every press fetches the recent transmissions over the
network and takes **the newest one that is a taxi clearance** — not simply the newest
one, which is routinely an advisory the controller added afterwards. With SayIntentions
itself not running there is nothing to read and you hear why. If the file is there but the request fails — the key rejected, or a five-second
timeout — you are **told the reason out loud** and the import carries on with whatever
else it has, which may be SayIntentions' own published ground track. Read the summary:
it says so. The two readouts above are unaffected.

By default the dialog opens with everything pre-filled so you can review it, then you
press **Calculate Route** to start guidance. Enable **SayIntentions import starts taxi
guidance immediately** on Settings → Taxi Guidance to skip the review step.

The Taxi Guidance dialog also carries a **Fill from SayIntentions** button (Alt+Y), one
tab stop above Calculate Route, which does exactly what the hotkey does — so the feature
is reachable with the dialog already open and without recalling the chord. It fills the
fields; it does not start guidance, unless the setting above says otherwise. Pressing it
while an import is already running is answered the same way as a second hotkey press.

An import replaces the **whole** route, including anything you had set up by hand
first — intersection departure, CAT III hold, hold-shorts. The clearance is the route.

#### Where the taxiway sequence comes from

There are two sources, and the import chooses between them on every press.

SayIntentions publishes its own **ground track** for the taxi — the pavement it expects
you to roll over, as plain coordinates. Matched against the airport's own taxiway graph,
that track names the legs of the route without anyone having to parse the controller's
phrasing, which is exactly where the text path keeps losing legs: Palma Ground said
"North" for a taxiway the navdata calls `N`, and that leg silently vanished from the
route. So when the track **agrees with** what was spoken — every cleared taxiway
present, in the order it was given — the sequence is taken from the track, and the
summary says *"Route from SayIntentions ground track."*

When the two disagree, the **clearance wins** and you are told so:
*"SayIntentions ground track differs from the clearance. Using the clearance."* The
track is a live plan that exists before any clearance does, so before you have been
cleared it is SayIntentions' own intended routing rather than a correction to what you
were actually told. What you heard on the frequency is what gets built, and a route that
is not the one ATC gave must never be discovered out on the taxiway.

When there is **no clearance at all** — you pressed before requesting taxi, or the
transmission fetch timed out — the track is still used, because it is the only thing
there is. But nothing has checked it, so the summary says exactly that: *"No cleared
taxiways to check it against, so this is SayIntentions' own plan, not ATC's."* Followed
by the reason the clearance is missing, when there is one. Treat that route as a
suggestion, not a clearance.

Either way, the **destination and the hold-shorts always come from the clearance**. The
ground track carries neither.

#### What the summary tells you

It names the destination, says where the sequence came from, then **everything that did
not survive**, and only then the route it actually built. Warnings lead deliberately: with
"start guidance immediately" on, the summary is spoken standing still and the first
turn-by-turn callout cuts off whatever is left of it once you start rolling, so the parts
you can act on go first.

- **"SayIntentions assigned South Terminal Gate A24, which this scenery lists under
  another name."** — the stand is here, under a different label: this scenery calls it
  A24A, the online data knows it as A24, and it was found through that alias. Spoken
  first, right after the lead.
- **"SayIntentions assigned Gate B06, which this airport does not have. This is the
  nearest stand to the assigned position."** — the stand name SayIntentions gave matches
  nothing in this scenery, by name or by any alias, and the destination came from the
  coordinate it published beside the name instead. Same slot as the line above, and you
  never hear both. The lead names only the stand that won, so these two are the only
  thing saying you are being taxied to a stand the controller did not name — and they say
  different things: an alias means the label is spelled otherwise, a position means
  nothing here answers to that name at all.
- **"Route from SayIntentions ground track."** — the sequence came from the published
  track, which agreed with the clearance.
- **"SayIntentions ground track differs from the clearance. Using the clearance."** —
  the two contradicted each other and the words won. You never hear both lines: a
  disagreement always resolves to the clearance.
- **"No cleared taxiways to check it against, so this is SayIntentions' own plan, not
  ATC's."** — a ground-track route with no clearance behind it. Only ever heard after
  the "Route from SayIntentions ground track." line.
- **"SayIntentions comms history timed out."** — or whatever else went wrong reading the
  frequency, including *"No taxi clearance on the SayIntentions frequency yet."* Only
  spoken when the clearance came up empty; a clearance that was read normally adds
  nothing.
- **"Could not apply K."** — a leg the route does not use, either because this airport
  does not have the taxiway or because the dialog could not seat it. The route is still
  built from whatever did match. On a clearance route both kinds are named, however many
  there are — every one of them is a word you heard.
- **"Could not apply F, G, R and 1 more."** — the same line on a ground-track route,
  where the names come from the airport's graph rather than from the controller. Only the
  legs the dialog could not seat appear (a name the airport lacks cannot occur — the
  sequence came out of the airport's own graph), and past three of them the rest become a
  count: ten unfamiliar syllables in a row is a recital, not information.
- **"12 of 40 ground track points were off the taxiways, so the route may be
  incomplete."** — most of the published track failed to match this airport's pavement.
  The last few points of a normal arrival are the turn into the stand, which is apron,
  so this only appears when a quarter or more of the track went unread.
- **"Could not set hold short of runway 22."** — a hold-short that reached no row.
  Treat it as still in force: guidance will not stop you there.
- **"Via A, B, C."** — the route that was built. On a ground-track route this is capped
  the same way the line above is (*"Via R7, E7, E6 and 9 more."*): those names come from
  the airport's graph rather than from the controller, and the real Zurich track runs
  twelve legs. The whole sequence is always in the dialog's route-summary box and in
  `sayintentions.log`.
- **"Hold short of runway 15R after N."** — a hold-short from the clearance that was
  set, on the taxiway it follows. One line per hold-short, in clearance order, after the
  route they belong to.
- **"SayIntentions route. Destination Gate A9 not set. Check the destination field."** —
  the dialog is open but you have to pick the destination yourself. When this happens the
  summary does *not* also claim to be routing you there.

Nothing that came out of the clearance is dropped in silence — with one deliberate
exception, above: on a **ground-track** route a taxiway the clearance named that this
airport does not have is not spoken at all (it is in `sayintentions.log` as
`notAtAirport`). Announcing it would mean saying "could not apply North" over a route that
does use taxiway N. A route shorter than the one you were cleared for is otherwise not
something you can see, so it is always said out loud.

If you press **Ctrl+Shift+Y** again while an import is still running, you hear
*"SayIntentions taxi route already being built."* The import can take several seconds —
reading the frequency, your position, and the airport's taxiway names — and two of them at
once would fight over the same dialog.

## Settings

There is no SayIntentions settings tab. The one option lives on **Settings → Taxi
Guidance**, under a SayIntentions heading at the foot of that tab:

- **SayIntentions import starts taxi guidance immediately** — off by default (see above).

It sits there because it decides what happens to a *taxi route*, which is that tab's
subject, and because it was the only option left once the API key was retired. The label
names SayIntentions first because the heading above it is a `Label` — not a tab stop — so
a screen-reader user arrowing between controls hears the checkbox entirely on its own,
with nothing to say the setting is scoped to the import rather than to taxi guidance at
large.

There is no API key field. SayIntentions publishes the key in `flight.json`
(`flight_details.api_key`) whenever a flight is active, confirmed in both live captures,
so a hand-entered copy could only duplicate it — or go stale and quietly override it with
something wrong. Removing the setting also retired the last error string that sent a pilot
looking for it: when there is no key and nothing in the file, the honest reason is that
SayIntentions is not running, and that is what is now spoken.

## Troubleshooting

Diagnostics are written to `%APPDATA%\MSFSBlindAssist\logs\sayintentions.log`. It
records which fields were found in `flight.json`, and for every route import one line
holding the destination, which **source** the sequence came from and whether the two
**disagreed**, both candidate sequences (`geoTaxiways` and `clearanceTaxiways`), the
ground track's point / **trimmed** / unsnapped / dropped-run counts and its stamp, the
stamp of the transmission the clearance came from (`clearanceStamp` — which says whether
the scan found the right one), the taxiways **applied**, the taxiways **skipped** (the
airport has them, the dialog could not seat them), the taxiways **not at this airport**,
the **hold-shorts** that were set and the ones that were **missed**. If the spoken summary and the dialog ever disagree, that line
is the record of what the import actually did — and it holds both sequences side by
side, so a disputed route can be read back without re-flying it. API keys are never
written to the log.

"SayIntentions flight.json not found" means no flight is active — SayIntentions writes
`%LOCALAPPDATA%\SayIntentionsAI\flight.json` only while connected to a flight.

Every aborted import now writes `Import aborted: <reason>` to `sayintentions.log`, at
Info. For five of the guards that stop the import — database unavailable, a
`flight.json` error, no current airport found, no taxi path data for the resolved
airport, no usable destination — `<reason>` is the same text the pilot heard. The
sixth, the database/simulator mismatch guard, announces through its own dialog rather
than a spoken line, so its log entry is a fixed note that the dialog was shown
(`Import aborted: database/simulator mismatch dialog shown.`), not the dialog's text.
Before this, an abort left no trace at all: the import and Ctrl+S both call into the
same SayIntentions comms endpoint, so a comms fetch with **nothing logged after it**
used to be indistinguishable between the two — a silently failed import and an
ordinary Ctrl+S last-transmission readout (which is a different feature and was never
expected to log anything) looked identical in `sayintentions.log`. Now every import
that runs writes something, always, so a comms fetch with nothing following it in the
log is a Ctrl+S, not a failed import. A second Ctrl+Shift+Y press while one import is
already running logs separately, at Debug (`Import refused: one is already
running.`), since a refusal is not a failed import.

---

## Developer internals

### Layout

Pure logic lives in `MSFSBlindAssist/Services/SayIntentions/` and is unit-tested:

| File | Responsibility |
| --- | --- |
| `SayIntentionsClearanceParser.cs` | All regex. Runway/gate/taxiway extraction from ATC speech. |
| `SayIntentionsClearanceSelector.cs` | Which transmission in a radio history is the taxi clearance. |
| `SayIntentionsTaxiPathSnapper.cs` | `taxi_path` geometry → an ordered taxiway sequence, against the airport's own named edges; and the trim to what is still ahead of the aircraft. |
| `SayIntentionsTransmissionClassifier.cs` | Radio vs cabin/PA classification. |
| `SayIntentionsInfoReport.cs` | The flight-information window's sections and line formatting. |
| `SayIntentionsEndpoint.cs` | SAPI URL construction, host allowlist, log redaction. |
| `SayIntentionsService.cs` | I/O only — `flight.json` reads and SAPI requests. |
| `SayIntentionsModels.cs` | Context/transmission/parking/result types. |

UI wiring is `MainForm.SayIntentions.cs`, which orchestrates and parses nothing itself —
`ChooseTaxiwaySource`, `ParseClearanceTaxiPlan` and `MapHoldShortsToTaxiways` live there
as pure statics so the wiring is testable at all. Deleting the geometry branch used to
leave the whole suite green. There is no SayIntentions settings panel: the one option is
a checkbox at the foot of `TaxiGuidancePanel`.

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
`TaxiPathPoints`, and are the source of the imported route whenever it agrees with the
clearance; no name-ish member ever is — see
[Reading `taxi_path`: coordinates only, never names](#reading-taxi_path-coordinates-only-never-names)
and [The route comes from the ground track, when it agrees with the clearance](#the-route-comes-from-the-ground-track-when-it-agrees-with-the-clearance).
The sibling `flight_details.timestamp` is a raw Unix epoch in **seconds**, fractional
(e.g. `1785357161.40969`), not a date string. It is read and logged, and it is **not**
when the path was computed — it is when SayIntentions last wrote the file, which is why
nothing trusts the geometry on the strength of it.

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
destination**. Two fields are now measured in flight too — the first two bullets
below — but the rest of this table is still untested airborne. Do not design a
further airborne readout against it.

The specific open questions, any of which a single mid-cruise copy of the file would
settle:

- **`current_airport` in flight is now measured.** In the cruise it holds the
  **controlling ARTCC's ident, not an airport**: `KZLC` (Salt Lake Center), then
  `KZOA` (Oakland Center) at the handoff — visible as two successive reads of the
  same field — restoring the real airport once back on the ground. Neither center
  ident exists in the airport table, which is exactly what the taxi import's
  candidate validation (see "Which airport the import resolves against" below) and
  the flight-information window's facility label both key on, alongside the
  `^KZ[A-Z]{2}$` shape — `KZPH` (Zephyrhills) and `KZZV` (Zanesville) are real
  airports, so the shape alone is forbidden as a filter. Measured from the owner's
  live log, 2026-08-04/05, a KDEN→KSFO leg plus a KATL session — the first airborne
  observations this integration has.
- **`assigned_gate` before arrival is now measured: yes.** "Terminal 2 Gate C6"
  appeared at least 31 minutes before landing on the same KDEN→KSFO leg, so the
  arrival-gate line in the flight-information window is available well before
  descent begins. Same provenance as above.
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

**There are TWO fallbacks and the gate has to cover both.** `MainForm` reads the live
`getCommsHistory` transmission, and `SayIntentionsService.ReadFlightContext` sets
`ClearanceText` from `flight.json`'s own transmissions when the file carries no
clearance field. Only the first was gated, and the second **takes precedence** — the
MainForm site runs only when `ClearanceText` is *already* empty, so the shape test never
saw the file's transmission at all. On rollout that transmission is the landing
clearance: it became the clearance text, `ParseDestinationRunway` found `runway 23L` with
no hold-short span to mask, and the just-landed aircraft was routed **at the runway it had
landed on**. Both sites go through the one `SayIntentionsClearanceSelector`, which calls
the one `LooksLikeTaxiClearance`, so they cannot drift.

### The clearance is not always the last thing said

The shape test above was applied to **the** last transmission, as a pass/fail gate: if the
newest thing on the frequency was not a taxi clearance, there was no clearance. KDTW,
live, 2026-07-31 is why that is not enough. The pilot was holding short of runway 4R, was
cleared to cross and continue, and pressed Ctrl+Shift+Y seven seconds later:

```
23:41:34  ATC   cross-runway 4R, then continue taxi via K, Q      <- the clearance
23:41:38  ATC   hold short of runway 4R, 737 on the runway        <- 4 s later
23:41:41  press
```

The advisory was the newest transmission and was correctly rejected — and nothing looked
one message further back. The import logged
`clearanceProblem='The last SayIntentions transmission was not a taxi clearance.'`, took
SayIntentions' unchecked ground track as the whole route, and delivered A5, A, R:
taxiways behind the aircraft. A controller interleaving advisories with clearances is
ordinary, so this is not a one-off.

The lookup now **scans newest-first for the newest transmission that IS a taxi clearance**.
Four things bound what it may return, and each has a failure behind it:

- **Never the pilot.** That rule already existed at the reader, and a scan-back is exactly
  where it stops being obvious: this same capture carries the pilot's readback of the
  ORIGINAL clearance — *"Taxi to Alpha 24 via Alpha 5, Alpha, Romeo, hold short of runway
  4R"* — sitting in the history looking every bit like a clearance.
- **This airport only.** Each `getCommsHistory` record carries an `ident`, and the feed
  spans the whole flight: the KDTW capture still held Memphis Ground's *"Runway 36L taxi
  via P2, T, M, M1"*, 2.5 hours and 500 miles behind. A record with no ident cannot
  contradict the bound and stays eligible — that is every transmission read out of
  `flight.json`, which publishes no ident anywhere, so treating absence as a mismatch
  would retire that path rather than bound it.
- **Within half an hour** of the newest transmission — the history's own clock, not the
  wall clock. Judgement rather than measurement, and sized against this one capture from
  both directions: the clearance the pilot needed sat 4 s back, and the original clearance
  the aircraft was **still rolling on** sat 13 min 57 s back, so a tighter window starts
  refusing clearances that are still in force. Half an hour is well past that and well
  short of a turnaround dwell, which is the resurrection worth stopping — a clearance from
  the leg before this one is a route already flown.
- **Route content, between two otherwise-eligible transmissions.** The newest one that
  carries something a route can be built FROM — a via-list, a destination runway or a
  destination gate (`HasRouteContent`) — outranks a newer one that is merely taxi-shaped
  but empty of any of those.

The airport bound does most of that work; the window is the belt beside it, and the only
one that bites at the `flight.json` site, where no ident exists. That site has never been
observed to carry comms at all, so its widening from one transmission to half an hour of
them is theoretical — but it is the bound to revisit first if SayIntentions ever starts
filling it.

**No callsign bound, deliberately.** The comms history only ever carries
transmissions to and from the user's own aircraft — SayIntentions does not put
ambient AI traffic or other network pilots into `getCommsHistory` (owner-
relayed, 2026-08-03). A callsign gate would therefore only ever exclude
records mis-parsed from our own feed, and `callsign_icao` is unreliable for
matching anyway (it is the hyphenated speech form). If a capture ever shows a
foreign clearance in the feed, this is the assumption it breaks.

**A bare advisory does not outrank the clearance behind it.** "Continue taxi,
give way to the company 737." is taxi-shaped, and as the newest transmission it
used to win the scan outright — degrading the import to shortest path while the
clearance actually in force sat 40 s further back. The scan now runs twice:
first for the newest transmission with ROUTE CONTENT (a via-list, a destination
runway or a gate — `HasRouteContent`), then, only when that finds nothing,
exactly as before, so a lone contentless advisory is still better than nothing.

**Ctrl+S is deliberately untouched.** It still speaks the newest ATC transmission, clearance
or not. The two are different questions of the same history — *what was just said* versus
*where was I told to taxi* — and at KDTW they have different answers four seconds apart.
What changed underneath is that the whole history is now cached rather than only its newest
entry, because one cached answer cannot serve both.

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

**And the separator before the runway is a hyphen as readily as a space.** KDTW Ground,
live, 2026-07-31: *"cross-runway 4R, then continue taxi via K, Q"*. Spelled `\s+`, the
mask matched CROSS followed by whitespace and never saw that crossing at all, so
`ParseDestinationRunway`'s leftmost "runway 4R" became the **destination**: a taxiing
aircraft routed at the active runway it had just been cleared to *cross*. It went
unnoticed only because the clearance was being thrown away for a different reason (above),
so the parser never ran on it — fixing the lookup exposed it. `PrefixToRunway` is now a
const of its own, shared between the mask and the capture for the same reason `HoldPrefix`
is, and `HoldPrefix` already had `[\s-]+` inside its own "hold-short" — the same lesson,
one clause further along.

**And a crossing or hold-short can name SEVERAL runways.** KDTW-style parallel
pairs arrive as *"cross runway 28L and runway 28R"* or *"cross runways 4L and
4R"*; with only the first token bound, the second runway stayed unmasked and —
on a gate-bound clearance — became the leftmost "runway NN" the destination
capture found: an aircraft routed AT a runway it was cleared to cross. The list
tail (`RunwayList`) is one spelling shared by the mask and the hold-short
capture, plural "runways" included, and a plural hold-short yields one
hold-short per runway (the dialog carries one per row, so the second is
announced as "could not set" rather than dropped in silence). The tail admits
WRITTEN runways only (`WrittenRunwayTailToken`, at most two digits, never the
full spoken-word branch) — a spoken multi-runway list ("runways four left and
four right") therefore still masks only its first runway, the same coverage as
before lists existed, and a missed mask extension can only under-mask, never
fabricate a hold-short.

The same masking is why the taxiway scan does **not** truncate at `cross`/`then`: a
clearance legitimately continues, and reuses taxiways, across a runway crossing (the
KBOS pattern in [taxi-guidance.md](taxi-guidance.md)). It stops only at a genuine
terminator — `contact`, `monitor`, `squawk`, `remain`, `report`, `give way`, `follow`,
`information`, `caution`, `traffic`, `expect`. `information` is there because the ATIS
letter is spoken phonetically ("advise you have information Sierra"): read as route
text it silently appends a real taxiway S to the clearance, or claims the airport is
missing one. `caution`/`traffic`/`expect` are there for the same reason from the other
direction: SI appends advisory tails to a clearance, and a phonetic word inside one
("caution golf cart crossing") became a route leg the controller never cleared.

**Known residual:** a SENTENCE-INITIAL bare designator in an un-terminated tail can
still match — "…via Kilo, Quebec. A 737 is on short final." reads the capital A as
taxiway A, because no terminator word precedes it and the case asymmetry only
protects lowercase prose. Same family as the "proceed north then LE" residual below:
closing it needs a guard on what may FOLLOW a single-letter designator, whose failure
mode is the silent dropped leg.

**Hold-shorts are RUNWAY hold-shorts, by SayIntentions' own behaviour.**
SayIntentions never instructs a hold short of a TAXIWAY (owner-confirmed,
2026-08-03), so the mask and captures deliberately bind the hold/cross prefixes
to runway tokens only, and a taxiway name after "hold short of" would today be
read as a route leg. That is acceptable while the phrase cannot occur on the
wire; if SI ever adds taxiway hold-shorts this is the first thing to revisit —
the fix belongs in the shared `HoldPrefix`/`RunwayList` consts (mask the span,
report "could not set hold short of taxiway …"), never in a second copy of the
phrasing. The per-row hold-short combo the import populates is runway-only too
("Hold short of runway"), so parsing alone would not be enough — Progressive
Taxi's separate terminator block does offer a taxiway-terminator option, but
that block is self-contained with its own combos and sits outside the import's
path entirely.

### Taxiway matching case asymmetry

`BuildTaxiwayPattern` emits `(?:A|(?i:ALPHA))` per character: the literal designator
matches **case-sensitively** (uppercase only) while the NATO word does not. That
asymmetry is the only thing stopping the English article "a" being read as taxiway A,
and the preposition "at" as taxiway AT. Callers must never pass
`RegexOptions.IgnoreCase` to this pattern.

Overlapping candidates resolve longest-first, so "Alpha-Tango" reads as `AT` rather
than `A` followed by `T`.

Each taxiway's pattern is compiled once and cached (`TaxiwayPatterns`, keyed on the
normalized name) rather than rebuilt on every scan — `ScanTaxiways` runs one pattern
per known taxiway per keypress, upwards of a hundred at a large airport, past what the
static `Regex.Matches` cache holds.

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

Case folding is CULTURE-INVARIANT everywhere: every IgnoreCase regex in the
integration carries `RegexOptions.CultureInvariant`, because under tr-TR the
pattern letter I folds to dotless ı and `\b(?:TAXI|VIA)\b` stops matching
"taxi" — which killed the entire import for Turkish-locale users.
`SayIntentionsCultureTests` runs the load-bearing paths under tr-TR.

### Reporting what did not survive

Three things can go missing between the clearance and the route. All three are spoken.

| Lost | Detected by | Reported as |
| --- | --- | --- |
| A taxiway this airport does not have | `ScanTaxiways` → `Unresolved` | `Could not apply …` *(clearance-sourced routes only — see below)* |
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
quietly at this layer.

There IS now a structured second source that can catch such a loss, but it catches it by
**restoring the leg rather than reporting it**: the published ground track carries the
pavement the parse could not name, and when the parse's surviving legs still run through
that track in order the route is taken from the track instead — see
[The route comes from the ground track, when it agrees with the clearance](#the-route-comes-from-the-ground-track-when-it-agrees-with-the-clearance).
That is the live LEPA "North" case, recovered. It is a recovery and not a safety net:
where the track is absent, disagrees, or is rejected as too long to be a description of
the cleared route, this scan is once again all there is, and a bare-designator loss is
still quiet.

The report itself is **dropped entirely on a ground-track route**, because there it
describes nothing: every name in that sequence came from the airport's own graph, so
announcing "Could not apply North" over a route that does include `N` teaches exactly the
distrust the phonetic-only rule exists to prevent. The other half of the line — a taxiway
the dialog could not seat — is spoken on both paths.

**That exception has a cost, and it is accepted rather than overlooked.** On a ground-track
route a taxiway the clearance genuinely named that this airport genuinely does not have is
now never spoken — the only record is `notAtAirport=[…]` in `sayintentions.log`. It is the
better of the two failures (a false "could not apply" over a leg the route IS taking
poisons every later announcement, while this one loses a line about a taxiway that could
never have been applied anyway), but it means the general rule "nothing from the clearance
is dropped in silence" has exactly one hole in it. Do not close it by restoring the line,
and do not remove that log field — it is what makes the hole diagnosable.

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

Turning those coordinates into a route is a separate concern:
`SayIntentionsTaxiPathSnapper.Snap` snaps each point to the nearest edge of the
airport's own named taxiway graph — never to anything SI publishes as a name — and the
import uses that sequence whenever it agrees with the clearance. How it is snapped, and
what "agrees" means, is
[the next section](#the-route-comes-from-the-ground-track-when-it-agrees-with-the-clearance).

`flight_details.timestamp` is read alongside it, into `TaxiPathStampUtc`, purely as
diagnostic data — it goes in the log line and nothing branches on it. See
[Why a timestamp cannot be the trust signal](#why-a-timestamp-cannot-be-the-trust-signal)
for why. The parse is still worth describing because it is a shape trap: the value is a
raw Unix epoch in **seconds**, fractional — e.g. `1785357161.40969` →
`2026-07-29T20:32:41.409Z` — NOT the ISO-ish `stamp_zulu` date-string shape used for
transmissions elsewhere in this same file (see `ParseZuluStamp`); confirmed against ten
real wire captures (LSZH and EGLL, 2026-07-29/30). A value that is not a plausible
epoch-seconds instant — zero, negative, or large enough to overflow `DateTime` outright,
which is exactly what a future migration to millisecond or microsecond epochs would
publish — is treated the same as an absent field: `TaxiPathStampUtc` falls back to the
flight.json file's own last-write time, a later answer than SI's own but still an honest
one, rather than an unhandled exception on the pilot's Ctrl+S/Ctrl+Shift+S/Ctrl+Shift+Y
hotkeys (an unguarded conversion of a live millisecond-shape value took down all three
at once before this range check existed).

### The route comes from the ground track, when it agrees with the clearance

The imported taxiway sequence is derived from SayIntentions' published `taxi_path`
geometry, snapped to the airport's own taxiway graph, **whenever that track agrees with
the spoken clearance**. Otherwise the clearance wins and the disagreement is announced.
The clearance always owns the destination and the hold-shorts; geometry carries neither.

This exists because deriving the route from the **phrasing** keeps failing on naming
variance, and failing silently. Palma Ground, live: *"Taxi to holding point runway 24R
via LE, E, North, H2."* LEPA's navdata calls that taxiway `N`; SayIntentions rendered the
bare letter as the plain English word; the leg was dropped and the pilot was handed a
three-taxiway route with nothing to say a fourth had gone missing (see
[Taxiway matching case asymmetry](#taxiway-matching-case-asymmetry) for the pattern-side
half of that fix). Geometry has none of that problem: every name it can produce is a name
the router already knows, because it comes out of the router's own graph.

#### The measurement

Four live captures, and the whole design falls out of them.

**LSZH, 2026-07-29.** Zurich Ground, 20:33:42Z: *"Taxi to Gate E52 via E4, E, C."* The
`taxi_path` published 9 s later snaps to exactly `E4, E, C`:

```
run-lengths:  E4x5  Ex7  "Link 6"x1  Ex1  "Link 5"x1  Ex10  Cx11  (unsnapped)x4
keep runs >= 2 points  ->  E4 , E , C        (identical at >= 3)
```

The four unsnapped points are the turn into stand E52, which is apron rather than taxiway
pavement. `Link 5`, `Link 6` and `Inner` are OSM names for connector stubs no controller
ever says; they appear as single points because SI's path clips their corners.

**EGLL, 2026-07-30.** *"Taxi to Gate 325 via N5E, A, F, G"*, captured 10 s later, snaps to
exactly `N5E, A, F, G` — this time against **navdata**-named edges (EGLL has 5214 of 5706
segments named), so unlike LSZH the result does not rest on the OSM augmentation:

```
run-lengths:  N5Ex6  LINK34x1  Ax15  Fx3  Bx1  Fx21  Gx17   4 of 68 unsnapped
nearest-edge distance:  median 1.53 m,  p90 2.31 m
```

Those two distances close an open question the snapper recorded: the 25 m tolerance had
only ever been measured against OSM centrelines, and a systematic navdata-vs-OSM offset
would have been invisible to that measurement. It is not there. SI's published points sit
essentially **on** the navdata centreline too, so the tolerance is not calibrating a fit —
it is drawing a line between "on pavement" and "on the apron", with two orders of
magnitude of daylight either side.

**LEPA** is the failure the feature exists for, and geometry recovers it: the text parse
gave `LE, E, H2`, the track gives `LE, E, N, H2`, and the parse's three legs run straight
through the track's four. The route delivered is the clearance with its missing leg
restored.

**The stale case.** An LSZH capture taken 1 min BEFORE the clearance, during the landing
rollout, gives `R7, E7, E6, E7, N, E, Inner, E, B, E5, F, C` — twelve legs across the
airfield. That is SayIntentions' *own* plan, a genuinely different route, and it was
sitting in `taxi_path` the whole time. Delivering it would have been confidently wrong,
and with **Start taxi guidance immediately** enabled the aircraft would have started
following it.

So the track has to earn the route.

#### Why a timestamp cannot be the trust signal

The first version of this gated on freshness: use the geometry only when its stamp is at
or after the clearance transmission's. It was built, and it could not do the job.

`flight_details.timestamp` is when SayIntentions **wrote the file**, not when it computed
the path. Three captured pairs carry a **byte-identical** `taxi_path` under stamps
**68 s, 116 s and 252 s apart** — the same geometry, aging, restamped on every write. And
a file write is always later than a transmission already on the frequency. So whenever
both stamps existed the gate passed. It admitted every stale path there is while reading,
in code and in the log, like a safety gate — which is worse than having no test at all.

`taxi_path` also carries no stamp of its own, and nothing else in the file does either.
There is no field on the wire that says when the path was computed. **Do not rebuild
this gate**, on `flight_details.timestamp` or on the file's mtime, and do not reason from
"the stamp is newer, so the path must be" — the counter-example is three byte-identical
arrays.

#### What earns it instead: agreement

`MainForm.ChooseTaxiwaySource(clearance, geometry)` is a pure static — extracted as one
precisely so the decision is testable, since deleting the geometry branch outright used
to leave the entire suite green. In order:

1. **Geometry empty** (no path published, or one that snapped to nothing) → clearance.
   No better than no path at all; the log keeps the counts that say why.
2. **Clearance empty** (no text, or a parse that found nothing) → geometry. The track is
   all there is, and there is nothing for it to contradict — but nothing has *checked* it
   either, so this is the one accepted-geometry case the caller announces:
   *"No cleared taxiways to check it against, so this is SayIntentions' own plan, not
   ATC's."* See [A track nothing checked](#a-track-nothing-checked).
3. **The clearance runs through the geometry in order, gaps allowed** — a subsequence
   walk — **and** the track is at most `2n + 1` legs long for `n` cleared legs →
   **geometry**.
4. Anything else → **clearance**, and *"SayIntentions ground track differs from the
   clearance. Using the clearance."* is spoken.

**The walk, and why gaps are the point.** A real track legitimately names legs the
controller did not: the stand it starts on, the lead-in it ends on, and — the case this
is for — the leg the text parse could not name. What makes it evidence rather than
coincidence is **order**. A set-overlap or "mostly agrees" rule would accept the stale
EGLL track, which shares `F` and `G` with the clearance but reaches them across the far
side of the airfield. The same ordered walk is what rejects that track outright: it
carries `N5W` where the clearance says `N5E`, so it fails on the very first cleared leg.
One character apart is a different taxiway, on the other side of the stand group. Names
are compared through `NormalizeTaxiwayName`, the same way everything else in this import
compares them — spacing and punctuation stripped, case-insensitive, so `"n 5 e"` is
`N5E`.

**The comparison collapses consecutive duplicates; the delivered sequence does not.**
`ParseClearanceTaxiPlan` deliberately keeps a taxiway repeated across a hold-short — the
KBOS pattern, *"via November, hold short of 15R, then November, Kilo"* — because the form
carries one hold-short per row and collapsing the repeat throws the second stop away. The
snapper structurally cannot produce that repeat: it drops unsnapped and too-short runs
**before** collapsing consecutive duplicates, so `[N … N]` arrives as a single `N`.
Walked raw, `[N, N, K]` could never agree with its own track `[N, K]`, and the pilot heard
"the ground track differs from the clearance" about two descriptions of the same pavement
— which also switched the geometry path, and the dropped-leg recovery it exists for, off
for **every clearance of that shape**. So the walk runs against `CollapseConsecutive` and
only the walk does; the raw list still reaches the form, and each November keeps its row.
Only *adjacent* repeats collapse, so a genuine later revisit stays a leg the track has to
carry twice.

**The `2n + 1` ratio guard.** The walk's discriminating power scales with how many legs
the clearance has, and a short one runs straight through almost any track touching the
same corner of the airfield. Against the real LSZH pre-clearance publication — twelve
legs — *"via E, C"*, *"via N, E, B"* and even a bare *"via E"* all walk clean through,
and a track that agrees is taken **silently**. So the track additionally has to be short
enough to be a description *of* the cleared route rather than a route of its own: at most
two track legs per cleared leg, plus one. Every real agreement measured runs 1.0–1.33
track legs per cleared leg (LSZH 3:3, EGLL 4:4, the LEPA dropped leg 3:4); every stale
reading runs 2.5–12. The guard
counts the **collapsed** clearance, because a taxiway named twice is one leg of evidence
and constrains the walk exactly as much as one does — it must not buy the track extra
length.

#### A track nothing checked

Rule 2 is not a corner case. `flight.json` carries **no clearance text**, so every import
depends on a live `getCommsHistory` round-trip on a five-second timeout — and before the
pilot has requested taxi there is nothing on the frequency to read at all. What sits in
`taxi_path` then is SayIntentions' **own** pre-clearance plan: the live LSZH capture is
twelve legs across the airfield, published a minute before Zurich Ground said anything.

Taken silently, that announced *"SayIntentions route to Runway 16. Route from
SayIntentions ground track. Via R7, E7, E6, E7, N, E, E, B, E5, F, C…"* — a complete,
confident route no controller had given, with nothing to say the clearance had never been
read. (That quotation is the behaviour as it was. Such a route now also reads *"Via R7,
E7, E6 and 9 more."* — twelve graph names in a row was a recital in its own right; see the
cap above.) Worse, `GetLastTransmissionAsync`'s `Error` was **discarded** at the call site
(`last.Transmission` only), so the timeout, the HTTP failure and the "that was not a taxi
clearance" case all produced the same silence.

The route is still built, because a published track is often the only thing that survives
a slow SAPI and refusing it would substitute a shortest path that is no more ATC's route
and reads *"No taxiways from the clearance matched this airport"* — a line that claims a
clearance was consulted. What changed is that it **says what it is**:
`BuildExternalRouteAnnouncement` takes `clearanceNamedTaxiways` and
`clearanceLookupProblem`, and when the clearance came up empty it speaks the "own plan"
clause (ground-track path only — a shortest path is this app's, not SayIntentions') and
then the reason. A clearance that WAS read adds no words at all.

#### `taxi_path` is transient, and it is trimmed to what is ahead

It is populated during the taxi and empty in the cruise. Nothing here treats it as a
record of what was cleared.

**What it does with the legs already flown is not settled.** Two live captures, and they
disagree:

- **LSZH** went **77 → 40 points** as the aircraft moved — the *remaining* route,
  shrinking behind it.
- **KDTW, 2026-07-31** published **124 points of which 76 — 61 % — were behind the
  aircraft**, the first of them 1,510 m back down the taxiway. That is the route as
  **issued**, with more than half of it already flown.

One capture of each. This section used to state the first as the rule, and a whole safety
argument rested on it: a late press has a shorter track, so it no longer runs through the
clearance, so the walk fails and the clearance wins — "the intended outcome, not a
defect". That reasoning does not survive the second capture. At KDTW the track was long,
it *did* run through the (short) clearance, and it won — delivering A and R, taxiways the
aircraft had left.

So what actually makes a late press safe is a **trim**, not an assumption:
`SayIntentionsTaxiPathSnapper.TrimToPointsAhead` finds the published point nearest the
aircraft and snaps from there on. At KDTW that turns `A, R, K, Q, U9` into `R, K, Q, U9` —
A dropped, and R kept, because the aircraft is standing on R with about 220 m of it still
to run past the crossing. What is behind is decided by where the aircraft is, not by which
leg it started on.

It is **guarded by the same 25 m** the snap uses. If the nearest published point is
farther than that, the aircraft is not on the published track — towed, repositioned, or
the track belongs to somewhere else — and nothing can say which part of it is behind, so
the path passes through untouched. A wrong trim silently deletes legs the pilot was
cleared for; no trim at worst restores the old behaviour. An exact tie breaks toward the
**earlier** point, so a route that doubles back past the aircraft keeps the whole of its
second pass.

Nothing about a trim is announced. A route that starts where the aircraft is standing is
the expected answer, not a warning. `sayintentions.log` records it as `geoTrimmed`, which
with `geoPoints` adds back up to what SayIntentions published.

The trim also feeds the agreement walk, and helpfully: a track cut to the remaining route
is shorter, so it is a *description* of the remaining clearance rather than a route of its
own, which is what the `2n + 1` guard is measuring.

#### The naming source is a real limit

The snapper searches **named** edges only, and named-edge coverage varies hard between
airports (fs2024 navdata): EGLL 91 %, EDDF 87 %, KJFK 77 %, LEPA 63 % — and **LSZH 0 of
1840**. At LSZH every name came from the OSM taxi-data augmentation; without it there is
nothing to snap to and the import degrades to the clearance text.

At a low-coverage field the failure is subtler than a miss. Unnamed pavement is invisible
to the search, so a point genuinely on an unnamed taxiway can be attributed to a *named*
one up to 25 m away. Geometry removes the dependency on SayIntentions' **phrasing**; it
does not remove the dependency on names existing at all.

The snapper never fetches names itself. It takes edges off the already-built `TaxiGraph`
(`TaxiGraph.GetNamedEdges()`, via `TaxiAssistForm.GetLoadedTaxiwayEdges`), which the
augmenting provider has already filled from OSM where navdata left segments unnamed.
Re-fetching would duplicate the augmentation layer and put a network call on a hotkey.
`GetNamedEdges` sorts by a key intrinsic to the edge (`TaxiwayName`, then endpoint
coordinates) rather than by node id, because node ids come from `Build()` processing
order: the snapper breaks a nearest-edge tie with a strict `<`, so an ordering that
depends on how navdata happened to be imported could silently flip which taxiway a blind
pilot is told.

#### Cost

The snap is linear over every named edge for every published point — 20–90 ms on the UI
thread, e.g. a 111-point capture against EGLL's 5,189 named edges is ~576 k distance
evaluations at about 40 ms. That is acceptable because it happens **once**, behind a
hotkey that is already awaiting an HTTP round-trip and an airport load. A spatial index
would be more code for no perceptible gain.

#### Accepted limits

These are decisions with their evidence attached, not open bugs.

- **The `2n + 1` guard is calibrated on ONE stale capture.** Real matches sit at
  1.0–1.33 and stale ones at 2.5–12, so the gap is wide — but a second stale capture
  could move the boundary, and the constant should be revisited against one rather than
  defended on principle.
- **A badly-degraded parse now loses a correct track.** Where the text recovers one leg
  of five, the track legitimately *is* much longer and the guard rejects geometry that
  was right. This is the deliberate direction: falling back to a clearance we know is
  incomplete still names the legs it could not apply, so the pilot is warned, whereas
  accepting a stale track is silent and wrong. Bias toward the clearance. A track
  rejected on length is reported as a disagreement, never dropped quietly.
- **Hold-short anchors cross sources on the geometry path.** They are parsed from the
  clearance and then mapped onto whichever sequence is being applied, so a clearance
  anchor naming a taxiway the track lacks maps to `-1` and is announced as unsettable
  rather than hung on the wrong row (see
  [Hold-shorts belong to their own taxiway](#hold-shorts-belong-to-their-own-taxiway)).
  A mid-taxi import whose shortened track begins after an already-crossed hold-short will
  report it that way — which is correct, and audible.
- **Both leg lists are capped at three names plus a count, on the geometry path only.**
  That is the unapplied legs *and* the "Via …" line naming the route being taken: those
  names come from the graph rather than from the controller, ten unfamiliar syllables in a
  row is a recital rather than information, and the real LSZH pre-clearance track is twelve
  legs long — so the "Via" line had exactly the same problem the skipped list did. A
  clearance route's lists are never capped: every name there is a word the pilot heard.
  Nothing is lost outright either way — the dialog's route-summary box and
  `sayintentions.log` carry the full sequence.

### Gate names

`ParseDestinationGate`'s capture admits a **hyphen** as well as a space, so "gate A-9"
reaches stand A9. Normalizing `A-9` → `A9` afterwards was not enough while the capture
itself stopped at the bare letter: that routed the pilot to stand "A" — or, with no such
stand, fell through to the departure RUNWAY as the destination.

`NormalizeParkingName` strips a descriptor tail only when the dash is **spaced**
("A9 - Terminal 1"). A bare hyphen is part of the stand name.

**A leading zero is padding, not identity.** EDDB taxi-in, live, 2026-07-30:
SayIntentions assigned `"Gate B06"`, while the navdata this app routes on stores that
stand as `parking` `name='GB'`, `number=6` — `LittleNavMapProvider` maps the MSFS gate
code `GB` to `B`, and `TaxiGraph.FormatParkingDisplayName` renders the pair as "B 6".
Normalized, the two sides read `B06` and `B6`; `MatchDestinationLabel` compares them for
exact equality, so the assigned gate could never match. Destination resolution then ran
its whole chain — clearance runway, clearance gate, assigned gate, departure runway —
and took the last candidate it has, the ARRIVAL RUNWAY: a just-landed aircraft was
routed at 24L, along exactly the M3, B and V2 the controller had given for the gate. The
taxiway half of that import was perfect, geometry and clearance agreeing exactly. Only
the destination was wrong, which is the dangerous shape — everything else sounded right.

The near-miss is why this is a rule and not just a fix. The RUNWAY half of the same
comparison already tolerated exactly this: `CleanRunway` pads to two digits, so a
clearance's "05L" meets navdata's "5L" without anyone having to think about it. Only the
gate half did not.

`NormalizeParkingName` therefore ends by stripping leading zeros within a digit run,
`(?<![0-9])0+(?=[0-9])`, applied last — after the non-alphanumeric strip, so it sees the
digits the comparison will actually use. **Both guards are load-bearing.** The lookbehind
confines the run to the START of a digit group; without it "100" loses its middle zero and
reads as stand 10. The lookahead requires a digit to survive, so a lone "0" and any
trailing zero stay. And `B10` must never collapse to `B1` — that is this same wrong-stand
failure pointed the other way, and the more insidious one, because the route it produces
leads to a real stand the pilot has no reason to doubt.

`ParkingNamesNormalize` and `OnlyLeadingZerosAreStrippedFromAStandNumber` pin the
normalization; `Gate_destination_matches_across_a_zero_padded_stand_number` pins the match
against EDDB's own B-pier labels, including that asking for `B1` finds nothing rather than
`B 10`.

#### When the scenery does not name the stand the way SayIntentions does

Zero padding was one spelling difference. Sceneries have others — a MARS suffix the
navdata carries and nobody says, a stand the online data letters differently, a name this
scenery simply does not have — and every one of them lands in the same place: the name
matches nothing, destination resolution runs its whole chain, and the last candidate it
has is the **arrival runway**. The taxiway half of the import is meanwhile perfect, which
is what makes it dangerous — everything else sounds right.

A gate candidate is resolved in **three steps** — its name, then this scenery's own other
names for it, then the coordinate SayIntentions published beside the name. Each is weaker
evidence than the one above, so each runs only where the one above found nothing, on that
same candidate.

**Step two is the scenery's own alias.** Online data (OSM / apt.dat) routinely labels a
stand differently from the navdata, `GateAliasResolver` already collects those as
`ParkingSpot.Aliases` — number-matched, letter-agreeing — and the dialog's gate search box
already finds a stand by typing the name ATC used. KDTW taxi-in, live, 2026-07-31: *"Taxi
to South Terminal Gate A24 via Alpha-5, Alpha, Romeo, hold short of runway 4R"*, with
`assigned_gate` `"South Terminal Gate A24"`. The scenery calls that stand **A24A**
(`parking` `name='GA'`, `number=24`, `suffix='A'`). OSM calls it A24, the alias was
present, the search box found it — and the import could not, because the combo carries
`ParkingSpot.ToString()`, `A 24A - Gate Medium, also A24 (online)`, and
`NormalizeParkingName` deletes everything from the first **spaced dash** onward, which
every `Describe()` branch puts ahead of the alias (`" - {type}"`). The alias was invisible
to the one matcher that needed it, the assigned gate never resolved, and destination
resolution ran to the arrival runway: 04L, along exactly the A5, A and R the controller
had given for the gate, with the taxiway half of the import perfect.

`MatchDestinationAlias` compares the identifier against `NormalizeParkingName` of each
alias — exact and normalized, so a full label meets a bare alias and zero-padding is still
a spelling, and **never a substring**: a one- or two-character stand id `Contains`-matches
almost any entry the combo offers, "A2" included, and the "(None - calculate shortest
path)" sentinel with it.

**Step three is the published coordinate.** `current_flight` publishes `assigned_gate_lat`
/ `assigned_gate_lon` beside the name, as JSON **strings**, so there is a way to ask the
same question with no language in it. It sits last because it is a guess at which pavement
an unrecognized name must have meant, where an alias is still the same stand under another
label. It is attached to the **assigned gate** alone, behind the same `flight_destination`
check the name sits behind: an arrival stand's coordinate is as wrong at the departure
airport as its name is, and unlike the name it would always find *something* there.

Both fallbacks read `_destinationSpotMap`, which holds gate entries only while gate mode
is the selected destination type — a runway candidate probed in between repopulates it
with runway entries — so `SelectDestinationType(false)` is made once for the pair, ahead
of either.

**The coordinate test is the stand's own radius, doubled.** Not a number of metres, and no
longer plain containment. A radius multiple is what keeps it self-scaling with nothing to
tune: a Gate Extra states ~50 m of scale, a medium gate ~21 m, a packed GA spot a few
metres, and any single constant is either too tight for the first or too loose for the
last. The factor exists because the published point is the **nose-stop**, whose offset
scales with the parked aircraft rather than with whatever radius the navdata recorded, so
how much of the radius it eats is not a property of the stand at all.

| capture | stand | point sits | its radius | runner-up | contained? |
| --- | --- | --- | --- | --- | --- |
| EDDB 2026-07-30 | GB 6 | 18.9 m out | 21.6 m (71 ft) | 47.5 m | yes |
| KDTW 2026-07-31 | GA 24A | 30.1 m out | 22.9 m (75 ft) | 75.0 m | **no** |

Containment was calibrated on the first of those, the only capture there was, and the
second disproved it — on a gate whose name had also failed, so it fell through to the
arrival runway exactly as before. What both captures agree on is the **margin**: the right
stand is nearest by roughly 2.5×. So the radius decides who is admissible and centre
distance picks the winner among them, and `NoseStopRadiusFactor = 2.0` admits both correct
stands and neither runner-up (21.6 × 2 = 43.2 < 47.5; 22.9 × 2 = 45.8 < 75.0).

**"Exactly one of 139 spots" is no longer the property being relied on** —
nearest-among-admissible is. Doubled, EDDB's wide GB 7A (65.1 m out, 50 m radius) is
admissible too and loses on centre distance instead; overlapping tolerances are ordinary
on a pier of wide stands. **Two** real arrivals is all the factor is calibrated on, and it
is the number to re-check the first time a third disagrees — a stand that should seat and
does not, or a neighbour that wins. The 150 m ceiling beside it is still a sanity backstop
against a whole apron recorded as one stand, the same role `GateAliasResolver`'s 150 m
plays, and not the discriminator.

**The published point is the nose-stop, not the stand datum.** At EDDB it sat 18.9 m from
the navdata spot centre on bearing 68.6° against a stand heading of 68.8° — straight out
along the stand's own axis, the same distinction [gsx.md](gsx.md) records for GSX stop
positions. It is *expected* to sit off-centre, at KDTW by more than the whole radius, so a
"near the centre" test would reject the stand the aircraft is parked on.

**Radius units are per source and must be converted before the comparison.** A navdata
spot's radius is FEET, a GSX-sourced one's is METRES — the mix-up `ParkingSpot.FitsAircraft`
already records, where it "filtered almost everything out". Read raw, EDDB's 71 becomes
71 m and the 47.5 m runner-up the doubled radius is there to exclude is admitted along
with everything else; on a pier where the inflated neighbour is the nearer of the two, it
wins outright.

`SayIntentionsGatePositionMatcher` is the pure half and takes metres only, so the unit can
never be in doubt where the comparison happens; `TaxiAssistForm.MatchGateByPosition`
converts by source and is the only caller. A tie resolves to the earlier candidate — two
spots at one centre are nearly always one piece of pavement listed twice under variant
names (`C16` beside `C16S`), so either label taxis you to the same place, and what would
actually hurt is the answer changing between keypresses. `(0, 0)` is rejected at the
reader: it is a real coordinate to a distance test and exactly what an unset pair looks
like once two absent numbers are read as zero.

### An import owns the whole route

`ApplyExternalRoute` calls `ResetRouteShapingControls` first. `OnDestTypeChanged` only
clears the runway-only boxes when the destination TYPE changes, so a runway route
imported over a hand-built runway route otherwise keeps the old intersection departure
and CAT III hold — a different lineup point, with nothing in the announcement to reveal
it. `chkFitFilter` is deliberately exempt: it describes the aircraft's wingspan rather
than the route, and forcing it either way could hide the very gate the clearance names.

**A FAILED import owns nothing, and that needs its own restore.**
`TryResolveExternalDestination` documents "probing leaves no mark", but probing a *gate*
candidate sets `cmbDestType`, and `OnDestTypeChanged` unticks `chkIntersection` (which via
`OnIntersectionToggled` also empties `cmbIntersection` and `_intersectionMap`) and
`chkCatIiiHold` on the way out of runway mode. Putting the *type* back re-shows both boxes,
unticked. So the mirror image of the bug above: a pilot hand-builds "Runway 27L,
intersection departure at T4, CAT III hold", presses Ctrl+Shift+Y, the clearance names a
gate this airport does not have, everything fails, and they hear *"SayIntentions route
unavailable. No usable assigned runway or gate found."* — "nothing happened" — while their
intersection departure and LVP hold are gone, and the next Calculate lines them up at the
full-length threshold holding at the CAT I line. `RestoreDestinationState` therefore
restores those three as well as the type, search and destination, in that order (the
intersection list is rebuilt against whichever runway is selected, so the destination has
to be back first).

The intersection restore does **not** go through the checkbox: `OnIntersectionToggled` →
`ShowIntersectionListOrFallback` moves focus to the combo and can announce "No runway
intersections available. Full length departure." Neither belongs to a silent restore — the
pilot performed no action. The handler is detached, `PopulateIntersections` is called
directly, and `RestoredIntersectionIndex` picks the entry that was selected, the first if
that intersection is no longer offered, or `-1` (untick) if the runway has none.

**Apply atomically.** Everything that can throw — reading the graph's named edges,
snapping the ground track, showing the form — now runs *before* the destination probe, so
`TryResolveExternalDestination` and `ApplyExternalRoute` are adjacent statements. The probe
mutates the form on success and only restores on failure, so a throw between the two left
the form holding SayIntentions' destination on top of the pilot's leftover taxiway rows,
announced as nothing more than *"SayIntentions taxi route failed."*

### One import at a time, one airport load at a time

The import awaits repeatedly on the UI thread — up to 5 s of comms history, 1.5 s for a
fresh position, up to 8 s of taxiway-name prefetch, then a graph build — which is long
enough for a pilot who has heard nothing to press Ctrl+Shift+Y again. Two runs interleave at
every await. `BuildTaxiRouteFromSayIntentionsAsync` takes an `Interlocked` latch and
refuses the second press **out loud** (*"SayIntentions taxi route already being built."*);
silence is what made the pilot press twice in the first place.

The dialog's **Fill from SayIntentions** button calls that same method, through a
`Func<Task>` the form is constructed with — never a `MainForm` reference — so the button
and the hotkey share the one latch and cannot interleave. The call **re-enters the form it
was pressed on**: the import fetches `GetOrCreateTaxiAssistForm()`, which returns that same
already-constructed instance (the form is hide-on-close and never disposed), then loads the
airport and applies the route to the very controls the pilot is standing in. That is safe
in both directions — the latch is taken by the caller rather than anywhere on the button's
path, so a click never contends with itself, and the airport load *chains* rather than
rejects, so a nested call waits for pending work instead of deadlocking on it.

`LoadAirportDataAsync` needs its own guard for a different reason: it *clears*
`cmbFirstTaxiway`, `cmbDestination` and the dynamic taxiway rows before its awaits and
repopulates them after. A second load interleaving in that window leaves one caller
resolving its clearance against emptied combos — every `combo.Items.IndexOf` returns `-1`,
and the pilot hears *"No taxiways from the clearance matched this airport. Using shortest
path."* for a clearance that was perfectly good, with guidance starting on it. Loads are
**chained** rather than dropped, because a second load is usually a *different* airport (a
typed ICAO, the aircraft having moved) and dropping it would strand the form on the wrong
one. All three entry points — `SetAircraftPosition`, the `txtAirport.Leave` handler and
`LoadAirportForExternalRouteAsync` — go through the same chain.

### The summary has to survive guidance starting

With auto-start on, the order is `ApplyExternalRoute` → `OnCalculateClicked` → `LoadRoute`
(queues the router's own summary) → `StartGuidance` (first-taxiway `AnnounceImmediate`,
which **discards** anything queued) → the form's standstill `AnnounceImmediate`. The
import's summary used to be plain queued speech announced *after* all of that, so it was
the first thing a tactical callout killed — taking *"could not apply D, E"*, *"could not
set hold short of runway 22L"* and *"SayIntentions ground track differs from the
clearance"* with it. This codebase has paid for that lesson twice already, in
`TaxiGuidanceManager.Routing.cs` (the constrained-length advisory) and in
`OnCalculateClicked` (the runway-reach warning).

So `ApplyExternalRoute` no longer starts guidance. `TaxiAssistForm.StartImportedRoute`
does, taking a `Func<bool, string>` the form invokes at the moment it speaks — the `bool`
being whether guidance **actually** started, so a route that failed to calculate is never
announced as "Guidance started." and gets the "review the fields" tail instead, which is
also the right advice after a failed Calculate. Every Calculate abort an import can reach
goes through `AnnounceCalculateAbort`, which joins the reason and the summary into one
utterance rather than letting one stomp the other.

Within the summary, **warnings lead**: the utterance is spoken at a standstill but the
first callout after the aircraft rolls still cuts the tail. And the lead sentence names the
destination only when the destination actually seated — it used to open *"SayIntentions
route to Gate A9."* and then say *"Destination not set."* two sentences later, the first
thing the pilot hears contradicting the second.

### Which airport the import resolves against

`ResolveSayIntentionsAirport` tries `context.CurrentAirport`, then `context.Origin`,
then `context.Destination`, in that order, through `SelectImportAirport` — the first
candidate the navigation **database** actually knows wins, checked via `AirportExists`
(the lightest single-row lookup on `IAirportDataProvider`; never `GetTaxiPaths`, which
returns thousands of rows, and never a graph build just to validate an ident). A
candidate skipped for not being in the database is logged at **Debug**
(`Import airport candidate 'KZOA' is not in the navigation database; trying the
next.`) rather than Info — it is normal traffic on a cruise-phase read, not a problem.
Only once every candidate has been tried and rejected does `ResolveSayIntentionsAirport`
fall back to the nearest airport by position, exactly as it did before this validation
existed.

This is the fix behind the ARTCC-facility finding above: preferring `current_airport`
unvalidated dead-ended the import on a controlling-center ident with *"No taxi path
data available for KZOA"* — correctly, since there is no taxi path data for an ARTCC,
but not what the pilot asked for. `KnownAirport`, the small wrapper `MainForm` passes
as the lookup, treats a missing `airportDataProvider` as "every candidate known" rather
than blocking — a missing provider must never dead-end a caller that would otherwise
work.

### One graph build per keypress

`MainForm` never builds a `TaxiGraph`. `TaxiAssistForm.LoadAirportForExternalRouteAsync`
loads the airport once and returns the taxiway names its graph knows; the clearance is
resolved against that list, and destinations resolve through
`TaxiAssistForm.TryResolveExternalDestination`, which searches the already-populated
destination combo. The form owns its own label formats — callers pass a normalized
identifier (`"15L"`, `"A9"`), never a constructed `"Runway 15L"` string.

**Which makes `_graph` the import's success signal, so a failed load must null it.**
`LoadAirportDataAsync` used to claim `_currentIcao` up front and assign `_graph` only
after its awaits, with no failure exit touching `_graph` — airport not in the database,
no taxi paths, an exception. A failed load therefore left the form holding the *previous*
airport's graph under the *new* airport's name, and the method's own early return
(`icao == _currentIcao && _graph != null`) then matched forever, so it could never
rebuild. Manually that was cosmetic. For the import it was a wrong route: taxi at LMML,
fly to an EDDF with no taxi paths, press Ctrl+Shift+Y, and `knownTaxiways` came back as
**LMML's** names, so the `Count == 0` guard never fired — the EDDF clearance resolved
against LMML taxiways, `GetLoadedTaxiwayEdges()` handed the snapper LMML pavement to snap
EDDF coordinates onto, and with auto-start on, guidance began. `_graph` is now dropped
before the first await and `_currentIcao` claimed only once a graph exists.

### The import handler must not fail silently

`BuildTaxiRouteFromSayIntentionsAsync` is dispatched as a discarded Task (`_ = …`), which
is safe **only** while nothing can throw outside its top-level try. Two guards used to sit
ahead of it, one of them `ValidateDatabaseSimulatorMatch()`, which reads SimConnect and
provider state and can open a modal dialog. Anything they threw was captured into the
discarded Task: the pilot heard nothing and `sayintentions.log` recorded nothing. The
whole body is inside the try.

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
