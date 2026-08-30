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

## Part B — regression walk

Fly one cold-and-dark to shutdown cycle with the First Officer window open. Every
flow phase must complete, and no checklist item may sit un-ticked with its switch
visibly in the commanded position.
