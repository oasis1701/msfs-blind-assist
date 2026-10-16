Support for the **Synaptic Simulations A220-300**.

Panels for the overhead, pedestal, lighting, fuel, hydraulics, air and electrical
systems; the flight-control panel as set dialogs (speed, heading, altitude, vertical
speed, baro, autopilot) with every mode button its physical control has; readout
hotkeys for the FCP windows, flight director, approach capability, trim, fuel, flaps
and gear; final-approach pilot-monitoring callouts; and a Ctrl+M Announcement Monitor
so you can mute anything that talks too much.

The FMS (Shift+M), the electronic checklist (Ctrl+Shift+C) and the EFB (Shift+T) are
read and driven over the simulator's own display link, so the pages that exist only as
drawn graphics in this aircraft are presented as text you can arrow through, with
entry fields you can type into and checklist items the app can action for you.

The altitude selector now reaches the altitude you asked for. It could previously stop
up to 500 ft short and announce that it had failed — most visibly after the selector
had been left in metres, where "set 5000" would report *"selector reads 5085 feet"* for
a target one click away. The walks also no longer need the display link to be up: the
selected altitude, heading and speed are read back from the simulator directly when it
is not.

Ctrl+N opens an A220 radios window: the captain's nav source, course and tuned
frequency, both NAV radios with their preset and AUTO/MANUAL tuning, and entries to set
the course, the NAV presets, the tuning mode and the nav source. The FMS still tunes an
approach's ILS itself on AUTO.

The FMS navigation database can be updated from the EFB window: its "FMS navigation
database" button signs you in to Navigraph (reading out the code and opening the page
in your browser), lists the cycles available to you, loads one into both FMSs and says
which database the FMS is using. The EFB tablet's own update button does nothing in this
version of the aircraft.

The ground power unit can be attached and removed from the electrical panel, and when
external power does not connect you are told why — most often that the beacon is on,
which makes the EFB remove the ground power unit.

The FMS fuel page no longer loses a field after a refused entry, reads each value once
under its own name, says the accepted range when an entry is refused, and tells a
prediction from a constraint on the legs page. Constraints can be typed straight onto a
procedure waypoint, and Direct To reaches every leg in its list.

The crew alerting system is now read from the aircraft itself. Alt+E opens a window with
every warning, caution, advisory and status message, warnings first, refreshed live. New
warnings and cautions are spoken as they appear, advisories after them, and on
connecting you hear one line with the ones already showing. Ctrl+E turns the call-outs
off and on.

Gear, engine starts, IRS alignment and the doors are announced as they change ("Gear
down and locked", "Engine 2 start complete", "IRS aligned", "Forward passenger door
open"), each with its own Ctrl+M checkbox. The gear readout (Shift+G) adds the brake
temperatures, and the Utilities section gains Doors and IRS panels.

Shift+1 to Shift+6 read V1, VR, V2, VREF, VAC and the flap speeds from the PFD, and
"Rotate" is now called on time at VR and "V2" at V2, as on the other airliners (the
aircraft calls V1 itself); each has its own row in Ctrl+M. Starting a take-off roll with V1
or VR not set tells you so once, and Rotate is not called.

Ctrl+B is now "Altimeter and minimums": QNH in hectopascals or inches, the unit, Standard,
and the BARO or RADIO minimums, for both sides or one, each read back from the aircraft.
B reads the altimeter as the PFD shows it, including Standard. The same settings are on
the CTP panels.

The Transponder panel sets the mode (AUTO, STBY, TA/RA, TA ONLY, ALT ON, ALT OFF), the
TCAS altitude limit, the squawk code, IDENT and the flight ID, each read back; X reads
the squawk and mode.

The autobrake has its real five positions, RTO, OFF, LO, MED and HI, and a position the
aircraft refuses is said. The master caution/warning cancel now clears the lamps, and
external power retries once when a press is lost.

D and Shift+D read the distance and time to destination and the distance to top of
descent from the FMS's active flight plan, Ctrl+W the next waypoint, and the Hotkey List
shows the A220's own keys. The Ctrl+M monitor manager has a search box and a Show filter
like the other aircraft.

The panels now follow the A220 cockpit: the overhead in the order you work it (Electrical,
Hydraulics, APU, Equipment Cooling, Fire Protection and on to the lights and signs), then the
glareshield, a Center Panel section for the standby instrument and the gear and brakes, and
the pedestal — each panel starting with the switches you use every flight, in the order you
use them (batteries first in Electrical, the parking brake first in Trim, the guarded switches
last). New on
the panels: equipment cooling inlet and exhaust, the APU and cargo fire pushes and all six
fire bottles, both wipers, CVR test and erase, service interphone and mechanic call, the
standby altimeter (type a setting in hectopascals or inches, or press STD), VHF 1 to 3
transmit select, each VHF's receiver on or off and its volume (so you can listen to a second
radio, such as COM 2 on SayIntentions), and the speedbrake lever in every position from
retracted to max. Probe heat is now the ground-test button it is in the cockpit; the engine
fire pushes are one-press buttons, as in the cockpit; every guarded switch says so (and only
those); and the lamp-test switch that did nothing is gone. The electronic checklist can now
set the equipment cooling and the speedbrake for you; the fire controls stay yours to press.
The standby altimeter's STD button tells you when it is on STD. In this version of the
aircraft the APU and cargo fire pushes, the fire bottles, CVR, service interphone and
mechanic call do not do anything yet, and an engine fire push stays pushed until you reload
the flight.
