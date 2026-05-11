# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

MSFS Blind Assist - C# Windows Forms accessibility application for Microsoft Flight Simulator. Multi-aircraft support (FlyByWire A320, Fenix A320, extensible). SimConnect integration, screen reader optimized (NVDA/JAWS). .NET 9, Windows Forms, SQLite.

## Build Commands

```bash
dotnet build MSFSBlindAssist.sln -c Debug
dotnet build MSFSBlindAssist.sln -c Release
```

**Output:** `MSFSBlindAssist\bin\x64\{Debug|Release}\net9.0-windows\win-x64\`

**Prerequisites:** MSFS_SDK environment variable, .NET 9 SDK

The solution contains two projects: `MSFSBlindAssist` (main app) and `MSFSBlindAssistUpdater` (small WinForms auto-update helper). `dotnet build MSFSBlindAssist.sln` builds both.

## Testing

No automated test project exists. Verification is done by running the app against a live sim (MSFS 2020 or 2024). When making changes, build, then describe an in-sim test plan in the PR — the human owner of the repo runs it. Don't add unit tests speculatively; this is a SimConnect-driven UI app where most code paths only execute against the real simulator.

## Git Workflow

The `main` branch is protected. Always create a new branch for changes and open a pull request — never commit directly to main.

## CRITICAL Rules (Always Follow)

### Screen Reader Announcements

**CRITICAL:** Screen readers automatically announce ALL UI control interactions.

**NEVER announce:**
- Button presses in panel controls
- Combo box/dropdown value changes
- Any direct user interaction with UI elements

**ONLY announce:**
- Numeric input confirmations (user needs exact value feedback)
- Error conditions (validation failures)
- Background state changes (not directly triggered by user)

**Why:** Screen readers already announce UI interactions. Redundant announcements = poor UX.

### SimConnect Connection Timing

**CRITICAL:** In SimConnectManager.cs, set `IsConnected = true` BEFORE calling `SetupDataDefinitions()`. Required for `StartContinuousMonitoring()` to execute properly (has guard clause requiring `IsConnected == true`). See SimConnectManager.cs:251

### Accessible TreeView Controls

**CRITICAL:** Never use `TreeView` directly in forms. Use `NativeAccessibleTreeView` (`Controls/NativeAccessibleTreeView.cs`) instead. .NET 9's `TreeViewAccessibleObject` (UIA-based) produces incorrect navigation order in NVDA — items appear out of sequence, focus jumps between unrelated nodes. `NativeAccessibleTreeView` bypasses the .NET 9 UIA implementation and falls back to the native Win32 SysTreeView32 MSAA proxy, which works reliably.

**Pattern for tree views with detail data:**
- Parent nodes show summary text only — no child nodes pre-populated
- Add a dummy child `new TreeNode("Loading...") { Tag = "placeholder" }` so the expand indicator (+) appears
- Handle `BeforeExpand` to lazily populate real child nodes on demand, checking for the placeholder first
- Store the data index in `parent.Tag` so the expand handler can look up the data
- Leaf nodes (e.g. airport endpoints with no detail) get no placeholder and no expand indicator

This lazy-loading pattern keeps the tree lightweight (fewer total nodes) and avoids accessibility edge cases.

### Database Paths

**CRITICAL:** Never hardcode `Path.Combine(..., "FBWBA", "databases", ...)` or `Path.Combine(..., "MSFSBlindAssist", "databases", ...)`. The user's DB may live at *either* location for historical reasons (the app was renamed). All code must go through `Database/DatabasePathResolver.cs`:

- `ResolveExistingDatabasePath(simVer)` — for **reads** (canonical first, legacy fallback). Used by `DatabaseSelector`, `MainForm`, `ElectronicFlightBagForm` lookups, etc.
- `GetCanonicalDatabasePath(simVer)` — for **writes** (always `MSFSBlindAssist\databases\`). Used only by the build target in `DatabaseBuildProgressForm`.

`NavdataReaderBuilder.GetDefaultDatabasePath` delegates to the resolver and is safe for reads. The MS Store package name for FS2024 is `Microsoft.Limitless_8wekyb3d8bbwe` (not "FlightSimulator2024"); FS2020 is `Microsoft.FlightSimulator_8wekyb3d8bbwe`. Both are referenced in `NavdataReaderBuilder.GetMSFSBasePath` when resolving `UserCfg.opt` for the scenery base path.

### Taxi Guidance

**Feature:** Turn-by-turn taxi assistance — stereo-panned steering tone + spoken announcements, driven by a graph built from the navdatareader `taxi_path` / `start` / `parking` tables. Works on any airport the user's DB exposes.

**Key rules when touching this code:**
- **No airport-specific hardcoding.** Everything comes from the user's DB. Taxiway names (`A`, `K2`, `LINK 53`, `HAWKER`), parking abbreviations (`G`, `GA–GZ`, `P`, `NP`, `EP`), and runway IDs flow through unchanged.
- **Do not break the teleport → takeoff-assist flow.** The MainForm runway-reference seeding from taxi lineup must remain guarded by `!takeoffAssistManager.IsActive && !takeoffAssistManager.HasRunwayReference` so the existing teleport dialog path wins.
- **Do not announce runway info** (length, surface, ILS) from taxi guidance. Out of scope.
- **WAYPOINT_CAPTURE_RADIUS_M (25 m) must skip the last segment.** Otherwise it preempts the gate arrival radius (6 m) and the 50/20/10 ft parking countdown. Runways are unaffected (30 m > 25 m), but gates break without this guard.
- Steering tone uses **stereo pan only** — no frequency or volume modulation. Hysteresis (3°/6°) + 400 ms min sustain + low-pass filter on heading error kill flapping. Thresholds are **width-aware**: scaled per call by `sqrt(pathWidthFeet / 60)` clamped to `[0.65, 1.40]`. For taxi / gate lineup, call the `UpdateHeadingError(error, pathWidthFeet)` overload with the real segment width (gate lineup uses the no-arg baseline). The takeoff *roll* (`TakeoffAssistManager`) handles the wide-runway case itself.
- **Runway lineup uses explicit thresholds, NOT width scaling.** Call `_steeringTone.UpdateHeadingErrorWithThresholds(error, silent=0.5°, activation=1°, maxPan=15°)`. Width scaling — even at the 25 ft / `MIN_SCALE = 0.65` clamp — gave silent ≈1.95° / activation ≈3.9°, which left pilots sitting 3° off heading with no audio cue (between silent and activation thresholds). The new precision values keep the tone panning until heading is centered within ½° and re-resume immediately past 1°. Do NOT call the width-scaled overload from the runway-lineup branch.
- **Lineup-aligned hysteresis** (governs the "Lined up" announcement): enter at heading <1° AND cross-track <10 ft; exit at >2° OR >20 ft. These are literals in `UpdateLineup` runway branch — tightened from 2°/5° / 15ft/30ft after the same too-loose-deadband bug.
- **Lineup pulse mode.** When stopped (≤3 kt) AND (heading error ≥5° OR cross-track ≥10 ft), the runway-lineup branch calls `_steeringTone.SetPulse(true)` — same pan direction, but tone toggles on/off at `PULSE_HZ = 3.0` (volume modulation, phase from `DateTime.UtcNow.Ticks`). Pure audio cue ("you've stopped but you're not done yet") with no speech — pilot's hands are on rudder + throttle and can't field verbal callouts. Forced off in the gate-lineup branch. **Cross-track condition is critical**: intercept-angle saturates at ±30° when cross-track is large, so a pilot who matches the saturated desired heading then stops would get heading error ≈ 0 (tone silent) even though cross-track is still huge. Without the cross-track branch here, that pilot has zero audio cue that they need to move forward to let the intercept controller close on centerline. Pulse keeps firing until BOTH dimensions are within the lineup-aligned hysteresis.
- **Runway lineup math is intercept-angle, not bearing-to-threshold.** `desiredHeading = runwayHeading + intercept · sign(crossTrack)`, with `intercept` rising on a sqrt curve from 0° (at `LINEUP_NOISE_DEADBAND_FEET = 8`) to 30° (at `LINEUP_INTERCEPT_SAT_FEET = 100`). Don't reintroduce a bearing-to-threshold blend — once the aircraft crosses the threshold (which happens during every "line up and wait"), the threshold is behind the aircraft and bearing-to-threshold sits on the ±180° wrap, producing chaotic sign flips on GPS jitter.
- **Lineup state entry must reset the heading-error smoother.** `_smoothedHeadingError = 0; _headingErrorInitialized = false;` at every `LiningUp` entry path (both Continue-past-hold-short and gate `HandleArrival`). Without the reset, the taxi-phase low-pass residual (often 50–80°) leaks into the lineup tone for ~300 ms and steers the pilot off the runway at low speed.
- **No feet-quantity verbal cues for blind-pilot guidance.** "42 feet left of centerline" has no spatial reference for a blind pilot — the tone is the instrument for cross-track. Heading numbers are fine (every pilot has a heading instrument).
- **Threading.** `TaxiGuidanceManager._stateLock` serializes the SimConnect-thread `UpdatePosition` against UI-thread mutators (`LoadRoute`, `StartGuidance`, `StopGuidance`, `ContinuePastHoldShort`, `GetStatusAnnouncement`). Any new public method that touches `_route` / `_state` / `_currentSegmentIndex` MUST acquire `_stateLock`. `TaxiSteeringTone` has its own `_lock` covering `UpdateHeadingError` / `UpdateHeadingErrorWithThresholds` / `SetPulse` / `Pause` / `Resume` / `Start` / `Stop` so audio buffer ops can't race with disposal.
- Magnetic → true heading conversion uses `magVariation` (east positive) before comparing to graph bearings.
- Off-route detection uses **perpendicular cross-track distance** (equirectangular projection, clamped to segment endpoints). Do not switch to endpoint-distance comparisons — that breaks on long segments.
- Hold-short node naming picks **connector-style** names (letter+digit like `A5`) over plain parallel names (`A`) when both are available on the same hold-short node. Preserve this ranking in `TaxiGraph` hold-short resolution.
- **"Where Am I" (Output > `Alt+Y`)** — `TaxiGraph.DescribeLocation(lat, lon)` returns `Taxiway X` / `Gate X` / `Runway X` for the nearest airport. It does NOT depend on guidance being active; the manager caches a query-only graph in `_whereAmICachedGraph`. **Ground-only by design** — gated on `MainForm._lastOnGround` (cached from `SIM_ON_GROUND`); announces `"In flight."` when airborne. Airborne queries belong to the separate LocationInfo hotkey (city/terrain). **Runway detection** uses `TaxiGraph.RunwayCenterlines` — paired runway-start positions from the navdatareader `start` table — not `taxi_path.type='R'` edges (the DB has none). The pair is found by matching reciprocal headings within ±15° and a threshold separation of 200–6000 m. Without this, a pilot standing mid-runway only got a "Runway X" callout within 50 m of a threshold node. Note: hotkey must NOT collide with output `Shift+Y` (`HOTKEY_STATUS_DISPLAY`) — Win32 silently rejects duplicate-chord registrations.
- **Landing Exit Planner (Input > `Shift+X`)** — pre-touchdown exit picker. `LandingExitPlanner` edge-detects airborne→on-ground with GS ≥ 40 kt and auto-activates `TaxiGuidanceManager.LoadRoute(...)` using the pre-built graph. Reuses the existing ILS destination runway/airport (via `simConnectManager.GetDestinationRunway()`/`GetDestinationAirport()`) when set — do not duplicate runway-selection UI. **MainForm's SIM_ON_GROUND handler always uses `RequestAircraftPositionAsync` to feed `ProcessGroundState`** — do NOT trust `LastKnownPosition` here. The cached position is only updated by VISUAL_GUIDANCE / TAKEOFF_ASSIST / TAXI_GUIDANCE paths, and during a hand-flown approach with visual guidance off, none of those fire — the cache stays at whatever the last active path left there (typically the departure-airport taxi-out at GS ~10 kt). Feeding that stale GS to `ProcessGroundState` fails the planner's `GS ≥ 40 kt` "real landing" gate and the activation is silently skipped. The async request adds one SimConnect roundtrip (~33 ms at 30 Hz) — negligible inside the rollout window — and guarantees fresh GS / lat / lon at the moment of the SIM_ON_GROUND change. `_activatedThisLanding` inside ActivateGuidance + a HasPendingExit recheck inside the async callback together prevent double-fire if SIM_ON_GROUND bounces (oleo flicker on hard landings). The `lastKnownPosition` mirror in cases 505/506/507 of SimConnectManager remains because other consumers (TCAS altitude diff, WeatherRadarForm altitude readout, Where-Am-I) still benefit from a fresher cache — but the landing-exit gate cannot rely on it. **`SetExit(..., bool currentlyAirborne)`** arms `_wasAirborne` from the actual air/ground state, NOT unconditionally true. Source: `simConnectManager.LastKnownOnGround` (mirrored from MainForm's SIM_ON_GROUND handler). Wrong-side fix: setting unconditionally to `true` while ON THE GROUND would meet the activation condition on the next ground-state event with GS≥40, false-triggering during a high-speed taxi or rejected takeoff. Honoring actual state means an on-ground plan correctly waits for the next takeoff+land cycle. Form's runway combo items each carry their own wind suffix in display text (`RunwayChoice` wrapper, refreshed via `RefreshRunwayItemsWithWind` when the async `RequestWindInfo` callback resolves — marshal back to UI thread via `BeginInvoke`); the screen reader reads "30R, 12 knot headwind" on focus during dropdown navigation, no separate post-selection announcement needed. Suffix suppressed when `|headwind| < 3 kt`. Don't auto-recommend a specific exit — that needs aircraft-perf data we don't have; let the pilot judge from the wind number.
- **`TaxiAssistForm` aircraft-position freshness.** OnCalculateClicked refreshes `_aircraftLat/Lon/Heading` from `_simConnectManager.LastKnownPosition` immediately before route construction. Without this, the route starts from wherever the aircraft was when the FORM was opened — typically a pre-pushback gate position — and the post-pushback aircraft is already off-route from frame one, triggering the 3-second off-route detector and an immediate recalc. The form's `_simConnectManager` is optional (defaults null for callers that don't have one) but MainForm always passes it. `LastKnownPosition` is updated by every position-bearing SimConnect path (visual guidance, hand-fly, etc.) so it's nearly always within a frame of truth, even when the taxi-specific position monitor isn't active yet.
- **Weather Radar source selection (ActiveSky vs MSFS) — silent fallback.** `Services/ActiveSkyClient.cs` probes candidate HTTP ports (19285 default, 19286, 19287) **in parallel** with a 1.2-second per-port `CancellationToken` timeout against `/ActiveSky/API/GetMode`. First successful 2xx wins; cached port is tried alone on subsequent refreshes (instant). Worst-case detection time when AS is missing: ~1.2 s, not n×timeout. **Important: do NOT scan `%APPDATA%\HiFi\` for a settings-file port** — AS keeps gigabytes of weather logs/history in subdirectories and recursive `Directory.EnumerateFiles` there blocks the UI thread for many seconds (this WAS tried and pulled out). When AS is detected, `WeatherRadarForm.FetchAmbientAsync` pulls ambient/surface conditions from `/GetWeatherAreaJson?stations=` and the position-specific METAR from `/GetCurrentConditions`. **API doc gotcha**: despite the doc, `/GetCurrentConditions` returns a METAR-style ASCII string (`@POS 070835Z 14707KT 9999 -RA FEW311 29/23 Q1006 RMK ADVANCED INTERPOLATION`), NOT JSON. `GetWeatherAreaJson` with an empty stations list returns the documented JSON ambient/surface block; the empty-stations response also includes a `"NULL RMK NOT FOUND"` placeholder METAR which we filter out at parse time. Fallback is the existing SimConnect `AMBIENT_*` SimVar path. **Why prefer AS**: under ActiveSky, SimConnect's `AMBIENT_PRECIP_STATE` bitmask sticks on snow, `AMBIENT_IN_CLOUD` flickers, and wind values lag MSFS's interpolation. **In-cloud** is still read from SimConnect even under AS — MSFS knows where it renders clouds regardless of who set them. **Precipitation** is parsed first from the position METAR (`/GetCurrentConditions`) using a small WMO/ICAO weather-token decoder, then closest-station METAR from JSON, then SimConnect bitmask as last resort. **METAR-says-no-precip = "None", not fallthrough** — only an entirely missing METAR triggers the next source. **Turbulence**: AS reports a 1-100 scale that sits at ~25 in calm conditions (atmospheric baseline). We bucket into FAA AIM 7-1-23 categories (`light` / `moderate` / `severe` / `extreme`) and **hide the line entirely when ≤25** so users don't see "25/100" alarmingly when nothing is happening. **Visibility shown in both km and statute miles** in both source paths so it's useful regardless of US vs ICAO convention. **Silent fallback by design**: the status line shows only `Last updated: HH:mm:ss` — no "weather source: X" or "AS: detected on port Y" diagnostics. Users without AS see the same form they always saw; users with AS get the richer fields (surface wind+gust, ceiling, QNH, turbulence) automatically.
- **ActiveSky weather-update auto-announce.** `Services/ActiveSkyWeatherMonitor.cs` polls every 60 s on a `System.Windows.Forms.Timer` and announces only when AS has actually pulled fresh weather data. **Cadence signal: JSON `TimeStamp` field from `/GetWeatherAreaJson` (Unix epoch).** When AS refreshes at its configured interval (5 / 10 / 15 min), TimeStamp advances; when the user is sitting still and AS hasn't refreshed, it doesn't. Sanity guard: also require the normalized METAR content to differ — if AS turns out to update TimeStamp on every API call (request-time semantics rather than data-freshness), the content guard prevents per-poll announcements. Fallback when TimeStamp is 0/missing: pure normalized-METAR-content comparison (visibility / clouds / weather / CAVOK; strip wind / temp / pressure / timestamp because AS interpolates those continuously). **Announcement format** (full METAR decode, screen-reader friendly): `"Active sky weather updated. Decoded weather at <ICAO>. Wind: 123 at 4 knots[, gusting 15][, varying between 100 and 140]. Visibility: 10 kilometres or more. Clouds: Few at 1,500 feet, broken at 3,000 feet, overcast at 5,000 feet. Precipitation: None. Temperature: 20. Dew point: 10. Altimeter: 1013 (29.92 inches)."` Cloud groups, weather phenomena, temperature/dew, and altimeter are decoded directly from the METAR (the JSON only gives a single ceiling and surface fields). Closest-station METAR (`/GetClosestStationWeather`) supplies the airport ICAO label; if that endpoint returns nothing, fall back to the position METAR labelled "your position". Wind/surface vis/temp/QNH from JSON are used only when the METAR fails to parse those fields. **Silent for non-AS users**: each tick that fails to detect AS produces no announcement and no error. **First-poll silence**: baseline established without announcement; same on AS-came-back after a drop. Started unconditionally in `MainForm` constructor; disposed in cleanup. **Don't move the WMO/ICAO weather-token decoder out of `WeatherRadarForm.ParsePrecipFromMetar`** — the monitor has its own copy in `WeatherRadarFormPrecipShim` to avoid making the form helper public; if you change one, change both.
- **METAR Report form (Output Shift+M) — dual VATSIM + ActiveSky display, silent when AS absent.** `Forms/METARReportForm.cs` starts compact (500×400, original VATSIM-only layout). On `Load`, async-detects AS via `_activeSky.IsRunningAsync()`. If AS is detected, the AS METAR section becomes visible (TextBox below VATSIM one), the form grows to 500×580, and the close button shifts down. If AS isn't detected, the form stays compact and the user sees the original VATSIM-only experience — no "(ActiveSky not running)" status text or other indicator. On Enter in the ICAO field, both METARs are fetched in parallel (`VATSIMService.GetMETARAsync` + `_activeSky.GetMetarAsync`) when AS is up; only VATSIM otherwise. AS textbox `Visible = false` initially (so `TabStop` skips it automatically when hidden). Tab order is ICAO → VATSIM METAR → AS METAR (skipped if hidden) → Close.
- **Heading-independent start-node selection when a taxiway sequence is given.** `LoadRoute` first tries `_graph.FindNearestNodeOnTaxiway(lat, lon, taxiwaySequence[0])` and falls back to the heading-aware `FindNearestNodeInDirection` only if no node on the requested first taxiway exists nearby. Why: post-pushback the aircraft can be pointing 180° away from where the first taxiway is (e.g., ATC told them to face NE for pushback, but the cleared taxi route runs SW). The heading-aware fallback would pick an apron node "ahead" of the aircraft instead of the requested taxiway, the route's approach segments would diverge from the aircraft's heading, and the off-route detector would fire on the first frame of taxi. Snapping directly to the user's requested first taxiway makes the constrained route honor the clearance regardless of pushback orientation; the pilot will turn after pushback, and the lineup tone guides them onto the first segment naturally.
- **ILS spatial+heading fallback for orphaned fs2024 rows.** The fs2024 vanilla navdata extraction has 213 ILS rows where `loc_airport_ident`, `loc_runway_name`, AND `loc_runway_end_id` are all NULL/empty — the ILS row itself is correct (right ident, frequency, location, heading) but the join columns weren't populated by navdatareader. KPHX, KORD, and several other major airports are affected (KPHX has 5 such orphans including 07R). fs2020 has zero orphans. `LittleNavMapProvider.GetILSForRunway` uses the direct `loc_airport_ident = ICAO AND loc_runway_name = name` query as the fast path; on miss it falls through to `GetILSForRunwayFallback` which: (a) looks up the runway end's threshold lat/lon and heading, (b) searches unlinked ILS rows within a 0.1° (~11 km) bounding box of the airport whose `loc_heading` is within ±5° of the runway heading (with ±180° wrap handling), (c) picks the closest by squared-distance to the threshold. Localizer antennas sit on the runway centerline beyond the far end so closest-by-distance matching is unambiguous. Wired into both ILS code paths: `GetILSForRunway` (used by ILS-guidance lookup) and `CreateRunwayFromReader` (which sets `Runway.ILSFreq/ILSHeading` from `runway_end.ils_ident`, also empty for KPHX 07R in fs2024). For fs2020 users this is a no-op; for fs2024 users it re-links the orphans at query time without requiring a navdata rebuild. `ReadILSFromReader` is shared between fast and fallback paths so the projection stays consistent.
- **DB operational-flag filtering — broad scenery compatibility.** `Database/Models/Runway.cs` has `IsClosed`, `IsLanding`, `IsTakeoff` flags (defaults: open / can-land / can-takeoff — PERMISSIVE). Read from `runway_end.has_closed_markings` / `is_landing` / `is_takeoff` via `LittleNavMapProvider.SafeReadBool(reader, columnName, defaultValue)` — handles missing column / NULL / int-as-bool gracefully. **TaxiAssistForm filters its destination dropdown to `!IsClosed && IsTakeoff`; LandingExitForm filters to `!IsClosed && IsLanding`.** Sparse DBs (most navdatareader builds) populate every row permissively → no behavior change. Rich DBs (third-party scenery, some Navigraph merges) → automatic filtering. When adding new DB-backed fields that may not exist on every build, ALWAYS use `SafeReadBool` (or a similar safe-read helper) with a permissive default — do NOT use `Convert.ToInt32(reader["col"])` directly.
- **Per-row "Hold short of runway" picker** in `TaxiAssistForm` (mnemonic `Alt+O` — cycles across the first row + every dynamic row). `TaxiGuidanceManager.LoadRoute` accepts `Dictionary<int, string>? userRunwayHoldShorts` mapping taxiway-sequence index → runway designator; `ApplyUserRunwayHoldShorts` finds the first segment after that taxiway whose endpoint sits on the chosen runway's centerline (via the existing `WhichRunwayContains` helper) and tags it as a hold-short. Runs BEFORE auto-detection (`InsertRunwayCrossingHoldShorts`) so the user-set `HoldShortRunway` label wins where both fire on the same segment. If the route doesn't cross the requested runway between this taxiway and the next, the method returns a warning string that's appended to the route summary announcement — the route still loads. The auto-detector remains the primary mechanism (covers most ATC clearances since FAA mandates hold-short of every crossed runway); the explicit picker is for confirmation and rare clearance/route mismatches.
- **Constrained-router runway bridge.** ATC clearances commonly contain consecutive taxiways separated by a runway crossing — e.g., *"K14 hold short 30L M17"* at OMDB. K14 ends at the 30L hold-short on its side; M17 starts at the 30L hold-short on the opposite side. They share NO graph node, so the previous `FindBestIntersection` failure path bailed to whole-route shortest path, ditching the user's clearance entirely. `TaxiRouter.FindRunwayBridge(currentTaxiway, nextTaxiway)` finds the closest pair of nodes between them within `MAX_BRIDGE_METERS = 200`; on success the constrained search routes along the current taxiway to its exit, free-A*-bridges across the runway, and resumes the constrained sequence at the entry on the next taxiway. The 200 m cap prevents silent half-airport jumps when an ATC clearance is genuinely wrong (in those cases, fall back to shortest path with a clear log line). Applied at both the step-1 (first→second taxiway) and the inner-loop (i→i+1) intersection lookups.
- **TaxiSteeringTone pulse-state reset.** `_pulseActive` is reset to `false` in both `Start()` and `Stop()`. Without this, a previous lineup session that ended with `SetPulse(true)` would leak its pulse state into the next route — first Taxiing-phase `UpdateHeadingError` call uses the width-scaled overload, which doesn't touch `_pulseActive`, so the inherited true would pulse the taxiing tone at 3 Hz. Always reset audio-modulation state on start/stop boundaries — don't trust caller-side cleanup.
- **TaxiSteeringTone volume refresh every sounding frame.** `SetTone` always calls `_toneGenerator.UpdateVolume(EffectiveVolume())` while sounding, regardless of `_pulseActive`. The previous "only refresh in pulse mode" optimization left the tone stuck at zero volume during a pulse→continuous transition: when the user is stopped-misaligned (pulse fires) and then starts moving, `SetPulse(false)` is called, but if the last pulse cycle had set the volume to 0 (silent half), the next frame in continuous mode skipped UpdateVolume and the tone stayed silent until something else triggered a state change (oversteer / going silent / Pause). Always refreshing the volume on every sounding frame is cheap (one float assign per ~30 Hz tick) and removes the entire class of bug.
- **Verbal turn direction is computed from aircraft heading, NOT route's static `TurnDirection`.** `ComputeTurnVerbalFromHeading(targetBearing, aircraftHeadingTrue)` derives the spoken "left / slight right / continue" from the angular difference between the aircraft's current true heading and the next segment's bearing — same input the steering tone uses for its pan, so the two always agree. The route's pre-computed `TaxiRouteSegment.TurnDirection` is `nextSeg.bearing - currentSeg.bearing` and assumes the aircraft is exactly on-axis with the current segment. When the aircraft is off-axis (post-pushback rotation, after a wide turn, brief deviation, or starting at the gate before moving) the actual turn it must make to align with the next segment can be the OPPOSITE direction from the route's intent — and the static verbal cue contradicted the (correct) tone. All three spoken sites — advance notice, "now" callout, status query (`GetStatusAnnouncement`) — go through the helper. The `TurnDirection != "straight"` predicates stay on the static field (those just check whether there's *any* turn at the junction; that doesn't depend on aircraft heading).
- **Parking listing parity with the gate-teleport dialog.** `TaxiAssistForm` (parking destination) builds its dropdown from `IAirportDataProvider.GetParkingSpots(icao)` — the same data source `GateTeleportForm` uses — labelled with `ParkingSpot.ToString()` (e.g. `"P 21 - Ramp GA Large (Jetway)"`). Routing endpoint is the nearest graph node within `MAX_PARKING_TO_GRAPH_M = 100 m`; the parking spot's actual lat/lon is the lineup convergence target, matching `SimConnectManager.TeleportToParkingSpot`. Don't drive the listing off graph parking-tagged nodes — that silently drops parking spots whose lat/lon lacks a nearby graph node (common in third-party scenery whose taxi paths lag the parking layout).
- **Auto-inserted runway-crossing hold-shorts.** `TaxiGuidanceManager.InsertRunwayCrossingHoldShorts(route, destinationName)` runs after `LoadRoute` builds the route, AFTER `TruncateToHoldShort`. For every segment pair where the next segment's endpoint lies on a runway centerline (geometry via the existing `TaxiGraph.RunwayCenterlines`, half-width tolerance), it tags the current segment `IsHoldShortPoint = true` with `HoldShortRunway = "runway X"`. Skip the destination runway (already handled by `TruncateToHoldShort`) and skip duplicate consecutive same-runway tags. This implements FAA AIM 4-3-18 / ICAO Doc 4444: explicit hold-short and ATC continue at every runway crossing on the route. Don't disable this — VATSIM controllers expect it, and silently rolling across an active runway is a runway-incursion risk.
- **Crossings-based taxiway connectivity + full-airport fallback.** `TaxiGraph.GetConnectedTaxiwayNames(name)` BFS counts **named-taxiway crossings** (default `maxCrossings = 2`), not raw graph hops — walking along the seed taxiway and through unnamed connectors is free; only crossing into a different named taxiway consumes the budget. The previous hop-based 4-edge limit silently hid M1 from the M5 dropdown at KSFO (4–6 unnamed connectors physically lie between them) and similar patterns elsewhere. `TaxiAssistForm`'s "Add Taxiway" combo additionally lists every airport taxiway (via `GetAllTaxiwayNames()`) below the connected ones — the heuristic prioritizes the dropdown for the common case while the full list ensures the user can match any ATC-named taxiway even when the heuristic doesn't surface it. The constrained-path router and `FindRunwayBridge` resolve the actual route from any pair of selections. `GetReachableTaxiwayNames(name, maxCrossings)` is public for callers wanting a different budget.
- **Ground-speed announcer.** Periodic GS callout in `UpdatePosition` controlled by `UserSettings.TaxiGuidanceGroundSpeedAnnounceInterval` (0=off, 5, 10). Round-to-nearest-multiple via `Math.Round(gs / interval, AwayFromZero)` — 4/5/6 kt all read as "5 knots", 9/10/11 read as "10 knots". The previous floor-bucket implementation flipped between "0" and "5" every time the raw value crossed 5.000, producing announcements that bore no resemblance to the actual speed. Hysteresis: once a bucket has been announced, the new bucket must be reached with a 0.5 kt margin past its rounding boundary before re-announcing — kills jitter at the new midpoint (e.g., 7.5 kt with interval=5) so a steady throttle near a boundary doesn't alternate "5"/"10". First sample after `LoadRoute` establishes baseline silently. Goes through plain `_announcer.Announce` (NOT `AnnounceInstruction`) so the fading "10 knots" callout doesn't displace the most recent actionable instruction in the Repeat-Last buffer. **Source field is `taxiData.GroundVelocityKnots` (real GS), NOT IAS.** The TAKEOFF_ASSIST_DATA struct has both fields; takeoff assist still reads IAS for V-speed callouts (correct), but cross-feature consumers — and the GS announcer in particular — must use real GS. At low taxi speeds IAS reads near zero (pitot pressure below indicator threshold), which made the announcer say "0 kt" at 5-kt actual GS before the GROUND VELOCITY field was added to the struct.
- **Tactical and safety-critical taxi announcements use `AnnounceImmediate`, not `Announce`.** `AnnounceInstruction` (the helper used for turns, hold-shorts, taxiway changes, lineup, arrival, and distance countdowns) calls `_announcer.AnnounceImmediate` internally — every tactical callout interrupts queued speech because the pilot needs the cue *now*, not after a fading "10 knots" GS callout finishes. The same applies to standalone safety callouts (speed warnings, runway-crossing alerts, off-route warnings, lineup-achieved, parking arrival). Two sites still use plain `_announcer.Announce`: (a) the `LoadRoute` route summary at start of guidance (informational, not time-critical), (b) the periodic GS announcer (must not displace the Repeat-Last buffer). When adding new taxi callouts, default to `AnnounceInstruction` / `AnnounceImmediate`; justify any plain `Announce` call explicitly.

Details: [docs/taxi-guidance.md](docs/taxi-guidance.md).

### Multi-Aircraft Architecture

**Core interfaces:**
- **IAircraftDefinition** - Contract for all aircraft
- **BaseAircraftDefinition** - Recommended base class (provides hotkey routing, caching, helpers)
- **FlyByWireA320Definition** - Reference implementation

**Each aircraft defines:**
- `GetVariables()` - All simulator variables
- `GetPanelStructure()` - Section/panel hierarchy
- `BuildPanelControls()` - Panel-to-variables mapping (cached automatically by base class)
- `GetHotkeyVariableMap()` - Simple hotkey action → event name mappings
- `HandleHotkeyAction()` - Custom hotkey logic (optional override)

## Quick Reference

### Adding Panel Control
1. Add to aircraft's `GetVariables()` with `UpdateFrequency.OnRequest`
2. Add variable key to `BuildPanelControls()` under appropriate panel
3. Test - automatic registration and UI generation

### Adding Background Monitoring
1. Add to `GetVariables()` with `UpdateFrequency.Continuous` + `IsAnnounced = true`
2. Do NOT add to `BuildPanelControls()` - batched monitoring is automatic
3. Change detection and announcements are automatic (supports 1000 variables)

### Adding New Aircraft
1. Create class inheriting `BaseAircraftDefinition`
2. Override: `GetVariables()`, `GetPanelStructure()`, `BuildPanelControls()`
3. Add menu item in `MainForm.Designer.cs` + click handler
4. Add to `LoadAircraftFromCode()` switch statement
5. Use `FlyByWireA320Definition.cs` as template

### Variable Types
- **K:EVENT** - Standard MSFS events (via SimConnect TransmitClientEvent)
- **L:VARIABLE** - Local variables (reading aircraft state)
- **H:EVENT** - Hardware events (via MobiFlight WASM module)
- **PMDGVar** - PMDG SDK variables (read via Client Data Area broadcast)

### PMDG 777 Specific Patterns

**Switch control:** Use CDA (SetClientData) with direct position values for most switches.
- Two-position toggles: `SendPMDGEvent(eventName, eventId, targetPosition)` where targetPosition is 0 or 1
- Multi-position selectors: same, with the target position index
- Momentary buttons: `SendPMDGEvent(eventName, eventId, 1)` — parameter 1 = pressed, 0 = no-op
- Continuous knobs (brightness, temperature, EFIS baro/mins): **cannot be controlled via SDK** — do not add to panels
- **Fuel control levers:** Exception — use CDA with **inverted** parameter (1=Cutoff, 0=Run). See special case in HandleUIVariableSet.
- **Ground power switches (ELEC_ExtPwr):** Momentary push buttons — send parameter 1 regardless of target. See special case in HandleUIVariableSet.

**Radio frequencies and transponder:** Use standard SimConnect events (not PMDG SDK):
- `COM_STBY_RADIO_SET_HZ` / `COM2_STBY_RADIO_SET_HZ` for setting standby freqs
- `COM_STBY_RADIO_SWAP` / `COM2_RADIO_SWAP` for swapping active/standby
- `XPNDR_SET` for squawk code (BCD16 encoded)

**CDU array index convention:** The PMDG SDK uses `0=Captain(L), 1=F/O(R), 2=Observer(C)` for ALL crew-position arrays — including CDU data areas (`PMDG_777X_CDU_0/1/2`), `CDU_BrtKnob[3]`, and all `CDU_annun*[3]` fields. The CDU form dropdown is ordered Left/Center/Right (0/1/2), which does NOT match the SDK ordering. `PMDG777CDUForm` uses a `DataCDUIndex` computed property to remap dropdown index to SDK index (`1→2, 2→1`). The event prefix switch (`EVT_CDU_L_/C_/R_`) uses the raw dropdown index and is correct as-is.

**CDU interaction:** CDU buttons must send parameter 1 (pressed) via CDA; parameter 0 also registers as a press (not a release). Text entry sends one character at a time with 350ms delay; repeated characters need an extra 400ms for the CDU to distinguish separate presses. CDU display uses color and font-size data to detect toggle selections (non-white color or non-small font = selected, marked with `X`). Toggle detection only applies to rows with adjacent `<>` (mapped from 0xA1/0xA2 arrow symbols). Scratchpad announcements are suppressed during text entry and clearing (`_typingInProgress`/`_clearingInProgress` flags); `_previousScratchpad` is only updated when the announcement actually fires. CLR uses `_clearingInProgress` to suppress intermediate states and only announces "Cleared" once the scratchpad is empty.

**MCP dialogs:** Use `ValueInputForm` with `ToggleButtonDef` for mode toggles. Opened non-modal (`Show()`, not `ShowDialog()`) with `ShowCancelButton = false` so other windows remain accessible. Dialogs stay open after value entry (callback pattern). `MCP_IASBlank` indicates FMC-controlled speed. VS/FPA dialog uses `inputEnabledCheck` to gate input on mode engagement (`MCP_annunVS_FPA`). `EVT_MCP_VS_SET` requires VS mode to be engaged first ("VS window open").

**VS/FPA event naming (SDK names are misleading):** `EVT_MCP_VS_SWITCH` (69855) is the **engage/disengage** button. `EVT_MCP_VS_FPA_SWITCH` (69852) is the **VS↔FPA display mode toggle**. Confirmed by live sim testing — do not trust the SDK naming alone.

**Announcements:** Use `Announce()` (queued) in ProcessSimVarUpdate, `AnnounceImmediate()` only in HandleHotkeyAction. `IsAnnounced = true` is required for continuous monitoring registration. Suppress button push state (_Sw_Pushed) announcements via RenderAsButton check. Annunciator lights announce both on and off states. For variables needing cache but no auto-announcement, set `IsAnnounced = true` and return `true` from ProcessSimVarUpdate to suppress.

### PMDG 777 EFB Bridge

The EFB (Electronic Flight Bag) tablet is made accessible via a JavaScript bridge injected through an MSFS mod package override.

**Architecture:** A standalone MSFS Community package (`zzz-pmdg-efb-accessibility`) overrides the EFB's `PMDGTabletCA.html` to load an additional JS script. The `zzz-` prefix ensures it loads after the PMDG package alphabetically, so our HTML takes precedence. The JS bridge communicates with the C# app via HTTP on `localhost:19777`.

**Key components:**
- **`EFBBridgeServer`** (`SimConnect/EFBBridgeServer.cs`) — HttpListener with `/ping`, `/state` (POST), `/commands` (GET) endpoints. JS pushes state, C# queues commands. Command queue capped at 50 entries; `HasPendingCommand()` enables deduplication. Auto-restarts listener on unexpected failures (5 retries, 2s delay). Start/Stop protected by lock. Fires `Error` event on server failures.
- **`EFBModPackageManager`** (`Patching/EFBModPackageManager.cs`) — Installs/updates/removes the mod package. Reads original PMDG HTML at install time (no PMDG IP in repo), appends bridge script tag with double-patch guard (checks for existing script tag before appending). Auto-updates bridge JS on app startup via `BridgeVersion` constant.
- **`pmdg-efb-accessibility-bridge.js`** (`Resources/`) — Runs inside MSFS Coherent GT. Hooks into EFB's `MessageService.messaging_bus` EventBus. Must be Coherent GT compatible (no `AbortSignal.timeout`, top-level try-catch, `var` not `let/const`, no arrow functions, `.indexOf()` not `.includes()`). Critical state types are queued on POST failure and flushed on reconnection (max 20 pending, 3 retries per entry). `tryConnect` has a connecting guard to prevent concurrent attempts; `navigraphStateSent` flag prevents duplicate Navigraph state posts.
- **`PMDG777EFBForm`** (`Forms/PMDG777/`) — Accessible form with SimBrief, Navigraph, Preferences tabs. Opened via Shift+T in input mode. Shows connection status (always visible above tabs, announced on transitions). Buttons disable on click and re-enable on response or timeout. SimBrief fetch: 30s timeout. Navigraph auth: 60s timeout.

**JS bridge constraints (Coherent GT):**
- No `AbortSignal.timeout()` — use manual Promise-based timeout
- Top-level try-catch wrapping entire script — errors must never break the EFB
- `layout.json` in the mod package must have exact file sizes — MSFS validates these
- Sim must be restarted after mod package install/update for MSFS to load new files
- The JS file is copied while the sim is closed (sim locks files while running)

**Communication flow:**
- JS → C#: `POST /state` with `{type, data}` JSON (state updates, auth codes, SimBrief data). Failed POSTs for critical state types are queued and retried on reconnection.
- C# → JS: `GET /commands` polled every 500ms, returns JSON array of `{command, payload}`. Commands expire after 30s if not polled.
- Bridge connects on startup, retries every 5s if server unavailable. On reconnection, flushes pending states and re-sends Navigraph auth status.

## Detailed Documentation

**Claude: Read these docs only when the task specifically requires them.**

**When to read detailed docs:**
- **Adding complex features or workflows** → [Adding Features](docs/adding-features.md), [Quick Reference](docs/QUICK-REFERENCE.md)
- **Implementing new aircraft** → [Architecture](docs/architecture.md), [Adding Features](docs/adding-features.md)
- **Working with FCU/MCP/display systems** → [Architecture](docs/architecture.md)
- **Adding or modifying hotkeys** → [Hotkey System](docs/hotkey-system.md)
- **Fenix rotary encoders (RMP, FCU)** → [Fenix Increment/Decrement](docs/fenix-increment-decrement.md)
- **Tuning visual guidance PID controller** → [Visual Guidance](docs/visual-guidance.md)
- **Working on taxi guidance (graph, router, tone, form)** → [Taxi Guidance](docs/taxi-guidance.md)
- **Understanding variable patterns** → [Variable System](docs/variable-system.md)
- **API reference** → [Aircraft Definitions](docs/aircraft-definitions.md)
- **Dependencies and key files** → [Development](docs/development.md)

**Available documentation:**
- **[Quick Reference](docs/QUICK-REFERENCE.md)** - Common patterns and workflows (read first for most tasks)
- **[Architecture](docs/architecture.md)** - Core components, multi-aircraft system, FCU architecture
- **[Adding Features](docs/adding-features.md)** - Step-by-step workflows for common development tasks
- **[Variable System](docs/variable-system.md)** - Three patterns for managing variables (Panel, Monitoring, Hotkey)
- **[Fenix Increment/Decrement](docs/fenix-increment-decrement.md)** - Counter-based pattern for Fenix rotary encoders
- **[Visual Guidance](docs/visual-guidance.md)** - PID controller tuning and ground track monitoring
- **[Taxi Guidance](docs/taxi-guidance.md)** - Turn-by-turn taxi assistance, steering tone, ATC-constrained routing
- **[Aircraft Definitions](docs/aircraft-definitions.md)** - Multi-aircraft dictionary system API reference
- **[Hotkey System](docs/hotkey-system.md)** - Dual-mode hotkeys and multi-aircraft routing
- **[Development](docs/development.md)** - Dependencies, key files, development notes

## Technology Stack

.NET 9 (C# 13), Windows Forms, SimConnect SDK (MSFS), SQLite, NVDA/Tolk (screen readers)
