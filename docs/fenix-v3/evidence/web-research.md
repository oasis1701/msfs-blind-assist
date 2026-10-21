# Fenix A32X Version 3 (3.0.0.7426): research notes, 2026-10-21

Labels used for every claim:
- **[OFFICIAL]**: Fenix's own blog, status page, help centre (support.fenixsim.com) or Fenix's YouTube video.
- **[PRESS]**: flight-sim news sites summarising Fenix's video. These are secondhand.
- **[REPO]**: a third-party tool's GitHub repo, or the HubHop preset database.
- **[COMMUNITY]**: forum posts. This is hearsay.

## 0. Bottom line

- **Official release notes exist.** The blog post "Fenix A32X Version 3 Released!" is dated 2026-10-21 at 11:00 UTC and has a changelog headed **V3.0.0.7426**, the same build as the one installed here. Fenix says the list was cut down from about 4,000 items. [OFFICIAL]
  - fenixsim.com is a JavaScript single-page app, so WebFetch only sees "Loading...". I read the post from the page's embedded Next.js data instead (`__NEXT_DATA__` on https://fenixsim.com/blog).
- **No official third-party, API or SDK notes.** Neither the release post nor the changelog mentions the GraphQL data service, the new `Fenix.GqlGateway.exe`, the `:8083`/`:18083` split, `FenixControlManifest.dll`/`controls.bin`, or renamed L:vars. A web search for "GqlGateway" found nothing public. Treat any API change as unannounced and measure it locally.
- **Fenix's help centre still documents `http://localhost:8083/`.** The EFB settings page, the Web MCDU and document upload are all reached there. That article was updated 2026-10-18 for V3. [OFFICIAL]
- **No neo in V3.** V3 is a CEO-only update, and the A320neo/A321neo are still in development. [OFFICIAL]
- **Third-party tools have not adapted yet.** On release day, none of the tools checked had a V3-specific commit or release: Any2GSX, MobiFlight's Fenix WinWing CDU script, cduhub, PilotsDeck, gsx-integrator and FS2Crew. [REPO]

## 1. Coordinator's priority question: who publishes the `FNX2PLD_*` L:vars?

**Answer: Fragtality's third-party tool FenixQuartz publishes them. The Fenix aircraft never has.**
- `FNX2PLD` comes from the tool's original name, "PilotsDeck_FNX2PLD" ("Fenix to PilotsDeck").

The evidence:
1. **FenixQuartz's own README.** [REPO, Wayback snapshots of github.com/Fragtality/FenixQuartz, 2023-11-02 and 2024-12-14]
   - Its `lvarPrefix` setting defaults to `FNX2PLD_`. The README says the default "still defaults to the old Name FNX2PLD" for compatibility.
   - L:var output only happened in its "Raw-Value/L-Var" mode, that is with `rawValues=true` and `useLvars=true`, through the MobiFlight WASM module. Otherwise it wrote FSUIPC offsets.
   - The 2023 README said the takeoff speeds were always exported as L:vars, whatever the mode.
2. **HubHop presets.** [REPO]
   - Every `FNX2PLD_*` preset on HubHop is labelled `FNX320+FENIXQUARTZ ...`: TO-SPEEDS V1/VR/V2, AUTOPILOT TARGET SPD/HDG/ALT/VS, the dashed LEDs, BAT 1/2, XPDR, CLOCK CHR/ET, ISIS BARO, and COM 1/2 active/standby.
   - So they only work while FenixQuartz is running.
3. **The original code.** The fork `brettjenkins/PilotsDeck_VIZIX` still holds the `PilotsDeck_FNX2PLD` project (2022). It reads Fenix process memory and writes FSUIPC offsets. [REPO]

