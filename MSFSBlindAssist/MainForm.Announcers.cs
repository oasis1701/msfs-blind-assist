using System.Collections.Concurrent;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.FenixA320;
using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Forms.PMDG777;
using MSFSBlindAssist.Forms.HS787;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Patching;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;

public partial class MainForm
{
    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (InvokeRequired)
        {
            // PRODUCER: Enqueue event for batch processing instead of immediate BeginInvoke
            // This reduces UI thread marshaling overhead by ~95% for high-volume updates (400+ vars/sec)
            if (Interlocked.Increment(ref queuedEventCount) <= MAX_QUEUE_SIZE)
            {
                eventQueue.Enqueue(e);
            }
            else
            {
                // Queue full - drop event and track for diagnostics
                Interlocked.Decrement(ref queuedEventCount);
                Interlocked.Increment(ref droppedEventCount);

                // Log overflow warning (throttled to prevent log spam)
                if (droppedEventCount % 100 == 1)
                {
                    Log.Debug("MainForm", $"WARNING: Event queue overflow! Dropped {droppedEventCount} events. Consider increasing MAX_QUEUE_SIZE or reducing variable count.");
                }
            }
            return;
        }

        // CONSUMER: Process event on UI thread (called from ProcessEventBatch)
        // Step 1: ALWAYS store the value first (needed by all consumers)
        currentSimVarValues[e.VarName] = e.Value;

        // Initial-snapshot fast path: populate caches and refresh UI controls
        // but skip all announcement paths. These events represent "what the
        // cockpit looked like when the app started", not user-triggered
        // transitions, so announcing them would spam the user on every launch.
        if (e.IsInitialSnapshot)
        {
            UpdateControlFromSimVar(e.VarName, e.Value);
            // Also mirror to displayValues so panel display textboxes have
            // the right initial content when first rendered.
            if (currentAircraft.GetVariables().ContainsKey(e.VarName) &&
                GetDisplayVarNamesCached().Contains(e.VarName))
            {
                displayValues[e.VarName] = e.Value;
            }
            return;
        }

        // FBW A380 engine-mode-selector watchdog: the cockpit ENG START knob only fans
        // ignition to engines 1+2 on builds whose template defaults ENGINE_COUNT=2 (the
        // A320 inheritance), so engines 3+4 motor but never light. The knob updates
        // XMLVAR_ENG_MODE_SEL (monitored as ENGINE_MODE_SELECTOR); mirror its position onto
        // engines 3+4 via TURBINE_IGNITION_SWITCH_SET3/4 (live-verified to address + light
        // the outboard engines). Keys on the selector var only → no feedback loop; harmless
        // when MSFSBA's own Engine Mode Selector combo is used (it already fires SET1-4).
        if (currentAircraft?.AircraftCode == "FBW_A380" && e.VarName == "ENGINE_MODE_SELECTOR")
        {
            int igPos = (int)Math.Round(e.Value);
            if (igPos >= 0 && igPos <= 2)
            {
                simConnectManager?.ExecuteCalculatorCode($"{igPos} (>K:TURBINE_IGNITION_SWITCH_SET3)");
                simConnectManager?.ExecuteCalculatorCode($"{igPos} (>K:TURBINE_IGNITION_SWITCH_SET4)");
            }
            // Fall through so ENGINE_MODE_SELECTOR still auto-announces its position.
        }

        // FBW A380 STD-flag watchdog: in STD the FCU forces the stock altimeter to
        // exactly 1013.25 hPa, but its KOHLSMAN SETTING STD write is TRANSITION-only —
        // a session that starts with STD already engaged reads a stale 0 (observed
        // live 2026-06-11), which mis-read the Altimeter STD combo/hotkey. When the
        // MB mirror sits at the STD constant for >2 s with the flag still 0, back-fill
        // the flag (everything keys on it). ONE direction only (0→1): the FCU's
        // exit-write is same-tick reliable, and correcting 1→0 could fight a
        // mid-transition frame. The def suppresses these vars' announcements.
        if (currentAircraft?.AircraftCode == "FBW_A380" &&
            (e.VarName == "BARO_MB_WATCH_L" || e.VarName == "BARO_MB_WATCH_R"))
        {
            bool baroCapt = e.VarName == "BARO_MB_WATCH_L";
            bool atStdConstant = Math.Abs(e.Value - 1013.25) < 0.02;
            double? stdFlag = simConnectManager?.GetCachedVariableValue(
                baroCapt ? "A32NX_FCU_LEFT_EIS_BARO_IS_STD" : "A32NX_FCU_RIGHT_EIS_BARO_IS_STD");
            if (atStdConstant && (stdFlag ?? 0) < 0.5)
            {
                var nowUtc = DateTime.UtcNow;
                var since = baroCapt ? _a380BaroStdMismatchL : _a380BaroStdMismatchR;
                if (since == DateTime.MinValue)
                {
                    if (baroCapt) _a380BaroStdMismatchL = nowUtc; else _a380BaroStdMismatchR = nowUtc;
                }
                else if ((nowUtc - since).TotalSeconds > 2)
                {
                    simConnectManager?.ExecuteCalculatorCode($"1 (>A:KOHLSMAN SETTING STD:{(baroCapt ? 1 : 2)}, Bool)");
                    if (baroCapt) _a380BaroStdMismatchL = DateTime.MinValue; else _a380BaroStdMismatchR = DateTime.MinValue;
                }
            }
            else
            {
                if (baroCapt) _a380BaroStdMismatchL = DateTime.MinValue; else _a380BaroStdMismatchR = DateTime.MinValue;
            }
            // Fall through; the def's ProcessSimVarUpdate returns true for these keys.
        }

        // Step 2: Handle special one-off announcements (terminal cases only)
        if (HandleSpecialAnnouncements(e))
        {
            return; // These are terminal - no further processing needed
        }

        // Step 2.5: Allow aircraft-specific variable processing (e.g., FCU display combining)
        // This lets each aircraft handle complex variables before generic processing.
        //
        // The HS787 auto-announces ~100 of its vars from INSIDE ProcessSimVarUpdate, which returns
        // true and exits this method (line below) BEFORE reaching either the generic disabled-monitor
        // gate OR the generic _uiSetEcho gate further down. So two suppressions that work for every
        // other aircraft (which announce on the generic path) silently never fire on the 787:
        //   (1) monitor-manager mute (Ctrl+M),
        //   (2) UI-set echo — don't re-announce a value the user JUST set via a combo (the screen
        //       reader already spoke the combo selection).
        // Both are fixed here the same way: ProcessSimVarUpdate auto-announces ONLY via the
        // suppressible announcer.Announce(...) (its AnnounceImmediate calls are all hotkey readouts),
        // so suppress the announcer just for this var's processing — the per-branch state/baseline
        // updates still run, only the speech is dropped.
        bool hs787 = currentAircraft!.AircraftCode == "HS_787";
        bool hs787Muted = hs787 &&
            Settings.SettingsManager.Current.HS787DisabledMonitorVariablesSet.Contains(e.VarName);
        // Same silent-no-op class for the A32NX family: the A320 (EFIS baro) and the
        // Headwind A330 (stock-Kohlsman altimeter) announce those vars from INSIDE
        // ProcessSimVarUpdate, which returns true and exits before the generic
        // A32NXDisabledMonitorVariables gate below — so a Ctrl+M un-tick never muted
        // them. Suppress right here, exactly like the HS787.
        bool a32nxMuted = (currentAircraft.AircraftCode == "A320" || currentAircraft.AircraftCode == "HW_A330") &&
            Settings.SettingsManager.Current.A32NXDisabledMonitorVariablesSet.Contains(e.VarName);
        // UI-set echo suppression — applies to EVERY aircraft, not just the HS787 (was the bug).
        // A def that auto-announces from INSIDE ProcessSimVarUpdate (the PMDG APU selector + the
        // Boris Audio Works soundpack switches, the HS787, the A380, ...) returns true and exits
        // this method BEFORE the generic _uiSetEcho gate further down ever runs, so without
        // suppressing right here the value the user JUST set via a combo is spoken TWICE: once by
        // the screen reader (the combo selection) and again by the def. Match on the time window
        // ONLY (not the value): a combo set can write a different encoding than the SDK reads back
        // (event position vs struct field, 0/1 vs 0/100), so a value compare silently misses and
        // the double-announce survives. The user just touched THIS control, so any change to it
        // inside the short echo window IS the echo. The generic value-matched gate below still
        // guards the non-def-handled announce path and its own baseline accuracy.
        bool uiEcho = _uiSetEcho.TryGetValue(e.VarName, out var ue)
            && Environment.TickCount64 - ue.tick < UiSetEchoSuppressMs;
        bool suppressDefAnnounce = hs787Muted || a32nxMuted || uiEcho;
        bool prevSuppressed = announcer.Suppressed;
        if (suppressDefAnnounce) announcer.Suppressed = true;
        bool wasProcessedByAircraft;
        try
        {
            wasProcessedByAircraft = currentAircraft.ProcessSimVarUpdate(e.VarName, e.Value, announcer);
        }
        finally
        {
            if (suppressDefAnnounce) announcer.Suppressed = prevSuppressed;
        }
        if (wasProcessedByAircraft)
        {
            // The def announced (suppressed) and updated its own baseline — consume the echo so a
            // later change from any source still announces. (If the def did NOT handle it, the echo
            // is left intact for the generic _uiSetEcho gate further down.)
            if (uiEcho) _uiSetEcho.Remove(e.VarName);
            // Update window title if flight phase changed (for aircraft that track flight phases)
            if (!string.IsNullOrEmpty(currentAircraft.CurrentFlightPhase))
            {
                this.Text = $"MSFS Blind Assist - {currentAircraft.CurrentFlightPhase} phase active";
            }
            // Check StateVariable reverse lookup only (don't call full UpdateControlFromSimVar
            // which can interfere with aircraft-specific processing — we tried it and combo
            // programmatic updates appear to trigger the user-action SIC handler despite the
            // updatingFromSim flag for HS787 vars whose write handler toggles state).
            UpdateButtonStateFromStateVariable(e.VarName, e.Value);
            return; // Aircraft handled it completely, no further generic processing needed
        }

        // Step 3: Update display values (if this variable is used in any panel display)
        // This happens silently without announcements - users read the display manually
        // (cached name set — this gate runs PER EVENT; see GetDisplayVarNamesCached)
        if (currentAircraft.GetVariables().ContainsKey(e.VarName) &&
            GetDisplayVarNamesCached().Contains(e.VarName))
        {
            displayValues[e.VarName] = e.Value;

            // Signal completion for pending requests
            if (pendingDisplayRequests != null && pendingDisplayRequests.ContainsKey(e.VarName))
            {
                pendingDisplayRequests[e.VarName].TrySetResult(true);
            }

            // Repaint the display list if visible — COALESCED. During the auto-refresh tick the
            // whole panel is force-read at once, so N responses land in quick succession; without
            // debouncing, each would rebuild + reconcile the entire list (O(N) × N). Schedule one
            // repaint instead.
            if (currentControls.ContainsKey("_DISPLAY_") && currentControls["_DISPLAY_"] is ListBox)
            {
                ScheduleDisplayRepaint();
            }
            // DON'T return - continue processing for announcements if needed
        }

        // Step 4: Update UI controls (if this variable has a control in current panel)
        UpdateControlFromSimVar(e.VarName, e.Value);

        // Step 5: Handle pending state announcements (button press feedback)
        if (pendingStateAnnouncements.TryRemove(e.VarName, out _))
        {
            AnnounceVariableState(e.VarName, e.Value);
            // DON'T return - might also need continuous monitoring
        }

