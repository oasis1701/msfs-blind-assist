# Volanta live map: full text of the rules

The one-line form of each rule is in `.claude/rules/volanta.md`. Background: docs/volanta.md.

## VOL-1

Never request anything from Volanta's servers. `api.volanta.app` and `webassets.volanta.app` sit behind
Cloudflare and refuse every client but Volanta itself (a 403 page, measured 2026-10-15), and getting past that
is not ours to do. `VolantaLink` only reads, through the debugger's response bodies, the files Volanta's own map
page has just downloaded; it adds no request of its own, so a pilot reading the window costs Volanta nothing.

## VOL-2

Volanta's map stops downloading while its window is minimized or hidden (its own check is "visible and not
minimized"); with its Performance mode on, minimizing removes the map. Measured: no download for 11 minutes
while minimized, full cadence for 9 minutes behind the sim. Chromium still reports the page "visible", so the
link asks Volanta's window object (`VolantaLinkSnapshot.VolantaPaused`), and the window also treats 30 seconds
without a new file as stale. Never present the last snapshot as current while either holds: say why it is
standing still.

## VOL-3

Volanta runs one copy at a time, so the debugging switch only takes effect on a fresh launch: a second launch
just brings the first copy forward. `VolantaLauncher` therefore asks the pilot, closes a running Volanta the way
its close button does (`CloseAsync`), waits, and starts it again with the switch. It never force-quits Volanta:
a Volanta hidden in the notification area has no window to close, and the pilot is told to close it there.

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
