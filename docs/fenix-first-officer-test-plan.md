# Fenix A320 First Officer — In-Sim Test Plan

The First Officer engine's generic L:var layer (`FirstOfficer/Generic/`) was extracted from
the PMDG-specific 777/737 implementations, and a **Fenix A320 First Officer** was built on
top of it as the first non-PMDG profile. There is no automated test project (SimConnect/UI
app), so the repo owner verifies the sections below against a live sim (MSFS 2020 or 2024)
with the Fenix A320 loaded.

Open the window from **Tools → "Fenix A320 First Officer"**. The window has two tabs:
**Flows** and **Checklists** — same shared `FirstOfficerForm<TExec,TState>` UI as the PMDG
777/737 windows.

---

## Part A — Cold and dark to takeoff

Start cold-and-dark at a gate, MSFSBA connected, Fenix FO window open.

1. Run flows **1–6 in order**: Electrical Power Up → Preflight → Before Start → Engine
   Start → After Start → Before Takeoff. For each step, confirm the corresponding overhead
   / MIP / pedestal switch physically moves — read back via the panel controls (screen-reader
   focus/announce) or the Ctrl+M-style state readouts, not just the FO's own narration.
2. **Electrical Power Up**: BAT 1/2 ON, external power ON (if available), nav/logo lights ON,
   **cockpit lighting bright** (annunciator, dome and the brightness knobs — see Part G).
3. **Preflight**: recorder ground control ON, CVR test (listen for the test tone — a captain
   reminder, not automated), IRS 1/2/3 → NAV (no pause; alignment runs in the background —
   confirm the FO does NOT wait/announce an alignment delay), crew oxygen ON, APU fire test /
   engine 1 fire test / engine 2 fire test (each a **held** switch: TEST for ~3 s then back to
   NORMAL — listen for the fire-bell as the verification cue), packs 1/2 ON, crossbleed AUTO,
   pack flow NORMAL, hot air ON, cabin pressure mode AUTO, strobes AUTO, wing lights OFF, no
   smoking AUTO, emergency exit lights ARM, altitude reporting ON, TCAS traffic ALL, **ECAM
   page → DOOR**. **Radar
   steps are ACTIVE, not omitted**: confirm "Weather radar: OFF" (`S_WR_SYS`) is checked as
   part of the Electrical Power Up group's auto-detect (see Part E) — the radar/PWS controls
   pre-existed on the Fenix def and both the flow and checklist wire through them.
4. **Before Start**: **ECAM page → APU**, captain MCP reminder, **APU master ON → ~3 s dwell →
   APU START pulses** → the flow then **waits for APU AVAIL** before proceeding (confirm a real
   wait, not an instant pass-through) — APU bleed ON, fuel pumps ALL ON, external power OFF,
   seatbelt signs ON, beacon ON, **FCU speed push to managed**, **FCU heading push to managed**,
   then the Captain reminder **"Set cleared altitude on the FCU"** and **FCU altitude pushed**
   about 2 s later — a Captain reminder is spoken and the flow carries on, it does not wait for
   you, so set the altitude first or pause the flow at the reminder (confirm the FCU windows
   show "managed", not a stale/doubled push — see the FCU regression section below).
5. **Engine Start**: **ECAM page → engine**, engine mode selector → IGN START, **engine 1 master
   ON** then **engine 2 master ON**, each followed by "Engine N starting — waiting for the engine
   to stabilize" — confirm each engine actually spools and stabilizes; verify via **N2**, not
   just the master-switch position (CFM56 idle N2 ≈ 58–60%; the FO's own state evaluator uses
   ≥ 55% as "running" — cross-check against the ECAM/EWD N2 gauge). A start that never
   stabilizes must stop the flow after 120 s.
6. **After Start**: engine mode selector → NORM, APU bleed OFF, APU master OFF, ground
   spoilers ARMED, rudder trim RESET, flaps → SimBrief takeoff setting (if SimBrief was
   loaded — see Part C; if not loaded, confirm the flow instead speaks the Captain reminder
   "Flaps: set for takeoff" rather than setting a wrong flap value; the reminder is skipped only
   when the lever reads a takeoff position, so a write that did not take still gets it; with no
   plan, the After Start line "Flaps: takeoff setting" stays unticked after the flow and ticks
   itself once you set flaps 1-3, FO-20), nose light TAXI,
   **cockpit lighting dim** (Part G), **ECAM page → status**. **Observation to report:** with
   a plan loaded, if the flaps do not move after "Flaps: takeoff setting", that action line
   still shows done (the write is not verified).
