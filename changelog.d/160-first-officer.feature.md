A First Officer for the PMDG 777, PMDG 737, iFly 737 MAX8, Fenix A320, FlyByWire A32NX,
FlyByWire A380 and HeadwindSim A330-900neo. Open it from the File menu: the item is named
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
First Officer window is open. On the PMDG jets and the iFly, the app checks that the
autopilot really engaged, and tells you if it did not.
