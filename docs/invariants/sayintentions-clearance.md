# SayIntentions clearance parsing — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/sayintentions-clearance.md`, which Claude Code loads when it reads matching code. Background: [sayintentions.md](../sayintentions.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## SIC-1

- Destination-runway extraction must run against text with every hold-short/crossing span MASKED — a leftmost `Regex.Match` makes the runway a pilot was told to hold short OF become the taxi destination, routing them at an active runway. → [sayintentions.md](../sayintentions.md)

## SIC-2

- The mask and the hold-short capture must keep sharing the ONE `HoldPrefix` const — never split them back into two regexes. Spelled separately they drifted: crossings and bare "hold short" were handled but a pilot readback ("holding short of runway 15", exactly what SI publishes as the newest transmission) still made 15 the destination. It must cover holding short / hold-short / hold short of the / remain short of / ICAO "holding point". → [sayintentions.md](../sayintentions.md)

## SIC-3

- The "fall back to the frequency" path must stay gated on `LooksLikeTaxiClearance` — otherwise a landing clearance heard on rollout becomes a taxi route. The shape test accepts TAXI **or** a bare VIA (abbreviated clearances omit the verb), so the exclusion list is what carries the weight: a live KBOS capture showed CLEARANCE DELIVERY passing on its "via" alone (*"Cleared to Miami via the SSOXS7 departure…"*), building a shortest-path route to the departure runway and announcing it as a SayIntentions route with nothing to reveal where it came from. The pilot's READBACK of the same clearance is published as a transmission too. Excluded on `cleared to land` / `climb and maintain` / `squawk NNNN` / `as filed` — each belongs to clearance delivery and to nothing a ground controller says while taxiing you. → [sayintentions.md](../sayintentions.md)

## SIC-4

- The taxiway scan must NOT truncate at `cross`/`then` — a clearance legitimately continues and reuses taxiways across a runway crossing (KBOS pattern); crossings are masked, and only a real terminator (contact/monitor/squawk/remain/report/give way/follow/**information**/caution/traffic/expect) ends the route. `information` must stay in that list: the ATIS letter is spoken phonetically ("advise you have information Sierra") and otherwise appends a real taxiway S to the route or reports a false missing one. `caution`/`traffic`/`expect` must stay too — SI appends advisory tails to a clearance, and a phonetic word inside one ("caution golf cart crossing") became a route leg the controller never cleared. → [sayintentions.md](../sayintentions.md)

## SIC-5

- Taxiway literal alternatives are matched CASE-SENSITIVELY while their NATO words are not — that asymmetry is the only thing stopping the article "a" reading as taxiway A and "at" as taxiway AT. Never add `RegexOptions.IgnoreCase` to `BuildTaxiwayPattern`'s output. (The separate unresolved-token pattern DOES use IgnoreCase, and must: it matches whole NATO words only, with no bare-designator branch.) → [sayintentions.md](../sayintentions.md)

## SIC-6

- Taxiway patterns must carry spoken DIGITS as well as letters — without them "Bravo Four" decayed to taxiway B, a real taxiway at most airports, so the wrong route was delivered with full confidence and never reported. → [sayintentions.md](../sayintentions.md)

## SIC-7

- Never resolve a destination or taxiway by substring `Contains` — a one-character name matches almost any combo item, including "(None - calculate shortest path)". Exact, normalized comparison only. → [sayintentions.md](../sayintentions.md)

## SIC-8

- BOTH kinds of lost taxiway must be NAMED in the announcement of a CLEARANCE-sourced route — one the dialog could not seat AND one this airport does not have. `ParseTaxiways` returns only names the graph knows, so the second kind can only come from `ScanTaxiways`'s `Unresolved` half; reporting just the first leaves a pilot hearing a shorter route than ATC cleared with no way to see it. Keep `ParseTaxiways`'s signature working — it has callers and tests. A GEOMETRY-sourced route deliberately drops the second kind (see the geometry bullets below), and the ACCEPTED COST of that exception is real: on a geometry route a taxiway the clearance named that the airport genuinely does not have is never spoken at all, only logged (`notAtAirport=[…]` in sayintentions.log). It is the better failure — the alternative is a false "could not apply North" over a route that does include N, which teaches the pilot to distrust the whole readout — but it is a trade, not a free win, so do not "restore" the missing line and do not remove the log field that is now the only record of it. → [sayintentions.md](../sayintentions.md)

## SIC-9

