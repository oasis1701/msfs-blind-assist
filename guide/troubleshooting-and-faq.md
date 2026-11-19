---
title: "Troubleshooting and FAQ"
---

<div class="guide-part" markdown="1">

Chapter 3 of 4 in [Appendices](index.md#appendices) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [BA Is Silent, or Speech Sounds Wrong](#trouble-speech)
- [Hotkeys Do Nothing](#trouble-hotkeys)
- [BA Will Not Connect, or Uses the Wrong Aircraft](#trouble-connect)
- [Cockpit Controls Do Not Respond (MobiFlight)](#trouble-mobiflight)
- [PMDG: CDU Window or Readouts Are Empty](#trouble-pmdg)
- [Airport or Runway Not Found: the Navigation Database](#trouble-database)
- [A Taxi Route Will Not Calculate](#trouble-taxi)
- [Tones Play on the Wrong Device, or Not at All](#trouble-audio)
- [Updates](#trouble-updates)
- [Sim Performance](#gs-sim-performance)
- [Logs: What to Send When You Ask for Help](#trouble-logs)
- [Frequently Asked Questions](#trouble-faq)

</nav>

Most problems have a simple cause. Work through the matching section below. If nothing helps, collect your logs (see the last section) and ask on the MSFS Blind Assist Discord server.

## BA Is Silent, or Speech Sounds Wrong {#trouble-speech}

- **Check the announcement method** on the **Announcements** tab of Settings (see [Announcement Settings](core-concepts.md#core-announcement-settings)). If it says *"No screen reader detected"*, check that NVDA or JAWS is actually running.
- **Speech in an unexpected voice.** Hearing the Windows voice usually means BA could not reach your screen reader for that message.
- **BA does not repeat your own key presses, and many controls work with no sound at all.** Silence after you change a control does not mean it failed; read the control or panel row again after a second or two.
- **You may have muted the announcement.** Open the [Monitor Manager](core-concepts.md#monitor-manager) (Output Ctrl+M) and check that the item you expect is not switched off.
- **VATSIM announcements specifically:** press Output Alt+V in case you muted them for the flight, and check the VATSIM tab of Settings. After BA installs its vPilot plugin, you must restart vPilot once.

## Hotkeys Do Nothing {#trouble-hotkeys}

- **Enter a mode first, and the right one.** Almost every hotkey works only after \] (Output, *"output"*) or \[ (Input, *"input"*), and the same key can do different things in each mode: Output Shift+D reads top of descent, Input Shift+D opens the destination runway window. See [Output Mode and Input Mode](getting-started.md#hotkey-modes) and the [Hotkey Reference](hotkey-reference.md).
- **Check your aircraft.** Hotkeys differ between aircraft.
- **Hotkeys may be suspended** from the File menu. If BA says *"Warning: failed to re-register hotkeys. Another application may be using the bracket keys."*, close the other program, or a second copy of BA, and try again.
- **Hand Fly and Visual Guidance take over single letters** (A, S, H, V, Q, D, B, P and F). Turn the mode off (Output Ctrl+H or Ctrl+V) before typing in another program.
- **"Not connected to simulator."** BA is not connected yet. See the next section.
- **Keyboard layout.** If your bracket keys never turn a mode on, try the two keys to the right of P, or ask for help on the Discord.

## BA Will Not Connect, or Uses the Wrong Aircraft {#trouble-connect}

- **"Waiting for simulator."** BA's status line shows this until the simulator is running. Load into a flight; BA connects on its own, says *"Initializing, please wait"*, then *"Connected to"* followed by the aircraft's title.
- **BA uses the profile you chose, not the one it guesses.** After connecting, BA says which profile is active, for example *"FlyByWire Airbus A320neo Profile and panels active"*. If that is not your aircraft, choose the right one from the **Aircraft** menu. BA remembers your choice for next time.
- **Your aircraft is not in the Aircraft menu.** Only the aircraft listed there have panels and aircraft-specific features. The general features — taxi guidance, weather, location, teleport and so on — still work with other aircraft, but the panels will not match.
- **iFly 737 MAX8: _"iFly 737 not detected."_ or \*"iFly plugin not responding."**\* BA talks to the iFly through the iFly's own SDK connection, which only exists while the aircraft is loaded and the simulator is running and not paused. Load the aircraft, unpause, and try again. See [Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md).

## Cockpit Controls Do Not Respond (MobiFlight) {#trouble-mobiflight}

On several aircraft, including the FlyByWire A320neo and A380X, the Fenix A320, the Headwind A330, the TFDi MD-11 and the BlueBird 757-200 (and for a handful of switches on the HorizonSim 787-9), BA needs the free **MobiFlight WASM module** to operate switches and knobs. Without it, reading and announcements still work, but many controls do nothing.

If you hear *"MobiFlight calculator path unavailable. Some cockpit controls may not respond."*, install or reinstall the module in your Community folder and restart the simulator (see [What You Need](getting-started.md#gs-requirements)). Only the FlyByWire A320neo and A380X, the Headwind A330, the TFDi MD-11 and the BlueBird 757-200 give that warning. The Fenix A320 and the HorizonSim 787-9 do not warn you, so if one of their switches never moves, check MobiFlight first.

## PMDG: CDU Window or Readouts Are Empty {#trouble-pmdg}

The PMDG 737 and 777 only share their data with other programs if their SDK broadcast is switched on in the aircraft's options file. BA checks this when you pick the aircraft and, if the lines are missing, opens a window titled with the aircraft's name and **SDK setup**.

Follow the window's instructions with the simulator closed: PMDG rewrites the file when the simulator exits and can drop lines you added while it was running. The window and every step are described in [The PMDG SDK Setup Check](getting-started.md#gs-pmdg-sdk).

If BA says it could not find the options file at all, that is normal before you have loaded that PMDG aircraft once; PMDG creates the file the first time. Load the aircraft, then follow the steps.

## Airport or Runway Not Found: the Navigation Database {#trouble-database}

BA's airport, runway, taxiway and procedure features all read a navigation database built from your simulator's own scenery. If it is missing or built for the wrong simulator, they fail.

- **Build it, for the right simulator.** Open **File**, then **Nav Database**, and press **Build FS2020 Database** or **Build FS2024 Database** (close MSFS 2020 first; have MSFS 2024 at its main menu).
- **"Database Mismatch Warning".** Choose **Yes - Open Settings** to switch, rather than **No - Continue Anyway**: the wrong database can give you wrong runway positions.
- **Rebuild after scenery or navigation data changes**, so BA sees the same airport layout as the simulator.
- **Messages such as \*"Airport KJFK not found in database"**\* in the taxi planner, teleport or destination runway windows usually mean the database is missing or out of date.

See [The Navigation Database](getting-started.md#nav-database) for more.

## A Taxi Route Will Not Calculate {#trouble-taxi}

- **Load the airport.** The taxi planner needs the airport's taxi network. If the status says *"No taxi data for"* the airport, the database has no taxiways for it, often because the scenery or database is out of date.
- **Be at the airport and connected.** Calculate Route uses your aircraft's live position. *"Could not find your position on the taxi network."* means BA could not place you on a taxiway or stand: check you are at the right airport and on the pavement.
- **Check your clearance entry.** In Progressive Taxi, a message such as *"Could not find taxiway B from A. Check your entry."* means the taxiways you entered do not join up the way you entered them. Check the order and spelling against what ATC said. Some sceneries name taxiways differently from the charts; the planner's lists show the names this airport actually uses.
- **"Could not calculate a route to the destination."** There is no connected path. The destination may be on a part of the taxi network that the scenery has left disconnected. Try another destination nearby, or a different entry point.
- **Warnings in the route summary** — for example that the route does not reach the runway, or that there is no hold short point for a runway — are BA being honest about a problem in the airport data. Read them before you move. [Everything Taxi Guidance Says](taxi-guidance.md#taxi-callouts) explains each one.
- **A runway or stand looks wrong.** Your scenery's data may differ from the real airport. See [Taxi Guidance](taxi-guidance.md).

## Tones Play on the Wrong Device, or Not at All {#trouble-audio}

1. Open Settings and go to the **Audio** tab.
2. In **Guidance tone output device**, choose where you want BA's guidance tones (taxi, takeoff, hand fly, landing guidance and docking). This does not change where your screen reader speaks.
3. Press **Test Tone** to hear a tone on the chosen device, and **Stop Test** to end it. The **Status** line shows which device is really in use.

If your chosen device is unplugged, the tones move to the Windows default device and come back when you plug it in; see [Choosing where the tones play](core-concepts.md#audio-language). *"No audio device available for guidance tones."* means Windows has no working output at all, so every guidance tone is silent.

If a tone is on the right device but too quiet or too loud, each tone has its own volume setting; see the [Tone Cheat Sheet](tone-cheat-sheet.md).

## Updates {#trouble-updates}

- Checking for updates, preview builds and going back to a release are described in [Updates](getting-started.md#gs-updates).
- If an update ever leaves you stuck, you can download the release zip from the project's releases page, close BA, and extract it over your BA folder.

## Sim Performance {#gs-sim-performance}

### What it is {#what-it-is-26}

The Sim Performance window shows the same kind of figures a sighted pilot reads from the simulator's own developer frame-rate display: how smoothly the simulator is running, and how hard it is working your computer. It is useful when you are tuning graphics settings or wondering whether the simulator is struggling.

### How to use it {#how-to-use-it-13}

Open it from the **File** menu, **Sim Performance**. It opens whether or not the simulator is connected. It is a list with one value per row:

- **Simulator**, which simulator is running, or *Simulator: not running*.
- **Frame rate**, in frames per second, and **Frame time**, in milliseconds. While the simulator is paused or still loading it says *Frame rate: no frames received, sim paused or still loading*.
- **Simulation rate**, for example *1x*, *4x* or *paused*.
- **Simulator CPU**, as a percentage of all cores.
- **Simulator memory** and **System memory**.
- **GPU load**, **Simulator video memory** and **GPU memory in use**, where Windows can measure them.

The list updates itself every second, so there is no refresh button. Arrow to the row you care about and stay there: that row is re-read whenever its value changes, while the others stay quiet. Press Escape or the **Close** button to close it.

The simulator's internal per-thread timings, and its verdict about what is limiting performance, cannot be read from outside the simulator, so they are not shown.

## Logs: What to Send When You Ask for Help {#trouble-logs}

Press Windows+R, type %APPDATA%\\MSFSBlindAssist\\logs and press Enter. Zip that folder and send it with a short description: what you did, what you expected, what happened, and roughly what time. [Logs and Getting Help](getting-started.md#logs-and-help) has the full steps.

<div class="callout tip" role="note" markdown="1">

**Tip:** Send logs soon after the problem. Logs are rotated as they grow, so an old problem may have been written over.

</div>

## Frequently Asked Questions {#trouble-faq}

**Do I need to see the screen at all?** No. Everything BA does is designed for a screen reader and the keyboard. Some aircraft displays can only be read through BA's optional AI display reading, which needs your own API key; see [AI Display Reading and Scene Description](core-concepts.md#ai-display-reading).

**Does BA fly the aircraft for me?** No. BA gives you access to the aircraft's own controls and tells you what is happening. The aircraft's autopilot flies when you engage it; BA's tones help you fly by hand when you choose to.

**Does BA tell me when a runway is clear?** Never. Its runway watch tells you what traffic it can see, as *"no traffic seen"*, because it cannot see everything ATC can. Only a clearance tells you a runway is yours.

**Why does BA not announce my button presses?** Your screen reader already does. If BA spoke too, you would hear everything twice. BA speaks when you type a value, when something goes wrong, and when something changes on its own.

**Can I use BA with MSFS 2020 and MSFS 2024?** Yes. Build the navigation database for each simulator you use.

**My aircraft is not listed. Can I still use BA?** Yes, for the general features: taxi guidance, ground traffic, weather, location, planning, teleport and many readouts. The panels and aircraft-specific windows are only available for the aircraft in the Aircraft menu.

**Where do I get help?** The MSFS Blind Assist Discord server, linked from the project's home page, is where users and developers talk. Bring your logs.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Glossary](glossary.md)
- [Next chapter: Hotkey Reference](hotkey-reference.md)
- [Guide contents](index.md)

</nav>
