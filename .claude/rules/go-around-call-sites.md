---
paths:
  - "MSFSBlindAssist/Services/LandingExitPlanner.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitGoAround*.cs"
---
# Go-around rule for the landing-exit planner and its go-around tests

MIRRORS: copied word for word from landing-touchdown.md. The planner re-arms after a go-around (`RearmAfterGoAround`), and landing-exits.md globs both files, so landing-touchdown.md cannot glob them without loading its EXIT mirrors twice. Change the rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [ROL-7] A go-around or touch-and-go ENDS landing-exit guidance and keeps the plan (`LandingExitGoAround`): held only while KNOWN airborne, decided after `ConfirmMs` by a FRESH position read (never the 1 Hz cache), then ONE sentence. Full: docs/invariants/landing-touchdown.md#rol-7
