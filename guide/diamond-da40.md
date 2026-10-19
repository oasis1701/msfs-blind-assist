---
title: "COWS Diamond DA40"
---

<div class="guide-part" markdown="1">

Chapter 3 of 3 in [Part 7: Aircraft Guides](index.md#part-7-aircraft-guides) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Before you fly](#da40-before)
- [How the panels are organised](#da40-panels)
- [The Status Display](#da40-status)
- [The G1000 windows](#da40-g1000)
- [Radios, transponder and altimeters](#da40-radios)
- [The GFC 700 autopilot](#da40-autopilot)
- [Reading the engine and the aeroplane](#da40-readouts)
- [The DA40-NG](#da40-ng)
- [The DA40-XLS](#da40-xls)
- [Checklists](#da40-checklists)
- [Failures and simulation](#da40-simulation)
- [Things worth knowing](#da40-quirks)

</nav>

BA supports both COWS Diamond DA40 models, the **DA40-NG** (Austro diesel engine with a FADEC: one power lever, no mixture, no propeller lever) and the **DA40-XLS** (Lycoming IO-360 with mixture, propeller and magnetos). Both have the Garmin G1000 and the GFC 700 autopilot. Everything in the earlier chapters works on the DA40; this chapter covers what is different. The complete key list is in the [Hotkey Reference](hotkey-reference.md#hk-da40) and in File, Hotkey List.

## Before you fly {#da40-before}

You need the COWS DA40 1.2.0 or newer and the MobiFlight WASM module, which BA uses for the aircraft's own variables. Pick **COWS Diamond DA40-NG** or **COWS Diamond DA40-XLS** from the Aircraft menu. You do not have to pick the right one: when the simulator loads a DA40, BA switches to the model that is loaded, NG or XLS, and keeps following it if you load the other.

After a flight loads, BA waits a few seconds before it speaks the aircraft's switches, so you do not hear every switch that differs from your last flight.

## How the panels are organised {#da40-panels}

Press Ctrl+1 for the Sections list and Ctrl+2 for the Panels list. The controls are named exactly as the placards in the cockpit name them.

- **Instrument Panel**: Electrical, Engine Start, ECU (NG) or Magnetos (XLS), Lighting Switches, Ice and Pitot, Standby Instruments, Annunciators, Radios, ELT.
- **Center Console**: Power and Levers, Mixture and Propeller (XLS), Fuel System, Priming (XLS), Flaps, Elevator Trim, Flight Controls, Brakes, Cabin Heat and Vent, Audio.
- **Circuit Breakers**: all 34 breakers, in six groups (Engine and Fuel, Flight Instruments, Avionics, Bus and Power, Lighting, Airframe Systems). Each one can be pulled and pushed back in.
- **Autopilot**: GFC 700 and Flight Director.
- **Cabin**: Doors and Windows, Seating and Payload.
- **Simulation**: the aircraft's whole failure set, engine damage, the reset, and the COWS options.

There is no G1000 panel: the PFD and the MFD have their own windows (see [The G1000 windows](#da40-g1000)), because everything on them is already reachable there.

## The Status Display {#da40-status}

### What it is {#da40-status-what}

On this aircraft the Status Display is the main way to read the aeroplane. Every panel has one, holding what a glance at that part of the real panel would take in: the whole electrical picture, the whole engine picture, the fuel, the breakers. A gauge with a coloured arc says its arc with its value, for example *"87 celsius, green"*, because the arc is what a sighted pilot actually sees.

### How to use it {#da40-status-how}

Press Ctrl+3 to move to the Status Display of the panel you are on, and read it with the arrow keys. F5 refreshes it without moving you from your place in it. It also refreshes itself every second.

### What you will hear {#da40-status-hear}

Switches and selectors speak when they change outside BA (moved in the cockpit, by hardware or by a failure). Numbers such as volts, temperatures and RPM do not speak, because they change all the time; read them from the Status Display. Use the [Monitor Manager](core-concepts.md#monitor-manager), Output Ctrl+M, to silence anything you would rather not hear.

## The G1000 windows {#da40-g1000}

### What they are {#da40-g1000-what}

The PFD and MFD windows are not summaries: they are the displays themselves, read a line at a time, with the bezel on your keyboard. Output **Alt+P** opens the PFD. Output **Alt+M** or **Alt+N** opens the MFD, and so does Input **Shift+M**, the key that opens the MCDU on the airliners, because the G1000 keeps the flight plan, Direct-To and procedures on the MFD.

### How to use them {#da40-g1000-how}

- Arrow keys read the window; Enter presses the softkey on the selected row.
- **Shift+Enter** turns the FMS cursor on and off. Nothing on a page can be changed until it is on.
- **Ctrl+Left** and **Ctrl+Right** turn the large FMS knob: between fields with the cursor on, between page groups with it off.
- **Ctrl+Up** and **Ctrl+Down** turn the small FMS knob: change the field you are on (or open its list), or step the page with the cursor off.
- **Ctrl+Enter** is ENT. **Ctrl+D** Direct-To, **Ctrl+F** Flight Plan, **Ctrl+P** Procedures, **Ctrl+E** Menu, **Ctrl+L** Clear.
- **Ctrl+G** lists every page and opens the one you pick. On the PFD it lists its windows, five of which have no bezel button at all.
- **Ctrl+T** types an ident straight into the waypoint box under the cursor; the aircraft completes it and BA reads back the facility it found.
- **Ctrl+Shift+L** closes a window that has opened over the page, such as a waypoint information window or a duplicate-ident list. While one is open the bezel buttons under it do nothing, and CLR does not close it.
- **Ctrl+Page Up** and **Ctrl+Page Down** change the map range. **F5** refreshes, **Escape** closes.

To load a SimBrief plan, use the G1000's own Flight Plan Catalog on the MFD, as a sighted pilot does; BA reads every step of it.

### What you will hear {#da40-g1000-hear}

The CAS messages (warnings, cautions and advisories) are spoken as they appear, whether or not a window is open, and Output **Alt+E** reads the ones showing now. Mode changes on the flight mode annunciator are spoken as they happen, naming the autopilot's modes when it is engaged and the flight director's when it is not. When the flight plan sequences you hear the waypoint, for example *"Passing SOXOM. Next VEKIN, 14 miles."*; BA never makes the radio call for you.

## Radios, transponder and altimeters {#da40-radios}

The G1000 tunes on the PFD, so the radio keys work in the PFD window:

- **Alt+Up/Down** and **Alt+Left/Right**: COM megahertz and kilohertz. **Ctrl+Alt** with the same arrows: NAV.
- **Alt+Enter** and **Ctrl+Alt+Enter** move the tuning box between radio 1 and radio 2.
- **Alt+Shift+Enter** swaps the COM standby into active, **Ctrl+Alt+Shift+Enter** the NAV. Tuning changes only the standby frequency: without the swap you are still on the old one.
- **Ctrl+Shift+Up/Down** turns the baro knob one step.

The frequency speaks itself when the knob comes to rest. The **Radios** panel keeps a typed entry for when you already know the frequency, and Input **Ctrl+N** sets both NAV standby frequencies in one window.

Input **Ctrl+B** sets both altimeters, the G1000's and the standby, from one entry, in hectopascals or inches. Output **B** reads both. The transponder is set with the PFD's XPDR softkeys; Output **X** reads the code and mode.

## The GFC 700 autopilot {#da40-autopilot}

- The **GFC 700** panel has every mode button, the selected values, the course and heading bug, the go-around button and the pre-flight test's state.
- Input **Ctrl+P** opens the autopilot buttons window: Autopilot, Flight Director and every mode, each showing its state.
- Input **Ctrl+A**, **Ctrl+S**, **Ctrl+H** and **Ctrl+V** open a value window for the selected altitude, airspeed, heading bug and vertical speed, with the modes that use that value beside it.
- Output **Shift+A**, **Shift+S**, **Shift+H** and **Shift+V** read the selected values; plain A, S, H and V read where the aircraft is.

There is no autothrottle. The autopilot's pre-flight test runs when the avionics come on and is announced, with its result. If the autopilot refuses a mode you pressed, you hear the control's name and *"no change"*, so a refused press is never silent. The GFC 700 trims for itself while engaged, silently.

## Reading the engine and the aeroplane {#da40-readouts}

These output keys answer at once, in this aeroplane's own terms.

<div class="table-wrap" markdown="1">

The table lists the DA40's own output keys and what each reads.

| Key | What it reads |
|---|---|
| Alt+S | The engine at a glance: every engine gauge with its arc, plus volts and amps. |
| Alt+I | The standby instruments: airspeed, altitude, compass and attitude, with a warning if the gyro is caged or has toppled. |
| P | RPM (the NG also says what the governor is targeting when it differs). |
| E | Power: NG load, RPM and power lever; XLS manifold pressure, RPM, throttle and mixture; both with fuel flow. |
| Shift+P | Engine temperatures: NG oil, coolant and gearbox; XLS oil, hottest cylinder head and hottest exhaust. |
| F | Fuel in each tank and the total. |
| Shift+F | Fuel flow, endurance, and the difference between the tanks against the AFM limit. |
| B | Both altimeter settings. |
| L | Flaps: UP, T/O or LDG. |
| Shift+G | "Fixed gear". |
| W, Shift+W | Weight and centre of gravity. |
| Shift+2 to Shift+6 | The AFM's speeds: rotate and climb, approach and glide, the flap limits, manoeuvring and never-exceed. |
| Ctrl+W | The waypoint you are flying to: ident, distance, bearing and time. |
| D, Shift+D | Distance and time to the destination, and to the top of descent. |

</div>

Shift+F does not match the MFD's fuel totalizer, and is not meant to: the totalizer is a figure you set yourself on the Engine page, which then counts down by fuel used. F and Shift+F read the tanks.

## The DA40-NG {#da40-ng}

- **Power and Levers**: the single power lever, as a percentage of travel. Output E and P say the load and the RPM the FADEC commands.
- **Engine Start**: the electric master, the engine master and its guard, the start key (hold it and release it, as in the real aeroplane), and the glow state: on a cold engine wait for the glow indication to go out before you crank.
- **ECU**: the ECU voter (ECU A, AUTO, ECU B) and the ECU test, which BA holds for you until the test completes, about 30 seconds, saying first any condition that is not met. With the engine master off, the same button resets latched failures and charges the batteries.
- **Fuel System**: the fuel pumps, the transfer pump and the fuel valve with its safety wire. The fuel gauge stops at 14 gallons, and BA says so rather than reading it as a quantity.

## The DA40-XLS {#da40-xls}

- **Power and Levers**: throttle, propeller and mixture.
- **Mixture and Propeller**: **Set Best Mixture**, which runs the aircraft's lean assist and says where the mixture settled, and **Cycle Propeller** for the run-up. You can also lean by ear: type a mixture value and listen to Output Shift+P and E.
- **Magnetos**: the ignition key (OFF, R, L, BOTH, START). During a magneto check BA says the RPM drop on each side.
- **Priming**: how far the engine is primed and whether it is flooded, with Clear Flooding.
- **Fuel System**: the fuel selector (LEFT, RIGHT, OFF) and the electric fuel pump.
- **Engine Start**: the aircraft's own auto-start and auto-shutdown, narrated step by step if you use them.

## Checklists {#da40-checklists}

Output **Shift+C** opens the DA40's own checklist, read from the COWS package: every procedure page, the weights and speeds, and the Tips and help page, where COWS writes down its operating advice (warm-up times, power settings for the circuit, how to reset failures). The checklist follows the aircraft through COWS updates.

## Failures and simulation {#da40-simulation}

The **Simulation** section holds the aircraft's whole failure set, grouped as COWS groups it, with every failure the random picker can raise, the breaker trips, engine damage and its reset, and the COWS options (also on the MFD's Engine page menu). A failure graded from 0 to 100 is spoken when it starts and when it gets worse. Engine damage survives a reload, as in the aircraft; the reset is in the same section.

## Things worth knowing {#da40-quirks}

- **Held controls.** The ECU test, the standby horizon cage and the fuel valve's safety wire are held, not clicked, and BA holds them for the right time. Press once and let it run.
- **Silent switches.** BA's panel controls make no cockpit sound. Confirm a change by reading it back.
- **Take-off trim.** Press Centre Trim on the Elevator Trim panel: centred is the take-off position on this aeroplane, and the AFM gives no number to calculate.
- **Refuelling.** There is no fuel panel in the cockpit, so the Fuel System panel offers the transaction: type the gallons for each tank, or Fill Both Tanks. On the ground with the engine stopped only. With GSX, its own refuelling is the more realistic way.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Boeing Aircraft and the MD-11](boeing-aircraft-and-the-md-11.md)
- [Next chapter: General Flying Concepts](general-flying-concepts.md)
- [Guide contents](index.md)

</nav>
