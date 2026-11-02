---
paths:
  - "MSFSBlindAssist/Services/FenixChannel*.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.DisplayChannel.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannel*.cs"
---
# Fenix display channel rules (port 18083)

Loaded when Claude reads the channel's code. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fenix-channel.md.

- [FNX-21] The Fenix display channel (127.0.0.1:18083) is read-only: `FenixChannelClient` opens only the `FenixChannelCall` paths in `FenixChannelFields`, never a write, action or credential call (WriteDataRef, GetNavigraphAccessToken, DoRpc…) nor Fenix's license or startup checks; an authentication refusal ends the channel for the session. Full: docs/invariants/fenix-channel.md#fnx-21
- [FNX-22] Channel data reaches a pilot only through `FenixChannelTrust`, per display: trusted while the channel streams, the build is in `FenixChannelFields.VerifiedBuilds`, every field has its shape and that display's cross-checks pass; a disagreement held `MismatchHoldMs` latches it to the AI read until reconnect. Never speak an untrusted value. Full: docs/invariants/fenix-channel.md#fnx-22
- [FNX-23] A build joins `FenixChannelFields.VerifiedBuilds` only after a developer re-checked every field number in `FenixChannelFields` against that installed build with the team's local channel tools; never on a version bump alone, and never by loosening a check or tolerance to make a build pass. Full: docs/invariants/fenix-channel.md#fnx-23
- [FNX-24] Channel text keeps its colors: `FenixChannelText` parses the format codes into runs, a read that speaks channel text says each color, and a line with a code it does not know makes its display untrusted; never strip the codes to plain text for speech. Full: docs/invariants/fenix-channel.md#fnx-24
