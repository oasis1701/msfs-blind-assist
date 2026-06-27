# A380 "finish-line" pass — 2026-06 (overnight autonomous)

Goal: make the FBW A380X a finished product — every display complete, every panel
audited, high/low-value controls added, all live-verified, code cleaned/hardened.
Doors are intentionally EXCLUDED this pass (noted as a possible A320-parity task).
Each item below was: implemented → built (0 errors) → every new SimVar/L:var
live-verified via SimConnect MCP against a running A380X → committed (NOT pushed).

Method: 5 parallel source-grounded audit agents (one per display) produced the gap
lists; a 5-agent panel-by-panel sweep (Overhead/Pedestal/Glareshield/Instrument/Ground,
FCU excluded) produced the control gaps. All var names cited back to FBW A380 source
(`fbw-aircraft/fbw-a380x/src/systems/instruments/src`).

## ✅ STATUS — RESOLVED: architecture strengthened, ALL additions re-applied + live-verified

The additions were briefly reverted (`2935c3b`) after they hit the registration ceiling, then
the **registration architecture was strengthened** (`72ced22`) and **all the additions
re-applied on top** (`60c5a07`). Live-verified on a running A380X:
`[Registration] individualDefs=505 batchCovered=561 ~530 total defs` + `[Detection] FULLY
CONNECTED — 'FlyByWire A380X'`. The A380 now uses ~530 of the 1000 SimConnect data-definition
budget (was ~1083 and overflowing) — ~470 vars of headroom.

**The ceiling was SimConnect's documented 1000-data-definition limit (NOT MobiFlight).** The fix:
(1) stop double-registering continuous vars (read them from the batch cache; ~1083→~530 defs);
(2) register bulk vars LAST so detection can't be stranded by an overflow; (3) a 900-def cap +
persistent `logs/registration.log` (footprint, "FULLY CONNECTED", "[CEILING]"). See CLAUDE.md
"THE SIMCONNECT DATA-DEFINITION CEILING" for the full writeup. **A320 still needs a live connect
check** (shared `SimConnectManager`).

---

### (historical) Why it was reverted first

**Real root cause (proven via live SimConnect-exception logging):** the A380 was already at
**MobiFlight WASM's variable-table ceiling**. Baseline = **1015** registered vars → connects
cleanly (0 SimConnect exceptions). The pass pushed it to **1066** vars; the 2nd
continuous-monitoring batch's `AddToDataDefinition` then failed wholesale
(`SIMCONNECT_EXCEPTION_TOO_MANY_OBJECTS` + `UNRECOGNIZED_ID`). That async exception disrupts
the one-shot AIRCRAFT_INFO/ATC response → `IsFullyConnected` never sets → "MSFS detected but
every hotkey says not connected" (auto-announce is a separate path, so it kept working).

How it was found: instrumented `RegisterAllVariables` + `OnRecvException` to log
SendID→variable, launched MSFSBA against the live sim, read the exact failing ops.
Demonstrated baseline(1015)=clean, pass(1066)=BATCH2 fails. Demoting the new controls to
OnRequest (continuous 561→528=baseline) did NOT fix it — it's the **total** var count, not
the continuous count.

**The earlier "fix" theories in the commit log (INDICATED ALTITUDE:3 / pushback names) were
WRONG** — those `NAME_UNRECOGNIZED` exceptions are pre-existing and harmless (present in the
working baseline too). The only thing that matters is the total var count vs the ceiling.

### To re-apply the work (the real plan)
The A380 needs HEADROOM before any net var additions. Options, best first:
1. **Eliminate the continuous-var double-registration.** Every Continuous+IsAnnounced var is
   registered TWICE — once as an individual data def (for on-demand reads) AND again inside a
   `CONTINUOUS_BATCH_n` def (for auto-announce). Make continuous vars read their on-demand
   value from the batch cache instead of an individual def, and skip the individual
   registration for them. That frees ~528 MobiFlight slots → huge headroom. **Risk:** affects
   the shared `SimConnectManager` (A320 too); must verify on-demand reads of continuous vars
   still work (RequestVariable currently needs an individual def — line ~3284). Test live.
2. Trim ~50+ low-value EXISTING continuous vars to OnRequest to make room (regresses some
   existing auto-announce; judgment-heavy).
3. Re-apply additions in small batches, testing the connection (via the diagnostic below)
   after each, staying under the ceiling.

All the finish-pass code is in git history: commits `32b6a3a` (ISIS) … `3b578d6` (ACP),
`b8db3b8`/`89d2066` (docs). Cherry-pick once headroom exists.

