# DA40-XLS — the variable surface

Companion to [da40.md](da40.md), which covers the NG. Two halves: the **names** (what exists
and how it groups, from the package XML) and the **measurements** (units, encodings and
behaviour, read live at EGNX through a cold start and a full run-up). Everything in the
measured sections was read or written against the running aircraft; nothing is inferred
from a name.

## Method — names

```bash
PKG="$LOCALAPPDATA/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache/Packages/Community/cows-da40/SimObjects/Airplanes"
ex(){ grep -rhoE 'L:[A-Za-z0-9_/]+(:[A-Za-z0-9]+)?' "$1" --include=*.xml | sed 's/^L://' | sort -u; }
ex "$PKG/COWS_DA40XLS" > xls.txt; ex "$PKG/COWS_DA40NG" > ng.txt
sed -E 's/:[A-Za-z0-9]+$//' xls.txt | sort -u > xlsb.txt
sed -E 's/:[A-Za-z0-9]+$//' ng.txt  | sort -u > ngb.txt
comm -23 xlsb.txt ngb.txt          # the 380 XLS-only base names
```

⚠️ **The character class must include `/`.** Sixteen real names carry a unit suffix after a
slash — `TB_FF_JET_PRESS_KG/HR`, `TB_FF_JET_PRESS_KG/HR_T`, `ENG_FUEL_SYSTEM_FLOW_TO_SERVO_G/S`,
`TB_MASS_FLOW_KG/HR` … — and a pattern that stops at the slash produces truncated names that
do not exist. Reading one returns 0 with no error, which is indistinguishable from a real
variable reading zero: an earlier pass "measured" `TB_FF_JET_THROTTLE_KG` at 0 while the real
`TB_FF_JET_THROTTLE_KG/HR` was 18.8. ⚠️ Nor may the pattern require a leading `(` — that
loses another 19 (`ASSIST_PRIME_PERCENT`, `DISP_MAP`, all four `DISP_LEAN_*`).

| | Full names | Base names (`:n` collapsed) |
|---|---|---|
| XLS total | 1414 | 999 |
| NG total | 1075 | 980 |
| Shared | 662 | 619 |
| **XLS-only** | **736** | **380** |
| NG-only | 413 | 361 |

Quote the 380. It counts the PACKAGE, not MSFSBA's profile — [da40.md](da40.md)'s "86 are
NG-only" counts `CowsDA40Definition`'s own definitions and is a different measurement.

## ⚠️ The live enumerator cannot list these

`msfs_list_lvars` (MobiFlight `MF.LVars.List`) caps at 1000 names, alphabetically, and still
reports the list complete. With other add-ons registered it ran out at `FAILURES_DISP_EGT`,
so `FUEL_*`, `MIXTURE_*`, `OC_*`, `PROP_*`, `STARTER_*`, `TB_*` — the whole engine — were
invisible, and `filter_prefix` filters the already-truncated reply (`filter_prefix=STARTER`
returned zero while `L:STARTER_SWITCH` read fine). Enumerate from the XML; read by name.

## The five XLS interaction components

`ENGINE_Lever_Mixture_1`  `ENGINE_Lever_Propeller_1`  `ENGINE_pedestal`  `FUEL_SELECTOR`  `STARTER`

## The controls — measured

| Control | Variable | Encoding / behaviour (measured) |
|---|---|---|
| **Magneto key** | `STARTER_SWITCH` | **0 OFF · 1 RIGHT · 2 LEFT · 3 BOTH · 4 START.** 1 and 2 proven by the firing map (`ENG_MAG_CYL:1R`=1/`:1L`=0 at 1, the reverse at 2). Writable, holds 0–3. **4 is momentary**: springs back to 3 within a second (`MOMENTARY_SWITCH`, `STATE_MAX_TIMER 1`). ⚠️ **Writing 4 does NOT crank** — six sustained writes produced nothing; the template's `CODE_POS_4` runs on a cockpit CLICK, never on an L:var write. |
| **Starter** | `STARTER_SPAD:1` | 1 engages, 0 releases; **holds, and cranks.** The vendor names both this and `STARTER_SWITCH` as bindings; this is the one that works from outside. The NG's read-only-mirror finding for `STARTER_SWITCH` does not apply here — but it doesn't matter, because SPAD is the input on both. |
| Throttle | `THROTTLE_LEVER` (read) · `K:THROTTLE1_SET` (write) | `THROTTLE_LEVER` is a **read-only mirror** = stock `GENERAL ENG THROTTLE LEVER POSITION` ÷ 100, no `STATE_` prefix to warn you; a write snaps back. Drive the stock event (0–16383) and read the stock position back. ⚠️ **Not linear and COWS trims it**: commanded 12 % read back 9.2 %; measured 9 % → 860 rpm, 30 % → 1450, 34 % → 1510, 38 % → 2190. Measure, never compute. |
| Mixture | `INPUT_MIXTURE` | 0–100, **writable, holds**. 100 = full rich = air/fuel 10:1 (`EGT_MIXTURE` 10.35). Stock mixture lever is COWS-owned (Logic 643 rewrites it) — never write it. |
| Propeller | `INPUT_PROPELLER` | 0–100, **writable, holds**. Maps linearly onto governor target `OP_PROP_TARGET_RPM` from `PROP_SPREAD_LO` (this engine 1469.86) at 0 to `PROP_SPREAD_HI` (2676.38) at 100. `STATE_PROP_LVR` mirrors it. |
| Fuel selector | `FUEL_SELECTOR` | **0 LEFT · 1 RIGHT · 2 OFF** (`ANIMTIP` and Logic 1924–1950; maps to stock selector 2/3/1). `STATE_FUEL_SELECTOR` mirrors. |
| Electric pump | `K:ELECT_FUEL_PUMP1_SET` | stock event; COWS reads stock `GENERAL ENG FUEL PUMP SWITCH:1`; read `GENERAL ENG FUEL PUMP ON` for running. |
| Battery master | `K:MASTER_BATTERY_SET` | stock; COWS bus follows (`ELEC_BUS_MAIN_VOLT`). |
| Resets | `RESET_DAMAGE` `RESET_BATT` `RESET_FLOOD` `RESET_PLUGS` `RESET_FAILURES` `RESET_ALL` | Momentary; a write of 1 is consumed (reads back 0) and acts. Consumed in `COWS_DA40_Failures.xml` and the MFD plugin, not `Logic.xml`. |

## ⚠️ The state trap that makes the aircraft unstartable, silently

**Symptom:** master on, pump running, selector on a full tank, mixture rich, throttle at the
priming mark — and `TB_FUEL_FLOW_GPH` stays 0, `ASSIST_PRIME_CYL_GRAM` stays 0, the starter
turns nothing over, **and nothing in the cockpit says why**: no CAS message, no failure flag,
fuel pressure simply reads 0. A blind pilot has no channel for this at all.

**Cause:** the per-engine "performance variation" set (POH p.5) is all zero — `FUEL_SPREAD_PRESSURE`,
`SPREAD_INJ_TRIM`, `SPREAD_OP`, `SPREAD_OC`, `SPREAD_ROUGH`, `MAG_SPREAD_TIMING`,
`CYL_SPREAD_EGT:1-4`, and the saved `STATE_*` copies behind them — while the two latches that
gate their generation (`CYL_SPREAD_SET`, `SPREAD_SET`) read 1, "already done". Fuel pressure
is a product (Logic 336: `servo grams × 2.2 × FUEL_SPREAD_PRESSURE × boil factor`) and the idle
jet is multiplied by `min(ENG_FUEL_PRESS / 0.8, 1)` (Logic 382), so a zero spread is zero
fuel, structurally, forever. `FUEL_QUANT_PROBE:n` is multiplied by `CYL_SPREAD_EGT:n`, so
`FUEL_FEED_QUANTITY` reads 0 with 20 gal in the tank. The instrument spreads use a
self-healing "regenerate if zero" gate and were fine (`STATE_SPREAD_AIR` 1.004); the engine
spreads use a latch and are not. The `STATE_*` are persisted (`systems.cfg` `LocalVar.10-29`);
the latches are not — a load-order race against a zeroed save is the likely origin.

