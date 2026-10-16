# Volanta live map: full text of the rules

The one-line form of each rule is in `.claude/rules/volanta.md`. Background: docs/volanta.md.

## VOL-1

Never request anything from Volanta's servers. `api.volanta.app` and `webassets.volanta.app` sit behind
Cloudflare and refuse every client but Volanta itself (a 403 page, measured 2026-10-15), and getting past that
is not ours to do. `VolantaLink` only reads, through the debugger's response bodies, what Volanta's own page has
just downloaded: its three traffic files and, from the API host, nothing but its friends list
(`/api/v1/Relationships/Friends`). It adds no request of its own, so a pilot reading the window costs Volanta
nothing. The owner chose this on 2026-10-15 over having Volanta's page fetch the friends list itself, which would
have meant requests and using the login token inside Volanta.

## VOL-2

Volanta's map stops downloading while its window is minimized or hidden (its own check is "visible and not
minimized"); with its Performance mode on, minimizing removes the map. Measured: no download for 11 minutes
while minimized, full cadence for 9 minutes behind the sim. Chromium still reports the page "visible", so the
link asks Volanta's window object (`VolantaLinkSnapshot.VolantaPaused`), and the window also treats 30 seconds
without a new file as stale. Never present the last snapshot as current while either holds: say why it is
standing still.

## VOL-3

Volanta runs one copy at a time, so the debugging switch only takes effect on a fresh launch: a second launch
just brings the first copy forward. With Volanta's "show in tray" setting on, its close button (and a WM_CLOSE
to its window) does not quit it: it hides it in the notification area, still running, its window no longer
visible (measured 2026-10-15), so its map stops polling ([VOL-2]). Whether Alt+F4 quits also depends on that
setting (the owner's report), so it is never the advice. The one sure quit by hand is the Quit item of Volanta's
notification-area menu ("Show Volanta", "Reset Window Position", "Quit").

A restart is therefore two steps: `VolantaLauncher.ForceCloseAsync` ends Volanta's processes and its sim plugins
(`VolantaPlugin_x64`/`_x86`: Volanta's own quit stops them first, so a force-close must too, or a stale plugin
stays connected to the sim beside the new one), then `VolantaLauncher.StartWithAccess` starts it with the switch.
The owner chose this on 2026-10-15, replacing first an earlier "never kill" rule and then a version that asked
Volanta to quit (its own quit over the port, else its window close) before force-closing: in the pilot's tests
asking never quit Volanta, only the force-close did, so asking only added seconds. The window asks the pilot
first and says Volanta will be force-closed. Only if Windows refuses to end Volanta is the pilot sent to Quit in
its notification-area menu. Volanta's restarts measured on 2026-10-15, the force-closed ones included, kept
tracking the same flight.

## VOL-4

The pilot's own flight is in Volanta's file, with a position as old as anyone's (113 s at a measured snapshot,
which read as traffic 15 nm behind at 443 kt). `VolantaTrafficCalculator.IsOwnFlight` leaves it out by the
logged-in Volanta user's id (the user id on the pilot's own entry; a user has one live flight), or by Volanta's
flight id, which the live status message names; before either is known, by the entry whose position time and
place match Volanta's own last position report. Never by distance alone: a real aircraft can be that close.

The user id matters because Volanta sends no position report and no live status while the sim is paused
(measured 2026-10-15: nothing in 30 s), so without it the pilot's own flight would be listed as traffic. The link
reads it from the NAME of the storage key Volanta keeps per user for its "what's new" notice
(`last-read-whats-new-version:<id>`), checked to be a GUID. Never read Volanta's login token (its `authToken`
storage value) for it, even though the token carries the id: that is the pilot's credential.

## VOL-5

The network file gives every pilot `onGround: false`, a height above ground of 0 and a vertical speed of 0,
parked or not (all 1,150 rows, measured). `VolantaFeedParser` infers on the ground below
`VolantaFeedParser.NetworkGroundSpeedKt` and leaves height above ground and vertical speed unknown for network
pilots; only a Volanta user's own flags are trusted.

## VOL-6

Volanta downloads the pilot's friends list once, when it logs in at startup (`GET /api/v1/Relationships/Friends`):
measured 2026-10-15, its page appeared 1.9 s after launch and the friends request went out 0.6 s after that. A
response body is readable only by a session that enabled network events before the request, so only a link
already attached catches it. Every launch from this app therefore calls `VolantaLink.ExpectLaunch` first, which
makes the link look for Volanta's page every 150 ms for a minute instead of every 3 s.