- The clearance is the NEWEST transmission that IS a taxi clearance, scanned newest-first — never merely the newest transmission tested for shape. KDTW, live: Ground said *"cross-runway 4R, then continue taxi via K, Q"* and added *"hold short of runway 4R, 737 on the runway"* 4 s later; the advisory was correctly rejected, nothing looked one message back, and the import took an unchecked ground track down taxiways already behind the aircraft. FOUR bounds on the scan, all load-bearing: never a `Pilot` transmission (the same capture carries the pilot's readback of the ORIGINAL clearance, which a speaker-blind scan would resurrect); records for THIS airport only by `ident` (the feed spans the flight — KDTW still held Memphis's *"Runway 36L taxi via P2, T, M, M1"* from 2.5 h earlier — while a record with NO ident stays eligible, because flight.json publishes none and treating absence as a mismatch retires that path); and within 30 minutes of the newest transmission (judgement, sized on the one capture: the needed clearance sat 4 s back, the still-in-force original 13 min 57 s back). BOTH clearance-text sources go through the one `SayIntentionsClearanceSelector`. **Ctrl+S must NOT change** — it answers "what was just said", the import asks "where was I told to taxi", and at KDTW those have different answers 4 s apart; the whole history is cached instead of only its newest entry precisely because one cached answer cannot serve both. Between eligible transmissions, one with ROUTE CONTENT (via-list/runway/gate — `HasRouteContent`) outranks a newer bare "continue taxi"; the contentless advisory is still returned when it is all there is. → [sayintentions.md](../sayintentions.md)

## SIC-10

- The hold-short/crossing mask must accept a HYPHEN before the runway, not just whitespace — SayIntentions published *"cross-runway 4R…"* at KDTW, `\s+` never matched it, and the unmasked leftmost "runway 4R" became the taxi DESTINATION: an aircraft routed at the active runway it had just been cleared to cross. The separator lives in the ONE `PrefixToRunway` const shared by the mask and the capture, for the same reason `HoldPrefix` is shared. The prefix binds a LIST of runway tokens (`RunwayList`, two-parameter — a WRITTEN-only tail via `WrittenRunwayTailToken`, at most two digits: "28L and runway 28R", plural "runways 4L and 4R"); a second crossed runway left unmasked became the DESTINATION on a gate-bound clearance. A SPOKEN multi-runway list only masks its first runway — a documented residual, and the safe direction, since a missed extension can only under-mask. A plural hold-short yields one hold-short per runway. → [sayintentions.md](../sayintentions.md)

## SIC-11

- Every IgnoreCase regex in the SayIntentions integration MUST carry `RegexOptions.CultureInvariant` — tr-TR folds the pattern letter I to dotless ı, and `\b(?:TAXI|VIA)\b` matching nothing killed the whole import for Turkish-locale users (`SayIntentionsCultureTests` pins it). The dynamic taxiway patterns get `CultureInvariant` WITHOUT `IgnoreCase` — the literal branch's case-sensitivity is the documented asymmetry. → [sayintentions.md](../sayintentions.md)

## SIC-12

- SI never instructs a hold short of a TAXIWAY (owner-confirmed 2026-08-03) — the hold/cross prefixes deliberately bind runway tokens only, and a taxiway hold-short phrase would read as a route leg today. Revisit ONLY if SI adds the phrasing, and fix it in the shared consts, never a second copy. → [sayintentions.md](../sayintentions.md)

## SIC-13

- The comms history is OWN-AIRCRAFT-ONLY (owner-relayed 2026-08-03) — never add a callsign gate to the clearance selector; it could only exclude our own records, and `callsign_icao` is the hyphenated speech form anyway. → [sayintentions.md](../sayintentions.md)

## SIC-14

- The runway side binds TIGHTLY in `RunwayToken` (`\s?`, word-bounded side words, `[LCR]` with no letter/digit after) — with `\s*` and bare `[LCR]`, "runway 22 remain this frequency" made the destination 22R off the r of "remain", including across a blanked mask span. → [sayintentions.md](../sayintentions.md)

## SIC-15

- Unresolved-taxiway detection must stay PHONETIC-ONLY (whole NATO words, optional digit) — never widen it to bare uppercase designators, which false-positive on ordinary abbreviations. A false "could not apply K" teaches the pilot to distrust the whole announcement; a miss is the better failure. → [sayintentions.md](../sayintentions.md)

## SIC-16

