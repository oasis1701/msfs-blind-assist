# PanelSweep

A live check of the BlueBird 757's panel controls. For every switch and push-on/push-off button the
app can set, it reads the control, writes each of its other positions the way the app does, checks that
the control really went there, and puts it back. The result is a table (a `.tsv` file) that says which
positions land and which do not.

It exists for plan 12 (the remaining panels). A control whose position "does not land" is either a
label or position name the panel shows wrongly (plan 16 lists those) or a fault in the app's writer (a
follow-up).

It is a standalone console tool, not part of the solution (CORE-4). It compiles the app's own planner
(`Bbs757ControlMap.cs`, `Bbs757Write.cs`, `Bbs757Rpn.cs`, linked, never copied), so it tests the writer
the app ships, and it reads the map the app embeds (`MSFSBlindAssist/Resources/bbs757_control_map.json`).

## Build

```
dotnet build tools/b757-gen/PanelSweep -c Debug -p:Platform=x64
```

It builds on its own and needs nothing from the app's output. It ships both `SimConnect_msfs_2020.dll`
and `SimConnect_msfs_2024.dll` (from the repository root) and, when it connects, copies the one for the
running sim to `SimConnect.dll` next to its exe, the way the app does at start-up.

The exe is in `tools/b757-gen/PanelSweep/bin/x64/Debug/net10.0-windows/`.

## Run

Look first, without the sim. A dry run never connects to it:

```
PanelSweep.exe --dry-run
PanelSweep.exe --dry-run --panel "Fuel"
```

It prints, panel by panel, every control and what the sweep would do with it (the positions it would
write, in order, and how many calls each takes; or `denied`, `skipped: button`, `skipped: guarded`), the
counts, the rough running time, and two audits: no denied node is planned for a write, and no planned
command is longer than MobiFlight's buffer. A denylist line that names a node the map no longer has is a
stale entry (a renamed node has lost its protection): the dry run prints it as a warning, and a live run
refuses to start on one (exit code 2, before the `YES` prompt), naming it.

The live sweep:

```
PanelSweep.exe
PanelSweep.exe --panel "Fuel"
PanelSweep.exe --out C:\some\folder
```

`--panel NAME` limits both modes to one panel (`Fuel`), one section (`Overhead`) or `"Overhead / Fuel"`,
ignoring case; if nothing matches exactly it takes every panel whose name contains the text. `--out DIR`
says where the results go (the current folder by default). The tool always reads the `denylist.txt` next
to its exe (the build copies it from this folder); there is no option to point it at another one.

### Conditions for the live run (the owner runs it once)

- The owner is at the keyboard, watching or listening to the aircraft.
- The BlueBird 757 is loaded, **on the ground, parked, engines off, APU running**.
- MSFSBA is closed, so nothing else writes to the cockpit.

The tool asks you to type `YES` before it connects. Then it checks, before writing anything to the
cockpit, that the sim says the aircraft is on the ground with both engines off (it refuses if not), and
that a one-shot calculator write of a test value into a variable of its own (`L:PANELSWEEP_PROBE`) comes
back on a read (it refuses if not: the MobiFlight module is missing or the aircraft is not loaded).

It takes about 16 minutes for the whole 757 (the dry run's estimate on 2026-10-28, with plan 12 task 11's
windows and furniture: 198 controls to sweep, 313 position writes, each control put back afterwards). **Ctrl+C** stops after the step in progress and
puts that control back, then writes the results so far. A second Ctrl+C stops at once and names the
control that may need putting back by hand.

## What moves during the sweep

Every control below is moved through each of its positions and put back where it was, one control at a
time, each position held for about 2 seconds. So during the run you will see (and some you will hear)
these change, panel by panel, as the dry run lists them on 2026-10-28:

- **Overhead, Electrical:** the left and right utility buses (L and R Util Bus). The power sources (standby
  power, bus ties, generator controls, APU generator) and the battery are denied, so the buses stay powered.
- **Overhead, IRS:** the IRS display knobs (IRS Center Knob: L, C, R; IRS Large Knob: TK/GS, PPOS, WIND,
  HDG/STS). The IRS mode selectors are denied.
- **Overhead, Yaw dampers:** the left and right yaw dampers.
- **Overhead, Hydraulics:** all four electric hydraulic pumps (L, C1, C2, R ELEC Pump), which start and stop
  (hydraulic pressure rises and falls), and both engine pump switches.
- **Overhead, Panel lighting:** Lt Ovrd.
- **Overhead, Ram air turbine and engine limiters:** the left and right engine limiters.
- **Overhead, Fuel:** all six fuel pumps (left, right and center) and both crossfeed valves.
- **Overhead, Anti-ice:** engine and wing anti-ice.
- **Overhead, Wipers:** the wiper selector (OFF, LOW, HIGH): the wipers run.
- **Overhead, Exterior lights:** every exterior light (anti-collision red and white, wing, runway turnoff,
  taxi, landing, position, logo).
