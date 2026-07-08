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

### B4. Auto-fly (opt-in via Settings → First Officer)
Enable Auto Gear / Auto Flaps / Auto AP in **File → Settings… → First Officer tab**, then fly:
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
- Load the PMDG 777 → Tools shows **"PMDG 777 First Officer"**, NOT "PMDG 737 First Officer".
- Load the PMDG 737 → Tools shows **"PMDG 737 First Officer"**, NOT the 777 item.
- Load an aircraft with no FO support (A320 FBW / HS787) → **no** FO item appears (the Fenix
  and A380 show only their own items).
- First Officer automation settings are a tab in **File → Settings…** and are visible for
  EVERY aircraft (the old aircraft-gated "First Officer Settings" menu item is retired).

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

## Part F — checklist bug-fixes + readback/tab UX (2026-06-28)

Read switch states via the sim tools (PMDG field in parentheses). Items F1–F4 are **777-only**; F5–F7 apply to **both** aircraft.

### F1. 777 APU starts when ticked (Before Start)
Cold-dark, APU off → on the **Checklists** tab open **Before Start** and tick **"APU: START …"**. The selector goes **ON** (`ELEC_APU_Selector` = 1), then ~2 s later **START** (= 2, springs back to 1) and the APU spools up. Previously the item was a no-op. If START does not register, the ON→START gap is too short — tell me and I'll raise `ApuOnToStartMs` (777 `AircraftActionExecutor`).

### F2. 777 BOTH external power disconnect (Before Start)
With both GPUs connected, tick **"External power: Disconnect when APU available"** → **both** ground-power units disconnect (not just one). Also check the symmetric paths: **Electrical Power Up → "External power: PUSH"** connects both; **Electrical Power Down → "APU or Ground Power switches: OFF"** disconnects both + APU off. Previously only one toggled (two CDA writes collided in the same frame). If still only one, raise `CdaWriteSpacingMs`.

