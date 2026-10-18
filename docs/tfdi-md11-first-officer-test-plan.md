# TFDi MD-11 First Officer — in-sim test plan

Sim-facing behaviour cannot be unit-tested, so this plan is how the MD-11 First Officer gets
verified. The switch logic was decoded from TFDi's own code, and most of the ground flows were
driven live on 26 September 2026 (MD-11F GE at CYYZ, MSFS 2024), each step reading back as done.
Nothing airborne has run yet, and nobody has yet listened to it the way a pilot will. That is
what this pass is for.

Build: 28 September 2026 or later (the file version ends in `c4c09aae` or a later commit).

## How to use this plan

- Two sessions. Session 1 stays on the ground, about an hour. Session 2 is a short flight.
- Run the flows from the Flows tab, in order. Each section says what to check.
- Marks used below:
  - **Settle**: never confirmed live.
  - **Recheck**: worked on 26 September, but something it depends on has changed since.
  - **Listen**: only your ears can judge it.
- What the log records: every flow step's result, and every switch the First Officer moves, with
  the reason for any refusal (`debug.log`). What it does not record: anything spoken. So for
  anything that sounds wrong, doubled, cut off or silent, note the clock time and the words.

## Before you start

1. SimBrief: generate an MD-11 plan with takeoff performance, so it carries a takeoff flap
   between 10 and 25 degrees. Your SimBrief username must be in Settings.
2. Start cold and dark at a gate with ground power available (EFB Services page).
3. Start MSFSBlindAssist fresh for the session.
4. Open File, then "TFDi MD-11 First Officer".

## Session 1 — on the ground

### The window

1. **Settle:** the File → First Officer item is read as "TFDi MD-11 First Officer" while the
   MD-11 is the selected aircraft, and Shift+C (output mode) opens the same window.
