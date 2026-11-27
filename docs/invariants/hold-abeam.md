# Hold-short passed beside its node

Full text of the rules in `.claude/rules/hold-abeam.md`. Background: [taxi-guidance.md](../taxi-guidance.md).

## HLD-24

A hold-short the aircraft passes beside its node, outside the 25 m capture circle, is latched by `Navigation.HoldShortAbeam.IsReached`: level with the node or up to 40 m past it along the hold segment, within 75 m to the side, heading within 60 degrees of the segment's bearing. `UpdatePositionFrame` calls it after the circle and goes through `AdvanceSegment`, so the stop, its wording and the hold state are the ordinary ones.

OMDB, 2026-11-24, K4 to Runway 30R with its hold short of runway 12R: passed 35 m to the side at 8 kt, nothing latched. One "Hold short runway 12R at K4. Stop.", no hold state, a silent Continue, the index left on the hold segment for good ([STR-5] keeps the pin-breaker off a hold segment) and the tone steering at the route beyond the line.

The guardrails:

- **Measured by `TryMeasureHoldAbeam`, never by `AlongTrackToSegmentEnd` on the hold segment alone.** `AlongTrackToSegmentEnd` answers "along 0, cross 0" for a segment under 1 m, which reads as level with the node and on the line from anywhere on the airport, and `AdvanceToNearestSegment` parks the index on a hold segment it has just judged too far away to call reached. `TryMeasureHoldAbeam` takes the direction from the hold segment, or from the nearest of the two segments before it when the hold segment is under 1 m (`HoldShortAbeam.MinAxisMetres`, `AxisLookBackSegments`), and measures to the hold node either way. With no direction in that reach it measures nothing and the hold is left to the capture circle. `HoldShortAbeamTests` pins the look-back.
- **Never on the final segment.** `AdvanceSegment` on a hold segment calls `HandleHoldShort`, the stop for a hold on the way. A runway route's last segment ends at the departure runway's own hold, which must come through `HandleArrival`: that is what sets `_holdShortAtDestination`, so that Continue there is a lineup clearance and not a crossing clearance. That hold keeps its own captures (the 12 m arrival circle and the stop within 30 m at 2 kt or less), so it is still missed on a rolling pass to one side; the pilot's guide says so.
- **The bounds are measured against live passes; fly a VirtualPilot A/B before changing one.** 75 m is wider than any on-route tolerance (61 m at the widest taxiway). 40 m past is the reach of the skipped-hold announcement (`HOLD_SHORT_ANNOUNCE_MAX_DIST_M`). 60 degrees keeps an aircraft still rolling along the taxiway before a 90 degree turn onto a short hold segment from being stopped early, and still takes the live pass 43 m to the side heading 40 degrees off the segment.

What the capture does in ordinary flights (VirtualPilot, 2026-11-27, fs2024 navdata, the 100 busiest airports, 7,617 taxi, departure, runway-stop and missed-exit flights, main against this): it adds a stop beside a hold in 11 of them, 27 to 58 m from the node. Five are flights that never arrived on main, the index pinned on a hold the route doubled back on or started beside (KCLE, KSLC, KSFO, VHHH twice). No flight that completed on main gains a stop; in the others the same stop comes where the aircraft is level with the node, at KSFO before runway 01L/19R's pavement where main made it on the pavement.

Open, from the review of 2026-11-27, not measured:

- A hold segment of 1 m to a few metres takes its direction from two nodes that close together. Raising `MinAxisMetres`, or taking a chord over the last 20 m, moves the direction onto the leg before a turn and reopens the case the heading bound closes. It needs a count of hold segments that short first.

## HLD-25

A hold reached beside its node ([HLD-24](#hld-24)) is not taken while the aircraft is `HoldShortAbeam.IsRollingOnBehindTheLine`: more than 10 m farther from the hold's runway than the hold node is (`MaxBehindLineMetres`), and steering 30 degrees or more away from where the tone leads (`FullPanSteeringErrorDegrees`, the taxi tone's full pan). That is a pilot who missed the turn onto the hold's connector and rolls on along the taxiway they were on.

A connector that leaves a parallel taxiway at 30 to 60 degrees puts that taxiway inside every bound of `IsReached`: level with the node along the connector's own direction, 36 to 70 m from it, heading 29 to 60 degrees off it. The stop was then spoken on the wrong taxiway ("Stop. Hold short of runway 27R at W. Press continue when cleared." at KDTW, 38 m from the node), and Continue would have announced the crossing from there. The answer to a missed turn is the off-route recalculation, as on main.

Measured with VirtualPilot's `missturn` mode, 2026-11-27, fs2024 navdata, the 100 busiest airports, 40 plans each, 1,502 missed turns (751 at 10 kt and 751 at 20 kt):

| | main | the capture alone | with this rule |
|---|---|---|---|
| stopped behind the line | 0 | 61 | 0 |
| stopped as near the runway as the node, or at a hold that guards no runway | 0 | 7 | 7 |
| held inside the 25 m circle | 11 | 11 | 11 |
| answered by the off-route recalculation (a new route, or the route kept and the reason spoken) | 1,352 | 1,288 | 1,345 |
| nothing within 300 m | 139 | 135 | 139 |

The guardrails:

- **Both tests, never one alone.** Each of the 61 was 26 to 53 m behind the line with the tone at full pan. Of the 11 stops the capture adds to ordinary flights ([HLD-24](#hld-24)), KCLE has the tone at full pan but is 45 m nearer the runway than the node, and VHHH J5 (21 and 22 m behind, on a 180 degree turn the tone is leading) and KSFO (35 m behind) are off full pan. Behind-the-line alone drops VHHH and KSFO, the tone alone drops KCLE, and all four are flights that never arrived before the capture. With both tests all 11 stops are kept, at the same places.
- **Behind the line is measured against the runway the node guards** (`MetresBehindHoldLine`, on the node's side of the centreline; across the runway reads as past the line). The runway comes from the incursion guard's per-graph cache (`_incursionNodeGeometry`, which `CheckRunwayIncursion` builds earlier in the same position frame), never from a `RunwayPavement.BuildShapes` per frame.
- **A hold that guards no known runway is taken as before.** A stop the route placed on an ordinary node, a named holding point and a node more than 150 m from any centreline have no entry in that cache; there is nothing to judge "behind" by, and a stop too many is the safe error. One of the 7 is such a hold (LEBL K7).
- **The steering error is `_smoothedHeadingError`,** what the tone is fed, so the test is about what the pilot hears.
- **Fly `missturn` and the ordinary modes before changing either figure.** `MISSED_TURN_STOPPED_BEHIND_LINE` must stay at 0, and the 11 stops must stay.

Not flown: a missed turn above 20 kt.
