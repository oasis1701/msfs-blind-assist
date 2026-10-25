---
title: "Takeoff, Climb and Cruise"
---

<div class="guide-part" markdown="1">

Chapter 1 of 2 in [Part 6: Flying](index.md#part-5-flying) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Takeoff Assist](#takeoff-assist)
- [Takeoff Speed Callouts](#takeoff-vspeed-callouts)
- [Autopilot Windows](#autopilot-windows)
- [Hand Fly Mode](#hand-fly)
- [Altitude Callouts](#flying-altitude-callouts)
- [Other Automatic Callouts in Flight](#flying-auto-callouts)
- [TCAS Traffic](#tcas)

</nav>

A typical departure with BA looks like this:

1. Taxi to the runway with [Taxi Guidance](taxi-guidance.md). When you line up, Takeoff Assist switches itself on.
2. Roll down the runway following the Takeoff Assist tone, listening to the speed and V-speed calls.
3. At liftoff BA hands you over to Hand Fly mode, which gives you a tone for pitch and bank.
4. Engage the autopilot when you are ready, using the autopilot windows to set speed, heading, altitude and vertical speed.
5. In the climb and cruise, BA calls each 1,000 feet and tells you about anything that changes.

Each step can also be done by hand, and each automatic step can be switched off.

## Takeoff Assist {#takeoff-assist}

### What it is {#takeoff-assist-what}

Takeoff Assist keeps you on the runway centreline during the takeoff roll. It plays a steady tone in your headphones that moves left or right to show which way to steer, calls out your pitch as you rotate, and calls out speeds as you accelerate. It works on every aircraft BA supports.

### When to use it {#takeoff-assist-when}

Use it for every takeoff. It is switched on for you when taxi guidance lines you up on your departure runway. If you got onto the runway another way, for example by teleporting, press Output Ctrl+T once you are lined up and pointing down the runway.

### How BA knows which runway you are on {#takeoff-assist-runway}

Takeoff Assist steers you along the real centreline of the runway, so it needs to know which runway that is. It finds out in this order:

1. If you teleported to a runway with Input Shift+R (see [Teleporting to a Runway or Gate](planning-your-flight.md#teleport)), it uses that runway.
2. If taxi guidance just lined you up, it uses the runway taxi guidance took you to.
3. Otherwise, if you are on the ground at an airport in your navigation database, BA works out which runway is under the aircraft from your position and heading.
4. If none of these works, it draws an imaginary centreline straight ahead of you from where you are now, along the heading you are pointing.

When it switches on you hear which of these it used:

- *"Takeoff assist active, runway 27L at EGLL"* means BA found the real runway. The tone follows its painted centreline.
- *"Takeoff assist active, no runway selected, extending centerline from current position, heading 272"* means BA could not identify a runway. It will keep you on a straight line from where you are, so make sure you are lined up and centred before you press Ctrl+T.

The runway is forgotten every time Takeoff Assist switches off, so your next flight always starts fresh and never reuses the last airport's runway.

### The heading check {#takeoff-assist-heading-check}

When BA knows the real runway, it compares your heading with the runway heading the moment Takeoff Assist switches on. If you are more than about 3 degrees off, you hear a warning such as *"Warning: heading 262, runway heading 270. Turn right to align before rolling."* Turn the way it says before you advance the thrust levers. Sighted pilots make this same check by looking down the runway; this gives you the numbers.

### How to use it {#takeoff-assist-how}

1. Line up on the runway. If taxi guidance brought you here, you hear *"Lined up. Activating takeoff assist."* followed by the runway announcement. Otherwise press Output Ctrl+T.
2. Listen for the heading warning. If there is none, you are aligned.
3. Advance the thrust levers and start the roll.
4. Steer with the rudder toward the tone (see below). When the tone is silent, or centred, you are tracking the centreline.
5. Listen for *"80 knots"*, *"100 knots"* and the V-speed calls.
6. Rotate. Takeoff Assist calls your pitch every time it changes by a degree: *"+3"*, *"+7"*, *"+12"*.
7. After liftoff, if Hand Fly auto-activation is on (it is by default), BA turns Takeoff Assist off and Hand Fly on for you, and says *"Airborne, hand fly."* If it is off, press Output Ctrl+T to switch Takeoff Assist off yourself; you hear *"Takeoff assist off"*.

You can press Output Ctrl+T at any time to switch Takeoff Assist off, for example after a rejected takeoff. Pressing it again switches it back on.

<div class="callout note" role="note" markdown="1">

**Note:** Takeoff Assist needs the simulator connected. If it is not, Ctrl+T says *"Not connected to simulator."*

</div>

### The centreline tone {#takeoff-assist-tone}

The Takeoff Assist tone is a steering tone with a fixed pitch (see [The Audio Language](core-concepts.md#audio-language)): it sits on the side you should press rudder toward, fully to one side for a correction of about 5 degrees or more. With the default setting it only sounds when you need a correction of 1 degree or more; if you choose to hear it all the time, it sits in the centre when you are on track.

The tone does more than tell you whether the nose points down the runway. It also knows how far you are from the centreline. If you have drifted to one side, the tone asks you to point slightly back toward the centreline (up to 10 degrees, gently, so you never get a sharp swerve at speed), and as you close in it eases you back onto the runway heading. Within about 8 feet of the centreline it simply asks you to hold the runway heading. So if you follow the tone until it goes quiet, you end up both on the centreline and pointing straight down it.

<div class="callout tip" role="note" markdown="1">

**Tip:** Make small, smooth rudder inputs and wait a moment for the tone to respond. Chasing the tone with big inputs at high speed causes a swing from side to side.

</div>

If you learnt the older Takeoff Assist where you steered away from the tone, you can reverse it in Settings (see below).

### What you will hear {#takeoff-assist-callouts}

- **Centreline position**, as the distance from the centreline changes by about 10 feet, at most twice a second. The number is in steps of 10 feet and the word is the side you are on: *"2 left"* means you are about 20 feet left of the centreline, so steer right. *"center"* means you are within 25 feet of it. You can mute these and keep just the tone.
- **Pitch**, whenever it changes by one degree, at most twice a second: *"level"*, *"+5"*, *"+14"*, or *"-1"* for nose down.
- **Speeds**: *"80 knots"* and *"100 knots"*, on every aircraft. The V-speed calls come from the aircraft itself or from BA's own V-speed callouts, whether or not Takeoff Assist is on (see [Takeoff Speed Callouts](#takeoff-vspeed-callouts)).
- **Ground speed**, if you have turned it on (see Settings below), for example *"60 knots."*.

### The handover to Hand Fly at liftoff {#takeoff-assist-handover}

Once you are flying you need pitch and bank, not centreline, so by default BA switches over for you shortly after liftoff. It waits long enough that a bounce on the runway or a rejected takeoff never turns your centreline guidance off by mistake.

When it switches you hear one short phrase:

- *"Airborne, hand fly."* Takeoff Assist is off and Hand Fly mode is now on.
- *"Airborne."* You had already switched Hand Fly on yourself before takeoff, so only Takeoff Assist was turned off.
- *"Airborne, hand fly, quick keys failed."* Hand Fly is on, but its single-letter readout keys could not be set up (see [If something goes wrong](#hand-fly-problems) under Hand Fly).

The Hand Fly tone starts immediately.

### Legacy mode {#takeoff-assist-legacy}

Legacy mode is the original Takeoff Assist. It plays no tone. Instead it speaks how far your heading is from the runway heading, in whole degrees, whenever that changes by a degree: *"3 right"* means you are pointing 3 degrees right of the runway heading, so steer left; *"center"* means within 1 degree. It knows nothing about your distance from the centreline, so a small heading error can still let you drift sideways. Use it only if you prefer speech to tones. When it switches on you hear *"Takeoff assist active legacy mode, runway 27L at EGLL"* or *"Takeoff assist active legacy mode, reference heading 272"*.

### Settings {#takeoff-assist-settings}

These are on the **Hand Fly** tab of the Settings window (see [The Settings Window](getting-started.md#settings-overview)):

- **Takeoff Assist Tone**: the sound of the tone. Choose from Sine (Smoothest), Triangle (Smooth), Sawtooth (Bright) or Sine (Rich).
- **Takeoff Assist Volume**: 0 to 100 percent. The default is 5 percent.
- **Mute centerline deviation announcements**: keeps the tone and pitch calls but drops the *"2 left"* and *"center"* speech. Off by default.
- **Steer toward the tone to stay on the centerline**: on by default. Untick it to reverse the panning, so you steer away from the tone instead.
- **Hard-pan centerline tone (full left or full right; speaker-friendly)**: the tone jumps fully to one side instead of moving gradually. Useful on speakers, where a slightly off-centre tone is hard to tell from a centred one. Off by default.
- **Play steering tone**: when the tone sounds. **Always** plays it all the time (centred when on track). **At 1 degree error** (the default) up to **At 5 degrees error** keep it silent until the correction needed reaches that size.
- **Legacy takeoff assist mode (heading-based, no tone)**: see above. Off by default.
- **Enable takeoff assistant call outs**: the 80 knots and 100 knots calls. On by default.
- **Auto-activate Takeoff Assist on lineup**: on by default. Takeoff Assist switches on when taxi guidance lines you up on your departure runway. This happens once per taxi route: if you switch Takeoff Assist off after it came on, it stays off until your next taxi route.
- **Auto-activate Hand Fly on takeoff (deactivates Takeoff Assist)**: on by default. This is the liftoff handover described above.

Ground speed calls during the roll are set on the Taxi Guidance tab; see [Ground speed](knowing-where-you-are.md#where-ground-speed).

The tone, volume and steering settings here are also used by the rollout centreline tone after landing; see [Flare and Rollout Tones](approach-and-landing.md#flare-assist).

### If something goes wrong {#takeoff-assist-problems}

- **You heard "no runway selected" when you were on a real runway.** BA could not match your position to a runway in its navigation database. Check that the database matches your simulator (see [The Navigation Database](getting-started.md#nav-database)). The tone still works, along a straight line from where you pressed Ctrl+T, so only press it once you are centred and aligned.
- **Takeoff Assist did not come on at lineup.** It only switches itself on when taxi guidance was guiding you to that runway and you came to a steady lineup on it, and only once per route. Press Output Ctrl+T.
- **The tone keeps swinging from side to side.** You are over-correcting. Use smaller rudder inputs and give the aircraft a moment to respond.
- **You hear nothing at all.** With the default setting the tone is silent while you are on track, which is the goal. To confirm it is working, set **Play steering tone** to **Always** for a while. Also check that **Legacy takeoff assist mode** is not ticked, and the **Takeoff Assist Volume**.

## Takeoff Speed Callouts {#takeoff-vspeed-callouts}

Several aircraft get spoken *"V1"*, *"Rotate"* and *"V2"* calls on the takeoff roll from BA, timed to the moment your airspeed reaches each speed. These are action cues, so they speak immediately, even over other speech.

<div class="table-wrap" markdown="1">

| Aircraft | Who makes the calls | Where the speeds come from |
|---|---|---|
| FlyByWire A320 and Headwind A330 | BA | The MCDU PERF TAKE OFF page |
| FlyByWire A380 | BA | The PERF page |
| iFly 737 MAX 8 | BA | The FMC |
| TFDi MD-11 | BA, plus *"N1 70 percent"* | The FMS |
| Fenix A320 | BA (the Fenix also calls *"V1"* itself, so you hear it twice) | The MCDU PERF TAKE OFF page |

</div>

How they work:

- They arm only on the ground, below 40 knots, with V1 and VR entered. So a landing can never fire them, and after a rejected takeoff they re-arm for the next attempt.
- Each is called once per takeoff roll.
- If two come due at the same moment, for example where V1 and VR are the same speed, you hear them together as one phrase: *"V1, Rotate"*.
- On the MD-11, *"N1 70 percent"* is called once when the first engine reaches 70 percent N1, where the autothrottle takes over.
- Each call can be muted in the Monitor Manager (Output Ctrl+M) with its speed's row (V1, VR, V2). See [The Monitor Manager](core-concepts.md#monitor-manager).

Takeoff Assist's *"80 knots"* and *"100 knots"* come on top of these on every aircraft. Details for each aircraft are in [Airbus Aircraft](airbus-aircraft.md) and [Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md).

## Autopilot Windows {#autopilot-windows}

### What they are {#autopilot-windows-what}

The autopilot windows let you set the autopilot by typing a number instead of turning a knob. Each window sets one value — speed, heading, altitude or vertical speed — and also carries the buttons that belong with it, such as push and pull or the mode switches. A separate autopilot window holds the main engage buttons. Every aircraft BA supports has them; their exact contents follow the real aircraft's panel. Airbus aircraft call the panel the FCU, Boeing aircraft the MCP, and the MD-11 the flight control panel.

### Opening them {#autopilot-windows-keys}

The keys are the same on almost every aircraft, all in input mode:

<div class="table-wrap" markdown="1">

| Key | Window |
|---|---|
| Input Ctrl+S | Speed |
| Input Ctrl+H | Heading |
| Input Ctrl+A | Altitude |
| Input Ctrl+V | Vertical speed (or flight path angle) |
| Input Ctrl+B | Altimeter (barometric setting) |
| Input Ctrl+P | Autopilot panel: autopilot, autothrust or autothrottle, approach modes, disconnects |

</div>

Not every key exists on every aircraft; each aircraft's guide in Part 8 lists what it has. To read the values back without opening a window, Output Shift+S, Shift+H, Shift+A and Shift+V read the speed, heading, altitude and vertical speed windows on every aircraft.

### Using a value window {#autopilot-windows-how}

1. Press the key, for example Input Ctrl+A. The window opens with the cursor in the value box.
2. Type the value, for example 12000, and press Enter. There is no Set button; Enter in the value box is how a value is set.
3. BA confirms the value it set. On the FlyByWire Airbuses, for example, the speed window says *"FCU speed 250, selected"*.
4. Tab on to the other buttons if you need them, such as **Altitude Pull (selected)**.
5. Press Escape or the **Close** button. Focus goes back to where you were.

On the Airbus aircraft the windows stay open alongside your other windows, so you can leave one open while you work in another. If you type something the aircraft cannot accept, BA tells you why, for example *"Invalid number format"*, and selects the text so you can type again.

A typical speed window contains a value box, push and pull buttons (or **Speed Intervene** on a Boeing), a speed or Mach toggle, and **Close**. Buttons that have a state say it in their name, for example *"SPD / MACH toggle — now knots (press for Mach)"*.

### Push, pull, managed and selected {#autopilot-windows-managed}

On Airbus aircraft each FCU knob can be pushed or pulled:

- **Push** hands that value to the flight management computer. This is called **managed**: the aircraft flies the speed, route or altitude profile from your flight plan.
- **Pull** makes the aircraft fly the number in the window. This is called **selected**.

The same idea exists on Boeing aircraft in other words: LNAV and VNAV are the flight computer flying the route and profile; HDG SEL, LVL CHG or FLCH and V/S are you choosing the numbers; and **Intervene** lets you change a value without leaving VNAV.

When an Airbus FCU window is managed it shows dashes, and BA's readouts say *"managed"* rather than inventing a number. With the FCU unpowered they say *"not available"*.

On the FlyByWire Airbuses and the Fenix A320 you can also push and pull the knobs straight from the keyboard, without opening a window:

<div class="table-wrap" markdown="1">

| Key | Action |
|---|---|
| Input Shift+1 / Input Ctrl+1 | Push / pull heading |
| Input Shift+2 / Input Ctrl+2 | Push / pull altitude |
| Input Shift+3 / Input Ctrl+3 | Push / pull speed |
| Input Shift+4 / Input Ctrl+4 | Push / pull vertical speed |

</div>

The TFDi MD-11 has the first three pairs (heading, altitude and speed); its vertical speed wheel is in the Ctrl+P and Ctrl+V windows.

### Autopilot shortcut keys (FlyByWire Airbuses) {#autopilot-windows-toggles}

On the FlyByWire A320, Headwind A330 and FlyByWire A380, these keys work the engage buttons directly:

<div class="table-wrap" markdown="1">

| Key | Action |
|---|---|
| Input Shift+A | Autopilot 1 on or off |
| Input Ctrl+O | Autopilot 2 on or off |
| Input Shift+P | Approach mode (APPR) |
| Input Ctrl+J | Autothrust: arm or disconnect |
| Input Ctrl+L | Localizer (LOC) |

</div>

The result is announced by the aircraft's flight mode announcements, which are spoken automatically whenever a mode changes.

### The autopilot panel window {#autopilot-windows-panel}

Input Ctrl+P opens the autopilot panel. It is a list of buttons, each of which says its current state. On the FlyByWire A320 you find AP1 and AP2, **A/THR** (which says Disengaged, Armed or Active), **AP disconnect**, **A/THR disconnect**, the approach and localizer buttons, and a Flight Director status line. On the Boeings you find the A/P and A/T buttons, LNAV, VNAV and the other mode buttons, with a separate disconnect button that always disconnects. The MD-11's window also carries a live status list of all four windows.

### What you will hear {#autopilot-windows-hear}

- Changes that happen without you — a mode change, a capture, the autopilot dropping out — are announced automatically through the flight mode announcements on the aircraft that have them.
- On every aircraft except the MD-11, turning a real knob, on your hardware or in the virtual cockpit, speaks the new value too, so you do not need to read the window after each turn. On the FlyByWire Airbuses, values the aircraft shows as dashes are not spoken.

All of these can be muted individually in the Monitor Manager (Output Ctrl+M).

### If something goes wrong {#autopilot-windows-problems}

- **The value is set but the aircraft does not follow it.** On an Airbus the window is probably still managed; press **Pull (selected)**. On a Boeing check that the right mode is engaged, for example HDG SEL for a heading.
- **The vertical speed you set does nothing.** On most aircraft the vertical speed only takes effect once V/S mode is engaged; use the pull or engage button in the vertical speed window.
- Aircraft-specific behaviour is described in [Airbus Aircraft](airbus-aircraft.md) and [Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md).

## Hand Fly Mode {#hand-fly}

### What it is {#hand-fly-what}

Hand Fly mode helps you fly the aircraft by hand without the autopilot. It gives you a tone that shows your pitch and bank at every moment, spoken pitch and bank calls, and a set of single-letter keys that read your flight instruments instantly.

### When to use it {#hand-fly-when}

Use it whenever you are flying by hand: after takeoff until the autopilot is engaged, during manual climbs and turns, and any time you want to keep an ear on your attitude. It comes on automatically at liftoff when Takeoff Assist is active (see [The handover to Hand Fly at liftoff](#takeoff-assist-handover)). You can also turn it on or off at any time with Output Ctrl+H. You hear *"Hand fly mode active"* or *"Hand fly mode off"*.

### The Hand Fly tone {#hand-fly-tone}

The Hand Fly tone is a single continuous note that carries two pieces of information at once:

- **Its pitch shows your aircraft's pitch.** Wings-level, nose-level flight is the middle note. Nose up raises the note; nose down lowers it. The note keeps rising all the way to 20 degrees nose up, so it stays useful through the whole initial climb, and keeps falling down to 10 degrees nose down. The nose-down side changes faster per degree than the nose-up side, because you rarely need large nose-down attitudes.
- **Its position between your ears shows your bank.** Right wing down moves the tone to your right ear; left wing down moves it to your left. It reaches fully one side at 10 degrees of bank. Centred means wings level.

So a centred tone at the middle note is level flight with wings level. A tone sitting in your right ear at a higher note is a climbing right turn. To level the wings, roll away from the side the tone is on until it comes back to the centre.

<div class="callout note" role="note" markdown="1">

**Note:** This is different from the Takeoff Assist tone, which never changes note and points to where you should steer. The Hand Fly tone shows what the aircraft is doing now. See [The Audio Language: What the Tones Mean](core-concepts.md#audio-language) for how all of BA's tones fit together.

</div>

If you also turn on [Visual Guidance](approach-and-landing.md#visual-guidance) (Output Ctrl+V), the Hand Fly tone goes quiet so that you only hear Visual Guidance's two tones, which use the same pitch and pan rules. Hand Fly's spoken calls carry on. When Visual Guidance ends, the Hand Fly tone comes back.

### What you will hear {#hand-fly-callouts}

Depending on the feedback type you choose, Hand Fly speaks:

- **Pitch**, whenever it changes by a degree, at most twice a second: *"level"*, *"+8"*, *"-2"*. This is exactly the same style as Takeoff Assist, so nothing changes in your ears at liftoff.
- **Bank**, whenever it changes by a degree, at most twice a second: *"left 15"*, *"right 5"*, *"wings level"*.
- **Heading** (on by default), once a second when it has changed: just the number, *"275"*.
- **Vertical speed** (off by default), once a second when it has changed: *"+1850"*, *"-700"*.

### Quick-access keys {#hand-fly-quick-keys}

While Hand Fly mode (or Visual Guidance) is on, these single letters work on their own, without pressing a bracket first:

<div class="table-wrap" markdown="1">

| Key | What it reads |
|---|---|
| A | Altitude above sea level |
| Q | Altitude above the ground |
| S | Indicated airspeed |
| H | Magnetic heading |
| V | Vertical speed |
| P | Pitch |
| B | Bank |
| D | Distance to your destination runway |
| F | Target vertical speed (Visual Guidance only) |

</div>

Pressing F when Visual Guidance is off says *"Visual guidance not active"*. D needs a destination runway; see [Choosing a Destination Runway](approach-and-landing.md#destination-runway).

<div class="callout warning" role="note" markdown="1">

**Warning:** These letters are taken over for the whole computer while Hand Fly or Visual Guidance is on. Typing an A, B, D, F, H, P, Q, S or V into another program, such as a chat window, will not work until you turn the mode off.

</div>

### How to use it {#hand-fly-how}

1. Take off with Takeoff Assist. At liftoff you hear *"Airborne, hand fly."* and the tone starts. Or press Output Ctrl+H.
2. Hold your takeoff pitch by keeping the note steady at the right height. Listen to the pitch calls to confirm the number.
3. To turn, roll until the tone moves to the side you are turning and the bank call says the angle you want, for example *"right 15"*. Roll back toward the centre to level the wings.
4. Press the quick keys for anything else: S for speed, A for altitude, V for climb rate.
5. When the autopilot is engaged, press Output Ctrl+H to turn Hand Fly off.

### Settings {#hand-fly-settings}

On the **Hand Fly** tab of the Settings window:

- **Hand flying feedback type**: **Audio Tones Only**, **Screen Reader Announcements Only**, or **Both Tones and Announcements** (the default).
- **Hand fly tone type**: Sine (Smoothest), Triangle (Smooth), Sawtooth (Bright) or Sine (Rich).
- **Volume**: 0 to 100 percent, default 5 percent. The **Test Tone** button plays a sample with your settings so you can hear the note move; press it again to stop. The sample sweeps only part of the nose-up range, so it does not play the very highest note.
- **Monitor Heading (announce heading changes at 1-second intervals)**: on by default.
- **Monitor Vertical Speed (announce VS changes at 1-second intervals)**: off by default.
- **Auto-activate Hand Fly on takeoff (deactivates Takeoff Assist)**: on by default.

The same tab holds the Visual Guidance and Takeoff Assist tone settings; those are covered in their own sections.

### If something goes wrong {#hand-fly-problems}

- **"Hand fly mode active, quick keys failed."** (or *"Airborne, hand fly, quick keys failed."* at liftoff). Another program on your computer already uses one of the single-letter keys as its own global hotkey, so BA could not take it. Hand Fly still works, and you can still read everything with output mode (for example Output S for speed). Close the other program, or turn Hand Fly off and on again, which retries the keys.
- **No tone, only speech.** Check that the feedback type is not **Screen Reader Announcements Only**, and check the volume. If Visual Guidance is on, the Hand Fly tone is deliberately silent.
- **Too much speech.** Choose **Audio Tones Only**, or untick **Monitor Heading**.

## Altitude Callouts {#flying-altitude-callouts}

While you are airborne, BA calls each 1,000 feet you cross, climbing or descending, as a plain number: *"5000"*, *"10000"*, *"36000"*. It is the thousand you just crossed, so passing 4,000 feet says *"4000"* both on the way up and on the way down. The call comes at the moment you reach the round number.

To keep things quiet in the cruise, BA never repeats the thousand it just called. If you level off at 36,000 feet and turbulence takes you a few feet above and below it, you hear *"36000"* only once. The call comes back once you cross a different thousand, and everything resets when you land.

Nothing is called on the ground, and nothing is called when you first connect in mid-air: BA notes where you are silently and calls the next crossing.

On the FlyByWire A380 with the metric altitude (MTRS) button on, the calls are every 300 metres instead and say the unit: *"3000 meters"*.

Turn these calls on or off with **Announce 1,000-foot altitude crossings** on the **Announcements** tab of Settings. They are on by default.

## Other Automatic Callouts in Flight {#flying-auto-callouts}

### Liftoff and touchdown {#flying-air-ground}

On every aircraft BA says *"Airborne"* when the wheels leave the ground and *"On ground"* when they touch down again, so you always know the moment of liftoff and touchdown, including a bounce. If you find it chatty during circuits, mute it in the Monitor Manager (Output Ctrl+M), where it is listed as Ground State.

### Speed brake {#flying-speed-brake}

On the PMDG 737, PMDG 777 and iFly 737 MAX 8, BA announces the speed brake lever once it comes to rest, rather than at every position it passes on the way:

- *"Speed brake down"* — the lever is down, or short of the Armed detent.
- *"Speed brake armed"* — only at the Armed detent itself.
- *"Speed brake flight"*, *"Speed brake 50 percent"*, *"Speed brake fully deployed"* — at those detents.
- *"Speed brake 35 percent"* — wherever the lever rests between detents, for example with a hardware lever.

You can also move the lever on those aircraft from the Control Stand panel. Turning the speed brake off in the Monitor Manager (Output Ctrl+M) keeps it quiet.

### Trim {#flying-trim}

BA announces stabiliser trim as it changes. On most aircraft it is spoken in degrees with a direction, in steps of a tenth of a degree, for example *"Trim up 1.4"*. It stays quiet while the trim only wobbles on the ground. Some aircraft use their own trim units, to match the number the FMC asks for and the trim indicator shows:

- PMDG 737 and iFly 737 MAX 8: *"Trim 5.3"*.
- PMDG 777: *"Trim 5.25 units"*.

On every aircraft, press Output Shift+T to turn trim announcements on or off; you hear *"Trim announcements on"* or *"Trim announcements off"*. Trim announcements can also be muted from the Monitor Manager.

### Thrust lever detents {#flying-thrust-detents}

On the FlyByWire A380, BA tells you when the thrust levers reach a detent: Idle, Climb, Flex MCT, TOGA or Reverse. This is a background announcement: if you pick a detent from the Thrust Levers control yourself, your screen reader reads your choice and BA then tells you once the levers actually arrive. On the FlyByWire A320 and Headwind A330, picking a detent from the Thrust Levers control is not repeated back, because your screen reader has already read it.

### Cockpit warnings: ECAM and EICAS {#flying-ecam}

Airliners show warnings, cautions and status messages on a central display: ECAM on Airbus aircraft, EICAS on Boeings. BA reads these to you so you know when something needs attention.

On the FlyByWire A320, Headwind A330 and FlyByWire A380, BA speaks ECAM warnings, cautions and memos as they appear. Press Output Ctrl+E to turn these call-outs off or on; you hear *"E W D monitoring disabled"* or *"E W D monitoring enabled"*. Master Warning and Master Caution are always announced. To read the whole display at any time, press Output Alt+E to open the E/WD window.

Other aircraft work in a similar way: the PMDG aircraft announce Master Warning and Master Caution and their annunciator lights, and the HorizonSim 787 announces Master Warning and Master Caution and lists the full EICAS alerts in its Alt+E window. Each aircraft's guide in Part 8 gives the details, and every one of these announcements can be muted in the Monitor Manager.

## TCAS Traffic {#tcas}

### What it is {#tcas-what}

BA's TCAS Traffic window lists the other aircraft around you — AI traffic, multiplayer and network pilots — with their distance, direction and height. You can pick aircraft to track and then hear where they are with one key. On the FlyByWire Airbuses, BA also speaks the aircraft's own TCAS traffic and resolution advisories.

### The TCAS Traffic window {#tcas-window}

Press Output Ctrl+R to open the window. It has a status line at the top, then two lists:

- **Airborne Traffic**, for example *"Airborne Traffic — 4 aircraft"*.
- **On Ground Traffic**, for the aircraft on the ground. When a parked aircraft is at a known gate or stand, its line includes "at" and the gate or stand name, which is handy for finding a friend at the airport.

Both lists are sorted nearest first. Each line reads like this: *"UAL 123 — 4.2nm, 2,000ft above, 2 o'clock — 310 knots — United — type B738 — KSFO to KLAX — heading 145 — 14,500 feet"*. The clock position is relative to your nose: 12 o'clock is straight ahead, 3 o'clock to your right, 6 o'clock behind and 9 o'clock to your left. Aircraft within 200 feet of your height are *"same altitude"*. Ground traffic leaves out the height and adds the stand it is at, when BA can work it out. Parts that are not known, such as the route, are left out.

The lists show the traffic at the moment the window opened. While the window is open BA updates its traffic picture in the background every few seconds; press F5 to refresh the lists with the latest picture.

### Tracking an aircraft {#tcas-tracking}

1. In the TCAS Traffic window, arrow to the aircraft.
2. Press the Applications key (or Shift+F10) to open the context menu.
3. Choose **Add to track list**. You hear, for example, *"UAL 123 added to track list."*
4. Later, from anywhere, press Output R. BA takes a fresh look and reads each tracked aircraft, for example *"UAL123, 4.2nm, 2,000ft above, 2 o'clock, 310kts"*.

If a tracked aircraft has gone out of range, R reads its last known position followed by *"last known"*. If it has not been seen yet, you hear *"UAL123, not yet in range"*. With nothing tracked, R says *"No tracked aircraft. Add aircraft to track list from the TCAS window."* To stop tracking, choose **Remove from track list** from the same menu.

This is useful for following a friend on a network, or keeping track of the aircraft ahead of you in an approach sequence.

<div class="callout note" role="note" markdown="1">

**Note:** The TCAS Traffic window is BA's own traffic list, not your aircraft's collision-avoidance system. It gives no warnings by itself. Traffic on the ground while you taxi is covered by [Ground Traffic](ground-traffic.md).

</div>

### Traffic and resolution advisories (FlyByWire Airbuses) {#tcas-ra}

On the FlyByWire A320, Headwind A330 and FlyByWire A380, BA announces the aircraft's own TCAS when its state changes: a traffic advisory, a resolution advisory, and clear of conflict.

During a resolution advisory it also tells you what to fly, because you cannot see the red and green bands on the vertical speed scale:

- For an advisory that asks you to change your climb or descent: *"TCAS: Climb. Fly vertical speed plus 1500 to plus 2000 feet per minute."*
- For an advisory that asks you not to change something: *"TCAS: Do not descend."* or *"TCAS: Do not climb more than 1000 feet per minute."*, sometimes with *"Maintain plus 500 feet per minute"*.

If the advisory strengthens or reverses, you hear the new instruction. Follow the instruction promptly with smooth control inputs, as a real crew would, and return to your clearance once you hear clear of conflict. These announcements can be muted in the Monitor Manager with the TCAS advisory row.

### If something goes wrong {#tcas-problems}

- **The lists are empty.** There may be no traffic near you. Check that AI or multiplayer traffic is turned on in the simulator, or that you are connected to your network.
- **Callsigns are missing.** Some AI traffic has no callsign; those aircraft are listed by type, or as *"unknown"* with a number.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Gates, GSX and Docking](gates-gsx-and-docking.md)
- [Next chapter: Approach and Landing](approach-and-landing.md)
- [Guide contents](index.md)

</nav>
