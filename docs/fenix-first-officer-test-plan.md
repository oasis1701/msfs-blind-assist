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
2. **Electrical Power Up**: BAT 1/2 ON, external power ON (if available), nav/logo lights ON.
3. **Preflight**: recorder ground control ON, CVR test (listen for the test tone — a captain
   reminder, not automated), IRS 1/2/3 → NAV (no pause; alignment runs in the background —
   confirm the FO does NOT wait/announce an alignment delay), crew oxygen ON, APU fire test /
   engine 1 fire test / engine 2 fire test (each a **held** switch: TEST for ~3 s then back to
   NORMAL — listen for the fire-bell as the verification cue), packs 1/2 ON, crossbleed AUTO,
   pack flow NORMAL, hot air ON, cabin pressure mode AUTO, strobes AUTO, wing lights OFF, no
   smoking AUTO, emergency exit lights ARM, altitude reporting ON, TCAS traffic ALL. **Radar
   steps are ACTIVE, not omitted**: confirm "Weather radar: OFF" (`S_WR_SYS`) is checked as
   part of the Electrical Power Up group's auto-detect (see Part E) — the radar/PWS controls
   pre-existed on the Fenix def and both the flow and checklist wire through them.
4. **Before Start**: captain MCP reminder, **APU master ON → ~3 s dwell → APU START pulses**
   → the flow then **waits for APU AVAIL** before proceeding (confirm a real wait, not an
   instant pass-through) — APU bleed ON, fuel pumps ALL ON, external power OFF, seatbelt signs
   ON, beacon ON, **FCU speed push to managed**, **FCU heading push to managed** (confirm both
   FCU windows show "managed", not a stale/doubled push — see the FCU regression section below).
5. **Engine Start**: engine mode selector → IGN START, **engine 2 master ON** then **engine 1
   master ON** — confirm each engine actually spools and stabilizes; verify via **N2**, not
   just the master-switch position (CFM56 idle N2 ≈ 58–60%; the FO's own state evaluator uses
   ≥ 55% as "running" — cross-check against the ECAM/EWD N2 gauge).
6. **After Start**: engine mode selector → NORM, APU bleed OFF, APU master OFF, ground
   spoilers ARMED, rudder trim RESET, flaps → SimBrief takeoff setting (if SimBrief was
   loaded — see Part C; if not loaded, confirm this step is skipped/no-ops rather than
   setting a wrong flap value), nose light TAXI.
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
   "Already set" announcements with no switch movement.

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

1. In **File → Settings… → First Officer**, enable **Auto Gear Up**, **Auto Gear Down**, and
   **Auto AP** for the Fenix.
2. Fly a takeoff and confirm:
   - Positive rate of climb above ~50 ft AGL with gear down → gear auto-raises, "Positive
     rate. Gear up." announced.
   - Climbing through the **configured AP altitude** (Settings → First Officer numeric
     field, default **350 ft AGL**) → **AP1 engages**; the announcement speaks the
     configured number (e.g. "350 feet. Autopilot one engaged.").
3. On approach/descent, confirm gear auto-lowers between 2000 ft and 100 ft AGL while
   descending (not already down), "Two thousand feet. Gear down." announced.
4. **Confirm the Auto Flaps checkbox has NO effect on the Fenix.** Enable "Auto Flaps" in
   the Settings dialog's First Officer tab; fly a climb/descent and confirm flaps never move automatically —
   the Fenix `FenixFOAutoManager` deliberately stores `AutoFlapsEnabled` but never acts on
   it (the Fenix exposes no V1/VR/V2/VAPP L:vars outside the MCDU display, so a speed-based
   auto-flap schedule would be weight-blind guesswork). This is a documented non-feature,
   not a bug — verify it stays inert rather than doing something unexpected.

---

## Part E — Checklists

Open the Checklists tab; work through both the 12 auto-detect **state groups** (Electrical
Power Up, Preflight, Before Start, Engine Start, After Start, Before Takeoff, After Takeoff,
Descent, Approach, After Landing, Shutdown, Secure) and the 9 **readback (`*_CL`) groups**
(Before Start Checklist, After Start Checklist, Before Takeoff Checklist, After Takeoff
Checklist, Approach Checklist, Landing Checklist, After Landing Checklist, Parking Checklist,
Securing the Aircraft Checklist).

