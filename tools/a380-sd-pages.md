# FlyByWire A380X — ECAM SD page readouts + ARINC429 decode (for C#/SimConnect)

Reference for building blind-accessible System Display (SD) readouts from an external app.

## Sources

- ARINC429 word layout / decode (authoritative):
  - `fbw-aircraft/fbw-a380x/src/wasm/fbw_a380/src/Arinc429.cpp`
  - `fbw-aircraft/fbw-a380x/src/wasm/fbw_a380/src/Arinc429.h`
- TS wrapper used by the SD pages: `fbw-aircraft/fbw-a380x/src/systems/instruments/src/Common/arinc429.tsx`
  (`useArinc429Var(name)` = `new Arinc429Word(useSimVar(name,'number'))` from `@flybywiresim/fbw-sdk`)
- SimVar catalogue: `fbw-aircraft/fbw-a380x/docs/a380-simvars.md`
- SD page sources: `fbw-aircraft/fbw-a380x/src/systems/instruments/src/SD/Pages/**`

All L:var names below carry the `A32NX_` prefix even on the A380X (legacy naming). Read every L:var
from SimConnect as a `double` ("number"/Units don't matter for the raw read — see decode note).

---

## 1. ARINC429 decode recipe (C#)

FBW packs a 32-bit ARINC429 word into the 64-bit double held by the L:var as follows:

- Read the L:var as a `double` (SimConnect `FLOAT64`).
- Cast/reinterpret that double to a `UInt64` **by numeric value** (NOT by bit-reinterpretation):
  the C++ does `static_cast<uint64_t>(simVar)` — i.e. it truncates the double's *numeric* value to an
  integer. So in C#: `ulong u64 = (ulong)theDouble;`  (`Convert.ToUInt64` / a checked cast — do not use
  `BitConverter`).
- **Low 32 bits = the payload, reinterpreted as an IEEE-754 `float`** (the value):
  `uint u32 = (uint)(u64 & 0xFFFFFFFF); float value = BitConverter.Int32BitsToSingle((int)u32);`
- **Bits 32–33 = the SSM (Sign/Status Matrix)**: `uint ssm = (uint)(u64 >> 32);`
- SSM meanings (2-bit enum): `0b00 FailureWarning`, `0b01 NoComputedData`, `0b10 FunctionalTest`,
  `0b11 NormalOperation`.
- **"Valid / usable"** = `NormalOperation (0b11)`. FBW's `valueOr()` treats both `NormalOperation` and
  `FunctionalTest (0b10)` as data-present; for a readout, prefer requiring `0b11` and announcing
  "invalid"/"XX" otherwise. `isNoComputedData` = `0b01`, `isFailureWarning` = `0b00`.

Important nuance: the *value* is the full 32-bit float — FBW does **not** use the classic 19-bit BNR
field with a per-label scale factor. The encoder simply stores a `float` in the low word, so there is
**no scaling to apply**: the decoded float is already in engineering units (kg, °C, psi, ft, %, etc.).

Discrete words: same container, but the low 32 bits are an integer bitfield (stored via float). To test
a bit, reinterpret the low word as uint and read bit `(n-1)` (FBW uses 1-based bit numbers, ARINC labels
bits 1..32): `bool b = ((u32 >> (n-1)) & 1) != 0;`. FBW's `bitValueOr(n, default)` returns the bit only
when SSM is NormalOperation or FunctionalTest, else the default.

Minimal C# helper:

```csharp
public readonly struct Arinc429Word {
    public readonly uint Ssm;      // 0..3
    public readonly float Value;   // engineering units, already scaled
    private readonly uint _raw32;  // low word as integer (for discretes)

    public Arinc429Word(double simVar) {
        ulong u64 = (ulong)simVar;            // numeric truncation, matches FBW
        _raw32 = (uint)(u64 & 0xFFFFFFFF);
        Value  = BitConverter.Int32BitsToSingle((int)_raw32);
        Ssm    = (uint)(u64 >> 32);
    }
    public bool IsNormalOperation => Ssm == 0b11;
    public bool IsFailureWarning  => Ssm == 0b00;
    public bool IsNoComputedData  => Ssm == 0b01;
    public float ValueOr(float d) => (Ssm == 0b11 || Ssm == 0b10) ? Value : d;
    public bool BitValueOr(int n, bool d) =>
        (Ssm == 0b11 || Ssm == 0b10) ? ((_raw32 >> (n - 1)) & 1) != 0 : d;
}
```

Plain (non-ARINC) L:vars: read normally as a double and use directly (no SSM). The "type" column below
flags which is which. For discrete words, individual bit meanings are in `a380-simvars.md`.