- Both quiet-guards on that scan are load-bearing: skip a phonetic word OVERLAPPING a resolved name (an airport can have AT without A or T, and both words of "Alpha-Tango" sit inside it), and skip a token whose designator IS a known taxiway (`BuildTaxiwayPattern` has no phonetic branch for a spaced name, so "Bravo Four" can't match a graph spelling it "B 4" — a matching gap, not a missing taxiway). → [sayintentions.md](../sayintentions.md)

## SIC-17

- Each hold-short must be tied to the taxiway it FOLLOWS, never the last one in the clearance — that put the stop at the wrong crossing and dropped every hold-short after the first. Cut the clearance on the parser's OWN mask (never a second copy of the phrasing), keep a taxiway repeated across a hold-short so each gets its own row, and map a name the applied sequence lacks to -1 so it is reported rather than hung on whatever row is last. → [sayintentions.md](../sayintentions.md)

## SIC-18

- `NormalizeParkingName` must only strip a SPACED-dash descriptor ("A9 - Terminal 1") — a bare hyphen is part of the stand name ("A-9") and splitting on it matches the wrong spot. The gate CAPTURE must admit that hyphen too; stopping at the bare letter routed the pilot to stand "A", or fell through to the departure RUNWAY as the destination. → [sayintentions.md](../sayintentions.md)

## SIC-19

- A leading zero in a stand number is PADDING, not identity — `NormalizeParkingName` must strip it (`(?<![0-9])0+(?=[0-9])`, applied last), or SI's live EDDB "Gate B06" never equals navdata's "B 6" under `MatchDestinationLabel`'s exact compare, the assigned gate cannot resolve, and destination resolution falls through its WHOLE chain to the ARRIVAL RUNWAY: a just-landed aircraft routed at 24L along the very taxiways cleared for the gate, with the taxiway half of the import perfect so everything else sounded right. BOTH regex guards are load-bearing — without the lookbehind "100" loses its middle zero and reads as stand 10, and `B10` must NEVER collapse to `B1`, the same wrong-stand failure pointed the other way. The RUNWAY half already tolerated this padding via `CleanRunway` ("05L" vs "5L"); only the gate half did not. → [sayintentions.md](../sayintentions.md)

## SIC-20

- SayIntentions renders a single-letter taxiway as its COMPASS WORD, not only as the NATO one — a live LEPA clearance ("via LE, E, North, H2") meant taxiway N, and both halves missed it: the pattern stopped at the "orth" and the phonetic-only scan could not report it. NORTH/SOUTH/EAST/WEST/CENTER/CENTRE are spoken forms of N/S/E/W/C and must stay merged into the ONE spoken-forms table (`SpokenForms`) feeding BOTH `BuildTaxiwayPattern` and the unresolved scan — wiring only the matcher leaves a missing leg unreported; wiring only the scan reports a taxiway the airport does have. → [sayintentions.md](../sayintentions.md)

## SIC-21

- A compass word is the one piece of ordinary English in that table, so it alone carries `IsDirectionProse` — a direction when `the` or a runway number leads into it, or when the very next word is English rather than the next designator ("taxi north on Bravo", "the north side", "runway 24 Center"). Apply it to BOTH scans from the one helper: the failures are mirror images (an airport WITH the letter silently gains a leg ATC never cleared; one without it announces a false "could not apply North"), so guarding one scan and not the other makes the announcement contradict itself between airports. Never make capitalization the signal — SI's text is generated — and never widen the guard to NATO words. → [sayintentions.md](../sayintentions.md)

## SIC-22

- The "next word" that guard inspects must stay bounded to three separators: a blanked-out hold-short span is twenty-odd spaces, and reaching across it makes the last taxiway of an ordinary "…, November hold short of runway 06L for landing traffic" read as prose and vanish. → [sayintentions.md](../sayintentions.md)

## SIC-23

- There are TWO "fall back to the frequency" paths and BOTH must pass `LooksLikeTaxiClearance`: the live `getCommsHistory` read in MainForm AND `SayIntentionsService.ReadFlightContext`'s assignment of `ClearanceText` from flight.json's own transmissions. Only the first was gated, and the second takes precedence — the MainForm site runs only when `ClearanceText` is ALREADY empty, so the shape test never saw the file's transmission. On rollout that is the LANDING clearance: it became the clearance text and `ParseDestinationRunway` routed the just-landed aircraft AT the runway it had landed on. Never gate one without the other — which is now structural: both go through the one `SayIntentionsClearanceSelector`, so neither site has a gate of its own to forget. → [sayintentions.md](../sayintentions.md)