### F3. 777 transponder XPNDR sets the MODE, not squawk 0002 (Before Start)
Note the squawk code, then tick **"Transponder: XPNDR"** → the transponder MODE goes to XPNDR (`XPDR_ModeSel` = 2) and the **squawk code is UNCHANGED** (previously it was overwritten to `0002`). (This is the checklist executor fix; Part E3 covered the flow's separate TA/RA path.)

### F4. 777 readback `*_CL` checklists are action-free
Open any readback checklist group (e.g. **Before Start Checklist**, **Landing Checklist**, **Shutdown Checklist**) and tick an item — **no switch should move** (e.g. ticking "Beacon: ON" must NOT toggle the beacon). The item should still **auto-tick on its own** once the real switch reaches position (set it via the matching action group / flow, or by hand in the cockpit). Spot-check that a state-group item (e.g. **Before Start → "Beacon: ON"**) DOES still fire its switch when ticked — only the `*_CL` groups changed.

### F5. Before/After Takeoff action groups (both aircraft)
On the **Checklists** tab confirm the tree now lists **"Before Takeoff"** and **"After Takeoff"** action groups directly **above** their **"… Checklist"** readback groups (like every other phase). Ticking an action-group item fires the switch (777: landing/turnoff/strobe lights, transponder TA/RA, LNAV/VNAV arm, gear/flaps; 737: landing/position lights, A/T arm, transponder, packs, start switches, turnoff, gear, autobrake). The matching **"… Checklist"** items still auto-tick from state.

### F6. Single-expand tree (both aircraft)
Expand one top-level checklist group, then expand another → the first **collapses automatically** (only one group open at a time). Child/leaf items are unaffected; NVDA navigation order stays correct.

### F7. Load SimBrief on the Checklists tab (both aircraft)
The **Checklists** tab now has a **"Load SimBrief"** button (in addition to the Flows tab). Press it → it fetches the OFP and announces transition altitudes, exactly like the Flows-tab button. While a load is in progress both buttons are disabled.

---

## Part G — action groups for remaining flow phases (2026-06-28)

On the **Checklists** tab the action/readback pairing now covers every phase that has a flow.

### G1. 777 Descent + Approach action groups
Confirm the tree shows **"Descent"** above **"Descent Checklist"**, and **"Approach"** above **"Approach Checklist"**. Tick **Descent** items → Autobrake AUTO (`BRAKES_AutobrakeSelector` = 6), FO EFIS APP and FO EFIS 20nm range actuate. Tick **Approach → Speedbrake: ARM** → `FCTL_Speedbrake_Lever` arms. Reminders (landing data, recall, approach brief, altimeters) just tick. 777 **Landing** remains readback-only (no Landing flow).

### G2. 737 Descent + Approach + Landing action groups
Confirm **"Descent"/"Approach"/"Landing"** action groups appear above their `… Checklist` readbacks. Tick: **Descent → Seatbelt signs: ON** (`COMM_FastenBeltsSelector` = 2); **Approach → EFIS mode: APP** + **range 20** (Captain EFIS); **Landing → Engine start switches: CONT** (`ENG_StartSelector_0/1` = 2) + **Speedbrake: ARMED**. Reminders tick only.

### G3. Run Related Flow from the new groups
Selecting a new action group and pressing **Run Related Flow** starts the matching flow (Descent Setup / Approach Setup on the 777; Descent / Approach / Landing on the 737).

---

## Part H — reported-bug fixes + gear-split feature (2026-06-28)

All but H2/H8 are code-confident; H2 and H8 are the items to watch in-sim.

### H1. Split auto-gear into climb / descent (BOTH aircraft)  *(feature)*
The Settings dialog's **First Officer** tab now has **two** gear checkboxes: "Auto-raise gear on positive rate (climb)"
and "Auto-lower gear at 2000 ft AGL (descent)". Verify:
- Enable **only climb** → after takeoff the gear auto-raises (announce "Positive rate. Gear up."),
  and on the approach the gear does **NOT** auto-lower.
- Enable **only descent** → gear does NOT auto-raise on climb; auto-lowers at 2000 ft AGL on approach.
- Enable **both** → both behaviours (the pre-existing behaviour).
- **Migration**: a user who previously had the old single "Auto-manage gear" enabled gets **both**
  new boxes ticked on first launch after upgrade.
- **737 gear-down lands in DOWN** (lever position 2 / three green), not the OFF detent — this was the
  copied-from-777 bug (777 uses 1=Down, 737 uses 2=Down).

### H2. Ground power (Electrical Power Up)  *(verify in sim)*
With ground power **available** at the stand: run the Electrical Power Up flow / tick "Ground power: ON"
→ the GRD POWER switch latches and the checklist item ticks. If GPU is **not** available, the flow now
announces it is **skipping** ground power (verify-fail feedback) instead of silently doing nothing.
*Cross-check:* if the cockpit-panel GRD PWR works but the FO does not with GPU available, escalate
(the FO dispatch matches the proven panel path, so that would indicate a deeper issue).

### H3. APU starts (Before Start)
Tick **"APU: ON line"** (or run the Before Start flow): the APU selector goes **ON → (≈2 s) → START**
and the APU **spools up** (EGT rises, comes on line). A direct jump to START no longer leaves it un-started.

### H4. Engine start — status + actually starting (cold-and-dark)
- On a **cold-and-dark** ramp, open the Engine Start group: items read **"Engine 1/2: running"** and are
  **NOT** auto-ticked (no more false "complete" at engine-off). They tick only once N2 ≥ ~50%.
- **Run Related Flow** on the Engine Start group (or run the Engine Start flow): each engine motors to
  ~N2 20% **before** the start lever introduces fuel, then starts normally. Confirm both engines start.
- *Tunable:* the N2 thresholds (20% fuel-introduction, 50% "running") and the `TURB ENG N2` unit
  (percent vs ratio) — adjust if the sim reads differently.

### H5. Generators (Before Taxi)
With the APU generators on, tick **"Generators: ON"** (or run Before Taxi): **BOTH** engine generators
come on line (the spaced writes no longer lose GEN 1). Checklist "Generators: ON" ticks.

### H6. APU off (Before Taxi)
After H3/H5, run Before Taxi → the APU selector goes **OFF** and the "APU: OFF" item ticks.
(Watch this together with H3 — it was a likely downstream symptom of the APU never starting cleanly.)

### H7. Probe heat (Before Taxi)
Tick **"Probe heat: ON"** (or run Before Taxi): **BOTH** probe-heat switches turn ON (spaced writes),
and the checklist "Probe heat: ON" ticks.

### H8. Landing gear "UP and OFF" auto-ticks (After Takeoff)
After takeoff, set the gear lever to **OFF** (via the After Takeoff action group or the flow). The
**After Takeoff Checklist → "Landing gear: UP and OFF"** item now ticks (detects UP *or* OFF; it used to
require UP only and never matched once the lever went to OFF).

### H9. Altimeters auto-set to STANDARD above transition (requires SimBrief)
Load a SimBrief OFP with a transition altitude, then climb through it: the FO sets **both** altimeters
to STD and announces "Transition altitude. Altimeters set to standard." (the STD push now commits via
the momentary-toggle dispatch). Descending through the transition level → back to QNH + "set local
pressure" callout. *Note:* the transition altitude still comes from SimBrief only (no default fallback,
by design). The **777** is unchanged here (it already read STD state and committed correctly).

---

## Known limitations (by design / data availability)
- **Baro-STD has no NG3 state field** — the STD push now COMMITS (momentary-toggle dispatch), but the
  phase monitor still pushes STD/QNH at the transition alt/level using its own one-shot latch (it cannot
  read whether STD is already selected). Manually toggling STD mid-flight can desync the latch.
- **Auto-STD needs a SimBrief transition altitude** — there is no default transition altitude; without a
  loaded OFP the altimeters are not auto-set (by design — issue 9 chose SimBrief-only sourcing).
- **GEN / APU-GEN auto-detect read-back may lag** — the PMDG snapshot can lag; the switch still
  actuates, only the checklist auto-tick may be slow.
- **Test items (fire test, overhead test, etc.) are captain reminders**, not automated — a blind
  pilot cannot observe the visual test result, so the FO prompts the captain to perform them.
