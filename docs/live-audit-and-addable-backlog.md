# Live-flight audit checklist + Addable-items backlog (A380 & A320)

Two parts:
1. **Part 1 — Live-flight audit checklist.** Everything already implemented that can ONLY be
   confirmed correct from an actual flight (right phase / airborne / on a runway). Ground-cold
   verification is impossible for these, so they were shipped on source-reading + static checks and
   are waiting on a flight to sign off.
2. **Part 2 — Addable-items backlog.** A complete report (from the 2026-06-03 three-agent
   exhaustive sweep of EWD/SD, PFD/ND/ISIS, flyPad/MFD, and the whole cockpit L-var surface) of
   everything else that COULD be added/made-accessible, niche or not. **Nothing here is
   implemented** — it's the "is anything else addable?" survey. Pick from it deliberately.

---

# PART 1 — Live-flight audit checklist

Tick these off in a real flight. Grouped by the flight phase where each becomes testable. Most are
A380; the A320 column notes whether the same item exists/ported there.

## On the ground, engines running / APU start (before pushback)
- [ ] **EWD computed THR%** reads ~idle and tracks the thrust levers as you advance them
      (clamp formula; should hit ~100% at TOGA). *(A320: same once the EWD decode is ported.)*
- [ ] **EWD thrust-limit line** (`Thrust limit:` per engine) shows the active limit (CLB/FLX/TOGA).
- [ ] **APU page**: on APU start, AVAIL appears; FLAP shows MOVING→OPEN; N/EGT climb; gens come on.
- [ ] **Fuel page** pump/valve states (if Part 2 fuel layer is added) react to the fuel panel.
- [ ] **Reverser** state stays stowed (becomes testable only after landing — see below).
- [ ] **Doors**: each passenger/cargo door announces Open/Closed correctly as you work the flyPad;
      crew-oxygen line present. *(A320: after the doors→read-only port.)*

## Taxi / take-off roll
- [ ] **Brake temps** (WHEEL page) respond after taxi braking.
- [ ] **EWD memos** sequence correctly (T.O memo, etc.).
- [ ] **FMA** take-off modes announce (MAN TOGA/FLX, SRS, RWY) — confirms the FMA decode under load.
- [ ] **V-speeds** on the speed tape (V1/VR/V2) — currently registered but unrendered; only
      meaningful airborne with FAC-computed values (Part 2 §PFD-1c).

## Climb / cruise
- [ ] **Transition LEVEL** (`PFD_TRANS_LVL`) decodes to "flight level N" and matches the FCU as you
      climb through it. *(A320: after the trans-LVL port.)*
- [ ] **FCU selected ALT/HDG** readout tracks the FCU as you dial it in the climb. *(A320: ported.)*
- [ ] **SAT/TAT** (`PFD_SAT/TAT`) read sane outside-air temps at altitude. *(A320: ported.)*
- [ ] **ND GS / TAS / wind** read correctly (these are in the ND form; confirm they're sane and, if
      added to the monitored panel, auto-announce). *(A320: nav-radio freqs ported; GS/TAS/wind n/a
      unless added.)*
