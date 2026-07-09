# FlyByWire A380 First Officer — In-Sim Test Plan

The First Officer engine's generic L:var layer (`FirstOfficer/Generic/`), already proven by
the Fenix A320 First Officer, now has a **FlyByWire A380 profile** (`FirstOfficer/FBWA380/`).
Unlike Fenix/PMDG, the A380 executor does not own its own write mechanism — it delegates
every control write to `FlyByWireA380Definition.ApplyUIVariable`, the same verified path the
A380 panels use, wrapped in a suppressed-announcer guard so the FO's own step narration stays
the single voice. There is no automated test project (SimConnect/UI app), so the repo owner
verifies the sections below against a live sim (MSFS 2020 or 2024) with the FlyByWire A380
loaded.

Open the window from **Tools → "FlyByWire A380 First Officer"**. The window has two tabs:
**Flows** and **Checklists** — the same shared `FirstOfficerForm<TExec,TState>` UI as the
PMDG 777/737 and Fenix windows.

---

## Part A — Window & gating

1. Load the FlyByWire A380. Confirm **Tools → "FlyByWire A380 First Officer"** appears.
   (First Officer automation settings live in **File → Settings… → First Officer**, a tab
   that is visible for every aircraft — the old "First Officer Settings" menu item is retired.)
2. Open the window — confirm it has exactly two tabs, **Flows** and **Checklists**, matching
   the other FO windows' layout.
3. Switch to each of the other aircraft in turn (A320 FBW, Fenix A320, PMDG 737, PMDG 777, any
   other loaded aircraft e.g. HS787) and confirm **"FlyByWire A380 First Officer" is NOT
   visible** on any of them — only its own aircraft-specific FO item(s) show (Fenix shows only
   "Fenix A320 First Officer"; PMDG 737/777 show only their own item).
4. Switch back to the A380 — confirm the menu item reappears.

---

## Part B — Flows (cold-and-dark → shutdown, all 12 in order)

Start cold-and-dark at a gate, MSFSBA connected, A380 FO window open. Run the 12 flows in
order: **Cockpit Preparation → Before Start → Engine Start → After Start → Taxi → Lineup →
After Takeoff → Climb → Approach → Landing → After Landing → Parking**. For each step, confirm
the corresponding overhead/glareshield/pedestal control physically moves — read back via the
panel controls (screen-reader focus/announce) or the panel display fields, not just the FO's
own narration.