### Diagnostic recipe (reusable)
Instrument `SimConnectManager.RegisterAllVariables` (capture `GetLastSentPacketID()` per
`AddToDataDefinition` into a `SendID→var` map) + `StartContinuousMonitoring` (same, per batch
add) + `OnRecvException` (write exception name/SendID/Index + mapped var to a log file).
Launch `MSFSBlindAssist.exe` against the running sim for ~12s, kill it, read the log. The
exact failing variable/operation is named. (This instrumentation was applied, used, and then
reverted with the baseline restore.)

---

## (Below: the additions that were made then reverted — kept for the re-apply effort)

Commits this pass (oldest→newest), all on top of `b8db3b8`:
- `32b6a3a` ISIS: standby `:3` source + slip/skid + LS deviation
- `841190c` ND: range-array bug fix + GS/TAS/wind + TRUE REF + appr capability
- `7953b88` PFD: radio-alt/VS/trans-alt + speed bugs + marker + LS course
- `8971da8` SD: pressurization AUTO/MAN + starter-valve rows
- `f063a6a` EWD: thrust-limit N1 readout
- `80851b2` Panels: gravity guards, ISIS LS toggle, autobrake DECEL, ND-filter Off
- `51fa0eb` Panels: new Reset overhead panel, fire-agent discharge, pushback readouts
- `3b578d6` ACP: VHF3/HF1-2/TEL1-2 receive (both RMPs)
- `b8db3b8` docs: CLAUDE.md finish-pass record
- `bba7602` **FIX: re-broken aircraft detection** — the pass reintroduced the "not
  connected" bug via two stock-SimVar readouts whose NAMES aren't valid SimConnect
  data-definition vars: `INDICATED ALTITUDE:3` (INDICATED ALTITUDE isn't indexable) and
  `PUSHBACK STATE`/`PUSHBACK ATTACHED`. `AddToDataDefinition` is async so the bad name
  doesn't throw in the per-var try/catch — SimConnect raises an exception later that
  disrupts the one-shot AIRCRAFT_INFO/ATC response → detection never completes. Reverted
  ISIS to the **non-indexed** altitude/baro (like the proven A320 ISIS; kept slip/skid +
  LS deviation) and dropped the pushback readouts. **NEW RULE: a stock SimVar must be a
  real data-definition name, and an indexed one (`NAME:n`) only works if the base simvar
  is actually indexable (KOHLSMAN:n ✓, INDICATED ALTITUDE:n ✗) — verify before adding.**

**⚠️ Recommended before relying on it:** start MSFSBA against the A380 and confirm it
announces the aircraft + hotkeys work (detection sanity), then spot-check a new panel
(e.g. the Reset panel) and a display (ND velocities). All vars were verified to READ via
SimConnect MCP, but MSFSBA's own combo WRITE path for the new controls was confirmed only
by equivalence to existing proven controls + a live calc-path set/read/restore — not by
clicking each one in the running app.

---

## ISIS (standby instruments) — DONE

**Correctness fixes** (the form was reading the PFD/baro air-data source, not the
standby ADM the real ISIS uses):
- Standby **altitude** now reads `INDICATED ALTITUDE:3` (was the non-indexed PFD source).
- Standby **baro** now reads `KOHLSMAN SETTING MB:3`, shown in **both hPa and inHg**
  (the real ISIS shows the inHg pair). Registered with explicit units (millibars + inHg)
  because the generic auto-register forces "number" (which returns Pascals for Kohlsman).

**Additions** (high value, all live-verified):
- **Slip/skid ball** — `ACCELERATION BODY X` (G), clamped ±0.3 G → "% left/right / centred".
- **LS overlay deviations** — when `A32NX_ISIS_LS_ACTIVE`, surface LOC + G/S deviation in
  dots (÷0.4, full-scale 2 dots) gated by `A32NX_RADIO_RECEIVER_{LOC,GS}_IS_VALID`. Makes
  the existing "LS overlay: On" line actionable on a standby approach.

**Deferred (low value for a blind pilot, noted not done):** ISIS pilot-set SPD/ALT
reference bugs (`A32NX_ISIS_BUGS_*`, verified readable), metric-altitude / inHg-unit
persistent-property flags, and the display-unit power/self-test state.

Files: `Aircraft/FlyByWireA380Definition.cs` (ISIS Stock registrations),
`Forms/FBWA380/FBWA380ISISForm.cs` (Vars + Render + DotsPhrase helper).

---

## ND (Navigation Display) — DONE

**Bug fix:** ND range readout was wrong for the A380. `A32NX_EFIS_L_ND_RANGE` is an
INDEX into the A380 array `[-1(ZOOM),10,20,40,80,160,320,640]`, but the form used the
A320's 6-entry `10..320` array — so every range read one step high and 640/ZOOM were
impossible. Fixed to the 8-entry A380 array (index 0 = ZOOM).

**Additions** (all live-verified against the running A380X):
- **Ground speed** `A32NX_ADIRS_IR_1_GROUND_SPEED`, **True airspeed**
  `A32NX_ADIRS_ADR_1_TRUE_AIRSPEED`, **Wind** dir/speed
  `A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR` / `_WIND_SPEED_BNR` — all ARINC429 BNR words
  decoded via `Arinc429Word`; render "---" when not in Normal Operation (e.g. TAS/wind
  are No-Computed-Data on the ground).
- **TRUE REF awareness** `A32NX_FMGC_TRUE_REF` — shows "Reference: TRUE" and switches the
  TO-WPT bearing to `A32NX_EFIS_L_TO_WPT_TRUE_BEARING` (degrees) instead of the magnetic
  bearing when true-north reference is selected.
- **Approach capability** (e.g. "CAT3 DUAL") from `A32NX_EFIS_L_APPR_MSG_0/_1` (packed
  6-bit, decoded with the existing ident unpacker).

**Deferred (string-simvar limitation, documented):** the VOR1/2 + ADF1/2 nav-source
block needs `NAV IDENT:n` / `ADF IDENT:n` (string simvars) which MSFSBA's numeric
SimVar pipeline can't read; these idents remain available via the ND **F6 live scrape**.
TCAS message state and ROSE-VOR/ROSE-ILS tuned-station blocks also deferred (mode-specific,
lower value). RWY AHEAD already covered by ROW/ROP auto-announce.

Files: `Forms/FBWA380/FBWA380NavDisplayForm.cs` (NdRanges array + Vars + Render).

---

## PFD (Primary Flight Display) — DONE

Already present (kept through the doors/PFD revert): FMA modes/armed, autothrust,
approach capability, AP1/2, attitude/heading/IAS/altitude, PFD messages, GW + CG %MAC,
V1/VR/V2, Mach, Track, ILS freq/DME, and the FAC speed-protection bugs Vmax/VLS/
Vαprot/Vαmax/Vsw. **Additions this pass (all live-verified):**
- **Radio altitude** `A32NX_RA_1_RADIO_ALTITUDE` (ARINC429, feet) — live-verified Normal
  Operation. Highest-value missing number on short final.
- **Vertical speed** `A32NX_ADIRS_IR_1_VERTICAL_SPEED` — live testing revealed this is an
  ARINC429 word (the audit mis-called it "plain"); decoded ft/min, Normal Operation.
- **Transition altitude** `A32NX_FM1_TRANS_ALT` (ARINC429, feet; "not available" when unset).
- **Characteristic speed bugs** (FAC ARINC429, knots): green dot `_V_MAN`, F-speed `_V_3`,
  S-speed `_V_4`, VFE-next `_V_FE_NEXT`.
- **Marker beacon** `MARKER BEACON STATE` (stock enum → None/Outer/Middle/Inner).
- **ILS/LS course** `A32NX_FM_LS_COURSE` (plain L:var; -1 = no course set) — completes the
  ILS block alongside the existing freq/DME.

Used a new `ArincUnit(key,name,display,unit)` helper (RA/VS/trans-alt) mirroring the
existing `ArincKt`. All ARINC vars are OnRequest (no monitoring-batch / detection impact).

**Deferred (documented):** ILS ident (`NAV IDENT:3`, string simvar — numeric pipeline
can't read; via scrape), FCU selected ALT/HDG readouts (respecting "don't touch the FCU"),
transition level (needs FL formatting), LOC/GS deviation (already in ND + ISIS), DH (already
in Minimums), managed/preselect target speeds, FPV/FPA/drift, beta target, TCAS RA band.

Files: `Aircraft/FlyByWireA380Definition.cs` (registrations + d["PFD"] + LS-course decode).

---

## SD pages — DONE

The SD decode (`A380SdRows`) was already near-complete (pages 1/2/3/7/8/9/10/12 had no
gaps). Genuine additions this pass (live-verified):
- **PRESS page**: pressurization **AUTO/MAN mode** (`A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO`)
  — the page's prominent mode label.
- **ENGINE page**: per-engine **starter (cranking) valve** (`A32NX_PNEU_ENG_n_STARTER_VALVE_OPEN`)
  — open while motoring/starting; useful start context for a blind pilot.

Audit "bug fixes" that were **false positives** (verified, correctly NOT changed):
- *Pitch-trim* — the SD row's `ELEVATOR_TRIM` key is already registered to the stock
  `ELEVATOR TRIM POSITION` in **degrees** (def line ~1800); the audit only saw the key name.
- *Landing-elevation ARINC* — `A32NX_FM1_LANDING_ELEVATION` reads a plain `0.0` live (not the
  ~4.29e9 an ARINC NCD word shows), so the existing "not set (auto)" plain decode is correct.
- *EXT PWR available* — already surfaced as "GPU n Available" under Ground Services; not
  duplicated into the SD ELEC AC rows.

Files: `Aircraft/FlyByWireA380Definition.cs` (`A380SdRows` cases 0 + 4).

---

## EWD (Engine/Warning Display) — DONE

The EWD's numeric set is small and already well-covered: per-engine N1, **N1-command**
(`A32NX_AUTOTHRUST_N1_COMMANDED:n`), EGT, thrust-rating mode + FLEX, reverser-deployed
(eng 2/3), plus the live-scrape memos/warnings/status boxes. (N2/N3/FF are SD-ENG-page
data, not EWD, and are already exposed.) **Addition this pass (live-verified = 84.9%):**
- **Thrust limit N1 %** `A32NX_AUTOTHRUST_THRUST_LIMIT` — the green max-N1 the current
  thrust-rating mode allows (complements the already-announced limit TYPE).

**Deferred (documented):** the computed **THR %** gauge headline (derivable from N1 +
idle/max limits — N1 is already exposed), per-lever **TLA_N1** (≈ N1-command live, a
near-duplicate), reverser **deploying/selected** transitional states (deployed already
shown), the **BLEED supply** line (CPIOM AGS ARINC bits), and the **IDLE** memo. THR LK /
A.FLOOR are FG-EventBus-only → available via the EWD live scrape, not a SimVar.

Files: `Aircraft/FlyByWireA380Definition.cs` (autothrust readout + d["Thrust Levers"] + decode).

---

## Panel-by-panel audit (5 parallel agents) — controls/readouts added

Five category agents (Overhead, Pedestal, Glareshield-minus-FCU, Instrument, Ground)
audited every subpanel against FBW source. The panels were already heavily covered from
prior passes; the genuine source-confirmed gaps were added (write paths live set/read/
restore-verified where non-destructive):

**Instrument group** (commit 80851b2):
- Gear: gravity-extension guards as settable controls — master guard (promoted from
  readout) + left/right (`A32NX_LG_GRVTY_SWITCH_GUARD_1/2`). All three gate the gravity
  lever's DOWN position (FBW `CODE_POS_2_VERIF`) — without them DOWN was unreachable.
- ISIS: **LS** promoted from readout to settable toggle (ILS scales on standby).
- Autobrake: **DECEL light** readout (`A32NX_AUTOBRAKES_DECEL_LIGHT`), auto-announced.
- EFIS: **ND Filter "Off" (0)** option added so the WPT/VORD/NDB overlay can be deselected.

**Overhead + Fire + Ground** (this commit):
- **New "Reset" overhead panel** — the 10 latching computer-reset pushbuttons
  (`A32NX_RESET_PANEL_*`: FMC A/B/C, FWS 1/2, AESU 1/2, NSS AVNCS/FLT OPS, ARPT NAV).
  Live set/read/restore-verified. Entirely new (no reset panel existed before).
- **Fire panel: agent discharge buttons** — Engine 1-4 Agent 1/2 + APU agent
  (`A32NX_OVHD_FIRE_AGENT_*_IS_PRESSED`, momentary). Completes the fire drill (handle →
  discharge). NOT live-fired (pressing discharges the bottle); registered via the proven
  `Press` momentary path; squib readouts already in `d["Fire"]`.
- **Pushback readouts** — Tug Attached (`PUSHBACK ATTACHED`) + Pushback State
  (`PUSHBACK STATE`), registered as **stock SimVars** (space names → SimVar, never L:var).

**Deferred (documented, with source evidence in agent reports):**
- ACP RX switches (VHF3/HF1-2/TEL1-2 ×2 RMPs, `A380X_RMP_*_VOL_RX_SWITCH_*`) — clean +
  high-confidence, just not yet wired (audio routing, medium value).
- Pushback **Call/Release Tug** control — needs stock-SimVar (PUSHBACK STATE/WAIT) write
  plumbing + state-machine live testing; higher risk, deferred.
- ACP TX channels / NAV_SEL / INT_RAD (mirror-var + name-mismatch uncertainty), ND chrono
  push (no readback), ISIS bug editor, COCKPITDOOR_OPEN (you removed it earlier as "not
  needed"), GPU connect (unverified publish target).

Files: `Aircraft/FlyByWireA380Definition.cs` (GetVariables registrations, GetPanelStructure,
BuildPanelControls p["Reset"]/p["Gear"]/p["ISIS"]/p["Fire"], GetPanelDisplayVariables).
