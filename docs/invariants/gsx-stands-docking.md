# Stands, gate lists and docking guidance — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/gsx-stands-docking.md`, which Claude Code loads when it reads matching code. Background: [gsx.md](../gsx.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## DCK-1

- Domain boundary with PR #84: this codebase owns POSITIONING only (gate/stand selection, docking geometry, deice positioning) — never add live service-state logic here, and docking must never read service vars. → [gsx.md](../gsx.md)

## DCK-2

- `GsxNavdataMerger` must never cross-concourse-borrow coordinates — a navdata candidate may only donate coordinates when its normalized concourse matches the GSX gate's; otherwise drop the spot (a mislabeled coordinate on another pier is worse than omission). → [gsx.md](../gsx.md)

## DCK-3

- GSX gate spot-position priority is `this_parking_pos` → navdata → stop position as LAST resort — the GSX stop position is a VDGS nose-stop reference, not an aircraft-datum location; using it as the spot position teleports the datum into the stand. → [gsx.md](../gsx.md)

## DCK-4

- The `.py` per-aircraft stop offset must apply to ALL non-deice gates including `.ini` gates — skipping it for `.ini` gates left every `.ini`-airport 777 parking ~5m short. → [gsx.md](../gsx.md)

## DCK-5

- `GsxOffset.Zero` must be a strict no-op (skip the shift entirely) — any resolver miss at any layer must degrade to Zero, never throw or half-apply. → [gsx.md](../gsx.md)

## DCK-6

- Two file-parsing paths deliberately SURVIVED the Remote API move and are NOT version floors — no GSX update retires either: (1) the `.ini`/navdata gate LIST, because `handlerData.airport` only ever describes the airport GSX has LOADED while `TaxiAssistForm`/`GateTeleportForm` both accept a typed remote ICAO (`GateDataSource` takes the API path only when the `handlerData` capability is advertised AND `handlerData.airport.icao` equals the requested ICAO); and (2) the `.py`/`.ini` stop-offset chain, because the API cannot supply the docking stop at all — see the "Remote API does NOT publish the docking stop position" bullet at the end of this group. → [gsx.md](../gsx.md)

## DCK-7

- `ParkingSpot.GsxIdentifier` is populated ONLY by `GsxRemoteParkingReader`, so a gate list served from the `.ini`/navdata fallback cannot be auto-selected — `gate.select` degrades to `BadArgs`, i.e. to manual selection, which is the pre-existing baseline and the intended degradation. `ParkingSpot.Radius` is FEET on a navdata spot and METRES on a GSX one — convert by `Source`, never assume. → [gsx.md](../gsx.md)

## DCK-8

- `ParkingSpot.Name` is the CONCOURSE LETTER on every path, GSX Remote API included — NEVER terminal prose. GSX's `uiTerminalName` belongs in `ParkingSpot.TerminalName`, which `Describe()` renders after the type (i.e. after the first spaced dash, the boundary `SayIntentionsClearanceParser.NormalizeParkingName` cuts at) and before the Jetway/VDGS notes — but ONLY when it DISAMBIGUATES (`TerminalNameDisambiguates`, set by `GsxTerminalDisambiguator.Mark` at the end of `GateDataSource`'s Remote API path for the stands whose letter+number+suffix another stand STILL shares after lettering), and stripped of its size-hint tail by `ParkingSpot.SpeakableTerminalName`. `uiTerminalName` is the profile author's SECTION HEADER, not always a terminal: at KJFK "Terminal 4 - Concourse B" (five stands share "Gate 2", so it is essential), at EHAM "A-Platform =< Medium " / "K/M-Platform buffer overflow (TD) N/A " — rendered on a UNIQUE stand that read "A 42 - Gate Small, A-Platform =< Medium" ("equals less than") for no information (live EHAM 2026-08-15). Terminal prose in `Name` broke three consumers at once: `GateAliasResolver` (`StandId.Parse` rejects it, so EVERY online alias candidate was dropped), SayIntentions' assigned-gate resolution — which then falls through its whole chain to the ARRIVAL RUNWAY — and MainForm's parked-at-the-right-stand check. The terminal must still reach the LABEL for a COLLIDING stand, though: `uiGateName` alone names five different KJFK stands and the dropdown de-duplicates by label text, so four of five would be unreachable. Split the identity with `StandId.Parse`, the shared canonical parse, never a local regex. → [gsx.md](../gsx.md)

## DCK-9

