# Volanta live map

Extras, Volanta opens the **Volanta Live Map** window: Volanta's live map as text. A pilot who cannot see
Volanta's map hears the same traffic: the Volanta users and online network pilots around them, and the online
ATC.

## What the pilot gets

- **Traffic tab**: every aircraft on Volanta's map around the pilot, nearest first, one line each: callsign,
  clock position, distance, altitude above or below (or "on the ground"), climbing or descending, speed, type,
  who flies it (a Volanta user, and the network they also fly on; or the network pilot), the route and, once
  looked up from the offline location data Output C reads, where it is: ". Location: Pike Lake, Wisconsin,
  United States", or "Location: over Lake Michigan" over water, never C's distance or coast ([VOL-8]). Filters:
  distance (10 to 1,000 nm, then a warning row, then "All pilots across Volanta", about 3,000 rows; landing on
  the warning row keeps the distance chosen before), all / airborne / on the ground, which sources (Volanta
  users, VATSIM, IVAO, PilotEdge, APOC) and a search over callsign, flight number, user, type, registration
  and airports. A search covers every distance, whatever the distance filter says (the owner's choice,
  2026-10-16); sources and airborne or on the ground still apply. The ATC tab keeps its own distances, 10 nm
  to any. The details box below the list shows everything known about the selected aircraft. Right after it,
  "Report location to nearest major city": ticked, the location says how far and which way the aircraft is
  from the nearest major city instead ("92 miles west of Wichita, Kansas, United States"), a major city being
  the pilot's own Minimum Population for Major Cities within their Major Cities Range (GeoNames tab), never a
  district of a city. The checkbox text names both values. Unticked at every opening.
