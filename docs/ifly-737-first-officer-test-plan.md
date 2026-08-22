# iFly 737 MAX8 First Officer — In-Sim Test Plan

The iFly 737 MAX8 is the sixth First Officer profile (PMDG 777, PMDG 737 NG3, Fenix A320,
FlyByWire A380, FlyByWire A32NX, iFly 737 MAX8). It is a step-for-step port of the **PMDG 737**
profile — same 13 flow phases, same 24-group checklist structure, same procedures — but writes go
through `IFly737MAXDefinition.ApplyUIVariable`, the panels' own verified write path, instead of a
second PMDG-style CDA command table, with **two** sanctioned bypasses: the pressurization
altitudes (sent directly via `SendDirect`/`Sdk.SendCommand` because the def's numeric-entry path
would speak over the flow's step narration) and **altimeters to standard** (set by VALUE via the
stock `KOHLSMAN_SET` event — the Ctrl+B altimeter dialog's own live-verified mechanism — because
`BARO_STD_Status` is momentary and the EFIS STD command is a toggle, so no closed loop through
the STD button is possible) (see `docs/first-officer.md` for why). There is
no automated test project for SimConnect/UI behavior, so the repo owner verifies this against a
live sim (MSFS 2020 or 2024) with the iFly 737 MAX8 loaded.

Open the window from **Tools → "iFly 737 MAX8 First Officer"**. The window has two tabs:
**Flows** and **Checklists**, identical in layout to every other First Officer window.

