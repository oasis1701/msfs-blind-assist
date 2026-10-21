# Fenix A320 v3 (3.0.0.7426): what it means for MSFS Blind Assist

Investigation of 2026-10-21, on `main` at 2d6444ab1. The previous Fenix measured in this repo was 2.4.0.4720.
Lives on the `FenixV3` branch with the plans (`docs/fenix-v3/plans/`) and the evidence (`docs/fenix-v3/evidence/`);
plan 12 deletes `docs/fenix-v3/` before the branch merges.

How it was checked: the live sim (Fenix A320 CFM WF at KATL, engines running) through the simconnect MCP, the
Fenix data service on 127.0.0.1:8083 (read-only queries), the Coherent debugger (:19999), the installed package
and install folder (read only, nothing executed), Fenix's official V3 release post, and four research agents
(code inventory, name diff against the package, package survey, web research). "Verified live" below means read
in the running sim today; everything else says where it comes from.

## 1. Broken or changed by v3: fix

| # | What the pilot notices | Cause | Evidence | Fix |
|---|---|---|---|---|
| 1.1 | DC SERVICE bus power changes are never announced | Fenix fixed its own typo: `B_ELEC_BUS_POWER_DC_SERIVICE` is now `B_ELEC_BUS_POWER_DC_SERVICE` | Verified live: old name reads 0, new reads 1 (L:var and data service); HubHop's v2 preset uses the typo | Rename (FenixA320Definition.cs:5714) |
| 1.2 | Cockpit door video light never announced | `I_OH_DOOR_VIDEO` renamed `I_OH_COCKPIT_DOOR_VIDEO` | Package model + data service know only the new name; HubHop v2 preset has the old | Rename (:6717) |
| 1.3 | COMMERCIAL upper legend never announced | `I_OH_ELEC_COMMERCIAL_U` gone; only `_L` exists | Package + data service; HubHop v2 had both | Drop `_U` (:1522), check `_L` is covered |
| 1.4 | "Gravity Gear Extension" / "Gravity Gear Crank" (Cockpit Other) do not extend the gear | v3 crank is `S_HYD_GRAVITY_GEAR_EXTEND` 0 to 3 turns in 0.05 steps, plus a stow handle; `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION` is gone. Our 0/1 control writes 1 = a third of the crank | Package template `FNX32X_Interact_Emergency_Gear_Lever_Template` | Redesign the control to crank to 3 and stow back to 0; in-sim test |
| 1.5 | AUTOLAND light read-back may be dead | Model now reads `I_MIP_AUTOLAND_{CAPT,FO}_{U,L}`; the data service still knows the old unsplit name too | Package + data service (all names exist, all 0 now) | In-sim check during an autoland warning which name lights |
| 1.6 | **Risk:** "Takeoff config normal" over a bad config | v3 rebuilt the FWC: MASTER WARN/CAUT now follow its deliberately delayed timing (official changelog). Our panel button and FO hold TO CONFIG 1.5 s and read the CACHED (about 1 Hz) master warning/caution (`FenixTakeoffConfigVerdict`, FenixA320Definition.cs:12749, FenixActionExecutor.cs:223) | Release notes + code | In-sim test with a bad config (e.g. flaps 0) first; likely read fresh over a window, or after release |
| 1.7 | FCU vertical speed spoken every second or two during a managed climb or descent | v3's `N_FCU_VS` carries the live V/S while the window is dashed (`B_FCU_VERTICALSPEED_DASHED` = 1); v2 held it still; nothing gates the announcement | A collaborator's v3 test flight, 2026-10-21: dashed from 13:40, `N_FCU_VS` 3400 → 1700 → 4100 → 3400 between 14:09 and 14:12 | Silence `N_FCU_VS` while dashed (plan 02 Task 10) |

Not broken, checked: the MCDU subscription and key writes (same query shapes, same XML `title`/`line`/`scratchpad`,
key names match the v3 web MCDU), FCU readouts (`N_FCU_*` agree on both paths), the EFB page loading at `/#/efb` (it loads, but a screen reader cannot use it: section 8), ATC MODEL
`A320` (ICAO detection and door offsets), COM 1/2 standby frequencies, battery and bus names other than 1.1, every
`S_CDU*_KEY_*`, the FCU push/pull and encoder convention (`(L:S_FCU_SPEED) --/++`, `E_FCU_*`), 944 of our 1,028 names
found in the package, and the rest of the 68 runtime-only names found live through the data service.

