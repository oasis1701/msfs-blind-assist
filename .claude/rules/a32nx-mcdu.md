---
paths:
  - "MSFSBlindAssist/SimConnect/CoherentA32nxMcduClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentEvalClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentLinkState.cs"
  - "MSFSBlindAssist/SimConnect/CoherentViewOwnership.cs"
  - "MSFSBlindAssist/Services/FbwMcdu*.cs"
  - "MSFSBlindAssist/Services/FlyByWireMCDU*.cs"
  - "MSFSBlindAssist/Services/FlyByWireSimBridgeMcduClient.cs"
  - "MSFSBlindAssist/Forms/FlyByWireA320/**"
  - "MSFSBlindAssist/Resources/coherent-a32nx-*.js"
  - "tests/MSFSBlindAssist.Tests/**/*A32nxMcdu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwMcdu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FlyByWireMCDU*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CoherentLinkState*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CoherentViewOwnership*.cs"
---
# FlyByWire A32NX MCDU transport rules

Loaded when Claude reads matching code. Background: docs/a32nx.md. Full text of each rule: docs/invariants/a32nx-mcdu.md.

- [A320-5] The A32NX MCDU runs over Coherent (`CoherentA32nxMcduClient`) with SimBridge as fallback, picked by `FbwMcduTransportArbiter`, which never replays a remembered frame; the client claims its view (`CoherentViewOwnership`), and never add a Captain/FO side selector. (more: see full) Full: docs/invariants/a32nx-mcdu.md#a320-5
- [A320-32] One socket per Coherent view rests on `CoherentViewOwnership`: one-shots (`CoherentEvalClient`) enter via `TryEnterOneShot`; `CoherentA32nxMcduClient` `Claim`s its view Start→Stop, reconnect gaps included, and never connects while `OneShotInFlight`. Never gate a caller on 'holds a socket now?' (D/Shift+D rides `EvalOnMcduViewAsync`). Full: docs/invariants/a32nx-mcdu.md#a320-32
- [A320-33] The A32NX MCDU's "readable" (Coherent live, window "Connected") lives in `CoherentLinkState` alone: one lock, every event tagged with its socket's generation, every teardown reported (`DropSocket` in the loop, `_link.Stop()` on Stop); never a readable or agent flag kept in the client or set from two threads. Full: docs/invariants/a32nx-mcdu.md#a320-33
- [A320-34] `CoherentA32nxMcduClient` keeps reading the MCDU with its window CLOSED (1 s idle, 250 ms open; `SetActive` wakes `WakeableDelay`): never stop a closed window's polling, or FMS scratchpad messages (`FbwMcduReadBack`) no longer reach the pilot. Full: docs/invariants/a32nx-mcdu.md#a320-34
- [A320-35] `FlyByWireMCDUForm.SendTextToMCDU` awaits each key, calls `HoldForTyping` before the send AND after delivery, then waits 50 ms: FBW's keypad applies a key 150-200 ms late and keeps order only at >= 50 ms spacing; never shorten the gap or drop either hold. Full: docs/invariants/a32nx-mcdu.md#a320-35
