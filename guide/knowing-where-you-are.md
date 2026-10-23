---
title: "Knowing Where You Are"
---

<div class="guide-part" markdown="1">

Chapter 1 of 4 in [Part 5: On the Ground](index.md#part-4-on-the-ground) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Where Am I](#where-am-i)
- [Look Around](#where-look-around)
- [The Surroundings Window](#surroundings)
- [Callouts While You Taxi](#where-taxi-callouts)
- [The Off-the-Pavement Alerts](#where-off-pavement)
- [Where the Names Come From](#where-name-sources)
- [Settings for This Chapter](#where-settings)
- [If Something Goes Wrong](#where-troubleshooting)

</nav>

A sighted pilot glances out of the window and knows at once that they are on taxiway Bravo, next to Concourse C, with the fuel farm behind them. BA gives you the same picture in words, when you ask and as you taxi. These are ground features; in the air, use [Location Info and Nearest City](core-concepts.md#location-info) instead.

## Where Am I {#where-am-i}

### What it is {#where-am-i-what}

Where Am I tells you, in one short sentence, what is under your aircraft right now and which airport you are at. It works whether or not taxi guidance is running, so you can use it at any time on the ground: parked at the gate, holding short of a runway, or lost on an apron.

### How to use it {#where-am-i-how}

1. Make sure you are on the ground.
2. Press Output Alt+Y.
3. BA speaks your location, for example:
   - *"Taxiway B at KJFK."*
   - *"Gate A25 at KJFK."*
   - *"Runway 22L at KJFK."*
   - *"Near taxiway K at EGLL."*

### What you will hear {#where-am-i-hear}

BA checks in a fixed order, so the most useful answer wins:

- **A stand or gate.** If you are within about 40 metres of a parking spot, BA names it: *"Gate A25"*, or *"Parking 12"*, *"Ramp 3"*, *"Hangar 2"* if that is how the airport names its spots. Close to a runway, BA only names a stand when you are very close to it, so that a hold line or the runway itself is never reported as a gate.
- **A runway.** If you are on runway pavement, anywhere along its length, BA names the runway by the end nearer to you: *"Runway 27L"*.
- **A taxiway.** If you are on a named taxiway: *"Taxiway B"*.
- **Near a taxiway.** If you are not on any mapped line but there is a named taxiway point within about 60 metres: *"Near taxiway K"*. This often means you are on an apron or a stretch of pavement the airport data does not map.

When nothing matches, you hear *"Not on a known taxiway or ramp at"* followed by the airport code. Other answers you may hear:

- *"In flight."* Where Am I only works on the ground.
- *"No airport nearby."* You are not at an airport in the navigation database.
- *"No taxi data available for"* and the airport code. The navigation database has no taxiway map for this airport.
- *"Airport database not available."* BA has no navigation database loaded. See [The Navigation Database](getting-started.md#nav-database).

<div class="callout tip" role="note" markdown="1">

**Tip:** Where Am I always uses the airport you are actually standing at, even a small field with a three-character code, never a nearby heliport or a neighbouring field.

</div>

## Look Around {#where-look-around}

### What it is {#where-look-around-what}

Look Around answers a bigger question: not just "what am I on?" but "what is around me?". In one sentence you hear your Where Am I line, the apron or concourse you are in, and up to four nearby buildings and places, each with its direction and distance.

### How to use it {#where-look-around-how}

1. On the ground, press Output Alt+L.
2. Listen. A typical answer sounds like this:

*"Gate B12 at KATL. At Concourse B. Concourse A, ahead and to the left, 180 metres. Control tower, to the right, 420 metres. Fuel, behind and to the left, 350 metres."*

The first time at an airport the answer can take a few seconds; you hear *"Looking around."* first. A late answer waits its turn rather than cut off a taxi instruction.

### What you will hear {#where-look-around-hear}

The answer has three parts.

1. **Where Am I.** The same line Output Alt+Y gives.
2. **Where you are.** One of these, if BA can tell:
   - *"On the Commercial Ramp."* You are on a named apron or de-ice pad.
   - *"On the GA ramp."* You are among the stands of a ramp.
   - *"On the Apron."* You are inside an apron that has no name.
   - *"At Concourse B."* You are within about 120 metres of a concourse or terminal.
3. **What is nearby.** Up to four features within 600 metres, nearest first, each as *name, direction, distance*. You never hear two of the same kind, except hangars and FBOs, because a general aviation field can be lined with hangars. Several unnamed hangars are grouped into one entry, for example *"Hangars, to the left, 80 metres."* Anything you are standing right next to is described as *"here"* instead of with a direction.

Directions are always relative to the way your aircraft is pointing:

<div class="table-wrap" markdown="1">

| You hear | Means |
|---|---|
| ahead | Within about 20 degrees of your nose |
| ahead and to the right / left | Between about 20 and 70 degrees off your nose |
| to the right / left | Roughly abeam, between about 70 and 110 degrees |
| behind and to the right / left | Between about 110 and 160 degrees |
| behind | Within about 20 degrees of your tail |

</div>

Ground traffic callouts use exactly the same words, so "ahead and to the left" always means the same thing in BA.

The kinds of place BA knows about are terminals, concourses (including piers and satellites), FBOs, hangars, the control tower, fuel, cargo areas, fire stations, helipads, aprons, de-ice pads and airport offices. When a place has no name of its own, BA uses the kind word: *"Fuel"*, *"Control tower"*, *"Cargo ramp"*, *"De-ice pad"*.

Other answers:

- *"No surroundings data for"* and the airport code. BA could not find any buildings or places for this airport.
- *"Nothing within 600 metres."* There is data, but nothing close enough to mention.
- *"Surroundings lookup failed."* Something went wrong. Try again in a moment.
- *"In flight."* Look Around only works on the ground.

Distances follow the **Use feet for distances (default: metres)** setting on the Taxi Guidance tab.

## The Surroundings Window {#surroundings}

### What it is {#surroundings-what}

The Surroundings window is a fuller, browsable version of Look Around. It lists everything within about a kilometre of you, and it also shows two things a pilot often needs at an unfamiliar airport: whether fuel is available, and every radio frequency the airport has. You can tune any of those frequencies into COM 1 with a single key.

### How to use it {#surroundings-how}

1. On the ground, press Output Ctrl+Shift+L.
2. The window opens, titled *"Surroundings at"* followed by the airport code. BA does not speak a summary when it opens. Your screen reader reads the window and the first list item.
3. The window has up to three lists. Press Tab to move between them and the Up and Down arrows to move through a list. Each list announces its name and your position in it, for example "3 of 12". A list that would be empty is left out.
   - **Airport**: the fuel line, for example *"Fuel available"*, *"Avgas available"* or *"Jet fuel available"*. This list is missing when the airport has no fuel.
   - **Frequencies**: one row per frequency, each starting with its kind, in the order you would normally use them: ATIS, clearance delivery, ground, tower, departure, approach, center, CTAF, UNICOM and the rest. For example *"Ground 121.9"* or *"Tower 118.3"*. Where an airport has several frequencies of one kind with different names, the name is added to tell them apart: *"Ground 129.25, RAMP CONTROL"*. Because every row starts with its kind, you can press a letter to jump, for example G for ground or T for tower.
   - **Nearby, 12 items** (the number changes): every place within about a kilometre, nearest first, as *name, detail, direction, distance*. The detail adds useful extras where BA knows them, such as the airline that uses a concourse or the operator of an FBO.
4. To tune a frequency, move to it in the Frequencies list and:
   - press **Enter** to put it into COM 1 standby, or
   - press **Shift+Enter** to put it straight into COM 1 active.
5. Press **Escape**, or use the **Close** button (Alt+C), to close the window. Focus goes back to the window you were in when you pressed the hotkey.

The window stays open after you tune, so you can tune several frequencies one after another. It is a snapshot: it does not update as you move. Press Output Ctrl+Shift+L again for a fresh one, and the new window replaces the old one.

### What you will hear when you tune {#surroundings-tune}

A list cannot tell you that anything happened, so BA reads COM 1 back after tuning and tells you what it actually holds:

- *"COM 1 standby 121.9"* or *"COM 1 active 121.9"*: the frequency is set.
- *"Could not tune COM 1 standby to 121.705. It reads 121.7."*: the radio set something different, usually because it uses a different channel spacing.
- *"COM 1 did not report back after tuning 121.9."*: BA sent the frequency but could not read the radio back.
- *"Not connected to the simulator."*: BA is not connected.

Some aircraft cannot be tuned this way. On the FlyByWire A380X you hear *"This aircraft tunes its radios only through its RMP window, input mode Ctrl+Shift+R."*

When there is nothing to show at all, BA speaks instead of opening an empty window: *"No surroundings data for"* the airport, or *"Nothing within 1000 metres."*

### Taxiing to a place {#surroundings-taxi-to-place}

When you have found a place you want to go to, for example an FBO after landing, the fuel island or a cargo ramp, you can taxi straight to it. In the taxi planner, choose the destination type **Place** and pick it by name. BA takes you to the stand in front of it, or to the nearest taxiway point when there is no stand, with docking guidance where the scenery gives a stop position. See [The Taxi Planner](taxi-guidance.md#taxi-planner) for the details.

## Callouts While You Taxi {#where-taxi-callouts}

As well as answering when you ask, BA can tell you things as you taxi. Each of these has its own setting on the **Taxi Guidance** tab of the Settings window, so you can choose how much you hear.

### Buildings you pass {#where-passing-callouts}

With **Announce airport buildings as you taxi past them** switched on, BA tells you when a building comes abeam:

*"Passing Concourse B, on the left."*

The side is *"on the left"* or *"on the right"*. BA calls a building at the moment you are closest to it, once you have driven past rather than stopped beside it. It names terminals, concourses, FBOs, the control tower, fuel, cargo areas, fire stations, and hangars that have a real name. It does not say "Passing Hangar" for an anonymous hangar, because that names nothing you could look for.

To keep things calm:

- each building is called at most once every five minutes, and BA says at most one passing callout every ten seconds;
- callouts are spoken only while you are taxiing, between about 2 and 40 knots;
- they wait their turn behind taxi guidance, and never interrupt it;
- they are never spoken on a runway, during takeoff, during the landing rollout, or while docking at a gate.

This setting is off by default.

### Taxiways you cross {#where-crossing-taxiways}

While taxi guidance is running, BA names the taxiways you cross at junctions where your route goes straight on: *"Crossing taxiway Link 53."* or, for more than one, *"Crossing taxiways C, D."* This helps you check your progress against the clearance. It is controlled by **Announce crossing taxiways (e.g., "Crossing taxiway Link 53")**, which is on by default. Turn it off for a quieter taxi. The rest of what taxi guidance says is described in [Everything Taxi Guidance Says](taxi-guidance.md#taxi-callouts).

### Ground speed {#where-ground-speed}

Taxi speed is hard to judge without seeing the ground move. BA can speak your ground speed whenever it reaches a new step:

*"15 knots."*

Set **Announce ground speed every:** to **Every 5 knots** or **Every 10 knots**. The default is **Off**. Speeds are rounded to the nearest step, so with 5-knot steps anything from about 13 to 17 knots reads as *"15 knots."* BA leaves a little margin before it changes the number, so the callout does not flicker when you are right on a boundary.

Ground speed callouts work in every ground phase: taxiing, the takeoff roll and the landing rollout. They work whether or not taxi guidance is running, and they stop in the air. They are queued, so they never cut off an instruction.

During the takeoff roll you may want fewer of them, so that they do not crowd out the takeoff assist callouts. **During takeoff assist, announce ground speed every:** has these choices:

- **Same as taxi (default)**: use the taxi setting.
- **Off**: no ground speed callouts while takeoff assist is active.
- **Every 5 knots** or **Every 10 knots**: a different step just for the takeoff roll.

See [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist) for the rest of the takeoff callouts.

## The Off-the-Pavement Alerts {#where-off-pavement}

A blind pilot cannot see the edge of the taxiway. BA has two alerts that tell you when you have left the hard surface.

### "Off the pavement, on grass" (optional) {#where-surface-callout}

With **Tell me when I leave the paved surface** switched on, BA watches what the simulator says is under your wheels:

- *"Off the pavement, on grass."* Your wheels have left the paved surface. The last word tells you what you are on now: *"grass"*, *"an unpaved surface"*, *"water"*, or *"snow or ice"*.
- *"Back on pavement."* You are back on a paved surface.
- *"Now on grass."* (or another surface) The surface changed again while you were already off the pavement.

BA only speaks when you have travelled about 12 metres on the new surface, so a wheel clipping the grass at a corner is not announced. It ignores changes between paved surfaces, such as asphalt to concrete, because those do not matter. It stays silent when you are stopped, when you have just been placed somewhere, and after a teleport.

Unlike the other surroundings callouts, this one is still spoken during the takeoff roll and the landing rollout, because running off the side is most dangerous exactly there. It is off by default. We recommend switching it on.

### "Off pavement" during the landing roll {#where-landing-off-pavement}

During the landing roll and while you follow a landing exit, BA has a second, built-in alert that needs no setting:

*"Off pavement."*

It means you have left the runway you landed on and every mapped taxiway, for example by running off the side during the rollout or cutting a corner on the exit. You hear it after about one second off the pavement while moving at 5 knots or more, and again every six seconds while you stay off and keep moving. It does not say which way to turn: the steering tone is the only guide to direction, so a spoken "left" could never contradict it. Follow the tone back to the pavement. When both alerts are on, you only hear one of them for each excursion. See [The Landing Exit Planner](approach-and-landing.md#landing-exit) for the rest of the rollout guidance.

## Where the Names Come From {#where-name-sources}

Everything above depends on names: taxiway names, stand names and building names. BA combines several sources.

### The navigation database {#where-names-navdata}

Your navigation database, built from the simulator's own scenery, is the foundation. It provides the taxiway map, the stands, the runways, the fuel information and the frequencies. It has no buildings in it, though, and at some airports many taxiways have no names. See [The Navigation Database](getting-started.md#nav-database).

### Online names: OpenStreetMap and X-Plane {#where-names-online}

With **Online taxiway, gate and airport building names (OpenStreetMap + X-Plane)** switched on, which is the default, BA fetches real-world data from OpenStreetMap and the X-Plane Scenery Gateway:

- names for taxiways the navigation database leaves unnamed;
- alternative names for stands, so a gate a controller calls "A24" can be matched to the stand your scenery calls "A 24A";
- airport buildings such as terminals, concourses, hangars, the tower and fuel.

Online data only ever fills gaps. A name your navigation database already has is never replaced, and online data never moves a taxiway, a stand or a hold line. The steering always follows your simulator's own scenery.

BA fetches this data in the background, before you need it: for the airport you are at when you connect or load a flight, for your destination when you choose it, and for both ends of a SimBrief plan when you load one. It is kept for the whole session, even if you switch navigation databases.

If you switch the setting off, BA uses only the navigation database and your installed scenery, and makes no online requests at all. The change applies immediately.

**Refreshing the names.** If an airport's names seem incomplete, open the Settings window, go to the **Taxi Guidance** tab and press **Refresh Taxiway Names**. BA downloads fresh data for the airport you are at (within about 5 nautical miles) and tells you the result:

- *"Taxiway names refreshed for KJFK: 12 added."*
- *"Taxiway names refreshed for KJFK. No new names found."*
- *"Online taxi data is switched off. Nothing refreshed."*
- *"No airport nearby."*
- *"Aircraft position unavailable."*

The tab also shows the attribution for this data: *"Online names: © OpenStreetMap contributors (ODbL) + X-Plane Scenery Gateway."*

### Your installed scenery (offline) {#where-names-scenery}

At add-on airports, BA can also read the scenery package installed on your computer. Named hangars, concourses, cargo buildings and the tower are read straight from the package, so a hangar that OpenStreetMap leaves unnamed can still be called by name. This uses no internet at all.

It is controlled by **Read installed scenery for airport buildings (offline)**, which is on by default. On MSFS 2024 BA looks in both the Community and Community2024 folders. The first time, it does a one-time scan to find which package belongs to which airport.

Under the checkbox there is a read-only **Scenery index status** box you can Tab to. It says *"Scenery index is off."*, *"No airport scanned yet this session."*, or a short report on the last airport read, starting with its code and the number of features found in which package.

## Settings for This Chapter {#where-settings}

Every setting named in this chapter is on the **Taxi Guidance** tab of the Settings window and applies immediately. Ground traffic distances have their own unit setting, described in [Ground Traffic](ground-traffic.md).

## If Something Goes Wrong {#where-troubleshooting}

- **Where Am I says "Not on a known taxiway or ramp".** You may be on an apron or a piece of pavement the airport data does not map. Try Look Around (Output Alt+L), which can still tell you which apron you are on and what is around you.
- **A taxiway is named in the clearance but BA never says it.** The navigation database may not name it. Make sure online names are switched on, then press Refresh Taxiway Names on the Taxi Guidance tab.
- **Look Around says "No surroundings data".** The airport has no buildings in the navigation database, OpenStreetMap or your scenery. At add-on airports, check that **Read installed scenery for airport buildings (offline)** is on and look at the Scenery index status box.
- **No passing callouts.** They are off by default, and have the limits listed in [Buildings you pass](#where-passing-callouts).
- **Tuning from the Surroundings window says "Could not tune".** The frequency in the list does not match a channel your radio can select. The message tells you what the radio reads now, so you can adjust it with the aircraft's own radio controls.
- **"In flight."** All of these features work only on the ground.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Weather](weather.md)
- [Next chapter: Taxi Guidance](taxi-guidance.md)
- [Guide contents](index.md)

</nav>