---

## 2. Per-SD-page readable values

Notation — type: **D** = plain double L:var (read directly); **A429** = ARINC429 word (decode per §1);
**A429-Disc** = ARINC429 discrete bitfield. Tank/zone/engine indices abbreviated.

### FUEL  (`FuelPage.tsx`, simvars "Fuel ATA 28")  — 18 values
- Per-tank qty `A32NX_FQMS_{tank}_TANK_QUANTITY` — A429, kg. tank ∈ {FEED_1..FEED_4, LEFT_OUTER, LEFT_MID, LEFT_INNER, RIGHT_OUTER, RIGHT_MID, RIGHT_INNER, TRIM} (11 vars). Backups: `A32NX_FQDC_1_/FQDC_2_{tank}_TANK_QUANTITY` if FQMS invalid.
- `A32NX_FQMS_TOTAL_FUEL_ON_BOARD` — A429, kg (FOB)
- `A32NX_FQMS_GROSS_WEIGHT` — A429, kg (aircraft GW)
- `A32NX_FQMS_CENTER_OF_GRAVITY_MAC` — A429, % MAC (CG)
- `A32NX_TOTAL_FUEL_QUANTITY` — D, kg (sim total; simpler fallback)
- `A32NX_FUEL_USED:{1..4}` — D, kg per engine; `A32NX_APU_FUEL_USED` — A429, kg
- `A32NX_ENGINE_FF:{1..4}` — D, kg/h per engine (also used for ALL ENG FF)

### ENGINE  (`EngPage.tsx`, `EngineColumn.tsx`)  — 7 values/engine (engine ∈ 1..4)
Note: A380X RR engines display **N2 and N3** (no N1 readout). All plain D unless noted.
- `A32NX_ENGINE_N2:{e}` — D, %
- `A32NX_ENGINE_N3:{e}` — D, %
- `A32NX_ENGINE_FF:{e}` — D, kg/h
- `A32NX_ENGINE_OIL_QTY:{e}` — D, qt
- `GENERAL ENG OIL TEMPERATURE:{e}` — D, °C (sim var; amber >177)
- oil pressure (psi) — see `OilPressureGauge.tsx` (`A32NX_ENGINE_OIL_PRESSURE:{e}` family)
- `TURB ENG VIBRATION:{e}` — D, vib (used for N1/N2/N3 vib rows)
- State: `A32NX_ENGINE_STATE:{e}` — D enum (0 off … running); `A32NX_PNEU_ENG_{e}_STARTER_VALVE_OPEN` — D bool

### BLEED  (`Bleed/*`)  — 5 values/engine + crossbleed
- `A32NX_PNEU_ENG_{e}_PRECOOLER_OUTLET_TEMPERATURE` — D, °C (bleed/precooler outlet temp)
- `A32NX_PNEU_ENG_{e}_REGULATED_TRANSDUCER_PRESSURE` — D, psi (precooler inlet/regulated press)
- `A32NX_PNEU_ENG_{e}_PR_VALVE_OPEN`, `_HP_VALVE_OPEN` — D bool
- Pack outlet temp: `A32NX_COND_PACK_{1,2}_OUTLET_TEMPERATURE` — D, °C
- Crossbleed valves: `A32NX_PNEU_XBLEED_VALVE_{L,C,R}_OPEN` — D bool
- APU bleed: `A32NX_APU_BLEED_AIR_VALVE_OPEN` — D bool; `A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE` — D, psi

### HYD  (`Hyd/*`)  — 4 values/system (system ∈ GREEN, YELLOW; A380X is 2x electric, no Blue/engine pumps)
- `A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE` — D, psi (main system pressure)
- `A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE_SWITCH` — D bool (pressurised)
- `A32NX_HYD_{sys}_RESERVOIR_LEVEL` — D, gallons (×3.785 = litres; UI caps display at 70 L)
- `A32NX_HYD_{sys}_RESERVOIR_LEVEL_IS_LOW` — D bool; also `_RESERVOIR_OVHT`, `_RESERVOIR_AIR_PRESSURE_IS_LOW` — D bool
- Elec pump status: `A32NX_HYD_{G,Y}{A,B}_EPUMP_ACTIVE`, `_EPUMP_OVHT` — D bool

