# SayIntentions gate resolution — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/sayintentions-gates.md`, which Claude Code loads when it reads matching code. Background: [sayintentions.md](../sayintentions.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## SI-1

- `assigned_gate` is always an ARRIVAL gate at `flight_destination` — SayIntentions does not assign a departure gate at all (per an SI developer). Never infer the gate's role from where the aircraft is standing: at the origin that announced the arrival stand as "Departure gate J1 at LMML", naming an airport the stand does not belong to. It also means the gate may appear in the destination-resolution chain ONLY behind a check that the airport being routed at IS the destination — the old unconditional fallback would, at the departure airport, have matched whatever local stand shared the name (`A9` and friends recur across airports) and routed there silently. Same gate for the parked-at-the-right-stand comparison. The live EDDF capture cannot settle this either way: it was taken AT the destination, where the two readings coincide. → [sayintentions.md](../sayintentions.md)

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).

## SI-2

- `assigned_gate` is the FULL label, not a stand id — a live EDDF arrival gave "Terminal 3 Gate J1". The stand id is whatever follows the LAST gate/stand keyword; a label carrying no keyword ("A-9", "J1") is used whole. Stripping noise words instead left "TERMINAL3J1", which matches no navdata spot, so the assigned gate could never resolve and destination resolution fell through to the STALE previous-leg `flight_plan_departing_runway` — routing a just-landed aircraft at a runway. → [sayintentions.md](../sayintentions.md)

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).

## SI-3

- A gate candidate resolves in THREE steps — name, then this scenery's online ALIASES, then the published `assigned_gate_lat`/`assigned_gate_lon` — and BOTH fallbacks must be tried INSIDE `TryResolveExternalDestination`'s candidate loop, on the same candidate whose name just failed, never after the loop. On a NON-arrival chain the last candidate is the ARRIVAL RUNWAY, which is the exact failure they exist to stop (a just-landed aircraft routed at the runway it landed on, taxiways perfect, nothing sounding wrong), so a fallback below the loop lets the runway win first and reproduces it. `SelectDestinationType(false)` is made ONCE for the pair — `_destinationSpotMap` holds gate entries only while gate mode is selected, and a runway candidate probed in between repopulates it.

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).

## SI-4

- ALL THREE of those steps see only what the gate list LISTED, so `TryResolveExternalDestination` must neutralise EVERY browsing filter on that list before probing — the gate SEARCH box, the OCCUPIED-stands filter (`_suppressOccupiedFilter`) and the WINGSPAN "show fitting only" filter (`_suppressFitFilter`, via `TaxiAssistForm.ShouldApplyFitFilter`). `PopulateDestinations` drops a filtered-out spot BEFORE its label reaches the combo, `_destinationSpotMap` and `_destinationThresholdMap`, so a hidden stand does not merely fail the NAME step: the alias and the published coordinate have nothing to match against either and all three go blind at once. The wingspan filter is the one that was missed, and it is the strongest case of the three — `ParkingSpot.FitsAircraft` reads a navdata parking RADIUS or a GSX MAX WING SPAN, both scenery-authored and frequently wrong, and the box is ticked BY DEFAULT whenever wingspan data exists, so this was the normal configuration and not an edge case: a SayIntentions clearance to a gate could not seat it, and on a known arrival that is the loud `ComposeUnresolvedArrivalGateMessage` abort for a stand the airport HAS and the aircraft FITS. A controller naming the stand outranks a scenery number saying it will not fit. Both suppressions are LATCHED on a successful seat, never restored — the Calculate-path and show-path gate-source refreshes both repopulate, and a rebuild dropping the seated stand clears the selection and aborts the very import that seated it — and cleared only by the pilot toggling that checkbox themselves or by an airport load; on a FAILED probe both are restored and the list rebuilt under them, because probing leaves no mark. Do NOT "fix" the ticked-but-inert checkbox by unticking it: that silently discards the pilot's own preference (`chkFitFilter` is deliberately exempt from `ResetRouteShapingControls` — it describes the aircraft, not the route) and changes a control with no screen-reader announcement.

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).

## SI-5

- On a KNOWN ARRIVAL — the routed airport IS `flight_destination` AND an assigned gate exists — the candidate list (`BuildSayIntentionsDestinationCandidates`) carries GATE candidates ONLY: clearance gate first, then the assigned gate with its coordinate; NO clearance-runway, departure-runway or arrival-runway candidate. Both runway routes produced the just-landed-aircraft-routed-at-a-runway failure live (KSTL "Gate A2"→12R, KLAX "Gate 52A"→24R, 2026-08-15/19): an arrival clearance naming the landing runway outside a masked span won the old runway-first order, and an unseatable gate (scenery labels the stand differently or lacks it outright) fell through to the arrival runway. An unresolvable known-arrival gate must FAIL LOUDLY (`ComposeUnresolvedArrivalGateMessage`, naming the controller's gate when it differs from the assigned one), never seat a runway — and never re-add a runway fallback to "help" it. Accepted residual: a departure imported while the previous leg's arrival record still stands (turnaround before re-filing, round-robin plan) aborts loudly instead of seating its runway — documented in [sayintentions.md](../sayintentions.md), revisit only against a live capture. `destProbe=[…]` in sayintentions.log records every candidate probe and why it missed (incl. distance from the published coordinate to the nearest stand). → [sayintentions.md](../sayintentions.md)

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).

## SI-6

- The ALIAS step exists because the alias is invisible to `MatchDestinationLabel`, not because it was missing: the combo carries `ParkingSpot.ToString()` (live KDTW: `A 24A - Gate Medium, also A24 (online)`) and `NormalizeParkingName` strips everything from the first SPACED DASH, which every `Describe()` branch puts ahead of the alias (`" - {type}"`). So the scenery's A24A never met SI's, OSM's and the controller's A24, and destination resolution took the arrival runway while the form's own gate search found that stand perfectly. Compare EXACT normalized alias against exact normalized identifier — never `Contains`: a one- or two-character stand id substring-matches almost any combo entry, and "A2" must never seat A24. → [sayintentions.md](../sayintentions.md)

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).

## SI-7

- The coordinate step attaches to the assigned gate ALONE, behind the same `flight_destination` check the name sits behind — an arrival stand's coordinate is as wrong at the departure airport as its name is, and unlike the name it always finds something. Acceptance is the stand's own radius times `NoseStopRadiusFactor` (2.0), and the winner is the NEAREST admissible stand — never a tuned metre constant, and no longer plain containment: the point is the NOSE-STOP, whose offset scales with the parked AIRCRAFT, so live KDTW put it 30.1 m out from a 22.9 m-radius stand (EDDB: 18.9 m out, 21.6 m radius) and containment dropped the right stand into the arrival-runway fallback. The factor is calibrated on TWO real arrivals — re-check it when a third disagrees — and stays a radius multiple because that still self-scales (Gate Extra ~50 m, medium gate ~21.6 m, GA spot metres). "Exactly 1 of 139 EDDB spots contained the point" is NOT the property any more; nearest-among-admissible is (doubled, EDDB's GB 7A qualifies too and loses on distance). `MaxMatchMetres` (150 m) is only a pathological-navdata backstop, the same role `GateAliasResolver`'s 150 m plays. `ParkingSpot.Radius` is FEET on a navdata spot and METRES on a GSX one — convert by `Source` before building a `GatePositionCandidate` or every tolerance is 3.28× too wide and the EDDB runner-up this excludes gets admitted. `(0,0)` is rejected at the reader, because null island is a real coordinate to a distance test. → [sayintentions.md](../sayintentions.md)

Moved 2026-10-10 from sayintentions-import.md (split for the rule budget).