        // Step 6: Process continuous monitoring for auto-announcements
        // Only announce variables marked with IsAnnounced = true and UpdateFrequency = Continuous
        if (currentAircraft.GetVariables().ContainsKey(e.VarName))
        {
            var varDef = currentAircraft.GetVariables()[e.VarName];
            if (varDef.IsAnnounced && varDef.UpdateFrequency == UpdateFrequency.Continuous)
            {
                // INDICATED_ALTITUDE is continuously monitored only to feed the 1,000-ft
                // crossing announcer (HandleSpecialAnnouncements); never speak it as a raw
                // "Altitude: 5234" through the generic gate. Display/feed already ran above.
                if (e.VarName == "INDICATED_ALTITUDE") return;

                // Check if disabled in Fenix Monitor Manager
                if (currentAircraft.AircraftCode == "FENIX_A320CEO" &&
                    Settings.SettingsManager.Current.FenixDisabledMonitorVariablesSet.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in PMDG Announcement Monitor. AircraftCode
                // for PMDG aircraft starts with "PMDG_" (e.g. "PMDG_777") so
                // a single prefix check covers any future PMDG additions
                // sharing the same disabled-variables list.
                if (currentAircraft.AircraftCode.StartsWith("PMDG_", StringComparison.Ordinal) &&
                    Settings.SettingsManager.Current.PMDGDisabledMonitorVariablesSet.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in the A380 Monitor Manager.
                if (currentAircraft.AircraftCode == "FBW_A380" &&
                    Settings.SettingsManager.Current.A380DisabledMonitorVariablesSet.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in the A32NX Monitor Manager. The Headwind A330
                // is an A32NX fork that reuses the same monitor-manager form and the
                // same A32NXDisabledMonitorVariables setting.
                if ((currentAircraft.AircraftCode == "A320" || currentAircraft.AircraftCode == "HW_A330") &&
                    Settings.SettingsManager.Current.A32NXDisabledMonitorVariablesSet.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // Check if disabled in the HS787 Monitor Manager.
                if (currentAircraft.AircraftCode == "HS_787" &&
                    Settings.SettingsManager.Current.HS787DisabledMonitorVariablesSet.Contains(e.VarName))
                {
                    return; // Skip announcement for disabled variable
                }

                // For PMDG variables, build the description from ValueDescriptions
                // since PMDG events don't carry description strings like SimConnect does
                string description = e.Description;
                if (string.IsNullOrEmpty(description) && varDef.ValueDescriptions.Count > 0)
                {
                    if (varDef.ValueDescriptions.TryGetValue(e.Value, out string? desc))
                        description = $"{varDef.DisplayName}: {desc}";
                    else if (!varDef.OnlyAnnounceValueDescriptionMatches)
                        description = $"{varDef.DisplayName}: {e.Value}";
                }
                else if (string.IsNullOrEmpty(description))
                {
                    description = $"{varDef.DisplayName}: {e.Value}";
                }

                // Generic ARINC429 auto-decode for the announce path (only reached for vars
                // the aircraft's ProcessSimVarUpdate did NOT handle, so existing ad-hoc ARINC
                // announce branches are untouched — no double-decode). Renders the spoken value
                // decoded instead of a raw word.
                if (currentAircraft is BaseAircraftDefinition arincAnnDef &&
                    arincAnnDef.TryDecodeArinc429(e.VarName, e.Value, out string arincSpoken))
                {
                    description = $"{varDef.DisplayName}: {arincSpoken}";
                }

                // Suppress the duplicate echo of a value the user JUST set via the UI (the
                // screen reader already spoke the combo). Update the baseline silently so a
                // later change to this var from any OTHER source still announces. Consumed
                // once; only a value matching what the user set within the window is dropped.
                if (_uiSetEcho.TryGetValue(e.VarName, out var echo)
                    && Math.Abs(echo.value - e.Value) < 0.001
                    && Environment.TickCount64 - echo.tick < UiSetEchoSuppressMs)
                {
                    _uiSetEcho.Remove(e.VarName);
                    simVarMonitor.SetBaseline(e.VarName, e.Value);
                    return;
                }

                simVarMonitor.ProcessUpdate(e.VarName, e.Value, description);
            }
        }
    }

    /// <summary>
    /// CONSUMER: Process batched events from the queue on UI thread.
    /// Called by eventBatchTimer every EVENT_BATCH_INTERVAL_MS (~33ms).
    /// Drains the queue in controlled batches to prevent UI thread freezing.
    /// </summary>
    private void ProcessEventBatch(object? sender, EventArgs e)
    {
        int processedCount = 0;
        int batchStartQueueSize = queuedEventCount;

        // Drain queue in batches (up to MAX_BATCH_SIZE events per timer tick)
        // This prevents UI freezing if queue contains thousands of events
        while (processedCount < MAX_BATCH_SIZE && eventQueue.TryDequeue(out SimVarUpdateEventArgs? eventArgs))
        {
            Interlocked.Decrement(ref queuedEventCount);

            // Call OnSimVarUpdated directly on UI thread (InvokeRequired will be false)
            // This executes the exact same logic as before, just batched instead of individual
            OnSimVarUpdated(this, eventArgs);

            processedCount++;
        }
    }

    /// <summary>
    /// Handles special announcements that should terminate processing.
    /// Returns true if the event was handled and no further processing is needed.
    /// </summary>
    private bool HandleSpecialAnnouncements(SimVarUpdateEventArgs e)
    {
        // NOTE: Aircraft-specific ProcessSimVarUpdate() is now called in the main flow (line 206)
        // to avoid duplicate calls. Flight phase window title updates happen there.

        // Feed g-force to the landing-rate tracker so it can capture the peak touchdown g
        // inside the post-touchdown window (the ReadLastLandingPeakG hotkey). Not announced.
        // HOISTED to the top of this ladder: G_FORCE is registered HighFrequency=true
        // (BaseAircraftDefinition), i.e. it fires on every SIM_FRAME — every branch below
        // this one otherwise re-tests its own (much lower frequency) VarName first on every
        // single frame for no reason. Pure reorder; none of the string-equality checks below
        // can also match "G_FORCE", so moving this first changes no other branch's behavior.
        if (e.VarName == "G_FORCE")
        {
            landingRateAnnouncer.ProcessG(e.Value);
            return true;
        }

        // 1,000-foot crossing callouts. INDICATED_ALTITUDE is also a panel-display var, so
        // this is a NON-terminal feed (no early return) — processing continues so the
        // display box still updates. The var is registered IsAnnounced=false (per aircraft),
        // so the generic announce gate stays silent and only these callouts speak.
        if (e.VarName == "INDICATED_ALTITUDE")
        {
            altitudeCalloutAnnouncer.ProcessAltitude(e.Value, _lastOnGround);
        }

        // Handle FCU hotkey value announcements
        if (e.VarName == "FCU_HEADING" || e.VarName == "FCU_SPEED" || e.VarName == "FCU_ALTITUDE" ||
            e.VarName == "FCU_HEADING_WITH_STATUS" || e.VarName == "FCU_SPEED_WITH_STATUS" ||
            e.VarName == "FCU_ALTITUDE_WITH_STATUS" || e.VarName == "FCU_VSFPA_VALUE")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // Ground-speed announcer. GROUND_VELOCITY is a continuous base variable (always
        // monitored while connected). Route it to the dedicated announcer's bucket/hysteresis
        // logic and return true so the generic "value changed" announcement is suppressed.
        // The announcer self-gates on the interval setting AND on the on-ground state
        // (_lastOnGround, cached from SIM_ON_GROUND) — GS callouts are on-ground only.
        if (e.VarName == "GROUND_VELOCITY")
        {
            groundSpeedAnnouncer.ProcessGroundSpeed(e.Value, _lastOnGround, takeoffAssistManager.IsActive);
            // Taxiway-name augmentation is fetched ONLY for the active flight's departure and
            // destination (the airports you actually taxi at, both force-fresh) plus on demand when
            // you type an ICAO into the gate-teleport dialog. The old 50 NM geofence scan was removed
            // — it added background fetching for airports you never taxi at, with no benefit.
            return true;
        }

        // Touchdown vertical speed is monitored only so the ReadLastLandingRate hotkey can
        // read it from the cache (it's latched by the sim at touchdown). It must never be
        // spoken as a generic "value changed" call-out — swallow it here.
        if (e.VarName == "PLANE_TOUCHDOWN_NORMAL_VELOCITY")
        {
            return true;
        }

        // Handle takeoff assist toggle activation (receives position from RequestPositionForTakeoffAssist)
        if (e.VarName == "POSITION_FOR_TAKEOFF_ASSIST")
        {
            if (e.PositionData.HasValue)
            {
                var pos = e.PositionData.Value;

                // If takeoff assist isn't already active AND doesn't already have a
                // reference, try to seed one. Probe order:
                //   (1) taxi-guidance lineup reference (the common case — pilot taxied
                //       to the runway via taxi guidance)
                //   (2) under-aircraft runway detection (pilot taxied manually; the
                //       runway centerline geometry is available from the airport's
                //       taxi graph, so we can identify the runway from position +
                //       heading alone — same geometry Where-Am-I uses)
                //   (3) (no fallback here — TakeoffAssistManager.Toggle's no-reference
                //       branch will create a synthetic centerline from current
                //       position and heading)
                if (!takeoffAssistManager.IsActive && !takeoffAssistManager.HasRunwayReference)
                {
                    // (1) Taxi-guidance lineup
                    bool seeded = false;
                    if (taxiGuidanceManager.TryGetRunwayLineupReference(
                        out double rwyLat, out double rwyLon,
                        out double rwyHdgTrue, out double rwyHdgMag,
                        out string rwyId, out string rwyIcao))
                    {
                        if (!string.IsNullOrEmpty(rwyId))
                        {
                            takeoffAssistManager.SetRunwayReference(
                                rwyLat, rwyLon, rwyHdgTrue, rwyHdgMag, rwyId, rwyIcao);
                            seeded = true;
                        }
                    }

                    // (2) Under-aircraft detection — only when on the ground. Same
                    //     ICAO-resolution pattern as Where-Am-I (canonical 4-char
                    //     ICAOs only; the 3-char idents the DB also returns are for
                    //     fields the taxi-graph layer can't load).
                    if (!seeded && _lastOnGround && airportDataProvider != null)
                    {
                        var nearby = airportDataProvider
                            .GetNearbyAirportICAOs(pos.Latitude, pos.Longitude, 5.0)
                            .Where(c => c != null && c.Length == 4)
                            .ToList();
                        if (nearby.Count > 0 &&
                            taxiGuidanceManager.TryDetectRunwayUnderAircraft(
                                airportDataProvider, nearby[0],
                                pos.Latitude, pos.Longitude,
                                pos.HeadingMagnetic, pos.MagneticVariation,
                                out double detLat, out double detLon,
                                out double detHdgTrue, out double detHdgMag,
                                out string detRwyId, out string detIcao))
                        {
                            takeoffAssistManager.SetRunwayReference(
                                detLat, detLon, detHdgTrue, detHdgMag, detRwyId, detIcao);
                        }
                    }
                }

                takeoffAssistManager.Toggle(pos.Latitude, pos.Longitude, pos.HeadingMagnetic, pos.MagneticVariation);
            }
            return true;
        }

        // Handle takeoff assist position updates (for centerline tracking)
        if (e.VarName == "TAKEOFF_ASSIST_POSITION" && takeoffAssistManager.IsActive)
        {
            if (e.PositionData.HasValue)
            {
                var pos = e.PositionData.Value;
                takeoffAssistManager.ProcessPositionUpdate(pos.Latitude, pos.Longitude, pos.HeadingMagnetic);
            }
        }

        // Handle takeoff assist pitch updates
        if (e.VarName == "TAKEOFF_ASSIST_PITCH" && takeoffAssistManager.IsActive)
        {
            takeoffAssistManager.ProcessPitchUpdate(e.Value);
        }

        // Handle takeoff assist IAS updates (for speed callouts)
        if (e.VarName == "TAKEOFF_ASSIST_IAS" && takeoffAssistManager.IsActive)
        {
            takeoffAssistManager.ProcessSpeedUpdate(e.Value);
        }

        // Handle taxi guidance position updates (active during Taxiing, LiningUp,
        // AND LandingRollout phases). LandingRollout is critical: BeginLandingRollout
        // sets state=LandingRollout and UpdateLandingRollout's per-frame logic (auto-
        // transition to Taxiing on slowdown, distance-based callouts) only runs if
        // UpdatePosition is fed every frame. Without LandingRollout in this gate, the
        // touchdown announcement fires once and then the state-machine is silent
        // until StopGuidance.
        if (e.VarName == "TAXI_GUIDANCE_POSITION" &&
            (taxiGuidanceManager.State == TaxiGuidanceState.Taxiing ||
             taxiGuidanceManager.State == TaxiGuidanceState.LiningUp ||
             taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout))
        {
            if (e.PositionData.HasValue)
            {
                var pos = e.PositionData.Value;
                // DIAGNOSTIC: log the first TAXI_GUIDANCE_POSITION event we
                // dispatch while in LandingRollout, so we can tell whether the
                // per-frame data is actually flowing during the rollout phase.
                if (taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout &&
                    !_diagLoggedFirstRolloutPos)
                {
                    _diagLoggedFirstRolloutPos = true;
                    try
                    {
                        _landingExitLog.Info(
                            $"[MF] First TAXI_GUIDANCE_POSITION in LandingRollout: " +
                            $"lat={pos.Latitude:F6} lon={pos.Longitude:F6} hdgMag={pos.HeadingMagnetic:F1} " +
                            $"magVar={pos.MagneticVariation:F2} gs={pos.GroundSpeedKnots:F1}");
                    }
                    catch { }
                }
                taxiGuidanceManager.UpdatePosition(
                    pos.Latitude, pos.Longitude,
                    pos.HeadingMagnetic, pos.MagneticVariation,
                    pos.GroundSpeedKnots);
            }
        }

        // Docking guidance runs on every TAXI_GUIDANCE_POSITION frame regardless of
        // taxi-guidance state — it has its own guard (_gate == null / disabled / not Idle
        // → reset), so calling it every frame is safe and cheap. NOTE the feed itself is
        // taxi-scoped: OnTaxiGuidanceStateChanged stops position monitoring when taxi
        // reaches Arrived/Inactive, so docking gets NO frames after that point. That is
        // fine by design — arrival ownership is engage-latched (docking has either already
        // finished, or never engaged and taxi announced the arrival), the parked solid
        // tone is self-sustaining until the pilot presses Stop, and stale docking state is
        // cleared at the next flight boundary (takeoff-assist / LandingRollout) or healed
        // by the absolute-distance disengage on the next route's frames.
        if (e.VarName == "TAXI_GUIDANCE_POSITION" && e.PositionData.HasValue)
        {
            var pos = e.PositionData.Value;
            dockingGuidanceManager.UpdatePosition(
                pos.Latitude, pos.Longitude,
                pos.HeadingMagnetic, pos.MagneticVariation,
                pos.GroundSpeedKnots);

            // Exactly one panning tone at a time, and ENGAGE-LATCHED arrival ownership.
            // Docking owns the PRECISE final lineup: once it is engaged (within ~50 m of the
            // stop, roughly aligned, slow) it pans an intercept-angle cue to the gate centerline,
            // so mute the taxi steering tone AND taxi's terminal arrival callouts while docking
            // is active (Docking or Stopped). Before engagement taxi speaks and steers normally —
            // critical for navdata gates where docking may NEVER engage (approach outside the 70°
            // cone, stop beyond engage range, approximate navdata heading): the pilot still gets
            // taxi's full arrival sequence instead of total verbal silence. The brief overlap case
            // (taxi says "Stop. Hold position." at its route-end node and docking engages a moment
            // later with "Docking guidance… X to stop") is sequential and self-correcting — far
            // better than the silent-arrival failure mode of suppressing for the whole approach.
            // IsActive / IsArmedAwaitingEngage are lock-free volatile snapshots — no lock cost.
            bool dockingActive = dockingGuidanceManager.IsActive;
            taxiGuidanceManager.SetSteeringToneSuppressed(dockingActive);
            taxiGuidanceManager.SetDockingActive(dockingActive);
            // Pre-engage window: docking armed with the GSX stop still ahead — taxi's
            // arrival wording redirects forward instead of saying "parking brake" at
            // the navdata point (KATL F3 2026-06-11: 26 s parked short, docking Armed).
            taxiGuidanceManager.SetDockingPending(dockingGuidanceManager.IsArmedAwaitingEngage);
        }

        // Cache SIM_ON_GROUND on every update, regardless of which features are
        // currently active. AnnounceWhereAmI uses this to silence itself in
        // flight (it's a ground-only feature — there's a separate location/city
        // hotkey for airborne queries). The landing-exit planner forwarding is
        // gated separately on HasPendingExit, but the cache must always run.
        if (e.VarName == "SIM_ON_GROUND")
        {
            bool onGround = e.Value >= 0.5;
            bool justTouchedDown = onGround && !_lastOnGround;
            _lastOnGround = onGround;
            // Mirror to SimConnectManager so other components (LandingExitForm,
            // etc.) that have a SimConnectManager reference can read the latest
            // air/ground state without a separate MainForm dependency.
            simConnectManager.LastKnownOnGround = onGround;

            // Auto-deactivate visual guidance on touchdown: from this moment on,
            // the landing-exit planner / taxi guidance take over the rollout and
            // taxi guidance respectively, so the dual-tone guidance no longer
            // has a useful job. Keeping it running would compete with the taxi
            // steering tone audibly. Only fires on the airborne→on-ground edge,
            // so a user who manually engages visual guidance on the ramp for any
            // reason (preflight test, etc.) is not surprised by auto-deactivation.
            if (justTouchedDown && visualGuidanceManager.IsActive)
            {
                visualGuidanceManager.Toggle();
            }

            // Open the peak-g capture window at the touchdown edge, seeded with the g at contact,
            // so the ReadLastLandingPeakG hotkey reports the impact spike. The landing RATE itself
            // is read live from the persistent PLANE_TOUCHDOWN_NORMAL_VELOCITY cache by its hotkey.
            if (justTouchedDown)
            {
                landingRateAnnouncer.OnTouchdown(
                    simConnectManager.GetCachedVariableValue("G_FORCE") ?? 1.0);
            }

            // Feed SIM_ON_GROUND transitions to the landing-exit planner so it
            // can detect touchdown and auto-activate taxi guidance to the
            // pre-selected exit. ALWAYS request a fresh aircraft position at
            // this moment — do NOT trust SimConnectManager.LastKnownPosition.
            //
            // Why: lastKnownPosition is only updated by VISUAL_GUIDANCE,
            // TAXI_GUIDANCE, and TAKEOFF_ASSIST data paths. None of those
            // fire during a hand-flown approach without visual guidance
            // enabled. In that case the cached position is whatever the
            // last active path left there — typically the departure-airport
            // taxi-out at GS ~10 kts. Feeding that to ProcessGroundState
            // fails the planner's GS≥40 kt "real landing" gate and the
            // activation is silently skipped at touchdown. Always going
            // through RequestAircraftPositionAsync costs one SimConnect
            // roundtrip (~33 ms at 30 Hz) — negligible inside the rollout
            // window — and guarantees fresh GS / lat / lon at the moment
            // the planner needs them.
            //
            // _activatedThisLanding inside ActivateGuidance + a
            // HasPendingExit recheck inside the callback together prevent
            // double-fire if SIM_ON_GROUND bounces (oleo flicker on hard
            // landings).
            if (landingExitPlanner.HasPendingExit)
            {
                bool capturedOnGround = onGround;
                simConnectManager.RequestAircraftPositionAsync(p =>
                {
                    if (!landingExitPlanner.HasPendingExit) return;
                    double hdgTrue = p.HeadingMagnetic + p.MagneticVariation;
                    landingExitPlanner.ProcessGroundState(
                        capturedOnGround, p.GroundSpeedKnots, p.Latitude, p.Longitude, hdgTrue);
                });
            }
        }

        // Keep the open Taxi Assist form's cached position fresh so that
        // when the user presses Calculate (especially during a mid-taxi
        // route amendment), the route starts from the CURRENT position.
        if (e.VarName == "TAXI_GUIDANCE_POSITION" &&
            taxiAssistForm != null && !taxiAssistForm.IsDisposed && taxiAssistForm.Visible &&
            e.PositionData.HasValue)
        {
            var pos = e.PositionData.Value;
            taxiAssistForm.UpdateAircraftPosition(pos.Latitude, pos.Longitude, pos.HeadingMagnetic);
        }

        // Handle hand fly mode pitch updates
        if (e.VarName == "PLANE_PITCH_DEGREES" && handFlyManager.IsActive)
        {
            // Convert radians to degrees and negate (SimConnect uses body axis: negative = nose up)
            double pitchDegrees = -(e.Value * (180.0 / Math.PI));
            handFlyManager.ProcessPitchUpdate(pitchDegrees);
            // Don't return - allow data to flow to visual guidance too
        }

        // Handle hand fly mode bank updates
        if (e.VarName == "PLANE_BANK_DEGREES" && handFlyManager.IsActive)
        {
            // Convert radians to degrees (positive = right bank, negative = left bank)
            double bankDegrees = e.Value * (180.0 / Math.PI);
            handFlyManager.ProcessBankUpdate(bankDegrees);
            // Don't return - allow data to flow to visual guidance too
        }

        // Handle hand fly mode heading updates
        if (e.VarName == "PLANE_HEADING_DEGREES_MAGNETIC" && handFlyManager.IsActive)
        {
            // Convert radians to degrees
            double headingDegrees = e.Value * (180.0 / Math.PI);
            handFlyManager.ProcessHeadingUpdate(headingDegrees);
            // Don't return - allow data to flow to visual guidance too
        }

        // Handle hand fly mode vertical speed updates
        if (e.VarName == "HAND_FLY_VERTICAL_SPEED" && handFlyManager.IsActive)
        {
            // Already in feet per minute
            handFlyManager.ProcessVerticalSpeedUpdate(e.Value);
            return true;
        }

        // Handle visual guidance position updates
        // Handle visual guidance position updates (AIRCRAFT_POSITION struct)
        if (e.VarName == "VISUAL_GUIDANCE_POSITION" && visualGuidanceManager.IsActive && e.PositionData != null)
        {
            var pos = e.PositionData.Value;

            // Update position data from AIRCRAFT_POSITION struct
            visualGuidanceManager.UpdateLatitude(pos.Latitude);
            visualGuidanceManager.UpdateLongitude(pos.Longitude);
            visualGuidanceManager.UpdateAltitudeMSL(pos.Altitude);
            visualGuidanceManager.UpdateHeading(pos.HeadingMagnetic);
            visualGuidanceManager.UpdateGroundSpeed(pos.GroundSpeedKnots);
            visualGuidanceManager.UpdateVerticalSpeed(pos.VerticalSpeedFPM);

            // Note: AGL is updated separately via VISUAL_GUIDANCE_AGL handler
            // ProcessUpdate() is called when AGL arrives to ensure all data is complete

            return true;
        }

        // Handle visual guidance AGL updates (requested separately)
        if (e.VarName == "VISUAL_GUIDANCE_AGL" && visualGuidanceManager.IsActive)
        {
            visualGuidanceManager.UpdateAGL(e.Value);

            // Process the update now that all position data should be available
            visualGuidanceManager.ProcessUpdate();
            return true;
        }

        // Handle visual guidance ground track updates (for PID drift detection)
        if (e.VarName == "VISUAL_GUIDANCE_GROUND_TRACK" && visualGuidanceManager.IsActive)
        {
            visualGuidanceManager.UpdateGroundTrack(e.Value);
            return true;
        }

        // Visual guidance attitude (pitch / bank) now comes from VG's own SimConnect
        // monitoring batch — no longer dependent on HandFly being active. Heading is
        // already populated by the VG position update above.
        if (e.VarName == "VISUAL_GUIDANCE_PITCH" && visualGuidanceManager.IsActive)
        {
            // SimConnect pitch is positive=nose down (Euler convention); negate to
            // standard right-handed convention (positive=nose up).
            double pitchDegrees = -(e.Value * (180.0 / Math.PI));
            visualGuidanceManager.UpdatePitch(pitchDegrees);
            return true;
        }
        if (e.VarName == "VISUAL_GUIDANCE_BANK" && visualGuidanceManager.IsActive)
        {
            // SimConnect bank is left-positive; VisualGuidanceManager.StandardBank() applies
            // the sign conversion at the consumer side, so we pass the raw SimConnect value
            // (just converted from radians to degrees).
            double bankDegrees = e.Value * (180.0 / Math.PI);
            visualGuidanceManager.UpdateBank(bankDegrees);
            return true;
        }
        if (e.VarName == "VISUAL_GUIDANCE_AOA" && visualGuidanceManager.IsActive)
        {
            // INCIDENCE ALPHA from SimConnect arrives in radians. VG smooths and sanity-gates
            // it consumer-side; we just convert and forward.
            double aoaDegrees = e.Value * (180.0 / Math.PI);
            visualGuidanceManager.UpdateAoA(aoaDegrees);
            return true;
        }

        // Handle aircraft variable hotkey announcements
        // A380 metric-altitude mode (FCU MTRS / A32NX_METRIC_ALT_TOGGLE): when active, the
        // current-altitude readouts (A = MSL, Q = AGL) speak metres instead of feet. Gated to
        // the A380 by both the aircraft-type check and the MetricAlt flag — no other aircraft
        // and no non-metric A380 state reach this branch, so feet behaviour is unchanged.
        if ((e.VarName == "ALTITUDE_MSL" || e.VarName == "ALTITUDE_AGL")
            && currentAircraft is Aircraft.FlyByWireA380Definition a380Alt)
        {
            // Metric on -> "X meters"; metric off -> "X feet". Previously the off case fell
            // through and spoke just the number with no unit — now it says "feet" for
            // consistency with the "meters" suffix.
            if (a380Alt.MetricAlt) announcer.AnnounceImmediate($"{e.Value * 0.3048:0} meters");
            else announcer.AnnounceImmediate($"{e.Value:0} feet");
            return true;
        }

        if (e.VarName == "ALTITUDE_AGL" || e.VarName == "ALTITUDE_MSL" || e.VarName == "AIRSPEED_INDICATED" ||
            e.VarName == "AIRSPEED_TRUE" || e.VarName == "GROUND_SPEED" || e.VarName == "MACH_SPEED" ||
            e.VarName == "VERTICAL_SPEED" || e.VarName == "HEADING_MAGNETIC" || e.VarName == "HEADING_TRUE" ||
            e.VarName == "BANK_ANGLE" || e.VarName == "PITCH_ANGLE" ||
            e.VarName == "SPEED_GD" || e.VarName == "SPEED_S" || e.VarName == "SPEED_F" ||
            e.VarName == "SPEED_VFE" || e.VarName == "SPEED_VLS" || e.VarName == "SPEED_VS" ||
            e.VarName == "FUEL_QUANTITY" || e.VarName == "FUEL_QUANTITY_KG" || e.VarName == "GROSS_WEIGHT" || e.VarName == "GROSS_WEIGHT_KG" || e.VarName == "FLAP_POSITION" || e.VarName == "GEAR_POSITION" || e.VarName == "WAYPOINT_INFO" ||
            e.VarName == "OUTSIDE_TEMP" || e.VarName == "SQUAWK_CODE" ||
            e.VarName == "LOCAL_TIME_SECONDS" || e.VarName == "ZULU_TIME_SECONDS")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // Handle destination runway distance announcements
        if (e.VarName == "DISTANCE_TO_RUNWAY")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // Handle ILS guidance announcements
        if (e.VarName == "ILS_GUIDANCE")
        {
            announcer.AnnounceImmediate(e.Description);
            return true;
        }

        // ECAM LED announcements are now handled by aircraft-specific ProcessSimVarUpdate()

        // Handle special display updates
        if (e.VarName == "DISPLAY_UPDATE")
        {
            return true;
        }

        // Handle FCU_VALUES special case
        if (e.VarName == "FCU_VALUES")
        {
            announcer.Announce(e.Description);
            return true;
        }

        // Handle ECAM message announcements (using queue for sequential delivery)
        if (e.VarName == "ECAM_MESSAGE")
        {
            announcer.AnnounceWithQueue(e.Description);
            return true;
        }

        return false; // Not a special case, continue normal processing
    }

    /// <summary>
    /// Announces the state of a variable based on its value descriptions.
    /// </summary>
    private void AnnounceVariableState(string varName, double value)
    {
        if (currentAircraft.GetVariables().ContainsKey(varName))
        {
            var varDef = currentAircraft.GetVariables()[varName];
            if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
            {
                string stateDescription = varDef.ValueDescriptions[value];
                announcer.AnnounceImmediate(stateDescription);
            }
            else
            {
                // Fallback to display name + value if no descriptions
                announcer.AnnounceImmediate($"{varDef.DisplayName}: {value}");
            }
        }
    }

    private void UpdateControlFromSimVar(string varName, double value)
    {
        bool controlFound = currentControls.ContainsKey(varName);

        if (controlFound)
        {
            updatingFromSim = true;

            Control control = currentControls[varName];
            if (control is TrackBar slider)
            {
                // Reflect a sim-side axis change back into the slider (updatingFromSim is set,
                // so the slider's ValueChanged handler won't write it back — no feedback loop).
                if (currentAircraft.GetVariables().TryGetValue(varName, out var sVarDef) && sVarDef.RenderAsSlider)
                {
                    double sspan = (sVarDef.SliderMax - sVarDef.SliderMin) == 0 ? 1 : (sVarDef.SliderMax - sVarDef.SliderMin);
                    int pct = (int)Math.Round((value - sVarDef.SliderMin) / sspan * 100.0);
                    pct = Math.Max(0, Math.Min(100, pct));
                    if (slider.Value != pct) slider.Value = pct;
                }
            }
            else if (control is ComboBox combo)
            {
                // Synthetic, MSFSBA-internal selector combos (the A32NX System Display page
                // picker A32NX_MSFSBA_SD_PAGE, the synthetic speed-brake combo, and the
                // thrust-lever _DETENT combos) are the SOLE source of truth for their own value:
                // the combo's SelectedIndex IS the state. They have no real, continuously
                // broadcast sim var to defer to — the backing L:var is written ONLY by the
                // user's own selection and is re-requested purely to repaint the status box.
                // Re-setting SelectedIndex from those (stale / async) round-trip reads yanks the
                // selection backward while the user is arrowing (the "wonky" A320 SD combo). Skip
                // the snap-back for them; the same update still flows on to repaint the box.
                // (The A380 SD combo is a REAL Continuous sim var whose broadcast always agrees
                // with the user's selection, so it is unaffected. Mirrors the synthetic-combo
                // exclusion list in FlyByWireA320Definition.cs.)
                bool isSyntheticSelector =
                    varName == "A32NX_MSFSBA_SD_PAGE" ||
                    varName == "A32NX_MSFSBA_SPEEDBRAKE" ||
                    varName.EndsWith("_DETENT", StringComparison.Ordinal);

                // Find the matching value in the combo box
                if (!isSyntheticSelector && currentAircraft.GetVariables().ContainsKey(varName))
                {
                    var varDef = currentAircraft.GetVariables()[varName];
                    if (varDef.ValueDescriptions.ContainsKey(value))
                    {
                        string description = varDef.ValueDescriptions[value];
                        int index = combo.Items.IndexOf(description);
                        if (index >= 0 && combo.SelectedIndex != index)
                        {
                            combo.SelectedIndex = index;
                        }
                    }
                }
            }
            else if (control is TextBox textBox && textBox.ReadOnly)
            {
                // Read-only status TextBox. Two flavors:
                //  (a) Continuous-numeric readout (RenderAsReadOnlyStatus + Units +
                //      no ValueDescriptions) — format as "<value:Format> <Units>".
                //  (b) Enum-style status field (door state, annunciator, etc.) —
                //      mirror the value through ValueDescriptions; fall back to
                //      raw numeric if the cached value isn't in the map.
                if (currentAircraft.GetVariables().ContainsKey(varName))
                {
                    var varDef = currentAircraft.GetVariables()[varName];
                    string newText;
                    bool isContinuousReadout =
                        varDef.RenderAsReadOnlyStatus &&
                        (varDef.ValueDescriptions == null || varDef.ValueDescriptions.Count == 0) &&
                        !string.IsNullOrEmpty(varDef.Units);
                    if (isContinuousReadout)
                    {
                        double displayValue = value * varDef.Scale + varDef.Offset;
                        newText = $"{displayValue.ToString(varDef.Format, System.Globalization.CultureInfo.InvariantCulture)} {varDef.Units}";
                    }
                    else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.TryGetValue(value, out string? desc))
                    {
                        newText = desc;
                    }
                    else
                    {
                        newText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (textBox.Text != newText)
                        textBox.Text = newText;
                }
            }
            else if (control is Button btn)
            {
                // Update stateful button label from StateVariable or ValueDescriptions
                if (currentAircraft.GetVariables().ContainsKey(varName))
                {
                    var varDef = currentAircraft.GetVariables()[varName];
                    if (!string.IsNullOrEmpty(varDef.StateVariable))
                    {
                        // This button uses a StateVariable — but this update is for the button's own variable,
                        // not the state variable. Skip — the state variable update will handle the label.
                    }
                    else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 0)
                    {
                        // Mirror the build-time button-label logic (the RenderAsButton branch):
                        // resting-state (value 0) suppression is OPT-IN via
                        // SuppressRestingButtonState, set only by the FBW momentary-button
                        // helpers (ECAM-CP keys, calls, acks, tests) — a momentary push-button
                        // has no meaningful RESTING state, so relabelling e.g. "ECAM All" to
                        // "ECAM All: Released" reads as noise. By DEFAULT the value-0 label
                        // shows: on PMDG ("LNAV: Off") and HS787 ("Baro STD: QNH") it IS
                        // meaningful state and must not be silenced. The functional dispatch
                        // keys on the var name/events, never the label, so this is cosmetic.
                        string newLabel = ((value != 0 || !varDef.SuppressRestingButtonState)
                                           && varDef.ValueDescriptions.TryGetValue(value, out string? stateText))
                            ? $"{varDef.DisplayName}: {stateText}"
                            : varDef.DisplayName;
                        if (btn.Text != newLabel)
                        {
                            btn.Text = newLabel;
                            btn.AccessibleName = newLabel;
                        }
                    }
                }
            }

            updatingFromSim = false;
        }

        // Also update any button labels whose StateVariable matches this variable
        UpdateButtonStateFromStateVariable(varName, value);
    }

    /// <summary>
    /// Updates button labels for any buttons whose StateVariable matches the given variable name.
    /// Separated from UpdateControlFromSimVar so it can be called independently without
    /// triggering control updates that could interfere with aircraft-specific processing.
    /// </summary>
    private void UpdateButtonStateFromStateVariable(string varName, double value)
    {
        foreach (var kvp in currentControls)
        {
            if (kvp.Value is Button stateBtn && currentAircraft.GetVariables().ContainsKey(kvp.Key))
            {
                var btnVarDef = currentAircraft.GetVariables()[kvp.Key];
                if (btnVarDef.StateVariable == varName)
                {
                    string stateLabel = $"{btnVarDef.DisplayName}: {(value != 0 ? "On" : "Off")}";
                    stateBtn.Text = stateLabel;
                    stateBtn.AccessibleName = stateLabel;
                }
            }
        }
    }

    private void RequestAllCurrentValues()
    {
        // The new continuous monitoring system automatically handles critical variables,
        // so we don't need to request ALL variables on connection anymore.
        // This dramatically improves connection performance.
        if (simConnectManager != null && simConnectManager.IsConnected)
        {
            Log.Debug("MainForm", "Connection established - continuous monitoring active for critical variables");
            // Continuous variables (IsAnnounced = true) are automatically requested every second
            // Panel variables are requested when panels are opened
            // Individual variables are requested on hotkey presses
        }
    }

    private void HandleButtonStateAnnouncement(string eventName)
    {
        // Check if this button has a corresponding state variable to announce
        if (currentAircraft.GetButtonStateMapping().ContainsKey(eventName))
        {
            string stateVarKey = currentAircraft.GetButtonStateMapping()[eventName];

            // Request the state after a short delay to allow the sim to update
            System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
            stateTimer.Interval = 300; // 300ms delay
            stateTimer.Tick += (s, e) =>
            {
                stateTimer.Stop();
                stateTimer.Dispose();

                // Request the current state and announce it
                if (currentAircraft.GetVariables().ContainsKey(stateVarKey))
                {
                    // Track this state announcement request
                    pendingStateAnnouncements.TryAdd(stateVarKey, true);

                    // Request with forceUpdate=true to ensure we get the update even if value hasn't changed
                    simConnectManager.RequestVariable(stateVarKey, forceUpdate: true);
                }
            };
            stateTimer.Start();
        }
    }

    private void UpdateDisplayText(ListBox displayBox)
    {
        if (GetPanelDisplayVarsCached().TryGetValue(currentPanel, out var displayVars))
        {
            var allVars = currentAircraft.GetVariables();
            List<string> values = new List<string>();

            foreach (var varKey in displayVars)
            {
                if (allVars.TryGetValue(varKey, out var varDef))
                {
                    // ALWAYS prefer SimConnectManager's lastVariableValues cache over the
                    // displayValues entry. The cache is written unconditionally, BEFORE any
                    // suppression, on every individual response AND every continuous-batch
                    // delivery — so it is at least as fresh as displayValues for every
                    // deliverable var. displayValues alone goes STALE for def-handled vars:
                    // when ProcessSimVarUpdate returns true, OnSimVarUpdated exits before the
                    // Step-3 displayValues write, so those rows (A32NX COM frequencies, A380
                    // EFIS baro, HS787 flight data) would freeze at their first-painted value
                    // forever — the old 3 s tick masked this by clearing displayValues every
                    // cycle via the Refresh button, which the live 1 s tick no longer does.
                    // displayValues remains the fallback for values delivered through paths
                    // that don't populate the cache, and covers stable continuous announced
                    // vars whose SimVarUpdated was suppressed (e.g. IRS POS_SET held at 1).
                    double? cached = simConnectManager?.GetCachedVariableValue(varKey);
                    if (cached.HasValue)
                    {
                        displayValues[varKey] = cached.Value;
                    }

                    if (displayValues.ContainsKey(varKey))
                    {
                        double value = displayValues[varKey];
                        string displayValue;

                        // Aircraft-specific decode for non-presentable raw values
                        // (e.g. ARINC429 baro/minimums words on the A380, which would
                        // otherwise render as a ~14-billion raw double).
                        if (currentAircraft.TryGetDisplayOverride(varKey, value, out string overrideText))
                        {
                            displayValue = overrideText;
                        }
                        // Generic ARINC429 auto-decode (after the ad-hoc override so baro/minimums/
                        // rudder etc. keep their custom logic; covers any IsArinc429 var with just
                        // value+unit, so a raw ~14-billion word never reaches a panel field).
                        else if (currentAircraft is BaseAircraftDefinition arincDef &&
                                 arincDef.TryDecodeArinc429(varKey, value, out string arincText))
                        {
                            displayValue = arincText;
                        }
                        // ARINC429 ENUM decode (mirrors the FBW ProcessSimVarUpdate announce
                        // guard): some announced FBW discretes (e.g. APU low fuel pressure)
                        // arrive as a huge SSM-encoded word (12884901888 = 0x3_00000000) that
                        // matches no 0/1 ValueDescription, so they'd render as a raw ~13-billion
                        // number. Decode to the 0/1 payload and map via ValueDescriptions.
                        else if (varDef.ValueDescriptions is { Count: > 0 } && value >= 4294967296.0
                                 && varDef.ValueDescriptions.TryGetValue(
                                        System.Math.Round(new SimConnect.Arinc429Word(value).ValueOr(0f)), out string? arincEnumDesc))
                        {
                            displayValue = arincEnumDesc;
                        }
                        // Check if we have value descriptions (like Off/Aligning/Aligned)
                        else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsKey(value))
                        {
                            displayValue = varDef.ValueDescriptions[value];
                        }
                        else
                        {
                            // Use numeric formatting for values without descriptions
                            string unit = "";
                            string formattedValue = "";

                            switch (varDef.Units)
                            {
                                case "volts":
                                    formattedValue = $"{value:F1}";
                                    unit = "V";
                                    break;
                                case "millibars":
                                    formattedValue = $"{value:F2}";
                                    unit = " hPa";
                                    break;
                                case "inHg":
                                    formattedValue = $"{value:F2}";
                                    unit = " inHg";
                                    break;
                                case "kHz":
                                    // Convert kHz to MHz for display (better precision)
                                    double freqMHz = value / 1000.0;
                                    formattedValue = $"{freqMHz:F3}";
                                    unit = " MHz";
                                    break;
                                default:
                                    formattedValue = $"{value:F0}";
                                    break;
                            }

                            displayValue = $"{formattedValue}{unit}";
                        }

                        values.Add($"{varDef.DisplayName}: {displayValue}");
                    }
                    else
                    {
                        values.Add($"{varDef.DisplayName}: --");
                    }
                }
            }

            // Split any multi-line entries (the SD-page override block is one display var
            // whose value is a multi-row block) into one list item per row, then update only
            // the items whose text changed — preserving the selected ROW (by content) so the
            // reader stays put. A per-item list update never moves a caret, so NVDA's cursor
            // never jumps. The reconcile lives in Forms.DisplayList.UpdateInPlace.
            var lines = new List<string>();
            foreach (var v in values)
                lines.AddRange((v ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
            Forms.DisplayList.UpdateInPlace(displayBox, lines);
        }
    }

    /// <summary>
    /// Request a status-list repaint, COALESCED via a short one-shot debounce: the FIRST push arms
    /// the 120 ms timer and later pushes leave it running, so a force-read burst (the auto-refresh
    /// tick reads the whole panel at once) collapses into a single
    /// <see cref="UpdateDisplayText(ListBox)"/> pass a bounded 120 ms after the burst began. The
    /// repaint reads the current cache, so the single pass shows the freshest data; a straggler
    /// landing after the tick simply arms the next window. Do NOT switch this to a
    /// restart-per-push trailing debounce — sustained sub-120 ms pushes (hand-fly's per-SIM_FRAME
    /// PLANE_PITCH/BANK/HEADING, which are PFD/ISIS display vars) starve a trailing debounce and
    /// freeze the "live" list exactly while values change fastest.
    /// </summary>
    private void ScheduleDisplayRepaint()
    {
        if (_displayRepaintDebounce == null)
        {
            _displayRepaintDebounce = new System.Windows.Forms.Timer { Interval = 120 };
            _displayRepaintDebounce.Tick += (s, e) =>
            {
                _displayRepaintDebounce!.Stop();
                if (currentControls != null &&
                    currentControls.TryGetValue("_DISPLAY_", out var dc) && dc is ListBox lb)
                    UpdateDisplayText(lb);
            };
        }
        if (!_displayRepaintDebounce.Enabled)
            _displayRepaintDebounce.Start();
    }

    private void OnSimVarValueChanged(object? sender, SimVarChangeEventArgs e)
    {
        // For PMDG aircraft, IsInitialValue is always true on first change because the
        // simVarMonitor has never seen the variable before. But PMDG data manager already
        // suppresses the initial snapshot, so any change that reaches here IS a real change.
        // The FBW A380 has the SAME behaviour: its L:vars are monitored changed-only, so a
        // var's first sample only arrives WHEN it first changes (no startup baseline) — which
        // made the first switch/flap movement after load silent (only the 2nd worked). The
        // 5-second announcement grace period (EnableAnnouncements) already suppresses the
        // cold-and-dark startup snapshot, so treating the A380 like PMDG here is safe.
        bool isPMDG = currentAircraft is IPMDGAircraft;
        bool announceInitialChange = isPMDG || currentAircraft?.AircraftCode == "FBW_A380";
        bool shouldAnnounce = announceInitialChange ? !updatingFromSim : (!e.IsInitialValue && !updatingFromSim);

        if (shouldAnnounce && !string.IsNullOrEmpty(e.Description))
        {
            announcer.Announce(e.Description);
        }
    }

    private void BuildPMDGFieldMap()
    {
        _pmdgFieldToKeyMap = new Dictionary<string, string>();
        if (currentAircraft == null) return;
        foreach (var kvp in currentAircraft.GetVariables())
        {
            // Map Name (struct field name) → Key (variable key)
            if (!_pmdgFieldToKeyMap.ContainsKey(kvp.Value.Name))
                _pmdgFieldToKeyMap[kvp.Value.Name] = kvp.Key;
        }
    }

    private void OnPMDGVariableChanged(object? sender, PMDGVarUpdateEventArgs e)
    {
        if (_pmdgFieldToKeyMap == null) BuildPMDGFieldMap();

        // Translate struct field name to variable key
        if (!_pmdgFieldToKeyMap!.TryGetValue(e.FieldName, out string? varKey))
        {
            if (e.FieldName is "ELEC_GrdPwrSw" or "ELEC_GenSw_0" or "ELEC_GenSw_1" or "ELEC_APUGenSw_0" or "ELEC_APUGenSw_1")
                Log.Debug("MainForm", $"PMDG event {e.FieldName} DROPPED (varKey not found in map)");
            return;
        }

        if (e.FieldName is "ELEC_GrdPwrSw" or "ELEC_GenSw_0" or "ELEC_GenSw_1" or "ELEC_APUGenSw_0" or "ELEC_APUGenSw_1")
            Log.Debug("MainForm", $"PMDG event {e.FieldName} -> varKey={varKey} value={e.Value} initial={e.IsInitialSnapshot}");

        // Route PMDG variable changes through the same pipeline as SimVar updates
        var simVarEvent = new SimVarUpdateEventArgs
        {
            VarName = varKey,
            Value   = e.Value,
            Description = string.Empty,
            IsInitialSnapshot = e.IsInitialSnapshot,
        };
        OnSimVarUpdated(this, simVarEvent);
    }

    /// <summary>
    /// Output Ctrl+G: speak the most recent GSX tooltip without opening the
    /// AccessGSX window. The GsxService keeps the last tooltip cached for the
    /// duration of the SimConnect connection, so this works whether or not the
    /// AccessGSX form has been opened this session.
    /// </summary>
    private void ReadLatestGsxTooltip()
    {
        if (_gsxService == null || !_gsxService.IsConnected)
        {
            announcer.AnnounceImmediate("Access GSX: not connected to the simulator.");
            return;
        }
        _gsxService.RefreshTooltip();
        string tooltip = _gsxService.LastTooltip;
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            announcer.AnnounceImmediate("No GSX tooltip yet.");
            return;
        }
        announcer.AnnounceImmediate(tooltip);
    }

    // Non-handler async void (called from the hotkey dispatcher, not subscribed to an
    // event) — wrapped so a fault in the poll/announce path can't escape as an
    // unobserved async-void exception.
    private async void AnnounceTrackedTcasTraffic()
    {
        try
        {
            if (tcasService == null || !tcasService.HasTracked)
            {
                announcer.AnnounceImmediate("No tracked aircraft. Add aircraft to track list from the TCAS window.");
                return;
            }

            // Kick off a fresh poll so SimConnect returns the latest positions.
            // Wait ~600 ms for responses to arrive before reading announcements.
            tcasService.PollNow();
            await Task.Delay(600);

            var items = tcasService.GetTrackedAnnouncements();
            announcer.AnnounceImmediate(string.Join(". ", items));
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error in AnnounceTrackedTcasTraffic: {ex.Message}");
            announcer.AnnounceImmediate("Error reading traffic.");
        }
    }

    /// <summary>
    /// Speaks FMS flight progress for the A380 D / Shift+D hotkeys. The numbers come
    /// from the FMS guidance controller in the MFD page (no stock SimVar exposes
    /// them), read via the Coherent debugger. <paramref name="tod"/> selects Top of
    /// Descent (Shift+D) vs distance to destination (D). Async fire-and-forget; the
    /// announcement lands when the eval returns.
    /// </summary>
    public async void AnnounceA380FlightInfo(bool tod)
    {
        if (coherentClient == null) { announcer.AnnounceImmediate("Flight info unavailable."); return; }
        string raw = "";
        try { raw = await coherentClient.EvalForResultAsync("window.__MSFSBA_A380 ? __MSFSBA_A380.flightInfo() : ''"); }
        catch (Exception ex) { Log.Debug("MainForm", $"{ex.Message}"); }
        AnnounceFlightInfoJson(raw, tod);
    }

    /// <summary>
    /// A32NX equivalent. The A320 has no D/Shift+D path of its own and drives its MCDU over
    /// the SimBridge relay (not the Coherent MCDU bridge), so we read its FMS guidanceController
    /// directly via a ONE-SHOT Coherent eval of the self-contained coherent-a32nx-flightinfo.js,
    /// then announce identically to the A380 (PMDG-format TOD).
    /// </summary>
    public async void AnnounceA32NXFlightInfo(bool tod)
    {
        string js = LoadA32NXFlightInfoJs();
        if (string.IsNullOrEmpty(js)) { announcer.AnnounceImmediate("Flight info unavailable."); return; }
        // The Headwind A330 hosts the MCDU in the "A339X_MCDU" Coherent view; the A32NX
        // uses "A32NX_MCDU". The flight-info JS queries both <a32nx-mcdu>/<a339x-mcdu>
        // elements, so only the view needle changes per airframe.
        string mcduView = (currentAircraft as Aircraft.FlyByWireA320Definition)?.FlightInfoMcduView
            ?? "A32NX_MCDU";
        string raw = "";
        try { raw = await SimConnect.CoherentEvalClient.EvalAsync(mcduView, js); }
        catch (Exception ex) { Log.Debug("MainForm", $"{ex.Message}"); }
        AnnounceFlightInfoJson(raw, tod);
    }

    private string LoadA32NXFlightInfoJs()
    {
        if (_a32nxFlightInfoJs == null)
        {
            try { _a32nxFlightInfoJs = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-a32nx-flightinfo.js")); }
            catch { _a32nxFlightInfoJs = ""; }
        }
        return _a32nxFlightInfoJs;
    }

    // Parse the flightInfo JSON (same shape for the A380 + A32NX) and speak the D/Shift+D
    // readout. Shared so both FBW jets announce identically (PMDG-format TOD).
    private void AnnounceFlightInfoJson(string raw, bool tod)
    {
        try
        {
            if (string.IsNullOrEmpty(raw)) { announcer.AnnounceImmediate("Flight management not ready."); return; }

            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var okEl) || okEl.ValueKind != System.Text.Json.JsonValueKind.True)
            {
                announcer.AnnounceImmediate("Flight management not ready.");
                return;
            }

            double? Num(string key) =>
                r.TryGetProperty(key, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? e.GetDouble() : (double?)null;

            if (tod)
            {
                double? td = Num("distToTD");
                double? tc = Num("distToTC");
                double? tdSecs = Num("timeToTD");   // FMS time-to-go (seconds), null until computed
                double? phase = Num("flightPhase");  // FMGC phase: >=4 = descent/approach/… = past TOD
                // Past TOD once descending — the robust PMDG-parity signal (PMDG keys off
                // FMC_DistanceToTOD going negative; the A380's (T/D) pseudo-waypoint just
                // disappears, so its distance/time can read stale — phase is authoritative).
                bool pastTod = phase.HasValue && phase.Value >= 4 && phase.Value <= 7;
                if (pastTod || (td.HasValue && td.Value <= 0.5))
                    announcer.AnnounceImmediate("Past top of descent");
                else if (td.HasValue)
                {
                    // Match the PMDG TOD readout format exactly:
                    // "145 miles to top of descent: 00:16:58" (time from the FMS).
                    string eta = tdSecs.HasValue ? FormatEtaSeconds(tdSecs.Value) : "";
                    announcer.AnnounceImmediate($"{Math.Round(td.Value)} miles to top of descent{eta}");
                }
                else if (tc.HasValue && tc.Value > 0.5)
                    announcer.AnnounceImmediate($"{Math.Round(tc.Value)} miles to top of climb");
                else
                    announcer.AnnounceImmediate("Top of descent not yet computed");
            }
            else
            {
                double? dd = Num("distToDest");
                double? ddSecs = Num("timeToDest");   // FMS time-to-go (seconds), null if the profile hasn't computed it
                if (dd.HasValue && dd.Value >= 0)
                {
                    // Match the TOD readout format: "1355 miles to destination: 02:54:33"
                    // (the ": HH:MM:SS" suffix is omitted when the FMS supplies no time).
                    string eta = ddSecs.HasValue ? FormatEtaSeconds(ddSecs.Value) : "";
                    announcer.AnnounceImmediate($"{Math.Round(dd.Value)} miles to destination{eta}");
                }
                else
                    announcer.AnnounceImmediate("Destination distance not available");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"{ex.Message}");
            announcer.AnnounceImmediate("Flight info error.");
        }
    }

    // ": HH:MM:SS" suffix for the A380 TOD readout — identical to the PMDG TOD
    // format (PMDG737Definition.FormatEtaFromDistance). Empty when there's no time.
    private static string FormatEtaSeconds(double seconds)
    {
        if (seconds <= 0) return "";
        int totalSeconds = (int)Math.Round(seconds);
        int hh = totalSeconds / 3600;
        int mm = (totalSeconds % 3600) / 60;
        int ss = totalSeconds % 60;
        return $": {hh:D2}:{mm:D2}:{ss:D2}";
    }

    /// <summary>
    /// "Where Am I" — tells the pilot which taxiway/runway/gate they're currently on at
    /// the nearest airport. Works whether or not taxi guidance is active. Format:
    /// "Taxiway Bravo at KJFK." / "Gate A25 at KJFK." / "Runway 22L at KJFK."
    /// </summary>
    private void AnnounceWhereAmI()
    {
        if (airportDataProvider == null)
        {
            announcer.AnnounceImmediate("Airport database not available.");
            return;
        }

        // Where Am I is GROUND-ONLY by design: it tells the pilot which gate /
        // taxiway / runway they're sitting on. In flight there's a separate
        // location/city hotkey for that — Where Am I would otherwise just pick
        // the nearest taxiway 4000 ft below, which is misleading. Silence it
        // when airborne. Default _lastOnGround = true means a startup-time
        // query before any SIM_ON_GROUND sample still works on the ramp.
        if (!_lastOnGround)
        {
            announcer.AnnounceImmediate("In flight.");
            return;
        }

        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            string announcement;
            try
            {
                // GetNearbyAirportICAOs may return 3-char idents for small fields with
                // no canonical ICAO (kept for the GateResolver TCAS-gate use case). The
                // taxi-graph lookup needs canonical 4-char ICAOs, so filter here at the
                // call site — do NOT add the filter to the SQL or it breaks GateResolver.
                var nearby = airportDataProvider.GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
                    .Where(c => c != null && c.Length == 4)
                    .ToList();
                if (nearby == null || nearby.Count == 0)
                {
                    announcement = "No airport nearby.";
                }
                else
                {
                    announcement = taxiGuidanceManager.DescribeCurrentLocation(
                        airportDataProvider,
                        nearby[0],
                        position.Latitude,
                        position.Longitude);
                }
            }
            catch (Exception ex)
            {
                announcement = $"Location lookup failed. {ex.Message}";
            }

            if (this.InvokeRequired)
                this.Invoke(() => announcer.AnnounceImmediate(announcement));
            else
                announcer.AnnounceImmediate(announcement);
        });
    }

