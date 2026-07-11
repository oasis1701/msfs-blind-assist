# First Officer Fire / Warning Tests — Design (2026-07-11)

Add real, self-completing fire tests to the First-Officer preflight flows of the PMDG 737,
PMDG 777 and FBW A380, matching the existing Fenix behavior (held switch → audible warning →
automatic release; the sound is the blind-pilot verification). On the 737, additionally add
the overspeed (Mach/IAS clacker) and stick-shaker (stall warning) tests. Everything is
mirrored on the Checklists tab. Cargo smoke/fire tests are explicitly OUT of scope
(user decision — straight Fenix parity).

## Reference behavior (Fenix, already shipped)

- Flow: three fire-test steps early in Preflight (APU, ENG1, ENG2), each a pseudo-key the
  executor intercepts and runs as a HELD switch: write 1 → hold 3 s → write 0
  (`FenixActionExecutor.FireTest`, `HoldAsync`). The fire bell is the verification cue.
- Checklist: the same tests as `ActionManual` items firing the same executor method — no
  persistent state exists for a completed test, so the box records "test performed".
- Nothing ever sticks in TEST: the hold self-completes and releases.

## Per-aircraft design

### PMDG 737 NG3

**Live-probed 2026-07-11 (all confirmed working on the running sim):**

- `EVT_FIRE_DETECTION_TEST_SWITCH` (event_base+696 = 70328): a Control-CDA position write
  MOVES AND HOLDS the spring-loaded OVHT/FIRE test switch. Positions: 0 = FAULT/INOP,
  1 = neutral, 2 = OVHT/FIRE. At 2 the fire bell sounds and, staggered over ~1.7 s,
  `WARN_annunFIRE_WARN[0..1]`, `WARN_annunOVHT_DET`, `FIRE_HandleIlluminated[0..2]` and
  `FIRE_annunENG_OVERHEAT[0..1]` all light; writing 1 releases and everything clears.
  (Mouse path equivalent also verified: RIGHTSINGLE→2, LEFTSINGLE→0, release flags → 1.)
- `EVT_OH_WARNING_TEST_STALL_1/2_PUSH` (69935/69936) and
  `EVT_OH_WARNING_TEST_MACH_IAS_1/2_PUSH` (69933/69934): no CDA state field; actuated by
  TransmitClientEvent `#<id>` with `MOUSE_FLAG_LEFTSINGLE` (0x20000000) = press-and-hold,
  then `MOUSE_FLAG_LEFTRELEASE` (0x00020000) = release. Stick shaker / clacker sound while
  held (user-confirmed by ear; PMDG does NOT drive the stock `STALL WARNING` /
  `OVERSPEED WARNING` simvars, so audio is the only feedback — exactly the Fenix model).

**Flow (Preflight):** the dead `Captain("PF_TESTS", "Perform the overhead and fire tests…")`
step is REPLACED by five real steps placed at the TOP of the flow, immediately after the
`PF_WALK` walk-around step (user request: tests early in preflight):

1. `PF_FIRE_TEST` — "Fire warning test — listen for the fire bell": CDA write 2, hold
   **2.0 s**, write 1.
2. `PF_STALL_TEST1` — "Stall warning test 1 — listen for the stick shaker": press, hold
   **3.5 s**, release.
3. `PF_STALL_TEST2` — same, channel 2.
4. `PF_OVSPD_TEST1` — "Overspeed warning test 1 — listen for the clacker": press, hold
   **1.5 s**, release.
5. `PF_OVSPD_TEST2` — same, channel 2.

Hold times are user-specified constants on the 737 executor (tunable in-sim).

**Checklist (PREFLIGHT group):** the `Reminder("PF_TESTS", …)` item is REPLACED by five
`ActionManual` items (same order, near the top of the group mirroring the flow order),
each firing the same executor method as its flow step.

**Executor:** the Fenix pseudo-key pattern — `ExecuteStepAsync` intercepts
`FIRE_TEST`, `STALL_TEST_1/2`, `OVSPD_TEST_1/2` pseudo event names and runs the held
sequences through the serialized dispatch gate (both writes of a hold pace through
`DispatchCoreAsync`, so CDA frame-coalescing is impossible by construction). The
warning-test buttons need a transmit-with-flag press/release helper on the executor
(press LEFTSINGLE → `Task.Delay(hold)` → release LEFTRELEASE), reusing the existing
transmit plumbing (`SendPMDGMomentaryToggle`'s transport, but with the hold between the
pair instead of back-to-back).

### PMDG 777

- Single test button: `EVT_OH_FIRE_OVHT_TEST` with bool state field `FIRE_FireOvhtTest_Sw`.
- **Flow (Cockpit Preparation):** one new step near the top — after the electrical block
  (`CP_BACKUP_GENS`), before window heat: `CP_FIRE_TEST` — "Fire and overheat test — listen
  for the fire bell": CDA write 1, hold **3.0 s** (Fenix parity), write 0.
- **Checklist (PREFLIGHT group):** one `ActionManual` item, same position, same method.
- **Not live-verifiable this session** (sim has the 737 loaded): the held write-1/write-0
  shape follows the bool switch field + the proven 737 fire-switch result; flagged in the
  777 test plan for in-sim verification (the repo's standard model).

### FBW A380

- Single test pushbutton: `A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED`, already exposed as an
  `OnOff` panel control and written through the verified `ApplyUIVariable` path (the A380
  FO executor's standard delegation, under the suppressed-announcer wrap).
- **Flow (Cockpit Preparation):** `Captain("CP_FIRETEST", "Fire test: perform")` is
  REPLACED by a real step and MOVED UP to sit right after `CP_OXY` (crew oxygen) — the
  Fenix order (oxygen → fire tests): `CP_FIRETEST` — "Fire test — listen for the fire
  warning": write 1, hold **3.0 s**, write 0.
- **Checklist (COCKPIT_PREP group):** the `Reminder("CP_FIRETEST", …)` becomes an
  `ActionManual` item at the matching position, firing the same executor method.
- Flagged in the A380 test plan for in-sim verification (hold duration may need tuning if
  the FBW test sequence wants a longer press).

## Invariants respected

- Tests are HELD, self-completing, never left in TEST (Fenix invariant).
- No `*_CL` readback group gains a CheckAction (the new items live in STATE/ACTION groups
  only; `ActionManual` — no auto-detect state exists for "test performed").
- All 737 writes go through the serialized, paced dispatch gate; no two CDA writes share a
  frame. FlowManager's 2 s inter-step pause stays (the tests are deliberately paced).
- No announcements beyond FlowManager's step narration — the warning sounds themselves are
  the verification (screen-reader announce rules: no redundant chatter).
- Checklist item order keeps mirroring flow step order.

## Out of scope

- Cargo smoke/fire detection tests (all aircraft) — user decision.
- 737 FAULT/INOP position and extinguisher squib tests — user decision (OVHT/FIRE only).
- Any announced annunciator readback of test results (kept Fenix-simple; can be added
  later if wanted).

## Verification

- Build: `dotnet build MSFSBlindAssist.sln -c Debug` (x64 solution build).
- 737: immediately verifiable in the current live session (bell/shaker/clacker by ear +
  CDA annunciator readbacks already proven).
- 777 / A380: test-plan additions (docs/pmdg-737-first-officer-test-plan.md gets the new
  Part items; 777/A380 plans get their fire-test rows) — human-verified per repo model.
- Pure logic (definitions data): the existing structural checks (action-free `*_CL`
  invariant) continue to cover the new items.
