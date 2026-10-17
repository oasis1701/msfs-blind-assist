# Volanta live map: full text of the rules

The one-line form of each rule is in `.claude/rules/volanta.md`. Background: docs/volanta.md.

## VOL-1 (retired: the window now reads Volanta's public web map, which downloads from Volanta's servers)

Retired 2026-10-17, when the window moved from a debugging port on the pilot's Volanta app to Volanta's public web
map (the owner's and the PR author's choice). The rule said never to request anything from Volanta's servers
(`api.volanta.app` and `webassets.volanta.app` refuse plain clients with a Cloudflare 403) and to read only what
the Volanta app's own page had downloaded. The web map is Volanta's own page in a real browser engine, logged out
or logged in, so its requests are a browser's and nothing is worked around; but they are requests the app now
causes, about 415 MB an hour while the window is in the foreground ([VOL-12]).

## VOL-2

Never present the last snapshot as current while Volanta's data is standing still: say why. Where (the owner's
choice, 2026-10-16): in a line at the top of each list, Traffic, Friends and ATC, above the rows last received,
while the web map's page is loading or loading again, while it could not start (no WebView2 runtime), when nothing
at all has come for 30 seconds since the page came up or was last live again (Volanta's site busy, or a check the
hidden page cannot show), and when no new Volanta users' file has come for 30 seconds. Its key starts with
`VolantaListOrder.StatusKeyPrefix`, so it stays first even while the reader is in the list ([VOL-9]). It replaced
a spoken notice: the owner wants fewer announcements here, and the spoken notice left an empty list behind.

"Since" counts from `VolantaLinkSnapshot.ConnectedAt`: when the page came up, or was last live again after a
pause ([VOL-12]). The snapshot keeps the files of before a pause or a reload, so counting any file ever received
would never call a silent page silent, and "nothing new for 30 seconds" counts from `ConnectedAt` when that is
later than the last file, so a pause is not called stale before anything is overdue (review of PR 311,
2026-10-16). One reason, worked out once (`VolantaLiveMapForm.StandstillOf`), feeds the list's line, the status
boxes and the Connection tab: worked out three times, they had drifted apart.

The first version read the Volanta app's own map, which stops downloading while its window is minimized or
hidden (measured: no download for 11 minutes while minimized), so its line also said "minimized"; the web map
has no such state.

## VOL-3 (retired: the Volanta app is no longer started, restarted or closed)

Retired 2026-10-17 with the debugging port ([VOL-1]). The rule made a restart of the Volanta app two steps (a
force-close of Volanta and its sim plugins, then a start with the debugging switch), asked first, because Volanta
runs one copy at a time and its "show in tray" setting makes its close button only hide it. The web map needs
neither the Volanta app nor a restart.

## VOL-4

The pilot's own flight is in Volanta's file, with a position as old as anyone's (113 s at a measured snapshot,
which read as traffic 15 nm behind at 443 kt). `VolantaTrafficCalculator.IsOwnFlight` leaves it out by the
logged-in Volanta user's id (the user id on the pilot's own entry; a user has one live flight), or by Volanta's
flight id; before either is known, by the entry whose position time and place match Volanta's own last position
report. Never by distance alone: a real aircraft can be that close.

