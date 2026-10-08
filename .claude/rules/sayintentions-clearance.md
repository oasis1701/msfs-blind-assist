---
paths:
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsClearance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsClearance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsLiveClearance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsCulture*.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs"
---
# SayIntentions clearance parsing rules

Loaded when Claude reads matching code. Background: docs/sayintentions.md. Full text of each rule: docs/invariants/sayintentions-clearance.md.

- [SIC-1] Extract the destination runway only from text with every hold-short/crossing span MASKED: a leftmost `Regex.Match` turns the runway a pilot must hold short OF into the taxi destination, routing them at an active runway. Full: docs/invariants/sayintentions-clearance.md#sic-1
- [SIC-2] The mask and the hold-short capture must share the ONE `HoldPrefix` const, never two regexes (they drift); it must cover holding short, hold-short, hold short of the, remain short of and ICAO "holding point". Full: docs/invariants/sayintentions-clearance.md#sic-2
- [SIC-3] The fall-back-to-the-frequency path must stay gated on `LooksLikeTaxiClearance`; since TAXI or a bare VIA passes the shape test, the exclusions carry the weight: `cleared to land`, `climb and maintain`, `as filed`. Never add squawk: it ends real taxi clearances ([SIC-4]). Full: docs/invariants/sayintentions-clearance.md#sic-3
- [SIC-4] The taxiway scan must NOT truncate at `cross`/`then`; only a real terminator ends the route (contact, monitor, squawk, remain, report, give way, follow, information, caution, traffic, expect), and the last four must stay. Full: docs/invariants/sayintentions-clearance.md#sic-4
- [SIC-5] Taxiway literals match CASE-SENSITIVELY, NATO words do not: never add `RegexOptions.IgnoreCase` to `BuildTaxiwayPattern`'s output, or "a" reads as taxiway A. (The unresolved-token pattern does use IgnoreCase, and must.) Full: docs/invariants/sayintentions-clearance.md#sic-5
- [SIC-6] Taxiway patterns must carry spoken DIGITS as well as letters, or "Bravo Four" decays to taxiway B and the wrong route is delivered with full confidence and never reported. Full: docs/invariants/sayintentions-clearance.md#sic-6
- [SIC-7] Never resolve a destination or taxiway by substring `Contains` (a one-character name matches almost any combo item, even "(None - calculate shortest path)"); exact, normalized comparison only. Full: docs/invariants/sayintentions-clearance.md#sic-7
- [SIC-8] A CLEARANCE route's announcement must NAME both lost kinds: a taxiway the dialog could not seat AND one the airport lacks (`ScanTaxiways`'s `Unresolved`); a GEOMETRY route drops the second on purpose: never restore it, keep its `notAtAirport=[…]` log. Full: docs/invariants/sayintentions-clearance.md#sic-8
- [SIC-9] The clearance is the NEWEST transmission that IS a taxi clearance, via `SayIntentionsClearanceSelector`: never `Pilot`, this airport's `ident` only (no ident stays eligible), within 30 minutes; Ctrl+S must NOT change. (more: see full) Full: docs/invariants/sayintentions-clearance.md#sic-9
- [SIC-10] The hold-short/crossing mask must accept a HYPHEN before the runway ("cross-runway 4R") through the ONE shared `PrefixToRunway` const, and bind a LIST of runways (`RunwayList`), or an unmasked crossed runway becomes the destination. Full: docs/invariants/sayintentions-clearance.md#sic-10
- [SIC-11] Every IgnoreCase regex in the SayIntentions integration MUST carry `RegexOptions.CultureInvariant` (tr-TR's dotless ı killed the import); the dynamic taxiway patterns get `CultureInvariant` WITHOUT `IgnoreCase`. Full: docs/invariants/sayintentions-clearance.md#sic-11
- [SIC-12] SI never instructs a hold short of a TAXIWAY, so the hold/cross prefixes bind runway tokens only; revisit ONLY if SI adds the phrasing, and then in the shared consts, never a second copy. Full: docs/invariants/sayintentions-clearance.md#sic-12
- [SIC-13] The comms history is OWN-AIRCRAFT-ONLY: never add a callsign gate to the clearance selector; it could only exclude our own records, and `callsign_icao` is the hyphenated speech form anyway. Full: docs/invariants/sayintentions-clearance.md#sic-13
- [SIC-14] The runway side binds TIGHTLY in `RunwayToken` (`\s?`, word-bounded side words, `[LCR]` with no letter/digit after); with `\s*` and bare `[LCR]`, "runway 22 remain this frequency" became 22R. Full: docs/invariants/sayintentions-clearance.md#sic-14
- [SIC-15] Unresolved-taxiway detection must stay PHONETIC-ONLY (whole NATO words, optional digit); never widen it to bare uppercase designators: a false "could not apply K" is worse than a miss. Full: docs/invariants/sayintentions-clearance.md#sic-15
- [SIC-16] Both quiet-guards on the unresolved scan are load-bearing: skip a phonetic word OVERLAPPING a resolved name, and skip a token whose designator IS a known taxiway (a matching gap, not a missing taxiway). Full: docs/invariants/sayintentions-clearance.md#sic-16
- [SIC-17] Tie each hold-short to the taxiway it FOLLOWS, never the clearance's last; cut on the parser's OWN mask, keep a taxiway repeated across a hold-short as its own row, and map a name the sequence lacks to -1 so it is reported. Full: docs/invariants/sayintentions-clearance.md#sic-17
- [SIC-18] `NormalizeParkingName` must strip only a SPACED-dash descriptor ("A9 - Terminal 1"): a bare hyphen is part of the stand name ("A-9"), and the gate CAPTURE must admit that hyphen too. Full: docs/invariants/sayintentions-clearance.md#sic-18
- [SIC-19] `NormalizeParkingName` must strip a stand number's leading zero (`(?<![0-9])0+(?=[0-9])`, applied last) so "B06" matches "B 6"; both guards are load-bearing: "100" must not become 10, nor `B10` collapse to `B1`. Full: docs/invariants/sayintentions-clearance.md#sic-19
- [SIC-20] NORTH/SOUTH/EAST/WEST/CENTER/CENTRE are spoken forms of N/S/E/W/C and must stay in the ONE `SpokenForms` table feeding BOTH `BuildTaxiwayPattern` and the unresolved scan; wiring only one half breaks the other. Full: docs/invariants/sayintentions-clearance.md#sic-20
- [SIC-21] Only compass words carry `IsDirectionProse` (after `the` or a runway number, or followed by an English word); apply it to BOTH scans from the one helper, never key it on capitalization, and never widen it to NATO words. Full: docs/invariants/sayintentions-clearance.md#sic-21
- [SIC-22] The "next word" the `IsDirectionProse` guard inspects must stay bounded to three separators: reaching across a blanked hold-short span makes a clearance's last taxiway read as prose and vanish. Full: docs/invariants/sayintentions-clearance.md#sic-22
- [SIC-23] BOTH fall-back-to-the-frequency paths (the live `getCommsHistory` read in `SayIntentionsService.GetLastTaxiClearanceAsync` AND `SayIntentionsService.ReadFlightContext`'s `ClearanceText`) must pass `LooksLikeTaxiClearance`, through the one `SayIntentionsClearanceSelector`. Full: docs/invariants/sayintentions-clearance.md#sic-23