**This test plan carries LIVE-VERIFY items called out inline — each one is a place where the
code made a documented assumption about the aircraft that a code review could not settle
without a live sim.** They are collected again in Part D for a single pass if you'd rather do
them separately from the walkthrough. Nothing else in the app depends on them being right on
day one — a wrong assumption fails safely (a refused write, a Captain reminder, or a false "did
not set" announcement), but each is worth 30 seconds to confirm because a silently-wrong one
would mislead a blind pilot who cannot see the switch it claims to have moved.

---

## Settled statically — do NOT re-run these in the sim (2026-08 vendor-SDK investigation)

A read-only sweep of the v1.5 vendor package (`737MAX_SDK\sdk\SDK_Defines.h`, `key_command.h`)
and the installed cockpit model (`iFly737Max_INTERIOR.xml`) settled the following. Each was
previously a LIVE-VERIFY item somewhere in this plan; they are listed here so the tester skips
them rather than re-running them.

| Item | Verdict | Evidence |
|---|---|---|
| Gear lever positions | Exactly two, 0 UP / 1 DN — **no OFF detent** | vendor-documented Value2 on the gear SET command; matches the def's `new[] { "Up", "Down" }` |
| Fire/OVHT test encoding | 0 FAULT / 1 Neutral / 2 OVHT, and the model has **no release callback** — so our hold-then-write-neutral is mandatory, not a nicety | `key_command.h` Value2 column; `iFly737Max_INTERIOR.xml` clickspot has press-only |
| Stall / overspeed / GPWS / TCAS tests | Click-only — **no documented hold semantics** | `key_command.h` (no hold/release Value2 pairs) |
| Autobrake | 0..5 | vendor Value2 range |
| Transponder mode | 0..3, **ALT OFF first** | vendor Value2 + the status field's own labels |
| Belts / no-smoking | 0 / 1 / 2 | vendor Value2 |
| Flight director / autothrottle | 0 / 1 | vendor Value2 |
| FLT ALT / LDG ALT | literal feet, within the vendor's documented ranges | `key_command.h` |
| Speedbrake write-vs-read scale mismatch | **Confirmed real** — write `0~254` (detents 0/34/180/254) vs read `0~225` (detents 0/35/149/224) | `key_command.h` vs `SDK_Defines.h` — the read-only stance is vendor-justified, see B7 |
| MCP CMD A / B / CWS A / CWS B | CMD A press 7 / release 8; CMD B 9/10; CWS A 37/38; CWS B 39/40 | `iFly737Max_INTERIOR.xml` clickspot triggers |
| Attendant call | a genuine momentary chime press | model XML |
| ND range | **0..10** — the struct's `0~2` comment is a stale vendor doc bug; the command doc is authoritative | `key_command.h` vs `SDK_Defines.h` |
| Starter-valve field | **Does not exist** — exhaustive header search | `SDK_Defines.h` |
| APU EGT field | **Does not exist** — exhaustive header search | `SDK_Defines.h` |
| `BARO_STD_Status` latched vs momentary | **MOMENTARY** — triple-corroborated (see below) | `SDK_Defines.h:560` "0:switch released / 1:switch pressed" (iFly's momentary-button phrasing, cf. MINS_RST/CTR/TFC); model XML momentary clickspot (triggers 35/36 captain, 37/38 F/O) with **no persistent STD-mode variable anywhere**; `INSTRUMENT_EFIS_{L,R}_BARO_STD` is a toggle CLICK with no `_SET` and no Value2 |

**The BARO_STD finding superseded the old "latched vs momentary" test item.** The FO no longer
touches the EFIS STD buttons at all — it sets 29.92 inHg by value through the stock
`KOHLSMAN_SET`, which is idempotent (safe to repeat, silent when already standard). What is
still open is whether that one write reaches BOTH altimeters — see D4.

---

## Part A — Window lifecycle

1. Load the iFly 737 MAX8. Open **Tools → "iFly 737 MAX8 First Officer"** — title reads
   **"First Officer — iFly 737 MAX8"**.
2. Switch to another aircraft and back → the window disposes and re-creates cleanly (same as
   every other FO window).
3. With the window open, disconnect/reconnect SimConnect (or restart the iFly plugin) →
   the window re-wires; flows/checklists keep driving switches once the SDK shared-memory
   client reports ready again.
4. Confirm no other aircraft's Tools menu shows the iFly item, and the iFly's Tools menu shows
   only its own First Officer item (not PMDG 737/777, Fenix, or A380).

---

## Part B — Flows: cold-and-dark → secure walkthrough

Start cold-and-dark at a gate with ground power available. Run each flow in order from the
Flows tab; spot-check the panels/overhead after each. Table columns mirror the PMDG 737 test
plan's Part B2 — this aircraft's flow *steps* are the same, only the underlying write differs.

| Flow | Expected (spot-check the overhead/MCP) |
|------|----------------------------------------|
| Electrical Power Up | Battery ON (guarded write); Standby power AUTO; Ground power ON (momentary press — see B1 note); IRS selectors → NAV, no pause (alignment runs in background) |
| Preflight | Walk-around pause; fire/stall/overspeed warning tests (held/click, aural result); TCAS/WXR/GPWS-equivalent self-tests where available (**the WXR test is a reminder by choice, not by absence — see B2 below**); yaw damper ON; window heat ON; wing/engine anti-ice OFF; packs AUTO; isolation OPEN; engine bleeds ON; both FDs ON; autobrake RTO; transponder **ALT OFF** (not STBY — see B3); EFIS MAP/40; pressurization altitudes set from SimBrief if loaded (see B5); emergency exit lights **ARMED** (see B4); captain reminders for the rest |
| Before Start | Captain MCP reminder; **APU selector → ON → (2 s) → START, then waits for the APU generator to come on line** (see B6 — this is the flow's biggest deviation from the PMDG port); fuel pumps ON (center gated on quantity — see Part E); electric hydraulic pumps ON; APU bleed ON; anti-collision ON; transponder TA/RA |
| Engine Start | Packs OFF; ENG 2 start switch GRD + start lever IDLE at N2 ≥ 20%, then ENG 1 the same (no start-valve wait — this SDK has no start-valve field, see the flow's header comment) |
| Before Taxi | After-start power transfer (generators ON, APU bleed OFF, APU OFF), then probe heat **ON** (only Auto/On exist — see B3); packs AUTO; isolation AUTO; start switches CONT; taxi + turnoff lights ON; lower DU item is a Captain reminder (no lower-DU field on this SDK); captain reminders for anti-ice and takeoff flaps |
| Before Takeoff | Landing lights ON; strobes ON; A/T arm (absolute switch, no toggle hazard); transponder TA/RA |
| After Takeoff | Packs AUTO; start switches OFF; turnoff lights OFF; **gear lever UP** (not "OFF" — this airframe's lever has only Up/Down, see B3); autobrake OFF |
| Descent | Seatbelt sign ON; captain reminders for autobrake, ILS, landing data |
| Approach | EFIS APP / range 20; altimeter reminder |
| Landing | Start switches CONT; **speedbrake ARM is a Captain reminder** (see B7); missed-altitude reminder |
| After Landing | Landing lights off; taxi light ON; strobes steady; anti-ice OFF; probe heat **AUTO**; APU ON; start switches OFF; autobrake OFF |
| Shutdown | APU generators ON; start levers CUTOFF (no spool-down wait); signs/lights off; fuel pumps OFF; window heat OFF; transponder **ALT OFF** |
| Secure | IRS OFF; emergency exit lights **OFF**; window heat OFF; packs OFF |

Verify **Pause / Resume / Stop** mid-flow, and that **"Run Related Flow"** from a checklist
group starts the matching flow — these are generic engine behaviors, unchanged from every other
aircraft.

### B1. Ground power has no availability readback
Unlike the PMDG 737 (whose ground-power checklist items are already stateless presses for the
same reason), this SDK exposes no ground-power-availability field at all. The flow's "Ground
power: ON" step presses unconditionally with no follow-on wait — confirm it presses once per
run with no error, whether or not a GPU is actually connected at the stand.

### B2. Weather-radar self-test — reminder BY CHOICE, and an OPTIONAL probe
Preflight has no `WXR_TEST` step — the item is a Captain reminder. Confirm the checklist item
reads as a reminder (tick holds, no aural test result expected) and that no flow step tries to
fire one.

**Correction (2026-08):** this is *not*, as previously written here, a missing-command
limitation of the SDK. `KEY_COMMAND_FMS_WXR_SYS_CTRL_SET` is documented in v1.5
`key_command.h` — "WXR, System Control Switch - Set … Value2: 0:switch TEST; 1:switch NORM" —
is already generated as `IFlyKeyCommand.FMS_WXR_SYS_CTRL_SET`, and has a readable status field
`Weather_Radar_System_Control_Switch_Status` (0 TEST / 1 NORM). The earlier grep looked for a
test *click* command and drew the wrong conclusion. It is left unwired deliberately: this
airframe has a documented class of test switches that accept commands and do nothing (the A/P
and A/T disengage-light TEST switches, live-tested 2026-07-23 and found unmodelled), so
selecting TEST blind risks latching an unmodelled or un-releasing TEST mode.

**Optional probe (not required for this pass):** by hand, send `FMS_WXR_SYS_CTRL_SET` with
Value2 = 0, watch `Weather_Radar_System_Control_Switch_Status` and listen/look for a radar test
pattern, then send Value2 = 1 and confirm it returns to NORM.
**If the TEST position is modelled and self-contained** (it enters, it is observable, and NORM
releases it cleanly), say so — the Captain reminder can then be upgraded to a real automated
test like the PMDG jets have.

### B3. Switch-position wording — **LIVE-VERIFY (reduced scope)**
Three labels were deliberately changed from the PMDG-737-ported wording because this airframe's
switches don't have the position the PMDG text names. Two are now settled statically (see the
table above) and need no in-sim check:
- ~~**"Transponder: ALT OFF"**~~ — settled: the vendor documents mode 0..3 with **ALT OFF first**.
- ~~**"Gear lever: UP"**~~ — settled: the vendor documents exactly two positions, 0 UP / 1 DN.

Still worth 30 seconds:
- **"Probe heat: AUTO"** (Preflight, After Landing) — this probe-heat switch is registered with
  only Auto/On, no OFF detent.

**Observation:** read the real cockpit probe-heat placard as the flow sets it.
**Expected:** the physical switch shows exactly **AUTO**, matching what the FO announces.
**If different:** the definition's registered `ValueDescriptions` strings (cited in the code
comments next to each item) don't match the real placard — tell me the real wording and the
label gets corrected; this is cosmetic (spoken label only), not a functional defect.

### B4. Emergency exit lights — **LIVE-VERIFY**
Preflight arms the emergency exit lights (`Emergency_Light_Switch_Status`); Secure/Shutdown turn
them off. This switch has FOUR positions (0=Guard closed / 1=Off / 2=Armed / 3=On) and the code
now treats **0 (guard closed) as equivalent to ARMED everywhere** — both detection and any
future write logic.

**Observation:** with the guard closed (the switch's rest position, most likely how the aircraft
spawns), does the Preflight checklist's "Emergency exit lights: ARMED" item auto-tick, and does
the flow announce it as already armed (no write) rather than trying to move a guarded switch?
**Expected:** yes to both — a guard-closed switch reads as armed, no write attempted.
**Still open after the 2026-08 model sweep:** the cockpit XML's guard is **animation only** —
the actual guard/position logic lives inside the WASM plugin, so the model cannot tell us
whether ARMED is reachable with the guard down. This item stays live for exactly that reason.
**If different:** if the real switch needs the guard OPENED before ARMED is reachable (the
PMDG 777's emergency-exit-light guard needed exactly this fix once), tell me and the write path
gets a guard-open step added — this switch currently has no write wired at all (only detection),
so if you ever need to change its position by hand, use the cockpit control; the FO only reads
this one, it does not set it.

### B5. Pressurization altitude LED windows — **LIVE-VERIFY**
Preflight sets the FLT ALT / LAND ALT windows from a loaded SimBrief plan (skipped, no
announcement, if no plan is loaded). The composition helper that reads these five-digit LED
windows back for checklist auto-detect now allows **leading blanks** (so a value under 10,000 ft
reads correctly padded) and an **optional leading minus** immediately before the first digit (so
a below-sea-level LAND ALT, e.g. a Schiphol-elevation destination, reads as negative).

**Observation:** load a SimBrief OFP with a destination field elevation under 1,000 ft (e.g.
450 ft) and run Preflight. Read the physical LAND ALT window on the pressurization panel, then
check the Preflight checklist's pressurization item auto-ticks.
**Expected:** the window shows blank-blank-4-5-0 (right-aligned, leading digits blank) and the
checklist item ticks. If you can find a destination with a below-sea-level elevation, confirm
the window shows a minus sign immediately to the left of the first digit with no gap or blank
cell between them.
**If different:** if the real window pads differently (e.g. leading zeros instead of blanks, or
the minus sign floats with a gap), the composer in `IFly737FoComposition.ComposeAltWindow` needs
its cell-order assumption corrected — tell me what you see digit-by-digit.

### B6. APU-availability wait — **LIVE-VERIFY**
Before Start commands the APU ON → (2 s dwell) → START, then waits on
`APU_GEN_OFF_BUS_Light_Status` reading **lit** as "the APU generator is available" before
pressing the generator-transfer buttons and dropping ground power. This is the one signal this
SDK exposes for APU-generator availability (the absence of an EGT field, which the PMDG NG3 uses
instead, is now **settled statically** — an exhaustive `SDK_Defines.h` search found none).

Two things the 2026-08 header sweep pinned down and one it could not:
- `APU_GEN_OFF_BUS_Light_Status` is a **SCALAR** field, not a per-side array — do not expect
  (or look for) separate left/right readings.
- The vendor documents its values only as **OFF / DIM / BRI**, i.e. brightness, not semantics.
  Nothing in the header says lit means "available" — that mapping is still an assumption, which
  is exactly what this item tests.
- **The APU knob wrinkle:** the cockpit XML models the APU selector as a plain 2-position rotary
  with **no spring-loaded START detent visible at all**. The `ENGAPU_APU_SET 2` START-latch
  behaviour is therefore WASM-internal and unobservable from the model — so the ON → 2 s → START
  sequence below still has to be confirmed by watching the APU actually spool.

**Observation:** run Before Start from cold-and-dark. Confirm the sequence: APU selector ON,
2 seconds later START, the APU actually spools up, the flow then announces waiting on the APU
generator, and only once the
blue APU GEN OFF BUS light actually illuminates does it announce the generator transfer and drop
ground power.
**Expected:** the transfer and ground-power drop happen only after the light is lit — no bus
power loss at any point.
**If different:** if the light's polarity is inverted (lit = NOT available) the flow would
transfer immediately on a cold APU and this needs to be flipped — tell me what the light state
actually was at the moment the flow proceeded.

### B7. Speedbrake — Captain reminder by design (mismatch now CONFIRMED)
Landing's "Speedbrake: ARM" step is a Captain reminder, not an automated write — the lever's
write command has a scale mismatch against its own status readback. The 2026-08 header sweep
**confirmed** it: the write is `0~254` with detents 0 / 34 / 180 / 254, the read is `0~225` with
detents 0 / 35 / 149 / 224. The read-only stance is vendor-justified, not merely cautious. This
is deliberate, not a gap to test — confirm the reminder is spoken and no lever movement is
attempted.

### B8. Engine start — GRD auto-release — **LIVE-VERIFY (still open)**
Engine start gates on the start switch springing back from GRD plus N2 (there is no starter-valve
field — settled statically). Confirm the switch does return from GRD on its own at the expected
N2, and that the flow's gate sees it.

### B9. Ground-power click direction — **LIVE-VERIFY (still open)**
GRD PWR / generator connect is "Move DOWN = ON, Move UP = OFF". That direction is corroborated
by the vendor's UP/DOWN ↔ Value2 convention **only** — nothing in the model XML confirms it.
Confirm a DOWN click actually connects (watch `ENG_TRANSFER_BUS_OFF` / `APU_GEN_OFF_BUS`).

---

## Part C — Checklists (Checklists tab): auto-detect parity

1. The tab lists the same 24 groups in the same flight order as the PMDG 737 (9 auto-detect
   state/action groups + readback `_CL` checklists, one pair per phase that has a flow, plus the
   phases with no matching action group).
2. As you run the flows (or set switches by hand), the matching state-group items **auto-tick**.
3. `RevertToState` items (gear, autobrake, the readback checklists) **un-tick** when the state no
   longer matches — e.g. tick "Gear lever: UP" then move the gear lever to DOWN by hand; the item
   should un-tick within the manual-tick grace window.
4. Ticking an actionable state-group item **fires the switch** (e.g. tick "Battery: ON" → the
   battery physically moves), confirmed by the item staying ticked on the next poll.
5. Readback `_CL` groups are action-free — ticking a readback item must **not** move a switch; it
   only auto-ticks from live state.
6. **Landing Checklist speedbrake-armed item** (`LDC_SPDBRK`) is the one readback item that
   diverges from the PMDG port: it auto-detects on `SPEED_BRAKE_ARMED_Light_Status` (this SDK
   does expose that light, unlike the PMDG NG3 struct) rather than staying a plain reminder. Arm
   the speedbrake by hand and confirm the item ticks.

---

## Part D — Automatic modes

### D1. Auto-AP-engage, closed loop (universal service, shared with every aircraft)
Enable Auto AP in **File → Settings… → First Officer tab**, then fly:
- Climbing through the **effective AP altitude** → autopilot **CMD A** engaged via the
  clickspot-replay fallback (`AUTOMATICFLIGHT_CMD_A` momentary), verified against the readback
  before announcing. The effective height is the configured number (default 350 ft AGL) raised
  to this aircraft's own engage floor — confirm what floor value is in effect by checking
  `IFly737MAXDefinition.MinimumAutopilotEngageAltitudeAgl` if the announced height surprises you.
- **The announcement must only fire once the CMD A annunciator reads engaged** — not on the
  press. If the press is rejected (e.g. engaged too low, or a fault), the service retries a
  bounded number of times before announcing "Autopilot did not engage. Captain action required."
  Force a rejection if you can (hold the AP disengage bar through the climb) and confirm you
  never hear a false "Autopilot engaged."
- Engage CMD A manually below the floor → the FO must stay silent and must NOT press (a press on
  an already-engaged toggle would disconnect it).

### D2. LNAV/VNAV at 400 ft AGL (737-specific, fixed height, independent of the AP-engage setting)
Climbing through 400 ft AGL with Auto AP enabled → LNAV/VNAV pushed, but **only modes whose MCP
annunciator is unlit** are pressed; the announcement names what was pushed ("400 feet. LNAV and
VNAV engaged."), nothing announced if both were already armed.

**LNAV_Switch_Status value 3 — LIVE-VERIFY.** This switch/light is a 0-5 composite; the lit
test (`value % 3 > 0`) classifies value **3 as UNLIT**. If the real switch can ever read exactly
3 while the LNAV light is actually ON, the FO would press an already-armed LNAV button (a
toggle) and disarm it instead of confirming it. **This item got STRONGER, not weaker, in the
2026-08 model sweep:** `iFly737Max_INTERIOR.xml` tracks the button's press-state and its lamp in
**separate** L:vars, so "pressed with the light off" and "pressed with the light on" are both
representable states of the composite — value 3 with the light lit is not structurally
impossible, it simply has not been observed. Please look for it deliberately.
**Observation:** at the moment the 400 ft AGL
push fires, if you can read the raw `LNAV_Switch_Status` value (via the SDK probe tool or a
watch), note whether it was ever 3 with the light lit. **Expected:** value 3 does not occur with
the light lit (the composite pattern intends 3 = "pressed, light off" as a real distinct state).
**If different:** flag it — the lit-test formula needs a special case for 3.

### D3. 10,000 ft landing lights + transition altitude/level
Climb through 10,300 ft → "Above ten thousand. Landing lights off." (both lights, plain 0/1
status on this airframe, no retractable/fixed split). Descend through 9,700 ft → "Below ten
thousand. Landing lights on." With SimBrief loaded, climb through the transition altitude →
"Transition altitude. Altimeters set to standard." — confirm the altimeters actually go to
standard (see D4 below). Descend through the transition level → announce-only "set local
altimeter pressure now" — the FO cannot set QNH itself here; use Ctrl+B.

### D4. Altimeter STANDARD via `KOHLSMAN_SET` — **LIVE-VERIFY (new)**
This item REPLACES the old "BARO_STD latch semantics" test, which is superseded: `BARO_STD_Status`
is settled MOMENTARY (see the table at the top), so the guarded-toggle push it tested no longer
exists. The transition-altitude push (and the `BARO_STD_BOTH` pseudo-key generally) now sets
**29.92 inHg by value** through the stock `KOHLSMAN_SET` event with **no altimeter index** —
the same mechanism the app's Ctrl+B altimeter dialog already uses and has live-verified. It
skips when the cached `ALTIMETER_SETTING` already reads standard, and it is idempotent, so
re-running it is harmless.

**What is still open:** whether that one indexless write reaches **both** altimeters. The iFly
*appears* to track ONE Kohlsman for both sides (the Ctrl+B dialog's own "applies to both
altimeters" claim), but nothing has confirmed it.

**Observation:** with SimBrief loaded, climb through the transition altitude. Read the Captain's
PFD baro readout **and** the First Officer's.
**Expected:** the announcement "Transition altitude. Altimeters set to standard.", **both** PFDs
showing STD (or 29.92 / 1013), and the monitored altimeter value announced once by the app (the
FO itself says nothing extra on success — that is by design; the `ALTIMETER_SETTING` monitor is
the confirmation channel, exactly as it is for Ctrl+B).
**If different:** if only the Captain's side goes to standard, the indexless `KOHLSMAN_SET`
assumption is wrong and both the FO push and the Ctrl+B dialog need an indexed second write —
tell me which side moved. If **neither** moved, you should hear "Altimeter standard did not set."
about 1.5 s later; if you hear that while the PFD *did* go to standard, the readback timing needs
lengthening. Hearing that phrase on a run that visibly worked was the exact bug this change
fixed, so it is worth listening for specifically.

### D5. Before Start re-run — **LIVE-VERIFY, expected-to-be-broken today**
The APU-availability wait (B6) uses a **Stop-on-timeout** failure policy — a timeout aborts the
whole Before Start flow rather than skipping past it. This was ported from the PMDG 737 template
where a "Skip after 30 s" policy exists specifically to make a **second** run of Before Start
(after the APU has already transferred to the bus) pass through quickly instead of waiting the
full timeout every time. The iFly port does not yet have that Skip-on-rerun behavior.

**Observation:** run Before Start once successfully (APU on the bus, generators transferred).
Then run Before Start a **second time** without changing anything.
**Expected (per the code as shipped):** the flow sits silent through the APU-generator wait for
the full ~120 s timeout, then **aborts** — meaning every step after that point (fuel pumps,
hydraulics, anti-collision, transponder) never runs on the second pass.
**What to confirm:** does this actually happen as described? If so, and it's a real annoyance in
practice (e.g. because a checklist correction workflow re-runs Before Start), tell me — the fix
is a Skip-on-timeout policy plus a "some signal that says the generator is already on the bus"
skip guard. `APU_Generator_Switch_Status` is a **candidate** for that skip guard (the switch
position after a successful transfer), but it is deliberately **not wired** yet — it needs this
live confirmation of what it reads (and whether it's reliable) before it's trusted the way the
PMDG NG3's `APU_annunAPU_GEN_OFF_BUS`-based skip is trusted. Don't wire it without first checking
what value it holds cold, mid-transfer, and after a successful transfer.

---

## Part E — Center fuel pump automation (opt-in, default OFF)

This is the same shared `CenterFuelPumpAutomation` policy used by the PMDG 737 and 777 (see
`docs/first-officer.md`'s Center fuel pump section for the full policy description). As of
2026-08-16 this policy is **QUANTITY-based**, not annunciator-based: OFF fires once center
fuel quantity is confirmed below `OffThresholdLbs` (1000 lb) for 2 continuous seconds, in any
phase of flight, and auto-arm requires center quantity above `ArmThresholdLbs` (1500 lb) with
the wing pumps already on. The low-press annunciators are no longer read by this policy at all.
The iFly adapter (`IFly737FOAutoManager.UpdateCenterPumps`) feeds it fuel quantity from the SDK
and the wing/center pump switch states — this is the first time the rewritten policy runs
against a non-PMDG SDK, so confirm the field wiring produces sane behavior, not just that the
pure policy logic is correct (that part already has unit-test coverage independent of the
aircraft, including an invalid-quantity guard — iFly is the adapter that can pass NaN through
unconverted).

1. **Ground arm refused at or below 1500 lb, performed above it.** Enable "Auto-manage center
   fuel pumps (PMDG 737/777 and iFly MAX8)" in **Settings → First Officer**. Load on the ground
   with center fuel at or below 1500 lb, wing pumps off. Turn the wing pumps ON by hand (or run
   Before Start up to that point) — the center pumps must stay OFF (no arm at ≤1500 lb, matching
   the 500 lb hysteresis gap above `OffThresholdLbs`). Reload or refuel above 1500 lb and repeat
   — the center pumps should switch ON shortly after wing pumps go on ("Center fuel pumps on."),
   matching the Before Start flow's merged "Fuel pumps: ON" step.
2. **In-flight depletion — auto-off within a few seconds of crossing 1000 lb.** Let the center
   tank run dry in flight (or reduce center fuel before the flight if you can) with the pumps
   running. As the gauge crosses below 1000 lb, the pumps should switch off within a few seconds
   with "Center tank low. Center fuel pumps off." Verify in
   `%APPDATA%\MSFSBlindAssist\logs\center_pumps.log`: `belowMs` should accrue toward 2000 as the
   quantity stays below threshold, and the line ending in `-> TurnOff` should appear once the
   confirm completes.
3. **Manual-off respected.** Switch the center pumps off by hand (wing pumps still on, quantity
   still above `OffThresholdLbs`) and confirm they do **not** re-arm on their own — only a
   genuine ground refuel above the recorded floor + 250 lb, a settings toggle off/on, or an
   aircraft switch clears the latch.
4. **Ground refuel above floor + 250 re-arms.** After either a dry-off or a manual-off latch is
   set (floor recorded at the quantity where it latched), refuel on the ground to above
   `floor + 250` lb — the latch should clear and, with wing pumps on and quantity above 1500 lb,
   the pumps should re-arm ("Center fuel pumps on.").
5. With the setting **disabled** (default), confirm the center pumps never move on their own —
   only the flow/checklist paths (Before Start / Shutdown) touch them.
6. Check `%APPDATA%\MSFSBlindAssist\logs\center_pumps.log` if anything looks wrong — it traces
   every input/latch/action for this feature and is the first thing to attach to a bug report.
   Its line shape is now `qty= dt= ready= gnd= pumps= wing= belowMs= dryOffLatch=
   manualOffLatch= floor= pending= -> Action` (no more `dry=`/`cred=`/`dryMs=`).

---

## Known limitations (by design, not defects to test for)

- **The weather-radar self-test is a Captain reminder BY CHOICE, not because the command is
  missing** (corrected 2026-08) — `FMS_WXR_SYS_CTRL_SET` (Value2 0 TEST / 1 NORM) exists and is
  readable back, but is left unwired pending in-sim proof that the TEST position is modelled at
  all (see B2, which carries an optional probe).
- **No lower-DU/EICAS synoptic-page-select field exists** — the Before Taxi lower-DU item is a
  permanent Captain reminder.
- **Speedbrake ARM is a permanent Captain reminder** — the lever write's scale mismatch against
  its own status readback is now CONFIRMED from the vendor headers (write 0~254, read 0~225), so
  the write stays deliberately unwired (see B7).
- **Ground power has no availability readback at all** (worse than the PMDG NG3, which at least
  has an unreliable one) — every ground-power checklist/flow item is a stateless press.
- **No engine start-valve field, and no APU EGT field** — both confirmed absent by an
  exhaustive 2026-08 search of `SDK_Defines.h`. Engine-start gating relies on the start switch
  springing back plus N2 only (there is no "starter valve open" confirmation step like the
  PMDG), and APU availability comes from the `APU_GEN_OFF_BUS` annunciator instead of EGT.
- **Takeoff flaps / landing autobrake / speedbrake are Captain items on every aircraft** in this
  fleet, not just this one — unchanged fleet-wide policy.

## Part F — Regression: other five aircraft unaffected

This work only adds new files under `FirstOfficer/IFly737/` plus a menu item and profile
registration; it does not touch the PMDG 777/737, Fenix, A380 or A32NX profiles. As a light
regression check: open each of the other five First Officer windows once, confirm they still
open with their own title and run one flow each without error.
