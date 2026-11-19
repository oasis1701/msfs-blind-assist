---
title: "Networks and Integrations"
---

<div class="guide-part" markdown="1">

Chapter 1 of 1 in [Part 7: Networks and Integrations](index.md#part-6-networks-and-integrations) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [SayIntentions](#sayintentions)
- [VATSIM](#vatsim)
- [Volanta](#volanta)
- [ActiveSky](#integrations-activesky)
- [AI Providers](#integrations-ai)

</nav>

MSFS Blind Assist works alongside several other programs that many pilots already use: SayIntentions for AI air traffic control, vPilot for flying on the VATSIM network, Volanta for flight tracking, ActiveSky for weather, and an AI provider (Google Gemini or Anthropic Claude) for reading displays and describing routes. None of them is required. Each one simply adds something when it is there.

<div class="table-wrap" markdown="1">

| Program | What BA does with it | Key |
|---|---|---|
| SayIntentions | Reads the last ATC call, shows your gate and runways, turns a taxi clearance into a taxi route | Output Ctrl+S, Output Ctrl+Shift+S, Input Ctrl+Shift+Y |
| vPilot (VATSIM) | Speaks connections, private messages, radio messages and SELCAL alerts | Output Alt+V to mute |
| Volanta | Shows Volanta's live map as text: traffic, friends, online ATC | Output Shift+O |
| ActiveSky | Richer weather in the weather windows and announcements | (see the Weather chapter) |
| Gemini or Claude | AI display reading, scene description, route briefing | (see Core Concepts) |

</div>

## SayIntentions {#sayintentions}

### What it is {#si-what}

SayIntentions is an AI air traffic control service. You talk to it, and it talks back like a real controller. BA reads the active SayIntentions flight on your computer so that you can:

- hear the last thing the controller said again, whenever you like;
- look up your assigned arrival gate, the runways in use and the altimeter setting;
- turn a spoken taxi clearance into a ready-made route in the taxi planner, without typing the taxiways in by hand.

There is no SayIntentions settings tab and no API key to enter. SayIntentions writes a small flight file on your computer while you are connected to a flight, and BA finds everything it needs in that file, including the key it uses to ask SayIntentions for the recent radio calls. If SayIntentions is not running, BA tells you so.

### The keys {#si-keys}

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| Output Ctrl+S | Read the last SayIntentions ATC transmission |
| Output Ctrl+Shift+S | Open the SayIntentions Flight Information window |
| Input Ctrl+Shift+Y | Build a taxi route from the current SayIntentions taxi clearance |

</div>

<div class="callout note" role="note" markdown="1">

**Note:** Ctrl+S means different things in the two modes. In Output mode it reads the last SayIntentions transmission. In Input mode it opens the aircraft's speed window. Make sure you are in the mode you want.

</div>

The two readouts, Output Ctrl+S and Output Ctrl+Shift+S, work even when BA is not connected to the simulator, because they only read the SayIntentions flight. Building a taxi route needs your aircraft's position, so the simulator must be connected, and SayIntentions must be reachable over the internet.

### Hearing the last transmission {#si-last-transmission}

Press Output Ctrl+S to hear the most recent call from the controller. This is handy when a long clearance went past too fast, or you were busy with a checklist.

Two kinds of message are left out on purpose:

- **Cabin and crew chatter.** SayIntentions mixes cabin announcements and crew intercom into the same stream as ATC. BA filters these out, so pressing Ctrl+S during taxi gives you the ground controller, not the purser. A real ATC instruction that happens to contain a cabin-sounding word, such as *"Hold position, passenger aircraft crossing left to right"*, is still recognised as ATC.
- **Your own transmissions.** Your readback is usually the newest thing on the frequency the moment you press the key, so BA never reads your own words back to you.

What you might hear instead of a transmission:

- *"No ATC transmission yet. Only your own calls so far."* — you have spoken on the frequency, but the controller has not replied yet. There is no need to keep pressing.
- *"SayIntentions flight.json not found. Start an active SayIntentions flight."* — SayIntentions is not connected to a flight.
- *"No SayIntentions transmission found. Check SayIntentions is connected to this flight."* — the flight is there but nothing has been said yet.

### The Flight Information window {#si-flight-info}

Press Output Ctrl+Shift+S to open the **SayIntentions Flight Information** window. It is a read-only window built for looking things up quickly, not a block of text to read from the top.

How to use it:

1. Press Output Ctrl+Shift+S. The window opens with the first section selected, and your screen reader reads the section name, its first item and how many items it has.
2. Press Tab to move from one section to the next.
3. Use the Up and Down arrows to move within a section. Typing a letter jumps to the next item starting with that letter.
4. Press Escape to close the window and go back to the simulator.

The sections, each shown only when there is something in it:

- **Flight** — the current airport, origin, destination, aircraft type, callsign and filed route. While you are in the cruise, SayIntentions often reports the en-route control centre instead of an airport. BA labels that line **Controlling facility** rather than calling it an airport.
- **Gate and runway** — your assigned arrival gate, the departure runway, and the runway you are cleared to land on (or the arrival runway).
- **Airport sections** — one for each airport, headed with the airport's code, for example "EDDF airport". Each lists the landing runways, departing runways, preferred runway, runway flow and the altimeter.

Some details worth knowing:

- **The gate is always your arrival gate.** SayIntentions does not assign departure gates, so the gate line always means the stand you will park at, at your destination. Until SayIntentions assigns one, you see *"Assigned arrival gate: none assigned yet"*.
- **At your destination, BA checks where you are parked.** If you are within about 100 metres of a stand, an extra line tells you whether it is your assigned gate, for example *"Aircraft appears near assigned gate A24."* or *"Aircraft appears near B 12, not assigned gate A24."* This catches a wrong arrival position.
- **The departure runway disappears once you are airborne**, and again at your destination, because by then it is old ground information.
- **The airport you need comes first.** On the ground at your departure airport, its section leads. Everywhere else, including in the air, your destination leads.
- **The altimeter is given in both units**, for example *"Altimeter: 30.12 inches (1020 hPa)"*, so you never have to convert in your head.

The window deliberately leaves out the ATIS text, the METAR, the TAF, the wind and the visibility. You already get those by listening to the ATIS, or from the METAR window (Output Shift+M; see [Weather](weather.md)). The window keeps only the runway picture and the altimeter, the things worth having on hand without sitting through the ATIS again.

If SayIntentions is not running, BA speaks the reason instead of opening an empty window.

### Building a taxi route from a clearance {#si-taxi-import}

When the ground controller gives you a taxi clearance, BA can read it, work out the route over the airport's real taxiways, and fill in the [taxi planner](taxi-guidance.md#taxi-planner) for you. You then check it and start guidance as usual.

### How to use it {#si-taxi-steps}

1. Request taxi from SayIntentions and listen to the clearance as normal. Read it back if the controller expects you to.
2. Press Input Ctrl+Shift+Y. You hear *"Reading SayIntentions taxi clearance."*
3. Wait a few seconds. BA reads the recent radio calls, your position and the airport's taxiway names. The taxi planner opens, filled in with the destination, the taxiways in order, and any hold-short points the controller gave.
4. Listen to the spoken summary. It ends with *"Review the fields, then press Calculate Route to start guidance."*
5. Check the planner's fields against what you heard: the destination, the taxiway list, and the hold-shorts.
6. Press **Calculate Route** (Alt+C) to start guidance.

You can also start the import from inside the taxi planner. The **Fill from SayIntentions** button (Alt+Y), just above Calculate Route, does exactly what Input Ctrl+Shift+Y does.

<div class="callout warning" role="note" markdown="1">

**Warning:** Always review an imported route before you roll. BA works hard to get it right, and it tells you about every part it could not apply, but it is reading a spoken clearance and matching it to scenery data that is sometimes incomplete. You are still the pilot. If the route does not match what you heard, fix the fields by hand before calculating.

</div>

### What it fills in {#si-taxi-fills}

- **The destination** — the runway or gate the controller cleared you to. A runway you are told to hold short of, or to cross, is never taken as your destination.
- **The taxiways** — the cleared taxiways, in the order given. A taxiway used twice in one clearance, for example either side of a runway crossing, gets a row each time.
- **The hold-shorts** — each hold-short is tied to the taxiway it comes after in the clearance, so the stop is placed at the right crossing.

An import replaces the whole route, including anything you had already set up by hand, such as an intersection departure or a CAT III hold. The clearance is the route.

### Where the taxiway list comes from {#si-taxi-source}

SayIntentions publishes its own ground track for your taxi: the path over the pavement it expects you to follow. BA can use that track or the words of the clearance, and it chooses between them each time you press the key.

- **When the track agrees with the clearance** (every cleared taxiway is on it, in the order you were given them), BA takes the taxiway list from the track. This catches legs that are hard to pick out of speech. For example, a controller saying "North" for a taxiway called N. You hear *"Route from SayIntentions ground track."*
- **When the two disagree, the clearance wins.** You hear *"SayIntentions ground track differs from the clearance. Using the clearance."* What you heard on the frequency is what gets built.
- **When there is no clearance at all** (you pressed before asking for taxi, or the radio calls could not be read), BA uses the track because it is the only thing available, and warns you: *"Route from SayIntentions ground track. No cleared taxiways to check it against, so this is SayIntentions' own plan, not ATC's."* Treat that route as a suggestion, not a clearance.

Whichever source is used, the destination and the hold-shorts always come from the clearance. The ground track carries neither.

### What the summary tells you {#si-taxi-summary}

The summary is spoken in a fixed order: the destination first, then where the route came from, then every warning, and only then the route itself. Warnings come early on purpose. If you start guidance straight away, the first turn instruction cuts off whatever is left of the summary, so the things you need to act on come first.

These are the lines you may hear, in order:

- *"SayIntentions route to Gate A9."* — the lead, naming the destination that was set.
- *"SayIntentions route. Destination Gate A9 not set. Check the destination field."* — BA could not set the destination. Pick it yourself in the planner.
- *"SayIntentions assigned South Terminal Gate A24, which this scenery lists under another name."* — the stand is here, but this scenery spells it differently. BA found it anyway.
- *"SayIntentions assigned Gate B06, which this airport does not have. This is the nearest stand to the assigned position."* — no stand here matches that name, so BA chose the stand nearest the position SayIntentions published. Check it is the one you want.
- *"Route from SayIntentions ground track."* or *"SayIntentions ground track differs from the clearance. Using the clearance."* — where the taxiway list came from, as described above.
- *"No cleared taxiways to check it against, so this is SayIntentions' own plan, not ATC's."* — no clearance was found to check the track against.
- *"No taxi clearance on the SayIntentions frequency yet."* or *"SayIntentions comms history timed out."* — why no clearance was found. Usually you have not requested taxi yet.
- *"Could not apply K."* — a cleared taxiway that is not in the route, either because this airport does not have it or because the planner could not use it. The rest of the route is still built. On a route from the clearance, every missing taxiway is named, because each one is a word you heard. On a route from the ground track, only the first three are named and the rest are counted, for example *"Could not apply F, G, R and 1 more."*
- *"12 of 40 ground track points were off the taxiways, so the route may be incomplete."* — much of the published track did not match this airport's taxiways. Check the route carefully.
- *"Could not set hold short of runway 22."* — a hold-short BA could not place. **Treat it as still in force: guidance will not stop you there**, so you must stop yourself.
- *"Via A, B, C."* — the route that was built. A long route from the ground track is shortened in speech, for example *"Via R7, E7, E6 and 9 more."* The whole list is always in the planner's route summary box.
- *"Hold short of runway 15R after N."* — a hold-short that was set, one line for each.
- *"Review the fields, then press Calculate Route to start guidance."* — or *"Guidance started."* if you have turned on auto-start (see Settings below).

If BA cannot find any taxiways it can use, you hear *"No taxiways from the clearance matched this airport. Using shortest path."* (or the same about the ground track). Check that route very carefully against the clearance.

If you press Input Ctrl+Shift+Y again while an import is still running, you hear *"SayIntentions taxi route already being built."* Wait for the first one to finish.

### A worked example {#si-taxi-example}

You are parked at your departure gate. SayIntentions Ground says: "Taxi to runway 27 via A, B, hold short of runway 22."

1. You read back the clearance.
2. You press Input Ctrl+Shift+Y. You hear *"Reading SayIntentions taxi clearance."*
3. The taxi planner opens and BA says something like: *"SayIntentions route to Runway 27. Route from SayIntentions ground track. Via A, B. Hold short of runway 22 after B. Review the fields, then press Calculate Route to start guidance."*
4. You Tab through the planner: destination Runway 27, taxiways A then B, a hold short of runway 22 after taxiway B. It matches what you heard.
5. You press Alt+C to calculate the route and start guidance.

### Arriving at your gate {#si-taxi-arrival}

After landing, SayIntentions usually has an arrival gate assigned. On an arrival with an assigned gate, the import only ever routes you to a gate, never to a runway, even if the controller mentioned the runway you just vacated.

If this scenery genuinely has no stand that matches the gate SayIntentions assigned, BA says so by name and stops, rather than guessing. You hear something like *"SayIntentions assigned Gate A2, but no matching stand was found at KSTL. Pick the destination gate in Taxi Assist, then Calculate."* Open the destination list in the taxi planner and search for the gate yourself. The planner's own search can often find a stand under a slightly different name.

BA also finds the gate even when the scenery says your aircraft is too big for it. Sceneries often get stand sizes wrong, so a gate the controller actually cleared you to is not hidden just because the size data disagrees.

### Settings {#si-settings}

The one SayIntentions setting lives on the **Taxi Guidance** tab of Settings, under the SayIntentions heading at the bottom:

- **SayIntentions import starts taxi guidance immediately** — off by default. When ticked, the import calculates the route and starts guidance straight away, and the summary ends with *"Guidance started."* Leave it off while you are learning, so you always get the chance to review the route first.

### If something goes wrong {#si-troubleshooting}

- **"SayIntentions flight.json not found."** SayIntentions only writes its flight file while you are connected to a flight. Start or reconnect your SayIntentions flight.
- **The import says there is no taxi clearance.** You may have pressed before asking for taxi, or the controller's last message was something else. BA looks back through recent calls for the newest taxi clearance, so a later "hold position" or traffic advisory does not hide it.
- **A taxiway you heard is missing.** Listen for the "Could not apply" line. Add the taxiway by hand in the planner if the airport has it under another name.
- **The destination is wrong or missing.** Set it yourself in the planner's destination field before pressing Calculate Route.
- **Something still seems wrong.** BA keeps a record of every import in the file sayintentions.log, in your logs folder (see [Logs and Getting Help](getting-started.md#logs-and-help)). It shows what BA read, which source it used and what it could not apply. Your SayIntentions key is never written to the log.

<div class="callout tip" role="note" markdown="1">

**Tip:** SayIntentions can also act as the datalink provider for the FlyByWire aircraft's DCDU. That is set up in the aircraft, not in BA. See [Airbus Aircraft](airbus-aircraft.md).

</div>

## VATSIM {#vatsim}

### What it is {#vatsim-what}

VATSIM is a free online network where pilots fly together with real people acting as air traffic controllers. You connect with a program called vPilot. vPilot shows private messages, radio text and SELCAL alerts on screen, which is hard to follow without sight.

BA can speak those events for you through your screen reader, using a small plug-in it installs into vPilot for you. This replaces the old separate "vPilot to TTS" program, so you only need BA.

BA also uses VATSIM's public data in other places, whether or not you fly on VATSIM yourself:

- The METAR window (Output Shift+M) fetches its reports from VATSIM's weather service. See [Weather](weather.md).
- The wind readout uses a VATSIM METAR for the destination wind.
- When network traffic is shown in the simulator, the TCAS window and ground traffic callouts look up its aircraft type and route from VATSIM's live data, so you hear a real type instead of a placeholder. See [TCAS Traffic](takeoff-climb-and-cruise.md#tcas) and [Ground Traffic](ground-traffic.md).

### What you will hear {#vatsim-hear}

Once set up, BA speaks five kinds of vPilot event:

<div class="table-wrap" markdown="1">

| Event | What you hear |
|---|---|
| Connected to the network | *"Connected as"* followed by your callsign |
| Disconnected from the network | *"Disconnected from network"* |
| Private message | *"Private message from"* the sender, then the message |
| Radio message on a frequency you are tuned to | The sender, then the message |
| SELCAL alert | *"SELCAL alert from"* the sender |

</div>

These announcements wait their turn behind other speech. VATSIM chatter never interrupts something more urgent, such as a landing callout or a taxi instruction. On a very busy frequency BA may skip a message rather than let a backlog delay a more important warning.

BA only listens. It cannot send messages, tune frequencies or squawk on VATSIM; you still do those in vPilot.

### Setting it up {#vatsim-setup}

1. Install vPilot if you have not already.
2. In BA, open **Settings** from the File menu and go to the **VATSIM** tab (the third tab, after Weather).
3. Tick **Announce VATSIM events from vPilot**.
4. Underneath are five checkboxes, one for each event: **Connected to the network**, **Disconnected from the network**, **Private messages**, **Radio messages (on-frequency)** and **SELCAL alerts**. All five are ticked by default. Untick any you do not want spoken.
5. Press **OK**. BA installs its plug-in into vPilot and tells you what happened as part of the *"Settings saved"* confirmation.
6. **Restart vPilot.** vPilot only loads plug-ins when it starts, so you must close and reopen it once after the first install.

vPilot does not need to be running for the install to work. BA also checks the plug-in every time it starts, so an update to BA never leaves an old plug-in behind. That check is silent unless something needs your attention.

### The status box {#vatsim-status}

The VATSIM tab has a read-only **vPilot status** box you can Tab to and read with your screen reader. It always tells you what to do next. You may read:

- *"VATSIM announcements are turned off."* — the main checkbox is unticked.
- *"vPilot was not found. Install vPilot, then re-open these settings."*
- *"The plugin is not installed. Press OK to install it."*
- *"An older plugin is installed. Press OK to update it."*
- *"The plugin is installed and up to date."*
- *"vPilot is connected."* — everything is working.
- *"vPilot is not connected. Start vPilot, or restart vPilot if you have just installed the plugin."*
- *"Announcements are muted for this session. Press \] then Alt+V to unmute."*

The box refreshes when you open the tab and when you tick or untick the main checkbox.

### Muting the chatter {#vatsim-mute}

On a busy frequency you may want quiet for a while. Press Output Alt+V to mute or unmute VATSIM announcements without opening Settings.

- *"VATSIM announcements muted"* / *"VATSIM announcements unmuted"*.
- *"VATSIM announcements are turned off in Settings."* — the main checkbox is off, so there is nothing to mute.

The mute lasts until you close BA. Every time you start BA, VATSIM announcements are unmuted again, so a mute from your last flight is never carried into the next one by accident. Alt+V works even before BA has connected to the simulator.

### If you used vPilot to TTS before {#vatsim-migrating}

If you used the old "vPilot to TTS" program:

- Its vPilot plug-in is removed automatically when BA installs its own, so you never hear the same event twice. You hear once: *"Removed the old vPilot to TTS plugin. MSFS Blind Assist now handles VATSIM announcements."*
- The old tray program itself is not removed. Uninstall it yourself once you have switched.
- Its settings are not copied across. BA's defaults already match what it did (every event on, not muted).

### If something goes wrong {#vatsim-troubleshooting}

- **You hear "vPilot plugin installed. Restart vPilot to load it."** That is normal after the first install. Close and reopen vPilot.
- **You hear "vPilot is running with an older plugin. Close vPilot and re-open Settings to update it."** (or, at start-up, "...Close vPilot and restart MSFS Blind Assist to update it."). vPilot is holding the old plug-in open. Close vPilot, then do what the message says.
- **You hear "The vPilot plugin could not be installed. See the log for details."** Something stopped the copy, such as folder permissions. Send your logs when asking for help (see [Logs and Getting Help](getting-started.md#logs-and-help)); the plug-in keeps its own log, vpilot-plugin.log, in the same folder.
- **Nothing is spoken.** Open the VATSIM tab and read the status box. Check you have not muted announcements with Alt+V, and that the event you expected is ticked.

Turning VATSIM announcements off stops them at once, but leaves the plug-in in vPilot. That is deliberate and harmless; with BA not listening, the plug-in does nothing.

## Volanta {#volanta}

### What it is {#volanta-what}

Volanta is a flight tracker. It logs your flights and shows a live map of other Volanta users and online network pilots. The map is visual, so BA reads it for you and turns it into lists: the aircraft around you, your Volanta friends, and the online air traffic controllers.

BA reads Volanta's public web map by itself, in a hidden browser, so the Volanta app does not need to be open. It can stay in the notification area logging your flights.

### When to use it {#volanta-when}

- To know who else is flying near you, on Volanta or on VATSIM, IVAO, PilotEdge or APOC.
- To find out where your friends are and what they are doing.
- To check which air traffic control is online at an airport before or during a network flight.

### Opening the window {#volanta-open}

Press Output Shift+O, or choose **Volanta** from the **Extras** menu. The **Volanta Live Map** window opens. It works even when BA is not connected to the simulator.

The window has four tabs: **Traffic**, **Friends**, **ATC** and **Connection**. On each list tab the order of controls is: the list first, then the details for the selected item, then (on Traffic and Friends) the major-city checkbox, then the status line and the filters.

You need an internet connection and the Microsoft Edge WebView2 runtime, which is part of Windows 11. When the window first connects you hear *"Connected to Volanta's live map."* If the connection drops you hear *"Lost the connection to Volanta's live map."*

### The Traffic tab {#volanta-traffic}

Every aircraft on Volanta's map around you, nearest first, one line each. A line gives the callsign, the clock position and distance from you, how far above or below you it is (or "on the ground"), whether it is climbing or descending, its speed, its type, who is flying it, its route, and where it is. For example the location might be *"Location: Pike Lake, Wisconsin, United States"*, or *"Location: over Lake Michigan"* over water.

Below the list, the details box shows everything known about the selected aircraft, one item per line.

The filters:

- **Distance** — 10, 25, 50, 100 (the default), 200, 500 or 1,000 nautical miles, then a warning item, then **All pilots across Volanta**. That last choice lists about 3,000 aircraft, which is a lot to arrow through.
- **Show** — **All aircraft**, **Airborne only** or **On the ground only**.
- **Sources** — checkboxes for **Volanta users**, **VATSIM**, **IVAO**, **PilotEdge**, **APOC** and **Other networks**.
- **Search** — type a callsign, flight number, user name, aircraft type, registration or airport. A search covers every distance, whatever the Distance box says.

**Report location to nearest major city** changes each location to how far and which way the aircraft is from the nearest major city, for example *"92 miles west of Wichita, Kansas, United States"*. A "major city" uses your own Minimum Population for Major Cities and Major Cities Range from the GeoNames tab of Settings (see [Location Info and Nearest City](core-concepts.md#location-info)). The checkbox starts unticked every time you open the window.

### The Friends tab {#volanta-friends}

Once you are logged in to Volanta, this tab lists your Volanta friends. Friends who are flying come first, nearest first, placed like a traffic line. Then friends who are online in Volanta, with their status in words, for example *"cruising over United States"* or *"taxiing to gate B12 at KGRB"*. Offline friends come last. It has its own **Report location to nearest major city** checkbox, which works on its own, separately from the Traffic tab's.

### The ATC tab {#volanta-atc}

Online VATSIM and IVAO controllers, nearest first, with their frequency. The details box adds the controller's name, the airport, when they came online and the ATIS text.

Its distance filter runs from 10 nautical miles to **Any distance**, and you can tick **VATSIM** and **IVAO** separately. The search finds an airport's controllers wherever they are: type an airport code, callsign, controller name or frequency. For US airports, "KATL" also finds stations VATSIM names without the K, such as ATL\_TWR.

### The Connection tab and logging in {#volanta-login}

The Connection tab shows whether the web map is running, whether you are logged in, what it downloaded in the last minute, and what has been received. It also has a short **About the Volanta live map** text, and one button.

Logging in is optional. Without it you get the traffic and ATC, but no friends list, and your own flight may appear in the traffic list. To log in:

1. Go to the Connection tab and press **Log in to Volanta**.
2. Volanta's own login page opens in a window of its own. Log in there.
3. The login window closes by itself once you are logged in, and you hear *"Logged in to Volanta."* (Escape closes it sooner.) Your login is kept for next time.

The same button then reads **Log out of Volanta**. Pressing it logs you out and you hear *"Logged out of Volanta."*

### How the lists update {#volanta-updates}

- The tab you are on updates by itself every 5 seconds. If the line you are on changes, it is read again.
- While you are in a list, its order holds still, so the Down arrow always reaches the next aircraft. New aircraft join at the end.
- Press **F5**, or the **Re-sort now** button, to re-sort at once. BA reads the tab's status line, for example how many aircraft are shown.
- If Volanta's data stops arriving, a line at the top of the list says why. The last aircraft received stay below it.

<div class="callout note" role="note" markdown="1">

**Note:** The map only downloads while the Volanta window is in front. Five seconds after you switch to another window it pauses, and it catches up within a few seconds when you come back. While it runs it uses roughly 415 megabytes of internet data an hour, so keep it closed when you do not need it if your data is limited.

</div>

### If something goes wrong {#volanta-troubleshooting}

- **The lists stay empty.** Check the status line at the top of the list and the Connection tab; they say whether the map is loading, unable to start, or not receiving anything. Check your internet connection.
- **You hear "Volanta's login page cannot open: the web map is not running. The status above says why."** Read the Connection tab's status for the reason.
- **Your own flight shows as traffic.** Log in to Volanta; once logged in, BA recognises your flight and leaves it out.
- **Positions look a little old.** Volanta's positions can be a minute or more out of date. BA moves airborne traffic forward along its heading and speed for up to three minutes to make up for this, but treat distances as approximate. This is not a collision-avoidance tool; for traffic near you in the air, use [TCAS Traffic](takeoff-climb-and-cruise.md#tcas).

## ActiveSky {#integrations-activesky}

If you use the ActiveSky weather add-on, tick **Enable ActiveSky integration** on the **Weather** tab of Settings (off by default) and BA reads richer weather from it. Everything about it is in [ActiveSky](weather.md#activesky) in the Weather chapter.

## AI Providers {#integrations-ai}

BA can use Google Gemini or Anthropic Claude, with your own key, to read cockpit displays aloud, describe the scene outside, and write a route briefing. BA always uses exactly the provider and model you chose. Setting it up on the **AI** tab is described in [AI Display Reading and Scene Description](core-concepts.md#ai-display-reading); for the briefing, see [The AI Route Briefing](planning-your-flight.md#planning-route-briefing).

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Approach and Landing](approach-and-landing.md)
- [Next chapter: Airbus Aircraft](airbus-aircraft.md)
- [Guide contents](index.md)

</nav>