1. **Manual tick fires the switch.** In a state group (e.g. Electrical Power Up → "Battery 1:
   ON"), tick the item manually with the switch OFF — confirm the physical switch moves ON.
2. **Untick/retick.** Untick an item, confirm nothing reverts on the aircraft (unticking is a
   pure UI action, no reverse-action); retick it and confirm the action fires again (or is a
   no-op "Already set" if the switch is still in the target position).
3. **Auto-tick from a cockpit-side switch change.** With an item unticked, change the
   underlying switch directly in the cockpit (mouse/VR/panel), independent of the FO —
   confirm the checklist item ticks itself within a poll cycle (the state evaluator polls
   OnRequest fields roughly every second).
4. **Readback CLs (`*_CL` groups) never move switches.** Open any `*_CL` group (e.g. Before
   Start Checklist, Landing Checklist, Securing the Aircraft Checklist) and tick an
   auto-detectable item (e.g. "Signs: ON and AUTO", "Landing gear: DOWN", "Parking brake:
   SET") — confirm **no switch moves**. The item should only tick itself once the real
   switch independently reaches the stated position (set it via the matching state-group
   flow, or by hand in the cockpit). Cross-reference: this was verified structurally in Step
   1 (grep confirms every `*_CL` item's `CheckAction` is `null`).
5. **10-second manual-tick grace — no immediate revert.** Manually tick a `RevertToState`
   item whose real switch does NOT yet match (e.g. tick "Gear: UP" in After Takeoff while
   gear is still down) — confirm the tick **holds** for the ~10-second grace window instead
   of instantly reverting to unticked, then genuinely reverts if the switch still hasn't
   moved to match after that window. This exercises `ChecklistManager.ManualTickGrace`
   (10 s).
6. Spot-check the **RADAR items in the After Landing Checklist readback group** — "Radar:
   OFF" / "Predictive windshear: OFF" — confirm both auto-tick from `S_WR_SYS` /
   `S_WR_PRED_WS` and never fire an action when manually ticked.

---

## Part F — Landing / After landing / Shutdown / Secure

1. Run flows **10–12**: After Landing → Shutdown → Secure (flow 9 is Approach, already
   covered by the phase-monitor/checklist behavior above — include it here if not already
   exercised in Part C).
2. **After Landing**: landing lights retract to OFF, spoilers disarm, APU master ON (started
   for ground power handoff), weather radar OFF, predictive windshear OFF, transponder
   STANDBY, strobes AUTO, nose light TAXI, engine/wing anti-ice OFF as applicable.
3. **Shutdown**: parking brake ON, APU bleed ON, engine 1/2 masters OFF, seatbelt signs OFF,
   beacon OFF, fuel pumps ALL OFF, nose light OFF, runway turnoff lights OFF.
4. **Secure**: ADIRS OFF (all three), crew oxygen OFF, parking brake confirmed SET, APU
   master OFF, batteries 1 and 2 OFF.
5. **LANDING_CL auto-ticks on approach configuration** — during the approach (before
   touchdown), configure landing gear DOWN, signs ON, ground spoilers ARMED, flaps to
   landing setting, and confirm the **Landing Checklist** (`LANDING_CL`) group's matching
   items auto-tick from live state as each is set — "Landing gear: DOWN", "Signs: ON",
   "Ground spoilers: ARMED", "Flaps: SET" (checked via `A_FC_SPEEDBRAKE < 0.5` for spoilers
   armed and `S_FC_FLAPS > 2.5` for flaps set — confirm these thresholds match your actual
   flap lever detent for a landing configuration).
6. Confirm the **Parking Checklist** (run after Shutdown, at the gate) auto-ticks: APU bleed
   ON, engines OFF (via the `FO_ENGINES_OFF` synthetic — both N2 < 20%), seatbelt signs OFF,
   fuel pumps OFF, parking brake ON.

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
3. After Landing APU block: unchanged (timeout announces and continues).
