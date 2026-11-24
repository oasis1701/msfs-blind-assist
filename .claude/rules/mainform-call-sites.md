---
paths:
  - "MSFSBlindAssist/MainForm*.cs"
---
# Rules for every MainForm partial

MIRRORS: copied word for word from their areas' rule files. They name no code, so they load on every partial; a rule whose code some partials hold is in the call-site file for those. Change both together (ClaudeContextBudgetTests checks).

- [STAB-11] The sim tablet opens from the Extras menu (In-Sim Tablet (FS2024 Native)) for EVERY aircraft, beside an aircraft's own tablet and never in place of it: never gate it per aircraft nor route it through Input Shift+T, which stays the aircraft's own (the owner's decision, 2026-10-19). On MSFS 2020 it says the tablet is MSFS 2024 only. Full: docs/invariants/sim-tablet.md#stab-11
- [SIC-17] Tie each hold-short to the taxiway it FOLLOWS, never the clearance's last; cut on the parser's OWN mask, keep a taxiway repeated across a hold-short as its own row, and map a name the sequence lacks to -1 so it is reported. Full: docs/invariants/sayintentions-clearance.md#sic-17
- [UPD-7] The automatic startup update check must stay SILENT on failure and when up to date — only the menu item reports those. Full: docs/invariants/updates.md#upd-7
