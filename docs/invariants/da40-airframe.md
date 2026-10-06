# COWS DA40 airframe rules (electrical, lights, doors, breakers, controls, options) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/da40-airframe.md`, which Claude Code loads when it reads matching code. Background: [da40.md](../da40.md).
The text is verbatim from the CLAUDE.md invariants PR #242 added; where two bullets told one rule they sit together under one ID. A trailing "→ doc" pointer is the original's.

## DA40F-1

- **THE DA40 HAS TWO INDEPENDENT SETS OF CIRCUIT BREAKERS AND THEY DO NOT TALK TO EACH OTHER.** The aeroplane's own breakers are pure `L:CB_*` toggles (0 IN / 1 PULLED) — the model's breaker template writes nothing but that L:var, because its `K:2:ELECTRICAL_CIRCUIT_BREAKER_TOGGLE` version is COMMENTED OUT in `COWS_DA40NG_CircuitBreakers.xml` under the author's own note, "I cant firgure this shit out...". The SIM meanwhile keeps its own per-bus breakers (`A:CIRCUIT BREAKER PULLED:n` with `A:BUS LOOKUP INDEX` set, and `A:CIRCUIT CONNECTION ON:n`), which nothing in the aircraft ever touches. So a sim-level pulled breaker kills its circuit while `L:CB_*` still reads IN — measured live at VCBI: every one of the 34 `L:CB_*` read 0 (in) while `CIRCUIT BREAKER PULLED:18` (circuit 18, `Name:Starter`) read 1 and `CIRCUIT ON:18` read 0. **MSFSBA's breaker panel reads only the `L:CB_*` side, so it can honestly report "all breakers in" over a circuit that is actually dead.** When a system is unpowered and its `L:CB_*` says IN, read the sim's set before concluding the breaker is not the problem. → [da40.md](../da40.md)

- The DA40 has 34 circuit breakers across six panels, read from the model's own `CircuitBreakers.xml`; `(L:CB_XXX)` is 0 IN / 1 PULLED and they genuinely gate systems (pulling `CB_FLP` extinguishes `FLAP_LIGHT:1`, verified live). There is no Copilot breaker set. → [da40.md](../da40.md)

## DA40F-2

- **⚠️ `L:STATE_LIGHT_*` ARE SAVE-STATE MIRRORS, NOT LAMPS** - the model writes them FROM the light SimVar and restores them TO it, so they can never disagree with the switch except for the frame they take to catch up. A watcher built on them produced only timing artefacts ("Landing light lit with the switch OFF" a moment after switching it off). ⚠️ What actually gates a light is `CIRCUIT ON:n` - true only when switched on AND the breaker is in AND the bulb has not failed AND the bus is above 19 V (landing 14, ACL 26, position 27, taxi 28). Measured: breaker pulled, `LIGHT LANDING` STAYS 1 and `CIRCUIT ON:14` goes to 0 - which is also why pulling a breaker looks like it does nothing. Announce the fault in one short sentence and say NOTHING when it clears. → [da40.md](../da40.md)

## DA40F-3

- **⚠️ THE XLS's MASTER IS A SPLIT ROCKER — Battery Master and Alternator Master — and the alternator half had no control at all.** `DA40_ELEC_ALT_MASTER` binds `GENERAL ENG MASTER ALTERNATOR:1`, which is the NG's ENGINE master, so it is XLS-only. Both XLS halves apply the rocker's interlock from the state read BEFORE any toggle — NOT by replaying the switch's own click code (COWS_DA40_IN.xml `Bat_Master`/`ALT_Master`), which toggles first and re-reads after; a K: event does not change its SimVar within the same execution, so that branch never fires (measured live: ALT on from both-off left the battery off). All six combinations measured correct with the read-first form — battery OFF takes the alternator off, alternator ON brings the battery on — and go through `ExecuteCalculatorCodeUnique`, because the interlock moves a master without the pilot and the same pick can then be the next write in a row. Only `TOGGLE_ALTERNATOR1` moves it; `ALTERNATOR1_SET` is inert (measured). The NG keeps "Electric Master", its key's own detent name. → [da40.md](../da40.md)

## DA40F-4

- **⚠️ `TOGGLE_AIRCRAFT_EXIT` MUST GO THROUGH `ExecuteCalculatorCodeUnique`.** Opening a door and then closing it is the same calculator string twice running, MobiFlight coalesces byte-identical consecutive commands, and the CLOSE is dropped - so doors opened and would not shut, and doing a different door in between "fixed" it. Measured both ways on EXIT OPEN:2. → [da40.md](../da40.md)

