# Sim tablet (MSFS 2024's own EFB) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/sim-tablet.md`, which Claude Code loads when it reads matching code. Background: [sim-tablet.md](../sim-tablet.md).

## STAB-1

- Never focus a sim tablet text box or send it a mousedown (`coherent-sim-tablet-agent.js`): focus fires `FOCUS_INPUT_FIELD`, which hands the pilot's keyboard to the tablet. Type with `.value` plus an `input` event; `releaseKeyboard` hands back a focus the OS takes by itself after a press. → [sim-tablet.md](../sim-tablet.md)

Why. The tablet OS's `TextBox` (efb_os.js) calls `Coherent.trigger("FOCUS_INPUT_FIELD", …)` in its `onFocusIn`, and the sim then routes every key to the tablet: the pilot's sim keys and MSFSBA's own global hotkeys stop working until the box loses focus. Its `onmousedown` focuses the input, so a mousedown reaching a box is a focus too. None of this is needed: measured live on 2026-10-19, `input.value = x` plus `new InputEvent('input')` reaches the box's model through its own `_onInput` debounce, with no focus (a seat weight 170 → 160 moved the total mass 2079 → 2069 LBS; the route string box took a whole route). Some boxes focus themselves after an action (a box built with `focusOnClear`), so after every press and every set the reader runs `releaseKeyboard`: if a tablet input holds focus it calls `blur()` and dispatches `InputEvent('focusout')`, the box's own focus-out path, which sends `UNFOCUS_INPUT_FIELD`. It runs again on the next ticks, as the OS focuses after its own debounce. It only acts right after the reader's own action, never on a scrape, so a sighted helper typing on the tablet is left alone.

## STAB-2

- A press fires the ONE event the element listens to (`A.press`): `click()` for an onclick control, a lone mousedown for an onmousedown-only one (the Flight Planner's search results); never both, never a pointer sequence. → [sim-tablet.md](../sim-tablet.md)

Why. The OS's buttons set `onclick` to their callback (`AbstractButton.onAfterRender`), as do the tab strips, dropdown items and switches; `el.click()` runs it once. The Flight Planner's facility search results (`FacilityResultItem`) set only `onmousedown`, measured live: a `click()` on one did nothing, a mousedown selected KSEA. Firing both would run a control that listens to both twice, and the MD-11 reader's pointerdown/mousedown/pointerup/mouseup/click sequence would send a mousedown into text boxes, which focuses them (STAB-1).

## STAB-3

- Disabled is the OS's `disabled` ATTRIBUTE (on div list items too) or a shown `.disabled-layer`, read by `A.isDisabled`, never `el.disabled` alone; a press on a disabled control is refused (false), never sent. → [sim-tablet.md](../sim-tablet.md)

Why. The OS disables a component with `setAttribute("disabled", "")` (`GamepadUiComponent.disable`), on `<div>` dropdown items as well as buttons, where `el.disabled` is undefined. The Flight Planner disables ENTER ROUTE STRING while its plan is locked and LOAD FROM WEB and SEND TO AVIONICS during the cooldown after a send. The CSS shows the control's own `.disabled-layer` child while it is disabled, which is also what a captured fixture keeps. One container carries `disabled="false"` while enabled, so that value is not disabled. A refused press must report false, so the shell says "Unavailable" rather than the pilot pressing a dead item.

## STAB-4

- The SimBrief import ACTS by structure (`A.route.*`: the Folder-icon Import menu's item at `ENTER_ROUTE_STRING`, the airport steps in order, runways by designator), never by the English labels, which the OS translates; the reader may READ labels. → [sim-tablet.md](../sim-tablet.md)

Why. Every label on the tablet goes through `Utils.Translate`, so "ENTER ROUTE STRING" and "Arrival airport" are English only on an English sim. The Flight Planner's own enum fixes the Import menu's order (LOAD_FROM_WEB, LOAD_PLN_FILE, ENTER_ROUTE_STRING, REQUEST_FROM_AVIONICS), and the menu is the route header's dropdown whose selector shows the Folder icon. The departure, arrival and alternate airports are the route steps holding an airport search box, in that order, and a runway is matched on its designator ("runway 01", SimBrief's "01" and "1" are all runway 1), never on the word "runway". Reading text for the pilot is fine: it is spoken in the sim's own language.

## STAB-5

- The SimBrief import never sends the route to the avionics: Send stays the pilot's own press on the Route page, after reviewing what loaded (the owner's choice, 2026-10-19). → [sim-tablet.md](../sim-tablet.md)

Why. Sending replaces the flight plan in the aircraft's avionics. The tablet can change the plan as it loads (it picked runway 10 at KSAV where SimBrief planned 01, and picked an ILS approach once runway 01 was set), so the pilot hears the summary first and then chooses Send to avionics, Send to ATC, or both, from the Route page's Send menu.

## STAB-6

