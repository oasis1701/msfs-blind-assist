# Route start: on a runway, and away from the first cleared taxiway

Full text of the rules in `.claude/rules/route-runway-start.md`. Background: [taxi-guidance.md](../taxi-guidance.md).

## RTE-37

A plain taxi route loaded while the aircraft rolls or stands along a runway starts on a node of that runway's own pavement (`RouteRunwayStart.Candidates`, tried in order by route loading), because runway pavement is not a routable corridor and the nearest graph node can be a parallel taxiway across the infield.

RJAA 34L, 2026-11-06: stopped between A3 and A2, a SayIntentions clearance (A1, A6, ...) the router could not join, the lead-in rejected, and a route starting on taxiway A 200 m to the side. The tone aimed 482 m straight over the grass.

The order is the first cleared taxiway's node ahead, the other nodes ahead by distance (30 m or more), then the nodes behind, one node per exit. A heading more than about 45° off the runway has no "ahead" and keeps the older start. So does a first cleared taxiway within the lead-in range (75 m), which the usual pre-snap starts on (RTE-23). Runway destinations and landing-exit routes have their own start logic and stay out (`RouteRunwayStart.Applies`).

A node whose straight roll would touch a runway the aircraft is not already on is dropped, because no stop can be placed before the route's first node. An exit whose route leaves the runway and comes back onto it is skipped (`RouteReturnsToRunway`): at KFLL 10L Q the far bank's node made the hold pass stop the pilot short of the runway just left. With no exit left, the older start stands, since a stop on the pavement is never acceptable (HLD-4).

Until the aircraft reaches it, the tone aims at the start node itself (`AimsAtStartNode`): the look-ahead walk reads along the first segment and, where that leaves at an angle, lands up the exit, aiming the whole roll beside the runway. The spoken "Join taxiway X in N" counts the roll to the start node (`_routeStartGapMeters`), and the far-start warning is not spoken for it.

Measure any change with `VirtualPilot rwystop` ([virtual-pilot.md](../virtual-pilot.md)).

## RTE-38

The lead-in ends where the route meets the first cleared taxiway (`TaxiLeadIn.Extract`): at its first segment of that name, or, when no segment carries the name, at its first node on that taxiway.

The router may join the first cleared taxiway exactly where the next one leaves it (`FindConstrainedPathCore` step 1, entry node equal to the first target). The route then has no segment of that name. `Extract` used to walk to the first such segment, found none, and measured the whole route as lead-in. The length guard (`IsAcceptable`, gap × 2.5 + 300 m) then rejected it, and the route started on the cleared taxiway's node however far away that was.

VTBS 20L, 2026-11-18: cleared E7, E8, D6 but vacated at E9, with the E7/E8 junction 550 m up the parallel. A 640 m lead-in was measured as 2,213 m and rejected, and the tone aimed 550 m in a straight line that left taxiway E by 55 m, over grass and a pond. VHHH 07L the same evening: cleared C8 with the C8/C junction 600 m ahead on the parallel, measured as 4,875 m, and the tone led off the runway edge across the infield.

A route that never touches the taxiway at all is still measured whole, so a router fallback keeps failing the guard.

## RTE-39

When the route from the aircraft's position never drives the first cleared taxiway and turns straight back at the junction where it meets it (`TaxiLeadIn.DoublesBackAtUndrivenStart`, 150° or more at that one node), that taxiway is left out of the clearance and the route is built again (`UndrivenClearanceStart.Count`, applied by `LoadRoute`). The pilot hears "Taxiway E7 is not on the way from here and was left out." ahead of the summary.

With RTE-38 alone the VTBS lead-in was accepted, and the route ran up E, out along E8 to the E7 junction beside the runway hold line, and back along E8: a reversal no aircraft can make on a taxiway. Without E7 the route goes up E and turns onto D6.

It stops at the first taxiway the route can keep:

- A taxiway the route only passes through at a junction stays. Leaving out every undriven taxiway was tried first, and at VTBS it also dropped E8 and D6, after which the router was free to take D7 instead of the cleared D6.
- The last cleared taxiway stays: it decides where a runway route ends (RTE-17).
- A taxiway a hold-short pick is keyed to stays (`PicksAllow`), so the pilot's own stop is never lost. The picks that remain move down with the clearance (`Shift`), in the stored copies too, because a recalculation reads them against `_originalTaxiwaySequence`.
- A clearance the router cannot honour once the taxiway is gone keeps it.

It runs only where a lead-in would be built: a first cleared taxiway within 75 m keeps its pre-snap (RTE-23), and a route started on a runway (RTE-37) is not touched.

A lead-in still starts at the nearest node whatever the heading, so some routes now open with "Taxiway X is behind you" where they used to open with a far-start warning and a straight line.

Measure any change with `VirtualPilot missedexit`, on both navdata sets (`VP_SIM=2024`).