- Stating that convention does NOT populate it: `uiGateName` usually carries no letter (KJFK: 9 of 231 stands do, 91 carry it only in `uiTerminalName`, 131 have none anywhere), so `GsxConcourseLetterFiller` BORROWS it and `GateDataSource` runs it right after the reader. Without it the 91 render as "25" while SayIntentions asks for "B25", and that lands on the ARRIVAL RUNWAY exactly like terminal-prose-in-`Name` did. The borrow is NAME-ONLY (never the coordinates/heading/radius/stop — that is why it is not a `GsxNavdataMerger` call), never overwrites a letter GSX did supply, and a stand left letterless is a CORRECT answer — `Name = ""` must stay a supported shape (131 KJFK stands, all of ENGM). → [gsx.md](../gsx.md)

## DCK-10

- The two letter sources are ordered `uiTerminalName`'s "Concourse X" wording FIRST, navdata second, and that order is MEASURED — never flip it back on general "navdata is authoritative" grounds. Across all 222 letterless KJFK stands joined to real fs2024 navdata the two DISAGREE on 46 (32 agree, 52 navdata-only, 13 terminal-only, 79 letterless), and GSX is right every sampled time: navdata calls T4's "Gate 25" concourse A, but KJFK T4 is Concourse A (A2-A7) + Concourse B (B20-B41), so it is B25 — what ATC and SI say. Navdata's letter is the BGL parking-name enum (`GATE_A`…`GATE_Z` → `MapParkingName`), i.e. whatever the scenery author set, so navdata is authoritative for stand GEOMETRY and demonstrably NOT for the concourse letter — a measured exception, not a contradiction. Navdata still decides wherever the terminal names no concourse: position AND number must agree inside a measured 10 m (closest two KJFK stands 21.2 m; closest same-number/different-letter pair 227.4 m), and two in-range navdata stands disagreeing are REFUSED, never arbitrated. Never widen the radius or the "Concourse" wording to "improve" recall — leading with GSX prose can already invent a letter at an airport that letters concourses but not gates, and the filter's strictness is the only mitigation. → [gsx.md](../gsx.md)

## DCK-11

- A heading from the Remote API must be normalized to 0-360 through the SAME `GsxProfileParser.NormalizeHeading` the `.ini` path uses — GSX publishes SIGNED headings (122/231 in the KJFK capture, 37/68 live at ENGM) and the number is SPOKEN ("gate heading -120"), which a pilot cannot reconcile with a heading indicator. `double.NaN` must pass through UNCHANGED: it is the "GSX published no heading" sentinel `HasUsableHeading` tests for. → [gsx.md](../gsx.md)

## DCK-12

- Deice pads live in `handlerData.airport.deIceAreas`, NOT in `handlerData.airport.parkings` (live ENGM: 9 pads in their own collection; the distinct `uiType` values across all 99 parkings contain no pad) — so `GsxRemoteParkingReader` needs NO deice exclusion and cannot leak a pad into the gate list. `GetDeiceAreas` stays on the `.ini`'s `is_deicearea` key because nothing has wired `deIceAreas` up yet, NOT because the API can't tell a pad from a stand. → [gsx.md](../gsx.md)

## DCK-13

- **Never mutate a list handed back by `GateDataSource`/`ParkingSpotSource.GetSelectableGates`** (`.Clear()`, `.Remove`, `.Sort`) — it is the SAME instance `GateDataSource` holds in its per-ICAO caches, as `ParkingSpotSource`'s own doc says. `GateTeleportForm.ClearGatesAndParking` cleared it in place, emptying GSX's cached gate list, and the retyped ICAO then hit that cache and reported "No gates or parking found" for the rest of the dialog's life — slipping past `TryBuildGatesFromRemoteApi`'s deliberate refusal to CACHE an empty list, which sits after the cache-hit early return. Drop the reference instead. → [gsx.md](../gsx.md)

## DCK-14

- **Any cache holding STAND NAMES must be keyed on `GateDataSource.GetGateListVersion`'s token as well as the ICAO, compared through `ShouldRebuildGateList`** (which rebuilds on an upgrade/refresh and never on the downgrade a transient GSX drop causes). Names are frozen into a `TaxiGraph`'s nodes at build time, so a graph built before GSX published the airport keeps navdata's concourse letters forever: `TaxiAssistForm._graph` (its same-ICAO early return matched for the whole session) and `TaxiGuidanceManager._whereAmICachedGraph` (whose only invalidations are the online taxiway-name fetch and `ClearWhereAmICache`, which at the time had no production caller at all — a database switch calls it now, for the runway-shape memo that rides on the same pair, so a switch is no longer among the gaps here) both said "Gate A 25" while the destination combo and the TCAS label said "B 25" — the one-stand-two-names defect `ParkingSpotSource` exists to remove. → [gsx.md](../gsx.md)

