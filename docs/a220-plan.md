# Synaptic A220-300 — Implementation Plan (pre-release, v2)

> **Historical document.** This is the plan written BEFORE the aircraft shipped, kept
> because it records why each transport was chosen and what the vendor documentation
> said at the time. It is NOT a description of the current state: most of it is built
> and flown, some of it was overturned by live measurement, and the "nothing here is
> sim-verified yet" line below was true in July 2026 and is not true now. For what the
> code actually does, and for every finding that came out of the sim, read
> [a220.md](a220.md) — that is the maintained document. The two source documents this
> plan cites but does not include (the vendor's PDF manual and the user's own Solo Pilot
> Guide) are not redistributed here.

**Status:** planning — aircraft releases 27–31 July 2026 (MSFS Marketplace, FS2020+FS2024; user buys the FS2020 Marketplace version). Nothing here is sim-verified yet.
**Plan updated:** 27 July 2026, from three new sources:

1. **Official manual v1.0.0** (22 July 2026) — `A220_MSFS_Manual_v1.0.0.pdf` (34 pages, image-based; fully transcribed).
2. **Official docs site** (docs.synapticsim.com, repo `synapticsim/docs`, updated 27 July): the **SimVars page is now filled in** — the complete official L:var surface, organized by system — plus an **Inputs page** listing every sim event the aircraft consumes. Both vendored verbatim at `tools/a220-gen/reference/simvars.mdx` and `inputs.mdx` (re-fetch from the repo before implementation in case of updates). Integrations/map-server/introduction pages are still scaffolds.
3. **User's Solo Pilot Guide** (`Synaptic_A220_Solo_Pilot_Guide.docx`, 27 July) — blind-pilot gate-to-gate workflow; defines what the implementation must make possible.
4. **"Flight Guide by A220 Driver"** (community pilot guide PDF, 302 pp, linked from docs.synapticsim.com; mined 27 July) — real-procedure FMA/ASA vocabulary, autoland gates, standard PM callouts, latched-mode traps. Source of the W4 announcement additions below (page refs: FMA/modes 25–26/39–44, approach ops 154–161, CAT II/III + autoland 174–175, standard callouts 138–139, abbreviated procedures 285–286).

Already in the tree (untracked, from the 24 July session): `tools/a220-gen/generate_a220_ecl.py` → `MSFSBlindAssist/Aircraft/A220/SynapticA220EclData.cs` (215 ECL sensed vars + 574 CAS messages, index = ABI) + pin tests. Verified 27 July: **no ecl-editor commits since our clone** — the generated data is current.

---

## 1. The four transports (decided)

| Surface | Transport | Existing MSFSBA idiom |
|---|---|---|
| Systems state + switches (overhead/ICCP, FCP annunciators, engine, gear, lights…) | **Official L:vars** (`L:A22X *`, `L:INI_*`) — documented in simvars.mdx | A380/FBW L:var panels, calc-path writes |
| Physical controls with stock-event bindings (FCP knobs/buttons, flaps, radios, XPDR, trim, autobrake, engine masters, TOGA) | **Stock K: events** — documented in inputs.mdx | PMDG/iFly MCP-style event sends |
| Avionics text (FMS pages, EICAS/CAS list, ECL live state, PFD/ND detail, EFB) | **Coherent GT debugger (:19999)** scrape/drive agents — Synaptic avionics are TS instruments (mach bundler), same family as FBW/HS787 | HS787 CDU, A380 MFD/EWD, PMDG EFB |
| Checklist **content** | `checklists.json` (ecl-editor schema; custom packs live plaintext in `Community\inibuilds-aircraft-a220\Config\Synaptic` — confirmed by manual p.31) + live check-state over Coherent | iFly checklist + new "first officer" feature |

Marketplace DRM is at-rest encryption only; L:vars/events/Coherent all work on a running Marketplace aircraft (HS787 precedent). The one likely loss: the *stock* checklist JSON on disk may be encrypted → read stock checklist content from the running ECL instrument over Coherent (recon item R6).

---

## 2. Critical implementation gotchas (read before writing any code)

1. **⚠️ A220 L:var names contain SPACES** (`L:A22X APU Switch`, `L:A22X L Boost Pump`). `SimConnectManager.SetLVar` deliberately routes any name containing a space/colon to the **unreliable data-def write path** (`SimConnectManager.EventSend.cs` ~line 36 guard — correct for stock-SimVar shapes, wrong for these). **Every A220 L:var write must therefore go through `ExecuteCalculatorCode` directly** (`2 (>L:A22X APU Switch)`) from the def's `HandleUIVariableSet` catch-all — the A380 OVHD catch-all pattern. Do NOT "fix" the global guard: it protects `TRANSPONDER STATE:1`-style names for every other aircraft. Recon must verify the RPN parser accepts spaces inside `(L:…)`/`(>L:…)` (expected yes — parser reads to the closing paren; test day 1, R2).
   - Reads are fine: the LVar read path registers `L:{name}` as a data-def datum, which accepts spaces.
   - The generic "space/colon = stock SimVar" classification note in architecture.md gains an exception: **`A22X `/`INI_` prefix = L:var regardless of spaces** (same lesson as the A380 `A32NX_FUEL_USED:n` prefix rule).
