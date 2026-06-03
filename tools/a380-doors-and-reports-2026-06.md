# A380 doors re-add + reports (2026-06-03)

This session: (1) re-applied the live-verified display decode fixes, (2) re-added the A380
doors (now connection-safe), (3) produced two REPORT-ONLY analyses the user asked for —
cross-aircraft impact of the SimConnect-ceiling strengthening, and scrapable/new additions.
Per the user's instruction, the cross-aircraft items are REPORTED, NOT modified.

---

## 1. Display decode fixes (committed 1a61b79)

Live-verified via MCP + source:
- SD APU N / APU N2 → ARINC429 decode (were read plain; raw ~1.1e9 word shown as "%").
- SD Landing elevation → ARINC429 decode + keep "not set (auto)".
- ELEC "AC ESS" bus: `A32NX_ELEC_AC_ESS_SCHED_BUS_IS_POWERED` typo → `_SHED_` (live: SHED=1, SCHED=0).
- PFD "FMGC L/DEV Request": `A32NX_FMGC_1_LDEV_REQUEST` doesn't exist → `A32NX_FMGC_L_LDEV_REQUEST`
  (fixed A380 def + shared PFDForm + A320 def for parity).
- ISIS Localizer dots /0.4 → /0.8 (LOC full-scale 0.8; GS 0.4). ISIS slip sign (FBW negates body-X).
- Deferred (need in-flight off-track state): `A32NX_FG_CROSS_TRACK_ERROR` decode, `TO_WPT_BEARING`
  radians-vs-degrees. Read 0 on ground; noted for in-flight verification.

## 2. A380 doors re-added (committed 8ee590e) — CONNECTION-SAFE, LIVE-VERIFIED

