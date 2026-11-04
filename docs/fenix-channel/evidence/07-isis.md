# Plan 07 evidence: the ISIS

Fenix build **3.0.0.7613**, Daniel Wolak's install, worked by Daniel with Claude. Raw fields come from the team's local
channel tools and a subscribe-only recorder like the team's watcher, which stay local and are never committed.

## The fields ([FNX-23]): Task 2 Step 6, 2026-11-04

`IsisState` and `BugState`, and `RemoteData`'s field 182, read fresh from the installed
`C:\Program Files\FenixSim A320\deps\displays\FenixDisplay.exe` (product version 3.0.0.7613) with the team's metadata
tool (233 `ProsimRemoting` types), with the sim closed. Every number and wire shape Task 2 adds stands, with no change:

- `RemoteData.isis` is 182, `IsisState`.
- `IsisState`: 1 Powered to 9 NewStyle `boolean`; 10 Pitch, 11 Bank, 12 Airspeed, 13 Altitude, 14 Mach, 15 Brightness
  `double`; 16 Init and 17 QuickRealign `Nullable<int32>`; 18 Qnh, 19 LocDots, 20 GsDots `double`; 21 SelectedBug
  `int32`; 22 Bugs `BugState[]`; 23 Slip `double`. The same as the team's contract dump of 2026-11-01.
- `BugState`: 1 Enabled `boolean`, 2 Value `double`.

The countdowns' wire type on the live stream (Task 1 Step 1) is still to be seen: the decoder reads 16 and 17 as a plain
varint, which is how protobuf-net writes a `Nullable<int32>` with a value, and a nested wrapper would read as a wrong
shape ("field 182.16 has the wrong shape"), so it fails closed until Step 1 says otherwise.

## Robin's ground push of 2026-11-01

Field 182 of `sample-aircraft-push-ground-2026-11-01.bin` (KATL, on the ground, the team's earlier sample), not this
install: .1, .6 and .9 set (powered, hPa, new style); .10 0.193, .11 -0.102, .12 9.30, .13 372.9; .14 NaN; .15 255;
.18 1013; .19 and .20 NaN; .16 and .17 left out; **six bugs, each an empty `BugState`** (not enabled, value 0), where
the plan expected default values (four speed bugs, then two altitude bugs). Task 1 Step 5 finds what sets them. The
altitude 372.9 at KATL (elevation about 1,026 ft) is unexplained: Task 1 Step 3 reads it beside the baro setting.