**FenixQuartz's status:**
- The repo `github.com/Fragtality/FenixQuartz` now returns **404**. It is missing from Fragtality's public repo list, whose most recent pushes are Any2GSX, PilotsDeck, Any2GSX-Plugins, DynamicLOD and CFIT. The last Wayback capture is 2024-12-14. I found no release or commit after that. It looks withdrawn or private and is effectively unmaintained. I found no V3 compatibility statement. [REPO]
- The December 2024 README itself said:
  - FenixQuartz now "reads the native Fenix L-Vars" to compose its strings, and was compatible with "Fenix 2.2.0.232 upwards".
  - The L-Var output modes were **deprecated**, because most data "can now be directly accessed via native Fenix L-Vars". [REPO]
- So `FNX2PLD_*` reading 0 on this machine is expected: FenixQuartz is not installed. This has nothing to do with V3.

**When the Fenix gained its own native readout L:vars:** [OFFICIAL, Fenix blog changelogs]
- **2024-02-28, V2 Block 2 release:** "Added lvars for various hardware readouts (FCU, RMP, ADIRS, XPDR, QNH, Battery Voltage, and Rudder Trim)".
- **2024-03-11, V2.0.0.392:** "Fixed missing EFIS, RMP, ELEC and FCU Lvars".
- **2024-04-15 update:**
  - Added EFIS BARO QNH L:vars (`B_FCU_EFIS*`).
  - Added **TO PERF L:vars (`N_MISC_PERF_TO_*`)**.
  - Added ISFD L:vars (`N_MIP_ISFD_*`, `B_MIP_ISFD_*`).
  - Added the standby XPDR display L:var (`N_FREQ_STANDBY_XPDR_SELECTED`).
- **2024-07-26 update:** added `B_FC_RUDDER_TRIM_DASHED` and `B_ELEC_BUS_POWER_*`.
- **2025-07-28, BFU:** added `L:CONFIG_WEIGHT_LBS` and `S_THIRD_PARTY_REFUELG`.

**Native replacements for each `FNX2PLD_*` variable.**
- The names come from community HubHop presets. The variable families are confirmed by the official changelogs above.
- **I did not check these on V3.** Read them in the sim before relying on them.

| FenixQuartz (`FNX2PLD_`) | Native Fenix L:var |
|---|---|
| `speedV1` / `speedVR` / `speedV2` | `N_MISC_PERF_TO_V1` / `_VR` / `_V2`; flex is `N_MISC_PERF_TO_FLEX`. HubHop's V1 preset has a typo, `L:L:N_MISC_PERF_TO_V1`. |
| `fcuSpd` / `fcuHdg` / `fcuAlt` / `fcuVs` | `N_FCU_SPEED` / `N_FCU_HEADING` / `N_FCU_ALTITUDE` / `N_FCU_VS` |
| `fcuSpdDashed` / `fcuHdgDashed` / `fcuVsDashed` | `B_FCU_SPEED_DASHED` / `B_FCU_HEADING_DASHED` / `B_FCU_VERTICALSPEED_DASHED`. Managed dots are `I_FCU_SPEED_MANAGED` / `I_FCU_HEADING_MANAGED` / `I_FCU_ALTITUDE_MANAGED`, per FenixQuartz's README. |
| `bat1` / `bat2` | `N_ELEC_VOLT_BAT_1` / `N_ELEC_VOLT_BAT_2` |
| `xpdr` | `N_FREQ_XPDR_SELECTED`; standby is `N_FREQ_STANDBY_XPDR_SELECTED`; also `N_PED_XPDR_CHAR_DISPLAYED` |
| `clockChr` / `clockEt` | `N_MIP_CLOCK_CHRONO` / `N_MIP_CLOCK_ELAPSED`; also `N_MIP_CLOCK_UTC` and `N_MIP_CLOCK_MODE` |
| `isisBaro` | The `N_MIP_ISFD_*` family. The exact name is not on HubHop. |
| `com1Active` / `com1Standby` / `com2...` | `N_PED_RMP1_ACTIVE` / `N_PED_RMP1_STDBY`, and the same for RMP2 and RMP3 |
| rudder trim | `N_FC_RUDDER_TRIM_DECIMAL` (HubHop divides it by 10) and `B_FC_RUDDER_TRIM_DASHED` |
| captain/F/O baro | `N_FCU_EFIS1_BARO_HPA` / `N_FCU_EFIS1_BARO_INCH`, and the same for EFIS2 |
| `isVsActive` | No native equivalent found. FenixQuartz computed it itself. |

