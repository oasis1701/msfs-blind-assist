---
title: "Core Concepts"
---

<div class="guide-part" markdown="1">

Chapter 1 of 1 in [Part 2: Core Concepts](index.md#part-2-core-concepts) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [A Flight with BA, Start to Finish](#concepts-story)
- [How BA Talks to You](#core-speech)
- [Announcement Settings](#core-announcement-settings)
- [Pause, Slew and Sim Rate](#core-sim-state)
- [The Monitor Manager](#monitor-manager)
- [Readout Hotkeys](#readouts)
- [The Audio Language: What the Tones Mean](#audio-language)
- [AI Display Reading and Scene Description](#ai-display-reading)
- [Location Info and Nearest City](#location-info)

</nav>

This part explains the ideas that run through the whole of MSFS Blind Assist (BA). If you read only one section, make it [The Audio Language: What the Tones Mean](#audio-language): every guidance tone in BA follows the same small set of sound rules.

## A Flight with BA, Start to Finish {#concepts-story}

This walk-through follows one imaginary flight and shows which BA features you might use at each step; each one has its own chapter, linked from the text. The hotkeys named are the ones most aircraft use; check your aircraft's list in the [Hotkey Reference](hotkey-reference.md). If you are new to airliners, [General Flying Concepts](general-flying-concepts.md) explains the flying behind it.

### At the gate {#at-the-gate}

You start the simulator with the aircraft parked at a stand. BA announces *"Connected to"* and the aircraft's title, then that the aircraft's profile and panels are active. If you prefer to start somewhere else, you can [teleport to a runway or gate](planning-your-flight.md#teleport).

You plan first. If you use SimBrief, Output Shift+B opens the SimBrief briefing, and the [SimBrief](planning-your-flight.md#simbrief) section explains how to load your plan and hear an AI route briefing. The [Flight Planning Window](planning-your-flight.md#flight-planning-efb) (Output Shift+E) lets you look at your route, SID, STAR and approach, and look up runways and frequencies. You check the weather with the METAR report (Output Shift+M) and the [Weather Radar](weather.md#weather-radar) (Output Shift+R).

Then you set up the aircraft: its panels in the main window, its flight computer (Input Shift+M opens the MCDU, CDU or FMC on every aircraft), and its own tablet through [Aircraft EFBs](planning-your-flight.md#aircraft-efb) (Input Shift+T on most aircraft) for fuel, payload and takeoff performance. The [First Officer](first-officer.md) (Output Shift+C on most aircraft) runs the flows with you and keeps you from missing a step on the checklists. If you use GSX for ground services, the accessible GSX window is covered in [Gates, GSX and Docking](gates-gsx-and-docking.md).

### Pushback and taxi out {#pushback-and-taxi-out}

After pushback, you want to know where you are. Output Alt+Y ([Where Am I](knowing-where-you-are.md#where-am-i)) tells you the taxiway, stand or runway you are on, and Output Alt+L describes what is around you.

ATC gives you a taxi clearance, something like *"Taxi to holding point runway 27L via A, B, hold short of runway 27R."* You open the [Taxi Planner](taxi-guidance.md#taxi-planner) with Input Shift+Y and enter exactly that, as explained in [Entering an ATC Clearance](taxi-guidance.md#taxi-clearances). BA builds a route and starts [Taxi Guidance](taxi-guidance.md): a steering tone that pans towards the way you need to turn, plus spoken turns and stops. At each hold short line it stops you, and you press Input Y to continue only once ATC has cleared you. [Ground Traffic](ground-traffic.md) callouts warn you about other aircraft near your route.

### Takeoff {#takeoff}

At the runway, taxi guidance lines you up on the centreline and tells you *"Lined up"*. [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist) switches itself on at lineup (or you start it with Output Ctrl+T). Its tone keeps you on the centreline through the takeoff roll. On aircraft that support it, BA calls *"V1"*, *"Rotate"* and *"V2"* from the speeds you entered in the flight computer.

### Climb and cruise {#climb-and-cruise}

After liftoff you climb on the autopilot or hand fly with [Hand Fly Mode](takeoff-climb-and-cruise.md#hand-fly) (Output Ctrl+H), which gives you a tone for pitch and bank. You set speeds, headings and altitudes through the [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows) (Input Ctrl+S, Ctrl+H, Ctrl+A, Ctrl+V on every aircraft). BA announces each thousand feet you cross if that setting is on, and on many aircraft it reads the flight mode annunciator to you as modes change.

In the cruise you build a [listening scan](general-flying-concepts.md#concepts-scan), keep an ear on [TCAS Traffic](takeoff-climb-and-cruise.md#tcas), check the weather ahead, and use [Location Info and Nearest City](#location-info) to hear what you are flying over.

### Descent and approach {#descent-and-approach}

Output Shift+D on most aircraft tells you the distance and time to top of descent. Before you start down, you choose your landing runway with Input Shift+D ([Choosing a Destination Runway](approach-and-landing.md#destination-runway)), which lets Output Ctrl+D read your distance to that runway and Output Ctrl+I give you an ILS report. If you want a manual landing, you can tick the manual landing assist there to get [flare and rollout tones](approach-and-landing.md#flare-assist). You can also pick your runway exit in advance with the [Landing Exit Planner](approach-and-landing.md#landing-exit) (Input Shift+X).

If you plan to fly the final approach by hand, [Visual Guidance](approach-and-landing.md#visual-guidance) (Output Ctrl+V) gives you two tones to match: one for where the aircraft should be pointing, one for where it is pointing.

### Landing and taxi in {#landing-and-taxi-in}

After touchdown, because you set a destination runway, the rollout tone keeps you on the centreline, and if you planned an exit, taxi guidance tells you when to turn off. Once you are clear of the runway, BA can route you on to your stand, and [Docking Guidance](gates-gsx-and-docking.md#docking) brings you to the exact stop mark with a beep that speeds up as you get closer. Output Ctrl+Shift+R tells you how firm your landing was.

## How BA Talks to You {#core-speech}

BA speaks through your screen reader. When you are working in BA's own windows, two different things are talking to you, and it helps to know which is which.

**Your screen reader** reads everything you touch: the name of a button as you Tab to it, the new value of a combo box as you arrow through it, the text of a list item. That is ordinary Windows behaviour and BA does not interfere with it.

**BA** adds its own announcements on top, but only for things your screen reader cannot know about. BA deliberately does not repeat what you have just done. When you choose "On" in a combo box, your screen reader has already said "On", so BA stays quiet rather than saying it a second time. BA speaks in only three situations:

- **A number you typed has been set.** When you type a value into a box and press Enter, BA confirms the exact value, because you need to know the aircraft took exactly the number you meant.
- **Something went wrong.** A value outside the allowed range, a control that could not be reached, a feature that is not available. Errors are always spoken.
- **Something changed by itself.** A master caution, a flight mode change, the gear coming down, the autopilot dropping out, a taxi instruction, the simulator pausing. These are "background" changes you did not trigger, and they are the heart of what BA announces.

<div class="callout note" role="note" markdown="1">

**Note:** One aircraft is a deliberate exception. On the TFDi MD-11, pressing a cockpit button is confirmed once with the control's resulting state, because the aircraft itself gives no other way to know whether the press did anything. The [Boeing and MD-11 chapter](boeing-aircraft-and-the-md-11.md) explains this.

</div>

Some announcements are urgent and interrupt whatever is being spoken (a "Stop" from ground traffic, a hold-short instruction, a flare-assist go-around). Most are queued politely behind the current sentence, so a routine readout never cuts off something important.

BA also reports what the simulator itself is doing. See [Pause, Slew and Sim Rate](#core-sim-state) below.

## Announcement Settings {#core-announcement-settings}

The general speech options live on the **Announcements** tab of the Settings window (File menu, then Settings; see [The Settings Window](getting-started.md#settings-overview)).

### How announcements are delivered {#how-announcements-are-delivered}

The first group asks you to *"Choose how announcements are delivered"*:

- **Screen Reader (NVDA, JAWS, etc.) - Recommended.** BA sends its announcements to your screen reader, so they come out in your normal voice, at your normal speed, and on your braille display if you use one. This is the right choice for almost everyone.
- **SAPI (Windows Speech) - Fallback.** BA speaks with the built-in Windows voice instead. Use this only if you have no screen reader running, or if your screen reader is not working with BA.

Below the two choices is a status line that tells you whether BA found a screen reader, for example *"Screen reader detected: NVDA"*. If it says no screen reader was detected, BA recommends SAPI mode so that you still hear speech.

### Other announcement options {#other-announcement-options}

- **Announce nearest city automatically.** A combo box with the choices Off, Every 1 minute, Every 2 minutes, Every 5 minutes, Every 10 minutes, Every 15 minutes and Every 20 minutes. When it is on, BA speaks the same sentence as Output C (see [Location Info and Nearest City](#location-info)) at that interval while you fly. It is Off by default. It stays silent if you have not downloaded the location data.
- **Include seconds in time announcements (Output Z / Shift+Z).** When ticked, the local-time and Zulu-time readouts include seconds. Off by default, so you hear hours and minutes only.
- **Announce GSX tooltips when Access GSX window is closed.** When ticked, GSX's progress messages (boarding, fuelling, pushback and so on) are read aloud even when the Access GSX window is hidden. Off by default. See [Gates, GSX and Docking](gates-gsx-and-docking.md).
- **Announce 1,000-foot altitude crossings.** On by default. See [Altitude Callouts](takeoff-climb-and-cruise.md#flying-altitude-callouts).

## Pause, Slew and Sim Rate {#core-sim-state}

BA tells you when the simulator changes state, because a blind pilot has no other way to notice that the world has stopped moving or sped up. You will hear:

- *"Sim paused"* and *"Sim unpaused"* when the simulator's Escape menu opens and closes.
- *"Active pause on"* and *"Active pause off"* when you toggle active pause.
- *"Slew on"* and *"Slew off"* when slew mode is turned on or off.
- *"Sim rate 4x"*, *"Sim rate 0.5x"* and so on when the simulation rate changes.

When two things happen at once they are joined in one sentence. Opening the Escape menu while active pause is on ends active pause, so you hear *"Sim paused. Active pause off"*.

Nothing is spoken while a flight is loading, and BA does not announce the state the simulator is already in when BA first connects. You only hear about changes.

## The Monitor Manager {#monitor-manager}

### What it is {#what-it-is-5}

As you fly, BA watches hundreds of cockpit values and announces them when they change: lights, switches, flight modes, warnings, autopilot states and more. Most pilots want most of these, but nobody wants all of them all the time. The Monitor Manager is a checklist of every automatic announcement for the aircraft you are flying, where you can silence any one of them.

Every aircraft has its own Monitor Manager, because every aircraft announces different things. Its title names the aircraft, for example "A320 Monitor Manager", "A380 Monitor Manager", "Fenix Monitor Manager", "PMDG Announcement Monitor", "787 Monitor Manager", "iFly 737 Monitor Manager" or "MD-11 Monitor Manager". The Headwind A330 uses the A320 one.

### When to use it {#when-to-use-it}

- A value keeps announcing and is distracting you, for example a clock counter or a light that flickers.
- You want a quieter cockpit on a long cruise.
- You want to check what you have muted before an important flight.

Muting only affects automatic announcements. The on-demand readout hotkeys always work, and the value still appears in BA's panels and Status Display.

### How to use it {#how-to-use-it-4}

1. Press Output Ctrl+M. The Monitor Manager opens with the cursor in the **Search** box.
2. Type a few letters of the announcement you want, for example "flap" or "seat belt". The list below narrows to the matching rows as you type.
3. Press Tab to move to the **Show** combo box, and Tab again to reach the list. As you enter the list, your screen reader tells you what it holds and how many rows, for example *"All variables, 12 of 300"*.
4. Arrow up and down through the rows. Each row is a checkbox. **Checked means BA announces it; unchecked means it is muted.**
5. Press Space to untick a row and mute it, or tick it again to bring the announcement back.
6. Press Escape to close the window.

Your choices are saved the moment you tick or untick a row, and they are remembered separately for each aircraft. Inside the window you can also press Alt+S to jump to the Search box and Alt+H to jump to the Show combo box.

The **Show** combo box has three choices:

- **All** lists every announcement.
- **Muted** lists only the ones you have silenced. Use this to review what you have turned off.
- **Unmuted** lists only the ones that still speak.

Each time you open the Monitor Manager it starts fresh, with an empty search and Show set to All, so you never mistake a filtered list for missing rows. It does remember the row you were last on.

<div class="callout tip" role="note" markdown="1">

**Tip:** On the aircraft where BA calls the take-off speeds (the FlyByWire Airbuses, the Fenix A320, the iFly 737 MAX8 and the MD-11), V1, Rotate and V2 each have their own row (V1, VR, V2; on the Fenix, "MCDU V1 Speed", "MCDU VR Speed" and "MCDU V2 Speed"), so you can keep some and mute others. The FlyByWire aircraft also have a separate quick toggle for their ECAM call-outs, Output Ctrl+E. See your aircraft's chapter.

</div>

### If something goes wrong {#if-something-goes-wrong-2}

- **I muted something and it still speaks.** Check that you unticked the right row: search for it and set Show to Muted to confirm. Some aircraft announce one event from two places (for example a light and an ECAM memo for the same condition); BA tries to speak it once, but if you still hear it, look for a second row with a similar name.
- **I cannot find a row.** Try a shorter search, or a different word. Rows use the cockpit's own names, for example "Nav Lights" or "Left Forward Pump".
- **I want my 1,000-foot callouts off.** These are not in the Monitor Manager. Use the Announcements tab setting described above.

## Readout Hotkeys {#readouts}

### What they are {#what-they-are}

Readout hotkeys let you ask for any instrument reading at any moment, the way a sighted pilot glances at a gauge. They are all in Output mode: press \], then the key. Each speaks one short answer and returns you to normal.

The common set below works on most aircraft. Each aircraft adds its own keys, and a few aircraft leave some out, so always check the hotkey list for the aircraft you are flying: File menu, then Hotkey List, or the [Hotkey Reference](hotkey-reference.md) at the end of this guide.

### Altitude, speed and heading {#altitude-speed-and-heading}

<div class="table-wrap" markdown="1">

| Key | What it reads |
|---|---|
| Output A | Altitude above sea level (MSL) |
| Output Q | Altitude above the ground (AGL) |
| Output V | Vertical speed |
| Output S | Indicated airspeed |
| Output T | True airspeed |
| Output G | Ground speed |
| Output M | Mach number |
| Output H | Magnetic heading |
| Output U | True heading |

</div>

### Aircraft state {#aircraft-state}

<div class="table-wrap" markdown="1">

| Key | What it reads |
|---|---|
| Output F | Fuel on board in pounds (on the PMDG Boeings, the 787 and the MD-11, tank by tank) |
| Output Shift+F | Fuel in kilograms |
| Output W | Gross weight in pounds (on the FlyByWire aircraft, with centre of gravity) |
| Output Shift+W | Gross weight in kilograms |
| Output L | Flap position |
| Output Shift+G | Landing gear position |
| Output B | Altimeter setting, in hectopascals and inches, or "Standard" |
| Output X | Transponder squawk code |

</div>

### Weather and time {#weather-and-time}

<div class="table-wrap" markdown="1">

| Key | What it reads |
|---|---|
| Output I | Wind |
| Output O | Outside air temperature in Celsius |
| Output Z | Local time at the aircraft's position, with the time zone name |
| Output Shift+Z | Zulu (UTC) time |

</div>

The local time is worked out from where the aircraft is on the globe, including daylight saving, so you hear something like *"16:38 Eastern Daylight Time"* near New York. Zulu time is spoken like *"03:30Z"*. Tick **Include seconds in time announcements** on the Announcements tab to hear *"20:30:45 British Summer Time"* and *"00:15:30Z"* instead.

### Navigation and distance {#navigation-and-distance}

<div class="table-wrap" markdown="1">

| Key | What it reads |
|---|---|
| Output D | Distance to destination, from the aircraft's FMS (not on the Fenix or the MD-11) |
| Output Shift+D | Distance and time to top of descent, once the FMS has worked it out in flight (not on the Fenix or the MD-11) |
| Output N | NAV 1 and NAV 2: frequency, station ident, localiser, glideslope and DME |
| Output Ctrl+W | The active waypoint on the navigation display: name, distance and bearing (FlyByWire aircraft) |
| Output C | Nearest city or town, or the water you are over (see [Location Info and Nearest City](#location-info)) |

</div>

Top of descent is spoken like *"145 miles to top of descent: 00:16:58"*. Once you have passed it you hear *"Past top of descent"*, and before the FMS has worked it out, *"Top of descent not yet computed"*. On the FlyByWire Airbuses, when the FMS has not worked out top of descent yet but does know your top of climb, Shift+D gives the distance to top of climb instead, for example *"85 miles to top of climb"*. On the PMDG 777, D and Shift+D can read the FMC's PROG page instead; see [FMC Settings](planning-your-flight.md#planning-fmc-settings).

### Characteristic speeds and approach capability {#characteristic-speeds-and-approach-capability}

The Shift+number keys read the speeds your aircraft cares about. What they mean depends on the aircraft:

- **Airbus (FlyByWire A320, A330, A380):** Shift+1 Green Dot, Shift+2 S speed, Shift+3 F speed, Shift+4 VLS, Shift+5 VS (stall), Shift+6 VFE Next.
- **PMDG 737, PMDG 777 and iFly 737 MAX:** the flap manoeuvring speeds, from flaps up on Shift+1 upward. The exact flap settings differ between types.
- **MD-11:** Shift+1 slat retraction speed, Shift+2 flap retraction speed.

Output Ctrl+0 reads the approach capability (the CAT rating for an autoland) on the FlyByWire A320neo, Headwind A330-900neo and FlyByWire A380X; see [Approach capability](approach-and-landing.md#land-ctrl-0).

### Landing and trim {#landing-and-trim}

- **Output Ctrl+Shift+R** reads your last landing rate, and **Output Ctrl+Shift+G** its peak g-force. See [Landing Rate and G-Force](approach-and-landing.md#land-rate).
- **Output Shift+T** turns trim announcements on and off, on every aircraft. See [Trim](takeoff-climb-and-cruise.md#flying-trim).

<div class="callout note" role="note" markdown="1">

**Note:** Values that have no real unit are read as a bare number, for example *"V1: 0"* when a speed has not been entered yet.

</div>

### Hand Fly and Visual Guidance keys {#hand-fly-and-visual-guidance-keys}

When Hand Fly or Visual Guidance is active, a set of single-letter keys works without pressing \] first: A, S, D, H, P, B, Q and V (and F for the target vertical speed in Visual Guidance). They are described in [Hand Fly Mode](takeoff-climb-and-cruise.md#hand-fly) and [Visual Guidance](approach-and-landing.md#visual-guidance).

## The Audio Language: What the Tones Mean {#audio-language}

### Why tones {#why-tones}

Speech is slow. By the time BA has said "two degrees left of centreline" the aircraft has moved on. A tone is instant and continuous: you hear the correction you need the whole time, without a single word, while your hands are busy with the rudder, the throttle or the stick. That is why BA's guidance features use sound.

All of BA's tones follow one shared set of rules. Learn them once and you can use every guidance feature.

### The rules {#the-rules}

1. **Left and right (stereo pan) tells you which way to steer.** By default the tone plays on the side you should steer **toward**. Tone in your right ear: steer right. Tone in your left ear: steer left. The further off you are, the further the tone moves to that side.
2. **Silence means "on profile".** When you are doing exactly what is needed, most steering tones go quiet. Silence is good news, not a fault. BA never just goes silent to mean "I have given up": when guidance genuinely stops, it says so in words (for example *"Go around detected. Flare assist rearmed."* or *"Rollout guidance complete"*).
3. **Pitch (high and low) means up and down, where a tone uses it.** A higher note means more nose-up, or "come up". A lower note means nose-down, or "go down". Steering tones that only guide left and right (taxi, takeoff, rollout) keep a fixed note.
4. **Pulsing means "you need to act".** A tone that turns on and off rhythmically is asking you to do something you are not doing yet. On runway lineup, the steering tone pulses about three times a second when you are stopped but not yet lined up. Docking uses a beep that speeds up as you close on the stop.
5. **A solid tone means "here".** At a gate, the docking beep becomes a steady tone at the exact stop point: brake now.

Two features use the tone as an **attitude indicator** rather than a steering cue, so rule 1 works the other way round for them:

- **Hand Fly** plays one tone that shows the aircraft's attitude. Pitch follows the nose (higher note = more nose-up, the centre note = level). Pan follows the bank: bank right and the tone moves right. To level the wings, roll away from the tone until it is centred.
- **Visual Guidance** plays two tones: one for the attitude the guidance wants and one for the attitude you have. You match them: roll until the two sit in the same place left to right, and pitch until their notes are the same and the "beating" between them stops.

### Each tone-using feature {#each-tone-using-feature}

The [Tone Cheat Sheet](tone-cheat-sheet.md) in the appendices lists every tone on one page: what its pan, pitch, silence and pulsing mean. Each feature's own section has the details: [Taxi Guidance](taxi-guidance.md), [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist), [Hand Fly Mode](takeoff-climb-and-cruise.md#hand-fly), [Visual Guidance](approach-and-landing.md#visual-guidance), [Flare and Rollout Tones](approach-and-landing.md#flare-assist) and [Docking Guidance](gates-gsx-and-docking.md#docking).

BA keeps different features' tones from playing over each other. When docking guidance takes over at the gate, the taxi steering tone goes quiet; when Visual Guidance is on, the Hand Fly tone is muted; on the landing rollout, the centreline tone and the exit guidance tone take turns.

### Settings that change the tones {#settings-that-change-the-tones}

Each feature has its own tone settings, on the **Taxi Guidance** tab (taxi steering and docking) or the **Hand Fly** tab (hand fly, visual guidance, takeoff assist, and the flare and rollout tones, which share the takeoff assist sound). The same kinds of setting appear in each place:

- **Tone type.** Every tone offers the same four sounds: **Sine (Smoothest)**, **Triangle (Smooth)**, **Sawtooth (Bright)** and **Sine (Rich)**. Choose whatever you hear most clearly over engine noise.
- **Volume.** A slider from 0 to 100 percent. Every guidance tone starts at a gentle 5 percent so that it sits underneath the sound of the aircraft.
- **Test Tone.** The tabs have a **Test Tone** button that plays a sample with your current settings, sweeping from one side to the other so you can check both ears. Press it again (it now says **Stop Test**) to stop early.
- **Direction (invert).** For taxi guidance and docking: **Invert steering tone direction (steer AWAY from the tone)**, off by default. For takeoff assist and the flare and rollout tones: **Steer toward the tone to stay on the centerline**, ticked by default. Change these only if "steer away" feels more natural to you; the two are separate, so you can set each your own way.
- **Hard pan (speaker-friendly).** On stereo speakers it can be hard to tell "slightly left" from "centred". The hard-pan options make the tone jump fully to one speaker or the other instead of drifting gradually: **Hard-pan steering tone (full left or full right; speaker-friendly)** for taxi and docking, **Hard-pan centerline tone (full left or full right; speaker-friendly)** for takeoff assist and the rollout, and **Hard-pan visual-guidance tones (speaker-friendly)** for visual guidance, which snaps once the bank is more than about one degree. All are off by default. Headphone users normally leave them off, because the gradual pan also tells you how far off you are.
- **Different sounds for different tones.** Visual Guidance's two tones default to different sounds (Triangle for the wanted attitude, Sine for your actual attitude) so they stay distinguishable even when they are exactly matched. If you change one, pick a different sound for the other. The flare's sink-rate tone automatically uses a contrasting sound to the centreline tone for the same reason.

### Choosing where the tones play {#core-audio-device}

You can send BA's guidance tones to a different audio device from the simulator. Many pilots keep the simulator on their speakers and put the guidance tones in a headset.

1. Open the Settings window and go to the **Audio** tab.
2. In **Guidance tone output device**, choose a device. **Windows default device** follows whatever Windows is using.
3. Press **Test Tone** to hear a short tone sweep left and right on the selected device. This lets you compare devices before you commit to one.
4. Read the **Status** box below for confirmation, for example *"Using Headset Earphone."*. If the device you chose could not be opened, the status box says so and names the device the test actually played on.
5. Press OK to save.

This one setting covers every guidance tone: taxi, takeoff assist, hand fly, visual and landing guidance, and the docking beeps. It does not change where your screen reader speaks; NVDA and JAWS choose their own output device.

The tones follow your hardware by themselves. If you unplug the chosen device, the tones move to the Windows default device and you hear *"Guidance tone device Headset is not available. Using the Windows default device."* (naming your device). Plug it back in and they return, with *"Guidance tone device Headset is back. Moving the guidance tones to it."* If no audio device at all is available you hear *"No audio device available for guidance tones."* A device you saved but is not plugged in is still shown in the list, marked "(not connected)", so your choice is never lost.

### If something goes wrong {#if-something-goes-wrong-3}

- **I hear no tone at all.** Remember that silence often means "on profile". Drift deliberately off heading for a moment: the tone should appear. If it does not, check the volume slider for that feature, press Test Tone on its settings tab, and check the Audio tab's Status box.
- **The tone is in the wrong ear.** Check the direction setting for that feature, and check your headphones are on the right way round. The Test Tone sweep plays left then right.
- **I cannot tell small offsets from centred on speakers.** Turn on the hard-pan option for that feature.
- **Two tones blur together in Visual Guidance.** Give the two tones different sounds on the Hand Fly tab.

## AI Display Reading and Scene Description {#ai-display-reading}

### What it is {#what-it-is-6}

Some cockpit screens have no text behind them that BA can read directly: the picture is drawn by the aircraft and nothing else. For those, BA can take a picture of the simulator and ask an AI service to read it for you. The same idea lets BA describe the view out of the window.

You choose between two AI services: **Google Gemini** or **Anthropic Claude**. You use your own account and your own key with either, and the service bills you directly, not BA.

### What it can read {#what-it-can-read}

- **Alt+D, scene description:** on every aircraft, Output Alt+D describes the outside scene: what the simulator window is showing.
- **Cockpit displays:** on the aircraft whose screens cannot be read as text, Output Alt+P, Alt+N, Alt+E, Alt+S and Alt+I read the PFD, the navigation display, the engine/warning display, the lower or system display, and the standby instruments. Which keys exist depends on the aircraft: the Fenix A320, PMDG 737 and 777, iFly 737 MAX, HorizonSim 787 and TFDi MD-11 have AI display reads. The FlyByWire aircraft read their displays as text instead, from the Panels list, and need no AI. See your aircraft's chapter for exactly what each key reads.

The AI is also used for the route briefing in the flight planning window; see [The Flight Planning Window](planning-your-flight.md#flight-planning-efb).

### You do not need to look at the display first {#you-do-not-need-to-look-at-the-display-first}

On the Fenix A320, the PMDG 737 and 777, the iFly 737 MAX and the TFDi MD-11, BA moves the simulator's camera to the cockpit view that shows the display, takes the picture, and puts your own view straight back, including a saved custom view such as a cabin or wing view. The camera is away for well under a second, and your view is back before the AI is even asked. On the HorizonSim 787 the camera is not moved for you, so switch to a cockpit view that shows the display first.

You may hear:

- *"Switch to a cockpit view first."* You are in an outside or drone view. BA does nothing until you return to the cockpit.
- *"Could not confirm the cockpit view switch; reading what is on screen."* BA could not check that the camera moved, often because the simulator is paused or on a menu. It reads whatever is on screen.
- *"Could not return to your previous view."* BA could not put your view back. It will try again on your next display read.

BA takes its picture of the simulator window itself, so a BA window sitting on top of the simulator does not end up in the picture.

### How to use it {#how-to-use-it-5}

1. Set up an AI provider once (see Settings below).
2. Press Output and the display key, for example \] then Alt+P. You hear *"Capturing PFD..."* (the display's name).
3. Wait a few seconds while the AI reads the picture. You then hear *"PFD analysis ready."* and a result window opens with the cursor at the start of the text. For a scene, you hear *"Capturing scene..."* and then *"Scene description ready"*.
4. Read the result with the arrow keys like any text.
5. Press Escape, or the **Close (ESC)** button, to close the window and return to the simulator.

Only one read runs at a time. If you press another while one is still working you hear *"A display read is already in progress."*

<div class="callout warning" role="note" markdown="1">

**Warning:** An AI reading is a description of a picture, not a reading from the aircraft's systems. It can occasionally misread a small or partly hidden number. Use it to understand the display, and confirm anything critical with a direct readout hotkey where one exists.

</div>

### Settings {#settings}

The **AI** tab of the Settings window:

- **AI Provider.** Choose **Google Gemini** or **Anthropic Claude**. This is a hard switch: the display reads, scene description and route briefing all use the provider you choose. BA never quietly switches to the other one.
- **Gemini API Key** and **Claude API Key.** Paste your key into the box for your provider. The tab has a link to each provider's key page: a free Gemini key is available from Google AI Studio (aistudio.google.com/apikey), and a Claude key from the Anthropic console (console.anthropic.com/settings/keys). Treat your key like a password and never share it.
- **Gemini Model** and **Claude Model.** Choose which model to use. Press **Refresh models** to load the current list from the provider once your key is entered. If BA cannot reach the provider, it offers a short built-in list and says so in the status line below. For Claude, the larger models are the most capable and the smaller ones are cheaper.
- **Enable Grounding with Google Search (paid feature).** Gemini only. Lets the route briefing look up current information on the web. It needs a billing account with Google. Off by default.
- **Enable web search for NOTAM route grounding (extra cost).** Claude only. Lets the route briefing search the web while it is being written, billed per search. Off by default.

<div class="callout note" role="note" markdown="1">

**Note:** Costs depend on your provider and model and are charged to your own account. Display reads and scene descriptions are single pictures and cost little each; web searches during route briefings cost extra. Check your provider's pricing page for current figures.

</div>

### If something goes wrong {#if-something-goes-wrong-4}

- **"AI provider API key not configured. Please go to File menu, Settings, AI tab."** No key is entered for the provider you selected. Add one on the AI tab.
- **"Microsoft Flight Simulator window not found."** The simulator is not running, or BA cannot see its window.
- **The provider says the model is unavailable.** Some models appear in the list but your account may not be allowed to use them. Choose a different model.
- **The provider is busy.** BA retries automatically for a short while; if it still fails, try again in a minute.
- **The display was cut off or the wrong one was read.** On the HorizonSim 787, check you were in a cockpit view that shows the whole display.

## Location Info and Nearest City {#location-info}

### What it is {#what-it-is-7}

Two tools tell you where you are over the world, as opposed to where you are on an airport (for that, see [Where Am I](knowing-where-you-are.md#where-am-i)):

- **Output C** speaks one sentence: the nearest town, the water you are over, and how far the coast or land is.
- **Output Shift+L** opens the **Location Information** window, a fuller picture with lists of cities, landmarks, airports, terrain and water around you.

Both answer instantly and work offline, from location data you download once.

### Getting the location data {#getting-the-location-data}

The data comes from GeoNames (place names) and Natural Earth (seas, coastlines, lakes, regions and state outlines). You download it once; after that, nothing is looked up online when you press a key, and no account is needed.

1. Open the Settings window and go to the **GeoNames** tab.
2. Press **Download location data**. The download is about 440 megabytes; the finished data takes about 330 megabytes on disk. Make sure you have about 1.5 gigabytes free while it builds.
3. BA speaks its progress: *"Downloading location data, about 440 megabytes to download."*, then 25, 50 and 75 percent, then *"Download finished. Building location data; this takes about a minute."*, and finally *"Location data ready, 8.4 million places."* (the number may differ slightly).

The download keeps running if you close the Settings window. If you press **Cancel download**, or lose your connection, the next press picks up where it stopped. The status box on the GeoNames tab always shows the latest state. If the location data is missing when BA starts, BA also offers to download it then (see Part 1).

Until the data is installed, Output C tells you *"Location data not downloaded. Download it in Settings, GeoNames tab."* While it is still downloading or building you hear *"Location data is still downloading."* or *"Location data is still being built."*

### Output C: the nearest place {#output-c-the-nearest-place}

Press \] then C. Over land you hear the nearest town with its state or province and country, the distance and direction, and the sea coast if it is close, for example *"Near Brighton, England, United Kingdom, 0.5 miles north, English Channel coast 1.2 miles south"*.

Over water BA tells you what you are over and how far land is, then the nearest town: *"Over Chesapeake Bay, nearest land 5.2 miles west. Near Dares Beach, Maryland, United States, 5.4 miles west."* Over a lake it gives the shore: *"Over Lake Michigan, shore 24 miles east"*.

If no town is within range you hear *"No town within 25 miles"* followed by the region and country.

The automatic nearest-city announcement (Announcements tab) speaks this same sentence on a timer.

### The Location Information window {#the-location-information-window}

Press \] then Shift+L. The window opens already filled in, with the cursor in its first box, **Where you are**. Over land it shows the state or province you are in (for example *"In: Bavaria, Germany"*), the mountain range or desert if you are over one, the named coast if it is within range, and the time zone. Over water it shows the sea or lake, the nearest land or shore, and the nearest region.

Press Tab to move through the other boxes, each a read-only text you can arrow through:

- **Major Cities**
- **Tourist Landmarks**
- **Terrain** (mountains, hills, valleys, capes, islands)
- **Nearby Places**
- **Cardinal Directions** (the nearest towns to the north, east, south and west)
- **Airports**
- **Water Bodies** (lakes, bays, gulfs, straits and coves)

A box with nothing in range says so, for example *"No major cities found."* Press Escape to close the window and return to the simulator.

### Settings {#settings-2}

The rest of the **GeoNames** tab controls how far each search reaches and how much it lists:

- **Distance Ranges:** **Nearby Cities Range**, **Cardinal Cities Range**, **Major Cities Range**, **Landmarks Range** (used by all four landmark lists) and **Coast Range** (how close the sea coast must be, over land, before C mentions it; 50 by default, up to 1,000). Over water the nearest land is always given, whatever the Coast Range.
- **Minimum Population for Major Cities:** how big a town must be to count as a major city (50,000 by default).
- **Distance Units:** **Miles** or **Kilometers**. This changes the spoken and shown distances.
- **Maximum Results Per Category:** how many entries each list shows (Nearby Places, Major Cities, Airports, Terrain Features, Water Bodies, Tourist Landmarks).
- **Reset to Defaults** puts the ranges back.

The ranges are capped at 310 miles (500 kilometres), and the Coast Range at 1,000, so a press never searches half a continent.

<div class="callout note" role="note" markdown="1">

**Note:** The coastline data is accurate to roughly a kilometre. It is right for "the coast is 5 miles west" in flight, but not for judging a few hundred metres on a coastal airport.

</div>

### If something goes wrong {#if-something-goes-wrong-5}

- **C says the data is not downloaded.** Go to the GeoNames tab and press Download location data.
- **The download stalled.** BA says *"Location data download stalled, no data for 20 seconds."* and retries by itself. If it gives up, press the button again: it resumes rather than starting over.
- **C says the data will be ready at the next start.** Another program (often antivirus) was holding the file. Restart BA.
- **Distances are in the wrong unit.** Change Distance Units on the GeoNames tab. This is separate from the ground distance unit used by taxi guidance.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Getting Started](getting-started.md)
- [Next chapter: The First Officer and Checklists](first-officer.md)
- [Guide contents](index.md)

</nav>