After that, Volanta's hub keeps the list current: "friend-added", "friend-removed", and "rich-status-update" for
each friend's status (the pilot's own status comes the same way and is ignored). `VolantaFriendsCache` saves the
list's ids and names, never a status, so a Volanta started some other way still shows who the friends are, with
their status "not known yet" until Volanta reports a change. Whether a friend is flying, and where, never comes
from the list: it is their entry in Volanta's flight file, matched by user id, so it is live whichever way the
list arrived. The window's one restart button (Connection tab) restarts Volanta even when it already has access,
after asking, because that is also how the list is caught again; the Friends tab points there.

## VOL-7

The reader's place in a details list (Selected aircraft, friend or station details) must survive the lists'
5-second updates. Measured by the owner on 2026-10-15: NVDA was thrown up the details list on every update while
aircraft moved. The cause: while a list reconciles, a row rewritten under the selection briefly selects
whatever row lands there, and the selection handler showed THAT row's details; when they had fewer lines (an
aircraft on the ground has no altitude or vertical speed, a network pilot no simulator), the details list's place
was clamped up to its last line and stayed there when the right details came back.

So a list's selection handler does nothing while the list reconciles; its details are shown once, after, for the
final selection. And a details list keeps the reader on the same FIELD, not the same line number: each row is
keyed by its field name (`VolantaListOrder.DetailKeys`: the text before ": ", repeated names numbered), so the
reader stays on "Ground speed" when a line above it comes or goes. The owner chose these two fixes and declined a
third, holding the traffic list's order while the reader is in its details.

## VOL-8

Each traffic line ends, once known, with where the aircraft is: ". Location: Pike Lake, Wisconsin, United
States", or over water "Location: over Lake Michigan, near Milwaukee, Wisconsin, United States" (the owner's
choice, 2026-10-15). It is the place Output C would name, without C's distance, direction, coast or nearest
land, composed by `LocationPhrases.TrafficPlace` like every other C wording ([LOC-8]), from the same offline
location data.

A lookup reads the location database, so it never runs on the UI thread, and the list may hold hundreds of
aircraft refreshed every 5 seconds. `OfflineLocationService.GetPlaces` answers many positions in one read (one
connection, the admin names loaded once) and skips the nearest-land search, which at sea reaches 3,000 km;
only the 2 km shore band that tells an island or coastal airport from open sea is kept. `VolantaPlaceCache`
keeps each flight's place until it has moved 2 nm (every 16 s at 450 knots, once for a parked aircraft), keeps
a lookup that found nothing so it is not retried in place, and forgets flights that left the map. The window
looks up the due rows nearest first, 40 per read, in bursts of at most 2 s, then refreshes the lists in place;
until a row's place is known, its line simply has no "Location:".

**Major-city mode** (2026-10-15). The Traffic and Friends tabs each have "Report location to nearest major
city", unticked at every opening and not saved (the owner's choice). Ticked, the line says how far and which
way the aircraft is from the nearest major city: "Location: 92 miles west of Wichita, Kansas, United States",
over water "over Lake Michigan, 23 miles east of Milwaukee, …", the city alone within a mile or kilometre of it,
and the state and country (or the water alone) with no major city in range. The distance is said because the
nearest major city can be far: measured on the real data at the default 50,000, central Kansas is 92 nm from
Wichita, rural Wisconsin 76 nm from Eau Claire. `LocationPhrases.TrafficCityPlace` composes it.

A major city is the pilot's own GeoNames tab: at least the Minimum Population for Major Cities, within the
Major Cities Range (capped at 310 miles or 500 km by `LocationQuerySettings.From`), as Location Info's Major
Cities list reads them. The checkbox text names both values, so the pilot hears what it follows. GeoNames'
sections of a city (PPLX) are never one: they carry their own populations, so at 50,000 Manhattan read "East
Village" and JFK "South Ozone Park"; without them, "New York City" and "Queens" (owner, 2026-10-15). A suburb
that is a city of its own above the threshold still counts (Feltham at Heathrow, Des Plaines at O'Hare at
50,000; London and Chicago at 250,000): that is the pilot's threshold doing its job. Location Info's own Major
Cities list still includes sections.

`OfflineLocationService.GetMajorCities` searches outward (25, 50, 100, 200 km, then the range) and stops at
the first radius holding a city, the nearest of all; measured on the real data, at most 2 ms a position, against
up to 7 ms for one search over the whole range around Frankfurt. It keeps the city's position, not words:
`TrafficCityReport.TextAt` works the distance and direction out again at every update from where the aircraft
is now, so they never lag the 2 nm the lookup waits. Each kind of place has its own `VolantaPlaceCache`, with
one burst of lookups at a time each, so a tab in one mode never starves the other. When the GeoNames tab's
settings change, the window replaces both caches (what they hold was found under the old population, range or
units) and the checkbox text follows; a burst still running fills only the old cache.
