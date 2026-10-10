# Docking guidance: approach geometry, tones, completion and disengage — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/gsx-docking.md`, which Claude Code loads when it reads matching code. Background: [gsx.md](../gsx.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## DCK-17

- The docking lateral cue must use the heading-error angle, never `CalculateCrossTrackError` — that assumes the aircraft is ahead of the reference and yields ±180° garbage when docking from behind. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-18

- `DockingGeometry.ClampStopToOccupancy` must never be removed or simplified to clamp on `gatedistancethreshold` unconditionally, and must clamp only the `.py`-shifted stop, never the navdata base point — it must remain a no-op for deice pads, navdata-only gates, already-inside-circle datums, and VDGS-reliant gates whose stop sits beyond the threshold. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-19

- Docking's lateral tone must use the runway-lineup PRECISION profile (`UpdateHeadingErrorWithThresholds`), never the width-scaled overload — its MIN_SCALE clamp is far too loose for parking. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-20

- `DockingCompleted` must fire `taxiGuidanceManager.StopGuidance()` exactly once (event raised outside the docking lock), or taxi guidance can be left stuck in LiningUp after parking. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-21

- Arrival ownership must stay ENGAGE-LATCHED (docking `IsActive` = Docking or Stopped state), never widened back to gate-set semantics — the old semantics left a pilot in total verbal silence when docking never engages (approach outside cone, navdata heading error). → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-22

- Docking's stop tolerance and beep plateau must not regress: `StopToleranceMetres` stays 0.3m and `BeepNearMetres` must equal it (no plateau) — a plateau makes 2m-to-stop sound identical to the stop itself and pilots park short. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-23

- Docking completion requires SQUARENESS with the gate axis (`DockingGeometry.IsSquare`, `StopMaxHeadingErrorDeg` 7°) as well as the ≤2 m cross gate — never reduce it back to cross+along only (KJFK gate 20: a 17.4°-askew park announced "GSX docking complete." while GSX offered reposition, and the callout landed mid-alignment-turn). An askew arrival must TERMINATE like `IsLateralMiss` (stop-and-retry, silence, overshoot-flavoured Stopped), never advise an in-place turn: the stop band leaves 1.3 m of travel, ~2° of heading change on a heavy. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-24

- A VERIFIED-good park CONCLUDES guidance: the solid "docked" tone holds `CompletedHoldToneSeconds` (3 s) as a positional reference across the moment of stopping, then docking falls silent for good with a verbal closure ("… Aligned with gate. Parking brake."; a deice pad gets no gate-alignment claim). Never restore the old hold-until-the-pilot-presses-Stop behaviour, and never hold ANY tone after an OVERSHOOT/askew stop — a "docked" marker over a bad park misleads. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-25

- The concluded-park hold tone must fade on a one-shot `Timer` armed at completion, NEVER a per-frame countdown — completion raises `DockingCompleted` → `StopGuidance()` → `StopTaxiGuidanceMonitoring()`, and docking is fed from that same `TAXI_GUIDANCE_POSITION` stream, so the completing frame is normally the LAST frame the manager sees and a countdown never runs (the tone held forever). The same applies to any future "N seconds after the park" behaviour. Cancel the timer at both reset sites + dispose, re-check the state under the lock in the callback, and only ever use the non-blocking `Timer.Dispose()` under `_lock`. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-27

- Disabling docking (or losing the gate) mid-approach must fully `ResetLocked`, not just go silent — leaving `_state` latched at Docking/Stopped keeps `IsActive` true forever and mutes taxi's steering tone with no lateral cue. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-28

- Takeoff-assist activation and `LandingRollout` entry must both call `SetDestinationGate(null)` — a stale departure gate could otherwise keep docking `IsActive` latched on landing and mute the rollout steering tone. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-29

