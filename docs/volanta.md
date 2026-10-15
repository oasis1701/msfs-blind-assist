# Volanta live map

Extras, Volanta opens the **Volanta Live Map** window: Volanta's live map as text. A pilot who cannot see
Volanta's map hears the same traffic: the Volanta users and online network pilots around them, and the online
ATC.

## What the pilot gets

- **Traffic tab**: every aircraft on Volanta's map around the pilot, nearest first, one line each: callsign,
  clock position, distance, altitude above or below (or "on the ground"), climbing or descending, speed, type,
  who flies it (a Volanta user, and the network they also fly on; or the network pilot) and the route. Filters:
  distance (10 nm to any), all / airborne / on the ground, altitude band, which sources (Volanta users, VATSIM,
  IVAO, PilotEdge, APOC) and a search over callsign, flight number, user, type, registration and airports. The
  details box below the list shows everything known about the selected aircraft.
- **Friends tab** (after Traffic): the pilot's Volanta friends. Flying friends first, nearest first, with the
  same placing as a traffic line; then friends online in Volanta, with their status in words ("cruising over
  United States", "taxiing to gate B12 at KGRB"); then offline. The details list adds everything about a flying
  friend's flight. A button restarts Volanta to load the list again ([VOL-6]).
- **ATC tab**: online VATSIM and IVAO stations, nearest first, with frequency; the details box adds the
  controller, the airport, when they came online and the ATIS text.
- **Connection tab**: whether Volanta has live map access, what has been received and when, and the button that
  starts or restarts Volanta with access.

In each list tab the list comes first in the tab order, then the selected item's details, then the status and
the filters. The tabs use `AccessibleTabControl`, as the EFB does, so NVDA names only the tab.

Everything updates on its own every 5 seconds, in place, as the Sim Performance window does: only changed rows
are rewritten and the selection follows its row by key, so the reader's cursor never jumps, and the row the
pilot is on re-reads when it changes (the owner's choice: an earlier version kept that row's text until the
pilot moved). While the pilot is in a list its order holds (new rows join at the end, `VolantaListOrder`), so
the down arrow always reaches the next row. The details list below, one field per row, is live. F5 (or Re-sort
now) re-sorts at once and reads the tab's status; a filter change re-sorts too. Connected, lost, and Volanta
minimized or updating again are each spoken once, queued.

## Requirements

Volanta installed, the pilot logged in, and Volanta **started by MSFSBA** (the Connection tab's button), and
kept **open, not minimized**. Behind the sim is fine.

## How it works

Volanta is an Electron app (Chromium inside). Its servers refuse every client but Volanta itself:
`api.volanta.app/api/v1/cdn/*` and `webassets.volanta.app/*` answer a plain request with a Cloudflare 403. So
the window never asks them for anything. Instead:

1. `VolantaLauncher` starts Volanta with `--remote-debugging-port=9223 --remote-debugging-address=127.0.0.1`.
   Nothing in Volanta blocks the switch (no check for it in its code; its Electron fuses do not cover it).
   Volanta runs one copy at a time, so a running Volanta is first asked to close, as its close button does,
   and is never force-quit.
2. `VolantaLink` finds Volanta's main page on that port (the page Volanta serves itself from
   `http://127.0.0.1:17580`), opens its debugger socket and turns on network events.
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

Quitting: with Volanta's "show in tray" setting on, its close button (and Alt+F4) hide it in the notification
area rather than quitting it. A restart therefore escalates: Volanta's own quit over the debugging port (the
same as Quit in its notification-area menu) when it has access, else its window asked to close; if Volanta is
still running a few seconds later, its processes and sim plugins are force-closed ([VOL-3]). The confirmation
says a force-close may happen, and the pilot hears when one did.

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
