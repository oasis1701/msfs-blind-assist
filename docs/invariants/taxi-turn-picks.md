# Taxi Turn picks and the off-chart leg: rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/taxi-turn-picks.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).

## TURN-1

- **Turn picks are applied after the default route, never inside it.** `TurnPickApplier.Apply` takes the route `FindConstrainedPathCore` built exactly as before and rewrites it row by row, so a clearance with every Turn on Automatic is byte-identical to the route without the feature. Threading a pick into the constrained search would change every route (the search's tie-breaks and its fallback to shortest path are measured, RTE-33). `TaxiRouter.BlockedNodes` is how a pick keeps the rest of the route from coming back through the junction; its searches skip those nodes, and it is empty by default, so the check costs nothing without a pick. Never add a pick parameter to the router's own entry points. → [taxi-guidance.md](../taxi-guidance.md)

## TURN-2

- **Every gate of `OffChartLeg.Judge` is load-bearing.** A leg is 5 to 150 m, leaves at most 25 degrees to the other side of the incoming heading and 100 degrees to the picked side, turns 20 to 120 degrees onto the taxiway, keeps the two turns under 170 degrees, lies on mapped pavement at both ends and every 5 m, and touches no runway it does not start on. Measured at KBOS 2026-11-14 ("H, left on C"): the 126 m diagonal across the corner passes every shape gate and is refused ONLY by the pavement gate; the legs that would re-enter 04R from the connector start are refused ONLY by the runway gate. Loosening either puts a tone on grass or on an active runway. The runway gate is "any runway the leg does not start on", not just the one being crossed. → [taxi-guidance.md](../taxi-guidance.md)

## TURN-3

- **A recalculation never judges a Turn pick again from where the aircraft is.** A pick is resolved once, at load. `TryRecalculateRoute` reads its phase from the loaded route: ahead, it rebuilds only the part up to the turn and keeps the turn and everything after it as loaded (`TurnPickCarryOver.TailFrom`), and a pick that is not kept is always said ("could not be kept"), never dropped silently; in the turn (between the junction and the taxiway), it counts as missed and "Route changed" says so; made, its junction stays blocked for every later recalculation so none routes back through it. The blocked set changes only when a route is adopted, and a refused `LoadRoute` restores it (RTE-29). Re-judging from the aircraft would let the aircraft's drift reroute the pilot's named turn, or undo a turn already made. → [taxi-guidance.md](../taxi-guidance.md)
