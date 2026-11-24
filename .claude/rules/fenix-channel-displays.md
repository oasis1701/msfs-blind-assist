---
paths:
  - "MSFSBlindAssist/Services/FenixChannelPfd*.cs"
  - "MSFSBlindAssist/Services/FenixChannelNd*.cs"
  - "MSFSBlindAssist/Services/FenixChannelEwd*.cs"
  - "MSFSBlindAssist/Services/FenixChannelSd*.cs"
  - "MSFSBlindAssist/Services/FenixChannelIsis*.cs"
  - "MSFSBlindAssist/Services/FenixChannelFma*.cs"
  - "MSFSBlindAssist/Services/FenixChannelDcdu*.cs"
  - "MSFSBlindAssist/Services/FenixChannelFields.Sd*.cs"
  - "MSFSBlindAssist/Forms/Fenix/FenixChannel*Form.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelPfd*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelNd*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelEwd*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelSd*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelIsis*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelFma*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FenixChannelDcdu*.cs"
---
# Fenix display channel rules: each display's reader

Loaded when Claude reads the code of a display's window, keys or announcer, on top of fenix-channel.md. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fenix-channel-displays.md.

- [FNX-27] A PFD flag joins the Fenix PFD window (`FenixChannelPfdRows`) only after a capture in the sim showed what the PFD draws for it; an unmeasured flag is left out, never named by analogy with the real A320. Full: docs/invariants/fenix-channel-displays.md#fnx-27
- [FNX-28] No Fenix channel code reads RemoteData field 155 as the approach minimum: it is not the setting (700.0 with BARO 700, but -701.76 and -124.87 with RADIO 200, moving with the aircraft). The minimum is the FMA's Approach column (slot 13, "RADIO 200"). Full: docs/invariants/fenix-channel-displays.md#fnx-28
- [FNX-29] An SD page composer makes its page not available when a nested message its rows read is absent, never its defaults: protobuf leaves out the Fenix's valve value Open (0) and every false or 0, so a missing message would read every valve open and every bus not powered (`FenixChannelSdReader.Message`). Full: docs/invariants/fenix-channel-displays.md#fnx-29
- [FNX-31] The Fenix E/WD window's engine block follows the definition's variant (IAE V2500: EPR first; CFM56, LEAP, PW GTF: N1 first), never channel field 6.5, whose CFM56 value 0 protobuf-net leaves out; an unread engine type shows no block, never a guessed order. Full: docs/invariants/fenix-channel-displays.md#fnx-31
- [FNX-33] A Fenix DCDU window press goes through the definition's PressDcdu (a full press-release), once per press of a key (a held key presses again otherwise, and WILCO then SEND is two presses on one slot); `FenixChannelDcduKeys.Decide` judges a soft key at the press, and a blank label or an untrusted DCDU sends nothing and says so. Full: docs/invariants/fenix-channel-displays.md#fnx-33
- [FNX-34] Fenix Ctrl+PageDown presses `S_DCDU1_MSGDWN`, never MSGUP: the DCDU's message counter rises with MSGDWN though its cockpit part is named Minus, so swapping them to match the names breaks DOWN-goes-forward ([A320-17]). Full: docs/invariants/fenix-channel-displays.md#fnx-34
- [FNX-35] An SD page lists only the parts its airframe has, chosen by `FenixChannelSdPageContext.Variant` and the page's own layout flags (179.26, 179.27, 151.8.5, 151.8.6, 137.19), never by a value's presence: an A321 push reported every slide armed, overwing ones included. Full: docs/invariants/fenix-channel-displays.md#fnx-35
- [FNX-36] An SD value with a measured meaning (`GearUnit`, `GearDoorPosition`, `HotAirStates`, the surfaces' signs) speaks only a measured value; any other reads "not available" and is reported, never a word guessed from a neighbor. Full: docs/invariants/fenix-channel-displays.md#fnx-36
- [FNX-37] An SD row speaks a color only where the Fenix sends one (a field named for it, a `...Pulse` as "pulsing"), never a default or guessed color, and never the color of an element the SD is not drawing (the A321's flow amber is set all flight with no flow). Full: docs/invariants/fenix-channel-displays.md#fnx-37
- [FNX-38] The ISIS window (`FenixChannelIsisRows`) shows values only while the ISIS verdict trusts it, every 182 member it reads has its shape and 182 carries a QNH or a phase length; 182.16 and 182.17 are phase lengths read as drawn, never a countdown, with no attitude row while either shows, and a round-gauge Fenix keeps the AI read. Full: docs/invariants/fenix-channel-displays.md#fnx-38
- [FNX-40] The ND's map points (`FenixChannelNdMapData.Check`) are used only while the GetMapData reply's mode and option equal the same snapshot's EFIS, its range covers the view, its center lies within a tenth of its range plus 0.5 NM of the ND's and its version was confirmed in the last 3 s; never a reply alone. Full: docs/invariants/fenix-channel-displays.md#fnx-40
- [FNX-41] The ND window (`FenixChannelNdRows`) lists only what the ND draws, nothing merged or dropped; it speaks a color only from the channel (a color member, or text via `FenixChannelNdSpeech`: amber and red always, another only when unusual for its line) or a `FenixChannelNdLook` entry citing a capture whose `ColorTellsApart` is set. Full: docs/invariants/fenix-channel-displays.md#fnx-41
- [FNX-42] `FenixChannelFmaAnnouncer` compares what the pilot would hear (`FenixChannelFmaSpeech.Read`), never raw FMA slots, so the change box and the b/f letters never speak; a change speaks once it has held `SettleMs` (300 ms), column 5 never, and the first trusted PFD reading after a start or a trust gap is a silent baseline. Full: docs/invariants/fenix-channel-displays.md#fnx-42
- [FNX-43] Fenix E/WD alerts (`FenixChannelEwdAlertAnnouncer`) come only from the ECAM entries stream (never -STS- items or action lines); a title's color only from its own E/WD line (`FenixChannelEwdAlertLines.Find`); one interrupt per step; an item counts as gone only after `LeaveHoldMs`; never wait for the first list after a blackout. Full: docs/invariants/fenix-channel-displays.md#fnx-43