- **A door is OPEN or CLOSED and must never announce a percentage** - `EXIT OPEN` sweeps as the door swings, so one action produced three announcements, two of them meaningless. Speak the state it comes to REST in, on a settle, and keep the percentage for the panel scan. Off the stop is open, which is what the aircraft's own DOOR OPEN warning means. → [da40.md](../da40.md)

- **The DA40's canopy has NO latch state to model** - the real aeroplane's two-stage latch is not simulated, and the only `LATCH` variables in the package are `INPUT_LATCH` (an elevator-animation input latch) and the FADEC ECU-fail latches. The four exits are stock `EXIT OPEN` percentages, and the aeroplane raises a **`DOOR OPEN` CAS message at WARNING severity** instead (verified live). ⚠️ `TOGGLE_AIRCRAFT_EXIT`'s parameter is one HIGHER than the SimVar index: parameter 3 opens `EXIT OPEN:2`. → [da40.md](../da40.md)

## DA40F-5

- DA40 rudder trim is NOT adjustable by anything: COWS writes `RUDDER TRIM PCT` every frame as a speed-dependent yaw compensation, so a `RUDDER_TRIM_SET` write is overwritten within a frame (verified live). → [da40.md](../da40.md)

## DA40F-6

- DA40 brakes FADE — above 400 C authority drops, by 760 C ninety percent is gone — and the aeroplane has no brake temperature gauge, so per-wheel temperature and fade must be on the scan. → [da40.md](../da40.md)

## DA40F-7

- **The DA40 Flight Controls panel must read SURFACE position (`ELEVATOR`/`AILERON`/`RUDDER POSITION`), never the model's `INPUT_ELEVATOR`/`INPUT_AILERON` stick vars** — the checklist's "Flight controls ... CHECKED" exists to catch a jammed or disconnected control, which moves the stick and not the surface, so reading the input back reports the check as passing in exactly the case it is for. The three readouts are Continuous + IsAnnounced (to reach the batch cache) and silenced via `SilentCachedReadouts`, ⚠️ which is keyed by VARIABLE KEY and never by SimVar name. → [da40.md](../da40.md)

- **⚠️ RETRACTED: "THE DA40's ELEVATOR IS 13.3 PERCENT AT NEUTRAL BY DESIGN" WAS NOT ESTABLISHED, AND THE FIX BUILT ON IT COULD ANNOUNCE "CENTRED" OVER A DEFLECTED SURFACE.** The model values are real (`ELEVATOR_ANG_TOT = stick + ELEVATOR_MATH_NEUTRAL`, `/30`, neutral measured live at 4 with the axis unbound) but the readout reads the STOCK `ELEVATOR POSITION` SimVar, and whether THAT follows the model chain was never checked. Measured later on an A380: `ELEVATOR POSITION` and `YOKE Y POSITION` came back IDENTICAL to every digit (0.09099859930574894, twice) while aileron read -1.3e-7 and rudder 6.3e-6 — the stock SimVar is a PASS-THROUGH OF THE AXIS, on an airframe with no such constant, so that 9 percent was the pilot's own off-centre elevator axis. ⚠️ THE TEST THAT SETTLES THIS FOR ANY AIRCRAFT IS `ELEVATOR POSITION` vs `YOKE Y POSITION`: identical means the SimVar is the raw stick and any offset is the pilot's hardware; different means the model contributes. Nothing is subtracted any more — the surface reads as it reads and the STICK is named beside it when they disagree (`DescribeElevatorStickAgreement`), which tells a jam (stick moves, surface does not) from an off-centre axis (both move together) instead of guessing. Never re-add a fixed bias to a surface readout: this codebase's own rule is that the panel reads the SURFACE precisely so a jammed or disconnected control is caught, and a constant subtracted from it defeats exactly that. → [da40.md](../da40.md)

## DA40F-8

- **The DA40 has NO computed take-off trim** - the trim wheel carries a physical T/O mark and the AFM gives no number for it, so there is nothing to calculate and nothing to add. The trim already reads as "centred" or "N degrees nose up/down" and there is a Centre-of-travel control. The autopilot cannot help: the GFC 700 trims only while engaged, and it may not be engaged below the AFM's minimum engagement height. → [da40.md](../da40.md)

## DA40F-9

