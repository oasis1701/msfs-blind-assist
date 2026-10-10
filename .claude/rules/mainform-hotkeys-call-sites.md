---
paths:
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
---
# Ground-traffic rule for MainForm.Hotkeys.cs

MIRRORS: copied word for word from ground-traffic.md, whose globs leave MainForm.Hotkeys.cs out; its hotkey dispatch runs the Alt+G traffic summary the rule keeps ungated. Change both together (ClaudeContextBudgetTests checks).

- [TRF-1] `GroundTrafficSuppression` (pure, not a MainForm lambda) silences Caution/Warning on the takeoff roll, with no route and in a STILL-ROLLING rollout; the 120° arc never gates Awareness, Alt+G stays ungated; a fast landing exit filters speech, never mutes the monitor (more: see full). Full: docs/invariants/ground-traffic.md#trf-1