Data-service behaviour change: an unknown name now returns `""`, not null. Nothing shipped depends on it
(the app reads only the MCDU there); any future reader must treat `""` as "unknown". `doors.entry.*` became
`doors.control.*`; `system.config.Units.Weight` is gone (use the settings API's `weight_unit`).

## 2. Broken before v3, surfaced by this investigation

| # | What | Evidence | Fix |
|---|---|---|---|
| 2.1 | 16 `FNX2PLD_*` L:vars come from **FenixQuartz** (Fragtality), a separate tool now discontinued (GitHub 404; its own README called the L:var output deprecated). Not installed here; every one reads 0. **Only one feature depends on them: the FO's takeoff-speeds read-back** (FenixChecklistDefinitions.cs:599-600, polled in FenixStateEvaluator.cs:67). Everything else already has a working native twin in the definition (battery voltage `N_ELEC_VOLT_BAT_1/2`, which the owner heard dip during APU start in v2; `N_FCU_*`; `B_FCU_*_DASHED`; `N_MIP_CLOCK_*`); the `FNX2PLD_*` copies are dead duplicates that never speak and cost 16 data definitions. The ISIS baro has no native twin in the app yet | Web research (FenixQuartz README archives, HubHop "FNX320+FENIXQUARTZ"), live reads, code | Point the FO read-back at `N_MISC_PERF_TO_V1/VR/V2` (already defined; the takeoff-speed hotkey and Takeoff Assist use them); delete the 16 dead definitions and their settings-seed entries; optionally add `N_MIP_ISFD_BARO_HPA/_INCH` |
| 2.2 | The overhead ELAC/SEC/FAC controls are unreachable: `BuildPanelControls` assigns `["Flight Controls"]` twice (FenixA320Definition.cs:8791 and :8990), the pedestal list wins | Code | Give the overhead panel its own name/structure entry |
| 2.3 | "Generator 1" writes `S_OH_ELEC_GEN1_LINE` (the EMER ELEC GEN 1 LINE button) instead of `S_OH_ELEC_GEN1` (:1641, :9425); Generator 2 is right | Live: `S_OH_ELEC_GEN1` exists; package model names both buttons | Point it at `S_OH_ELEC_GEN1` |
| 2.4 | Stale notes: view indices "unmeasured" (:13186, :13205) though measured; docs/first-officer.md:802 names APU lamp `_L` (code uses `_U`); docs/fenix-increment-decrement.md describes `E_PED_RMP*` encoders no longer used | Inventory | Doc fixes |
| 2.5 | Panel controls that move differently from the cockpit (all 443 checked against the v3 model, the uncertain ones measured live, `evidence/panel-sweep.md`): momentary buttons offered as Off/On combos ("On" flips the function whatever its state, "Off" does nothing: bus tie, PA transmit, CALLS …); FCU and baro knob counters stepped from an app-side count (after an app restart, "+1" moved FCU speed -2); baro STD read and written as a state though it is a push/pull counter (after cockpit pulls, the app's "STD" gave QNH); Cargo Discharge "Off" fires agent 2; DCDU brightness ramps; Manual Vertical Speed labels reversed; ADIRS SYS DISP and radar mode positions wrong; N1 MODE registered twice | Model + live measurements, 2026-10-21 | Plan 02 Task 11 (the First Officer's baro read: plan 05 Task 7) |

Native replacements for FenixQuartz (existence verified live on v3 through the data service; units to confirm on the L:var):

| FenixQuartz | Native | Note |
|---|---|---|
| `FNX2PLD_speedV1/VR/V2` | `N_MISC_PERF_TO_V1/VR/V2` | 0 until perf is entered |
| `FNX2PLD_fcuSpd/Hdg/Alt/Vs` | `N_FCU_SPEED/HEADING/ALTITUDE/VS` | already used by the readouts |
| `FNX2PLD_fcuSpdDashed/HdgDashed/VsDashed` | `B_FCU_SPEED_DASHED/HEADING_DASHED/VERTICALSPEED_DASHED` | |
| `FNX2PLD_bat1/2` | `N_ELEC_VOLT_BAT_1/2` | L:var 27.6 (volts); data service 276 (tenths) |
| `FNX2PLD_clockChr/Et` | `N_MIP_CLOCK_CHRONO/ELAPSED` | -1 = not running |
| `FNX2PLD_isisBaro` | `N_MIP_ISFD_BARO_HPA` / `_INCH` | inch value x100 like the FCU |
| `FNX2PLD_isVsActive` | none | FenixQuartz derived it |

## 3. Variants (A319/A321, IAE, future NEO)

- One SimObject `FNX_32X` with 20 presets, all CEO: A320 CFM/IAE SL/WF; A319 CFM/IAE SL/WF HD/SD; A321 CFM/IAE SL/WF SC/TC.
  TITLE e.g. "FenixA320 CFM WF". The preset system predates v3 (Feb/Apr 2026 files).
- **No NEO yet** (Fenix status page: in development), but groundwork ships: `B_CONFIG_IS_NEO`, settings enum
  `engine_family` Cfm56|IaeV2500|CfmLeap|PwGtf, `A320-251NX/271NX`, `A321-251NX/271NX` system packs. 43 of our names
  live only in the CEO cockpit part: re-check them when a NEO arrives.
- Variant L:vars, **verified live on an A320 (3) and an A321 (4)**: `N_CONFIG_AIRCRAFT_TYPE` follows the settings
  `type_code` order, 0 Unknown / 1 A318 / 2 A319 / 3 A320 / 4 A321 (the A321 wing model also keys on 4). Fenix's own
  LOD.js maps 1/2/3 = A319/A320/A321: their bug, not a source. `N_CONFIG_ENGINETYPE` **0 = CFM, 1 = IAE** (read on
  the CFM A320/A321 and the IAE A321; the settings `engine_family` order Cfm56/IaeV2500/CfmLeap/PwGtf suggests 2/3 for
  the future NEO engines). `N_CONFIG_ACT_COUNT` **2** on the IAE A321 with tanks (`act_count` 2, 2,349 kg each).
  `B_CONFIG_IS_NEO` 0 on all three. `B_CONFIG_DCDU` 1 on the A321 livery (Delta N391DN, `dcdu_enabled` True).
  `B_CONFIG_STDBY` 0 = ISIS (read 0 on the ISIS A321; the model shows the ISIS node at 0). About 40
  `B_CONFIG_*`/`N_CONFIG_*` say which equipment the livery fits (DCDU, standby, DDRMI, ADIRS vs GNADIRS, ACP type,
  `N_CONFIG_ACT_COUNT`, units). The settings API gives the same as words (`type_code` A321, `aircraft_variant`
  TwoClass, `is_sharklet`, `standby_type` Isis).
- A321 differences we don't handle (verified in the package's per-type panels and live): the A321 interiors attach
  `Panel_Pneumatic_A321`, which has an **ECON FLOW pushbutton** (`S_OH_PNEUMATIC_ECON_FLOW`, light
  `I_OH_PNEUMATIC_ECON_FLOW_L`) and **no PACK FLOW selector**, and `Panel_Calls_A321`, which adds **CALLS MID and
  EXIT** (`S_OH_CALLS_MID`, `S_OH_CALLS_EXIT`). Our definition has none of the three; its PACK FLOW control has no
  knob on an A321 (the system name still reads 1). ADIRS keypad vs plain ADIRS switches depend on `B_CONFIG_ADIRS`.