- **Runtime-data items** (altimeters, trim, MCP courses, ILS frequencies, pressurization
  altitudes) are captain reminders — they depend on data the FO cannot set blind.

## Part I — pressurization FLT/LAND ALT from SimBrief (2026-07-01)

Setup: PMDG 737 at a gate, powered, MSFSBA connected. A SimBrief OFP filed — note its
initial/cruise altitude and destination field elevation.

1. **SimBrief load announce** — First Officer window → Load SimBrief. Alongside the
   existing transition/flaps announcements, expect: "Pressurization plan: flight altitude
   <cruise> feet, landing altitude <destination elevation> feet."
2. **Preflight flow sets both** — run the Preflight flow. After "Engine bleeds: ON" expect
   "Flight altitude: set" followed by the PR #120 monitor callout "Flight altitude
   <cruise, rounded to 500> feet", then "Landing altitude: set" + "Landing altitude
   <destination elevation, rounded to 50> feet". The captain pressurization reminder must
   NOT fire — instead "Already set: Flight and landing altitudes". Confirm the values on
   the Air Systems panel (Flight/Landing Altitude fields read them back on focus).
3. **Idempotent re-run** — run Preflight again: both steps announce "Already set: …" and
   send nothing.
4. **No-SimBrief fallback** — fresh session WITHOUT loading SimBrief; run Preflight:
   no "Flight altitude: set"/"Landing altitude: set" announcements at all (quiet skip),
   and the reminder fires: "Captain action required: Set flight and landing altitudes on
   the pressurization panel."
5. **Checklist tick fires the set** — SimBrief loaded, then dial FLT ALT wrong via the
   Air Systems panel. Checklists → Preflight → "Flight and landing altitudes: SET" shows
   unticked; tick it → both values set (two callouts, ~350 ms apart) and it re-ticks.
6. **Auto-tick from panel** — untick state (dial a window off-plan), then hand-set both
   windows to the planned values via the Air Systems panel: item auto-ticks.
7. **Descent auto-verify** — Descent Checklist → "Pressurization: landing altitude set"
   auto-ticks while LAND ALT matches destination elevation; dial LAND ALT 500 ft off →
   it unticks (RevertToState).
8. **Mode-selector readback** — Preflight Checklist → "Pressurization mode selector:
   AUTO" auto-ticks with the selector in AUTO; select MAN → unticks.
9. **777 regression** — load the 777, open its FO window, Load SimBrief. The
   "Pressurization plan: …" announce IS expected (it lives in the shared form; the 777
   evaluator no-ops the stored value). The key checks: no new 777 flow steps or checklist
   items, the 777 Preflight flow and checklists behave exactly as before, no errors.
10. **No-SimBrief manual ticks hold** — fresh session WITHOUT SimBrief: Checklists →
    Preflight → manually tick "Flight and landing altitudes: SET", and Descent
    Checklist → "Pressurization: landing altitude set". Both ticks must HOLD (no
    auto-revert on the next poll) — with no plan the match fields are indeterminate
    (NaN) and the checklist manager skips both auto-tick and revert.

---

## Part J — flow↔checklist parity audit + reported-bug fixes (2026-07-02)

Root-caused and fixed in one pass: external power never coming on (the raw `ELEC_GrdPwrSw`
struct bool reads TRUE even with no GPU at the stand — live-verified — so the power-up flow
skipped the step as "Already set"), flight directors not settable from the checklist
(`action: null`), items falsely showing checked (StayComplete latched cold-and-dark
coincidences), generators never auto-ticking from FO actions (locally-tracked state the FO
never notified), and unreliable engine starts. All state-group items are now live state
mirrors (RevertToState) and every flow switch step has a same-dispatch checklist item.

Setup: PMDG 737 cold-and-dark at a gate, MSFSBA connected, FO window open.

1. **Ground power — no GPU at the stand** — with NO ground power unit connected (PMDG FMC
   ground connections / GSX GPU absent): run Electrical Power Up. After battery + standby,
   expect "Ground power: ON", then "Waiting for: Ground power on the buses" and after ~10 s
   "Timed out waiting for: Ground power on the buses" + "Skipping" — NOT the old silent
   "Already set". The Checklists → Electrical Power Up → "Ground power: ON" item must stay
   UNTICKED (previously it false-ticked).
2. **Ground power — GPU available** — connect ground power (CDU MENU → FS ACTIONS → GROUND
   CONNECTIONS, or GSX). Run the flow (or tick the checklist item): the GRD PWR switch
   actuates, ground power comes on the buses, and the checklist item auto-ticks. Re-run of
   the flow now says "Already set: Ground power: ON".
3. **Flight directors from the checklist** — with both FDs off, tick Preflight →
   "Flight directors: ON": both FD switches move (frame-spaced) and the item stays ticked.
   With FD L already on, tick again after unticking: only FD R is pressed (no wrong-way
   toggle). Same via the Preflight flow (regression).
