# VATSIM and the vPilot plugin — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/vatsim.md`, which Claude Code loads when it reads matching code. Background: [vatsim.md](../vatsim.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## VAT-1

- The pipe name `MSFSBlindAssist.vPilot` must NEVER be changed back to the legacy `vPilot-to-TTS` — `NamedPipeServerStream` defaults to ONE server instance per name, so if a user still runs the old standalone tray app it owns that name and MSFSBA's server cannot start at all. → [vatsim.md](../vatsim.md)

## VAT-2

- The plugin's `Send` must never block vPilot's event thread — it enqueues and returns; a background sender owns the pipe and backs off. The original's `pipe.Connect(500)` on the event thread cost vPilot 500 ms per event whenever nothing was listening, and it is what makes leaving the plugin installed with the feature off free. → [vatsim.md](../vatsim.md)

## VAT-3

- VATSIM text always goes through `AnnounceWithQueue`, NEVER `AnnounceImmediate` — chatter must not interrupt a landing callout or a taxi instruction. This is also why the `announcer.Suppressed` grace window silently dropping VATSIM messages for a few seconds after an aircraft detect is ACCEPTED, not a bug to "fix" by switching to `AnnounceImmediate`. → [vatsim.md](../vatsim.md)

## VAT-4

- VATSIM chatter shares `ScreenReaderAnnouncer`'s speech queue with ECAM messages and must never head-of-line-block one — `VatsimAnnouncementService.OnMessageReceived` drops (never queues) a message once `QueuedAnnouncementCount` reaches `MaxSharedQueueDepth` (5, ~4.5 s of backlog). That depth read must stay INSIDE the `BeginInvoke` marshal, on the UI thread that does the enqueue — never back on the pipe listener thread: `BeginInvoke` only posts, so a burst arriving while the UI thread is busy has every message read a depth that has not yet absorbed its predecessors, all pass the gate, and all enqueue together — the exact block the cap exists to prevent. The fix for that blocking is capping the queue depth, NEVER switching VATSIM to `AnnounceImmediate` — that would violate the invariant above. → [vatsim.md](../vatsim.md)

## VAT-5

- `VPilotWireFormat.cs` is compiled into BOTH the app and the net48 plugin from ONE source via a linked `<Compile>` (never a copy) — the two ends of the wire cannot be allowed to drift. Message newlines and tabs are escaped; the line protocol depends on it. → [vatsim.md](../vatsim.md)

## VAT-6

- The plugin's own log (`%APPDATA%\MSFSBlindAssist\logs\vpilot-plugin.log`) is the ONE exception to "every log write goes through `Utils/Logging/Log`" — it runs in vPilot's process on .NET Framework and cannot reference the app's logger. It still resolves into the canonical logs folder. → [vatsim.md](../vatsim.md)

## VAT-7

- `VPilotPluginInstaller` writes outside MSFSBA's own tree, so every method is best-effort and must NEVER throw — a failed install degrades to a status the settings dialog explains, and `VatsimPanel.Validate` never fails. → [vatsim.md](../vatsim.md)

## VAT-8

- `Locked` applies ONLY to updating a DLL vPilot has already loaded; a FIRST install succeeds even with vPilot running (nothing holds a file that does not exist yet). Never collapse the two messages — they ask the pilot to do different things. → [vatsim.md](../vatsim.md)

## VAT-9

- There is NO vPilot folder Browse button and NO `VPilotPluginsFolderOverride` setting, and adding one back is a regression, not a feature — vPilot has no portable install mode, always installs under `%LOCALAPPDATA%\vPilot` and always writes `HKCU\Software\vPilot\Install_Dir`, and the standalone vPilot-to-TTS ran for years on that one key across dozens of users with no reported location problem. A user-chosen path can only point somewhere vPilot isn't: it buys a way to mis-install plus a setting with no way to clear it. For the same reason `ResolvePluginsFolder` must NOT regain the branch that accepted any folder merely NAMED `Plugins` — that existed only so Browse could take either folder. → [vatsim.md](../vatsim.md)

## VAT-10

- The startup install check announces `Locked`, `Failed`, a first-install `Installed` (`VPilotInstallResult.PluginWasAbsent`), and the one-shot `LegacyRemoved` notice (via the queued announcer, whose timer cannot tick until the message pump runs, so it lands after the form is up) — all four are otherwise invisible or unrepeatable, and the first three mean the pilot is about to fly on a plugin that is not the one shipped, or not loaded at all. An update-refresh `Installed`, `AlreadyCurrent`, and `VPilotNotFound` stay settings-only: the same check runs on every launch, so announcing those would narrate every app update for no action on the pilot's part. → [vatsim.md](../vatsim.md)

## VAT-11

- Turning the master switch off leaves the plugin DLL in place by design — removal fails while vPilot is running, so "off" would become a close-vPilot chore that silently no-ops. → [vatsim.md](../vatsim.md)

## VAT-12

- `HotkeyAction.ToggleVatsimAnnouncements` must stay in `MainForm.OnHotkeyTriggered`'s `offlineActions` set — muting VATSIM chatter has nothing to do with SimConnect, and without it the pilot hears "Not connected to simulator, please wait". → [vatsim.md](../vatsim.md)

## VAT-13

- Status/diagnostic text in a settings panel is a read-only `TextBox`, NEVER a `Label` — a `Label` is not in the tab order, so with a screen reader it has to be hunted for with the review cursor instead of tabbed to. Applies to any new status readout, not just the VATSIM one. → [vatsim.md](../vatsim.md)
