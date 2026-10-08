---
paths:
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsInfoReport.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsEndpoint.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsTransmissionClassifier.cs"
  - "MSFSBlindAssist/Forms/SayIntentionsInfoForm.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsClearanceSelector.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsModels.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsInfoReport*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsEndpoint*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsTransmissionClassifier*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsFlightContext*.cs"
---
# SayIntentions readouts and flight data rules

Loaded when Claude reads matching code. Background: docs/sayintentions.md. Full text of each rule: docs/invariants/sayintentions-readouts.md.

- [SIR-1] `incoming_message`/`outgoing_message` are named from SAYINTENTIONS' point of view: incoming is the PILOT, outgoing is ATC; don't "correct" it back, or Ctrl+S announces the pilot's readback as the controller. Full: docs/invariants/sayintentions-readouts.md#sir-1
- [SIR-2] The flight-information readout (`Ctrl+Shift+S`) is a READ-ONLY WINDOW of list boxes ([SIR-14]), not a spoken string: announce no summary on open, and when nothing is available SPEAK that rather than open it empty. Full: docs/invariants/sayintentions-readouts.md#sir-2
- [SIR-3] Each airport block of the flight-information window keeps ONLY the runway configuration (landing/departing/preferred/flow) and the altimeter (Flight and Gate are their own sections); never re-add ATIS, METAR, TAF, wind, visibility, density altitude or the ATIS letter without a reason that survives that rule. Full: docs/invariants/sayintentions-readouts.md#sir-3
- [SIR-4] Never use SI's `phonetic` ATIS variant; respace runway lists (`22L, 22R`), format aviation numbers with InvariantCulture, and strip the hyphens from `callsign_icao` (not an ICAO callsign) before speaking. Full: docs/invariants/sayintentions-readouts.md#sir-4
- [SIR-5] `flight.json` contains PERSONAL DATA (`Email`, `displayname`, `userid`): never dump the raw file to a log, and never commit one as a fixture without redacting those plus `api_key`. Full: docs/invariants/sayintentions-readouts.md#sir-5
- [SIR-6] `flight.json`'s field table rests on a few ground captures plus two fields measured in cruise (`current_airport`, [SI-31]; `assigned_gate`): never design a further en-route readout against the rest; `cleared_for_*`, `clearance`, `last_clearance` and `taxi_clearance` were absent, so anything depending on them is untested. Full: docs/invariants/sayintentions-readouts.md#sir-6
- [SIR-7] The departure runway is GROUND information: keep BOTH gates, suppressed airborne (`_lastOnGround`) AND at the destination, or it repeats a stale ground fact for the whole cruise. Full: docs/invariants/sayintentions-readouts.md#sir-7
- [SIR-8] The cabin-word veto is overridable ONLY by the three-keyed `IsCabinVetoOverridden` test; never widen a verb leg of `AtcInstructionVocabulary` without its matching rescue leg, and read the six residuals (a-f) before "fixing" a leak. Full: docs/invariants/sayintentions-readouts.md#sir-8
- [SIR-9] The SAPI hostname comes from a file this app does not own: validate https + the sayintentions.ai allowlist before attaching the API key, and never log the key (use `SayIntentionsEndpoint.Redact`). Full: docs/invariants/sayintentions-readouts.md#sir-9
- [SIR-10] The flight-info window leads with the ARRIVAL airport unless the departure block names `current_airport`, never departure-first unconditionally; one airport in both blocks prints ONCE, deduped on a heading actually PRINTED. Full: docs/invariants/sayintentions-readouts.md#sir-10
- [SIR-11] The altimeter renders BOTH units, `30.12 inches (1020 hPa)`: inHg at a fixed 2 decimals, hPa whole, both `InvariantCulture`; keep the word "inches", not "inHg", because the line is spoken. Full: docs/invariants/sayintentions-readouts.md#sir-11
- [SIR-12] Ctrl+S must never return a `Pilot`-speaker transmission (a no-speaker one stays eligible), and `SayIntentionsClearanceSelector` repeats that drop itself rather than inheriting it. Full: docs/invariants/sayintentions-readouts.md#sir-12
- [SIR-13] There is NO API key setting: the key comes from `flight.json` (`flight_details.api_key`) only; never re-add the field, and never write an error pointing the pilot at a setting (the honest reason is that SayIntentions is not running). Full: docs/invariants/sayintentions-readouts.md#sir-13
- [SIR-14] The flight-information window is one LIST BOX PER SECTION, never a multiline TextBox, each heading both a `Label` and the list's `AccessibleName`, item 0 pre-selected; keep `Flatten` working, and `HasContent` tests items only. Full: docs/invariants/sayintentions-readouts.md#sir-14
- [SIR-15] SI request caching must commit AFTER the request completes and coalesce onto the in-flight task; stamping the cache time before awaiting makes a second press speak "no transmission available". Full: docs/invariants/sayintentions-readouts.md#sir-15
