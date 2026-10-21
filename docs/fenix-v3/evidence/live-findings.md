# Fenix v3 live findings (2026-10-21, sim up, Fenix A320 CFM WF at KATL, engines running)

Version: Fenix 3.0.0.7426 (was 2.4.0.4720). Gateway `settings { softwareVersion }` = "3.0.0.7426".

## Processes / ports
- New `Fenix.GqlGateway.exe` (deps\systems\gateway) listens 0.0.0.0:8083 (IPv4 only; no ::1) -> `localhost` still costs the ::1 refusal for non-happy-eyeballs clients (A320-40 still right).
- `FenixSystem.exe` listens 127.0.0.1:18083 and ::1:18083 (internal backend; GET / -> 400 text/plain).
- New separate `FenixCDU.exe` process (deps\FenixCDU.exe).

## Data service (GraphQL at 127.0.0.1:8083/graphql)
- Same query shapes work: `dataRef { dataRef(name:) { value } }`, `dataRefs(names:)` subscription, `dataRef { writeInt/writeBool/writeFloat/writeString }`.
- BEHAVIOUR CHANGE: an unknown name now returns "" (empty string), not null.
- MCDU: `aircraft.mcdu{1,2}.display` unchanged XML (<root><title/><line/>x12<scratchpad/>). MCDU MENU now lists `<ATSU`. Key writes `system.switches.S_CDU{n}_KEY_{key}` = what the v3 web MCDU uses (it writes 1 only).
- NEW non-null vs v2 memory: `aircraft.engines.2.n2`, `aircraft.engines.{1,2}.egt`, `aircraft.temperature.tat`, `aircraft.engines.1.oil.{pressure,temperature,quantity}`.
- `system.switches.*`, `system.indicators.*`, `system.numerical.*`, `system.gates.*` now publish the cockpit names (were mostly null in v2). Encodings can differ from the L:var (dome: L:var 0=Off vs data 2=Off per the EFB's own code).
- Renamed/gone: `doors.entry.d1l` -> "" (EFB now uses `doors.control.d{1..4}{l,r}`), `system.config.Units.Weight` -> "" (settings API has `weight_unit`).
- EFB names (121, efb-datarefs.txt): AOC datalink `fenix.aoc.message.uplink/downlink.all`, `aircraft.atsu.ground.init`, printer, `aircraft.sharklets.installed`, seat occupancy, ground services (`groundservice.groundpower/pneumatic/preconditionedAir/pushback...`), `gsx.couatl.started`, `control.state.*` (panel state "TurnAroundPreConditioned"/"Ready"), weights incl. mtow/mlw/mzfw, cargo, water/waste.
- FAP names (fap-datarefs.txt): cabin zone temps, CIDS cabin lights ("Bright"), cabin ready, water/waste, lav, slides/doors.
- Typed APIs: `settings { softwareVersion, state { settings { key category value possibleValues source type writable } } }` (146 settings, settings.tsv), `setSetting(s)`, `fap { pram { state ... playNext/directPlay/stop/setBgmOn/setVolume ... } announcementsVariant }`, `navigraph {...}` (do not touch), subscriptions `settingsState`, `pramState`, `documentUpdated`, `efbResponseMessageUpdated`, `virtualPurserCommands`.
- REST at /fenix: `GET /fenix/failures/manual` = 549 failures by ATA chapter (JSON with id/title/failed); `/failures/saveManual`, `/failures/arrival`, `/tasks/startDeboarding`, `/tasks/cancelBoarding`, `/efb-messages`, `/intersection/save|delete`, `/settings/uploadBg|resetBg`.
- Pages: `/` and `/index.html` (EFB, no target), `/index_capt.html?target=sim-capt#/efb`, `index.html?target=sim-fo#/efb`, `/fap/index.html?target=sim-fap#/fap` (separate FAP app), web MCDU route `#/mcdu`.

## Settings of note (settings.tsv)
- Variant model: `engine_family` Cfm56|IaeV2500|CfmLeap|PwGtf, `type_code` A318|A319|A320|A321, `is_sharklet`, `aircraft_variant`.
- Could affect our control paths: `encoder_acceleration_enabled` (off here), `efis_baro_linking_type` Independent|Linked|LinkedWithStandby, `fd_ls_sync_enabled`, `channel_spacing_type` Khz25|Khz8d33 (8.33 here), `fcu_readouts_type` Old|New, `weight_unit` (Pounds here), `baro_unit` (Inhg here), `control_position_type` Captain|FirstOfficer, `standby_type` Isis|Analogue (Analogue here), `isis_type`, `adirs_type` Adirs|Gnadirs, `acp_type` Classic|Modern, `tcas_ap_enabled`, `fls_enabled`, `buss_enabled`, `dcdu_enabled` (off), `ddrmi_enabled`.
- Datalink: `acars_service` Hoppie|SayIntentions|BeyondAtc, `virtual_acars_enabled`, printer (`printer_name` "", MS Print to PDF, Pushover).
- Cabin: Virtual Purser (announcements, lights, doors arming, lav locking), cabin ready call/ECAM.
- ACP volume per process lists (`acp_process_vhf1_csv` = vpilot, `_int_` = couatl, `_cab_` = slc).

## SimConnect (MCP)
- TITLE "FenixA320 CFM WF", ATC_MODEL "ATCCOM.AC_MODEL A320.0.text" (ICAO extraction still fine), 2 engines, ENGINE_TYPE 1 (jet).
- L:vars checked OK: S_OH_ELEC_BAT1=1, N_FCU_SPEED=100, N_FCU_ALTITUDE=100 (both match data service; power-up defaults).
- `FNX2PLD_*` (fcuSpd/Hdg/Alt, bat1) all 0 with engines running: no publisher. FenixQuartz not installed, no FNX2PLD value in any app log -> likely never worked in this setup (pre-v3 issue?), origin being researched.
- Stock engine SimVars now ~track Fenix N1/N2 at idle (v2: sim N1 = Fenix N2), but EGT/FF still differ (stock EGT 490 vs Fenix 411; stock FF 891 pph = 404 kg/h vs Fenix 287 kg/h).

## Coherent (:19999)
- Pages: 3 x EfbHost (iframes to the gateway: capt / fo / fap), 2 x FenixLiveView, RefuelPanelGauge, many WasmInstrument (displays are pixel canvases -> no text), VLivery Readouts (FNX32X-READOUTS-ELEMENT), MSFS 2024 tablet has a "Fenix EFB" app.
