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

## 5. Loading the flight plan (already imported in the flyPad)

This guide assumes you have already imported your SimBrief/OFP flight plan into
the **flyPad** before the flight. To bring it into the FMS:

1. Open the MCDU (Shift+M) and select the **Captain** side.
2. Go to the **ACTIVE / INIT** page.
3. Enter the **FLT NBR**, then **FROM** and **TO** airports (4-letter ICAO).
   Entering the city pair creates the active flight plan.
4. Use the **CPNY F-PLN REQUEST** / company-route button (or the AOC/init
   request) to pull the imported company flight plan when it is available.
5. Go to the **F-PLN** page and check the route. Clear any **discontinuities**.
6. Set the **departure runway and SID**, then the **arrival runway and STAR**.
7. On the **FUEL & LOAD** page, confirm block fuel and zero-fuel weight.
8. On the **PERF** page, complete the **TAKEOFF** sub-page: enter **V1, VR, V2**
   and the takeoff flap setting.

If a button does nothing and reads "disabled", the FMS isn't ready for it yet
(for example, the company request needs a loaded flight plan first).

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
