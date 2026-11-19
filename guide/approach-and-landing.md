---
title: "Approach and Landing"
---

<div class="guide-part" markdown="1">

Chapter 2 of 2 in [Part 6: Flying](index.md#part-5-flying) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Choosing a Destination Runway](#destination-runway)
- [Approach Readouts](#land-readouts)
- [Visual Guidance](#visual-guidance)
- [Flare and Rollout Tones](#flare-assist)
- [The Landing Exit Planner](#landing-exit)
- [Everything the Rollout Says](#land-rollout-callouts)
- [Landing Rate and G-Force](#land-rate)

</nav>

The last few minutes of a flight are the busiest. You need to know where the runway is, whether you are lined up with it, whether you are on the right slope down to it, and, once you are on the ground, where to turn off. BA gives you tools for every one of those questions, and almost all of them start with choosing a destination runway. Most need the [navigation database](getting-started.md#nav-database), because BA has to know where the runway and its taxiways are.

## Choosing a Destination Runway {#destination-runway}

### What it is {#land-dest-what}

The destination runway is the runway you tell BA you are going to land on. BA does not read it from your flight plan or your aircraft's FMS; you choose it yourself, in a small window. Once it is set, BA can measure your distance to it, give you an ILS report for it, fly Visual Guidance to it, and arm the landing tones for it.

### When to use it {#land-dest-when}

Set it as soon as you know your landing runway: at the top of descent, after listening to the ATIS, or when ATC tells you which runway to expect. You can set it again at any time if the runway changes; the newest choice always wins.

### How to use it {#land-dest-how}

1. Press Input Shift+D. The **Select Destination Runway** window opens with the cursor in the **Airport ICAO Code** box.
2. Type the four-letter ICAO code of the airport, for example EGLL. As soon as you type the fourth letter, BA loads that airport's runways. The status line says how many it found, for example *"Found 4 runways for London Heathrow"*. If the code is wrong, it says *"Airport EGXX not found in database"*.
3. Press Enter. The cursor moves to the **Available Runways** list and the first runway is selected.
4. Arrow up and down through the list. Each runway reads with its length, width and surface, then its localizer if it has one, for example: "Runway 27L - 3660 m long, 52 m wide - Asphalt - ILS CAT III 109.50, course 268". The course is the final approach course from the airport's approach procedure, the one on the chart. For a localizer that no procedure in your database uses, BA works it out from the localizer's heading instead, and it can be a degree or two off the chart. "ILS" means a localizer and a glideslope. "Localizer", as in "Localizer 108.70, course 152", means a localizer only: you get left and right guidance but no glideslope to capture. A glideslope that is not the usual 3 degrees is said, for example London City 27's "ILS 111.15, course 272, glideslope 5.5 degrees". So are CAT II and CAT III, for example "ILS CAT III 110.30"; CAT I, the usual kind, is not. An offset approach, whose course does not line up with the runway, is named for what it is: "LDA", as in KDCA 19's "LDA 111.35, course 147", where the runway itself is 186, or "IGS". You follow its course to a point near the runway, then turn to line up and land visually. An LDA that has a glideslope adds "with glideslope", and an IGS without one adds "no glideslope". With an MSFS 2020 database BA cannot tell these apart, so they read as ILS or Localizer. Distances follow your distance unit setting (see the Settings section below).
5. If you want to hand-fly the flare with tones, Tab to the **Manual landing assist (adds flare tones)** checkbox and press Space to tick it. Leave it unticked for an approach you will fly on the autopilot or the ILS. The next section explains what it does.
6. Press Enter (or Tab to **Select** and press Space). The window closes, focus goes back to the simulator, and BA confirms:
   - *"Destination runway set: EGLL Runway 27L"*
   - or, with the checkbox ticked, *"Destination runway set: EGLL Runway 27L. Manual landing assist armed."*

To close the window without changing anything, press Escape or choose **Cancel**.

<div class="callout tip" role="note" markdown="1">

**Tip:** Setting the destination also starts BA quietly fetching that airport's online taxiway names and building data in the background, so they are ready long before you land. The earlier you set it, the better.

</div>

### If something goes wrong {#land-dest-problems}

- *"Airport database not found. Configure database from File menu first."* You have not built the navigation database yet. See [The Navigation Database](getting-started.md#nav-database).
- BA warns that the database does not match your simulator. You built the database for MSFS 2020 but are flying MSFS 2024, or the other way round. Select the right database first.
- *"No runways found for ..."* The airport exists in the database but has no runway records. Try updating your navigation data.

## Approach Readouts {#land-readouts}

Three one-key readouts tell you where you stand on the approach. The first two need a destination runway. Without one, BA says: *"No destination runway selected. Press left bracket then shift+d to select a destination runway first."*

### Distance and bearing to the runway (Output Ctrl+D) {#land-ctrl-d}

Press Output Ctrl+D for the straight-line distance in nautical miles and the magnetic bearing from you to the start of the runway:

*"12.4 miles to runway 27L at EGLL, bearing 268 degrees."*

The bearing is useful on a visual approach: if your heading is close to the bearing, the runway is roughly ahead of you.

<div class="callout tip" role="note" markdown="1">

**Tip:** While Hand Fly mode or Visual Guidance is on, the single key D gives the same distance without entering Output mode. See [Hand Fly Mode](takeoff-climb-and-cruise.md#hand-fly).

</div>

### The ILS report (Output Ctrl+I) {#land-ils}

The ILS report is a snapshot of where you are relative to the runway's ILS: how far from the threshold, which side of the centreline, how far above or below the glideslope, and which headings to fly to join the localizer. It reads the ILS from the navigation database and your position from the simulator, so it works even if your radios are not tuned. It does not need an ILS approach to be armed on the autopilot.

Press Output Ctrl+I. If you are on the centreline (within about a tenth of a mile), you hear something like:

*"6.2 nautical miles from threshold, on centerline. 40 feet below glideslope. Runway heading 273."*

If you are off to one side, BA gives you the offset and three headings to choose from:

*"14.0 nautical miles from threshold, 1.8 nautical miles left of centerline, 7 degrees left of centerline. 300 feet above glideslope. Fly heading 333 for 60 degree intercept, 318 for 45 degree intercept, or 303 for 30 degree intercept."*

"Left of centerline" means your aircraft is to the left of the extended runway centreline as you face the runway. Pick the intercept that suits you: 30 degrees is the gentle, normal choice; 60 degrees closes the gap fastest.

Other things you may hear:

- The glideslope part is only given within 25 nautical miles of the runway.
- *"Warning: ILS signal range is 18 nautical miles."* at the start of the report means you are farther out than the ILS is published to reach. The numbers are still calculated, but your aircraft's own ILS receiver may not have a signal yet.
- *"Approaching from opposite direction. Fly heading 093 to extend outbound."* You are on the wrong side of the airport. Fly that heading to get out onto the approach side, then turn back.
- *"No ILS available for runway 27L at EGLL."* The database has no ILS for that runway. Use Visual Guidance or Output Ctrl+D instead.

Separately from this report, BA says *"Glideslope alive"* on its own when your NAV 1 receiver starts picking up a glideslope signal, and *"Glideslope lost"* when the signal goes away. Hearing "Glideslope alive" after tuning an ILS tells you the receiver has the signal; it does not mean the autopilot has captured it. You can mute it in the Monitor Manager (Output Ctrl+M), where it is listed as Glideslope.

### Approach capability (Output Ctrl+0) {#land-ctrl-0}

On the FlyByWire A320neo, the Headwind A330-900neo and the FlyByWire A380X, Output Ctrl+0 tells you what kind of approach the autoflight system is currently able to fly, the same information the top of the aircraft's flight mode annunciator shows. You hear, for example, *"Approach capability LAND 3 dual"*, *"Approach capability LAND 3 single"*, *"Approach capability LAND 2"* or *"Approach capability none computed"*. These aircraft also announce a change on their own while you are in flight, for example after a system failure lowers the capability.

On the other aircraft this key does nothing at the moment.

### Wind at the destination {#land-wind}

Output I, the wind readout described in [Readout Hotkeys](core-concepts.md#readouts), adds the wind at your destination airport once a destination runway is set. It is a quick way to check for a tailwind before you choose a landing exit.

## Visual Guidance {#visual-guidance}

### What it is {#land-vg-what}

Visual Guidance is an audio instrument for hand-flying an approach. It is BA's equivalent of the flight director needles a sighted pilot follows: it works out what the aircraft should be doing to reach the runway centreline and a normal glide path, and plays it to you as sound. You do not need an ILS. Visual Guidance uses the runway's position and glide-path angle from the navigation database, so it works on any runway in the database, including ones with no instrument approach at all.

It is made of two tones playing together:

- The **guidance tone** (BA calls it the desired tone) is where the aircraft **should** be. Its pitch, high or low, is the pitch attitude the aircraft should have. Its position from left to right is the bank angle the aircraft should have.
- The **current tone** (the current-attitude tone) is where the aircraft **is**. Its pitch is your actual pitch attitude and its position left to right is your actual bank angle.

By default the two have different sounds: the guidance tone is a triangle wave, which sounds slightly buzzy, and the current tone is a sine wave, which sounds pure and smooth. Different sounds stop them blending into one note when they match, which is exactly when you need to hear that there are two.

### The matching idea {#land-vg-matching}

Your whole job is to make the current tone sit on top of the guidance tone. You do not need to think about glide paths or angles; you fly the aircraft until the two tones agree.

**Up and down (pitch).** Listen to the two pitches. If the guidance tone is higher than the current tone, raise the nose a little. If it is lower, lower the nose. When the two are close in pitch you hear a beat: a wobble or flutter in the sound, caused by two nearly-equal notes rubbing against each other. The bigger the difference, the faster the flutter. A difference of half a degree of pitch is a clear fast flutter; a tenth of a degree is a slow, lazy wobble. When the wobble stops and you hear one steady chord, your pitch is exactly what the guidance wants.

**Left and right (bank).** Listen to where each tone sits between your ears. If the guidance tone is further to the right than the current tone, roll to the right. If it is further left, roll left. When both tones come from the same place, your bank matches what the guidance wants.

**Both at once.** The two axes are separate and you fly them together, not one after the other: roll to bring the positions together while you pitch to stop the beating, just as a sighted pilot keeps both flight director bars centred at once.

Remember what the guidance tone is telling you. It is not "where the runway is"; it is "the attitude that will get you there". When you are far left of the centreline, the guidance tone will sit to the right, asking for a right bank to turn back. As you close in, it moves back toward the middle, asking you to roll out. Follow it smoothly and it brings you onto the centreline without overshooting.

<div class="callout tip" role="note" markdown="1">

**Tip:** The tones are deliberately very sensitive near the matched state: small errors are clearly audible. For big corrections, such as the turn onto final, the guidance tone may sit fully over to one side. That is normal. The spoken bank callouts described below cover those big corrections.

</div>

### When to use it {#land-vg-when}

- Hand-flying a visual approach, or any approach where you want to fly the final yourself.
- Practising approaches.
- Backing up an autopilot approach, to hear whether the aircraft is doing what you expect.

You do not need Hand Fly mode to use Visual Guidance. If it is on, its tone is muted while Visual Guidance runs and its spoken calls carry on.

### How to use it {#land-vg-how}

1. Set a destination runway (Input Shift+D).
2. Fly toward the runway. Visual Guidance can start from a long way out; it will lead you onto the extended centreline.
3. Press Output Ctrl+V. BA says *"Visual guidance active, runway 27L"* and both tones start.
4. Match the tones as described above. Listen for the phase callouts (below), which tell you which part of the approach you are in.
5. At touchdown, Visual Guidance switches itself off and says *"Visual guidance off"*. From there the rollout tones and the Landing Exit Planner take over, if you have set them up.

To switch Visual Guidance off yourself, press Output Ctrl+V again. If you press it with no destination runway set, BA says *"No destination runway selected"* and nothing starts.

### Quick-access keys {#land-vg-keys}

While Visual Guidance is on, the single-letter keys of [Hand Fly mode](takeoff-climb-and-cruise.md#hand-fly-quick-keys) (A, S, D, H, P, B, Q and V) work with no bracket key first, plus F. F tells you the descent rate the guidance is asking for and how far you are above or below the ideal glide path, for example *"target -700, 40 high"* (aim for 700 feet per minute down; you are 40 feet above the path) or *"target -500, 120 low"*. If you press F when Visual Guidance is off, BA says *"Visual guidance not active"*.

When you press any of these keys, BA holds back its own per-second bank and centreline callouts for three seconds so your answer is not cut off.

If another program is already using one of these keys, BA tells you *"Visual guidance active. Some quick-access keys unavailable; use output mode."* The same readouts are still available through Output mode.

### What you will hear {#land-vg-hear}

**Phase callouts.** Visual Guidance moves through phases and announces each one as you enter it:

- *"Intercepting final, 1.8 miles to final"*: you are more than about a third of a mile to one side of the extended centreline. The guidance is turning you to join it. The distance is how far you are from the centreline, not from the runway.
- *"Established on centerline"*: you are on the extended centreline but not yet on the glide path.
- *"On vertical profile"*: you are on the centreline and within about 100 feet of the glide path. This is where you want to be.
- *"Extending, turn to heading 093"*: you are more than 500 feet up and on the wrong side of the runway for this approach. Fly the heading given to fly out onto the approach side. While you extend, BA gives progress every ten seconds, such as *"2.5 miles behind threshold"*, and *"Approaching threshold"* when you are close to it.
- *"Flare"*: your wheels are about 30 feet above the runway (40 feet on the Headwind A330 and the PMDG 777, 35 feet on the MD-11). The guidance tone now asks for the flare pitch. Raise the nose smoothly to match.
- *"Touchdown"*: the wheels are a few feet from the runway.

**Distance callouts.** *"5 miles"*, *"3 miles"* and *"1 mile"* from the runway threshold.

**Bank callouts.** About once a second, when the bank error changes, BA speaks how much more bank you need: *"3 left"* means roll 3 degrees further to the left than you are now; *"2 right"* means roll 2 degrees further right. *"matched"* means your bank is what the guidance wants. *"On commanded path"* means your bank matches and the guidance wants the wings level: you are tracking the centreline.

**Centerline callouts.** When your position changes by a tenth of a mile, BA says how far you are from the centreline, for example *"0.4 nm left of center"*, or *"on runway center"* when you are within about 300 feet of it. Left and right here are your aircraft's position relative to the centreline.

<div class="callout note" role="note" markdown="1">

**Note:** The vertical guidance is designed for a normal stabilised approach with the autothrust holding your speed. The guidance tone asks for pitch, and pitch controls your rate of descent only while something else holds the speed. If you are managing thrust by hand, keep your speed steady or the vertical guidance will feel inconsistent.

</div>

### Settings {#land-vg-settings}

All Visual Guidance sound settings are on the **Hand Fly** tab of the Settings window (see [The Settings Window](getting-started.md#settings-overview)):

- **Visual Guidance Tone:** the sound of the guidance (desired) tone. Default: triangle.
- **Visual Guidance Volume:** its volume.
- **Current-attitude tone:** the sound of the current tone. Default: sine. Choose a different sound from the guidance tone, or the two become hard to tell apart when matched.
- **Current-attitude Volume:** its volume. Turning it down makes the guidance tone stand out more. Do not turn it off; you need both.
- **Hard-pan visual-guidance tones (speaker-friendly):** when ticked, both tones jump fully left or fully right once the bank is more than about one degree, instead of moving smoothly. Use it on speakers, where "a little to the left" is hard to hear. On headphones you will usually leave it off.

### If something goes wrong {#land-vg-problems}

- **The tones are always fully to one side during the turn onto final.** Normal. The guidance is asking for more bank than the tone can show. Follow the spoken bank callouts until you are close to the centreline.
- **The pitch tone feels jumpy.** Make small, smooth pitch changes and give the aircraft a second to respond. Chasing the tone with large inputs makes it worse.
- **You hear three tones.** Visual Guidance mutes Hand Fly's tone automatically; if you hear a third tone, it is probably another BA tone such as taxi or takeoff assist that is still running. Switch it off.

## Flare and Rollout Tones {#flare-assist}

### What it is {#land-flare-what}

The flare and rollout tones guide the very end of the landing: the last 50 feet above the runway, the touchdown, and the landing roll. They are separate from Visual Guidance and work whether or not you use it.

There are two parts:

- **The flare tones**, which you switch on with the **Manual landing assist (adds flare tones)** checkbox when you choose your destination runway. They guide you from 50 feet down to touchdown.
- **The rollout centerline tone**, which is on by default for every landing with a destination runway or a planned landing exit, including autolands. It guides you from touchdown down the runway.

### The flare tones: two axes at once {#land-flare-tones}

From 50 feet above the runway (measured at the wheels) to touchdown, you hear two tones at the same time, one for each axis. They use different sounds so you can tell them apart.

**The vertical tone: your sink rate.** Its pitch says whether you are coming down at the right rate for your height. BA uses a smooth flare profile: at 50 feet it expects roughly a normal approach descent rate, and the expected rate shrinks as you get lower, down to a gentle 120 feet per minute at the moment the wheels touch.

- A **high** tone means you are sinking too fast. Pull back: raise the nose to slow the descent.
- A **low** tone means you are floating: not coming down fast enough. Relax the back pressure and let the aircraft settle.
- **Silence** means you are on the profile. A well-flown flare is a quiet one.

This is deliberately a sink-rate tone, not a pitch target, because the right flare pitch differs from aircraft to aircraft while a good sink rate is the same for all of them.

**The lateral tone: your position over the centerline.** This tone moves left and right. It sits on the side you need to steer toward: if it is on your right, steer right with the rudder. It follows not only where you are but also how fast you are drifting, so if you are correctly crabbed into a crosswind and holding the centreline, it sounds centred instead of nagging you. A small band around the centre is quiet.

### The rollout centreline tone {#land-rollout-tone}

When the wheels touch, the vertical tone stops: there is no pitch task left. The lateral tone carries straight on and becomes a centreline steering tone for the landing roll. It works exactly like the Takeoff Assist tone and uses the same settings (see [Takeoff Assist](takeoff-climb-and-cruise.md#takeoff-assist)): steer toward the tone, or away from it if you have chosen that, until it goes quiet. If the aircraft starts to swerve, you hear which way to steer back before it leaves the runway.

You can get this rollout tone without the flare tones. As long as the setting **Rollout centerline tone when a destination runway or landing exit is set** is ticked (it is by default), any destination runway or any planned landing exit arms the rollout tone on its own. That includes an autoland.

How the rollout tone ends depends on whether you planned an exit:

- **With a landing exit planned**, the tone stays with you until exit guidance takes over to steer you off the runway, or until you stop. BA's exit guidance and the centreline tone take turns; they never play together. Near your exit, the exit guidance's own tone steers you into the turn.
- **Without a landing exit**, the tone ends when you slow below about 40 knots or when you are clear of the runway.

### What you will hear {#land-flare-hear}

- *"Flare guidance"*: the flare tones have started, at 50 feet.
- *"Rollout guidance"*: the rollout centreline tone has started after touchdown. With a landing exit planned, the touchdown sentence about your exit (see the Landing Exit Planner below) normally takes its place.
- *"Rollout guidance complete"*: the rollout tone has finished.
- *"Flare guidance, runway 27R, not 27L."* or *"Rollout guidance, runway 27R, not 27L."*: you are landing on a different runway from the one you chose (a parallel, or the other end). The tones switch to the runway you are really on.
- *"Go around detected. Flare assist rearmed."*: you climbed back above 100 feet during the flare. The tones stop and are ready again for your next approach. BA always speaks here, because in this feature silence means "on profile" and must never be mistaken for the assist giving up.
- *"Flare sink rate cue unavailable"*: the vertical tone could not play, usually because your audio device disappeared. Fly the flare without it.
- *"Manual landing assist off"* or *"Rollout centerline tone off"*: the tones have been disarmed, for example because you switched navigation database. Set your destination again.
- *"Manual landing assist armed for EGLL runway 27L"*: the flare tones were brought back by another change, such as a landing exit plan for the same airport.

### How they behave {#land-flare-behaviour}

- **Engaging.** The flare tones start only if you are armed, have come down from above 80 feet, are now at or below 50 feet, are descending, and are within 3 miles of the runway.
- **Bounces.** If you bounce back into the air (above about 5 feet), the vertical tone comes back to talk you through the second touchdown. The lateral tone never stops.
- **Circuits and touch-and-goes.** The assist stays armed after a landing, so every approach to the same runway gets the tones without opening the window again.
- **With Visual Guidance on.** While Visual Guidance is running, the flare tones stay silent, because Visual Guidance has its own flare cue. The rollout tone still starts after touchdown, once Visual Guidance has switched itself off.
- **Hand Fly.** Hand Fly's tone is muted while the flare or rollout tones are playing.
- **Taxi guidance.** When taxi guidance takes over after landing, the rollout tone stops at once, without talking over the taxi instructions.
- **Light aircraft.** The rollout-only tone (without the flare checkbox) starts only if you touch down at 50 knots or more, so a very slow touchdown gets no rollout tone.

### Settings {#land-flare-settings}

- **Manual landing assist (adds flare tones):** the checkbox in the Select Destination Runway window. Unticked by default, and chosen fresh each time you set a destination.
- **Rollout centerline tone when a destination runway or landing exit is set:** on the **Hand Fly** tab of Settings. Ticked by default.
- The rollout tone, and the lateral flare tone, use the Takeoff Assist tone settings on the same tab: **Takeoff Assist Tone:**, **Takeoff Assist Volume:**, **Steer toward the tone to stay on the centerline**, and **Hard-pan centerline tone (full left or full right; speaker-friendly)**. The vertical flare tone uses a contrasting sound automatically.

## The Landing Exit Planner {#landing-exit}

### What it is {#land-exit-what}

Before you land, you choose the taxiway you want to leave the runway on. When you touch down, BA starts guidance automatically: it counts you down to the exit, tells you when to slow down and when to turn, steers you into the turn with a tone, and leads you off the runway until you are past the holding point. If you cannot make your exit, it finds the next one for you.

It is the landing half of taxi guidance. When it finishes, you use the normal taxi planner to get to your stand (see [Taxi Guidance](taxi-guidance.md)).

### When to use it {#land-exit-when}

Plan your exit during the descent or approach, once you know your landing runway. A good exit choice depends on your aircraft's landing distance, the wind and where your stand is. You can also plan on the ground before a circuit; guidance then starts on the next real landing.

### How to use it {#land-exit-how}

1. Press Input Shift+X. The **Landing Exit Planner** window opens.
2. **Airport ICAO** (Alt+A): this is filled in for you from your destination runway or, if you have not set one, from your loaded flight plan. To use another airport, type its code and press Tab. While the airport loads, the status line reads, for example, *"EGLL: fetching taxiway names…"* and then *"EGLL: building taxi graph…"*.
3. **Runway** (Alt+R): a list of runway directions, already set to your destination runway if you have one. If the runway BA was given is not in the list, it tells you, for example: *"Runway 27R is not in this airport's runway list. Showing runway 09L instead — check the runway before you plan an exit."*
4. When a runway is selected, BA tells you how many exits it found, for example *"6 exit options for runway 27L."* If some exits have no taxiway mapped clear of the runway in your database, it adds *"2 of them have no taxiway mapped clear of the runway."*
5. **Exit taxiway** (Alt+E): arrow through the exits. They are listed in order of distance from the landing threshold, nearest first. BA pre-selects a sensible one: never a turnaround, and one that leads clear of the runway if there is one.
6. If ATC has given you a land and hold short instruction (LAHSO), tick **Land and hold short (LA&HSO) of** (Alt+H) and choose the crossing runway you must stop before. Each choice reads like *"Runway 27 — hold point 1700 m from threshold (estimated)"*: BA estimates the stop point from the two runways' geometry, so treat it as a guide, not a painted line. Only runways that actually cross yours are listed. If your exit lies beyond that point, Plan Exit refuses it: *"Exit B9 is at 3200 metres, beyond the LAHSO hold point at 1700 metres. Pick an earlier exit or uncheck land and hold short."*
7. Choose **Plan Exit** (Alt+P). The window closes and BA confirms, for example: *"Landing exit planned: taxiway B5 at EGLL runway 27L, 2100 metres from threshold. Guidance will auto-start on touchdown."* With a LAHSO, it adds *"Land and hold short of runway 27: estimated hold point 1700 metres from the threshold."*

To remove a plan, open the window again and choose **Clear Plan** (Alt+C). BA says *"Landing exit plan cleared."* Escape closes the window without changing anything.

<div class="callout tip" role="note" markdown="1">

**Tip:** At some airports the navigation database has few taxiway names and BA fetches the rest online. If the names arrive while the window is open, the list updates itself and BA says, for example, *"Taxiway names updated for LSZH. Now 9 exits for runway 28."* Your chosen exit stays selected.

</div>

### Reading the exit list {#land-exit-list}

Each exit reads like this:

"B5 — 2100 m from threshold (High-speed, right, 30°)"

- **The name**: the taxiway you will turn onto. "(unnamed)" means the database has no name for it.
- **The distance** from the landing threshold, in metres or feet depending on your setting.
- **The type**:
  - **High-speed**: a rapid exit taxiway that leaves the runway at a shallow angle (50 degrees or less). You can turn off at speed.
  - **Normal**: an ordinary exit at roughly a right angle. You must be at taxi speed to take it.
  - **End**: an exit at or near the far end of the runway that turns back sharply (more than 110 degrees). You will need to slow right down, and it often means a sharp turn back.
- **The side**: left or right, as you face the landing direction.
- **The angle** between the exit and the runway, in degrees.
- **"(sharp turn back)"** at the end means the exit's path turns back hard soon after it leaves the runway, so you must be at walking pace by the turn. BA pre-selects an exit without this mark when there is one.
- **"(backtrack required)"** at the end means the exit is a turn pad at the runway end: you roll to the end, turn around and backtrack along the runway to a taxiway. BA guides that backtrack for you.
- **"— WARNING: no taxiway mapped clear of the runway"** at the end means your database has nothing past the junction. BA could guide you to the turn but not off the runway. Choose another exit if you can.

Where the database has several entries for what is really one turn-off, the list shows it once.

If BA finds no exits at all, it says *"No usable exits found on runway 27L in the database. Try another runway or update your navdata."*

<div class="callout note" role="note" markdown="1">

**Note:** Plan for the conditions. With a tailwind, a wet runway or a heavy aircraft, choose an exit further down the runway. BA warns you on the rollout if you are too fast for the exit, but it cannot make the aircraft stop sooner. Use Output I to check the wind at your destination before you choose.

</div>

### Worked example {#land-exit-example}

You are descending into an airport where you expect runway 27L. Your stand is on the north side, and the runway has a high-speed exit B5 on the right about 2,100 metres from the threshold, and normal exits B7 at 2,700 metres and B9 at 3,200 metres.

1. You press Input Shift+D, type the airport code, choose runway 27L, and leave the manual landing assist checkbox unticked because you will autoland. BA says *"Destination runway set: ... Runway 27L"*.
2. You press Input Shift+X. The airport and runway are already filled in. You arrow to "B5 — 2100 m from threshold (High-speed, right, 30°)" and choose Plan Exit. BA confirms the plan.
3. You fly the approach. The aircraft lands. Within a second you hear: *"Touchdown. high-speed exit taxiway B5 in 1700 metres."* At the same time, the rollout centreline tone starts; keep it quiet with the rudder.
4. *"Approaching high-speed exit taxiway B5, 500 metres."* You keep braking normally.
5. *"Taxiway B5, 300 metres."* This extra callout is only given for high-speed exits.
6. *"Taxiway B5, 150 metres. Slow down."* "Slow down" is added because you are still faster than about 60 knots, too fast even for a high-speed exit. You brake harder.
7. *"Turn right now, taxiway B5."* The steering tone swings right. You turn with the tone and let it guide you along the exit.
8. When you are past the runway holding point you hear: *"Off the runway at Taxiway B5. Stop and hold position. Open the taxi planner to set a route to your gate."* You stop, call ground, and use Input Shift+Y to plan your taxi to the stand.

Now suppose you had been too fast at step 7. Instead of "turn now" you would hear *"Too fast for taxiway B5. Continue to taxiway B7, 600 metres."* and guidance would carry on to B7 with a new countdown.

### How guidance starts {#land-exit-start}

- Guidance starts by itself when BA sees a real landing: the aircraft was in the air and is now on the ground at 40 knots or more. Taxiing onto the ground or teleporting does not count.
- If you land on a different runway from the one you planned (the parallel, or the other end), BA tells you at touchdown and guides you to a suitable exit on the runway you are actually on, for example: *"Touchdown on runway 27R, not 27L. Exit taxiway A6 in 900 metres."*
- If BA cannot work out which runway you landed on, it says *"Touchdown. Runway not identified. No exit guidance this landing."* Your plan is kept for the next landing.
- If you go around or do a touch-and-go after guidance has started, and are still in the air five seconds later, BA says *"Exit guidance off, plan kept."* and your plan is ready again for the next touchdown.
- Switching simulator or rebuilding the navigation database clears a pending plan and says so, because the old runway positions may no longer match.

## Everything the Rollout Says {#land-rollout-callouts}

Every callout between touchdown and leaving the runway, in roughly the order you meet them. Distances are spoken in metres or feet according to your setting; the feet versions are 1,500, 900 and 500 feet for the exit countdown and 1,500, 500 and 100 feet for the runway-end countdown. Any instruction you missed can be repeated with Output Ctrl+Y.

### At touchdown {#land-callouts-touchdown}

- *"Touchdown. exit taxiway B7 in 1200 metres."* (or *"high-speed exit"*, or *"runway-end exit"*): guidance has started; this is your exit and how far away it is. Keep the centreline.
- *"Touchdown on runway 27R, not 27L. ..."*: you are not on the runway you planned for. BA has picked an exit on this runway. If that touchdown sentence also carries *"Slow down."* or *"Turn left now."*, it has folded in callouts that came due while it was speaking; act on them.
- *"Touchdown on runway 27R, not 27L. No usable exit."*: there is no usable exit on the runway you landed on. BA goes straight to the runway-end countdown.
- *"Touchdown. Runway not identified. No exit guidance this landing."*: BA could not work out which runway you are on, for example after a bounce, a touch-and-go or a landing beside the runway. You get no exit guidance on this landing, but your plan is kept for the next one. Taxi off the runway yourself, then use Where Am I (Output Alt+Y) and the taxi planner.

The touchdown sentence can end with a reminder about this landing: *"LAHSO: hold short of runway 27."* if you planned a land and hold short, *"Sharp turn back, slow down early."* for an exit marked "(sharp turn back)", or *"Backtrack required, to taxiway A."* for a turn-pad exit.

### Approaching the exit {#land-callouts-approach}

- *"Approaching exit taxiway B7, 500 metres."* (1,500 feet): your first warning.
- *"Taxiway B5, 300 metres."* (900 feet): high-speed exits only, a second warning, about where a sighted pilot would see the first exit lights.
- *"Taxiway B7, 150 metres."* (500 feet): get ready to turn. If it ends with *"Slow down."*, you are too fast to make this exit: above about 60 knots for a shallow exit, or above about 30 knots for a right-angle exit or an end exit. Brake.
- *"Left turn ahead, taxiway B7."* or *"Right turn ahead, taxiway B7."*: on an ordinary right-angle exit, an early warning that the turn is coming. It comes sooner the faster you are, so you have time to slow down; it does not mean turn yet.
- *"Turn left now, taxiway B7."* or *"Turn right now, taxiway B7."*: turn now. The steering tone swings to the exit; follow it. For a very shallow exit (under 20 degrees) you hear *"Gentle left now"* or *"Gentle right now"* instead: make a small steering input, not a full turn.

If BA cannot be sure which side the exit turns to, for example because the scenery draws the junction right on the centreline, it leaves the direction out and says *"Turn now, taxiway B7."* It never guesses a side, because a wrong side is worse than none.

### Land and hold short {#land-callouts-lahso}

If you planned a LAHSO, BA counts you down to the estimated hold point whatever happens with your exit, even if you miss every exit and end up in the runway-end countdown:

- *"LAHSO hold point, runway 27, in 600 metres."*
- *"LAHSO hold point in 300 metres. Slow down."*
- *"LAHSO hold point in 150 metres. Stop before runway 27."*
- *"Warning: past the LAHSO hold point for runway 27."*: you have rolled past it. Stop and tell ATC.

When BA has to switch to another exit, it only ever picks one short of the hold point.

### Changing your exit on the rollout {#land-callouts-repick}

If ATC changes your exit while you are rolling out ("turn right at Hotel"), open the Landing Exit Planner (Input Shift+X), pick the new exit and choose Plan Exit. BA switches guidance to it: *"New exit, taxiway H, 600 metres ahead."* You can tick a LAHSO here too, and BA adds *"Land and hold short of runway 27."* A LAHSO you planned before landing stays in force; it cannot be cancelled on the roll.

BA refuses a change it cannot safely follow, says why, and keeps your current exit:

- *"Exit H is behind you. Keeping taxiway G."*
- *"Taxiway H is past the LAHSO hold point for runway 27. Keeping taxiway G."*
- *"That exit list is for runway 09. Rolling out on runway 27. Keeping taxiway G."*: the planner was showing the other end of your runway, whose exits are measured from the other threshold. Choose the runway you are on and pick again.

### The steering tone on the rollout {#land-callouts-tone}

During the middle of the rollout, the tone keeps you on runway heading and quiet. It only starts steering you toward the exit once you are close to it and turning its way. On an ordinary right-angle exit, it holds runway heading right up to *"Turn ... now"*, so it never pulls you toward the exit before you should turn; after the callout it pauses briefly while you start the turn, then steers you along the exit. If you drift off the centreline while slowing down, the tone steers you back onto runway heading rather than pulling you at the exit early. Close to the exit, a drift toward the exit's side goes quiet, because you are plainly already making the turn.

### Too fast, missed, or early {#land-callouts-retarget}

- *"Too fast for taxiway B7. Continue to taxiway B9, 500 metres."*: at the turn point you were too fast to take B7. Do not turn. BA has switched to the next exit you can reach and restarted the countdown. The sentence may end with *"Slow down."*.
- *"Taxiway B7, too fast to turn. Slow down."*: you are too fast for the exit and there is no other exit ahead. Brake. Do not try the turn at that speed.
- *"Missed taxiway B7. Retargeting taxiway B9, 500 metres ahead."*: you rolled past the exit. BA is now guiding you to the next one ahead. It never sends you to an exit behind you, and never back across the runway you just landed on. If you had started to turn, the sentence includes *"Straighten."*: straighten up along the runway first.
- *"Taking earlier exit, taxiway B5, 300 metres ahead."*: you slowed down sooner than expected and there is a reachable exit before the one you planned. BA has switched to it. If you prefer the planned exit, you will need to stop guidance and taxi there yourself.
- *"Missed taxiway B7. No reachable exit remaining."* or *"Too fast for taxiway B7. No reachable exit remaining."*: there is no exit ahead that BA can route you to. The runway-end countdown starts.
- *"Missed last exit on runway 27L."*: you passed the last exit. The runway-end countdown starts.
- *"Continue rolling to taxiway B7, 400 metres ahead."*: you slowed or stopped well short of your exit. Keep rolling to it at taxi speed.
- *"Left the runway short of taxiway B7. Now following taxiway B5."*: you turned off at an earlier taxiway than planned. That is fine: guidance follows you onto the taxiway you actually took.
- *"Off pavement."*: your wheels have left the runway and taxiway surface. Steer back onto the pavement and slow down.

### Leaving the runway {#land-callouts-vacate}

Exit guidance leads you down the exit taxiway until you are past the runway holding point, not just off the edge of the runway. When it ends, you hear one of these:

- *"Off the runway at Taxiway B7. Stop and hold position. Open the taxi planner to set a route to your gate."*: a normal, successful exit. Stop, contact ground, and plan your taxi with Input Shift+Y.
- *"Taxiway data ends at Taxiway B7. You may still be on the runway — continue ahead until clear, then open the taxi planner to set a route to your gate."*: your database maps nothing beyond this junction, so BA cannot be sure you are clear. Do not stop here: keep going straight ahead slowly until you are sure you are off the runway.
- *"You have passed the Taxiway B7 vacate point. Exit guidance ended. Stop and hold position, then open the taxi planner to set a route to your gate."*: you are off the runway but went a different way from the exit route. Stop and plan a route from where you are.
- *"You have left the runway short of Taxiway B7. Exit guidance ended. Stop and hold position, then open the taxi planner to set a route to your gate."*: you left the runway before your exit and BA could not match where you went.
- *"Exit guidance ended: no usable route from here. Stop and hold position, then open the taxi planner to set a route to your gate."*: you are off the runway but BA cannot route from here.
- *"Exit reached. Route unavailable. Taxi to taxiway B7 and use the taxi planner."*: BA could not build the route off the exit. Turn off at the exit yourself and plan with the taxi planner.

### The runway-end countdown {#land-callouts-runway-end}

When there is no exit left ahead, BA counts you down to the end of the runway instead:

- *"Runway end in 500 metres."* (1,500 feet)
- *"Runway end in 150 metres. Slow down."* (500 feet; "Slow down" only if you are faster than about 30 knots)
- *"Runway end in 30 metres. Stop."* (100 feet): stop now.
- *"Stopped on runway 27L. Runway end in 900 metres."*: said once if you stop part-way down the runway, for example when ATC holds you. After that BA stays quiet until you move.

The countdown ends according to what you do next:

- If you turn off onto a taxiway: *"Runway vacated. No route set — use the taxi planner for a route to your stand."*
- If you reach the end and turn around: *"End of runway 27L. Turn around, heading 093. Backtracking."* The tone steers you back along the runway on that magnetic heading.
- If you turn around part-way down the runway: *"Backtracking on runway 27L, heading 093."*

While backtracking:

- *"Taxiway H ahead on the left. Vacate runway."*: the taxiway BA found for you to leave the runway is coming up, about 200 metres ahead, on the side named. The tone now swings toward it; follow it off the runway. Without a name or a clear side you hear just *"Taxiway ahead. Vacate runway."*
- *"Runway vacated. No route set — use the taxi planner for a route to your stand."*: you are off, clear of every runway. The tone stops, and if you ask for taxi status you will hear that no route is loaded. That is expected.
- *"Backtracking on the runway. No taxiway connection found — use the taxi planner to set a route."*: BA cannot find a taxiway to leave by. Ask ATC and plan with the taxi planner.

<div class="callout warning" role="note" markdown="1">

**Warning:** Backtracking on an active runway is something you only do with ATC permission. BA tells you how to do it safely; it does not know whether you are cleared.

</div>

### Other things to know {#land-callouts-other}

- **Ground traffic** callouts wait while you are rolling down the runway; see [When Ground Traffic Speaks](ground-traffic.md#traffic-when).
- **To stop exit guidance** at any time, press Input Ctrl+Y (Stop Taxi Guidance).

## Landing Rate and G-Force {#land-rate}

After every landing, two Output keys tell you how firm it was. They work on every aircraft.

- **Output Ctrl+Shift+R**: the touchdown vertical speed, for example *"Landing rate 142 feet per minute."*
- **Output Ctrl+Shift+G**: the peak vertical g-force during the touchdown, for example *"Landing g-force 1.24 g."*

Both values are kept until your next landing. Before your first landing of the session, both say *"No landing recorded this session."*, even if you started the flight on a runway.

As a rough guide for an airliner, 100 to 200 feet per minute is a smooth landing, 200 to 400 is normal and firm, and more than about 600 is hard. A g-force around 1.1 to 1.3 g is typical.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Takeoff, Climb and Cruise](takeoff-climb-and-cruise.md)
- [Next chapter: Networks and Integrations](networks-and-integrations.md)
- [Guide contents](index.md)

</nav>
