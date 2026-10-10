---
paths:
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.MenuHandlers.cs"
---
# Rules for the services MainForm wires up

MIRRORS: copied word for word from ground-traffic.md, audio-output.md, surroundings.md and weather.md, whose globs leave these partials out; change both together (ClaudeContextBudgetTests checks).

- [TRF-1] `GroundTrafficSuppression` (pure, not a MainForm lambda) silences Caution/Warning on the takeoff roll, with no route and in a STILL-ROLLING rollout; the 120° arc never gates Awareness, Alt+G stays ungated; a fast landing exit filters speech, never mutes the monitor (more: see full). Full: docs/invariants/ground-traffic.md#trf-1
- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
- [AUD-8] The four device notices (fell back, recovered, default changed, no device) are all queued, raised from `RunSweep` with `Gate` released after that sweep's rebinds; `OpenFor` never announces, and the sink marshals to the UI thread with non-blocking `BeginInvoke`. Full: docs/invariants/audio-output.md#aud-8
- [AUD-9] The session's first sweep is a baseline (`AudioOutputRouter.RequestBaselineSweep`, from `MainForm`'s connect timer after "Initializing, please wait"): at it `FellBackToDefault` and `NoDeviceAvailable` speak and `DefaultDeviceChanged`/`RecoveredPreferred` stay silent; never re-silence the degraded pair (more: see full). Full: docs/invariants/audio-output.md#aud-9
- [SUR-10] The surface-change callout (`SurfaceChangeGate`) has its own switch, is not behind `SuppressCheck`, speaks only a surface FAMILY change confirmed by `ConfirmMetres` from its first reading; other `lastKnownPosition` writers must carry the surface fields forward (more: see full). Full: docs/invariants/surroundings.md#sur-10
- [SUR-22] MainForm's passing-callout `SuppressCheck` covers takeoff assist, docking, `LandingRollout`, `LiningUp`, `HoldShort`, `ProgressiveHold` and both backtrack states, and deliberately NOT `Taxiing`: a pilot under active taxi guidance must still hear the surroundings. Full: docs/invariants/surroundings.md#sur-22
- [SUR-24] The runway probe (`TaxiGuidanceManager.IsOnRunwayPavement`) answers only from geometry in hand and NEVER builds a graph; its warm-up reads runway rows only (`PrepareRunwayShapeWarmUp`, never `GetTaxiPaths`); `_graphGeneration` is stamped only when a NEW `_graph` instance is installed, never when a re-route hands the same graph back. Full: docs/invariants/surroundings.md#sur-24
- [WX-7] ActiveSky is strictly opt-in (`UserSettings.ActiveSkyEnabled`, default off): no AS probe may run on an unopted path, gated centrally in `ActiveSkyClient.IsRunningAsync`; `ActiveSkyWeatherMonitor` runs only when `ShouldRun` (also needs `WeatherAutoAnnounceEnabled`), never on the AS switch alone. Full: docs/invariants/weather.md#wx-7