## 2. What's new in V3

Everything in this section is **[OFFICIAL]** (the 2026-10-21 blog, its changelog and the 2026-10-17 video) unless it is marked otherwise.

**Scope and context**
- Free for existing A320 and A319/A321 owners, on both MSFS 2020 and MSFS 2024, through the Fenix Installer.
- The status page says "We have just released the major V3 update". The A32Xneo is still in development. Status page last updated 2026-10-21 14:05.
- Main areas: flight model (FM), fly-by-wire (FBW), flight director (FD), autopilot and guidance, flight attendant panel (FAP), cabin data system (CIDS), art and active noise cancellation (ANC), ECAM, FADEC, ground model, EFB, and VNAV/FMS.
- Much of this work comes from the neo project and was applied back to the CEO first.
- No new variants, and no LEAP or PW1100G engines. The FADEC was rebuilt to lay "groundwork for the LEAP and PW GTF".
- The MSFS 2024 modular SimObjects, livery.cfg options, walkaround and native MSFS 2024 EFB app already came with the Big Fenix Update (BFU) in July 2025, not V3. V3 adds smaller items marked [2024], such as georeferenced LIDO charts and new circuit breakers.

**Flight model, FBW, FD and autopilot**
- Inertia rebuilt with the MSFS 2024 inertia tensor, with part of it backported to MSFS 2020.
- Retuned rotation, flare and ground effect.
- FBW retuned on all variants.
- Flight directors rewritten to give earlier, smoother guidance.
- Autopilot pitch and roll made less twitchy.
- Fixes to A/THR arming, activation and approach response, SRS, V/S engagement, autoland flare and rollout, and alpha floor and alpha protection.
- Validated against five certified full-flight simulators.

**Ground model**
- A hybrid nose-wheel steering and taxi model, with better wet, slope, tight-turn and braking behaviour.
- Better support for single-engine taxi.
- New setting for Single Engine Taxi Without APU (SETWA).

**ECAM and flight warning computer (FWC)**
- New FWC simulation: two FWCs running out of sync, power-transient handling, and real sensor refresh rates.
- Reworked EMER CANC, CLR, overflow and right-hand memo logic.
- MASTER WARN and MASTER CAUT are now driven by the new FWC engine.
- ECP logic reworked, including key delays.
- TOS1 takeoff-configuration check added.
- Intermediate radio-altimeter callouts added, plus a 500 ft glide-deviation callout setting.
- Fenix says CPU load is about net neutral.

**FADEC and engines**
- Rebuilt for the CFM56 and IAE V2500.
- Independent FADEC per engine.
- Selectable pack-inhibit standards at engine start.
- Auto TOGA option.
- More accurate FLEX validation.
- Hung and hot start logic, start-abort cranking, IAE fuel depulsing, and CFM A-A-B-B igniter alternation.
- Derated takeoff is promised for later. [PRESS, from the video]

**FMS (VNAV, LNAV, MCDU)**
- Large VNAV overhaul following Thales FMS behaviour:
  - idle and geometric descent segments, a DECEL pseudo-waypoint, and level-off deceleration;
  - MORE DRAG and TOO STEEP messages;
  - user-set IDLE and PERF factors.
