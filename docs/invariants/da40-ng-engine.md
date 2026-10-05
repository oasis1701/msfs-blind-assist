# COWS DA40-NG engine and fuel rules — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/da40-ng-engine.md`, which Claude Code loads when it reads matching code. Background: [da40.md](../da40.md).
The text is verbatim from the CLAUDE.md invariants PR #242 added; where two bullets told one rule they sit together under one ID. A trailing "→ doc" pointer is the original's.

## DA40E-1

- The ECU voter is INVERTED: 0 = ECU B, 1 = AUTO, 2 = ECU A (from the model's ANIMTIPs). → [da40.md](../da40.md)

## DA40E-2

- The NG fuel valve CANNOT leave MAIN until the safety wire is broken — the model forces `0 (>L:FUEL_SELECTOR)` on every position while `FUEL_SELECTOR_WIRE_CUT` is clear, so the write looks accepted and reads back 0. Both AFM uses of the valve need it broken: EMERGENCY aux feed, and OFF, which is the ENGINE FIRE procedure and really does clear `ENG ON FIRE:1`. → [da40.md](../da40.md)

## DA40E-3

- The NG fuel gauge SATURATES at 14 US gal (AFM 2.14.4) — measured live at 18.78 actual against 14.0 indicated. Never report the indication as a quantity without saying it is on the cap; the AFM requires a dipstick measurement above it. → [da40.md](../da40.md)

## DA40E-4

- The transfer pump SWITCH and whether the pump is TURNING are different facts, disagreeing for four reasons (main full, aux empty, breaker out, volts low) — report both. → [da40.md](../da40.md)

## DA40E-5

- **⚠️ THE NG's EMERGENCY FUEL POSITION THROWS FUEL OVERBOARD AND NOTHING SAID SO.** The POH: *"There is no sensor to stop the transfer of fuel. Fuel will be pushed overboard if the transfer isn't stopped."* The model agrees — at `FUEL_SELECTOR == 1` the cooling loop returns `FUEL_TEMP_ENG_FLOW:1` to the main tank every tick, clamped `19.5 min` (NG Logic 2048ff), and past that clamp the fuel is gone. The rate is `PROP RPM / 2300 * 45 / 3600` gallons per SECOND (≈0.75 gal/min at 2300 rpm, the POH's "around 0.7"). The contrast is the ELECTRIC transfer pump, which moves ~1 gal/min and stops itself at a sensor near 14 gal. `DA40_FUEL_XFER_EMERG` reports it and is never announced — a state the display can report is scanned for. ⚠️ The valve is a CONTROL, so it is on no display list and its own override never runs: the row's composer needs the position captured in `ProcessSimVarUpdate`. → [da40.md](../da40.md)

## DA40E-6

- `FADEC_ECUTEST_TIMER:1` is a per-stage governor WATCHDOG that resets between stages (4 latches a fault), NOT the test's elapsed time — never present it as one. → [da40.md](../da40.md)

## DA40E-7

- **The DA40's ECU test button has TWO functions on one control**, told apart by the engine master and the duration: 26 seconds with the master ON is the ECU test, 10 seconds with it OFF resets latched failures and charges the batteries. ⚠️ This is the ONE control on the aeroplane where a blocker is REFUSED rather than reported - with the master on, the same hold runs a different function entirely (and cycles the propeller), so doing it anyway would not be a degraded version of what the pilot asked for. → [da40.md](../da40.md)

## DA40E-8

- **A lamp a pilot is WAITING ON must announce, not just appear in a scan.** The DA40's glow annunciation was OnRequest, so a blind pilot had to sit on the Engine Start panel pressing F5 through the start; the AFM start is "master on, wait for glow to go out, crank". It is a described STATE, not a moving number, so making it Continuous + IsAnnounced does not breach the numeric-silence rule. ⚠️ Note the general split on this aeroplane: a light SWITCH announces, the light's `*_STATE` LAMP does not - they agree almost always and the interesting case is when they DISAGREE (pulled breaker, failed bulb), which is logic rather than a flag and is not written yet. → [da40.md](../da40.md)

## DA40E-9

- Take RPM from `PROP_RPM_SENS:1`, never `DISP_PROP_RPM` — the display var is quantised to 10 RPM. Generally: a `DISP_*` var is what the G1000 DRAWS, not what the sensor measured. → [da40.md](../da40.md)

- **⚠️ THAT RULE'S SECOND SENTENCE IS RIGHT AND ITS FIRST IS A TRAP EVERYWHERE ELSE: `DISP_*` IS THE INDICATION, AND THE INDICATION FAILS.** COWS's own `Documentation/Failures.txt` lists the engine-indication failures as **`FAILURES_DISP_*`** — "Loss of oil temperature indication" fails `DISP_OT` specifically — so `DISP_` is what the pilot READS, not merely what the screen draws. Injected live: `FAILURES_DISP_OT = 1` took `DISP_OT` from 87.59 to **0** while `WC_TEMP_OIL_SENS:1` still read 86.70, and `FAILURES_DISP_VOLT = 1` took `DISP_VOLTS` 28.14 → 0. So reading the physics (`WC_TEMP_*`) or the sensor (`*_SENS`) on a panel shows a blind pilot a perfect temperature off a DEAD GAUGE, defeating a failure class COWS deliberately modelled and handing them something the sighted pilot cannot have. **THE LINE: a quantity WITH an indication is read from the INDICATION so it fails together; a quantity with NO indication at all (block temperature, radiator temperature, the thermostat, damage, health) may be exposed from the model, because there is no indication to defeat** — the same judgement already made for engine damage and health. `DISP_` also LAGS (87.59 drawn against 86.70 computed on a cooling engine) and that is the gauge's own needle dynamics, which a sighted pilot reads too. ⚠️ The RPM exception survives ONLY on the NG, by luck: `FAILURES_DISP_RPM` does not exist there (it is in the XLS-flavoured failure list with MAP, CHT and EGT), so on the **XLS** that same rule WOULD read through a failed RPM indication and must be re-decided rather than inherited. → [da40.md](../da40.md)

## DA40E-10

- The NG power lever commands LOAD and its commanded RPM is NON-MONOTONIC (2150 at idle, 1800 at 20 %, 2300 at full) — always report commanded RPM beside actual, or "disc mode" is invisible. → [da40.md](../da40.md)

- **The DA40 power lever is a real 0-100 percent and is clamped** (`Math.Clamp(value, 0, 100)`), so a bogus typed entry saturates rather than doing anything strange. It commands LOAD, not RPM, and below 20 percent the engine is in disc mode - high RPM, high drag. ⚠️ MAX POWER is the one figure that changes with altitude and temperature, and it lives in a table in a SCANNED PDF with no text layer - read it off the image and have a human check the numbers before building on them. → [da40.md](../da40.md)

## DA40E-11

- **⚠️ BELOW −10 °C THE NG's FULL-POWER RPM IS HELD AT 2100 AND IT LOOKS LIKE A GOVERNOR FAULT.** The model gates the whole 92-to-100 % branch on `(A:AMBIENT TEMPERATURE, celsius) -10 >` and otherwise writes a flat 2100 (NG Logic 176–184), so full power on a cold morning reads 200 rpm low. ⚠️ The POH's TEXT says minus ten while its own chart bubble says ten — **the MODEL settles it at minus ten**, and that is the general rule when the two disagree. The rest of the curve is exact and worth stating: 0 % → 2150, 20 % → 1800, 92 % → 2100, 100 % → 2300, linear between. → [da40.md](../da40.md)

## DA40E-12

- **The DA40 NG start key is `L:STARTER_SPAD:1` (1/0); `K:SET_STARTER1_HELD` is INERT and `L:STARTER_SWITCH` is a READ-ONLY MIRROR.** Measured live: writing STARTER_SPAD:1 = 1 moves STARTER_SWITCH to 2 (key in START), while firing SET_STARTER1_HELD down the same calculator path leaves it at 1 — MSFSBA shipped the event and the Start Key button had never once cranked. STARTER_SWITCH must never be written: the model recomputes it every frame from the electric master and the start input, so a write does not even stick (0 was written and came straight back to 1) — the same mirror rule as `STATE_*`, without the prefix to warn you. The vendor `DA40 LVAR bindings.txt` names STARTER_SPAD as the input; the event was an assumption. → [da40.md](../da40.md)

## DA40E-13

- **⚠️ THE DA40's INDUCTION FILTER BLOCKS WITH ICE AND NOTHING SAID SO** - which is the entire reason the alternate air control exists. The model's condition: ice accreting AND relative wind ≥60 kt AND precipitation >5 mm/h AND ALTERNATE AIR CLOSED builds `FILTER_RESRTICTION` (their spelling) to 100, clearing only when the OAT reaches zero. Not resettable - the fix is opening alternate air BEFORE it happens. Airframe ice already announced via the generic `STRUCTURAL ICE PCT` tracker; only this was missing. → [da40.md](../da40.md)

## DA40E-14

- **⚠️ THE DA40 ACCUMULATES ENGINE DAMAGE AND IT SURVIVES A RELOAD.** `DAMAGE_BLOCK`, `DAMAGE_OIL`, `DAMAGE_TURBO` (+friction/heat), `DAMAGE_FUEL` (+cuts/oscillation), dust and overstress, published as `HEALTH_*` fractions from 1.0 down. MSFSBA had the enable SWITCH and the reset BUTTON and nothing about the state between them, so a pilot could fly a damaged engine and never find out. Exposed as percentages, and a FALL of 5 points or more is announced - damage happens during something, and the moment it starts is when the pilot can still act. `HEALTH_FUEL` has indices 1, 11 and 12; only :1 is exposed because the package never says what the others track. → [da40.md](../da40.md)

- **⚠️ THE DA40's ENGINE-HEALTH READINGS ARE NOT ALL ON ONE SCALE, AND READING THEM AS IF THEY WERE SHIPPED A MISLEADING NUMBER.** The model computes `HEALTH_BLOCK = 1 - DAMAGE_BLOCK/800` but `HEALTH_OIL`/`HEALTH_FUEL = (100 - DAMAGE)/100`, with damage capped at 100 — so a COMPLETELY DESTROYED BLOCK PUBLISHES 0.875, and "engine block 88 percent" described an engine the model could not damage further. Rescaled in both the call-out and the panel row. ⚠️ `HEALTH_FUEL:11`/`:12` are the two FUEL PUMPS (they multiply circuits 42/43 into `FUEL_PRESS`), settled from the model rather than left unexposed. Health is what the physics multiply through: block → power, friction and heat; oil → oil pressure; pumps → fuel pressure; `HEALTH_FUEL:1` below 0.01 stops combustion. ⚠️ Past 90 damage is SELF-SUSTAINING (`FAILURES_x OR DAMAGE_x >= 90`), an overheating block is damaged directly with no failure involved (`WC_TEMP_BLOCK >= 140`), and oil damage IGNORES the `DAMAGE_ENABLED` switch every other accumulator obeys. `L:RESET_DAMAGE` is the whole repair — there is no partial one. → [da40.md](../da40.md)
