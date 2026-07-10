# PR #127 review fixes: intersection-departure announcements, multi-crossing taxiways, full-length filter

**Date:** 2026-07-10
**Branch:** `feat/intersection-departures` (blindflightsimmer's fork, PR #127)
**Status:** Approved

## Background

The PR #127 review confirmed the intersection-departure geometry and routing
integration but left three findings to fix:

- **(3)** The post-Calculate intersection confirmation and
  `LastRouteReachWarning` are two back-to-back `AnnounceImmediate` calls — the
  second stomps the first, defeating the warning's "spoken last so it is heard
  in full" design.
- **(4)** `TaxiGraph.GetRunwayIntersections` keeps a single node per taxiway
  *name* (minimum perpendicular distance), so a taxiway that meets the runway
  at two well-separated points offers only one, arbitrarily chosen, entry.
  A navdata probe against the user's fs2024 DB found 23 such taxiways across
  ten major airports (KORD Y4 meets 04R/22L at 1361 m AND 1684 m; EDDF M17's
  two meeting points are 555 m apart) — these are the paired branches of
  high-speed exits sharing one name. Real, not theoretical.
- **(5)** At displaced-threshold runways (KJFK 22R: 3438 ft ≈ 1 km displaced;
  EHAM 36C: 1499 ft), the taxiway serving the NORMAL full-length lineup point
  sits well inside the physical pavement and lists as a bogus "intersection"
  (e.g. "~1,000 m from threshold"). Also: the duplicate-label guard in
  `PopulateIntersections` is currently unreachable dead defense.

User decisions (2026-07-10): one entry per meeting point (not one per
taxiway); filter entries at/before the full-length lineup point.

## Design

### 1. Combined standstill announcement (finding 3)

In `TaxiAssistForm.OnCalculateClicked`, compose the post-`StartGuidance`
standstill speech as ONE `AnnounceImmediate` call. Parts, in order:

1. Intersection confirmation ("Intersection W departure, runway 22R. About
   2,100 m of runway ahead.") — when an intersection was used.
2. `LastRouteReachWarning` — when set. Placed LAST so the safety-relevant
   text ends the utterance, preserving the original intent.

Joined with a space; announced once, only when non-empty. Two consecutive
`AnnounceImmediate` calls always stomp each other, so combining is the only
shape that guarantees both are heard. No announcer changes.

### 2. One entry per meeting point (finding 4)

`GetRunwayIntersections` replaces its per-taxiway single best-node pick with
along-track clustering:

1. Collect ALL qualifying nodes per taxiway (per-node gates unchanged:
   `perp ≤ halfWidth + 5`, `along ≥ 15 m`, `remaining ≥ 45 m`, `along ≤ len`).
2. Sort the taxiway's qualifying nodes by along-track distance. A gap
   **> 100 m** (`CLUSTER_GAP_M`) between consecutive nodes starts a new
   cluster.
3. Emit the minimum-perpendicular node of EACH cluster as its own
   `RunwayIntersection`.

Threshold rationale: distinct paired-exit branches measured 105–555 m apart
in the probe; polyline nodes along a single shallow exit are far denser than
100 m. Over-splitting is benign (both points are genuinely on the runway and
the labels disambiguate); under-splitting hides a real branch — the failure
that matters.

The form needs no changes for this: labels already embed remaining /
from-threshold distances, so same-named entries are naturally distinct for a
screen reader. The duplicate-label guard in `PopulateIntersections` stays and
becomes genuinely load-bearing (display rounding could in principle collide
two close clusters); it gets a comment saying so.

### 3. Full-length entrance filter (finding 5)

`GetRunwayIntersections` gains optional `lineupLat`/`lineupLon` parameters —
the start-table lineup point that full-length departures line up at (the form
already holds it in `_destinationThresholdMap`). When provided:

- Project the lineup point onto the centerline → `lineupAlong`.
- Sanity clamp: if `lineupAlong > totalLen / 2`, IGNORE the filter entirely
  (corrupt start row must not empty the list).
- Otherwise drop any meeting point with
  `along ≤ lineupAlong + 50 m` (`FULL_LENGTH_MARGIN_M`) — those are the
  normal full-length entrance, not a shortcut.

Ordinary runways are unaffected: the lineup point projects at ≈ 0 m, where
the existing 15 m minimum dominates. `PopulateIntersections` is the only call
site and passes the point when it has one; passing nothing preserves today's
behavior exactly.

### 4. Tests, PR body

`RunwayIntersectionTests` additions (same synthetic-equator idiom):

- Two-branch taxiway (branches ~300 m apart) → two ordered entries, each the
  min-perp node of its own cluster.
- Dense chain (< 100 m gaps) → still exactly one entry.
- Lineup filter: entries at/before `lineupAlong + 50 m` dropped, later ones
  kept; omitted lineup point → unchanged list; lineup point beyond
  mid-runway → filter ignored.

PR body test plan gains: paired-exit airport (e.g. KORD 22L, taxiway Y4)
lists both branches with distinct distances; KJFK 22R no longer lists the
full-length entrance connector. Full suite green locally and in CI; push to
the fork.

## Out of scope

- Any change to hold-short placement, lineup guidance, or routing — the
  destination NodeId semantics are untouched.
- Announcement behavior anywhere other than the single Calculate-time
  standstill sequence.
