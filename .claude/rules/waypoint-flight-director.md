---
paths:
  - "MSFSBlindAssist/Services/WaypointFlightDirectorManager.cs"
  - "MSFSBlindAssist/Navigation/WaypointFlightDirectorGeometry.cs"
  - "MSFSBlindAssist/Navigation/WaypointConstraintMapper.cs"
  - "MSFSBlindAssist/Navigation/WaypointTracker.cs"
  - "MSFSBlindAssist/Services/SlipCueGenerator.cs"
  - "MSFSBlindAssist/Forms/TrackFixForm*.cs"
  - "tools/WaypointFdProbe/**"
  - "tests/MSFSBlindAssist.Tests/**/*Waypoint*.cs"
---
# Waypoint Flight Director rules

Loaded with the en-route audio flight director code. Background: docs/waypoint-flight-director.md. Full text of each rule: docs/invariants/waypoint-flight-director.md.

- [WFD-1] The FD is 100% stock-SimVar and aircraft-agnostic: never add per-airframe variables or events to it; per-aircraft differences belong on `WaypointFlightDirectorProfile`, whose numbers are class defaults awaiting live tuning. Full: docs/invariants/waypoint-flight-director.md#wfd-1
- [WFD-2] The FD's `TonePitchRangeDeg` must EQUAL its profile's `MaxPitchDeg` (the FD clamps its pitch command there); it is deliberately not the same number as `VisualGuidanceProfile.TonePitchRangeDeg`. Full: docs/invariants/waypoint-flight-director.md#wfd-2
- [WFD-3] EVERY aircraft definition states `GetWaypointFlightDirectorProfile()` explicitly, even for the baseline (the Headwind A330 would otherwise inherit the A320's); never derive `BankRateLeadSec` from a measured `TaxiTurnLeadSeconds`. Full: docs/invariants/waypoint-flight-director.md#wfd-3
- [WFD-4] FD and Visual Guidance are MUTUALLY EXCLUSIVE and share the ref-counted 505 stream: each Releases only what it Acquired (`_vgHoldsStream`/`_fdHoldsStream`) and Resumes only the HandFly suppression it took. Full: docs/invariants/waypoint-flight-director.md#wfd-4
- [WFD-5] `AUTOPILOT MASTER` stays the LAST `VisualGuidanceData` field; AP-mute detection ORs it with the cached `A32NX_AUTOPILOT_1/2_ACTIVE` vars, which the FBW Airbuses drive instead of the stock simvar. Full: docs/invariants/waypoint-flight-director.md#wfd-5
- [WFD-6] There is NO spoken top-of-descent cue and NO bugless fly-a-heading mode; both were considered and dropped (the tone is the instrument; a heading is a Course on a slot). Don't re-add either. Full: docs/invariants/waypoint-flight-director.md#wfd-6
- [WFD-7] Capture-radius arrival stays ARMED (counts only once the fix was approached from outside the radius) and abeam arrival stays gated on MOVING; a course leg uses abeam only when it started far outside the fix. Full: docs/invariants/waypoint-flight-director.md#wfd-7
- [WFD-8] `AdvanceLeg` coalesces a multi-slot skip into ONE callout: every advance is an interrupting `AnnounceImmediate`, so one slot per frame produced a burst of half-spoken names. Full: docs/invariants/waypoint-flight-director.md#wfd-8
- [WFD-9] `GPS GROUND MAGNETIC TRACK` is MAGNETIC, like `CalculateMagneticBearing`: a course leg lifts the course (by the fix's `ReferenceMagVar`) and the track (by live magvar) into one true frame; never mix references or convert twice. Full: docs/invariants/waypoint-flight-director.md#wfd-9
- [WFD-10] The pitch command goes through `EffectiveAoaDeg`, never raw `aoaDeg`, and deliberately does NOT detect an addon stuck at 0.0 AoA (substituting ~5 degrees commands a persistently nose-high attitude). Full: docs/invariants/waypoint-flight-director.md#wfd-10
- [WFD-11] The slip cue's ball SIDE is UNVERIFIED in-sim: the convention lives in the single `MainForm.SlipCueBallSign` const, and code and docs say it is unconfirmed until someone has flown it. Full: docs/invariants/waypoint-flight-director.md#wfd-11
- [WFD-12] `TURN_COORDINATOR_BALL` stays `DeferredSubscription` (it streams only while Ctrl+K is on), unlike always-on `G_FORCE`; both branches sit at the TOP of `HandleSpecialAnnouncements`. Full: docs/invariants/waypoint-flight-director.md#wfd-12
- [WFD-13] The EFB "Track Slot N" handler REJECTS a position-less fix (0,0): ARINC maneuver legs parse to (0,0) and would steer the FD at null island. Full: docs/invariants/waypoint-flight-director.md#wfd-13
- [WFD-14] The constraint TYPE comes from the raw ARINC `alt_descriptor` (`WaypointFix.AltDescriptor`), never the formatted `AltitudeRestriction` string, and a single-bounded `B` maps to AtOrAbove, never dropped. Full: docs/invariants/waypoint-flight-director.md#wfd-14
- [WFD-15] The pure math (`WaypointFlightDirectorGeometry`, `WaypointConstraintMapper`) is guarded by the xUnit suite, not `tools/WaypointFdProbe` (standalone, never run by CI): a case added to the probe must be added to the tests. Full: docs/invariants/waypoint-flight-director.md#wfd-15