2. **`INTERACTIVE POINT OPEN:{N}` is stock** — doors use it with documented indices (0=fwd pax, 1=fwd service, 2=aft pax, 3=aft service, 4=aft cargo, 5=fwd cargo, 6/7=overwing L/R, 10–12=equipment bays). Never force-register as L:var (existing invariant; broke A380 detection once).
3. **APU switch is a hold-to-start**: enum 0=Off/1=Run/2=Start, and the manual says hold START ≥3 s. The combo's Start action must write 2, sustain it ~3 s (timer re-writes with the `{seq} 0 *` anti-coalescing prefix — MobiFlight drops identical consecutive calc strings), then return to 1. Same sustained-write pattern as the A380 seat motors.
4. **Def budget**: simvars.mdx alone lists ~350 L:vars (incl. ~90 lamp outputs and ~120 aural flags). Do not register the aural flags at all (the aircraft plays those sounds itself — announcing them would double up audio). Lamps register as `Continuous+IsAnnounced` batch vars (0 individual defs, MD-11 pattern); watch `registration.log` `approxTotalDefs`.
5. **Aural callouts are native**: the aircraft speaks RA callouts (EFB-configurable: 5/10/20/30/35/40/50/80/100/200/300/400/500/1000/2500 ft), V1, takeoff-config items, TAWS. MSFSBA must NOT duplicate them. Our generic monitors that would collide (e.g. any future V-speed callouts) stay off for this def; the existing 1,000-ft `AltitudeCalloutAnnouncer` etc. are fine (different information).
6. **Entry Mode is a hazard, not a feature, for us**: the aircraft's hardware-keyboard FMS entry mode **disables ALL other sim inputs** while active. A blind user who forgets it is stranded ("dead controls" — troubleshooting §20.4 of the guide). Our FMS accessibility must drive the scratchpad **via Coherent**, never via Entry Mode; and the def should detect/announce Entry Mode state if a var for it exists (recon R5).
7. **Guarded/momentary controls** follow the A380 rule: ongoing-state controls = combos (all the enum switches); true one-shots (chrono, master warn/caution cancel, IDENT, lamp test) = buttons with `SuppressRestingButtonState` where applicable.
8. **Self-announcing defs need the global echo wrap** — already all-aircraft (the `announcer.Suppressed` wrap around `ProcessSimVarUpdate`), nothing to do, just don't regress it.

---

## 3. Workstreams — "exactly all the things"

### W1. Aircraft definition core
`SynapticA220Definition : BaseAircraftDefinition` + partial classes (A380/HS787 file layout): `.cs` (core/detection), `.PanelControls.cs`, `.SimVarUpdate.cs`, `.UiVariableSet.cs` (calc-path catch-all), `.Dialogs.cs`, `.Displays.cs`.

- **Generator**: extend `tools/a220-gen` with `generate_a220_simvars.py` parsing `reference/simvars.mdx` → `SynapticA220SimVarData.cs` (name, unit, enum value labels, read/write role parsed from the description verbs). Pin tests like `SynapticA220EclDataTests`. Hand-curated overrides live in the generator (ifly-gen convention), never hand-edit output.
- **Detection**: ATC model/title match for "A220-300" (recon R1 confirms exact aircraft.cfg strings; OFP type code is BCS3).
- Menu item in `MainForm.Designer.cs`, `LoadAircraftFromCode` switch entry.

### W2. Panels (combos/buttons from documented L:vars + events)
Mirror the manual's cockpit geography as the section/panel tree:

- **Overhead — Electrical**: BATT 1/2 (`ELECTRICAL MASTER BATTERY:{1,2}`), GEN L/R/APU off switches, bus isolation (0=Main/1=Auto/2=Ess), cabin power, RAT (guarded), EXT PWR (`EXTERNAL POWER ON`; avail = `L:INI_GPU_AVAIL`), gen disconnect (guarded).
- **Overhead — Hydraulics**: PTU (Off/Auto/On), ACMP 2B/3A/3B (Off/Auto/On), Hyd 1/2 SOV.
- **Overhead — Fuel**: L/R boost pumps (Off/Auto/On), manual transfer (Off/Right/Center/Left), gravity transfer (guarded).
- **Overhead — Air/Bleed**: L/R bleed, APU bleed, crossbleed (Closed/Auto/Open), L/R packs, pack flow, ram air (guarded), trim air, zone temp knobs (cockpit/fwd/aft cabin %), cargo air (fwd: Off/Vent/Lo Heat/Hi Heat; aft: Off/Vent), recirc, manual temp.
- **Overhead — Anti-ice/Heat**: cowl anti-ice L/R (Off/Auto/On), wing anti-ice (Off/Auto/On), probe heat, window/windshield heat L/R.
- **Overhead — Pressurization**: manual press mode, manual rate knob, emergency depress (guarded), ditching (guarded), pax oxygen (guarded).
- **Overhead — Fire**: L/R/APU fire push (guarded), extinguisher bottles, cargo fire; ELT (Test/Arm/On); evac.
- **Overhead — Flight controls/misc**: PFCC 1/2/3, CVR, TAWS inhibits (gear/terrain/flaps/GS), aural warn inhibit.
- **Eyebrow — Lights & signs**: nav, beacon, strobe, logo, wing insp, taxi (Off/Narrow/Wide), landing L/R/nose, emergency lights (Off/Arm/On), seat belts (Off/Auto/On), no-PED (Off/Auto/On), dome, annun (Dim/Bright/Storm) + lamp test button.
- **Pedestal — Engine**: engine masters 1/2 (`ENGINE_MASTER_{n}_SET`), start mode selector (L Crank/Auto/R Crank), continuous ignition; rudder trim (RUDDER_TRIM_LEFT/RIGHT + readout); parking brake (`PARKING_BRAKE_SET`, state `L:A22X Parking Brake`).
- **Main panel — Gear/brakes**: gear lever (stock `GEAR HANDLE POSITION`; recon gear events R8), alternate gear, autobrake (LO/MED/HI events + `INCREASE/DECREASE_AUTOBRAKE_CONTROL`; test `SET_AUTOBRAKE_CONTROL` live — HS787 precedent says SET may no-op, step-walk fallback proven; read-back `L:A22X Autobrake`), nose steer off, alternate brake.
- **Flaps/spoilers**: flap combo detents 0–5 (`FLAPS_SET`/detent events; state `L:A22X Flap Lever`), alternate flap; speedbrake via stock spoiler events (note: SPOILERS_ARM_* are consumed/masked — no arm function; do not expose an "arm spoilers" control).
- **Glareshield — CTP (per side L/R)**: baro knob (`{side} Altimeter Set` delta, `Altimeter STD`, `Altimeter HPA` unit), nav source cycle, crosstune. Display-select/QAK keys (MAP/FMS/CNS/CHKL/SYN/DATA, TERR/TFC/WX, INBD L/R) likely have no L:vars → Coherent/H-event recon (R4); only expose what has a working transport.
- **Glareshield — Master warn/caution**: cancel buttons (`L:A22X Master Caution Warning` settable to acknowledge), chrono L/R.