## DCK-15

- **A DATABASE switch is invalidated by CLOSING the window, not by a cache clear** — `RefreshDatabaseProvider` closes `tcasForm` alongside `electronicFlightBagForm` because `GateResolver` captures its provider in a `readonly` field at construction. `GateResolver.ClearCache()` has no production caller and could not fix a database switch even if it did (it clears the spot cache and never touches the provider); the gate-list token does not move on a database switch either. → [gsx.md](../gsx.md)

## DCK-16

- Docking's forward distance math must stay datum-aligned — never reintroduce the per-aircraft `gsx.cfg` longitudinal door offset into the stop math; it describes door position on the airframe, not a stop offset, and parked a B777 ~26m short when subtracted. → [gsx.md](../gsx.md)

## DCK-17

- The docking lateral cue must use the heading-error angle, never `CalculateCrossTrackError` — that assumes the aircraft is ahead of the reference and yields ±180° garbage when docking from behind. → [gsx.md](../gsx.md)

## DCK-18

- `DockingGeometry.ClampStopToOccupancy` must never be removed or simplified to clamp on `gatedistancethreshold` unconditionally, and must clamp only the `.py`-shifted stop, never the navdata base point — it must remain a no-op for deice pads, navdata-only gates, already-inside-circle datums, and VDGS-reliant gates whose stop sits beyond the threshold. → [gsx.md](../gsx.md)

## DCK-19

- Docking's lateral tone must use the runway-lineup PRECISION profile (`UpdateHeadingErrorWithThresholds`), never the width-scaled overload — its MIN_SCALE clamp is far too loose for parking. → [gsx.md](../gsx.md)

## DCK-20

- `DockingCompleted` must fire `taxiGuidanceManager.StopGuidance()` exactly once (event raised outside the docking lock), or taxi guidance can be left stuck in LiningUp after parking. → [gsx.md](../gsx.md)

## DCK-21

- Arrival ownership must stay ENGAGE-LATCHED (docking `IsActive` = Docking or Stopped state), never widened back to gate-set semantics — the old semantics left a pilot in total verbal silence when docking never engages (approach outside cone, navdata heading error). → [gsx.md](../gsx.md)

## DCK-22

- Docking's stop tolerance and beep plateau must not regress: `StopToleranceMetres` stays 0.3m and `BeepNearMetres` must equal it (no plateau) — a plateau makes 2m-to-stop sound identical to the stop itself and pilots park short. → [gsx.md](../gsx.md)

## DCK-23

- Docking completion requires SQUARENESS with the gate axis (`DockingGeometry.IsSquare`, `StopMaxHeadingErrorDeg` 7°) as well as the ≤2 m cross gate — never reduce it back to cross+along only (KJFK gate 20: a 17.4°-askew park announced "GSX docking complete." while GSX offered reposition, and the callout landed mid-alignment-turn). An askew arrival must TERMINATE like `IsLateralMiss` (stop-and-retry, silence, overshoot-flavoured Stopped), never advise an in-place turn: the stop band leaves 1.3 m of travel, ~2° of heading change on a heavy. → [gsx.md](../gsx.md)

## DCK-24

- A VERIFIED-good park CONCLUDES guidance: the solid "docked" tone holds `CompletedHoldToneSeconds` (3 s) as a positional reference across the moment of stopping, then docking falls silent for good with a verbal closure ("… Aligned with gate. Parking brake."; a deice pad gets no gate-alignment claim). Never restore the old hold-until-the-pilot-presses-Stop behaviour, and never hold ANY tone after an OVERSHOOT/askew stop — a "docked" marker over a bad park misleads. → [gsx.md](../gsx.md)

## DCK-25

- The concluded-park hold tone must fade on a one-shot `Timer` armed at completion, NEVER a per-frame countdown — completion raises `DockingCompleted` → `StopGuidance()` → `StopTaxiGuidanceMonitoring()`, and docking is fed from that same `TAXI_GUIDANCE_POSITION` stream, so the completing frame is normally the LAST frame the manager sees and a countdown never runs (the tone held forever). The same applies to any future "N seconds after the park" behaviour. Cancel the timer at both reset sites + dispose, re-check the state under the lock in the callback, and only ever use the non-blocking `Timer.Dispose()` under `_lock`. → [gsx.md](../gsx.md)

