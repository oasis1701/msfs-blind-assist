---
paths:
  - "MSFSBlindAssist/Services/TakeoffRollCallouts.cs"
  - "MSFSBlindAssist/Services/TakeoffAssistManager.cs"
  - "MSFSBlindAssist/Services/UnusualAttitudeMonitor.cs"
  - "tests/MSFSBlindAssist.Tests/TakeoffRollCalloutsTests.cs"
  - "tests/MSFSBlindAssist.Tests/UnusualAttitudeMonitorTests.cs"
---
# Rules whose code the take-off-roll and unusual-attitude services hold

MIRRORS: copied word for word from da40-shared-code.md, whose globs leave these services out (the take-off-roll speed calls, the bank warning); change both together (ClaudeContextBudgetTests checks).

- [DA40S-12] Take-off-roll speed calls are decided only by `TakeoffRollCallouts` and applied for the CURRENT aircraft on every path that configures `TakeoffAssistManager` (aircraft switch AND Settings save); a profile is never applied without being taken back. Full: docs/invariants/da40-shared-code.md#da40s-12
- [DA40S-13] `UnusualAttitudeMonitor` INTERRUPTS beyond 45 degrees of bank with hysteresis and announces the recovery at 20 degrees, a deliberate exception to the silent-clear rule; never turn it into a query and never silence the recovery. Full: docs/invariants/da40-shared-code.md#da40s-13