- **Overhead, Window heat:** the four window heat switches.
- **Overhead, Passenger signs:** the seatbelt and no smoking signs (the cabin chimes).
- **Overhead, Pressurization:** Mode Select (AUTO 1, AUTO 2, MAN) and Auto Rate.
- **Overhead, Equipment cooling:** the equipment cooling switch.
- **Overhead, Air conditioning and bleed:** both pack selectors, the isolation valve, both engine bleeds and
  the APU bleed (the packs lose their air while it is off), the three zone temperature selectors (Flight
  Deck, Forward Cabin, Aft Cabin), trim air and both recirculation fans.
- **Overhead, Indicator lights and door lock:** the door lock switch (Unlocked, Auto, Deny) and the Warning
  Ind switch (DIM, BRT): the annunciators dim and brighten.
- **Glareshield, MCP:** both flight director switches, the A/T arm switch, the bank limit selector and the
  autopilot disengage bar.
- **Forward panel, Captain and First officer foot and shoulder heat:** the foot and shoulder heaters.
- **Forward panel, Captain and First officer instrument source selectors:** the flight director, nav, EFI,
  IRS and air data source switches of each side.
- **Forward panel, Brakes:** the autobrake selector and the left brake reserve.
- **Forward panel, Heading reference:** HDG Ref.
- **Forward panel, Standby instruments:** the standby EICAS switch and the gyro selector (OFF, ILS, BCRS).
- **Forward panel, Captain and First officer DDRMI:** both needles' VOR/ADF switches.
- **Forward panel, Captain and First officer chronometer:** Chrono ET (RUN, HOLD, RESET) and Chrono Set
  (RUN, HOLD, slow and fast slew): the clocks' elapsed time and time change.
- **Pedestal, TCAS and transponder:** the TCAS mode and transponder selectors.
- **Pedestal, Captain and First officer EFIS:** ND mode and range, the map overlays (WXR, TERR, NAV AID,
  APRT, DATA, WPT, INOP) and the PFD and ND brightness knobs: both NDs change what they show, and each PFD
  and ND dims to its minimum and back to full.
- **Pedestal, EICAS control:** the upper and lower EICAS brightness knobs (each display dims to its minimum
  and back to full), the EICAS computer switch and the thrust reference controls (EICAS Lwr Thrust Ref: L,
  BOTH, R; Thrust Ref Pull).
- **Pedestal, CDU brightness:** both CDU brightness knobs.
- **Pedestal, Audio panels 1 and 2:** every microphone and receiver selector, voice/range and boom/oxygen.
- **Other, P61 panel:** the flight recorder and the service interphone. (The P61 test switches are no longer
  in the map: they spring back to neutral at once, so the app tests from buttons instead, and the sweep never
  moves them.)
- **Other, Oxygen:** the four O2 Ears switches (captain, first officer, jumpseat, observer).
- **Other, Flight deck door:** the cockpit door.
- **Other, Forward attendant panel:** the ceiling, window and entry lights and the work light.
- **Other, Windows and sun visors:** both side windows open and close, both sun visors appear and slide
  along their track, and the four window shades are drawn and stowed (an aft shade moves only while its
  side window is closed).
- **Other, Seats and stowage:** both pilots' seats run aft or forward, and the armrests, headrests, both
  jumpseats and their armrests, the chart holders, the floor box and the three stowage doors move.

Nothing on the denylist moves (below), nor any button, guard or guarded switch.

Two of these controls change one another, so the sweep can leave them changed without a `RESTORE FAILED`:
an aft window shade drawn before the run is left stowed (the side windows are swept before the shades, and
opening a side window makes BlueBird put a drawn aft shade up, which the window's restore does not bring
back), and stowage door 1 open past Half is left at Half (sweeping Jumpseat 2 pulls it back to 24). Stow
both before the run, or set them back after it.

## What it does to a control

1. Reads the control's state variable (`L:` var) with a SimConnect data definition, as the app reads it.
2. For each of the control's other positions, in the order the map lists them: reads again, asks the
   app's `Bbs757Write.ToPosition` for the calls, sends them one at a time **400 ms apart**, waits
   **1.5 s** after the last, reads, and compares with `Bbs757Rpn.PositionIndex`.
3. Puts the control back where it was read, through `ToPosition` again, and checks. If the first reading
   was off every position, it writes that raw value back to the state variable instead. A restore is
   tried up to three times; if it still fails, the control is named on screen and in the table.

Writes go to the MobiFlight calculator as one-shot `MF.SimVars.Set.<rpn>` commands, on the same
`MobiFlight.Command` channel the app uses, each made unique with a `{seq} 0 *` prefix so MobiFlight
cannot drop it as a repeat of the command before ([SIM-17]). The tool never registers an expression with
`MF.SimVars.Add`: MobiFlight re-runs a registered expression on every poll, so a write inside one runs
again and again (measured on 2026-10-28: it drove the rudder trim to its stops; docs/b757.md,
"PanelSweep").

## What it leaves alone

