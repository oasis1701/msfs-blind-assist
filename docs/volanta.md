# Volanta live map

Extras, Volanta (or `]` then Shift+O, `HotkeyAction.ShowVolanta`, which works with the sim not connected) opens
the **Volanta Live Map** window: Volanta's live map as text. A pilot who cannot see
Volanta's map hears the same traffic: the Volanta users and online network pilots around them, their Volanta
friends, and the online ATC. The window reads Volanta's public web map (`https://fly.volanta.app/map`) in a browser
the pilot never sees, so the Volanta app need not run: most blind pilots keep it in the notification area to log
their flights, where its own map downloads nothing.

## What the pilot gets

- **Traffic tab**: every aircraft on Volanta's map around the pilot, nearest first, one line each: callsign,
  clock position, distance, altitude above or below (or "on the ground"), climbing or descending, speed, type,
  who flies it (a Volanta user, and the network they also fly on; or the network pilot), the route and, once
  looked up from the offline location data Output C reads, where it is: ". Location: Pike Lake, Wisconsin,
  United States", or "Location: over Lake Michigan" over water, never C's distance or coast ([VOL-8]). Filters:
  distance (10 to 1,000 nm, then a warning row, then "All pilots across Volanta", about 3,000 rows; landing on
  the warning row keeps the distance chosen before), all / airborne / on the ground, which sources (Volanta
  users, VATSIM, IVAO, PilotEdge, APOC, and other networks for any network Volanta's file names that the
  others do not) and a search over callsign, flight number, user, type, registration
  and airports. A search covers every distance, whatever the distance filter says (the owner's choice,
  2026-10-16); sources and airborne or on the ground still apply. The ATC tab keeps its own distances, 10 nm
  to any. The details box below the list shows everything known about the selected aircraft. Right after it,
  "Report location to nearest major city": ticked, the location says how far and which way the aircraft is
  from the nearest major city instead ("92 miles west of Wichita, Kansas, United States"), a major city being
  the pilot's own Minimum Population for Major Cities within their Major Cities Range (GeoNames tab), never a
  district of a city. The checkbox text names both values. Unticked at every opening.
- **Friends tab** (after Traffic, logged in): the pilot's Volanta friends. Flying friends first, nearest first,
  with the same placing as a traffic line; then friends online in Volanta, with their status in words ("cruising
  over United States", "taxiing to gate B12 at KGRB"); then offline. The details list adds everything about a
  flying friend's flight. Its own "Report location to nearest major city" checkbox, after the details, works as
  the Traffic tab's, independently ([VOL-6]).
- **ATC tab**: online VATSIM and IVAO stations, nearest first, with frequency; the details box adds the
  controller, the airport, when they came online and the ATIS text. Its search (the owner's request,
  2026-10-16) looks up an airport's ATC wherever it is: it matches the callsign, the airport Volanta gives the
  station (a center too, LFFF_CTR at LFPG), the controller and the frequency, at any distance
  (`VolantaAtcSearch`). A US code also finds the stations VATSIM names without its K: "KATL" finds ATL_TWR.
  A station covering the airport from above under another airport's code is not found.
- **Connection tab**: whether the web map is up, whether the pilot is logged in to Volanta, what it downloaded in
  the last minute, what has been received and when, and one button: "Log in to Volanta", or once logged in, "Log
  out of Volanta" (the owner's request, 2026-10-17).

In each list tab the list comes first in the tab order, then the selected item's details, the major-city
checkbox (Traffic and Friends, the owner's choice, 2026-10-16), then the status and the filters. The tabs use `AccessibleTabControl`, as the EFB does, so NVDA names only the tab.

The tab in view updates on its own every 5 seconds (another tab is brought up to date when it is switched to,
and F5 updates them all), in place, as the Sim Performance window does: only changed rows
are rewritten and the selection follows its row by key, so the reader's cursor never jumps, and the row the
pilot is on re-reads when it changes (the owner's choice: an earlier version kept that row's text until the
pilot moved). While the pilot is in a list its order holds (new rows join at the end, `VolantaListOrder`), so
the down arrow always reaches the next row. A list the pilot is not in is rebuilt in one go instead, and in the
list they are in only the rows within 150 of theirs are rewritten: rewriting every row of a big list blocked the
window for seconds ([VOL-9]). The details list below, one field per row, is live. Every checkbox is drawn by
Windows (FlatStyle.System), the only kind whose tick NVDA hears under .NET 10 ([VOL-10]). F5 (or Re-sort
now) re-sorts at once and reads the tab's status (a list of more than 300 rows is rebuilt in one go then, its
selection kept); a filter change re-sorts too. Connected and lost are each spoken once, queued, as are
"Logged in to Volanta." when the login window closes itself and "Logged out of Volanta." (the button's new name
is not heard by itself, [VOL-10]); nothing else is (the owner wants fewer announcements here).
While Volanta's data cannot be pulled, each list instead has a line at its top saying why: the web map loading,
or unable to start, nothing at all for 30 seconds since it came up or was last live again, or nothing new for 30
seconds. The rows last received stay below it, and it stays first while the pilot is in the list ([VOL-2]). The
status boxes and the Connection tab give the same reason (`StandstillOf`).

## Requirements

The Microsoft Edge WebView2 runtime (part of Windows 11; the EFB windows use it too) and an internet connection.
Logging in to Volanta is optional: logged out, the window has the traffic and ATC; logged in, also the friends
list and the pilot's own flight, which is then never listed as traffic ([VOL-4]).

## How it works

1. `VolantaWebMap` opens `https://fly.volanta.app/map` in a `CoreWebView2Controller` hosted in a window that is
   never shown, on its own browser profile (`%LOCALAPPDATA%\MSFSBlindAssist\VolantaWebMap`, which keeps the login).
   The page's ad, analytics and session-recording services (Facebook, Google Ads, Reddit, Klaviyo, PostHog,
   Sentry, Mapbox's telemetry) are blocked (`VolantaWebMap.IsTracker`); blocking them changed nothing the map
   downloads.
2. Through WebView2's own DevTools access it watches the page's network: each time the page finishes downloading
   one of Volanta's traffic files, its friends list or the pilot's active flight (`VolantaLink.FeedKindForUrl`),
   it asks the page for that response's body and hands it to `VolantaLink`, which parses it (`VolantaFeedParser`).
   It also hands over the page's SignalR hub frames (the pilot's own flight and position, friend changes) and,
   every 5 seconds, a storage check that reads key NAMES only: who is logged in and whether a login is stored
   ([VOL-13]). The page asks for the Volanta users' file twice in a row, the second from its cache; that copy is
   parsed once ([VOL-14]).
