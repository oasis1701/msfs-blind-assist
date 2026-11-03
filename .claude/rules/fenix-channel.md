---
paths:
  - "MSFSBlindAssist/Services/FenixChannel*.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.DisplayChannel.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.*Channel*.cs"
  - "MSFSBlindAssist/Forms/Fenix/FenixChannel*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannel*.cs"
---
# Fenix display channel rules (port 18083)

Loaded when Claude reads the channel's code. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fenix-channel.md.

- [FNX-21] The Fenix display channel (127.0.0.1:18083) is read-only: `FenixChannelClient` opens only the `FenixChannelCall` paths in `FenixChannelFields`, never a write, action or credential call (WriteDataRef, GetNavigraphAccessToken, DoRpc…) nor Fenix's license or startup checks; an authentication refusal ends the channel for the session. Full: docs/invariants/fenix-channel.md#fnx-21
- [FNX-22] Channel data reaches a pilot only via `FenixChannelTrust`, per display: trusted while the channel streams, the build is in `FenixChannelFields.VerifiedBuilds`, each field has its shape, its display is live and its cross-checks pass; a disagreement held `MismatchHoldMs` latches it to the AI read until reconnect. Never speak untrusted values. Full: docs/invariants/fenix-channel.md#fnx-22
- [FNX-23] A build joins `FenixChannelFields.VerifiedBuilds` only after a developer re-checked every field number in `FenixChannelFields` against that installed build with the team's local channel tools; never on a version bump alone, and never by loosening a check or tolerance to make a build pass. Full: docs/invariants/fenix-channel.md#fnx-23
- [FNX-24] Channel text keeps its colors: `FenixChannelText` parses the format codes into runs, a read that speaks channel text says each color, and a line with a code it does not know makes its display untrusted; never strip the codes to plain text for speech. Full: docs/invariants/fenix-channel.md#fnx-24
- [FNX-25] The altitude cross-check compares field 197, the true altitude, with SimConnect's PLANE ALTITUDE (`FenixChannelMonitor.AltitudeKey`), never INDICATED ALTITUDE: the Fenix writes its own baro into the sim's altimeter, so the indicated altitude follows its QNH (a normal power-up at 1013 against 1028.8 hPa latched the PFD, 2026-11-02). Full: docs/invariants/fenix-channel.md#fnx-25
