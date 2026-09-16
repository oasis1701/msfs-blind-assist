# PR #238 — deferred runway-geometry and hold-placement findings

> **For agentic workers:** this document is the complete handover. Everything needed to pick any
> item up — the mechanism, the measured evidence, the file and line, the tripwires, the tests to
> write and the flights to fly — is here. You should not need the review conversation that produced
> it. REQUIRED SUB-SKILLS: `superpowers:systematic-debugging` before proposing a fix,
> `superpowers:test-driven-development` for every change.

**Where this came from.** A max-effort review of PR #238 (*"fix(taxi): test runway crossings against
the pavement, not the start rows"*) run against the rebuilt fs2024 navdata. Nineteen candidates were
raised; four were fixed inside PR #238 (commit `a1da2793`), one was **withdrawn after measurement**
(§9), and the ten below were deliberately deferred. PR #238 merged with these open.

**Why they were deferred rather than fixed.** Every one of them sits on, or next to, the
landing-rollout and runway-geometry code, where a cluster of safety distances is *derived* from
other constants with nothing in the code linking them (§0). Several can only be confirmed by flying
them. Deferring was a judgement that shipping an unverified change to a blind pilot's runway-safety
path is worse than shipping a known, documented gap.

> ### ⚠ This document is TEMPORARY — delete it and its two references when the work lands
>
> It is a work list, not a permanent record, and it must not outlive the findings it describes. The
> PR that closes the last open item **must** remove all three of these in the same commit:
>
> - [ ] this file, `docs/design/2026-09-16-pr238-deferred-runway-findings-plan.md`
> - [ ] the "Known open items" banner at the top of the **Runway crossings and entries** section in
>       `docs/taxi-guidance.md`
> - [ ] the **PR #238 deferred runway findings** entry in `CLAUDE.md`'s *Available documentation* list
>
> `grep -rn "2026-09-16-pr238-deferred-runway-findings-plan" --include=*.md .` must return nothing
> afterwards. Closing only *some* items: strike those sections through and leave the rest, but do
> **not** remove the references until every item is closed or explicitly withdrawn.
>
> Anything here that turns out to be a durable rule rather than a task — most likely §0's
> derived-constant tripwire and §9's two "do not fix these" measurements — should be **moved into
> `CLAUDE.md`'s taxi-guidance invariants or `docs/taxi-guidance.md` before this file is deleted**, so
> the knowledge survives the cleanup. Do not simply delete §0 and §9.

---

## 0. Read this first — the derived-constant tripwire

This is the single most dangerous thing in the area. Several rollout constants are **arithmetic
consequences** of two margins, and nothing in the code, the compiler or the tests links them:

| Constant | Where | Derived from |
|---|---|---|
| `RolloutExitGate.VacatedShortAlongTrackFeet` = 350 | `RolloutExitGate.cs:149` | the exact **5 m** gap between the exit-node corridor (`halfWidth + 15 m`) and the pavement boundary (`halfWidth + RunwayClearMarginM`) |
| `RolloutExitGate.EarlyVacateMaxPassedFeet` = 1400 | `RolloutExitGate.cs:118` | same gap |
| the 25 m corridor clamp | handoff reachability | same gap |
| `RolloutExitGate.RunwayClearMarginM` = 10 | `RolloutExitGate.cs:220` | the codebase's ONE definition of "off the runway" |
| `RolloutExitGate.DefaultRunwayWidthFeet` = 200 | `RolloutExitGate.cs:226` | fallback half-width, **different** from `RunwayShape.DefaultHalfWidthMeters` (75 ft) |

**If you change a half-width or either margin, those three numbers silently stop being derived.**
There is no compile error. The boundary tests keep passing because they pin the *old* arithmetic.
`RunwayVacateResolver` additionally keeps its **own** copy of the 75 ft default and its own
`SameRunwayLateralM = 30.0`, the latter calibrated against the residual scatter left by
`TaxiGraph.SnapStartToRunwayCenterline` — so loosening the snap invalidates it too.

Before touching any tolerance in this area, re-derive all five and say so in the commit message.

---

## 1. The landing re-crossing guard is blind when the route's anchor node is on the pavement

- **File:** `MSFSBlindAssist/Navigation/RolloutRunwayReCrossing.cs:48` (`RouteReCrossesRunway`)
- **Severity:** high — the failure direction is *accepting* a route back across the runway you landed on.
- **Confidence:** plausible; not reproduced in sim.

