---
title: "The First Officer and Checklists"
---

<div class="guide-part" markdown="1">

Chapter 1 of 1 in [Part 3: The First Officer](index.md#part-first-officer) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [What it is](#what-it-is-13)
- [When to use it](#fo-when-to-use)
- [How to use it](#how-to-use-it-7)
- [The Flows tab](#fo-flows-tab)
- [The Checklists tab](#fo-checklists-tab)
- [Loading SimBrief](#fo-simbrief)
- [What you will hear](#fo-what-you-will-hear)
- [Settings](#fo-settings)
- [If something goes wrong](#fo-if-something-goes-wrong)

</nav>

## What it is {#what-it-is-13}

Output Shift+C opens the **First Officer**, a crew member who works the flows and checklists with you. Its window has two tabs:

- **Flows** runs each phase's procedure for you, from powering up the aircraft to securing it. The First Officer sets its own switches, checks that each one really moved, and reminds you of the Captain's items.
- **Checklists** holds the aircraft's normal checklists. A line ticks itself once the aircraft is in that state, and ticking a line yourself has the First Officer do it.

The First Officer is on the PMDG 777 and 737, the iFly 737 MAX8, the Fenix A320, the FlyByWire A32NX and A380X, the Headwind A330-900neo and the TFDi MD-11. You can also open it from the File menu, where the **First Officer** item is named after your aircraft, for example "Fenix A320 First Officer". On any other aircraft the item is not there, and Output Shift+C says *"No checklist for this aircraft."* and opens nothing.

The First Officer never programs the MCDU, CDU or FMC: you still enter the flight plan and performance yourself. Items only the Captain can judge, such as the landing autobrake setting, are spoken as reminders and left to you.

<div class="callout note" role="note" markdown="1">

**Note:** The A380X also has a live Electronic Checklist, read from the aircraft's own display and ticked by the aircraft's own sensing, on Output Ctrl+Shift+C. See [Airbus Aircraft](airbus-aircraft.md).

</div>

## When to use it {#fo-when-to-use}

Open it at the start of each flight, before you power up the aircraft. Once opened, it keeps working for the rest of the flight, even with its window closed: it switches the landing lights at 10,000 feet and sets the altimeters at the transition (see [Settings](#fo-settings)). Run each flow at its point in the flight, then read the matching checklist. If you would rather set everything yourself, skip the flows and use the checklists to check your work.

If you first open it in flight, it marks the checklists up to After Takeoff complete and says *"In flight: checklists through After Takeoff marked complete."*

## How to use it {#how-to-use-it-7}

1. Press Output Shift+C. The first time, the window opens on its tab strip; after that it returns to the control you left.
2. Press Ctrl+Tab to switch between the **Checklists** and **Flows** tabs, or use the arrow keys on the tab strip.
3. Press Escape to close the window. You go back to where you were, usually the simulator. Your ticks are kept, and a running flow carries on.

The window opens before BA has connected to the simulator; its checklists come to life once it connects. Loading a new flight in the simulator stops a running flow and unticks every checklist.

## The Flows tab {#fo-flows-tab}

The Flows tab has:

- **Available flows:** this aircraft's flows, in flight order. On the Fenix A320, for example: Electrical Power Up, Preflight, Before Start, Engine Start, After Start, Taxi, Line-up, After Takeoff, Descent, Approach, Landing, After Landing, Shutdown and Securing.
- **Flow steps:** the steps of the selected flow. A Captain's item starts with "[CAPTAIN]". During and after a run, each step is marked with a check mark (done), an arrow (skipped) or a cross (failed).
- **Current step** and **Flow status:** read-only lines saying what is happening, for example "Step 3: Beacon: ON" and "Running: Before Start".
- The buttons **Start Flow**, **Pause / Resume**, **Stop Flow** and **Load SimBrief**.

To run a flow, select it in Available flows and press Enter, or Tab to Start Flow and press it. The First Officer works through the steps about two seconds apart. Pause / Resume holds the flow and continues it; Stop Flow ends it after the current step. Only one flow runs at a time.

Before it switches anything that could stop an engine, a flow checks that it is safe. The power-up flows that check the engine controls (Electrical Power Up on the Fenix A320, A32NX, Headwind A330 and 777, Cockpit Preparation on the A380X and 777, and Power Up on the MD-11) and the 737 Preflights stop if an engine is running or the aircraft is in the air, and the shutdown, parking and securing flows stop unless the aircraft is on the ground. The securing flows also stop if an engine is still turning. A shutdown or parking flow cuts the engines first, then waits up to two minutes for them to spin down (*"Waiting for: Engines stopped"* when flow progress is spoken) before it switches the fuel pumps off. Either way you hear why.

On the Fenix A320 the First Officer goes by the Fenix's own engine readings. Engine Start moves on to the second engine once the Fenix says the first start is complete, and allows up to 3 minutes for each engine. Shutdown's *"Engines stopped"* waits for the Fenix's engines to spin down, usually about 20 seconds, before the fuel pumps go off. On the Fenix A321, the Preflight flow leaves out the pack flow selector, which the A321 does not have; set its Econ Flow button as you need it.

## The Checklists tab {#fo-checklists-tab}

The checklists are a tree. Each checklist is named with its progress, for example "Before Start — Not started", "Before Start — 3 of 7 complete" or "Before Start — Complete". Press Right Arrow to open one and Up and Down Arrow to read its lines; opening one closes the others.

- A line ticks itself once the aircraft is in that state, so as you read down a checklist you hear what is already done.
- Press Space to tick a line yourself. If it is a switch the First Officer can set, it sets it, and BA adds the value it reads, such as the flap setting. If the switch never moves, the line unticks itself and you hear *"Unable to complete:"* followed by the line.
- If the aircraft's state makes a line unsafe to do, the tick is refused and BA says why. A line that switches the fuel pumps, a hydraulic pump, the IRS or a battery off is done only on the ground with the engines stopped, and a packs-off line only on the ground. Until BA has read the aircraft, it refuses too. For example: *"Fuel pumps left on: not read on the ground, or the engines are not read stopped."*
- A line the First Officer cannot set, such as a read-back item, only records that you checked it.
- A checklist you finished, by hand or with its flow, stays complete for the rest of the flight, even when later flows move the same switches.
- The **Checklist status** line below the tree shows the selected line with its live value and whether it is complete, without ticking it.

On the Fenix A320, these lines take their live value from the Fenix itself:

- Before Start Checklist, "Takeoff speeds and thrust: SET": V1, VR, V2 and flex from the MCDU's PERF TAKE OFF page, for example "V1 142, VR 145, V2 149, flex 55", or "not set" until you enter them.
- Line-up Checklist and Landing Checklist, "Cabin: READY": "reported ready" or "not reported". In Fenix v3 the cabin crew reports the cabin ready when it is; your cabin call from the cockpit no longer makes it ready.
- Cockpit Preparation Checklist, "Fuel quantity: CHECKED": the Fenix's own total, which counts any extra center tanks.
- Cockpit Preparation Checklist and Approach Checklist, "Baro reference: SET": "standard" or the QNH, for example "QNH 1013", read from the EFIS's own QNH light, so it stays right after you pull and push the baro knob.

The buttons below the tree:

- **Run Related Flow** switches to the Flows tab and runs the flow that belongs to the selected checklist.
- **Reset Section** unticks the selected checklist (*"Before Start checklist reset"*), and **Reset All** unticks them all (*"All checklists reset"*).
- **Load SimBrief** is the same as on the Flows tab.

## Loading SimBrief {#fo-simbrief}

**Load SimBrief** fetches your latest [SimBrief](planning-your-flight.md#simbrief) plan, so set your SimBrief username first. The First Officer uses the plan's transition altitude and level for the altimeters, its takeoff flaps in the flows, and on the PMDG 737 and iFly 737 MAX8 its cruise altitude and destination elevation for the pressurization panel. You hear one announcement, for example *"SimBrief flight plan loaded. Transition altitude 5000 feet, transition level 6000 feet. Takeoff flaps 5. Pressurization plan: flight altitude 36000 feet, landing altitude 400 feet."*

With your username set, the plan loads by itself whenever you open the window with no plan loaded. A plan belongs to one flight: it is forgotten after you land or load a new flight. If SimBrief still has the flight you have just flown, you hear *"SimBrief still has the flight just flown. Generate the next flight plan, then press Load SimBrief."* Without a plan, a step that needs one, such as setting the takeoff flaps, becomes a Captain reminder.

## What you will hear {#fo-what-you-will-hear}

While a flow runs:

- *"Before Start flow started"* when it begins and *"Before Start flow complete"* when it ends.
- Each step as the First Officer does it, for example *"Beacon: ON"*.
- *"Already set:"* and the step, when the aircraft is already that way, and *"Waiting for:"* or *"Waiting 30 seconds:"* and the step while it waits.
- *"Captain action required:"* and the item, for something you must do yourself.
- *"Skipping:"* and the step when a switch did not move, or *"Timed out waiting for:"* when a wait ran out. The flow carries on with the next step.
- *"Before Start flow stopped. Unable to complete:"* and the step, when a step the rest of the flow depends on fails.
- *"Before Start paused"*, *"Before Start resumed"*, *"Stopping after the current step:"* and *"Before Start flow cancelled"* for your own presses.

These lines wait their turn and do not cut off your screen reader. To hear fewer of them, untick **Speak flow progress** in [Settings](#fo-settings).

With the window closed, the First Officer still speaks what it does, for example *"Above ten thousand. Landing lights off."*, *"Transition altitude. Altimeters set to standard."* and, at the transition level, *"Transition level. Set local altimeter pressure now."* The exact words vary a little by aircraft.

## Settings {#fo-settings}

The **First Officer** tab of Settings (File menu, Settings) has these options:

<div class="table-wrap" markdown="1">

| Setting | What it does | Default |
|---|---|---|
| Auto-raise gear on positive rate (climb) | Raises the gear after a takeoff BA watched from the ground: *"Positive rate. Gear up."* | Off |
| Auto-lower gear at 2000 ft AGL (descent) | Lowers the gear on a real descent below 2,000 feet above the ground, at 250 knots or less: *"Gear down."* | Off |
| Auto-manage flaps (FBW A380, A32NX, Headwind A330) | Extends and retracts the flaps by the speed tape, on those three aircraft only. | Off |
| Auto-engage autopilot on climbout | Engages the autopilot climbing through the height below, after a takeoff BA watched, for example *"350 feet. Autopilot engaged."* | Off |
| Autopilot engagement altitude (feet AGL) | The height for the setting above, 100 to 3,000 feet. An aircraft with a higher minimum uses its own, such as 400 feet on the PMDG 737. | 350 |
| Automatically switch landing lights at 10,000 feet | Landing lights off climbing through 10,000 feet and on descending through it. | On |
| Automatically set altimeters at transition altitude and level | Standard climbing through the SimBrief transition altitude, local pressure descending through the transition level. When off, the First Officer only reminds you. | On |
| Auto seat belt signs | Disabled; At 10,000 feet; or Top of climb and top of descent. | Disabled |
| GPWS test type (PMDG 737) | Which GPWS self-test the 737's Preflight flow and checklist run: Short test or Long test. | Short test |
| Auto-manage center fuel pumps (PMDG 737/777 and iFly MAX8) | Center tank pumps on during ground setup when the center tank has fuel, and off when it runs dry. | Off |
| Speak flow progress | Speaks each step as a flow runs. When off, a flow speaks only "flow started", Captain actions, skipped or failed steps and "flow complete". | On |

</div>

The gear and autopilot options work on every aircraft, including the HorizonSim 787, whether or not the First Officer window has been opened. The landing lights, altimeters, seat belts, flaps and center pumps work once you have opened the First Officer window, and keep working with it closed until you change aircraft.

## If something goes wrong {#fo-if-something-goes-wrong}

- **"No checklist for this aircraft."** This aircraft has no First Officer.
- **"A flow is already running. Stop it first."** Only one flow runs at a time. Press Stop Flow, or wait for the flow to finish.
- **"The last flow is still stopping. Start again when it has stopped."** The flow you stopped is finishing a step that cannot be interrupted, such as the weather radar test. Wait for *"… flow cancelled"*.
- **"… flow stopped. Unable to complete: Engines stopped"** You ran a power-up flow with an engine turning. Choose the flow for this point in the flight. A shutdown or securing flow run in the air stops the same way, on *"On the ground"*. A securing flow run with an engine turning, or a shutdown whose engines take more than two minutes to spin down, stops on *"Engines stopped"*: check the engines are shut down, then run it again.
- **"Transition altitude. Set both altimeters to standard."** (or *"Transition level. Set both altimeters to QNH and set local pressure."*) On the FlyByWire A380, BA had not yet worked out whether you are flying MSFS 2020 or MSFS 2024, whose FlyByWire builds use different altimeter controls, so it set nothing. Set the altimeters yourself. The same reason makes an A380 or A32NX flow skip its flight director, baro unit, navigation display or ECAM page step. On the A380X in MSFS 2024 a flight director step is also skipped when one flight director is on and the other off, because the aircraft's single FD button cannot set them both. Set them yourself.
- **"Skipping: …"** The switch did not move, or the aircraft did not show it in time. Set it yourself from BA's panels and carry on; its checklist line stays unticked until the aircraft shows it.
- **"No related flow found for this section"** That checklist has no flow; work it by hand.
- **"SimBrief username not set. Configure it in Settings."** Enter your SimBrief username, as described in [SimBrief](planning-your-flight.md#simbrief).
- **"Failed to fetch SimBrief data: …"** Check your internet connection and your SimBrief username, then press Load SimBrief again.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Core Concepts](core-concepts.md)
- [Next chapter: Planning Your Flight](planning-your-flight.md)
- [Guide contents](index.md)

</nav>