- LNAV rework: turn transitions, holds (now including database holds), offsets, DME arcs, and xA legs. The navigation display and the guidance now use the same path.
- **In-sim Navigraph navdata updates through the MCDU.** See section 3.
- New DIRECT TO "ABEAM PTS" default setting.
- Visual SIDs handled.
- Many MCDU formatting and colour fixes:
  - pages: F-PLN, PERF TAKEOFF, CRZ, PROG, INIT, SEC, and CLOSEST AIRPORTS;
  - display details: dash sizes, slew-arrow position, and TO SHIFT shown as 4 dashes;
  - approaches labelled "RNV" instead of "RNAV".
- Fixed MCDUs being dark on first start, and an MCDU rendering artifact.

**Cabin (CIDS, FAP, Virtual Purser, airline packs)**
- Full CIDS simulation.
- Detailed Enhanced Flight Attendant Panel (FAP), also reachable on an external device through the EFB.
- **Virtual Purser:**
  - makes announcements;
  - arms and disarms doors;
  - locks lavatories;
  - sets cabin lighting by day and night;
  - runs cabin checks;
  - re-tries unanswered cabin calls.
- **Airline Packs** replace the old announcement packs. They add tags such as `[Overwater]`, time of day, flight phase and variants, plus custom livery tags (`fnx_airline_pack_custom_tags`), EFB wallpapers and FAP screensavers. Old packs still work.
- Water and waste simulation, with a small effect on weight and CG. Servicing through the MCDU or GSX.
- **CABIN READY changes:**
  - New options for how CABIN READY shows: none, checklist or memo (livery key `fnx_cabin_ready_fwc_type`).
  - New setting for the cabin-ready call (`fnx_cabin_ready_call_enabled`).
  - The legacy cabin-ready setting was removed.
  - **A cockpit cabin call no longer sets CABIN READY immediately.** It now only hurries the Virtual Purser along. (Help centre developer guide.)

**EFB**
- New searchable Settings app, with user overrides that persist between flights.
- New Documents app: interactive QRH and ECAM documents that pin themselves on failures, plus custom Markdown and PDF documents in `C:\ProgramData\Fenix\EFB\Documents`. Upload is through `http://localhost:8083`.
- "Departure Perf" renamed "Takeoff", "Arrival Perf" renamed "Landing".
- Non-Navigraph map layer for external EFB devices.
- Fixes to SimBrief import, weights, decimal and thousand-separator input handling, and landing distance.

**Sound and art**
- Rebuilt binaural sound, reworked CFM56 and V2500 sound sets, and over 300 new FWC samples.
- Active noise cancellation, toggled by clicking the cockpit headset.
- Physics-based panel vibration and new cabin window refraction.

**Datalink**
- Fixed a rare crash when a CPDLC message arrives, and some freezes in the Hoppie implementation. No new datalink features.

**Not in V3:** full state saving or persistence, the MEL, the newer FWC software standard, and the neo. [OFFICIAL blog]

## 3. Third-party, API and integration changes

**Official items relevant to an app that drives the Fenix** [OFFICIAL, V3.0.0.7426 changelog and help centre]
- **New key-event bindings.** "Added bindings to MSFS key events for autopilot and pedal disc (water rudder toggle)".
  - The changelog does not list which autopilot events.
- **Changed key-event behaviour.** `TOGGLE WATER RUDDER` is now press-and-hold for the tiller disconnect, not a toggle.
- **Radio SimVars fixed.** "Fixed standby frequencies not updating their respective simvars". Each RMP now has its own local standby frequency, so stock COM standby SimVars may read differently from v2.4.
- **Process volume syncing.** "Added Windows process volume syncing with in-sim ACP volume knobs on startup". Fenix no longer forces 50%. The changelog does not say which processes are affected.
- **FCU logic reworked.** "Improved FCU selected/dashed/managed logic". The semantics of `B_FCU_*_DASHED` and `I_FCU_*_MANAGED` may have changed in edge cases.
- **Warning lights now follow the new FWC timing.** MASTER WARN and MASTER CAUT are now attached to the FWC engine, which deliberately models hardware delays.
- **New EFIS and XPDR fixes.** EFIS QNH no longer sometimes shows 0000. Fixed a green border on the XPDR display with legacy readouts.
- **GSX integration:**
  - New "Complete" and "Abort" menu options.
  - Services started from the GSX menu are now supported, not only from the EFB.
  - New settings to auto-connect the jetway or stairs.
  - Cargo doors no longer close before GSX finishes loading.
  - No deboarding during a gate-to-gate tow.
