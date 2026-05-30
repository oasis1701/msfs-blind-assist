# FlyByWire A380X MCDU — Accessible Getting-Started Guide

This guide explains how to use the A380X flight management system (FMS) through
MSFS Blind Assist, from start-up to landing. On the A380 the "MCDU" is part of
the **MFD** (Multi-Function Display) and is driven by the KCCU cursor, not the
classic A320 line-select keys. MSFS Blind Assist reads and drives it live
through the MSFS Coherent GT debugger — **no add-on install is needed**, but
**MSFS must be in Developer Mode** and the A380X must be loaded with the
displays powered up.

> Prerequisite: load the A380X profile in MSFS Blind Assist (Aircraft menu →
> FlyByWire Airbus A380X). On startup the app connects to the MFD automatically.

## 1. Opening the MCDU window

Press **Shift+M** (input mode is not required — it is a direct chord). A window
opens with three parts:

- **MCDU selector** — choose Captain or First Officer side (start with Captain).
- **Display list** — the live page contents, one item per line.
- **Scratchpad** — a text box at the bottom where you type values before sending
  them to a field.

A **status line** tells you whether the MCDU is connected. If it says "not
connected", make sure MSFS is running with Developer Mode on, the A380X is
loaded, and the displays are powered up, then press the **Refresh** button.

## 2. Reading the page

Arrow up and down through the **display list**. Each line is one thing:

- **Static text** — labels, headings, values you cannot act on.
- **Editable fields** read as **"LABEL: value"**, e.g. `FLT NBR: BVI214`,
  `FROM: EDDF`, `TO: LIMC`, `CRZ FL: 350`. The label tells you exactly what the
  field is for, so you always know what to type where. An empty mandatory field
  shows question marks; an empty optional field shows dashes.
- **Buttons / tabs** are announced with their role, e.g. `IRS, button`. A
  control that cannot be used right now reads `…, button, disabled` — activating
  it just says "Unavailable" rather than doing nothing.

The current page name is shown above the list (e.g. `ACTIVE / ACTIVE/INIT`), and
the MFD scratchpad message (if any) is read on the last line.

## 3. Typing a value into a field

The A380 has no shared scratchpad like the A320 — values go straight into the
field you choose. MSFS Blind Assist gives you a scratchpad box so the workflow is
familiar:

1. Type the value into the **scratchpad** box (the text field at the bottom).
2. Go to the **display list** and arrow to the **field** you want (its label
   tells you which one it is — `FLT NBR`, `FROM`, `CRZ FL`, …).
3. Press **Enter** on that field. The value is typed in and confirmed.

Example — set the flight number: type `BVI214` in the scratchpad, arrow to
`FLT NBR:` in the list, press Enter. The field now reads `FLT NBR: BVI214`.

Other keys while on a field:

- **Enter on a button** (with the scratchpad empty) activates it.
- **Backspace** clears the scratchpad first; once empty, it clears the selected
  field (Airbus CLR behaviour).
- **Delete** clears the selected field.
- **F5** refreshes the page.

## 4. Moving between MFD pages

Three keyboard-accessible ways to reach a page:

1. **"Go to MFD page" combo box** at the top of the window — lists all 20 pages
   grouped by tab (ACTIVE, POSITION, SEC INDEX, DATA). Choose one to jump
   straight to it. This is the most reliable way to reach pages that have no
   dedicated button (FUEL & LOAD, WIND, IRS, GNSS, TIME, STATUS, AIRPORT, …).
2. The **quick page buttons** (INIT, F-PLN, PERF, RAD NAV, SEC FPL, ATC COM,
   DIR) with their Alt+letter mnemonics, for the common pages.
3. The MFD's **own top tabs**, which appear in the page line list.

Also:

- **Ctrl+Up / Page Up** — previous page
- **Ctrl+Down / Page Down** — next page

Some pages (WIND, REPORT, GNSS, TIME, the DATA sub-pages) only open once the FMS
has the flight plan or data they need — that is the real aircraft behaviour, not
a limitation.

## 5. Preparing the FMS — the FlyByWire flow

This follows FlyByWire's own **A380X Beginner Guide → Preparing the FMS**
(linked at the end), in the same order, but described for the accessible MCDU
window. It assumes your SimBrief/OFP flight plan is already imported into the
**flyPad** and IFR clearance is obtained. Open the MCDU (Shift+M), select the
**Captain** side, then work the pages in this order:

