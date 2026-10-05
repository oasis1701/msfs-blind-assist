# DA40Probe — live harness for the COWS DA40

Presses every DA40 panel control through MSFSBA's own code against a running sim and reads each one
back. It runs the real `SimConnectManager` and `CowsDA40Definition` and calls `HandleUIVariableSet`
exactly as the panel does, so a PASS means the control works from MSFSBA, not merely that a variable
can be written. It found the 2026-10-05 fixes a static review missed: vertical speed sent an event the
GFC 700 ignores, a typed selected altitude never counted as selected, two cache lookups used a SimVar
name instead of a variable key, three "graded" failures were on/off flags, and the ECU test hold was
too slow for the held-button template.

Standalone build, not part of `MSFSBlindAssist.sln`:

```bash
dotnet build tools/DA40Probe -c Debug
```

Run the exe from its output folder with the DA40 loaded and MSFSBA closed (two clients registering the
same definitions get in each other's way). Output goes to the console and to
`probe_<variant>_<mode>.tsv` beside the exe.

| Mode | What it does |
|---|---|
| `DA40Probe NG list` | Offline. Every panel control, its kind, and whether the definition has a write path for it. |
| `DA40Probe NG live [filter]` | Presses every control (or those whose key or panel contains `filter`): each combo position, each button, each typed entry with its current value. Reads back, restores, and records what MSFSBA said. |
| `DA40Probe NG set "KEY=1[@READKEY][#ms];…"` | Sets a value, waits (default 3000 ms), reads back, restores. |
| `DA40Probe NG keep "KEY=1[@READKEY][#ms];…"` | The same without the restore. |
| `DA40Probe NG hotkeys "ReadFuelQuantity,…"` | Fires hotkey actions and dumps whatever a window they open shows. |
| `DA40Probe NG ie "INPUT_EVENT,READKEY"` | Steps an input event through 0, 1, 2, 1, 0 and reads a key after each. |
| `DA40Probe NG calcloop "rpn|ms|secs"` | Repeats a calculator string, for a held input such as the toe brakes (`100 (>L:INPUT_BRAKE:1)`). |

Use `XLS` in place of `NG` for the XLS.

## Read the aircraft state before and after

The `live` pass changes the aeroplane while it runs, and some of it sticks:

- **The engine master is switched off and on**, which stops a running engine. Restart it with
  `keep "DA40_START_STARTER_ENGAGE=1@DA40_POWER_RPM#12000"` and then ALWAYS
  `keep "DA40_START_STARTER_RELEASE=1"`: MSFSBA deliberately never releases the start key on its own
  (the AFM's crank limit is the pilot's), so the starter stays engaged on a running engine.
- **Breaker-trip tests pop breakers.** The pass restores them, but check every `L:CB_*` afterwards.
- **TO/GA disconnects the autopilot** on this aircraft, so filter the GFC 700 controls separately when
  checking the autopilot.
- **The GFC 700 needs the avionics master** (its servos are on the avionics bus): with it off every
  autopilot mode reads FAIL, correctly. A Ctrl+E start leaves it off.
- **Check the parking brake before adding power.** With Realistic Parking Brake on, the handle alone
  holds 0 percent; the brake latches only the pedal pressure applied after it
  (`calcloop "100 (>L:INPUT_BRAKE:1) 100 (>L:INPUT_BRAKE:2)|40|3"`). An unbraked test once rolled
  the aircraft about 60 m.
- Typed entries with no read-back key report `DIFF` with null values; that is the harness, not the
  aircraft. Canopies read about 98 percent while still swinging.
