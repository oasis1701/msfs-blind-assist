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
