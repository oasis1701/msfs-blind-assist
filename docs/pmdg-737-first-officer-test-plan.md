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
  on approach below VREF margins → flaps extend. (Requires FMC V2/VREF programmed.)
- Descending through 2000 ft AGL with gear up → **"Gear down"**.
- **Auto-flaps is now closed-loop**: it reads the actual flap position from the flap gauge
  (`MAIN_TEFlapsNeedle`), so it tracks manual flap moves and does not depend on the takeoff-flap
  assumption.
  - **Calibration check (one minute):** with the 737 loaded and flaps set to **5**, read
    `MAIN_TEFlapsNeedle` via the sim tools — it should read **≈ 5** (the needle is assumed to be
    the flap **angle in degrees**). If it reads a different scale (e.g. ≈ 12.5 for "percent of
    full travel"), tell me the value at flaps 5 / 15 / 30 and I'll adjust the one mapping in
    `AircraftStateEvaluator.FlapDetent()`. If the read is implausible, auto-flaps falls back to
    its internal command tracking, so it degrades to the previous behavior rather than failing.

### B5. Flight-phase + SimBrief
- **Load SimBrief** (Flows tab) → confirm "Takeoff flaps: N" + transition alt/level announced.
- Climb through 10,000 ft → landing lights OFF; descend through 10,000 ft → ON.
- Climb through the SimBrief transition altitude → both altimeters pushed to STD (announced);
  descend through the transition level → both pushed off STD with a "set local pressure" prompt.

---

## Part C — FD/AT Arm idempotency (flow ↔ panel parity, 2026-06-28)

The FD and AT Arm switches are driven by `SendPMDGEvent(event, id, MOUSE_FLAG_LEFTSINGLE)`
on BOTH the panels (`HandleUIVariableSet`) and the FO flows/checklists — the mouse flag is
the SDK-mandated method because these switches ignore direct position values. It is a
single-click **toggle**, so a flow step that fired it unconditionally flipped FD/AT the wrong
way whenever the switch was already in the target state. The flow `MouseFlag(...)` steps now
require a `SkipCondition` predicate, so `FlowManager` **no-ops** the step (announcing
"Already set: …") when the switch is already correct — matching the panel's `current==target`
guard and the checklists' `SetFDLeft(target, state)` path.

### C1. 777 — run each affected flow from BOTH starting states
Read switch state via the sim tools (PMDG fields `MCP_FD_Sw_On_left` / `_right`,
`MCP_ATArm_Sw_On_left` / `_right`).

- **Already-correct (the bug case):** with FD L/R and AT Arm **OFF**, run **Preflight**. The
  three FD/AT steps must announce **"Already set: …"** and the switches **stay OFF**. (Before
  the fix they flipped **ON**.)
- **Needs-changing:** with FD L/R **ON**, run **Preflight** → they go **OFF**.
- **Shutdown:** FDs **ON** → run **Shutdown/Secure** → **OFF**; FDs already **OFF** → run it
  again → **"Already set"**, stay **OFF**.

### C2. 737 — same check
Read-back fields: `MCP_FDSw_1` / `MCP_FDSw_2` (FD L/R), `MCP_ATArmSw` (AT Arm).

- FDs **OFF** → run **Preflight** → **ON**; FDs already **ON** → run again → **"Already set"**,
  stay **ON**.
- AT **OFF** → run **Before Takeoff** → **ARM**; already **ARM** → run again → **"Already
  set"**, stay **ARM**.

### C3. 777 flap-detent parameter — PENDING verification
`AircraftActionExecutor.SetFlapsPosition` sends the per-detent
`EVT_CONTROL_STAND_FLAPS_LEVER_*` event with **param 1**; the 777 panel sends the same events
with **MOUSE_FLAG_LEFTSINGLE**. The 737 NG3 is known to accept param 1 (validated by the
auto-flaps work); the 777 is unconfirmed. To verify: set flaps to **5**, then run the 777
**Secure/Shutdown** "Flaps: UP" action (which calls `SetFlapsPosition(0)`) and read
`FCTL_Flaps_Lever`.
- Reads **0 / UP** → param 1 works on the 777; **no code change** needed (close this item).
- **Did not move** → change `SetFlapsPosition`'s final dispatch from
  `ExecuteSingle(eventName, null, false, true)` to `ExecuteSingle(eventName, null, true, false)`
  (mouse flag), rebuild, and re-test.

The sim tools can READ these PMDG fields but cannot WRITE to actuate this 777 (their event
transport does not replicate the app's CDA `SetClientData` write — verified 2026-06-28), so all
toggling above must be driven through the app's FO flows/checklists; the sim tools are the
read-back instrument only.

---

## Part D — FO coverage + menu visibility (2026-06-28)

### D1. Menu visibility (all aircraft)
Each PMDG First Officer menu item is now gated to its own aircraft.
- Load the PMDG 777 → Tools shows **"PMDG 777 First Officer"** and **First Officer Settings**, NOT "PMDG 737 First Officer".
- Load the PMDG 737 → Tools shows **"PMDG 737 First Officer"** + Settings, NOT the 777 item.
- Load any other aircraft (A320 / A380 / HS787) → **neither** FO item nor FO Settings appears.

### D2. 737 engine-driven hydraulic pumps (functional-hole fix)
The 737 FO now manages the engine-driven hyd pumps (previously never set; the 777 already did). PMDG cold-dark starts the EDP switches OFF.
- Cold-dark → run **Before Start** → the new **"Engine hydraulic pumps: ON"** flow step fires; confirm `HYD_PumpSw_eng_0` / `HYD_PumpSw_eng_1` read ON (via sim tools), and the new Before Start checklist item **"Engine hydraulic pumps: ON"** auto-ticks (alongside the existing electric-pump item).
- Run **Shutdown** → the new **"Engine hydraulic pumps: OFF"** flow step fires; confirm both read OFF. (No checklist line for the OFF — shutdown keeps the generic "Hydraulic panel: set" reminder.)

### D3. 777 primary hydraulic pump readback
The Before Start flow already turns the primary pumps ON; the checklist now verifies them.
- Run **Before Start** → confirm the new checklist item **"Engine and Electric primary hydraulic pumps: ON"** auto-ticks (it reads `HYD_PrimaryEngPump_Sw_ON_0/1` + `HYD_PrimaryElecPump_Sw_ON_0/1`, which the flow's `BS_HYD_ENG` / `BS_HYD_ELEC` set). If you manually switch a primary pump OFF, ticking the item re-commands all four ON.

---

## Part E — position-value corrections (2026-06-28)

A systematic audit cross-referenced every FO flow/checklist position value against the panel's authoritative `ValueDescriptions`. Fixes below. Read switch states via the sim tools (PMDG field in parentheses).

### E1. 777 anti-ice AUTO (was OFF) — **safety-relevant**
Run **Cockpit Prep** (or tick the Preflight checklist items). Confirm **Wing Anti-Ice** (`ICE_WingAntiIceSw`) and **Engine Anti-Ice 1/2** (`ICE_EngAntiIceSw_0/1`) read **1 = AUTO**, not 0 = Off. (Previously the FO set them OFF.)

### E2. 777 demand pumps AUTO (was ON)
Run **Before Start**. Confirm the demand pumps (`HYD_DemandElecPump_Selector_0/1`, `HYD_DemandAirPump_Selector_0/1`) read **1 = Auto**, not 2 = On.

### E3. 777 transponder (flow now sets the mode)
Run **Before Takeoff** → confirm `XPDR_ModeSel` = **4 (TA/RA)**; run **After Landing** / **Shutdown** → confirm `XPDR_ModeSel` = **0 (STBY)**. (Previously the flow fired the wrong event and never changed the mode.)

### E4. 777 strobe / storm / recirc — **VERIFY value 1 actually turns them ON**
These were changed from 2→1 based on the panel's `{0=Off,1=On}` map, but flagged for live confirmation in case the real switch is 3-position. Run **Before Takeoff** (strobe), **Shutdown** (storm), **Cockpit Prep** (recirc) and confirm `LTS_Strobe_Sw_ON` / `LTS_Storm_Sw_ON` / `AIR_RecircFan_Sw_On_0/1` go to ON. If any does **not** turn on at value 1, tell me — the panel map is then incomplete and the value should be 2.

### E5. 737 battery ON (byte 1, was phantom byte 2)
Cold-dark → run **Electrical Power Up** → confirm the battery turns ON (`ELEC_BatSelector` = **1**) and the **"Battery: ON"** checklist item auto-ticks. (Previously it commanded byte 2 — a non-existent detent — and never detected ON.)

### E6. 737 Lower DU label — **deferred, needs in-sim name check**
The flow step `BT_LOWERDU` sends value **1**, which the panel labels **"NORM"**, but the FO announces it as **"SYS"**. Left unchanged (the middle-detent name is itself marked "verify in sim" in the panel def). Tell me the real middle-position name on the 737 lower DU selector and I'll align the label (cosmetic — spoken label only, no functional effect).

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