7. **Before Takeoff**: autobrake MAX, weather radar → SYSTEM 1, predictive windshear AUTO,
   TCAS TA/RA, transponder AUTO, **takeoff config test** (`S_ECAM_TO` — a press-HOLD-release:
   held ~1.5 s, then the result is spoken — "Takeoff config normal." on a good config /
   "Takeoff config: check configuration." on a bad one — then the button RELEASES back to 0;
   see the momentary press-release regression section below), runway
   turnoff lights ON, **landing lights ON (both)**, nose light TAKEOFF, strobes ON.
8. Confirm checklist items **auto-tick** as each flow step lands — open the Checklists tab
   alongside the Flows tab (or check it immediately after each flow) and verify the matching
   state-group item (e.g. Electrical Power Up → "Battery 1: ON") shows ticked without you
   manually checking it.

---

## Part B — Warm start skip behavior

Load the Fenix ready-to-taxi (engines running, APU off, on ground power disconnected,
electrical/pneumatic systems already configured for taxi).

1. Run **Electrical Power Up**, then **Preflight**.
2. Every step whose switch is **already in the target state** must announce **"Already set"**
   (a quiet skip) — not re-fire the action. Spot-check at minimum: battery ON (already on),
   nav/logo lights (already on), packs ON, crossbleed AUTO, pack flow NORMAL.
3. **Nothing must toggle OFF.** In particular:
   - **Autobrake pulses** (`S_MIP_AUTOBRAKE_LO/MED/MAX` are momentary pulses in the dispatch
     table) — confirm running Before Takeoff on an already-armed autobrake does not
     un-arm/re-arm it in a way that leaves it in the wrong mode, and does not fire a pulse
     against an already-correct setting.
   - **External power pulses** (`S_OH_ELEC_EXT_PWR` is a momentary pulse) — with ground power
     already disconnected, confirm Before Start's "External power: OFF" step announces
     "Already set" and does NOT pulse the switch (which would reconnect/toggle power state
     unexpectedly on a momentary control).
4. Re-run both flows a second time back-to-back — confirm the second pass is entirely
   "Already set" announcements with no switch movement. (The exceptions are the momentary
   ECAM page buttons and the lighting-scene knobs, which have no skip test and simply select
   the page / set the levels again.)

---

## Part C — SimBrief + phase monitor

1. **Load SimBrief** (Flows tab, or the equivalent Checklists-tab button if present) with a
   filed OFP that has a transition altitude/level and a takeoff flap setting.
2. Confirm the announcement includes **transition altitude/level** and **takeoff flaps**
   (e.g. "Takeoff flaps: 1" / transition altitude and level values).
