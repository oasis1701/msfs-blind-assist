---
title: "Taxi Guidance"
---

<div class="guide-part" markdown="1">

Chapter 2 of 4 in [Part 5: On the Ground](index.md#part-4-on-the-ground) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [How Taxi Guidance Works](#taxi-overview)
- [Before You Start](#taxi-before-you-start)
- [The Taxi Planner](#taxi-planner)
- [Choosing Your Destination](#taxi-destination-types)
- [Entering an ATC Clearance](#taxi-clearances)
- [Worked Examples](#taxi-examples)
- [Changing a Clearance on the Move](#taxi-amend)
- [SayIntentions Taxi Import](#taxi-sayintentions)
- [Progressive Taxi](#taxi-progressive)
- [Calculating the Route](#taxi-calculating)
- [Starting Guidance](#taxi-starting)
- [The Steering Tone](#taxi-tone)
- [Following the Guidance](#taxi-following)
- [Hold Shorts and Continue](#taxi-holds)
- [Runway Crossings](#taxi-crossings)
- [Lining Up on the Runway](#taxi-lineup)
- [Going Off Route and Recalculation](#taxi-off-route)
- [Arriving at a Stand](#taxi-gate-arrival)
- [Taxi Routes in the Route Briefing](#taxi-briefing)
- [Hotkeys During Taxi](#taxi-hotkeys)
- [Taxi Guidance Settings](#taxi-settings)
- [Everything Taxi Guidance Says](#taxi-callouts)
- [If Something Goes Wrong](#taxi-troubleshooting)

</nav>

Taxi guidance is how MSFS Blind Assist gets you from a stand to a runway, from a runway exit to a stand, or anywhere else on an airport, following the route ATC actually gave you. It is the feature most pilots use on every flight.

## How Taxi Guidance Works {#taxi-overview}

Taxi guidance gives you two things at once.

- **A steering tone** for the fine steering, second by second: tone in your left ear, steer left; in your right ear, steer right; quiet, you are pointing the right way.
- **Spoken instructions** for the planning: turns coming up, the taxiway you have joined, runways you must stop before, the distance to your stand, and when you are lined up.

Follow the tone, and listen for the words.

Here is how the pieces fit together on a typical departure:

1. You open the **taxi planner** (Input Shift+Y). Its window is titled "Taxi Guidance". You choose where you are going, and enter the taxiways ATC gave you, in order.
2. You press **Calculate Route**. BA works out the route over your scenery's real taxiways, adds a stop before every runway the route crosses, and tells you what it has built.
3. Guidance starts straight away. You release the parking brake, taxi, and follow the tone and the callouts.
4. At each runway hold line BA tells you to stop. When ATC clears you across or onto the runway, you press **Continue** (Input Y).
5. At the departure runway, BA lines you up on the centreline, tells you when you are lined up, and can hand you over to Takeoff Assist automatically.

After landing it works the same way in reverse: you plan a route from the runway exit to your stand, and BA takes you there and hands you to docking guidance at the gate.

Taxi guidance works at any airport your navigation database covers, and every name in its lists is what that airport's scenery calls things.

## Before You Start {#taxi-before-you-start}

Taxi guidance needs:

- **A [navigation database](getting-started.md#nav-database).** Without one, the planner says *"Airport database not available. Configure database in settings."*
- **A connection to the simulator,** with your aircraft on the ground at the airport. BA starts the route from wherever you are standing.

A few things make the experience better.

- **Online taxiway names** (on by default) fill in names your scenery leaves blank and add the names of painted holding points such as "A2", which the named holding point features need. See [Online names](knowing-where-you-are.md#where-names-online).
- **Rebuilding your database after a scenery change**, or BA will still be using the old airport's taxiways.
- **Knowing where you are.** Output Alt+Y ([Where Am I](knowing-where-you-are.md#where-am-i)) is a good first check before you plan a route.

## The Taxi Planner {#taxi-planner}

Open the taxi planner with Input Shift+Y. The window is titled **Taxi Guidance**, and its controls run in the order a controller reads a clearance: where you are, where you are going, and the taxiways in between. Some controls appear only when they apply (stand filters for a stand, runway options for a runway), so the Tab order changes with the destination type.

Pressing Escape closes the window. **Closing the window does not stop guidance**; use the **Stop Guidance** button or Input Ctrl+Y for that. When you open the planner again it still holds what you entered, so amending a clearance mid-taxi is quick.

Here is every control, in Tab order.

### Airport (ICAO) {#airport-icao}

The airport's identifier, filled in for you with the airport you are standing at. Type a different one and Tab away to load another airport. The status line under the Calculate button shows progress while it loads, for example *"EGLL: fetching taxiway names…"*, then *"EGLL: building taxi graph…"*, then a line such as *"EGLL: 6085 nodes, 9120 paths."* when the airport is ready. If the airport is missing you will see *"Airport XXXX not found in database."*, and if the scenery has no taxiway data at all, *"No taxi data for XXXX."*

### Destination type {#destination-type}

A list with five choices:

- **Runway** — taxi to a runway for departure.
- **Gate / Parking** — taxi to a stand, gate or parking spot.
- **Progressive Taxi** — taxi one leg at a time, ending at a hold short or a point ATC named, with no final destination yet.
- **Deice Area** — taxi to a remote de-icing pad (these come from GSX airport profiles).
- **Place** — taxi to a named place such as an FBO, hangar, fuel island, terminal or cargo area.

Each one is explained in [Choosing Your Destination](#taxi-destination-types).

### Gate search {#gate-search}

Only shown for Gate / Parking. Type a gate letter or number, such as "B12" or just "12", and the destination list shrinks to the stands that match. It also matches the names stands are known by online, so if ATC calls a stand by its real-world name and your scenery spells it differently, typing the real-world name will usually still find it.

### Destination {#destination}

The list of places you can go, which depends on the destination type.

- For runways, each item reads like *"Runway 27L"*. Closed runways are left out. A runway whose full length can only be reached by taxiing along it first reads *"Runway 28 (backtrack required)"*; see [Full-length departure (requires backtrack)](#full-length-departure-requires-backtrack).
- For stands, each item names the stand, its type and its equipment, for example *"Gate A 25 - Gate Medium (Jetway)"*. The stands nearest to you by taxi distance come first, so a stand just across a runway, close as it looks, is listed behind the ones you can reach without going all the way round. If the stand is also known by another name online, the item ends with something like *", also 25 (online)"*. A stand the taxiway network does not reach is still listed, with *"(no taxi route)"* on the end, so you know it exists; BA will tell you it cannot route there if you choose it.
- For places, each item names the place, its kind and the stand it ends at.

When you pick a runway and BA has online data for that airport's painted holding points, it may also tell you which painted line the normal full-length departure uses, for example *"Full length holding point A1."* Without that data it names the taxiway the full-length departure enters by, for example *"Full length entry via N9."* Either helps you check the default matches your clearance before you calculate.

### Intersection departure {#intersection-departure}

Runway only. A checkbox. Tick it when ATC clears you to depart from an intersection partway down the runway rather than from the full length. A list appears beneath it, with one entry for each taxiway that meets the runway, such as *"B3, 2400 metres remaining, 650 metres from threshold"*. The first number is how much runway you will have ahead of you; the second is how far that point is from the start of the runway. Choose the one ATC gave you.

If the runway has no usable intersections, you hear *"No runway intersections available. Full length departure."* and the box unticks itself.

### Depart from named holding point {#depart-from-named-holding-point}

Runway only. A checkbox. Controllers often name the painted holding point you should use, such as "hold short A2", even when the taxiway in your scenery is not called A2. Tick this box to choose the painted line by name. A list appears, with entries such as *"A2 (runway hold), 3600 metres remaining, 0 metres from threshold"*. The word in brackets says what kind of line it is: *"(runway hold)"* for the normal line, *"(ILS hold)"* for a line set further back to protect the ILS, and *"(intermediate hold)"* for a line out on the taxiway system.

BA then routes you through that exact painted line, not just to the runway entrance it shares with its neighbours. These names come from online data, so this needs the online names setting switched on and the airport's data loaded. In Progressive Taxi, where the online data has no name for a line ATC uses, BA can also offer one from the scenery's own signs, marked *"(from sign, approximate)"*: it stops you at the sign's position, which may not be exactly on a painted line. When there is nothing to offer, you hear *"No named holding points available for this runway. Full length departure."* and the box unticks itself.

### CAT III / low-visibility hold (LVP) {#cat-iii-low-visibility-hold-lvp}

Runway only. A checkbox. By default BA holds you at the line closest to the runway, which is what a normal clearance means. When low-visibility procedures are in force, ATC holds you further back at the CAT II/III line, to keep you clear of the ILS critical area. Tick this box when ATC says so, or when you hear "low visibility procedures in force" on the ATIS.

If BA knows the name of that line from online data, choosing a runway with this box ticked tells you, for example *"CAT three hold A3."*, or *"CAT three hold not named in online data. Full length holding point A1."* After you calculate, the route summary tells you what it actually found: *"Holding at the CAT three ILS hold."*, *"No CAT three ILS hold found at this holding point. Holding at the full-length line."*, or *"No CAT three ILS hold found before the runway."*

### Full-length departure (requires backtrack) {#full-length-departure-requires-backtrack}

Runway only. A checkbox. Some runways have no taxiway reaching their very end. To use the full length you enter partway down, taxi along the runway to the end (a backtrack, or "backtaxi"), turn around, and line up. Tick this box when you need that. It is off by default, and nothing about backtracking happens unless you tick it. See [Backtracking for a full-length departure](#taxi-backtrack-departure).

### Show fitting only and Hide occupied stands {#show-fitting-only-and-hide-occupied-stands}

Gate / Parking and Place only. Two checkboxes that hide stands too small for your aircraft and stands already occupied. Untick them if a stand you need is missing. See [Choosing a Stand](gates-gsx-and-docking.md#gsx-choosing-a-stand).

### First taxiway {#first-taxiway}

The first taxiway of your clearance. The list starts with **(None - calculate shortest path)**, then every taxiway at the airport, nearest first.

<div class="callout warning" role="note" markdown="1">

**Warning:** When the planner loads, BA preselects the nearest taxiway in the direction you are facing, as a guess. Always check this box and set it to the first taxiway ATC actually said. If ATC gave you no taxiways, or you just want BA to find the way, choose (None - calculate shortest path).

</div>

### Hold short (after the first taxiway) {#hold-short-after-the-first-taxiway}

A checkbox beside the first taxiway. Tick it to make guidance stop at the end of this taxiway, where it meets the next one, and wait for you to press Continue. Use it for an instruction such as "hold short of taxiway C". The stop is announced as *"Stop. Hold short of end of taxiway A. Press continue when cleared."*

### Hold short of runway (after the first taxiway) {#hold-short-of-runway-after-the-first-taxiway}

A list beside the first taxiway, starting with **(none)**, then every runway at the airport. Use it when ATC explicitly tells you to hold short of a runway after this taxiway: "via A, hold short of runway 22R". BA already stops you before every runway your route crosses, so this is optional, but it makes sure the stop is exactly the one ATC named, labelled the way you selected it. Leave it at (none) to rely on BA's automatic runway holds.

### Add Taxiway {#add-taxiway}

A button that adds another taxiway row below the first. It only works once the first taxiway is set to a real taxiway. You can add up to 20 more rows. Each new row is labelled "Taxiway 2", "Taxiway 3" and so on, and has its own taxiway list, **Hold short** checkbox, **Remove** button and **Hold short of runway** list, exactly like the first row.

In each new row's taxiway list, the taxiways that connect to the previous one come first, nearest first, followed by every other taxiway at the airport in alphabetical order. The list also starts with **(None - end here)**. The taxiway in the row just above is left out, because choosing the same taxiway twice in a row usually means a slip of the finger. The exception is when the row above holds short, which is how clearances such as "N, hold short of runway 15R, N" are entered (see [Worked examples](#taxi-examples)).

The **Remove** button deletes that row and every row after it.

### Progressive Taxi controls {#progressive-taxi-controls}

Only shown for Progressive Taxi. Below your last taxiway row you get a **Terminator** list, saying how this leg ends, and one or two more lists for the target. They are explained in [Progressive Taxi](#taxi-progressive).

### Fill from SayIntentions {#fill-from-sayintentions}

A button that fills the whole planner from your latest SayIntentions taxi clearance. It does the same as Input Ctrl+Shift+Y. See [SayIntentions taxi import](#taxi-sayintentions).

### Calculate Route {#calculate-route}

Builds the route and starts guidance immediately. See [Calculating the Route](#taxi-calculating).

### Stop Guidance {#stop-guidance}

Stops taxi guidance. The status line says *"Guidance stopped."*

### Last route summary {#last-route-summary}

A read-only text box at the end of the window holding the summary of your last route, or the reason the last calculation failed. Arrow through it with your screen reader whenever you want to hear the route again. It is the most reliable way to read the full summary, because the first guidance instruction often begins before BA has finished speaking it.

### Keyboard shortcuts in the planner {#keyboard-shortcuts-in-the-planner}

The planner has Alt shortcuts that jump straight to a control.

<div class="table-wrap" markdown="1">

| Shortcut | Goes to |
|---|---|
| Alt+A | Airport (ICAO) |
| Alt+T | Destination type |
| Alt+G | Gate search |
| Alt+E | Destination |
| Alt+I | Intersection departure |
| Alt+P | Depart from named holding point (and the Progressive named holding point list) |
| Alt+L | CAT III / low-visibility hold |
| Alt+B | Full-length departure (requires backtrack) |
| Alt+F | First taxiway (also Show fitting only) |
| Alt+H | Hold short checkboxes |
| Alt+O | Hold short of runway lists (also Hide occupied stands) |
| Alt+D | Add Taxiway |
| Alt+2 to Alt+9 | Taxiway 2 to Taxiway 9 |
| Alt+R | Remove buttons |
| Alt+N | Progressive Taxi terminator |
| Alt+U | Progressive Taxi runway target |
| Alt+W | Progressive Taxi taxiway target |
| Alt+Y | Fill from SayIntentions |
| Alt+C | Calculate Route |
| Alt+S | Stop Guidance (also Last route summary) |

</div>

Where a letter is shared, as with Alt+F, Alt+O, Alt+H, Alt+R and Alt+S, pressing it again moves on to the next control with that letter, and Windows may move to a button rather than press it. When in doubt, Tab to the button and press Space or Enter.

## Choosing Your Destination {#taxi-destination-types}

### Gate or stand {#gate-or-stand}

Choose **Gate / Parking**, then the stand. Use the Gate search box to narrow the list. When you calculate, BA routes you to the stand, lines you up with it, and, if docking guidance is switched on, hands you to docking for the final metres. See [Arriving at a Stand](#taxi-gate-arrival) and [Docking Guidance](gates-gsx-and-docking.md#docking).

If another aircraft is parked on the stand when you calculate, BA warns you, for example *"Warning: BAW123 is at the destination gate."*, or *"Warning: an aircraft is at the destination gate."* if it has no callsign.

With GSX, calculating the route can also prepare the stand in GSX; see [Letting BA Prepare Your Stand in GSX](gates-gsx-and-docking.md#gsx-auto-select).

### Runway, full length {#runway-full-length}

Choose **Runway**, then the runway, leave the runway checkboxes unticked, and enter your taxiways. BA takes you to the hold line before the full-length entrance, stops you there, and after you press Continue lines you up at the start of the runway. This is the normal departure.

BA works out the full-length start point from the airport's data, and makes sure the route ends at a real runway entrance. It never silently swaps in an entrance further down the runway, so you can never be handed less runway than you asked for. If no taxiway reaches that point, BA warns you instead of guessing.

### Runway, intersection or named holding point {#runway-intersection-or-named-holding-point}

Tick **Intersection departure** or **Depart from named holding point** and choose the entry (see the planner controls above). BA routes you there, holds you short, and confirms after you calculate, for example *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."* or *"Holding point A2, runway 28. About 3600 metres of runway ahead."* A holding point partway down the runway is treated like an intersection departure; at a full-length holding point the lineup is still at the full length, and the choice just decides which entrance you use.

Any runway destination can also have the **CAT III / low-visibility hold (LVP)** box ticked, which only changes where you stop before the runway.

### Runway, full length with a backtrack {#taxi-backtrack-departure}

Choose the runway and tick **Full-length departure (requires backtrack)**. BA finds the entrance nearest the departure end, routes you there and holds you short with *"Stop. Hold short of Runway 32. Press continue when cleared to enter and backtrack."* When ATC clears you onto the runway, press Continue. You hear *"Entering Runway 32. Backtrack to full length, heading 140, then turn around to heading 320."*

The tone now steers you down the runway centreline towards the far end. As you approach it you hear *"Approaching runway end. Slow down, prepare to turn around to heading 320."*, then *"Runway end. Turn around to line up, heading 320."* The tone stays on through the whole turnaround. It pans hard to whichever side you are turning and settles down as you straighten, so silence always means you are lined up, never that BA has given up. You then hear the normal *"Lined up, Runway 32. Hold position."*

If BA cannot find a suitable entrance, it says *"No backtrack entrance found for this runway. Full length departure."* and plans a normal full-length departure.

### Progressive Taxi {#progressive-taxi}

Choose **Progressive Taxi** when ATC gives you part of a route at a time. See [Progressive Taxi](#taxi-progressive).

### Deice Area {#deice-area}

Choose **Deice Area** to taxi to a remote de-icing pad. The pads come from GSX airport profiles, so this list is empty at airports with no GSX de-icing data, and you hear *"No deicing areas at this airport."*

### Place {#place}

Choose **Place** to taxi to a building or facility by name: an FBO after landing, a hangar, a fuel island, a terminal or a cargo area. BA takes you to the stand in front of it, with docking included where the scenery gives a stop. The list depends on what BA knows about the airport, from its database, from online map data and from your installed scenery. While it is being built you may hear *"Loading places for KXYZ."* If nothing can be routed to, you hear *"No places to route to at KXYZ."*, and if the data could not be read, *"Places could not be loaded for KXYZ."* The places themselves are described more in [The Surroundings Window](knowing-where-you-are.md#surroundings).

## Entering an ATC Clearance {#taxi-clearances}

This is the heart of taxi guidance. The rule is simple: **enter the taxiways in the order ATC said them, and put each hold short on the row of the taxiway it comes after.**

BA follows your clearance in order. It does not take a shortcut down some other taxiway just because it is shorter. If you enter A, B, K, the route really does go along A, then B, then K, and then on to your destination.

### The basic method {#the-basic-method}

1. Open the planner with Input Shift+Y.
2. Check the airport.
3. Choose the destination type and the destination: the runway or stand ATC gave you.
4. Tick any runway option ATC gave you: intersection, named holding point, low visibility, or backtrack.
5. Set **First taxiway** to the first taxiway ATC said.
6. If ATC said to hold short of something after that taxiway, tick **Hold short** (for a taxiway) or choose the runway in **Hold short of runway**.
7. Press **Add Taxiway** and choose the next taxiway. Repeat until you have entered them all.
8. Press **Calculate Route**, then listen to the summary, or read it in the **Last route summary** box, and check it matches what ATC said.

You do not need to enter runway crossings as taxiways, and you do not need to tell BA to stop before them. BA stops you before every runway your route crosses or enters, automatically, whether or not ATC mentioned it.

<div class="callout tip" role="note" markdown="1">

**Tip:** You do not have to enter the little unnamed links between stands and taxiways. Leave them out, just as a controller does. BA finds its own way from your stand onto the first taxiway you entered. When that first taxiway is some distance away, the summary tells you how you will reach it, for example *"First taxi onto A."* or *"First taxi via L1 to reach A."*

</div>

### Taxiway names you can't find {#taxiway-names-you-can-t-find}

The lists only contain taxiways that exist in your scenery's data. If ATC names a taxiway you cannot find:

- Try the online names setting, if it is off. It fills in names your scenery leaves blank.
- Check you have the right airport, and that your database was built after you installed this scenery.
- Some default sceneries have fewer taxiways than the real airport. You may need to pick the nearest equivalent, or use (None - calculate shortest path) and let BA find the way.

## Worked Examples {#taxi-examples}

The airports in these examples are made up, but the phraseology is real.

### Example 1: a standard departure with a runway hold short {#example-1-a-standard-departure-with-a-runway-hold-short}

ATC: *"Speedbird 123, taxi to holding point runway 27L via A, B, hold short of runway 22R."*

1. Destination type: **Runway**. Destination: **Runway 27L**.
2. First taxiway: **A**.
3. Add Taxiway, then Taxiway 2: **B**. On the Taxiway 2 row, set **Hold short of runway** to **22R**, because the hold short comes after B.
4. Calculate Route.

You will hear a summary along the lines of *"Route to Runway 27L via A, B. 1450 metres, crossing runway 22R."* As you near the 22R hold line on B, BA counts you down (see [Hold Shorts and Continue](#taxi-holds)) and stops you with *"Stop. Hold short of runway 22R. Press continue when cleared."* When ATC says "cross runway 22R", press Input Y. You hear *"Continuing. Taxiway B."* and carry on to the 27L hold.

### Example 2: a clearance that includes a crossing {#example-2-a-clearance-that-includes-a-crossing}

ATC: *"Taxi to holding point runway 27L via A, cross runway 22R, B."*

Enter it exactly as in Example 1, but with no hold short on the B row: First taxiway **A**, Taxiway 2 **B**. Do not try to enter "cross runway 22R" anywhere.

You will still be stopped at the 22R hold line, because BA cannot hear ATC and rolling across an active runway unannounced is the most dangerous mistake on an airport. Check the clearance (and the [Ground Traffic](ground-traffic.md) callouts or Output Alt+G if you are unsure about traffic), then press Input Y to cross.

### Example 3: a taxiway used twice, "A, C, A" {#example-3-a-taxiway-used-twice-a-c-a}

ATC: *"Taxi to runway 09 via A, C, A."* This happens where the controller wants you off A for a short stretch, round a parked aircraft or a closed section, and then back onto it.

1. Destination: **Runway 09**.
2. First taxiway: **A**.
3. Add Taxiway, Taxiway 2: **C**.
4. Add Taxiway, Taxiway 3: **A**. It is in the list, because only the taxiway immediately above, C, is left out.
5. Calculate Route.

Check the summary: it should say *"via A, C, A"*. If the second A turns out to be very short, that is just the geometry of the airport.

### Example 4: the same taxiway on both sides of a runway {#example-4-the-same-taxiway-on-both-sides-of-a-runway}

ATC: *"Taxi to runway 22R via N, hold short of runway 15R."* Then later, *"Cross runway 15R, continue on N, hold short of runway 22L."*

When you know the whole clearance, enter it in one go:

1. First taxiway **N**, and on that row set **Hold short of runway** to **15R**.
2. Add Taxiway. Because the row above holds short, **N** is offered again. Choose **N**, and set its **Hold short of runway** to **22L**.
3. Add Taxiway, choose **N** again.

Each runway gets its own stop, and Continue at each one clears just that runway.

### Example 5: an intersection departure {#example-5-an-intersection-departure}

ATC: *"Taxi via A, B3, line up runway 27L at B3."*

1. Destination: **Runway 27L**. Tick **Intersection departure** and choose **B3** in the list that appears.
2. First taxiway **A**, Taxiway 2 **B3**.
3. Calculate Route. You hear *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."*

If you only need a different painted line at the full-length end ("hold short A2"), use **Depart from named holding point** instead.

### Example 6: a low-visibility departure {#example-6-a-low-visibility-departure}

ATIS: *"Low visibility procedures in force."* ATC: *"Taxi to CAT 3 holding point runway 27L via A, B."*

Enter the route as in Example 1, and also tick **CAT III / low-visibility hold (LVP)**. The summary tells you whether a CAT III line was found.

### Example 7: a gate after landing {#example-7-a-gate-after-landing}

ATC, after you vacate: *"Speedbird 123, taxi to stand B12 via K, L."*

1. Stop clear of the runway. If you were using the Landing Exit Planner, it has already told you where you are; see [The Landing Exit Planner](approach-and-landing.md#landing-exit).
2. Open the planner with Input Shift+Y. The airport is filled in.
3. Destination type: **Gate / Parking**. In **Gate search**, type **B12**, then choose the stand in **Destination**.
4. First taxiway **K**, Taxiway 2 **L**.
5. Calculate Route. BA routes you from where you are actually stopped, and at the stand hands you to docking guidance.

If your route crosses a runway on the way in, you will be stopped before it, exactly as on departure.

### Example 8: no taxiways given {#example-8-no-taxiways-given}

ATC: *"Taxi to stand 40."* or you are flying offline and just want to get to the runway.

Set First taxiway to **(None - calculate shortest path)** and calculate. BA finds the shortest route over the taxiways, still stopping you before every runway.

## Changing a Clearance on the Move {#taxi-amend}

ATC often amends a clearance while you are taxiing. Open the planner again with Input Shift+Y, change the taxiways (Remove deletes a row and everything after it), and press Calculate Route. The new route starts from where you are right now, not from where you were when you first opened the window.

## SayIntentions Taxi Import {#taxi-sayintentions}

If you fly with SayIntentions, BA can fill in the planner from the taxi clearance SayIntentions gave you. Press **Input Ctrl+Shift+Y**, or the **Fill from SayIntentions** button in the planner. BA fills in the destination, the taxiways and any runway hold shorts, and names anything it could not apply, so you can check it against what you heard.

By default the import only fills the form, and you press **Calculate Route** yourself after checking it. If you would rather it started guidance straight away, tick **SayIntentions import starts taxi guidance immediately** in the SayIntentions section of the Taxi Guidance settings tab. After landing, when SayIntentions has assigned you an arrival gate, the import always routes you to a gate, never back to a runway.

The details, including how BA reads the clearance and SayIntentions' own ground track, are in [SayIntentions](networks-and-integrations.md#sayintentions).

## Progressive Taxi {#taxi-progressive}

Some controllers give a clearance one piece at a time: "taxi via A, B, hold short of runway 09", then later "cross runway 09, taxi via C, hold short of taxiway D". Progressive Taxi lets you follow each piece without pretending to know where you will end up.

1. Choose **Progressive Taxi** as the destination type. The Destination list disappears, and the **Hold short of runway** lists on each row are hidden, because the end of the leg is set separately.
2. Enter the taxiways for this leg as usual. You need at least one; otherwise you hear *"Select at least one taxiway for progressive taxi."*
3. Below your last taxiway row, choose how the leg ends in the **Terminator** list:
   - **Hold short of runway**: then choose the runway in **Runway to hold short of**. Guidance ends at that runway's hold line on your last taxiway.
   - **Hold short of taxiway**: then choose the taxiway in **Hold short of taxiway**. Guidance ends where your last taxiway meets it.
   - **After crossing runway**: then choose the runway in **Runway to cross**, and optionally the taxiway to cross at in **Cross at taxiway (optional)**. Leave that at (none) to cross at the nearest point. Use this when ATC has already cleared you across: BA does not stop you before that runway, and the leg ends just past it.
   - **End of last taxiway**: guidance ends at the end of your last taxiway.
   - **Hold at named holding point**: then choose the holding point in **Named holding point**, such as "VIKAS". The list comes from online data; it shows *"(none available at this airport)"* when there are none.
4. Press **Calculate Route**.

When you reach the end of the leg, the tone stops and you hear one of:

- *"Hold short of runway 09. Set a new route when cleared."*
- *"Hold short of taxiway D. Set a new route when cleared."*
- *"Across runway 09. Hold position. Set a new route when cleared."*
- *"Hold at VIKAS. Set a new route when cleared."*
- *"Route ended. Hold position. Set a new route when cleared."*

When ATC gives you the next piece, open the planner, enter the next leg, and calculate again. Pressing Continue does nothing at the end of a Progressive leg, because there is no further route to continue along.

Other runways on a Progressive leg are still protected. If the leg crosses any runway other than one you chose under **After crossing runway**, BA still stops you before it.

If BA cannot find the end point you chose, you hear something like *"Could not find taxiway D from C. Check your entry."*, and for a named holding point you cannot reach, *"Cannot taxi to VIKAS from your position. Check your entry."*

## Calculating the Route {#taxi-calculating}

When you press **Calculate Route**, BA:

1. Takes your aircraft's current position.
2. Builds a route that follows your taxiways in order.
3. Adds a stop before every runway the route crosses or enters, placed at the scenery's painted hold line where it has one, and never on a runway.
4. Speaks a summary and writes it into the **Last route summary** box.
5. Starts guidance.

### Reading the route summary {#reading-the-route-summary}

A summary has the form *"Route to (destination) via (taxiways). (distance), (runways and holds)."* For example:

- *"Route to Runway 27L via A, B. 1450 metres, crossing runway 22R."*
- *"Route to Gate B 12 - Gate Heavy (Jetway) via K, L. 2.3 kilometres, crossing runways 09L and 09R."*
- *"Route to Runway 04R via C. First taxi onto C. 800 metres."*

What each part means:

- **via ...** lists the taxiways in the order you will use them.
- **First taxi onto C** or **First taxi via ... to reach C** means your first cleared taxiway is some way from you, and BA has added the bit of apron or link taxiway that gets you onto it.
- **The distance** is the whole route. Short distances are in metres or feet, depending on your setting; long ones switch to kilometres, or nautical miles if you use feet.
- **crossing runway X** and **entering runway X** name every runway the route crosses or goes onto, other than your own departure runway at the end. A runway crossed twice says *"twice"*. Both ends of one runway are named together, such as *"10L/28R"*.
- **N hold short points** counts the other stops you asked for, such as a taxiway hold short.

### Warnings in the summary, and what to do {#warnings-in-the-summary-and-what-to-do}

Most routes produce a clean summary. When something is not right, a warning is added. These are the ones you may meet.

**"... with no hold short point for runway 06L"** (added to the runway list). The route crosses or enters that runway, but there is nowhere safe to stop before it. BA will not stop you there. You must judge that runway yourself: slow down, check with ATC, and listen for traffic. Every other runway named without this warning will get a proper stop. Because guidance starts talking straight away and can cut the summary short, BA also says it again once you are stopped and guidance has started, *"Warning: no hold short point for runway 06L."*, so you hear it whatever happens to the summary. A stop you have already rolled past is never reported missing.

**"Warning: this route does not reach Runway 27L. Check your taxiway entry and reprogram."** You hear this short form; the box holds a longer explanation, such as *"Warning: this route ends about 450 metres to the side of Runway 27L and does not reach the runway. You may be missing the taxiway that connects to the runway."* or *"Warning: this route stops short of Runway 27L ... The last taxiway you entered does not connect to it."* Usually the clearance's last taxiway runs beside the runway, and you have left out the short link taxiway onto it. Add it and calculate again. If you carry on anyway and BA is still far from the centreline during lineup, it repeats: *"This route does not reach Runway 27L. Reprogram the taxi route, including the taxiway that connects to the runway."*

**"Warning: could not follow specified taxiways. Using shortest path. Reason: ..."** BA could not make a route out of the taxiways you entered, for example because two of them never meet, and has used the shortest route instead. The reason is technical, such as *"Step 2: No intersection between 'B' and 'Z'"*, but it tells you which pair failed. Check you picked the right taxiways, in the right order.

**"Warning: route via A, B is 3.2 kilometres; direct route is 900 metres. Check taxiway selection."** The route follows your clearance but is far longer than the direct route, which often means a taxiway was picked by mistake, or was in the wrong order. Sometimes it adds *"Taxiway A is 600 metres from your position."*, meaning your first taxiway is a long way from where you are. If the clearance really is that long, ignore the warning.

**"Note: requested hold-short(s) could not be set — runway 22R (route does not cross it after taxiway B)."** You asked for a runway hold short that the route never reaches after that taxiway. Check you put the hold short on the right row. If it says *"(no safe place to hold short after taxiway B)"*, the route does meet that runway, but there is nowhere safe to stop before it.

**"Could not compute a path onto taxiway A along the apron; route starts on A."** BA could not find its own way from your stand to the first taxiway, so the route begins on that taxiway. Taxi onto it carefully; the tone will guide you to it.

**"Your position isn't connected to the taxiway network. The first 40 metres to taxiway A aren't mapped."** Your stand, or the spot you are on, is not joined to the taxiways in the airport's data. BA will steer you straight across that unmapped stretch towards the start of the route, so check it is clear. A similar warning, *"Gate X isn't connected to the taxiway network you're on. The first N of the route aren't mapped."*, means the destination is the part that is cut off.

### When a route cannot be built at all {#when-a-route-cannot-be-built-at-all}

If Calculate fails, BA tells you why and puts the same reason in the summary box. Any route you were already following carries on unchanged. Common reasons:

- *"No airport loaded. Enter an ICAO code first."*
- *"Please select a destination."*
- *"No taxi route to Gate C 9. This stand can't be reached by the taxi network."*
- *"No taxi route to Gate C 9. It isn't connected to the taxiway network you're on."*
- *"No taxi route from here. You aren't on the connected taxiway network, and the way onto it crosses runway 13."* BA refuses to steer you across a runway on ground its data does not map.
- *"Gate list updated from GSX. Please choose the destination again."* GSX finished loading and the stand list changed. Choose your stand again and recalculate.
- *"No taxi path data available for this airport."*, *"Could not find a nearby taxiway node."* or *"Could not calculate a route to the destination."* Usually missing or broken taxiway data in the scenery.

## Starting Guidance {#taxi-starting}

Guidance starts as soon as the route is calculated. You do not need to press anything else. The status line says *"Route loaded. Guidance active."*

The first thing you hear is one of:

- *"Taxiway A. Steering guidance active."* You are already on your first taxiway.
- *"Steering guidance active. Join taxiway A in 60 metres."* You are on a stand or apron, and the first named taxiway is that far ahead.
- *"Steering guidance active."* The route has no named taxiways.
- *"Stop. Hold short of runway 22R. Press continue when cleared."* You are already standing at a runway hold line that is the route's first stop. The tone stays silent until you press Continue.

Straight after that, BA may add:

- Your intersection or holding point, for example *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."*
- Any warning about unmapped ground at the start, or a route that does not reach the runway.
- **A turn cue, when the route starts behind you or at a sharp angle.** This is common after a pushback, when you are facing the wrong way. You may hear *"Taxiway A is behind you. Turn left to come around."*, *"Make a U-turn to the right."*, *"Sharp turn left onto taxiway K."* or *"Sharp turn right."*

When a warning is spoken at the start, BA holds back its other early callouts for about twelve seconds so they do not talk over it. The tone works throughout.

Now release the parking brake and start rolling. If you are already moving onto your first taxiway, you will hear *"Taxiway A."* when you join it.

## The Steering Tone {#taxi-tone}

The steering tone follows the rules in [The Audio Language: What the Tones Mean](core-concepts.md#audio-language): a fixed pitch, panned to the side you should steer toward, further to the side for a bigger correction (fully to one side at about 30 degrees off course), and silent once you are pointing within a few degrees of where you should be. Steer until it goes quiet; you do not need to centre it exactly. For steering away from the tone, or a hard-panned tone on speakers, see [Taxi Guidance Settings](#taxi-settings).

### Turns {#turns}

The tone looks ahead along the route, so as you approach a turn it starts drifting towards the new direction before you reach the junction, just as a pilot looking out of the window would start the turn a little early. Follow it into the turn.

During a turn, the tone allows for the way the aircraft keeps swinging after you ease off the tiller. It goes quiet slightly *before* your nose reaches the new heading. That is deliberate: when it goes quiet mid-turn, hold that rate of turn and start unwinding, and you will roll out on the line. If it swings to the other side, you are turning too far; unwind.

How early it goes quiet depends on how fast you are turning: the faster the turn, the earlier. In the Fenix A320 family it also depends on how far you have already turned. A big turn, such as a 90-degree corner, goes quiet a little earlier than a small one, because the Fenix carries more swing out of a big turn. Unwind when you hear it go quiet.

After a long turn, once the route ahead is straight, you may hear *"Straighten."* That is your cue to roll out now.

### On the runway: lineup precision {#on-the-runway-lineup-precision}

When you are lining up on a runway, or at a stand, the tone becomes much more precise.

- It stays on until you are within half a degree of the runway heading, and comes back if you drift more than one degree.
- It reaches full pan at 15 degrees instead of 30.
- If you are to the side of the centreline, it does not just point you along the runway; it points you back towards the centreline at an angle (up to 30 degrees), easing off as you get closer, so you arrive on the line already pointing the right way.

**Pulsing tone.** If you stop, or nearly stop (3 knots or less), on the runway while still not lined up, either because you are pointing the wrong way or because you are well off the centreline, the tone pulses on and off three times a second instead of playing steadily. It means "you're stopped and not lined up yet". Follow its direction and roll forward gently; the pulse stops as soon as you are moving.

### When the tone stops {#when-the-tone-stops}

The tone pauses at every hold short (silence there means "stay put", not "on track"), once you are lined up, and when you arrive. It resumes when you press Continue.

<div class="callout tip" role="note" markdown="1">

**Tip:** The tone volume is quiet by default (5 percent), so ATC is easy to hear over it. Adjust it on the Taxi Guidance settings tab, using **Test Tone** to try it.

</div>

## Following the Guidance {#taxi-following}

Here is what happens along the way, and what you do.

### Turns {#turns-2}

For each turn, you normally get two callouts:

1. **Advance notice**, typically 10 seconds ahead at your current speed: *"In 100 metres, turn left onto taxiway B."*, *"In 80 metres, slight right onto taxiway C."*, or for a turn of 60 degrees or more, *"In 150 metres, sharp right onto taxiway D, 90 degrees."* Where the taxiway name changes but the route goes straight on, you hear *"In 100 metres, continue onto taxiway E."*
2. **Turn now**, a few seconds before the turn: *"Left now, taxiway B."*, *"Slight right now, taxiway C."* or *"Sharp right now, taxiway D."*

"Slight" means a turn of between about 20 and 60 degrees. The direction word is worked out from the way you are actually pointing at the time, so it always agrees with the tone, even if you have drifted off line.

When you join the new taxiway you hear its name: *"Taxiway B."*

### Curves {#curves}

Some taxiways bend gradually rather than turning at a junction. When the next 100 metres or so curve by 30 degrees or more, you hear *"Curving left."* or *"Curving right."* Follow the tone round the bend.

### Crossing taxiways {#crossing-taxiways}

When you go straight across another taxiway, you hear *"Crossing taxiway K."*, or *"Crossing taxiways K, L."* for more than one. These help you keep track of where you are, and match the taxiway names a controller may use ("give way to traffic on K"). You can switch them off on the Taxi Guidance settings tab.

### Speed reminders {#speed-reminders}

BA reminds you if you are going too fast:

- *"Slow for turn."* above about 10 knots approaching a turn.
- *"Slow for sharp turn."* above about 8 knots approaching a turn of 60 degrees or more.
- *"Taxi speed, 32 knots."* above 30 knots on a straight taxiway.

These repeat at most every eight seconds. If you would like to hear your speed regularly, turn on **Announce ground speed every** on the Taxi Guidance settings tab.

### Checking where you are {#checking-where-you-are}

At any time:

- **Output Y** tells you your taxiway, your next turn and the distance to go, for example *"Taxiway A, right onto taxiway B in 150 metres, 1200 metres to Runway 27L."* At a hold it reminds you what you are holding for.
- **Output Ctrl+Y** repeats the last instruction exactly, if another sound drowned it out. If nothing has been said yet you hear *"No taxi instruction yet."*, and with no guidance running, *"No taxi guidance active."*
- **Output Alt+Y** tells you where you are on the airport. See [Where Am I](knowing-where-you-are.md#where-am-i).

### Other traffic {#other-traffic}

Taxi guidance steers you along your route, but it is not what watches for other aircraft. That is the ground traffic monitor, which calls out traffic ahead, on your route and on the runway as you hold. See [Ground Traffic](ground-traffic.md).

## Hold Shorts and Continue {#taxi-holds}

A hold short is a point where guidance stops you and waits for you to say you have been cleared. BA places one:

- before every runway your route crosses or enters,
- at the departure runway itself,
- after any taxiway row where you ticked **Hold short**, and
- at any runway you chose in a **Hold short of runway** list.

### The countdown {#the-countdown}

As you approach a hold short, BA counts you down in three steps, starting earlier the faster you are going:

1. *"Hold short runway 22R in 120 metres."*
2. *"Hold short runway 22R in 60 metres. Slow down."*
3. *"Hold short runway 22R in 30 metres. Stop."*

If you come to a stop before the last step, you hear *"Hold short runway 22R. Stop."* The runway name may include the taxiway the line is on, such as *"runway 15R at N"*. A taxiway hold reads *"Hold short end of taxiway A in 60 metres."*

### At the line {#at-the-line}

When you reach the line, the tone stops and you hear *"Stop. Hold short of runway 22R. Press continue when cleared."* Bring the aircraft to a stop and set the parking brake if you are waiting. Output Y reminds you: *"Holding short of runway 22R. Press continue when cleared."*

### Continue {#continue}

When ATC clears you across or onto the runway, press **Input Y** (left bracket, then Y). You hear *"Continuing. Taxiway B."*, the tone comes back, and you carry on.

If the route started behind you and you have been holding at its start, the continue callout includes the turn you need, for example *"Continuing. Sharp turn left onto taxiway K."*

<div class="callout warning" role="note" markdown="1">

**Warning:** Only press Continue when you have actually been cleared. BA cannot hear ATC. If you are flying offline, treat every runway hold as a moment to stop, listen for traffic, and check the runway is free before you cross.

</div>

### One stop that guards two runways {#one-stop-that-guards-two-runways}

Sometimes two runways are so close together that there is nowhere to stop between them, so one hold line guards both. BA names both and clears them one at a time, the way a controller would:

1. At the line: *"Stop. Hold short of runway 09 and runway 01. Press continue when cleared for runway 09."*
2. When cleared for 09, press Continue. You hear *"Runway 09 cleared. Still holding. Press continue when cleared for runway 01."* Stay where you are.
3. When cleared for 01, press Continue again, and you move off.

### Two runways close together {#two-runways-close-together}

Where two parallel runways sit close together, ATC often clears you across both at once ("cross runways 28L and 28R at F"), but your route still has a stop before each. When you press Continue at the first and BA sees the second stop a short way ahead, it offers to clear both:

1. Press Continue at the first hold line. You hear something like *"Continuing. Taxiway F. Next hold short of runway 28R at F in 120 metres. Press continue again if cleared across both."*
2. If ATC cleared you across both, press Continue again once you have heard that. You hear *"Also cleared across runway 28R at F. No stop between the runways."*, and BA does not stop you between them.
3. If you were cleared across the first runway only, do nothing. BA stops you at the second hold line as normal.

A second press only counts once the offer has been spoken, about two seconds after the first, so a key that bounces or a quick double tap never clears the second runway by mistake. The offer stands for about twelve seconds.

### At the departure runway {#at-the-departure-runway}

At the hold for your departure runway you hear *"Stop. Hold short of Runway 27L. Press continue when cleared."* Continue here means "cleared to line up" (or cleared for takeoff). See [Lining Up on the Runway](#taxi-lineup).

## Runway Crossings {#taxi-crossings}

Every runway your route crosses or enters gets a stop, as described above, including your own departure runway if you cross it on the way to its threshold. A recalculated route gets its runway stops too, and a stop you have already rolled past is dropped, so you are never told to stop on a runway you were cleared across.

### Reminders and warnings near hold lines {#reminders-and-warnings-near-hold-lines}

BA watches for runway hold lines as you move, whether or not they are on your route.

- *"Crossing runway 22R at B."* A reminder that the hold line just ahead is part of your route, a runway crossing you have planned.
- *"Passing the runway 09L at F hold line."* The route passes a runway hold line but does not then go onto the runway, for example a line set back to protect an ILS beside your taxiway. You are not crossing a runway here.
- *"Warning: approaching runway 36L at M8, off route."* You are heading towards a runway hold line that is **not** on your route, for example because you turned the wrong way or missed a turn. Stop and check where you are with Output Alt+Y. This only speaks for a hold line actually ahead of you, on your heading or on the taxiway you are turning onto, not for lines beside a parallel taxiway you are correctly following. It repeats for the same line no more than every ten seconds.

## Lining Up on the Runway {#taxi-lineup}

At the departure runway, guidance takes you from the hold line onto the centreline.

1. At the hold: *"Stop. Hold short of Runway 27L. Press continue when cleared."* As you approach the runway you may also hear *"Runway 27L ahead."*
2. When ATC says "line up and wait" or "cleared for takeoff", press Input Y. You hear *"Entering Runway 27L, heading 268."*, with *"Turn right."* or *"Turn left."* added if you are more than ten degrees off the runway heading. Where the hold line is set back from the runway and the taxiway runs on before meeting it, you hear *"Continue onto the runway, then turn left."* instead: the tone first follows the taxiway onto the runway, and only then turns you onto the centreline, so you never cut across the grass. If the stop before your runway also guards a runway you cross on the way in, both are named at the hold line, for example *"Hold short of Runway 32 and runway 35."*
3. Follow the precise lineup tone onto the centreline (see [On the runway: lineup precision](#taxi-tone)). Roll forward gently while you turn; if you stop before you are lined up, the tone pulses.
4. When you are within one degree of the runway heading and within about 10 feet (3 metres) of the centreline, the tone stops and you hear *"Lined up, Runway 27L. Hold position."* This is the same position the runway teleport puts you in.

While lining up, Output Y says *"Lining up Runway 27L, runway heading 268. Follow the tone. 40 metres to go."*, and once lined up, *"Lined up Runway 27L, heading 268. Aligned — hold position."*

If you drift out of alignment again before taking off, the tone comes back.

If guidance is replaced by something else while you are lining up, you hear *"Lineup interrupted. No longer aligning with the runway."*

### Handing over to Takeoff Assist {#handing-over-to-takeoff-assist}

When you line up, BA can switch on Takeoff Assist for you. You hear *"Lined up. Activating takeoff assist."*, taxi guidance ends, and the takeoff roll tone takes over. This is controlled by **Auto-activate Takeoff Assist on lineup** on the **Hand Fly** tab in Settings, which is on by default. It fires once per route: if you switch Takeoff Assist off afterwards, it will not switch itself back on until your next route. You can always switch Takeoff Assist on or off yourself with Output Ctrl+T. See [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist).

## Going Off Route and Recalculation {#taxi-off-route}

If you leave your route, for example by missing a turn or taking the wrong taxiway, BA notices and plans a new route from where you are.

### When BA decides you are off route {#when-ba-decides-you-are-off-route}

You count as off route when you are clearly away from the taxiway you should be on for a few seconds while moving. To avoid false alarms, BA does not judge this until you have joined the route at least once (so the taxi from your stand onto the first taxiway never counts), nor during and just after a turn.

### "Route changed" {#route-changed}

When BA builds a new route, you hear *"Route changed. Now via C, D, crossing runway 26R. 900 metres to Runway 27L."* It names the taxiways of the new route and every runway it crosses, so you are never surprised by a hold short. BA keeps as much of your original clearance as still applies, starting from the latest cleared taxiway you are on. The new route gets stops before every runway, just like the original.

If the new route would be exactly the same as the rest of your old one, for example after cutting a corner wide, BA says nothing and simply carries on.

### When BA cannot recalculate {#when-ba-cannot-recalculate}

- *"Off route. Unable to recalculate."* BA could not find a route from where you are. Stop, check your position with Output Alt+Y, and plan a new route in the planner.
- *"Off route. Could not follow clearance. recalculated route too long. Continuing on original route."* or *"... recalculated route heads backwards ..."* The only new route BA could find would take you far out of your way, or send you back the way you came, so it kept your original route. Get back onto it, or plan a new one. The middle part may instead give a technical reason from the route builder.
- *"Off route. Unable to recalculate. The way to Gate C 9 crosses runway 13."* and similar. The only way to your destination from where you are crosses a runway over ground the airport data does not map, so BA will not steer you there.

## Arriving at a Stand {#taxi-gate-arrival}

As you near your stand, BA counts you down: *"Gate B 12 - Gate Heavy (Jetway) in 15 metres."*, *"10 metres."*, *"5 metres. Stop."* (in feet: 50, 20 and 10). If you stop early, you hear *"Stop. Hold position."*

Then BA turns you to face the stand: *"Align with Gate B 12 - Gate Heavy (Jetway), heading 270."*, with *"Turn left."* or *"Turn right."* if needed. The precise lineup tone guides you round. When you are lined up, you hear *"Aligned with Gate B 12 - Gate Heavy (Jetway). Parking brake."*

In these callouts BA uses the stand's full name exactly as it appears in the destination list, which is why it can be long. The examples below shorten it to "Gate B 12".

### With docking guidance {#with-docking-guidance}

If **Docking guidance (auto-engage at the gate)** is switched on in Settings and the stand has a known stop position, taxi guidance hands over to docking guidance for the last part, which takes you precisely to the stop mark with its own tone, beeps and distances. In that case the callouts change to tell you to keep going:

- *"5 metres. Continue ahead for docking."* or *"Docking guidance ahead. Continue forward."*
- *"Align with Gate B 12, heading 270. Then continue ahead for docking guidance."*
- *"Aligned with Gate B 12. Continue ahead. Docking guidance will take over."*

While docking is in charge, Output Y reports docking's own distance to the stop. See [Docking Guidance](gates-gsx-and-docking.md#docking).

Where a route ends somewhere with no stand heading, such as the end of a taxiway, you simply hear *"(destination) reached."*

### After landing {#after-landing}

If you used the Landing Exit Planner, exit guidance ends once you have left the runway, with a message such as *"Off the runway at B4. Stop and hold position. Open the taxi planner to set a route to your gate."* That is your cue to open the planner (Input Shift+Y) and enter your taxi-in clearance, as in [Example 7](#taxi-examples). After backtracking off a runway you hear *"Runway vacated. No route set — use the taxi planner for a route to your stand."* See [The Landing Exit Planner](approach-and-landing.md#landing-exit) for everything that happens between touchdown and that point.

## Taxi Routes in the Route Briefing {#taxi-briefing}

The AI route briefing also previews your expected taxi out and taxi in. It is a briefing, not live guidance; see [The AI Route Briefing](planning-your-flight.md#planning-route-briefing).

## Hotkeys During Taxi {#taxi-hotkeys}

These keys are the same on every aircraft. For how Output and Input modes work, see [Output Mode and Input Mode](getting-started.md#hotkey-modes).

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| Input Shift+Y | Open the taxi planner |
| Input Y | Continue past the current hold short |
| Input Ctrl+Y | Stop taxi guidance (you hear *"Taxi guidance stopped."*) |
| Input Ctrl+Shift+Y | Build a taxi route from your latest SayIntentions clearance |
| Input Shift+X | Open the Landing Exit Planner |
| Output Y | Taxi status: taxiway, next turn, distance to go |
| Output Ctrl+Y | Repeat the last taxi instruction |
| Output Alt+Y | Where Am I: your taxiway, stand or runway |
| Output Alt+L | Look Around: what is near you |
| Output Ctrl+Shift+L | The Surroundings window |
| Output Alt+G | Nearest ground traffic |
| Output Ctrl+T | Switch Takeoff Assist on or off |

</div>

## Taxi Guidance Settings {#taxi-settings}

Open Settings from the File menu and choose the **Taxi Guidance** tab.

- **Steering tone type**, **Volume** (default 5 percent), **Test Tone**, **Invert steering tone direction (steer AWAY from the tone)** and **Hard-pan steering tone (full left or full right; speaker-friendly)** — the tone settings described in [Settings that change the tones](core-concepts.md#audio-language).
- **Announce crossing taxiways (e.g., "Crossing taxiway Link 53")** — on by default. Untick for quieter taxiing.
- **Announce ground speed every:** and **During takeoff assist, announce ground speed every:** — see [Ground speed](knowing-where-you-are.md#where-ground-speed).
- **Use feet for distances (default: metres)** — every taxi distance in feet instead of metres. This covers turns, hold short countdowns, stands, runway exits and the status readouts.
- **Show ground traffic distances in metres (default: feet)** — a separate setting for the ground traffic callouts only. See [Ground Traffic](ground-traffic.md).
- **Auto-select arrival gate in GSX on route calculation** — see [Gates, GSX and Docking](gates-gsx-and-docking.md).
- **Docking guidance** settings — see [Docking Guidance](gates-gsx-and-docking.md#docking).
- **Refresh Taxiway Names**, **Online taxiway, gate and airport building names (OpenStreetMap + X-Plane)** and **Read installed scenery for airport buildings (offline)** — where BA gets names from. See [Knowing Where You Are](knowing-where-you-are.md).
- **Announce airport buildings as you taxi past them** and **Tell me when I leave the paved surface** — optional extra callouts while taxiing, such as *"Passing Concourse B, on the left."* and *"Off the pavement, on grass."* See [Callouts While You Taxi](knowing-where-you-are.md#where-taxi-callouts) and [The Off-the-Pavement Alerts](knowing-where-you-are.md#where-off-pavement).
- **SayIntentions import starts taxi guidance immediately** — see [SayIntentions Taxi Import](#taxi-sayintentions).

Two related settings live elsewhere:

- **Auto-activate Takeoff Assist on lineup**, on the **Hand Fly** tab, on by default.
- The sound device your guidance tones play on, on the **Audio** tab.

## Everything Taxi Guidance Says {#taxi-callouts}

This is a reference list of every taxi guidance callout, grouped by when you hear it. Names, numbers and distances are examples. Where a stand is named, BA uses its full name from the destination list; it is shortened to "Gate B 12" here.

### When you plan and start a route {#when-you-plan-and-start-a-route}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Full length holding point A1."* | The painted line a normal departure from this runway uses (heard when you choose a runway). | Check it matches your clearance; if not, use Depart from named holding point. |
| *"CAT three hold A3."* | With the LVP box ticked, the low-visibility line for this runway. | Nothing; for information. |
| *"No runway intersections available. Full length departure."* | The runway has no intersections to offer. | Plan a full-length departure, or check the runway. |
| *"No named holding points available for this runway. Full length departure."* | No online holding-point names for this runway. | Use the normal full length, or an intersection. |
| *"No deicing areas at this airport."* | No GSX de-icing pads here. | Choose another destination type. |
| *"Route to Runway 27L via A, B. 1450 metres, crossing runway 22R."* | The route summary. | Check it matches your clearance. |
| *"Warning: this route does not reach Runway 27L. Check your taxiway entry and reprogram."* | The route ends away from the runway. | Add the missing link taxiway and recalculate. |
| *"Warning: BAW123 is at the destination gate."* | Your stand is occupied. | Check with ATC; choose another stand if needed. |
| *"Intersection B3 departure, runway 27L. About 2400 metres of runway ahead."* | Confirms your intersection. | Check the runway length is enough. |
| *"Holding point A2, runway 28. About 3600 metres of runway ahead."* | Confirms your named holding point. | Nothing. |
| *"No backtrack entrance found for this runway. Full length departure."* | No entrance suited to a backtrack. | Plan a normal departure. |
| *"Taxiway A. Steering guidance active."* | Guidance has started; you are on A. | Start taxiing. |
| *"Steering guidance active. Join taxiway A in 60 metres."* | Guidance has started; A is ahead. | Taxi onto A, following the tone. |
| *"Taxiway A is behind you. Turn left to come around."* | The route starts behind you. | Turn round in the direction given. |
| *"Make a U-turn to the right."* | The same, with no taxiway name. | Turn round to the right. |
| *"Sharp turn left onto taxiway K."* or *"Sharp turn left."* | The route starts at a sharp angle. | Make the turn. |
| *"Your position isn't connected to the taxiway network. The first 40 metres to taxiway A aren't mapped."* | You start off the mapped taxiways. | Taxi carefully onto the route, checking it is clear. |

</div>

### While taxiing {#while-taxiing}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Taxiway B."* | You have joined taxiway B. | Nothing; for information. |
| *"In 100 metres, turn left onto taxiway B."* | A turn is coming. | Get ready, slow if needed. |
| *"In 80 metres, slight right onto taxiway C."* | A gentle turn (20 to 60 degrees) is coming. | Get ready. |
| *"In 150 metres, sharp right onto taxiway D, 90 degrees."* | A big turn, with its angle. | Slow well before it. |
| *"In 100 metres, continue onto taxiway E."* | The name changes but you go straight. | Carry straight on. |
| *"Left now, taxiway B."* | Start the turn now. | Turn, following the tone. |
| *"Curving left."* | The taxiway bends gradually. | Follow the tone round. |
| *"Straighten."* | You have turned enough; the route is straight ahead. | Roll out of the turn. |
| *"Crossing taxiway K."* | You are going straight across K. | Nothing; for information. |
| *"Slow for turn."* / *"Slow for sharp turn."* | Too fast for the coming turn. | Slow down. |
| *"Taxi speed, 32 knots."* | Faster than 30 knots. | Slow down. |
| *"Runway 27L ahead."* | Your destination is close. | Get ready to stop. |
| *"Crossing runway 22R at B."* | A runway crossing on your route is just ahead. | Make sure you are cleared. |
| *"Warning: approaching runway 36L at M8, off route."* | You are heading for a runway that is not on your route. | Stop, check your position. |

</div>

### At hold shorts {#at-hold-shorts}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Hold short runway 22R in 120 metres."* | A runway hold is coming. | Prepare to stop. |
| *"Hold short runway 22R in 60 metres. Slow down."* | It is close. | Slow down. |
| *"Hold short runway 22R in 30 metres. Stop."* / *"Hold short runway 22R. Stop."* | Stop now. | Stop. |
| *"Stop. Hold short of runway 22R. Press continue when cleared."* | You are at the line; the tone is paused. | Wait for clearance, then Input Y. |
| *"Stop. Hold short of end of taxiway A. Press continue when cleared."* | A taxiway hold you asked for. | Wait for clearance, then Input Y. |
| *"Stop. Hold short of runway 09 and runway 01. Press continue when cleared for runway 09."* | One stop guards two runways. | Input Y when cleared for 09. |
| *"Runway 09 cleared. Still holding. Press continue when cleared for runway 01."* | First runway cleared; still waiting. | Stay put; Input Y when cleared for 01. |
| *"Continuing. Taxiway B."* | You pressed Continue; guidance resumes. | Taxi on. |

</div>

### Lining up and departing {#lining-up-and-departing}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Stop. Hold short of Runway 27L. Press continue when cleared."* | At your departure runway hold. | Input Y when cleared to line up. |
| *"Entering Runway 27L, heading 268. Turn right."* | You are cleared on; turn onto the runway. | Follow the lineup tone. |
| *"Lined up, Runway 27L. Hold position."* | You are on the centreline, aligned. | Wait for takeoff clearance. |
| *"Lined up. Activating takeoff assist."* | Takeoff Assist is taking over. | Prepare for takeoff. |
| *"Lineup interrupted. No longer aligning with the runway."* | Lineup guidance was stopped by a new route. | Check what you intended. |
| *"This route does not reach Runway 27L. Reprogram the taxi route, including the taxiway that connects to the runway."* | You are far from the runway centreline during lineup. | Stop and plan a new route. |
| *"Stop. Hold short of Runway 32. Press continue when cleared to enter and backtrack."* | Backtrack departure hold. | Input Y when cleared to backtrack. |
| *"Entering Runway 32. Backtrack to full length, heading 140, then turn around to heading 320."* | Taxi down the runway to its end. | Follow the tone. |
| *"Approaching runway end. Slow down, prepare to turn around to heading 320."* | The turnaround is close. | Slow down. |
| *"Runway end. Turn around to line up, heading 320."* | Turn around now. | Follow the tone through the turn. |

</div>

### Progressive Taxi {#progressive-taxi-2}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Hold short of runway 09. Set a new route when cleared."* | End of the leg. | Plan the next leg when ATC gives it. |
| *"Hold short of taxiway D. Set a new route when cleared."* | End of the leg. | Plan the next leg. |
| *"Across runway 09. Hold position. Set a new route when cleared."* | You are across the runway you were cleared over. | Plan the next leg. |
| *"Hold at VIKAS. Set a new route when cleared."* | At the named holding point. | Plan the next leg. |
| *"Route ended. Hold position. Set a new route when cleared."* | End of your last taxiway. | Plan the next leg. |

</div>

### Arriving {#arriving}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Gate B 12 in 15 metres."*, *"10 metres."*, *"5 metres. Stop."* | Distance to the stand. | Slow and stop. |
| *"Stop. Hold position."* | You stopped before the last step. | Stay stopped. |
| *"Align with Gate B 12, heading 270. Turn left."* | Turn to face the stand. | Follow the tone. |
| *"Aligned with Gate B 12. Parking brake."* | You are parked and aligned. | Set the parking brake. |
| *"5 metres. Continue ahead for docking."* / *"Docking guidance ahead. Continue forward."* | Docking guidance will take you the rest of the way. | Keep rolling slowly. |
| *"Aligned with Gate B 12. Continue ahead. Docking guidance will take over."* | Aligned; docking takes over. | Follow docking guidance. |
| The destination's name, then *"reached."* | You have arrived somewhere with no stand heading. | Stop. |

</div>

### Off route {#off-route}

<div class="table-wrap" markdown="1">

| You hear | What it means | What to do |
|---|---|---|
| *"Route changed. Now via C, D, crossing runway 26R. 900 metres to Runway 27L."* | A new route from where you are. | Follow it; check it agrees with ATC. |
| *"Off route. Unable to recalculate."* | No new route could be found. | Stop and plan a route. |
| *"Off route. Could not follow clearance. recalculated route too long. Continuing on original route."* | The new route would be a big detour, so it kept the old one. | Return to your route or plan a new one. |
| *"Off route. Unable to recalculate. The way to Gate C 9 crosses runway 13."* | The only way crosses an unmapped runway. | Stop and ask ATC. |

</div>

### Hotkey answers {#hotkey-answers}

<div class="table-wrap" markdown="1">

| You hear | When |
|---|---|
| *"Taxiway A, right onto taxiway B in 150 metres, 1200 metres to Runway 27L."* | Output Y while taxiing. |
| *"Holding short of runway 22R. Press continue when cleared."* | Output Y at a hold. |
| *"Lining up Runway 27L, runway heading 268. Follow the tone. 40 metres to go."* | Output Y while lining up. |
| *"Backtracking Runway 32 to full length. 600 metres to go, then turn around to heading 320."* | Output Y during a backtrack departure. |
| *"Arrived at Gate B 12."* | Output Y after arriving. |
| *"No route loaded."* | Output Y when guidance has nothing to follow. |
| *"No taxi guidance active."* | Output Y or Ctrl+Y with guidance off. |
| *"No taxi instruction yet."* | Output Ctrl+Y before anything has been said. |
| *"Taxi guidance stopped."* | Input Ctrl+Y. |

</div>

## If Something Goes Wrong {#taxi-troubleshooting}

**I can't hear the tone.** The tone is silent whenever you are on track, at a hold and once lined up, so silence is usually good news. Check the volume and use Test Tone on the Taxi Guidance tab. Check which device it plays on, on the Audio tab. If you have a hold-short cue, press Continue only once cleared; the tone resumes then.

**The tone tells me to turn the opposite way to the words.** It should not: both are worked out from your actual heading. If you have the **Invert steering tone direction** box ticked, the tone means steer away from it.

**I followed the tone and ended up going in circles after a wide turn.** Stop, press Output Y to hear where you are, and if necessary recalculate from the planner. Guidance always picks up your real position on the taxiway again.

**The first taxiway in the planner isn't the one ATC said.** BA guesses the nearest taxiway in front of you. Always set First taxiway yourself.

**A taxiway ATC named isn't in the list.** Your scenery's data does not have it under that name. Switch on online taxiway names, rebuild your database if you have changed scenery, or pick the nearest equivalent. See [Taxiway names you can't find](#taxi-clearances).

**The summary says the route does not reach the runway.** The last taxiway you entered runs beside the runway without joining it. Add the short link onto the runway as the last taxiway.

**The summary says it used the shortest path.** Two of your taxiways do not meet in the scenery's data, or they are in the wrong order. Check each pair; the reason names the pair that failed.

**I'm stopped at a hold line and nothing happens.** BA is waiting for you. Press Input Y when cleared. At a Progressive Taxi end point, plan the next leg instead.

**BA stopped me at a runway ATC had already cleared me across.** That is by design: BA cannot hear ATC. Press Input Y and carry on. For a Progressive leg you can choose **After crossing runway** so it does not stop you there.

**I heard "Route changed" when I didn't expect it.** You strayed far enough from the route for BA to replan. Check the new taxiways against your clearance; if they are wrong, stop and recalculate from the planner with the right ones.

**I closed the planner and guidance is still running.** Closing the window only hides it. Use Input Ctrl+Y or Stop Guidance to stop.

**The stand I need isn't in the list.** Untick **Show fitting only** and **Hide occupied stands**, and clear the **Gate search** box. If the stand is listed with "(no taxi route)", the scenery's taxiways do not reach it; teleporting there may be the only option (see [Teleporting to a Runway or Gate](planning-your-flight.md#teleport)).

**Distances are in the wrong units.** Use **Use feet for distances** on the Taxi Guidance tab. Ground traffic distances have their own setting.

For anything else, see [Logs and Getting Help](getting-started.md#logs-and-help). Taxi guidance writes its own logs, which help a lot when reporting a problem at a particular airport.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Knowing Where You Are](knowing-where-you-are.md)
- [Next chapter: Ground Traffic](ground-traffic.md)
- [Guide contents](index.md)

</nav>
