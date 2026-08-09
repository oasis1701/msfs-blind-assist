# VATSIM Announcements (vPilot)

MSFS Blind Assist speaks VATSIM network events reported by vPilot — connections
and disconnections, private messages, on-frequency radio chatter, and SELCAL
alerts — through its own screen-reader announcer. The feature replaces the
standalone `vPilot-to-TTS` tray application: everything that application did (a
vPilot plugin plus a separate tray app speaking over a named pipe) now lives
inside MSFS Blind Assist, sharing its settings, its announcer and its speech
queue instead of running a second copy of all three alongside it.

## What it announces

Five vPilot events, each with its own on/off setting. The wording is carried
over unchanged from the standalone app, so a pilot migrating from it hears the
same phrases:

| Event | What you hear |
| --- | --- |
| Connected to the network | "Connected as *your callsign*" |
| Disconnected from the network | "Disconnected from network" |
| Private message | "Private message from *sender*: *message*" |
| Radio message, on a frequency you're tuned to | "*sender*: *message*" |
| SELCAL alert | "SELCAL alert from *sender*" |

Announcements are queued rather than spoken immediately, so VATSIM chatter never
interrupts something more urgent like a landing callout or a taxi instruction.

## Setting it up

Open **Settings → VATSIM** (the second tab, right after Announcements), tick
**Announce VATSIM events from vPilot**, and press **OK**. Underneath the master
switch is one checkbox per event above — all five are ticked by default, and the
group only becomes interactive once the master switch itself is on. Untick any
event you don't want spoken.

Pressing OK with the switch on installs the plugin into vPilot's `Plugins`
folder — vPilot does not need to be running for a first install to succeed (see
*Installing*, below, for the one case where it does matter). **vPilot only loads
a plugin at startup, so you need to restart vPilot before it takes effect.** The
same install/refresh check runs quietly every time MSFS Blind Assist itself
starts, so an app update that changes the plugin never leaves an old copy
behind.

A read-only status box on the tab always tells you what to do next. It
refreshes when the tab opens, when you tick or untick the master switch, and
after Browse — not after pressing OK, which saves and closes the dialog
immediately, leaving nothing open to refresh. What OK actually did (installed
the plugin, updated it, or found no vPilot at all) is spoken instead, as part
of the regular "Settings saved" confirmation:

- With the master switch off, it leads with *"VATSIM announcements are turned
  off."*
- If vPilot can't be found at all: *"vPilot was not found. Use Browse to select
  your vPilot folder."*
- Once vPilot is found, it names the folder and then says one of *"The plugin is
  not installed. Press OK to install it."*, *"An older plugin is installed.
  Press OK to update it."*, or *"The plugin is installed and up to date."*
- With the switch on and the plugin installed, it adds whether vPilot is
  actually attached right now: *"vPilot is connected."* or *"vPilot is not
  connected. Start vPilot, or restart vPilot if you have just installed the
  plugin."*
- Muted for the session, it adds a reminder: *"Announcements are muted for this
  session. Press ] then Alt+V to unmute."*

It's a normal read-only text box you can Tab to and read with your screen
reader, not a caption you have to hunt for with the review cursor.

If vPilot is installed somewhere MSFS Blind Assist can't find automatically, use
**Browse…** and point it at either your vPilot folder or the `Plugins` folder
inside it — either one works.

Turning the master switch back off stops the announcements immediately but
leaves the plugin DLL sitting in vPilot's `Plugins` folder. That's deliberate:
removing it would fail while vPilot is running, so "off" would otherwise become
a "close vPilot first" chore. Left in place with nobody listening, the plugin
costs vPilot nothing.

## Muting VATSIM chatter

Press **Output mode > Alt+V** to mute or unmute VATSIM announcements for the
rest of the session, without opening Settings — handy on a busy frequency. It
resets to unmuted every time MSFS Blind Assist restarts, so a mute from last
flight can never be silently carried into the next one.

- *"VATSIM announcements muted"* / *"VATSIM announcements unmuted"*
- If the master switch itself is off in Settings: *"VATSIM announcements are
  turned off in Settings."* — never a silent key that appears to do nothing.

Alt+V works even before MSFS Blind Assist has connected to the simulator —
muting chatter has nothing to do with the flight.

## Migrating from vPilot-to-TTS

If you used the standalone `vPilot-to-TTS` tray application before:

- Its vPilot plugin (`vPilot-to-TTS.dll`) is **removed automatically** the next
  time MSFS Blind Assist installs or refreshes its own plugin, so you never hear
  the same event announced twice.
- The old **tray application itself is not touched** — MSFS Blind Assist has no
  way to reach into a separate program. Uninstall it yourself once you've
  switched over.