- **⚠️ MSFSBA COULD NOT PUT FUEL IN THE DA40 AT ALL** - the Fuel System panel had the valve, wire, pumps and transfer pump (managing fuel) and nothing for HAVING any. There is no fuel panel in the cockpit to reproduce, so the honest model is the TRANSACTION: gallons per tank plus a fill-both button, typed in gallons and read back in the pilot's chosen unit. ⚠️ Ground only, engine off, and REFUSED otherwise - the second control on this aeroplane that refuses rather than reports. ⚠️ Gate on `SIM_ON_GROUND`, NOT `DA40_ECU_PRE_ON_GROUND`: same SimVar, but the ECU one is OnRequest so the cache is empty and the fallback would allow refuelling in the cruise. → [da40.md](../da40.md)

## DA40F-10

- **The COWS DA40 is built to cooperate with GSX** - the model sets `L:GSX_PARK` when on ground, parking brake set, ground speed ≤1 kt and both brake pedals ≥25%, which is the aircraft telling GSX it is chocked. MSFSBA's GSX side is aircraft-independent and already tracks refuelling in detail (including the crew prose). ⚠️ Which services GSX OFFERS a light single is GSX's decision - there is no DA40 profile in `%APPDATA%\Virtuali\Airplanes`, so it falls back to what it derives from aircraft.cfg. Answer that with GSX running, not by reading code. → [da40.md](../da40.md)

## DA40F-11

- **The COWS options list is VARIANT-SPECIFIC and was not.** The POH prints the same MFD Engine-page menu for both airframes and they differ by exactly two rows — "Engage Starter w/ Mixture" (`L:START_MIXTURE`) and "Priming Assist" (`L:ASSIST_PRIME`, which the Priming panel already owns) — so a variant-blind `BuildOptionVariables` was offering the NG two switches its aeroplane has not got. ⚠️ The mixture starter needs **the ignition at BOTH**: its gate is `INPUT_MIXTURE == 0` AND `STARTER_SWITCH == 3` or 4 AND `START_MIXTURE == 1` AND under 500 rpm, so with the key anywhere else it is a silent no-op that reads as a broken option. → [da40-xls-variables.md](../da40-xls-variables.md)

- **Both of the DA40's settings sets are writable through the display window, and neither is a SimVar** - the 9 COWS options live on the MFD Engine page menu (Ctrl+E, then Ctrl+Enter to toggle a row) and the 77 Working Title G1000 settings on Aux System Setup and the PFD softkeys (Shift+Enter for the cursor, Ctrl+Up/Down to open a list, Ctrl+Enter to accept). Both verified by round trip against the underlying variable. → [da40.md](../da40.md)

## DA40F-12

- **The GMA 1347 is the stock `ASOBO_AS1000_MID_Template` (`NO_AUX`, `NO_COM_3`, `NO_NAV_3`) and sits between the displays, on neither screen**, so the display windows cannot press it (DA40-22 does not apply). Push nodes exist for COM1 MIC, COM2 MIC, COM1, COM2, NAV1, NAV2, DME, ADF, MKR/MUTE, HI SENS and DISPLAY BACKUP; SPKR, PA, AUX and MAN SQ have none and PILOT has only its lamp, so they are not offered. Its input events take a VALUE: `AS1000_MID_NAV_1` 1 lights the key and sets `NAV SOUND:1`, 0 clears it, a second 1 does nothing (measured for every key, NG, 2026-10-06). MKR/MUTE moves `MARKER BEACON TEST MUTE`, never `MARKER SOUND`. The PFD's VOL knobs are set per radio with `COM1/2_VOLUME_SET` and `NAV1/2_VOLUME_SET_EX1` (percent). → [da40.md](../da40.md)

## DA40F-13

- **The COM audio controls are one system in the sim.** `AS1000_MID_COM_2 = 1` also set `COM RECEIVE ALL` (measured on the NG, 2026-10-06), `COM_RECEIVE_ALL_SET` moves `COM RECEIVE:2`, and a MIC selection moves the receive flags. MainForm's echo window suppresses only the control the pilot set, so the others' knock-on changes were spoken as if somebody else had made them (CORE-7). After MSFSBA writes one of the four, `IsComAudioSideEffect` keeps the other three silent for `ComAudioGraceMs` (2.5 s, outlasting the 1 s batch, DA40S-2), and their `RefreshControlWhenDefHandled` still moves their combos. A change from the cockpit or hardware, with no write of ours in the window, speaks. → [da40.md](../da40.md)
