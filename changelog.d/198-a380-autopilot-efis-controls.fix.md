The A380's Localizer and Approach controls report their real state again, and so do the
ND display buttons. A FlyByWire update moved the autopilot's guidance computers around and
renamed the variables underneath, so these had been reading as permanently off no matter
what the aircraft was actually doing.

The ND filter buttons — Waypoints, VOR/DME and NDB — are now three separate switches
instead of one either/or list, because the aircraft lets you have all three showing at
once. Cruise Altitude Mode, Speed Protection and FMA Reversion are back as well. The
Expedite control is gone from the A380, which has no such button on its FCU; the A320
keeps it.

Vertical speed, heading and flight path angle read out correctly again. Vertical speed had
started announcing a 500 foot-per-minute selection as "98400 feet per minute", because the
same FlyByWire update changed the units those values arrive in.

Please update the FlyByWire A380X to the latest Development build in the FlyByWire Installer
before using these readouts. FlyByWire changed these variables on 18 August 2026, and an
older copy of the aircraft will not report them correctly.
