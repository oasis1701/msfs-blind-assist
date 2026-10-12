---
paths:
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
  - "MSFSBlindAssist/MainForm.Designer.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.IFly737.cs"
  - "MSFSBlindAssist/MainForm.MD11.cs"
  - "MSFSBlindAssist/MainForm.MenuHandlers.cs"
  - "MSFSBlindAssist/MainForm.PanelBuilder.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
---
# Visual guidance and hand fly rules for MainForm's partials

MIRRORS: copied word for word from visual-guidance.md, which globs MainForm.Hotkeys.cs and no other partial; change both together (ClaudeContextBudgetTests checks).

- [VG-2] Every path that activates hand fly or visual guidance must leave output hotkey mode first, or the quick-access keys stay dead for the rest of the session; the liftoff auto-handoff calls `hotkeyManager.ExitOutputHotkeyMode()` itself, and that exit must stay silent. Full: docs/invariants/visual-guidance.md#vg-2
- [VG-11] VG auto-deactivation on the airborne-to-on-ground edge must not be gated on ground speed or any other condition: a landing at any speed must trigger it. Full: docs/invariants/visual-guidance.md#vg-11
