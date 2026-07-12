# FlyByWire A32NX First Officer — In-Sim Test Plan

The First Officer engine's generic L:var layer (`FirstOfficer/Generic/`), already proven by the
Fenix A320 and FBW A380 First Officers, now has a **FlyByWire A32NX profile**
(`FirstOfficer/FBWA320/`). It is a hybrid: procedures/phase structure are reconciled from the
**Fenix A320** profile (same Airbus SOPs, 2 engines, same phase layout), while the write mechanism
and auto-flaps schedule are reused from the **FBW A380** profile — the executor delegates every
control write to `FlyByWireA320Definition.ApplyUIVariable` (a new thin public wrapper around the
def's existing `HandleUIVariableSet`, the same verified path the A320 panels use), wrapped in a
suppressed-announcer guard so the FO's own step narration stays the single voice. There is no
automated test project (SimConnect/UI app), so the repo owner verifies the sections below against
a live sim (MSFS 2020 or 2024) with the FlyByWire A32NX loaded.

Open the window from **Tools → "FlyByWire A32NX First Officer"**. The window has two tabs:
**Flows** and **Checklists** — the same shared `FirstOfficerForm<TExec,TState>` UI as the PMDG
777/737, Fenix, and A380 windows.

---

## Highest-risk items to probe FIRST

These are derived-from-code (design spec §7) and unverified until in-sim. Probe them before
running the full 12-flow sequence — a failure here likely means every downstream step in that
system is also silently wrong:

1. **Baro polarity is INVERTED vs the A380 template.** The A32NX is `PULL=STD, PUSH=QNH`
   (`A32NX.FCU_EFIS_L/R_BARO_PULL`/`_PUSH` via `FireFCUButton`) — the **opposite** of the A380's
   PUSH=STD/PULL=QNH. Confirm the Cockpit Preparation "baro to hectopascals" step and the
   transition-altitude/level phase-monitor callouts actually flip the correct direction on both
   sides. The `_EIS_BARO_IS_STD` L:vars are DEAD (never written) — state must read off
   `A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE == 0`, not those.
2. **Indexed nose/landing-light events.** Lineup and the 10k-feet phase monitor drive the stock
   indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET` events. Confirm both the landing and nose/taxi
   beams actually illuminate/extinguish at Lineup, After Takeoff, the 10,000 ft crossing (both
   directions), and After Landing — an indexing mistake here is silent (no error, just a dark or
   permanently-lit light).
3. **Autobrake calc-path.** "Autobrake: MAX" writes `A32NX_AUTOBRAKES_ARMED_MODE_SET` via the
   calculator path (buttons `A32NX.AUTOBRAKE_BUTTON_LO/MED/MAX`). Confirm the write actually arms
   the mode (read back via the autobrake annunciator/ECAM), not just that the calc string executes
   without error.
4. **Wing anti-ice write.** Confirm the OFF/ON write targets
   `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION` (the real cockpit-button input) and that the write
   sticks — the `_SYSTEM_*` L:vars are Rust-owned per-frame outputs and any write to those reverts
   within ~2 s regardless of phase (a known A32NX gotcha, not specific to this feature).
5. **ECAM ECP page pulses.** Unlike the A380 (a sticky page-index write), the A320 selects SD
   pages via **momentary ECP button press/release pulses**
   (`ECAM_ENG/APU/BLEED/COND/ELEC/HYD/FUEL/PRESS/DOOR/STS`). Confirm each ★-marked ECAM page step
   (DOOR at Preflight/Parking, APU at Before Start, ENG at Engine Start, STS at After Start)
   actually changes the visible SD page — and confirm the pulse is a genuine press-then-release
   (a stuck "pressed" ECP button is a plausible failure mode). There is deliberately **no F/CTL or
   WHEEL ECP key** — the takeoff-config-test ECAM check stays a Captain reminder.
6. **`PUSH_OVHD_CALLS_ALL` / other `_IS_PRESSED` release pulses.** Cabin notify
   (`CABIN_CALL_ALL` → `PUSH_OVHD_CALLS_ALL`) and the fire-test PBs
   (`A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED`, `A32NX_FIRE_TEST_ENG1/ENG2/APU`) must each land as **two
   separate writes** — 1, then 0 — never a same-frame `1…0` calc string (the Rust sampler misses a
   same-tick pulse). Confirm the cabin chime/mechanical horn actually STOPS after the notify call
   (a stuck 1 is an endless horn) and that each fire test both sounds AND self-cancels.
7. **Cockpit-lighting analog potentiometer writes.** The six flood/integral knobs
   (`BRIGHT_PEDESTAL_SET`, `BRIGHT_MAINPANEL_SET`, `BRIGHT_GLARESHIELD_CAPT_SET`,
   `BRIGHT_GLARESHIELD_FO_SET`, `BRIGHT_GLARESHIELD_INTEG_SET`, `BRIGHT_OVERHEAD_INTEG_SET`) are
   indexed `LIGHT_POTENTIOMETER_SET`-style analog writes with **no readable "target reached"
   state** — confirm the "Panel and integral brightness: SET" grouped action step visibly changes
   cockpit lighting for the Prep/Taxi/Securing scenes in §4.1 of the design spec, since a silent
   write failure here has no other symptom (the checklist item is `ActionManual` and cannot
   auto-detect a miss).

---

## Part A — Window & gating

1. Load the FlyByWire A32NX. Confirm **Tools → "FlyByWire A32NX First Officer"** appears. (First
   Officer automation settings live in **File → Settings… → First Officer**, a tab visible for
   every aircraft.)
2. Open the window — confirm it has exactly two tabs, **Flows** and **Checklists**, matching the
   other FO windows' layout.
3. Switch to each of the other aircraft in turn (Fenix A320, FBW A380, PMDG 737, PMDG 777, any
   other loaded aircraft e.g. HS787) and confirm **"FlyByWire A32NX First Officer" is NOT
   visible** on any of them — only its own aircraft-specific FO item(s) show.
4. Switch back to the A32NX — confirm the menu item reappears.

---

## Part B — Flows (cold-and-dark → shutdown, all 12 in order)

Start cold-and-dark at a gate, MSFSBA connected, A32NX FO window open. Run the 12 flows in order:
**Electrical Power Up → Preflight → Before Start → Engine Start → After Start → Before Takeoff /
Taxi → After Takeoff → Descent → Approach → Landing → After Landing → Shutdown / Parking**. For
each step, confirm the corresponding overhead/glareshield/pedestal control physically moves —
read back via the panel controls (screen-reader focus/announce) or the panel display fields, not
just the FO's own narration.

1. **Electrical Power Up**: safety checks (masters off, mode NORM, gear down, wipers off, WXR
   off, park brake on), batteries ON, ext power ON (a guarded momentary), nav + logo lights ON,
   **cockpit lighting SET (day/prep scene)** per §4.1 — ANN Bright, dome Bright, standby compass
   ON, floods/integrals to their Prep defaults.
2. **Preflight**: CVR test (Captain reminder — no verified write), IRS 1-3 to NAV, crew oxygen
   ON, **fire tests (APU/ENG1/ENG2, held ~3 s)** — confirm each sounds the fire bell/master
   warning and self-cancels, packs ON, crossbleed AUTO, pack flow NORMAL, hot air ON, pressurization
   mode AUTO, strobes/wing lights/no-smoking/emergency-exit lighting, altitude reporting (Captain
   reminder), TCAS traffic ALL, flight directors 1/2 ON, **ECAM page → DOOR**. Captain reminders:
   QNH, FCU altitude, squawk, EFB, MCDU.
3. **Before Start**: APU master ON → dwell → START → wait for AVAIL (Stop on timeout), APU bleed
   ON, fuel pumps ON (all tanks), ground power OFF, seatbelt signs ON (confirm the sign
   illuminates, not just the switch), beacon ON, FCU speed pushed managed, FCU heading pushed
   managed, cockpit door LOCKED, **ECAM page → APU**. Captain: doors/ground services, thrust
   levers idle, clearance.
4. **Engine Start**: mode selector → IGN/START, engine 1 master → START, confirm the flow waits
   for N2 to cross the running threshold (Stop on timeout) before engine 2, **ECAM page → ENG**.
5. **After Start**: mode → NORM, APU bleed/master OFF, ground spoilers ARM (confirm real state
   `A32NX_SPOILERS_ARMED` reads armed — the Act-key write itself has no readback), rudder trim
   RESET fires once, takeoff flaps set per SimBrief (if loaded), nose light → TAXI, **cockpit
   lighting DIM for taxi/flight** (ANN Dim, dome Dim) per §4.1, **ECAM page → STS**. Captain:
   anti-ice as required, pitch trim.
6. **Before Takeoff / Taxi**: autobrake MAX (confirm it actually arms — see risk item 3 above),
   weather radar ON, predictive windshear AUTO, TCAS TA/RA, transponder AUTO, takeoff-config test
   (Captain reminder — no F/CTL ECP key), turn-off lights OFF, landing lights ON, nose light →
   T.O., strobes ON, **cabin notify: advise cabin for takeoff** (`CABIN_CALL_ALL` — confirm the
   chime sounds once and stops, see risk item 6). Captain: takeoff clearance.
7. **After Takeoff**: ground spoilers DISARM, packs ON, turn-off lights OFF. (Gear/AP handled by
   the auto-managers; 10k-feet lights and transition-altitude STD are handled by the phase
   monitor — see Part E.)
8. **Descent**: seatbelt signs ON (landing autobrake stays a Captain item on every aircraft —
   confirm it is NOT automated here); arrival-performance and MCDU reminders.
9. **Approach**: LS (localizer/glideslope) pushed captain + F/O sides, **cabin notify: notify
   cabin for landing**. Captain: minimums, engine mode, ECAM page as required (reminder).
10. **Landing**: readback-only (`LANDING_CL`) — missed-approach altitude reminder, spoilers ARMED
    confirmed via real state (no dedicated Landing flow, matching the A320-family convention).
11. **After Landing**: spoilers disarm, flaps up (Captain item, not automated), weather
    radar/predictive windshear OFF, transponder STBY, TCAS STBY, strobes AUTO, landing lights
    OFF, nose light → TAXI, APU start for the gate, anti-ice OFF.
12. **Shutdown / Parking**: parking brake ON, APU bleed ON, engine masters OFF (confirm the flow
    waits for `FO_ENGINES_OFF` before continuing), seatbelt signs OFF (confirm the sign actually
    extinguishes before the next step), beacon OFF, fuel pumps OFF, nose/turn-off lights OFF,
    cockpit door UNLOCKED, **cockpit lighting SET (parking/bright scene)** per §4.1, **ECAM page →
    DOOR**.
13. **Securing** (FSFO 12 tail): IRS 1-3 OFF, crew oxygen OFF, emergency-exit lighting OFF,
    no-smoking OFF, APU bleed/master OFF, ext power OFF, batteries OFF, **cockpit lighting OFF**
    (all knobs to 0, dome off, standby compass off) per §4.1.
14. **No double-announce.** Throughout all flows, confirm each step is announced **once** — the
    executor wraps every write in a suppressed-announcer guard around `ApplyUIVariable`, so the
    def's own internal `Announce()` calls are dropped and only the FO's step narration speaks.
    See the note at the end of this document about the ONE expected exception (continuously
    monitored vars).
15. **Captain reminders announce and wait.** Every `Captain(...)` step (CVR test, altitude
    reporting, takeoff-config test, ECAM page selection where no ECP key exists, minimums,
    landing autobrake, flaps, IFR clearance, MCDU programming, etc.) is spoken as a reminder and
    the flow **pauses for acknowledgement** — confirm it does not silently auto-complete.
16. **Already-in-target-state steps announce as skipped.** Re-run a flow (or a step) whose switch
    is already correct — confirm the matching step announces a quiet skip ("Already set" or
    equivalent) instead of re-firing the action. This matters especially for autobrake MAX
    (re-running Before Takeoff / Taxi while MAX is already armed must not disarm/re-arm it) and
    ECAM page pulses (re-selecting a page already displayed should not double-pulse the ECP
    button).

---

## Part C — Checklists tab

Open the Checklists tab. There are 12 STATE/ACTION groups mirroring the 12 flows above, plus the
`*_CL` readback groups (Cockpit Prep, Before Start, After Start, Taxi, Lineup, Before Takeoff,
Approach, Landing, After Landing, Parking, Securing).

1. **STATE items auto-tick as switches reach position.** With an item unticked, change the
   underlying switch directly in the cockpit (mouse/VR/panel), independent of the FO — confirm
   the checklist item ticks itself within about 1-2 s.
2. **Manual tick fires the switch.** Tick an unticked STATE item manually (e.g. "APU: ON" in
   Before Start with the APU actually off) — confirm the real switch moves.
3. **Untick/retick is idempotent.** Untick a ticked item, then retick it — confirm the action
   either fires again or is a no-op ("Already set") if the switch is still correct.
4. **Autobrake special case.** In the Before Takeoff / Taxi group, "Autobrake: MAX" must detect
   on the real armed-mode state, not a momentary press var. Tick it with autobrake disarmed —
   confirm it arms. Untick and retick while MAX is already armed — confirm the guarded action
   does NOT re-send a conflicting mode.
5. **Readback (`*_CL`) items never fire a switch.** Open any `*_CL` group and tick an
   auto-detectable item — e.g. "Parking brake: ON", "Beacon: ON", "Weather radar: ON", "Spoilers:
   ARMED" (reads `A32NX_SPOILERS_ARMED` directly), "Engines: OFF" (via `FO_ENGINES_OFF`). Confirm
   **no switch moves** for any of these — the item only ticks itself once the real state
   independently reaches the stated condition.
6. **Cockpit-lighting checklist items.** Confirm the ANN, dome, and standby-compass items
   auto-detect their live 3-state/discrete state independently (§4.1), while "Panel and integral
   brightness: SET" is a manual-only action item with no auto-revert (the analog knobs have no
   readable target state).
7. **ECAM page items.** Tick an ECAM-page checklist item by hand (e.g. "ECAM page: APU" in Before
   Start) — confirm it pulses the matching ECP button once, not repeatedly, and does not
   re-pulse if ticked again while that page is already displayed.
8. **Spot-check a group with no matching STATE/ACTION group** (e.g. Descent, which is mostly
   reminders) — confirm it behaves as pure reminders with no auto-ticking.
9. **Engine anti-ice / wing anti-ice readbacks.** Turn anti-ice ON by hand, confirm the After
   Landing item UN-ticks (RevertToState); run the flow or tick the item and confirm it re-ticks
   as the switch goes OFF. Watch specifically for wing anti-ice reverting on its own within ~2 s
   if the write targeted the wrong (Rust-owned output) L:var — see risk item 4.

---

## Part D — Auto managers

1. In **File → Settings… → First Officer**, enable **Auto Gear Up**, **Auto Gear Down**, and
   **Auto AP** for the A32NX.
2. Fly a takeoff and confirm:
   - Positive rate of climb above ~50 ft AGL with gear down → gear auto-raises, **"Positive rate.
     Gear up."** announced.
   - Climbing through the **configured AP altitude** (Settings → First Officer numeric field,
     default **350 ft AGL**) → AP1 engages; the announcement speaks the configured number.
3. On approach/descent, confirm gear auto-lowers descending through roughly 2000 ft AGL (and
   above 100 ft AGL, not already down) — **"Two thousand feet. Gear down."** announced.

### Auto-flaps (opt-in, reused near-verbatim from the A380)

`FbwA320FOAutoManager.CheckFlaps` drives the flap lever from the shared `A32NX_SPEEDS_*` L:vars
(green dot / S / F / VFE-next) — the identical logic proven on the A380, keyed on the same
variable names since both aircraft share the FBW speed-tape implementation. All checks below
require **"Auto Flaps"** enabled in File → Settings… → First Officer, and the label there should
now read **"Auto-manage flaps (FBW A380 and A32NX)."**

4. **OFF = no movement (default).** Leave "Auto Flaps" disabled. Fly a full departure and
   approach and confirm the FO never moves the flap lever automatically.
5. **Climbout retraction.** Take off with flaps 1 or 2. Accelerating in the climb, confirm:
   passing F speed → lever to 1 (if starting from 2), **"Flaps 1."**; passing S speed → lever to
   0, **"Flaps up."**
6. **Retraction ignores the landing config.** Select CONF 3 on the MCDU PERF APPR page
   (`A32NX_SPEEDS_LANDING_CONF3` = 1) before departure — confirm climbout retraction is
   unaffected.
7. **Approach extension, FULL landing.** With CONF FULL selected (default), fly a decelerating
   approach below 5000 ft AGL. Confirm each step fires as IAS drops through green dot → S → F →
   (with GEAR DOWN) → FULL. Confirm the 3 → FULL step is gear-gated.
8. **Approach extension, CONF 3 landing.** Select CONF 3 — confirm the schedule stops at Flaps 3
   and FULL is never commanded.
9. **VFE-next guard.** Hold IAS just above VFE-next on a descending approach — confirm no
   extension occurs until IAS drops below VFE-next minus the margin.
10. **Go-around, one-step SOP retraction.** From a FULL landing configuration, apply TOGA and
    climb — confirm an immediate step to Flaps 3, then the normal climbout schedule resumes.
    Repeat from CONF 3 (flaps 3) → Flaps 2.
11. **Turbulence immunity.** Brief positive-VS spikes on final must NOT trigger the go-around
    step (requires VS > 500 fpm AND +200 ft AGL regained above the approach's lowest AGL).
12. **Pilot override is respected (monotonic).** After the FO extends a step, manually retract
    one notch — confirm the FO does not re-extend it. Symmetrically on climbout.
13. **No flap movement on the ground.** Confirm no automatic flap movement while on the ground
    after landing (flaps-up stays a Captain item).
14. **Departure level-off never extends flaps.** Level off below 5000 ft AGL with takeoff flaps
    still out and accelerate — confirm no extension fires (extension only arms after a
    descending segment below 5000 ft AGL).
15. **High-final gust does not move the lever.** A brief climb indication on a high approach
    platform (landing flaps out above 3,000 ft AGL) must not fire the go-around step.

---

## Part E — Phase monitor (10,000 ft lights + transition altitude/level)

1. **10,000 ft landing-light band.** Climbing through ~10,300 ft → landing lights OFF and nose
   light → OFF, "Above ten thousand. Landing lights off." announced. Descending through ~9,700 ft
   → landing lights ON and nose light → T.O., "Below ten thousand. Landing lights on."
   announced. Confirm the hysteresis band prevents chatter right at 10,000 ft.
2. **Load SimBrief** with a filed OFP that has a transition altitude and transition level. Confirm
   the announcement reports both.
3. Climb through the transition altitude — confirm **both EFIS baro references switch to STD**
   via the A320's PULL=STD polarity (risk item 1) and "Transition altitude. Altimeters set to
   standard." is announced on both Captain and First Officer sides together.
4. Descend through the transition level — confirm both baro references return to QNH mode
   (PUSH=QNH) and the reminder to dial in local pressure is announced.
5. **No-transition-loaded reminder.** Without loading SimBrief, climb through 18,000 ft (+300 ft
   hysteresis) — confirm the one-shot reminder fires, and re-fires on a later re-climb after
   descending back below 17,000 ft.

---

## Part F — Regression & lifecycle

1. **Aircraft swap disposes/recreates cleanly.** With the A32NX FO window open, switch to another
   aircraft (e.g. Fenix, A380, or PMDG 737) — confirm the A32NX FO window closes/disposes without
   error, and the other FO windows (if open) are unaffected. Switch back and reopen — confirm no
   stale state carries over (e.g. no leftover armed/ticked items from before the swap).
2. **Other FO windows unaffected.** With the PMDG 737/777, Fenix, or A380 First Officer windows
   open, confirm none of their behavior changes as a result of this feature — run one flow and
   one checklist tick on each as a spot-check.
3. **Sim disconnect/reconnect re-wires.** Disconnect MSFSBA from the sim (or restart the sim) with
   the A32NX FO window open, then reconnect — confirm the executor/evaluator re-wire without
   requiring the window to be closed and reopened.

---

## Expected-behavior note (not a bug)

FO-driven switch changes on continuously monitored vars (lights, park brake, seatbelts) may ALSO
produce the normal background monitor announcement — same as on the other FO profiles. This is
legitimate state feedback, not a double-announce bug.

---

## Known limitations (by design)

- **CVR test, recorder ground control, altitude reporting, takeoff-config test, and the gear-lever
  check are Captain reminders** — no verified A320 write key exists for these, so they remain
  `Captain(...)` reminders rather than automated actions. This is intentional, not a gap to fill
  during this pass.
- **No FMC/MCDU programming, ever** (project-wide deliberate decision) — SimBrief load-only.
- **Landing autobrake selection is a Captain item on every aircraft**, including this one.
- **No takeoff-flap automation** — the takeoff flap setting stays a Captain item; auto-flaps only
  handles climbout retraction, approach extension, VFE-next protection, and the SOP go-around
  step.
- **ECAM F/CTL and WHEEL pages have no ECP key on the A320** — the takeoff-config-test ECAM check
  stays a Captain reminder rather than an automated page pulse.
- **The six cockpit-lighting flood/integral knobs are `ActionManual`, not auto-detect** — analog
  potentiometers have no clean "target reached" readback, so the grouped "Panel and integral
  brightness: SET" item never auto-reverts; only the ANN/dome/standby-compass discrete controls
  auto-detect.
- **Cockpit lighting values are sensible defaults, tunable in-sim** — the SOP for cockpit lighting
  is "as required," not a hard specification.
