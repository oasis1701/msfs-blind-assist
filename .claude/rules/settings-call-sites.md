---
paths:
  - "MSFSBlindAssist/Settings/UserSettings.cs"
  - "MSFSBlindAssist/Settings/SettingsManager.cs"
  - "MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs"
---
# Rules whose settings live in UserSettings

MIRRORS: each line below is copied word for word from its area's rule file, because the setting, default or save path it guards lives in these files, which that area's globs do not cover. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [WX-7] ActiveSky is strictly opt-in (`UserSettings.ActiveSkyEnabled`, default off): no AS probe may run on an unopted path, gated centrally in `ActiveSkyClient.IsRunningAsync`; `ActiveSkyWeatherMonitor` runs only when `ShouldRun` (also needs `WeatherAutoAnnounceEnabled`), never on the AS switch alone. Full: docs/invariants/weather.md#wx-7
- [WX-11] Route-advisory location context is additive-only (no geometry renders as before, never dropped); announcements are proximity events: Approach once inside `RouteAdvisoryProximityNm` (never folded into `SigmetProximityRangeNm`), behind-suppression Approach-only, Leave confirmed over 2 ticks, expiry silent (more: see full). Full: docs/invariants/weather.md#wx-11
- [DCK-35] `DistanceFormatter` is display-only; guidance thresholds stay unit-native (metric). `GroundTrafficUseMetres` and `GroundDistanceUnit` are independent toggles: never fold them together. Full: docs/invariants/gsx-stands-docking.md#dck-35
- [SUR-10] The surface-change callout (`SurfaceChangeGate`) has its own switch, is not behind `SuppressCheck`, speaks only a surface FAMILY change confirmed by `ConfirmMetres` from its first reading; other `lastKnownPosition` writers must carry the surface fields forward (more: see full). Full: docs/invariants/surroundings.md#sur-10
- [DCK-33] Hot paths must not regress: docking far-field math gated to <150 m or engaged, fired callout latches early-out, `TaxiAssistForm`'s gate list cached per ICAO, `SettingsManager.Save` writing outside its static lock. Full: docs/invariants/gsx-stands-docking.md#dck-33
- [VG-13] Desired and current tone waveforms must default to different shapes (triangle and sine, the user settings `VisualGuidanceToneWaveform` and `VisualGuidanceCurrentToneWaveform`): identical waveforms phase-cancel at a matched state, exactly when the pilot most needs the difference audible. Full: docs/invariants/visual-guidance.md#vg-13
