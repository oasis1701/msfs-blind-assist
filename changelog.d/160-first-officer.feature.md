A First Officer for the PMDG 777, PMDG 737, iFly 737 MAX8, Fenix A320, FlyByWire A32NX,
FlyByWire A380, HeadwindSim A330-900neo and TFDi MD-11. Open it from the File menu: the item is named
for the aircraft you are flying ("PMDG 777 First Officer", "Fenix A320 First Officer" and
so on) and only shows up when you are flying that aircraft. The window has two tabs. On
the Flows tab, pick a phase's flow and press Start Flow. The First Officer works through
the procedure and speaks each step, from power-up to securing the aircraft, and you can
pause, resume or stop it. Things that are the Captain's job, such as the landing
autobrake, trim or anything you have to look at, are read out as reminders and left for
you to do. The Checklists tab is a tree with every phase's checklist. Each item ticks
itself once the aircraft is actually in that state, however it got there. When you tick
an item yourself, the First Officer does it for you, or just ticks it if it is already
done. Run Related Flow starts the flow for the section you have selected.

Where the aircraft reports a switch's position, the First Officer checks that the switch
really moved before it calls the step done. If a step fails, you hear "Skipping:" and
the item's name, and that item is not ticked along with the rest of the flow: it keeps
following the real switch, and ticks itself if you set it by hand. If you tick an item
and the action doesn't take, the tick is removed and you hear "Unable to complete:" and
the item's name. Slow items wait for the real result. Before Start leaves ground power connected until the APU is actually available.
The engine start items tick once each engine is running, whoever started it. The gear
lines are checked against the aircraft's own gear indications wherever it provides them.
Preflight runs the aircraft's own self-tests the way a pilot does, and the test's sound
is your confirmation: the fire tests, for example, and on the PMDG jets the crew oxygen,
TCAS, weather radar and GPWS tests. Load SimBrief brings in the transition altitude and
level, the takeoff flap setting and, on the PMDG 737, the pressurization altitudes. It
loads by itself when the window opens if your SimBrief username is set.

On the Fenix A320, FlyByWire A32NX and Headwind A330, the Checklists tab holds Airbus's
current A320 normal checklist: Cockpit Preparation, Before Start, After Start, Taxi,
Line-up, Approach, Landing, After Landing, Parking and Securing, with the ECAM takeoff
and landing memos read out line by line. Ticking a line speaks what the First Officer
reads, such as the flap setting or the TCAS mode. The flows are the same on all three: each
engine start waits for the engine to be running, the takeoff flaps come from SimBrief (with
a reminder to you when no plan is loaded), the ECAM pages and the cockpit lighting are set
at the same points, the FCU altitude is pushed, and the gear is confirmed up after takeoff.
Taxi and Line-up are two separate flows, and Run Related Flow starts each one from its own
checklist. Taxi, run while you taxi, arms the autobrake, sets the weather radar and transponder,
calls the cabin, checks that the spoilers, flaps and seat belts are set and ends with the takeoff
config test. Line-up, run when you are cleared onto the runway, sets TCAS and the takeoff lights.
Shutdown has a "TCAS: STANDBY" line and Securing an "External power: OFF" line.

On the FlyByWire A380, the Checklists tab now holds the A380's own normal checklists from its
electronic checklist, in its own words (Cockpit Preparation, Before Start, After Start, Taxi,
Line-up, Approach, Landing, Parking and Securing the Aircraft): ticking a line you confirm speaks
what the First Officer reads, such as the fuel, the baro reference or the takeoff speeds. You now
also hear the A380 flows check the aircraft without moving anything: After Start checks that the
rudder trim reads neutral after its reset, Taxi checks the takeoff memo (seat belts, ground spoilers
and flaps), Line-up checks that both packs are on, and Landing checks the landing memo (seat belts
and landing flaps) and, last of all, that the gear is down; the Approach flow also now calls the
cabin crew for landing. A finished flow now ticks a line the First Officer can read only when one of
its steps set it or one of its checks confirmed it; a line whose step was skipped is left to the
aircraft: like any line, it ticks itself when the aircraft shows it done, and stays open until then.

On the TFDi MD-11, the Checklists tab follows TFDi's normal checklist from Cockpit Entry to
Shutdown. It leaves out the 10,000 feet, transition and cruise pages: while the window is open,
the First Officer switches the landing lights at 10,000 feet (unless you turn that off), sets the
altimeters to standard and the Dial-A-Flap to 15 at the transition altitude, and reminds you to
set local QNH at the transition level. If you used ground power, Shutdown switches external power
off just before the battery.

While the window is open, it also switches the landing lights at 10,000 feet, and you
can turn that off in Settings. If SimBrief has supplied the transition altitude and
level, it also sets the altimeters to standard at the transition altitude and back to QNH
at the transition level. The rest of the automation is on the new First Officer tab in
Settings, and all of it is off until you turn it on:

- Seat belt signs switch at 10,000 feet, or at top of climb and top of descent.
- On the PMDG 737, PMDG 777 and iFly MAX8, the center fuel pumps come on during ground
  setup when the center tank holds more than 1,500 lbs. They go off with a callout once it drops
  below 1,000 lbs.
- On the FlyByWire A32NX, A380 and Headwind A330, flaps follow the speed tape, stopping at
  CONF 3 when that is your landing configuration.
- The gear goes up on a positive rate and down at 2,000 feet.
- The autopilot engages at a height you choose. On the 737s, LNAV and VNAV are also
  pushed at 400 feet.

The gear and autopilot options work on every aircraft the app supports, whether or not a
First Officer window is open. On the PMDG jets, the iFly, the FlyByWire A32NX and A380 and
the Headwind A330, the app checks that the autopilot really engaged, and tells you if it did not.
