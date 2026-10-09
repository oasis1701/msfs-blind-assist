# SayIntentions readouts and flight data — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/sayintentions-readouts.md`, which Claude Code loads when it reads matching code. Background: [sayintentions.md](../sayintentions.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## SIR-1

- `incoming_message`/`outgoing_message` are named from SAYINTENTIONS' point of view, NOT the pilot's: incoming is what SI RECEIVED (the PILOT speaking), outgoing is what SI SENT (ATC). The intuitive reading is backwards and makes Ctrl+S announce the pilot's own readback as the controller — and makes "prefer the ATC call within a record" systematically prefer the pilot. Measured across 89 records of a live capture; don't "correct" it back. → [sayintentions.md](../sayintentions.md)

## SIR-2

- The flight-information readout (`Ctrl+Shift+S`) is a READ-ONLY WINDOW read item by item, not a spoken string — it carries the flight, the gate, and each airport's runway configuration and altimeter (SIR-3), and speaking that as one run-on gives a blind pilot no way to re-hear one part or stop it. Do NOT announce a summary when it opens: the screen reader already speaks the window and the first item (see the Screen Reader Announcements rule). The window is one list box per section, as SIR-14 says; it replaced the read-only TextBox this rule first described, whose `WordWrap`-off and caret-at-0 clauses went with it. When nothing is available, SPEAK that instead of opening an empty window. → [sayintentions.md](../sayintentions.md)

Corrected 2026-10-08: this text described the read-only TextBox that SIR-14's list boxes replaced (`WordWrap` off, caret at 0, the ATIS, METAR and TAF that SIR-3 removed); SIR-14 matches the code, so this rule now defers to it. Evidence: `Forms/SayIntentionsInfoForm` (a `DisplayListBox` per section) and `MainForm.AnnounceSayIntentionsAssignedStatusAsync` (no announcement on open; speaks instead when `SayIntentionsInfoReport.HasContent` is false).

## SIR-3

- Each airport block of the flight-information window keeps ONLY what a pilot cannot get by listening to the ATIS or opening the METAR window (`Shift+M`): the runway configuration (landing / departing / preferred / flow) and the altimeter. The window's Flight and Gate sections are separate and carry their own rows. `departure_wx` also carries the decoded ATIS, METAR, TAF, wind, visibility and density altitude — those were briefly all shown and it was wrong, because twenty lines of already-heard weather is the wall the window exists to remove. The ATIS letter is parsed but not shown; it is not runway information. Do not re-add any of it without a reason that survives that rule. → [sayintentions.md](../sayintentions.md)

Corrected 2026-10-08: scoped to each airport block; the window also has Flight and Gate sections. Evidence: `SayIntentionsInfoReport.Build`, which adds the Flight (`AddFlight`), gate (`AddGateAndRunway`) and airport (`AddAirports`) sections.

## SIR-4

- Do not use SI's `phonetic` ATIS variant: it is pre-spelt for SI's own speech synthesis and reads worse through a screen reader than the plain text. Respace runway lists (`22L,22R` → `22L, 22R`) or the reader runs the designators together, and format aviation numbers with InvariantCulture so the altimeter doesn't read `29,73` beside a METAR saying `A2973`. `callsign_icao` is NOT an ICAO callsign — a live capture had it identical to `callsign` and hyphen-spelt (`Skyhawk-One-Two-Three-Alpha-Zulu`); strip the hyphens before speaking. → [sayintentions.md](../sayintentions.md)

## SIR-5

- `flight.json` contains PERSONAL DATA — `Email`, `displayname`, `userid` in plain text. Never dump the raw file to a log, and never commit one as a fixture without redacting those plus `api_key`. → [sayintentions.md](../sayintentions.md)

## SIR-6

