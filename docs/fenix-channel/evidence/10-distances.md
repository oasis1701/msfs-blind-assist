# Plan 10 evidence: D and Shift+D

Fenix build **3.0.0.7613**, MSFS 2024, measured on 2026-11-04 by Hadi with Claude, with no new flight: every rule was
replayed over plan 03's recordings of its two flights (`03-nd.md`, sittings 2 and 3). The recorder kept every push of
the display channel. Each push holds the route (2), the present position (192), the markers (2.12) and the captain's
MCDU text (183), so the computed side and the MCDU's side come from the same push, the "same instant" Task 1 asked for.
The recordings and the replay scripts stay local and are never committed.

## The recordings

| | Sitting 2, 2026-11-03 | Sitting 3, 2026-11-04 |
|---|---|---|
| Flight | KATL 08R to KCVG ILS 18L, 20:27 to 21:54 local | KCVG to KATL ILS 08L, 10:08 to 12:15 local |
| Pushes | 7,712 | 10,086 |
| With the F-PLN's DEST line and a DIST | 4,056: 20:57:54 (climbing) to 21:53:28 (landing roll), with gaps in the cruise; none at the gate (the MCDU on INIT FUEL PRED) | 3,879: the KCVG gate (10:08 to 10:31) and 11:44:24 to 12:05:35 (descent and approach) |
| With PERF CRZ's "TO (T/D)" and a DIST | 634: 21:10:37 to 21:26:08 | 1,237: 11:23:50 to 11:40:32 |

What the two flights held: a SID; a STAR with a curved leg (AAJAY to IGOCO, two turn pieces of radius 1.76 NM); up to
three F-PLN discontinuities at once (sitting 2: after PADGT and ARTUR until 21:04:20 and 21:04:54, after the heading leg
MANUAL until 21:40:53); HDG (sitting 2, 21:00:28 to 21:02:45); a DIR TO; an overfly; a cruise level entered late
(sitting 3, 11:23:03); an early descent (sitting 3, 11:40:33, with PERF CRZ printing the T/D 40 NM ahead); two ILS approaches; two landing
rolls. Not held: a step climb or step descent, a go-around flown, a flight without a STAR, a heading leg as the TO
waypoint, the alternate made active.

## The MCDU's figures

- The F-PLN's last two lines: `DEST   UTC  DIST  EFOB` in flight, `DEST   TIME  DIST  EFOB` at the gate, then the
  destination's line, DIST in whole miles. At the KCVG gate: `KATL08L ----   398 -----`, a distance with no time.
- PERF CRZ: `TO` and `(T/D)` on one line, `UTC` and `DIST` two lines below; at 21:10:37 `0233` and `174`.
- Hadi (2026-11-04): the DEST distance shows whatever the lateral mode, and grows while the aircraft is vectored away
  from the route.

## Distance to destination (Task 1 Step 4)

The rules tried, every 20 s of the pushes with a DEST line, against the tolerance (1 NM plus 0.5 % of the DEST DIST):

| Rule | Sitting 2 | Sitting 3 |
|---|---|---|
| The plan's: straight to the first path element carrying the TO's ident, then chords; not available on a break | 5 within, 33 missed (worst 18.6 NM), 47 not available | 71 within, 38 missed (worst 112 NM) |
| Along-track on the active leg, turn pieces as radius times angle; not available on a break | 36 within, 2 missed (1.4 NM), 47 not available | 109 within (worst 0.7) |
| Waypoint to waypoint: straight to the TO, then each waypoint symbol (kind 4), then the threshold | 66 within, 19 missed (worst 2.0) | 109 within (worst 0.6) |
| **The rule kept** (below) | 83 within, 2 missed (2.0) | 109 within (worst 0.5) |

Why the others fail:

- The first element carrying the TO's ident can lie far behind the aircraft: a turn's start, or "KCVG18L" on the kind 2
  element at FRAZE's point, which counted the final approach twice (up to +7 NM on final).
- Chords between a turn's points lose 1.3 to 1.8 NM on the curved leg AAJAY to IGOCO, and so does the waypoint rule.
  The turn's radius times its course change gives the MCDU's figure there.
- Along the line, the distance does not grow while vectored away; the MCDU's does (Hadi). Straight to the TO waypoint
  does, and sitting 2's HDG stretch fits it (-0.0 NM at 21:00:34).

The rule kept, on every push with a DEST line (7,935): **7,770 within the tolerance**. The other 165:

- 162 in the 52 s after BAIRE was sequenced (sitting 2, 21:48:59 to 21:49:51), up to 2.0 NM below the MCDU. BAIRE's
  turn is 153° (333° to 180° true). The sum fell 2.7 NM at the sequencing; the DEST DIST stayed at 11, then fell from
  11 to 7 in 42 s while the sum fell 1.4 NM, and ended within 0.6 NM of the sum. The MCDU's figure lags
  after a sharp turn is sequenced; the sum follows the same path.
- 3 on the landing rolls (sitting 2 21:53:28, sitting 3 12:05:34 and 12:05:35): the missed approach becomes the active
  route (its elements lose member 4, the TO turns to "1500"), no destination is left, and the rule says not available
  where the MCDU shows 0.

## Top of descent (Task 1 Step 5)

- **No T/D before a cruise level.** Sitting 3 had no type 0 marker from 10:08 until 11:23:10, 7 s after Hadi entered the
  cruise level on INIT (11:23:03). Hadi: the MCDU showed no T/D until he did. In sitting 2 the cruise level was in by
  20:58:18 (the first `(T/C)` row) and the first T/D came at 21:04:55 (the first `(T/D)` row at 21:04:43), between the
  clearing of the PADGT (21:04:20) and ARTUR (21:04:54) discontinuities; what made it appear then is not measured.
