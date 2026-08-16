# iFly 737 MAX8 First Officer — In-Sim Test Plan

The iFly 737 MAX8 is the sixth First Officer profile (PMDG 777, PMDG 737 NG3, Fenix A320,
FlyByWire A380, FlyByWire A32NX, iFly 737 MAX8). It is a step-for-step port of the **PMDG 737**
profile — same 13 flow phases, same 24-group checklist structure, same procedures — but every
write goes through `IFly737MAXDefinition.ApplyUIVariable`, the panels' own verified write path,
instead of a second PMDG-style CDA command table (see `docs/first-officer.md` for why). There is
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
| Preflight | Walk-around pause; fire/stall/overspeed warning tests (held/click, aural result); TCAS/WXR/GPWS-equivalent self-tests where available (**no WXR test exists on this SDK — see B2 below**); yaw damper ON; window heat ON; wing/engine anti-ice OFF; packs AUTO; isolation OPEN; engine bleeds ON; both FDs ON; autobrake RTO; transponder **ALT OFF** (not STBY — see B3); EFIS MAP/40; pressurization altitudes set from SimBrief if loaded (see B5); emergency exit lights **ARMED** (see B4); captain reminders for the rest |
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

### B2. No weather-radar self-test
Preflight has no `WXR_TEST` step — the item is a Captain reminder. Confirm the checklist item
reads as a reminder (tick holds, no aural test result expected) and that no flow step tries to
fire one. This is a permanent limitation of the iFly SDK, not a bug.

### B3. Switch-position wording — **LIVE-VERIFY**
Three labels were deliberately changed from the PMDG-737-ported wording because this airframe's
switches don't have the position the PMDG text names:
- **"Transponder: ALT OFF"** (Preflight, Shutdown) — the resting/ground position on this
  transponder mode selector, per its own registered labels.
- **"Gear lever: UP"** (After Takeoff) — this gear lever has only Up/Down, no OFF detent.
- **"Probe heat: AUTO"** (Preflight, After Landing) — this probe-heat switch has only Auto/On,
  no OFF detent.

**Observation:** read the real cockpit switch/label at each of these three points as the flow
sets it.
**Expected:** the physical switch shows exactly the wording above (ALT OFF / UP / AUTO), matching
what the FO announces.
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
SDK exposes for APU-generator availability (there is no EGT field to fall back on, unlike the
PMDG NG3, which uses EGT).

**Observation:** run Before Start from cold-and-dark. Confirm the sequence: APU selector ON,
2 seconds later START, the flow then announces waiting on the APU generator, and only once the
blue APU GEN OFF BUS light actually illuminates does it announce the generator transfer and drop
ground power.
**Expected:** the transfer and ground-power drop happen only after the light is lit — no bus
power loss at any point.
**If different:** if the light's polarity is inverted (lit = NOT available) the flow would
transfer immediately on a cold APU and this needs to be flipped — tell me what the light state
actually was at the moment the flow proceeded.

### B7. Speedbrake — Captain reminder by design
Landing's "Speedbrake: ARM" step is a Captain reminder, not an automated write — the lever's
write command has a documented but **unverified scale mismatch** against its own status
readback (0-254 command range vs 0-225/0-224 observed status range, different detent numbers).
This is deliberate, not a gap to test — confirm the reminder is spoken and no lever movement is
attempted.

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
toggle) and disarm it instead of confirming it. **Observation:** at the moment the 400 ft AGL
push fires, if you can read the raw `LNAV_Switch_Status` value (via the SDK probe tool or a
watch), note whether it was ever 3 with the light lit. **Expected:** value 3 does not occur with
the light lit (the composite pattern intends 3 = "pressed, light off" as a real distinct state).
**If different:** flag it — the lit-test formula needs a special case for 3.

### D3. 10,000 ft landing lights + transition altitude/level
Climb through 10,300 ft → "Above ten thousand. Landing lights off." (both lights, plain 0/1
status on this airframe, no retractable/fixed split). Descend through 9,700 ft → "Below ten
thousand. Landing lights on." With SimBrief loaded, climb through the transition altitude →
"Transition altitude. Altimeters set to standard." — confirm both EFIS baro knobs actually read
STD (see D4 below). Descend through the transition level → announce-only "set local altimeter
pressure now" — the FO cannot set QNH itself here; use Ctrl+B.

### D4. Altimeter STANDARD — BARO_STD latch semantics — **LIVE-VERIFY**
The transition-altitude push (and the `BARO_STD_BOTH` pseudo-key generally) reads
`BARO_STD_Status_{0,1}` as a **latched** "this side is currently on STD" indicator before
deciding which side(s) to press — a side already confirmed STD is left alone. The generated SDK
header describes the underlying field only with the generic boilerplate "0: switch released /
1: switch pressed", which is ambiguous between a real latch and a momentary press flag. The
shipped Ctrl+B altimeter dialog already reads this field the same (latched) way, which
corroborates but does not prove it.