- The observed wire format began as ONE capture, taken stopped on the ground AT THE DESTINATION. Since then two fields are measured in the cruise — `current_airport` holds the controlling ARTCC's ident (SI-31) and `assigned_gate` appears at least 31 minutes before landing — and the taxi captures behind SI-13 and SI-17 were taken on the ground; every other field is still unobserved airborne. Do not design a further en-route readout against the captured field table. In particular: `cleared_for_takeoff`, `cleared_for_landing`, `clearance`, `last_clearance` and `taxi_clearance` were all ABSENT from it, and two of those sit in the destination-resolution chain — anything depending on them is untested against real SI. A single mid-cruise copy of the file settles the rest. → [sayintentions.md](../sayintentions.md)

Corrected 2026-10-08: no longer "ONE capture": two fields are measured in the cruise; and the sentence saying `flight_plan_route` and `callsign_icao` are never spoken is gone, since the window shows both (SIR-4 strips the callsign's hyphens). Evidence: docs/sayintentions.md, "What flight.json holds AIRBORNE is unknown"; `SayIntentionsInfoReport.AddFlight` (the Callsign and Route rows).

## SIR-7

- The departure runway is GROUND information — suppressed airborne (`_lastOnGround`) as well as at the destination. Without the air/ground gate it was the last line the status readout had left, so it repeated a stale ground fact for the whole cruise in front of the arrival gate and arrival runway. Keep BOTH gates: the destination check is what covers the aircraft back on the ground after rollout, when `onGround` is true again. → [sayintentions.md](../sayintentions.md)

## SIR-8

- The cabin-word veto is overridable ONLY by the three-keyed instruction-shape test (`IsCabinVetoOverridden`: channel not cabin, no cabin marker in the FIELDS, imperative shape in the message via `AtcInstructionVocabulary`) — a silenced ATC instruction is the failure the readout must never have, but "cleared to land"/"taxi"/"runway" are ordinary purser prose. The shape itself rests on a shared `NarrationGuard` lookbehind on every verb-initial leg plus a per-token noun-phrase blocklist inside the `TAXI…VIA` gap — never widen a verb leg without adding its matching rescue leg beside it (`CLEARED TO CROSS` beside `CROSS`, `CONTINUE TAXI` beside `CONTINUE`). A verb leg and its rescue pairing are designed TOGETHER, not by a blanket guarded/unguarded split — `CONTINUE TAXI` carries the guard WITH its own rescue semantics (it defers to the word before `CONTINUE`, not before `TAXI`), while `CLEARED TO CROSS` is deliberately UNGUARDED as `TO`'s rescue; never strip or add a guard on either without re-running the probe matrix against the residual pins. `SayIntentionsTransmissionClassifier.cs` inventories six honest residuals beside `AtcInstructionVocabulary` (lettered a-f) — read them before "fixing" a leak this file already knows about. → [sayintentions.md](../sayintentions.md)

Clarified 2026-10-09: the one-line form's "never widen a verb leg without its matching rescue leg" now says what the classifier's own summary says: where the shared guard catches a genuine ATC form, widen the guard, then add the matching rescue leg beside it, never a hole in the guard (`CLEARED TO CROSS` rescues what TO blocks, `CONTINUE TAXI` what CONTINUE blocks). Evidence: `SayIntentionsTransmissionClassifier`'s class summary.

Split out on 2026-10-09: SIR-16 (one shared `NarrationGuard` on every verb-initial leg, and the `TAXI…VIA` blocklist). This text keeps it as written; it has its own section below.

## SIR-9

- The SAPI hostname comes from a file this app does not own; validate https + the sayintentions.ai allowlist before attaching the API key, and never log the key (use `SayIntentionsEndpoint.Redact`). → [sayintentions.md](../sayintentions.md)

## SIR-10

- The flight-info window leads with the ARRIVAL airport unless the departure block is the one naming `current_airport` — never emit the two unconditionally departure-first. On an arrival that put the origin's runways and altimeter first (LMML ahead of EDDF, 1300 nm behind the aircraft), so the first altimeter the pilot arrowed onto was the wrong field's. Both blocks naming one airport print ONCE, from the arrival block, and the dedupe must key on a heading actually PRINTED — keying on the name alone lets an empty stub block swallow the one carrying the data. → [sayintentions.md](../sayintentions.md)

## SIR-11

- The altimeter renders BOTH units — `30.12 inches (1020 hPa)`, inHg at a fixed 2 decimals, hPa whole, both `InvariantCulture`. SI publishes inHg only, so an inHg-only line is wrong for every hPa field, and stripping a whole value's decimals printed "30 inches" beside "30.12 inches" in one window. Keep the word "inches", not "inHg" — the line is spoken. → [sayintentions.md](../sayintentions.md)

## SIR-12

- Ctrl+S must never return a `Pilot`-speaker transmission — a readback is normally the NEWEST thing on the frequency when the key is pressed, so ordering by timestamp announced the pilot their own words back. Preferring ATC only WITHIN a record is not enough: the readback arrives in a later record than the clearance it repeats. A transmission with NO speaker stays eligible (it comes from the bare-`message` fallback, so inferring "pilot" from an absence would be a guess, and silence is the worse failure). The taxi import reads the same HISTORY (not the same answer — see the scan-back bullet above), and repeats the drop in `SayIntentionsClearanceSelector` rather than inheriting it, because a scan-back is where it stops being obvious: the KDTW capture carries a pilot readback that is a full taxi clearance by shape. → [sayintentions.md](../sayintentions.md)

## SIR-13

- There is NO API key setting — the key comes from `flight.json` (`flight_details.api_key`) and nowhere else. A hand-entered copy could only duplicate it or go stale and override it with something wrong. Never re-add the field, and never write an error string pointing the pilot at a setting to fill in: when there is no key and nothing in the file, the honest reason is that SayIntentions is not running. → [sayintentions.md](../sayintentions.md)

## SIR-14

- The flight-information window is one LIST BOX PER SECTION, never a multiline TextBox — a list item brailles as a discrete unit and announces its position ("3 of 7"), which a text box cannot do, and the window is a lookup surface you jump around rather than a run you arrow through. Each list carries its heading as BOTH a visual `Label` and its own `AccessibleName`, and item 0 is pre-selected so tabbing in says section + first value + count in one utterance — this is NOT the old TextBox select-all it replaced, which read the whole report in one breath. `Build` returns SECTIONS; `Flatten` renders the same report as the flat headed run of lines, and every ordering rule (lead airport, one-print dedupe, altimeter formatting) is pinned through `Flatten` — keep it working. `HasContent` tests section ITEMS only, never headings. → [sayintentions.md](../sayintentions.md)

## SIR-15

- SI request caching must commit AFTER the request completes and coalesce onto the in-flight task — stamping the cache time before awaiting makes a second hotkey press during a slow request speak "no transmission available". → [sayintentions.md](../sayintentions.md)

## SIR-16

- Every VERB-initial leg of `AtcInstructionVocabulary` (HOLD SHORT, HOLD POSITION, GIVE WAY, CROSS, TAXI TO, TAXI…VIA, CONTINUE TAXI, LINE UP AND WAIT, and any new one) carries the ONE shared `NarrationGuard` lookbehind, never a per-leg guard. The verb is the same in both registers (a controller says "cross runway 27", a captain says "we will cross runway 27"), so the word in front of it is what separates them; rounds 1-3 each blocked ONE surface form with its own per-leg guard, and the next review found the modal variant still open ("we WILL cross") or a real instruction newly silenced. The `TAXI…VIA` gap also keeps its per-token noun-phrase blocklist. `SayIntentionsTransmissionClassifierTests.CabinSpeechStaysFilteredEvenWhenItSoundsOperational` catches a stripped guard on some legs only (no row for HOLD POSITION or GIVE WAY), nothing covers a new leg, and every cabin row reaching `TAXI…VIA` has "we" first, so deleting the blocklist fails no test. → [sayintentions.md](../sayintentions.md)

Split from SIR-8 on 2026-10-09: one mechanism per ID.
