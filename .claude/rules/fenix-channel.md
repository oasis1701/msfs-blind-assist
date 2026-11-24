---
paths:
  - "MSFSBlindAssist/Services/FenixChannel*.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.*Channel*.cs"
  - "MSFSBlindAssist/Forms/Fenix/FenixChannel*.cs"
  - "MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Channel*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannel*.cs"
---
# Fenix display channel rules (port 18083)

Loaded when Claude reads the channel's code. The rules for each display's own reader are in fenix-channel-displays.md. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fenix-channel.md.

- [FNX-21] The Fenix display channel (127.0.0.1:18083) is read-only: `FenixChannelClient` opens only the `FenixChannelCall` paths in `FenixChannelFields`, never a write, action or credential call (WriteDataRef, GetNavigraphAccessToken, DoRpc…) nor Fenix's license or startup checks; an authentication refusal ends the channel for the session. Full: docs/invariants/fenix-channel.md#fnx-21
- [FNX-22] Channel data reaches a pilot only via `FenixChannelTrust`, per display: trusted while the channel streams, the build is in `FenixChannelFields.VerifiedBuilds`, each field has its shape and its display is live. Never speak untrusted values; never compare a display with SimConnect to judge it: they can differ for no fault (2026-11-04). Full: docs/invariants/fenix-channel.md#fnx-22
- [FNX-23] A build joins `FenixChannelFields.VerifiedBuilds` only after a developer re-checked every field number in `FenixChannelFields` against that installed build with the team's local channel tools; never on a version bump alone, and never by loosening a check to make a build pass. Full: docs/invariants/fenix-channel.md#fnx-23
- [FNX-24] Channel text keeps its colors: `FenixChannelText` parses the format codes into runs, a read says each run's color (the ND, via `FenixChannelNdSpeech`, only where it tells something; the DCDU names none), and a line with a code it does not know makes its display untrusted; never strip the codes to plain text for speech. Full: docs/invariants/fenix-channel.md#fnx-24
- [FNX-26] Every Fenix channel announcer checks its `FenixChannelAnnouncementRows` key before speaking and keeps tracking while muted (an unmute replays nothing); its first reading after a start, a trust gap or a new `FenixChannelSnapshot.Connection` number is silent, the E/WD alerts' remembered list being the one exception ([FNX-43]). Full: docs/invariants/fenix-channel.md#fnx-26
- [FNX-30] Channel text the trust gate does not scan is checked by its reader (`FenixTextLine.UnknownCodes`): an unknown letter in the E/WD's thrust limit or flaps, the SD's bottom line or STATUS, or any line the ND or the DCDU reads takes that whole window to its status row, never a row with the letter dropped. Full: docs/invariants/fenix-channel.md#fnx-30
- [FNX-32] A Fenix display key opens its window while the display is trusted or dark (`FenixDisplayVerdict.IsDisplayOff`, the one status row), through `OpenChannelWindow` alone; any other reason not to trust it leaves the key to the AI read with no extra words, because that screen is live. Full: docs/invariants/fenix-channel.md#fnx-32
- [FNX-39] A channel member with a contract default is left out on the wire at that value (protobuf-net): read an absent one as its default, never as 0 or missing (`FenixChannelNd` reads an absent zoomSelection as range 40 and an absent mode as ROSE NAV, with LS sent as 0). Full: docs/invariants/fenix-channel.md#fnx-39
