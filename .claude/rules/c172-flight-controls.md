---
paths:
  - "MSFSBlindAssist/Aircraft/C172/CessnaC172G1000Definition.FlightControls.cs"
  - "MSFSBlindAssist/Aircraft/C172/CessnaC172BaseDefinition.cs"
  - "tests/MSFSBlindAssist.Tests/**/CessnaC172FlightControlsTests.cs"
---
# Cessna 172 trim and throttle rules

Loaded with the C172's elevator trim and throttle code. Background: docs/c172.md. Full text of each rule: docs/invariants/c172-flight-controls.md.

- [C172F-1] The C172's trim is written only with `ELEVATOR_TRIM_SET` (22 degrees per 16383 both ways, travel 22 up to 19 down, `TrimSetValue`): the HANDLING_ELEVATORTRIM_WHEEL input only follows the trim, and each HANDLING_ELEVATORTRIM_YOKE write is one 0.11 degree step. Full: docs/invariants/c172-flight-controls.md#c172f-1
- [C172F-2] HANDLING_TRIM_DISCONNECT is a direct set, read from ELEVATOR TRIM DISABLED, and while on it blocks every trim, `ELEVATOR_TRIM_SET` and a hardware trim included: every trim write asks it first and refuses aloud (`HandleTrimSet`). Full: docs/invariants/c172-flight-controls.md#c172f-2
- [C172F-3] Never write the throttle through its input event ENGINE_THROTTLE_1: it takes a 0-1 fraction though it reads back in percent (20 written was full power). The combo writes THROTTLE1_SET, 0-16383, measured exact. Full: docs/invariants/c172-flight-controls.md#c172f-3