- **Buttons and held buttons** (a press has no position to read back): listed as `skipped: button`.
- **Guards, and controls a guard covers** (the map's `guard` field): listed as `skipped: guarded`, even
  for a momentary button that carries a guard. Their writes need the app's guard sequence
  (`SetGuardedAsync`), which the tool does not link. Switches that sit under a cover the map does not
  link to them would only report a false `no change` with the cover shut, so they are on the denylist
  together with their covers (below).
- **The denylist** (`denylist.txt`, one node name per line, `#` for comments): controls that are
  **never written**, whatever else happens: the fire handles, their unlock buttons and rotations; cargo
  fire and its bottles; the stabilizer trim cutouts; the battery; the electrical sources (standby power,
  both bus ties, both generator controls and the APU generator: switching a power source off on ground
  power can drop the buses, as a bus-tie press broke ground power on the Fenix); the APU selector; the IRS selectors;
  the fuel control switches; the engine start selectors and the ignition selector; the generator drive
  disconnects; the RAT; passenger oxygen; the emergency lights; the gear lever; the flap lever; the
  parking brake; the alternate flaps selector and its arm switches (they drive the flaps electrically);
  the gear lock override; the covered switches the map does not link to their covers (the Elec Eng Cont
  switches, the flap, gear and terrain overrides, the P61 pressurization main buttons); and the guards
  of all of these. The list is checked before the first write to a
  control and again before every write. On top of the list the tool denies the guard a denied control
  names in the map, any control whose guard is denied, and any control that writes the same state
  variable as a denied one. A missing or empty denylist stops the tool.

## Reading the results

The tool writes `sweep-<date>.tsv` (tab-separated, UTF-8), one row per position tried and one row for
each control it did not sweep. The columns:

| Column | Meaning |
|---|---|
| `node` | The map node, e.g. `B757_OVHD_ISOLATION_BUTTON`. |
| `label` | The spoken name the panel shows. |
| `position` | The position tried, as `label (value)`; `(restore) ...` for the put-back. Empty for a skipped control. |
| `written` | The calculator code sent (several calls are separated by ` \| `). Empty if nothing was sent. |
| `read back` | The value the state variable held 1.5 s after the last call. |
| `verdict` | What happened. |

Verdicts:

| Verdict | Meaning |
|---|---|
| `landed` | The control read back at the position written. |
| `no change` | It read back where it was before the write: the write did nothing, or the control sprang back. |
| `wrong position: X` | It went to a different position, X. |
| `read back off every position (v)` | It read back a value that is none of the control's positions. |
| `not read after the write`, `not read before the write` | The sim delivered no value in 2 s. |
| `refused: ...` | The app's planner declined to write from that reading. |
| `not sent: ...` | The command would not fit (over 1023 characters) or SimConnect refused it. |
| `write error: SimConnect X` | The sim raised SimConnect exception X for one of the position's writes: the write did not run, so this is a transport fault, not the control's (where it would otherwise have read `no change`). |
| `already there` | The control already read the target. |
| `restored`, `at start (nothing to restore)` | The put-back worked. |
| `RESTORE FAILED: left at v` | The control was not put back: set it by hand. |
| `skipped: not read` | The state variable gave no value, so nothing was written. |
| `denied`, `skipped: button`, `skipped: guarded`, `skipped: no readable positions` | Not swept, and why. |
| `interrupted (...)`, `not reached (...)` | The sweep stopped here: `stopped with Ctrl+C`, or `stopped: SimConnect fault` (below). |

The screen shows each problem as it happens and a list at the end, with the count of SimConnect
exceptions and of positions with a write error. If one of the tool's SimConnect message handlers throws,
or receiving a message fails with anything but SimConnect's own error, the tool prints `SimConnect FAULT:
...` at once, puts back the control in progress (its reads may fail then, and it says so if the restore
does) and stops; every control after it reads `not reached (stopped: SimConnect fault)`. The exit code
is 0 when it finished, 4 when the pre-flight refused (nothing was written), 5 when some control was not
put back, and 6 when it stopped on a SimConnect fault.

### What to do with a failure

The owner runs the sweep once and attaches the TSV's failures to the plan 12 handoff. Failures are
every row that is not `landed`, `already there`, `restored`, `at start (...)`, `denied`, `skipped: ...`:
a label or position name that is wrong goes to plan 16's list; a write that does nothing, or lands
elsewhere, is a writer fault and becomes a follow-up. A spring-loaded switch that returns by itself
reads as `no change`; that is how it behaves, not a fault.

## Not tested in the sim

The planning half (which controls, which positions, the denylist) runs under `--dry-run` and was checked
against the map. The live half ran once, on 2026-10-28: 168 controls, 267 of 276 positions landed, every
control was put back and there was no SimConnect exception. The nine that did not land were BlueBird's
momentary door lock and Chrono ET RESET (they spring back by themselves) and the autobrake's DISARM logic
with the IRS off and the brakes set (docs/b757.md, "PanelSweep"). Its first start had stopped at
connect, before any write (fixed in `1a9b39c8f`). Plan 12 task 11's 30 windows and furniture controls have
not been swept live yet.