The earlier removal blamed "doors break detection". REAL root cause (since fixed): registering
the stock SimVar `INTERACTIVE POINT OPEN:n` (space + colon) through the L:var path. Registered
explicitly as `Type = SimVar` (the A320's proven pattern), with the strengthening's ~470-def
headroom, they are safe.

LIVE-VERIFIED exit map (MCP, `TOGGLE_AIRCRAFT_EXIT` param ↔ `INTERACTIVE POINT OPEN:n`, 1:1):
- Only the **10 MAIN-DECK doors (ip0..9)** respond to the event — toggling each 0..9 drove its
  interactive point 0→1. (Caveat learned live: firing 9 toggles in one frame DROPS some — test
  one at a time.)
- **Upper-deck (ip10..15) and cargo (ip16..17) do NOT respond** to `TOGGLE_AIRCRAFT_EXIT`
  (ip10 toggled alone stays 0; ip16 stays 0). Those are GSX/flyPad-ground only → not exposed.
- Index→door from `flight_model.cfg [INTERACTIVE POINTS]` (Z=front-to-back, X=port-negative):
  ip0/1 = Main Door 1 L/R … ip8/9 = Main Door 5 L/R.

Implementation (mirrors the A320 doors): 10 combos under Ground Services > Doors; state =
`INTERACTIVE POINT OPEN:n` (0..1 animation fraction); `TryGetDisplayOverride` → Open/Closed/N%;
`ProcessSimVarUpdate` announces once per transition (honours Ctrl+M mute); combo change fires
`TOGGLE_AIRCRAFT_EXIT:n`.

Connection verified live AFTER adding doors:
`individualDefs=504 batchCovered=571 (+10) approxTotalDefs~529, FULLY CONNECTED.`

BUILD GOTCHA (cost time this session): `dotnet build MSFSBlindAssist.csproj` (no platform) outputs
to `bin\Debug\` (AnyCPU); the RUNNABLE exe is `bin\x64\Debug\`. Always build with
`-p:Platform=x64` (or the .sln) or you launch a stale binary.

---

## 3. REPORT — cross-aircraft impact of the SimConnect-ceiling strengthening (NOT modified)

The strengthening (commit 72ced22) made batch-covered vars (Continuous + IsAnnounced +
!ExcludeFromBatch) SKIP their individual SimConnect data def — they're read from the
`lastVariableValues` cache the batch fills. `RequestVariable(name, forceUpdate:true)`
**early-returns / no-ops if the var is NOT in `variableDataDefinitions`** (SimConnectManager.cs
~3321) and the forceUpdate add happens AFTER that guard — so a FORCED on-demand read of a
batch-covered var now does nothing, and the batch only fires `SimVarUpdated` on a value CHANGE.

Per-aircraft verdict (traced in code):

- **FBW A32NX — AT-RISK.** The HDG/SPD/ALT FCU readout is a two-leg rendezvous in
  `ProcessSimVarUpdate`: it force-reads both the `_VALUE` leg (OnRequest → still works) AND the
  `_MANAGED` status leg. The status legs `A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED` /
  `_SPD_MACH_MANAGED` / `_LVL_CH_MANAGED` are Continuous+IsAnnounced → now batch-covered, no
  individual def → their force-read no-ops, the rendezvous never completes, **the readout goes
  silent** (triggered by the FCU Heading/Speed/Altitude windows on open + the readout hotkeys).
  **VS/FPA is SAFE** (both `_VS_FPA_VALUE` and `A32NX_TRK_FPA_MODE_ACTIVE` are OnRequest).
- **Fenix A320 — AT-RISK (worse).** Same rendezvous, but BOTH legs are Continuous+IsAnnounced
  (`N_FCU_HEADING/SPEED/ALTITUDE/VS` + `I_FCU_*_MANAGED` + `B_FCU_VERTICALSPEED_DASHED`), so all
  four FCU readout hotkeys can go silent in steady state. The Fenix SET path is SAFE (reads via
  `GetCachedVariableValue`, not a force-read).
- **PMDG 737 / 777 — SAFE.** FCU/state vars are `SimVarType.PMDGVar`, skipped entirely by
  `RegisterAllVariables` and read via the CDA broadcast — untouched.
- **HorizonSim 787 — SAFE.** Already marks its on-demand Continuous+IsAnnounced vars
  `ExcludeFromBatch = true` (the author hit this exact no-op before); the strengthening preserves
  that carve-out.

RECOMMENDED FIX (awaiting user go-ahead — NOT applied):
Mark the affected FCU rendezvous vars `ExcludeFromBatch = true` so they keep an individual def
(restoring pre-strengthening behavior; they then run a per-var SECOND subscription):
- A32NX: `A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED`, `_SPD_MACH_MANAGED`, `_LVL_CH_MANAGED`.
- Fenix: `N_FCU_HEADING`, `N_FCU_SPEED`, `N_FCU_ALTITUDE`, `N_FCU_VS`, `I_FCU_HEADING_MANAGED`,
  `I_FCU_SPEED_MANAGED`, `I_FCU_ALTITUDE_MANAGED`, `B_FCU_VERTICALSPEED_DASHED`
  (verify each is Continuous+IsAnnounced first).
Alternatively make the readout fall back to `GetCachedVariableValue` instead of relying on a
`SimVarUpdated` event. The A380 is unaffected (its FCU readouts use a different path / OnRequest).
Secondary same-root-cause spots to audit if touched: MainForm panel F5 Refresh force-read and the
delayed state-announce helper (both no-op for any batch-covered field — mostly mitigated because
`GetPanelDisplayVariables` strips announced 2-state vars and most fields are OnRequest).

---

## 4. REPORT — scrapable / new A380 additions (NOT added)

READY (SimVar/JS, low risk):
- SAT/TAT + ISA dev (`A32NX_ADIRS_ADR_1_STATIC/TOTAL_AIR_TEMPERATURE`, ARINC; SD footer shows them).
- Trim-tank fuel qty (`A32NX_FQMS_TRIM_TANK_QUANTITY`, ARINC kg, metric-toggle aware).
- Transition LEVEL in FL (`A32NX_FM1/2_TRANS_LVL`) — we have trans ALT but not LEVEL.
- Beta-target on approach (`A32NX_BETA_TARGET` + `_ACTIVE`).
- TCAS RA green/red VS band (`A32NX_TCAS_VSPEED_GREEN/_RED` + state) — "fly to X fpm", needs live RA.
- ETA + EFOB at dest via the EXISTING `flightInfo()` agent object:
  `fmcService.master.fmgc.getDestEFOB(true)` + `guidanceController.vnavDriver.getDestinationPrediction()`
  (`.estimatedFuelOnBoard` / `.secondsFromPresent`). Predictions null until airborne (verify in flight).
- FMGC flight-phase announce (`A32NX_FMGC_FLIGHT_PHASE` enum) — orientation cue.
- FCU selected ALT/HDG readout hotkey (values already cached by the FCU windows).
- Preselected QNH (`A380X_EFIS_L/R_BARO_PRESELECTED`).

SCRAPE-side (READY):
- SD permanent StatusArea footer block (TAT/SAT/ISA/GW/GWCG/ZULU) — `SDv2/StatusArea.tsx`, always
  rendered, not yet read (the SD path uses per-page `A380SdRows`, not the footer).
- F-PLN EFOB/T.WIND column toggle (`MfdFmsFpln.tsx` header button) — fold into `buildFplnLines`
  when that mode is selected (in-flight).
- flyPad Dispatch→Overview (W&B summary) + Ground→Payload (ZFW/CG/pax/cargo targets) — a
  column/grid-aware reading-order pass like the Dashboard fix (verify if jumbled).

SKIP — WIP / not modelled (verified):
- FCU BKUP panel (`fcubkup/*`) — FBW marks it 🟥 not-started; renders empty.
- Oxygen pressure PSI — hardcoded in `Oxygen.tsx`, no var.
- INOP-SYS / STATUS / LIMITATIONS list — channel open via `systems-host.fwsCore` but dev build
  leaves it empty even on failures.
- Cargo smoke test (`A32NX_FIRE_TEST_CARGO`) — DEAD in FBW (read by nothing; SD hardcodes false).
  Engine Fire Test DOES work. (This is the "edge case like the cargo smoke test" the user asked about.)

---

## 5. Display hybrid audit + "not available" verification (2026-06-03 session 2)

USER QUESTION: "can a hybrid be done — where a value with no SimVar is SCRAPED instead,
per-display, formatted properly?" ANSWER: **YES, feasible** — and the cleanest mechanism is
NOT DOM scraping but reading the value via the **Coherent debugger** (`coherentClient.
EvalForResultAsync`, the same hook the D/Shift+D distance uses), which can read STRING
simvars and JS-internal values that MSFSBA's numeric SimConnect pipeline cannot.

3 parallel agents audited every SD page (0-15), the Upper E/WD, PFD, ND, ISIS, AND every
panel status display. KEY CONCLUSION: **the hybrid is rarely needed** — almost everything
was already correctly decoded, genuinely not-modelled, or a wrong-var BUG (now fixed).

FIXED this session (confirmed-live wrong-var bugs — the "not available / garbage" the user saw):
- ELEC 247XP / 247PP buses: read non-existent `_AC_247XP_` / `_DC_247PP_` (static 0) →
  the real names drop the AC_/DC_ prefix (live: `A32NX_ELEC_247XP_BUS_IS_POWERED` = 1).
- PRESS cabin alt / VS / ΔP: read `MAN_CABIN_*` (manual-mode-only, static garbage in AUTO;
  live MAN_CABIN_ALTITUDE = -415 ft) → the CPIOM-B1 ARINC words `_CABIN_*_B1` (IsArinc429).
- APU N2: plain Read showed the raw ~12.8e9 ARINC word when running → IsArinc429 decode.
- F/CTL SD: added the missing rudder-trim row (`A32NX_SEC_1_RUDDER_ACTUAL_POSITION`).

CONFIRMED-GENUINE (no fix — correctly "not modelled" in the FBW dev build):
oil pressure (clamped ≤0), oxygen PSI (no var; hardcoded 1829/1854 in source), landing-elev
auto, minimums not-set, TO pitch-trim not-computed, GW-CG range guard, APU EGT no-data.

SCRAPE-HYBRID candidates (the ONLY genuine ones — all STRING simvars / JS-internal, no
numeric SimVar; read via the Coherent debugger eval): **ILS ident** (`NAV IDENT:3`),
**VOR 1/2 ident** (`NAV IDENT:1/2`), **ADF 1/2 ident** (`ADF IDENT:1/2`), **ND chrono**.
Plus SD oxygen PSI (1829/1854 hardcoded constant — low value). Passenger doors already scrape.
⚠️ On the A380 the PFD/ND are HOTKEY-readouts (no status box), so these idents need a NEW
readout home (a hotkey or appended to an existing one) — a design choice flagged for the user.

VAR-FIXABLE remaining (pure SimVar, no scrape — not yet done, need a readout home or are
completeness adds): PFD transition LEVEL (`A32NX_FM1_TRANS_LVL`, FL format), PFD FCU selected
ALT/HDG (stock `AUTOPILOT ALTITUDE LOCK VAR:3` / `HEADING LOCK DIR`), EWD computed THR% +
BLEED line + IDLE memo (replicate `thrustPercentFromN1`).

## 6. A320-parity notes (for when loaded into the A32NX)
- Doors: the A32NX already has working doors (Type=SimVar) — its exit indices/door count differ
  (re-map from its `flight_model.cfg`); don't copy the A380 map.
- The display decode fixes (FMGC LDEV name) were already applied to the A320 def + shared PFDForm
  this session.
- The cross-aircraft FCU regression fix above (if approved) is itself an A32NX change.
