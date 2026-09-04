# Waypoint Flight Director (audio)

A **synthetic, audio flight director** that guides a blind pilot **hand-flying** to the waypoints
tracked in the 5 Shift+F slots. It is the missing middle of a flight: **takeoff tone → en-route
flight director → landing tone.** Before it, a blind pilot had to engage the autopilot immediately
after takeoff because there was no way to hand-fly a climb, a level-off, a vector, or a leg to a fix.

It is the same **dual-tone "match the two tones"** idiom as Visual Landing Guidance, generalised
from the final approach to the en-route phase, and it is **completely global** — computed from stock
SimVars only, so it works on **any aircraft, IFR or VFR, with no autopilot, no real flight director,
and no per-aircraft code.**

## What you hear

Two tones play at once:

- **Desired tone** — the flight director's *command*. Its **stereo pan = commanded bank** (how much
  to roll, and which way), its **frequency (pitch) = commanded pitch** (climb/descend).
- **Current tone** — your aircraft's *actual* attitude (pan = actual bank, frequency = actual pitch).

You fly to make the two tones **identical** — pans matched (centred bank command satisfied) and
frequencies zero-beat (pitch matched). When they match, you are tracking the leg.

## How to use

1. **Track your fixes.** Open the Track Fix window (input mode → **Shift+F**), enter a waypoint, pick
   a slot (1–5), and Track. Fill slots **1 → 5 in the order you want to fly them** — the FD walks them
   in order. You can also track straight from the **Electronic Flight Bag route viewer (Shift+E)** —
   right-click (or the context-menu key) a waypoint and pick "Track Slot N". Instead of tracking
   silently, that **opens the Track Fix window pre-filled** with the fix, the slot, and the altitude
   constraint + course **mapped from the fix's own navdata** (e.g. a SID leg "at or above 6000", a STAR
   fix "between 16000 and 19000", an airway course) — you just review and press **Track**, or tweak
   anything first (add an altitude the navdata didn't carry, change the constraint, clear the course).
   So the auto-filled constraint is always visible and editable, not hidden. (Courses are used when
   magnetic and present; otherwise the leg flies direct-to.) A position-less leg — an ARINC "maneuver"
   leg with no fix, e.g. *"Climb heading 071° to 600 feet"* — can't be tracked (no point to fly to); the
   app says so and opens nothing.
2. **(Optional) Add a crossing altitude.** In the Track Fix window, enter a **Crossing Altitude**
   (feet MSL) and pick a **constraint** (At / At or above / At or below / Between). Leave the altitude
   blank for **lateral-only** guidance at that fix. The **Upper Altitude** box appears **only when you
   select "Between"** (it's not used by the other constraints).
3. **Engage.** Output mode → **Ctrl+F**. The FD starts on the first **filled** slot and announces the
   active leg. If *no* slot holds a waypoint it says "No waypoints to track" and does nothing.
4. **Hand-fly** to match the tones. On reaching each fix the FD announces the next leg
   (e.g. *"Next, TOPM, 18 miles, bearing 102."*) and sequences automatically.
5. **It stops** after the last filled slot ("Final waypoint reached"). Empty slots *between* filled
   ones are skipped, not treated as the end — tracking into 1, 2 and 4 flies 1 → 2 → 4. Press
   **Ctrl+F** again to turn it off at any time.

The FD is an **audio overlay only — it never touches the controls or the autopilot.** It does not
validate your route: put the right fixes in the right slots in the right order; it walks them as given.

## Lateral guidance

Track error = `bearing-to-fix − GPS ground track` (using **ground track**, not heading, means
nulling the error flies a straight, **wind-corrected** path — no chasing the bearing). The commanded
bank is a proportional roll law (small error → small bank, capped ~25–28°) with **rate-lead
anticipation** so turns roll out cleanly instead of overshooting. Below a per-aircraft speed floor
(ground track is unreliable slow/on the ground) it falls back to heading. The commanded bank (and
pitch) are **rate-limited between frames**, so the steering tone moves smoothly into and out of turns
instead of wobbling on track/heading jitter.

## Course / radial tracking (optional)

By default the FD flies **direct to** each fix. If you set a **Course** on a slot (in the Track Fix
window), that leg instead **captures and holds the course line through the fix** — an airway leg, an
approach course, or a VOR radial. It works like flying a localizer needle by ear: off the line the
command banks you to intercept it (steeper the further off, shallowing as you close in); once
established it holds you on the line, wind-corrected. Enter the course you want to *fly* (magnetic) —
the fix is just a point on the line, so the same field covers inbound courses and outbound radials. A
course leg sequences when you reach the fix (capture radius); an outbound radial simply holds until
you turn the FD off or advance.

### "To altitude" legs (CA / FA / VA)

A SID's initial climb is usually an ARINC **course-to-altitude** leg — VCBI's ANUT1D opens with
*"climb course 220° to 500 ft"*. These carry a course and a target altitude but **no fix at all**, so
they reach the FD with no position. They are still completely specified, and the FD flies them:

- **Laterally** as a pure course hold — there is no fix to measure a cross-track against, so the
  intercept term drops out and the command just holds the course, still wind-corrected through
  ground track.
- **Vertically** at the profile's pitch limit until the altitude is made, then level. There is no
  distance, so the required-FPA geometry has nothing to work with; a SID initial climb is flown at
  the aircraft's climb capability anyway.
- **Sequencing** on ALTITUDE rather than distance or abeam — "+" (at or above) ends on reaching it,
  "−" (at or below) on being under it.

Legs that are position-less *and* underspecified — a bare CI/VI intercept, or a fix whose
coordinates could not be resolved — are still refused, because there is nothing to fly toward.

**Magnetic → true is referenced correctly.** A magnetic course isn't relative to today's variation at
your position — a VOR radial is defined by the *station's declination* (VORs are re-aligned rarely, so
that can differ from the current value by several degrees), and an airway/CF leg by the local variation
where it's defined. The FD captures that **reference variation from navdata** (the referenced navaid's
declination, else the fix's own local variation — `WaypointFix.ReferenceMagVar`) when you track the fix,
and converts the course to true against *it*, lifting your GPS ground track to true with the *aircraft's*
own live magvar — the whole intercept is then computed in one consistent true frame, the same way an
RNAV/FMS does it. If navdata carries no variation for the fix (or you hand-enter a course on a fix that
isn't in the database), it falls back to the aircraft's live magvar. This is the biggest accuracy win
where station declination is large; the remaining refinements (ellipsoidal geodesics, a live WMM) are
below what's audible when matching two tones by ear.

## Vertical guidance (crossing altitudes)

Each slot can carry an optional crossing target. Commanded pitch ≈ **required flight-path angle +
live angle of attack** — the live AoA encodes weight/flap/speed, so this needs no performance model.

- **At** — always command to cross exactly at the target.
- **At or above** — neutral (hold level) while you'll arrive at or above the target; commands a climb
  only if you'd arrive below.
- **At or below** — mirror: neutral while you'll be at or below; commands a descent only if you'd
  bust above.
- **Between X, Y** — neutral inside the window; commands toward whichever bound you'd violate.

With no crossing altitude set, the vertical tone holds level (lateral-only FD). There is **no spoken
top-of-descent cue** — the tone is the instrument, and *when* to start down is the pilot's call
(especially in VFR, where managing the descent is your prerogative, not the app's).

**Descents arm only when due.** A *climb* to a crossing restriction is commanded straight away (you
climb to meet a SID's "at or above"), but a *descent* is held until it's geometrically due — the
required path to the fix reaches a normal ~3° gradient, or you're within ~25 NM of the fix for a
shallow step. So a far constrained fix (e.g. a STAR fix "at or below 11000" tracked while you're at
FL350, 150 NM out) does **not** nudge you to descend at cruise; the descent tone simply arms when
descending is appropriate. This is tone-only — still no spoken TOD callout. (Tunable per aircraft:
`DescentArmFpaDeg`, `VerticalArmRangeNm`.)

## Centered tone change (optional)

An optional extra cue, **off by default** (set it in Hand Fly Options). When on, you pick a waveform
that the command tone switches to **while you are on track** (the bank command is near zero); off
track it reverts to its normal waveform. So a change in *timbre* — not just the left/right pan — tells
you whether you're centered. When off, the tone keeps its normal waveform at all times. Visual
Guidance has the identical option (there "on track" means on the localizer). Only the command tone
changes waveform, so it stays distinguishable from the current-attitude tone.

## Rudder coordination cue (Ctrl+K) — independent

A separate aid you can toggle any time you're hand-flying, with or without the FD: **Ctrl+K**. When
the inclinometer ball is out of centre it plays a **hard-panned white-noise tick** entirely in the ear
on the side of the rudder to press — ball left → left ear → press left rudder; ball right → right ear →
press right rudder ("step on the ball"). The tick speeds up the further out the ball is and is silent
when you're coordinated. Nothing else — no pitch, no proportional pan, no speech. Default off; the tick
volume is `UserSettings.SlipCueVolume`.

> **⚠️ The ball SIDE is not yet confirmed in-sim.** The cue is wired on the documented SimConnect
> convention (`TURN COORDINATOR BALL` in the `Position` unit, -127..+127, positive = ball right), but
> that has not been flown. A reversed cue would tell you to press the *wrong* pedal, so confirm the
> side on a first flight before relying on it. If it is backwards, flip the single
> `MainForm.SlipCueBallSign` constant to `-1.0` — that is the entire fix, and it is the only place
> the convention is applied.

## Autopilot auto-mute

When the **autopilot** is engaged the FD tones go silent (and it announces "Autopilot engaged,
flight director standing by") and resume when you disengage — so you
hand-fly with the FD, engage the AP for cruise, and the tone steps aside on its own. On by default
(`WaypointFdApAutoMute`).

## Per-aircraft tuning

Tunables live on `WaypointFlightDirectorProfile` (`IAircraftDefinition.GetWaypointFlightDirectorProfile()`).
Heavier/faster jets roll more slowly and cover ground faster, so they use a gentler roll gain, a
larger capture radius and a longer rate-lead.

Every supported aircraft carries an explicit profile — including the ones that simply take the
baseline, so the choice is visible in the definition rather than inherited by accident.

| Aircraft | Roll gain (°/° error) | Max bank | Max pitch | Capture radius | Speed floor | Rate-lead |
|---|---|---|---|---|---|---|
| A320 baseline — FBW A32NX (NEO), Fenix (CEO) | 1.1 | 25° | 12° | 0.5 NM | 40 kt | 1.0 s |
| PMDG 737-800 (baseline — 737-class narrowbody) | 1.1 | 25° | 12° | 0.5 NM | 40 kt | 1.0 s |
| iFly 737 MAX8 (baseline — 737-class narrowbody) | 1.1 | 25° | 12° | 0.5 NM | 40 kt | 1.0 s |
| Headwind A330neo | 0.9 | 27° | 10° | 0.8 NM | 60 kt | 1.4 s |
| PMDG 777 | 0.9 | 27° | 10° | 0.8 NM | 60 kt | 1.3 s |
| HorizonSim 787 | 0.9 | 27° | 10° | 0.8 NM | 60 kt | 1.3 s |
| FlyByWire A380X | 0.85 | 28° | 10° | 0.9 NM | 60 kt | 1.5 s |

`TonePitchRangeDeg` (the pitch at which the tone frequency saturates) is **kept equal to
`MaxPitchDeg`** on every profile — 12° on the narrowbodies, 10° on the widebodies. The FD clamps its
pitch command to `MaxPitchDeg`, so a tone that saturates earlier cannot represent commands the FD
itself issues: it originally inherited Visual Guidance's 6°, which pinned the tone at full frequency
through any normal en-route climb and cost the pilot all pitch resolution in exactly the regime the
FD exists for. The resulting matching slopes (25 Hz/° at 12°, 30 Hz/° at 10°) sit in the band the
777's in-sim calibration settled on. This is deliberately a *different* number from the VG profile's
`TonePitchRangeDeg`, which covers only the narrow approach envelope and is sized for beat
sensitivity rather than reach.

> The Headwind A330 override is load-bearing, not decorative: that class derives from
> `FlyByWireA320Definition`, so without its own profile it inherits an *explicit* narrowbody one and
> the wrong values look deliberate. Note also that a per-aircraft **taxi**-turn lead is not evidence
> for `BankRateLeadSec` — the taxi figure is ground steering dominated by the pilot's own rollout
> anticipation (the PMDG 777 runs a 0.3 s taxi lead against a 1.3 s FD lead).

> **⚠️ These are best-effort class defaults and need live in-sim tuning.** Because the FD is for
> *hand-flying*, there is no autopilot to verify against — the gains, caps, capture radius and
> rate-lead should be flown and adjusted per aircraft (the same way the taxi-turn-lead and the
> Visual Guidance profiles were calibrated). If turns overshoot, lower the roll gain or raise the
> rate-lead; if the tone chases noise at low speed, raise the speed floor.

Tone settings live in **Hand Fly Options** (`UserSettings.WaypointFd*`): desired/current waveform +
volume, hard-pan, AP-auto-mute, and the centered tone change (toggle + waveform). A **"Test Flight
Director Tones"** button there previews the desired + current tones with a left↔right bank sweep,
applying your hard-pan and centered-tone selections so you can hear both before flying.

## Normal & abnormal scenarios handled (universal FD)

- **Toggle, default off.** The FD does nothing until you press Ctrl+F; with it off the app behaves
  exactly as before. On engage it starts at the **first filled slot** and follows the filled slots in
  order; engaging with **no** slots filled says "No waypoints to track" and does not activate.
- **Gaps in the slots are skipped, not fatal.** If you track into slots 1, 2 and 4 (or slot 3 couldn't
  be tracked), the FD flies 1 → 2 → 4 — it skips empty interior slots instead of stopping at the first
  gap. It ends only after the last filled slot or slot 5 ("Final waypoint reached").
- **A course leg passed wide still sequences.** An inbound course/airway leg (one that started well
  outside the fix) advances on station-passage (abeam) as well as capture-radius, so being blown wide
  of the fix doesn't strand you on that leg.
- **Engaged parked/overhead a fix can't cascade.** Capture-radius arrival is *armed* — it only counts
  once the fix has been approached from **outside** the radius, so the initial dwell of a leg that
  starts inside it is ignored. A **direct-to** leg that started on the fix instead sequences once
  you've **flown clear of the radius while moving**. Result: no chain-reaction through every slot on
  the first frames, whether parked or airborne over the fix.
  An **outbound radial** is the deliberate exception: it starts on the fix and leaves the radius
  within seconds by definition, so "flown clear" would sequence away the very radial you asked to
  fly, every time. A course leg that starts on its fix therefore holds until you advance or turn the
  FD off — matching *Course / radial tracking* above.
- **Overhead a fix:** bearing spins, but arrival sequences first (capture radius / abeam) and the
  required-FPA is guarded inside ~0.05 NM, so the command doesn't blow up.
- **Low speed / on the ground / no GPS track:** below the per-aircraft speed floor the lateral
  guidance falls back to heading (ground track is unreliable slow).
- **Crosswind:** lateral nulls to a straight wind-corrected path (uses ground track, not heading).
- **Heading/track wrap (359↔001), reciprocal/180° track error:** normalised to ±180°; the command
  saturates to the bank cap toward the shorter turn.
- **Steep required climb/descent:** commanded pitch clamps to the per-aircraft pitch cap.
- **Autopilot engaged:** tones auto-mute (if enabled) and resume on disengage. AP detection uses the
  stock `AUTOPILOT MASTER` (Boeing / 787 / most) OR'd with the FlyByWire `A32NX_AUTOPILOT_1/2_ACTIVE`
  vars (the FBW Airbuses don't drive the stock simvar), so it works across the fleet.
- **Touchdown:** auto-deactivates on the airborne→ground edge (taxi/rollout tones take over).
- **Mutually exclusive with Visual Guidance:** engaging one stops the other; the shared 505 stream
  is reference-counted (with per-feature claim flags so an aborted activation can't stop the other's
  stream). Hand-Fly's tone is suppressed while the FD runs and resumes after — suppression is likewise
  tracked per feature, so a Visual-Guidance activation that aborts (e.g. no destination runway) can't
  un-mute Hand-Fly underneath a running FD and leave three tones playing.
- **Aircraft swap:** the FD, Visual Guidance, and the rudder-coordination slip cue are all stopped
  when you change aircraft, so a tone/tick tuned for the old airframe never carries onto the new one
  (the slip cue owns its own audio device and is also disposed on app close).
- **Paused sim:** no data updates arrive, so the tones simply hold; nothing misbehaves.

## In-sim verification checklist

1. **Stock GA (e.g. C172, no FD/AP):** track 2–3 fixes in slots 1–3, engage; confirm the pan steers
   to each fix and sequences on arrival; confirm graceful behaviour when slot 1 is empty.
2. **Vertical:** set *At or above* and *At or below* crossing altitudes; confirm the tone is neutral
   when the constraint is already satisfied and commands a climb/descent only when it would be
   violated.
3. **Wind:** confirm the ground-track lateral nulls to a straight path in a crosswind.
4. **Arbitration:** confirm Hand-Fly mutes while the FD runs; confirm AP-master auto-mute; confirm
   Visual Guidance and the FD never run together (engaging one stops the other).
5. **Airliner with AP:** hand-fly a leg on the FD, engage the AP → tone steps aside; disengage →
   tone resumes. Repeat per tuned aircraft (777, 787, A320 CEO/NEO, A380) and adjust the profile.

## Architecture (maintainers)

- `Navigation/WaypointFlightDirectorGeometry.cs` + `Navigation/WaypointConstraintMapper.cs` — pure
  command math and the navdata→slot constraint mapping. Guarded in CI by
  `tests/MSFSBlindAssist.Tests/WaypointFlightDirectorGeometryTests.cs` and
  `WaypointConstraintMapperTests.cs`. `tools/WaypointFdProbe`
  (`dotnet run --project tools/WaypointFdProbe -p:Platform=x64`) runs the same cases as a dev-loop
  console probe — it is standalone, not in the solution, and **not** run by CI, so any case added
  there must be added to the xUnit tests as well.
  The geometry exposes: `TrackError` (bearing-to-fix vs **GPS ground track**, so nulling it flies a
  wind-corrected straight line), `CommandedBankDeg` (proportional roll law
  `KRoll·(trackErr − yawRate·lead)` clamped to the bank cap — the rate-lead off the track derivative
  is what kills overshoot), `RequiredFpaDeg` + `CommandedPitchDeg` (`pitch ≈ FPA + live AoA`, the VG
  nominal-pitch trick, so there is no performance model), `ResolveVerticalTarget`
  (At / AtOrAbove / AtOrBelow / Between → command or neutral), `ProjectedCrossingAltFt`,
  `CrossTrackNm` + `CourseInterceptTrackDeg` (the generalised ILS localizer capture), and
  `HasArrived` (capture radius OR abeam >90° off track). `AltitudeConstraintType` lives here too.
- `Services/WaypointFlightDirectorManager.cs` — stateful manager (tones, sequencing, announcements,
  AP auto-mute). Mirrors `VisualGuidanceManager`; owns its own two `AudioToneGenerator`s with the
  same deferred start (and starts the follower only if the desired tone started). It NEVER touches
  the controls. `StandardBank` negates SimConnect's left-positive bank, same as VG. Notable state:
  - **Arrival/sequencing.** Capture-radius arrival is *armed* — it only counts once the fix has been
    approached from outside the radius (`legInsideAtStart` / `legArmedCapture`), so a leg that
    starts on the fix cannot cascade. Abeam (station passage) counts only while moving. A course leg
    uses abeam only when it started well outside the fix (`legStartDistNm > 4× capture radius`); an
    outbound radial starts behind the fix, where abeam would misfire.
  - **Multi-slot skips are coalesced.** `AdvanceLeg` keeps advancing within one frame across fixes
    already flown past (`IsAlreadyBehind`) and speaks ONE callout naming how many were skipped —
    every advance uses `AnnounceImmediate`, which interrupts, so one advance per frame produced a
    burst of half-spoken waypoint names.
  - **Descent-arm gate.** A crossing-altitude CLIMB commands immediately; a DESCENT is held (vertical
    stays level) until the required FPA reaches `DescentArmFpaDeg` (~3°) or the fix is within
    `VerticalArmRangeNm` (~25 NM), so a far constrained STAR fix does not nudge a descent at cruise.
  - **`EffectiveAoaDeg`** uses the live INCIDENCE ALPHA when it has arrived and is plausible,
    otherwise the profile's `TypicalApproachAoaDeg`. See its comment for what it deliberately does
    not try to detect.
  - **`SlewCommands`** rate-limits the rendered bank/pitch between frames
    (`MaxBankRateDegPerSec` / `MaxPitchRateDegPerSec`) so the tone does not wobble on track jitter.
- Rides the shared `VISUAL_GUIDANCE_DATA` (req 505) stream, reference-counted in `SimConnectManager`
  (`Acquire`/`ReleaseVisualGuidanceMonitoring`). Fed by MainForm sibling handler blocks. FD and
  Visual Guidance are mutually exclusive — activating one stops the other. `AUTOPILOT MASTER` is the
  LAST `VisualGuidanceData` field (so existing offsets are unchanged), surfaced as
  `VISUAL_GUIDANCE_AP_MASTER`; VG ignores it.
- `WaypointTracker` slots carry the optional crossing altitude/constraint/course; entered in
  `TrackFixForm`, or pre-filled there from the EFB via `TrackToSlotRequested` →
  `ShowFormPrefilled` → `WaypointConstraintMapper.FromFix`. The dialog keeps the resolved fix in
  `_prefilledFix` so Track uses its exact coordinates (navaid/runway fixes are not in the `waypoint`
  table) unless the pilot edits the ident.
- Per-aircraft tuning via `WaypointFlightDirectorProfile`.

Design spec: `docs/superpowers/specs/2026-06-16-waypoint-flight-director-design.md`.
