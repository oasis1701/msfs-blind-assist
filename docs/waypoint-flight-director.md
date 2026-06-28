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
   in order. You can also track from the Electronic Flight Bag route viewer (Shift+E).
2. **(Optional) Add a crossing altitude.** In the Track Fix window, enter a **Crossing Altitude**
   (feet MSL) and pick a **constraint** (At / At or above / At or below / Between). Leave the altitude
   blank for **lateral-only** guidance at that fix. For *Between*, also enter the **Upper Altitude**.
3. **Engage.** Output mode → **Ctrl+F**. The FD starts on slot 1 and announces the active leg. If slot
   1 is empty it says "No waypoints to track" and does nothing.
4. **Hand-fly** to match the tones. On reaching each fix the FD announces the next leg
   (e.g. *"Next, TOPM, 18 miles, bearing 102."*) and sequences automatically.
5. **It stops** at the first empty slot or after slot 5 ("Final waypoint reached"). Press **Ctrl+F**
   again to turn it off at any time.

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
volume is `UserSettings.SlipCueVolume`. (The ball sign is confirmed in-sim; if ever reversed it's a
one-line flip.)

## Autopilot auto-mute

When the **autopilot** is engaged the FD tones go silent (and it announces "Autopilot engaged,
flight director standing by") and resume when you disengage — so you
hand-fly with the FD, engage the AP for cruise, and the tone steps aside on its own. On by default
(`WaypointFdApAutoMute`).

## Per-aircraft tuning

Tunables live on `WaypointFlightDirectorProfile` (`IAircraftDefinition.GetWaypointFlightDirectorProfile()`).
Heavier/faster jets roll more slowly and cover ground faster, so they use a gentler roll gain, a
larger capture radius and a longer rate-lead.

| Aircraft | Roll gain (°/° error) | Max bank | Max pitch | Capture radius | Speed floor | Rate-lead |
|---|---|---|---|---|---|---|
| A320 baseline — FBW A32NX (NEO), Fenix (CEO) | 1.1 | 25° | 12° | 0.5 NM | 40 kt | 1.0 s |
| PMDG 777 | 0.9 | 27° | 10° | 0.8 NM | 60 kt | 1.3 s |
| HorizonSim 787 | 0.9 | 27° | 10° | 0.8 NM | 60 kt | 1.3 s |
| FlyByWire A380X | 0.85 | 28° | 10° | 0.9 NM | 60 kt | 1.5 s |

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
  exactly as before. On engage it follows slots 1→5; engaging with slot 1 empty says "No waypoints
  to track" and does not activate.
- **Empty slot mid-route / final fix:** stops at the first empty slot or after slot 5 ("Final
  waypoint reached").
- **Engaged while parked or behind a fix:** the abeam (station-passage) arrival only counts when
  actually moving, so it can't cascade through every slot on the first frame; the capture-radius
  arrival still works at any speed.
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
  stream). Hand-Fly's tone is suppressed while the FD runs and resumes after.
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

- `Navigation/WaypointFlightDirectorGeometry.cs` — pure command math, probe-tested by
  `tools/WaypointFdProbe` (`dotnet run --project tools/WaypointFdProbe -p:Platform=x64`).
- `Services/WaypointFlightDirectorManager.cs` — stateful manager (tones, sequencing, announcements,
  AP auto-mute). Mirrors `VisualGuidanceManager`; owns its own two `AudioToneGenerator`s.
- Rides the shared `VISUAL_GUIDANCE_DATA` (req 505) stream, reference-counted in `SimConnectManager`.
  Fed by MainForm sibling handler blocks. FD and Visual Guidance are mutually exclusive.
- `WaypointTracker` slots carry the optional crossing altitude/constraint; entered in `TrackFixForm`.
- Per-aircraft tuning via `WaypointFlightDirectorProfile`.

Design spec: `docs/superpowers/specs/2026-06-16-waypoint-flight-director-design.md`.
