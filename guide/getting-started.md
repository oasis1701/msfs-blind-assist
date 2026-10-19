---
title: "Getting Started"
---

<div class="guide-part" markdown="1">

Chapter 2 of 2 in [Part 1: Getting Started](index.md#part-1-getting-started) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [What You Need](#gs-requirements)
- [Installing](#install)
- [Your First Launch](#gs-first-launch)
- [The PMDG SDK Setup Check](#gs-pmdg-sdk)
- [The Navigation Database](#nav-database)
- [Connecting to the Simulator](#gs-connecting)
- [Choosing Your Aircraft](#gs-aircraft)
- [The Main Window](#main-window)
- [Output Mode and Input Mode](#hotkey-modes)
- [The Hotkey List](#gs-hotkey-list)
- [The Menus](#gs-menus)
- [The Settings Window](#settings-overview)
- [Updates](#gs-updates)
- [Logs and Getting Help](#logs-and-help)
- [Your First Ten Minutes](#gs-quick-start)

</nav>

## What You Need {#gs-requirements}

- **Windows 10 or Windows 11, 64-bit.**
- **Microsoft Flight Simulator 2020 or Microsoft Flight Simulator 2024.** BA works with both, and detects which one is running.
- **A screen reader.** NVDA or JAWS is recommended. Without one, BA uses Windows speech instead.
- **The .NET 10 Desktop Runtime (x64)** from Microsoft. BA is built on it. Download it from Microsoft's .NET 10 download page; choose the Desktop Runtime for x64. If it is missing or too old, BA tells you when you start it, in a message box titled *"MSFS Blind Assist - Missing Requirements"*, and closes.
- **The MobiFlight WASM module** (free), for some aircraft. BA uses it to operate cockpit controls on the FlyByWire A320neo, the FlyByWire A380X, the Headwind A330-900neo, the Fenix A320, the TFDi MD-11 and the COWS Diamond DA40. Without it, many controls on those aircraft will not respond, although reading values and hearing announcements still work. On the HorizonSim 787-9 a handful of switches, such as the batteries and generators, also need it. The PMDG and iFly aircraft do not need it. Download it from the MobiFlight website's WASM module page, place it in your simulator's Community folder (the same folder your add-on aircraft live in), and restart the simulator.

<div class="callout note" role="note" markdown="1">

**Note:** On the FlyByWire A320neo, the FlyByWire A380X, the Headwind A330-900neo and the TFDi MD-11, if the module is missing, BA tells you a short while after connecting: *"MobiFlight calculator path unavailable. Some cockpit controls may not respond."* If you hear that, install or reinstall the module and restart the simulator. The Fenix A320, the COWS DA40 and the HorizonSim 787-9 do not give this warning, so on those check the module yourself if a control never responds.

</div>

Some features also need their own free accounts or keys, such as SimBrief for flight plans or an AI provider key for reading cockpit displays aloud. You do not need any of these to start; each is explained in its own chapter.

## Installing {#install}

BA has no installer. It comes as a zip file that you extract to a folder of your choice.

1. Download BA from the project's GitHub page. There are two builds:
   - **The stable release**, named MSFSBA.zip. This is the one most pilots should use.
   - **The preview build**, named MSFSBA-preview.zip. It is rebuilt every time a change is finished, so it has the newest work. Every change is reviewed and tested before it gets there, but it has had far less flying time than a release, so bugs are more likely.
2. Extract the zip to a folder you will remember, for example a folder called MSFS Blind Assist in your Documents. Avoid extracting it inside the simulator's own folders.
3. Open the folder and run **MSFSBlindAssist.exe**.

To update by hand later, close BA and extract the new zip over the same folder. Most of the time you will not need to: BA can update itself (see [Updates](#gs-updates)).

Inside the folder you will also find a folder called **HotkeyGuides**, holding the hotkey list for every aircraft as plain text files, and a **Guides** folder holding a separate manual for the FlyByWire A380X.

Only one copy of BA can run at a time. If you start it twice, you hear a message box saying *"MSFS Blind Assist is already running. Only one instance can run at a time."*

### Where BA keeps your files {#where-ba-keeps-your-files}

Everything BA stores about you lives in one folder: **%APPDATA%\\MSFSBlindAssist**. You can open it by pressing Windows+R, typing %APPDATA%\\MSFSBlindAssist and pressing Enter. Inside it are:

- **settings.json**, your settings.
- The **databases** folder, holding the navigation databases BA builds.
- The **logs** folder, holding diagnostic logs (see [Logs and Getting Help](#logs-and-help)).

Because your settings live there and not in the program folder, updating or reinstalling BA never loses them.

## Your First Launch {#gs-first-launch}

When BA starts, it opens its main window. After about two seconds you hear *"Initializing, please wait"*, and BA begins looking for the simulator. You can start BA before or after the simulator; it keeps trying until it finds it.

On the very first launch, and on every launch while something is missing, BA may open one or two setup windows before anything else. They appear one at a time, never stacked on top of each other.

### The Set up MSFS Blind Assist window {#the-set-up-msfs-blind-assist-window}

This window opens when BA is missing data it needs:

- the **navigation database** for the simulator you last connected to, which taxi guidance, teleporting, ILS guidance and flight plans all need; or
- the **location data** used by the nearest-city readout (Output C) and the Location Info window.

Focus lands on a read-only text box headed **What is missing**, which lists only what is missing. It begins *"Some data MSFS Blind Assist needs is not set up yet."* For the navigation database it says that it is not built, that building takes 2 to 5 minutes, and that for MSFS 2024 the simulator must be running at the main menu. For location data it tells you the current state, such as how much disk space the download needs.

The buttons are:

- **Download location data** (or **Update location data** if some older data is already installed). This starts the download in the background. The window stays open and the button's label changes to show progress: *Downloading location data*, then *Building location data*, then *Location data ready*. Pressing it again while a download runs does nothing; if the download fails or is cancelled, the label returns to *Download location data* and pressing it retries. While the download runs, the **Not now** button becomes **Close**, and the download carries on after you close the window, with its progress spoken.
- **Build navigation database**. This closes the window and opens the Nav Database window, described in [The Navigation Database](#nav-database).
- **Not now**, or Escape, closes the window without doing anything.

BA never remembers that you dismissed this window: it comes back on every launch until the data is there. If you would rather it did not, turn off **Check for missing data when the app starts** on the Updates tab of the Settings window.

<div class="callout tip" role="note" markdown="1">

**Tip:** While a location data download started from this window is still running, BA skips that launch's automatic update check. An update would restart the app and cancel your download. It checks again next time.

</div>

The location data and its options are covered in [Location Info and Nearest City](core-concepts.md#location-info).

## The PMDG SDK Setup Check {#gs-pmdg-sdk}

This applies only if you fly the PMDG 737 or the PMDG 777.

### What it is {#what-it-is}

PMDG aircraft share their cockpit information with outside programs through a feature PMDG call the SDK data broadcast. It is switched off unless a few lines are present in the aircraft's options file. Without those lines, BA's CDU window and many cockpit readouts for the PMDG stay empty.

BA checks this for you whenever you choose the PMDG 737 or 777 from the Aircraft menu, and when you start BA with one of them already selected. If any installed variant (for example the 737-800, or the 777-300ER) is missing the lines, a window opens titled **PMDG 737 SDK setup** or **PMDG 777 SDK setup**.

BA never changes PMDG's files itself. The window tells you exactly what to do.

### How to use it {#how-to-use-it}

Focus lands on the **Instructions** box, which your screen reader reads. Below it are:

- **Lines to add**, a read-only box containing the exact lines. For the 737 they are a line reading \[SDK\], then EnableDataBroadcast=1, EnableCDUBroadcast.0=1 and EnableCDUBroadcast.1=1. The 777 adds EnableCDUBroadcast.2=1 for its centre CDU.
- **Files to fix**, a list of each options file with a problem, such as *"737-800: no \[SDK\] section"*.
- **Copy lines**, which puts the lines on the clipboard and says *"Lines copied."*
- **Open folder**, which opens Windows Explorer on the folder of the file selected in the list, with the file already selected so that pressing Enter opens it.
- **Close**, or Escape.

The steps the window gives are:

1. Close Microsoft Flight Simulator first. PMDG rewrites this file when the simulator exits and can drop lines added while it runs.
2. For each file in the list, select it, press **Open folder**, then press Enter on the selected file to open it (usually in Notepad).
3. If the file already has an \[SDK\] section, replace it with the lines; otherwise paste them at the end. Keep only one \[SDK\] section.
4. Save the file and start the simulator.

If BA cannot find an options file at all, the instructions explain that PMDG creates it the first time you load that aircraft in the simulator. Load the aircraft once, then pick it again from BA's Aircraft menu (or restart BA) and the window will list where the file is.

Like the startup data window, this one appears every time while anything is missing.

## The Navigation Database {#nav-database}

### What it is {#what-it-is-2}

BA keeps its own database of airports, runways, taxiways, gates, navaids and instrument procedures, built from the scenery and navigation data installed in your simulator. Taxi guidance, the landing exit planner, teleporting to runways and gates, the flight planning window, approach guidance and airport lookups all read from it. Without it, those features cannot work.

There is a separate database for each simulator: one for MSFS 2020 and one for MSFS 2024. BA automatically uses the one that matches the simulator it is connected to, so if you fly both, build both.

The database is built by a tool included with BA, called navdatareader (written by Alexander Barthel, the author of Little Navmap).

### When to build it {#when-to-build-it}

- Once, before you use any of the features above.
- Again after you install, remove or update add-on airport scenery, or update your navigation data, so the database matches what you fly. BA reads your scenery at build time; it does not notice later changes on its own.
- Again if an announcement in this guide or a release note tells you a rebuild picks up a fix.

### How to build it {#how-to-build-it}

Open the window from the **File** menu, **Nav Database**. You can also reach it from the Set up MSFS Blind Assist window's **Build navigation database** button.

The window contains:

- A short explanation, including the folder where databases are stored.
- **Active database status**, for example *"Active Database: FS2020 - 41,804 airports - Navigraph data available"*, or *"Active Database: FS2024 (Not Built)"*.
- **Build FS2020 Database** and **Build FS2024 Database** buttons. Focus starts on the FS2020 button.
- **Verify Active Database**, which re-checks the active database and speaks its status.
- **Close**, or Escape.

The two simulators are built differently, and this matters:

- **For MSFS 2020, close the simulator first.** The tool reads the scenery files directly from disk.
- **For MSFS 2024, the simulator must be running and loaded to the main menu.** The tool asks the running simulator for its scenery data.

To build:

1. Close or start the simulator as described above.
2. Press the build button for your simulator. A confirmation message box reminds you of the requirement and says the build takes 2 to 5 minutes. Choose **Yes**.
3. A progress window opens, titled for example **Building FS2020 Database**, with a progress bar, a status line and a **Cancel** button. Leave it to finish. If you press Cancel, BA asks *"Are you sure you want to cancel the database build?"*
4. When it finishes, BA speaks the result, for example *"FS2020 database built successfully"*, and shows it in a message box titled **Build Complete**. If the result includes a warning, BA reads the warning in full: it matters.
5. Press **Close** to return to the main window. BA then says something like *"Database settings saved. Using FS2020 - 41,804 airports"*.

If the build fails or is cancelled, a message box titled **Build Not Completed** explains why.

### Navigraph data {#navigraph-data}

If your simulator has Navigraph's navigation data installed, the database includes it, and the status line says *"Navigraph data available"*. Otherwise it says *"No navigraph, using Microsoft's Native nav data"*. Nothing else changes: every feature simply reads the one database, whichever data it holds. If you install or update Navigraph's data, rebuild the database so BA sees it.

### The Database Mismatch Warning {#the-database-mismatch-warning}

Sometimes the simulator you are running and the database BA is using do not match — most often because you have only built the database for the other simulator. When you use a feature that depends on accurate airport positions (teleporting to a runway or gate, choosing a destination runway, or importing a SayIntentions taxi route), BA stops and opens a window titled **Database Mismatch Warning**. It says, for example, *"Warning: You are running Flight Simulator 2024 but have FS2020 database selected. This may provide incorrect runway coordinates and airport data. Would you like to switch to FS2024 database now?"*

- **Yes - Open Settings** (the default) cancels what you were doing and opens the Nav Database window so you can build the right database.
- **No - Continue Anyway** carries on with the mismatched database. Positions may be wrong, so use this only if you understand the risk.
- **Cancel** stops what you were doing and changes nothing.

### If something goes wrong {#if-something-goes-wrong}

- **The FS2024 build fails or finds nothing.** Make sure MSFS 2024 is fully loaded to its main menu, not still on a loading screen, before you press the button.
- **The FS2020 build fails.** Make sure MSFS 2020 is completely closed.
- **An add-on airport has the wrong gate names or is missing.** Rebuild the database after installing the scenery. If the gates are still the simulator's default ones, see [Gates, GSX and Docking](gates-gsx-and-docking.md): some GSX add-ons can affect stand names, and BA's build guards against this.

## Connecting to the Simulator {#gs-connecting}

BA connects to the simulator on its own. You do not press a connect button.

1. Start the simulator and load a flight. You can do this before or after starting BA.
2. While BA is looking, its main window's status line reads **Waiting for simulator**, and BA tries again every few seconds.
3. Once the flight is loaded and BA has identified the aircraft, you hear *"Connected to"* followed by the aircraft's title from the simulator, and its airline and flight number or its registration if the simulator has them. Then BA says which BA profile is active, for example *"FlyByWire A320neo Profile and panels active"*. The status line shows the same text.

For about five seconds after connecting, BA holds back its automatic announcements. This stops it from reading out every light and system state at once when you connect to an aircraft that is already set up. Hotkeys you press still speak straight away.

If you press a hotkey before BA has finished connecting, you hear *"Not connected to simulator, please wait"*.

If the simulator closes or the connection drops, BA says *"Disconnected from simulator"* and goes back to looking for it. When you load a flight again, it reconnects by itself.

## Choosing Your Aircraft {#gs-aircraft}

BA has a separate profile for each aircraft it supports: its own panels, hotkeys and announcements. BA does not switch profiles for you when you load a different aircraft in the simulator. Choose the matching profile from the **Aircraft** menu. BA remembers your choice and uses it the next time it starts. The first time you run BA, the FlyByWire A320neo is selected.

The Aircraft menu lists the nine supported aircraft, in this order:

1. FlyByWire Airbus A320neo
2. FlyByWire Airbus A380X
3. Headwind Airbus A330-900neo
4. Fenix A320 CEO
5. PMDG Boeing 737 (the 737-600, -700, -800 and -900)
6. PMDG Boeing 777
7. HorizonSim Boeing 787-9
8. iFly Boeing 737 MAX8
9. TFDi Design MD-11

To open the menu, press Alt, then use Right Arrow to reach Aircraft and Down Arrow to move through the list; the current aircraft is marked as checked. Press Enter on the one you are flying. You hear *"Switched to"* and the aircraft name, and the main window's Sections list fills with that aircraft's sections.

<div class="callout note" role="note" markdown="1">

**Note:** If the profile you choose does not match the aircraft loaded in the simulator, panels and hotkeys will not work properly. Always pick the profile for the aircraft you actually loaded.

</div>

Each aircraft has its own chapter in Part 7: see [Airbus Aircraft](airbus-aircraft.md) and [Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md).

## The Main Window {#main-window}

The main window is where you operate the aircraft's panels. Take a few minutes to learn it before your first flight; everything else builds on it.

### Layout {#layout}

From top to bottom and left to right, the window contains:

- **The menu bar**, with four menus: **File**, **Aircraft**, **Extras** and **Help**. Press Alt to reach it.
- **The connection status line**, which reads *Waiting for simulator*, *Connected to …*, or *Disconnected from simulator*. It is a label, so you do not Tab to it; your screen reader can read it with its read-window or review commands.
- **The Sections list** (its screen reader name is *Aircraft sections*). This lists the main areas of the cockpit for your aircraft, such as Overhead, Glareshield, Main Instrument Panel and Pedestal. The exact sections depend on the aircraft.
- **The Panels list** (named *Panel list*). This lists the panels inside the section you selected. On the overhead, for example, you might find Electrical, Fuel, APU and Lights.
- **The panel controls area**. This holds the controls of the panel you selected, each with a label in front of it, and often a Status Display at the bottom.

The window title is normally *MSFS Blind Assist*. On some aircraft it also shows the current flight phase.

### Moving around {#moving-around}

The Tab key moves from the Sections list, to the Panels list, to the first control in the panel, and on through the controls. Shift+Tab goes back. Three shortcuts jump directly, as long as the main window has the focus and you are not in Input mode:

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| Ctrl+1 | Focus the Sections list |
| Ctrl+2 | Focus the Panels list |
| Ctrl+3 | Focus the Status Display of the current panel (and fill it if it is empty) |
| F5 | Refresh the current panel's Status Display without moving away from where you are |

</div>

A typical way to operate something:

1. Press Ctrl+1, and arrow up or down to the section, for example Overhead.
2. Press Tab (or Ctrl+2) to reach the Panels list, and arrow to the panel, for example Lights.
3. Press Tab to move into the controls, and keep pressing Tab to reach the control you want.
4. Change it (see below). Your screen reader tells you the new value.

If there is no Status Display on the panel you are in, Ctrl+3 says *"No status display on this panel."*

### The kinds of controls you will meet {#the-kinds-of-controls-you-will-meet}

Each control in a panel has a label ending in a colon, followed by the control itself. You will meet these types:

- **Combo boxes (drop-down lists).** Most switches and selectors are combo boxes, one item per position: for example Off, On; or Off, Auto, On. Arrow up and down to choose a position. **The aircraft switch moves as soon as you arrow onto a new position**; there is no separate confirm step. Your screen reader reads the new position, and BA does not repeat it.
- **Buttons.** Momentary push buttons and one-shot actions are buttons. Press Space or Enter to press them. Some buttons show their current state in their name, for example *"LNAV: Off"*, so you can hear the state before you press.
- **Text boxes.** Controls that take a number, such as a frequency or a value, appear as a text box; your screen reader says *"Type a value and press Enter to set"*. Type the value and press Enter. There is no separate Set button: Tab goes straight to the next control. Decimal values can be typed with either a comma or a full stop, whatever language Windows uses. Do not type thousands separators: a value such as 10,000 or 10.000 is refused rather than guessed at, so type 10000. Where it helps, BA confirms the value it set, because that is something your screen reader cannot tell you.
- **Read-only status fields.** Some rows are not controls at all but readouts, such as an annunciator, a door state or a gauge value. They are read-only text boxes: Tab to them to hear the current value. You cannot change them.
- **Sliders.** A few continuous controls, such as some dimmers or levers, are sliders. Use the arrow keys for small steps, and Page Up and Page Down for larger ones.

### The Status Display {#the-status-display}

Many panels end with a **Status Display**: a read-only list (its screen reader name is *Status display, updates live (F5 to refresh now)*) that shows live readings for that panel, one per row — for example voltages, pressures, temperatures, quantities, or the pages of a system display. Below it is a **Refresh** button.

- Arrow up and down to read each row.
- The list updates itself while you read it. Only the row whose value actually changed is re-read, and your position in the list stays where it is, so you can sit on one row and listen to it change.
- Press F5 at any time to refresh it immediately, or press the Refresh button.
- Press Ctrl+3 from anywhere in the main window to jump straight to it.

On the Airbus aircraft in particular, Status Displays are where you read much of what a sighted pilot reads off the cockpit screens.

### When the simulator is not connected {#when-the-simulator-is-not-connected}

You can browse sections, panels and controls before BA connects, but controls have no live values yet and changing them has no effect. Values appear once BA connects.

## Output Mode and Input Mode {#hotkey-modes}

This is the most important idea in BA, and the one that beginners most often find confusing at first. Read this section slowly, and try each step as you go.

### Why there are two modes {#why-there-are-two-modes}

BA's hotkeys have to work while the simulator has the focus, and the simulator itself uses almost every key on the keyboard. If BA permanently took over keys such as A, S or H, you could no longer use them in the simulator. So BA owns only two keys all the time: the two square bracket keys. Pressing one of them puts BA into a **mode** for a moment, during which the other keys mean BA commands. After one command, the mode ends and your keyboard goes back to normal.

- **Output mode** is for reading things out. You enter it with the **right bracket key, \]**. BA says *"output"*.
- **Input mode** is for doing things: opening windows, setting the autopilot, teleporting and so on. You enter it with the **left bracket key, \[**. BA says *"input"*.

A simple way to remember it: Output is information coming out of the aircraft to you; Input is you putting something into the aircraft.

### How to use a mode {#how-to-use-a-mode}

1. Press \] (for Output) or \[ (for Input). You hear *"output"* or *"input"*.
2. Press the command key or key combination, for example A, Shift+H or Ctrl+T.
3. BA carries out the command, and the mode ends by itself.

For example, to hear your altitude: press \], hear *"output"*, then press A. In this guide that is written "Output A"; "Input Shift+R" means press \[, then Shift+R.

### Cancelling a mode {#cancelling-a-mode}

If you pressed a bracket key by mistake, or changed your mind:

- Press the **same bracket key again**. BA says *"cancelled"*, and your keyboard is back to normal.
- Or, if BA's own window has the focus, press **Escape**. BA says *"cancelled"*.

The mode does not time out. It stays on until you give it a command, cancel it, or press Escape. If you press a key that is not a command in that mode, nothing happens and the mode stays on, so if you are not sure whether you are still in a mode, press the bracket key again: if you hear *"cancelled"*, you were.

While you are in one mode, the other bracket key does nothing. Cancel the first mode before entering the other.

<div class="callout warning" role="note" markdown="1">

**Warning:** While a mode is active, BA has taken over its command keys across the whole computer. If you then try to type in the simulator or another program, those letters will trigger BA commands instead. If keys seem to stop working, a mode is probably still active: press the bracket key you last used to cancel it.

</div>

### Keys that work without a mode {#keys-that-work-without-a-mode}

A few keys are not part of either mode:

- Ctrl+1, Ctrl+2, Ctrl+3 and F5 work only inside BA's main window, as described in [The Main Window](#main-window).
- Some features, such as Hand Fly mode and visual guidance, turn on a small set of instant keys for as long as they are active, so you can read key values with a single press. Those are explained in their chapters, starting with [Hand Fly Mode](takeoff-climb-and-cruise.md#hand-fly).

### Suspending hotkeys {#suspending-hotkeys}

If another program needs the bracket keys — for example while you type a chat message to ATC — open the **File** menu and choose **Suspend Hotkeys**. BA says *"Hotkeys suspended"* and stops listening to the brackets entirely. Choose it again to turn hotkeys back on; you hear *"Hotkeys resumed"*. If BA cannot take the keys back because another program is now using them, it warns you: *"Warning: failed to re-register hotkeys. Another application may be using the bracket keys."*

### Different aircraft, different keys {#different-aircraft-different-keys}

The two modes are the same on every aircraft, and many keys are shared. Output A, S and H read altitude, indicated airspeed and heading on every supported aircraft. But each aircraft also has its own keys for its own systems, and the same key can do different things on different aircraft. The Hotkey List (next section) always shows the keys for the profile you have selected.

<div class="callout note" role="note" markdown="1">

**Note:** BA listens for the keys Windows calls the bracket keys on a US or UK keyboard (the two keys to the right of P). On some other keyboard layouts Windows gives those codes to different physical keys. If pressing your bracket keys never turns a mode on, try the two keys to the right of P, or ask for help on the Discord.

</div>

## The Hotkey List {#gs-hotkey-list}

### What it is {#what-it-is-3}

The Hotkey List shows every hotkey for your current aircraft profile, grouped into categories, with Output mode keys and Input mode keys clearly separated. It is the quickest way to find a key while you are learning.

### How to use it {#how-to-use-it-2}

1. Open the **File** menu and choose **Hotkey List**.
2. The window opens with focus on the **Search** box (named *Search by category or keyword*). Type a word such as fuel, autopilot, taxi or heading, and the list below shows only the matching entries. You can also arrow through the box to pick a category; the first choice, **All Categories**, shows everything.
3. Press Enter in the Search box to jump into the list itself.
4. Shift+Tab back to the **Show** box (named *Show Hotkey Mode*) to show **All hotkeys**, only **Output hotkeys**, or only **Input hotkeys**.
5. Read the list with the arrow keys. It is a read-only text box, so you can also use your screen reader's normal reading commands.
6. Press Ctrl+F at any time to return to the Search box, and press **OK** or Escape to close the window.

If nothing matches, the list says so, for example *No hotkeys match "fuel" in input hotkeys.*, which tells you to try the other mode.

The same lists are in the HotkeyGuides folder inside BA's program folder, as text files you can open in any editor, and the full set for every aircraft is reproduced at the end of this guide in the [Hotkey Reference](hotkey-reference.md).

## The Menus {#gs-menus}

BA's menu bar has four menus. Press Alt to reach it, then use the arrow keys.

### The File menu {#the-file-menu}

- **Nav Database…** opens the window for building and checking navigation databases. See [The Navigation Database](#nav-database).
- **Settings…** opens the Settings window. See [The Settings Window](#settings-overview).
- **FMC Settings** appears only for the PMDG aircraft, the Fenix A320 and the HorizonSim 787-9. See [FMC Settings](planning-your-flight.md#planning-fmc-settings).
- **Hotkey List** opens the Hotkey List, described above.
- **Sim Performance** opens the Sim Performance window. See [Sim Performance](troubleshooting-and-faq.md#gs-sim-performance).
- **Suspend Hotkeys** turns the bracket keys off and on again. See [Suspending hotkeys](#hotkey-modes).
- **Update Application** checks for a newer version straight away. See [Updates](#gs-updates).

### The Aircraft menu {#the-aircraft-menu}

Lists the nine supported aircraft, with the current one checked. See [Choosing Your Aircraft](#gs-aircraft).

### The Extras menu {#the-extras-menu}

Holds tools that work alongside the simulator. It currently has one item, **Volanta…**, which opens a text view of Volanta's live map: Volanta users and network pilots around you, your friends, and online ATC. You can also open it with Output Shift+O. It is described in [Volanta](networks-and-integrations.md#volanta).

### The Help menu {#the-help-menu}

- **User Guide** opens this guide in your web browser, all on one page and matching the version of BA you have installed, so it works without an internet connection. If that copy is missing, as it is when you build BA yourself, BA says so and opens the online guide instead. See [Finding Your Way Around This Guide](welcome.md#gs-using-guide).
- **Project Website** opens msfsblindassist.com, where you can download BA and read the guide online.
- **About** shows the BA version you are running, the exact build, and the author. Press **OK** or Escape to close it.

## The Settings Window {#settings-overview}

Open the Settings window from the **File** menu, **Settings…**. It holds nearly every option in BA, organised into tabs. Each tab is explained in full in the chapter for its feature; this section gives you the map.

### How to use it {#how-to-use-it-3}

- The window opens on the first tab, **Announcements**. Press Ctrl+Tab to move to the next tab and Ctrl+Shift+Tab for the previous one, or Tab to the tab list and use the arrow keys.
- Within a tab, use Tab and Shift+Tab to move between options.
- Press **OK** to save every tab at once. BA applies the changes straight away, without a restart, and says *"Settings saved"*. If something on a tab is not valid, BA switches to that tab, explains the problem in a message box, and puts you on the field to fix; nothing is saved until you do.
- Press **Cancel** or Escape to close without saving anything.

### The tabs {#the-tabs}

<div class="table-wrap" markdown="1">

| Tab | What it covers |
|---|---|
| Announcements | Whether BA speaks through your screen reader or through Windows speech, and general announcement options: the nearest-city auto-announce, seconds in time readouts, GSX tooltips, 1,000-foot crossings. |
| Weather | ActiveSky integration, and the automatic weather, turbulence, icing and SIGMET/AIRMET announcements. |
| VATSIM | Announcing VATSIM activity from vPilot. |
| GeoNames | Downloading the offline location data used by Output C and Location Info, and the distance ranges and units it uses. |
| SimBrief | Your SimBrief username, for loading flight plans. |
| AI | Your AI provider (Gemini or Claude), its key and model, for reading cockpit displays and describing scenery and routes. |
| Audio | Which sound device BA's guidance tones play through. |
| Hand Fly | Hand-flying feedback, visual guidance tones, Takeoff Assist and the rollout centreline tone. |
| Taxi Guidance | The taxi steering tone, taxi callouts, distance units, docking guidance, online taxiway names, airport buildings, and SayIntentions taxi import. |
| Updates | Release or preview builds, automatic update checks, the startup data check, and the version you are running. |

</div>

The Announcements tab, including the screen reader or Windows speech choice, is described in [Announcement Settings](core-concepts.md#core-announcement-settings).

## Updates {#gs-updates}

### What it is {#what-it-is-4}

BA can find, download and install new versions for you from GitHub, without you visiting the website.

### How it works {#how-it-works}

When **Check for updates automatically when the app starts** is on (it is on by default), BA checks once, just after its main window appears. The check never delays startup and is completely silent when there is nothing new, or when it cannot reach the internet. If an update is available, BA says, for example, *"Update available: version 8.1.0"*, and opens the update window. It does this on every launch while an update is waiting; turn the check box off if you would rather check by yourself.

To check at any time, choose **Update Application** from the File menu. BA says *"Checking for updates..."*. If you are up to date, it tells you, for example *"You are running the latest version (8.0.0)."* If the check fails, it tells you why.

### The Update Available window {#the-update-available-window}

The window, titled **Update Available**, shows:

- A heading, *"A new version of MSFS Blind Assist is available!"*
- **Current version**, the one you are running.
- **Latest version**, the one on offer.
- **Release Notes**, a formatted document describing what is new for you, with headings you can jump between, real lists, and links that open in your web browser.
- **Update Now** and **Cancel** buttons.

Press **Update Now** to download the update. The status line shows the download's progress, then *"Download complete. Ready to install."* BA says *"Launching updater. Application will close and restart."*, closes, installs the update into the same folder, and starts again. Your settings and databases are not touched.

### Release builds and preview builds {#release-builds-and-preview-builds}

On the **Updates** tab of the Settings window, under **Which builds should MSFS Blind Assist offer?**, you can choose:

- **Release builds (recommended)**, the default. You are offered only full, tested releases.
- **Preview builds**. You are also offered the preview build, which has the newest changes as soon as they are finished. When you choose it, BA explains the trade-off and asks you to confirm. On the preview channel you are always offered whichever is newer, the preview or the release, so you can never miss a real release.

To go back from a preview to the release, set the channel back to **Release builds**, then choose **Update Application** from the File menu. BA offers the current release even though its version number is lower than your preview. The window is then titled **Return to Release Build**, says so plainly, and its button reads **Install Release**.

<div class="callout note" role="note" markdown="1">

**Note:** A newer build can store settings an older build does not know about. If you go back to an older build, those particular options are reset to their defaults; everything else is kept.

</div>

The Updates tab also shows **Version you are running**, the exact version and build. Include it whenever you report a problem.

## Logs and Getting Help {#logs-and-help}

### Logs {#logs}

BA writes diagnostic logs as it runs. They do not affect how it flies, but they are what lets the developers understand a problem you report. All logs are in one folder:

**%APPDATA%\\MSFSBlindAssist\\logs**

To open it, press Windows+R, type %APPDATA%\\MSFSBlindAssist\\logs and press Enter. The main log is **debug.log**. Others cover particular features, for example **startup.log** (replaced on every launch), **taxi\_guidance.log**, **taxi\_router.log**, **landing\_exit.log**, **takeoff\_assist.log**, **ground\_traffic.log**, **gsx.log** and **sayintentions.log**. Logs are kept to a sensible size automatically, with older entries rotated away.

### Reporting a problem {#reporting-a-problem}

When something does not work as this guide describes:

1. Note what you did, what you expected, and what happened instead — including what BA said.
2. Note your BA version and build. It is on the Updates tab of the Settings window and in Help, About, for example *v8.0.1-pre.7 (build 4f7e7ba)*. The build part names the exact code you were running.
3. Note the aircraft and the simulator (2020 or 2024), and the airport if it is a ground problem.
4. Collect the logs folder. The easiest way is to zip the whole **%APPDATA%\\MSFSBlindAssist** folder, which contains your settings and logs together. If that is too large because of the databases, zip just the logs folder.
5. Share it with the BA community on the project's Discord server, or on the project's GitHub page.

Before reporting, it is worth checking [Troubleshooting and FAQ](troubleshooting-and-faq.md) in the appendices, and the "If something goes wrong" part of the feature's own section.

### Getting help {#getting-help}

The BA community meets on Discord, where you can ask questions, get help setting up, and talk with other blind pilots. The invitation link is in the README on the project's GitHub page. The [Glossary](glossary.md) explains aviation and BA terms you may meet in this guide.

## Your First Ten Minutes {#gs-quick-start}

If you want to try BA straight away, follow these steps. Each links to the section with more detail.

1. Install the .NET 10 Desktop Runtime and, if your aircraft needs it, the MobiFlight WASM module. See [What You Need](#gs-requirements).
2. Extract BA and run MSFSBlindAssist.exe. See [Installing](#install).
3. If the Set up MSFS Blind Assist window opens, start the location data download, then build the navigation database for your simulator. Remember: close MSFS 2020 before building its database; have MSFS 2024 running at its main menu before building its database. See [The Navigation Database](#nav-database).
4. Start the simulator and load a flight, parked at a gate, in an aircraft BA supports.
5. In BA, open the Aircraft menu and choose that aircraft. You hear *"Switched to"* and its name.
6. Wait for *"Connected to"* and *"Profile and panels active"*.
7. Press \], hear *"output"*, then press A. BA reads your altitude. Try Output H for heading and Output S for airspeed.
8. Switch to BA's window (Alt+Tab), press Ctrl+1, and arrow through the Sections list. Tab into the Panels list and then into a panel's controls to explore. Press Ctrl+3 to jump to a panel's Status Display.
9. Open File, Hotkey List, and search for something you want to do.

When you are comfortable with those, read [Core Concepts](core-concepts.md) to learn what BA announces and what its tones mean, then follow the guide through [Planning Your Flight](planning-your-flight.md) and on to the ground.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Welcome and How to Use This Guide](welcome.md)
- [Next chapter: Core Concepts](core-concepts.md)
- [Guide contents](index.md)

</nav>