1. **INIT page** (from the ACTIVE dropdown, or the INIT button at the bottom of
   F-PLN). Enter, one field at a time (type into the scratchpad, arrow to the
   field, Enter):
   - **FLT NBR** — flight number
   - **FROM** and **TO** — departure and destination ICAO (the city pair builds
     the flight plan)
   - **ALTN** — alternate airport
   - **CI** — cost index
   - **CRZ FL** — cruise flight level
2. **F-PLN / DEPARTURE** (DEPARTURE button). Select the **RUNWAY**, then the
   **SID**, leave **TRANS** unless required, then **TMPY F-PLN** to stage it.
3. **F-PLN / EN ROUTE.** Click the start waypoint, choose **AIRWAYS**, enter
   **VIA** (airway) then **TO** (next waypoint); repeat for each leg, then
   **TMPY F-PLN**.
4. **F-PLN / ARRIVAL.** Click the destination, select arrival **RUNWAY**,
   **APPR** (approach), **VIA** (transition) and **STAR**, then **TMPY F-PLN**
   and **INSERT TMPY** to activate the whole plan.
5. **NAVAIDS** (from INIT). Set **VOR1 IDENT** / frequency and the **LS IDENT**
   for departure if needed; the arrival ILS auto-tunes within ~250 nm.
6. **FUEL & LOAD** (from INIT). Enter **ZFW**, **ZFWCG**, **BLOCK** fuel,
   **TAXI**, **PAX NBR**, verify **CI**, **ALTN** fuel and **FINAL** reserve.
7. **T.O PERF** (from INIT). Enter **V1**, **VR**, **V2**; select **FLEX/TOGA**
   (FBW's guide uses FLEX with a temperature) or DERATED; set **FLAPS**,
   **THS FOR** (trim), **PACKS**, **ANTI ICE**, and **TRANS** altitude. V-speeds
   and FLEX come from the SimBrief Takeoff Performance calculator.

If a button does nothing and reads "disabled", the FMS isn't ready for it yet
(e.g. the company request needs a loaded flight plan first).

## 6. From start to landing — FMS flow

- **Before start:** INIT complete, F-PLN checked, FUEL & LOAD confirmed, PERF
  TAKEOFF entered, initial climb altitude set on the **FCU** (use the FCU set
  dialogs — input mode then Shift+A for heading, etc., or read with output mode
  then the FCU read keys).
- **Climb:** check the **PERF / CLB** page; the FMS manages speed and the
  vertical profile. The flight-mode annunciator changes (OP CLB, CLB, ALT…) are
  announced automatically.
- **Cruise:** confirm **CRZ FL** and cost index on the INIT/PERF pages.
- **Descent:** set the arrival runway and STAR if not done; check the **RAD NAV**
  page for the ILS frequency and course; **activate the approach phase** on the
  **PERF** page about 40 miles out.
- **Approach & landing:** the FMS manages the approach speed; the **APPR** mode
  on the FCU establishes on the localizer and glide slope. FMA mode changes
  (G/S, LAND, FLARE) are announced automatically.

## Source

The FMS flow above mirrors FlyByWire's official **A380X Beginner Guide**, which
is reviewed by an A380 type-rated pilot:

- Preparing the FMS — https://docs.flybywiresim.com/pilots-corner/a380x/a380x-beginner-guide/03_preparing-fms/
- Full beginner guide (cockpit prep → powering down) — https://docs.flybywiresim.com/pilots-corner/a380x/a380x-beginner-guide/overview/

The `Checklists/FBW_A380_Checklist.txt` cold-and-dark and shutdown flows follow
the same guide's **Cockpit Preparation** and **Powering Down** pages.

## Related

- **System Display window** (ShowStatusPage / ShowECAM hotkey) — decoded fuel,
  engine, pressurization, APU, electrical, hydraulic and air-conditioning
  readouts, including the ARINC429 quantities (per-tank fuel, gross weight, CG).
- **PFD window** (ShowPFD) — FMA modes, approach capability, PFD messages.
- **Navigation Display window** (ShowNavigationDisplay) — ND mode/range, the TO
  waypoint, cross-track error, RNP, ILS deviation.
- **Checklist** (ShowChecklist) — the A380 cold-and-dark-to-shutdown checklist,
  `Checklists/FBW_A380_Checklist.txt`.
- **Hotkey reference** — `HotkeyGuides/FBW_A380_Hotkeys.txt`.