## DCK-26

- Docking's taxi-away disengage must use ABSOLUTE distance, never along-track — along-track goes negative once the stop is behind the aircraft and can never trip for a forward taxi-out. → [gsx.md](../gsx.md)

## DCK-27

- Disabling docking (or losing the gate) mid-approach must fully `ResetLocked`, not just go silent — leaving `_state` latched at Docking/Stopped keeps `IsActive` true forever and mutes taxi's steering tone with no lateral cue. → [gsx.md](../gsx.md)

## DCK-28

- Takeoff-assist activation and `LandingRollout` entry must both call `SetDestinationGate(null)` — a stale departure gate could otherwise keep docking `IsActive` latched on landing and mute the rollout steering tone. → [gsx.md](../gsx.md)

## DCK-29

- Docking's "Slow down." threshold is `SlowDownSpeedKts` 3.0 (lowered from 5.0, which was silent through every live approach that ended askew or through the stop) — and `SlowDownMetres` (6 m) must NOT be widened to make the warning earlier: the callout is a one-shot re-armed only at engage/reset, so a wider gate lets it fire during the normal deceleration from taxi speed and be spent before the band where it matters. The speed threshold is the knob, not the distance. → [gsx.md](../gsx.md)

## DCK-30

- Docking's 1-knot speed callout is the only PERIODIC cue on that path and must stay QUEUED (`Announce`), placed LAST after the slow-down/milestone one-shots and skipped on a frame one of them fired — every other docking callout is `AnnounceImmediate`, which interrupts, so speaking it first didn't make speed win (the last speaker wins), it just consumed the knot and cut the phrase off. `EngageLocked` must `Arm(currentSpeed)`, never `Reset()`: a "next sample always speaks" re-arm put a number over the multi-second engage callout 16-33 ms later on every dock. → [gsx.md](../gsx.md)

## DCK-31

- The runway-style stopped-misaligned pulse must NEVER be re-added to gate lineup — precision parking is docking's job; pulsing 3Hz at a correctly-parked pilot demanding precision to a possibly-offset navdata point is a misfeature. → [gsx.md](../gsx.md)

## DCK-32

- MainForm must call `taxiGuidanceManager.SetSteeringToneSuppressed(dockingGuidanceManager.IsActive)` every frame so only one steering tone ever plays — taxi and docking must never pan simultaneously. → [gsx.md](../gsx.md)

## DCK-33

- Hot-path perf invariants must not regress: docking far-field telemetry/lineup math stays gated to <150m or engaged; hold-short/parking/exit-approach/runway-end callout paths must early-out once their latches have fired; `TaxiAssistForm`'s gate list must stay cached in memory per ICAO (never re-query per keystroke); `SettingsManager.Save` must write the file OUTSIDE its static lock. → [gsx.md](../gsx.md)

## DCK-34

- SimConnect's `PLANE_HEADING_DEGREES_TRUE`/`_MAGNETIC` are returned in RADIANS despite the name — always multiply by 57.2958 before using as degrees (lat/lon are already in degrees). → [gsx.md](../gsx.md)

## DCK-35

- `DistanceFormatter` is a DISPLAY layer only — never use it for guidance thresholds; those must stay unit-native (metric) internally. `GroundTrafficUseMetres` is a separate, independent toggle from `GroundDistanceUnit` — never fold them together. → [gsx.md](../gsx.md)

## DCK-36

