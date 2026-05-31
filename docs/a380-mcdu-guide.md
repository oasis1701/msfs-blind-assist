# FlyByWire A380X MCDU — Accessible Getting-Started Guide

This guide explains how to use the A380X flight management system (FMS) through
MSFS Blind Assist, from start-up to landing. On the A380 the "MCDU" is part of
the **MFD** (Multi-Function Display) and is driven by the KCCU cursor, not the
classic A320 line-select keys. MSFS Blind Assist reads and drives it live
through the MSFS Coherent GT debugger — **no add-on install and no Developer
Mode are needed**. You just need the A380X loaded with the cockpit displays
powered up (the debugger port is opened by the sim itself).

> Prerequisite: load the A380X profile in MSFS Blind Assist (Aircraft menu →
> FlyByWire Airbus A380X). On startup the app connects to the MFD and flyPad
> automatically. If the cockpit is cold and dark, power up first (batteries +
> external/APU power) so the displays come alive.

## 1. Opening the MCDU window

In **input mode** press **Shift+M**. A window opens with these parts:

- **MCDU selector** — choose Captain or First Officer side (start with Captain).
- **"Go to MFD page" combo** — jump to any of the 20 MFD pages (see section 4).
- **Display list** — the live page contents, one item per line.
- **Scratchpad** — a text box at the bottom where you type values before sending
  them to a field.

A **status line** tells you whether the MCDU is connected. If it says "not
connected", make sure MSFS is running, the A380X is loaded, and the displays are
powered up, then press the **Refresh** button.

## 2. Reading the page

Arrow up and down through the **display list**. Each line is one thing:

- **Static text** — labels, headings, values you cannot act on.
- **Editable fields** read as **"LABEL: value"**, e.g. `FLT NBR: BVI214`,
  `FROM: EDDF`, `TO: LIMC`, `CRZ FL: 350`. The label tells you exactly what the
  field is for, so you always know what to type where. An empty **mandatory**
  field reads **"(empty)"** (the amber entry boxes on screen); an empty
  **optional** field shows dashes (e.g. `CI: ---`).
- **Combobox fields** read as **"LABEL: value (combobox)"**, e.g.
  `MODE: ECON (combobox)` or `CRZ: ECON (combobox)`. These are **dropdowns,
  not free-text** — you pick from a short fixed list rather than typing. (On
  screen they have a little down-arrow.) The `(combobox)` tag is just the
  control type, not part of the value. To change one: arrow to it and press
  **Enter** — the options appear immediately below as their own selectable lines
  (e.g. `LRC`, then `ECON`); arrow to the one you want and press **Enter** to
  select it. The list then closes and the field shows your choice.
- **Buttons / tabs** are announced with their role, e.g. `IRS, button`. A
  control that cannot be used right now reads `…, button, dimmed` — activating it
  just says "Unavailable" rather than doing nothing.

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

## 4a. Reading the flight plan (F-PLN) — what the lines mean

The F-PLN page lists the route as **one line per waypoint, in order**. MSFSBA
folds each waypoint together with the *leg that leads to it* and any altitude
restriction, so a line reads like:

> **DER22, via BASU1D, 2 NM, track 217°**

Breaking that down:

- **DER22** — the waypoint name (a navaid, intersection, or runway like `VCBI22`).
- **via BASU1D** — the airway or procedure you fly to reach it (here the BASU1D
  departure). It is spoken **once** and then stays silent until it changes (so a
  six-leg SID doesn't repeat "BASU1D" six times); when it changes you'll hear the
  new one, e.g. **via P570**.
- **2 NM** — the leg distance: how far this waypoint is from the previous one, in
  nautical miles. (So "BASU1D, 2 NM" then later "BASU1D, 6 NM" are simply the
  first and second legs of the SID — 2 nm to the first point, 6 nm to the next.)
- **track 217°** — the magnetic track flown along that leg.

Some lines add a **constraint**, e.g. *"500, via C220°, 1 NM, at or above 500
feet"* or *"BI613, 5 NM, track 294°, at or below 5000 feet"* — an altitude you
must cross at/above/below at that point. A line whose name is just a number (like
**500**) is a computed point where the procedure levels or turns at that altitude,
not a real navaid.

Empty fields are dropped: before takeoff the time/speed/altitude **predictions**
are blank in the aircraft (shown as dashes), so MSFSBA simply omits them; once
airborne an **ETA hh:mm** is added to each line.

**Seeing the WHOLE route.** The page shows a *window* of about a dozen waypoints
at a time, with the **destination pinned at the bottom** (the DEST / arrival
airport). The last waypoint you hear before DEST is just the bottom of the
*current window*, NOT the end of the route — press **Ctrl+Down / Page Down** to
scroll to the next set of waypoints (and **Ctrl+Up / Page Up** to go back). Keep
paging down to walk the entire route to the destination.

## 5. Loading the flight plan — SimBrief first

**The fast way is to import your SimBrief OFP — but importing it into the flyPad
is NOT enough on its own.** You finish the import with **INIT REQUEST on the MCDU
INIT page**, which is the step that actually pulls the route into the FMS. After
that you only review the data and fill the performance numbers — no hand-typing
the route.

> Important: do **not** set a destination on the MSFS World Map before the flight.
> If you do, the aircraft imports that MSFS plan instead and the **INIT REQUEST**
> option will not appear.

### 5a. Import from SimBrief (recommended)

1. Generate your **SimBrief OFP with "Detailed Navlog" enabled** (a checkbox when
   you create the dispatch) — the FMS import needs it.
2. Open the **flyPad EFB** (input mode → **Shift+T**) and, the first time, set
   your **SimBrief username / Navigraph account** in flyPad **Settings →
   SimBrief**. It opens as a web page your screen reader browses normally.
3. *(Optional)* On the flyPad **Dispatch / OFP** page, **Import** your latest OFP
   so the flyPad shows the route, fuel and weights for reference.
4. Open the **MCDU** (Shift+M), go to the **INIT** page (Alt+I, or the ACTIVE
   dropdown), and activate **INIT REQUEST** (the field that fetches the SimBrief
   route). **This is the step that loads the route into the FMS** — city pair,
   cruise FL, and the SID/STAR/airways from your plan.
5. Arrow through **F-PLN** to confirm the route reads as expected. If you see
   "NOT IN DATABASE" / "AWY/WPT MISMATCH", your SimBrief AIRAC is older than the
   sim's nav data — regenerate the OFP on the matching cycle.

After INIT REQUEST you still **review and complete** three things in the FMS:

- **INIT** — confirm FROM/TO, ALTN, CI and CRZ FL; type any that are blank.
- **FUEL & LOAD** — confirm ZFW, ZFWCG, BLOCK fuel, TAXI, PAX, ALTN and FINAL
  reserve against the SimBrief figures.
- **T.O PERF** — enter V1/VR/V2, FLEX temp (or TOGA), FLAPS, THS trim, PACKS,
  ANTI ICE and TRANS altitude. The V-speeds and FLEX come from the SimBrief
  *Takeoff Performance* calculator (or the in-sim PERF calc).

That's the whole FMS prep when you fly with SimBrief — flyPad SimBrief setup,
then **INIT REQUEST** on the MCDU, then review.

### 5b. Without SimBrief — build the plan by hand

If you are not using SimBrief, enter the plan manually in this order (this
mirrors FlyByWire's **A380X Beginner Guide → Preparing the FMS**). Open the MCDU,
select the **Captain** side, then work the pages:

1. **INIT page** (ACTIVE dropdown, or the INIT button on F-PLN). Enter, one field
   at a time: **FLT NBR**, **FROM** and **TO** (the city pair seeds the plan),
   **ALTN**, **CI**, **CRZ FL**.
2. **F-PLN / DEPARTURE.** Select **RUNWAY**, then **SID**, leave **TRANS** unless
   required, then **TMPY F-PLN** to stage it.
3. **F-PLN / EN ROUTE.** Select the start waypoint, choose **AIRWAYS**, enter
   **VIA** (airway) then **TO** (next waypoint); repeat per leg, then **TMPY
   F-PLN**.
4. **F-PLN / ARRIVAL.** Select the destination, choose arrival **RUNWAY**,
   **APPR**, **VIA** (transition) and **STAR**, then **TMPY F-PLN** and **INSERT
   TMPY** to activate the whole plan.
5. **NAVAIDS** (from INIT) — set **VOR1 IDENT**/frequency and the departure
   **LS IDENT** if needed; the arrival ILS auto-tunes within ~250 nm.
6. **FUEL & LOAD** and **T.O PERF** — same as the review steps in 5a above.

If a button does nothing and reads "dimmed", the FMS isn't ready for it yet
(e.g. a company request needs a loaded flight plan first).

## 6. From start to landing — FMS flow

- **Before start:** flight plan loaded (5a or 5b), F-PLN checked, FUEL & LOAD
  confirmed, T.O PERF entered, initial climb altitude set on the **FCU**. Set the
  FCU with input mode then **Ctrl+A** (altitude), **Ctrl+H** (heading), **Ctrl+S**
  (speed), **Ctrl+V** (V/S); read any back in output mode with **Shift+A / H / S
  / V**. The AP/ATHR/LOC/APPR/EXPED controls are **stateful combos** in the FCU
  panel — they read their live state (On/Off, Armed/Active) and you pick to
  toggle.
- **Climb:** check the **PERF / CLB** page; the FMS manages speed and the
  vertical profile. Flight-mode annunciator changes (OP CLB, CLB, ALT…) are
  announced automatically.
- **Cruise:** confirm **CRZ FL** and cost index on the INIT/PERF pages.
- **Descent:** set the arrival runway and STAR if not done; check the **RAD NAV**
  page for the ILS frequency and course; **activate the approach phase** on the
  **PERF** page about 40 miles out.
- **Approach & landing:** the FMS manages the approach speed; **APPR** mode on
  the FCU establishes on the localizer and glide slope. FMA mode changes (G/S,
  LAND, FLARE) are announced automatically.

## Source

The FMS flow above mirrors FlyByWire's official **A380X Beginner Guide**, which
is reviewed by an A380 type-rated pilot:

- Preparing the FMS — https://docs.flybywiresim.com/pilots-corner/a380x/a380x-beginner-guide/03_preparing-fms/
- SimBrief / flyPad import — https://docs.flybywiresim.com/pilots-corner/a380x/a380x-beginner-guide/
- Full beginner guide (cockpit prep → powering down) — https://docs.flybywiresim.com/pilots-corner/a380x/a380x-beginner-guide/overview/

The `Checklists/FBW_A380_Checklist.txt` cold-and-dark and shutdown flows follow
the same guide's **Cockpit Preparation** and **Powering Down** pages.

## Related

- **flyPad EFB window** (input mode → Shift+T) — SimBrief/OFP import, ground
  services, performance, and settings, shown as an accessible web page.
- **System Display window** (ShowStatusPage / ShowECAM hotkey) — decoded fuel,
  engine, pressurization, APU, electrical, hydraulic and air-conditioning
  readouts, including the ARINC429 quantities (per-tank fuel, gross weight, CG).
- **PFD window** (ShowPFD) — FMA modes, approach capability, PFD messages.
- **Navigation Display window** (ShowNavigationDisplay) — ND mode/range, the TO
  waypoint, cross-track error, RNP, ILS deviation.
- **Checklist** (ShowChecklist) — the A380 cold-and-dark-to-shutdown checklist,
  `Checklists/FBW_A380_Checklist.txt`.
- **Hotkey reference** — `HotkeyGuides/FBW_A380_Hotkeys.txt`.