### ELEC AC  (`ElecAc/*`)  — 3 values/generator + bus powered flags
- Engine gens (bus 1..4): `A32NX_ELEC_ENG_GEN_{n}_POTENTIAL` — D, V; `_LOAD` — D, % load
- APU gens (1,2): `A32NX_ELEC_APU_GEN_{n}_POTENTIAL` V, `_LOAD` %, `_FREQUENCY` Hz — D
- Ext pwr: `A32NX_ELEC_EXT_PWR_POTENTIAL` V, `_FREQUENCY` Hz — D
- Emer gen (RAT): `A32NX_ELEC_EMER_GEN_POTENTIAL` V, `_LOAD` % — D
- Static inverter: `A32NX_ELEC_STAT_INV_POTENTIAL` V, `_FREQUENCY` Hz — D
- Bus powered flags: `A32NX_ELEC_AC_{bus}_BUS_IS_POWERED` — D bool

### ELEC DC  (`ElecDc/*`)  — 2 values/source
- TR units (bus name): `A32NX_ELEC_TR_{n}_POTENTIAL` — D, V; `_CURRENT` — D, A
- Batteries (1..4 / ESS / APU): `A32NX_ELEC_BAT_{n}_POTENTIAL` — D, V; `_CURRENT` — D, A
- Bus boxes: `A32NX_ELEC_{bus}_POTENTIAL` V, `_CURRENT` A — D
- Bus powered flags: `A32NX_ELEC_DC_{bus}_BUS_IS_POWERED` — D bool

### PRESS  (`Press/*`, simvars lines ~320-356)  — 5 values (system ∈ B1..B4 redundant, pick a valid one)
- `A32NX_PRESS_CABIN_ALTITUDE_B{n}` — A429, ft (cabin altitude); target `A32NX_PRESS_CABIN_ALTITUDE_TARGET_B{n}` — A429, ft
- `A32NX_PRESS_CABIN_VS_B{n}` — A429, ft/min (cabin V/S); target `A32NX_PRESS_CABIN_VS_TARGET_B{n}` — A429, ft/min
- `A32NX_PRESS_CABIN_DELTA_PRESSURE_B{n}` — A429, psi (delta-P)
- Outflow valve % open: `A32NX_PRESS_OCSM_{n}_...` outflow A429 word (see OutflowValve.tsx)
- Manual fallbacks (D): `A32NX_PRESS_MAN_CABIN_ALTITUDE` ft, `A32NX_PRESS_MAN_CABIN_VS` ft/min, `A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE`

### COND  (`Cond/*`)  — ~18 values
- Cockpit: `A32NX_COND_CKPT_TEMP` — D, °C
- Main deck zones: `A32NX_COND_MAIN_DECK_{1..8}_TEMP` — D, °C (8 zones)
- Upper deck zones: `A32NX_COND_UPPER_DECK_{1..7}_TEMP` — D, °C (7 zones)
- Cargo: `A32NX_COND_CARGO_FWD_TEMP`, `A32NX_COND_CARGO_BULK_TEMP` — D, °C
- (UI shows min/max across decks; expose per-zone for accessibility)

### WHEEL  (`Wheel/*`)  — 1 value type (per wheel) + gear/steering states
- Brake temps: `A32NX_REPORTED_BRAKE_TEMPERATURE_{n}` — D, °C (per brake unit n; A380X has many bogies)
- Gear/door positions, steering: D (no tyre-pressure L:var is modelled — tyre press not available)

### APU  (`Apu/*`)  — 6 values
- `A32NX_APU_N` — A429, % (APU N / RPM)
- `A32NX_APU_N2` — A429, % (second spool, where modelled)
- `A32NX_APU_EGT` — A429, °C; warning limit `A32NX_APU_EGT_WARNING` — A429, °C
- `A32NX_APU_FUEL_USED` — A429, kg
- `A32NX_APU_LOW_FUEL_PRESSURE_FAULT` — A429-Disc
- Bleed press: `A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE` — D, psi; gen load/V/Hz under ELEC APU GEN vars above
- States (D bool): `A32NX_OVHD_APU_START_PB_IS_AVAILABLE`, `A32NX_OVHD_APU_MASTER_SW_PB_IS_ON`, `A32NX_APU_FLAP_OPEN_PERCENTAGE` (D, %)

---

Notes:
- Many CPIOM "discrete words" (`A32NX_COND_CPIOM_B{1..4}_{AGS,TCS,VCS,CPCS}_DISCRETE_WORD`) drive valve/
  fault icons on Bleed/Cond/Press; decode as A429-Disc and consult `a380-simvars.md` bit tables only if you
  need valve-open / fault annunciation. They are not scalar readouts.
- For redundant Bx-suffixed words (Press) pick the first source whose SSM == NormalOperation, mirroring the
  page's `valueOr` fallback chain.