- **The cruise.** One type 0 marker, no color member (white), no go-around member. Its distance by the rule below
  matched PERF CRZ's "TO (T/D)" DIST in **all 1,871 pushes printing it**, within 1 NM plus 0.5 % (worst 1.26 NM, at
  170 NM where the tolerance is 1.85).
- **The descent begins.** The white marker went in the push where the descent began: sitting 2 at 21:26:10 (DES
  engaged), sitting 3 at 11:40:33. Hadi: it goes because the FMS enters the descent phase. In the same push the first
  type 2 (EOD) marker came, and type 0 markers came back: cyan, and white after a level-off (sitting 2 from 21:34:29,
  sitting 3 from 11:48:27). They are descent restart points, not the T/D: sitting 3 had one 133 NM ahead at 11:40:34.
  The plan's rule (one T/D marker ahead: its distance) would have said "133 miles to top of descent" there.
- **Type 2 marks the descent.** No type 2 marker in any push before the descent began (17,798 pushes); one or more in
  every push from then until every descent marker went on the approach (sitting 2 at 21:48:56.9 with G/S*, sitting 3
  at 12:02:01). From there to the gate the route has no T/D or EOD marker, so Shift+D is not available.
- A first version that placed the marker on the straight line from the aircraft to the TO waypoint said not available
  for two pushes in the turn just after ARTUR was sequenced (sitting 2, 21:24:10 and 21:24:12): the straight line passed
  0.56 NM from the marker. Placing the marker on the drawn line fixed it.
- The verdicts over each flight by the rule kept: sitting 2 not available to 21:04:55, a distance to 21:26:08, past
  to 21:48:56, not available after; sitting 3 not available to 11:23:10, a distance to 11:40:32, past to 12:02:01,
  not available after.

## The rules (Task 1's outcome, built in Task 2)

The line: the elements of 2.4 that `IsPath` keeps and that are not on the missed approach, in order, to the
destination element (`IsDestination`) or, with none, to the last of them.

- A turn piece (kind 1 with its radius, course in and course out) counts as its radius times the course change (the
  smaller angle, in radians); every other element as the great circle from the last kept point.
- A discontinuity end (`BreaksPath`) first drops the kept points before it that carry its own ident and lie more than
  0.05 NM from it (a heading leg's drawn far point: MANUAL's at 47.38 N), then is kept at its own point; the next start
  (kind 2) follows straight from there. The final approach's end and start (16 = 2) are ordinary points.
- The aircraft's distance to the line's end: the great circle to the TO waypoint's point (2.11), plus the line from the
  last kept point carrying the TO's ident.
- D: the TO is the destination runway (its ident is the arrival's, `ArrivalIdent`): the great circle to the arrival
  threshold (2.1[2]). Otherwise the aircraft's distance to the line's end, when the line reaches a destination element.
- Shift+D: any type 2 (EOD) marker: past. Otherwise the type 0 markers without the go-around member: none, or two or
  more, not available; one, placed on the line (within 0.5 NM across, between -0.1 and the step plus 0.1 along; the
  nearest across), its distance to the line's end subtracted from the aircraft's: 0.1 NM or less is past, more is the
  distance.
- Not available, for both keys: no TO (2.11 absent or no ident); the TO on the missed approach; the TO with no waypoint
  symbol (kind 4) of its ident, unless it is the destination runway (never seen in 16,444 pushes with a TO: a heading leg
  such as MANUAL would be one, its drawn point 500 NM out); the TO's ident on no kept point. D also: fewer than two
  runways in 2.1; no destination element in the line.

## The tolerance

D keeps the plan's: 1 NM plus 0.5 % of the DEST DIST. Shift+D's reference is now PERF CRZ's printed "TO (T/D)" DIST,
one whole-mile figure, so it takes the same tolerance: 1 NM plus 0.5 % of that figure (the plan's 0.5 NM for each summed
F-PLN leg is not needed).

## The recorded cases (Task 2's tests)

Fourteen pushes, each kept as a route table (the position, 2.1, 2.11, 2.4 and 2.12) in
`FenixChannelDistancesRecordedTests.cs`, with the MCDU's figure of the same push:

| Case | Push | MCDU | Expected |
|---|---|---|---|
| GateKcvg | sitting 3, 10:10:01 | DEST 398 | D within the tolerance; Shift+D not available |
| ClimbThreeDiscontinuities | sitting 2, 20:59:54 | DEST 346 | D within; Shift+D not available |
| ClimbInHdg | sitting 2, 21:00:34 | DEST 342 | D within; Shift+D not available |
| CruiseManualGapOpen | sitting 2, 21:19:04 | DEST 220 | D within; Shift+D a distance |
| CruiseTopOfDescent | sitting 2, 21:13:40 | T/D 149 | Shift+D within |
| TurnAfterArtur | sitting 2, 21:24:12 | T/D 64 | Shift+D within |
| CurvedLeg | sitting 2, 21:44:53 | DEST 29 | D within; Shift+D past |
| DescentRestartMarker | sitting 2, 21:41:02 | DEST 48 | D within; Shift+D past (a cyan type 0 ahead) |
| BeforeEarlyDescent | sitting 3, 11:40:32 | T/D 40 | Shift+D within |
| EarlyDescent | sitting 3, 11:40:34 | none | Shift+D past |
| NoCruiseLevel | sitting 3, 11:20:01 | none | Shift+D not available |
| AfterGlideslope | sitting 3, 12:03:00 | DEST 7 | D within; Shift+D not available |
| RunwayTo | sitting 3, 12:04:30 | DEST 3 | D within (the runway as the TO) |
| LandingRoll | sitting 2, 21:53:28 | DEST 0 | D not available |
