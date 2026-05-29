# ECAM / System-Display Readout Notes (A320 today → A380X plan)

Investigation date: 2026-05-29. Scope: how MSFSBA exposes FlyByWire A320 ECAM/EWD/SD
content to blind users, the underlying FBW L:vars, the A380X equivalents, and a
recommendation for extending the readout model to the A380X.

---

## (a) How A320 ECAM reading currently works in MSFSBA

**Yes — A320 ECAM reading already exists** and is reasonably mature. Three pieces:

### 1. Upper ECAM / E/WD memo + warning lines  →  `Forms/A32NX/ECAMDisplayForm.cs`
- Opened via hotkey `HotkeyAction.ShowECAM` → `FlyByWireA320Definition.ShowA320ECAMDisplay`
  → `new ECAMDisplayForm(announcer, simConnectManager)`.
- It calls `SimConnectManager.RequestECAMMessages()` (SimConnectManager.cs:4610), which
  requests **14 numeric-code L:vars** over plain SimConnect (no MobiFlight needed):
  - `L:A32NX_Ewd_LOWER_LEFT_LINE_1..7`  (note: **lowercase `Ewd`** in the A320 build)
  - `L:A32NX_Ewd_LOWER_RIGHT_LINE_1..7`
- Each returns a **9-digit numeric code**, not text. The code is mapped to a string via
  `SimConnect/EWDMessageLookup.cs` (a port of FBW's `EWDMessages.tsx`), e.g.
  `"000000001" → "NORMAL"`. The string carries FWC ANSI color escapes
  (`\x1b<2m`=red/warning, `<4m`=amber/caution, `<3m`=green/memo, `<5m`=white/action,
  `<6m`=cyan/info, `<7m`=gray/condition). `EWDMessageLookup.CleanANSICodes` strips them
  and `GetMessagePriority` extracts the color label appended to each spoken/displayed line.
- The same 14 lines are *also* defined in `FlyByWireA320Definition.GetVariables()`
  (lines ~2139-2236) as `UpdateFrequency.Continuous, IsAnnounced = true` — so they are
  **already auto-monitored and announced in the background** (real-time ECAM call-outs),
  in addition to the on-demand window.
- The form additionally pulls engine N1/N2/EGT/FF (`A32NX_ENGINE_*:1|2`), PACK 1/2 state,
  TOGA thrust limit, and FOB, and formats them at the top of the readout. It is a
  read-only multiline `TextBox` with F5-refresh / ESC-close, subscribing to
  `SimVarUpdated` and `ECAMDataReceived` events.

### 2. Lower ECAM STATUS page  →  `Forms/A32NX/StatusDisplayForm.cs`
- Opened via `HotkeyAction.ShowStatus` → `ShowA320StatusDisplay`.
- Calls `SimConnectManager.RequestStatusMessages()` which reads **36 numeric-code L:vars**:
  `L:A32NX_STATUS_LEFT_LINE_1..18` and `L:A32NX_STATUS_RIGHT_LINE_1..18`
  (defined in the A320 definition ~lines 2240-2365+, `UpdateFrequency.OnRequest`,
  `IsAnnounced = false` — display-only, not auto-announced).
- Same numeric-code → `EWDMessageLookup.GetMessage` → ANSI-strip → color-label pipeline.

### 3. ECAM control-panel state (not page content)
- `FlyByWireA320Definition` defines `ECAM_ENG/APU/BLEED/COND/ELEC/HYD/FUEL/PRESS/DOOR/`
  `BRAKES/FLT_CTL/ALL/STS/RCL/TO_CONF/EMER_CANC/CLR_1/CLR_2` as H:event buttons plus the
  matching `A32NX_ECP_LIGHT_*` LED L:vars, and `A32NX_ECAM_SFAIL` (warning-page enum).
  These drive the SD page *selection* (which page is shown), not the page's text content.

**Key takeaway:** The A320 SD page content the user hears is NOT the actual SD page; it is
the EWD memo/warning *line* codes plus a handful of hand-picked system L:vars
(engine/PACK/fuel). The genuine SD system-page numbers (per-tank fuel, bleed temps, hyd
pressures, etc.) are mostly *not* surfaced — only the EWD line memos and STATUS lines are.

---

## (b) Concrete A320 ECAM/EWD/SD readable L:vars (from FBW source)

There is **no `a320-simvars.md`** in the local `fbw-a32nx/docs` tree (the path in the task
does not exist); the authoritative source is the code. The readable families used today:

| Purpose | L:var family | Encoding |
|---|---|---|
| E/WD memo/warning lines (upper ECAM lower half) | `A32NX_Ewd_LOWER_LEFT_LINE_1..7`, `A32NX_Ewd_LOWER_RIGHT_LINE_1..7` | 9-digit numeric code → `EWDMessages` dict |
| ECAM STATUS page lines (lower ECAM) | `A32NX_STATUS_LEFT_LINE_1..18`, `A32NX_STATUS_RIGHT_LINE_1..18` | numeric code → same dict |
| ECAM warning page selector | `A32NX_ECAM_SFAIL` | enum (-1..12 = none/ENG/BLEED/…/CRUISE) |
| ECAM CP LEDs | `A32NX_ECP_LIGHT_*` | bool |
| Engine params (used in readout) | `A32NX_ENGINE_N1/N2/EGT/FF:1|2` | number |
| Thrust limit | `A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA` (and `_TYPE/_IDLE/_REV`) | number |
| Flight phase (gates memos) | `A32NX_FWC_FLIGHT_PHASE` | enum |

The numeric-code scheme is the **legacy FWC** model: simple flat L:vars per line, each an
integer the display layer looks up in a static dictionary. This is what makes the A320
easy to read over SimConnect — every line is an independent scalar L:var.

---

## (c) A380X equivalents + extra A380 displays

Source: `fbw-a380x/docs/a380-simvars.md` and
`fbw-a380x/src/systems/instruments/src/{EWD,SD,MFD,OIT,PFD,ND}/`.

The A380X is a **much newer architecture** with two distinct content categories:

### c.1 EWD MEMOs — still flat numeric-code L:vars (READABLE)
`EWD/shared/EwdSimvarPublisher.tsx` maps:
- `memo_left`  → `L:A32NX_EWD_LOWER_LEFT_LINE_#index#`
- `memo_right` → `L:A32NX_EWD_LOWER_RIGHT_LINE_#index#`

Differences vs A320:
- **`EWD` is UPPERCASE** (A320 build uses `Ewd`).
- **10 lines per side, not 7** (`WdMemos.tsx`: `Array(10)`, `memo_left_1..10`).
- Codes resolved via `MsfsAvionicsCommon/EcamMessages/index.ts` → `EcamMemos` dict, with
  `padEWDCode()` zero-padding. This is a **different, larger dictionary** than the A320's
  `EWDMessageLookup` and must be re-ported (or extended) for the A380.
- Same ANSI/FWC color-escape convention, so the existing `CleanANSICodes` /
  `GetMessagePriority` logic in `EWDMessageLookup.cs` is reusable.

### c.2 EWD WARNINGS / CAUTIONS / abnormal procedures — NOT L:vars (EventBus only)
This is the **critical architectural change.** On the A380 the actual warnings, cautions,
abnormal-sensed procedures, INOP SYS list, limitations, INFO, and landing-perf alerts are
**not exposed as SimConnect-readable L:vars.** They travel only over the in-process JS
EventBus as structured objects, published by the FWS core and consumed inside the avionics
JS (`MsfsAvionicsCommon/providers/FwsPublisher.ts`, interface `FwsEvents`):
- `fws_abn_sensed_procedures: ChecklistState[]` — the abnormal procedure checklists
- `fws_active_procedure`, `fws_active_item`, `fws_show_from_line`
- `fws_normal_checklists: ChecklistState[]`, `fws_show_normal_checklists`
- `fws_inop_sys_all_phases: string[]`, `fws_inop_sys_appr_ldg`, `fws_inop_sys_redundancy_loss`
- `fws_limitations_all_phases / _appr_ldg: string[]`
- `fws_information: string[]`, `fws_alerts_impacting_ldg_perf: string[]`
- `fws_show_failure_pending / _sts_indication / _adv_indication: boolean`
- `fws_*_attention_getter*: boolean[]`

`ChecklistState` is `{ id, procedureActivated, itemsChecked[], itemsToShow[], itemsActive[], … }`;
the *text* lives in the static `EcamAbnormalSensedProcedures` / `EcamAbnormalProcedures`
dictionaries keyed by `id`, joined to live state at render time (see
`EWD/elements/WdAbnormalSensedProcedures.tsx`, `WdNormalChecklists.tsx`,
`WdLimitations.tsx`). **None of this is a SimVar** — SimConnect cannot read it directly.
A bridge (see recommendation) would be required to surface it.

### c.3 SD (System Display) pages — real per-system L:vars (READABLE)
SD pages (`SD/Pages/{Apu,Bleed,Cond,Doors,ElecAc,ElecDc,Engine,Fctl,Fuel,Hyd,Press,Status,
Wheel,Video,Cb,Generic}`) read **individual system L:vars / ARINC429 words**, all readable
over SimConnect. Examples from `SD/Pages/FuelPage.tsx`:
- `L:A32NX_FUEL_USED:1..4`, `L:A32NX_ENGINE_FF:1..4`, `L:A32NX_APU_FUEL_USED`
- ARINC429 quantity words: `L:A32NX_FQMS_FEED_1..4_TANK_QUANTITY`,
  `L:A32NX_FQMS_LEFT/RIGHT_OUTER/MID/INNER_TANK_QUANTITY`, `L:A32NX_FQMS_TRIM_TANK_QUANTITY`,
  pump words `L:A32NX_FQMS_LEFT/RIGHT_FUEL_PUMP_RUNNING_WORD`, and FQDC mirror set.
- The other pages follow the same pattern (Bleed temps/pressures, Hyd Green/Yellow
  pressures, Elec AC/DC bus volts/amps, Press cabin alt/VS/Δp, Cond zone temps, Doors,
  Wheel brake temps/tyre press, Fctl surface positions). These are concrete scalars/words
  per system — the right primitive to build a blind-readable SD readout from.
- ARINC429 vars need ARINC decoding (`useArinc429Var`): value + SSM validity bits, not a
  plain double. MSFSBA must decode the word (low 19 bits as the value, sign/scale per
  label) rather than read it as a raw number.

### c.4 Page selection + extra A380 displays
- `A32NX_ECAM_SD_CURRENT_PAGE_INDEX` (enum, doc §ECAM Control Panel ATA 31):
  0 ENG, 1 APU, 2 BLEED, 3 COND, 4 PRESS, 5 DOOR, 6 EL/AC, 7 EL/DC, 8 FUEL, 9 WHEEL,
  10 HYD, 11 F/CTL, 12 C/B, 13 CRZ — note A380 splits ELEC into EL/AC + EL/DC and adds C/B.
- ECAM CP buttons: `A32NX_BTN_{ALL,ABNPROC,CHECK_LH,CHECK_RH,CL,CLR,CLR2,DOWN,EMERCANC,
  MORE,RCL,TOCONFIG,UP}` (0/1).
- **Extra A380 display suite** (beyond A320's PFD/ND/EWD/SD): **MFD** (Multi-Function
  Display — the FMS/flight-plan/perf/surveillance interface, replaces the MCDU; there is
  already an `FBWA380MCDUForm.cs`), **OIT** (Onboard Information Terminal), **RTPI/RMP**,
  plus the standard PFD/ND. The MFD content is again largely EventBus/FMS-state driven, not
  flat L:vars. `SDv2` is a newer in-progress SD rewrite (`SDv2/SDSimvarPublisher.tsx`) — be
  aware the SD var surface may move; target the per-system L:vars, which are stable, rather
  than any SD-internal bus topic.

---

## (d) Recommendation for exposing A380 ECAM (upper/lower) + displays in MSFSBA

Build the A380 readout in **three tiers**, mirroring the A320 forms but tracking the
A380's split architecture, and make the windows **auto-refresh** rather than F5-only.

1. **EWD memos (upper ECAM, lower half) — directly portable.**
   Add `A32NX_EWD_LOWER_LEFT_LINE_1..10` and `A32NX_EWD_LOWER_RIGHT_LINE_1..10` (uppercase
   `EWD`, 10 lines) to `FlyByWireA380Definition.GetVariables()` as
   `Continuous + IsAnnounced` (background call-outs) and add an `RequestEWDMessages()` A380
   variant. Port FBW's `EcamMemos` dict (from `EcamMessages/index.ts`, with `padEWDCode`)
   into a new `EWDMessageLookupA380` (or extend `EWDMessageLookup`); reuse the existing
   `CleanANSICodes` / `GetMessagePriority` ANSI pipeline unchanged. Implement
   `ISupportsECAM` on the A380 definition and clone `ECAMDisplayForm` as an A380 form.

2. **SD system pages (lower ECAM) — new, the biggest value-add.**
   Drive a per-page readout from the real system L:vars listed in (c.3). Add the relevant
   `A32NX_FQMS_*`, `A32NX_FUEL_USED:n`, bleed/hyd/elec/press/cond/wheel/fctl L:vars to the
   A380 definition (OnRequest for the window; Continuous only for the few worth
   auto-announcing). Build an SD readout form that lets the user pick a page (mirroring
   `A32NX_ECAM_SD_CURRENT_PAGE_INDEX`'s ENG/APU/BLEED/COND/PRESS/DOOR/EL-AC/EL-DC/FUEL/
   WHEEL/HYD/FCTL/CB/CRZ taxonomy) and reads that page's decoded values. **Add an
   ARINC429-word decode helper** in SimConnectManager (value + SSM-valid check) since many
   A380 SD quantities are ARINC429, not plain doubles — reading them raw gives garbage.
   This finally gives blind users the actual SD content the A320 never fully exposed.

3. **Warnings / cautions / abnormal procedures (the heart of the upper ECAM) — needs a
   bridge; do NOT expect L:vars.**
   These live only on the JS EventBus (`fws_abn_sensed_procedures`, `fws_active_procedure`,
   `fws_inop_sys_*`, `fws_limitations_*`, `fws_information`, etc.). SimConnect cannot read
   them. Two options, in order of effort:
   (i) **Short term:** approximate from what *is* readable — the EWD memo lines (tier 1)
       already include many memos/cautions, plus `A32NX_FWC_FLIGHT_PHASE`, the
       `fws_show_failure_pending/sts/adv` indications if a backing L:var exists, and Master
       Warning/Caution + per-system warning enums. This covers a useful subset without a
       mod.
   (ii) **Full fidelity:** add a small **JS bridge** in the spirit of the existing PMDG EFB
       bridge (`zzz-pmdg-efb-accessibility` + `EFBBridgeServer` on localhost) — an MSFS
       community package that hooks the A380 EWD instrument's EventBus, serializes the
       `FwsEvents` objects (joining `ChecklistState.id` to the `EcamAbnormalSensedProcedures`
       text dictionary on the JS side, or shipping ids + a ported dict on the C# side), and
       POSTs them to a local HTTP listener in MSFSBA. This is the only way to get the live
       abnormal-procedure checklists, INOP SYS list, and limitations verbatim. Reuse the
       EFB-bridge pattern (HttpListener, Coherent-GT-safe JS, version-stamped auto-update).

**Net:** Tiers 1 and 2 are straightforward SimConnect/L:var work and deliver memos + full
SD pages. Tier 3 (real warnings/abnormal procedures) is fundamentally different from the
A320 — it is EventBus-only and requires a JS bridge, not just more L:var requests. Plan the
A380 ECAM feature around that split rather than assuming the A320's flat-L:var model carries
over.
