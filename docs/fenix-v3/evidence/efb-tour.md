# Fenix v3 EFB tour, screen-reader view (2026-10-21)

Fenix 3.0.0.7426, A320-214 CFM loaded, sim running. Page `http://127.0.0.1:8083/?target=external#/efb` in the
desktop app's built-in browser (Chromium, the engine our `FenixEFBForm` WebView2 uses; our window already gets the
`external` target, plan 03 decision 1). Read only: apps were opened and closed, no switch, button or entry that
changes the aircraft or the EFB's data was pressed (Panel States and quick settings were closed unused).

Method: the browser's accessibility tree (what a screen reader receives) plus a page script listing every clickable
element with its role, whether Tab reaches it ("focusable") and its computed name. NVDA itself was not run: in browse
mode NVDA can usually still activate a clickable element that Tab cannot reach (announced as "clickable"), but an
element with no text gives it nothing to land on.

## Per app

| App (route) | What works | What a screen-reader pilot meets |
|---|---|---|
| Home (`#/efb`) | Status line reads (date, local/UTC time, "A320-214 CFM"); "Open quick settings" is a real button | The 9 app tiles (Fenix, Takeoff, Landing, Charts, Documents, Failures, Settings, MCDU, FAP) are plain text, not buttons, not on the Tab order. The home button (bottom left, `#homeButtonFree`) has no name and no Tab stop: once in an app, a keyboard or screen-reader user has no way back. The top-bar arrow opens an app switcher whose 9 icons are pictures without names. Status icons unnamed |
| Quick settings | "Open settings" button | Brightness slider unnamed; airplane-mode button (`airplane.png`) unnamed; Escape does not close the pop-up |
| Fenix > My Flights (`#/efb/fenix/my-flight`) | Bottom tabs My Flights / Ground Services / Mass and Balance / Panel States are real links with "current page" | The three cards "Tap to import from SimBrief" (Flight, Route, Weather) are not on the Tab order; two unnamed buttons in the bottom bar |
| Fenix > Ground Services | "Toggle Jetway", "Toggle NWS Pin" buttons | All 14 switches (GPU, PCA, Engine Fan Heating, Chocks & Cones, Fwd/Aft Stairs, doors 1L/1R/2L/2R, Fwd/Aft cargo, Bulk, Connect Tug) read **"switch, on"** with their name as separate text after them (Angular Material slide toggles whose label text sits outside the toggle). Pushback Start/Stop not on the Tab order; the three tug steering arrows have no names |
| Fenix > Mass and Balance | Live ZFW/MACZFW/GW/MACGW figures and capacities read as text | Entry boxes named only by their limit ("Max 180", "Max 19,460", "Max 41,897", "Max 134,482") instead of passengers / cargo / fuel / zero fuel weight; button names run together ("ResetAll", "LoadAircraft", "SEND TOMCDU"); the GW column header reads only "(LBS)"; layout tables |
| Fenix > Panel States | The four state buttons read (Cold & Dark, Turnaround with APU, Turnaround with PCA, Ready) | The pop-up has no title; loading progress (`loading-notification`, progress bar) is never spoken |
| Takeoff (`#/efb/depperf`) | Section texts and buttons (Calculate, Send to MCDU, Reset, Sync Loadsheet, Sync Live WX, View intersection data) | Every entry box is named by its placeholder ("----", "---/--", "--") instead of ICAO code / TOW / MACTOW / wind / temperature / altimeter; the six drop-downs (runway, surface, flap config, Force TOGA, anti-ice, packs) have no names; the result panel is an MCDU-style grid of fragments read in layout order. Results not judged: no calculation was run |
| Landing (`#/efb/arrperf`) | Same buttons | Same unnamed boxes and runway list; runway condition codes 6 Dry … 1 Poor show the selected one by colour only; the five option boxes (Low A/Brake, Idle/No Rev, Manual Land, Flap Full, A/THR On) are focusable but not buttons; the 9 failure categories (Electrical … Engine) are not on the Tab order |
| Charts (`#/efb/charts`) | Navigraph / Reset buttons | A Navigraph moving map: pictures only (out of reach by nature). Sidebar (Add Departure, Route, Add Destination, Add Alternate, Sync SimBrief) not on the Tab order; zoom and map buttons unnamed |
| Documents (`#/efb/documents`) | Search box labelled | Folders and files not on the Tab order (plan 07 builds its own window) |
| Failures (`#/efb/manual-failures`) | Search, Failed/Armed/Reload buttons; failure buttons are real buttons | ATA chapters not on the Tab order and do not say open/closed; failure buttons lose their group ("Pack 1" twice, under Overheat and under Regulator Fault; "1", "2" under CPC); armed/failed state by colour (plan 10 builds its own window) |
| Settings (`#/efb/settings`) | Each option is a heading plus description; search labelled; Restart/Sleep EFB, Expanded/Compact view buttons | Every on/off option is a checkbox named **"on"**; the picture choices (GNADIRS/ADIRS, Classic/Modern, hPa/inHg …) never say which is selected (`option--active` class only); every "⋮" menu button is unnamed; the category list (Aircraft … Realism) is not on the Tab order (plan 11 builds its own window) |
| MCDU tile (`#/mcdu`) | The Left/Right choice reads | Leaves the EFB: the web MCDU is two canvases, no text and no controls for a screen reader, and no way back to the EFB. Our Fenix MCDU window already covers the MCDU |
| FAP tile (`#/fap/screen-saver`) | — | Leaves the EFB for the FAP's screensaver (a livery picture): nothing readable, no way back. Plan 08's Cabin window covers the FAP |

Messages: the EFB has no spoken notifications. Its only live-region code is Angular's own (vendor bundle); app
notices (`loading-notification`, `fap-notification`, `no-data-notification`) are plain page text.

## Existing pattern in the app

`MSFSBlindAssist/Forms/IFly737/IFlyEfbForm.cs` injects an accessibility script into the iFly EFB (WebView2): clickable
DIVs become buttons with Tab stops and Enter/Space, Alt+Home goes home, choice rows become radio groups with
`aria-checked` synced from the page's own "selected" class by a MutationObserver, inputs get labels from their row.
Injected scripts are tested offline with jsdom against trimmed live DOM captures (`tools/flypad-settings-test`,
`tools/md11-efb-test`: `npm install`, `node --test`).
