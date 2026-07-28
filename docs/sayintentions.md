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
position, so it requires a connected sim.

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

By default the dialog opens with everything pre-filled so you can review it, then you
press **Calculate Route** to start guidance. Enable **Start taxi guidance immediately**
in Settings → SayIntentions to skip the review step.

Any taxiway from the clearance that does not exist at the airport is named aloud
("Could not apply Kilo") rather than dropped silently — the route is still built from
whatever did match.

## Settings

**Settings → SayIntentions** holds two options:

- **API key** — optional. Leave it blank and the integration uses the key
  SayIntentions publishes in `flight.json` during an active flight. Set it explicitly
  if you want comms history and parking lookups to work in other situations.
- **Start taxi guidance immediately** — off by default (see above).

## Troubleshooting

Diagnostics are written to `%APPDATA%\MSFSBlindAssist\logs\sayintentions.log`. It
records which fields were found in `flight.json`, the destination and taxiways that
were resolved, and anything that was skipped. API keys are never written to the log.

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

The same masking is why the taxiway scan does **not** truncate at `cross`/`then`: a
clearance legitimately continues, and reuses taxiways, across a runway crossing (the
KBOS pattern in [taxi-guidance.md](taxi-guidance.md)). It stops only at a genuine
terminator — `contact`, `monitor`, `squawk`, `remain`, `report`, `give way`, `follow`.

### Taxiway matching case asymmetry

`BuildTaxiwayPattern` emits `(?:A|(?i:ALPHA))` per character: the literal designator
matches **case-sensitively** (uppercase only) while the NATO word does not. That
asymmetry is the only thing stopping the English article "a" being read as taxiway A,
and the preposition "at" as taxiway AT. Callers must never pass
`RegexOptions.IgnoreCase` to this pattern.

Overlapping candidates resolve longest-first, so "Alpha-Tango" reads as `AT` rather
than `A` followed by `T`.

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