2. **Listen:** SimBrief loads by itself when the window opens. You should hear ONE announcement,
   in full: "SimBrief flight plan loaded.", the transition altitude and level, "Takeoff flaps"
   and the pressurization plan. Until 2026-10-18 each part interrupted the one before it, so only
   the last was heard. (This part is shared by every aircraft's First Officer.)
3. At the end of a session: switching aircraft closes the window.

### Power Up

Worked on 26 September. Fuel switches off, parking brake, battery, external power (without the
GPU you get a Captain reminder instead), weather radar off, emergency power armed, IRS 1, 2 and
auxiliary to NAV, nav lights, and the IRS initialize reminder.

1. **Listen:** nothing is spoken twice. A switch the First Officer moves is announced by the flow
   step only, never again by the MD-11's own lamp announcements.

### Preflight

Worked on 26 September, except the Dial-A-Flap (no SimBrief plan that day). About five minutes,
ending with a wait of about 100 seconds for the hydraulic test.

1. **Listen:** during the annunciator light test (about 488 lamps), nothing is read lamp by lamp,
   during or after.
2. **Listen:** you hear the GPWS voice test. The cover stays closed and the switch ends at NORMAL.
3. **Listen:** fire test bells, the voice recorder test, both oxygen tests, the TCAS test and the
   cargo door test. Note any you expected to hear and did not.
4. **Settle:** "Master warning: reset" comes once, right after the cargo fire test. Afterwards no
   warning is sounding. Then, on the Checklists tab, tick "Engine and APU fire test", "Cargo fire
   manual test" and "Master warning: reset" in that order: no warning is left sounding.
5. **Settle:** "Dial-A-Flap: takeoff setting" is done, not "Skipping", and the Dial-A-Flap reads
   your SimBrief takeoff flap (the Ctrl+P window shows it). Without a plan it says Skipping and
   gives the Captain reminder instead.

### Before Start

Worked on 26 September. APU start (waits up to three minutes), seat belts on, external power off
only once APU power is on, auxiliary hydraulic pump 1, ignition A, APU bleed on, beacon, Captain
reminders.

### Engine Start

Worked on 26 September: engines 3, 1 and 2, about three and a half minutes.

1. **Recheck:** the first step, "Engine data", passes. It waits for engine 3's N2, whose request
   number changed on 28 September. A failure sounds like "Engine Start flow stopped. Unable to
   complete: Engine data".
2. **Listen:** each start switch is pulled once. A second click would abort that start.

### After Start

1. **Recheck:** "Flap handle: Dial-A-Flap detent" and "Spoilers: ARM" are done on the first try,
   not "Skipping". Both failed on 26 September until a fix, and that fix has since been replaced
   by main's version of it.
2. **Settle:** about a minute and a half after "APU bleed: OFF", the APU shuts itself down.
3. Autobrake T.O., the system display config page, nose light taxi, Captain reminders.

### Before Takeoff

Worked on 26 September. Landing lights on, strobes, transponder TA/RA with altitude reporting,
spoilers and autobrake checked, Captain reminders. Run it at lineup.

1. **Settle:** it ends with "NAV: ARM", "PROF: ARM" and "Auto flight: ON", all done. The
   autothrottle then reads on (autopilot window), and the read-back lines "NAV: Armed", "PROF:
   Armed" and "Auto Flight: On" are ticked.
2. **Listen:** "Autothrottle: on" may be spoken after "Auto flight: ON". Note whether it is.
3. **Settle:** run Before Takeoff again: "Already set: Auto flight: ON", and NAV and PROF stay
   armed.

### After Landing, on the ground

Not run yet. Without a flight the spoilers are armed rather than deployed, which still checks
most of it.

1. **Settle:** "Spoilers: DOWN" disarms them with one click.
2. **Settle:** "Flap handle: UP" brings the handle up from the Dial-A-Flap detent.
3. Landing lights retracted, strobes off, weather radar off, APU start.

### Parking

Not run yet. The flow waits for ground speed below 1 knot, so be stopped.

1. **Settle:** parking brake set. It is a toggle, so it must never release a brake already set.
2. **Settle:** fuel switches off, and the engines shut down. It waits for them to spool down
   before ignition off.
3. Seat belts off, then beacon, landing, nose, strobe, logo and turnoff lights off. Nav lights
   stay on.

### Shutdown

Not fully run yet.

1. **Settle:** packs off go through Air MANUAL first (they are inert in Air AUTO).
2. **Settle:** APU off, then a wait of up to two and a half minutes for it to stop, then battery
   off.
3. Emergency lights and power off, windshield heat off, IRS off, cargo temperatures off, dome
   off, EVAC off.
4. **Settle:** run Shutdown once with ground power on the aircraft (GPU connected on the EFB
   Services page, External Power switched on). After the APU has stopped you hear "External
   power: OFF", then "Battery: OFF", and the aircraft goes dark. TFDi does drop ground power with
   only the battery left (measured 13 October), so "Skipping: External power: OFF" means the press
   did not take: note the time. Without ground power the step says "Already set: External power: OFF".
5. **Listen:** in that run, note whether "APU: OFF" says Skipping (with ground power on, the APU
   is no longer powering the aircraft) and whether the APU still stops within the wait.

### Guards, re-runs and the Flows tab

1. **Recheck:** Power Up or Shutdown started with an engine running stops at once, with "Unable
   to complete: Engines stopped". (Worked on 26 September.)
2. **Listen:** re-run a finished flow. Every switch step says "Already set" and nothing moves.
3. **Listen:** pause during a wait, then resume. You hear "paused" and "resumed", and the wait
   does not time out while paused.
4. **Listen:** "flow complete" never cuts off the last step's words, and Start Flow works again
   straight after a flow ends.

### Checklists tab

1. **Listen:** section headers read as plain items. Lines you can tick read as check boxes,
   checked or not checked.
2. **Settle:** TFDi's read-back lines tick themselves once their switch is in position. Each list
   follows its TFDi page: the After Start Checklist no longer repeats spoilers and autobrake, and
   starts with "Engine Anti-Ice: As Required".
3. **Settle:** ticking an action line moves the switch. If a line ever says "Unable to complete",
   note which one.
4. **Settle:** after Parking and Shutdown, the engine start lines stay ticked.
5. Run Related Flow from a section starts that section's flow.
6. Between After Takeoff and Before Landing there is only the Descent section: the 10,000 feet,
   transition and cruise lists are gone, because the automatic modes in session 2 cover them.

## Session 2 — a short flight

Climb above both 10,000 feet and the transition altitude (18,000 feet in North America).

Settings, on the First Officer tab of File, Settings:

1. "Auto-raise gear on positive rate": **off**, so After Takeoff's own gear step is tested.
2. "Auto-lower gear at 2000 ft AGL": **on**, so the stock gear-down event is tested.
3. "Auto-engage autopilot on climbout": **on**. The MD-11 never engages below 400 feet.
4. "Automatically switch landing lights at 10,000 feet": on.
5. "Auto seat belt signs": At 10,000 feet.

### After Takeoff

1. **Settle:** "Gear: UP" raises the gear lever once you are definitely airborne.
2. **Settle:** "Spoilers: DISARM" and "Autobrake: OFF".

### Autopilot

1. **Settle:** climbing through 400 feet you hear "400 feet. Autopilot engaged" only once AUTO
   FLIGHT has actually engaged. If it never takes: "Autopilot did not engage. Captain action
   required".

### Climb

1. **Settle:** above 10,000 feet: "Above ten thousand. Landing lights off."
2. **Settle:** the seat belt signs follow your setting.
3. **Settle:** at the transition altitude you hear one sentence: "Transition altitude. Altimeters
   standard, Dial-A-Flap 15." If the flap handle is still in the Dial-A-Flap detent, it asks you
   to set the Dial-A-Flap instead. Check that the altimeters read standard.

### Descent

1. **Settle:** through the transition level: "Transition level. Set local altimeter pressure
   now." It only speaks; it never sets the altimeters.
2. **Settle:** below 10,000 feet: "Below ten thousand. Landing lights on."
3. The Descent flow: landing lights, then reminders for QNH, the landing autobrake and the
   sterile cockpit chime.

### Before Landing

Let the auto-lower fire first, then run Before Landing. Always run it: it is the backstop if the
gear did not come down.

1. **Settle:** after "Gear down.", Before Landing says "Already set: Gear: DOWN". If it lowers
   the gear itself instead, the stock gear-down event did not reach the MD-11.
2. **Settle:** "Spoilers: ARM".
3. **Measure, new 2026-10-18 (FO-13):** the flow's last step is the read-only gear check, the
   lever AND the stock legs: "Already set: Gear: DOWN" with the gear down and locked, or up to 20 s
   of "Waiting for: Gear: DOWN". Before relying on it, read `A:GEAR CENTER POSITION`,
   `A:GEAR LEFT POSITION` and `A:GEAR RIGHT POSITION` (percent) on the MD-11 once with the gear up
   and once down: they must read about 0 and about 100. If they do not move, the check cannot
   confirm the MD-11's gear and the legs are the wrong source.

### Landing and After Landing

1. **Settle:** with the spoilers deployed after touchdown, "Spoilers: DOWN" stows them with one
   click. The lever runs forward by itself, and nothing announces "armed" on the way.
2. **Settle:** "Flap handle: UP", then lights, strobes, weather radar, APU start.
3. Taxi in, then Parking and Shutdown as in session 1.

### Captain items

The First Officer must never move the flap handle in flight, run an auto-flap schedule, set the
landing autobrake, or touch thrust, trims, reversers or the FMS. If you hear it do any of these,
stop and report it.

## Review fixes, 18 October 2026

What the PR #160 review changed on the MD-11. Each is **Settle**: none has run in the sim.

1. **Read-back lines are delivered or left open.** Every read-back line a flow latches now has a
   step behind it: a write completes its read-back line too, and new read-only checks ("Already
   set: …", or a 5 s "Waiting for: …" then "Skipping: …") cover the lines no write sets. Listen for
   the new checks: Power Up opens with the flap handle, gear handle, fuel dump, manifold drain and
   emergency power selector (OFF, before the flow arms it); Preflight opens with battery, IRS NAV,
   ignition off and start switches in, and checks EVAC and GPWS "armed/normal and guarded" after
   their steps; Before Start checks "IRS: aligned"; Engine Start ends with "Engine start switches:
   IN"; Before Takeoff checks the takeoff flaps and wing anti-ice; Parking checks "Exterior
   lights: all off except nav". Then make one fail on purpose: open the EVAC guard by hand and
   run Preflight. You must hear "Skipping: Evacuation switch: armed and guarded", and on the
   Checklists tab "EVAC Switch: Armed and Guarded" must stay unticked after the flow completes.
2. **After Takeoff waits for the flaps.** After gear up, spoilers and autobrake, the flow says
   "Waiting for: Flaps and slats: up and retracted" and waits up to five minutes for the handle at
   UP, then confirms the gear up (lever and legs) as its last step. "Flaps and Slats: Up and
   Retracted" must not tick before you retract the flaps.
3. **Engine start never fuels an engine without a starter.** Abort a start (push the START switch
   in) while N2 is between 15 and 50 %, then run Engine Start again before N2 drops below 15 %: the
   flow must stop at "Engine n start switch: PULL" and never put that fuel switch ON. Run it again
   below 15 % and it starts normally.
4. **Listen:** After Landing's flap step (FULL to UP) speaks the step, not every detent the handle
   passes: the flap read-out stays quiet while the First Officer walks the handle.
5. **Gear legs** are measured in Before Landing (item 3 there) and checked in After Takeoff.

## Reporting a problem

For each one, note the clock time, the flow and step, the words you heard, and what you expected.
The logs are in `%APPDATA%\MSFSBlindAssist\logs`. Report back after each session, while they
still cover it.

## Results

Filled in after each session.