1. **Cockpit Preparation**: gear lever DOWN, spoilers disarmed (write is the `A380X_MSFSBA_
   SPOILERS_ARM` Act key, but confirm the real state — `A32NX_SPOILERS_ARMED` — reads
   disarmed), parking brake ON, engine mode NORM, **engine masters 1-4 OFF** (a `Multi` step —
   confirm all four fuel valves move together), weather radar OFF, **batteries 1 and 2 ON**,
   a 5-second standby wait, **ground power 1-4 ON**, **ADIRS 1-3 to NAV**, crew oxygen ON, nav
   + logo lights ON, **seatbelt signs ON — the seat-belt control is now the faithful
   3-position switch (On/Auto/Off, PR #139): confirm the FO drives the SWITCH to the ON
   position (not Auto) and that the actual sign (the stock `CABIN SEATBELTS ALERT SWITCH`
   readout, "Seat Belts Sign") illuminates**, no-smoking AUTO, emergency exit lighting ARM,
   wing anti-ice OFF (now backed by the real cockpit button var
   `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION`), wing lights OFF, **packs 1/2 ON**, crossbleed
   AUTO, pack flow NORMAL, **hot air 1/2 ON**, **baro reference to hectopascals (both
   sides)**, anti-skid ON, **EFIS mode ARC (both sides)**, **EFIS range 40 (both sides)**,
   **flight directors 1/2 ON** (now actuated via `K:TOGGLE_FLIGHT_DIRECTOR` per side — the
   old `A32NX_FCU_EFIS_L/R_FD_ACTIVE` L:var is dead on the A380X; confirm both FDs actually
   engage on the PFDs). Confirm the flow waits for the seatbelt SIGN to actually read ON
   (`WaitForField` on the sign light, up to 60 s) before continuing to the final captain
   reminders (IFR clearance, payload, MCDU). If the switch was already in AUTO with the sign
   lit (engines running), the seatbelt step skips as "already set" — also correct.
2. **Before Start**: cockpit door LOCKED, **FCU speed pushed to managed**, **FCU heading pushed
   to managed** (confirm both read "managed" on the FCU Speed/Heading windows or hotkey
   readouts — the same atomic RPN push mechanism validated for the Fenix), **FCU altitude
   pushed** (after the captain sets a cleared altitude), APU ON, APU bleed ON, **ground power
   1-4 OFF**, **fuel pumps ON** (all 8: feed tanks 1-4, MAIN + STBY each), beacon lights ON,
   parking brake confirmed ON.
3. **Engine Start**: APU bleed ON, engine mode selector → IGN, **engine 1 master → START**,
   **engine 2 master → START**, a **60-second standby wait**, **engine 3 master → START**,
   **engine 4 master → START**, a second **60-second standby wait**. Confirm all four engines
   actually spool (cross-check via the Engines display or EWD) — this aircraft has no N2-gated
   wait like the Fenix/PMDG 737; the waits are fixed dwell periods, so the FO does not block on
   engine state.
4. **After Start**: engine mode → NORM, APU OFF, APU bleed OFF, **nose light selector →
   TAXI** (the nose light is now the faithful 3-position T.O./Taxi/Off combo `NOSE_LIGHT`,
   PR #139 — confirm the selector reads "Taxi" and the nose taxi beam `LIGHT TAXI:1` comes on),
   spoilers ARMED (write the Act key, confirm real state `A32NX_SPOILERS_ARMED` reads armed),
   **rudder trim RESET** fires once (a real switch action, not just a reminder).
5. **Taxi**: **autobrake MAX** — confirm this ARMS `A32NX_AUTOBRAKES_RTO_ARMED` (the write is a
   momentary press of `A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED`, which self-resets after ~1.5 s;
   the flow does not wait on it, so just confirm the armed state lands), engine mode NORM,
   weather radar ON, predictive windshear ON.
6. **Lineup**: strobe lights ON, **landing and nose lights ON** — this is now TWO writes:
   wing landing lights ON (`LIGHT LANDING:2`) **and the nose selector → T.O.** (which lights
   the nose takeoff + taxi beams). Confirm both actually illuminate.
7. **After Takeoff**: spoilers DISARM, nose light selector → TAXI.
8. **Climb**: autobrake disarm, seatbelt signs ON (switch to ON; sign confirmed lit).
9. **Approach**: seatbelt signs ON, **EFIS mode → ILS (both sides)**.
10. **Landing**: spoilers ARMED (again, via the Act key — confirm real state armed).
11. **After Landing**: weather radar OFF, predictive windshear OFF, engine mode NORM, APU ON,
    **engine anti-ice 1-4 OFF** (a 4-way `Multi`), wing anti-ice OFF, spoilers OFF, landing
    lights OFF, strobe lights OFF, nose light selector → TAXI.
12. **Parking**: parking brake ON, **APU generators 1/2 ON**, **engine masters 1-4 OFF**, then
    confirm the flow genuinely **waits** for `FO_ENGINES_OFF` (both engines' N2 below the
    running threshold, up to 120 s) before continuing — a 5-second standby wait, beacon OFF,
    wing lights OFF, nose light selector → OFF, engine anti-ice OFF, wing anti-ice OFF,
    APU bleed ON, **fuel pumps OFF** (all 8), **seatbelt switch → OFF** (position 2 — confirm
    the switch reads Off, not On/Auto), then the flow **waits** for the seatbelt SIGN to
    actually go OFF (up to 60 s) before the cockpit door is set UNLOCKED as the final step.
13. **No double-announce.** Throughout all 12 flows, confirm each step is announced **once**
    by the FO — not twice. The executor wraps every write in a suppressed-announcer guard
    around `ApplyUIVariable`, so the def's own internal `Announce()` calls (e.g. "Rudder trim
    reset") are dropped and only the FO's step narration speaks. See the note at the end of
    this document about the ONE expected exception (continuously-monitored vars).
14. **Captain reminders announce and wait.** Every `Captain(...)` step (wipers off, flaps
    confirm up, thrust-lever checks, ECAM page changes, fire test, altimeters set QNH, TCAS/
    transponder settings, exterior walkaround, IFR clearance, MCDU programming, etc.) is spoken
    as a reminder and the flow **pauses for acknowledgement** — confirm it does not silently
    auto-complete or skip ahead.
15. **Already-in-target-state steps announce as skipped.** Re-run a flow (or a step) whose
    switch is already correct (e.g. run Cockpit Preparation twice in a row) — confirm the
    matching step announces a quiet skip ("Already set" or equivalent) instead of re-firing the
    action. This matters especially for `TX_AUTOBRAKE` (RTO arm) — re-running Taxi while RTO is
    already armed must NOT re-press the momentary switch, which would disarm it.

---

## Part C — Checklists tab

Open the Checklists tab. There are **12 STATE/ACTION groups** (Cockpit Preparation, Before
Start, Engine Start, After Start, Taxi, Lineup, After Takeoff, Climb, Approach, Landing, After
Landing, Parking) mirroring the 12 flows above, plus **10 readback (`*_CL`) groups** (Cockpit
Preparation Checklist, Before Start Checklist, After Start Checklist, Taxi Checklist, Lineup
Checklist, Before Takeoff Checklist, Approach Checklist, Landing Checklist, After Landing
Checklist, Parking Checklist).

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
4. **TX_AUTOBRAKE special case.** In the Taxi group, "Autobrake: MAX" detects on the **latched**
   `A32NX_AUTOBRAKES_RTO_ARMED` state (NOT the momentary `_IS_PRESSED` press var, which
   self-resets). Tick it with RTO disarmed — confirm it arms. **Untick and retick while RTO is
   already armed** — confirm the guarded action does **NOT** re-press the momentary switch
   (a second press would disarm it); the tick should recognize the armed state and no-op.
5. **Readback (`*_CL`) items never fire a switch.** Open any `*_CL` group (e.g. Before Start
   Checklist, Landing Checklist, Parking Checklist) and tick an auto-detectable item — e.g.
   "Parking brake: ON" (Before Start Checklist / Parking Checklist), "Beacon: ON" (Before Start
   Checklist), "Weather radar: ON" (Taxi Checklist), "Spoilers: ARMED" (Landing Checklist — this
   one reads the real `A32NX_SPOILERS_ARMED` state directly, not the write-only Act key),
   "Engines: OFF" (Parking Checklist, via `FO_ENGINES_OFF`), "Wing lights: OFF" / "Fuel pumps:
   OFF" (Parking Checklist). Confirm **no switch moves** for any of these — the item only ticks
   itself once the real state independently reaches the stated condition (set it via the
   matching STATE-group flow, or by hand in the cockpit).
6. Spot-check a few groups with **no matching STATE/ACTION group** — Before Takeoff Checklist
   is readback-only (runway/flaps/takeoff-speeds/altitude readback, all captain reminders with
   no auto-detect) — confirm it behaves as pure reminders with no auto-ticking.
7. **WATCH ITEM — anti-skid tick stability.** Cockpit Preparation → "Anti-skid: ON" detects on
   the stock `ANTISKID BRAKES ACTIVE` SimVar, which the A380 definition documents as reading
   UNRELIABLY over the data-def path (the same batch has returned both 1 and 0). Leave the
   Checklists tab open on Cockpit Preparation for a couple of minutes with anti-skid steady ON
   and listen/watch for the item churning ticked↔unticked. The write side is safe either way
   (the definition drives the toggle off its own tracked state, so a flaky read can never
   double-toggle the switch). **If the item visibly/audibly flaps, report it — the fix is to
   demote this one item to a manual action (no auto-detect).**
8. **Engine anti-ice readbacks (After Landing / Parking → "Engine anti-ice: OFF").** These
   detect on the stock `ENG ANTI ICE:n` readouts (the write keys have no readable state). Turn
   engine anti-ice ON by hand, confirm the items UN-tick (RevertToState); run the flow or tick
   the item and confirm they re-tick as all four switch off.

---

## Part D — Auto managers

1. In **File → Settings… → First Officer**, enable **Auto Gear Up**, **Auto Gear Down**, and
   **Auto AP** for the A380.
2. Fly a takeoff and confirm:
   - Positive rate of climb above ~50 ft AGL with gear down → gear auto-raises, **"Positive
     rate. Gear up."** announced.
   - Climbing through the **configured AP altitude** (Settings → First Officer numeric
     field, default **350 ft AGL**) → **AP1 engages**; the announcement speaks the
     configured number (e.g. **"350 feet. Autopilot one engaged."**).
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
   APPR page (`A32NX_SPEEDS_LANDING_CONF3` = 1), then take off with flaps 2/3. Confirm the climbout
   retraction to 1 then up is **unaffected** — CONF 3 only caps *extension*, never retraction.
7. **Approach extension, FULL landing.** With CONF **FULL** selected (default; `LANDING_CONF3` = 0),
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

- **Transponder / TCAS mode / ECAM page selection / exterior walk-around / flaps / approach
  autobrake selection are Captain items by design** — every one of these appears only as a
  `Captain(...)` reminder in the flows (and a `Reminder` in the checklists), never actuated by
  the FO. Some (transponder, TCAS mode, ECAM page, flaps) have real settable A380 controls
  elsewhere in MSFSBA's panels, but the FO does not drive them — these are pilot-flying
  judgment calls (e.g. flap setting depends on SimBrief/performance data the FO does not
  compute; ECAM page selection is a captain's monitoring choice).
- **FCU managed-push checklist items are `ActionManual`** ("FCU speed: managed", "FCU heading:
  managed", "FCU altitude: pushed", "Rudder trim: RESET") — these have no readable resting
  state to auto-detect against (a push/pull knob action, not a persistent switch position), so
  they can only be manually ticked (which fires the action) — they never auto-tick from state.
- **Auto-flaps is speed-tape driven, not V-speed driven** — `FbwA380FOAutoManager.CheckFlaps`
  schedules the flap lever off the published `A32NX_SPEEDS_*` L:vars (green dot / S / F / VFE-next)
  and the MFD PERF APPR landing config (`A32NX_SPEEDS_LANDING_CONF3`), not off a V1/VR/V2/VAPP
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
