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
