---
title: "General Flying Concepts"
---

<div class="guide-part" markdown="1">

Chapter 1 of 1 in [Part 8: General Flying Concepts](index.md#part-8-general-flying-concepts) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [The Phases of a Flight](#concepts-phases)
- [Building a Listening Scan](#concepts-scan)
- [Automation: Who Is Flying the Aircraft?](#concepts-automation)
- [The Flight Mode Annunciator (FMA)](#concepts-fma)
- [Approach Types in Plain Terms](#concepts-approaches)
- [Stable Approach Basics](#concepts-stable)
- [ATC Basics](#concepts-atc)
- [Runway Safety for a Blind Pilot](#concepts-runway-safety)
- [Checklists](#concepts-checklists)
- [Where to Learn More](#concepts-learn-more)

</nav>

This chapter steps back from individual aircraft: how an airline flight is put together, how airliner automation thinks, what ATC expects of you, and how to keep a picture of the flight in your head with BA. Nothing here is specific to one type. Where Airbus and Boeing do things differently, both are described. Everything is written for the simulator: real-world flying needs real training, real manuals and a real instructor, and nothing in this guide replaces them.

<div class="callout tip" role="note" markdown="1">

**Tip:** You do not need to read this chapter in one go. If you are brand new to airliners, read the phases of flight first (and the example flight in [A Flight with BA, Start to Finish](core-concepts.md#concepts-story), in Part 2), then come back to automation and the FMA once you have flown a few circuits.

</div>

## The Phases of a Flight {#concepts-phases}

Every airline flight follows the same broad shape. Knowing which phase you are in tells you what to expect next and which BA features matter right now.

- **Preflight.** The aircraft is parked at a stand (a gate or a remote parking spot). You power it up, program the flight computer with the route, load fuel and passengers, work out takeoff speeds and settings, and get your route clearance from ATC.
- **Pushback and engine start.** A tug pushes the aircraft backwards off the stand, because airliners cannot reverse on their own. Engines are usually started during or just after the push.
- **Taxi out.** You drive along the taxiways to the departure runway, following the route ATC gives you, and stop at the holding point before the runway.
- **Takeoff.** Cleared onto the runway, you line up on the centreline, set takeoff thrust, and accelerate. At the decision speed V1 you are committed to go; at VR you rotate (raise the nose); V2 is the safe climb speed if an engine fails.
- **Climb.** Gear up, flaps retracted in stages as speed builds, then a long climb to cruising altitude, usually with the autopilot on from a few hundred or a few thousand feet.
- **Cruise.** Level flight along the planned route. This is the quiet part: monitoring fuel, weather and position, and preparing for the arrival.
- **Descent.** Starting at the top of descent, a point the flight computer calculates, you descend towards the destination, often on a published arrival procedure (a STAR).
- **Approach.** The final part of the descent, lined up with the landing runway, with gear down and flaps set for landing. This might be an ILS, an RNAV approach or a visual approach.
- **Landing.** Crossing the runway threshold at about 50 feet, flaring just above the ground, touching down, and slowing down on the runway with reverse thrust and brakes.
- **Taxi in.** Leaving the runway at an exit taxiway, then taxiing to your arrival stand, where you park, set the parking brake and shut down.

## Building a Listening Scan {#concepts-scan}

A sighted pilot keeps a "scan": the eyes move over the instruments in a pattern, again and again, so nothing drifts unnoticed. You can do the same with your ears. BA gives you two kinds of information: automatic announcements, which come to you, and readout hotkeys, which you ask for. A good scan uses both.

Let the automatic announcements do the watching for you. Master warnings and cautions, mode changes, gear and flap movement and altitude crossings are announced as they happen on most aircraft. If an announcement is too chatty for you, silence just that one in the [Monitor Manager](core-concepts.md#monitor-manager) (Output Ctrl+M) rather than ignoring the lot.

Then ask for what matters in each phase. The readout keys are listed in [Readout Hotkeys](core-concepts.md#readouts). A sensible pattern:

- **Taxi:** ground speed every so often (BA can announce it automatically, see the Taxi Guidance settings), Output Y for taxi status, and Output Alt+G for nearby ground traffic.
- **Takeoff and initial climb:** let the tones and callouts work; check Output S (airspeed) and Output V (vertical speed) once you are climbing.
- **Climb:** every minute or two, altitude (Output A), airspeed (Output S) and vertical speed (Output V). Check the autopilot targets with the FCU or MCP readouts (Output Shift+H, Shift+S, Shift+A, Shift+V) after every change you make.
- **Cruise:** every few minutes, altitude and speed; now and then fuel (Output F), wind (Output I), and distance to destination (Output D). Check the fuel against your plan once an hour or so.
- **Descent:** every minute, altitude, speed and vertical speed, plus distance to the runway (Output Ctrl+D) once you have chosen one.
- **Approach:** much more often — every 15 to 30 seconds — altitude above the ground (Output Q), airspeed and vertical speed. Gear (Output Shift+G) and flaps (Output L) before you are established.

Two habits make the scan work. First, after you change anything, read it back: set 5,000 feet, then press Output Shift+A to hear that the FCU or MCP really says 5,000. Second, whenever you hear a mode change, ask yourself whether it is the one you expected. Surprises are where mistakes begin.

<div class="callout tip" role="note" markdown="1">

**Tip:** Fewer, well-chosen checks beat constant ones. If you press a key every few seconds in the cruise, the important announcement you are waiting for may get talked over.

</div>

## Automation: Who Is Flying the Aircraft? {#concepts-automation}

A modern airliner has layers of automation. Understanding them is the single most important thing for flying one well, sighted or not.

- **Flight director.** Commands shown to the pilot: "pitch up a little, bank left". You can follow them by hand.
- **Autopilot.** Flies the aircraft to follow those same commands. Most airliners have two or three autopilots, but you normally use one, except for autoland where more may be engaged.
- **Autothrust or autothrottle.** Controls engine thrust to hold a speed or a thrust setting.
- **Flight management system (FMS).** The computer that holds your route, performance data and speeds. You talk to it through the MCDU or CDU.

The autopilot needs to know what to follow. That is where modes come in.

### Selected and managed modes {#selected-and-managed-modes}

Every axis (speed, lateral path, vertical path) can be told what to do in one of two ways:

- **Selected:** you dial a number on the glareshield panel and the aircraft follows that number. "Fly heading 270." "Hold 250 knots." "Climb at 1,500 feet per minute."
- **Managed** (Airbus) or **FMS-driven** (Boeing, called LNAV and VNAV): the aircraft follows the flight plan and speeds in the FMS. You do not touch the heading knob; the aircraft turns at each waypoint by itself.

Pilots mix the two all the time. A common example: ATC gives you a heading to fly away from your route, so you select a heading, and later they clear you back "direct to" a waypoint, so you return to managed or LNAV navigation.

### Airbus FCU and Boeing MCP {#airbus-fcu-and-boeing-mcp}

Airbus and Boeing put the same ideas on very different panels.

On an **Airbus**, the panel is the **FCU** (Flight Control Unit). Each knob can be **pushed** or **pulled**. Pushing a knob usually hands that axis to the FMS (managed); pulling it means "use the number I have dialled" (selected). So pulling the heading knob flies the selected heading, and pushing it returns to the flight plan route. Pushing the altitude knob climbs or descends in managed mode, respecting the route's altitude constraints; pulling it climbs or descends in open mode straight to the selected altitude. BA's Airbus aircraft give you push and pull keys and windows for each knob — see [Autopilot Windows](takeoff-climb-and-cruise.md#autopilot-windows).

On a **Boeing**, the panel is the **MCP** (Mode Control Panel). There is a button for each mode: LNAV and VNAV for the flight plan, HDG SEL for a selected heading, LVL CHG or FLCH for a climb or descent at a set speed, V/S for a set vertical speed, ALT HOLD to stay level, and LOC and APP for the approach. You press the button for the mode you want, and the windows hold the numbers it uses.

### Autothrust and autothrottle {#autothrust-and-autothrottle}

The two makers also differ on thrust. On a Boeing, the autothrottle physically moves the thrust levers, so the levers always show the thrust the engines are making. On an Airbus, the thrust levers sit in fixed positions called detents, and in normal flight they stay in the CLB detent from shortly after takeoff until just before landing; the autothrust changes the thrust without moving them. That is why Airbus pilots talk about "thrust levers to CLB" after takeoff and "idle" at touchdown. On some Airbus aircraft, such as the FlyByWire A380X, BA announces each lever detent as you reach it.

<div class="callout warning" role="note" markdown="1">

**Warning:** In both designs, disconnecting the autothrust or autothrottle without a plan can leave you with the wrong thrust. Know what the engines will do before you disconnect anything.

</div>

## The Flight Mode Annunciator (FMA) {#concepts-fma}

The FMA is a strip at the top of the primary flight display that tells you, at every moment, exactly which modes are active and which are armed (waiting to become active). It is how a pilot answers the question "what is the aircraft going to do next?"

On an **Airbus**, the FMA has five columns, read left to right:

- **Thrust:** what the autothrust is doing, for example SPEED, MACH, THR CLB or THR IDLE.
- **Vertical:** for example CLB, OP CLB, ALT, ALT CST, DES, V/S, G/S.
- **Lateral:** for example NAV, HDG, LOC, RWY.
- **Approach capability:** for example CAT 3 DUAL, or blank.
- **Engagement:** which autopilot, flight directors and autothrust are on, for example AP1, 1FD2, A/THR.

Active modes are shown in green on the top line; armed modes are shown underneath in blue or magenta.

On a **Boeing**, the FMA shows the autothrottle mode on the left, the roll (lateral) mode in the middle and the pitch (vertical) mode on the right, for example SPD, LNAV, VNAV PTH. Armed modes appear underneath. Above it, the autopilot status shows FD, CMD, or LAND 3 during an autoland.

Why it matters to you: the FMA is the truth, and the panel is only a request. Pressing APPR or APP does not mean the aircraft will capture the glideslope; the FMA showing G/S armed, and then G/S active, does. On aircraft where BA reads the FMA aloud, listen for each change and check it against what you expected. On aircraft where it does not, use the engaged-mode readouts in the autopilot panel or window your aircraft provides.

## Approach Types in Plain Terms {#concepts-approaches}

An approach is a published path from the end of the arrival down to the runway. There are three kinds you will fly most often.

- **ILS (Instrument Landing System).** Two radio beams from the runway: the **localizer** (LOC) gives left and right guidance along the centreline, and the **glideslope** (G/S or GS) gives a descent path, usually 3 degrees. The autopilot can capture both and fly the aircraft down to a few hundred feet, or all the way to touchdown in an autoland if the aircraft and the runway are certified for it. Output Ctrl+I gives you an ILS report once you have chosen your destination runway.
- **RNAV or RNP approach.** Uses GPS and the FMS instead of radio beams. The lateral path comes from the flight plan; on most airliners the vertical path is built by the FMS too. The FMA modes have different names (for example FINAL APP on an Airbus), but the idea is the same.
- **Visual approach.** You fly to the runway by looking at it. For a blind pilot in the simulator, BA's [Visual Guidance](approach-and-landing.md#visual-guidance) and the destination runway readouts take the place of looking out of the window.

Every instrument approach has a **minimum**: a decision altitude or height (DA or DH), or a minimum descent altitude (MDA). At that point you must be able to see the runway, or go around. Minimums are printed on the approach chart and are not part of the navigation data BA reads, so BA cannot tell you what they are; you set them in the aircraft from the chart or your flight plan.

## Stable Approach Basics {#concepts-stable}

Airlines require an approach to be **stable** by a set height, typically 1,000 feet above the runway in cloud and 500 feet in good weather. If it is not stable by then, the crew goes around. That rule exists because unstable approaches cause a large share of landing accidents, and it is just as useful in the simulator.

Typical stable approach criteria are:

- On the correct lateral and vertical path, needing only small corrections.
- In the landing configuration: gear down, landing flaps set.
- At the approach speed, not slower, and not more than a few knots faster.
- Descending at a normal rate, usually no more than about 1,000 feet per minute.
- Thrust set to match, not at idle.
- Landing checklist complete.

With BA, check these with Output Shift+G (gear), Output L (flaps), Output S (speed), Output V (vertical speed) and Output Ctrl+D or Ctrl+I (distance and path) as you pass through about 1,500 feet above the ground.

<div class="callout tip" role="note" markdown="1">

**Tip:** A go-around is a normal manoeuvre, not a failure. If your approach is not stable, go around, climb away, and try again. BA's flare assist even tells you *"Go around detected. Flare assist rearmed."* so you are ready for the next attempt.

</div>

## ATC Basics {#concepts-atc}

Air traffic control keeps aircraft apart and tells you where to go. In the simulator you might talk to the simulator's own ATC, to an AI service such as SayIntentions, or to real people on a network such as VATSIM. The principles are the same everywhere.

### Frequencies and who you talk to {#frequencies-and-who-you-talk-to}

At a large airport you will typically speak to several controllers in turn:

- **ATIS** — a recorded broadcast of the weather, the runways in use and the altimeter setting. Each version has a letter, "information Alpha", "information Bravo", and you tell the controller which one you have.
- **Clearance Delivery** — gives you your route clearance before you start.
- **Ground** — pushback and taxi.
- **Tower** — takeoff and landing, and anything on the runways.
- **Departure, Approach and Centre** — the radar controllers who look after you in the air.

When a controller says *"contact Tower on 118.7"*, you change to that frequency and call them.

### Clearances and readbacks {#clearances-and-readbacks}

A **clearance** is permission to do something specific. Until you are cleared, you do not do it. When you receive a clearance you **read it back**, repeating the important parts so the controller can check you heard them correctly.

Always read back:

- Runway instructions: hold short, cross, line up, cleared for takeoff, cleared to land.
- Altitudes, headings and speeds you are assigned.
- Frequencies, squawk (transponder) codes and the altimeter setting.
- Taxi routes, if the controller gives you one.

End your readback with your callsign. If you did not understand something, say "say again". It is always better to ask than to guess.

### Taxi phraseology {#taxi-phraseology}

These are the words you will hear most often on the ground, and what they mean.

- **"Taxi to holding point runway 27L via A, B"** — follow taxiway A, then B, to the holding point for runway 27L. In the United States you will hear "taxi to runway 27L via A, B" instead.
- **"Hold short of runway 27R"** — stop before the runway's hold short line and do not enter it. You must read this back.
- **"Cross runway 27R"** — you may cross that runway. You must read this back too. Never cross a runway without hearing this.
- **"Hold position"** — stop where you are.
- **"Give way to the Airbus coming from your left"** — let that aircraft pass first.
- **"Follow the 737 ahead"** — taxi behind that aircraft.
- **"Line up and wait runway 27L"** (in some countries "line up runway 27L") — enter the runway and line up on the centreline, but do not take off yet.
- **"Cleared for takeoff runway 27L"** — you may take off. This is the only phrase that means that.
- **"Expedite"** — please be quick.

BA's [Taxi Planner](taxi-guidance.md#taxi-planner) is built around these clearances: you enter the taxiways in the order ATC gave them and tell it which runways to hold short of. [Entering an ATC Clearance](taxi-guidance.md#taxi-clearances) has worked examples. If you fly with SayIntentions, BA can even fill the planner in from the controller's last taxi instruction — see [SayIntentions](networks-and-integrations.md#sayintentions).

### The altimeter setting {#the-altimeter-setting}

The altimeter needs to know the air pressure to show your height correctly. Near the ground, ATC gives you the local pressure, called **QNH**, in hectopascals (for example 1013) or inches of mercury (for example 29.92). Above the **transition altitude** you change to the **standard** setting (1013 hPa or 29.92 inches) so everyone at high level uses the same reference; heights are then flight levels, such as FL350 for about 35,000 feet. On the way down you switch back to QNH at the **transition level**. Output B reads your altimeter setting on most aircraft.

## Runway Safety for a Blind Pilot {#concepts-runway-safety}

Runways are the most dangerous place on an airport. A runway incursion, an aircraft on a runway where it should not be, is one of the most serious events in aviation. In the simulator it costs you nothing but your flight, but on an online network it can spoil someone else's, and good habits here are good habits everywhere.

- **Never enter or cross a runway without a clearance.** BA stops you at every runway hold short with *"Stop. Hold short of"* and the runway, and waits for you to press continue (Input Y). Press it only when ATC has actually cleared you.
- **Read back every runway instruction.** Hold short, cross, line up, takeoff and landing clearances, every time.
- **Know where you are.** Output Alt+Y tells you at any moment whether you are on a taxiway, a stand or a runway. Use it whenever you are unsure, and especially before you move after a stop.
- **Take warnings seriously.** If BA says *"Warning: approaching runway"* and that you are off route, stop and check your position and your clearance.
- **"No traffic seen" is not "clear".** At a hold, BA's runway watch tells you what traffic it can see on the runway and on final. It never says a runway is clear, because it cannot see everything ATC can. The controller's clearance is what lets you go.
- **After landing, get off the runway.** Do not stop on the runway unless told to. Use the [Landing Exit Planner](approach-and-landing.md#landing-exit) so you know which exit you are aiming for, and stop to tidy up only once you are clear of the runway.
- **When in doubt, stop and ask.** "Hold position" is always safe, and saying "confirm cleared to cross runway 27R" costs you five seconds.

## Checklists {#concepts-checklists}

Airline crews work from two kinds of procedure. **Flows** are the sequences of actions you do from memory as you work around the cockpit, such as setting up the overhead panel. **Checklists** are short lists read afterwards to confirm the most important items were done. They are read at fixed points: before start, after start, before takeoff, after takeoff, approach, landing, after landing and shutdown.

Use checklists in the simulator even when nobody is watching. They catch the forgotten item — the parking brake still set, flaps not selected for takeoff, the altimeter not reset — before it matters. [The First Officer and Checklists](planning-your-flight.md#checklists) explains the flows and checklists BA's First Officer runs with you, and some aircraft, such as the FlyByWire A380X, have their own electronic checklists that BA can read.

<div class="callout note" role="note" markdown="1">

**Note:** BA's built-in checklists are a convenience, not an official procedure. For the real sequence, use the documentation that came with your aircraft.

</div>

## Where to Learn More {#concepts-learn-more}

You will learn fastest by flying, but these kinds of resources help a great deal:

- **Your aircraft's own documentation.** Most add-on aircraft come with a tutorial flight, a pilot's operating handbook or flight crew operating manual, and a quick reference. They are written for sighted users, but the procedures and explanations are what you need.
- **The developers' online documentation.** FlyByWire, PMDG, Fenix and others publish manuals and guides on their websites; many are plain text pages that work well with a screen reader.
- **Flight simulation communities.** Forums, Discord servers and user groups for your simulator, your aircraft and your network. The MSFS Blind Assist Discord server (linked from the project's home page) is the place to ask other blind pilots how they do things.
- **Network training.** Online networks such as VATSIM run free pilot training and publish guides to phraseology and procedures, which teach you how to talk to ATC with confidence.
- **Aviation theory books and courses.** Private pilot ground school material explains weather, navigation, aerodynamics and radio procedure from the beginning. Many are available as audiobooks or accessible ebooks.
- **Podcasts and videos.** Many airline pilots talk through real flights, procedures and incidents. Look for ones that describe what they are doing rather than only showing it.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md)
- [Next chapter: Tone Cheat Sheet](tone-cheat-sheet.md)
- [Guide contents](index.md)

</nav>
