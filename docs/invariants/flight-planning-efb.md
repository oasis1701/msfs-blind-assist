# Flight-planning EFB and procedure data (Shift+E) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/flight-planning-efb.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## EFB-1

- Never drop a fix-less leg in `ParseLegToWaypoint` — empty-`fix_ident` ARINC 424 legs legitimately carry SID climb-outs and missed-approach heading legs; synthesize a maneuver label instead of returning null. → [architecture.md](../architecture.md)

## EFB-2

- Resolve fix coordinates across ALL fix tables (vor/ndb/runway_end/airport), not just `waypoint`, or navaid/runway/airport fixes stay at (0,0) and corrupt distance/bearing; `approach_leg.fix_lonx`/`fix_laty` are NULL in this navdata build — do not rely on them. → [architecture.md](../architecture.md)

## EFB-3

- Circling approaches share `suffix='A'` with STARs — distinguish by presence of a missed-approach leg; `GetSTARs` must exclude them or they pollute the STAR list. → [architecture.md](../architecture.md)

## EFB-4

- "ALL"-runway SID/STAR must load the runway-INDEPENDENT procedure body (empty runway_name AND non-RW arinc_name) — keying on `runway_name` alone degenerates to a random runway transition's legs. → [architecture.md](../architecture.md)

## EFB-5

- `FlightPlanManager.AppendWaypoints` must drop the duplicate shared boundary fix between a transition and its procedure, or a spurious 0 NM leg appears. → [architecture.md](../architecture.md)

## EFB-6

- `GetRunwayDetailedInfo` must explicitly alias the `runway_end` columns (`re.heading AS end_heading`, etc.) and read them by those `end_*` names — the bare columns are ambiguous between `runway`/`runway_end`; Microsoft.Data.Sqlite resolves the duplicate to the LAST occurrence (`re`, verified by probe — not the primary-end value once assumed), but the code must not rely on that positional accident. → [architecture.md](../architecture.md)

## EFB-7

- Minimums (DA/MDA/visibility) are not present in navdata at all (not CIFP, MSFS, or Navigraph DFD) — this is a data-source limitation, not a fixable bug; don't try to source them from the DB. → [architecture.md](../architecture.md)

## EFB-8

- The Cold Temperature Correction math must be transcribed VERBATIM from FlyByWire's EUROCONTROL formula (including the redundant term and round-up-to-10ft) and must never correct a published altitude downward on a warm temperature. → [taxi-guidance.md](../taxi-guidance.md)

## EFB-9

- Never query the `ils` table by `ident` alone — always scope by airport (+runway); anything unscoped must go through `GetILSForRunway` so it is spatially validated (498 idents are shared across airports and roughly 200 fs2024 rows are orphaned join columns — `OrphanIlsMatcher` states the measured count and its date). → [pmdg-efb.md](../pmdg-efb.md)

## EFB-10

- An ORPHANED fs2024 `ils` row (join columns NULL) is re-linked to a runway by `OrphanIlsMatcher`: nearest the runway's **CENTERLINE**, and only when no other runway end at the airport is nearer to it. NEVER “simplify” that back to the nearest antenna to the THRESHOLD on the reasoning that a localizer sits beyond the far end so the closest must be this runway's — that was the original rule, the reasoning is false at every parallel-runway airport (straight-line range is dominated by the ~3 km along-track term and barely sees the lateral offset that separates parallels), and it mis-assigned 46 of 230 runway ends across fs2024. Live KATL, Orbx scenery (blank `ils_ident` on all ten ends, so every runway takes this path): 08L's own localizer is 3,027 m from its threshold and 09R's is 3,011 m, so a SIXTEEN-METRE margin gave 08L, 08R and 09L all the same 108.90 MHz / 090 course. The MUTUAL-BEST half is not redundant with cross-track — without it a closely-spaced parallel with no localizer of its own borrows its neighbour's (KPHX 25L/25R, 246 m apart); a runway with none must report NONE, because a wrong ILS frequency read to a blind pilot is worse than no frequency. `MaxCrossTrackMetres` (300 m) is a backstop for the uncontested absurd (VVLO 9,971 m off centerline), NOT the discriminator — the whole-database result is identical from 250 m to 1,000 m, so never tune it to chase a mismatch; the widest legitimate offset measured is 216 m (BIAR 19). A candidate must also lie AHEAD of the threshold and within `LengthMetres + MaxLocalizerSetbackMetres` (3,000 m) of it — the centerline is extended forward without limit, so an aligned antenna far downfield (a second airport on the same bearing) is otherwise admitted on cross-track alone, which the cross-track backstop cannot catch BECAUSE the two runways are collinear; the ceiling is keyed to the runway's OWN length rather than a second tuned distance, `ils.range` cannot serve (18 NM ≈ 33 km, wider than the bounding box, so it constrains nothing), the widest legitimate setback measured is 1,650 m (UBFI 11) with the whole-database result identical from 2,000 m to 4,000 m, and an end whose length is unknown (0) SKIPS the ceiling rather than risk dropping a real ILS. ⚠️ The mutual-best competitor set is the requested airport's ends ONLY, and widening it to every airport in the bounding box was implemented, MEASURED and REJECTED — do not "fix" that gap without a real case in hand: the 32 localizers currently accepted by ends at two idents are all DUPLICATE AIRPORT RECORDS for one physical field (UZTT/UTTT, UZSS/UTSS, OJMS/OJ40, FVBU/FVJN, FNLF/FNBJ, ORSJ/ORSU, VAJA/VEDO…) where both records describe the same runway and both must keep it, so making them compete hands it to whichever record is marginally closer and leaves the other reporting NO ILS (UTTT 08L at 2.4 m loses to UZTT 08L at 0.1 m) — 32 measured regressions to close a gap with zero observed instances. The ORPHAN COUNT is stated in exactly one place, `OrphanIlsMatcher`'s class comment (roughly 200; 217 measured in a 2026-08-30 fs2024 build), because it had drifted to four different values across the tree — it varies with installed scenery, so it is a dated measurement and never an invariant. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: the one-line form now names the unknown-length exception this text already gave. Evidence: `OrphanIlsMatcher.SelectBest`, whose along-track ceiling is `double.MaxValue` when the end's `LengthMetres` is 0, pinned by `OrphanIlsMatcherTests.Unknown_runway_length_disables_the_along_track_ceiling`.