**Observation:** set both sides to STANDARD by hand (or via a prior successful FO push), then
independently trigger the "set standard" action again (re-run Preflight, or climb through the
transition altitude a second time in a scenario where you can force it).
**Expected:** a **silent no-op** — nothing is pressed, no announcement, because both sides
already read STD.
**If different:** if instead the FO presses one or both knobs and then announces a failure (or
worse, silently flips an already-STD side back to QNH), the field is momentary, not latched —
the whole guard in `IFly737ActionExecutor.SetAltimetersStandardCoreAsync`/`IsBaroStd` needs
rethinking (it can no longer trust a "true" reading to mean "leave it alone"). Also worth
checking: does turning the Captain's baro knob to STD by hand actually move
`BARO_STD_Status_0` to a value this code reads as true?

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

### D6. ND range scale — **LIVE-VERIFY**
`SetEFISRangeCapt`/the checklist's EFIS-range items use a 0..10 index (0.5/1/2/5/10/20/40/80/
160/320/640 nm), following the SDK command documentation. The generated struct's own field
comment for `ND_Range_Status` instead says "0~2", which the code treats as wrong/stale.

**Observation:** run Preflight (sets EFIS range to index 6 = 40 nm) and read the Captain's ND
range readout.
**Expected:** the display shows **40 nm**.
**If different:** note what range actually shows at index 6 — if it's a small number (0, 1, or
2), the struct comment was right and the command-doc-based 0..10 scale is wrong; tell me the
actual displayed range and the index-to-range table gets corrected.

---

## Part E — Center fuel pump automation (opt-in, default OFF)

This is the same shared `CenterFuelPumpAutomation` policy used by the PMDG 737 and 777 (see
`docs/first-officer.md`'s Center fuel pump section for the full policy description — arm-ON
gating, the cumulative low-press debounce, the two arm-suppressor latches, the pending-command
write-then-verify latch). The iFly adapter (`IFly737FOAutoManager.UpdateCenterPumps`) feeds it
from `Fuel_CENTER_L/R_Switch_Status` + `LOW_PRESSURE_CENTER_L/R_Light_Status` (center) and the
four wing pump/low-press pairs (credibility check) — this is the first time this policy runs
against a non-PMDG SDK, so confirm the field wiring produces sane behavior, not just that the
pure policy logic is correct (that part already has unit-test coverage independent of the
aircraft).

1. **Enable** "Auto-manage center fuel pumps (PMDG 737/777 and iFly MAX8)" in
   **Settings → First Officer**. Load with center fuel present, wing pumps off, on the ground.
2. Turn the wing pumps ON by hand (or run Before Start up to that point) — the center pumps
   should switch ON shortly after ("Center fuel pumps on."), matching the Before Start flow's
   merged "Fuel pumps: ON" step.
3. Let the center tank run dry in flight (or reduce center fuel before the flight if you can).
   Confirm the low-press annunciators light, and after ~3 seconds of (possibly intermittent)
   low-press signal, the pumps switch off with "Center tank low. Center fuel pumps off." — this
   should hold even if the low-press light flickers rather than staying solid (the debounce is
   cumulative, not reset-on-any-gap).
4. Confirm the pumps do **not** re-arm on their own after the dry-off — only a real refuel, a
   settings toggle off/on, or an aircraft switch clears the dry-off latch.
5. With the setting **disabled** (default), confirm the center pumps never move on their own —
   only the flow/checklist paths (Before Start / Shutdown) touch them.
6. Check `%APPDATA%\MSFSBlindAssist\logs\center_pumps.log` if anything looks wrong — it traces
   every input/latch/action for this feature and is the first thing to attach to a bug report.

---

## Known limitations (by design, not defects to test for)

- **No weather-radar self-test command exists in this SDK** — Preflight's WXR-test item is a
  permanent Captain reminder (see B2).
- **No lower-DU/EICAS synoptic-page-select field exists** — the Before Taxi lower-DU item is a
  permanent Captain reminder.
- **Speedbrake ARM is a permanent Captain reminder** — the lever write's scale mismatch is
  unverified and deliberately not wired (see B7).
- **Ground power has no availability readback at all** (worse than the PMDG NG3, which at least
  has an unreliable one) — every ground-power checklist/flow item is a stateless press.
- **No engine start-valve field** — engine-start gating relies on the start switch springing
  back plus N2 only; there is no separate "starter valve open" confirmation step like the PMDG.
- **Takeoff flaps / landing autobrake / speedbrake are Captain items on every aircraft** in this
  fleet, not just this one — unchanged fleet-wide policy.

## Part F — Regression: other five aircraft unaffected

This work only adds new files under `FirstOfficer/IFly737/` plus a menu item and profile
registration; it does not touch the PMDG 777/737, Fenix, A380 or A32NX profiles. As a light
regression check: open each of the other five First Officer windows once, confirm they still
open with their own title and run one flow each without error.