- [ ] **Nav-radio idents/freqs** (VOR/ADF/ILS) populate once you're in range of a station and have
      it tuned (ground-cold they're blank). *(A320: VOR/ADF freqs ported.)*
- [ ] **Fuel transfer / trim-tank** pump and valve activity in cruise (if Part 2 fuel layer added).
- [ ] **FMS predictions — ETA / EFOB at destination** (`A32NX_DESTINATION_*` / `flightInfo()` JS):
      these are **null on the ground** and only populate in flight. Still NOT implemented (needs a
      JS-agent change AND a flight) — see Part 2 §B and the parity doc §F.

## Descent / approach
- [ ] **EWD IDLE memo** — only arms in FMGC flight-phase ≥4 (descent). Confirm it appears when ≥3
      engines are at/near idle in descent and clears otherwise. *(A320: after the IDLE memo port.)*
- [ ] **Beta-target** (sideslip target) — confirm the blue β-target shows/decodes only when
      `A32NX_BETA_TARGET_ACTIVE`=1 (engine-out / large sideslip), and the sign/side is right.
- [ ] **TCAS RA VS band** — needs live conflicting traffic to trigger an RA; confirm the
      green/red vertical-speed band decodes ("climb to N fpm" / "no advisory").
- [ ] **Autoland capability** (LAND2 / LAND3 SINGLE / LAND3 DUAL / APPR1) — only shown on approach
      with the ILS captured; currently NOT surfaced (Part 2 §PFD-1a, FCDC discrete words).
- [ ] **Transition LEVEL** again on descent (passing it the other way).
- [ ] **PFD BC3 messages** (DECELERATE, DISCONNECT AP FOR LDG, etc.) — approach-only.

## Landing / rollout
- [ ] **BTV** (brake-to-vacate) rollout distance cadence updates during the landing roll.
- [ ] **ROW/ROP** (runway-overrun warning/protection) callouts — only fire on a real landing,
      especially a short/contaminated runway.
- [ ] **Reverser deployed** (engines 2 & 3 — A380 inboards) on the rollout (Part 2 §EWD if added).
- [ ] **Brake temps + brake fan** climb after a heavy stop; fan state if added.
- [ ] **Spoilers/ground-spoilers** deploy on touchdown (F/CTL page).

## After landing / shutdown
- [ ] **Gear page** transit/locked nuance in-flight (gear up/down cycles — only testable airborne).
- [ ] **Clock**: chronometer + ET already verified working live (CHR advances on toggle; ET advances
      once its switch is at Run). Re-confirm opportunistically; no known issue.

## A320-specific (separate from the parity port)
- [ ] **SimConnect connect check** — load the A32NX and confirm a clean connect: `registration.log`
      shows FULLY CONNECTED with `approxTotalDefs` well under 1000. (This just needs the A32NX
      loaded — NOT a flight — but it's the gate for the whole parity sweep in
      `a32nx-feature-parity-todo.md`.)
- [ ] Every parity item ported from the A380 inherits the matching in-flight check above once done.

---

# PART 2 — Addable-items backlog (report only — NOT implemented)

Source: the 2026-06-03 exhaustive three-agent sweep. Feasibility legend:
- **READY** = plain L:var/SimVar; register + format, no decode.
- **ARINC** = ARINC429 word; decode with `Arinc429Word` (numeric or bit).
- **string** = FBW `'string'` SimVar; needs the Coherent-debugger string path, not numeric SimConnect.
- **scrape-only** = no SimVar; exists only as rendered DOM / EventBus (Coherent scrape).
- **WIP** = present in FBW source but hardcoded/stubbed in this dev build → placeholder or skip.

> **Two correctness gaps to fix regardless of new features:**
> - **`_B1`-only shortcut** (PRESS + COND pages) and **SEC_1-only** (F/CTL rudder-trim source): FBW
>   picks the first NormalOp CPCS/CPIOM/SEC source (B1→B2→B3→B4 / SEC1↔SEC3). MSFSBA reads only the
>   B1/SEC1 word, so on a B1/SEC1 failure it shows "not available" while the real ECAM is still
>   valid on B2–B4. Should read all and pick the first NormalOp.
> - **`d["PFD"]` is mostly registered-but-unrendered**: ~45 PFD vars are fetched/cached but
>   `PFDForm.cs:FormatPFDData()` only prints FMA/approach/AP/messages. Rendering the rest (V-speeds,
>   Mach, track, RA, SAT/TAT, ILS, TCAS-VSI, FCU sel ALT/HDG, GW/CG, trans alt/lvl) is **READY** —
>   the data is already there. This is the single biggest low-effort win.

## A. Highest-value, lowest-effort (all READY plain vars unless noted)
1. **Render the already-cached `d["PFD"]` vars in PFDForm.cs** (V-speeds, Mach, track, RA, SAT/TAT,
   ILS freq/DME/course, TCAS VSI band, FCU sel ALT/HDG, GW/CG, trans alt/lvl). Data already cached.
2. **FUEL page system layer** — engine LP valves (`FUELSYSTEM VALVE OPEN:1-4`), crossfeed
   (`:46-49`), jettison L/R + JETTISON memo (`:57,58`), emergency-transfer (`:52,53`), trim-tank
   inlet/isolation (`:43,44,45,59`), per-pump switch-OFF (`CIRCUIT CONNECTION ON:…`), feed-tank LOW
   (<1375 kg). Pump-running states are ARINC bits (`A32NX_FQMS_{LEFT,RIGHT}_FUEL_PUMP_RUNNING_WORD`).
3. **ELEC contactors + battery detail** — gen line-contactors (`…CONTACTOR_990XU{1-4}…`), gen-OFF
   state (`GENERAL ENG MASTER ALTERNATOR:n`), battery OFF-PB (`…BAT_*_PB_IS_AUTO`), battery
   charge/discharge direction (sign of `A32NX_ELEC_BAT_n_CURRENT`, already read), TR-to-bus
   contactors, ESS-TR source label.
4. **CRUISE (SDv2 default) page fill-out** — per-engine + total FUEL USED (`A32NX_FUEL_USED:1-4` +
   APU), fwd/aft cargo temps, main/upper-deck min→max temps, landing elevation (ARINC), cabin
   VS/ALT AUTO/MAN flags.
5. **Controller channel-failure flags** (cheap, all bool READY) — COND TADD/VCM ch1/2, BLEED FDAC
   ch1/2, PRESS OCSM ch1/2.
6. **APU page** — AVAIL (`…APU_START_PB_IS_AVAILABLE`), FLAP MOVING/OPEN memo, master-sw-on gate.
   EGT-warning + low-fuel-press are ARINC.
7. **DOOR slide-armed "S" flags** per door; **HYD** per-pump section pressure-switch (LO) + fire
   shutoff valve open (all bool READY).

## B. Auto-announce situational-awareness readouts (mirror-readonly, READY)
- **TCAS mode/state** (TA-ONLY / TA-RA / STBY; RA corrective) — `A32NX_TCAS_MODE/_STATE/_TA_ONLY/_RA_CORRECTIVE`.
- **FWC flight phase** (1–12) — `A32NX_FWC_FLIGHT_PHASE` (context callout, e.g. "takeoff phase").
- **FMA reversion / protection** — `A32NX_FMA_MODE_REVERSION`, `_SPEED_PROTECTION_MODE`, `_SOFT_ALT_MODE`, `_TRIPLE_CLICK_MODE_REVERSION`.
- **Go-around** init speed / passed — `A32NX_GOAROUND_INIT_SPEED`, `_PASSED`.
- **FMS cruise altitude** — `A32NX_AIRLINER_CRUISE_ALTITUDE` / `_NEW_CRZ_ALT`.
- **FMS destination FOB** (predicted) — `A32NX_DESTINATION_FUEL_ON_BOARD` (in-flight; see Part 1).
- **RAT deployed** angle — `A32NX_RAT_ANGULAR_POSITION` ("RAT deployed" cue).
- **Cargo isolation valve** actual state/fault — `A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{FWD,BULK}_IS_ON`.

## C. Speed-tape FAC values (ARINC, one decode pattern) — PFD §1c
VMAX (`A32NX_FAC_1_V_MAX`), VLS (`_V_LS`), Valpha-prot (`_V_ALPHA_PROT`), Valpha-max
(`_V_ALPHA_LIM`), VSW (`_V_STALL_WARN`), VFE-next (`_V_FE_NEXT`), green-dot (`_V_MAN`), V3/V4
(`_V_3`/`_V_4`), speed trend (`_SPEED_TREND`). Managed/preselect speeds are READY
(`A32NX_SPEEDS_MANAGED_PFD`, `A32NX_SpeedPreselVal`, `A32NX_MachPreselVal`).

## D. FMA completeness (mostly READY) — PFD §1a
Thrust column (MAN TOGA/FLX+temp/MCT/SPEED/MACH/THR-modes/A.FLOOR/TOGA-LK via `A32NX_AUTOTHRUST_MODE`
+ `A32NX_AIRLINER_TO_FLEX_TEMP`), speed/mach preselect cells, autobrake row, armed-vertical
ALT-CST/G-S-armed distinction, expedite/cruise-alt mode, selected V/S-FPA value in the active cell,
**autoland capability LAND2/LAND3 (FCDC discrete — ARINC/scrape)**, MDA/DH block (ARINC),
AP/FD/A-THR engage detail (`AUTOPILOT FLIGHT DIRECTOR ACTIVE:1/2`, `A32NX_AUTOTHRUST_STATUS`).

## E. PFD message line (BC3) + flags — PFD §1b/1d/1e
- BC3 messages: TCAS armed/RA-inhibited/TRK-FPA-deselected (`A32NX_AUTOPILOT_TCAS_MESSAGE_*`, READY),
  T/D REACHED + SET HOLD SPD (already have vars), BTV EXIT MISSED (`A32NX_BTV_EXIT_MISSED`, READY);
  USE MAN PITCH TRIM / DISCONNECT AP FOR LDG / MOVE THR LEVERS / FCU ALT ABOVE-BELOW A/C are
  scrape-only/derived. (Note: `A32NX_PFD_MSG_CHECK_SPEED_MODE` looks **dead on the A380** — no source var.)
- Attitude flags: ATT-fail (ARINC SSM), AoA (`INCIDENCE ALPHA`, READY), FPV/bird on/off
  (`A32NX_TRK_FPA_MODE_ACTIVE`, READY), sidestick priority/position (READY).
- **GPWS/EGPWS PFD text** (PULL UP / SINK RATE / TOO LOW GEAR / GLIDE SLOPE / TERRAIN — TAWS ARINC
  word bits). *Low marginal value for a blind pilot: the GPWS aural already fires.* Listed for completeness.

## F. ND additions — ND §2
- **Add GS/TAS/wind to the monitored `d["ND"]`** (they're in the ND form but not auto-announced) — ARINC.
- **TCAS message line** (TCAS / TA ONLY / STBY / fault) — `A32NX_TCAS_STATE/_FAULT` — READY.
- VOR/ADF/ILS needle source: ident (string) + freq/CRS/tuning-mode (READY) per needle.
- Cross-track error display formatted (RNP-aware) — `A32NX_FG_CROSS_TRACK_ERROR` + `A32NX_FMGC_L_RNP` — READY.
- Selected-heading bug value, track/heading MAG-TRUE ref, ground track — READY.
- **FM message line** (GPS PRIMARY/LOST, NAV ACCUR DOWNGRAD, etc.), **WXR messages**, **ND chrono**,
  **RWY-AHEAD QFU runway designator** — scrape-only (the ND Coherent view; partly in the F6 "Live
  scrape" form already). Note: **ANP is not published by FBW** (only RNP) — don't chase.

## G. ISIS (minor) — §3
ATT-10S realign flag (scrape/READY if a flag var exists); the ISIS **speed bugs** the pilot sets
(`A32NX_ISIS_BUGS_ACTIVE` is registered in `d["ISIS"]` but the form never reads/renders the bug
values). Low value.

## H. SD per-page detail (the long tail) — see the full EWD/SD report below
Per-page extras worth a mention beyond §A: BLEED ground-supply indication (ARINC), bleed valve
amber-fault (WIP), ram-air-to-cabin (READY); COND bulk-heater + hot-air-disagree (ARINC bits);
PRESS cabin-VS-target donut + separate VS-AUTO/MAN + manual backup alt/VS/ΔP (READY/ARINC); WHEEL
NWS power-source + down-locked-per-system nuance + brake>300° amber (READY, low value); F/CTL
per-surface actuator power-source + surface-FAILED flags (READY but high effort, many surfaces) +
rudder-trim SEC-source selection (ARINC bit). EWD: reverser-deployed (eng 2/3), A.FLOOR/THR-LK
active, throttle-position donut (`A32NX_AUTOTHRUST_TLA_N1:n`) — all READY.

## I. flyPad / MFD / OANS — §4
Mostly already scraped. The one **systematic** gap: **MFD FMS PERF + FUEL&LOAD computed results**
(V1/VR/V2, THR-RED/ACC alts, FLEX/derated, CI, ZFW/ZFWCG, block/trip/reserve fuel, GW/CG
predictions) — these are NOT SimVars on the A380; they need a dedicated PERF/FUEL&LOAD **page
scrape-and-decode**. Highest-value remaining MFD work. Smaller scrape gaps: POSITION/MONITOR, IRS,
NAVAIDS, GNSS, DATA→STATUS (nav DB cycle), FCU-BKUP page, SURV/ADS-B detail, MFD scratchpad messages.

## J. Pedestal/overhead controls — §A of the L-var sweep
- **RMP** (radio panel) is half-covered: add the remaining transmit-channel selects
  (`A380X_RMP_{n}_{HF,TEL,INT,CAB,PA,NAV}_TX_1`, `_VHF_TX_2/3`), RX volume **level** knobs
  (`_VHF_VOL_1/2`), NAV-source/INT-RAD/VOICE selectors — all READY; keypad buttons need H-events.
  RMP-3 (overhead) audio likewise.
- **APU auto-exit test** (`A32NX_APU_AUTOEXITING_TEST_ON`), **emergency-gen test**
  (`A32NX_EMERELECPWR_GEN_TEST`) — niche overhead pushbuttons, READY.
- **Comfort openables** (fwd sunshades, seats, armrests, meal tables, keyboards, manual cockpit
  door anim, CAS/OIT panels) — all plain L:vars, READY but low value for a blind pilot.
- **Lighting preset load/save** (`A32NX_LIGHTING_PRESET_LOAD/_SAVE`) — the only cockpit-side light
  "control" in this build (lighting is otherwise flyPad-preset driven).

## K. Confirmed genuinely absent on this build — DO NOT chase
ENG nacelle temp, crew/cabin **oxygen PSI** (hardcoded 1829/1854), AVNCS/BULK cargo doors, all cargo
SMOKE/OVHT + trim-air duct "H", PRESS avncs/cab-air extract valves, WHEEL **tire pressure** (220) +
WING ACCU + A-SKID + BRK/STEER/LG CTL computers, FUEL collector cells, F/CTL SFCC computers, HYD
reservoir normal-filling band, **CIDS / cabin-lighting scenes / water-waste / service interphone**
(no L-vars — flyPad/EFB-modelled only), individual cockpit dome/flood light knobs, OIS/laptop power,
ASU/SATCOM/ACARS/DLS/printer (EFB-side), **ANP** on ND. The #104 toggle-event skips
(`…CABFANS_TOGGLE`, `…PACK1_TOGGLE`, `…RAMAIR_TOGGLE`) were re-checked and are correctly skipped
(duplicate of PBs already driven). KCCU keyboard ON/OFF intentionally left at sighted default.
