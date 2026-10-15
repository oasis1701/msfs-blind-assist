---
paths:
  - "MSFSBlindAssist/Services/Volanta/**"
  - "MSFSBlindAssist/Forms/Volanta/**"
  - "tests/MSFSBlindAssist.Tests/Volanta/**"
---
# Volanta live map rules

Loaded when Claude reads matching code. Background: docs/volanta.md. Full text of each rule: docs/invariants/volanta.md.

- [VOL-1] Never request anything from Volanta's servers (api.volanta.app and webassets.volanta.app refuse outside clients with a Cloudflare 403): `VolantaLink` only reads, through the debugger's response bodies, the files Volanta's own map page just downloaded. Full: docs/invariants/volanta.md#vol-1
- [VOL-2] Volanta's map stops downloading while its window is minimized or hidden, though Chromium still says "visible": read Volanta's own window state (`VolantaLinkSnapshot.VolantaPaused`) and treat 30 s without a file as stale; never present the last snapshot as current then, say why. Full: docs/invariants/volanta.md#vol-2
- [VOL-3] Volanta is single-instance, so the debugging switch needs a fresh launch: `VolantaLauncher` asks the pilot, closes Volanta as its close button does (`CloseAsync`) and relaunches it; never force-quit Volanta. Full: docs/invariants/volanta.md#vol-3
- [VOL-4] The pilot's own flight is in Volanta's file with a position up to minutes old: `VolantaTrafficCalculator.IsOwnFlight` leaves it out by the logged-in user's id or Volanta's flight id, else by Volanta's own position report, never by distance alone; never read Volanta's login token for the id. Full: docs/invariants/volanta.md#vol-4
- [VOL-5] Network-file rows always say not on the ground, height 0, vertical speed 0: `VolantaFeedParser` infers ground below `NetworkGroundSpeedKt` and leaves height and vertical speed unknown; trust those flags only for Volanta users. Full: docs/invariants/volanta.md#vol-5
