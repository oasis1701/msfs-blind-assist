# Live-flight audit checklist (A380 & A320)

Everything implemented for the A380 that can **only** be confirmed correct from an actual flight
(right phase / airborne / on a runway). Ground-cold verification is impossible for these, so they
shipped on source-reading + live var-reads + a clean connect, and are waiting on a flight to sign
off. Grouped by the phase where each becomes testable.

> **The "addable backlog" is retired (2026-06-03).** Every item from the former Part 2 survey was
> implemented this session (SD fuel/elec/hyd/channel-flags layer, EWD reverser + autothrust-message,
> PFD managed/preselect/expedite/FD/autobrake, ND GS/TAS/wind/XTE, RMP transmit selectors, APU/emer-gen
> tests, lighting preset, B1→B4 CPCS + SEC1→SEC3 correctness fixes, TCAS/FMA auto-announces). The few
> genuinely **not-modelled-on-this-FBW-build** items are recorded in `CLAUDE.md` (do-not-chase), not
> here. New build connects FULLY CONNECTED at approxTotalDefs~602 (ceiling 1000), 0 capped.

## On the ground, engines running / APU start
- [ ] **EWD computed THR%** tracks the thrust levers (idle → ~100% at TOGA); **thrust-limit line** shows CLB/FLX/TOGA.
- [ ] **EWD autothrust message** (THR LK / LVR TOGA / LVR CLB / LVR MCT / LVR ASYM) appears in the right modes.
- [ ] **APU page**: AVAIL + master-switch + FLAP MOVING/OPEN sequence on APU start; gens/V/Hz/load populate.
- [ ] **FUEL page** new layer: engine LP / crossfeed / jettison valves; feed/transfer/trim **pump-running** states react to the fuel panel & engine start.
- [ ] **ELEC page** gen line-contactors (990XU1-4), TR contactors, battery charging/discharging direction, gen-OFF.
- [ ] **HYD page** per-pump section-pressure-switch (pressurised/low) + fire-valve + elec-pump OFF-PB react to the HYD panel.
- [ ] **Doors** + **escape slides armed** announce correctly via the flyPad.
- [ ] **RMP transmit selectors** (VHF/HF/TEL/INT/CAB/PA/NAV, both ACPs): selecting a TX channel sticks; confirm it keys the right radio.
- [ ] **APU auto-exit test** / **emergency-gen test** / **lighting preset load/save** buttons fire (and don't error).

## Taxi / take-off roll
- [ ] **Brake temps** (WHEEL) rise after taxi braking.
- [ ] **FMA** take-off modes + **flight directors 1/2** + **autobrake** (RTO) read correctly under load.
- [ ] **Speed-tape FAC V-speeds** (VMAX/VLS/VSW/Valpha/VFE-next/green-dot/V3/V4) populate once FAC computes (airborne).

## Climb / cruise
- [ ] **Transition LEVEL** decodes "flight level N" and matches the FCU through the transition.
- [ ] **FCU selected ALT/HDG** + **selected V/S** + **expedite mode** track the FCU.
- [ ] **Managed / preselected speed + Mach** show on the PFD box once set in the MCDU PERF page.
- [ ] **SAT/TAT** read sane at altitude.
- [ ] **ND ground speed / TAS / wind / heading-reference** (monitored box) decode once the ADIRS aligns + moving (NCD on ground).
- [ ] **Cross-track error** shows magnitude + left/right of track.
- [ ] **Nav-radio idents/freqs** (VOR/ADF/ILS) populate in range.
- [ ] **CRUISE page** fuel-used per engine + cargo/deck temps + landing elevation.
- [ ] **FMS ETA/EFOB at destination** — still NOT implemented (null on ground; needs a JS-agent change + a flight). The only deferred display item; recorded in `CLAUDE.md`.

## Descent / approach
- [ ] **EWD IDLE memo** arms in FMGC phase ≥4 with ≥3 engines near idle; clears otherwise.
- [ ] **Beta-target** shows/decodes only when active (engine-out / large sideslip), correct side.
- [ ] **TCAS RA VS band** ("fly to N fpm") on a live RA; **TCAS mode** auto-announce (standby/TA-only/TA-RA) and **TCAS fault**.
- [ ] **FMA speed-protection** / **mode-reversion** auto-announce when they trigger.
- [ ] **Autoland capability** (the FCDC LAND2/LAND3 indication) — NOT yet surfaced (FCDC discrete words); recorded in `CLAUDE.md`.

## Landing / rollout
- [ ] **BTV** predicted + rollout distances; **ROW/ROP** callouts on a real (esp. short) landing.
- [ ] **EWD reverser deployed** (engines 2 & 3 — A380 inboards) on the rollout.
- [ ] **Spoilers / ground-spoilers** deploy on touchdown (F/CTL).

## After landing / shutdown / general
- [ ] **Gear page** transit/locked nuance (airborne gear cycles).
- [ ] **Clock** chronometer + ET (already verified working live earlier).
- [ ] **B1→B4 CPCS** + **SEC1→SEC3 rudder-trim** best-source: only testable by failing B1/SEC1 (failures page) — confirm PRESS/CRUISE/rudder-trim still read off B2-B4/SEC3 instead of "not available".

## A320-specific
- [ ] **SimConnect connect check** — load the A32NX, confirm FULLY CONNECTED + `approxTotalDefs` well under 1000 (gate for the parity sweep in `a32nx-feature-parity-todo.md`).
- [ ] Every parity item ported from the A380 inherits the matching in-flight check above once done.