- **Navdata moved.** Navdata is now updated or imported from the MCDU (DATA > A/C STATUS: CHECK, IMPORT, then activate the SECOND NAV DATA BASE). It is no longer done through Navigraph Hub.
  - Modes: Disabled, Hot Standby, Automatic, Manual Import.
  - Files go in `C:\ProgramData\Fenix\NavdataDFD\Import`, as DFD v2 files named like `2610-r001.zip`.
  - The help article says the old Navigraph Hub method applies to "V2.4.1.0 or older".
  - Before V3 navdata lived in `C:\ProgramData\Fenix\FenixSim A320\navdata`.
- **New livery and settings keys** (help centre, updated 2026-10-21). The full list has about 60 `fnx_*` keys. New or V3-related ones:
  - `fnx_cabin_ready_fwc_type`, `fnx_cabin_ready_call_enabled`, `fnx_cdls_keypad_beep_enabled`
  - `fnx_airline_pack_custom_tags`
  - `fnx_direct_to_default_abeam_enabled`
  - `fnx_single_engine_taxi_without_apu_enabled`
  - `fnx_start_pack_inhibit_type`, `fnx_iae_crank_type`
  - `fnx_gpws500_glide_deviation`
  - `fnx_fap_slide_disarmed_message`
  - `fnx_virtual_purser_*` (three keys)
- **Local web services.**
  - The help centre still tells users to open `http://localhost:8083/` for the EFB, `http://localhost:8083/settings` for EFB settings, and the Web MCDU. That article was updated 2026-10-18.
  - The new Documents article also points upload to `localhost:8083`.
  - Nothing official mentions `:18083`, a gateway, GraphQL, or any change in schema or data refs.
- **Mods are expected to break.** Fenix's release post says the first step for any problem is removing mods.

**No developer documentation.** I found no public Fenix SDK, API or L:var reference for V3. Fenix's help centre lists 75 articles, and the L:var binding example article was last updated 2026-08-10, before V3. Fenix's position, in its "Useful default keybinds" article: not every control binding affects the aircraft "due to it's externalised architecture". [OFFICIAL]

**How community tools use the Fenix data service today, all pre-V3, as a guide to what could break** [REPO]
- **MobiFlight** (`src/MobiFlightConnector/Scripts/Winwing/fenix_winwing_cdu.py`, last changed 2026-04-29):
  - connects to `ws://localhost:8083/graphql/`;
  - subscribes to `OnDataRefChanged`, which calls `dataRefs(names: [...])`;
  - uses the names `aircraft.mcdu1.display` and `aircraft.mcdu2.display`.
- **cduhub** (`library/mcdu-dotnet/FlightSim/FenixA320GraphQL.cs`, last changed 2026-04-05):
  - uses the same display data refs;
  - also uses `system.indicators.I_CDU1_FAIL`, `..._FM`, `..._FM1`, `..._FM2`, `..._IND`, `..._MCDU_MENU` and `..._RDY`, the same for CDU2, and the `system.switches.*` prefix.
  - The repo's last push was 2026-10-10. It has `FenixA320EfbMcdu.cs` too.
- **Fenix2GSX:**
  - **Deprecated** in April 2026; its README tells users to use **Any2GSX** instead.
  - Its log in issue #113 (2025-09) shows it called a Fenix EFB interface at `localhost:8083`.