Every combo: documented enum labels verbatim (e.g. "Off/Auto/On"), write via calc path catch-all (gotcha #1), read-back confirm per troubleshooting playbook. Lamp vars are **stripped from panel display sets** (A380 invariant) and go to the monitor (W4).

### W3. FCP (autoflight) — dialogs, hotkeys, mode announcements

**The FULL hotkey surface must be wired — not just the dialogs.** Model = the iFly def's `HandleHotkeyAction` switch (`IFly737MAXDefinition.cs` ~1800): every `HotkeyAction` below gets a case (or falls through to a working BaseAircraftDefinition stock-simvar default) — none may silently no-op on this aircraft.

- **FCP set dialogs** (Ctrl+S/H/A/V/B pattern, iFly/PMDG style): `FCUSetSpeed`/`FCUSetHeading`/`FCUSetAltitude`/`FCUSetVS`/`FCUSetBaro`, plus `FCUSetAutopilot` (AP/AT/mode toggles dialog) and `SetNavRadios`. `GetSpeedControlType()` etc. declared per what recon R8 proves (SetValue if a direct-set event exists, otherwise inc/dec walk).
- **Dialog completeness rule (user ruling 2026-07-27): every FCP dialog carries buttons for ALL functions of its physical control, not just value entry** — each knob's push/ring/toggle eventualities are reachable from its dialog:
  - **Speed (Ctrl+S)**: value entry + **FMS SPD ↔ MAN SPD** (outer ring) + **knots ↔ Mach** (inner push; auto-switches at 27,500 ft) — read-back always states mode + unit + target.
  - **Heading (Ctrl+H)**: value entry + **push-to-sync** (sync to current heading; shows AUTO when HDG not active) + **½ bank toggle** (HDG-only below 31,500 ft, auto above).
  - **Altitude (Ctrl+A)**: value entry + **FT ↔ M unit ring** (`FG Altitude Unit`) — walk math reads the live **fine/coarse** state (`FG Altitude Fine`, 100 vs 1000 ft) rather than assuming — **plus the "fly there" mode buttons: FLC** (`FLIGHT_LEVEL_CHANGE` — the book's default for altitude changes), **VNAV** (`FG VNAV Toggle` pulse), and **ALT hold** (`AP_ALT_HOLD`), so set-altitude-and-go is one dialog. Read-back confirms the engaged/armed result ("FLC, altitude 5000 armed") from the FG vars, not the button press. VS/FPA stay in Ctrl+V (their own values live there), matching the set-then-choose-mode flow of the other aircraft.
  - **VS/FPA (Ctrl+V)**: VS value + FPA value (±9.9°, 0.1 steps) + **explicit engage buttons for BOTH modes** — VS engage (`AP_VS_HOLD`), FPA engage (`AP_ATT_HOLD`) — the thumb-wheel value and the mode are set from one dialog. Read-back names the engaged mode + its value from `FG Vertical Speed`/`FG Flight Path Angle` + `L:A22X Selected FPA` (selected-VS readout var = recon R8), never a bare number. FPA is the aircraft's default vertical mode.
  - **Baro (Ctrl+B)**: QNH value + **STD toggle** + **hPa ↔ inHg** unit, per side via the CTP vars.
  - **Autopilot (Ctrl+P, `FCUSetAutopilot` — same hotkey as every other aircraft)**: AP on/off, AT arm/disconnect, FD L/R, XFR, ½ bank, **TOGA** (`AUTO_THROTTLE_TO_GA`), and the mode buttons (HDG/NAV/APPR/FLC/ALT/VS/FPA/VNAV). **TOGA belongs in this dialog (user ruling 2026-07-27)** because it's a required *before-takeoff* action, not just a go-around control: pressing it on the ground arms the TO/TO FMA modes for the takeoff roll — the blind pilot needs it reachable from the same Ctrl+P dialog as the rest of the FCP. **A TOGA press must ALWAYS speak the resulting FMA (user ruling 2026-07-27) — that announcement is the only confirmation the press worked**: once the PFD FMA scrape (R7/P3) exists, read back the actual FMA line after the press ("TO TO"; go-around "GA GA"); in P1, the stopgap reads the `FG *` vars and says the best truth available. A TOGA press must never be followed by silence — if the FMA/FG state didn't change (e.g. inhibited), say that explicitly. It's also the only exit from a latched approach mode (see W4), so the latched-APPR honest read-back can point the user straight back at this button. Note the ROLLOUT inhibit (W4): with ROLLOUT active the TOGA switches are inhibited — read-back must say so rather than falsely confirm. **Every mode press announces the RESULT — the mode set that actually armed/engaged — never the button press** (user ruling 2026-07-27): P1 from the `FG *` bools ("Approach armed"), P3+ the real FMA answer ("APPR LOC 1, glideslope white" / "APPR FMS 1, VGP white" — the same confirmation call the book's PM makes). Honest read-back on the latched cases (APPR below 1500 ft = TOGA-only; FCP AP-disconnect inhibit on ILS final). EDM stays OUT of the dialog (guarded emergency control; panel combo only).
- **FCP/mode READ hotkeys**: `ReadHeading`/`ReadSpeed`/`ReadAltitude` (selected FCP values + engaged mode context, iFly style: "Heading 250, HDG mode"), `ReadFCUVerticalSpeedFPA` (selected VS or FPA from `L:A22X Selected FPA`), `ReadApproachCapability` if the A220 exposes an equivalent (recon; otherwise announce "not available" rather than silence), `ReadNavRadioInfo`, `ReadAltimeter` (baro setting + STD state from the CTP vars).
- **Aircraft-state readout hotkeys**: `ReadFuelQuantity` and `ReadFuelInfo` (total + per-tank from stock fuel simvars; kg/lb per user setting), `ReadFlaps` (flap lever detent 0–5 by NAME from `L:A22X Flap Lever` + surface position), `ReadGear` (gear + door state), `ReadGrossWeightKg`, `ReadWaypointInfo`/`ReadDistanceToDest`/`ReadDistanceToTOD` (from stock GPS simvars until the FMS scrape lands).
- **Dialogs** detail: speed (AP_SPD_VAR_INC/DEC + kts/Mach toggle `AP_MANAGED_SPEED_IN_MACH_TOGGLE`; **plus an explicit FMS SPD ↔ MAN SPD switch** — the speed knob's outer ring, the A220's managed/selected split, used constantly in procedures ("SPD MAN VAPP"). State = `FG Speed Mode` enum, but NO documented input event exists for the ring — write transport is recon R8), heading (HEADING_BUG_INC/DEC; `HEADING_BUG_SET` is documented as *sync-to-current-heading* — recon whether it takes a param, R8; otherwise inc/dec walk like Fenix counters), altitude (AP_ALT_VAR_INC/DEC — respect the panel's **units + fine/coarse** state vars `FG Altitude Unit`/`FG Altitude Fine`; walk math must know the live increment), VS (AP_VS_VAR_INC/DEC), FPA (`AP_ATT_HOLD` mode + `L:A22X Selected FPA` readout).
- **Mode buttons**: AP (`AP_MASTER`), AT (`AUTO_THROTTLE_ARM` / `AUTO_THROTTLE_DISCONNECT`), HDG (`AP_HDG_HOLD`), NAV (`AP_NAV1_HOLD`), APPR (`AP_APR_HOLD`), FLC (`FLIGHT_LEVEL_CHANGE`), ALT (`AP_ALT_HOLD`), VS (`AP_VS_HOLD`), FPA (`AP_ATT_HOLD`), VNAV (`L:A22X FG VNAV Toggle` pulse), ½-bank (`AP_MAX_BANK_ANGLE_SET`), FD L/R (`L:A22X {L,R} Flight Director`), XFR (`FG Source Transfer`), TOGA (`AUTO_THROTTLE_TO_GA` — the documented binding), AP disconnect (`AUTOPILOT_OFF`).
- **FMA/mode monitor — the L:vars are only a stopgap; the PFD FMA scrape is REQUIRED for approach ops** (pilot-guide finding, 2026-07-27). The documented `FG *` L:vars are per-button booleans; the real FMA vocabulary the book procedures are gated on cannot be derived from them:
  - Lateral: `TO, GA, HDG, ROLL, FMS1/2, LOC1/2, VOR1/2 (+DR), APPR FMS1/2, APPR LOC1/2, APPR VOR1/2, APPR B/C1/2` — and bare **`APPR LOC`** (no digit) = guidance handed to the PFCCs below 2000 ft, an explicitly-called procedure gate. `FG Approach`=1 can't distinguish any of these.
  - Vertical: `GS, VGP, VPATH, (V)FLC, (V)VS, (V)FPA, PITCH, (V)ALT/(V)ALTS/VALTV (+CAP)`, reversions `OSPD/USPD/WSHR/EDM`. The V-prefix (VNAV) and CAP (capturing) states are spoken by the crew ("ALT-S CAP" → "ALT-S").
  - AT column: `THRUST, HOLD (60 kt–400 ft, levers frozen), SPD, LIM (can't reach target — early over/underspeed warning), RETARD`.
  - **Armed vs active is announced differently** (callout standard: active modes plain, armed modes with color — "APPR LOC 1, GS WHITE"); up to 3 vertical modes armed at once; failed modes amber.
  - Plan impact: engaged-mode L:var monitor ships in P1 as a stopgap, but the PFD FMA string scrape (R7) is the TOP item of the Coherent wave (P3), not polish — approach/autoland announcements depend on it.
- Readouts: selected heading = `AUTOPILOT HEADING LOCK DIR`; selected alt/speed/VS presumably stock `AUTOPILOT *` vars (recon R8); FPA = `L:A22X Selected FPA`.
- **Readout/dialog behavior details from the pilot guide (2026-07-27):**
  - HDG knob push = sync to current heading; when HDG is NOT the active mode the display shows **"AUTO"** (continuously synced) — `ReadHeading` must voice that state, not a stale number.
  - Speed target has managed/selected state (`FG Speed Mode`: FMS magenta vs manual cyan; IAS↔Mach auto-switches at 27,500 ft) — `ReadSpeed` voices "FMS speed" vs "manual 250".
  - **FPA is the default vertical mode** (±9.9°, 0.1 steps) and HDG the default lateral; TO mode holds runway heading and does NOT follow the HDG bug (mode context matters in readouts).
  - EDM engage auto-sets squawk 7700 + altitude 15,000 ft (+ AP/AT on, HDG+FLC) — announce the mode, don't fight the automatic FCP changes.
  - FCP AP-button disconnect is **inhibited below 1500 ft on an ILS approach** — the sidestick AP/PTY button is the working disconnect there; `FCUSetAutopilot`'s read-back must say "still engaged" honestly rather than assume the press worked.

### W4. Monitors & announcements
- **Annunciator/lamp monitor**: all `* Lamp`/`* Fail Lamp`/`* Off Lamp` vars as Continuous+IsAnnounced batch (baseline-first, silent first read, reset on aircraft switch/reconnect). Meaningful state changes only.
- **ASA (Approach Status Annunciator) monitor** (pilot guide pp. 42–43/174–175; not in the L:var list → PFD scrape, R7): announce capability when it appears (**APPR 1 / APPR 2 / LAND 2 / LAND 3 / STEEP**, shown 1500–800 ft AAE with AP on + landing config) and every **downgrade urgently** (**NO APPR 2 / NO LAND 3 / NO AUTOLAND** — flashing amber above 200 ft RA, red below; downgrades are go-around triggers in the CAT II/III procedure). Also announce the **"LAND 2/3 NOT AVAIL"** advisory (hand-flying: APPR 2 shows until AP is on). ASA is blank for all non-localizer approaches — silence there is correct, don't invent a state.
- **Autoland sub-mode gate announcements** (PFD scrape): **ALIGN** (200–150 ft), **FLARE** (below 65 ft), **RETARD** (below 20 ft), **ROLLOUT** (2 s after WOW) — and the **absence call at each gate ("No align", "No flare", "No retard", "No rollout")**, because each "NO" is an immediate go-around/manual-action trigger and silence is indistinguishable from a working autoland. Related facts to announce/document: with ROLLOUT active the TOGA switches are inhibited (go-around must be manual); rudder/NWS input disengages the AP during rollout; rollout guidance ends below 30 kt GS.
- **PM-callout announcer** — the pilot guide's Pilot Monitoring role is exactly the crew member a blind pilot doesn't have; these are human callouts, NOT native aurals (verified against the simvars.mdx aural list — the aircraft only speaks V1, radio heights, PLUS-100/MINIMUMS, and warnings):
  - **AT-takeover call on the takeoff roll (user ruling 2026-07-28): a VERY CLEAR announcement the moment the autothrottle takes over.** The A220 procedure is manual lever advance to ~70% N1, then the AT takes command and sets takeoff thrust — the blind pilot must know exactly when to stop pushing and let go. Announce the takeover edge explicitly (e.g. "Autothrottle has thrust — takeoff thrust set"), keyed on the FMA AT column becoming active (THRUST) once the PFD scrape (R7/P3) exists; P1 stopgap from the best available AT-engaged state var. Also announce the **HOLD transition at 60 kt** ("Autothrottle hold — levers frozen") since from then to 400 ft the levers are servo-frozen and manual lever movement is real again. This is a takeover-edge call, not a continuous mode chatter — one call per transition.
  - **"Rotate" at VR** — only V1 is an auto-callout; VR/V2 must come from us (V-speed source = recon R11).
  - **"Localizer alive" / "Glideslope alive"** + a **"within 1 dot"** cue — pairs with the FOT-mandated vectored-ILS procedure (delay APPR until LOC deviation < 1 dot; A220-FOT-22-30-012). Deviation standards: 1 dot lateral/vertical, ½ dot RNP-AR, 5° NDB, 100 ft NPA vertical.
  - **"Stabilized" / "Not stabilized" at 1000 ft AAE** (within 1 dot, VREF…VREF+20, sink ≤ 1000 fpm, gear + landing flap, checklists complete).
  - **Final-approach exceedance calls**: "Speed" (target +20/−5 kt), "Sink rate" (>1000 fpm); in the flare "Bank" (>7°) and "Pitch" (>7.5° up / below 0°) — the A220's tail-strike-protection calls.
  - **"Reverse green" / "No reverse"** on rollout (`L:A22X Engine {n} Reverser` deploy %).
  - All discrete/rate-limited, baseline-first, and must never overlap the native-aural list (gotcha #5).
- **Latched-mode cautions — silence misleads, announce the latch**: once APPR 1/2 or LAND 2/3 shows on the ASA (ILS at/below 1500 ft AAE), **approach mode cancels ONLY via TOGA** — an APPR re-press does nothing, so our read-back path must say "approach mode latched, TOGA to cancel", not just fail quietly. Same trap on circling: GS latches below 1500 ft AAE and will NOT level at MDA (procedure: select NAV / deselect VNAV before 1500 ft — FPA becomes active).
- **CAS/EICAS monitor** (headline safety feature, ≈ A380 EWD monitor): live message list must come from the EICAS instrument over Coherent (recon R7 — find the view + JS state). `SynapticA220EclData` CAS table (574 messages, 4 levels) classifies Warning/Caution/Advisory/Status for prioritized announcements. Baseline-silent on connect.
- **Flight stage**: `L:A22X Flight Stage` enum (Hangar/Taxi/Apron/Runway/Climb/Cruise/Approach/Final) — phase context for monitors and the checklist Summary tile.
- **Ctrl+M monitor manager**: dynamic var list (Fenix pattern); default-off for chatty vars.

### W5. FMS accessibility (Coherent-driven, the biggest new build)
The Pro Line Fusion model is **scratchpad-first**: type into the MKP scratchpad, then click a target field on any MFW. No LSKs. Our approach (all recon-gated, R5):
- Scrape the FMS window state (tiles `DBASE/POS/FPLN/PERF/ROUTE`, mode `ACT/SEC/MOD`, FPLN sub-tabs `INIT/WIND-TEMP/FUEL/ETP`, PERF DEP/CLB/CRZ/DES) from the DU5 instrument's JS state over Coherent — accessible form presenting pages as list rows (Shift+M CDU-form idiom, but page/field-shaped rather than 14×24 grid).
- Drive input by calling into the instrument (set scratchpad text, focus/commit target field) — the A380 lesson applies: find what the real MKP hardware keys publish (likely H: events or an instrument EventBus topic like the A380 KCCU `bus.pub('hEvent', …)`) and speak that protocol; never synthesize DOM clicks first.
- First-class guided flows (buttons in the form, mirroring the guide): **SimBrief route uplink** (SEC → FPLN → SEC INIT → FPLN UPLINK → SIMBRIEF → SEND → ACTIVATE SEC → EXEC — the #1 troubleshooting trap), **wind request** (FPLN → WIND/TEMP → FPLN WIND REQ), **FUEL page ZFW/CG/fuel entry** (mandatory for VNAV), **PERF DEP SET VSPEEDS** (the "SEND TO FMS isn't enough" trap), navdata source/reload status.
- `L:A22X Flight Plan Modified/Execute/Cancel` give EXEC-prompt state + programmatic EXEC/CNCL — useful and transport-cheap.
- **No extra guided flows beyond the list above (user ruling 2026-07-27)** — in particular the pilot guide's "Final Approach Track Extension" (Direct-To FAF/IF + CRS + EXEC, required on every radar-vectored approach to sequence the plan so NAV has a magenta leg to capture) and the PERF ARR setup are NOT dedicated flows. The generic FMS form just has to make them *possible* (Direct-To page + CRS field reachable); the W4 "within 1 dot" cue covers the timing half of the vectored-ILS procedure.

### W6. ECL — checklist reader + "first officer" actuation (user's headline ask)
- Read live checklist state (current checklist, item states, sensed completion) from the CHKL instrument over Coherent (R6); content schema already generated.
- Accessible checklist form: Summary/Normal/Non-Normal/Procedures tiles, item challenge/response, sensed state; FCTN operations (RESET CHKL/RESET ALL/OVERRIDE ITEM/OVERRIDE CHKL).
- **"Complete this item for me"**: map each of the 215 `ECL_VARIABLE` sensed states to the corresponding MSFSBA control write (the W2 panel map largely IS this map — build the ECL-var→control-key table in the def, coverage-tested so every sensed var either maps or is consciously excluded). We never tick the box ourselves: actuate the switch, let the aircraft's own sensing confirm it; if not sensed complete in ~2 s, say so. Item-at-a-time only — no "complete whole checklist" button; FCTN overrides stay separate deliberate actions.
- **Actuation scope (user ruling 2026-07-27): engine start and APU start ARE in scope.** The user wants start-type items automatable. Excluded from actuation: thrust levers (hardware axis fights any write), sidestick/flight-controls check, guarded destructive items (fire handles); judgment/"as required" items present-and-confirm only.
- **Timing-quirk controls are encapsulated in the control write itself** so the panel combo and the ECL do-item share one implementation: APU start = write 2, sustain ~3 s with seq-prefixed calc re-writes, return to 1 (gotcha #3); engine start = set start selector + run switch, then monitor the (slow, ~1 min 40 s) PW1500G start via N2/start indications and announce completion — never cycle the run switch during ENG START DELAY (manual/guide warning).
- Custom checklist packs (`checklists.json` in the Community package) parse offline with the existing schema code.

### W7. EFB accessibility (iniBuilds EFB)
Coherent-scrape the EFB views (likely own Coherent pages; PMDG-EFB/flyPad WebView2 idiom, Shift+T):
- Priority order: **Loadsheet** (SimBrief import, LOAD AIRCRAFT, ZFW/GW/CG readback — needed for FMS FUEL page), **Takeoff Performance** (SYNC FMS/SYNC WX, surface, thrust AUTO/TO/TO-1..3, flaps 2–4, CALCULATE, SEND TO FMS, results V1/VR/V2), **Landing Performance** (DRY/GOOD/MEDIUM/POOR, FLAPS 4/5, MANUAL/AUTOLAND, brake mode, REV, COMPUTE, VAPP/LD/FLD/margins), **Ground Equipment** (doors, GPU/chocks, jetway; MSFSBA GSX positioning still owns gate/docking), **Settings** (Aircraft/EFB/Audio/Callout/3rd-Party — one-time setup; expose at minimum IRS align time, TOD pause, AP/AT protection, callout toggles), **My Flight** (OFP text sections). Charts app is out of scope (visual).
- Cheap wins without scrape: `L:INI_PAUSE_AT_TOD_ENABLED`/`L:INI_PAUSE_AT_TOD_DISTANCE` are plain writable L:vars → expose directly in our UI.

### W8. Radios & transponder panel
Stock events, no scrape: COM1/2/3 active+standby set (`COM*_STBY_RADIO_SET_HZ` accepts external Hz sets and **re-broadcasts current freqs each tick for external apps** — read freqs from stock `COM ACTIVE/STANDBY FREQUENCY:n` with the "0.000 MHz" display-override rule), swaps, NAV1/2 standby set; XPDR digits + `XPNDR_SET` (documented to accept external BCD sets) + IDENT. TCAS mode = CTP territory (recon).

**⚠️ NAV-to-NAV transfer protection (pilot guide p. 42/157/286):** on the A220 the FMS autotunes the approach frequency within 31 nm and APPR handles the FMS→NAV source transfer itself; **manually setting an ILS frequency ACTIVE or changing NAV SRC before/during an approach breaks NAV-to-NAV transfer — including the automatic transfer BACK to FMS at 400 ft on a missed approach** (the cruise flow is explicit: "set frequencies but do NOT set ILS frequency active"). Our A220 `SetNavRadios` dialog must set NAV standby only (or warn before an active-set), and never auto-swap NAV frequencies as a convenience.

### W9. Flight-model integration & guidance profiles
- `GetVisualGuidanceProfile()`: measure `GlideslopeAltitudeBiasFt`/`FlareAltitudeBiasFt` (datum-vs-gear) live; A220 gear height is modest (guess ~10–15 ft bias — must measure, never copy).
- Hotkey standard set (F/W/L/Shift+G/B, speeds/altitudes) from stock simvars — free via BaseAircraftDefinition, but W3's explicit `HandleHotkeyAction` coverage list is the authority: verify each action against the A220 rather than assuming the base default reads the right var (e.g. flap detents need the L:var, not the stock percent).
- Reverse thrust: FADEC limits reverse to idle below 55 kt — no MSFSBA logic needed, but the landing-rollout announcements should not claim reverse states we can't read; `L:A22X Engine {n} Reverser` gives deploy %.
- Native "speed stability" FBW + autothrottle-moves-levers: no special handling, but flare-assist/VG interplay unchanged.

### W10. Docs, tests, release
- `docs/a220.md` (post-implementation, per-aircraft doc convention) + CLAUDE.md invariant bullets (space-L:var rule, aural-no-duplicate rule, Entry Mode rule).
- Characterization tests: generator pin tests (simvars + ECL), ECL→control coverage test, FCP walk math (alt fine/coarse), enum label tables.
- New `Resources\coherent-a220-*.js` agents each need an explicit csproj `<None Update>` entry (never wildcard-copied).
- In-sim test plan per PR (sim-facing paths can't be unit-tested).

---

## 4. Day-1 recon checklist (live aircraft required)

R1. Confirm aircraft.cfg title/ATC model strings for detection; confirm Community package name `inibuilds-aircraft-a220` + `Config\Synaptic` layout on the Marketplace FS2020 install.
R2. **Space-L:var round trip**: calc-path write `2 (>L:A22X Seat Belt Lights)` → data-def read-back `L:A22X Seat Belt Lights`. This unblocks everything in W2.
R3. Write-stickiness sweep of representative ICCP L:vars (pack, boost pump, APU switch incl. 3-s hold, taxi light enum) — downstream-effect read-back per troubleshooting playbook, never verdicts from `set_lvar`.
R4. Enumerate Coherent views at :19999 (DU1–5 instruments, EFB, any helper pages); one-socket-per-page rule applies. Identify which view owns FMS windows, EICAS/CAS, ECL.
R5. FMS input path: what do MKP hardware keys fire (H: events? bus events? L:var pulses?) — check behaviors XML if readable, else probe the instrument's EventBus from the debugger. Find the scratchpad + field-focus model. Also: is Entry Mode state readable (announce it)?
R6. ECL live state: instrument JS state shape for checklist items/sensed status; stock checklist content readable from the running instrument even if disk JSON is encrypted?
R7. CAS list + PFD approach surfaces: where the EICAS message array, the FMA strings (all four columns + armed/active/failed color state), the **ASA field** (APPR 1/2, LAND 2/3, STEEP, NO-* downgrades), and the autoland sub-mode annunciations (ALIGN/FLARE/RETARD/ROLLOUT) live in instrument state.
R8. Event probes: `HEADING_BUG_SET` with param; direct-set events for speed/alt/VS if any; `SET_AUTOBRAKE_CONTROL`; gear lever events; `AP_ATT_HOLD` behavior (FPA vs pitch-hold); **the FMS SPD ↔ MAN SPD outer-ring switch** (no documented input — probe for an L:var write, undocumented K: event, or H: event; read-back via `FG Speed Mode`).
R9. EFB view tech + DOM/state shape for Loadsheet/perf apps.
R10. Dump full L:var namespace live and diff against simvars.mdx (docs may lag the shipped build).
R11. V-speed readability: where do FMS-computed/SET-VSPEEDS V1/VR/V2 (and VREF/VAPP) live — L:var, stock `AIRSPEED` simvars, or FMS scrape only? Needed for the W4 "Rotate" callout (only V1 is a native aural).
R12. CTP minima source: is the set DA/MDA (and BARO vs RADIO minima mode) readable (L:var or scrape)? Needed so minima-related readouts and the W4 approach announcements agree with the aircraft's own PLUS-100/MINIMUMS aurals.

## 5. Phase order (post-recon)

1. **P1 Core + panels + FCP** (W1, W2, W3) — flyable with overhead flows, autoflight control, mode announcements. Pure L:var/event work, lowest risk, no Coherent dependency.
2. **P2 Monitors + radios** (W4 lamps/flight-stage, W8) — still scrape-free except CAS.
3. **P3 Coherent wave**: **PFD FMA/ASA/autoland scrape FIRST** (the W4 approach announcements + PM-callout announcer depend on it), then CAS monitor, then FMS form + guided flows (W4/W5).
4. **P4 ECL first officer** (W6).
5. **P5 EFB** (W7) + guidance profile measurement (W9).
6. **P6 Docs/tests/polish** (W10).

Each phase = its own branch + PR with an in-sim test plan; the user flies verification (solo guide doubles as the test script).
