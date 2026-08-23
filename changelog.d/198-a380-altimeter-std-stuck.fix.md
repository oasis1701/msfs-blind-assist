The A380's Standard/QNH state is now read from the flight control unit's own output rather than
from the simulator's altimeter mirror. Both report the same thing, so this is not a fix for
anything you would have noticed — it simply takes the reading from the source, which is one fewer
step that can drift when FlyByWire moves things around.

The altimeter being stuck on QNH was a separate problem, covered by the other notes in this
release: the commands the app sent were not reaching the aircraft.
