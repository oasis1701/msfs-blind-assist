# Fenix Cabin (FAP) Window Implementation Plan (plan 05)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a screen-reader pilot what the Fenix v3 Flight Attendant Panel (FAP) offers: an accessible "Cabin" window (cabin status, cabin ready, lights, announcements and boarding music, doors and slides, zone temperatures, water and waste), plus an app-wide spoken "Cabin ready" when the cabin reports ready.

**Architecture:** Pure models in `MSFSBlindAssist/Services/FenixCabin*.cs` turn data-service values into labelled lines, cautions and write requests, all copied from the FAP/EFB bundles; a tabbed `FenixCabinForm` (registered through plan 01's `FenixWindowRegistry.AddCabinWindows` seam, opened from the "Fenix windows" list) reads them through plan 01's `FenixDataService` (one data subscription, a 2 s poll of the FAP's audio state) and writes only through those models. A small `FenixCabinReadyMonitor`, started and stopped through plan 01's `StartCabinBackground`/`StopCabinBackground` seams in a new partial `FenixA320Definition.Cabin.cs`, is the one place that speaks "Cabin ready".

**Tech Stack:** C# 13 / .NET 10 Windows Forms, Newtonsoft.Json (`JObject`), plan 01's `FenixDataService`, xUnit.

**Spec:** `docs/fenix-v3/spec.md` sections 4.2 and 4.3; index and file ownership `docs/fenix-v3/plans/00-index.md`; foundation `docs/fenix-v3/plans/01-foundation.md` (consumed by exact name).

## Owner decisions

The plan implements the recommendation in each row unless the owner says otherwise before Task 7.

| # | Decision | Recommended (what the plan does) | Alternative |
|---|---|---|---|
| D1 | Which FAP pages | Cabin Status, Cabin Ready, Lights (folded into the Status tab), Audio, Doors / Slides, Temperature, Water / Waste, and the FAP's cautions as lines. Leave out Smoke Detection, Seat Setting, Cabin Programming and Layout Selection (FAP-local screens: the FAP's data-name table has no smoke, seat or programming name and no write behind them), System Info (part numbers the FAP generates from the MSN, plus two fixed "SYSTEM OK" / "DRAINMAST OK" lines), and FAP Set-Up's screen brightness (`aircraft.communication.cids.fap.brightness`; no use to a blind pilot). **Decided by the owner, 2026-10-21 (index answer 20): FAP Set-Up's loudspeaker volume IS included** (`aircraft.communication.cids.fap.speakerVolume`, writeFloat percent/100; Task 3 and Task 8) | — |
| D2 | Open and close passenger and cargo doors from the Doors tab (the EFB Ground Services writes, not a FAP function) | Include (Task 9, Step 3) | Doors tab read-only; doors stay in the EFB window |
| D3 | Door names on the A319/A320 aft doors | The FAP's own numbering: "Door 4 left" (data name `d4l`); the EFB calls the same door "2 left" | The EFB's "2 left" |
| D4 | "Cabin ready" speech | App-wide, once on each not-ready to ready change, always on, no setting | Also speak "Cabin not ready" on the falling edge; or a setting (plan 10, settings files) |
| D5 | The FAP's CABIN READY button in our window | Include as a checkbox; while the Fenix says it is not available (the FAP greys it), pressing it speaks "Cabin ready is not available yet." and writes nothing | Read-only line only |
| D6 | Announcement set selector (`announcementsVariant`, 0 to `maxVariant`) | Shown only when `maxVariant` > 0 (0 on the A321 today) | Never shown |
| D7 | FAP cautions (water, waste, lavatory, door pressure) | Shown as lines in the Status, Doors and Water tabs; never spoken in the background | Speak new cautions app-wide |

## Global Constraints

- Data service only through plan 01's `FenixDataService` (`127.0.0.1:8083`, never `localhost`, A320-40). No new HTTP or socket client.
- Every write name, value type and value comes from the FAP/EFB bundles (Evidence below); never guess a name, never introspect the schema, never call `navigraph`.
- An unknown name reads `""` = `FenixValueKind.EmptyOrUnknown`: shown as "no data", never closed, disarmed, off, 0 percent or a caution (FNX-1).
- Data-service callbacks run on a pool thread: the form marshals with a try/catch `SafeBeginInvoke` (A380C-6); the monitor posts to the UI `SynchronizationContext` inside the same try/catch.
- Speech (CORE-7): the window says nothing on opening and never repeats what the screen reader says (button names, checkbox states, combo values). It speaks only: a state read back after a press that the reader cannot say (volume, temperature target, water preselect, door state, announcement playing, memo), validation and failure messages, and nothing else. "Cabin ready" is spoken only by `FenixCabinReadyMonitor`.
- Status text is a read-only `TextBox` (VAT-13); value lists are `ListBox`es (one line per value, braille-friendly, A320-15's reasoning); every control has an `AccessibleName` equal to its visible text.
- Anything formatted is invariant: tests run it under de-DE and sv-SE (`TestCulture.Run`, `tests/MSFSBlindAssist.Tests/TestCulture.cs:11`).
- Variant: plan 01's `FenixA320Definition.Variant` / `VariantChanged`; never read `N_CONFIG_*`.
- Files: only `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs` (new partial: plan 01's cabin background seams), `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Cabin.cs`, `MSFSBlindAssist/Forms/Fenix/FenixCabin*.cs`, `MSFSBlindAssist/Services/FenixCabin*.cs`, new `tests/MSFSBlindAssist.Tests/FenixCabin*Tests.cs`. Anything else is a handoff request. Plan 01's `.claude/rules/fenix-data.md` globs already cover all of them (`Services/Fenix*.cs`, `Aircraft/FenixA320*.cs`, `Forms/Fenix/**`, `tests/**/*Fenix*.cs`).
- Never write to the sim during development without the owner's go-ahead; reads are fine.

## Review Focus

1. **The Fenix not loaded, or the data service down, when the window opens or mid-session:** the status box says so, every line reads "no data", every button speaks "The Fenix is not answering." and nothing throws (Task 4 `Connection_status_follows_the_FAP_screens`, Task 3 `SendAsync_is_false_when_the_service_is_down`, Task 7 Step 5).
2. **Unknown (`""`) values:** never turned into a state or a caution; door-pressure warnings need a known number (Task 4 `Unknown_values_never_become_states_or_cautions`).
3. **Doors the airframe lacks still answer** (the A321 reads overwing slides armed; an A320 answers `d2l`): only the layout's doors are listed or controlled (Task 1 layout facts, Task 4 `Door_lines_follow_the_layout_and_the_FAP_messages`).
4. **Comma-decimal PCs:** temperatures, offsets and percentages never spoken with a comma; a float write goes out as `0.5` (Task 4 `Formatting_is_invariant_under_comma_cultures`, Task 3 `SendAsync_writes_an_invariant_float_under_de_DE`).
5. **Cabin ready already true when the app connects, the window's own press, or a reconnect:** spoken at most once per real change, never at connect, never twice, never after the aircraft switched (Task 6 tests, Task 7 `Only_the_monitor_speaks_cabin_ready`).

## Evidence

Bundles: Fenix 3.0.0.7426, fetched 2026-10-21 with `docs/fenix-v3/tools/fetch_gateway_bundles.py` (output `<out>/fap/*.js`, `<out>/efb/*.js`). Chunk names below are today's; if yours differ, search for the quoted snippet. Offsets are character offsets in the one-line chunk.

| What | Chunk, offset | Snippet |
|---|---|---|
| FAP data-name table | FAP `813.26e2a64e6b0f70fb.js` 98092 | `3716:(oe,U,t)=>{…const e={aircraftType:"fenix.efb.aircraft.type",…` (every fixed name in Task 1) |
| Cabin ready write; button greyed unless available | FAP 813, 41992; 45125 | `writeBool(P.t.cabinReady,X)`; `("disabled",!X.cabinReadyAvailable)` |
| Cabin lights write; the four values | FAP 813, 66994; FAP `257.27fdcadd8844daf8.js` 68030, 69502 | `writeString(n.t.cabinLights,g)`; `setBrightnessLevel("Bright")`/`("Dim1")`/`("Dim2")`; Main On/Off `setBrightnessLevel(s?"Bright":"Off")` |
| Temperature offset limits and writes | FAP 813, 67797; 68690; 69467 | `minOffset=-2.5,…maxOffset=2.5,…offsetStep=.5,…minTemperature=18,…maxTemperature=30`; `writeFloat("fwd"===T?n.t.tempFwdOffset:n.t.tempAftOffset,Y)`; reset `writeFloat(n.t.tempFwdOffset,0)…writeFloat(n.t.tempAftOffset,0)` |
| Temperature shown and greyed | FAP 257, 120947; 123123 | `("celsius",…selectedTemperature+…selectedOffset)`; `offsetIncreaseDisabled(t){return t.selectedOffset>=…maxOffset||t.selectedTemperature+t.selectedOffset>=…maxTemperature}` (and the mirror `offsetDecreaseDisabled`) |
| Temperature data available | FAP 813, 68126 | `dataAvailable$=…[zoneControllerOperational$,lavGalleyExtractionFanRunning$,fapStore.cidsOperational$]…every` |
| Water preselect | FAP 813, 81256; 82139; 82370 | `preselectStep=25`; `Math.min((null!=te?te:100)+this.preselectStep,100)`; `Math.max((null!=te?te:100)-this.preselectStep,this.preselectStep)`, written with `writeFloat(n.t.waterTankPreselect,K)` |
| Lavatories per cabin; flush name | FAP 813, 85211; 83470 | `getLavatoryDesignations(S){…"SingleClass":return["A","D","E"];…"HighDensity":return["A","D"];…"TwoClass":return["A","M","D","E"]}`; `` `${n.t.lavatories}.${K.toLowerCase()}.${n.t.flushOperational}` `` |
| Water/waste cautions | FAP 813, 83993 to 85192 | `"WATER SERVICE DOOR OPEN"`, `"VACUUM SYSTEM DISABLED ON GROUND"` (`!B||!K` of powered, generator), `"…GROUND SERVICING"`, `"CHECK LAVATORY - POWER SUPPLY:"`, `"…WASTE TANK FULL"`, `"WASTE TANK NOT EMPTY - CHECK LEVEL"` with `d.showIf(ie,B>30)` |
| Door and slide names, meanings | FAP 813, 71235; 71640; 71896 | `doorDesignations={d1l:"1L",…d4r:"4R"}`, slides add `owl:"OWL",owr:"OWR"`; door `y?"open":"closed"`; slide `y?"armed":"disarmed"`; names `` `${doors}.${S}` `` etc. |
| Doors and slides per exits type; defaults per type | FAP 813, 61578; 62193; 60646; 63160 | `mainDoors$…"SingleOverwing":case"DoubleOverwing":return["1L","1R","4L","4R"]…`; `slides$…`; `A319:{…emergencyExitsType:"SingleOverwing",aircraftVariant:"HighDensity"…}` (A320 DoubleOverwing/SingleClass, A321 DoubleFullsize/TwoClass); `valueByKey("emergency_exits_type")` |
| Door messages | FAP 257, 77897 | `getDoorMessage(t,o,s,a,l){return null==a||a<3e3?"Check Slide Pressure":null==s||s<1e3?"Check Door Pressure":"closed"===t&&"disarmed"===o?"SlideInPark"===l?"Slide In Park":"Slide Disarmed":null}` (`l` = setting `fap_slide_disarmed_message`) |
| Audio state and mutations (verbatim text) | FAP 813, 87277 to 93156; `docs/fenix-v3/evidence/gql-operations.txt` lines 1-3, 5-10, 15-16, 21-22, 24 | `query GetBoardingMusicState`, `query GetPrerecordedAnnouncementsState`, `mutation SetBoardingMusicOn($on: Boolean!)` … `mutation StopAnnouncements` |
| How the FAP calls them | FAP 813, 64149; 64341; 64643; 64901; 78334; 78672; 79307; 80228; FAP 257, 8342 | `mutate({on:!this.get().bgmOn})`; `{volume:…volume+1}` / `-1` (no clamp); `{channelIdx:C}` (index in `bgmChannels`); `addToMemo… {announcementId:C.id}`; `removeFromMemo… {index:C}`; `directPlay… {announcementId:C.id}`; `stopGQL.mutate()`; variant `l.preferredVariant<l.maxVariant` |
| FAP power-up screens | FAP 813, 46483 | gateway not connected → "Lost connection"; `fapOperational` off → black; `fapReady` → "FAP Power Up" progress; `cidsReady` → "CIDS Power Up" progress |
| EFB door control: Int, values | EFB `964.5deea9a5abefe01c.js` 34956; 30938 | `a.d1l={dataRef:"doors.control.d1l",…dataType:"Int"…}`; enum `[i.Armed=0]…[i.Disarmed=1]…[i.Open=2]` |
| EFB cargo doors: Bool; movement | EFB 964, 36148; 43785 | `a.fwdC={dataRef:"doors.cargo.forward",…dataType:"Boolean"…}`; `fenix.doors.cargo.fwd.movement` (Boolean) |
| EFB write mechanism | EFB 964, 31352 | `supportedOperations={Int:"writeInt",Boolean:"writeBool",Float:"writeFloat",String:"writeString"}` |
| EFB toggle writes; cargo greyed while moving | EFB `856.fd2accb16ac96d75.js` 53907; 46276 | `setDoorState(t.graphqlField,i?ze.A.Open:ze.A.Disarmed):…setItemValue(t.graphqlField,i)`; `("disabled","fwdC"===…&&…fwdCargoDoorMovement||"aftC"===…&&…aftCargoDoorMovement)` |
| EFB doors per type | EFB 964, 28240 (A320); EFB `261.e692638d223fd4c1.js` 1230 (A319), 4584 (A321) | A319/A320: `d1l "1 left"`, `d1r`, `d4l "2 left"`, `d4r`, cargo fwd, aft (+ bulk on the A320); A321: `d1l`…`d4r` "1 left"…"4 right", cargo fwd, aft, bulk |

Live reads, 2026-10-21, IAE A321 at the gate (`type_code` A321, read-only queries): pram state `bgmOn` false, **one** channel `{"id":"f5957b3b-9ba4-4933-822f-806acf7f0cb7","name":"Boarding Music"}` (the spec's "15 channels" is not what this livery has: build the list from the data), `volume` 5, `onAnnounce` null, memo empty, announcements "After Takeoff" (`bc69d460-bfb0-4945-ac49-4da6d695d962`) and "Safety Briefing" (`ab9c425f-4b60-483b-9679-bab553460bcb`), `announcementsVariant` 0/0. Data: `cabinLights` "Bright", `cabinReady` false, `.available` false, CIDS and FAP operational/ready true, powerUp 100; temps fwd 24.000536617538728 / 24 / 0, aft 24.000544906158098 / 24 / 0; water 85.00000000000001, preselect 100, waste 5, full false; `door.d1l`…`d4r` false, `doorPressure.*` 2000, `slide.*` true (also `owl`/`owr` on this A321), `slidePressure.*` 3500 (`owl`/`owr` ""); `doors.control.*` 0; cargo and movement false; lavatories `a`,`m`,`d`,`e` true, `b` "". Settings: `emergency_exits_type` DoubleFullsize, `aircraft_variant` TwoClass, `fap_slide_disarmed_message` SlideDisarmed, `cabin_ready_call_enabled` True, `virtual_purser_*` (announcements off, lights/doors/boarding music control on). Bare `aircraft.communication.cids.door` / `.slide` / `.lav` read "": only the composed names exist. `docs/fenix-v3/evidence/fap-datarefs.txt` lists the fixed names only; the composed ones come from the snippets above.

Repo facts: `FenixA320Definition.ProcessSimVarUpdate` keeps the announcer at `MSFSBlindAssist/Aircraft/FenixA320Definition.cs:12100-12104` and is called on the UI thread from `MSFSBlindAssist/MainForm.Announcers.cs:242` (plan 01 starts the background seams there, `docs/fenix-v3/plans/01-foundation.md` Task 1 Interfaces); the `SafeBeginInvoke` pattern is `MSFSBlindAssist/MainForm.Announcers.cs:2122-2127`; tracked windows close on aircraft switch (`MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs:1144-1155`); `ScreenReaderAnnouncer.Announce` is dropped while `Suppressed` (start-up grace), `AnnounceImmediate` is not (`MSFSBlindAssist/Accessibility/ScreenReaderAnnouncer.cs:13-21`); input mode is `[` (`MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt:194`); the Fenix First Officer's "Cabin: READY" lines are reminders (`MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs:663`, `:703`).

## File structure

| File | Responsibility |
|---|---|
| `MSFSBlindAssist/Services/FenixCabinNames.cs` | Every cabin data name (fixed and composed), one list to subscribe |
| `MSFSBlindAssist/Services/FenixCabinLayout.cs` | Doors, slides, lavatories, ground doors, bulk cargo per airframe |
| `MSFSBlindAssist/Services/FenixCabinPram.cs` | FAP audio: state query and parse, the bundle's mutations, send |
| `MSFSBlindAssist/Services/FenixCabinWrites.cs` | Data-name writes with the FAP/EFB limits, send |
| `MSFSBlindAssist/Services/FenixCabinLines.cs` | Value cache, formatting, labelled lines per tab, cautions, connection status |
| `MSFSBlindAssist/Services/FenixCabinReadback.cs` | One read-back per press (CORE-7) |
| `MSFSBlindAssist/Services/FenixCabinReadyMonitor.cs` | App-wide "Cabin ready" |
| `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs` | Plan 01's `StartCabinBackground`/`StopCabinBackground`: owns the monitor for one definition instance |
| `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Cabin.cs` | The "Cabin" entry |
| `MSFSBlindAssist/Forms/Fenix/FenixCabinForm.cs` | Window shell, data plumbing, Status tab |
| `MSFSBlindAssist/Forms/Fenix/FenixCabinForm.Audio.cs` | Audio tab |
| `MSFSBlindAssist/Forms/Fenix/FenixCabinForm.Controls.cs` | Doors, Temperature, Water tabs |

All `Services` types are in namespace `MSFSBlindAssist.Services`; forms in `MSFSBlindAssist.Forms.Fenix`; the definition partial in `MSFSBlindAssist.Aircraft`. Test command used throughout: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter <Class>` (whole suite: without `--filter`).

---

### Task 1: Cabin names and per-airframe layout

**Files:**
- Create: `MSFSBlindAssist/Services/FenixCabinNames.cs`, `MSFSBlindAssist/Services/FenixCabinLayout.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinLayoutTests.cs`

**Interfaces:**
- Consumes: plan 01 `MSFSBlindAssist.Aircraft.FenixAircraftType` (`Unknown, A318, A319, A320, A321`).
- Produces:
  - `public static class FenixCabinNames` with consts `CabinReady` (`aircraft.communication.cids.cabinReady`), `CabinReadyAvailable`, `CabinLights`, `CidsOperational`, `CidsReady`, `CidsPowerUp`, `FapOperational`, `FapReady`, `FapPowerUp`, `TempFwdAir`, `TempFwdSelected`, `TempFwdOffset`, `TempAftAir`, `TempAftSelected`, `TempAftOffset`, `ZoneControllerOperational`, `LavGalleyFanRunning`, `WaterTank`, `WaterPreselect`, `WasteTank`, `WasteTankFull`, `VacuumGeneratorOperational`, `VacuumSystemPowered`, `WaterServicePanelOpen`, `WasteServicePanelOpen` (values exactly as in the FAP table, Evidence row 1), `CargoForward` = `doors.cargo.forward`, `CargoAft` = `doors.cargo.aft`, `CargoBulk` = `doors.cargo.bulk`, `CargoFwdMoving` = `fenix.doors.cargo.fwd.movement`, `CargoAftMoving` = `fenix.doors.cargo.aft.movement`; methods `string Door(string key)`, `DoorPressure(string key)`, `Slide(string key)`, `SlidePressure(string key)`, `DoorControl(string key)` (`doors.control.{key}`), `LavatoryFlush(string letter)` (`aircraft.communication.cids.lav.{lower}.flush.operational`); lists `DoorKeys` (`d1l,d1r,d2l,d2r,d3l,d3r,d4l,d4r`), `SlideKeys` (`d1l,d1r,d2l,d2r,owl,owr,d3l,d3r,d4l,d4r`), `LavatoryLetters` (`A,D,E,M`), `IReadOnlyList<string> All` (every fixed name, plus Door, DoorPressure, DoorControl per door key, Slide and SlidePressure per slide key, LavatoryFlush per letter: 78 names).
  - `public enum FenixExitsType { SingleOverwing, DoubleOverwing, DoubleFullsize, SingleOverwingSingleFullsize, DoubleOverwingSingleFullsize }`
  - `public sealed record FenixCabinLayout(FenixAircraftType Type, FenixExitsType Exits, IReadOnlyList<string> MainDoors, IReadOnlyList<string> Slides, IReadOnlyList<string> Lavatories, IReadOnlyList<string> GroundDoors, bool HasBulkCargo, bool SlideInPark)` with `public static FenixCabinLayout Resolve(FenixAircraftType type, string? emergencyExitsType, string? aircraftVariant, string? slideDisarmedMessage)` and `public static string DataKey(string designation)` (`"1L"` → `"d1l"`, `"OWR"` → `"owr"`). `MainDoors`/`Slides` hold FAP designations (`1L`, `OWL`); `GroundDoors` holds data keys (`d1l`); `Lavatories` holds letters.

- [ ] **Step 1: Write the failing tests** (xUnit, style of `tests/MSFSBlindAssist.Tests/FenixMcduSidesTests.cs`):
  - `Composed_names_follow_the_FAP_templates`: `Door("d1l")` == `aircraft.communication.cids.door.d1l`; `DoorPressure("d4r")` == `aircraft.communication.cids.doorPressure.d4r`; `Slide("owl")` == `aircraft.communication.cids.slide.owl`; `SlidePressure("d2r")` == `aircraft.communication.cids.slidePressure.d2r`; `DoorControl("d3l")` == `doors.control.d3l`; `LavatoryFlush("M")` == `aircraft.communication.cids.lav.m.flush.operational`.
  - `All_holds_every_name_once`: `All.Count == 78`, `All.Distinct().Count() == 78`, contains `CabinReady`, `CargoBulk`, `LavatoryFlush("E")`, `DoorControl("d4r")`.
  - `A321_double_fullsize`: `Resolve(A321, "DoubleFullsize", "TwoClass", "SlideDisarmed")` → `MainDoors` and `Slides` both `1L,1R,2L,2R,3L,3R,4L,4R`; `Lavatories` `A,M,D,E`; `GroundDoors` `d1l,d1r,d2l,d2r,d3l,d3r,d4l,d4r`; `HasBulkCargo` true; `SlideInPark` false.
  - `A320_double_overwing`: `Resolve(A320, "DoubleOverwing", "SingleClass", "SlideInPark")` → `MainDoors` `1L,1R,4L,4R`; `Slides` `1L,1R,OWL,OWR,4L,4R`; `Lavatories` `A,D,E`; `GroundDoors` `d1l,d1r,d4l,d4r`; `HasBulkCargo` true; `SlideInPark` true.
  - `A319_falls_back_by_type`: `Resolve(A319, null, null, null)` → `Exits` SingleOverwing; `MainDoors` `1L,1R,4L,4R`; `Lavatories` `A,D`; `HasBulkCargo` false.
  - `Settings_win_over_type`: `Resolve(A321, "SingleOverwingSingleFullsize", "TwoClass", null)` → `MainDoors` `1L,1R,3L,3R,4L,4R`; `Slides` `1L,1R,OWL,OWR,3L,3R,4L,4R`; `GroundDoors` `d1l,d1r,d3l,d3r,d4l,d4r`.
  - `Unknown_type_and_unknown_settings_use_the_A320_layout`: `Resolve(Unknown, "Bogus", "", null)` → `Exits` DoubleOverwing; `Lavatories` `A,D,E`; `GroundDoors` `d1l,d1r,d4l,d4r`; `HasBulkCargo` true.
  - `DataKey_maps_designations` theory: `1L`→`d1l`, `4R`→`d4r`, `OWL`→`owl`.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixCabinLayoutTests`.
- [ ] **Step 3: Implement both files.** `Resolve`: exits = the setting when it parses exactly (ordinal) as a `FenixExitsType`, else by type (A319 SingleOverwing, A321 DoubleFullsize, anything else DoubleOverwing, the FAP's A320 default); doors/slides per exits type exactly as the FAP's `mainDoors$`/`slides$` (Evidence); lavatories by the setting (SingleClass `A,D,E`, HighDensity `A,D`, TwoClass `A,M,D,E`), else by type (A319 HighDensity, A321 TwoClass, else SingleClass); ground doors = the EFB's per-type list (A321 all eight, else `d1l,d1r,d4l,d4r`) intersected with `MainDoors` keys, in `MainDoors` order; `HasBulkCargo` = type is not A319 (the EFB A319 table has none); `SlideInPark` = setting equals `SlideInPark`. A class comment cites the Evidence rows by chunk and snippet.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `git add MSFSBlindAssist/Services/FenixCabinNames.cs MSFSBlindAssist/Services/FenixCabinLayout.cs tests/MSFSBlindAssist.Tests/FenixCabinLayoutTests.cs` ; `git commit -m "feat(fenix-cabin): cabin data names and per-airframe layout"`

### Task 2: FAP audio (PRAM): state, mutations, send

**Files:**
- Create: `MSFSBlindAssist/Services/FenixCabinPram.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinPramTests.cs`

**Interfaces:**
- Consumes: plan 01 `FenixDataService.QueryAsync(string query, object? variables = null, CancellationToken ct = default)` (returns `data` or null), `FenixDataService(HttpMessageHandler? handler = null)`.
- Produces:
  - `public sealed record FenixCabinItem(string Id, string Name);`
  - `public sealed record FenixCabinPramState(bool MusicOn, IReadOnlyList<FenixCabinItem> Channels, int SelectedChannel, int Volume, string? NowAnnouncing, IReadOnlyList<FenixCabinItem> Memo, bool PlayingAllInMemo, IReadOnlyList<FenixCabinItem> Announcements, int PreferredVariant, int MaxVariant);`
  - `public sealed record FenixCabinMutation(string Query, object? Variables, string ResultField, bool UnderPram);`
  - `public static class FenixCabinPram`: `const string StateQuery`; `FenixCabinPramState? Parse(JObject? data)`; `SetMusicOn(bool on)`, `SetVolume(int volume)`, `SetChannel(int channelIdx)`, `AddToMemo(string announcementId)`, `RemoveFromMemo(int index)`, `ClearMemo()`, `DirectPlay(string announcementId)`, `PlayNext()`, `PlayAll()`, `Stop()`, `SetAnnouncementsVariant(int variant)`, each returning `FenixCabinMutation`; `bool Succeeded(FenixCabinMutation m, JObject? data)`; extension `Task<bool> SendAsync(this FenixDataService data, FenixCabinMutation m, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests.**
  - `Parse_reads_the_live_state_of_2026_10_21`: parse `{"fap":{"pram":{"state":{"bgmOn":false,"bgmChannels":[{"id":"f5957b3b-9ba4-4933-822f-806acf7f0cb7","name":"Boarding Music"}],"bgmSelectedChannel":0,"volume":5,"onAnnounce":null,"announcementsMemo":[],"playingAllInMemo":false,"prerecordedAnnouncements":[{"id":"bc69d460-bfb0-4945-ac49-4da6d695d962","name":"After Takeoff"},{"id":"ab9c425f-4b60-483b-9679-bab553460bcb","name":"Safety Briefing"}]}},"announcementsVariant":{"preferredVariant":0,"maxVariant":0}}}` → `MusicOn` false, one channel named "Boarding Music", `SelectedChannel` 0, `Volume` 5, `NowAnnouncing` null, `Memo` empty, `Announcements` ["After Takeoff", "Safety Briefing"] with those ids, `PreferredVariant` 0, `MaxVariant` 0.
  - `Parse_reads_a_playing_announcement_and_the_memo`: `onAnnounce` `{"name":"Safety Briefing"}`, memo `[{"id":"bc69…","name":"After Takeoff"}]`, `playingAllInMemo` true → `NowAnnouncing` "Safety Briefing", one memo item, `PlayingAllInMemo` true.
  - `Parse_is_null_without_fap_state`: `Parse(null)`, `Parse(JObject.Parse("{}"))` → null; `prerecordedAnnouncements` `"x"` (not an array) → empty `Announcements`.
  - `Mutations_are_the_FAP_bundle_documents` (theory; compare after collapsing whitespace runs to one space): `SetMusicOn(true)` → `mutation SetBoardingMusicOn($on: Boolean!) { fap { pram { setBgmOn(on: $on) { success } } } }`, variables `{"on":true}`; `SetVolume(6)` → `mutation SetVolume($volume: Int!) { fap { pram { setVolume(volume: $volume) { success } } } }`, `{"volume":6}`; `SetChannel(0)` → `mutation SetBgmChannel($channelIdx: Int!) { fap { pram { setBgmChannel(channelIdx: $channelIdx) { success } } } }`, `{"channelIdx":0}`; `AddToMemo("bc69d460-bfb0-4945-ac49-4da6d695d962")` → `mutation AddToAnnouncementsMemo($announcementId: UUID!) { fap { pram { addToMemo(announcementId: $announcementId) { success } } } }`, `{"announcementId":"bc69d460-bfb0-4945-ac49-4da6d695d962"}`; `RemoveFromMemo(0)` → `mutation RemoveFromAnnouncementsMemo($index: Int!) { fap { pram { removeFromMemo(index: $index) { success } } } }`, `{"index":0}`; `ClearMemo()` → `mutation ClearAnnouncementsMemo { fap { pram { clearMemo { success } } } }`, null; `DirectPlay(id)` → `mutation DirectPlayAnnouncement($announcementId: UUID!) { fap { pram { directPlay(announcementId: $announcementId) { success } } } }`; `PlayNext()` → `mutation PlayNextAnnouncement { fap { pram { playNext { success } } } }`; `PlayAll()` → `mutation PlayAllAnnouncements { fap { pram { playAll { success } } } }`; `Stop()` → `mutation StopAnnouncements { fap { pram { stop { success } } } }`; `SetAnnouncementsVariant(1)` → `mutation SetAnnouncementsVariant($variant: Int!) { fap { setAnnouncementsVariant(variant: $variant) { success } } }`, `{"variant":1}`, `UnderPram` false. Variables compared as `JObject.FromObject(m.Variables).ToString(Formatting.None)`.
  - `Succeeded_reads_success`: `SetMusicOn` with `{"fap":{"pram":{"setBgmOn":{"success":true}}}}` → true, `"success":false` → false, null → false; `SetAnnouncementsVariant` with `{"fap":{"setAnnouncementsVariant":{"success":true}}}` → true.
  - `StateQuery_is_a_query`: starts with `query`, contains `bgmChannels { id name }`, `prerecordedAnnouncements`, `announcementsVariant { preferredVariant maxVariant }`, no `mutation`.
  - `SendAsync_follows_success` (fake `HttpMessageHandler`): body `{"data":{"fap":{"pram":{"setVolume":{"success":true}}}}}` → true; `…false…` → false; a handler that throws `HttpRequestException` → false, nothing thrown.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixCabinPramTests`.
- [ ] **Step 3: Implement.** `StateQuery` = `query { fap { pram { state { bgmOn bgmChannels { id name } bgmSelectedChannel volume onAnnounce { name } announcementsMemo { id name } playingAllInMemo prerecordedAnnouncements } } announcementsVariant { preferredVariant maxVariant } } }` (the bundle's two state fragments plus its variant query, one round trip; read live 2026-10-21). Mutation texts verbatim from the bundle (whitespace aside); variables as anonymous objects with the bundle's names. `Succeeded` reads `data.fap.pram.<ResultField>.success` (or `data.fap.<ResultField>.success` when not `UnderPram`). `SendAsync` = `Succeeded(m, await data.QueryAsync(m.Query, m.Variables, ct))`.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `feat(fenix-cabin): FAP audio state and the bundle's announcement and music mutations`

### Task 3: Data-name writes with the FAP and EFB limits

**Files:**
- Create: `MSFSBlindAssist/Services/FenixCabinWrites.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinWritesTests.cs`

**Interfaces:**
- Consumes: Task 1 `FenixCabinNames`; plan 01 `FenixDataService.WriteIntAsync/WriteBoolAsync/WriteFloatAsync/WriteStringAsync` (true on success).
- Produces:
  - `public enum FenixCabinWriteKind { Int, Bool, Float, String }`, `public enum FenixCabinZone { Forward, Aft }`, `public enum FenixCargoDoor { Forward, Aft, Bulk }`, `public enum FenixCabinLightLevel { Bright, Dim1, Dim2, Off }`
  - `public sealed record FenixCabinWrite(FenixCabinWriteKind Kind, string Name, object Value);` (Value is `int`, `bool`, `double` or `string`)
  - `public static class FenixCabinWrites`: consts `OffsetStep = 0.5`, `MinOffset = -2.5`, `MaxOffset = 2.5`, `MinTemperature = 18`, `MaxTemperature = 30`, `PreselectStep = 25`, `MinPreselect = 25`, `MaxPreselect = 100`, `DoorDisarmed = 1`, `DoorOpen = 2`; `string LightWire(FenixCabinLightLevel)` (`Bright`, `Dim1`, `Dim2`, `Off`), `string LightName(FenixCabinLightLevel)` (`Bright`, `Dim 1`, `Dim 2`, `Off`), `FenixCabinLightLevel? ParseLight(string? wire)`; `FenixCabinWrite CabinReady(bool ready)`; `FenixCabinWrite CabinLights(FenixCabinLightLevel level)`; `FenixCabinWrite? TemperatureStep(FenixCabinZone zone, double selected, double offset, bool warmer)` (null at a FAP limit); `IReadOnlyList<FenixCabinWrite> ResetTemperatures()`; `FenixCabinWrite? PreselectStep(double? current, bool up)` (null when the value would not change); `FenixCabinWrite DoorToggle(string doorKey, double? controlValue)`; `FenixCabinWrite CargoDoor(FenixCargoDoor door, bool open)`; `FenixCabinWrite? SpeakerVolumeStep(double? current, bool up)` (Float, `aircraft.communication.cids.fap.speakerVolume`, a fraction 0 to 1; null at a limit; step and limits exactly as the FAP Set-Up page's own write, read from the FAP bundle and quoted here before coding, never guessed; live value 1 = 100 % on 2026-10-21); extension `Task<bool> SendAsync(this FenixDataService data, FenixCabinWrite w, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests.**
  - `Cabin_ready_and_lights_are_the_FAP_writes`: `CabinReady(true)` → (Bool, `aircraft.communication.cids.cabinReady`, true); `CabinLights(Dim1)` → (String, `aircraft.communication.cids.cabinLights`, "Dim1"); `ParseLight("Off")` → Off; `ParseLight("Blue")` → null; `LightName(Dim2)` == "Dim 2".
  - `Temperature_steps_follow_the_FAP_limits`: `TemperatureStep(Forward, 24, 0, warmer: true)` → (Float, `aircraft.cabin.fwd.temp.offset`, 0.5 as `double`); `(Aft, 24, 0, false)` → (Float, `aircraft.cabin.aft.temp.offset`, -0.5); null for `(Forward, 24, 2.5, true)`, `(Forward, 29.5, 0.5, true)` (target 30), `(Forward, 24, -2.5, false)`, `(Forward, 18.5, -0.5, false)` (target 18).
  - `Reset_writes_both_offsets_to_zero`: two Float writes, `aircraft.cabin.fwd.temp.offset` 0.0 and `aircraft.cabin.aft.temp.offset` 0.0.
  - `Preselect_steps_follow_the_FAP`: `PreselectStep(50, up: true)` → (Float, `aircraft.communication.cids.waterTank.preselect`, 75.0); `(null, false)` → 75.0 (the FAP treats unknown as 100); null for `(100, true)` and `(25, false)`.
  - `Door_toggle_follows_the_EFB`: `DoorToggle("d1l", 2)` → (Int, `doors.control.d1l`, 1); `DoorToggle("d1l", 0)` and `DoorToggle("d1l", null)` → (Int, `doors.control.d1l`, 2). No call ever produces 0.
  - `Cargo_doors_are_bool_writes`: `CargoDoor(Bulk, true)` → (Bool, `doors.cargo.bulk`, true); `CargoDoor(Forward, false)` → (Bool, `doors.cargo.forward`, false).
  - `Speaker_volume_steps_follow_the_FAP_setup_page`: the FAP's own step, written as a fraction (from the bundle); null at the top and bottom limits; under de-DE the body carries a dot decimal.
  - `SendAsync_writes_an_invariant_float_under_de_DE` (fake handler records the request body, answers `{"data":{"dataRef":{"writeFloat":true}}}`): under `TestCulture.Run("de-DE", …)`, `SendAsync(TemperatureStep(Forward, 24, 0, true)!)` → true; body contains `writeFloat`, `aircraft.cabin.fwd.temp.offset` and `"value":0.5`, not `0,5`.
  - `SendAsync_is_false_when_the_service_is_down`: throwing handler → false, nothing thrown.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixCabinWritesTests`.
- [ ] **Step 3: Implement.** Step logic exactly as the FAP (Evidence): warmer refused when `offset >= MaxOffset || selected + offset >= MaxTemperature`, cooler when `offset <= MinOffset || selected + offset <= MinTemperature`; preselect `min((current ?? 100) + 25, 100)` / `max((current ?? 100) - 25, 25)`, null when equal to `current`. `DoorToggle` writes `DoorDisarmed` when `controlValue == DoorOpen`, else `DoorOpen` (the EFB's toggle; it never writes Armed 0). `SendAsync` switches on `Kind` to plan 01's typed write.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `feat(fenix-cabin): cabin writes from the FAP and EFB bundles`

### Task 4: Values, formatting, lines and cautions

**Files:**
- Create: `MSFSBlindAssist/Services/FenixCabinLines.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinLinesTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3; plan 01 `FenixDataValue` (`Kind`, `Number`, `Bool`, `Text`, `FromJson`, `Unknown`), `FenixValueKind`.
- Produces:
  - `public sealed class FenixCabinData`: `bool Apply(string name, FenixDataValue value)` (true when the stored value changed), `FenixDataValue Get(string name)` (`Unknown` if never seen), `bool? Bool(string name)`, `double? Number(string name)`, `string? Text(string name)` (each null unless that kind), `bool AnyKnown`.
  - `public enum FenixCabinSection { Status, Audio, Doors, Temperature, Water }`; `public sealed record FenixCabinLine(FenixCabinSection Section, string Key, string Text);`
  - `public static class FenixCabinLines`: `IReadOnlyList<FenixCabinLine> Build(FenixCabinData data, FenixCabinPramState? pram, FenixCabinLayout layout)`; `string ConnectionStatus(bool serviceAnswering, FenixCabinData data)`; `IReadOnlyList<string> Cautions(FenixCabinData data, FenixCabinLayout layout)` (water and waste); `string? PressureMessage(double? doorPressure, double? slidePressure)`; `string DoorName(string designation)` (`"1L"` → `"Door 1 left"`, `"OWL"` → `"Overwing slide left"`); `string Degrees(double celsius)`, `string Percent(double value)`, `string Offset(double offset)`.
- Line keys (the read-back in Task 5 and the forms use them): Status `status.cabinReady`, `status.lights`, `status.music`, `status.announcing`, `status.doors`, `status.slides`, `status.cargo`, `status.temperature`, `status.water`, `status.cautions`; Audio `audio.music`, `audio.channel`, `audio.volume`, `audio.announcing`, `audio.memo`, `audio.playAll`, `audio.set` (only when `MaxVariant` > 0), `audio.unavailable` (only when `pram` is null); Doors `door.<key>` per main door, `slide.owl`/`slide.owr` when listed, `cargo.forward`, `cargo.aft`, `cargo.bulk` (when `HasBulkCargo`); Temperature `temp.available` (only when unavailable), `temp.forward`, `temp.aft`; Water `water.water`, `water.preselect`, `water.waste`, `water.wasteFull`, `water.waterPanel`, `water.wastePanel`, `water.vacuum`, `water.lav.<letter>`, `water.caution.<n>`.

- [ ] **Step 1: Write the failing tests.** A helper builds `FenixCabinData` from `(name, JToken)` pairs via `FenixDataValue.FromJson`. "Live A321" = the Evidence live reads with `FenixCabinLayout.Resolve(A321, "DoubleFullsize", "TwoClass", "SlideDisarmed")` and the Task 2 live pram state.
  - `Formatting_is_invariant_under_comma_cultures` (theory de-DE, sv-SE, inside `TestCulture.Run`): `Degrees(24.000536617538728)` == "24.0"; `Percent(85.00000000000001)` == "85 percent"; `Percent(12.5)` == "13 percent"; `Offset(0)` == "no offset"; `Offset(0.5)` == "offset plus 0.5"; `Offset(-1)` == "offset minus 1.0".
  - `Status_lines_for_the_live_A321`: in order `Cabin ready: no, not available yet`, `Cabin lights: Bright`, `Boarding music: off`, `Announcement playing: none`, `Doors: all closed`, `Slides: all armed`, `Cargo doors: all closed`, `Cabin temperature: forward 24.0, aft 24.0 degrees`, `Water 85 percent, waste 5 percent`, `Cautions: none`. With `cabinReady` true: `Cabin ready: yes`. With music on, volume 5: `Boarding music: on, Boarding Music, volume 5`. With `door.d1l` and `door.d4r` true: `Doors open: 1 left, 4 right`.
  - `Audio_lines_from_the_pram_state` (live): `Boarding music: off`, `Music channel: Boarding Music, 1 of 1`, `Volume: 5`, `Now announcing: nothing`, `Memo: empty`, `Playing all memo announcements: no`, and no `audio.set` line; with `MaxVariant` 2, `PreferredVariant` 1: `audio.set` == `Announcement set: 1 (0 to 2)`; with `pram` null: the one line `Audio: no data from the Fenix`.
  - `Temperature_lines_add_the_offset_to_the_target` (under de-DE): fwd air 24.4, selected 24, offset 0.5 → `Forward zone: 24.4 degrees, target 24.5 degrees, offset plus 0.5`; aft air 23.96, selected 24, offset 0 → `Aft zone: 24.0 degrees, target 24.0 degrees, no offset`; no `temp.available` line. With `ZoneControllerOperational` false the first temperature line is `Temperature control: not available`.
  - `Door_lines_follow_the_layout_and_the_FAP_messages`: layout `Resolve(A320, "DoubleOverwing", "SingleClass", "SlideInPark")`; data: d1l closed/armed/2000/3500 → `Door 1 left: closed, slide armed`; d1r closed/disarmed/2000/3500 → `Door 1 right: closed, slide in park`; d4l open/disarmed/2000/3500 → `Door 4 left: open, slide disarmed`; d4r closed/armed/2000/2900 → `Door 4 right: closed, slide armed, check slide pressure`; `slide.owl` true → `Overwing slide left: armed`; `doors.cargo.forward` true → `Forward cargo door: open`; `fenix.doors.cargo.aft.movement` true → `Aft cargo door: moving`. Data also holds `door.d2l` false: no line has key `door.d2l`.
  - `PressureMessage_follows_the_FAP_order`: `(2000, 2900)` and `(900, 2900)` → "check slide pressure"; `(900, 3500)` → "check door pressure"; `(2000, 3500)` → null; `(null, null)` → null (unknown is not low, FNX-1; the FAP warns on a missing value, we do not).
  - `Cautions_follow_the_FAP_rules` (TwoClass layout): `WaterServicePanelOpen` true → contains `Water service door open`; `VacuumSystemPowered` false with generator true → `Vacuum system disabled on ground`; `WasteServicePanelOpen` true → `Vacuum system disabled: ground servicing`; `LavatoryFlush("A")` false → `Check lavatory power supply: lavatory A`; `WasteTankFull` true → `Vacuum system disabled: waste tank full`; `WasteTank` 31 → `Waste tank not empty, check level: ground service required`; `WasteTank` 30 → that caution absent. Water tab shows each as `Caution: <text>`; Status shows `Cautions: <a>; <b>` including door pressure messages as `Door 4 right: check slide pressure`.
  - `Unknown_values_never_become_states_or_cautions`: empty data, A321 layout → `Cautions` empty; `Cabin ready: no data`; `Door 1 left: no status`; `Water: no data`; `Cautions: no data`; `Temperature control` line absent and `Forward zone: no data`.
  - `Connection_status_follows_the_FAP_screens`: `(false, empty)` → `The Fenix is not answering. Load the Fenix and wait for it to start.`; `FapOperational` false → `The flight attendant panel is off.`; `FapReady` false, `FapPowerUp` 40 → `Flight attendant panel starting: 40 percent.`; `CidsReady` false, `CidsPowerUp` 60 → `Cabin system starting: 60 percent.`; all ready → `Cabin system ready.`
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixCabinLinesTests`.
- [ ] **Step 3: Implement.** Numbers with `CultureInfo.InvariantCulture`: `Degrees` `F1`; `Percent` `Math.Round(v, MidpointRounding.AwayFromZero)`; offset `F1` with "plus"/"minus". Door state words: open/closed from `Door(key)`; slide words armed/disarmed from `Slide(key)`, "slide in park" when `SlideInPark` and the door is closed and the slide disarmed; append `PressureMessage` only for main doors. Cargo: "moving" when the movement flag is true, else open/closed from the bool (bulk has no movement name). Temperature unavailable when any of `ZoneControllerOperational`, `LavGalleyFanRunning`, `CidsOperational` is known false. A caution needs every value it reads known; `status.cautions` reads `Cautions: no data` while `!data.AnyKnown`. Doors/slides summaries count only the layout's lists. Short door names in summaries: `1 left`, `overwing left`.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `feat(fenix-cabin): labelled cabin lines, FAP cautions and connection status`

### Task 5: One read-back per press

**Files:**
- Create: `MSFSBlindAssist/Services/FenixCabinReadback.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinReadbackTests.cs`

**Interfaces:**
- Consumes: Task 4 `FenixCabinLine`.
- Produces: `public sealed class FenixCabinReadback` with `void Expect(string key, string? currentText, DateTime now, TimeSpan window, bool speakIfUnchanged)` (a second `Expect` for the same key replaces the first) and `IReadOnlyList<string> Collect(IReadOnlyList<FenixCabinLine> lines, DateTime now)`. Windows the forms use: 5 s for volume, temperature, preselect, announcing and memo; 30 s for doors and cargo doors. `speakIfUnchanged` true only for volume, temperature and preselect (numeric confirmations always speak, CORE-7).

- [ ] **Step 1: Write the failing tests** (`t0 = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc)`):
  - `Speaks_the_new_line_once`: `Expect("audio.volume", "Volume: 5", t0, 5 s, true)`; `Collect([audio.volume "Volume: 5"], t0+1 s)` empty; `Collect(["Volume: 6"], t0+2 s)` == `["Volume: 6"]`; `Collect(["Volume: 7"], t0+3 s)` empty.
  - `A numeric press that changes nothing still answers`: `Collect(["Volume: 5"], t0+6 s)` == `["Volume: 5, unchanged"]`.
  - `Other presses time out silently`: `Expect("door.d1l", "Door 1 left: closed, slide armed", t0, 30 s, false)`; `Collect(same, t0+31 s)` empty, and nothing on later calls.
  - `A missing line counts as unchanged`; `Two keys answer in press order`; `A repeated press replaces the expectation`.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixCabinReadbackTests`.
- [ ] **Step 3: Implement** (a list of pending expectations; no timers, the caller passes `now`).
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `feat(fenix-cabin): one read-back per cabin press`

### Task 6: App-wide "Cabin ready"

**Files:**
- Create: `MSFSBlindAssist/Services/FenixCabinReadyMonitor.cs`, `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinReadyMonitorTests.cs`

**Interfaces:**
- Consumes: plan 01 `FenixDataService.Subscribe(IReadOnlyList<string>, Action<string, FenixDataValue>)` (pool-thread callbacks), `FenixDataService.ReadAsync(string, CancellationToken)`, `FenixA320Definition.DataService`, and the seams declared in `FenixA320Definition.Hooks.cs`: `partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer);` (called once per definition instance, on the UI thread, from the first `ProcessSimVarUpdate` after a connection) and `partial void StopCabinBackground();` (called from `OnSwitchedAway` before the data service is disposed); `Accessibility.ScreenReaderAnnouncer.Announce(string)`.
- Produces:
  - In `FenixA320Definition.Cabin.cs` (`public partial class FenixA320Definition`): `private Services.FenixCabinReadyMonitor? _cabinReadyMonitor;`, `partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer)` (`_cabinReadyMonitor ??= Services.FenixCabinReadyMonitor.Start(DataService, announcer);`) and `partial void StopCabinBackground()` (`_cabinReadyMonitor?.Dispose(); _cabinReadyMonitor = null;`). Nothing else in this file.
  - `public sealed class FenixCabinReadyEdge` with `void Seed(FenixDataValue value)` (sets the baseline only if none yet and the value is a Bool) and `bool Next(FenixDataValue value)` (true only on a known false → true change after a baseline; non-Bool values are ignored and keep the baseline).
  - `public sealed class FenixCabinReadyMonitor : IDisposable` with `public const string SpokenText = "Cabin ready";`, `public static FenixCabinReadyMonitor Start(FenixDataService data, ScreenReaderAnnouncer announcer)` (call on the UI thread: it captures `SynchronizationContext.Current`), `internal FenixCabinReadyMonitor(Func<Action<string, FenixDataValue>, IDisposable> subscribe, Func<Task<FenixDataValue>> readOnce, SynchronizationContext? ui, Action<string> speak)`, `public void Dispose()`.

- [ ] **Step 1: Write the failing tests.** A test `SynchronizationContext` queues posts and runs them on `RunAll()`; a fake subscribe captures the callback and records disposal.
  - `Edge_speaks_only_on_a_known_rise`: `Next` over `false, true` → `false, true`; first value `true` → false (silent baseline); `true, false, true` → speaks on the third; `false, Unknown, true` → speaks on the third; `Unknown, true` → false (baseline); a Number 1 is ignored.
  - `Seed_sets_the_baseline_once`: `Seed(true)` then `Next(true)` → false; `Seed(false)` then `Next(true)` → true; `Next(false)` then `Seed(true)` then `Next(true)` → true (seed ignored once a baseline exists).
  - `Speaks_on_the_UI_context`: `readOnce` returns Bool false; the callback is invoked from `Task.Run` with Bool true for `aircraft.communication.cids.cabinReady` → nothing spoken until `RunAll()`, then exactly `["Cabin ready"]`.
  - `Ignores_other_names`; `Dispose_stops_speech_and_disposes_the_subscription`: after `Dispose`, a queued post and a later callback speak nothing; the fake subscription was disposed once.
  - `A_closed_context_does_not_throw`: a context whose `Post` throws `InvalidOperationException` → `OnValue` returns normally.
  - `The_definition_starts_and_stops_the_monitor` (source scan of `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs` under `ClaudeContextBudgetTests.RepoRoot()`; a live start would open a socket in CI): the file declares `partial void StartCabinBackground(` containing `FenixCabinReadyMonitor.Start(DataService, announcer)` and `partial void StopCabinBackground(` containing `_cabinReadyMonitor?.Dispose()` and `_cabinReadyMonitor = null`.
- [ ] **Step 2: Run them; they fail to compile.** `--filter FenixCabinReadyMonitorTests`.
- [ ] **Step 3: Implement.** Subscribe first, then `readOnce()` and `Seed` (the subscription's first message may arrive first; `Seed` ignores it then). One lock around the edge. On a rise: `ui.Post(_ => { if (!disposed) speak(SpokenText); }, null)` inside `try { … } catch (InvalidOperationException) { }`; `ui` null → speak inline. `Start` passes `data.Subscribe([FenixCabinNames.CabinReady], cb)`, `() => data.ReadAsync(FenixCabinNames.CabinReady)` and `announcer.Announce` (the automatic path: silent during the start-up grace period). Log start and dispose with `Log.Debug("FenixCabin", …)`; never log or speak per value. Then create `FenixA320Definition.Cabin.cs` with exactly the field and the two seam implementations in Interfaces (Read `FenixA320Definition.Hooks.cs` first so its rules load, CORE-16; do not edit it).
- [ ] **Step 4: Run them and the whole suite; all pass. Build the solution** (`dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`): the partial compiles against plan 01's declarations.
- [ ] **Step 5: Commit.** `git add MSFSBlindAssist/Services/FenixCabinReadyMonitor.cs MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs tests/MSFSBlindAssist.Tests/FenixCabinReadyMonitorTests.cs` ; `feat(fenix-cabin): app-wide cabin-ready announcement`

### Task 7: Window shell, registry entry and Status tab

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Cabin.cs`, `MSFSBlindAssist/Forms/Fenix/FenixCabinForm.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixCabinWindowTests.cs`

**Interfaces:**
- Consumes: plan 01 `FenixWindowRegistry` seam `static partial void AddCabinWindows(List<FenixWindowEntry> entries)`, `FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open)`, `FenixWindowContext(Definition, Data, SimConnect, Announcer, Owner)`, `FenixA320Definition.ShowFenixWindow<T>(Func<T>, Action<T>)`, `FenixA320Definition.Variant` / `VariantChanged` (UI thread), `FenixDataService.ReadManyAsync`, `Subscribe`, `QueryAsync`, `GetSettingsAsync` (`FenixSetting.Key`, `.Value`); Tasks 1-5.
- Produces: entry Name `Cabin`, Description `Flight attendant panel: cabin ready, announcements and boarding music, lights, doors and slides, temperature, water and waste`; `public sealed partial class FenixCabinForm : Form` with `public FenixCabinForm(FenixWindowContext context)`, `public void ShowForm()`, and for the partials: `private FenixCabinData _cache`, `FenixCabinLayout _layout`, `FenixCabinPramState? _pram`, `FenixCabinReadback _readback`, `bool _updatingControls`, `void SafeBeginInvoke(Action)`, `Task RunWriteAsync(FenixCabinWrite w, string? readbackKey, TimeSpan window, bool speakIfUnchanged)`, `Task RunMutationAsync(FenixCabinMutation m, string? readbackKey, bool speakIfUnchanged)` (5 s window), `void Refuse(string message)` (speaks with `AnnounceImmediate`), `void MarkDirty()`, `ListBox AddLineList(TabPage page, string accessibleName)`.

- [ ] **Step 1: Write the failing tests.** Source scans read files under `ClaudeContextBudgetTests.RepoRoot()` (`tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs:1135`):
  - `The_cabin_window_is_registered_once`: `Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "Cabin")`.
  - `Cabin_forms_marshal_only_through_SafeBeginInvoke`: the joined text of `MSFSBlindAssist/Forms/Fenix/FenixCabinForm*.cs` contains `catch (InvalidOperationException)`, and `Regex.Matches(text, @"(?<!Safe)BeginInvoke\(").Count == 1` (the call inside `SafeBeginInvoke`).
  - `Cabin_writes_go_through_the_bundle_builders`: that text contains none of `WriteBoolAsync(`, `WriteIntAsync(`, `WriteFloatAsync(`, `WriteStringAsync(`, and every `QueryAsync(` is `QueryAsync(FenixCabinPram.StateQuery`.
  - `Cabin_code_never_names_localhost`: no `localhost` in `MSFSBlindAssist/Services/FenixCabin*.cs` or `MSFSBlindAssist/Forms/Fenix/FenixCabin*.cs`.
  - `Only_the_monitor_speaks_cabin_ready`: across the Fenix files only (`MSFSBlindAssist/**/Fenix*.cs`, `MSFSBlindAssist/Forms/Fenix/**/*.cs`, `MSFSBlindAssist/FirstOfficer/Fenix/**/*.cs`), excluding `MSFSBlindAssist/Services/FenixCabinReadyMonitor.cs`, no file contains the exact C# string literal `"Cabin ready"` (closing quote included, so the window's "Cabin ready is not available yet." passes). Other aircraft are out of scope: `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.UiVariableSet.cs:212` says "Cabin ready signalled" by design.
- [ ] **Step 2: Run them; they fail** (no entry, files missing). `--filter FenixCabinWindowTests`.
- [ ] **Step 3: Implement the registry entry**: `static partial void AddCabinWindows(List<FenixWindowEntry> entries) => entries.Add(new("Cabin", "<Description above>", ctx => ctx.Definition.ShowFenixWindow(() => new FenixCabinForm(ctx), f => f.ShowForm())));`
- [ ] **Step 4: Implement the shell.** Title and `AccessibleName` "Fenix cabin", 900 × 620, `KeyPreview`, Escape closes (pattern `MSFSBlindAssist/Forms/Fenix/FenixEFBForm.cs:194-217`, but the form is disposed on close and restores the previous foreground window). Top: read-only `TextBox` `_statusBox` (AccessibleName "Cabin system status", text from `ConnectionStatus`). Below: `TabControl` with pages in this order: "Status", "Audio", "Doors", "Temperature", "Water" (Ctrl+Tab moves between them). Status page: `ListBox` "Cabin status" (Status lines), `CheckBox` "Cabin &ready", `ComboBox` (DropDownList) "Cabin &lights" with Bright, Dim 1, Dim 2, Off.
  Data flow (`OnShown`, async on the UI thread): settings via `GetSettingsAsync` (`emergency_exits_type`, `aircraft_variant`, `fap_slide_disarmed_message`) → `_layout = FenixCabinLayout.Resolve(Definition.Variant.Type, …)`; `ReadManyAsync(FenixCabinNames.All)` into `_cache` (`serviceAnswering` = any known value); `Subscribe(FenixCabinNames.All, (n, v) => SafeBeginInvoke(() => { if (_cache.Apply(n, v)) MarkDirty(); }))`; first pram refresh; start a 300 ms `System.Windows.Forms.Timer` (renders when dirty, then `_readback.Collect` and `announcer.Announce` each string) and a 2 s timer for the pram poll (`QueryAsync(FenixCabinPram.StateQuery)` → `Parse`; skipped while one is in flight). `VariantChanged` → re-resolve the layout, `MarkDirty`. `FormClosed`: dispose the subscription and timers, unhook `VariantChanged`.
  Rendering: each tab's `ListBox` updates item text in place only where it changed (keep `SelectedIndex`; rebuild only when the count changes); controls are set under `_updatingControls = true`, and their handlers return while it is set. The window speaks nothing on opening.
  Status controls: the checkbox (`CheckedChanged`, user only) refuses with "Cabin ready is not available yet." and restores itself unless `CabinReadyAvailable` is known true; otherwise `RunWriteAsync(CabinReady(isChecked), null, …)` (no read-back: the monitor speaks "Cabin ready"; the reader already said "checked"). The lights combo (`SelectionChangeCommitted`) writes `CabinLights(level)` with no read-back.
  `RunWriteAsync`/`RunMutationAsync`: when the service is not answering, `Refuse("The Fenix is not answering.")`; on false, `Refuse("The Fenix did not accept that.")`; on success, `_readback.Expect(key, current line text, DateTime.UtcNow, …)` and, for mutations, refresh the pram state at once. `SafeBeginInvoke` exactly as `MSFSBlindAssist/MainForm.Announcers.cs:2122-2127`.
- [ ] **Step 5: Run the tests and the whole suite; all pass. Build:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`. **Read-only live check** (sim up with the Fenix; touch no control that writes): run the app, `[`, Ctrl+Shift+F, arrow to "Cabin", Enter. Expected: status box "Cabin system ready."; the Status list reads the Task 4 live lines; with the Fenix closed or the gateway stopped, the box reads "The Fenix is not answering. Load the Fenix and wait for it to start." and lines read "no data". Escape closes and focus returns.
- [ ] **Step 6: Commit.** `feat(fenix-cabin): Cabin window with status, cabin ready and cabin lights`

### Task 8: Audio tab

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixCabinForm.Audio.cs`

**Interfaces:**
- Consumes: Task 7's partial members; Task 2's mutations; Task 4's audio lines.

The logic is tested in Tasks 2, 4 and 5; this task wires it, and Task 7's scans keep it honest.

- [ ] **Step 1: Build the page** (all with `AccessibleName` = text): `ListBox` "Audio status" (Audio lines); `CheckBox` "&Boarding music" → `SetMusicOn(isChecked)` (an explicit target, same mutation and variable as the FAP's toggle), no read-back; `ComboBox` "Music &channel" (DropDownList, items = `Channels[i].Name`, built from the data each time the list changes) → `SetChannel(selectedIndex)`, no read-back; buttons "Volume &up" / "Volume &down" → `SetVolume(Volume ± 1)` with read-back `audio.volume` (5 s, speak if unchanged); "Volume down" at 0 refuses "Volume is already 0." (the FAP sends -1; the server's range is unmeasured, Task 10); `ListBox` "Prerecorded announcements" (names); buttons "&Play now" → `DirectPlay(id)` and "&Add to memo" → `AddToMemo(id)` (read-backs `audio.announcing` and `audio.memo`, 5 s, silent if unchanged; no selection → "Select an announcement first."); `ListBox` "Announcement memo"; buttons "&Remove from memo" → `RemoveFromMemo(selectedIndex)`, "Clear &memo" → `ClearMemo()`, "Play &next" → `PlayNext()`, "Play a&ll" → `PlayAll()`, "&Stop" → `Stop()` (read-backs `audio.memo` or `audio.announcing`); `ComboBox` "Announcement s&et" (items `0` … `MaxVariant`) visible only when `MaxVariant` > 0 → `SetAnnouncementsVariant(value)`; buttons "FAP loudspeaker volume up" / "FAP loudspeaker volume down" → `SpeakerVolumeStep` (Task 3) with read-back of `aircraft.communication.cids.fap.speakerVolume` spoken as a percentage ("FAP loudspeaker volume: 80 percent"), and at a limit "FAP loudspeaker volume is already at maximum." / "…minimum." (owner answer 20).
- [ ] **Step 2: Build and run the whole suite; all pass.** Read-only live check: the Audio list reads `Boarding music: off`, `Music channel: Boarding Music, 1 of 1`, `Volume: 5`; the announcements list holds "After Takeoff" and "Safety Briefing". Press nothing.
- [ ] **Step 3: Commit.** `feat(fenix-cabin): audio tab: boarding music, volume and prerecorded announcements`

### Task 9: Doors, Temperature and Water tabs

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixCabinForm.Controls.cs`

**Interfaces:**
- Consumes: Task 7's partial members; Task 3's writes; Task 4's lines.

- [ ] **Step 1: Temperature page.** `ListBox` "Cabin temperatures"; buttons "&Forward warmer", "Forward &cooler", "&Aft warmer", "Aft c&ooler" → `TemperatureStep(zone, selected, offset, warmer)` with read-back `temp.forward` / `temp.aft` (5 s, speak if unchanged); a null step refuses "Forward zone is at its warmest setting." / "…coolest setting." (Aft likewise); unknown values or temperature unavailable refuse "Temperature control is not available."; "&Reset to the cockpit temperatures" → both `ResetTemperatures()` writes, read-back on both keys.
- [ ] **Step 2: Water page.** `ListBox` "Water and waste"; buttons "Preselect &up" / "Preselect &down" → `PreselectStep(current, up)` with read-back `water.preselect` (5 s, speak if unchanged); null refuses "Water preselect is already 100 percent." / "…25 percent."
- [ ] **Step 3: Doors page (D2).** `ListBox` "Doors and slides" (Doors lines); one button whose text follows the selected line: for a ground door, `&Open door 1 left` / `&Close door 1 left` (open when `DoorControl(key)` is 2) → `DoorToggle(key, control)`; for a cargo door, `&Open the forward cargo door` / `&Close …` → `CargoDoor(door, !open)`, refused with "The forward cargo door is moving." while its movement flag is true (the EFB greys it) and "The forward cargo door state is not known." when unknown; for a line with no control (an overwing slide, a door not in `GroundDoors`) the button reads `No door control for this line` and pressing it says so. Read-back on the line's key, 30 s, silent if unchanged. If the owner declines D2, skip this step: the list stays, no button.
- [ ] **Step 4: Build and run the whole suite; all pass.** Read-only live check: Doors lists eight doors on the A321, `Door 1 left: closed, slide armed`, no overwing line, three cargo lines; Temperature reads `Forward zone: 24.0 degrees, target 24.0 degrees, no offset`; Water reads `Water: 85 percent`, `Water preselect: 100 percent`, four lavatory lines A, M, D, E. Press nothing.
- [ ] **Step 5: Commit.** `feat(fenix-cabin): doors, temperature and water tabs`

### Task 10: Owner-in-sim checks and hand-back

- [ ] **Step 1: Full suite and build green;** `git diff --name-only <base-sha>` lists only this plan's files.
- [ ] **Step 2: Hand the owner the in-sim checks** (Handoff section, in this order; each is reversible). The writes are unverified until the owner runs them; record each result (heard phrase, value read back) in this plan's handoff section.
- [ ] **Step 3: Push to `FenixV3`** (index workflow, step 4) with the handoff section filled in, and set this plan's row to "done".

## Handoff to plan 10

**Pilot-facing changes**
- Fenix: input mode (`[`), Ctrl+Shift+F, "Cabin": a Cabin window with five tabs. Status (cabin ready, cabin lights, music, doors, slides, cargo doors, temperatures, water and waste, cautions; a Cabin ready checkbox; a cabin lights choice of Bright, Dim 1, Dim 2, Off), Audio (boarding music on or off, channel, volume; prerecorded announcements: play now, memo, play next, play all, stop), Doors (each door with its slide and pressure warnings; open or close a door or cargo door at the gate), Temperature (forward and aft zone, warmer, cooler, reset to the cockpit temperatures), Water (water, preselect, waste, service panels, lavatories, cautions).
- Anywhere in the app with the Fenix loaded: "Cabin ready" is spoken once when the cabin reports ready. In v3 the virtual purser reports it, not the cockpit cabin call (`docs/fenix-v3/evidence/web-research.md`).
- Guide: the Fenix chapter gets a "Cabin window" section; changelog category `feature`.

**Handoff requests** (none touch the Fenix definition: the monitor starts and stops through plan 01's `StartCabinBackground`/`StopCabinBackground`, implemented in plan 05's own `MSFSBlindAssist/Aircraft/FenixA320Definition.Cabin.cs`; plan 01's `fenix-data.md` globs already cover every plan 05 file)
1. Plan 08 (First Officer): the app-wide "Cabin ready" belongs to plan 05. The First Officer may show cabin ready as a live checklist value (`aircraft.communication.cids.cabinReady`, `FenixCabinNames.CabinReady` once merged) but must never speak it; `Only_the_monitor_speaks_cabin_ready` fails if any other Fenix file (`**/Fenix*.cs`, `Forms/Fenix/**`, `FirstOfficer/Fenix/**`) holds the string literal `"Cabin ready"`. Live settings 2026-10-21: `cabin_ready_call_enabled` True, `cabin_ready_fwc_type` Checklist.
2. Spec 4.3 says "15 music channels"; the IAE A321 Delta livery had one ("Boarding Music") on 2026-10-21. Reword to "a per-livery channel list".
3. For plan 01 / 10: the EFB's own engine enum is `CFM=0, IAE=1, LEAP=2, GTF=3` (EFB chunk 964, offset 30793). It is the order plan 01 assumes for `N_CONFIG_ENGINETYPE` 2 and 3, but it is the EFB's enum, not a reading of that L:var.

**Proposed rule lines** (`.claude/rules/fenix-data.md`; plan 10 numbers them)
- `- [FNX-?] Every Fenix cabin write comes from the FAP/EFB bundles and is built only by `FenixCabinWrites` (name, kind, value) or `FenixCabinPram` (the bundle's mutation text verbatim, its `success` checked); never guess a name or shape, and never write `doors.control.*` 0 (Armed), which the EFB never writes. Full: docs/invariants/fenix-data.md#fnx-?`
- `- [FNX-?] "Cabin ready" is spoken app-wide by `FenixCabinReadyMonitor` alone: once per known false-to-true change of `aircraft.communication.cids.cabinReady` after a silent baseline, unknown values ignored; the Cabin window and the First Officer never speak it (v3: the virtual purser sets it, not the cockpit cabin call). Full: docs/invariants/fenix-data.md#fnx-?`
- `- [FNX-?] The Cabin window lists and controls only the doors, slides and lavatories `FenixCabinLayout.Resolve` gives (Fenix settings `emergency_exits_type`/`aircraft_variant` first, then `FenixVariant.Type`): the data service answers for doors an airframe lacks (the A321 reads overwing slides armed). Full: docs/invariants/fenix-data.md#fnx-?`

**In-sim checks for the owner** (Fenix A321 at the gate, engines off; input mode `[`, Ctrl+Shift+F, "Cabin", Enter; Ctrl+Tab between tabs)
1. Reads: the status box says "Cabin system ready."; Status reads "Cabin ready: no, not available yet", "Cabin lights: Bright", "Doors: all closed", "Slides: all armed".
2. Audio (harmless, reversible): check "Boarding music": music plays in the cabin view; uncheck: it stops. "Volume up": hear "Volume: 6"; "Volume down": "Volume: 5". Press "Volume up" until you hear ", unchanged" and note the top value (the FAP does not clamp).
3. Status and Temperature: cabin lights to "Dim 1", then back to "Bright" (the Status line follows). "Forward warmer": hear "Forward zone: 24.0 degrees, target 24.5 degrees, offset plus 0.5"; "Reset to the cockpit temperatures": "…no offset". Water "Preselect down": "Water preselect: 75 percent"; "Preselect up": back to 100.
4. Cabin ready: through boarding to pushback with the virtual purser on, hear "Cabin ready" once when it reports ready (and not again when the window is open or reopened). If the purser never reports it, note when `.available` becomes true and press the Cabin ready checkbox once; hear "Cabin ready" once.
5. Doors (only if D2 is kept): select "Door 1 left", press "Open door 1 left": within 30 s hear "Door 1 left: open, …"; press "Close door 1 left": hear it closed. Same for the forward cargo door. Report what the slide line reads after closing (the EFB writes Disarmed 1) and whether the purser re-arms it. Announcements: "Play now" on "Safety Briefing": hear "Now announcing: Safety Briefing"; "Stop".