- **Settings are not migrated.** There are only six of them, and the defaults
  already match what the standalone app shipped with (every event on, unmuted
  at startup), so there is nothing worth carrying across.

The two plugins also talk over differently-named pipes, so nothing breaks even
before you get around to uninstalling the old tray application — see *The pipe
name*, below, for why that matters.

---

## Developer internals

### Layout

| File | Responsibility |
| --- | --- |
| `Services/VPilot/VatsimAnnouncementFormatter.cs` | **Pure.** `(type, from, message, options) → string?` — per-event gating and all wording. The unit-tested core. |
| `Services/VPilot/VatsimAnnouncementService.cs` | Owns the pipe server, the master-switch lifecycle and the session mute; marshals to the UI thread and calls `announcer.AnnounceWithQueue`. |
| `Services/VPilot/VPilotPipeServer.cs` | Background listener thread on the named pipe; one client at a time, auto-relistens after a disconnect. |
| `Services/VPilot/VPilotPluginInstaller.cs` | Finds vPilot's `Plugins` folder and copies the DLL into it; never throws. |
| `Services/VPilot/VatsimStatus.cs` | The status snapshot and the text shown in the settings tab. |
| `Forms/Settings/VatsimPanel.cs` | The VATSIM settings tab. |
| `plugins/MSFSBlindAssist.VPilotPlugin/Plugin.cs` | `IPlugin` entry point vPilot loads; subscribes the five `IBroker` events. |
| `plugins/MSFSBlindAssist.VPilotPlugin/PipeClient.cs` | Background sender thread with a bounded queue — the never-blocks half of the design. |
| `plugins/MSFSBlindAssist.VPilotPlugin/PluginLog.cs` | The plugin's own log — the one exception to "every write goes through `Log`". |
| `plugins/MSFSBlindAssist.VPilotPlugin/VPilotWireFormat.cs` | The wire protocol, linked (not copied) into both assemblies. |

### Two processes, one pipe

vPilot loads the plugin **in-process**, on .NET Framework 4.8 — not a choice,
vPilot only loads net48 assemblies — so the plugin can never be folded into the
main app the way every other integration in this codebase is. The two halves
talk over one named pipe:

```
vPilot process
  IBroker event -> Plugin -> bounded queue -> sender thread -> named pipe "MSFSBlindAssist.vPilot"

MSFS Blind Assist process
  VPilotPipeServer (listener thread)
    -> marshal to UI thread
    -> VatsimAnnouncementService   (master switch, session mute, lifecycle)
    -> VatsimAnnouncementFormatter (pure: per-event toggle, wording)
    -> announcer.AnnounceWithQueue
```

The service/formatter split is deliberate. `VatsimAnnouncementService` decides
whether the feature is running *at all* — the master switch and the session
mute — because both are lifecycle state it already owns. `VatsimAnnouncementFormatter`
decides whether *this* event type is wanted and what it says, kept pure so the
wording can be tested exhaustively with no pipe, no settings file and no screen
reader involved.

VATSIM text is always spoken with `announcer.AnnounceWithQueue`, never
`AnnounceImmediate` — chatter must not interrupt a landing callout or a taxi
instruction. One consequence of that choice is accepted rather than "fixed":
`announcer.Suppressed`, the app-wide grace window that silently drops queued
announcements for a few seconds right after an aircraft is detected, drops
VATSIM messages along with everything else during that window. Switching VATSIM
to `AnnounceImmediate` to dodge it would violate the no-interrupt rule above, so
it stays as it is.