    private void OnTaxiGuidanceStateChanged(object? sender, TaxiGuidanceState newState)
    {
        // DIAGNOSTIC: log state transitions to landing_exit.log so we can correlate
        // them with the rollout-phase per-frame log entries.
        try { _landingExitLog.Info($"OnTaxiGuidanceStateChanged newState={newState}"); }
        catch { }

        // DIAGNOSTIC: reset the first-rollout-pos one-shot whenever we ENTER
        // LandingRollout so each rollout gets its own fresh log entry.
        if (newState == TaxiGuidanceState.LandingRollout)
            _diagLoggedFirstRolloutPos = false;

        switch (newState)
        {
            case TaxiGuidanceState.Taxiing:
                simConnectManager.StartTaxiGuidanceMonitoring();
                break;
            case TaxiGuidanceState.LandingRollout:
                // A landing just started (Landing Exit Planner auto-activation). Any docking
                // destination still set belongs to the PREVIOUS flight's arrival — clear it so
                // the stale gate can't keep IsActive latched and mute the rollout steering tone.
                // Covers hand-flown departures where the takeoff-assist clear never ran.
                // (Position monitoring is unchanged here — it's already running from the
                // route-load Taxiing transition.)
                dockingGuidanceManager?.SetDestinationGate(null);
                break;
            case TaxiGuidanceState.Arrived:
            case TaxiGuidanceState.Inactive:
            case TaxiGuidanceState.ProgressiveHold:
                simConnectManager.StopTaxiGuidanceMonitoring();
                break;
        }
    }

