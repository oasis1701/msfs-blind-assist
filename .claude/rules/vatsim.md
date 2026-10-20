---
paths:
  - "MSFSBlindAssist/Services/VPilot/**"
  - "MSFSBlindAssist/Services/VATSIMService.cs"
  - "plugins/**"
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
  - "MSFSBlindAssist/Forms/Settings/VatsimPanel.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Vatsim*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*VPilot*.cs"
  - "MSFSBlindAssist/Services/VatsimPilotDataService.cs"
  - "MSFSBlindAssist/MainForm.MenuHandlers.cs"
  - "MSFSBlindAssist/MSFSBlindAssist.csproj"
---
# VATSIM and the vPilot plugin rules

Loaded when Claude reads matching code. Background: docs/vatsim.md. Full text of each rule: docs/invariants/vatsim.md.

- [VAT-1] Never change the pipe name `MSFSBlindAssist.vPilot` back to the legacy `vPilot-to-TTS`: `NamedPipeServerStream` defaults to one server per name, so an old tray app still running would stop MSFSBA's server starting at all. Full: docs/invariants/vatsim.md#vat-1
- [VAT-2] The plugin's `Send` must never block vPilot's event thread: it enqueues and returns, and a background sender owns the pipe and backs off. Full: docs/invariants/vatsim.md#vat-2
- [VAT-3] VATSIM text always goes through `AnnounceWithQueue`, never `AnnounceImmediate`, so chatter never interrupts a landing callout or taxi instruction; messages lost in the `announcer.Suppressed` grace window are accepted, not a bug. Full: docs/invariants/vatsim.md#vat-3
- [VAT-4] VATSIM chatter must never head-of-line-block ECAM speech: `VatsimAnnouncementService.OnMessageReceived` drops a message once `QueuedAnnouncementCount` reaches `MaxSharedQueueDepth` (5), reading the depth inside the `BeginInvoke` marshal, never on the pipe thread. Full: docs/invariants/vatsim.md#vat-4
- [VAT-5] `VPilotWireFormat.cs` is compiled into both the app and the net48 plugin from one source via a linked `<Compile>`, never a copy; message newlines and tabs stay escaped, since the line protocol depends on it. Full: docs/invariants/vatsim.md#vat-5
- [VAT-6] The plugin's own log (`%APPDATA%\MSFSBlindAssist\logs\vpilot-plugin.log`) is an exception to writing through `Utils/Logging/Log` (see [CORE-15]): it runs in vPilot's .NET Framework process, but still resolves into the canonical logs folder. Full: docs/invariants/vatsim.md#vat-6
- [VAT-7] `VPilotPluginInstaller` writes outside MSFSBA's own tree, so every method is best-effort and must never throw; a failed install degrades to a status the settings dialog explains, and `VatsimPanel.Validate` never fails. Full: docs/invariants/vatsim.md#vat-7
- [VAT-8] `Locked` applies only to updating a DLL vPilot has already loaded; a first install succeeds even with vPilot running. Never collapse the two messages: they ask the pilot to do different things. Full: docs/invariants/vatsim.md#vat-8
- [VAT-9] Never re-add a vPilot folder Browse button or a `VPilotPluginsFolderOverride` setting: vPilot always installs under `%LOCALAPPDATA%\vPilot` and writes `HKCU\Software\vPilot\Install_Dir`; nor may `ResolvePluginsFolder` regain the any-folder-named-`Plugins` branch. Full: docs/invariants/vatsim.md#vat-9
- [VAT-10] The startup install check announces only `Locked`, `Failed`, a first-install `Installed` (`PluginWasAbsent`) and the one-shot `LegacyRemoved`, via the queued announcer; an update-refresh `Installed`, `AlreadyCurrent` and `VPilotNotFound` stay settings-only. Full: docs/invariants/vatsim.md#vat-10
- [VAT-11] Turning the VATSIM master switch off leaves the plugin DLL in place by design: removal fails while vPilot is running, so "off" would become a close-vPilot chore that silently no-ops. Full: docs/invariants/vatsim.md#vat-11
- [VAT-12] `HotkeyAction.ToggleVatsimAnnouncements` must stay in `MainForm.OnHotkeyTriggered`'s `offlineActions` set; muting VATSIM chatter has nothing to do with SimConnect, and without it the pilot hears "Not connected to simulator". Full: docs/invariants/vatsim.md#vat-12
- [VAT-13] Status/diagnostic text in any settings panel is a read-only `TextBox`, never a `Label`: a `Label` is not in the tab order, so a screen-reader user has to hunt for it with the review cursor. Full: docs/invariants/vatsim.md#vat-13