The plugin project (`plugins/MSFSBlindAssist.VPilotPlugin/`) is net48/AnyCPU and
joins `MSFSBlindAssist.sln` with a `Debug|x64 → Debug|Any CPU` configuration
mapping — the same trick `MSFSBlindAssistUpdater` already uses for its own
csproj. The main app's `ProjectReference` to it is build-order only
(`ReferenceOutputAssembly="false"`, `SkipGetTargetFrameworkProperties="true"`,
`Private="false"` — a net10 project cannot reference a net48 assembly, and
nothing from it is linked); a separate `Copy` target lands the built DLL in
`$(OutDir)vPilotPlugin\` after every build, and that is what
`VPilotPluginInstaller` actually reads at run time. Landing it inside
`$(OutDir)` needed no release-workflow change — the release zip already
archives the whole output folder.

### The shared queue has a depth cap

`AnnounceWithQueue` is also what `MainForm`'s ECAM messages use, so VATSIM
chatter and ECAM callouts share one queue, drained one entry every
`QUEUE_INTERVAL_MS` (900 ms). Left unbounded, a run of backed-up radio
transmissions on a busy frequency would delay the next ECAM failure callout by
several seconds — behind a queue a pilot who cannot see the ECAM has no other
way to notice. `VatsimAnnouncementService.OnMessageReceived` guards against
this directly: immediately before queuing a message it reads
`ScreenReaderAnnouncer.QueuedAnnouncementCount`, and once that is already at or
past `MaxSharedQueueDepth` (5 — about 4.5 s of backlog) it drops the message
and logs the drop at Debug instead of queuing it.

That read happens **on the UI thread, inside the `BeginInvoke` marshal** — not
on the pipe listener thread where the message arrives. `BeginInvoke` only
*posts*: between a listener-thread read and the enqueue that follows it sits an
unbounded window of posted-but-not-yet-run delegates. With the UI thread busy —
a SimVar batch drain, a Coherent scrape, a panel rebuild — a burst of
transmissions would each read a depth that had not yet absorbed its
predecessors, every one would pass the gate, and they would all enqueue
together: precisely the head-of-line block the cap exists to prevent. Checking
inside the marshal costs one wasted marshal per dropped message, which is
nothing at chatter rates, and makes the cap exact instead of approximate.

Dropping, not queuing without bound, is the deliberate fix here — and the
*only* correct one. The obvious-looking alternative, switching VATSIM to
`AnnounceImmediate` so it never sits in the queue at all, is exactly what the
no-interrupt rule under *Two processes, one pipe*, above, forbids: VATSIM text
would then interrupt a landing callout or a taxi instruction, which is worse
than a dropped radio call. VATSIM text is chatter where the newest
transmission is what matters most, and the plugin's own `PipeClient.Send`
already drops its oldest queued message under a comparable backlog for the
same reason (see *Why the plugin's sender never blocks*, below) — the depth
cap is the same policy applied on the receiving end of the pipe instead of the
sending end.

### The wire format, and why it's a linked file

The line protocol is `type \t from \t message`, one line per event. Backslash,
tab, CR and LF are escaped on send and unescaped on receive so a multi-line
private message can never desync the reader.

`VPilotWireFormat.cs` lives once, physically, under
`plugins/MSFSBlindAssist.VPilotPlugin/`, and is pulled into the main app by a
`<Compile Include=... Link=...>` in `MSFSBlindAssist.csproj` — **linked, never
copied.** It's the same pattern `tools/PMDGDispatchTester` uses for
`PMDGNG3DataStruct.cs`, and for the same reason: the two ends of a wire protocol
must not be able to drift apart, since a change to the encoding on only one side
would silently break every message crossing the pipe.

### The pipe name

The pipe is `MSFSBlindAssist.vPilot`, not the standalone app's `vPilot-to-TTS`,
and the rename is load-bearing, not cosmetic: `NamedPipeServerStream` defaults
to **one server instance per name**. If a user still has the old standalone
tray app running, it owns the name `vPilot-to-TTS` — a second server MSFS Blind
Assist tried to open on that same name would simply fail to start. Reusing the
old name was never an option; the rename is what lets both apps coexist on a
machine that hasn't been fully migrated yet.

### Why the plugin's sender never blocks

The original `vPilot-to-TTS` plugin called `pipe.Connect(500)` directly inside
its vPilot event handlers — with nothing listening, that's a 500 ms stall
inside vPilot *per event*. The ported plugin's event handlers only ever enqueue
(`PipeClient.Send`) and return immediately; a single background sender thread
owns the pipe, connects with a 100 ms timeout, and — after three failed sends in
a row — backs off to 5 seconds and clears the queued backlog rather than
replaying stale chatter into a connection that only just came back. The queue
itself is bounded to about 200 messages and drops the oldest one first.

This is exactly what makes "leave the plugin installed with the feature
switched off" free: with nobody listening, the sender thread just idles in the
background instead of taxing vPilot's own event loop.

### Why the plugin logs where it does

The plugin writes its own log, `%APPDATA%\MSFSBlindAssist\logs\vpilot-plugin.log`
— the **one** documented exception to "every log write goes through
`Utils/Logging/Log`". It runs inside vPilot's process on .NET Framework and
cannot reference the main app's logger (different process, different target
framework), so `PluginLog` computes the path itself. It still resolves into the
same canonical logs folder as everything else, so "send me your logs" stays one
folder even for this one exception.

Unlike the original `vPilot-to-TTS` plugin, it also does **not** truncate the
log on every load. The original wiped its log at the start of every vPilot
session; this one appends instead (with a simple 1 MB-then-roll-to-`.old`
limit), so a support request can actually contain the session it's about.

### Installing: `Installed`, `AlreadyCurrent`, `Locked`, `VPilotNotFound`, `Failed`

`VPilotPluginInstaller.Install()` never throws — every method in it writes
outside MSFS Blind Assist's own tree, so a failure has to degrade to a status
the settings dialog can explain rather than an exception thrown while pressing
OK. `VatsimPanel.Validate` always returns true for exactly this reason: a
vPilot that can't be found must not block saving the rest of Settings.

`Locked` and a first `Installed` are **different situations that must not be
collapsed into one message**, because they ask the pilot to do two different
things:

- **`Locked`** — an *older* DLL is already installed and vPilot currently has
  it open. Only an *existing* file can be locked, and vPilot holds its plugins
  open for as long as it runs. Spoken as *"vPilot is running with an older
  plugin. Close vPilot and re-open Settings to update it."*
- **A first `Installed`** — there is nothing there yet, so nothing can be
  locked. The copy always succeeds, even with vPilot running; vPilot just won't
  load it until it next restarts. Spoken as *"vPilot plugin installed. Restart
  vPilot to load it."*

The other statuses: `AlreadyCurrent` (the installed DLL already matches the
shipped one — same length and `LastWriteTimeUtc`, which `File.Copy` preserves,
so this is an exact match rather than a heuristic) says nothing extra, since
"Settings saved" already covers it; `VPilotNotFound` (*"vPilot not found. Use
Browse in the VATSIM settings to locate it."*) means none of the three lookup
routes below found a vPilot folder at all; `Failed` covers everything else —
permissions, a missing shipped file, a disk error — reported as *"The vPilot
plugin could not be installed. See the log for details."*

Removing the legacy `vPilot-to-TTS.dll` (see *Migrating from vPilot-to-TTS*,
above) happens inside the same `Install()` call and is itself best-effort: if
the old tray app has it locked, the removal is silently skipped and retried on
the next install.

### Finding vPilot: the Plugins-folder resolution order

`VPilotPluginInstaller.FindPluginsFolder()` tries three candidates in order,
stopping at the first that resolves:

1. **The settings override** (`VPilotPluginsFolderOverride`) — set by pressing
   **Browse…** in the VATSIM tab.
2. **vPilot's own registry key**, `HKCU\Software\vPilot\Install_Dir`.
3. **The default install location**, `%LOCALAPPDATA%\vPilot`.

A candidate only counts if a `Plugins` subfolder actually exists under it — or
if the candidate path *is itself* already named `Plugins` and exists, which is
what lets Browse accept either the vPilot install folder or the `Plugins`
folder directly without the pilot needing to know which one is wanted. If
vPilot is present but has genuinely never loaded a plugin before (no `Plugins`
folder yet), `Install()` creates one.

The original `vPilot-to-TTS` tray app only ever read the registry key and gave
up silently if it wasn't there. The extra candidates, plus Browse as a last
resort, cover a relocated or portable vPilot install without a support
round-trip.

## Not implemented, and why

`IBroker` (the vPilot plugin API) exposes far more than the five events this
feature uses: `BroadcastMessageReceived`, `MetarReceived`, `AtisReceived`,
`ControllerAdded`/`ControllerDeleted`/`ControllerFrequencyChanged`/
`ControllerLocationChanged`, and the `AircraftAdded`/`AircraftUpdated`/
`AircraftDeleted` traffic events are all deliberately out of scope — the goal
was exact parity with what the standalone `vPilot-to-TTS` plugin already did,
not a superset. Broadcast messages are the strongest future candidate: vPilot
shows them in an on-screen window a blind pilot cannot easily catch.

Nothing that **sends** to VATSIM is implemented either: `SendPrivateMessage`,
`SendRadioMessage`, `SetPtt`, `SquawkIdent`, `SetModeC`, `RequestMetar` and
`RequestAtis` are all part of `IBroker` and all unused. This integration only
listens.

There are also no separate VATSIM-specific settings for run-at-Windows-startup,
a startup announcement, or a choice of speech engine — MSFS Blind Assist already
has its own version of all three, and the VATSIM feature simply uses them
instead of adding a second, competing set.

## Third-party

`RossCarlson.Vatsim.Vpilot.Plugins.dll` is Ross Carlson's vPilot plugin API
assembly. It is vendored under `plugins/MSFSBlindAssist.VPilotPlugin/lib/` and
referenced with `<Private>False</Private>` — build-time only. vPilot supplies
its own copy at runtime, and keeping ours out of the build output means it can
never be copied into vPilot's `Plugins` folder and shadow vPilot's real one.

vPilot itself is Ross Carlson's free VATSIM pilot client:
<https://vpilot.rosscarlson.dev/>.
