---
title: "Gates, GSX and Docking"
---

<div class="guide-part" markdown="1">

Chapter 4 of 4 in [Part 4: On the Ground](index.md#part-4-on-the-ground) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Choosing a Stand](#gsx-choosing-a-stand)
- [The Access GSX Window](#gsx-access-window)
- [GSX Settings](#gsx-settings)
- [What GSX Tells You](#gsx-announcements)
- [Letting BA Prepare Your Stand in GSX](#gsx-auto-select)
- [Docking Guidance](#docking)

</nav>

This chapter is about the stand at each end of the flight: choosing one, working with **GSX Ground Services Pro** (the add-on that brings jet bridges, buses, fuel trucks, pushback and a marshaller to the ramp), and docking guidance, BA's audio version of the parking guidance display.

You do not need GSX to use stands or docking guidance. Without GSX, BA still lists every stand in your scenery and still docks you onto it. GSX adds the ground services, a more precise stop position at many gates, and the confirmation *"GSX docking complete."* at the end.

<div class="callout note" role="note" markdown="1">

**Note:** The GSX parts of BA need **GSX 4.0.8 or newer**. If you have an older GSX, run FSDT Live Update (Virtuali's own updater) to install the latest version.

</div>

## Choosing a Stand {#gsx-choosing-a-stand}

### What it is {#what-it-is-20}

In the [taxi planner](taxi-guidance.md#taxi-planner) (Input Shift+Y), set **Destination type** to **Gate / Parking** and the **Destination** list fills with the airport's stands. This section explains that list and the controls beside it.

### How the stands are listed {#how-the-stands-are-listed}

Each stand is one item in the list. A typical item reads like this:

*"A 12 - Gate Medium (Jetway) \[SafeDock\]"*

In order, that is:

- **The stand's name** — here "A 12". BA uses one naming rule everywhere, so the taxi planner, the gate teleport list, Where Am I and the SayIntentions gate check all call a stand by the same name.
- **The stand type** after the dash — for example Gate Small, Gate Medium, Gate Heavy, Ramp GA or Ramp Cargo.
- **The terminal**, only when two stands at the same airport would otherwise sound identical. At a big airport such as New York JFK, "Gate 2" exists at more than one terminal, and the terminal name is what tells them apart. It comes right after the type so you hear it early.
- **(Jetway)** if the stand has a jet bridge.
- **The parking guidance system** in square brackets, when the scenery or GSX says which one is installed: SafeDock, APIS, AGNIS, Marshaller and so on.
- **Online names**, at the end, when the real-world name differs from your scenery's. For example *"B 6 - Gate Medium, also B04 (online)"* means your scenery calls it B 6 but real-world charts and ATC call it B04. These come from OpenStreetMap and the X-Plane Scenery Gateway; your scenery's own name always stays first.
- **(no taxi route)** at the very end means the stand exists but BA cannot find a taxiway that reaches it. It stays in the list so you know it is there, but you cannot be guided to it.

When BA knows where your aircraft is, the list is sorted **nearest first**, measured in a straight line, so the stands you are most likely to want are at the top. Before BA has your position, the list is sorted by type (small gates first, then larger gates, then ramps) and then by number.

### Finding a stand quickly {#finding-a-stand-quickly}

Use the **Gate search** box to type part of a stand's name. Type a letter to narrow the list to one concourse ("C"), a number ("18"), or a full name ("C18"). The search also matches online names, so you can type exactly what ATC said — "B04" finds the stand your scenery calls B 6. Then Tab to the Destination list and arrow through what is left.

### Show fitting only {#show-fitting-only}

The **Show fitting only** checkbox hides stands that are too small for your aircraft's wingspan. GSX stands are judged against the maximum wingspan GSX publishes for them; other stands against the size your scenery records. A stand with no size information at all is never hidden.

Scenery stand sizes are sometimes wrong. If a stand you were cleared to is missing, untick Show fitting only and look again. BA does this for you automatically when SayIntentions assigns you a gate: the assigned gate is found even if the scenery claims your aircraft is too big for it.

### Hide occupied stands {#hide-occupied-stands}

The **Hide occupied stands** checkbox (ticked by default) leaves out stands that already have an aircraft parked on them. When it hides any, the planner's status line says how many, for example *"84 listed, 6 hidden (occupied by traffic)."* This is shown on the status line only, not spoken, so it never talks over your screen reader while you type or arrow. Untick the box to see every stand again.

An arrival gate assigned to you by SayIntentions is always offered, occupied or not.

If you choose a stand that does have an aircraft on it, you are warned when you press Calculate Route: *"Warning: an aircraft is at the destination gate."* — or the other aircraft's callsign instead of "an aircraft" when BA knows it.

### Stand names, GSX and World of Jetways {#stand-names-gsx-and-world-of-jetways}

Your stand names come from your navigation database, which BA builds from your installed scenery (see [The Navigation Database](getting-started.md#nav-database)). When GSX is running and has a profile for the airport, BA also reads GSX's own list of stands, with their exact stop positions, sizes and jet bridges. At an airport where the GSX profile covers only some of the stands, you still see every stand — the GSX information is added to the stands it covers, and the rest keep their real heading, size and jet bridge from your scenery.

<div class="callout note" role="note" markdown="1">

**Note:** If you use GSX World of Jetways with **MSFS 2020**, older BA databases could replace an add-on airport's stands with default ones, so a gate the sim and ATC call "Gate 20" was announced as "G 6". This is fixed, but an existing database keeps its old names until you rebuild it. Rebuild your database from the Nav Database window (File menu) to pick up the fix.

</div>

Online stand names are controlled by the online names setting described in [Online names](knowing-where-you-are.md#where-names-online).

## The Access GSX Window {#gsx-access-window}

### What it is {#what-it-is-21}

GSX normally works through a menu drawn inside the simulator, which a screen reader cannot see. The Access GSX window shows that same menu, GSX's status and its messages as ordinary text your screen reader can read, and lets you choose menu options with the keyboard.

### Opening it {#opening-it}

Press **Input Alt+G**. The Access GSX window opens. It has:

- **GSX status** — a read-only line showing whether BA is connected to GSX.
- **Menu options** — the current GSX menu, one option per line.
- **Active services** — a list that appears only when two or more GSX services are running at once (see below).
- **Tooltip** — GSX's latest message.

If no GSX menu is open, the menu field reads *"GSX Menu hidden. Press F5 to open it."*

### Keys inside the window {#keys-inside-the-window}

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| F5 | Open, or reopen, the GSX menu |
| 1 to 9, and 0 | Choose that menu option (the same layout as the in-sim GSX menu) |
| A | Customize Airport positions |
| B | Customize Airplane |
| C | Open the accessible GSX Settings window (while a GSX menu is open) |
| D | Restart GSX |
| E | Reload SimBrief |
| Escape | Hide the Access GSX window (GSX keeps running) |

</div>

The menu is read out the way it is shown: the menu title, then each option with the key that chooses it — *"1. Request Deboarding"*, *"2. Request Catering"* and so on. The letters A to E are always listed at the end. They work even when no menu is open, which matters for **D, Restart GSX**: that is the standard fix when GSX stops responding, and a stuck GSX will not open a menu.

An option can carry a state after a dash, such as *"— In progress"*, *"— Completed"*, *"— Requested"* or *"— unavailable"*. These are the colours the sighted menu uses, spelled out in words. If you choose an unavailable option you hear *"That option is unavailable."* and nothing is sent.

When you pick an option, BA does not announce the key press itself — your screen reader already knows what you did. What you hear next is GSX's response: usually its next menu.

<div class="callout tip" role="note" markdown="1">

**Tip:** Long menus, such as a list of stands, are split by GSX into pages of up to ten options. The last option on a full page is "Next Page". Choose it with its number key to hear the next page.

</div>

### Menus that update by themselves {#menus-that-update-by-themselves}

While a service runs, GSX keeps refreshing its menu — an option can contain a live counter such as a passenger count. BA only reads the menu out again when something meaningful changes: a new menu opens, its title changes, options appear or disappear, or an option's wording changes for a real reason (for example "Request Boarding" becoming "Boarding no longer possible"). A counter ticking up on its own updates the list silently. Arrow through the menu at any time to hear the current numbers.

### Read the latest GSX message without opening the window {#read-the-latest-gsx-message-without-opening-the-window}

Press **Output Ctrl+G** from anywhere to hear GSX's latest message. If GSX has not said anything yet you hear *"No GSX tooltip yet."*

### Active services {#active-services}

When GSX is running two or more services at the same time — boarding and refuelling, say — an **Active services** list appears above the tooltip. Choosing a service decides which service's message is shown in the Tooltip field. It does not change what is announced: you still hear every service's own progress, whichever one you pick here. The list is hidden when zero or one service is running, so it stays out of your way.

### If GSX cannot be reached {#if-gsx-cannot-be-reached}

If GSX is not running, is too old, or has just restarted, the window still opens and the status line says why. Pressing F5 speaks the same reason. You will hear one of these:

- *"GSX's Remote API is not reachable. This needs GSX 4.0.8 or newer."* — GSX is not running, or it is older than 4.0.8.
- *"Lost the connection to GSX's Remote API. Reconnecting."* — GSX restarted; BA reconnects on its own. GSX restarts itself routinely, so give it a moment.
- *"Not connected to the simulator, so GSX is not being monitored."* — BA is not connected to MSFS.

BA never announces these at startup. You are only told when you ask for GSX.

## GSX Settings {#gsx-settings}

### What it is {#what-it-is-22}

GSX has many settings of its own — how its vehicles behave, sounds, timings and much more. BA presents GSX's settings page as an ordinary accessible window, so you can change them without the in-sim menu.

### How to use it {#how-to-use-it-11}

1. Open the Access GSX window with Input Alt+G.
2. Press F5 to open the GSX menu.
3. Press **C**. The **GSX Settings** window opens.
4. At the top is the **Settings sections** list — GSX's own tabs. Use the Left and Right arrow keys (or Up and Down) to move between sections.
5. Press Tab to move into the section's settings. Each setting is a normal control: checkboxes for on/off settings, lists for choices, number boxes for ranges, text boxes for text, buttons for actions, and read-only boxes for information.
6. Press **Close** or Escape when you are done.

Changes go straight to GSX and GSX saves them itself. Number and text settings are sent as you change them, and again when the window closes. When GSX confirms a change, the control updates where it is, without moving your focus.

If you press C before GSX has published its settings, the window shows *"No GSX settings were available."* Open the GSX menu with F5 and press C again.

## What GSX Tells You {#gsx-announcements}

### What it is {#what-it-is-23}

While services run, BA reads out the meaningful changes GSX reports, so you can follow your turnaround without watching the screen. You are never read the state GSX was already in when BA connected — only what changes afterwards.

### What you will hear {#what-you-will-hear-2}

- **Services starting and finishing**, for every GSX service. The start names the handling company when GSX has one, for example *"Refuel in progress by United Ground Express."* or *"Deboard in progress by OneJet."*. When a service ends you hear it named followed by "complete". When a finished service becomes requestable again, nothing is said — the invoice tells you it is done.
- **Passengers and bags**, at milestones rather than for every passenger: *"pax 0 of 155."*, *"pax 1 of 155."*, then every ten passengers, and *"bags 40 percent."* every ten percent. A changed passenger total is always announced.
- **Fuel**, at most once every 30 seconds while fuel is flowing: *"fuel 2221 kg loaded, aircraft 5252 kg."*
- **The bus**, once per phase — approaching, on the way, in position, leaving. The countdown in GSX's text ("ETA 15 secs") is not read every second.
- **The ground crew and vehicles.** GSX narrates what each vehicle is doing — the front loader raising its belt, the rear stairs in position, the trains on their way, the operator walking to the fuel pump, the boarding platform going down, and every "waiting for your action" prompt. This works for pushback, refuelling, boarding and deboarding. Only what has actually changed is spoken, so moving one vehicle does not read the whole ramp out again.
- **GSX's own banners** when no service is running — the follow-me car, the marshaller and the positioning messages — whenever their wording changes.
- **Ground connection timers.** GSX bills for metered connections such as the jet bridge, ground power and stairs. When one starts you hear its name followed by "timer running", for example *"Jetway operations timer running."* Every 15 minutes you get a reminder with the time so far and the amount, for example *"... still running, 30 minutes, amount 75.50."*, and when it stops, *"... timer stopped, 1 hour 6 minutes, amount 116.97."* Anything under a minute is read as "under a minute".
- **Invoices**, once each: *"Invoice available from"* the handling company, followed by the total, for example *"Invoice available from OneJet. Total 1761.42."* If GSX has not published an amount yet, the invoice is announced without one rather than with a made-up figure.

### Hearing GSX with the window closed {#hearing-gsx-with-the-window-closed}

While the Access GSX window is visible, its announcements are always spoken. When it is hidden, they follow a setting: **Announce GSX tooltips when Access GSX window is closed**, on the **Announcements** tab of Settings. It is **off** by default. Turn it on if you want to hear boarding, fuel, pushback and the ground crew while you work in other windows. A menu GSX opens on its own — a pushback confirmation, an operator question — is also read out with the window hidden when this setting is on; with it off, you find that menu waiting the next time you open the window.

### If something goes wrong {#if-something-goes-wrong-10}

If GSX announcements misbehave — something you expected is never spoken, or something repeats — BA records what it decided and why in a log file called gsx.log. Attach it to your bug report. See [Logs and Getting Help](getting-started.md#logs-and-help) for where the logs are.

## Letting BA Prepare Your Stand in GSX {#gsx-auto-select}

### What it is {#what-it-is-24}

Normally you tell GSX which stand you are parking at by working through its menu. BA can do this for you: when you calculate a taxi route to a stand, BA asks GSX to prepare that stand, so the jet bridge, marshaller and parking guidance display are ready there when you arrive. It then tells you what GSX actually did.

### How to use it {#how-to-use-it-12}

There is nothing extra to do. The setting **Auto-select arrival gate in GSX on route calculation** is on the **Taxi Guidance** tab of Settings, and it is on by default. With it on, every time you press Calculate Route with a **Gate / Parking** destination (or a **Place** destination that is a GSX stand), BA sends the stand to GSX in the background. Your route loads straight away; it does not wait for GSX.

### What you will hear {#what-you-will-hear-3}

- *"GSX prepared Gate A 12."* — success. GSX is set up at the stand you picked.
- *"Careful: you selected"* one stand *"but GSX prepared"* another — at large airports several stands can share a name, and GSX resolved to a different one. Check which stand you really mean, and if necessary pick it from the GSX menu yourself.
- *"GSX is already set up at"* the stand — nothing needed doing. If it is a different stand from the one you picked, the sentence starts with "Careful:" instead.
- *"GSX warns"* the stand *"may be too small for this aircraft."* — GSX prepared it, but its profile thinks your aircraft is too big.
- *"GSX released the previous stand's services."* — GSX had services set up at another stand; it let them go and prepared yours.
- *"GSX could not find"* the stand *", so no stand was prepared."* or *"GSX could not prepare this stand."* — nothing was prepared. Choose the stand from the GSX menu in the Access GSX window instead.
- *"The requested stand is occupied by another aircraft. GSX did not select it."* — BA never forces GSX onto an occupied stand, because that would put you nose-to-nose with an aircraft you cannot see. Pick another stand.
- *"GSX found more than one matching stand"*, followed by up to a few names — choose a more specific stand.
- *"Automatic gate selection needs GSX 4.0.8 or newer. This GSX build does not offer it, so select the stand in the GSX menu yourself."* — said once per taxi planner window, not on every route.

If GSX is not running, or no airport is loaded in GSX yet (for example you calculated the route while still far away), nothing is said. Calculate the route again closer to the airport.

<div class="callout tip" role="note" markdown="1">

**Tip:** Calculate your taxi-in route after landing, or on approach, rather than at the gate before departure. GSX needs to have your arrival airport loaded to prepare a stand there.

</div>

## Docking Guidance {#docking}

### What it is {#what-it-is-25}

Docking guidance gets you from the end of the taxi route onto the stand, square with the gate and stopped at the right spot for the jet bridge. Real airports do this with a parking guidance display or a marshaller. BA does it with sound:

- **A steering tone** — the same stereo tone taxi guidance uses — keeping you on the stand's centre line.
- **A proximity beep**, like a car's parking sensor, that speeds up as the stop gets closer and becomes a solid tone when you are on the stop.
- **A few short spoken calls**: the distance at fixed milestones, your speed in knots, and a clear closing phrase.

### When it is used {#when-it-is-used}

Docking guidance starts by itself. You only need to have taxi guidance running to a stand: choose a **Gate / Parking** (or **Deice Area**) destination in the taxi planner and start guidance. It works with or without GSX. It never starts for a runway destination.

With GSX, the stop is GSX's own stop position for your aircraft type at that gate, which is where the jet bridge expects your door. Without GSX, the stop is the centre of the parking spot in your scenery, which is usually close but less exact.

### Step by step: docking at a gate {#step-by-step-docking-at-a-gate}

1. **Taxi in on taxi guidance as normal.** Taxi guidance steers you along the taxiways and around the turn onto the stand. When the stop position is still ahead of the point where taxi guidance would normally stop you, taxi guidance says so instead of telling you to stop — you hear, for example, *"Align with Gate A 12, heading 270. Then continue ahead for docking guidance."* or *"10 metres. Continue ahead for docking."* Keep rolling slowly.

1. **Docking engages.** When you are within about 50 metres of the stop, roughly in line with the stand, and slower than 15 knots, docking takes over. (Some GSX gates set their own engage distance, between 20 and 70 metres.) You hear one sentence, for example *"Docking guidance. SafeDock display. 40 metres to stop. Slight right, 5 degrees. Jetway on your left."* That tells you the parking system installed at the gate (when there is one), the distance to the stop, how much you need to steer right now, and which side the jet bridge or door is on (when BA knows). The steering amount is rounded to 5 degrees and worded *"Slight"* for 5 to 10 degrees, plain for 15 to 20, and *"Sharp"* for 25 or more. It is said up front because the tone reaches full pan at 15 degrees: on a big, slow-turning aircraft you need the number to judge how much tiller to use.

1. **Steer with the tone.** From now on docking owns the steering tone, and taxi guidance's own tone goes quiet so you never hear two. Steer toward the tone (or away from it if you have inverted it in Settings). The tone is very precise here: it stays silent only when you are within half a degree of the correct line, and comes back as soon as you drift more than a degree. Near the stop, the guidance gradually turns you square with the gate, so a centred tone in the last few metres means you are both on the line and straight.

1. **Listen to the beeps.** The beeping starts slowly — about once a second around 30 metres out — and gets faster as you close in, until it is a rapid patter just before the stop. When you are on the stop, it becomes **one solid tone**. Creep forward until you hear the solid tone, then stop.

1. **Distance and speed calls.** On the way in you hear the distance at fixed points: *"30 metres to stop."*, *"20 metres to stop."*, *"10 metres to stop."*, *"5 metres to stop."* (or 100, 60, 30 and 15 feet if you use feet). If you are still faster than 3 knots in the last 6 metres, you hear *"Slow down."* once. And, unless you have turned it off, your ground speed is spoken every time it changes by a knot — *"4 knots."*, *"3 knots."*, *"1 knot."*, *"Stopped."* — because at gate speeds the normal speed announcements are silent, and both your hands are busy.

1. **The closing phrase.** When you stop on the spot, on the centre line and square with the gate, you hear *"GSX docking complete. Aligned with gate. Parking brake."* At a stand with no GSX stop position, the first word is *"Stop."* instead: *"Stop. Aligned with gate. Parking brake."* The solid tone keeps sounding for about three seconds, as a reference while you come to a halt, then fades. Guidance is now finished: set the parking brake. Taxi guidance stops too.

### Docking at a deicing pad {#docking-at-a-deicing-pad}

If your taxi destination is a Deice Area, docking works the same way but the wording changes. The opening call is *"Deicing guidance."* with the distance and steering, there is no jet bridge or door call, and the closing phrase is *"Stop. Parking brake."* — without any claim about being aligned with a gate, because a deicing pad is wide and BA does not check your alignment there.

### When the park goes wrong: "Back up and try again" {#when-the-park-goes-wrong-back-up-and-try-again}

Docking only calls a park complete when it really is good. If it cannot be, it stops you with a clear instruction instead of letting you creep into a bad position. There are three cases:

- **You rolled past the stop** (by more than about a metre): *"Stop. You have passed the stop position."*
- **You are too far to one side** of the stand line to make the stop: *"Stop. Too far left of the gate centerline. Back up and try again."* (or right).
- **You reached the stop, but crooked** — more than 7 degrees off the gate's heading: for example *"Stop. Nose 18 degrees right of the gate heading. Back up and try again."*

In each case the tone and beeps stop at once. No solid "docked" tone sounds over a bad park, so silence after one of these calls means "not parked correctly", never "done".

Why not just turn the last bit? Because at the stop there is only about a metre of rolling left, which is far too little to turn out a heading error — and GSX will not treat a crooked aircraft as parked, so the jet bridge will not come.

To try again:

1. Stop and set the parking brake.
2. Back the aircraft up a few metres, straight back along the stand line — for example with GSX pushback, or the aircraft's reverse if it supports it.
3. Open the taxi planner (Input Shift+Y) and calculate the route to the same stand again. This starts guidance afresh, and docking engages again as you roll forward onto the stand.

### Stopped short {#stopped-short}

If you stop between the 5-metre call and the stop and sit still for about four seconds, docking reminds you how far is left: *"5 metres to stop. Continue forward."* Roll on slowly until you hear the solid tone. (Distances are rounded, so very short gaps are given to the nearest 5 metres or 25 feet.)

### Checking your status: Output Y {#checking-your-status-output-y}

Press **Output Y** at any time during docking for a status in one sentence, for example:

- *"Docking. 15 metres to stop. Slight left, 5 degrees. Square with the gate."*
- *"Docking. 5 metres to stop. Centered. Nose 9 degrees left of the gate heading."*

This replaces the normal taxi status while docking is in control, so you never hear two different distances. After a good park, Output Y says *"Parked at the stop. Guidance complete."* (or *"Parked at the deicing pad. Guidance complete."*). After one of the failed endings it says *"At the stop. Hold position."*

### Stopping docking {#stopping-docking}

Docking ends by itself when you park, when you taxi well away from the stand (more than about 75 metres), or when you stop taxi guidance with **Input Ctrl+Y** (you hear *"Taxi guidance stopped."*). Starting a takeoff, or a new route to a runway, also clears it.

### Settings {#settings-4}

All the docking settings are on the **Taxi Guidance** tab of Settings, in the **Docking guidance** group.

- **Docking guidance (auto-engage at the gate)** — on by default. Untick to turn docking off entirely; taxi guidance then simply ends at the parking spot as it did before.
- **Docking beep sound** — the wave type of the proximity beep. The default is sine.
- **Docking beep volume** — a slider from 0 to 100 percent, with a **Test** button beside it to hear the beep at the chosen sound and volume.
- **Announce ground speed every knot while docking** — on by default. Untick if you find the speed calls too chatty.

The steering tone itself uses the taxi guidance tone settings on the same tab: **Steering tone type**, **Volume**, **Invert steering tone direction (steer AWAY from the tone)** and **Hard-pan steering tone (full left or full right; speaker-friendly)**. See [Taxi Guidance](taxi-guidance.md) for those.

Distances follow **Use feet for distances (default: metres)**, also on the Taxi Guidance tab.

### If something goes wrong {#if-something-goes-wrong-11}

- **Docking never engaged.** It needs you within its engage distance, roughly lined up with the stand, and below 15 knots. If you were still far off the stand line when you got close, docking waits and taxi guidance keeps steering you through the turn. Keep following the taxi tone and roll forward slowly. If you stop at the parking spot and hear taxi guidance's own arrival calls, docking was not able to engage at this stand.
- **GSX says the aircraft is not parked, or offers to reposition it.** Your park was outside what GSX accepts. If BA said "Back up and try again", follow the retry steps above.
- **The stop seems to be in the wrong place at a stand without GSX.** Without GSX, the target is the scenery's parking spot centre, not a jet bridge stop position. This is a limit of the scenery data.
- **Something felt wrong and you want to report it.** BA writes every docking attempt to a log file called docking.log. Include it in your report; see [Logs and Getting Help](getting-started.md#logs-and-help).

### Worked example: arriving at a GSX gate {#worked-example-arriving-at-a-gsx-gate}

You have landed and vacated the runway. You open the taxi planner, set Gate / Parking, type "A12" in the Gate search box, choose *"A 12 - Gate Medium (Jetway) \[SafeDock\]"*, enter your taxi clearance and press Calculate Route. A moment later you hear *"GSX prepared Gate A 12."* — the jet bridge will be waiting.

You taxi in on the tone. On the turn onto the stand you hear *"Align with Gate A 12, heading 270. Then continue ahead for docking guidance."* Rolling slowly forward, you hear *"Docking guidance. SafeDock display. 40 metres to stop. Slight right, 5 degrees. Jetway on your left."* and the slow beeping starts. You ease right until the tone falls silent. *"30 metres to stop."* ... *"20 metres to stop."* ... *"5 knots."* ... *"10 metres to stop."* The beeps get faster. *"3 knots."* ... *"5 metres to stop."* ... *"2 knots."* ... *"1 knot."* The beeps blur into a solid tone, you stop, and you hear *"GSX docking complete. Aligned with gate. Parking brake."* You set the parking brake, open the Access GSX window with Input Alt+G, press F5, and request deboarding.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Ground Traffic](ground-traffic.md)
- [Next chapter: Takeoff, Climb and Cruise](takeoff-climb-and-cruise.md)
- [Guide contents](index.md)

</nav>
