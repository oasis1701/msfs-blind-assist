# Headwind A330 First Officer — in-sim test plan

Sim-facing behaviour cannot be unit-tested, so this is the verification model for
the A330 First Officer. Run it in ONE session with the A339X loaded.

**Why this plan is not optional.** Every divergence below was found by measuring the
installed A339X package against the A32NX's. That method proves *absence* reliably
and proves nothing about *function*: this airframe is itself the proof, because the
FlyByWire baro display words are present in its `fbw.wasm` and never reach
MSFSBlindAssist's cache at all — which is why `HeadwindA330Definition` reads the
stock Kohlsman altimeter instead. So "the package has it" is a necessary condition
that was checked, never a sufficient one.

## Part A — the five corrected divergences

Everything below needs a running sim and is deliberately gathered here so it can be
run in **one** session at the end, rather than piecemeal. Nothing in this list blocks
the code landing; each item is a claim static analysis could not settle.

| # | Claim to settle | How to tell |
|---|---|---|
| L1 | `CODE_POS_0`/`CODE_POS_2` fire on an external L:var write, not only on a cockpit click | Write `XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position` = 0 with the sign off. If the sign lights without the reconcile toggle, they fire. Either result is fine — it only tells us whether step 2 of the seat-belt fix is load-bearing or a no-op. |
| L2 | Seat belts: the FO reaches ON, and AUTO never fights it | Run the flow with the switch starting in AUTO. Sign goes on, cockpit switch moves to ON, and stays there for >2 s (two 500 ms AUTO ticks). |
| L3 | Nav & logo auto-ticks from the cockpit switch | Set nav/logo ON by hand in the cockpit, open the FO. `EPU_NAVLOGO` must tick without the FO writing anything. |
| L4 | `ECAM_PAGE_STS` opens STATUS, not CRUISE | Run the After Start flow's ECAM STS step; read the SD page. |
| L5 | Landing lights: state read tracks the real switch, both ways | Toggle the single A330 landing-light switch by hand; the FO item must follow ON and OFF. |
| L6 | `A32NX_SPEEDS_LANDING_CONF3` actually delivers | Set CONF 3 landing on the MFD PERF APPR page; confirm the value reaches the cache and the auto-flaps cap engages. |
| L7 | The FCU pushes and baro PULL/PUSH reach the A339X FCU | Spot-check the SPD/HDG/ALT managed pushes and the transition-altitude baro push. Package-present, never exercised. |
| L8 | Nothing the A320 FO does is silently swallowed on the A330 | Walk one full cold-and-dark → takeoff cycle and compare against the A32NX run. |
| L9 | The cockpit-lighting scene leaves the Captain's ceiling and map lights alone | Run the Preflight and Secure lighting scenes. Neither lamp may change, and `L:A339X_CEILING_LIGHT_CAPTAIN` / `L:A339X_MAP_LIGHT_CAPTAIN` must still agree with what the lamps are doing. |

Two items are pre-existing A320 questions this work surfaced but does not own —
recorded so they are not re-derived: four Event-typed keys (`SPOILERS_ARM_TOGGLE`,
`ENGINE_MODE_SELECTOR`, the two `FCU_EFIS_*_FD_PUSH`) have no `HandleUIVariableSet`
branch, so `ApplySilent` writes a nonexistent L:var of the event's own name and
reports success; and `EngageAp1`/`SetGear` on this executor have no callers
repo-wide. Both behave identically on the A320 and the A330.

## Part A results — live session 2026-08-30 (A339X on stand, engines running)

Driven through the MobiFlight calculator path against the live aircraft. Every state
change made below was restored. **Seven of nine settled; L1 and L5 came back DIFFERENT
from what the design assumed, and both corrections are recorded in the spec and in code.**

| # | Verdict | Evidence |
|---|---|---|
| L1 | **ANSWERED — opposite of the assumption.** `CODE_POS_*` fire on a **cockpit click only**. | Wrote position `2` with the sign lit: switch moved to OFF, lamp stayed **ON**. Firing `CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE` then took the lamp to OFF. So the reconcile toggle is **load-bearing**, not belt-and-braces — position alone changes nothing a pilot can hear. |
| L2 | **CONFIRMED.** AUTO actively drives the sign. | With the lamp off, setting position `1` (AUTO) — engines running, gear down — re-lit the sign by itself. That is what would undo a bare toggle, and why the position write must come first. |
| L3 | **CONFIRMED, both halves.** | Read: `A:LIGHT NAV` = 1 while `L:A32NX_LIGHTS_NAV_LOGO` = 0. Write: the shipped two-operand RPN drove both lights off and back on. |
| L4 | **CONFIRMED from the aircraft's own code.** `Status = 13`. | `sd.js` — the same bundle that consumes `A32NX_ECAM_SD_CURRENT_PAGE_INDEX` — defines `Eng 0, Bleed 1, Press 2, ElecAC 3, ElecDC 4, Hyd 5, Apu 6, Cond 7, Door 8, Wheel 9, Fctl 10, Fuel 11, Crz 12, Status 13, CB 14`. ⚠ A LIVE read is **not** a reliable check here: a written index is reclaimed by the aircraft's auto-SD logic within seconds (measured: 13 written, read back as 9/Wheel). Verify from the bundle, not the screen. |
| L5 | **CONFIRMED, and WORSE than documented.** | `L:LIGHTING_LANDING_2` read `0` with the lights **on** AND `0` with them **off** — it is frozen, never written by this airframe. Because the A320's `BT_LANDING_LT` accepts `0` as "ON", the unported profile does not merely fail to tick: it reports **"Landing lights: ON" permanently, including when they are off** — a false positive on a before-takeoff checklist. |
| L6 | **PARTIAL.** The L:var delivers. | `A32NX_SPEEDS_LANDING_CONF3` read `0.0` (a real value, not absent). Still to do: select CONF 3 on the MFD PERF APPR page and confirm the value flips and the flap cap engages. |
| L7 | **NOT RUN.** | Needs an airborne or taxi phase. |
| L8 | **NOT RUN.** | Needs a full cold-and-dark cycle. |
| L9 | **CONFIRMED, harm demonstrated.** | Pot 10 pairs with `L:A339X_CEILING_LIGHT_CAPTAIN` (both read 0). Writing pot 10 = `50` — the A320 scene's DimFlight value — lit the Captain's ceiling light while its own state var stayed `0`: a desync at a brightness the binary switch cannot produce. Restored to 0. |

### Method note

`LIGHT LANDING:n` and other indexed SimVars could not be read through the MCP's
`get_simvar` (it throws on the indexed path). Read them by RPN into a scratch L:var
instead — `(A:LIGHT LANDING:2, Bool) (>L:SCRATCH)` — then read the scratch var. Keep
calculator strings short; a long multi-read string fails with "negative count".

## Part B — regression walk

Fly one cold-and-dark to shutdown cycle with the First Officer window open. Every
flow phase must complete, and no checklist item may sit un-ticked with its switch
visibly in the commanded position.