The flight id comes first from the request Volanta's page makes as soon as it is logged in, `GET
/api/v1/Flights/Active` (read in 1.19.2's code, 2026-10-17: it logs "Found active flight {id}" and resumes that
flight), whose `id` is the id of the pilot's entry in the flight file and which carries no login token
(`VolantaFeedParser.ParseActiveFlight`); then from the hub's live status message. Without the active flight, the
pilot's own flight was listed until the hub named it (the owner's test, 2026-10-17). Logged out there is neither,
and the pilot's flight can be listed: the Connection tab says to log in for it.

The user id is read from the NAME of the storage key Volanta keeps per user for its "what's new" notice
(`last-read-whats-new-version:<id>`), checked to be a GUID; never from the login token ([VOL-13]), though the
token carries it. Volanta writes that key for each user who dismisses the notice in that browser, which in a
hidden page is rare, and with two accounts there are two, returned in key order: `VolantaLink.ParseStorageCheck`
trusts an id only when there is exactly one, and an id kept from an earlier check is dropped rather than kept as
possibly another account's.

## VOL-5

The network file gives every pilot `onGround: false`, a height above ground of 0 and a vertical speed of 0,
parked or not (all 1,150 rows, measured). `VolantaFeedParser` infers on the ground below
`VolantaFeedParser.NetworkGroundSpeedKt` and leaves height above ground and vertical speed unknown for network
pilots; only a Volanta user's own flags are trusted.

## VOL-6

The friends list comes from Volanta's own request, `GET /api/v1/Relationships/Friends`, which the web map's page
makes at every logged-in load (the window's opening, the reload after a login): measured 2026-10-17, "Friends
list caught" at each. After that, Volanta's hub keeps the list current: "friend-added", "friend-removed", and
"rich-status-update" for each friend's status (the pilot's own status comes the same way and is ignored). The
list is the logged-in user's: `VolantaLink.LoggedOut` drops it with the login, and no copy is kept on disk (the
first version kept one, because the Volanta app downloaded the list only once per start).

Whether a friend is flying, and where, never comes from the list: it is their entry in Volanta's flight file,
matched by user id, so it is live whichever way the list arrived.

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
looks up the due rows nearest first, 40 per read, in bursts of at most 2 s, then refreshes the tab in view in
place without starting another burst: the next one waits for the next 5-second update. Started from that
refresh, bursts ran back to back, and with all pilots listed (about 3,000 moving aircraft, always some due) the
list was rebuilt every 2 s (review of PR 311, 2026-10-16). Until a row's place is known, its line simply has
no "Location:".

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

## VOL-9

Found while looking into silent checkboxes (2026-10-15; the silence itself was [VOL-10]): a filter checkbox
refreshes the list at once, and the list was rewritten row by row, which with a big list blocked the window for
seconds. NVDA waits on a window that does not answer, and its log showed freeze recoveries in that minute.

Measured with a WinForms ListBox of traffic-length lines (a filter change: every text new, a fifth of the rows
gone):

| Rows | Rewritten in place, scrollbar width measured | In place, fixed width | Rebuilt in one go, fixed width |
| --- | --- | --- | --- |
| 200 | 88 ms | 103 ms | 11 ms |
| 800 | 591 ms | 306 ms | 32 ms |
| 3,000 | 7,820 ms | 1,041 ms | 125 ms |

With `HorizontalScrollbar` on and no `HorizontalExtent`, the ListBox measures each row's text as it is
rewritten and, when the widest row goes, every row again: the cost grows with the square of the rows. "All
pilots across Volanta" is about 3,000 rows, so each 5-second update would have blocked the window for 8
seconds. So the traffic, friends and ATC lists have a fixed width (`ListScrollWidthPx`), and:

- A list nobody is in (the reader is on a checkbox, a filter, the details, or in the sim) is rebuilt in one
  go: `Items.Clear` and `AddRange`, the selection put back by key. Nobody hears it.
- In the list the reader is in, while its order holds, `VolantaListOrder.PlanHeldUpdate` removes the rows gone
  (highest index first), inserts a new status line ([VOL-2]) at the top, adds the other new rows at the end, and
  rewrites only the rows within `InPlaceWindowRows` (150) of the reader's row. A row farther away keeps its last text, at most one update old once the reader
  comes within 150 rows of it; jumping to it (End, a first letter) reads it as it was, and it re-reads at the
  next update.
- Re-sorting the list the reader is in (F5) rewrites every row in place only up to `RebuildAboveRows` (300)
  rows; a bigger list is rebuilt in one go like a list nobody is in, its selection put back by key, since every
  row moves anyway (in place, 3,000 rows took a second; review of PR 311, 2026-10-16). The short details lists
  are rewritten in place.
- Only the tab in view updates every 5 seconds; another tab is brought up to date when it is switched to, and
  F5 updates them all. With all pilots listed, the Traffic list alone cost about 125 ms each update while the
  pilot read another tab.

Measured with these: 3,000 rows take about 220 ms either way, 800 rows 22 ms rebuilt and 132 ms held.

## VOL-10

The owner reported (2026-10-15) that NVDA said nothing when a checkbox in the window was ticked or unticked with
Space, on the Traffic and the Friends tab alike, though it read "check box checked" on focus.

WinForms draws its own checkboxes (FlatStyle.Standard, the default) and tells screen readers about a change itself:
`CheckBox.NotifyAccessibilityStateChanged` calls `Control.AccessibilityNotifyClients(StateChange)`, which sends
Windows' EVENT_OBJECT_STATECHANGE. Decompiled from the installed runtimes, that call's check is:

| WinForms | Check before sending the notice |
| --- | --- |
| 8.0.32 | `IsHandleCreated && AccessibleObject.CanNotifyClients` |
| 9.0.21 | `IsHandleCreated && !LocalAppContextSwitches.NoClientNotifications` |
| 10.0.11, 10.0.13 | `IsHandleCreated && LocalAppContextSwitches.NoClientNotifications` |

The switch `Switch.System.Windows.Forms.AccessibleObject.NoClientNotifications` defaults to off, so in .NET 10 no
notice is sent at all. Focus is still announced, because Windows reports focus itself. Measured with an
out-of-process WinEvent hook, as NVDA listens, toggling each kind twice:

- standard style: no state-change event; with the switch set ON, one per toggle (the inversion confirmed);
- FlatStyle.System: one per toggle, sent by Windows' own checkbox (BM_SETCHECK), switch or not;
- standard style raising `NotifyWinEvent` itself on CheckedChanged: one per toggle.

So every checkbox in this window is FlatStyle.System. Setting the switch ON would fix it today and silence every
checkbox the day the check is fixed, so it is never used. Once WinForms sends its own notice again, a system checkbox
sends two; NVDA speaks a state change only when the state differs from what it last read, so it still speaks once.

The same bug affects every standard-style CheckBox and RadioButton in the app (Settings panels, the EFB, the
FMC and GSX settings, Taxi Assist, the weather radar): also seen there, not changed here. The same .NET 10 bug is
why the Connection tab's login button is not heard changing its name, so logging in and out are said aloud.

## VOL-11

Volanta's page polls its files only while it is "visible": in a browser, its map service sets `isTabVisible` from
`visibilitychange` events alone, starting from true (read in 1.19.2's code). Two traps, both measured with a probe
on 2026-10-17:

- The visibility must be the browser's own: the web map is a `CoreWebView2Controller` in a window that is never
  shown, and `VolantaWebMap.SetActive` sets its `IsVisible`. The WinForms WebView2 control passes its visibility on
  only on some changes: hidden under a hidden window it left the page "visible" and polling; shown again later it
  hid the page. The only browser switch is `--disable-features=CalculateNativeWinOcclusion`, so Windows' occlusion
  check never judges the never-shown window hidden; hidden pages are otherwise throttled as usual.
- A page loaded while hidden (a reload after the login window or a log out, a retry) is told nothing, starts out
  believing it is visible, and polled every 6 s until a `visibilitychange` was dispatched. So while paused the
  web map dispatches one after every load and every 5 seconds (`VolantaWebMap.HiddenNudgeScript`).

Paused, the page went "hidden" at once and downloaded nothing; resumed, it fetched the Volanta users within
0.3 s, the network pilots within 1.1 s and ATC within 3 s. About 20 % of one CPU core live, 1 to 5 % paused.

## VOL-12

The web map downloads only while the Volanta window is in the foreground (the owner's choice, 2026-10-17): it runs
only while the window is open, is live the moment the window is activated, and pauses 5 seconds after the window
loses the foreground (`WebMapPauseAfter`, so an Alt+Tab to the sim and straight back costs no catch-up). Live, it
downloads about 415 MB an hour (the Volanta users' file, about 790 KB over the network, every 6 s), plus about
15 MB to load the page. On resume, `VolantaLink.Resumed` moves `VolantaLinkSnapshot.ConnectedAt` to now, so the
last file, old by the length of the pause, is not called stale ([VOL-2]).

## VOL-13

The pilot's Volanta login lives in the web map's browser profile (`%LOCALAPPDATA%\MSFSBlindAssist\VolantaWebMap`).
The app reads storage key NAMES only: which per-user "what's new" keys exist ([VOL-4]), and whether Volanta's
`authToken` key exists (logged in, and the login window's check that closes it). Never a stored value, and never
the login token, which is the pilot's credential; nor a response that carries it (Volanta's session refresh).

Logging out does what Volanta's own logout does (read in 1.19.2's code: it clears its stored session in the
browser and sends no request): `VolantaWebMap.LogOutAsync` clears the profile's site storage and cookies, the
link forgets the friends list and the pilot's own flight (`VolantaLink.LoggedOut`), and the map reloads logged
out.

## VOL-14

The page asks for the Volanta users' file twice in a row, the second straight from its cache, so the web map skips
a file whose key matches the last one taken (`VolantaWebMap.BodyKey`: its length and its first and last 256
characters), to parse 3 MB once. The keys kept are forgotten at every page load and log out: measured 2026-10-17,
after a log out and a new login the friends list came again, identical, and was thrown away as the cached second
copy, so the Friends tab stayed empty.
