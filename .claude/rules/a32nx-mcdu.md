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