4. **False-check elimination (the Before Taxi report)** — cold-and-dark, fresh window:
   expand Before Taxi. "Isolation valve: AUTO" may show ticked ONLY while the valve is
   actually at AUTO; after the Preflight flow sets it OPEN, the item must UNTICK (it used
   to stay falsely checked). Same for "APU: OFF" once the APU is started in Before Start.
   Spot-check Shutdown/Secure groups no longer show complete at session start once switches
   move to non-matching states.
5. **Manual-tick grace** — tick a multi-switch item whose state is wrong (e.g. Preflight →
   "Window heat: ON" from cold): the tick must HOLD while the four spaced writes land
   (~1.5 s) and then stay ticked from live state. No visible untick/retick flicker beyond
   a brief window.
6. **Generators auto-tick** — after engine start, tick Before Taxi → "Generators: ON" (or
   run the Before Taxi flow): both GEN switches actuate AND the item + the Before Taxi
   Checklist readback "Generators: ON" tick. (Previously they never ticked from FO paths.)
7. **Engine start from the checklist** — Before Start complete (APU bleed ON), packs OFF via
   the new Engine Start → "Packs: OFF" item. Tick "Engine 2: START": start switch → GRD,
   engine motors, start lever moves to IDLE only after N2 ≥ 20%, item auto-ticks at N2 ≥ 50%.
   Then "Engine 1: START" the same. Ticking an item for an ALREADY-RUNNING engine must do
   nothing (no lever/selector movement).
8. **Engine start flow reliability** — run the Engine Start flow with APU bleed OFF and APU
   running: the flow first sets APU bleed ON (insurance step), then packs OFF, then per
   engine: GRD → "Engine 2 start valve open" wait → N2 wait → lever IDLE → valve-close wait.
   Negative test: pull the APU bleed OFF mid-motoring (or start with no air source at all):
   the start-valve wait times out within ~15 s and the flow STOPS with a clear announcement —
   fuel is never introduced.
9. **Recall items** — Before Taxi → "Recall: checked" and Descent → "Recall: checked":
   ticking presses the six-pack (System Annunciator); any latched annunciators + master
   caution light and are announced by the app's monitors; reset via the panel's Master
   Caution control.
10. **Parity additions spot-check** — new items actuate and auto-tick: Preflight (fuel pumps
    OFF incl. center, probe heat OFF, engine anti-ice OFF, recirc AUTO, logo ON, transponder
    STBY, EFIS MAP/40), Before Taxi (APU bleed OFF, packs AUTO, runway turnoff lights ON,
    lower DU SYS), Before Takeoff / After Takeoff / Descent / Approach / Landing items now
    auto-detect (e.g. "Landing lights: ON" ticks from the switches), After Landing (landing
    lights RETRACT, turnoff ON, position STEADY, engine anti-ice OFF, start switches OFF),
    Shutdown (APU generators ON, taxi/logo OFF, APU bleed ON, hydraulic pumps OFF, turnoff
    OFF, engine anti-ice OFF).
11. **IRS align removal** — Electrical Power Up no longer lists "IRS aligned" (alignment
    runs in the background; "IRS mode selectors: NAV" remains and auto-ticks).
12. **777 regression** — the shared ChecklistManager gained only the manual-tick revert
    grace; confirm 777 checklists still auto-tick/revert as before and manual ticks on
    RevertToState readbacks still un-tick (after ~10 s) when the state genuinely mismatches.

---

## Part K — auto-flaps fixes, BOTH aircraft (2026-07-02)

