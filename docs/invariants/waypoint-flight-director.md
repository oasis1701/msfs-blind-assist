# Waypoint Flight Director rules — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/waypoint-flight-director.md`, which Claude Code loads when it reads matching code. Background: [waypoint-flight-director.md](../waypoint-flight-director.md). The text is taken from that document.

## WFD-1

- The FD is 100% stock-SimVar and aircraft-agnostic — never add per-airframe variables/events to it; per-aircraft differences belong on `WaypointFlightDirectorProfile`, and every profile number is a best-effort class default still awaiting live in-sim tuning (there is no autopilot to verify hand-flying against). → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-2

- The FD's `TonePitchRangeDeg` must EQUAL that profile's `MaxPitchDeg` — the FD clamps its pitch command to MaxPitchDeg, so a tone saturating earlier cannot represent commands the FD itself generates (it inherited VG's 6° and pinned the tone at full frequency through any normal climb on every profile except the PMDG 777). It is deliberately NOT the same number as `VisualGuidanceProfile.TonePitchRangeDeg`, which covers a narrow approach envelope and is sized for beat sensitivity instead. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-3

- EVERY aircraft definition must state `GetWaypointFlightDirectorProfile()` explicitly, even when it just takes the baseline — `HeadwindA330Definition` derives from `FlyByWireA320Definition`, so an omitted override silently inherits an EXPLICIT narrowbody profile on a heavy widebody and reads as deliberate. Never derive `BankRateLeadSec` from a measured `TaxiTurnLeadSeconds`: the taxi figure is ground steering dominated by pilot rollout anticipation (PMDG 777: taxi 0.3 s vs FD lead 1.3 s). The TFDi MD-11 was the one definition that took the base class's A320 baseline silently (it derives from `BaseAircraftDefinition`, not an airliner); since 2026-10-07 it states the unmeasured heavy-widebody class profile the 787 and A330 carry. An aircraft added later (the open DA40, Learjet 35A and Citation 680 PRs among them) states its own too. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-4

- FD and Visual Guidance are MUTUALLY EXCLUSIVE and share the ref-counted 505 stream — each feature must Release only what it Acquired (`_vgHoldsStream`/`_fdHoldsStream`) and Resume only the HandFly suppression it took (`_vgSuppressedHandFly`/`_fdSuppressedHandFly`); an aborted activation otherwise tears down the OTHER feature's stream or un-mutes HandFly under a running FD. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-5

- `AUTOPILOT MASTER` must stay the LAST `VisualGuidanceData` field so existing offsets are unchanged; AP-mute detection must OR it with the cached `A32NX_AUTOPILOT_1/2_ACTIVE` vars — the FBW Airbuses do not drive the stock simvar, so without the OR the auto-mute silently never fires on them. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-6

- There is NO spoken top-of-descent cue and NO bugless "fly a heading" mode — both were considered and deliberately dropped (the tone is the instrument; a heading is flown by setting a Course on a slot). Don't re-add either. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-7

- Capture-radius arrival must stay ARMED (only counts once the fix has been approached from outside the radius) and abeam arrival must stay gated on MOVING — dropping either lets engaging parked or overhead a fix cascade through every slot. A course leg uses abeam only when it started far outside the fix; an outbound radial starts behind it, where abeam misfires. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-8

- `AdvanceLeg` must coalesce a multi-slot skip into ONE callout — every advance is an `AnnounceImmediate`, which interrupts, so advancing one slot per frame produced a burst of half-spoken waypoint names. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-9

- Ground track (`GPS GROUND MAGNETIC TRACK`) is MAGNETIC, matching `CalculateMagneticBearing` (`magnetic = true − variation`) — a course leg lifts BOTH the course (by the fix's `ReferenceMagVar`) and the track (by the aircraft's live magvar) into one true frame. Never mix the two references, and never convert twice. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-10

- The pitch command must go through `EffectiveAoaDeg`, never raw `aoaDeg` — that is the only thing applying the profile's `TypicalApproachAoaDeg` fallback. It deliberately does NOT try to detect an addon stuck at 0.0 AoA: substituting ~5° over a real 0° commands a persistently nose-high attitude, which is worse than the flat command. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-11

- ⚠️ The slip cue's ball SIDE is UNVERIFIED in-sim. The convention lives in the single `MainForm.SlipCueBallSign` const (flip to `-1.0` if a live check shows it backwards) — a reversed cue tells a blind pilot to press the WRONG pedal, so keep code and docs agreeing that it is unconfirmed until someone has flown it. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-12

- `TURN_COORDINATOR_BALL` must stay `DeferredSubscription` — it is a SIM_FRAME var for an off-by-default cue, so it only streams while Ctrl+K is on. Contrast `G_FORCE`, which must stay always-on because it captures a touchdown spike that cannot be requested retroactively. Both branches sit at the TOP of the `HandleSpecialAnnouncements` ladder so lower branches don't re-test their VarName every frame. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-13

- The EFB "Track Slot N" handler must REJECT a position-less fix (`Latitude==0 && Longitude==0`) — ARINC maneuver legs (CA/VA/VM/FM/CI/VI/CD/VD/CR, ~14% of legs) parse to (0,0) and would steer the FD at null island — with ONE exception: a leg that `WaypointConstraintMapper.FromFix` gives both a course and a terminating altitude (CA/FA/VA "climb course 220° to 500 ft", the opening leg of most SIDs and missed approaches). That leg is completely specified without a position, and the FD flies it as a course hold ended by the altitude (`WaypointFlightDirectorManager.ProcessToAltitudeLeg`). A bare intercept leg, or a fix whose coordinates did not resolve, is still refused aloud.

Updated 2026-10-07 (#269's port review): the line still said every position-less fix was rejected after the to-altitude legs became flyable. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-14

- The constraint TYPE comes from the raw ARINC `alt_descriptor` (`WaypointFix.AltDescriptor`), never the formatted `AltitudeRestriction` string (a fallback only) — and a single-bounded `B` maps to AtOrAbove (the floor), never dropped. → [waypoint-flight-director.md](../waypoint-flight-director.md)

## WFD-15

- The pure math (`WaypointFlightDirectorGeometry`, `WaypointConstraintMapper`) is guarded by the xUnit suite, NOT by `tools/WaypointFdProbe` — the probe is standalone, absent from the solution, and never run by CI. A case added to the probe must be added to the tests too or it protects nothing. → [waypoint-flight-director.md](../waypoint-flight-director.md)