**Mechanism.** `RunwayRouteClassifier` deliberately emits no passage when the first node it judges is
already ON the runway (`runOpen` closes with `runEntry == -1`, under the comment *"no entry: the route
started on the runway and vacated"*). `RouteReCrossesRunway` builds its node list with
`RunwayRouteClassifier.NodesFrom(segments, fromSegmentIndex)`, which starts at
`segments[fromSegmentIndex].FromNode` — the node **behind** the aircraft — and, unlike the hold pass,
does **not** prepend the aircraft's own position.

The guard only declines while the aircraft is still laterally on the landing runway, and in exactly
that window the A* anchor node is routinely on the pavement (the PR's own KATL fixture uses B1 at
about 2.5 m from the 26R centreline). If A* then routes the first edge straight to the far side, the
run opens with no preceding clear node, no passage is emitted, the guard returns false and the
handoff is **accepted** — the tone steers the aircraft back across the runway it just landed on.
That is the KATL 26R failure the guard exists to prevent. The removed per-edge
`TaxiGraph.EdgeCrossesRunwayStatic` caught it regardless of node placement.

**The mirror case is already reported** as a false *positive*: because the guard now also refuses an
**entry**, and the shape is the real pavement (wider and longer than the old start-row line), a
normal exit route whose junction merely touches the pavement can be refused, concluding landing-exit
guidance with *"Exit guidance ended: no usable route from here. Stop and hold position"* over a
perfectly good exit. Both directions are real and they do not cancel.

**The fix.** Promote the aircraft-prepend out of `RouteRunwayCrossings.ClassificationNodes` (private,
`RouteRunwayCrossings.cs`) into a public
`RunwayRouteClassifier.NodesFrom(segments, fromIndex, AircraftPosition?)` overload, and have BOTH the
hold pass and this guard call it. "The aircraft is the route's first point until it has rolled 10 m"
then has one definition instead of a rule the docs state generally and one caller implements
privately. `HandoffRouteReCrossesLandingRunway` already has the aircraft lat/lon at both call sites.

**Tripwires.** `RolloutRunwayReCrossingTests` pins specific KATL and KORD geometry, including
`An_end_exit_whose_junction_sits_centimetres_over_the_centerline_is_not_a_re_crossing` (node at
−0.292 m, KORD 10R W5) and `The_live_KATL_handoff_route_re_crosses_the_landing_runway`. Both must
still pass. Do not "fix" this by reverting to the per-edge intersection: its centimetre verdicts are
what lost P19/KBDN's crossing and invented ESMX's and KORD W5's.

**Tests to write first.**
1. A route whose node 0 is on the pavement and whose first edge spans to the far side **is** a
   re-crossing once the aircraft (off the pavement) is prepended.
2. The same route, aircraft ON the pavement, is still nothing (it started on the runway and left).
3. A normal same-side exit whose junction sits centimetres over the line is still not a re-crossing.

**In-sim.** Land at KORD 10R, take W5: the handoff proceeds with no *"Continue rolling to taxiway W5"*
loop. Land at KATL 26R, miss B1, let it retarget: the re-route must be refused, not steered across 08L.

---

## 2. `allowStartHold` is a second, differently-shaped answer to "is the aircraft on the runway"

- **Files:** `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs:523` and `:1439`
- **Severity:** medium — drops legitimate start holds; combines with §3 to announce them misleadingly.

**Mechanism.** Both landing-handoff sites compute
`bool offRunwayAtHandoff = !IsWithinRolloutRunwayLaterally(lat, lon)` and pass it as
`allowStartHold`. That predicate (`TaxiGuidanceManager.MathUtils.cs:209`) is **lateral-only, single-
runway, along-track-unbounded**, applies `RolloutExitGate.IsLaterallyClearOfRunway` (half-width + 10 m)
and defaults to a **200 ft** width. `RouteRunwayCrossings.PlaceHold` then decides the same question
again with `RunwayUnder` → `RunwayShape.Contains`, which is **extent-bounded, zero-margin**, and
defaults to a **75 ft** half-width.

Divergences on the same lat/lon in the same call chain:
- an aircraft that has rolled off the **far end** of the runway (short field, overrun, backtrack
  turnaround) is still inside the infinite strip, so `offRunwayAtHandoff` is false and a legitimate
  start hold on the re-route is refused;
- an aircraft 5 m outside the pavement edge is inside the 10 m margin and gets the same refusal,
  where `RunwayUnder` would have allowed it;
- on a width-less centreline the two disagree over a 7.6 m band by construction.

A refused start hold is recorded `Held = false` and — since PR #238's fix — **announced** as
*"with no hold short point for runway X"*, which is now a visible symptom rather than a silent one.

**The fix.** Delete the parameter and the `phase == "load"` re-derivation
(`TaxiGuidanceManager.Routing.cs`, `ApplyAutoHoldShortPasses`), and let `PlaceHold` decide from the
aircraft position it already receives. The one thing the bool genuinely encodes that the pass cannot
currently see is *"the aircraft is moving / already committed"* — supply ground speed instead. Note
the current plumbing converts bool → `"load"`/`"touchdown"` string → bool, so a fourth phase or a
typo silently disables start holds with no compile error.

**Tripwires.** §0. Also: `A_recalculated_route_never_starts_held` and
`No_start_hold_while_the_aircraft_stands_on_another_runway` must keep passing, and the
`phase=` token must stay in the `Route crossings:` log line.

---

## 3. `IsClearOf` is lateral-only while `Contains` is extent-bounded — the limbo band past a runway end

- **File:** `MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs:637` (walk 2), with the mirror at walk 1
- **Severity:** medium; narrow trigger.

**Mechanism.** `RunwayShape.Contains` requires `along` inside the extent; `RunwayShape.IsClearOf`
tests `|lateral|` only. A node **beyond** the runway's along-track extent but near its axis is
therefore neither "on the runway" nor "clear of" it. Walk 2 steps over it — and over every node
behind it — and falls through to `HoldStop(0)`, a **start hold**: the pilot is told
*"Stop. Hold short of runway 09"* before moving, hundreds of metres from the real hold line, while
no hold is placed where the route actually meets the pavement.

With the test fixture's numbers (half-width 30 m, clear margin 10 m, extent 0…3000 m) a node at
along −50 m, lateral 0 m demonstrates it. Trigger shape: a taxiway running off the end of a runway on
or near its extended centreline — a turnpad lead-in, or any approach to a crossing from beyond the end.

Walk 1 has the mirror asymmetry: it tests a bare `|lateral| > HalfWidthMeters` with **no** margin,
so an on-axis scenery hold line beyond the end is rejected as being "on the pavement", while walk 2
three lines later demands `IsClearOf` (half-width + 10 m). Two spellings of "off the pavement" in one
method, and `docs/taxi-guidance.md` states flatly that a stop is *"never placed inside the 10 m
margin"*, which walk 1 can do.

**The fix.** Give `RunwayShape` an extent-aware `IsClearOfAt(along, lateral)` — true when outside the
extent OR beyond half-width + margin — and use it in both walks. **Do not change
`RolloutExitGate.RunwayClearMarginM` itself** (§0); `IsClearOf` is `RunwayShape`'s own wrapper and
can be widened in meaning without touching the constant.

**Decide deliberately** whether walk 1 keeps the looser bare-half-width test. It exists so a scenery
hold line hugging the pavement edge is still usable; tightening it to `IsClearOf` would reject real
hold lines (measured: SC99's line is 7.2 m out on a 4.0 m half-width). Whichever you choose, make the
code, the method doc and `docs/taxi-guidance.md` agree — today one of the three is wrong.

**Tests to write first.** A route approaching a crossing from beyond the runway end places its hold
at the real node, not as a start hold. Plus a node in the half-width…half-width+10 m band on walk 1.

---

## 4. The extent envelope turns a bad start row into up to 800 m of extra runway

- **File:** `MSFSBlindAssist/Navigation/RunwayShape.cs:71` (the `usesPavement` envelope)
- **Severity:** medium — a wrong spoken position, plus silently skipped holds.

**Mechanism.** When the pavement is used, the extent is widened to envelope the centreline's start
rows. A start row outboard of the pavement therefore extends the runway.

**Measured** over 419 real centrelines at 300 fs2024 airports: exactly **three** start rows sit
outboard at all, at **29.5 m, 471.2 m and 799.9 m**. The 29.5 m one is legitimate; the other two are
bad data (KSAW 01, LIMC 17L).

At LIMC three graph nodes on **taxiway AB** fall inside the 800 m band. Running the live code at
their coordinates:
- `DescribeLocation` (Where-Am-I) answers **"Runway 17L"** while the aircraft is on a taxiway;
- `TryGetRunwayAtPosition` answers true, **"35R"** (this seeds takeoff assist);
- `RunwayUnder` answers 35R/17L, so a start hold there is silently skipped, logged only as
  *"aircraft on the pavement of runway 35R/17L"*;
- `IsOnAnyRunway` bars those nodes from ever being a hold stop and latches `crossedOther` past them,
  so a crossing whose only safe stop lies out there is reported unheld.

Two of the three nodes were already claimed on `main`; the third (28.9 m lateral) is newly claimed
because the half-width grew from the 22.86 m default to LIMC's real 30 m.

**Why it was not simply capped.** A 100 m cap separates the legitimate 29.5 m case from both bogus
ones with wide margins. But the envelope is deliberate and pinned by
`RunwayShapeTests.The_extent_covers_an_outboard_start_row`, which is built on the LIMC shape and
asserts a point 200 m beyond the pavement IS on the runway. More importantly, **runway-destination
lineup anchors on the `start` table** (a standing invariant — `Runway.StartLat/StartLon` is the
pavement edge and routes the aircraft to a node off the runway at displaced thresholds). At LIMC 17L
the lineup target therefore IS that bogus outboard row, so capping the extent could make the lineup
point "not on the runway" and break the reach test for that runway.

**The right fix is upstream:** repair or reject the bogus start row where it enters the graph, rather
than narrowing the frame that consumes it. `SnapStartToRunwayCenterline` already refuses rows past
midfield and returns others unchanged — extend that judgement to rows outboard of the pavement ends,
which the runway table gives you directly. Then the envelope has nothing pathological to absorb and
can be capped or dropped. **Do not** widen `SnapStartToRunwayCenterline` into an along-track
relocator: projecting rows onto their named runway once put both of an airport's rows at midfield,
failed the 200 m separation test, and cost AYCH the centreline it had.

**Verification recipe** (§10): re-run the `shape` and `envelope` sweeps; the legitimate 29.5 m case
must stay inside, LIMC's three AB nodes must stop being claimed, and `detect` must still report
0 misses at every start row.

---

## 5. Where-Am-I and takeoff assist name opposite runway ends at the same point

- **File:** `MSFSBlindAssist/Navigation/TaxiGraph.cs:1848` (`TryGetRunwayAtPosition`'s end choice)
- **Severity:** medium, bounded to airports with self-contradictory navdata.

**Mechanism.** `DescribeLocation` names the end through `RunwayShape.NameAt` (the **pavement** frame).
`TryGetRunwayAtPosition` migrated its *membership* test to `RunwayShape` but still picks the **end**
from `rwy.HeadingDeg1` and `rwy.Lat1/Lat2` (the **start-row** frame) — the PR's own comment says
*"The threshold point, heading and end choice below are unchanged"*, which is precisely the defect:
half the method moved.

On a name-swapped centreline the two frames are reversed. **Measured** over 419 centrelines, standing
25 % along the runway, six disagreed; four unambiguously the same centreline naming opposite ends:

| Airport | Where-Am-I says | Takeoff assist says |
|---|---|---|
| AYCH 03/21 | 03 | 21 |
| OIII 11R/29L | 11R | 29L |
| URWW 05/23 | 05 | 23 |
| EDVQ 27C/09R | 27C | 09R |

Verified against the runway table: at these airports the `start` row labelled "03" physically sits at
the 21 threshold **carrying 21's heading**. The navdata is self-contradictory; `TaxiGraph.Build`'s
heading pass exists to rescue exactly these airports.

A blind pilot asking Where-Am-I is told one runway while the takeoff-assist reference seeded at the
same spot carries the other, along with that end's threshold coordinates.

**The fix, and why it is not a one-liner.** Make `RunwayShape` pair each of its ends with the start
row **nearest that end by position**, rather than by name index, and expose that pairing. Then name,
threshold and heading always travel together: at AYCH the shape's end 1 is the pavement 03 end, the
start row nearest it is the one *labelled* 21, and the correct answer is "you are at the 03 end, line
up here". **Do not** simply return the pavement end as the threshold: runway-destination lineup must
stay anchored on the `start` table.

**Tripwires.** `MatchHoldShortRunwayName` deliberately keeps MATCHING on the start rows (at EGKK the
hold nodes between 26L and 26R are 26L's lines, and matching against 26R's pavement would rename
them) — only the END it names uses the shape. Do not "harmonise" that. `RunwayMembershipTests` pins
the Where-Am-I +5 m / takeoff-assist strict split; keep it.

---

## 6. A shared stop guarding two runways is cleared by one Continue

- **File:** `MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs:880` (`ComposeSharedLabel` in `PlaceHold`)
- **Severity:** medium; an architectural question, not a bug to patch.

**Mechanism.** When two runways resolve to the same stop, the label merges
(*"runway 09 and runway 01"*) and one segment is tagged. Guidance has exactly one `HoldShort` state
and one `ContinuePastHoldShort` per stop point, so a single Continue authorises crossing both. PR
#238's start-hold fix follows the same design (a start hold can now name two runways).

This repo states the opposing rule in its own words at `TaxiGuidanceManager.cs:3139`:

> explicit crossing clearance is required for **EACH** runway — controllers issue them one at a time,
> an aircraft must have crossed the previous runway before the next crossing clearance is issued.

**Measured frequency:** 0 of 2,518 sampled fs2024 routes produced a start hold naming more than one
runway, and shared segment stops are likewise rare. Real but uncommon.

**The fix** is a multi-stage hold that releases one runway per Continue: the stop holds a *list* of
designators, `HandleHoldShort` announces the first, and Continue pops it and either re-announces the
next or resumes. That is new state on a safety path and needs the owner's design agreement before
implementation — raise it as a question, not a patch.

---

## 7. An explicit pilot hold-short pick can vanish from the route summary

- **File:** `MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs:774` (`ApplyUserRunwayHold`'s `out _`)
- **Severity:** low-medium — a surprise stop, not a missed one.

**Mechanism.** `ApplyUserRunwayHold` places the stop but records **no** `TaxiRouteRunwayEvent` (its
out parameter is discarded), and `InsertRunwayHoldShorts` then resets `route.RunwayEvents`. When the
automatic pass skips that same passage — the destination-strip arrival skip — the pick is named
nowhere: `DescribeRunwayEvents` says nothing, and `CountNonRunwayHoldShorts` also skips it because its
label *does* name a runway. The pilot picks *"hold short of runway 04R"* on a route to 04R, hears no
mention of it in the summary, and is then stopped by a hold they were never told about.

**The fix.** Either record the event from the user pass and have `InsertRunwayHoldShorts` preserve
pre-existing events (watch for double-counting when the automatic pass classifies the same passage),
or narrow the destination-strip skip so a passage carrying a user pick is still recorded.

**Tripwire.** The destination-strip skip has its own incident history: a blanket same-runway skip once
dropped genuine mid-route crossings of the active runway (2026-08-24). Skip **only** the route's own
final arrival.

---

## 8. Two performance items

**8a. `LandingExitForm` refresh blocks the UI thread**
(`MSFSBlindAssist/Forms/LandingExitForm.cs:518`). `TaxiGraph.BuildAsync` is `Task.Run(() => Build(…))`,
so only the build is off-thread — the argument expressions, including a full `GetRunways(icao)` (a
30-column SELECT with three JOINs and two correlated ILS subqueries, plus a lazy orphan-ILS lookup)
and `GetNamedSpots` (directory listing, JSON read, navdata query, overlay, re-augment), run inline on
the UI thread while the Landing Exit dialog is open and a screen-reader user may be arrowing the exit
combo. The first load (`:371`) does the same, and calls `GetRunways` twice.

**Why it was not moved.** Doing so puts two threads on the gate-source and provider caches
concurrently; today the message loop serialises them, and the repo explicitly warns against handing
one gate-source instance to two threads. **Make those caches thread-safe first**, then move the
argument evaluation inside the `Task.Run`.

**8b. `RunwayShape.For` allocates twice per call**
(`MSFSBlindAssist/Navigation/RunwayShape.cs:106`). `PavementIsUsable` builds a throwaway `RunwayShape`
purely to reuse `Project`, then `For` discards it and builds a second. This multiplies across every
hot path the PR added: per runway per classification, per passage for `otherRunways` (rebuilt inside
`PlaceHold` and thrown away unread on the start-hold path, then re-derived by `RunwayUnder`), per node
per runway in `IsOnAnyRunway`, per runway per Where-Am-I keypress, and once per hold node per
candidate runway inside `TaxiGraph.Build`'s naming pass — hundreds of nodes at a large airport, on a
path that runs synchronously on the UI thread for a Where-Am-I cache miss.

**The fix.** Build the pavement candidate once, and widen its extent after the verdict rather than
constructing a second shape — this needs `ExtentMin/MaxMeters` to become settable by a private
method. Better still, memoise one `RunwayShape` per `RunwayCenterline` (the centreline is immutable
after `ApplyPavement`), which makes every call site O(1) after the first. **Do not** inline a second
copy of the projection math — this area already has four copies and that is its own finding.

---

## 9. Withdrawn — do not "fix" this

**The narrower lateral band is correct.** It was reported that replacing the fixed 75 ft default with
the runway table's real half-width loses off-centreline detection: at 8 m off, 53 of 419 runways are
missed against `main`'s 12.

**Measurement refutes it.** The nearest off-runway graph node sits **3.2 m** from a runway centreline
at p0 and **5.1 m** at p1; at SC99 a taxiway node is **4.2 m** from the centreline of a runway whose
half-width is **4.0 m**. There is no headroom to widen the band without claiming the adjacent
taxiway. At the realistic offset the PR is already better than `main` (3 misses of 419 at 3 m off,
against 12). 42,661 of 48,321 runways are narrower than 150 ft, so this affects most of the database —
and on all of them the strict real-width test is right and `main`'s fixed band was wrong.

**Related, also do not change:** `RouteProgressMeters` returning `0.0` for both "at the route start"
and "not near this route" (`RouteRunwayCrossings.cs:323`). It reads like a bug, but the 30 m
`RouteJoinMaxCrossTrackMetres` bound exists so that an aircraft stopped at the KORD 04L hold line,
90 m beside a route that starts along the runway, is still treated as the route's first point and
keeps its start hold. The "fabricated start holds" originally measured came from displacing the
aircraft perpendicular to its own route by up to 1,500 m, which production cannot produce because the
route is built from the aircraft's position. Separating the two meanings would undo the KORD guard.
If you touch it, keep the prepend behaviour identical and change only the naming.

---

## 10. Reproducing the measurements

No harness is committed. The numbers in this document came from a throwaway console project that
references the main assembly and drives the real code over the shipped database. To rebuild it:

```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Platforms>x64</Platforms>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\MSFSBlindAssist\MSFSBlindAssist.csproj" />
  </ItemGroup>
</Project>
```

Then, per airport:

```csharp
var provider = new LittleNavMapProvider(dbPath, "FS2024");      // %APPDATA%\MSFSBlindAssist\databases\fs2024.sqlite
var graph = TaxiGraph.Build(provider.GetTaxiPaths(icao), provider.GetParkingSpots(icao),
                            provider.GetRunwayStarts(icao), provider.GetRunways(icao));
var router = new TaxiRouter(graph);
var route  = router.FindShortestPath(startNodeId, endNodeId);
RouteRunwayCrossings.InsertRunwayHoldShorts(route, graph.RunwayCenterlines, "", allowStartHold: true,
    new RouteRunwayCrossings.AircraftPosition(lat, lon));
```

Sample airports by striding alphabetically across every airport that has both `taxi_path` rows and
`runway` rows, with the named cases first (OMDB, EGKK, KORD, KATL, EHAM, ESMX, KBDR, P19, F43, SC99,
LIMC, EDVQ, KBOS, KSFO, LEBL, KSAW, AYCH, OIII, URWW). Useful sweeps, with the numbers this document
cites so a change can be compared against them:

| Sweep | Baseline at PR #238 (`a1da2793`) |
|---|---|
| runway detected at every start row / mid-pavement / 20 m inside the pavement end | 419/419, 0 misses each |
| hold stops on the pavement of ANY runway | 0 of 2,041 |
| old per-edge test vs the classifier, 1,893 routes | both 794 · classifier-only 1,241 · **per-edge-only 0** |
| centrelines whose pavement is rejected (falls back to start rows) | 2 (both EDVQ) |
| start rows outboard of the pavement | 3 — 29.5 m, 471.2 m, 799.9 m |
| nearest off-runway node lateral distance | p0 3.2 m · p1 5.1 m · p50 23.4 m |
| routes with an unheld runway (of 2,518) | 18 |

**The one that must never regress:** per-edge-only = 0. That is "the new classifier loses no crossing
the old test found".

---

## 11. Suggested order

1. **§1** (landing guard) — highest severity, and the fix is a shared helper both callers want.
2. **§2** (`allowStartHold`) — removes a whole class of disagreement; do it with §1, same area.
3. **§4** (bad start rows, upstream) — unblocks capping the extent and fixes a wrong spoken position.
4. **§5** (end naming) — depends on §4's understanding of the start rows.
5. **§3** (limbo band) and **§7** (pick not announced) — small, independent.
6. **§8** (performance) — after the correctness work; §8a needs thread-safety first.
7. **§6** (one Continue per runway) — design question for the owner, not a patch.

Each should follow the same route this work did: write the failing test first, fix, re-run the full
suite (`dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`),
re-run `tools/ProgressiveTaxiProbe`, then re-run the §10 sweeps against the real database and quote
the before/after numbers in the PR.
