---
paths:
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
---
# Ground-traffic rule for MainForm.Hotkeys.cs

MIRRORS: copied word for word from ground-traffic.md, whose globs leave MainForm.Hotkeys.cs out; its hotkey dispatch runs the Alt+G traffic summary TRF-1 keeps ungated and speaks the taxi status (`GetStatusAnnouncement`) whose held runway TRF-6 derives. Change both together (ClaudeContextBudgetTests checks).

- [TRF-1] `GroundTrafficSuppression` (pure, not a MainForm lambda) silences Caution/Warning on the takeoff roll, with no route and in a STILL-ROLLING rollout; the 120° arc never gates Awareness, Alt+G stays ungated; a fast landing exit filters speech, never mutes the monitor (more: see full). Full: docs/invariants/ground-traffic.md#trf-1
- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