- Docking's "Slow down." threshold is `SlowDownSpeedKts` 3.0 (lowered from 5.0, which was silent through every live approach that ended askew or through the stop) — and `SlowDownMetres` (6 m) must NOT be widened to make the warning earlier: the callout is a one-shot re-armed only at engage/reset, so a wider gate lets it fire during the normal deceleration from taxi speed and be spent before the band where it matters. The speed threshold is the knob, not the distance. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-30

- Docking's 1-knot speed callout is the only PERIODIC cue on that path and must stay QUEUED (`Announce`), placed LAST after the slow-down/milestone one-shots and skipped on a frame one of them fired — every other docking callout is `AnnounceImmediate`, which interrupts, so speaking it first didn't make speed win (the last speaker wins), it just consumed the knot and cut the phrase off. `EngageLocked` must `Arm(currentSpeed)`, never `Reset()`: a "next sample always speaks" re-arm put a number over the multi-second engage callout 16-33 ms later on every dock. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-32

- MainForm must call `taxiGuidanceManager.SetSteeringToneSuppressed(dockingGuidanceManager.IsActive)` every frame so only one steering tone ever plays — taxi and docking must never pan simultaneously. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-33

- Hot-path perf invariants must not regress: docking far-field telemetry/lineup math stays gated to <150m or engaged; hold-short/parking/exit-approach/runway-end callout paths must early-out once their latches have fired; `TaxiAssistForm`'s gate list must stay cached in memory per ICAO (never re-query per keystroke); `SettingsManager.Save` must write the file OUTSIDE its static lock. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-34

- A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in whatever unit its `AddToDataDefinition` requests, whatever its name says. `SimConnectManager.Setup.cs` asks for degrees in `AIRCRAFT_POSITION`, `DEF_AI_TRAFFIC`, `VISUAL_GUIDANCE_DATA` and `FLARE_ASSIST_DATA`, and for radians in the hotkey readouts (`DEF_HEADING_MAG`, `DEF_HEADING_TRUE`) and `TAKEOFF_ASSIST_DATA`; the hand-fly heading (definition 371, `SimConnectManager.Monitoring.cs`) asks for radians too. Each radians value is converted ONCE where it is received — `SimConnectManager.Dispatch.cs` for the hotkey readouts and the take-off-assist and taxi-guidance positions, MainForm's hand-fly handler for 371 — so `AircraftPosition.HeadingMagnetic` is always degrees: never convert it again. Check the unit of the definition you read before converting anything (lat/lon are degrees). An aircraft definition's own heading variable follows its `Units` the same way: the FBW A320 and the Fenix register `PLANE_HEADING_DEGREES_MAGNETIC` in radians and cache it raw (the A320's display override converts it for display), while the A380's standby heading and the base `VISUAL_GUIDANCE_HEADING` ask for degrees. The old wording came from a live MCP/SimConnect read that returned radians (a logged 5.93 = 339.7°): true of that read, never of every definition. → [gsx.md](../gsx.md)

Corrected 2026-10-08: the rule said both headings always arrive in radians and must always be multiplied by 57.2958; four definitions ask for degrees, and following it would convert an already-converted heading a second time. Evidence: the `AddToDataDefinition` calls in `SimConnectManager.Setup.cs` and `SimConnectManager.Monitoring.cs`, the `180.0 / Math.PI` conversions in `SimConnectManager.Dispatch.cs`, the hand-fly heading handler in `MainForm.Announcers.cs`, and the heading registrations of `FlyByWireA320Definition`, `FenixA320Definition`, `FlyByWireA380Definition` and `BaseAircraftDefinition` (`VISUAL_GUIDANCE_HEADING`).

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).

## DCK-35

- `DistanceFormatter` is a DISPLAY layer only — never use it for guidance thresholds; those must stay unit-native (metric) internally. `GroundTrafficUseMetres` is a separate, independent toggle from `GroundDistanceUnit` — never fold them together. → [gsx.md](../gsx.md)

Moved 2026-10-10 from gsx-stands-docking.md (split for the rule budget).