- **Any2GSX:**
  - Version 0.5.0 (2026-10-13) changed the plugin interface. Fenix plugin users had to update the binary Fenix plugin before loading into the aircraft (issue #20, closed 2026-10-20, a pre-V3 problem).
  - Any2GSX-Plugins was pushed on 2026-10-20, but only the PMDG 737 and A220 plugins changed. No Fenix V3 change yet.
- **PilotsDeck, gsx-integrator (1.48.5, 2026-10-18), FS2Crew Fenix (last changelog 1.25.70, 2026-08-12):** nothing V3-specific found.
- **GitHub-wide search** for "fenix v3", "7426", "gateway 8083" and "GqlGateway" returned only unrelated hits.

**Hardware** [OFFICIAL help centre]
- The WinWing URSA Minor 32 throttle and 32 PAC guide was updated 2026-10-19. It uses standard MSFS bindings and nothing V3-specific.
- One changelog item fixes the rudder-trim animation behaving erratically "when using external hardware".
- I found no V3 notes from MobiFlight, WinWing, Honeycomb, Thrustmaster, FSUIPC or SPAD.neXt.

## 4. Known issues (first day)

- **MSFS 2024 SU7 drag problem.** Fenix flags a known SU7 drag-modelling problem that could affect its VNAV and flight model, linking DevSupport thread 18609. Its testing found nothing major. [OFFICIAL]
- **Mods.** Mods based on the Fenix A320 will likely break. Remove them first. [OFFICIAL]
- **Navdata process changed.** It may surprise users who expect Navigraph Hub. Navigraph sign-in is now done from the EFB Settings > Connections page. [OFFICIAL]
- **Incompatible add-ons.**
  - The installer warns about incompatible third-party add-ons.
  - HUES "zCore" makes the Fenix disappear from the aircraft menu.
  - A TriTri sim graphics mod was flagged too. [COMMUNITY, AVSIM 703091 and 702911 p.5]
- **Re-login and access prompts.** One user had to log in to the Fenix app again and accept an access or firewall prompt that opened behind full-screen MSFS. [COMMUNITY, AVSIM 703091]
  - This fits a new listener on `0.0.0.0` triggering a Windows Firewall prompt, but that is my inference.
- **One crash on load.** One CTD when loading the A319. It was fine on retry. [COMMUNITY]
- **Weather radar tilt** still does not work. This is a quote from Fenix's Discord relayed on AVSIM. [COMMUNITY]
- **Bugs expected.** Fenix expects bugs given the size of the change and asks for support tickets. [OFFICIAL]

## 5. Sources

**Official (Fenix)**
- V3 release blog and changelog V3.0.0.7426, 2026-10-21: https://fenixsim.com/blog/entries/2026-10-21_v3_update (data read from https://fenixsim.com/blog `__NEXT_DATA__`)
- Product status page, updated 2026-10-21 14:05: https://fenixsim.com/status
- "Fenix A320 Version 3" video, 1 h 54 min, 2026-10-17: https://www.youtube.com/watch?v=xC9-l9xdH9k
- Development Update, 2025-11-29: https://fenixsim.com/blog/entries/2025-11-29_dev_update
- 2025 Roadmap, 2025-03-28: https://fenixsim.com/blog/entries/2025-03-28_roadmap
- BFU release, 2025-07-28: https://fenixsim.com/blog/entries/2025-07-28_bfu_release
- V2B2 release, 2024-02-28: https://fenixsim.com/blog/entries/2024-02-28_v2b2_release
- V2.0.0.392, 2024-03-11: https://fenixsim.com/blog/entries/2024-03-11_v2b2_update
- Update, 2024-04-15: https://fenixsim.com/blog/entries/2024-04-15_update_and_sale
- Update, 2024-07-26: https://fenixsim.com/blog/entries/2024-07-26_fenix_installer_and_a320_update
- Help centre article list (Zendesk public API): https://support.fenixsim.com/api/v2/help_center/en-us/articles.json
  - How to update or import navdata, updated 2026-10-21: https://support.fenixsim.com/hc/en-us/articles/12458972723087
  - Livery Configuration Options, updated 2026-10-21: https://support.fenixsim.com/hc/en-us/articles/12466593239567
  - Virtual Purser and Airline Packs Developer Guide, created 2026-10-18: https://support.fenixsim.com/hc/en-us/articles/18038642484239
  - Creating your own EFB documents, 2026-10-21: https://support.fenixsim.com/hc/en-us/articles/18080273012495
  - EFB background and WEB MCDU (localhost:8083), updated 2026-10-18: https://support.fenixsim.com/hc/en-us/articles/12458461738255
  - EFB Sim Settings Explained, updated 2026-10-13: https://support.fenixsim.com/hc/en-us/articles/12375022304015
  - GSX Integration Guide, updated 2026-08-13: https://support.fenixsim.com/hc/en-us/articles/12374166453135
  - WinWing URSA Minor guide, updated 2026-10-19: https://support.fenixsim.com/hc/en-us/articles/14119688746255
  - Useful default keybinds, updated 2026-09-28: https://support.fenixsim.com/hc/en-us/articles/12466613431823

**Press (secondhand)**
- Threshold, 2026-10-17: https://thresholdx.net/fenix-simulations-announces-colossal-a320-v3-update-release-next-week/
- Simulation Daily, 2026-10-17: https://simulationdaily.com/news/microsoft-flight-simulator-fenix-a320-v3/
- MSFS Addons, 2026-10-18: https://msfsaddons.com/2026/10/18/fenix-a320-version-3-is-a-free-update-that-changes-almost-everything-you-see-hear-and-touch/
- FSElite, 2026-10-18: https://fselite.net/content/fenix-details-a320-v3-with-major-navigation-systems-and-cabin-overhaul-releasing-next-week/
- simFlight, 2026-10-19: https://www.simflight.com/2026/10/19/fenix-a320-v3-the-big-update/

**Community**
- AVSIM, "Fenix A3xx Update v3 released [21OCT26]", 2026-10-21: https://www.avsim.com/forums/topic/703091-fenix-a3xx-update-v3-released-21oct26/
- AVSIM, "Fenix A320 v3 landing next week!", 6 pages, 2026-10-17 to 10-21: https://www.avsim.com/forums/topic/702911-fenix-a320-v3-landing-next-week/
- Reddit could not be reached: the search tool and the site both blocked it.

**Third-party repos and databases**
- FenixQuartz README:
  - Wayback, 2023-11-02: http://web.archive.org/web/20231102103848/https://github.com/Fragtality/FenixQuartz
  - Wayback, 2024-12-14: http://web.archive.org/web/20241214155239/https://github.com/Fragtality/FenixQuartz
  - The live repo returns 404.
- PilotsDeck_FNX2PLD origin, fork: https://github.com/brettjenkins/PilotsDeck_VIZIX
- HubHop presets (`FNX320+FENIXQUARTZ`, native `N_*`), queried 2026-10-21 through the HubHop preset database.
- Fenix2GSX deprecation notice in its README, April 2026: https://github.com/Fragtality/Fenix2GSX
- Fenix2GSX issue #113 (localhost:8083 EFB interface): https://github.com/Fragtality/Fenix2GSX/issues/113
- Any2GSX issue #20, 2026-10-16 to 10-20: https://github.com/Fragtality/Any2GSX/issues/20
- Any2GSX-Plugins CHANGELOG and commits up to 2026-10-20: https://github.com/Fragtality/Any2GSX-Plugins
- MobiFlight `fenix_winwing_cdu.py`: https://github.com/MobiFlight/MobiFlight-Connector/blob/main/src/MobiFlightConnector/Scripts/Winwing/fenix_winwing_cdu.py
- cduhub `FenixA320GraphQL.cs`: https://github.com/vradarserver/cduhub/blob/main/library/mcdu-dotnet/FlightSim/FenixA320GraphQL.cs
- FS2Crew Fenix changelog: https://fs2crew.com/fs2crew-fenix-a320-change-log/
