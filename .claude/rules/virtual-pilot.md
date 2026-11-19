---
paths:
  - "MSFSBlindAssist/Utils/SimClock.cs"
  - "MSFSBlindAssist/Services/TaxiSteeringTone.cs"
  - "MSFSBlindAssist/Utils/Logging/Log.cs"
  - "tools/VirtualPilot/**"
---
# VirtualPilot harness hook rules

Loaded when Claude reads matching code. Background: docs/virtual-pilot.md. Full text of each rule: docs/invariants/virtual-pilot.md.

- [VP-1] The VirtualPilot hooks (`SimClock.Override`, `TaxiSteeringTone.Headless`, `Log.Redirect`, the `…ForHarness` accessors and A/B switches) stay INERT in the app, set only from `tools/VirtualPilot`; every clock read in taxi/rollout guidance goes through `SimClock.UtcNow`. Full: docs/invariants/virtual-pilot.md#vp-1
