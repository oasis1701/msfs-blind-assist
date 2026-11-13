---
title: "Planning Your Flight"
---

<div class="guide-part" markdown="1">

Chapter 1 of 2 in [Part 4: Planning and Preflight](index.md#part-3-planning-and-preflight) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Teleporting to a Runway or Gate](#teleport)
- [SimBrief](#simbrief)
- [The Flight Planning Window](#flight-planning-efb)
- [The AI Route Briefing](#planning-route-briefing)
- [Aircraft EFBs](#aircraft-efb)
- [Flows and Checklists](#checklists)
- [Tracking Waypoints](#fix-tracking)
- [NAV Radios](#planning-nav-radios)
- [FMC Settings](#planning-fmc-settings)

</nav>

A typical routine before you push back: build or load a plan in SimBrief, open the Flight Planning window to check the route and procedures, ask for the AI briefing if you have an AI key, then work through the aircraft's EFB and, with the First Officer, its flows and checklists. Most of these tools read the [navigation database](getting-started.md#nav-database).

<div class="callout tip" role="note" markdown="1">

**Tip:** The Flight Planning window and the SimBrief briefing work before BA has connected to the simulator, and the First Officer window opens then too, so you can plan while the sim is still loading.

</div>

## Teleporting to a Runway or Gate {#teleport}

### What it is {#what-it-is-8}

Teleporting moves your aircraft instantly to a chosen runway or parking spot at any airport in your navigation database. It is handy for practising take-offs, getting to the right stand after loading at the wrong one, or skipping a long taxi while you learn.

<div class="callout warning" role="note" markdown="1">

**Warning:** Teleporting moves the aircraft; it does not reset it. Teleport only with the aircraft stopped on the ground, and expect the aircraft's systems to stay exactly as they were. Online networks such as VATSIM do not allow teleporting while you are connected.

</div>

### Runway teleport (Input Shift+R) {#runway-teleport-input-shift-r}

1. Press Input Shift+R. The **Runway Teleport** window opens with the focus in **Airport ICAO Code**.
2. Type the four-letter code. The **Runway List** fills, and the status line says, for example, *"Found 4 runways for London Heathrow"*.
3. Press Enter to jump to the list, and arrow to the runway you want.
4. Press Enter (or the **Teleport** button). A **Confirm Teleport** box asks *"You're about to reposition your aircraft to Runway 27R at London Heathrow. Are you sure?"*. The focus starts on **No**, so you cannot teleport by accident; choose **Yes** to go.

BA places you at the start of the runway, lined up with it and on the ground. It also tells Takeoff Assist which runway you are on, so Ctrl+T guides you on that runway straight away; see [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist).

### Gate teleport (Input Shift+G) {#gate-teleport-input-shift-g}

1. Press Input Shift+G. The **Gate &amp; Parking Teleport** window opens with the focus in **Airport ICAO Code**.
2. Type the four-letter code. The **Gate and Parking List** fills with every stand and parking spot, and the status line says, for example, *"Found 120 gates and 45 parking spots for London Heathrow"*.
3. To narrow the list down:
   - Type part of a stand name in **Search**, then press Enter to jump to the matches. If nothing matches, BA says *"No matching gates."*. Search also finds the real-world names of stands that online data adds, so you can search for the gate number on your ticket even when the scenery calls it something else.
   - Choose a type in **Filter by parking type**: All, Gate Small, Gate Medium, Gate Large, Gate Heavy, Gate Extra, Ramp GA, Ramp Cargo, Ramp Military, Dock or Other (only the types the airport has are listed).
   - Leave **Show only fitting stands** ticked to hide stands too small for your aircraft. It is ticked automatically when BA knows your aircraft's wingspan.
4. Arrow to the stand and press Enter (or **Teleport**). Confirm with **Yes** in the **Confirm Teleport** box.

For how stand names, sizes and GSX gates work, see [Gates, GSX and Docking](gates-gsx-and-docking.md).

### If something goes wrong {#if-something-goes-wrong-6}

- **"Airport database not found. Configure database from File menu first."** Build the navigation database; see [The Navigation Database](getting-started.md#nav-database).
- **A box says the database does not match the simulator.** Your database was built for the other simulator version. Follow the box's advice, or see [The Navigation Database](getting-started.md#nav-database).
- **"Airport XXXX not found in database"** Check the code. Add-on airports appear only after you rebuild the database with them installed.
- **The stand you want is missing.** Untick **Show only fitting stands**, set the filter to All, and clear the Search field.

## SimBrief {#simbrief}

### What it is {#what-it-is-9}

SimBrief is a free online flight planner used by most simulator pilots. It works out a route, fuel, weights, take-off and landing performance and weather for your flight, and publishes the result as an operational flight plan (OFP). BA connects to your SimBrief account so you can load that plan into the Flight Planning window, read every page of the OFP in an accessible window, and even start a new SimBrief plan with the fields filled in for you.

### Setting your SimBrief username {#setting-your-simbrief-username}

BA needs only your SimBrief username, not your password.

1. Open **File**, then **Settings**, and move to the **SimBrief** tab.
2. In the **SimBrief Username** field, type the username you use to log in to www.simbrief.com.
3. Press OK to save.

From now on, the first time you open the Flight Planning window in a session, BA fetches your latest SimBrief plan automatically.

### Opening your briefing in the browser (Output Shift+B) {#opening-your-briefing-in-the-browser-output-shift-b}

Press Output Shift+B. BA says *"Opening your SimBrief briefing"* and opens your latest SimBrief briefing page in your web browser. This is SimBrief's own website, so you need to be logged in to SimBrief in that browser. Use this when you want the full original OFP exactly as SimBrief presents it; for an accessible, tabbed view of the same plan, use the SimBrief Flight Planner window below.

### The SimBrief Flight Planner window {#the-simbrief-flight-planner-window}

The SimBrief Flight Planner window has two tabs, **Create** and **View**. You open it from the Flight Planning window:

1. Press Output Shift+E to open the Flight Planning window.
2. Press Ctrl+Tab until you reach the **SimBrief** tab (it is the last tab).
3. Press **Create SimBrief Plan** to open the planner on its Create tab, or **View Detailed Plan Info** to open it on its View tab.

Every time you open the planner, BA fetches your latest plan again, so what you read is never stale.

### Creating a new plan (Create tab) {#creating-a-new-plan-create-tab}

The Create tab lets you fill in the basics in an accessible form and then hands them to the SimBrief website, which does the actual planning. The fields are:

<div class="table-wrap" markdown="1">

| Field | What to enter |
|---|---|
| Origin ICAO | The four-letter code of your departure airport, for example EGLL. Required. |
| Destination ICAO | The four-letter code of your destination, for example KJFK. Required. |
| Aircraft Type | An ICAO type code such as B77W or A20N. You can type it, or choose from the list, which loads from SimBrief. |
| Alternate ICAO | Your alternate airport, optional. |
| Cruise Level (ft) | For example 36000 or FL360, optional. |
| Airline ICAO Code | Three letters, for example BAW, optional. |
| Flight Number | The number without the airline prefix, for example 178, optional. |
| Cost Index | 0 to 999; it starts at 30. |
| Route (optional) | A route string such as MATCH DCT WOBUN. Leave it empty to let SimBrief choose. |
| Number of Passengers | Optional. |
| Freight Weight | Optional, in the units below. |
| Weight Units | lbs or kgs. |

</div>

When you are ready, press **Plan on SimBrief Website**. Your browser opens on SimBrief's dispatch page with your details already filled in, and the status line below the button says *"Opened SimBrief in your browser. You may need to log in first."* Finish the plan on the website and generate it. If you left Origin or Destination empty, the status line says *"Origin and Destination are required."* instead.

Once SimBrief has generated the plan, go back to the Flight Planning window and press **Load SimBrief** on its Route Viewer tab to load it into BA.

### Reading your plan (View tab) {#reading-your-plan-view-tab}

The View tab shows your latest OFP. At the top are a **Fetch Latest Plan** button and a status line, which reads, for example, *"Plan loaded: EGLL → KJFK"* once the plan has arrived. Below them is a set of tabs, one per part of the plan. Use Ctrl+Tab to move between them and Tab to move into each one.

- **Overview** is a tree with six sections: Flight Info, Departure, Destination, Cruise, Fuel Summary and Weight Summary. Arrow to a section and press Right Arrow to open it; its details appear as items underneath. Left Arrow closes it again.
- **Nav Log** is a tree with one line per waypoint: its name, whether it is on the SID or STAR, the airway, the leg and total distance, the course, the planned altitude and the planned speed. Press Right Arrow on a waypoint to see more: its full name and type, the FIR, a frequency, leg time, elapsed time, the forecast wind with its headwind or tailwind component, the outside temperature and ISA deviation, the minimum off-route altitude (MORA), and the planned fuel figures (fuel on board at that point, leg fuel and total fuel used). The window shows the keys at the top of the tab: *"Up/Down: waypoints Right: expand Left: collapse"*.
- **Fuel** and **Weights** are read-only lists with the full fuel breakdown and the weights and payload breakdown. Arrow through them line by line.
- **Performance** has three lists: **Takeoff Performance** (V1, rotation speed, V2, flaps, thrust and runway conditions), **En Route Performance** (climb, cruise and descent profiles and speeds), and **Landing Performance** (reference speed, flaps, brakes and runway surface). Under the take-off and landing lists are the buttons **Show Additional Takeoff Info** and **Show Additional Landing Info**, which add runway distances and weight data to the list and move you to them. Press the button again (it is now called Hide Additional…) to remove them. A button stays dimmed when the plan has no such extra data.
- **Weather** has one list each for the departure, destination and alternate airport, with the METAR and TAF for each. If the plan has no alternate, its list says *"No alternate in this flight plan."*

<div class="callout note" role="note" markdown="1">

**Note:** The SimBrief Flight Planner shows SimBrief's plan exactly as SimBrief made it. It does not change anything in your aircraft. You still enter the route and performance into the aircraft's own FMC, MCDU or EFB.

</div>

### If something goes wrong {#if-something-goes-wrong-7}

- **"SimBrief username not configured. Please configure in settings."**, or the View tab says *"No SimBrief username set. Go to File &gt; Define SimBrief Username."* Enter your username on the **SimBrief** tab of Settings, as above.
- **"Invalid SimBrief username or no flight plan found for user"** Check the spelling of the username, and make sure you have generated at least one plan on the SimBrief website.
- **"Timed out contacting SimBrief"** Check your internet connection and try again.
- **The plan you just made is not the one loaded.** BA always fetches your most recent SimBrief plan. Make sure you generated (not just saved) the new plan on the website, then press Load SimBrief again.

## The Flight Planning Window {#flight-planning-efb}

### What it is {#what-it-is-10}

The Flight Planning window, titled **Electronic Flight Bag**, is BA's own route viewer, procedure planner and airport reference. It shows your flight plan as a table of waypoints with distances and bearings from your aircraft, lets you choose a departure runway, SID, STAR, approach and arrival runway from the navigation database, looks up detailed airport and runway information, and produces the AI route briefing.

It is a planning and reference tool. It does **not** program your aircraft's FMC or MCDU; you still enter the route and procedures there. What it gives you is a clear, readable picture of the plan, so that when you enter it into the aircraft, or hear ATC give you a SID, you know what it means.

<div class="callout note" role="note" markdown="1">

**Note:** This window is not the same as your aircraft's own tablet (the flyPad, the PMDG tablet and so on). Those are covered in [Aircraft EFBs](#aircraft-efb).

</div>

### When to use it {#when-to-use-it-2}

- Before the flight, to check the route SimBrief planned and the waypoints of your SID, STAR and approach.
- When ATC or SayIntentions gives you a different runway or procedure, to see what the new one looks like.
- In flight, to see how far away each waypoint is (press F5 to refresh).
- At any time, to look up an airport's frequencies or a runway's length, ILS and lighting.

### Opening and closing it {#opening-and-closing-it}

Press Output Shift+E. The window opens with the focus on the waypoint list. If you have a SimBrief username set and no plan loaded yet, BA loads your latest SimBrief plan straight away and says *"SimBrief flight plan loaded"*.

Close the window with Alt+F4. Your flight plan is kept: the next time you press Output Shift+E it is still there, including any procedures you changed, and so is any route description you generated.

### How the window is laid out {#how-the-window-is-laid-out}

The window has eight tabs. Press Ctrl+Tab to move to the next tab and Ctrl+Shift+Tab to go back. In order they are:

1. **Route Viewer** — your flight plan, with the Load SimBrief, Refresh Position and Describe Route buttons.
2. **SID** — choose a standard instrument departure.
3. **STAR** — choose a standard terminal arrival route.
4. **Approach** — choose an approach procedure.
5. **Departure** — set the departure airport and runway.
6. **Arrival** — set the arrival airport and runway.
7. **Airport Lookup** — detailed airport and runway information.
8. **SimBrief** — open the SimBrief Flight Planner.

At the bottom of the window a status line (named **Status**) reports what just happened, such as *"Flight plan: 42 waypoints"*; read it with your screen reader's review commands.

Every procedure tab works the same way: you type a four-letter ICAO code, and as soon as the fourth letter is in, the lists below fill in by themselves. There is no Search button to press. The first item in each list is selected automatically, and choosing an item in one list fills the next list along.

### The Route Viewer tab {#the-route-viewer-tab}

This tab holds your flight plan.

The buttons along the top are:

- **Load SimBrief** — fetches your latest SimBrief plan and replaces the whole flight plan with it. BA says *"SimBrief flight plan loaded"*. Loading a plan also fetches the online taxiway names for both airports in the background, so taxi guidance has them ready when you get there.
- **Refresh Position (F5)** — works out the distance and bearing from your aircraft to every waypoint. BA says *"Position updated"*. This needs BA to be connected to the simulator; otherwise it says *"Not connected to simulator"*. You can press F5 anywhere in the window.
- **Describe Route** — creates the AI route briefing (see [The AI Route Briefing](#planning-route-briefing)). It is dimmed until a SimBrief plan is loaded, and while a briefing is being prepared.

Below the buttons is the **Waypoint Navigation List**. It is a table: each row is a waypoint, and each column is a piece of information about it. BA reads the cell you move to, saying the column name and the value, for example *"Waypoint, DET"* or *"Dist (NM), 23.4"*. An empty cell is read as the column name followed by *"blank"*.

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| Up Arrow / Down Arrow | Previous or next waypoint, staying in the same column |
| Left Arrow / Right Arrow | Previous or next column on the same waypoint |
| Home / End | First or last column of this waypoint |
| Ctrl+Home / Ctrl+End | First waypoint, or last waypoint |
| Page Up / Page Down | Move a screenful of waypoints at a time |
| F5 | Refresh distances and bearings from your current position |
| Applications key (or Shift+F10) | Track this waypoint in one of the five tracking slots |

</div>

The waypoints run in flight order: the departure airport and runway, the SID, the en-route part, the STAR, the approach, and the arrival airport. The first two columns, **Waypoint** and **Region**, are always there. Other columns appear only when at least one waypoint has something to show in them, so a simple plan has fewer columns than a detailed one. The possible columns are:

- **Dist (NM)** and **Brg (°)** — distance in nautical miles and bearing from your aircraft. These appear after you press Refresh Position.
- **Airway** — the airway or procedure the waypoint is reached by.
- **Type** and **Fix Type** — what kind of waypoint or leg it is.
- **Leg Dist** — the distance from the previous waypoint.
- **Course** and **T/M** — the course of the leg, and whether it is true (T) or magnetic (M).
- **Notes**, **Altitude** (restrictions such as *"AT OR ABOVE 5000 FT"*), **Speed** and **Spd Type** (speed limits).
- **ARINC**, **Missed** (MISS marks a missed-approach leg), **Vert Angle**, **Time**, **Turn**, **RNP**, **Theta**, **Rho**, **Flyover** and **Alt Fix** — detailed procedure data that appears on SIDs, STARs and approaches.

Some legs of a procedure are not flown to a named waypoint at all. The first leg of most SIDs, for example, is "climb on this heading until a certain altitude", and the missed approach often begins the same way. BA keeps these legs in the list and gives them a plain description instead of a waypoint name, such as *"Climb heading 071° to 600 feet"*. They have no position, so their distance and bearing cells are blank.

To track a waypoint, arrow to it, press the Applications key (or Shift+F10) and choose **Track Slot 1** to **Track Slot 5**. BA says, for example, *"Waypoint DET tracked in slot 1"*. See [Tracking Waypoints](#fix-tracking) for what to do next.

Once you have generated a route briefing, a read-only **Route Description** box appears below the list. Tab to it and read it line by line with the arrow keys.

### The SID tab {#the-sid-tab}

A SID (standard instrument departure) is the published route from the runway to the start of the en-route part of your flight.

1. In **SID Airport ICAO**, type the departure airport's code.
2. The **SID Runways** list fills with **ALL** first, then each runway that has SIDs. Choose your departure runway, or ALL to see every SID at the airport.
3. The **SID Procedures** list fills with the SIDs for that runway. Choose one.
4. The **SID Transitions** list fills with **None** first, then the SID's transitions. A transition joins the SID to a particular airway or waypoint; SimBrief and ATC will tell you which one, if any. Leave it on None if you do not need one.
5. Press **Load SID**. BA says, for example, *"Loaded CPT5J RWY 27R"* and the SID replaces any SID already in your plan.

Choosing **ALL** loads the part of the SID that is common to every runway. Choosing a runway loads the version for that runway, including its runway-specific first legs, so pick your actual runway whenever you know it.

### The STAR tab {#the-star-tab}

A STAR (standard terminal arrival route) is the published route from the end of the en-route part down towards the approach.

The STAR tab works exactly like the SID tab: type the arrival airport in **STAR Airport ICAO**, choose a runway (or ALL) in **STAR Runways**, choose a STAR in **STAR Procedures**, choose a transition or None in **STAR Transitions**, then press **Load STAR**. BA says, for example, *"Loaded ARTIP1A RWY 18R"* and the STAR replaces any STAR already in your plan.

### The Approach tab {#the-approach-tab}

1. In **Approach Airport ICAO**, type the arrival airport's code.
2. The **Approaches** list fills with every approach at that airport, named by type and runway, for example "ILS RWY 18R". Circling approaches, which serve the airport rather than one runway, are included too.
3. The **Transitions** list fills with None first, then the approach's transitions. Choose one or leave None.
4. Press **Load Approach**. BA says, for example, *"Loaded approach ILS RWY 18R"*. The approach, including its missed approach legs, replaces any approach already in your plan.

### The Departure tab {#the-departure-tab}

This tab sets the departure airport and runway at the very start of the plan.

1. In **Departure ICAO**, type the airport's code.
2. The **Departure Runways** list fills with every runway, each with its length, width, surface and, if it has one, its localizer: "ILS" (localizer and glideslope), "Localizer" (no glideslope), or "LDA" or "IGS" for an offset approach that does not line up with the runway, with its frequency and magnetic course. Lengths are shown in the ground distance unit you chose in Settings (**Use feet for distances (default: metres)** on the Taxi Guidance tab).
3. Choose the runway and press **Load Departure**. BA says, for example, *"Loaded departure EGLL runway 27R"*.

The departure runway you set here is also the runway the taxi-out part of the AI briefing plans to.

### The Arrival tab {#the-arrival-tab}

This tab sets the arrival airport and runway at the end of the plan. It works like the Departure tab: type the code in **Arrival ICAO**, choose a runway in **Arrival Runways**, and press **Load Arrival**. BA says, for example, *"Loaded arrival EHAM runway 18R"*. The arrival runway you set here is the one the taxi-in part of the AI briefing plans from.

<div class="callout note" role="note" markdown="1">

**Note:** Setting the arrival runway here does not set BA's destination runway for approach and landing guidance. That is done separately with Input Shift+D; see [Choosing a Destination Runway](approach-and-landing.md#destination-runway).

</div>

### The Airport Lookup tab {#the-airport-lookup-tab}

This tab is a reference page for any airport in your navigation database. You can find an airport by its ICAO code, or by part of its name or city.

1. In **Airport Lookup ICAO**, type the airport's code. As soon as the fourth letter is in, the **Airport Information** box fills in, the **Airport Lookup Runways** list fills with the runways, and the first runway's details appear in the **Runway Information** box. Nothing is spoken: press Tab, and the first line of the Airport Information box gives the airport's code and name, for example "KATL, Hartsfield - Jackson Atlanta I". A code with no airport reads "Airport ROME not found in database."
2. To search, type a code or part of an airport's name or city, and press Enter. Case, accents and apostrophes don't matter: "Zürich" finds "Zurich" and "ohare" finds "Chicago-O'Hare". With several words, each must appear somewhere in the code, name or city. Letters in the middle of a word count too, so "LAX" finds KLAX.
   - When what you typed is an airport's exact code, or only one airport matches, it loads at once and its code and name are spoken. KATL loads, even though other airports' names contain those letters. A code with no runway, usually a helipad's, loads at once only when nothing else matches: "MCO" on MSFS 2024 is a helipad named Orlando International Airport, so it opens the list, where you also find Orlando's KMCO.
   - Otherwise a list opens, best match first, and its title says how many airports match. Arrow to an airport and press Enter to load it, or press Escape to close the list; either way you are back in the field. The list shows at most 50 airports, and its title says when more matched: "50 of 70 airports match LONDON". An exact code also opens the list when another airport is named exactly what you typed, or a town is: "LIMA" lists Turin-Aeritalia, whose code it is, then the airports of Lima, Peru.
   - When nothing matches, you hear "No airports match" and what you typed.
3. The tab order is the field, Airport Information, the runway list, then Runway Information. Arrow through the runway list: the Runway Information box updates for each runway as you move.
4. Read either information box line by line with the arrow keys. Both boxes are read-only.

**Towns.** With the offline location data installed (see [Getting the location data](core-concepts.md#getting-the-location-data)), a town's name finds the airports around it, so "Gothenburg" lists Landvetter and Säve, each "11 nm from Gothenburg, Västra Götaland, Sweden". Each town adds up to 10 airfields within 30 nm, open ones and the longest runways first. A helipad is never added for a town; you find it by its own name or code. A town with no airfield within 30 nm lists its 3 nearest within 100 nm. When several towns share a name, the most populous comes first: "London" lists London, England, then London, Ontario, and so on. Only towns of 500 people or more are searched, and only by their whole name.

**On MSFS 2024**, the navigation data has no city names, so a city is found only through an airport's own name ("Atlanta" finds Hartsfield - Jackson Atlanta) or through the location data. Without the location data installed, a search that finds nothing tells you: "Searching by city on MSFS 2024 needs the location data."

**Distance from you.** When connected to the simulator, each line of the list, the spoken airport and the Airport Information box say how far the airport is from you and in which direction, for example "212 nm southwest". With the simulator closed they leave it out.

The **Airport Information** box starts with the airport's code and name, then is grouped under headings: basic information (ident and name), from aircraft (the distance and direction, when connected), location (coordinates, elevation and magnetic variation), frequencies (Tower, ATIS, AWOS, ASOS and UNICOM), status (closed, military, add-on scenery), fuel, a count of facilities (gates, ramps, runways, approaches and so on), a runway summary, parking, the tower position, the transition altitude and level where the navigation data has them, and the scenery file the airport comes from.

The **Runway Information** box starts with the runway's localizer under **Localizer and glideslope**. Its first line says what it is, in the same words as the runway lists: "ILS 110.10, course 171", "Localizer 108.70, course 152", "LDA 111.35, course 147", or "No localizer". A glideslope that is not 3 degrees is added, and CAT II or CAT III is named. The details follow (ident, frequency, localizer heading and width, glideslope angle and DME). Then it gives the dimensions, surface, true and magnetic heading, threshold position, displaced threshold, blast pad and overrun, pattern altitude and runway elevation, lighting, markings, whether the runway is used for take-off and landing, and any VASI or PAPI.

A localizer heading is marked as a true heading ("0.5° true"), so do not mistake it for the magnetic course. Fields the navigation data leaves empty are left out or read "N/A".

<div class="callout note" role="note" markdown="1">

**Note:** Approach minimums (the decision altitude, minimum descent altitude and visibility) are not in any navigation data, whether from the simulator or Navigraph. They exist only on the printed approach chart, so BA cannot show them. Get them from your chart source or your virtual airline, and enter them in the aircraft yourself.

</div>

<div class="callout tip" role="note" markdown="1">

**Tip:** The Airport Lookup lists only five main airport frequencies. For the airport's frequencies as a list you can tune straight into COM 1, use the Surroundings window (Output Ctrl+Shift+L); see [The Surroundings Window](knowing-where-you-are.md#surroundings).

</div>

### The SimBrief tab {#the-simbrief-tab}

This tab has two buttons, **Create SimBrief Plan** and **View Detailed Plan Info**, which open the SimBrief Flight Planner on its Create or View tab. See [SimBrief](#simbrief).

### Worked example: building a departure and an arrival {#worked-example-building-a-departure-and-an-arrival}

Here is a complete example. You are flying from London Heathrow (EGLL) to Amsterdam (EHAM). SimBrief planned you off runway 27R, but the ATIS says Amsterdam is landing on runway 18R and you want to look at your arrival before you go. The procedure names below are only an illustration; your list will show whatever your navigation data contains.

1. Press Output Shift+E. The Flight Planning window opens on the Route Viewer and your SimBrief plan loads: *"SimBrief flight plan loaded"*.
2. Arrow down the Waypoint column to hear the route. Press Right Arrow to hear a waypoint's Region, Airway and so on, then Home to come back to the Waypoint column.
3. Press Ctrl+Tab four times to reach the **Departure** tab. Type EGLL. The runway list fills. Arrow to "Runway 27R", Tab to **Load Departure** and press Space. You hear *"Loaded departure EGLL runway 27R"*.
4. Press Ctrl+Shift+Tab three times to go back to the **SID** tab. Type EGLL. The runway list fills, starting with ALL. Arrow to 27R. Tab to the SID Procedures list and arrow to the SID on your clearance, say CPT5J. Tab to the transitions list and leave it on None. Tab to **Load SID** and press Space: *"Loaded CPT5J RWY 27R"*.
5. Press Ctrl+Tab to reach the **STAR** tab. Type EHAM, choose 18R in the runway list, choose your STAR, leave the transition on None, and press **Load STAR**.
6. Press Ctrl+Tab to reach the **Approach** tab. Type EHAM. Arrow through the Approaches list to "ILS RWY 18R", choose a transition if your STAR needs one to join the approach, and press **Load Approach**: *"Loaded approach ILS RWY 18R"*.
7. Press Ctrl+Tab twice to reach the **Arrival** tab. Type EHAM, choose runway 18R and press **Load Arrival**: *"Loaded arrival EHAM runway 18R"*.
8. Press Ctrl+Tab to reach **Airport Lookup**. Type EHAM, arrow to runway 18R and read the Runway Information box: its length, ILS frequency and course, and lighting. These are the numbers you will set up for the approach.
9. Press Ctrl+Tab until you are back on the **Route Viewer** tab (Ctrl+Tab wraps round after the last tab). Your plan now runs from EGLL 27R through the SID, the en-route waypoints, your STAR and the ILS to EHAM 18R. Press F5 to fill in distances and bearings from where you are parked.

You can now enter the same procedures into your aircraft's FMC or MCDU, and press Describe Route if you want the AI briefing.

### If something goes wrong {#if-something-goes-wrong-8}

- **The lists stay empty after typing the ICAO.** Check the code has four letters and is in your navigation database. If you recently switched simulator or have not built the database, see [The Navigation Database](getting-started.md#nav-database).
- **A message box says "Please select a runway and SID"** (or STAR, or approach). One of the lists has nothing selected; arrow into it and choose an item.
- **The SID or STAR list is empty for my runway.** That runway may have no procedures in your data. Try ALL in the runway list.
- **The distance and bearing columns are missing.** Press F5 or Refresh Position while connected to the simulator.
- **Describe Route is dimmed.** Load a SimBrief plan first. The briefing is built from SimBrief's data, so a plan built only from these tabs cannot be described.

## The AI Route Briefing {#planning-route-briefing}

### What it is {#what-it-is-11}

The **Describe Route** button on the Route Viewer tab asks an AI service (Google's Gemini or Anthropic's Claude, whichever you set up) to write a narrative briefing of your whole flight from your SimBrief plan. It is written for a blind pilot: it describes the geography and terrain you fly over, what a sighted pilot would see out of the window, the SID and STAR, the weather, and the NOTAMs, and it ends with your expected taxi out and taxi in, worked out by BA on your own simulator scenery.

The briefing is in plain text with a heading for each part, about 600 to 900 words, so it reads well with a screen reader.

### Before you start {#before-you-start}

- You need an AI provider and API key set up on the **AI** tab of Settings. See [AI Display Reading and Scene Description](core-concepts.md#ai-display-reading) for how to choose a provider and get a key.
- You need a SimBrief plan loaded in the Flight Planning window.
- For the taxi part, you need your navigation database built. If you are using SayIntentions, BA also uses its assigned runways and arrival gate, when SayIntentions has them for this flight.

### How to use it {#how-to-use-it-6}

1. Open the Flight Planning window (Output Shift+E) and make sure your SimBrief plan is loaded.
2. Tab to **Describe Route** and press Space. BA says *"Generating route description, please wait"*. The status line shows *"Computing taxi routes..."* and then *"Generating route description..."*.
3. When it is done, BA says *"Route description ready"* and moves you to the **Route Description** box. Read it with the arrow keys.

A briefing usually takes from several seconds to about a minute, depending on your AI service and whether web search is on. You can close the Flight Planning window while it is being prepared: the briefing carries on, BA says *"Route description ready"* when it finishes, and it is waiting for you when you reopen the window. If you reopen the window before it finishes, the status line says *"A route description is still being prepared. Describe Route is available again when it finishes."*

The briefing is kept until you load a new SimBrief plan. Closing and reopening the window, or switching the navigation database, does not lose it.

### What is in the briefing {#what-is-in-the-briefing}

The briefing has seven parts:

1. **Flight overview** — origin and destination, distance and flight time, cruise altitude, direction of flight, and the countries or regions you cross.
2. **Departure and SID** — the area around the departure airport, terrain and noise-abatement concerns, what you would see on the climb-out, and the SID you filed with its waypoints and top altitude.
3. **En route** — the cities, mountains, coastlines, borders and other features along the way.
4. **Arrival and STAR** — the same for the destination and your STAR, with its bottom altitude.
5. **Weather** — departure and arrival weather from the METARs in your plan, and any significant weather or SIGMETs along the route.
6. **NOTAMs** — significant notices for both airports, such as closed runways, an ILS out of service or taxiway closures, in plain language. Current NOTAMs can only be found when web search is on (see Settings below).
7. **Taxi out and taxi in** — described next.

### The taxi out and taxi in {#the-taxi-out-and-taxi-in}

For the last part, BA plans both taxi routes itself before it asks the AI, using the same routing that taxi guidance uses, on your own scenery:

- The **taxi out** runs from where you are parked to your departure runway. If BA is connected and your aircraft is standing at the departure airport, it starts from your actual position; otherwise it picks a representative stand for your aircraft size.
- The **taxi in** runs from a landing exit you can comfortably make, off your arrival runway, to your stand. If SayIntentions has assigned you an arrival gate, that is the stand used; otherwise a typical stand for your aircraft is chosen and described as typical, not assigned.

For each leg you hear the taxiways in order, the turns, the distances in your ground distance unit, every hold-short point and every runway you cross. For the arrival you also hear which side to leave the runway, the exit taxiway and its distance from the threshold, and the next exit if you miss it. Each route ends by reminding you that it is the expected route on your scenery and that SayIntentions or ATC will give the actual taxi clearance.

After each route comes a one-sentence check against the real airport. It begins *"Real-world check, from memory rather than live charts:"* or, only when a web search actually found and read the airport's current charts, *"Real-world check, against current charts:"*. It says whether the route matches real-world practice, or names what differs. It may be followed by up to three tips, beginning *"Real-world suggestions, not from your scenery:"*, such as a preferred exit, a size restriction for your aircraft, or a NOTAM affecting your taxi.

When your scenery has no taxiway data for an airport, the route may be planned on OpenStreetMap or X-Plane airport data instead, and the briefing says so: this is for planning only, and taxi guidance cannot use it. When something cannot be planned, the briefing says so plainly, for example a closed runway, a gate your scenery does not have, or a runway with no taxiway at its full-length start.

<div class="callout note" role="note" markdown="1">

**Note:** The taxi part of the briefing is a preview, not a clearance. On the day, follow ATC or SayIntentions, and build the route you are actually given in the taxi planner; see [Taxi Guidance](taxi-guidance.md).

</div>

### Settings {#settings-3}

The AI tab settings are described in [AI Display Reading and Scene Description](core-concepts.md#ai-display-reading). For the briefing, the two web search options (**Enable Grounding with Google Search (paid feature)** for Gemini, **Enable web search for NOTAM route grounding (extra cost)** for Claude) matter most: without them, the NOTAM section cannot be current and every taxi check is from memory.

If you turned on Claude's web search but the Claude model you chose does not support it, BA still writes the briefing, and it begins *"Note: web search is not available for the selected Claude model, so this briefing is not grounded with current NOTAM or weather data."*

The distances in the taxi part follow **Use feet for distances (default: metres)** on the **Taxi Guidance** tab.

### If something goes wrong {#if-something-goes-wrong-9}

- **"AI provider API key not configured. Please configure it in File menu, Settings, AI tab."** Add your key on the AI tab of Settings.
- **"No SimBrief flight plan loaded. Please load a flight plan first."** Press Load SimBrief first.
- **"A route description is already being prepared."** Only one briefing runs at a time. Wait for *"Route description ready"*.
- **"The flight plan or database changed while the route description was prepared. Press Describe Route again."** You loaded a new SimBrief plan, or switched the navigation database, while the briefing was running, so it was thrown away rather than describing the wrong plan. Press Describe Route again.
- **"Error generating route description: …"** The AI service reported a problem, such as no internet connection, a key that is not valid, or a busy service. Check your key and connection and try again.
- **The briefing names a runway different from your SimBrief plan.** SayIntentions assigned a different runway for this flight; the briefing says so and names both.

## Aircraft EFBs {#aircraft-efb}

### What it is {#what-it-is-12}

Most modern airliners in the simulator come with an electronic flight bag: a tablet in the cockpit with pages for performance calculation, weight and balance, ground services, charts, settings and more. On their own these tablets are drawn as pictures inside the simulator and cannot be read by a screen reader. BA opens each aircraft's tablet in an accessible window instead, so you can read it and press its buttons with your screen reader.

The tablet you get depends on the aircraft:

<div class="table-wrap" markdown="1">

| Aircraft | Key | What opens |
|---|---|---|
| FlyByWire A320neo | Input Shift+T | A320 flyPad EFB |
| Headwind A330-900neo | Input Shift+T | A330 flyPad EFB |
| FlyByWire A380X | Input Shift+T | A380X flyPad EFB |
| PMDG 737 and 777 | Input Shift+T, or Input Ctrl+Shift+T for the First Officer's tablet | PMDG 737 EFB or PMDG 777 EFB (with "(First Officer)" for the First Officer's tablet) |
| TFDi MD-11 | Input Shift+T | MD-11 EFB |
| Fenix A320 | Input Shift+T | Fenix EFB |
| iFly 737 MAX8 | Input Shift+T | iFly 737 EFB |
| HorizonSim 787-9 | none | BA says *"787 E F B not available."* |

</div>

<div class="callout note" role="note" markdown="1">

**Note:** On every aircraft, Output Shift+T toggles trim announcements. The EFB is on Input Shift+T, so check which mode you are in.

</div>

The aircraft must be loaded and its tablet working; nothing needs installing in your Community folder. Each tablet's pages are described in [Airbus Aircraft](airbus-aircraft.md) and [Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md).

### The flyPad, PMDG and MD-11 tablets {#the-flypad-pmdg-and-md-11-tablets}

The FlyByWire flyPad, the PMDG tablet and the MD-11 EFB all open in the same kind of window. BA reads the live tablet page and shows it as a simple web page inside the window, so your screen reader treats it exactly like a web page in your browser.

At the top of the window are two status lines: the connection status (for example *"flyPad connected (42 elements)"*) and the current page (*"Page: Ground"*). Below them is the page itself.

The page always follows the tablet. Whatever page is open on the tablet is what you see, and when it changes, your screen reader announces the new page name, for example *"flyPad page: Payload"*. You move around the tablet by pressing its own menu buttons, just as a sighted pilot taps them.

Reading the page:

- Use your screen reader's browse mode (NVDA's browse mode, JAWS's virtual cursor). Arrow down to read line by line.
- The page title is a heading. Use your screen reader's quick keys to jump: H for headings, B for buttons, K for links, E or F for edit fields and other form fields.
- Text that is not a control, such as a value or a weather report, is read as plain lines. Long text such as a SimBrief OFP on the PMDG tablet keeps its layout.

Using controls:

- **Buttons and links:** press Enter or Space. You hear *"Activating"* and the button's name, and the press goes to the real tablet. A control the tablet has greyed out has *", dimmed"* at the end of its name, and pressing it says *"Unavailable"*.
- **Edit fields:** go into the field (your screen reader switches to focus mode), type the value and press Enter. The value is sent to the tablet when you press Enter or leave the field.
- **Drop-down lists, check boxes and sliders:** use them as on any web page. The change goes to the tablet as soon as you make it.
- **Pop-up messages:** when the tablet shows an alert or confirmation, such as a success message after saving, BA reads it out automatically.

Keys in these windows:

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| F5 | Re-read the tablet now |
| F6 | Say the connection status, for example *"flyPad connected. Page Dashboard. 42 elements."* |
| Alt+F4 | Close (hide) the window; it reopens on the same page |

</div>

BA keeps the window up to date by itself, several times a second, and changes values in place so your position on the page is not lost while you read. If the page looks out of date, press F5.

If BA cannot reach the tablet, F6 says, for example, *"flyPad not connected. Make sure the aircraft is loaded and the tablet is powered on."* Wait until the aircraft has fully loaded and try again.

<div class="callout note" role="note" markdown="1">

**Note:** The web view these windows use is Microsoft's free Edge WebView2 Runtime, which comes with Windows 11 and most Windows 10 installations. On a computer where it cannot start, these windows fall back to a plain list of controls: press Tab and Shift+Tab to move between them, and type in a field and press Enter to send it (BA says, for example, *"Set Passengers to 150"*). Installing or repairing the WebView2 Runtime from Microsoft's website restores the normal window.

</div>

### The Fenix and iFly EFBs {#the-fenix-and-ifly-efbs}

The Fenix A320 and the iFly 737 MAX8 serve their tablets as real web sites on your own computer. BA shows the live site in its window, so you read and use it exactly like a web page, with browse mode and quick keys. The iFly site already works with a screen reader; the Fenix v3 site does not on its own, so BA adds the names, states and keys it lacks.

- **Fenix EFB:** opens the Fenix tablet site. Every app is a button you can Tab to, and Alt+Home goes back to the EFB's home screen from anywhere; the details are in [the Fenix chapter's EFB section](airbus-aircraft.md#efb-2). If the Fenix A320 has not finished loading, the window says so instead, in a message you can reach with Tab: *"Fenix EFB is not reachable at http://127.0.0.1:8083. Make sure the Fenix A320 is fully loaded, then press Retry."* Press **Retry** once it has loaded. Escape closes an open menu or pop-up on the page first, then the window.
- **iFly 737 EFB:** opens the iFly SP1 tablet, with its Performance, Navigraph and SimBrief, Payload and Balance, Ground Services, Doors and other apps. BA says *"iFly EFB loaded."* when the page is ready. If the iFly EFB is not running yet, a message explains why and a **Retry** button is focused. Press Escape to close, F5 to reload, and Alt+Home to go back to the EFB's main page.

Both windows hide rather than close, so when you reopen them you are back on the page you left.

## Flows and Checklists {#checklists}

Before you push back, the First Officer (Output Shift+C) works the aircraft's flows and checklists with you. It has its own chapter: [The First Officer and Checklists](first-officer.md).

## Tracking Waypoints {#fix-tracking}

### What it is {#what-it-is-14}

BA has five tracking slots. Put a waypoint in a slot, and from then on one key press tells you how far away it is and which way to fly to reach it. This is useful for a fix ATC asks you to report, the start of a hold, a waypoint you need to cross at a certain altitude, or simply keeping track of your progress towards top of descent.

### Putting a waypoint in a slot {#putting-a-waypoint-in-a-slot}

There are two ways.

From the **Track Fix Window**:

1. Press Input Shift+F. The Track Fix Window opens with the focus in **Waypoint Name**.
2. Type the waypoint's name, for example DET.
3. Tab to **Tracking Slot** and choose Slot 1 to Slot 5.
4. Tab to **Track** and press Space.

If there is only one waypoint with that name, BA says *"Waypoint DET tracked in slot 1"* and the window closes. Many waypoint names are used more than once around the world. If there are several, BA says, for example, *"3 waypoints found for DET, select one"*, and shows a list titled *"Multiple waypoints found. Select one:"*. Each entry gives the name, region and distance from you, for example "DET - Region EG - 23.4 NM", with the nearest first. Arrow to the right one and press Enter (or press **Select**).

If the name is not in the database, BA says *"Waypoint DET not found"*. Press Escape to close the window without tracking anything. The window searches the navigation database's named waypoints; to track a waypoint on your route, the Route Viewer method below is often easier.

From the **Route Viewer** in the Flight Planning window: arrow to the waypoint, press the Applications key (or Shift+F10), and choose a Track Slot.

### Hearing a tracked waypoint {#hearing-a-tracked-waypoint}

Press Output 1, 2, 3, 4 or 5. BA reads the waypoint, its distance and its magnetic bearing from your aircraft right now, for example *"DET, 23 nautical miles, 120 degrees"*. If the slot is empty, it says *"Track slot 2 empty"*. Putting a new waypoint in a slot replaces the old one.

<div class="callout tip" role="note" markdown="1">

**Tip:** The bearing is the magnetic heading that would take you straight to the waypoint with no wind. Compare it with your heading (Output H) to tell whether the waypoint is ahead, to the left or to the right.

</div>

## NAV Radios {#planning-nav-radios}

### Reading the NAV radios (Output N) {#reading-the-nav-radios-output-n}

Press Output N to hear both NAV radios. For each one BA reads the frequency and, if it is receiving a station, its ident and name, the localizer course if it is an ILS, the glideslope angle, and the DME distance, for example *"Nav 1: 110.90, localizer course 271, glideslope 3.0 degrees, DME 5.2 nautical miles. Nav 2: 113.10, no signal"*. This is a quick way to confirm you have tuned the right ILS before the approach. Some aircraft read their radios in their own way; see your aircraft's chapter.

### Setting the NAV radios (Input Ctrl+N) {#setting-the-nav-radios-input-ctrl-n}

On the **PMDG 737** and the **iFly 737 MAX8**, Input Ctrl+N opens the **NAV Radios** window. It has four fields, already filled with the current settings:

- **NAV 1 frequency (MHz)** and **NAV 2 frequency (MHz)**, from 108.00 to 117.95.
- **NAV 1 course (degrees)** and **NAV 2 course (degrees)**, from 0 to 359.

Change what you need and press Enter in any of the four fields to apply all four and close the window; unchanged values are simply set again. Press Escape, or the **Cancel** button, to close it without changing anything. You can type a frequency as 110.30, 110.3 or 11030, with a dot or a comma, and BA rounds it to the nearest valid channel. If a value is out of range, BA tells you and puts you back in that field, for example *"NAV 1 frequency must be between 108.00 and 117.95 megahertz"*.

On the iFly, BA keys each frequency on the NAV control panel keypad, transfers it to active and sets the MCP course; read the result back with Output N. On other aircraft, tune the radios from their panels, or from the aircraft's own radio windows described in [Airbus Aircraft](airbus-aircraft.md) and [Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md).

## FMC Settings {#planning-fmc-settings}

### What it is {#what-it-is-15}

**File**, **FMC Settings** holds two settings for BA's FMC, CDU and MCDU windows. The menu item appears only while a PMDG aircraft, the Fenix A320 or the HorizonSim 787 is loaded.

- **Alternate line select keys (F1-F6 / F7-F12)** — by default, in an FMC window, Ctrl+1 to Ctrl+6 press the left line select keys and Alt+1 to Alt+6 press the right ones. Tick this to use F1 to F6 for the left keys and F7 to F12 for the right keys instead, which frees Ctrl and Alt for other shortcuts. The same choice is used by the FMC, CDU and MCDU windows of the other aircraft too.
- **Enhanced distance announcements (PMDG only)** — when ticked, Output D and Output Shift+D on the PMDG 777 read the FMC progress page instead of the default readout. Despite the name, it does not change the PMDG 737's readouts. Output D gives distance to destination with the estimated time of arrival in Zulu time and the landing fuel; Output Shift+D gives top of climb, step climb or top of descent in nautical miles with its time. The progress page is read from the right CDU, which must be powered. If the page cannot be read, BA falls back to the default readout. This has no effect on other aircraft.

Press OK to save. BA says *"FMC settings saved"*, and the change takes effect at once.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: The First Officer and Checklists](first-officer.md)
- [Next chapter: Weather](weather.md)
- [Guide contents](index.md)

</nav>
