---
title: "Weather"
---

<div class="guide-part" markdown="1">

Chapter 2 of 2 in [Part 4: Planning and Preflight](index.md#part-3-planning-and-preflight) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [Where BA Gets Its Weather](#wx-sources)
- [Wind: Output I](#wx-wind)
- [Outside Temperature: Output O](#wx-temperature)
- [The METAR Report Window](#wx-metar)
- [The Weather Radar](#weather-radar)
- [The Weather Radar without ActiveSky](#wx-radar-simulator)
- [The Weather Radar with ActiveSky](#wx-radar-activesky)
- [Weather Radar Troubleshooting](#wx-radar-troubleshooting)
- [ActiveSky](#activesky)
- [The Weather Settings Tab](#wx-settings)
- [Automatic Weather Announcements](#wx-announcements)
- [Cold Temperature Correction](#wx-cold-temperature)

</nav>

Weather matters at every stage of a flight: the wind decides which runway you use, the temperature and altimeter setting decide your performance and your safe altitudes, and clouds, rain, icing and turbulence decide how the flight will feel and whether you need to change your plan. A sighted pilot glances at a weather display. BA gives you the same information in words, through a few hotkeys, two windows, a calculator and a set of optional spoken alerts. The weather keys are all in Output mode and work the same on every aircraft.

<div class="table-wrap" markdown="1">

| Key | What it does |
|---|---|
| Output I | Speaks the wind where you are, and at your destination |
| Output O | Speaks the outside air temperature |
| Output Shift+M | Opens the METAR Report window |
| Output Shift+R | Opens the Weather Radar window |
| Output Ctrl+Shift+T | Opens the Cold Temperature Altitude Correction calculator |

</div>

<div class="callout note" role="note" markdown="1">

**Note:** On aircraft with a CDU or MCDU window, Input Shift+M opens that window; Output Shift+M always opens the METAR window.

</div>

## Where BA Gets Its Weather {#wx-sources}

BA can read weather from three places. Knowing which one is answering helps you understand what you hear.

- **The simulator itself.** This is always available. Whatever weather MSFS is drawing, live or preset, BA can read the wind, temperature, visibility, cloud and precipitation at your aircraft. When you do not use ActiveSky, this is the main source.
- **HiFi ActiveSky**, if you own it and switch on BA's ActiveSky integration. ActiveSky is a separate weather program that replaces the simulator's weather. When it is running and enabled in BA, BA reads the wind, precipitation, turbulence and station reports straight from ActiveSky, because under ActiveSky some of the simulator's own weather values become unreliable (precipitation, for example, can stick on "extreme snow").
- **The internet.** The VATSIM network's public METAR feed gives the written report for any airport. Aviationweather.gov supplies real-world SIGMETs, AIRMETs and pilot reports. Open-Meteo supplies forecast winds at height when ActiveSky is not used. You do not need a VATSIM account for any of this; you only need an internet connection.

BA never mixes sources for the same reading. Each readout tells you, or makes it clear, which source answered.

## Wind: Output I {#wx-wind}

Press Output I to hear two winds separated by a comma: the wind at your aircraft right now, then the wind reported at your destination. For example, *"270 at 12, 250 at 8, gusting 15"* means the wind here is from 270 degrees at 12 knots, and at your destination from 250 degrees at 8 knots with gusts to 15.

### What you will hear {#what-you-will-hear}

- The first wind is direction (three digits) at speed in knots, for example *"045 at 7"*, or *"calm"*.
- The second wind comes from your destination airport's latest METAR, read from the VATSIM feed. It can include *"gusting"* and the speed of the gusts. A variable wind is read as *"variable at 3"*. If the report cannot be found you hear *"unavailable"*.
- If you have not chosen a destination runway, the second part is *"no destination"*. Set one with Input Shift+D. See [Choosing a Destination Runway](approach-and-landing.md#destination-runway).

### With and without ActiveSky {#with-and-without-activesky}

- **ActiveSky integration off:** the wind here comes from the simulator. That is correct for anyone flying with the simulator's own weather.
- **ActiveSky integration on and ActiveSky running:** the wind here comes from ActiveSky, so it matches what the Weather Radar shows. While you are on the ground, the surface gust is added (*"270 at 12, gusting 20"*). In the air, BA does not add the surface gust, because a gust measured at the ground has nothing to do with the wind you are flying through at altitude.
- **ActiveSky integration on but ActiveSky not answering:** BA tells you before giving the simulator's wind, so you are never misled: *"ActiveSky not responding, using simulator wind. 270 at 12, no destination"*.

If you are not connected to the simulator you hear *"Not connected to simulator."*

## Outside Temperature: Output O {#wx-temperature}

Press \], then O. BA speaks the outside air temperature at the aircraft, for example *"-42 degrees Celsius"* in cruise or *"15 degrees Celsius"* on the ground. This always comes from the simulator. It is useful for checking engine and wing anti-ice conditions and for the cold temperature calculator later in this chapter (use the airport's reported temperature for that, not the temperature at cruise altitude).

## The METAR Report Window {#wx-metar}

### What it is {#what-it-is-16}

A METAR is the standard written weather report an airport issues, usually every half hour or hour. It gives the wind, visibility, weather, cloud, temperature, dew point and altimeter setting in a compact code. The METAR Report window lets you look up the METAR for any airport by its four-letter ICAO code.

### When to use it {#when-to-use-it-3}

- Before departure, to check the weather at your departure, destination and alternate airports.
- In cruise, to see how the destination weather is changing.
- To get the altimeter setting and runway-relevant wind before an approach.

### How to use it {#how-to-use-it-8}

1. Press \], then Shift+M. The **METAR Report** window opens with focus in the **Airport ICAO Code** box.
2. Type the four-letter code, for example EGLL. Letters are capitalised for you.
3. Press Enter. BA fetches the report and moves focus to the **VATSIM METAR** box with the whole report selected, so your screen reader reads it straight away.
4. Read the report with the arrow keys. Press Shift+Tab to go back to the code box and look up another airport.
5. Press Escape, or Alt+C for **Close**, to close the window. Focus returns to the simulator.

The report is shown exactly as issued, in METAR code. For example: *"EGLL 181150Z 27012KT 9999 FEW030 15/09 Q1013 NOSIG"* means Heathrow at 11:50 UTC on the 18th, wind 270 degrees at 12 knots, visibility 10 kilometres or more, a few clouds at 3,000 feet, temperature 15, dew point 9, pressure 1013 hectopascals, no significant change expected. If you want the same kind of report spoken in plain English, the Weather Radar's Closest Station box (with ActiveSky) decodes it for you.

### What you will hear and see {#what-you-will-hear-and-see}

- *"Please enter an ICAO code"* if you pressed Enter with the box empty.
- *"ICAO code must be exactly 4 characters (e.g., KJFK, EGLL, KATL)."* if the code is too short.
- *"No METAR data found for"* followed by the code, if the airport does not publish a report or the code is wrong. Small airfields often have no METAR.
- *"Error retrieving METAR data for"* followed by the code and the reason, if the internet lookup failed.

A short status line above the report says, for example, *"METAR for EGLL"*.

### With ActiveSky: the ActiveSky METAR and forecast {#with-activesky-the-activesky-metar-and-forecast}

If ActiveSky integration is switched on and ActiveSky is running when you open the window, the window grows two extra controls below the VATSIM box:

- **ActiveSky METAR**: the report for the same airport as ActiveSky has it. When you fly with ActiveSky this is the weather the simulator is actually showing you, which can differ from the real-world VATSIM report, especially if ActiveSky is in a historical or custom mode.
- **Forecast**: a list with Now, +1 hour, +2 hours, and so on up to +6 hours. Choosing an offset asks ActiveSky to build a forecast METAR for that time from the airport's report and forecast. The ActiveSky box then updates, and its name tells you which time you are reading, for example *"ActiveSky METAR, +2 hours"*.

The tab order is: code box, VATSIM METAR, ActiveSky METAR, Forecast, Close. The forecast list only affects the ActiveSky box. If ActiveSky has no report for that airport, its box says *"No ActiveSky METAR found for"* the code.

If ActiveSky was not running when you opened the window, close it and reopen it after starting ActiveSky.

## The Weather Radar {#weather-radar}

### What it is {#what-it-is-17}

The Weather Radar window is BA's complete weather picture. Despite the name it does not draw a radar image; instead it gathers everything known about the weather around you into a set of lists you read line by line: the conditions at your aircraft, nearby hazard advisories, pilot reports, and the winds above and below you. With ActiveSky it adds a decoded station report, a vertical profile of the cloud and winds, and the advisories along your planned route.

### When to use it {#when-to-use-it-4}

- Before departure, as a weather briefing.
- In the climb and cruise, to check for turbulence, icing reports, thunderstorm SIGMETs, and to pick a better cruise level from the winds aloft.
- Before descent, to check cloud layers, icing and the surface wind.
- Any time something feels wrong: "Am I in cloud?", "Is it raining?", "Is that turbulence going to get worse?"

### How to use it {#how-to-use-it-9}

1. Press \], then Shift+R. The **Weather Radar** window opens and fetches everything at once. Focus starts in the **Weather at Current Position** list.
2. Use the Down and Up arrow keys to read a list one line at a time. Each line is one fact, one advisory line, or one wind level.
3. Press Tab to move to the next list, Shift+Tab to go back.
4. The window refreshes itself every 30 seconds, changing only the lines that changed and keeping your reading position, so you can leave it open during the flight.
5. Press F5, or Alt+R for **Refresh (F5)**, to fetch fresh data immediately.
6. Press Escape, or Alt+C for **Close**, to close. Focus returns to the simulator.

The controls, in Tab order, are:

1. **ActiveSky status** (only when ActiveSky integration is on)
2. **Weather at Current Position**
3. **Closest Station Weather** (ActiveSky only)
4. **Vertical Profile** (ActiveSky only)
5. **Route Advisories** (ActiveSky only)
6. **Nearby Advisories**
7. **Winds Aloft**
8. **Decode advisories into plain English** checkbox (Alt+D)
9. **Refresh (F5)** button
10. **Close** button
11. **Status**, a single line that says *"Fetching weather data..."* during a fetch, then *"Last updated:"* with the time, or *"Error:"* with the reason if something failed.

The ActiveSky-only parts are hidden completely when the integration is off.

## The Weather Radar without ActiveSky {#wx-radar-simulator}

With the ActiveSky integration switched off (the default), you have four parts: Weather at Current Position, Nearby Advisories, Winds Aloft and the Status line.

### Weather at Current Position {#weather-at-current-position}

This list comes straight from the simulator and describes the air around your aircraft. You will read lines like these:

- **Wind:** *"Wind: 270° at 12 knots"*. The wind at your aircraft's position and height, the same as Output I. In cruise this is the wind you are flying through, not the wind at the ground.
- **Visibility:** *"Visibility: 4.5 km (2.8 statute miles)"*, or *"Visibility: 10+ km (6+ statute miles)"* when it is good. Both units are given because pilots in the United States think in statute miles and most of the world in kilometres. Under 5 km (about 3 miles) you are near the limits for visual flight; under about 1,500 metres you are in low-visibility territory and should expect an instrument approach.
- **Temperature:** *"Temperature: -12°C"*. The outside air temperature at the aircraft. At or below about +10°C with visible moisture (cloud or rain), icing is possible; that is when most aircraft need engine anti-ice.
- **In cloud:** *"In cloud: Yes"* or *"In cloud: No"*. Whether the aircraft is inside a cloud right now. Useful for anti-ice decisions and for knowing whether you will see the runway yet.
- **Precipitation:** *"Precipitation: None"*, or for example *"Precipitation: Moderate rain (35%)"*. The type is rain, snow or freezing rain, and the intensity is light, moderate, heavy or extreme. The percentage is the simulator's own intensity figure. Freezing rain is serious: it builds ice quickly on any aircraft.

If BA is not connected to the simulator, the list says *"Not connected to simulator."*

### Nearby Advisories {#nearby-advisories}

This list shows real-world hazard warnings around your position, downloaded from aviationweather.gov. It covers at least 300 nautical miles around you (or your proximity range from the Weather settings tab, if you have set it larger). There are two kinds of entry:

- **SIGMETs and AIRMETs.** These are official warnings issued to all pilots. A SIGMET warns of significant weather that affects every aircraft: thunderstorms, severe turbulence, severe icing, volcanic ash, tropical cyclones, dust storms. An AIRMET warns of weather that matters mostly to smaller aircraft: moderate turbulence or icing, low cloud and poor visibility (IFR conditions), mountain obscuration, low-level wind shear.
- **PIREPs, pilot reports.** Real pilots report the turbulence and icing they actually met, with the height and the aircraft type. A pilot report of moderate turbulence at the level you are about to climb to is one of the most useful things you can know.

The list starts with a header line giving your position and the range, for example *"Position: 51.47°, -0.45° \| Range: 300 nm"*, then a divider line. Next comes *"SIGMETs / AIRMETs"* with the count, then each advisory:

- A first line with its type and hazard, for example *"\[SIGMET\] Thunderstorms"*, followed by a qualifier such as embedded or frequent if one is given.
- *"Altitude:"* with the heights it covers, if known.
- A line giving the bearing and distance from you to the nearest part of the area, for example *"240° / 85 nm"*, and the time it is valid from and to.

Then *"Pilot Reports"* with the count, each one as *"\[PIREP\]"* followed by the hazard and the flight level, then the bearing and distance, the time of the report and the aircraft type.

If there are none, you read *"No active SIGMETs or AIRMETs found."* and *"No pilot reports (turbulence/icing) found within 300 nm."* (with your actual range).

The **Decode advisories into plain English** checkbox, just after the Winds Aloft list, changes how these entries are written. Unchecked, you get the short aviation abbreviations as published (for example "FL300" and "MOD"). Checked, BA spells them out: heights in feet, intensities and qualifiers as words. The choice is remembered. It takes effect on the next refresh (press F5 to see it at once).

<div class="callout note" role="note" markdown="1">

**Note:** Nearby Advisories are always real-world data. They match the simulator's weather when you fly with live weather. With preset or custom weather they may describe storms that are not in your sky.

</div>

### Winds Aloft {#winds-aloft}

This list gives the wind every 1,000 feet from 5,000 feet below you to 5,000 feet above you. On the ground, that means from the ground up to 5,000 feet. It starts with a line such as *"Aircraft: 35,000 ft \| forecast winds:"*, then a divider, then one line per level:

*"36,000 ft: 280° / 95 kts"*

The level closest to your current altitude is marked *"(nearest)"*. The last line says where the figures came from: *"Source: Open-Meteo"*. Open-Meteo is a free internet weather forecast, so these are forecast winds at your position, not a measurement from the simulator.

Use this list to choose a cruise level: if the headwind is much weaker 2,000 feet lower, it may be worth asking for that level. It also helps on descent, to expect how the wind will change as you come down.

If BA cannot get the data you read *"Winds aloft data unavailable."* If the simulator has not given BA a position yet, the advisories and winds lists say *"Aircraft position unavailable — connect to simulator first."*

## The Weather Radar with ActiveSky {#wx-radar-activesky}

When you switch on the ActiveSky integration (see [ActiveSky](#activesky)) and ActiveSky is running, the Weather Radar becomes much richer. ActiveSky knows things the simulator does not report, such as the surface wind and gusts, the cloud ceiling, the altimeter setting, the turbulence level and the full weather report of the nearest station. Every part of the window is described below in Tab order.

### ActiveSky status {#activesky-status}

The first line in the window. It says which mode ActiveSky is in and its weather clock, for example *"ActiveSky: Live Real time mode, weather time 1935Z"*. Live mode means ActiveSky is showing today's real weather. Other modes, such as a historical date or custom weather, mean the weather in your sky is not today's real weather; the Nearby Advisories list warns you about that (see below).

If the integration is on but BA cannot reach ActiveSky, this line explains why, for example *"ActiveSky: not detected"* followed by the technical reason. In that case the rest of the window falls back to the simulator, exactly as in the previous section, and the three ActiveSky-only lists say *"unavailable"*.

### Weather at Current Position, from ActiveSky {#weather-at-current-position-from-activesky}

The same list as before, but now filled from ActiveSky and with more lines:

- **Wind at altitude:** *"Wind (at altitude): 280° at 45 knots"*. The wind at your aircraft, the same figure Output I gives you.
- **Turbulence:** *"Turbulence: moderate"*. This line only appears when there is turbulence worth reporting. ActiveSky reports turbulence on a scale of 1 to 100 and sits at around 25 even in perfectly calm air, so BA hides that baseline and only shows the line from light upwards, using the words pilots and controllers use: light, moderate, severe or extreme. If there is no Turbulence line, the air is smooth.
- **Surface wind:** *"Surface wind: 250° at 14 knots, gusting 24"*. The wind at the ground below you. On the ground and on approach this is the wind that matters for takeoff and landing, including the gusts, which you add half of to your approach speed on many aircraft.
- **Visibility:** *"Visibility: 6.0 statute miles (9.7 km)"*, or *"Visibility: 10+ statute miles (16+ km)"*. ActiveSky reports visibility in statute miles, so here miles come first.
- **Temperature at altitude:** *"Temperature (at altitude): -45°C"*.
- **Surface temperature:** *"Surface temperature: 12°C"*. The temperature at the ground below you. This is the figure for the cold temperature calculator and for takeoff performance.
- **Temperature and dew point:** *"Temperature/dew point: 12 / 9°C"*. Only ActiveSky provides the dew point. When the temperature and dew point are within two or three degrees of each other, expect mist, fog or low cloud, especially as the air cools in the evening.
- **Cloud ceiling:** *"Cloud ceiling: 1,200 ft AGL"*, the height above the ground of the lowest layer that covers more than half the sky (broken or overcast). This is what decides whether you will see the runway in time. If there is no such layer below 8,000 feet you read *"Cloud ceiling: above 8,000 ft AGL or no broken/overcast layer"*.
- **QNH:** *"QNH: 1013 hPa / 29.91 inHg"*, the altimeter setting at your position in both units. Set this on your altimeter below the transition level.
- **In cloud:** *"In cloud: Yes"* or *"No"*. This still comes from the simulator, because ActiveSky does not report it, and the simulator always knows where its clouds are. If the simulator is not connected you read *"In cloud: unknown (sim not connected)"*.
- **Precipitation:** *"Precipitation: light rain"*, *"heavy snow"*, *"thunderstorm with heavy rain"*, *"freezing drizzle"*, *"showers of rain"* and so on, or *"None"*. Under ActiveSky this is read from the nearest station's METAR rather than from the simulator, because the simulator's own precipitation value is unreliable under ActiveSky. The same source is used by the precipitation announcements, so the window and the spoken alerts always agree.

<div class="callout tip" role="note" markdown="1">

**Tip:** Some of these lines, such as the cloud ceiling, QNH and surface gust, only exist when ActiveSky is answering. If they disappear from the list, ActiveSky has stopped responding and BA has quietly fallen back to the simulator. The ActiveSky status line at the top tells you for certain.

</div>

### Closest Station Weather {#closest-station-weather}

This list reads the METAR of the reporting station nearest to you, decoded into plain English, followed by the raw report. For example:

*"Decoded weather at EGLL. Wind: 270 at 12 knots. Visibility: 10 kilometres or more. Clouds: Few at 3,000 feet. Precipitation: None. Temperature: 15. Dew point: 9. Altimeter: 1013 (29.91 inches)."*

Then a blank line, *"Raw METAR:"*, and the report in code.

Wind gusts are read as *"gusting"* and a varying wind as *"varying between"* two directions. United States reports give visibility in statute miles and are read that way; elsewhere it is kilometres or metres. Cloud layers are listed from lowest to highest (few, scattered, broken, overcast, or vertical visibility in fog). *"Clouds: Clear"* means the report said CAVOK, ceiling and visibility OK. If ActiveSky cannot name a station the list says *"Decoded weather at your position"*, and if no report is available at all it says *"unavailable"*.

This is exactly the same text, word for word, as the spoken *"Active sky weather updated"* announcement described later, so what you read on demand and what you hear automatically never disagree.

### Vertical Profile {#vertical-profile}

This list describes the column of air above and below you, like a cross-section a meteorologist would draw. It has two parts.

First, *"Cloud layers:"*, each layer from lowest to highest, for example:

- *"Broken, 2,400 to 7,800 feet, moderate icing, rain"*
- *"Overcast, 14,000 to 22,000 feet, light turbulence"*

Each line gives the coverage (few, scattered, broken or overcast), the base and top in feet, and only if present the icing (light, moderate, heavy or severe), the precipitation (rain or snow) and the turbulence inside that layer. This is the best place to find out how thick a cloud layer is and whether you will pick up ice climbing through it. If there are none you read *"No cloud layers reported below FL560."*

Second, *"Winds and temperatures aloft:"* at the surface, around 5,000, 10,000, 18,000, 24,000 and 34,000 feet, plus the level nearest you, for example:

- *"Surface: 250 at 14, gusting 24, 12"*
- *"10,000 feet: 270 at 38, minus 6"*
- *"34,000 feet: 280 at 95, minus 49, light turbulence"*

Each line is direction, speed in knots, then the temperature in degrees Celsius (*"minus"* for below zero), and a turbulence word if there is any at that height.

If ActiveSky cannot give a profile for your position the list says *"unavailable"*.

### Route Advisories {#route-advisories}

This list shows the SIGMETs and AIRMETs that lie along the flight plan loaded in ActiveSky. It is different from Nearby Advisories: Nearby Advisories is everything around you now; Route Advisories is everything on your planned route, however far ahead. For it to work, ActiveSky must have your flight plan. If you link ActiveSky to SimBrief, ActiveSky downloads your plan itself; otherwise load the plan in ActiveSky. BA does not send a plan to ActiveSky.

- With nothing on your route you read *"No advisories on route."*
- With the **Decode advisories into plain English** checkbox unchecked, each advisory is shown as ActiveSky gives it: a header such as *"MHTG SIGMET J5 EMBD TS"*, a *"Valid until:"* line, and the advisory text in code, with a blank line between advisories. ActiveSky often lists the same advisory several times, once for each leg of the route it touches; BA shows it once.
- With the checkbox checked, each advisory becomes a plain-English summary, for example: *"MHTG SIGMET J5: Central American FIR, embedded thunderstorms, observed at 1830Z, tops FL520, moving west at 5 knots, no change expected."* then *"Valid until 2200Z."* The summary names the region, the hazard, whether it was observed or forecast, the heights, its movement and whether it is getting stronger or weaker. The list of latitude and longitude points that draws the area is left out of the summary because it means nothing when read aloud; uncheck the box if you want it. If BA cannot recognise the hazard it shows the advisory as written rather than risk leaving out what it is warning about.
- Where BA can work out where the area is, each advisory ends with a **Location** line: *"Location: 123 nm ahead"*, *"Location: 95 nm behind"*, *"Location: less than one nautical mile ahead"*, or *"Location: Inside"* when you are in it. Distances are measured to the nearest edge of the area. Ahead and behind are judged from your heading. Some advisories have no location BA can work out, and then there is simply no Location line.
- If ActiveSky did not answer, the list says *"unavailable"*.

### Nearby Advisories, with ActiveSky {#nearby-advisories-with-activesky}

This list stays exactly as described in the previous section, using real-world data from aviationweather.gov, because that data gives the precise bearing and distance. The one difference: if ActiveSky is in any mode other than Live, a note appears under the header: *"Note: nearby advisories are live real-world data; ActiveSky is in"* followed by the name of its current mode. It reminds you that today's real storms may not be in the sky ActiveSky is showing you.

### Winds Aloft, with ActiveSky {#winds-aloft-with-activesky}

The same levels as before, but ActiveSky answers instead of Open-Meteo, so these are the winds the simulator is really flying you through right now. The header says *"current winds"* instead of *"forecast winds"*, each line also gives the temperature (*"36,000 ft: 280° / 95 kts, -52°C"*), and the last line says *"Source: ActiveSky"*. If ActiveSky does not answer this particular request, BA uses Open-Meteo instead and the last line says so.

## Weather Radar Troubleshooting {#wx-radar-troubleshooting}

- **Everything says "Press F5 or Refresh".** The first fetch has not finished or failed. Press F5 and check the Status line at the end of the Tab order.
- **"Aircraft position unavailable".** BA is not connected to the simulator, or you are still in the menus. Load into a flight and press F5.
- **The ActiveSky lists are missing.** The integration is switched off. Turn on **Enable ActiveSky integration** on the Weather settings tab.
- **The ActiveSky lists say "unavailable" and the status line says "not detected".** ActiveSky is not running on this PC, or has not finished starting. Start it, wait until it shows weather, and press F5. BA talks to ActiveSky through the small web service ActiveSky runs on your own PC, so a firewall that blocks local connections can also cause this.
- **QNH or temperature showed huge numbers such as 1014999 hPa.** This happened with older versions of BA on Windows set to a language that uses a comma for decimals. It is fixed; update BA.

## ActiveSky {#activesky}

### What it is {#what-it-is-18}

HiFi ActiveSky is a separate paid weather program for MSFS. It replaces the simulator's weather with its own, built from real-world reports or from historical or custom scenarios. BA can read weather directly from ActiveSky while it is running on the same PC.

### Do I need it? {#do-i-need-it}

No. Everything in this chapter except the parts marked ActiveSky works with the simulator's own weather. If you do not own ActiveSky, leave the integration off; BA then never even looks for it, so nothing slows down.

If you do fly with ActiveSky, switch the integration on. Under ActiveSky the simulator's own weather values can be wrong (precipitation stuck on snow, a wind that does not match), and reading ActiveSky directly fixes that.

### How to switch it on {#how-to-switch-it-on}

1. Open the Settings window (see [The Settings Window](getting-started.md#settings-overview)) and go to the **Weather** tab.
2. In the **ActiveSky (HiFi)** group, check **Enable ActiveSky integration** (Alt+A).
3. Press OK.

From then on Output I, the Weather Radar, the METAR window and the precipitation announcement use ActiveSky, as described above, and the ActiveSky settings appear on the Weather tab. The spoken ActiveSky updates also need **Auto-announce weather state changes**, described next.

## The Weather Settings Tab {#wx-settings}

All weather settings are on the **Weather** tab of the Settings window. Some only appear when another setting they depend on is checked, so a setting that would do nothing is never in your way. Hidden settings keep their values.

<div class="table-wrap" markdown="1">

| Setting | Default | What it does |
|---|---|---|
| Enable ActiveSky integration | Off | Reads weather from ActiveSky when it is running. |
| Auto-announce weather state changes | Off | The master switch for spoken weather changes: cloud, precipitation, visibility, icing, and with ActiveSky the decoded weather updates, turbulence and mode changes. |
| Announce turbulence changes | On | Speaks turbulence getting worse or easing. Shown only when the master switch and ActiveSky are both on. |
| Announce icing | On | Speaks ice starting to build and clearing. Shown only when the master switch is on. |
| Auto-announce approaching SIGMETs and AIRMETs | Off | Speaks real-world SIGMETs and AIRMETs that come within the proximity range. |
| Auto-announce approaching pilot reports (PIREPs) | Off | Speaks significant pilot reports of turbulence or icing within the proximity range. |
| Announce route advisories by proximity (ActiveSky) | On | Speaks advisories on your ActiveSky route as you approach, enter and leave them. Shown only when ActiveSky is on. |
| Proximity range (nautical miles) | 100 | How close a SIGMET, AIRMET or PIREP must be to be announced. 10 to 500. |
| En-route advisory distance (nautical miles) | 100 | How far ahead a route advisory is announced. 10 to 500. Shown only when ActiveSky is on. |
| Weather announcement interval | Active Sky download interval | The shortest gap between spoken ActiveSky weather updates. Shown only when the master switch and ActiveSky are both on. |

</div>

The proximity range and the en-route advisory distance are separate settings for separate features; changing one never changes the other.

Each announcement is spoken only when something changes. When BA starts watching (and after you switch aircraft or reconnect), it quietly notes the weather you are already in and only speaks later changes.

## Automatic Weather Announcements {#wx-announcements}

### Cloud, precipitation and visibility {#cloud-precipitation-and-visibility}

With **Auto-announce weather state changes** on, BA speaks:

- *"Entering cloud"* and *"Leaving cloud"* as you pass into and out of cloud.
- *"Precipitation started:"* followed by the intensity or type, *"Precipitation now"* followed by the new description when it changes, and *"Precipitation stopped"*.
  - Without ActiveSky, the description is the intensity: light, moderate, heavy or extreme, for example *"Precipitation started: moderate"*. It is spoken again only when it moves to a different intensity band.
  - With ActiveSky, it is the full description from the nearest station's METAR, for example *"Precipitation started: light rain"* or *"Precipitation now heavy snow"*. The same description is never repeated.
- *"Visibility low:"* followed by the visibility in kilometres when it drops below 1.5 kilometres, and *"Visibility improving:"* when it rises back above, for example *"Visibility low: 0.8 km"*.

Cloud and visibility always come from the simulator, with or without ActiveSky.

### Icing {#icing}

With the master switch and **Announce icing** on, BA watches how much ice is actually building on the airframe, on any aircraft:

- *"Icing conditions, ice accumulating"* when ice starts to build up. Turn on your anti-ice (or check it is on) and consider leaving the icing layer by climbing or descending.
- *"Icing conditions cleared"* when the ice has gone.

BA waits for the ice to clear properly before saying it has cleared, so a reading hovering on the edge does not make it repeat itself.

<div class="callout note" role="note" markdown="1">

**Note:** The FlyByWire A380X has its own icing callout, taken from its ice detector: *"Icing conditions"* and *"Icing conditions cleared"*. On that aircraft BA uses that one only, so you never hear icing twice. The Announce icing setting does not switch the A380X's own callout off.

</div>

### Turbulence {#turbulence}

With ActiveSky, the master switch and **Announce turbulence changes** all on, BA speaks the turbulence level as it changes, in words only:

- *"Entering light turbulence"* (or moderate, severe, extreme) when you leave smooth air.
- *"Turbulence now moderate"* when it gets worse.
- *"Turbulence easing to light"* when it gets better.
- *"Smooth air"* when it has gone.

The words follow the standard pilot categories, the same ones the Weather Radar shows. To stop the callout flipping back and forth at a boundary, BA moves up a level as soon as the turbulence reaches it, but only moves back down once it has dropped clearly below. In moderate or worse turbulence, slow to your aircraft's turbulence penetration speed, make sure the seat belt sign is on and consider asking for a different level; the Vertical Profile and pilot reports help you choose one.

### ActiveSky weather updates {#activesky-weather-updates}

With ActiveSky and the master switch on, every time ActiveSky downloads new weather and the nearest station's report has changed, BA reads the new decoded report:

*"Active sky weather updated. Decoded weather at EGLL. Wind: 270 at 12 knots. Visibility: 10 kilometres or more. Clouds: Few at 3,000 feet. Precipitation: None. Temperature: 15. Dew point: 9. Altimeter: 1013 (29.91 inches)."*

This is the same text as the Weather Radar's Closest Station Weather list. It is a good way to keep track of a changing altimeter setting in the descent.

If these updates come too often, for example while you teleport around, use the **Weather announcement interval** setting. The choices are Active Sky download interval (no extra limit: BA speaks whenever ActiveSky has new weather), 5, 10, 15, 20, 30 or 45 minutes, or 1 hour. A change that arrives during the waiting time is spoken once the time is up, not lost.

BA also tells you if ActiveSky switches mode, for example *"ActiveSky weather mode changed to Live Real time mode."* It never announces the mode ActiveSky was already in when you started; you can always read it at the top of the Weather Radar.

### SIGMET, AIRMET and pilot report alerts {#sigmet-airmet-and-pilot-report-alerts}

These two settings work with or without ActiveSky and do not need the master switch. They use the same real-world data as the Weather Radar's Nearby Advisories list.

With **Auto-announce approaching SIGMETs and AIRMETs** on, each new advisory within your proximity range is spoken once, for example:

*"SIGMET: Thunderstorms, FL250 to FL450, bearing 240 degrees, 85 nautical miles"*

That is the type, the hazard, the heights if known, and the bearing and distance from you to the nearest part of the area.

With **Auto-announce approaching pilot reports (PIREPs)** on, each significant pilot report within range (moderate or worse turbulence or icing; light reports are skipped) is spoken once, for example:

*"Pilot report: MOD turbulence at FL350, bearing 310 degrees, 60 nautical miles"*

If an advisory or report is still within range, it is spoken again about every 15 minutes as a reminder.

### Route advisories (ActiveSky) {#route-advisories-activesky}

With ActiveSky on and **Announce route advisories by proximity (ActiveSky)** on, BA watches the SIGMETs and AIRMETs on the route loaded in ActiveSky (the same ones as the Weather Radar's Route Advisories list) and speaks them as they become relevant. This setting does not need the master switch.

- **Approaching:** when an advisory ahead of you comes within the en-route advisory distance (100 nautical miles unless you change it): *"Route advisory, 95 nautical miles ahead:"* followed by a short summary: its name, region, hazard and heights, for example *"Route advisory, 95 nautical miles ahead: MHTG SIGMET J5, Central American FIR, embedded thunderstorms, tops FL520."* Movement, trend and validity are left out of the spoken version to keep it short; read the Weather Radar's Route Advisories list for those. An advisory that is already behind you is not announced as approaching.
- **Already there:** if you are already inside an area when BA first sees it: *"Route advisory at your position:"* followed by its summary.
- **Entering:** when you fly into the area: *"Entering advisory area:"* followed by its summary.
- **Leaving:** when you are clear of it: *"Left advisory area:"* followed by its name only, to keep it short. BA waits about a minute of being outside before saying this, so brushing the edge of an area does not make it come and go.
- **No location known:** an advisory BA cannot place on the map is announced once when it first appears: *"Route advisory:"* followed by its summary.

Each area is only announced as approaching once. After you have been inside it, BA only tells you about entering and leaving it again. When an advisory expires or is replaced, BA says nothing; the Weather Radar always shows what is currently active. On a new flight after a turnaround (at least five minutes on the ground and then a takeoff), BA starts afresh, so an advisory you crossed on the way out will be announced again on the way back.

### If announcements do not come {#if-announcements-do-not-come}

- **Nothing about cloud or rain.** Check **Auto-announce weather state changes** is on, and that BA is connected to the simulator.
- **No turbulence or "Active sky weather updated" callouts.** These need ActiveSky running, **Enable ActiveSky integration** on, and **Auto-announce weather state changes** on. Turbulence also needs **Announce turbulence changes**.
- **No route advisory callouts.** ActiveSky must have your flight plan loaded, through its SimBrief link or by loading it in ActiveSky. Open the Weather Radar: if Route Advisories says *"No advisories on route."*, there is simply nothing to announce.
- **Too much talking.** Turn off the parts you do not need, or set a longer weather announcement interval. You can also use the Monitor Manager to control other announcements; see [The Monitor Manager](core-concepts.md#monitor-manager).

## Cold Temperature Correction {#wx-cold-temperature}

### What it is {#what-it-is-19}

Your altimeter assumes the air is at a standard temperature. In very cold air, the air is denser than standard, and the altimeter reads higher than you really are. On a cold day, an approach flown exactly at the published altitudes can put you hundreds of feet lower than intended, closer to terrain. Pilots correct for this by adding a margin to the published approach altitudes. The Cold Temperature Altitude Correction window does that sum for you. It uses the same standard EUROCONTROL formula as the FlyByWire flyPad's performance page.

It is a calculator only. It needs no simulator connection and no particular aircraft, so you can use it while planning on the ground.

### When to use it {#when-to-use-it-5}

When the temperature at your destination is at or below 0°C, and especially well below, before you brief the approach. Correct the altitudes that keep you clear of terrain: the initial and intermediate approach altitudes, the final approach fix altitude, the minimum descent altitude or decision altitude, and the missed approach altitudes. Many airlines and countries require it below a set temperature, and some approach charts state a lowest temperature at which the procedure may be flown.

### How to use it {#how-to-use-it-10}

1. Press \], then Ctrl+Shift+T. The **Cold Temperature Altitude Correction** window opens with focus in the first box.
2. In **Aerodrome (field) elevation, feet**, type the destination airport's elevation, for example 790. Negative values are allowed for the few airports below sea level.
3. Press Tab. In **Reported temperature, Celsius**, type the airport's reported temperature, for example -25. Use the METAR temperature or, with ActiveSky, the surface temperature, not the temperature at your cruise altitude.
4. Press Tab. In **Published altitudes (feet), one per line**, type each altitude from the approach chart on its own line, pressing Enter between them. For example 3000, then 2200, then 1010.
5. Press Tab to the **Calculate** button and press Space, or press Alt+A from anywhere. You can also press Enter in the elevation or temperature box (Enter in the altitudes box starts a new line instead).
6. Focus moves to the **Corrected altitudes** box. Read it line by line with the arrow keys.

If you entered only one altitude, BA also speaks the answer straight away.

### What you will hear and see {#what-you-will-hear-and-see-2}

The results start with a heading line, for example *"Cold temperature correction at 790 ft field elevation, -25 Celsius:"*, then one line per altitude:

- *"Published 3000 ft: corrected 3290 ft (add 290 ft)"*. Fly the corrected altitude.
- *"Published 1010 ft: no correction needed (warm), use 1010 ft"*. When it is not cold enough to matter, the published altitude stands. BA never lowers an altitude on a warm day.
- A line you mistyped is shown as, for example, *"3OOO: not a number"*.

The last line reminds you: *"Advisory only — verify against official sources."* Corrected altitudes are rounded up to the next 10 feet. The correction grows with the height above the airport, so the high initial approach altitudes get the biggest additions.

Error messages are spoken and shown in the results box, and focus returns to the box you need to fix:

- *"Enter a valid field elevation in feet."*
- *"Field elevation must be between minus 2,000 and 15,000 feet"*
- *"Enter a valid temperature in degrees Celsius."*
- *"Temperature must be between minus 90 and plus 60 degrees Celsius"*
- *"Enter at least one published altitude, one per line."*

Type numbers without thousands separators (3000, not 3,000). Either a full stop or a comma works as the decimal point.

<div class="callout warning" role="note" markdown="1">

**Warning:** If you fly corrected altitudes under ATC control, tell the controller, because their radar shows your uncorrected altitude. Remember to set the corrected minimum, not the published one, as your decision or minimum descent altitude.

</div>

Press Escape, or Alt+C for **Close**, to close the window.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Planning Your Flight](planning-your-flight.md)
- [Next chapter: Knowing Where You Are](knowing-where-you-are.md)
- [Guide contents](index.md)

</nav>
