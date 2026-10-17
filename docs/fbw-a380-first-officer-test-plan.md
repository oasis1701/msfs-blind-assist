# FlyByWire A380 First Officer — In-Sim Test Plan

The First Officer engine's generic L:var layer (`FirstOfficer/Generic/`), already proven by
the Fenix A320 First Officer, now has a **FlyByWire A380 profile** (`FirstOfficer/FBWA380/`).
Unlike Fenix/PMDG, the A380 executor does not own its own write mechanism — it delegates
every control write to `FlyByWireA380Definition.ApplyUIVariable`, the same verified path the
A380 panels use, wrapped in a suppressed-announcer guard so the FO's own step narration stays
the single voice. There is no automated test project (SimConnect/UI app), so the repo owner
verifies the sections below against a live sim (MSFS 2020 or 2024) with the FlyByWire A380
loaded.

Open the window with **Shift+C** in output mode (`]` then Shift+C), or from **File → First Officer**
(read as "FlyByWire A380 First Officer"). The window has two tabs:
**Flows** and **Checklists** — the same shared `FirstOfficerForm<TExec,TState>` UI as the
PMDG 777/737 and Fenix windows.

---

## Part A — Window & gating

1. Load the FlyByWire A380. Confirm **File → First Officer** appears, read as "FlyByWire A380
   First Officer". (First Officer automation settings live in **File → Settings… → First
   Officer**, a tab that is visible for every aircraft — the old "First Officer Settings" menu
   item is retired.)
2. Open the window with Shift+C — confirm it has exactly two tabs, **Flows** and **Checklists**,
   matching the other FO windows' layout. Ctrl+Shift+C still opens the live ECL instead.
3. Switch to each of the other aircraft in turn (A320 FBW, Fenix A320, PMDG 737, PMDG 777, any
   other loaded aircraft e.g. HS787) and confirm the First Officer item is read with THAT
   aircraft's name, never "FlyByWire A380 First Officer", and is absent on the HS787.
4. Switch back to the A380 — confirm the item is read as "FlyByWire A380 First Officer" again.

---

## Part B — Flows (cold-and-dark → securing, all 12 in order)

Start cold-and-dark at a gate, MSFSBA connected, A380 FO window open. Run the 12 flows in
order: **Cockpit Preparation → Before Start → Engine Start → After Start → Taxi → Lineup →
After Takeoff → Approach → Landing → After Landing → Parking → Securing**. For each step, confirm
the corresponding overhead/glareshield/pedestal control physically moves — read back via the
panel controls (screen-reader focus/announce) or the panel display fields, not just the FO's
own narration.