3. **Only in the foreground** ([VOL-12]): the browser runs only while the window is open, and is live only while
   the window is in the foreground; 5 seconds after the pilot switches away it pauses, and it is live again the
   moment the window comes back. Pausing sets the browser's own visibility, which Volanta's page follows: it polls
   only while "visible" ([VOL-11]). On resume it catches up within about 3 seconds.
4. **Login**: "Log in to Volanta" opens Volanta's login page (`https://fly.volanta.app/auth/login`) in a window of
   its own (`VolantaWebMapLoginForm`) on the same profile. The pilot logs in on Volanta's page; the window checks
   every second for Volanta's stored login (its key's NAME only) and closes itself once it is there, the Volanta
   window comes back and says "Logged in to Volanta.", and the map page reloads with the login. "Log out of
   Volanta" does what Volanta's own logout does: clears the profile's site storage and cookies, with no request
   ([VOL-13]).

The window is its own top-level window, never owned by the main window, so the pilot can Alt+Tab between the
two (the TCAS window's pattern).

The first version (2026-10-15 to 17) read the Volanta app's own map instead, over a Chromium debugging port it
started Volanta with, force-closing a running Volanta first. It was replaced, with the PR author, because that map
downloads nothing while Volanta sits in the notification area or is minimized, and because the port let any
program on the PC control Volanta ([VOL-1], [VOL-3], both retired).

## Measured (2026-10-15 to 17)

| File | What | Every | Over the network | Unpacked |
| --- | --- | --- | --- | --- |
| `volanta-flight-positions-slim.json` | Every Volanta user on the map (about 1,800) | 6 s | about 790 KB | about 3 MB |
| `network-flight-positions.json` | VATSIM, IVAO, PilotEdge and APOC pilots merged (about 1,150) | 16 s | about 240 KB | about 1.7 MB |
| `network-controllers.json` | Online VATSIM and IVAO ATC (about 200) | 60 s | about 50 KB | about 250 KB |

- The web map, live: about 415 MB an hour, plus about 15 MB to load the page; about 20 % of one CPU core and
  1 GB of memory across its browser processes. Paused: nothing downloaded, 1 to 5 % of a core. Logged out and
  logged in poll at the same pace.
- A Volanta user who also flies on a network is in the Volanta file, with `multiplayerNetwork`, and is left out
  of the network file, so each pilot is drawn once.
- **Positions are old.** At a snapshot, half of the airborne positions were under 11 s old, one in ten over
  100 s, the oldest 18 minutes. The pilot's own entry was 113 s old, so it read as traffic 15 nm behind at
  443 kt. `VolantaTrafficCalculator` moves airborne traffic on along heading and ground speed for at most 3
  minutes, and leaves the pilot's own flight out by its id ([VOL-4]).
- **The network file has no ground state.** All 1,150 rows said `onGround: false`, `altitudeAgl: 0`,
  `verticalSpeed: 0` and `transponder: null`, parked or not. On the ground is inferred below 40 kt; vertical
  speed and height above ground are left unknown.
- Altitudes are feet (36000 at FL360), speeds knots, vertical speed feet per minute, headings true.

## Testing

Pure logic (parser, calculator, filter, formatter, the link's file matching and storage check, the web map's
tracker blocking and download summary) has characterization tests in `tests/MSFSBlindAssist.Tests/Volanta/`, the
formatter under de-DE. The web map itself needs a browser and Volanta's site: the PR's test plan covers it.