- Extra centre tanks (ACT, A321 option; verified on "FenixA321 IAE SL TC", N932AM): the overhead gains the **ACT PUMP**
  pushbutton (`S_OH_FUEL_ACT`, model node `Fuel_ACT_Pump_Button` on `Part_Interior_Panel_Fuel_ACT_Enabled`; lights
  `I_OH_FUEL_ACT_U/_L`, legends unmeasured: read them in the sim, never infer). Fuel quantities
  `aircraft.fuel.ACT1/2.amount.kg` and `.capacity` are on the data service. Exterior: ACT refuel valves
  (`S_EXT_REFUELING_ACT_VALVE_1/2`, NORM/SHUT) and FQI selector. Our definition has none of them, and the fuel
  readouts don't count ACT fuel separately.
- IAE: our N1 MODE switches (`S_OH_ENG_N1_MODE_1/2`) exist, but not their lights (`I_OH_ENG_N1_MODE_1/2_L`), and the
  panel offers them on CFM aircraft too (the real button is IAE-only). **Stock engine SimVars are worse on the IAE.**
  Two paired readings at settled idle (owner confirmed idle), stock vs Fenix: N1 30.9 vs 22.4 %, N2 72.4 vs 62.3 %,
  EGT 506 vs 476 C, FF 1063 pph (482 kg/h) vs 441 kg/h. The stock values are identical for both engines to five
  decimals (a generic sim engine model); the Fenix's differ slightly per engine. On the CFM A320 at idle the stock
  N1/N2 were within about 2 %. The FO's "engine running" test is stock N2 >= 55 % (FenixStateEvaluator.cs:12-19); how
  stock N2 moves during an IAE start is unmeasured, so it may pass before the Fenix start completes. The Fenix's N2
  from the data service (`aircraft.engines.{1,2}.n2`) is the right source; a paired sample through one engine start
  would show the gap.