    private void ReadTrackedWaypoint(int slotNumber)
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator");
            return;
        }

        // Check if slot is empty
        if (waypointTracker.IsSlotEmpty(slotNumber))
        {
            announcer.AnnounceImmediate($"Track slot {slotNumber} empty");
            return;
        }

        // Get current aircraft position
        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            try
            {
                // Get tracked waypoint info with current distance and bearing
                string? waypointInfo = waypointTracker.GetTrackedWaypointInfo(
                    slotNumber,
                    position.Latitude,
                    position.Longitude,
                    position.MagneticVariation);

                if (waypointInfo != null)
                {
                    announcer.AnnounceImmediate(waypointInfo);
                }
                else
                {
                    announcer.AnnounceImmediate($"Track slot {slotNumber} waypoint not found");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MainForm", $"Error reading tracked waypoint: {ex.Message}");
                announcer.AnnounceImmediate($"Error reading track slot {slotNumber}");
            }
        });
    }

    // (Old PFD / ND / ECAM / Status display-window launchers removed — the FBW
    // aircraft read these through the accessible status-box panels now.)

    private void RequestDestinationRunwayDistance()
    {
        if (!simConnectManager.HasDestinationRunway())
        {
            announcer.AnnounceImmediate("No destination runway selected. Press left bracket then shift+d to select a destination runway first.");
            return;
        }

        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Request current aircraft position to calculate distance and bearing to destination runway
        // This will be handled asynchronously through the SimConnect event system
        simConnectManager.RequestDestinationRunwayDistance();
    }

    private void RequestILSGuidance()
    {
        // Check if destination runway is selected
        if (!simConnectManager.HasDestinationRunway())
        {
            announcer.AnnounceImmediate("No destination runway selected. Press left bracket then shift+d to select a destination runway first.");
            return;
        }

        // Check if connected to simulator
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Check if airport database is available
        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. ILS guidance requires database.");
            return;
        }

        // Get destination runway and airport
        var runway = simConnectManager.GetDestinationRunway();
        var airport = simConnectManager.GetDestinationAirport();

        if (runway == null || airport == null)
        {
            announcer.AnnounceImmediate("No destination runway selected.");
            return;
        }

        // Task 1 — Destination prefetch (silent, fire-and-forget)
        if (_augmentPrefetched.Add(airport.ICAO))
            _ = _augmentingProvider?.PrefetchAsync(airport.ICAO, force: true);

        // Query ILS data from database
        var ilsData = airportDataProvider.GetILSForRunway(airport.ICAO, runway.RunwayID);

        if (ilsData == null)
        {
            announcer.AnnounceImmediate($"No ILS available for runway {runway.RunwayID} at {airport.ICAO}.");
            return;
        }

        // Request ILS guidance calculation
        // This will be handled asynchronously through the SimConnect event system
        simConnectManager.RequestILSGuidance(ilsData, runway, airport);
    }

    // Non-handler async void (called from the hotkey dispatcher, not subscribed to an
    // event) — wrapped so a fault in the callback/format path can't escape as an
    // unobserved async-void exception (mirrors the guard already on RequestWindInfo).
    private async void RequestNavRadioInfo()
    {
        try
        {
            if (simConnectManager == null || !simConnectManager.IsConnected)
            {
                announcer.AnnounceImmediate("Not connected to simulator.");
                return;
            }

            bool received = false;
            string announcement = "";

            simConnectManager.RequestNavRadioInfo(navData =>
            {
                announcement = FormatNavRadioData(navData);
                received = true;
            });

            var timeout = DateTime.Now.AddSeconds(2);
            while (!received && DateTime.Now < timeout)
            {
                await Task.Delay(50);
                Application.DoEvents();
            }

            if (received)
                announcer.AnnounceImmediate(announcement);
            else
                announcer.AnnounceImmediate("NAV radio data unavailable.");
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error in RequestNavRadioInfo: {ex.Message}");
            announcer.AnnounceImmediate("Error getting NAV radio information");
        }
    }

    private string FormatNavRadioData(SimConnect.SimConnectManager.NavRadioData data)
    {
        var parts = new List<string>();

        parts.Add(FormatSingleNav("Nav 1", data.Nav1Freq, data.Nav1HasNav, data.Nav1HasLocalizer,
            data.Nav1HasGlideSlope, data.Nav1HasDME, data.Nav1DME, data.Nav1Localizer,
            data.Nav1GlideSlope, data.Nav1Ident, data.Nav1Name));

        parts.Add(FormatSingleNav("Nav 2", data.Nav2Freq, data.Nav2HasNav, data.Nav2HasLocalizer,
            data.Nav2HasGlideSlope, data.Nav2HasDME, data.Nav2DME, data.Nav2Localizer,
            data.Nav2GlideSlope, data.Nav2Ident, data.Nav2Name));

        return string.Join(". ", parts);
    }

    private string FormatSingleNav(string label, double freq, double hasNav, double hasLoc,
        double hasGS, double hasDME, double dme, double locCourse, double gsAngle,
        string ident, string name)
    {
        string freqStr = freq.ToString("F2");
        var info = new List<string> { $"{label}: {freqStr}" };

        if (hasNav <= 0)
        {
            info.Add("no signal");
            return string.Join(", ", info);
        }

        if (!string.IsNullOrWhiteSpace(ident))
            info.Add(ident);
        if (!string.IsNullOrWhiteSpace(name))
            info.Add(name);

        if (hasLoc > 0)
            info.Add($"localizer course {(int)locCourse}");

        if (hasGS > 0)
            info.Add($"glideslope {gsAngle:F1} degrees");

        if (hasDME > 0)
            info.Add($"DME {dme:F1} nautical miles");

        return string.Join(", ", info);
    }

    private async void RequestWindInfo()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        try
        {
            // Current wind. #129: when the user has OPTED INTO ActiveSky (Weather
            // settings tab) read the AS ambient wind + gust — under AS the SimConnect
            // ambient wind can diverge (AS wind smoothing), and the radar's "wind at
            // altitude" reads AS, so the two must match. When the switch is off,
            // TryGetActiveSkyConditionsAsync returns null INSTANTLY (the central gate
            // in ActiveSkyClient.IsRunningAsync — no probe, no ~1.2 s floor) and the
            // SimConnect path below is authoritative.
            string currentWind = "unavailable";
            var asConditions = await TryGetActiveSkyConditionsAsync();
            // AS opted-in but unreachable (not running / fetch failed): say so BEFORE the
            // SimConnect wind, so a user whose ActiveSky quietly isn't running learns it
            // here instead of trusting a silently-degraded readout. Prefixed into the ONE
            // utterance below — a second AnnounceImmediate would cut the first one off.
            string sourceNotice = "";
            if (asConditions == null && MSFSBlindAssist.Settings.SettingsManager.Current.ActiveSkyEnabled)
                sourceNotice = "ActiveSky not responding, using simulator wind. ";
            if (asConditions != null)
            {
                currentWind = FormatActiveSkyWind(asConditions);
            }
            else
            {
                bool currentWindReceived = false;
                simConnectManager.RequestWindInfo(currentWindData =>
                {
                    currentWind = FormatWindData(currentWindData);
                    currentWindReceived = true;
                });

                // Wait briefly for current wind data
                var timeout = DateTime.Now.AddSeconds(2);
                while (!currentWindReceived && DateTime.Now < timeout)
                {
                    await Task.Delay(50);
                    Application.DoEvents();
                }
            }

            // Check if destination airport is set
            if (simConnectManager.HasDestinationRunway())
            {
                var destinationAirport = simConnectManager.GetDestinationAirport();

                // Task 1 — Destination prefetch (silent, fire-and-forget)
                if (destinationAirport != null && _augmentPrefetched.Add(destinationAirport.ICAO))
                    _ = _augmentingProvider?.PrefetchAsync(destinationAirport.ICAO, force: true);

                // Get destination wind from VATSIM API
                var destinationWindData = await VATSIMService.GetAirportWindAsync(destinationAirport?.ICAO ?? "");
                string destinationWind = VATSIMService.FormatWind(destinationWindData);

                announcer.AnnounceImmediate($"{sourceNotice}{currentWind}, {destinationWind}");
            }
            else
            {
                announcer.AnnounceImmediate($"{sourceNotice}{currentWind}, no destination");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error in RequestWindInfo: {ex.Message}");
            announcer.AnnounceImmediate("Error getting wind information");
        }
    }

    private string FormatWindData(MSFSBlindAssist.SimConnect.SimConnectManager.WindData windData)
    {
        // Convert direction to integer and round speed to nearest knot
        int direction = (int)Math.Round(windData.Direction);
        int speed = (int)Math.Round(windData.Speed);

        if (speed == 0)
            return "calm";

        // Format as "direction at speed"
        return $"{direction:000} at {speed}";
    }

    /// <summary>Best-effort ActiveSky conditions for the wind readout; null if AS is off
    /// or the fetch fails (caller falls back to SimConnect). Cheap on repeat (cached port).</summary>
    private async Task<MSFSBlindAssist.Services.ActiveSkyClient.Conditions?> TryGetActiveSkyConditionsAsync()
    {
        try
        {
            if (!await weatherActiveSky.IsRunningAsync()) return null;
            return await weatherActiveSky.GetCurrentConditionsAsync();
        }
        catch { return null; }
    }

    /// <summary>ActiveSky wind-at-altitude for output+I — matches the Weather Radar's
    /// "Wind (at altitude)" line, plus the surface gust when AS reports one (#129).</summary>
    private static string FormatActiveSkyWind(MSFSBlindAssist.Services.ActiveSkyClient.Conditions c)
    {
        int direction = (int)Math.Round(c.AmbientWindDirection);
        int speed = (int)Math.Round(c.AmbientWindSpeed);
        if (speed == 0) return "calm";
        string text = $"{direction:000} at {speed}";
        if (c.SurfaceGustSpeed > 0)
            text += $", gusting {(int)Math.Round(c.SurfaceGustSpeed)}";
        return text;
    }

    private async void DescribeSceneAsync()
    {
        try
        {
            announcer.AnnounceImmediate("Capturing scene...");

            // Create screenshot and Gemini services
            var screenshotService = new ScreenshotService();
            var geminiService = new GeminiService();

            // Check if MSFS window is available
            if (!screenshotService.IsMsfsWindowAvailable())
            {
                announcer.AnnounceImmediate("Microsoft Flight Simulator window not found.");
                return;
            }

            // Capture screenshot
            byte[]? screenshot = await screenshotService.CaptureAsync();
            if (screenshot == null || screenshot.Length == 0)
            {
                announcer.AnnounceImmediate("Failed to capture scene screenshot.");
                return;
            }

            // Analyze scene with Gemini
            string analysis = await geminiService.AnalyzeSceneAsync(screenshot);

            // Show result in form (independent window with synchronous focus)
            var resultForm = new DisplayReadingResultForm("Scene", analysis, "Description");
            resultForm.ShowForm();

            announcer.AnnounceImmediate("Scene description ready");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("API key"))
        {
            announcer.AnnounceImmediate("Gemini API key not configured. Please configure it in File menu, Gemini Settings.");
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error in DescribeSceneAsync: {ex.Message}");
            announcer.AnnounceImmediate($"Error describing scene: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the nearest city announcement timer if enabled in settings.
    /// Checks the current setting value and configures the timer accordingly.
    /// Called when SimConnect initially connects.
    /// </summary>
    private void StartNearestCityAnnouncementTimer()
    {
        // Delegate to restart method for consistent behavior
        RestartNearestCityAnnouncementTimer();
    }

    /// <summary>
    /// Restarts the nearest city announcement timer with current settings.
    /// Stops timer if disabled (interval = 0), or updates interval and restarts if enabled.
    /// Should be called whenever GeoNames settings are saved.
    /// </summary>
    private void RestartNearestCityAnnouncementTimer()
    {
        if (nearestCityAnnouncementTimer == null)
            return;

        // Always stop first to ensure clean state
        nearestCityAnnouncementTimer.Stop();

        // Read current setting
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        int intervalSeconds = settings.NearestCityAnnouncementInterval;

        // Start with new interval if enabled
        if (intervalSeconds > 0)
        {
            nearestCityAnnouncementTimer.Interval = intervalSeconds * 1000; // Convert to milliseconds
            nearestCityAnnouncementTimer.Start();
            Log.Debug("MainForm", $"Nearest city announcement timer restarted: {intervalSeconds} seconds interval");
        }
        else
        {
            Log.Debug("MainForm", "Nearest city announcement timer stopped (disabled in settings)");
        }
    }

    /// <summary>
    /// Timer tick handler for nearest city announcements.
    /// Requests current aircraft position and announces the nearest city.
    /// </summary>
    private void NearestCityAnnouncementTimer_Tick(object? sender, EventArgs e)
    {
        AnnounceNearestCity();
    }

    private void WeatherAnnouncementTimer_Tick(object? sender, EventArgs e)
    {
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        if (!simConnectManager.IsConnected) return;

        if (settings.WeatherAutoAnnounceEnabled)
            CheckAmbientWeatherChanges();

        if ((settings.SigmetProximityAlertsEnabled || settings.PirepProximityAlertsEnabled) && !_proximityCheckRunning)
            _ = CheckWeatherProximityAsync(settings.SigmetProximityRangeNm,
                    settings.SigmetProximityAlertsEnabled, settings.PirepProximityAlertsEnabled);
    }

    private async void CheckAmbientWeatherChanges()
    {
        // SimConnect ambient (cloud in/out, visibility, and the precip fallback when AS is off).
        // The timeout resolves to NULL, never default(AmbientWeatherData): an all-zeros struct
        // reads as "left cloud, visibility 0 m, precip stopped" and would false-announce all
        // three AND corrupt the change baselines whenever the sim stalls past 3 s (loading
        // screen, menu pause). No data = skip this pass entirely; the next tick retries.
        var tcs = new TaskCompletionSource<MSFSBlindAssist.SimConnect.SimConnectManager.AmbientWeatherData?>();
        simConnectManager.RequestWeatherInfo(d => tcs.TrySetResult(d));
        _ = Task.Delay(3000).ContinueWith(_ => tcs.TrySetResult(null));
        var maybeData = await tcs.Task;
        if (maybeData is not { } data) return;

        // #129: under ActiveSky the SimConnect AMBIENT PRECIP STATE bitmask sticks, so
        // when AS is running source precip from the METAR. Use the SAME precedence as the
        // AS decoded-weather monitor AND the Weather Radar — closest-station METAR first,
        // position METAR fallback — so all three features agree. null = AS not active
        // (fall back to SimConnect); "" = AS says no precip.
        string? asPrecip = null;
        try
        {
            if (await weatherActiveSky.IsRunningAsync())
            {
                string? metar = await weatherActiveSky.GetClosestStationMetarAsync();
                if (string.IsNullOrWhiteSpace(metar))
                    metar = await weatherActiveSky.GetPositionMetarAsync();
                if (!string.IsNullOrWhiteSpace(metar))
                    asPrecip = MSFSBlindAssist.Services.WeatherRadarFormPrecipShim.ParsePrecipFromMetar(metar);
            }
        }
        catch { asPrecip = null; }

        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => AnnounceAmbientChanges(data, asPrecip)); return; }
        AnnounceAmbientChanges(data, asPrecip);
    }

    private void AnnounceAmbientChanges(MSFSBlindAssist.SimConnect.SimConnectManager.AmbientWeatherData data,
        string? asPrecip = null)
    {
        // Cloud entry/exit — always from SimConnect (AS doesn't expose in-cloud).
        double inCloud = data.InCloud;
        if (_prevInCloud >= 0 && Math.Abs(inCloud - _prevInCloud) > 0.5)
            announcer.Announce(inCloud >= 0.5 ? "Entering cloud" : "Leaving cloud");
        _prevInCloud = inCloud;

        if (asPrecip != null)
        {
            // ActiveSky path — announce on start / stop / phrase change. Trim + case-
            // insensitive compare so an unchanged phrase NEVER repeats ("light rain" ->
            // "light rain" stays silent; only a different phrase re-announces).
            string cur = asPrecip.Trim();
            if (_prevAsPrecip != null && !string.Equals(cur, _prevAsPrecip, StringComparison.OrdinalIgnoreCase))
            {
                bool wasNone = _prevAsPrecip.Length == 0;
                bool isNone = cur.Length == 0;
                if (wasNone && !isNone)
                    announcer.Announce($"Precipitation started: {cur}");
                else if (!wasNone && isNone)
                    announcer.Announce("Precipitation stopped");
                else
                    announcer.Announce($"Precipitation now {cur}");
            }
            _prevAsPrecip = cur;
            // Keep the SimConnect precip baseline in step so switching back (AS closed
            // mid-flight) doesn't fire a spurious change.
            _prevPrecipState = data.PrecipState;
            _prevPrecipRate = data.PrecipRate;
        }
        else
        {
            // SimConnect path (AS not running). Reset the AS baseline so AS re-baselines
            // silently if it comes back.
            _prevAsPrecip = null;

            double precipState = data.PrecipState;
            double precipRate = data.PrecipRate;
            bool wasRaining = _prevPrecipState > 0.5;
            bool isRaining = precipState > 0.5;

            if (_prevPrecipState >= 0)
            {
                if (!wasRaining && isRaining)
                    announcer.Announce($"Precipitation started: {DescribePrecipIntensity(precipRate)}");
                else if (wasRaining && !isRaining)
                    announcer.Announce("Precipitation stopped");
                else if (isRaining && _prevPrecipRate >= 0 && IntensityTier(precipRate) != IntensityTier(_prevPrecipRate))
                    announcer.Announce($"Precipitation now {DescribePrecipIntensity(precipRate)}");
            }
            _prevPrecipState = precipState;
            _prevPrecipRate = precipRate;
        }

        // Visibility — announce crossing the 1500 m threshold in either direction
        double vis = data.Visibility;
        if (_prevVisibility >= 0)
        {
            bool isLow = vis < 1500;
            if (isLow && !_prevVisLow)
                announcer.Announce($"Visibility low: {vis / 1000.0:F1} km");
            else if (!isLow && _prevVisLow)
                announcer.Announce($"Visibility improving: {vis / 1000.0:F1} km");
        }
        _prevVisibility = vis;
        _prevVisLow = vis < 1500;
    }

    private static int IntensityTier(double rate) => rate switch
    {
        < 20 => 0,   // light
        < 50 => 1,   // moderate
        < 80 => 2,   // heavy
        _    => 3    // extreme
    };

    private static string DescribePrecipIntensity(double rate) => rate switch
    {
        < 20 => "light",
        < 50 => "moderate",
        < 80 => "heavy",
        _    => "extreme"
    };

    private async Task CheckWeatherProximityAsync(int rangeNm, bool checkSigmets, bool checkPireps)
    {
        _proximityCheckRunning = true;
        try
        {
            var lastPos = simConnectManager.LastKnownPosition;
            if (lastPos == null) return;
            var pos = lastPos.Value;
            if (pos.Latitude == 0 && pos.Longitude == 0) return;

            // Clear stale announced keys every 15 minutes
            if ((DateTime.UtcNow - _sigmetKeysClearedAt).TotalMinutes > 15)
            {
                _announcedSigmetKeys.Clear();
                _announcedPirepKeys.Clear();
                _sigmetKeysClearedAt = DateTime.UtcNow;
            }

            if (!IsHandleCreated || IsDisposed) return;

            if (checkSigmets)
            {
                var advisories = await MSFSBlindAssist.Services.WeatherService.GetNearbyAdvisoriesAsync(
                    pos.Latitude, pos.Longitude, rangeNm);

                if (!IsHandleCreated || IsDisposed) return;

                foreach (var adv in advisories)
                {
                    string key = $"{adv.AdvisoryType}_{adv.Hazard}_{adv.ValidFrom}_{adv.ValidTo}";
                    if (_announcedSigmetKeys.Contains(key)) continue;
                    _announcedSigmetKeys.Add(key);

                    string msg = $"{adv.AdvisoryType}: {adv.HazardLabel}";
                    if (!string.IsNullOrEmpty(adv.AltitudeRange)) msg += $", {adv.AltitudeRange}";
                    msg += $", bearing {adv.BearingDeg:F0} degrees, {adv.DistanceNm:F0} nautical miles";
                    // No marshal needed: WeatherAnnouncementTimer_Tick fires on the UI thread and
                    // WeatherService has no ConfigureAwait(false), so the await above resumes here.
                    announcer.Announce(msg);
                }
            }

            if (checkPireps)
            {
                var pireps = await MSFSBlindAssist.Services.WeatherService.GetNearbyPirepsAsync(
                    pos.Latitude, pos.Longitude, rangeNm);

                if (!IsHandleCreated || IsDisposed) return;

                foreach (var p in pireps)
                {
                    if (!p.IsSignificantHazard) continue;  // skip light reports

                    string key = $"PIREP_{p.ObsTime}_{p.AltitudeFt}_{p.TurbulenceIntensity}_{p.IcingIntensity}";
                    if (_announcedPirepKeys.Contains(key)) continue;
                    _announcedPirepKeys.Add(key);

                    int fl = p.AltitudeFt / 100;
                    string msg = $"Pilot report: {p.HazardSummary} at FL{fl:D3}";
                    msg += $", bearing {p.BearingDeg:F0} degrees, {p.DistanceNm:F0} nautical miles";
                    // No marshal needed: see comment above (advisories loop).
                    announcer.Announce(msg);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Weather proximity check error: {ex.Message}");
        }
        finally
        {
            _proximityCheckRunning = false;
        }
    }

    /// <summary>
    /// Announces the nearest city to the current aircraft position.
    /// Used by both the periodic timer and the hotkey shortcut (] then C).
    /// </summary>
    private void AnnounceNearestCity()
    {
        try
        {
            // Guard clause: Check if SimConnect is connected
            if (!simConnectManager.IsConnected)
            {
                Log.Debug("MainForm", "Nearest city announcement skipped: Not connected to simulator");
                return;
            }

            // Check if GeoNames API is configured
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            if (string.IsNullOrWhiteSpace(settings.GeoNamesApiUsername))
            {
                announcer.Announce("GeoNames API not configured. Please configure it in the settings.");
                return;
            }

            // Request current aircraft position with callback
            simConnectManager.RequestAircraftPositionAsync(async (position) =>
            {
                try
                {
                    // Get location data from GeoNames service
                    var geoNamesService = new GeoNamesService();
                    var locationData = await geoNamesService.GetLocationInfoAsync(position.Latitude, position.Longitude);

                    if (locationData?.NearbyPlaces != null && locationData.NearbyPlaces.Count > 0)
                    {
                        var nearestCity = locationData.NearbyPlaces[0];

                        // Format announcement (same format as LocationInfoForm)
                        string announcement = $"Near {nearestCity.Name}";
                        if (!string.IsNullOrEmpty(nearestCity.State))
                        {
                            announcement += $", {nearestCity.State}";
                        }
                        if (!string.IsNullOrEmpty(nearestCity.Country))
                        {
                            announcement += $", {nearestCity.Country}";
                        }
                        announcement += $", {nearestCity.Distance:F1} {settings.DistanceUnits} {nearestCity.Direction}";

                        // Check if over a body of water
                        var waterLandmark = locationData.Landmarks.FirstOrDefault(l => l.Type == "water");
                        if (waterLandmark != null)
                        {
                            announcement += $", {waterLandmark.Name}";
                        }

                        announcer.Announce(announcement);
                        Log.Debug("MainForm", $"Nearest city announced: {announcement}");
                    }
                    else
                    {
                        // No nearby cities found - check if over water
                        var waterLandmark = locationData?.Landmarks.FirstOrDefault(l => l.Type == "water");
                        if (waterLandmark != null)
                        {
                            announcer.Announce($"Over {waterLandmark.Name}");
                            Log.Debug("MainForm", $"Nearest city announced: Over {waterLandmark.Name}");
                        }
                        else
                        {
                            Log.Debug("MainForm", "Nearest city announcement skipped: No nearby cities found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("MainForm", $"Error in nearest city announcement callback: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error during nearest city announcement: {ex.Message}");
            // Don't announce errors to avoid interrupting the user
        }
    }
}