- The import's waits live in `SimTabletSimBriefImport`, one reader call per step, never a timer chain inside the page: the client re-installs the reader on any eval timeout, which would drop a chain mid-import. → [sim-tablet.md](../sim-tablet.md)

Why. Every step is synchronous in the page once triggered (open the menu item, type, SAVE, pick a runway); only the waits between them are not: the route box's model takes the text on its next tick, SAVE enables after that, and the airports load asynchronously. `CoherentPmdgEfbClient` re-runs the agent script on a live socket whenever an eval times out, replacing `A`, so a `setTimeout` chain inside the page could be cut off halfway through. Waiting in C# also makes each step testable: `SimTabletSimBriefImportTests` plays the tablet with a scripted stand-in, and the jsdom tests in `tools/sim-tablet-test` pin each `A.route.*` step.

## STAB-7

- The tablet IGNORES a runway written into the route string ("KSAV/01", measured), so `SimTabletRoute.Build` writes none; the runways are picked afterwards on the airports' runway dropdowns, and the summary says every procedure the tablet then chose by itself. → [sim-tablet.md](../sim-tablet.md)

Why. Measured live on 2026-10-19: "KFFC/31 DCT SOONE DCT NOKIE DCT KSAV/01" loaded exactly like the same route without the suffixes, with runway 10 at KSAV. Picking runway 01 on the arrival airport's dropdown worked, and the tablet then chose the approach "ILS 1 via Direct" by itself (TERBE, MIZLU, RW01, the missed approach to TILLS), which went to the avionics with the route. A pilot who never hears that would fly an approach nobody planned, so the summary names every departure, arrival and approach procedure the Route page shows.

## STAB-9

- The route box opens holding the Flight Planner's current route, and its SAVE stays DISABLED while the text is that same route (measured), so `route.enter` compares the two and closes the box as unchanged, and the import skips SAVE; never retry SAVE on an unchanged route as a failure. → [sim-tablet.md](../sim-tablet.md)

Why. Measured live on 2026-10-19 with the KFFC-KSAV plan already loaded: the box opened holding "KFFC DCT SOONE DCT NOKIE DCT KSAV", and typing the same text left SAVE with `disabled=""` for good, while a different route enabled it within 600 ms and the same route disabled it again. A pilot pressing "Load latest SimBrief plan" twice, or after loading the plan another way, would otherwise hear "The sim tablet did not take the route" for a plan that is in fact loaded. The comparison ignores case and spacing; the import goes on to the runways and adds "The Flight Planner already had this route." to the summary. When SAVE never enables for a changed route, the import closes the box (`route.closeBox`) before failing, so no popup is left over the Route page.

## STAB-10

- Never write a speed and level group (`N0112F030`) into the route box: the tablet's own parser takes the level from the SPEED digits (measured: FL112). `SimTabletRoute.Build` drops any, and puts SimBrief's SID right after the origin and its STAR right before the destination, the only places the parser takes them. → [sim-tablet.md](../sim-tablet.md)

Why. Read from atlasapp.js (`EfbRouteParserService`, EFB OS 1.0.3) and measured on 2026-10-19. `tryParseAirportWithProcedures` takes the word right after the departure airport (after an optional speed and level group) as a departure procedure when it names one of that airport's departures, and the word right before the destination as an arrival; it never sets their transitions or an approach. Its `tryParseStepClimb` matches `/^([KNM])([0-9]{3,4})([FSAM])([0-9]{3,4})$/` but reads `parseInt(n[2])`, the speed, as the level: "KFFC N0112F030 DCT SOONE DCT NOKIE DCT KSAV" set the Performance cruise box from 450 to FL112, where SimBrief planned 3,000 ft. SimBrief's ATC route starts with exactly such a group, so the import uses `<general><route>` and drops any group it finds. SimBrief keeps the SID and STAR in `<general><sid_ident>` and `<star_ident>` (`SimBriefOFP.SidIdent`, `StarIdent`), which may be absent from the route itself, so the builder adds each where the parser looks unless the route already starts or ends with it. A jet plan through the box live is still to be measured.

## STAB-8

- Shift+T opens the sim tablet only for an aircraft whose `UsesSimTablet` is true (default false), as the LAST branch of the tablet hotkey, so an aircraft with a tablet of its own never reaches it; on MSFS 2020 it says the tablet is MSFS 2024 only. → [sim-tablet.md](../sim-tablet.md)

Why. The PMDG, Fenix, FBW, MD-11 and iFly aircraft have tablets of their own, which are better than the sim's for them, so the sim tablet is opt-in per aircraft, the owner's decision of 2026-10-19: an aircraft with no tablet of its own (the default MSFS 2024 aircraft) overrides `UsesSimTablet`. The branch comes after every aircraft-specific one in `MainForm.Hotkeys.cs` so that an aircraft that also set the flag still opens its own. MSFS 2020 has no "Electronic Flight Bag" view, and a window opened there would sit unconnected forever.