**Fix — the POH's own:** Engine page menu → **Reset: Damage** (`RESET_DAMAGE` = 1). Measured:
`FUEL_SPREAD_PRESSURE` 0 → 1.51446 (the generator's 1.45–1.55 range), every other spread
regenerated, `ENG_FUEL_PRESS` 0 → 1.67, `FUEL_FEED_QUANTITY` 0 → 19.9, and the engine
primed at 7.2 gph and started on the next attempt. ⚠️ A grep for writers of the LIVE name
finds nothing under the reset and suggests the POH is wrong; the reset writes the `STATE_*`
names (Logic 5565 and siblings). Judge it by effect.

**What MSFSBA should do:** `FUEL_SPREAD_PRESSURE == 0` with the aircraft loaded is a
detectable, nameable condition. Say it — *"engine variations not generated; Reset: Damage on
the Engine page menu"* — and offer the reset. The Engine page menu is already driveable from
the display window (Ctrl+E on the Engine page).

## The cold start, as variables — POH p.6, verified

1. `K:MASTER_BATTERY_SET` 1 → bus 28 V. `K:ELECT_FUEL_PUMP1_SET` 1 → `GENERAL ENG FUEL PUMP ON` 1, stock fuel pressure ~6.9 psi, `ENG_FUEL_SYSTEM_FLOW_G/S` ~15.
2. Throttle to the priming mark (Diamond: half open; `K:THROTTLE1_SET` 8192 → 50 %), `INPUT_MIXTURE` 100. **`TB_FUEL_FLOW_GPH` rises to ~7.2** (POH: confirm > 4.5). The jet is `(THROTTLE_LEVER × 30 + 3.8, min 20) kg/h × INPUT_MIXTURE / 100`, gated by pressure ≥ 0.8. About five seconds; the priming-assist gauge (`ASSIST_PRIME_CYL_GRAM` vs `_REQ`) only counts when the Priming Assist option is on.
3. `INPUT_MIXTURE` 0 (idle cut-off), throttle back to ¼ inch (`THROTTLE1_SET` ~2000), `STARTER_SPAD:1` 1.
4. **The crank signature:** `ENG_COMP_RPM` ~190 and `ENG_COMP_STARTER` ~73 while turning; stock `GENERAL ENG RPM` **does** show 160–190 during the crank; `ELEC_BATT_AMPS` **−77**. Fires at ~5 s: stock RPM 572 → 775 → 1080, `ENG COMBUSTION` 1.
5. `INPUT_MIXTURE` 100, `STARTER_SPAD:1` 0, throttle to ~1000 rpm. Alternator: `ELEC_ALT_VOLT_OUT` 27.9, `ELEC_BATT_AMPS` swings to +7.
6. After start: pump OFF (Diamond checklist); ON again when cleared for line-up.

If it will not start: `RESET_FLOOD` (POH p.3) removes the fuel and cools the lines;
`K:ENGINE_AUTO_START` runs COWS's own script, which handles a flooded start.

## The run-up, as variables — Diamond checklist, verified

Throttle **2000 rpm** (measured at 2184–2192, ±1 steady; at 1450 it jitters ±30, the
combustion model's per-cycle roughness — average before speaking it).

- **Prop cycle:** `INPUT_PROPELLER` 0 → target snaps to `PROP_SPREAD_LO`, RPM **2184 → 1513**;
  back to 100 → **2192**. `OP_PROP_BETA` 31° (= stock `PROP BETA:1`). `OP_PROP_OIL_PRIME`
  read 5.1 before and after a full cycle — it is not a per-cycle counter; the second and third
  cycles register on nothing readable.
- **Magnetos (AFM: max drop 175, max differential 50):** BOTH 2192 → R **2114 (−78)** → L
  **2076 (−116)** → BOTH 2188. Differential 38. ⚠️ **Which mags are live is `ENG_MAG_CYL:nL` /
  `:nR`** (1/0 per cylinder per mag). `ENG_MAG_PWR:L/:R` is NOT switch state — it read 0.85 with
  the left mag OFF, and 1.0/1.0 at 2000+ rpm against 0.75/0.86 at idle: it is magneto output
  versus RPM. Stock `RECIP ENG LEFT/RIGHT MAGNETO` sat at 1/1 through every position — dead.
- **Breakers, voltage:** `CB_ALT` `CB_STR` `CB_FUP` `CB_MAIN` 0 (in); bus 28.32 V, +3 A.
- Throttle idle; pump off.

## Units — settled by measurement

| Variable | Unit | Evidence |
|---|---|---|
| `TB_CALC_MAP`, `TB_TARGET_MAP` | **bar**, absolute | 1.01223 at ambient 29.890 inHg (×29.53 = 29.891); 0.5246 at idle = 15.49 |
| `DISP_MAP` | inHg | = `TB_CALC_MAP` × 29.53 to 0.01 (22.47 vs 0.7607 bar) — what the G1000 draws |
| `CHT_C:n` / `CHT_F:n` | °C / °F | 183.5 °C ↔ `DISP_CHT:n` 359 °F (integer) |
| `DISP_EGT:n` | °F, integer | 1110 idle, 1331 run-up |
| `DISP_CHT_HOT`, `DISP_CHT_HOT_CYL` | °F / cylinder index | |
| `DISP_OP_PROBE` | psi | = stock oil pressure 73.7 / 75.1 |
| `DISP_FF_PROBE` = `TB_FUEL_FLOW_GPH` | US gal/h | 10.56 = 10.558 |
| `EGT_MIXTURE` | air/fuel ratio | 10.35 at full rich; POH: rich is 10:1, best power 12.5:1, stoich 14.7:1 |
| `OP_PROP_TARGET_RPM`, `PROP_SPREAD_LO/HI` | rpm | governor target; 1469.86 / 2676.38 this engine |
| `OP_PROP_BETA` | degrees | = stock `PROP BETA:1` |
| `ENG_COMP_RPM` | rpm | the crank tach — 192 while the stock RPM was also ~190; the two disagree by ~10 % once running (793 vs 1081), stock is the flying tach |
| `DISP_PROP_RPM_PROBE` / `DISP_PROP_RPM` | rpm / rpm to 10 | = stock 920.5 / 920. ⚠️ `PROP_RPM_SENS:1` — the NG's tach per [da40.md](da40.md) — **does not exist on the XLS** (NG-only); reading it returns a phantom 0. `RPM_SENS_RPM` (728 at 928) is a sensor model, not RPM |
| `ELEC_BATT_CAPACITY` | Ah-like | nominal **240**, cap 250, restore floor 15; parked self-discharge 7/h of real clock; **3 % after ~50 min of master+pump with the engine dead** |
| `ELEC_BATT_AMPS` | A, − discharge / + charge | −76.9 cranking, +7.4 just after start, +3 at run-up |
| `ELEC_BUS_MAIN_VOLT`, `ELEC_ALT_VOLT_OUT` | V | 28.3 charging; 21.2 on a draining battery |
| `OC_TEMPERATURE`, `BC_TEMPERATURE`, `FUEL_TEMP` | °C | all = ambient 18.98782 cold; 83.5 / 22 warm |
| `ENG_FUEL_PRESS` | **unknown** | 0 → 1.67–1.77 running; the jet gates at 0.8 |
| `TB_FF_JET_THROTTLE_KG/HR` | kg/h | `THROTTLE_LEVER` × 30 + 3.8, min 20 |

## Stock simvars that are DEAD or DIVERGENT on the XLS

COWS runs its own engine below the MSFS 400-rpm floor and injects selected values back
(POH p.5). Everything else is either never written or is a different number.

| Stock simvar | State | Measured |
|---|---|---|
| `RECIP ENG MANIFOLD PRESSURE:1` | divergent | 0.599 inHg stopped (real 29.89); 13.5 running (real 15.5) — the injected engine-output value, not the gauge |
| `RECIP ENG LEFT/RIGHT MAGNETO:1` | **dead** | 1/1 at every key position |
| `GENERAL ENG STARTER ACTIVE:1` | **dead** | 0 through a real 77 A crank |
| `ELECTRICAL BATTERY LOAD:1` | **dead** | 0 during 77 A |
| `ELECTRICAL BATTERY VOLTAGE:1` | **dead** | 28 with the COWS battery at 3 % |
| `GENERAL ENG EXHAUST GAS TEMPERATURE:1` | **dead** | −273.15 °C |
| `RECIP ENG CYLINDER HEAD TEMPERATURE:1` | divergent | 99.6 °C vs `CHT_C:1` 85.4 |
| `GENERAL ENG GENERATOR SWITCH:1` | dead | 0 while `GENERATOR ACTIVE` 1 |
| `GENERAL ENG PROPELLER LEVER POSITION:1` | divergent | 0 % with the lever at 100, 54 % with it at 0 — an intermediate |
| `GENERAL ENG RPM:1` | **live once running**; 0 stopped but shows the crank | use `ENG_COMP_RPM` below 400 |
| `ENG COMBUSTION`, `GENERAL ENG OIL PRESSURE`, `PROP BETA`, `GENERAL ENG FUEL PUMP ON`, `ELECTRICAL MASTER BATTERY`, `GENERAL ENG THROTTLE LEVER POSITION` | live | |

## Corrections — recorded so they are not re-derived

- **`sim_running: false` does not mean the flight model is stopped.** It read false across
  three reconnects while the thermal model tracked ambient to five decimals, writes stuck, the
  key sprang back on its own timer, and the engine started. [da40.md](da40.md)'s zeros signature
  requires the starter COMMANDED — and is unusable on the XLS anyway, because `STARTER ACTIVE`
  and `BATTERY LOAD` are dead. Do not use it here.
- The POH's "Reset: Damage regenerates the variations" is **correct**; a claim here that the
  code could not do so was wrong (it greps the live name, the reset writes the saved one).
- A variable "reading 0" may be a truncated name that does not exist — see Method.
- A held write is not a working control (`STARTER_SWITCH` 4), and a refused single write is
  not a dead one (`INPUT_MIXTURE` refused only while the spread set was zero).

## Priming — measured through a hot restart

The Lycoming has no primer; the idle jet loads the induction with the pump on and the mixture
forward, and the charge sits OUTSIDE the cylinders (`ENG_FUEL_OUTSIDE_CYL_GRAM:1-4`, grams,
always live) until the crank draws it in. The model's own arithmetic, from `Logic.xml`:

| | Formula | Measured (hot, EGNX) |
|---|---|---|
| Required per cylinder | `1.5 / ENG_ATOMISE_CYL:n / ENG_INT_MANI_EVAP:1` | 0.39 g (evap 3.89, atomise 1.0) |
| Flooded per cylinder | `1.6 / ENG_INT_MANI_EVAP:1 / ENG_ATOMISE_CYL:n`, ANY cylinder over | 0.41 g — a six percent window |
| Vendor gauge `ASSIST_PRIME_PERCENT` | `(lines + cylinders) / (required + 10.5) × 100`, cap 150 | 78.6 with the cylinders three times over |

The vendor's `ASSIST_PRIME_*` family computes only with the MFD **Priming Assist** option on
(`L:ASSIST_PRIME`, writable), the pump on and RPM under 500. ⚠️ **Its percentage counts the
LINES against a fixed 10.5 g fill, so after a mixture-cut shutdown it read 78 % — "not yet
primed" — while every cylinder held 1.2 g, three times the charge and flooded by the model's
own test.** A pilot reading only the percentage primes more. Say cylinders and required, never
the percentage.

**Hot engine rate:** the jet loaded ~2.5 g per cylinder per second (0 → 6.3–7.8 g in four
seconds; 0 → 4.2–5.8 g in two), so the primed-to-flooded crossing takes under a fifth of a
second — faster than the 1 Hz batch. The AFM's warm-start "rich 1–3 s" floods this model.
Nothing can be timed by ear; the state must be spoken on the crossing itself.

**Flooded is recoverable, and the model agrees with AFM 4.5 (c):** pump off, mixture fully aft,
throttle mid, crank — the charge burns down and the engine coughs, 4.8 g in one second, 32 g in
4.7 (994 rpm), then dies unless the mixture comes forward. With the mixture forward while still
cranking it fired to a steady 1010 rpm. `RESET_FLOOD` is the POH's shortcut and is on the panel;
it was not exercised.

**Vapour:** `ENG_FUEL_LINE_BOIL:1-4` (indexed — the bare name is a phantom) is a random per-tick
boil once a line passes 100 on the model's scale (`ENG_FUEL_LINE_TEMP:n`, 157 on the hot
engine); the systemic factor `FUEL_TEMP_BOIL` subtracts a whole unit for the electric pump, so
the pump is the cure, as in the aeroplane. `START_HOT` has no writer in any XML — the MFD
plugin's — and is not built on.

**Three starter cycles cost the battery 239 → 125.**

## Fuel — measured

| Fact | Evidence |
|---|---|
| `FUEL_SELECTOR` 0 LEFT · 1 RIGHT · 2 OFF; a write drives the stock selector through the Logic (0 → tank 2, 1 → 3, 2 → 1) | ANIMTIPs; Logic 1924–1950; read 0 ↔ stock 2 after a reload |
| **`ENG_FUEL_PRESS` is BAR; `DISP_FP_PROBE` is the gauge in psi** | 1.616 against 23.43 = 14.5 psi/bar; AFM green 14–35 psi, no yellow |
| Tanks 2 × 20.6 gal, 0.5 unusable each; the sim's tank is 25 gal (the long-range size) | AFM 2.14.2; `FUEL TANK LEFT MAIN CAPACITY` 25 |
| The gauge (`FUEL_QUANT_PROBE:n`) is the true quantity plus a slosh term, **clamped to 15.25 between 15.25 and 18.5 gal** — the AFM's "max indicated 15 US gal per tank" | Logic 1979–1986; read 19.5 / 19.7 against 19.9 / 20.0 actual, above the band |
| Max tank difference 10 gal (NG: 9) | AFM 2.14.2 |
| `FUEL_FEED_QUANTITY` is the selected tank's probe, 0 when OFF, and was 0 while the spread set was zero | 19.50 = probe 1 with LEFT selected |
| Vapour lock is `FUEL_TEMP_BOIL_FAC` multiplying the pressure; the electric pump subtracts a unit from `FUEL_TEMP_BOIL` | Logic 2043–2046 |
| The stock fuel pressure simvar is the pump side only (6.9 psi with the engine stopped) | — |

## Engine Start — measured

| Fact | Evidence |
|---|---|
| **`K:ENGINE_AUTO_START` is COWS's own script**: the Inputs binding writes `1 (>L:INPUT_START, percent)`, and the Logic's Autostart block runs while `INPUT_START` is 1, walking `AUTOSTART_STEP` 0 → 11 in tenths | `COWS_DA40_Inputs.xml` 78; Logic 4439–4610 |
| The steps: 0 master on (waits for `AS1000_PFD_STATE` 2, zeroes mixture and throttle) · 1 strobes · 2 the **lower** tank (`probe1 ≥ probe2` → RIGHT) · 3 throttle +10 %/tick to 30 % · 4 pump on · 5 mixture +30/tick until any cylinder ≥ required (the Priming formula) · 6 mixture −30/tick to 0 · 7 throttle to 0.10 + alt/10000 × 0.05, timer reset · 8 **crank** (`AUTOSTART_START:1` 1, `AUTOSTART_STARTER_TIMER` +0.1/tick), out at `ENG_COMP_RPM` > 300, **gives up at 7** (step and `INPUT_START` zeroed in the same tick) · 9 mixture +10/tick to 100 − alt/10000 × 50 · 10 pump off · 11 alternator on, `INPUT_START` 0 | Logic, same block |
| Flooded (any cylinder ≥ 1.6/evap/atomise) skips 3 → 7; already turning (`ENG_COMP_RPM` > 400) skips 0 → 9 | Logic 4441–4447, 4561–4569 |
| `AUTOSTART_STEP` is residue between runs (read 1 on the ground, idle); `INPUT_START` is the running flag. `K:ENGINE_AUTO_SHUTDOWN` writes it −1 | read 2026-09-05 |
| ⚠️ **UNPOWERED, THE FUEL LOGIC DOES NOT TICK.** Master off after a reload: `ENG_FUEL_SYSTEM_SERVO_GRAM` 21.56 against a cap that computes to 0.5 + 0.0129, `ENG_FUEL_PRESS` 19.75, `DISP_FP_PROBE` 23.7 (the display probe frozen too). Master on: both 0 within a tick. So a start-readiness judgement checks the master BEFORE anything fuel-derived, and the Fuel panel's pressure row says "master off" rather than rendering the frozen number as 286 psi | measured 2026-09-05 |
| The `ENG_FUEL_PRESS` unit question is closed by the model's own hand: the G1000 template writes `DISP_FP_PROBE += (ENG_FUEL_PRESS × 14.5 − DISP_FP_PROBE) / 8` | Logic 2086 |
| `GENERAL ENG COMBUSTION:1` is live on the XLS (0 stopped, 1 running) | read across a start |

## Mixture and Propeller — measured through a lean at run-up power and a static full-throttle run

| Fact | Evidence |
|---|---|
| **The bars are drawn from `DISP_EGT:1-4` and `DISP_CHT:1-4`, in FAHRENHEIT** (the plugin titles them "EGT °F" / "CHT °F"); the bare `DISP_EGT` / `DISP_CHT` / `DISP_LEAN_PEAK` / `DISP_LEAN_DELTA` are phantoms. `DISP_EGT:n` = `EGT_PROBE:n` + a spread offset, rounded; `DISP_CHT:n` likewise from `CHT_PROBE:n`. `DISP_LEAN_HOTEST` / `DISP_CHT_HOT` + `DISP_CHT_HOT_CYL` are the maxima | Logic 2111–2146; read 1297/1310/1310/1287 F at 2211 rpm full rich |
| The CHT arcs: green 150–475 F, caution 475–500, red above; EGT has no arc, the COWS POH recommends not exceeding 1350 F | `Da40MfdPlugin.js` 'CHT °F' bars; POH p.7 |
| **Lean assist** = `DISP_LEAN_ASSIST` (the Assist softkey; the plugin writes it), and while it is 1 the Logic tracks per cylinder `DISP_LEAN_PEAK:n` = highest `DISP_EGT:n` seen and `DISP_LEAN_DELTA:n` = peak − now. `DISP_LEAN_HIGHLIGHT` and `DISP_LEAN_DELTA_BIGGEST` never moved (0 / 0.997) | Logic 2154–2172; the sweep below |
| **The lean sweep at 2211 rpm**: mixture 100 → AFR 10.7, EGT 1297–1310; 85 → 10.9, 1307–1322; 70 → 12.1, 1347–1361; 55 → 14.5, 1464–1482 (CHT hot 438); 48 → 15.8, peaks held at 1475–1484 while cylinders 1 and 3 fell to 1129 / 980 and rpm 2185 → 1905 (rough). Peak EGT sits at ~14.5–15.5 to 1; past it the drop is hundreds of degrees, not tens | read 2026-09-05 |
| `K:MIXTURE_SET_BEST` toggles `L:MIXTURE_SET_BEST`; while set, the Logic walks `INPUT_MIXTURE` until the four cylinders' air/fuel sum to 49–50 (12.5 to 1 each), or to 72 % flat with `AUTOMIXTURE` on. Written as the L:var, like the auto-start | Inputs.xml 168–234 |
| **The red box is per cylinder and INDEXED**: while `CHT_HEAT_OUTPUT_KW:n` ≥ 42 AND `TB_FF_MIXTURE:n` (air/fuel) is in 12.9–15.5: FAC = (kW − 42)/8, rich edge 14.7 − 1.8 FAC, lean edge 14.7 + 0.8 FAC, `DAMAGE_REDBOX_ITS:n` = depth past the nearer edge, `DAMAGE_CYL:n` += 0.05 × depth per tick. ⚠️ `ITS:n` HOLDS ITS LAST VALUE outside the box (19.7 with the engine stopped) — judge the box from its inputs. Run-up power gives 30.8 kW (box closed); static full throttle 47.8 kW (box 13.4–15.3), and full rich at 10.6 sits outside it | Logic 4988ff ("Whos there?"); measured |
| **Plug fouling is per PLUG**: `DAMAGE_MAG_FOUL:nR` / `:nL` (eight), += `DAMAGE_MAG_FOUL_RATE:n`/60 per tick, capped 0–100, where rate = 4 × (14.7 − AFR) − a power term − a CHT term, × rpm/1000 — rich, cold, low power fouls; power cleans. It costs spark: `ENG_MAG_FOUL_PWR:nX` = √(foul/100) × 0.8. ⚠️ A saved state can exceed the cap: plug 1R read **137** on this aircraft, which is why cylinder 1 dropped out first in the lean sweep | Logic 4930–4936, 665, 5139 |
| Shock cooling: `CHT_TEMP_INC:n` < −0.15 damages the cylinder ((inc + 0.15) × −0.2 per tick); `DAMAGE_CYL:n` > 99 = `KAPUTT_CYL:n`; `HEALTH_CYL:n` = 1 − damage/2000 − block/2000 | Logic 4966–4975, 5128–5158 |
| ⚠️ **DETONATION IS NOT MODELLED IN THIS BUILD.** The block that computes `DETONATION_TEMP_C:n` and `DETONATION:n` (Logic 1857–1897) sits inside a comment opened as `(*--Detonation--` and closed forty lines later; nothing consumes `DETONATION:n`; the 4 it read live is residue. No row, no call-out | Logic 1857, 1897 |
| ⚠️ **With the Engine Damage option OFF (`DAMAGE_ENABLED` 0, this aircraft) none of the damage accumulators run** — red box, fouling, shock cooling, cylinder damage all freeze. The panel derives the red box from its inputs regardless and says the option is off | measured |
| **Propeller cycle**: `INPUT_PROPELLER` 100 → 0 took 2212 → 1560 rpm in two seconds and back on restore; governor target `OP_PROP_TARGET_RPM` read 2687 at 100 (and a transient 2.5 mid-move — read it settled). `OP_PROP_OIL_PRIME` is the hub oil charge: fills toward 5.1 at rpm/2700 per tick while rpm > target with oil pressure and a working prop pump, drains by 0.1 otherwise; under 5 the governor is in beta-forced mode and the prop responds slowly — the reason for the run-up item | Logic 1340–1400 |
| **`K:ENGINE_AUTO_START` is unreliable from outside; `1 (>L:INPUT_START, percent)` is what its binding writes and it walked the script every time** (0 → step 4 within a second). The K route started the engine once, then left `INPUT_START` decayed to 0.001 with the step at 0. At completion the counter read **0**, not 11 — judge the outcome on combustion. `ENGINE_AUTO_SHUTDOWN` writes −1: mixture to cut-off, pump off, and once rpm is under 1 % throttle closed, key OFF, flag cleared | Inputs.xml 27–50, 79–82; measured |
| ⚠️ **The XLS's G1000 is the NXi (WT 1.4.1), and its database-acknowledge screen takes ENT only as the SimConnect H-event.** After a reload the MFD sat on "Press ENT or rightmost softkey to continue" (`.startup-confirm-screen`, display flex, z 9999) and the whole MFD read as "starting up" on every page; `A.press("ENT")` over the socket left it, `1 (>H:AS1000_MFD_ENT_Push)` cleared it (parent gains `hidden`). The display window routes Ctrl+Enter over SimConnect while that screen shows | measured |
| The XLS plugin registers `Da40EnginePage` / `Da40ChecklistPage` / `Da40ChecklistSelectionPopup` / `Da40ChecklistCategorySelectionPopup` — the NG's are `Da40Ng…`. Its EIS strip nests MAN IN and RPM as `.eis-dial-gauge`, its Engine page is `.da40-engine-page` with double dials (Oil °F/PSI, Fuel GPH/PSI) and digit-less bar charts | live DOM dumps |
| **Mag check at 2212 rpm**: right alone 2098 (−114), left alone 2133 (−79), differential 35 — inside the AFM's 175 / 50 | read |

## The assistance layer — read from the model, because the POH only names it

| Fact | Evidence |
|---|---|
| **`AUTOMIXTURE` is DETECTED, never set.** `(A:RECIP MIXTURE RATIO:1, ratio) 100 * 9 ==` OR `(L:AUTOMIXTURE_FORCE)` ramps it `+0.2` a tick to 1; the else-branch writes 0 in one step. So 0.2–0.8 is the ramp and means nothing — only the ends are states | Logic 1805–1819; forced live 0 → 1 and back |
| With it set, `MIXTURE_VALVE` is bypassed entirely: the lever writes `AUTOMIXTURE_TARGET` instead (>95 % → 10, 5–95 % → `20 − (pos/100 − 0.05)/0.9 × 10`, <5 % → 100) and `AUTOMIXTURE_TARGET_FF` delivers it | Logic 341–437 |
| **The engine cannot be flooded with it on**: every `ENG_FUEL_OUTSIDE_CYL_GRAM:n` above 2 is written back to 2, each tick. Priming is still required — the clamp is a ceiling, not a prime | Logic 1821–1849 |
| `MIXTURE_SET_BEST` snaps to 72 % rather than walking to 12.5 to 1 | Inputs.xml 168 |
| **`START_MIXTURE` is the "Engage Starter w/ Mixture" option** (XLS only; the NG's MFD menu has no such row). Its gate: `INPUT_MIXTURE == 0` AND `STARTER_SWITCH == 3` (BOTH) or 4 AND `START_MIXTURE == 1` AND `ENG_COMP_RPM < 500`. ⚠️ With the key anywhere but BOTH the option is a silent no-op | IN.xml 176–197; read 0 live |
| `ASSIST_PRIME` is the "Priming Assist" option; `ASSIST_PRIME_ACTIVE` is 1 only with the option on, the pump on and under 500 rpm; `ASSIST_PRIME_PERCENT` is `(system + cylinders) / (required + 10.5) × 100`, clamped 0–150 — so 100 % is the computed requirement. ⚠️ The POH says a HOT start may fire below the green area, which is why MSFSBA derives its own priming state from the cylinders instead | Logic 4633–4652; POH p.17 |

## Still open

- **Cruise** — the plan's third point; needs the aircraft flown.
- `RPM_SENS_*` and `OP_PROP_OIL_PRIME` meaning.
- ~~The POH's cruise power tables~~ **DONE** - all twelve columns are in
  `DA40PerformanceTables.XlsCruise`, and `DA40_XLS_CRUISE_TABLE` on Power and Levers reads them
  BACKWARDS from the live RPM and manifold pressure. The shaded recommended bands are deliberately
  NOT transcribed: they cannot be read off the image with enough confidence to state as fact.
- Lean assist in the cruise — the mechanics are measured on the ground (above); confirm the
  peaks and the first-to-peak call-out at cruise power.
- The throttle map, if a panel needs to command an RPM rather than a position.
- ⚠️ **`msfs_get_lvar` returns 0 for a name that is not registered at all** — `ELEC_MASTER` is
  not in the XLS package, and the 0 it "read" with the master on meant nothing. Same trap as a
  truncated name. Check the extract before believing any zero; the master's own variable is
  stock `ELECTRICAL MASTER BATTERY`.

## Appendix — the 380 XLS-only variables, by family

Base names, index suffixes such as `:1` / `:1L` collapsed; a name may contain `/` (`TB_FF_JET_PRESS_KG/HR`).
Generated from the package XML by the command in **Method**; `4` is an extraction artefact of that regex, not a variable.

### Assistance layer (7)

`AUTOMIXTURE AUTOMIXTURE_FORCE AUTOMIXTURE_TARGET AUTOMIXTURE_TARGET_FF AUTOSTART_MIX AUTOSTART_STARTER_TIMER AUTOSTART_THROT`

### Combustion (ENG_COMP_*) (19)

`ENG_COMP_AIRMASS_CYL ENG_COMP_AIRTEMP_CYL ENG_COMP_COMPRESSION_CYL ENG_COMP_CRANK_CYL ENG_COMP_FORCE_CYL ENG_COMP_FRIC_OIL ENG_COMP_IGN_CYL ENG_COMP_IGN_CYL_SOUND ENG_COMP_MAG_CYL ENG_COMP_POS_360 ENG_COMP_POS_720_2 ENG_COMP_POS_CYL ENG_COMP_PRESS_CYL ENG_COMP_PROP_DRAG ENG_COMP_PROP_WIND ENG_COMP_PROP_WIND_FAC ENG_COMP_PWR ENG_COMP_SHAKE ENG_COMP_STROKE_CYL`

### Detonation (4)

`DETONATION DETONATION_INT_TEMP_C DETONATION_TEMP_C DETONATION_TEMP_FAC`

### Engine efficiency + friction model (16)

`COMBUSTION COOKING ENG ENG_FRIC ENG_FRIC_FAC ENG_FRIC_HI ENG_FRIC_LO ENG_FRIC_RPM_HI ENG_FRIC_RPM_LO ENG_MECH_EFF ENG_MECH_EFF_FAC ENG_MECH_EFF_HI ENG_MECH_EFF_LO ENG_MECH_EFF_RPM_HI ENG_MECH_EFF_RPM_LO ENG_OIL_PWR_FAC`

### Exhaust temp, spread, lean assist (36)

`CYL_SPREAD_COOL CYL_SPREAD_EGT CYL_SPREAD_INJ CYL_SPREAD_SET DISP_LEAN_ASSIST DISP_LEAN_DELTA DISP_LEAN_DELTA_BIGGEST DISP_LEAN_HIGHLIGHT DISP_LEAN_HOTEST DISP_LEAN_PEAK EGT EGT_DELTA EGT_MANI EGT_MANI_COOLING EGT_MANI_HEATING EGT_MAP_FACTOR EGT_MAX EGT_MIXTURE EGT_PROBE EGT_RPM_FACTOR EGT_TABLE_MIX_HI EGT_TABLE_MIX_LO EGT_TABLE_MIX_RNG EGT_TABLE_TEMP_FAC EGT_TABLE_TEMP_HI EGT_TABLE_TEMP_LO EGT_TABLE_TEMP_RNG EGT_TARGET ENG_EGT ENG_EGT_FAC ENG_EGT_FAC_HI ENG_EGT_FAC_LO ENG_EGT_MIX_HI ENG_EGT_MIX_LO ENG_MAG_EGT SOUND_LEANEST_MIXTURE`

### Fuel line, temperature + vapour lock (34)

`ENG_FUEL_FIREWALL_TEMP_C ENG_FUEL_FIREWALL_TEMP_COOLING ENG_FUEL_FIREWALL_TEMP_HEATING ENG_FUEL_FLOW_KG/HR ENG_FUEL_LINE_BOIL ENG_FUEL_LINE_FLOW ENG_FUEL_LINE_FLOW_CHECK ENG_FUEL_LINE_FLOW_FAC ENG_FUEL_LINE_GRAM ENG_FUEL_LINE_IN_G/S ENG_FUEL_LINE_OUT_G/S ENG_FUEL_LINE_PRIMED ENG_FUEL_LINE_TEMP ENG_FUEL_LINE_TEMP_COOLING_AIR ENG_FUEL_LINE_TEMP_COOLING_FUEL ENG_FUEL_LINE_TEMP_HEATING ENG_FUEL_OUTSIDE_CYL_GRAM ENG_FUEL_PRESS ENG_FUEL_SYSTEM_FLOW_ENG_G/S ENG_FUEL_SYSTEM_FLOW_EXCES ENG_FUEL_SYSTEM_FLOW_EXCES_PRESSURE ENG_FUEL_SYSTEM_FLOW_G/S ENG_FUEL_SYSTEM_FLOW_TO_SERVO_G/S ENG_FUEL_SYSTEM_GRAM ENG_FUEL_SYSTEM_PRIMED ENG_FUEL_SYSTEM_SERVO_GRAM FUEL_QUANT_SLOSH FUEL_TEMP FUEL_TEMP_BOIL FUEL_TEMP_BOIL_FAC FUEL_TEMP_BOIL_FAC_FF FUEL_TEMP_BOIL_PRESS FUEL_TEMP_PUMP_PRESS_DROP STATE_FUEL_SYSTEM`

### Fuel selector + fuel spread (3)

`FSC_FUEL_SPREAD_PRESSURE FUEL_SPREAD_PRESSURE STATE_FUEL_SPREAD_PRESSURE`

### G1000 display probes (DISP_*) (17)

`DISP_CHT DISP_CHT_HOT DISP_CHT_HOT_CYL DISP_EGT DISP_FF_PROBE DISP_FP DISP_FP_PROBE DISP_MAP DISP_MAP_PROBE DISP_OP_PROBE DISP_PROP_RPM_PROBE FAILURES_DISP_CHT FAILURES_DISP_EGT FAILURES_DISP_FF FAILURES_DISP_FP FAILURES_DISP_MAP FAILURES_DISP_RPM`

### Head temp + block thermal (CHT_*, BC_*) (40)

`BC_COOLING BC_COOLING_AIR BC_COOLING_OIL BC_HEATING BC_HEATING_CHT BC_HEATING_FRIC BC_TEMPERATURE BC_TEMP_DIFF BC_TEMP_DIFF_OIL BC_TEMP_INC CHT_AIRFLOW CHT_AIRFLOW_ANG CHT_AIRFLOW_ANG_FAC CHT_AIRFLOW_IND CHT_AIRFLOW_INDUCED CHT_AIRFLOW_TOTAL CHT_AIRFLOW_WIND CHT_AIRF_AX_KNOTS CHT_AIRF_AX_KNOTS_DIFF CHT_AIRF_BETA CHT_AIRF_RAD_KNOTS CHT_C CHT_COOLING CHT_COOLING_OIL CHT_EFF_F CHT_EFF_R CHT_EGT_DELTA_K CHT_F CHT_HEATING_KW CHT_HEAT_EXHT_AIR_KW CHT_HEAT_EXHT_FUEL_KW CHT_HEAT_EXHT_KW CHT_HEAT_FRICTION_KW CHT_HEAT_FUEL_FLOW_KW CHT_HEAT_OUTPUT_KW CHT_PROBE CHT_TEMP_DIFF CHT_TEMP_INC FAILURES_CHT_BAFFLE FAILURES_CHT_OIL`

### Induction + manifold pressure (TB_*) (44)

`DENSITY_CORRECTION ENG_INT_MANI ENG_INT_MANI_COOLING ENG_INT_MANI_EVAP ENG_INT_MANI_HEATING FILTER_TARGET_MAP INT_AIR_DENSITY INT_AIR_DENSITY_TEMP_FAC TB_CALC_MAP TB_FF_FUEL_FLOW_KG/HR TB_FF_JET_PRESS_G/S TB_FF_JET_PRESS_KG/HR TB_FF_JET_PRESS_KG/HR_T TB_FF_JET_THROTTLE_KG/HR TB_FF_JET_VENTURI_KG/HR TB_FF_MIXTURE TB_FF_MIXTURE_RATIO TB_FF_SERVO TB_FF_VENTURI_P_LOSS_FAC TB_FF_VENTURI_P_LOSS_PASC TB_FF_VENTURI_SPEED_M/S TB_FUEL_FLOW_G/S TB_FUEL_FLOW_GPH TB_INT_AIR_DENSITY_TEMP_FAC TB_INT_PRSS TB_INT_TEMP_C TB_INT_TEMP_F TB_INT_TEMP_K TB_MASS_FLOW_KG/HR TB_POS TB_PRSS_RED TB_PUMP_LOSS_PWR TB_RSRTIC TB_RSRTIC_100000 TB_SET_FIX TB_SET_MAP TB_TARGET_MAP TB_VOL_EFF TB_VOL_EFF_FAC TB_VOL_EFF_HI TB_VOL_EFF_LO TB_VOL_EFF_RPM_HI TB_VOL_EFF_RPM_LO TB_VOL_FLOW_M/HR`

### Levers + prop governor (30)

`FAILURES_MIX_LEVER FAILURES_PROP_LEVER FAILURES_PROP_PUMP FAILURES_THROT_LEVER FSC_PROP_SPREAD_HI FSC_PROP_SPREAD_LO FSC_THROTTLE_SPREAD INPUT_MIXTURE INPUT_PROPELLER MIXTURE_LEVER_GAME_SET MIXTURE_SET_AVG MIXTURE_SET_BEST MIXTURE_VALVE MIXTURE_VALVE_RST OP_PROP_BETA OP_PROP_OIL_LOSS OP_PROP_OIL_PRIME OP_PROP_TARGET_RPM OP_PROP_TEMP_FAC PROP_ANI PROP_SPREAD_HI PROP_SPREAD_LO STATE_MIXT_LVR STATE_PROP_LVR STATE_PROP_SPREAD_HI STATE_PROP_SPREAD_LO STATE_THROTTLE_SPREAD THROTTLE_LEVER THROTTLE_LEVER_LINK THROTTLE_SPREAD`

### Magnetos + plug fouling (16)

`DAMAGE_MAG_FOUL DAMAGE_MAG_FOUL_RATE ENG_MAG_CYL ENG_MAG_FOUL_PWR ENG_MAG_PWR ENG_MAG_PWR_REQ ENG_MAG_PWR_REQ_DENS FAILURES_MAG FAILURES_MAG_GND_L FAILURES_MAG_GND_R FAILURES_MAG_L FAILURES_MAG_R FSC_MAG_SPREAD_TIMING MAG_SPREAD_TIMING RESET_PLUGS STATE_MAG_SPREAD_TIMING`

### Oil cooler + oil thermal (OC_*) (21)

`FAILURES_THERMOSTAT_OIL OC_COOLING OC_COOLING_ACT OC_COOLING_PAS OC_EFF OC_HEATING OC_HEATING_BLOCK OC_HEATING_CHT OC_HEATING_FRIC OC_HEATING_FUEL OC_OIL_FLOW_CHT OC_PUMP_SPEED OC_TEMPERATURE OC_TEMPERATURE_F OC_TEMP_DIFF OC_TEMP_DIFF_CHT OC_TEMP_INC OC_THERMOSTAT OC_THERMOSTAT_T OP_OIL_PRIME OT_PROBE`

### Other XLS-specific failures (7)

`FAILURES_FUEL_INJ FAILURES_FUEL_LEAK FAILURES_FUEL_LEAK_L FAILURES_FUEL_LEAK_R FAILURES_FUEL_PUMP FAILURES_FUEL_SPRING FAILURES_VACC_LEAK`

### Per-cylinder health + damage (14)

`CYL_SHAKE DAMAGE_BLOCK_FAC DAMAGE_CYL DAMAGE_CYL_FAC ENG_ATOMISE_CYL ENG_CYL_POWER_HEALTH_FAC ENG_CYL_PWR_FAC ENG_CYL_SHAKE_FAC FAILURES_CYL HEALTH_CYL KAPUTT_CYL RUN_COMB_CYL SOUND_CUT_CYL WORKING_CYL`

### Per-cylinder spread / build variation (18)

`FSC_CYL_SPREAD_COOL FSC_CYL_SPREAD_EGT FSC_CYL_SPREAD_INJ FSC_ENG_COMP_RPM FSC_OP_SPREAD_BYPASS FSC_SPREAD_OC FSC_SPREAD_OP FSC_SPREAD_ROUGH SPREAD_INJ_TRIM SPREAD_OC SPREAD_OP SPREAD_ROUGH STATE_CYL_SPREAD_COOL STATE_CYL_SPREAD_EGT STATE_CYL_SPREAD_INJ STATE_SPREAD_OC STATE_SPREAD_OP STATE_SPREAD_ROUGH`

### Priming (ASSIST_PRIME_*) (6)

`ASSIST_PRIME ASSIST_PRIME_ACTIVE ASSIST_PRIME_CYL_GRAM ASSIST_PRIME_CYL_REQ ASSIST_PRIME_PERCENT ASSIST_PRIME_SYS_GRAM`

### Red box (mixture damage) (4)

`DAMAGE_REDBOX_FAC DAMAGE_REDBOX_ITS DAMAGE_REDBOX_LOP DAMAGE_REDBOX_ROP`

### Sensors + filters (8)

`FILTER_CHT_TEMP_INC FILTER_PRSS_RED RAND_FP RPM_SENS_POS RPM_SENS_RESET RPM_SENS_RPM RPM_SENS_TIME RPM_SENS_TIME_FIX`

### Sound model (2)

`SOUND_ENGINE_HIGHS SOUND_TORQUE`

### Starter, mag key + flood reset (4)

`ENG_START_AIRVOL_CYL RESET_FLOOD START_MIXTURE START_MIXTURE_START`

### Unclassified (4)

`4 BRO_CMON_PLEASE_STOP STATE_FLAPS_R SUPER_SECRET_ENGINE_DEBUG`

### XLS-specific breakers + electrics (26)

`CB_ACN CB_ALT CB_ALT_OVER CB_APT CB_BATT_OVER CB_FAN CB_FUP CB_MAIN ELEC_ALT_ACTUAL_AMPS ELEC_BUS_ESS ELEC_ESS FAILURES_ALT_OVERVOLT FAILURES_CB_ADF FAILURES_CB_ALT_CONT FAILURES_CB_ALT_PROT FAILURES_CB_AV_BUS FAILURES_CB_BATT FAILURES_CB_FUEL_PUMP STATE_ALTERNATOR STATE_CB_ACN STATE_CB_ALT STATE_CB_APT STATE_CB_BAT STATE_CB_FAN STATE_CB_FUP STATE_CB_TAS`


## The FS Copilot sweep — what it found and the three rules that decided it

`FsCopilot_v1.2.1/Definitions/COWS_DA40XLS.yaml` is a **third-party inventory of this
airframe**: a shared-cockpit tool has to enumerate the state that matters or two aircraft
drift apart, so its `get:` list is a coverage test for READOUTS — none of which is a
`<Component ID>`, which is all `CowsDA40InteractionSurfaceTests` can see.

**The method, and the two traps it has to dodge.** Enumerate `L:` names from the YAML,
subtract `GetVariables()` **read from the BUILT ASSEMBLY** (a KEY is not a NAME, and a
BUTTON's Name is its own key — read the Name column alone and every `RESET_*` plus
`ATT_CAGE` reads as a gap when the gyro-cage button already holds `ATT_CAGE` from inside
its setter), then check each survivor against the installed package with a
**binary-inclusive** scan (a variable used only by a WASM gauge reads as absent from a text
grep over `.xml`).

⚠️ **A first pass of that scan reported `OC_TEMPERATURE`, `ENG_MAG_FOUL_PWR` and
`ENG_FUEL_LINE_GRAM` as "written nowhere in any XML, so the WASM writes them". That was
WRONG** — a grep of mine that silently matched nothing, recorded here so it is not
re-derived. All three have writers in `Logic.xml` (6, 8 and 37 of them), and
`ENG_MAG_FOUL_PWR:nX` is `sqrt(DAMAGE_MAG_FOUL:nX / 100) × 0.8`, so its resting **0 is a
clean plug**, not a dead variable. A zero proves nothing either way; find the writer.

```bash
# the dump comes from the env-gated CowsDA40VariableDumpTests
MSFSBA_DUMP_VARS=/tmp/d dotnet test … --filter DumpVariableSurface
grep -oE '(get|set):\s*L:[A-Za-z0-9_/]+(:[A-Za-z0-9]+)?' COWS_DA40XLS.yaml \
  | sed -E 's/^(get|set):\s*L://' | sort -u > yaml.txt
cut -f3 /tmp/d/da40-XLS-vars.tsv | sort -u > bound.txt
comm -23 yaml.txt bound.txt
```

⚠️ The name pattern must include `/` and must NOT require a leading `(` — and it still
truncates the two names carrying a SPACE (`L:GENERAL ENG FAILED:1`,
`L:KOHLSMAN SETTING HG:2`), which surface as bare `GENERAL` and `KOHLSMAN`. Both are
already bound.

### Rule 1 — a quantity BEHIND an indication is not bound

`CHT_C:1-4`, `CHT_PROBE:1-4`, `EGT_PROBE:1-4`, `EGT_DELTA:1-4` and `OT_PROBE` are all
listed and all deliberately absent. Each sits behind an indication this definition already
reads (`DISP_CHT:n`, `DISP_EGT:n`, `DISP_LEAN_DELTA:n`, the oil-temperature gauge), and the
XLS models `FAILURES_DISP_CHT` / `FAILURES_DISP_EGT` — so a probe row would show a blind
pilot a perfect temperature off a dead gauge, defeating a failure class COWS built on
purpose. A quantity with **no** indication is exposed from the model, because there is
nothing to defeat. Pinned both ways by `CowsDA40XlsEngineDetailTests`.

### Rule 2 — sixteen names are not in the installed package

The same sixteen the NG's file lists and the NG package lacks: `AFCS_FAIL_AIL/ELE/TRIM`,
the whole `FAILURES_SENS_*` family, `LIGHTING_PANEL_1`, `LIGHTING_GLARESHIELD_1`,
`ATT_CAGE_IsDown`, `RESET_ECU`. A row bound to one sits at 0 for ever and reports "no
failure" about a system nothing watches — worse than absent, because a pilot scans it and
is reassured.

### Rule 3 — ⚠️ THE XLS SPELLS BLOCK AND OIL DAMAGE WITHOUT AN INDEX

`DAMAGE_BLOCK`, `DAMAGE_OIL` and `HEALTH_OIL` are **unindexed** here. The NG's
`DAMAGE_BLOCK:1` / `DAMAGE_OIL:1` / `HEALTH_OIL:1` do not exist on this airframe at all, so
inheriting the NG spelling reads a phantom 0 — indistinguishable from an undamaged engine.
The full XLS set is `DAMAGE_{BLOCK,OIL,DUST,ENABLED,DISABLED,BLOCK_FAC}`,
`DAMAGE_CYL:1-4`, `DAMAGE_CYL_FAC:1-4`, `DAMAGE_MAG_FOUL:{1-4}{L,R}`,
`DAMAGE_MAG_FOUL_RATE:1-4`, `DAMAGE_REDBOX_{FAC,ITS,LOP,ROP}:1-4`, `HEALTH_CYL:1-4`,
`HEALTH_OIL` — and **no** `HEALTH_BLOCK`, `HEALTH_FUEL`, `DAMAGE_FUEL` or `DAMAGE_TURBO`.

The `DAMAGE_REDBOX_*` set is read and deliberately **not** shown: `ITS:n` holds its last
value once the box closes (19.7 with the engine stopped), so a row would report a box that
shut minutes ago. MSFSBA recomputes the box from its inputs.

## The Engine Variation panel (Simulation)

⚠️ **This is the set whose all-zero state makes the aeroplane silently unstartable** — the
trap documented above. Fuel pressure is a PRODUCT of `FUEL_SPREAD_PRESSURE` and the idle
jet is multiplied by `SPREAD_INJ_TRIM`, so a zero spread is zero fuel, structurally and for
ever, with no CAS message and no failure flag. `SPREAD_SET` and `CYL_SPREAD_SET` are the
latches that gate regeneration and both read 1 — "already done" — in the trapped state, so
the panel shows them and says to judge by the numbers, not by the latch.

Read-only, one row each, with the model's OWN generation range (Logic 5540ff) in the help
line — without it the number means nothing, and telling a healthy engine from the trapped
one is the whole point of the panel:

| Reading | Rolled as |
|---|---|
| `FUEL_SPREAD_PRESSURE` | 1.45 – 1.55 |
| `SPREAD_INJ_TRIM` | the MEAN of the four `CYL_SPREAD_INJ` |
| `SPREAD_OP` | 0.95 – 1.05 |
| `SPREAD_OC` | 0.9 – 1.1 |
| `OP_SPREAD_BYPASS` | −0.1 – +0.1 |
| `SPREAD_ROUGH` | 650 – 700 — a THRESHOLD, not a multiplier |
| `MAG_SPREAD_TIMING` | −1.5 – +1.5 |
| `THROTTLE_SPREAD` | 0 – 0.04 |
| `PROP_SPREAD_LO` / `_HI` | 1450 – 1470 / 2670 – 2690 rpm (the governor's travel) |
| `CYL_SPREAD_EGT:1-4` | 0.98 – 1.02 |
| `CYL_SPREAD_INJ:1-4` | 0.95 – 1.05 |
| `CYL_SPREAD_COOL:1-4` | 0.96 – 1.04 |
| `SPREAD_SET`, `CYL_SPREAD_SET` | the two latches, 0/1 |

⚠️ **`SPREAD_AIR`, `SPREAD_ALT` and `SPREAD_ALT_OFF` are NOT engine variation** and are
deliberately on the **Standby Instruments** panel instead. A first pass read them off the
abbreviations and called them "Induction" and "Alternator"; they are neither. `SPREAD_AIR`
(0.99 – 1.01) scales the standby AIRSPEED computation (`Inputs.xml` 1266) and
`SPREAD_ALT`/`_OFF` the standby ALTIMETER as `(indicated + offset) × multiplier`
(`IN.xml` 561ff), the offset being −30 – +30 ft. They are rolled by the same block, and the
unstartable trap turns on the fact that these self-heal while the engine's do not — so they
belong beside the instruments they explain, which is also why the panel is named for the
ENGINE.

## Priming, line by line (Priming panel)

The Lycoming has no primer: the idle jet loads the induction with the pump on and the
mixture forward. MSFSBA already read the charge sitting OUTSIDE the cylinders; what it
never read is the **fuel system itself filling**. Thresholds are the model's own:

| Reading | Primes above | Drops below |
|---|---|---|
| `ENG_FUEL_SYSTEM_GRAM` → `ENG_FUEL_SYSTEM_PRIMED` | 18 g (capped at 20) | 1 g |
| `ENG_FUEL_LINE_GRAM:S` → `ENG_FUEL_LINE_PRIMED:S` (the spider) | 2.49 g | 0.2 g |
| `ENG_FUEL_LINE_GRAM:1-4` → `ENG_FUEL_LINE_PRIMED:1-4` | 1.99 g | 0.2 g |

Plus `START_MIXTURE_START`, which the cockpit start sequence sets.

## Per-plug fouling power (Magnetos panel)

MSFSBA already read how fouled each plug IS (`DAMAGE_MAG_FOUL:{1-4}{L,R}`);
`ENG_MAG_FOUL_PWR:{1-4}{L,R}` is what that fouling COSTS it, and it is the multiplier the
firing test is actually compared against (`ENG_MAG_PWR:L/R × (1 − FAILURES_MAG) × rand ×
ENG_MAG_FOUL_PWR > ENG_MAG_PWR_REQ`). `DAMAGE_MAG_FOUL_RATE:1-4` is how fast it is
accumulating.

## The oil cooler (Power and Levers)

`OC_TEMPERATURE` (°C) and `OC_THERMOSTAT` have no gauge on this aeroplane, so both are read
from the model — the oil TEMPERATURE does have one and is read from its indication, which
is why `OT_PROBE` stays unbound under Rule 1.

## The XLS display walk — what `tools/coherent-coverage.js` found

Swept both displays against the live XLS. **PFD: 0 page-specific unread items across 15
real pages. MFD: 2 across 17**, and both are deliberate.

Two real holes closed, both the SAME trap — *the first `querySelector` match is not always
the one on screen* — and neither findable by reading code:

- **The XLS Engine page's △ Peak.** Each bar chart has its own delta-from-peak block, and
  the EGT chart's is `display:none` outside lean assist while the CHT chart's is on screen.
  A single `querySelector(".bar-chart-delta-peak")` read the hidden one, so the number a
  sighted pilot could see was never spoken. Both charts are now walked and named.
- **A row named after something not on screen.** The PFD's Hold At dialog draws EITHER a
  leg distance or a leg time and keeps both in the DOM; the hidden "4.0 NM" pair registers
  as the row's FIRST control, so `A.M.nameByRow` named the visible time digits after it and
  the readout said **"4.0: 1"** — announcing a four-mile leg to a pilot holding for one
  minute. Two guards now: a field that is not SELECTABLE cannot name its row, and neither
  can a value with no LETTER in it (a minute digit named the seconds digits).

The two MFD survivors:

| Survivor | Why it stays |
|---|---|
| `x100` | The RPM dial's scale legend. The strip already reads the real value ("RPM: 1020"); a legend on a dial says nothing about the aeroplane. |
| `CHKLST - Checklist` | The nav data bar's own abbreviation for a page MSFSBA already announces as "EIS - Checklist". |

⚠️ **RESIDUAL, NOT FIXED, and it wants flying before it is guessed at.** The Hold At
dialog's leg time is three separate spinner controls — the minute and the two seconds
digits — so it reads as three rows ("1", "Leg Time1: 0", "Leg Time1: 0") rather than one
"Leg Time 1:00". The whole hold still reads correctly in the block below the fields
("Hold South of Course 000° Inbound Leg Time 1:00 Turns Right"), so nothing is missing;
what is wrong is how the three editable digits announce while the knob walks them.

## Oil temperature and fuel pressure — the last two on the physics, and the arcs with them

Both read the model until now, knowingly: `DISP_OT` is FAHRENHEIT while the arc table was
CELSIUS, and `DISP_FP` is PSI while the arcs were BAR, and a band is looked up from the RAW
value — so swapping the source alone would have put a green needle in the red. The recorded
plan was to convert the celsius arcs arithmetically to 149 / 230 / 244 F and fly it.

⚠️ **THAT PLAN WAS WRONG, AND THE AEROPLANE SAYS SO ITSELF.** Both gauges are declared in
the XLS's own `panel/panel.xml`, against the very L:vars in question and in their units:

| Gauge | Source | Scale | Bands |
|---|---|---|---|
| Oil Temp | `L:DISP_OT` | −31 … 295 | red to −22, yellow to **122**, green **122–275**, yellow to 285, red above |
| Fuel Press | `L:DISP_FP` | 0 … 40 | red to **14**, green **14–35**, red above. No yellow |

122 – 275 °F is 50 – 135 °C — a Lycoming. The table being replaced said green 65 – 110 °C,
which is the **Austro's**: MSFSBA had been calling a caution at 111 °C on an engine whose own
gauge stays green to 135, and the arithmetic conversion would have kept calling it at 230 °F.
**Read the aircraft's gauge definition; never convert one airframe's arcs onto another.**

⚠️ **The unit goes in the OVERRIDE, not on the variable.** Both are L:vars, and the
continuous batch hands `SimVarDefinition.Units` straight to SimConnect (`Setup.cs` — the
per-variable OnRequest path hardcodes `"number"`, the batch path does not), so a temperature
or pressure unit there would be converted from a base an L:var does not have. Oil
temperature is declared `"number"` and rendered through `TryUnitText("fahrenheit", …)`, the
same idiom the cylinder-head rows use, so the band comes from the raw Fahrenheit while the
figure follows the pilot's G1000 choice.

## Manual lean assist — driven end to end on the live aircraft

| Step | What it is | Verified |
|---|---|---|
| 1 | MFD **EIS – Engine** page | page title read back as `EIS - Engine` |
| 2 | **Softkey 10, "Assist"** | `1 (>H:AS1000_MFD_SOFTKEYS_10)` over the calculator took `L:DISP_LEAN_ASSIST` 1 → 0 |
| 3 | Lean the mixture | `L:INPUT_MIXTURE` 58.1 → 50: `DISP_EGT:1` 1230 → 1259 with `DISP_LEAN_PEAK:1` following to 1278, `DISP_LEAN_DELTA:1` live at 3 |
| 4 | The call | a cylinder `PeakedAtF` (10 °F) below its own peak is announced once per session; **the first call is the POH's cue** |
| 5 | The row | Mixture and Propeller → **Lean Assist** carries all four at any time |
| 6 | **Set Best Mixture** | the aeroplane's own keybind: Automixture off, it walks the mixture down until the four cylinders average 12.5 : 1 and stops itself; on, it snaps to 72 % |

⚠️ **`DISP_LEAN_ASSIST` IS writable and holds** — measured, no XML writer touches it and
the MFD plugin only reads it — so a panel switch would work. It is deliberately NOT added:
a softkey belongs to the display window under this project's own rule. What changed instead
is that the row now names the key by NUMBER ("MFD Engine page, softkey 10, Assist"), because
that row is the only place a blind pilot learns where it is.

⚠️ **`DISP_LEAN_HIGHLIGHT` and `DISP_LEAN_DELTA_BIGGEST` have NO writer anywhere** — not
in any XML, not in the MFD plugin — so neither is bound. Reading one would give a row at 0
for ever on the one page a pilot leans from.

⚠️ **Clearing the database-acknowledge splash: use a REAL `>`.** Two attempts at
`1 (&gt;H:AS1000_MFD_ENT_Push)` did nothing because the `>` was still HTML-escaped in the
string that reached the RPN parser; the un-escaped form cleared it at once
(`initAcknowledged` true, the screen collapsing to a 0×0 rect under a `hidden` parent). The
agent's own `visible()` already rejects it correctly on both counts — there was no reader
bug, only a malformed command, and half an hour went into looking for the former.