1. **Cockpit Preparation**: gear lever DOWN, spoilers disarmed (write is the `A380X_MSFSBA_
   SPOILERS_ARM` Act key, but confirm the real state — `A32NX_SPOILERS_ARMED` — reads
   disarmed), parking brake ON, engine mode NORM, **engine masters 1-4 OFF** (a `Multi` step —
   confirm all four fuel valves move together), weather radar OFF, **batteries ON** (all four:
   BAT 1, BAT 2, ESS and APU),
   a 5-second standby wait, **ground power 1-4 ON**, **ADIRS 1-3 to NAV**, crew oxygen ON, nav
   + logo lights ON, **seatbelt signs ON — the seat-belt control is now the faithful
   3-position switch (On/Auto/Off, PR #139): confirm the FO drives the SWITCH to the ON
   position (not Auto) and that the actual sign (the stock `CABIN SEATBELTS ALERT SWITCH`
   readout, "Seat Belts Sign") illuminates**, no-smoking AUTO, emergency exit lighting ARM,
   wing anti-ice OFF (the stock switch, `STRUCTURAL DEICE SWITCH` — the A32NX's
   `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION` is a dead mirror on the A380), wing lights OFF,
   **packs 1/2 ON**, crossbleed AUTO, pack flow NORMAL, **hot air 1/2 ON**, **baro reference to
   hectopascals (both sides)** (the EFIS-CP's `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` = 0 since FBW
   #10855; confirm the "Capt Baro Unit" and "F/O Baro Unit" rows on the two EFIS panels
   read hPa), anti-skid ON,
   **EFIS mode ARC (both sides)**, **EFIS range 40 (both sides)**, **flight directors ON** (ONE
   FCU pushbutton since FBW #10855 — the FO presses `A32NX.FCU_FD_PUSH` only when the FD light
   `A32NX_FCU_FD_LIGHT_ON` is out and says "Already set" when it is lit; confirm the PFD status
   box reads flight director 1 and 2 "on"). Confirm the flow waits for the seatbelt SIGN to actually read ON
   (`WaitForField` on the sign light, up to 60 s) before continuing to the final captain
   reminders (IFR clearance, payload, MCDU). If the switch was already in AUTO with the sign
   lit (engines running), the seatbelt step skips as "already set" — also correct. If the sign
   never lights, the wait times out after 60 s ("Timed out waiting for: Waiting for seatbelt signs
   on", then "Skipping: …"), and both "Seatbelt signs: ON" in the Cockpit Preparation group and the
   Cockpit Preparation Checklist's "Seat belts: ON" stay open until the sign lights, while the rest
   of both lists tick.
2. **Before Start**: cockpit door LOCKED, **FCU speed pushed to managed**, **FCU heading pushed
   to managed** (confirm both read "managed" on the FCU Speed/Heading windows or hotkey
   readouts — the same atomic RPN push mechanism validated for the Fenix), the "Set cleared
   altitude on the FCU" reminder, then **FCU altitude pushed** about 2 s later (the flow does not
   wait for the altitude: set it before running the flow, see item 14), APU ON, APU bleed ON,
   **ground power 1-4 OFF**, **fuel pumps ON** (all 20: the 8 feed pumps, tanks 1-4 MAIN + STBY,
   and the 12 transfer pumps), beacon lights ON, parking brake confirmed ON.
3. **Engine Start**: APU bleed ON, engine mode selector → IGN, **engine 1 master → START**,
   **engine 2 master → START**, a **60-second standby wait**, **engine 3 master → START**,
   **engine 4 master → START**, a second **60-second standby wait**. Confirm all four engines
   actually spool (cross-check via the Engines display or EWD) — this aircraft has no N2-gated
   wait like the Fenix/PMDG 737; the waits are fixed dwell periods, so the FO does not block on
   engine state.
4. **After Start**: engine mode → NORM, APU OFF, APU bleed OFF, **nose light selector →
   TAXI** (the nose light is now the faithful 3-position T.O./Taxi/Off combo `NOSE_LIGHT`,
   PR #139 — confirm the selector reads "Taxi" and the nose taxi beam `LIGHT TAXI:1` comes on),
   **cockpit lights → annunciator dim** (the step says "Cockpit lights: annunciator dim"),
   spoilers ARMED (write the Act key, confirm real state `A32NX_SPOILERS_ARMED` reads armed),
   **rudder trim RESET** fires once (a real switch action, not just a reminder), then the
   **rudder-trim check** (read only, up to 20 s): "Already set: Rudder trim: NEUTRAL" when the
   trim already reads neutral, else "Waiting for: Rudder trim: NEUTRAL" until it does; a trim
   still off neutral after 20 s is "Timed out waiting for: …" then "Skipping: …", and the After
   Start Checklist's "Rudder trim: NEUTRAL" stays open. **In-sim check:** set a few degrees of
   rudder trim before the flow, then time how long the trim takes to centre after the reset
   (report the degrees and the seconds): the 20 s is a judgement, not a measurement.
5. **Taxi**: **autobrake RTO** — confirm this ARMS `A32NX_AUTOBRAKES_RTO_ARMED` (the write is a
   momentary press of `A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED`, which self-resets after ~1.5 s;
   the flow does not wait on it, so just confirm the armed state lands), engine mode NORM,
   weather radar ON, predictive windshear ON, the flaps reminder, ECAM page → flight controls,
   then the **takeoff-memo check** (read only: seat belts lit, ground spoilers armed, flap lever
   1-3; "Already set" when all three read right, else "Waiting for…" up to 5 s, then "Timed out
   waiting for…" and "Skipping: Seat belts, ground spoilers and flaps for takeoff"), and the
   **takeoff config test last**.
6. **Lineup**: the TCAS reminder (TA/RA, set on the MFD SURV page), the **packs check** (read only,
   "Packs 1 and 2: ON"; Cockpit Preparation switched them on, and Line-up never writes them),
   strobe lights ON, **landing and nose lights ON** — TWO writes: wing landing lights ON
   (`LIGHT LANDING:2`) **and the nose selector → T.O.** (which lights the nose takeoff + taxi
   beams), runway turn-off lights ON, then the cabin call. Confirm the lights actually illuminate.
7. **After Takeoff**: spoilers DISARM, autobrake disarm, nose light selector → TAXI, runway
   turn-off lights OFF.
8. **Approach**: seatbelt signs ON, **EFIS mode → ILS (both sides)**, then the **cabin call**
   (CALLS ALL chime; new 2026-10-09: confirm the chime sounds and the Approach group's "Notify
   the cabin crew for landing (call all)" ticks). The landing autobrake stays a Captain reminder.
9. **Landing**: the missed-approach altitude reminder, spoilers ARMED (via the Act key — confirm
   real state armed), then the **landing-memo check** (read only: seat belts lit and flaps at the
   landing setting, FULL, or 3 with CONF 3 selected) and, **last**, the **gear-down check** (read
   only, up to 20 s).
10. **After Landing**: weather radar OFF, predictive windshear OFF, engine mode NORM, APU ON,
    **engine anti-ice 1-4 OFF** (a 4-way `Multi`), wing anti-ice OFF, spoilers OFF, landing
    lights OFF, strobe lights OFF, nose light selector → TAXI.
11. **Parking**: parking brake ON, **APU generators 1/2 ON**, **engine masters 1-4 OFF**, then
    confirm the flow genuinely **waits** for `FO_ENGINES_OFF` (all four masters off, up to 120 s)
    before continuing — a 5-second standby wait, beacon OFF, wing lights OFF, nose light selector
    → OFF, engine anti-ice OFF, wing anti-ice OFF, APU bleed ON, **fuel pumps OFF** (all 20),
    **seatbelt switch → OFF** (position 2 — confirm the switch reads Off, not On/Auto), the
    transponder and TCAS standby reminders (MFD SURV page), the cockpit door UNLOCKED, then the
    flow **waits** for the seatbelt SIGN to actually go OFF (up to 60 s). A wait that times out is
    skipped aloud ("Skipping: Waiting for engines off"), and the lines it checks stay open until the
    aircraft agrees: a master that never reads off leaves "Engine masters 1 to 4: OFF" and the
    Parking Checklist's "Engines: OFF" open; a sign that stays lit leaves "Seatbelt signs: OFF" open.
12. **Securing**: crew oxygen OFF, the EFB reminder, IRS 1-3 OFF, emergency exit lighting OFF, no
    smoking OFF, nav and logo lights OFF, APU bleed OFF, APU master OFF, **"Waiting for: APU flap
    to close"** (up to 150 s; FlyByWire's order, owner 2026-10-14, so external power is no longer
    switched off before the APU master as in the shipped checklist text), external power OFF, and
    **batteries OFF last**. **In-sim check:** run Securing with the APU running: hear the flap wait
    after "APU master: OFF", then external power and the batteries OFF. This also confirms
    `A32NX_APU_FLAP_OPEN_PERCENTAGE` reads on the A380: if it never does, the wait times out
    ("Timed out waiting for: APU flap to close") and the flow carries on.
13. **No double-announce.** Throughout all 12 flows, confirm each step is announced **once**
    by the FO — not twice. The executor wraps every write in a suppressed-announcer guard
    around `ApplyUIVariable`, so the def's own internal `Announce()` calls (e.g. "Rudder trim
    reset") are dropped and only the FO's step narration speaks. See the note at the end of
    this document about the ONE expected exception (continuously-monitored vars).
14. **Captain reminders are spoken, and the flow carries on.** Every `Captain(...)` step (wipers
    off, flaps confirm up, thrust levers idle, altimeters set QNH, the transponder, TCAS and
    altitude-reporting reminders that name the MFD SURV page, IFR clearance, payload, MCDU
    programming, flaps for takeoff, the landing autobrake, the missed-approach altitude, etc.) is
    spoken as "Captain action required: …" and is NOT a pause: `FlowManager` announces it and
    moves on after its usual 2 s step pause, without waiting for acknowledgement — confirm each
    reminder is heard in full. So in Before Start the FCU altitude is pushed about 2 s after "Set
    cleared altitude on the FCU": set the altitude before running the flow, or pause the flow when
    you hear the reminder. (The five ECAM page changes, the fire test, recorder ground control and
    the takeoff config test are writes, not Captain reminders; the fire test has its own section
    below, "Preflight fire test".)
15. **Already-in-target-state steps announce as skipped.** Re-run a flow (or a step) whose
    switch is already correct (e.g. run Cockpit Preparation twice in a row) — confirm the
    matching step announces a quiet skip ("Already set" or equivalent) instead of re-firing the
    action. This matters especially for `TX_AUTOBRAKE` (RTO arm) — re-running Taxi while RTO is
    already armed must NOT re-press the momentary switch, which would disarm it.

---

## Part C — Checklists tab

Open the Checklists tab. Each flow's action group (Cockpit Preparation, Before Start, Engine Start,
After Start, Taxi, Lineup, After Takeoff, Approach, Landing, After Landing, Parking, Securing) sits
in flying order beside the nine **read-backs**, the A380's own ECL normal checklists in the ECL's
words (FOA-9): Cockpit Preparation Checklist, Before Start Checklist, After Start Checklist, Taxi
Checklist (with the takeoff memo), Line-up Checklist, Approach Checklist, Landing Checklist (the
landing memo), Parking Checklist and Securing the Aircraft Checklist. There is no Before Takeoff or
After Landing checklist. After its flow, each of the nine is already ticked and latched, so to hear
a line's value, tick it before running the flow, or untick and re-tick it after: the untick ("Fuel
quantity: SET, 12,400 kilograms: unchecked") re-opens the list.

1. **STATE items auto-tick as switches reach position.** With an item unticked (e.g. Cockpit
   Preparation → "Batteries: ON"), change the underlying switch directly in the cockpit
   (mouse/VR/panel), independent of the FO — confirm the checklist item ticks itself within
   about 1-2 s (the state evaluator polls the SimConnect L:var cache; batch-covered continuous
   vars update faster, OnRequest vars poll roughly once a second).
2. **Manual tick fires the switch.** Tick an unticked STATE item manually (e.g. "APU: ON" in
   Before Start with the APU actually off) — confirm the real switch moves.
3. **Untick/retick is idempotent.** Untick a ticked item (pure UI action, nothing reverts on
   the aircraft), then retick it — confirm the action either fires again or is a no-op
   ("Already set") if the switch is still correct.
4. **TX_AUTOBRAKE special case.** In the Taxi group, "Autobrake: RTO" detects on the **latched**
   `A32NX_AUTOBRAKES_RTO_ARMED` state (NOT the momentary `_IS_PRESSED` press var, which
   self-resets). Tick it with RTO disarmed — confirm it arms. **Untick and retick while RTO is
   already armed** — confirm the guarded action does **NOT** re-press the momentary switch
   (a second press would disarm it); the tick should recognize the armed state and no-op.
5. **Read-back items never fire a switch.** Untick and re-tick a self-ticking read-back line (after
   its flow it is already ticked), or tick one before running its flow — e.g. "Beacon: ON"
   (Before Start Checklist), "Radar: ON" (Taxi Checklist), "Ground spoilers: ARM" (Landing
   Checklist — it reads the real `A32NX_SPOILERS_ARMED`, not the write-only Act key), "Engines: OFF",
   "Wing lights: OFF" or "Fuel pumps: OFF" (Parking Checklist). Confirm **no switch moves**: the line
   only ticks itself once the real state reaches the stated condition.
6. **Blank lines speak what the First Officer reads.** Before running each list's flow (after it,
   untick and re-tick the line), tick "Fuel quantity: SET" and "Baro reference: SET" (Cockpit
   Preparation Checklist), "Parking brake: SET" and "T.O speeds and thrust: SET" (Before Start
   Checklist), "Anti-ice: SET" (After Start Checklist), "Flaps setting: SET" (Taxi Checklist) and
   "Minimum: SET" / "Autobrake: SET" (Approach Checklist): each speaks its value after the label
   ("Flaps setting: SET, CONF 2: checked"; the parking brake says "on" or "off") and shows it on the
   status line; the tree text never changes.
7. **WATCH ITEM — anti-skid tick stability.** Cockpit Preparation → "Anti-skid: ON" detects on
   the stock `ANTISKID BRAKES ACTIVE` SimVar, which the A380 definition documents as reading
   UNRELIABLY over the data-def path (the same batch has returned both 1 and 0). Leave the
   Checklists tab open on Cockpit Preparation for a couple of minutes with anti-skid steady ON
   and listen/watch for the item churning ticked↔unticked. The write side is safe either way
   (the definition drives the toggle off its own tracked state, so a flaky read can never
   double-toggle the switch). **If the item visibly/audibly flaps, report it — the fix is to
   demote this one item to a manual action (no auto-detect).**
8. **Engine anti-ice (After Landing / Parking → "Engine anti-ice: OFF").** These detect on the
   stock `ENG ANTI ICE:n` readouts (the write keys have no readable state). Turn engine anti-ice
   ON by hand, confirm the items UN-tick (RevertToState); run the flow or tick the item and
   confirm they re-tick as all four switch off.
9. **In-sim checks for the 2026-10-09 rebuild (prioritised):**
   1. **Taxi:** run the Taxi flow, then read the Taxi checklist. With the memo set, the memo lines
      tick. With the flaps up you hear "Timed out waiting for: …" and "Skipping: Seat belts,
      ground spoilers and flaps for takeoff": "Flaps: T.O" stays open until the lever is set and
      "Flaps setting" until you tick it; lines that already read right tick with the rest.
   2. **Landing:** run the Landing flow, then read the Landing checklist. The gear check is the
      flow's last step, followed only by "Landing flow complete"; "Flaps: LDG" ticks at FULL, or
      at 3 with CONF 3 set. With the flaps short of that you hear "Skipping: Seat belts and
      landing flaps": "Flaps: LDG" stays open until the flaps are set; lines that already read
      right tick with the rest.
   3. **Spoken values:** tick a few blank lines and listen, before running their flow, or untick
      and re-tick them after it: Fuel quantity, Baro reference, T.O speeds and thrust.
   4. **Surveillance (MFD SURV page):** listen to how the reminders sound, e.g. "Captain action
      required: TCAS mode: TA/RA (MFD SURV page)". Then, once, check that the page's TCAS
      selection works in your build; no live test of it is on record (FOA-9). With the
      transponder on AUTO, select TCAS TA/RA on the MFD SURV CONTROLS page (MCDU window, "Go to
      MFD page" → SURV: CONTROLS) and listen for MSFSBA's TCAS mode announcement ("traffic and
      resolution advisories"). If the mode stays at standby, report it: the TCAS reminders would
      then name a selection your build ignores.

---

## Part D — Auto managers

1. In **File → Settings… → First Officer**, enable **Auto Gear Up**, **Auto Gear Down**, and
   **Auto AP** for the A380.
2. Fly a takeoff and confirm:
   - Positive rate of climb above ~50 ft AGL with gear down → gear auto-raises, **"Positive
     rate. Gear up."** announced.
   - Climbing through the **configured AP altitude** (Settings → First Officer numeric
     field, default **350 ft AGL**) → **AP 1 engages**. **New 2026-10-09 (FO-21): the
     auto-autopilot used to press a stock event FlyByWire ignores, so this is the check to
     run.** Take off with "Engage autopilot" on and expect the aircraft's own **"Autopilot 1
     on"** call, then **"350 feet. Autopilot engaged."** (the configured number), **both heard
     in full** (the second must not cut the first off), with AP 1 flying and no second press.
3. On approach/descent, confirm gear auto-lowers descending through roughly 2000 ft AGL (and
   above 100 ft AGL, not already down) — **"Two thousand feet. Gear down."** announced.
### Auto-flaps

`FbwA380FOAutoManager.CheckFlaps` drives the flap lever from the published speed-tape L:vars
(green dot / S / F / VFE-next). It moves **at most one step per tick** and enforces a **4-second
dwell** (`FlapDwellSeconds`) between automatic lever movements. Nothing schedules while indicated
airspeed is below **30 kt** (`MinActionIasKts`) or while the flaps-handle index reads unknown. All
checks below require **"Auto Flaps"** enabled in File → Settings… → First Officer.

4. **OFF = no movement (default).** Leave "Auto Flaps" **disabled**. Fly a full departure and
   approach and confirm the FO **never** moves the flap lever automatically — the flaps schedule
   is inert unless the setting is on.
5. **Climbout retraction (flaps-2 takeoff).** Take off with flaps 2. Accelerating in the climb,
   confirm: passing **F speed** (`A32NX_SPEEDS_F`) → lever to 1, **"Flaps 1."** announced; passing
   **S speed** (`A32NX_SPEEDS_S`) → lever to 0, **"Flaps up."** announced. Repeat with a **flaps-3
   takeoff** — same rule, same two announcements (retraction is deliberately independent of the
   takeoff config: at F select 1, at S clean).
6. **Retraction ignores the landing config.** Before departure, select **CONF 3** on the MFD PERF
   APPR page (`A380X_FM_LANDING_CONF3` = 1), then take off with flaps 2/3. Confirm the climbout
   retraction to 1 then up is **unaffected** — CONF 3 only caps *extension*, never retraction.
7. **Approach extension, FULL landing.** With CONF **FULL** selected (default; `A380X_FM_LANDING_CONF3` = 0),
   fly a decelerating approach, not climbing, below **5000 ft AGL**. Confirm each step fires as IAS
   drops through the matching speed: below **green dot** (`A32NX_SPEEDS_GD`) → **"Speed checked.
   Flaps 1."**; below **S** → **"Speed checked. Flaps 2."**; below **F** → **"Speed checked. Flaps
   3."**; then with **GEAR DOWN** (`A32NX_GEAR_HANDLE_POSITION` down) → **"Speed checked. Flaps
   full."** Confirm the **3 → FULL** step is **gear-gated**: with the gear still up, FULL is held
   until the gear comes down.
8. **Approach extension, CONF 3 landing.** Select **CONF 3** on the MFD PERF APPR page. Fly the same
   approach and confirm the schedule stops at **"Speed checked. Flaps 3."** — the **2 → 3** step
   still requires **gear down**, and **FULL is never commanded** (the schedule caps at 3 for a CONF 3
   landing).
9. **VFE-next guard.** On a descending approach below 5000 ft AGL, hold IAS **just above**
   VFE-next (`A32NX_SPEEDS_VFEN`). Confirm **no extension** occurs until IAS drops below
   **VFE-next − 5 kt** (`VfeNextMarginKts`) — the FO will not select the next flap setting while
   still fast for it (or while VFE-next reads unknown).
10. **Go-around, one-step SOP retraction.** From a **FULL** landing configuration, apply TOGA and
    establish a climb. Confirm an **immediate, speed-independent** step: **"Go-around. Flaps 3."**,
    then the normal F/S retraction schedule cleans up (Flaps 1 at F, Flaps up at S). Repeat from a
    **CONF 3** approach (flaps 3) → **"Go-around. Flaps 2."** The go-around step is one lever move
    (FULL → 3, or 3 → 2), then the climbout schedule takes over. Also confirm the step is **never
    lost when the go-around comes within ~4 s of the last flap movement** (balked landing right
    after "Flaps full") — the FO stays in the approach context and retries the step on the next
    tick rather than skipping it.
11. **Turbulence immunity.** On a descending final in bumps, brief positive-VS spikes must **NOT**
    trigger the go-around step. The detector requires a real climb-out signature: **VS > 500 fpm**
    (`GaClimbFpm`) **and** at least **+200 ft AGL regained above the lowest AGL reached on the
    approach** (`GaAglGainFt`, tracked as `_minAglInApproach`). Confirm a gust that momentarily
    shows +VS without gaining 200 ft does not fire "Go-around. Flaps …".
12. **Pilot override is respected (monotonic).** During the approach, after the FO has extended a
    step, **manually pull the flap lever back one notch**. Confirm the FO does **not** re-extend
    that step (extension is monotonic per approach — the target must exceed the last auto command).
    Symmetrically, on climbout, re-extend a notch by hand and confirm the FO does not re-fight it.
13. **No flap movement on the ground.** Confirm the FO makes **no automatic flap movement while on
    the ground** — after landing, flaps-up stays a **Captain** item (the schedule resets its state
    at touchdown and does not re-arm until airborne again).
14. **Departure level-off never extends flaps.** Take off with flaps 2 or 3 and level off **below
    5000 ft AGL with the takeoff flaps still out** (e.g. ATC "maintain 3,000") and accelerate.
    Confirm the FO does **NOT** extend flaps (extension only arms after a **descending** segment
    below 5000 ft AGL — a departure level-off never opens the approach context) and that normal
    retraction proceeds ("Flaps 1." at F speed, "Flaps up." at S speed) during the level segment.
15. **High-final gust does not move the lever.** On a high approach platform (landing flaps out
    above 3,000 ft AGL), a brief climb indication must **not** fire "Go-around. Flaps …" — the
    high-altitude safeguard only disarms the schedule internally; the SOP step itself requires the
    full climb-out signature from check 11.

---

## Part E — Phase monitor (10,000 ft lights + transition altitude/level)

1. **10,000 ft landing-light band.** Climbing through **10,300 ft** → wing landing lights
   OFF and the **nose light selector → OFF**, **"Above ten thousand. Landing lights off."**
   announced. Descending through **9,700 ft** → wing landing lights ON and the **nose light
   selector → T.O.** (nose takeoff + taxi beams lit), **"Below ten thousand. Landing lights
   on."** announced. Confirm the 300 ft hysteresis band means a crossing right at 10,000 ft
   doesn't chatter back and forth.
2. **Load SimBrief** (Flows tab or the equivalent Checklists-tab button) with a filed OFP that
   has a transition altitude and transition level. Confirm the announcement reports both.
3. Fly (or simulate via teleport/time-compression) a climb through the transition altitude —
   confirm **both EFIS baro references switch to STD** and **"Transition altitude. Altimeters
   set to standard."** is announced. (The executor's `SetBaroStd` sets BOTH sides via the def's
   verified baro branch — confirm via the altimeter hotkey/readout on both Captain and First
   Officer sides that they land together, not desynced.)
4. Descend through the transition level — confirm both baro references return to **QNH mode**
   and **"Transition level. Altimeters set to QNH mode. Set local pressure now."** is
   announced (a reminder to dial in the actual QNH — the FO does not know the real-world QNH
   value).
5. **No-transition-loaded reminder.** Without loading SimBrief (no transition altitude), climb
   through 18,000 ft (+300 ft hysteresis) — confirm a one-shot reminder: **"Passing one eight
   thousand. No transition altitude loaded — set standard altimeters as required. Load
   SimBrief in the First Officer window for automatic altimeter changes."** Descend back below
   17,000 ft and re-climb through 18,300 ft — confirm the reminder fires again (the latch resets
   on the way back down).

---

## Part F — Regression & lifecycle

1. **Aircraft swap disposes/recreates cleanly.** With the A380 FO window open, switch to
   another aircraft (e.g. Fenix or PMDG 737) — confirm the A380 FO window closes/disposes
   without error, and the Fenix/PMDG FO windows (if open) are unaffected. Switch back to the
   A380 and reopen its FO window — confirm it opens fresh with both tabs intact and no stale
   state carried over (e.g. no leftover "armed"/ticked items from before the swap).
2. **Other FO windows unaffected.** With the PMDG 737/777 or Fenix First Officer windows open,
   confirm none of their behavior changes as a result of this feature — run one flow and one
   checklist tick on each as a spot-check (mirrors the Fenix plan's PMDG regression section).
3. **Sim disconnect/reconnect re-wires.** Disconnect MSFSBA from the sim (or restart the sim)
   with the A380 FO window open, then reconnect — confirm `OnSimConnectChanged()` re-wires the
   executor/evaluator without requiring the window to be closed and reopened, and that flows/
   checklist ticks work again once reconnected.

---

## Expected-behavior note (not a bug)

FO-driven switch changes on continuously-monitored vars (lights, park brake, seatbelts) may
ALSO produce the normal background monitor announcement — same as when the PMDG FO moves
switches. This is legitimate state feedback, not a double-announce bug.

---

## Known limitations (by design)

- **Transponder and TCAS settings, the takeoff flap setting and the landing autobrake are
  Captain items by design** — every one of these appears only as a `Captain(...)` reminder in
  the flows (and a `Reminder` in the checklists), never actuated by the FO. The transponder
  mode, altitude reporting and TCAS mode are set on the MFD SURV CONTROLS page, which you can
  click in MSFSBA's MCDU window ("Go to MFD page" → SURV: CONTROLS); MSFSBA has no variable or
  event write for them, so their reminders name the page, "(MFD SURV page)" (FOA-9). The flaps
  have real settable controls elsewhere in MSFSBA's panels, but the FO does not set the takeoff
  setting: it depends on SimBrief/performance data the FO does not compute (the opt-in
  auto-flaps schedule below is separate).
- **FCU managed-push checklist items are `ActionManual`** ("FCU speed: managed", "FCU heading:
  managed", "FCU altitude: pushed", "Rudder trim: RESET") — these have no readable resting
  state to auto-detect against (a push/pull knob action, not a persistent switch position), so
  they can only be manually ticked (which fires the action) — they never auto-tick from state.
- **Auto-flaps is speed-tape driven, not V-speed driven** — `FbwA380FOAutoManager.CheckFlaps`
  schedules the flap lever off the published `A32NX_SPEEDS_*` L:vars (green dot / S / F / VFE-next)
  and the MFD PERF APPR landing config (`A380X_FM_LANDING_CONF3`), not off a V1/VR/V2/VAPP
  source. It only runs when "Auto Flaps" is enabled (opt-in, default off). The **takeoff flap
  setting itself is never automated** (Captain item); the manager only handles climbout retraction,
  approach extension (gear-gated to FULL / capped at 3 for CONF 3), the Airbus-SOP go-around
  one-step retraction, and VFE-next overspeed protection. See Part D checks 4–13.
- **No engine-N2 gating in Engine Start** — unlike the Fenix and PMDG 737 First Officers (which
  wait for N2 to cross a running threshold before proceeding), the A380 Engine Start flow uses
  fixed 60-second dwell periods between engine pairs. Cross-check actual engine spool via the
  Engines display or EWD rather than relying on the FO to detect a stall/hung start.
- **Spoiler arm/disarm writes a write-only Act key (`A380X_MSFSBA_SPOILERS_ARM`), but every
  state read (flow Skip conditions and checklist auto-detect) uses the real
  `A32NX_SPOILERS_ARMED` var** — never expect the Act key itself to reflect state; if a
  spoiler step or checklist item seems stuck, check `A32NX_SPOILERS_ARMED` directly.

## APU-start gating (2026-07-06 pass)

The FO now actually starts the A380 APU: master ON → 3 s → START pushbutton → wait for
AVAIL (Stop on timeout) → release START → APU bleed → external power off. Previously only
the master was set and external power was dropped immediately (APU never started).

1. Before Start happy path: cold & dark, ext power on (EFB). Run Before Start. Expect:
   "APU: ON" → "Waiting 3 seconds…" → "APU start" → "Waiting for: Waiting for APU
   available" (~1-2 min) → "APU start button: released" OR "Already set: APU start
   button: released" → "APU bleed: ON" → "Ground power: OFF" → captain EFB-disconnect
   reminder. PASS: ECAM shows APU AVAIL before the EXT PWR pushbuttons go off; buses
   never drop to batteries.
   NOT a bug: the background monitor may also speak "APU Start: On" / "APU Start: Off"
   ~1 s after the FO's own step callouts — `A32NX_OVHD_APU_START_PB_IS_ON` is a
   continuously-monitored var like the master/bleed, mutable via Ctrl+M if disliked.
2. START PB auto-clear observation: at the "released" step, note which announcement fired.
   "Already set" = FBW cleared the latched PB itself; the explicit release = it did not.
   Report either way (informs whether the defensive clears stay).
3. Re-run idempotence: run Before Start again with the APU available — the whole APU block
   announces "Already set"/skips instantly; no re-start attempt.
4. Checklist path: reset to cold & dark + ext power. Tick "APU: ON" in the BEFORE_START
   checklist group instead of running the flow. PASS: the APU actually starts (master +
   START press) and AVAIL appears on ECAM ~1-2 min later. Then run the After Start flow
   and confirm "APU start button: released" precedes "APU: OFF".
5. After Landing: run the flow after vacating. Same block with a Skip-policy wait — a
   timeout announces "Skipping: …" and the remaining cleanup (anti-ice off etc.) still
   runs.
6. Timeout/abort: pull the APU FIRE pb (or run with empty feed tanks) and run Before
   Start. Expect the flow to stop after 3 min with external power untouched.

## Preflight fire test (2026-07-12)

`CP_FIRETEST` is now a real held test (was a captain reminder), moved up to right after
crew oxygen (Fenix order): the FO writes `A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED` 1 → 3 s →
0 through the def's fire-test branch; the release write also pulses the MASTERAWARN
acknowledge so the CRC cancels.

1. Run Cockpit Preparation with the aircraft powered: after "Crew oxygen: ON" the FO
   announces "Fire test — listen for the fire warning"; master warning + continuous
   repetitive chime sound ~3 s, then STOP (the auto-acknowledge must cancel the CRC —
   a chime that keeps sounding is a fail).
2. The COCKPIT_PREP checklist "Fire test" box ticks from the flow; ticking it by hand
   re-runs the test and the box stays ticked.
3. The Continuous+IsAnnounced monitor may speak "Fire Test: On/Off" around the test —
   note whether this is acceptably chatty; if not, a follow-up adds echo suppression.
4. If 3 s proves too short for the full A380 test sequence, tune
   `FbwA380ActionExecutor.FireTestHoldMs`.