Auto-flaps was non-operational on the 737 AND 777 for three stacked reasons, all fixed:
(1) the per-detent `EVT_CONTROL_STAND_FLAPS_LEVER_*` events were dispatched with CDA
param 1, but they are SDK mouse-click events that only commit with MOUSE_FLAG_LEFTSINGLE
(the 777 panel's proven flap-control convention) — the lever never moved even when the
schedule fired; (2) the schedule hard-gated on the LIVE `FMC_V2`/`FMC_LandingVREF`, which
PMDG only populates around their phase (both live-read 0 at cruise) — now cached from the
last plausible read, per leg; (3) extension only ran while descending and retraction only
while climbing — level acceleration/deceleration segments never moved flaps. Now:
retraction while not descending (suppressed once approach extension begins), extension
while not climbing below 5000 ft AGL.

Enable "Auto-manage flaps" in File → Settings… → First Officer. SimBrief loaded, V-speeds entered in
the CDU (TAKEOFF REF). Repeat the full sequence on BOTH the 737 and the 777.

1. **Retraction on climbout** — take off with planned flaps; after gear-up, as IAS
   accelerates past the V2-relative thresholds, expect one-step "Flaps ..." callouts AND
   the physical lever moving each step until clean. Verify it keeps stepping through a
   LEVEL acceleration segment (e.g. altitude capture at 3000 ft AGL).
2. **V2 disappearing is survivable** — confirm retraction still works late in the climbout
   even after the FMC has cleared the takeoff V-speeds from the CDU (the cached V2 drives
   the schedule).
3. **No cruise interference** — at cruise, no flap commands, no announcements.
4. **Extension on approach** — select VREF on APPROACH REF during descent prep. Below
   5000 ft AGL, decelerating (including LEVEL deceleration on downwind/intercept), expect
   one-step extensions as IAS drops through the VREF-relative thresholds, down to landing
   flaps (737: 30 / 777: 30). Above 5000 ft AGL nothing extends regardless of speed.
5. **No extend/retract oscillation** — during the approach deceleration, confirm no
   spurious retraction fires between extension steps (approach-phase latch).
6. **Go-around** — go around, climb above 3000 ft AGL: retraction schedule resumes
   (flaps clean up as speed builds); on touchdown all per-leg state resets.
7. **Manual override respected (737)** — move flaps manually mid-schedule; the manager
   reads the real gauge detent and continues from the actual position.

---

## Part L — 777 flow-vs-checklist parity audit + systemic fixes (2026-07-02)

The 737 Part J pass applied to the 777, plus 777-specific defects found during the audit:

**777-specific bugs fixed:**
- **Multi() flow steps coalesced** — the 777 executor fired every SetSwitchMultiple
  bundle (bus ties, engine anti-ice, demand pumps ×4, fuel pumps, crossfeed, EEC,
  packs, trim air, outflow valves, jettison...) in ONE sim frame, so only the LAST
  switch of each bundle actually moved. The executor now serializes + frame-paces
  every write (the 737 pattern).
- **Flap flow steps were dead** — the Before Taxi takeoff-flap steps, After
  Takeoff/After Landing flaps-up steps were built as momentary (param 1), which the
  SDK ignores for the per-detent flap click events. The executor now forces
  MOUSE_FLAG_LEFTSINGLE for every EVT_CONTROL_STAND_FLAPS_LEVER_* dispatch.
- **Battery flow step never ticked its checklist item** (CompletesChecklistItemId
  pointed at a nonexistent id).
- **Cockpit Prep generators step skipped on the wrong field** (checked the BACKUP
  generator switches instead of the mains) — now reads ELEC_Gen_Sw_ON.
- **GPU pushes could toggle the wrong way** — connect/disconnect items now press
  only the side whose ext-power annunciator disagrees with the target.

**Systemic (same as the 737):** all state-group items are RevertToState; CDA reads are
indeterminate until the first snapshot; detection reads the new FO_ANY_GPU_ON synthetic
for external power; LNAV/VNAV tick-actions are annunciator-guarded toggles.

**Parity additions/upgrades:** Electrical Power Up gains storm lights, parking brake,
nav/logo lights, ADIRU, thrust asym comp (all auto-detect); Preflight gains no-smoking,
master lights, jettison off, crossfeed off, wing lights, equip cooling, gasper, recirc
fans (and ~a dozen former tick-only items now auto-detect: generators, backup gens,
engine pumps, center pumps off, demand pumps, cargo fire, cab utility); Before Start
demand/center-pumps/transponder now auto-detect; Before Taxi gains taxi lights ON,
storm OFF, packs AUTO (auto-detect) and a SimBrief-driven takeoff-flap action; Before
Takeoff / After Takeoff / Descent / Approach / After Landing items upgraded to
auto-detect; Shutdown gains storm ON, engine/electric/demand pump OFF, taxi lights OFF,
FD OFF, transponder STBY, and center pumps in the fuel-pump item; the duplicate
preflight fuel-control item was removed.

Test (777, cold-and-dark → shutdown):
1. Run each flow; verify every switch in a Multi bundle now moves (spot-check: both
   bus ties, both engine anti-ice selectors, all four demand pumps, both crossfeeds).
2. Before Taxi flow sets the SimBrief takeoff flaps — the lever MOVES now; After
   Takeoff / After Landing flows retract it.
3. Electrical Power Up flow ticks "Battery: ON" in the checklist group.
4. Cockpit Prep with backup gens ON but main gens OFF: the generators step fires
   (previously skipped as "Already set").
5. Checklists: tick every new/upgraded item and confirm the switch moves + the item
   holds; confirm no item shows checked while its switch is elsewhere (cold-start
   false-latch gone — e.g. Shutdown/Secure groups no longer show complete at session
   start once switches move).
6. External power items: tick connect with GPU available (connects, ticks), tick
   disconnect with APU running (disconnects); ticking disconnect with no GPU
   connected does NOT connect one.
7. LNAV/VNAV: tick when already armed — no press fires (annunciator guard).

---

## Part M — captain-action parity in the checklist groups (2026-07-02)

Every CaptainReminder step in the flows now has a matching reminder item in the flow's
related checklist group, positioned in flow order.

- **737**: Preflight gains "Perform the overhead and fire tests as required" (last);
  Before Start gains "Set MCP airspeed, heading and initial altitude" (first) and
  "Confirm ground power and chocks removed, doors closed, and taxi clearance" (last);
  Before Taxi gains "Set engine and wing anti-ice as required" (after start switches
  CONT), and the takeoff-flaps reminder moved to flow order (after the turnoff lights,
  before the lower DU).
- **777**: Preflight gains "Reset checklists and obtain IFR clearance" and "Obtain ATIS"
  (end, matching the Cockpit Prep flow tail); Before Start gains "Obtain taxi clearance"
  and "Start ACARS if required" (end), and the three trim items moved up to follow the
  MCP/LNAV-VNAV block (flow order); Before Taxi gains "Set stabiliser trim for takeoff"
  (after the flight-controls check).

Test: expand each group listed above and confirm the reminders appear at the stated
positions; tick them (manual tick holds — reminders have no action and no auto-detect).

---

## Part N — 737 APU electrical transfer (2026-07-02)

The 737 Before Start never transferred the electrical load to the APU: the APU GEN 1/2
bus-transfer buttons (momentary on the 737, unlike the 777 where the gen switch is armed
in preflight) were never pressed and ground power was never dropped. Also fixed while in
the same power chain: the Shutdown flow only pressed APU GEN 1, and the After Landing
APU step only set the selector to ON without ever pressing START (the arrival APU never
actually started).

Test (737):
1. **Before Start flow** — cold-and-dark on ground power, run Electrical Power Up +
   Preflight + Before Start. After "Waiting for the APU to come on line", expect
   "Waiting for: Waiting for the APU generator" (added by the 2026-07-06 APU-gating pass —
   Part S supersedes this part's sequencing), then
   "APU generators: ON" (both transfer-bus source annunciators move to the APU) followed
   by "Ground power: OFF" (buses stay powered, now by the APU; GRD POWER AVAILABLE stays
   lit as long as the GPU is still connected). With no GPU connected, the Ground power
   step reads "Already set: Ground power: OFF".
2. **Checklist parity** — Before Start group: "APU generators: ON" (tick fires both
   buttons; action-only, no auto-detect — the switches are stateless) and "Ground power:
   OFF" (auto-ticks once no external power is on the buses) sit right after "APU: ON
   line".
3. **After Landing** — flow (or ticking "APU: ON line" in the After Landing group)
   actually STARTS the APU: selector ON → 2 s → START, START springs back to ON and the
   APU spools up. Previously it only reached ON and never started.
4. **Shutdown** — the "APU generators: ON" step presses BOTH buttons; after the start
   levers go to CUTOFF, both transfer buses stay powered by the APU (no partial
   power drop on bus 2).

---

## Part O — 737 ground power items are stateless presses (2026-07-02)

Follow-up to Parts J/N: even the FO_GPU_ON composite false-positives when a GPU is
plugged in while the APU (or engines) power the buses — AVAILABLE is lit and the
ground-service buses are hot, so "Ground power: ON" pre-checked / the flow skipped it as
"Already set" and external power still never came on (live-verified at a gate with APU
running). The NG3 exposes NO reliable "ext power on bus" signal — same reason the panel
uses stateless Ground Power On/Off buttons. Now: the checklist "Ground power: ON"
(Electrical Power Up) and "Ground power: OFF" (Before Start) items are stateless press
actions — every tick presses (directional, idempotent on repeats) — and the power-up
flow presses ON unconditionally (its 10 s wait still announces a timeout when no GPU
exists at the stand).

Test (737, at a gate with ground power available and the APU RUNNING):
1. Checklists → Electrical Power Up → "Ground power: ON" shows UNTICKED (no more
   pre-check). Tick it: external power takes the transfer buses (APU GEN OFF BUS
   annunciators light, APU still running).
2. Run the Electrical Power Up flow in the same state: "Ground power: ON" is PRESSED
   (no "Already set" skip).
3. Before Start → "Ground power: OFF" tick: external power drops, APU generators carry
   the buses again (after "APU generators: ON").
4. No-GPU stand: the flow's wait announces the 10 s timeout; ticking the checklist item
   presses harmlessly and simply stays ticked (stateless).

---

## Part P — transition-altitude STD, after-landing flaps, GPU item removal (2026-07-02)

1. **STD at transition altitude (BOTH aircraft)** — two fixes:
   (a) Crossing hardening: the direction gate on the transition (and 10k) crossings could
   miss the one 1 Hz tick that first sees the aircraft past the band if VS momentarily
   lulled (altitude capture) — the latch still flipped, permanently burning the crossing.
   Gates are now "not descending"/"not climbing" so a crossed band always fires.
   (b) No-SimBrief fallback: with no transition altitude loaded the monitor now announces
   once, climbing through 18,000: "No transition altitude loaded — set standard
   altimeters as required. Load SimBrief in the First Officer window..." (speech only —
   deliberately NO default STD push: a wrong-region default would toggle correctly-set
   altimeters the wrong way on the 737, which has no STD readback).
   Test: load SimBrief in the FO window (expect the "Flight phase monitor: transition
   altitude ..." announce), fly a climb through the TA → "Transition altitude. Altimeters
   set to standard." and both altimeters flip to STD. Then a flight WITHOUT loading
   SimBrief → the passing-18,000 reminder fires instead, no switch is touched.
2. **737 after-landing flaps** — the flow never had a flaps-up step (its description
   claimed one) and the checklist group had no flaps item. Added: flow retracts flaps
   after the start switches go OFF; checklist "Flaps: UP" auto-detects off the TE-flap
   gauge (MAIN_TEFlapsNeedle) and ticking it retracts. (The 777 already had both.)
   Test: land with flaps 30, run the After Landing flow → lever to UP, gauge runs to 0,
   checklist item ticks.
3. **"Ground power: OFF" removed from the Before Start checklist** (user decision) — the
   flow still drops ground power right after the APU generator transfer, and the BS_GND
   reminder covers confirmation.
4. **APU generators wiring re-verified (code audit)** — both paths press BOTH buttons,
   frame-paced: the flow BS_APUGEN is a Multi over EVT_OH_ELEC_APU_GEN1_SWITCH +
   EVT_OH_ELEC_APU_GEN2_SWITCH (Directional/LEFTSINGLE, ids base+28/29, identical to the
   panel's proven APU Gen On buttons), and the checklist item fires SetApuGenerators(1)
   through the same paced gate. In-sim check: with the APU on line, run Before Start (or
   tick "APU generators: ON") → BOTH transfer buses move to the APU (no SOURCE OFF
   annunciator remains lit on either side).

---

## Part Q — pilot-paced 737 engine start + shutdown wait removal (2026-07-02)

1. **737 Engine Start checklist is now pilot-paced (777 convention).** The old
   "Engine 1/2: START" tick ran a silent background sequence (GRD, then a wordless wait
   of up to 60 s for N2, then the lever) — after the starter moved it felt frozen. The
   group now has per-engine items the pilot drives:
   - "Engine 2 start switch: GRD" — tick moves the start switch (action-only; the GRD
     detent is solenoid-held and springs back at starter cutout).
   - "Engine 2 start lever: IDLE (at 25 percent N2)" — tick moves the lever; the pilot
     times the fuel introduction. Also auto-ticks if the lever is moved in the cockpit
     or via the app panels (derived lever field).
   - "Engine 2: running" — auto-verifies at N2 ≥ 50%.
   (Same trio for engine 1; "Packs: OFF" unchanged at the top.)
   Test: start both engines purely from the checklist, timing the levers yourself; then
   start via "Run Related Flow" (still fully automated with spoken waits and the
   start-valve/N2 abort guards) and confirm the lever + running items auto-tick.
2. **Shutdown flows no longer stall after cutoff.** The 777's fixed 60 s "Engines
   spooling down" wait is removed (flow proceeds straight from fuel cutoff to the next
   steps); the 737's start-valve wait (an instant no-op) is removed too.
   Test: run each shutdown flow with engines running — after "Fuel Control: CUTOFF" /
   "Engine start levers: CUTOFF" the next step announces within a second or two.

---

## Part R — landing lights at the 10k crossing, ALL lights, both aircraft (2026-07-02)

Audit findings, all fixed:
- **737**: `SetLandingLights` only drove the two RETRACTABLE lights — the two FIXED
  inboard lights were never touched by any FO path. Worse, the below-10k crossing set
  the retractables to 1 = EXTEND (deployed but DARK); ON is 2. Now every landing-light
  action drives all four (retractables to the target, fixed to on only for ON), the
  10k monitor uses 2 below ten thousand, and the Before Takeoff / After Landing flows
  fire all four events. After Landing's OFF detection reads all four switches.
- **777**: everything went through the ganged EVT_OH_LIGHTS_LANDING_LNR event, which
  was never live-verified — the executor and all three flow steps now fire the three
  individual per-switch events (L / NOSE / R), the same events the panel's proven
  landing-light controls use.
- **Both**: the 10k crossing gained the same direction-tolerant gates as the
  transition crossing (a momentary VS lull on the crossing tick can no longer burn
  the latch without firing).

Test (each aircraft, FO window open):
1. Climb through 10,300 ft → "Above ten thousand. Landing lights off." — verify ALL
   landing lights go off (737: both retractables to RETRACT AND both fixed inboards
   off; 777: L, NOSE and R all off).
2. Descend through 9,700 ft → "Below ten thousand. Landing lights on." — verify ALL
   lights come ON and actually illuminate (737 retractables at ON, not just extended).
3. Before Takeoff flow/item and After Landing flow/item: all lights on / all off.

## Part S — APU-start gating (2026-07-06 pass; 737 + 777)

Ground-power→APU transfers now wait on real availability signals and ABORT the Before
Start flow on a start failure (ground power stays connected). After Landing APU waits
announce a timeout but continue.

### S.1 737 Before Start, happy path (cold & dark + GPU)
1. Cold & dark, GPU connected via the power-up flow. Run Before Start.
2. Expect, in order: "APU selector: ON" → "Waiting 2 seconds…" → "APU selector: START" →
   "Waiting for: Waiting for the APU to come on line" (selector spring-back) →
   "Waiting for: Waiting for the APU generator" → "APU generators: ON" →
   "Ground power: OFF" → rest of flow.
3. PASS: the APU GEN transfer and ground-power drop happen only after the blue
   APU GEN OFF BUS light is lit (spot-check the overhead panel state); no bus power loss.
4. Timing note for the 30 s budget on the generator wait: from the START command, note the
   seconds between the selector springing back and "APU generators: ON" being spoken —
   report if the generator wait ever announces "Timed out" on a healthy start.

### S.2 737 EGT threshold calibration
1. Cold & dark: FO window open, APU never started. Re-run Before Start — the three APU
   command steps must RUN (not "Already set"), proving IsApuRunning() reads false cold.
2. With the APU running: re-run Before Start — the three APU command steps must announce
   "Already set: …", proving EGT ≥ 100 while running. If either direction misbehaves,
   tune `AircraftStateEvaluator.ApuRunningEgt` (currently 100).

### S.3 737 re-run after completed transfer
1. Immediately after a successful S.1, run Before Start again.
2. Expect: APU command steps "Already set", the come-on-line wait passes instantly, the
   generator wait announces "Timed out waiting for: Waiting for the APU generator" then
   "Skipping: …" after 30 s (the blue light is out because the APU is on the buses), and
   the flow CONTINUES to completion. PASS: no "flow stopped", no state change.

### S.4 777 Before Start, happy path
1. Cold & dark + GPU (power-up flow), run Before Start.
2. Expect "Waiting for: Waiting for the APU to start" to END as the APU reaches speed
   (not a fixed 90 s — on the 777 the APU start typically completes near 60-90 s), then
   "Waiting 5 seconds: APU stabilising", then hydraulics/fuel/beacon, then the two
   ground-power disconnects. PASS: EICAS shows the APU running before the GPU drops and
   the buses never blink.
3. Re-run: the wait passes instantly (APURunning already 1).

### S.5 777 After Landing
1. After landing + rollout, run After Landing.
2. Expect the APU block to end with "Waiting for: Waiting for the APU to start" completing
   when the APU is actually up (previously it keyed on the selector and completed
   immediately). A timeout announces and the flow continues (Skip policy).

### S.6 Timeout/abort path (both PMDG jets, optional)
Hard to force without a scripted APU failure; the Stop mechanism is the same code path as
the shipped engine-start N2 aborts (Part tested earlier). If a start failure ever occurs
naturally: expect "Timed out waiting for: …" then "Before Start flow stopped. Unable to
complete: …", with ground power still connected.

## Part T — checklist ticks stay ticked + position-lights walk (2026-07-06)

Two fixes: (1) the manual-tick revert grace is now ACTION-AWARE — a tick's revert
protection holds until its switch action has actually finished (covers the 4–20 s
transponder walk and anything queued behind it on the dispatch gate), then a fresh
10 s window covers the CDA readback; (2) `EVT_OH_LIGHTS_POS_STROBE` was live-proven
CDA-deaf (like the transponder rotary) and now click-walks on BOTH the FO path and
the panel's Position Lights combo.

### T.1 Transponder checklist ticks stick (the reported bug)
1. On the ground, transponder at STBY. Open the FO checklist, Before Start group.
2. Tick "Transponder: TA/RA". Wait 30 s without touching anything.
3. PASS: the item never unticks (previously it reverted ~10 s in, mid-walk, then
   re-ticked when the walk landed). The transponder ends at TA/RA.
4. Repeat for Shutdown "Transponder: STBY" (TA/RA → STBY walk). Same expectation.

### T.2 Position-lights checklist ticks stick AND actually move the switch
1. Position lights at STEADY. Before Takeoff group: tick
   "Position lights: STROBE & STEADY".
2. PASS: the overhead switch physically moves to STROBE & STEADY within ~3 s
   (previously it NEVER moved — the event was a silent no-op) and the tick stays.
   One intermediate detent may pass silently (the walk suppresses per-detent chatter).
3. After Landing group: tick "Position lights: STEADY" — switch walks back down, tick
   stays.

### T.3 Tick queued behind a walk stays ticked
1. Transponder at STBY, position lights STEADY. In quick succession tick
   "Transponder: TA/RA" (Before Takeoff) and then immediately
   "Position lights: STROBE & STEADY".
2. PASS: both ticks hold; the position-light walk runs after the transponder walk
   (writes are serialized) and both switches end on target.

### T.4 A genuinely wrong state still reverts (regression guard)
1. Tick any auto item, then within ~5 s move its switch to the WRONG position in the
   virtual cockpit (e.g. tick "Taxi light: ON", then switch it OFF in the VC).
2. PASS: the item unticks ~10–15 s after the switch settles wrong — revert still
   works; the fix only protects ticks while their own action is in flight.

### T.5 Panel Position Lights combo (was silently broken)
1. Open the 737 Lights panel, Position Lights combo. Pick "STROBE & STEADY".
2. PASS: the switch moves (previously nothing happened); no per-detent chatter; a
   re-pick mid-walk announces "Still setting position lights, please wait." and the
   newest pick wins. Pick "STEADY" to walk back.
3. Regression spot-check while here: the panel Transponder Mode combo still walks
   correctly (shares the counter that now also covers position-light walks).

### T.6 Before Takeoff / After Landing flows set the position lights
1. Run the Before Takeoff flow: the "Position lights: STROBE & STEADY" step must
   physically move the switch (previously a silent no-op) and the checklist's
   BTKO_STROBE auto-ticks.
2. After Landing flow: "Position lights: STEADY" moves it back.