- A per-ICAO gate-list cache must key on `GateDataSource.GetGateListVersion(icao)` as well as the ICAO — an O(1) compare-only token (`api:{HandlerDataVersion}`/`ini`/`navdata`; never a file, DB or `GetGates`, it is compared per keystroke; not a substitute for `GetActiveSource`) — compared through `GateDataSource.ShouldRebuildGateList`, which rebuilds on an UPGRADE/refresh and NEVER on the downgrade a transient GSX drop causes. A consumer that needs ONLY the token and holds no GateDataSource of its own reads the static `GateDataSource.ComputeGateListVersion` over the same four GSX signals (MainForm's `GateListVersion`, behind both the surroundings catalog cache — asked on every position sample the passing-callout monitor handles — and the Where-Am-I graph cache), never a GateDataSource constructed per ask; `GetGateListVersion` is that same call on its own fields, so the two cannot drift. A list bound from the fallback before GSX published the airport is otherwise served identifier-less all session (`TaxiAssistForm._cachedGateSpots`, `GateResolver`). `TaxiAssistForm` re-checks it on show, at the top of Calculate, and before a SayIntentions import resolves; a chosen stand the rebuilt list no longer carries leaves NOTHING selected (never item 0) — announced QUEUED on show, an `AnnounceCalculateAbort` on Calculate. → [gsx.md](../gsx.md)

## DCK-37

- `GateResolver` (the TCAS "at Gate …" label) names stands through `ParkingSpotSource.GetNamedSpots` — never raw `GetParkingSpots` (two names for one stand in one session) and never `GetSelectableGates` (it names, it does not act). → [gsx.md](../gsx.md)

## DCK-38

- `GsxGateMapper.MapGsxTypeToNavdataType` maps `GATE_EXTRA`(GSX 15)→navdata 14 and `RAMP_GA_EXTRA`(GSX 14)→navdata 15 — the numbering is SWAPPED between the two enums; leaving them unmapped made every A380-class stand "Spot N - Unknown" on the Remote API path. → [gsx.md](../gsx.md)

## DCK-39

- The Remote API does NOT publish the docking stop position (`stopPosition`/`objectPosition` are null on every parking) and exposes no method-invocation verb to compute one, so docking's stop geometry still comes from GSX's `.ini`/`.py` profile files (`GsxProfileLocator`/`GsxStopOffsetResolver`/`GsxPyOffsetEvaluator`/etc., all unchanged) — do NOT "finish the migration" by sourcing the stop from the API's `lat`/`lon`, which sit ~11.6 m from the real VDGS stop point, far outside docking's 0.3 m `StopToleranceMetres`. → [gsx.md](../gsx.md)

## DCK-40

- A stand has ONE name app-wide, and it is CORRECTED IN PLACE on the navdata list — the graph's spot SET is never swapped. `Services/ParkingSpotSource` has two shapes and the difference is load-bearing: `GetSelectableGates` (GSX's own list, for anything that must ACT on a stand — it carries `GsxIdentifier`, the stop position, max wingspan, `TerminalName`) and `GetNamedSpots` (navdata's own SET — same spots, count, coordinates and order — with only the concourse letter corrected from the gate list via `GsxStandNameOverlay`, then `AugmentParking` re-run so aliases resolve against the corrected identity). Every `TaxiGraph.Build` call (bar the one runway-rows-only build the next bullet carves out) and the SayIntentions parked-at-the-right-stand check use `GetNamedSpots`; the two selection dialogs use `GetSelectableGates`. NEVER call `IAirportDataProvider.GetParkingSpots` directly to build a list a pilot can hear a stand named from — that is navdata's BGL parking-name enum, which disagrees with GSX on 46 of 222 KJFK stands (GSX right in every sampled case), and it is how an aircraft parked exactly at B25 came to be told *"Aircraft appears near A 25, not assigned gate Terminal 4 Gate B25."*. `TaxiGuidanceManager.ParkingSpotSupplier` is the seam for its three graph builds and DEFAULTS to `GetParkingSpots` so unwired callers stay byte-identical. Call it where a graph is built or a dialog opens, NEVER on a position update, and its per-ICAO caches are `ConcurrentDictionary`s since PR #238 §8a, so one instance MAY now be touched from the UI thread and a background thread at once — that was the prerequisite for moving `LandingExitForm`'s `GetNamedSpots`/`GetRunwayStarts`/`GetRunways` off the UI thread, where they ran inline while a screen-reader user was arrowing the exit combo. MainForm's supplier still builds a fresh one per call, which is now belt-and-braces rather than required. What is UNCHANGED: never MUTATE a list `GateDataSource` hands back — it is the same instance held in its cache. → [gsx.md](../gsx.md)

## DCK-41

- NAMING a stand and deciding WHICH NODES ARE PARKING are different jobs, and only the first may change — which is why the seam above corrects names in place instead of handing `TaxiGraph.Build` the GSX list. `Build`'s parking pass only LABELS an existing node (`FindNearestNode` within 100 m — it creates no node and moves none), and `TaxiNode.ParkingName` has four consumers inside `DescribeLocation` (readout), plus the route briefing's own-position endpoint (also a readout). But the same pass ALSO writes `node.Type = TaxiNodeType.Parking` unconditionally, and `Type` is read by `NamedHoldingPointResolver` (skips parking nodes when snapping a named holding point — a Progressive-Taxi terminator target, and a resolver whose snap radii are probe-pinned and must not be re-tuned), by `HoldShortNodeResolver`, and by the route truncation in `TaxiGuidanceManager.Routing`. So a differently-sized spot list marks a different set of nodes `Parking` and can MOVE A HOLD-SHORT — a runway-incursion surface, not a readout — and additionally strips the Where-Am-I label off any stand it omits (GSX excludes Vehicle/Fuel stands and drops headingless ones: 230 of KJFK's 231). Never feed `Build` a list that is not navdata's own set. The ONE exception is a runway-rows-only `Build` with no taxi paths and NO parking at all (`RunwayPavement.BuildShapesFromRunwayRows`, the passing-callout runway probe's shapes): only its runway centrelines are read, the graph is thrown away at once and never names a stand, marks a node or places a hold-short. The route briefing's online planning graph (`OsmPlanningGraph`) passes NO parking at all: its stands stay in the bundle for the stand picker only. → [taxi-guidance.md](../taxi-guidance.md)

## DCK-42

- GSX's Remote API publishes a stand's `heading`, `type`, `hasJetway` and `airlineCodes` only when a GSX profile section covers it. A stand no section covers arrives with position, `uiType`, `uiTerminalName`, `parkingSystem`, `gateDistanceThreshold` and `maxWingspan` 999 alone.
- Measured:
  - KSAN live, 2026-10-07: 75 of 79 selectable stands. The installed profile is LatinVFR's, written for a different scenery, and its 4 matching sections are exactly the 4 stands with a heading.
  - KSFO, no profile: 208 of 208 in that day's log.
  - The committed KATL capture: 4 of 8. KJFK (iniBuilds profile): 1 of 231.
- Impact before the fix: `DropUnusableHeadings` left KSAN with 4 stands after touchdown and aborted the SayIntentions import of its assigned Gate 115. At KSFO the whole list fell back to navdata, without GSX identity.
- Why navdata is the donor:
  - All 75 KSAN stands have a same-numbered navdata stand 0.36–4.50 m away (median 2.16).
  - Where both publish a heading (187 KJFK stands), they agree to a median 0.24°, max 6.68°, with no 180° flips.
- Rules of the fill:
  - The `.ini`'s `this_parking_pos` heading still wins (GSX's own data, joined first).
  - It never overwrites a published value.
  - In-range candidates disagreeing by more than `MaxHeadingDisagreementDegrees` (10°) are refused, never arbitrated.
  - The size sets `Radius` = navdata feet × 0.3048 and `MaxWingspanMeters` = twice that. That is the fit navdata's own `FitsAircraft` gives, so the fit filter and SayIntentions' position match (SI-7) behave as on a navdata list.
  - The filler shares `GsxConcourseLetterFiller`'s one lazy navdata read and is skipped when nothing needs it.
  - A stand neither source can orient is still dropped.

→ [gsx.md](../gsx.md)

## DCK-43

- `type` is absent on every unconfigured stand (live KSAN: 75 of 79, exactly the 75 without a heading), while `uiType` is present on all of them. (The committed KATL capture carries no `type` on any stand because it was trimmed when saved, so it is not evidence either way.)
- Upper-cased with spaces as underscores, `uiType` IS the constant name the reader already maps ("Gate Medium" → `GATE_MEDIUM`). On all 231 KJFK stands it resolves to the same navdata type as `type`.
- Without it, the recovered stands read "Spot 115 - Unknown", outside every gate category.
- A PRESENT `type` with no matching constant still degrades to 0 (pinned by `Entry_with_no_matching_type_constant_degrades_to_unknown_type_zero`).
- Use `ToUpperInvariant`, never `ToUpper`: in tr-TR "Ramp Mil Cargo" folds to `RAMP_MİL_CARGO`.
- `maxWingspan` 999.0 is on every stand GSX publishes without a heading (live KSAN 75 of 75, KATL 4 of 4, and KJFK's 7 Vehicle/Fuel entries) and on no stand that has one (KJFK's 231 selectable stands top out at 90 m). Read as 499.5 m it would make every such stand fit any aircraft, so it reads as unpublished and `GsxNavdataGeometryFiller` (DCK-42) fills the real size.

→ [gsx.md](../gsx.md)