## Background: the former CLAUDE.md section

This section stood in CLAUDE.md's core until 2026-10. Kept here word for word.

### Flight-Planning EFB & Instrument-Procedure Data (Shift+E)

**Feature:** `Forms/ElectronicFlightBagForm.cs` — the screen-reader flight-plan builder (Departure / SID / STAR / Arrival / Approach / Airport Lookup tabs) + SimBrief import + Gemini route description. Procedure data comes from `Database/NavigationDatabaseProvider.cs` (the navdatareader `approach` / `approach_leg` / `transition` / `transition_leg` tables — SIDs are `suffix='D'`, STARs `suffix='A'`, approaches the rest), assembled by `Navigation/FlightPlanManager.cs`. **Navigraph navdata, when present in the user's DB, is used here automatically** — there is ONE merged DB read by every provider; nothing branches on data source.

**Key rules when touching this code (invariants behind the 2026-06 bugfix pass):**
- **Never drop a fix-less leg.** ARINC 424 path/terminator legs `CA`/`VA`/`CI`/`VI`/`VM`/`FM`/`CD`/`VD`/`CR`/`VR` legitimately have an empty `fix_ident` (~14 % of legs) — they carry the **initial climb of most SIDs and the heading legs of most missed approaches**. `ParseLegToWaypoint` must NOT `return null` on empty `fix_ident`; it synthesizes a readable maneuver label via `BuildManeuverLabel` (e.g. *"Climb heading 071° to 600 feet"*) from the leg `type` + course + altitude. The leg's path/terminator code lives in `approach_leg.type` (read into `WaypointFix.Type`), NOT `arinc_descr_code` (which this DB only fills with A/F/M/B fix-role letters).
- **Resolve fix coordinates across all fix tables, not just `waypoint`.** Navaid (`fix_type='V'`/`'N'`), runway-threshold (`'R'`, idents like `RW06L`) and airport (`'A'`) fixes are NOT in the `waypoint` table → a waypoint-only lookup left them at (0,0) and corrupted distance/bearing. `ResolveFixCoordinates` falls back to `vor`/`ndb`/`runway_end`/`airport` by fix_type. `approach_leg.fix_lonx`/`fix_laty` are NULL in this navdata build — do NOT rely on them. `FlightPlanManager.UpdateAircraftPosition` skips distance/bearing for any waypoint still at (0,0) — maneuver legs have no position.
- **Circling approaches (VOR-A, NDB-A…) share `suffix='A'` with STARs.** Distinguish them by **the presence of a missed-approach leg** (`EXISTS … approach_leg.is_missed=1`): `GetApproaches` INCLUDES suffix-A rows that have a missed leg (they're circling approaches); `GetSTARs`/`GetSTARsForRunway` EXCLUDE them. Without this, circling approaches were missing from the Approach list and polluting the STAR list.
- **"ALL"-runway SID/STAR loads the runway-INDEPENDENT body**, not an arbitrary runway's legs. The `GetSIDsForRunway`/`GetSTARsForRunway` "ALL" branch picks, per procedure name, the `approach_id` that is runway-independent under BOTH columns — empty `runway_name` AND a non-`RW…` `arinc_name` — then runway_name-less rows, then `MIN(approach_id)`. Keying on `runway_name` alone degenerated back to a random runway transition's legs at arinc-tagged airports (OMDB: `runway_name` NULL on every row).
- **Transition + procedure share their boundary fix** — `FlightPlanManager.AppendWaypoints` drops the duplicate first/last fix when concatenating, else it appears twice with a spurious 0 NM leg.
- **Airport Lookup**: the runway list needs a `SelectedIndexChanged` handler (selecting a runway must repopulate the runway-info box; it's not Load-button-only), and `GetRunwayDetailedInfo`'s `SELECT r.*, re.*` aliases the runway-END columns (`re.heading AS end_heading`, `end_altitude`/`end_lonx`/`end_laty`) and reads them by those explicit `end_*` names — the bare `heading`/`altitude`/`lonx`/`laty` are ambiguous between `runway` and `runway_end`. Microsoft.Data.Sqlite resolves such a duplicate to the LAST occurrence (`re`, the runway-end — verified by probe, NOT the first/`r` value earlier assumed), but the code must not depend on that positional accident; the explicit aliases keep the read unambiguous and provider-independent. Dropping an alias makes `reader["end_heading"]` throw "no such column" (the failure `RunwayInfoAliasingTests` pins), not silently return a center value.
- **Minimums (DA/MDA/visibility) are NOT in navdata** — not in CIFP, MSFS, or even Navigraph DFD; they live only on the visual plate. The EFB cannot show them; this is a data-source fact, not a fixable bug.
