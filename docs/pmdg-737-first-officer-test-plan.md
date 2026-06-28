# PMDG 737 First Officer — In-Sim Test Plan

The First Officer engine was generalised into a **shared generic core** and the **PMDG 777
FO was migrated onto it**, then a **PMDG 737 (NG3) FO** was added as a second implementation.
There is no automated test project (SimConnect/UI app), so the repo owner verifies the two
halves below against a live sim (MSFS 2020 or 2024).

Open the window from **Tools → "PMDG 737 First Officer"** (above the 777 item) while the
PMDG 737-800 is loaded. The window has two tabs: **Flows** and **Checklists**.

---

## Part A — 777 regression (the migration must be behavior-preserving)

Because the 777 FO was refactored onto the shared engine, confirm it still behaves exactly as
before:

1. Load the PMDG 777. Open **"PMDG 777 First Officer"**.
2. Run each flow on the Flows tab cold-and-dark → shutdown; confirm the same switch actions,
   announcements, waits, and captain reminders as before the change.
3. On the Checklists tab, confirm items auto-tick from live state, `RevertToState` items
   un-tick when state drifts, and ticking an actionable item fires its switch.
4. Confirm SimBrief load, the opt-in auto gear/flaps/AP, and the altimeter/landing-light phase
   automation still work.
5. Confirm the window opens, disposes on aircraft swap, and re-wires on a SimConnect reconnect.

Any difference from the pre-refactor 777 behavior is a regression to report.

---

## Part B — 737 First Officer

### B1. Window lifecycle
- Window opens from the menu; title reads **"First Officer — PMDG 737"**.
- Switch aircraft away and back → the 737 window disposes and re-creates cleanly.
- Disconnect/reconnect SimConnect → the window re-wires (flows still drive switches).

### B2. Flows (Flows tab) — run each, confirm the listed switches move
Pre-condition for the ground flows: start cold-and-dark (or at the matching phase).

| Flow | Expected (spot-check the overhead/MCP) |
|------|----------------------------------------|
| Electrical Power Up | Battery ON (guard opens, switch ON, guard closes); Standby power AUTO; Ground power ON; **IRS selectors → NAV with no pause** (alignment runs in background) |
| Preflight | Walk-around pause announced; yaw damper ON; window heat ON; wing/eng anti-ice OFF; packs AUTO; isolation OPEN; eng bleeds ON; both FDs ON; autobrake RTO; transponder STBY; EFIS MAP/40; captain reminders for pressurization, altimeters, tests |
| Before Start | Captain MCP reminder; **APU → START then waits for it to come on line**; fuel pumps ON; elec hyd pumps ON; APU bleed ON; anti-collision ON; transponder TA/RA |
| Engine Start | Packs OFF; ENG 2 start switch GRD + start lever IDLE → **waits for ENG 2 start valve to close**; then ENG 1 the same |
| Before Taxi | **After-start power transfer first** (generators ON, APU bleed OFF, APU OFF), then probe heat ON; packs AUTO; isolation AUTO; start switches CONT; taxi + turnoff lights ON; lower DU SYS; captain reminders for anti-ice and takeoff flaps |
| Before Takeoff | Landing lights ON; strobes ON; **A/T arm**; transponder TA/RA |
| After Takeoff | Packs AUTO; start switches OFF; turnoff lights OFF; gear lever OFF; autobrake OFF |
| Descent | Seatbelt sign ON; captain reminders for autobrake, ILS, landing data |
| Approach | EFIS APP / range 20; altimeter reminder |
| Landing | Start switches CONT; speedbrake ARMED; missed-altitude reminder |
| After Landing | Landing lights retract; taxi light ON; strobes steady; anti-ice OFF; probe heat OFF; APU ON; start switches OFF; autobrake OFF |
| Shutdown | APU gen ON; **start levers CUTOFF then waits for engine spool-down**; signs/lights off; fuel pumps OFF; window heat OFF; transponder STBY |
| Secure | IRS OFF; emergency exit OFF; window heat OFF; packs OFF |

Verify **Pause / Resume / Stop** mid-flow, and that **"Run Related Flow"** from a checklist
group starts the matching flow.

### B3. Checklists (Checklists tab) — auto-fire parity with the 777
- The tab lists 19 groups in flight order (9 auto-detect state groups + 10 readback checklists).
- As you run the flows (or set switches manually), the matching state-group items **auto-tick**.
- `RevertToState` items (gear, autobrake, landing gear in the readback checklists) **un-tick**
  when the state no longer matches.
- Ticking an actionable state-group item **fires the switch** (e.g. tick "Battery: ON" → the
  battery moves), confirmed by the item staying ticked on the next poll.
- The **"IRS aligned"** item (Electrical Power Up group) ticks on its own once alignment completes.

### B4. Auto-fly (opt-in via First Officer Settings)
Enable Auto Gear / Auto Flaps / Auto AP in **Tools → First Officer Settings**, then fly:
- Positive rate after takeoff → **"Gear up"**; ~500 ft → **autopilot CMD A engaged**.
- Climb accelerating past V2 margins → flaps retract one step at a time (UP/1/2/5/10/15/25/30/40);
  on approach below VREF margins → flaps extend.
- Descending through 2000 ft AGL with gear up → **"Gear down"**.

### B5. Flight-phase + SimBrief
- **Load SimBrief** (Flows tab) → confirm "Takeoff flaps: N" + transition alt/level announced.
- Climb through 10,000 ft → landing lights OFF; descend through 10,000 ft → ON.
- Climb through the SimBrief transition altitude → both altimeters pushed to STD (announced);
  descend through the transition level → both pushed off STD with a "set local pressure" prompt.

---

## Known limitations (by design / data availability)
- **Baro-STD has no NG3 state field** — the phase monitor pushes STD/QNH at the transition
  alt/level using its own one-shot latch (it cannot read whether STD is already selected).
- **GEN / APU-GEN auto-detect read-back may lag** — these are dispatched by mouse-flag and the
  PMDG snapshot can lag; the switch still actuates, only the checklist auto-tick may be slow.
- **Test items (fire test, overhead test, etc.) are captain reminders**, not automated — a blind
  pilot cannot observe the visual test result, so the FO prompts the captain to perform them.
- **Runtime-data items** (altimeters, trim, MCP courses, ILS frequencies, pressurization
  altitudes) are captain reminders — they depend on data the FO cannot set blind.