- The profile "Fenix A320 CEO" (code `FENIX_A320CEO`) covers all of them; nothing tells the pilot or the app which
  variant is loaded. CFM56 thresholds (FO engine running at stock N2 >= 55 %) are applied to IAE too.
- COM 3 reads 124.85 against RMP 3's 118.000 (COM 1/2 match RMP 1/2): check VHF 3 tuning in the sim.

## 4. What we could add

Ordered by value to a blind pilot (my judgement):

1. **Fenix-accurate engine readouts** from the data service: `aircraft.engines.{1,2}.{n1,n2,egt,ff.kg}`,
   `.oil.{pressure,temperature,quantity}`, `aircraft.temperature.tat`. Engine 2 N2, EGT and TAT were null in v2.
   Stock EGT/FF still disagree with the Fenix (live: stock EGT 490 vs Fenix 411 C; stock FF 404 vs Fenix 287 kg/h).
   Also lets the FO detect "engine running" from the Fenix's own N2 instead of stock.
2. **Flight Attendant Panel and cabin**: the FAP is a separate web app at `http://127.0.0.1:8083/fap/index.html`
   (in-sim it is `?target=sim-fap#/fap`). The owner opened it in a browser with the sim loaded (2026-10-21): it
   comes up on a **screensaver**, so hosting the web page like the EFB is not workable for a screen-reader pilot
   (the gateway runs a "FAP screensaver" service, and the purser sends `ScreenOffCommand`/`WakeUpCommand`).
   Checked in the built-in browser the same day (IAE A321 loaded): the screensaver route `#/fap/screen-saver` is a
   single `<img>` of the Cabin Status page (empty accessibility tree); a tap wakes `#/fap/cabin-status`. Awake, the
   pages have headings ("Cabin Status", "Audio", "Lights", "Doors / Slides", "Temperature", "Water / Waste") but the
   section links have no names, values are bare text not tied to their zone ("24.0" twice, "FWD"/"AFT" separate),
   only the 9 bottom-row `FAP-BUTTON`s take keyboard focus and they have no role or name, and the page controls
   (Reset, Area select, the temperature setters) are not focusable. Pages: Audio, Lights (`cabin-lighting`),
   Doors/Slides, Temp., Water/Waste, Smoke Detect., Seat Setting, System Info, plus CAUT, Screen Off, Cabin Ready,
   Cabin Status, EFB. Decision: build our own accessible FAP window from the data service instead: cabin zone temperatures (`aircraft.cabin.{fwd,aft}.temp.*`), cabin lights
   (`aircraft.communication.cids.cabinLights`), cabin ready, water/waste. Note: in v3 a cockpit cabin call no
   longer sets CABIN READY; the virtual purser does (official changelog), which may matter to FO flows.
3. **Cabin announcements and boarding music**: `fap { pram { state { prerecordedAnnouncements, bgmChannels, volume } } }`
   with mutations `directPlay`, `playNext`, `stop`, `setBgmOn`, `setBgmChannel`, `setVolume`. Live: 2 announcements
   ("After Takeoff", "Safety Briefing"), 15 music channels on the A320 loaded first; the channel list is per livery (the IAE A321 Delta livery had one).