3. Fly a full leg (or simulate via teleport/time-compression) and verify:
   - **STD at the transition altitude, both sides** — climbing through the transition
     altitude, both EFIS baro references switch to STD (`S_FCU_EFIS1_BARO_STD` and
     `S_FCU_EFIS2_BARO_STD` both go to 1). Check with the altimeter hotkey/readout on
     **both** the Captain and First Officer side — this is a direct state write on the
     Fenix (not a blind toggle), so confirm both land correctly and don't desync.
   - **QNH at the transition level** — descending through the transition level, both
     baro references return to QNH mode (0) and the FO announces "set local pressure now"
     (or equivalent) as a reminder to dial in the actual QNH.
   - **Landing lights (both) + nose light at 10,000 ft, both directions** — climbing through
     10,300 ft: landing lights retract and nose light goes OFF. Descending through 9,700 ft:
     landing lights come ON and nose light goes to T.O. Confirm the 300 ft hysteresis band
     (crossings very close to exactly 10,000 don't chatter).

---

## Part D — Auto managers

1. In **File → Settings… → First Officer**, enable **Auto-raise gear on climb**,
   **Auto-lower gear on descent**, and **Auto-engage autopilot** for the Fenix.
2. Fly a takeoff and confirm:
   - Positive rate of climb above ~50 ft AGL with gear down → gear auto-raises, "Positive
     rate. Gear up." announced.
   - Climbing through the **configured AP altitude** (Settings → First Officer numeric
     field, default **350 ft AGL**) → **AP1 engages**; the announcement speaks the
     configured number (e.g. "350 feet. Autopilot one engaged.").
3. On approach/descent, confirm gear auto-lowers between 2000 ft and 100 ft AGL while
   descending (not already down), "Two thousand feet. Gear down." announced.
4. **Confirm the Auto-manage flaps checkbox has NO effect on the Fenix.** Enable
   "Auto-manage flaps" in the Settings dialog's First Officer tab; fly a climb/descent and
   confirm flaps never move automatically —
   the Fenix `FenixFOAutoManager` deliberately stores `AutoFlapsEnabled` but never acts on
   it (the Fenix exposes no V1/VR/V2/VAPP L:vars outside the MCDU display, so a speed-based
   auto-flap schedule would be weight-blind guesswork). This is a documented non-feature,
   not a bug — verify it stays inert rather than doing something unexpected.

---

## Part E — Checklists

Open the Checklists tab; work through both the 12 auto-detect **state groups** (Electrical
Power Up, Preflight, Before Start, Engine Start, After Start, Before Takeoff, After Takeoff,
Descent, Approach, After Landing, Shutdown, Secure) and the 10 **readback (`*_CL`) groups**.
The read-backs are the current Airbus A320 normal checklist (November 2021, one card with the
A32NX and A330 — FOA-8), each ECAM memo line spoken on its own. "(live)" marks a line that
speaks the value the First Officer reads when you tick it:

- **Cockpit Preparation Checklist**: Gear pins and covers: REMOVED. Fuel quantity: CHECKED
  (live, kilograms). Seat belts: ON. ADIRS: NAV. Baro reference: SET (both) (live).
- **Before Start Checklist**: Parking brake: SET (live). Takeoff speeds and thrust: SET (both)
  (live: V1, VR, V2, flex). Windows: CLOSED (both). Beacon: ON.
- **After Start Checklist**: Anti-ice: SET (live). ECAM status: CHECKED. Pitch trim: SET.
  Rudder trim: NEUTRAL (a line you confirm: the Fenix rudder-trim var has never been measured).
- **Taxi Checklist**: Flight controls: CHECKED (both). Flaps setting: SET (both) (live). Radar
  and predictive windshear: ON and AUTO. Engine mode selector: SET (live). Then the takeoff memo,
  one line each: autobrake: MAX, signs: ON, cabin: READY, spoilers: ARMED, flaps: T.O, T.O
  config: NORMAL.
- **Line-up Checklist**: Takeoff runway: CONFIRMED (both). TCAS: TA/RA (live). Packs 1 and 2:
  SET (live).
- **Approach Checklist**: Baro reference: SET (both) (live). Seat belts: ON. Minimum: SET.
  Autobrake: SET (live, read only — you set it). Engine mode selector: SET (live).
- **Landing Checklist**: the landing memo, one line each: landing gear: DOWN, signs: ON,
  cabin: READY, spoilers: ARMED, flaps: SET (live; FULL, or 3 with the GPWS LDG FLAP 3 switch on).
- **After Landing Checklist**: Radar and predictive windshear: OFF.
- **Parking Checklist**: Parking brake or chocks: SET (live). Engines: OFF. Wing lights: OFF.
  Fuel pumps: OFF (all six).
- **Securing Checklist**: Oxygen: OFF. Emergency exit lights: OFF. EFBs: OFF. Batteries: OFF.

There is no Before Takeoff, After Takeoff or Departure Change checklist.

**Expected, not a defect: flows complete their read-back** (owner decision 2026-10-06, FOA-8).
Four read-backs show complete, every line ticked, as soon as their flow finishes: the **Before
Start Checklist** after the Before Start flow, the **After Start Checklist** after After Start,
the **Approach Checklist** after Approach and the **After Landing Checklist** after After
Landing. Ticking a line there to confirm it un-ticks it ("Windows: CLOSED (both): unchecked")
and re-opens that list, so its self-ticking lines follow the aircraft again. No flow completes
the other six read-backs.

1. **Manual tick fires the switch.** In a state group (e.g. Electrical Power Up → "Battery 1:
   ON"), tick the item manually with the switch OFF — confirm the physical switch moves ON.
2. **Untick/retick.** Untick an item, confirm nothing reverts on the aircraft (unticking is a
   pure UI action, no reverse-action); retick it and confirm the action fires again (or is a
   no-op "Already set" if the switch is still in the target position).
3. **Auto-tick from a cockpit-side switch change.** With an item unticked, change the
   underlying switch directly in the cockpit (mouse/VR/panel), independent of the FO —
   confirm the checklist item ticks itself within a poll cycle (the state evaluator polls
   OnRequest fields roughly every second).
4. **Readback CLs (`*_CL` groups) never move switches.** Open any `*_CL` group (e.g. Cockpit
   Preparation, Taxi, Landing, Securing) and tick an auto-detectable line (e.g. "Seat belts:
   ON", "Takeoff memo, spoilers: ARMED", "Batteries: OFF") — confirm **no switch moves**. The
   line should only tick itself once the real switch independently reaches the stated position
   (set it via the matching state-group flow, or by hand in the cockpit). Cross-reference:
   every `*_CL` item's `CheckAction` is `null`, checked structurally by the unit tests.
5. **10-second manual-tick grace — no immediate revert.** Manually tick a `RevertToState`
   line whose real switch does NOT yet match (e.g. tick "Beacon: ON" in the Before Start
   Checklist while the beacon is off) — confirm the tick **holds** for the ~10-second grace
   window instead of instantly reverting to unticked, then genuinely reverts if the switch
   still hasn't moved to match after that window. This exercises
   `ChecklistManager.ManualTickGrace` (10 s).
6. Spot-check the **radar lines**: Taxi's "Radar and predictive windshear: ON and AUTO" ticks
   only with a radar system selected (1 or 2) AND predictive windshear on AUTO, and After
   Landing's "Radar and predictive windshear: OFF" ticks from `S_WR_SYS` / `S_WR_PRED_WS`
   with both off; neither ever fires an action when ticked by hand.
7. **Live values.** Tick a line marked (live) — e.g. "Flaps setting: SET (both)" with the lever
   at 1 — and confirm the spoken tick text carries the value ("Flaps setting: SET (both), flaps
   1: checked") and the status line shows it ("… flaps 1 — Complete"), while the tree text of
   the line does not change. With the value unreadable (e.g. before the aircraft is powered)
   the line speaks as it always did, with no value.

---

## Part F — Landing / After landing / Shutdown / Secure

1. Run flows **10–12**: After Landing → Shutdown → Secure (flow 9 is Approach, already
   covered by the phase-monitor/checklist behavior above — include it here if not already
   exercised in Part C).
2. **After Landing**: landing lights retract to OFF, spoilers disarm, APU master ON (started
   for ground power handoff), weather radar OFF, predictive windshear OFF, transponder
   STANDBY, strobes AUTO, nose light TAXI, engine/wing anti-ice OFF as applicable. The After
   Landing Checklist's "Radar and predictive windshear: OFF" ticks only after both the radar and
   the predictive windshear read OFF (the flow's "Radar and predictive windshear: OFF" check),
   never on the radar step alone.
3. **Shutdown**: parking brake ON, APU bleed ON, engine 1/2 masters OFF, **TCAS: STANDBY**,
   seatbelt signs OFF, beacon OFF, fuel pumps ALL OFF, nose light OFF, runway turnoff lights
   OFF, **cockpit lighting bright**, **ECAM page → door**.
4. **Secure**: ADIRS OFF (all three), crew oxygen OFF, parking brake confirmed SET, APU
   master OFF, **external power OFF** (a guarded toggle: it must press only while the ON
   light is lit), batteries 1 and 2 OFF, **cockpit lighting off**: the annunciator is left
   BRIGHT for the next power-up, and only the dome and the four brightness knobs go off.
5. **LANDING_CL auto-ticks on approach configuration** — during the approach (before
   touchdown), configure landing gear DOWN, seat belts ON, ground spoilers ARMED, flaps to
   the landing setting, and confirm the **Landing Checklist** (`LANDING_CL`) memo lines
   auto-tick from live state as each is set — "Landing memo, landing gear: DOWN" (only once
   the gear shows three green — see the gear-down section below), "signs: ON", "spoilers:
   ARMED", "flaps: SET" (ticks at FULL, or at 3 with the GPWS LDG FLAP 3 switch on; the
   spoken value is the lever position). "cabin: READY" never ticks by itself — it is a line you
   confirm.
6. Confirm the **Parking Checklist** (run after Shutdown, at the gate): "Engines: OFF" (via the
   `FO_ENGINES_OFF` synthetic — both N2 < 20%), "Wing lights: OFF" and "Fuel pumps: OFF" (all
   six) auto-tick; "Parking brake or chocks: SET" is a line you confirm and speaks the brake
   state ("on"/"off") when ticked.

---

## Part G — The Airbus card, in one run (2026-10-06)

The Fenix and the A32NX First Officers were brought together (FOA-8): same read-backs, same
flows. This is the Fenix half of the one in-sim run the owner does for both; the A32NX half is
Part G of [docs/fbw-a320-first-officer-test-plan.md](fbw-a320-first-officer-test-plan.md). Do
ONE cold-and-dark to secure run with the First Officer flows and watch these four things, which
are new on the Fenix and could not be tested without the sim.

**Do these first: item 2 (listen for unexpected spoken knob announcements during every lighting
scene, and check the Secure scene after the batteries are off) and item 3 (the FCU altitude
push by panel, hotkey and flow).** A knob announcement means the First Officer is no longer the
only voice, the Secure scene writes to knobs on an unpowered aircraft, and the altitude push is a
change to code the panel and hotkeys share. Items 1 and 4 can wait.

1. **ECAM pages.** The flows now select the SD page at five points: door (Preflight), APU
   (Before Start), engine (Engine Start), status (After Start), door (Shutdown). Check each
   right after its step (the aircraft's own auto-SD may move on within seconds). The buttons
   are momentary pulses: each `S_ECAM_*` must read 0 afterwards. **Expected, not a defect:** a
   page press may be followed by a background announcement such as "ECAM DOOR: On". The
   Fenix's `I_ECAM_*` page lights are monitored, announced variables, so this is a background
   state change, which CORE-7 allows; it is not the knob announcements item 2 calls defects.
2. **Lighting scenes (do first).** Bright at power-up, dim after start, bright at shutdown, off
   at securing (the annunciator stays BRIGHT then, and only the dome and the knobs go off),
   each as annunciator, dome and one "Panel and integral brightness: SET" step that
   drives four knobs (`A_OH_LIGHTING_OVD`, `A_PED_LIGHTING_PEDESTAL`,
   `A_MIP_LIGHTING_FLOOD_MAIN`, `A_MIP_LIGHTING_FLOOD_PEDESTAL`). **Listen** during every scene
   write: the First Officer's own step line must be the only voice. Any spoken knob
   announcement (for example the pedestal light, `N_PED_LIGHTING_PEDESTAL`) is a defect to
   report. And at the end of Securing, which writes the "off" scene AFTER the batteries are
   off, check whether the knob writes actually took effect (power the batteries up again and
   read the knobs): if they did not, the scene needs to move before the battery steps.
3. **FCU altitude push (do first).** Before Start's "Set cleared altitude on the FCU" then "FCU altitude:
   pushed", and the panel and hotkey pushes, as in the FCU section below (item 4).
4. **The Landing read-back.** Approach through landing with the Checklists tab open: the memo
   lines tick as each item is set (Part F, item 5), the flaps line speaks the lever position
   when you tick it, and the gear line ticks only on three green.

The rest of the card (engine start waits for the engine to run, SimBrief takeoff flaps, the
Taxi read-back with its takeoff-memo lines, TCAS and external-power lines) is covered by
Parts A, E and F above.

---

## FCU push/pull regression (both the panel and the FO now share one atomic mechanism)

Mid-feature, the Fenix def's FCU speed/heading push/pull (the panel windows Ctrl+S/Ctrl+H
and the global push/pull hotkeys) were migrated from an app-side absolute counter
(`rmpCounters`) to an atomic, sequence-prefixed calculator (RPN) read-modify-write — the same
mechanism the First Officer's `PushFcuManaged` uses. Two independent absolute-counter writers
on one relative-encoder L:var would otherwise desync (a stale counter write silently
swallowing or doubling a push). Verify both paths still work AND don't fight each other:

1. **Panel/hotkey path alone.** With the Fenix loaded, open the FCU Speed window (Ctrl+S)
   and the FCU Heading window (Ctrl+H) — or use the global push/pull hotkeys directly.
   Push speed to managed, pull it back to selected, repeat several times rapidly. Do the
   same for heading. Confirm every push/pull registers (no swallowed presses, no doubled
   jumps) and the FCU display matches what you pushed/pulled.
2. **FO flow path alone.** With the FCU in a known state (e.g. speed/heading selected), run
   the Before Start flow (flow 3) — its "FCU speed: managed" and "FCU heading: managed"
   steps should each push exactly once. Confirm both land as "managed" with no
   double-push overshoot.
3. **No cross-talk.** Use the panel window to pull the FCU speed back to selected right after
   the FO flow pushed it to managed, then re-run the Before Start flow (or just the FCU
   steps) — confirm it correctly re-detects "selected" and pushes to managed again (not
   confused by the earlier writer's state). This is the regression the atomic-RPN fix
   targets: neither writer should silently overwrite the other's notion of the counter.
4. **Altitude joins the same mechanism (2026-10-06).** The panel's and the hotkeys' altitude
   push now use the same atomic write as speed and heading (the old app-side
   `DecrementCounter` is gone), and the First Officer pushes the altitude too: Before Start's
   "Set cleared altitude on the FCU" (Captain) then "FCU altitude: pushed". Set a cleared
   altitude, then push it three ways in turn — the altitude hotkey, the panel's altitude
   control, and the Before Start flow's "FCU altitude: pushed" — and confirm each push takes
   the FCU altitude to managed exactly once (no doubled or swallowed push), including a flow
   push straight after a panel pull.

---

## PMDG regression (shared `FirstOfficerForm` — spot-check only)

The Fenix FO reuses the same generic `FirstOfficerForm<TExec,TState>` window class as the
PMDG 777/737 First Officers. Confirm nothing regressed in the shared form:

1. Load the PMDG 737, open **"PMDG 737 First Officer"** — window opens normally, both tabs
   present, Flows/Checklists behave as before (spot-check one flow, one checklist tick).
2. Load the PMDG 777, open **"PMDG 777 First Officer"** — same spot-check.
3. Switch between aircraft (PMDG 737 → Fenix → PMDG 777, etc.) and confirm each FO window
   disposes/re-creates cleanly, and the Tools menu shows only the FO item(s) matching the
   currently loaded aircraft (Fenix shows only "Fenix A320 First Officer"; PMDG 737 shows
   only "PMDG 737 First Officer"; PMDG 777 shows only "PMDG 777 First Officer"; any other
   aircraft — A320 FBW, HS787 — shows none; the A380 shows only its own). First Officer
   automation settings live in the always-visible **File → Settings… → First Officer** tab
   (no aircraft gating — the old standalone menu item is retired).

---

## Momentary press-release regression (stuck TO CONFIG fix, 2026-07)

The generic FO pulse (`LVarActionExecutor.PulseCoreAsync`) was changed from press-only
(`0 → 200 ms → 1`, leaving the button held all flight) to a full press-release
(`0 → 200 ms → 1 → hold → 0`), mirroring the panel-side `ExecuteButtonTransition` fix
(PR #128). The held `S_ECAM_TO` was re-firing the level-triggered takeoff-config check
against the landing config after touchdown (FWC phase 9, below 80 kt) — a spurious red
`CONFIG` + master-warning aural on rollout. Verify:

1. **TO CONFIG releases and announces.** On the ground with a GOOD takeoff config, run the
   Before Takeoff flow (or tick the "Takeoff config test" checklist item). Expect
   "Takeoff config normal." spoken ~1.7 s after the step fires, and `S_ECAM_TO` back at
   **0** afterwards (read via the SimConnect MCP: `get_lvar S_ECAM_TO`). Repeat with a BAD
   config (e.g. flaps 0) — expect "Takeoff config: check configuration." plus the master
   warning while held, both clearing on release.
2. **No CONFIG warning on rollout (the original bug).** Fly a full circuit with the FO
   flows: Before Takeoff → takeoff → land → decelerate below 80 kt / stow reversers.
   Confirm NO red `CONFIG` / `SLATS/FLAPS NOT IN T.O. CONFIG` / master-warning aural
   appears during the rollout.
3. **`S_ECAM_STATUS` releases.** Run the Approach flow (or tick "ECAM status page (STS)");
   confirm `S_ECAM_STATUS` reads 0 afterwards and the STS page still displayed (the page
   latches on the rising edge).
4. **Edge-triggered pulses still land.** Spot-check the other pulsed buttons now that they
   release: external power connect/disconnect, APU START (Before Start flow — APU must
   still reach AVAIL), autobrake MAX (Before Takeoff — the Descent MED pulse was removed
   2026-07-08; landing autobrake is a Captain item), AP1 engage, LS 1/2 on approach, rudder-trim
   reset. Each effect must persist after the release (they latch into their `I_*`
   indicators on the 0→1 edge — the main-branch fix live-verified this class of button).
   The ECAM page buttons the flows now press (`S_ECAM_DOOR`, `S_ECAM_APU`, `S_ECAM_ENGINE`,
   `S_ECAM_STATUS`) are in the same pulse table: each must read 0 afterwards with the page
   still showing.

---

## Known limitations (by design)

- **No auto-flaps on the Fenix** — `FenixFOAutoManager.AutoFlapsEnabled` is stored from
  settings but never acted on. The Fenix has no V1/VR/V2/VAPP L:vars outside the MCDU
  display, so a speed-based schedule isn't feasible without weight-blind guesswork.
- **Fire tests and the CVR test are captain reminders / held-switch verifications**, not
  silent automation — a blind pilot cannot observe a visual test result, so the fire bell
  (audible) is the confirmation cue for the 3-second held fire tests.
- **Anti-skid is deliberately omitted** — no corresponding Fenix L:var was found; the A320
  default is ON and JD's guide lists it as a check-only item.
- **Cockpit door lock is omitted** — the def exposes no settable lock switch.
- **Radar `WxOff`/`WxSys1` position values are assumed** (`WxOff=1`, `WxSys1=0` on the
  3-position SYS switch) pending a live `ValueDescriptions` cross-check — if the weather
  radar steps don't land on the expected positions in Part A/E, this mapping is the first
  place to check.

## APU-start gating (2026-07-06 pass)

One change: the Before Start "Waiting for APU available" wait now ABORTS the flow on its
180 s timeout instead of continuing to pulse external power off.

1. Happy path regression: cold & dark + ext power, run Before Start. Behaviour is
   unchanged — AVAIL light gates APU bleed / fuel pumps / external power off.
2. Failure path (forceable on the Fenix: run Before Start with no fuel on board, or pull
   the APU fire handle first): expect "Timed out waiting for: Waiting for APU available"
   then "Before Start flow stopped. Unable to complete: Waiting for APU available" after
   ~3 minutes, with external power still on the bus. Fix the cause, re-run the flow —
   completed steps announce "Already set" and the flow proceeds.
3. After Landing APU block: a timeout announces and continues, and "APU: ON and available"
   is left unticked when the flow completes: the AVAIL wait, not the APU master write,
   completes that line (2026-10-06), so an APU that never comes up is never latched as done.

---

## "Landing gear: UP" by lights out, not the lever (2026-09-22)

1. In flight, after takeoff, with the gear still DOWN (disable **Auto-raise gear on climb**
   first), run the **After Takeoff** flow. Expect
   "Timed out waiting for: Landing gear: UP" then "Skipping: Landing gear: UP" spoken
   *before* "After Takeoff flow complete" (non-interrupting, so both are heard). With the gear
   up and the lights out (lever UP **and** every LDG GEAR indicator — all three wheels' upper
   and lower legends, plus the lever's red arrow — dark) the wait must pass at once; it must
   not pass merely because the lever moved. There is no After Takeoff checklist line behind
   this wait any more (Airbus deleted that list); the wait only confirms.

---

## "Landing gear: DOWN" by three green, not the lever (2026-09-25)

1. On approach, with **Auto-lower gear on descent** disabled, open the Checklists tab and
   put the gear lever DOWN by hand. The Landing Checklist's "Landing memo, landing gear: DOWN"
   line must stay **unticked** while the gear is still travelling, and tick itself only once
   all three gear show down and locked — not the moment the lever moves.
2. Parked on the ground on a fresh flight load, gear down, aircraft powered, without ticking
   any Landing Checklist line by hand (a hand-worked group that reaches 100% freezes and no
   longer un-ticks): the line reads ticked. Set the
   annunciator light switch (overhead, ANN LT) to **TEST** — the line must **un-tick** (the
   test lights every gear legend, which never counts as "three green"). Return the switch to
   BRT and confirm the line ticks again.
