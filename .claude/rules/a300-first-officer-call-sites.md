---
paths:
  - "MSFSBlindAssist/Aircraft/IniA300Definition.FirstOfficer.cs"
  - "MSFSBlindAssist/Aircraft/IniA300Definition.Announcements.cs"
  - "MSFSBlindAssist/Aircraft/IniA300Definition.Interaction.cs"
  - "MSFSBlindAssist/Aircraft/A300/A300FoQuiet.cs"
---
# Rules whose code the A300 definition's First Officer, Announcements and Interaction partials and A300FoQuiet hold

MIRRORS: copied word for word from first-officer-airbus.md, whose globs leave the definition out; the First Officer partial holds `FoApply` and the quiet (`A300FoQuiet`), and the Announcements and Interaction partials hold its speech and "did not move" through `IsFoQuietLamp`, `IsFoQuietRow` and `IsFoQuietMaster`. Change both together (ClaudeContextBudgetTests checks).

- [FOA-13] The A300 FO writes ONLY placed panel rows through `IniA300Definition.FoApply` (`IA300FoPort`, [A300-1]); a key that is not a row is refused and logged, never written another way; a button is pressed only while `A300FoKeys.PressConfirmations` says not done, and confirmed on it. Full: docs/invariants/first-officer-airbus.md#foa-13
- [FOA-14] An A300 FO write quiets ONLY its own row: its lights (`A300FoQuiet.LampsOf`), its lever call-out and its "did not move", for 5 s (the MD-11's per-control window); every other light, and the master warning and caution, still speak. Only a system test's run quiets every light (`QuietAllLightsFor`). A later call never shortens a window. Full: docs/invariants/first-officer-airbus.md#foa-14