4. **Failures window**: `GET http://127.0.0.1:8083/fenix/failures/manual` returns the failures by ATA chapter (549 on the A320 CFM, 555 on the A321 IAE: the catalog follows the variant) with
   titles and armed state (JSON). Arming is `POST /fenix/failures/saveManual` (not probed; read the EFB's call first).
5. **Fenix options, spoken or adapted to** (the EFB has its own searchable Settings app, checked in the built-in browser on 2026-10-21: readable headings, descriptions and search, but every on/off checkbox is named "on", each option ends in an unnamed button, the selected choice of a choice option shows only by colour (no pressed/checked state), and the category list is not keyboard reachable; so our own window is still worth building): `settings { state { settings { key category value possibleValues writable } } }`
   (146 options). Several can disturb our control paths if a pilot turns them on: `encoder_acceleration_enabled`
   (our FCU/baro encoder bursts), `efis_baro_linking_type` (our separate left/right baro sets), `fd_ls_sync_enabled`,
   `channel_spacing_type` (8.33 kHz on this PC), `fcu_readouts_type`, `weight_unit` (Pounds here), `baro_unit`.
   The settings list is also the cleanest variant source (`type_code`, `engine_family`, `is_sharklet`) and
   `settings { softwareVersion }` gives the Fenix version.
6. **Datalink**: CPDLC through Hoppie, SayIntentions or BeyondATC (`acars_service`), a DCDU only on liveries with
   `fnx_dcdu_enabled`, Virtual ACARS (AOC) messages, printing (`Base.txt` template; "Microsoft Print to PDF" or
   Pushover). The MCDU now lists `<ATSU`, readable in our MCDU window already. DCDU screen text is not published;
   FO DCDU keys exist (`S_DCDU2_LSK*`, `MSGUP/DWN`, `PGUP/DN`, `PRINT`). AOC text may be in
   `fenix.aoc.message.uplink` / `downlink.all` (empty now). SayIntentions as the ACARS provider overlaps our
   SayIntentions integration. Deferred by the owner until after FenixV3 (2026-10-21); plan 12 records it as a follow-up.
7. Smaller: registration and SELCAL (`L:FNX_TAIL_LO/HI`, `L:FNX_SELCAL`, 6-bit packed, decoder in
   `Registration.js`); `L:FNX_FLT_LOADED` (1 approach, 2 apron, 3 climb, 4 cruise, 5 final, 6 hangar, 7 runway, 8 taxi)
   for the FO's in-flight start; takeoff perf from `aircraft.fms.perf.takeOff.*`; ground services
   (`groundservice.*`), boarding status, panel state (`control.state.string`); EFB documents app = 607 ECAM procedures
   as text (its Documents app is not keyboard usable, section 8: plan 07 builds an accessible documents window and
   plan 03 makes the EFB's own app readable).
8. **Cockpit controls the app never offered** (owner request; measured live 2026-10-21 on the A321 IAE, every value
   restored): the headset `A320_Sound:Headset_On` (noise cancelling `A320_Sound:ANC` follows it), the clock
   (`S_MIP_CLOCK_CHR/RST/SET/ET/UTC`; chrono in seconds, ET in minutes, -1 blank; a written ET Reset does not spring
   back), the ISIS baro knob push `S_MIP_ISFD_BARO_BUTTON` (no readable state found) and the aft cargo temperature
   knob `A_OH_PNEUMATIC_CARGO_AFT_TEMP` (only with aft cargo heat, `B_CONFIG_CARGOMEAT` ≠ 0). Planned in plan 06 Task 5.

## 5. ECAM text (not pursued)

The gateway now tracks the live ECAM: its log shows `EcamDocumentsService: 6 entries, 1 titled crew alerts
[ENG THR LEVERS NOT SET]` and pins the matching procedure. That data sits in the gateway but is not published as a
data name we know of, and schema introspection was refused on purpose in v2 (not retried, not worked around).
Asking Fenix to publish it was dropped by the owner on 2026-10-21; the AI picture reads stay the route. The
procedure pins themselves are readable (the `pinned_documents` setting), but the owner decided not to announce them
(2026-10-21, index answer 22): without the ECAM itself they are only part of the picture. Plan 07's documents window
lists them and opens one with Ctrl+P.

## 6. For the pilot (not the app)

- The Fenix FMS is running the **bundled AIRAC 2503 (March 2025)**: its v3 navdata check says `AUTH REQUIRED`
  (`C:\ProgramData\Fenix\FenixSim A320\logs\log-navdata.txt`), so the cycle in the old `C:\ProgramData\Fenix\Navdata`
  is unused. Connect Navigraph in the Fenix EFB settings (Connections) or import through MCDU A/C STATUS with files
  in `C:\ProgramData\Fenix\NavdataDFD\Import`. Until then FMS procedures may not match the app's database.
- The ACP knobs now drive Windows app volumes (`acp_process_*_csv`: vPilot on VHF1, GSX on INT, SLC on CAB).
  Keep the screen reader out of those lists.

## 7. Needs the sim (few, prioritised)

1. TO CONFIG with a deliberately bad config (flaps 0): does our panel button and the FO say "check configuration"?
2. Gravity gear crank to 3 turns and back, and which AUTOLAND light lights during an autoland warning.
3. ~~Load an A319 and an A321~~ A321 done 2026-10-21 (type 4, ECON FLOW, CALLS MID/EXIT), and the IAE A321 read
   engine 1; an A319 would confirm type 2.
4. After any FenixQuartz replacement: V-speeds read back once perf is entered.
5. Thrust-lever reverse with the engines at idle (plan 02 Task 11 Step 1): the other panel questions were measured live
   on 2026-10-21 (section 2.5).

Each plan also lists its own few owner checks; these are the investigation's.

## 8. The v3 EFB with a screen reader (tour of 2026-10-21)

Our "Fenix EFB" window hosts the Fenix's own EFB page, and its code still says the page is "already screen-reader
accessible". That was true of v2; the v3 EFB is not. Every app was opened in the same browser engine, read only
(details per app: `evidence/efb-tour.md`). What a blind pilot meets:

- **No way around the EFB by keyboard.** The home-screen app tiles, the app switcher and the home button are not
  buttons and not on the Tab order; the home button and the switcher's icons have no names at all, so once in an app
  there is no way back to the home screen.
- **Switches and boxes without names.** Ground Services' 14 switches (GPU, PCA, stairs, chocks, every door, tug) and
  every on/off option in Settings read "switch, on" / "checkbox, on"; the Takeoff and Landing entry boxes read their
  placeholders ("----"), their drop-downs read nothing; Mass and Balance's boxes read only their limits ("Max 180").
- **Selections shown by colour only:** Settings' picture choices (GNADIRS/ADIRS, hPa/inHg …), Landing's runway
  condition code, failure armed/failed state.
- **Two dead ends.** The MCDU and FAP tiles leave the EFB for pages a screen reader cannot use (the web MCDU is two
  canvases; the FAP opens on its screensaver) with no way back. Our MCDU window and plan 08's Cabin window cover them.
- **Silent progress and messages:** loading notices and progress bars are plain text; nothing is spoken.
- Out of reach by nature: the Charts moving map.

How to fix, in order of value:

1. **An accessibility layer our EFB window injects**, as `IFlyEfbForm` already does for the iFly EFB: give tiles, the
   switcher, the home button and the collapsible lists button roles, names and Tab stops (Enter and Space press them;
   Alt+Home goes home anywhere); name every switch, box and drop-down from the text the page shows beside it; expose
   selected and open states from the page's own classes, kept current by a MutationObserver; name the unnamed icon
   buttons; speak loading notices. Tested offline with jsdom against trimmed live page captures, like
   `tools/md11-efb-test`. Ground Services, Mass and Balance, Panel States, Takeoff and Landing have no other accessible
   path in the app, so they come first; Settings, Documents and Failures get the layer too, beside plans 11, 07 and 10.
2. **The window steps in at the dead ends:** when the page navigates to the MCDU or FAP, go back to the EFB home and
   say where to go instead (the app's MCDU window; Fenix windows > Cabin).
3. Correct the window's doc comment ("already accessible"), and move the window to 127.0.0.1 with a tabbable status
   box. All three are plan 03 (the EFB window).

Risk: the layer keys on Fenix's page structure; a Fenix update can silently undo a fix. The jsdom captures are the
regression check, re-captured after each Fenix update.

## Evidence files

In `docs/fenix-v3/evidence/` (described in `docs/fenix-v3/plans/00-index.md`): `live-findings.md`, `settings.tsv`
(credentials masked), `efb-datarefs.txt`, `fap-datarefs.txt`, `gql-operations.txt`, `fenix-options-labels.tsv`,
`failures-manual-shape.json` (first ATA chapter only), `inventory.md`, `fenix-names.txt`, `namediff-missing.tsv`,
`namediff-unused-by-us.md`, `web-research.md`, `efb-tour.md`, `panel-sweep.md`. Fenix's EFB and FAP bundles are not committed: fetch them from your
own running Fenix with `docs/fenix-v3/tools/fetch_gateway_bundles.py <folder>`.