- **Friends tab** (after Traffic): the pilot's Volanta friends. Flying friends first, nearest first, with the
  same placing as a traffic line; then friends online in Volanta, with their status in words ("cruising over
  United States", "taxiing to gate B12 at KGRB"); then offline. The details list adds everything about a flying
  friend's flight. Its own "Report location to nearest major city" checkbox, after the details, works as the
  Traffic tab's, independently. Restarting Volanta from the Connection tab loads the list again ([VOL-6]).
- **ATC tab**: online VATSIM and IVAO stations, nearest first, with frequency; the details box adds the
  controller, the airport, when they came online and the ATIS text. Its search (the owner's request,
  2026-10-16) looks up an airport's ATC wherever it is: it matches the callsign, the airport Volanta gives the
  station (a center too, LFFF_CTR at LFPG), the controller and the frequency, at any distance
  (`VolantaAtcSearch`). A US code also finds the stations VATSIM names without its K: "KATL" finds ATL_TWR.
  A station covering the airport from above under another airport's code is not found.
- **Connection tab**: whether Volanta has live map access, what has been received and when, and the window's one
  button that starts or restarts Volanta with access. It restarts Volanta even when it already has access (after
  asking), since that is also how the friends list is reloaded.

In each list tab the list comes first in the tab order, then the selected item's details, the major-city
checkbox (Traffic and Friends, the owner's choice, 2026-10-16), then the status and the filters. The tabs use `AccessibleTabControl`, as the EFB does, so NVDA names only the tab.

Everything updates on its own every 5 seconds, in place, as the Sim Performance window does: only changed rows
are rewritten and the selection follows its row by key, so the reader's cursor never jumps, and the row the
pilot is on re-reads when it changes (the owner's choice: an earlier version kept that row's text until the
pilot moved). While the pilot is in a list its order holds (new rows join at the end, `VolantaListOrder`), so
the down arrow always reaches the next row. A list the pilot is not in is rebuilt in one go instead, and in the
list they are in only the rows within 150 of theirs are rewritten: rewriting every row of a big list blocked the
window for seconds ([VOL-9]). The details list below, one field per row, is live. Every checkbox is drawn by
Windows (FlatStyle.System), the only kind whose tick NVDA hears under .NET 10 ([VOL-10]). F5 (or Re-sort
now) re-sorts at once and reads the tab's status; a filter change re-sorts too. Connected and lost are each
spoken once, queued, as is a start from the Connection tab that has not connected after a minute (Volanta's
updater can relaunch it without the switch), and nothing else is (the owner wants fewer announcements here).
While Volanta's data cannot be pulled, each list instead has a line at its top saying why: Volanta minimized
("Connected to Volanta, but it is minimized, so this list is not updating"), nothing new for 30 seconds,
nothing at all for 30 seconds since connecting (Volanta's login screen: the line says to log in), Volanta
starting, another program on Volanta's port, or no access.
The rows last received stay below it, and it stays first while the pilot is in the list ([VOL-2]). The owner
found Volanta minimized after Alt+Tabbing away from it just after it started (2026-10-16), before it sent
anything, and the lists stood empty.

## Requirements

Volanta installed, the pilot logged in, and Volanta **started by MSFSBA** (the Connection tab's button), and
kept **open, not minimized**. Behind the sim is fine.

## How it works

Volanta is an Electron app (Chromium inside). Its servers refuse every client but Volanta itself:
`api.volanta.app/api/v1/cdn/*` and `webassets.volanta.app/*` answer a plain request with a Cloudflare 403. So
the window never asks them for anything. Instead:

1. `VolantaLauncher` starts Volanta with `--remote-debugging-port=9223 --remote-debugging-address=127.0.0.1`.
   Nothing in Volanta blocks the switch (no check for it in its code; its Electron fuses do not cover it).
   Volanta runs one copy at a time, so a running Volanta is force-closed first, after the pilot confirms
   ([VOL-3], "Restarting" below). The exe is the running copy's own, read before it is closed; else the
   installer's per-user folder (`%LOCALAPPDATA%\Programs\Volanta`); else the install Windows' uninstall keys
   register (an all-users or moved install: its `DisplayIcon`, since Volanta's installer writes no
   `InstallLocation`). With none found the pilot is told to start Volanta by hand and press the button again,
   which then reads the exe from the running copy.
2. `VolantaLink` finds Volanta's main page on that port (the page Volanta serves itself from its own local web
   server: `http://127.0.0.1:17580` in 1.19.2; any loopback page is taken), opens its debugger socket and turns
   on network events. If the port answers but the browser behind it is not Volanta (another program's debugging
   port on 9223, so Volanta could not open it), the link says so (`VolantaLinkStatus.PortTaken`) and the button
   refuses to restart Volanta until that program is closed.
3. Each time Volanta's map finishes downloading one of its traffic files, the link asks the page for that
   response's body and parses it (`VolantaFeedParser`). It also reads Volanta's SignalR hub frames for the
   pilot's own flight id and Volanta's position report, and every 5 seconds asks Volanta whether its window is
   minimized or hidden, and which user is logged in (from the name of a per-user storage key, never the login
   token), so the pilot's own flight is left out even while the sim is paused and Volanta reports nothing.

Nothing extra reaches Volanta's servers: the link only reads what Volanta downloaded anyway.

Friends: Volanta downloads the friends list once, at startup, so the link catches it only when this app started
Volanta (it then looks for Volanta's page every 150 ms). Volanta's hub then reports friends added and removed and
each friend's status. A saved copy of ids and names (`%APPDATA%\MSFSBlindAssist\volanta\friends.json`) covers a
Volanta started another way. Who is flying always comes from the live flight file, by user id.

Restarting: with Volanta's "show in tray" setting on, its close button (and Alt+F4) hide it in the notification
area rather than quitting it, and in the pilot's tests asking Volanta to quit never worked. A restart is
therefore two steps: force-close Volanta and its sim plugins, then start it with access ([VOL-3]). The
confirmation says Volanta will be force-closed. By hand, the sure quit is Quit in Volanta's notification-area
menu.

The window is its own top-level window, never owned by the main window, so the pilot can Alt+Tab between the
two (the TCAS window's pattern).

## Measured (Volanta 1.19.2, Electron 40, 2026-10-15)

| File | What | Every | Size |
| --- | --- | --- | --- |
| `volanta-flight-positions-slim.json` | Every Volanta user on the map (about 1,800) | 6 s | 1.7 MB |
| `network-flight-positions.json` | VATSIM, IVAO, PilotEdge and APOC pilots merged (about 1,150) | 16 s | 1.1 MB |
| `network-controllers.json` | Online VATSIM and IVAO ATC (about 200) | 60 s (requested twice) | 150 KB |

- A Volanta user who also flies on a network is in the Volanta file, with `multiplayerNetwork`, and is left out
  of the network file, so each pilot is drawn once.
- **Minimized stops it.** Volanta's map only downloads while `currentWindow.isVisible() && !isMinimized()`
  (its `shouldPoll`). Measured: downloads stopped the moment the window was minimized and did not resume for 11
  minutes; behind the sim, not focused, the cadence held for 9 minutes. With Volanta's own Performance mode on,
  minimizing tears its map down entirely. Chromium's `document.visibilityState` stays "visible" throughout, so
  the link asks Volanta's own window object instead. The window also calls traffic stale after 30 seconds
  without a new file, which covers the causes not measured (another Volanta page, Performance mode).
- **Volanta never minimizes itself for losing focus.** Read in Volanta 1.19.2's own code (2026-10-16): no blur,
  idle or Alt+Tab handler minimizes or hides its window. It minimizes itself only when started with
  `--minimized` (its "Launch on login" entry), which this app's launch never passes; it hides itself on close
  with "Minimise Volanta to tray on close" on, and on minimize with Performance mode on. Its "real-time tracking
  off when lost focus" setting pauses only its own smoothing of positions, not the downloads. So behind the sim
  is safe. The pilot did find Volanta minimized after Alt+Tabbing away from it just after it started, and its log
  shows a minimize 6 s after a start: something outside Volanta's code minimized it, cause not yet known.
- **Positions are old.** At a snapshot, half of the airborne positions were under 11 s old, one in ten over
  100 s, the oldest 18 minutes. The pilot's own entry was 113 s old, so it read as traffic 15 nm behind at
  443 kt. `VolantaTrafficCalculator` moves airborne traffic on along heading and ground speed for at most 3
  minutes, and leaves the pilot's own flight out by its id (the `flightId` of Volanta's
  "flight-live-status-update" message is the `id` of the pilot's entry in the Volanta file).
- **The network file has no ground state.** All 1,150 rows said `onGround: false`, `altitudeAgl: 0`,
  `verticalSpeed: 0` and `transponder: null`, parked or not. On the ground is inferred below 40 kt; vertical
  speed and height above ground are left unknown.
- Altitudes are feet (36000 at FL360), speeds knots, vertical speed feet per minute, headings true.

## Testing

Pure logic (parser, calculator, filter, formatter, the link's page and file matching) has characterization tests
in `tests/MSFSBlindAssist.Tests/Volanta/`, the formatter under de-DE. The link and launcher need Volanta
running: the PR's test plan covers them.
