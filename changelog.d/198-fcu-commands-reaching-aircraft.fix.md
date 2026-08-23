Controls that send commands to FlyByWire aircraft are reaching the aircraft again. A check that
decides whether MSFS Blind Assist can talk to the simulator the fast way could never succeed,
because it compared each test message against the wrong reply — so on many systems it quietly
concluded the fast route was unavailable and fell back to one the FlyByWire FCU ignores.

The visible effect was that FCU controls appeared to do nothing at all: switching the altimeter
between QNH and Standard, arming Localizer or Approach, and the ND display buttons. Knobs driven
from the FCU windows kept working, which is why this went unnoticed for so long.
