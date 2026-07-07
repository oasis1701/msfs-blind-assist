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

namespace MSFSBlindAssist;

public partial class MainForm
{
    private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
    {
        // Actions that don't require SimConnect connection (can be used offline)
        var offlineActions = new HashSet<HotkeyAction>
        {
            HotkeyAction.ShowChecklist,
            HotkeyAction.ShowMETARReport,
            HotkeyAction.ShowColdTempCorrection,
            HotkeyAction.SimBriefBriefing,
            HotkeyAction.ShowElectronicFlightBag,
            HotkeyAction.ShowFenixMCDU,
            HotkeyAction.ShowPMDGEFB,
            HotkeyAction.ShowPMDGEFBFirstOfficer,
            HotkeyAction.TaxiStatus
        };

        // Guard clause: Block SimConnect-dependent actions if not fully connected
        if (!offlineActions.Contains(e.Action) && !simConnectManager.IsFullyConnected)
        {
            announcer.Announce("Not connected to simulator, please wait");
            return;
        }

        // If the pilot fired a manual readout query while visual guidance is active, open a
        // short grace window so VG's per-second bank/centerline callouts don't interrupt the
        // readout mid-sentence. See VisualGuidanceManager.NotifyManualQuery.
        if (visualGuidanceManager.IsActive && IsManualReadoutAction(e.Action))
        {
            visualGuidanceManager.NotifyManualQuery();
        }

        // Try aircraft-specific handler first
        bool handledByAircraft = currentAircraft.HandleHotkeyAction(e.Action, simConnectManager, announcer, this, hotkeyManager);

        // If aircraft handled it and it's a button action, check for state announcement
        if (handledByAircraft)
        {
            // Get the variable mapping to see if this needs state announcement
            var buttonStateMap = currentAircraft.GetButtonStateMapping();
            var variableMap = (currentAircraft as Aircraft.BaseAircraftDefinition)?.GetType()
                .GetMethod("GetHotkeyVariableMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.Invoke(currentAircraft, null) as Dictionary<HotkeyAction, string>;

            if (variableMap != null && variableMap.TryGetValue(e.Action, out string? eventName))
            {
                // Check if this button has a state announcement
                if (!string.IsNullOrEmpty(eventName))
                {
                    HandleButtonStateAnnouncement(eventName);
                }
            }
            return; // Action was handled by aircraft
        }

        // Fall through to universal actions (truly universal, not aircraft-specific)
        switch (e.Action)
        {
            case HotkeyAction.ReadAltitudeAGL:
                simConnectManager.RequestAltitudeAGL();
                break;
            case HotkeyAction.ReadAltitudeMSL:
                simConnectManager.RequestAltitudeMSL();
                break;
            case HotkeyAction.ReadAirspeedIndicated:
                simConnectManager.RequestAirspeedIndicated();
                break;
            case HotkeyAction.ReadAirspeedTrue:
                simConnectManager.RequestAirspeedTrue();
                break;
            case HotkeyAction.ReadGroundSpeed:
                simConnectManager.RequestGroundSpeed();
                break;
            case HotkeyAction.ReadMachSpeed:
                simConnectManager.RequestMachSpeed();
                break;
            case HotkeyAction.ReadLastLandingRate:
            {
                // Read straight from the persistent touchdown-velocity cache (ft/s × 60 = fpm).
                // The value is latched by the sim at touchdown and survives until the next landing.
                // Gated on a touchdown edge actually OBSERVED this session (HasLanding) — the
                // SimVar can hold a junk latched value at a runway spawn.
                double? td = simConnectManager.GetCachedVariableValue("PLANE_TOUCHDOWN_NORMAL_VELOCITY");
                if (landingRateAnnouncer.HasLanding && td.HasValue && System.Math.Abs(td.Value) > 0.01)
                {
                    int fpm = (int)System.Math.Round(System.Math.Abs(td.Value) * 60.0);
                    announcer.AnnounceImmediate($"Landing rate {fpm} feet per minute.");
                }
                else
                {
                    announcer.AnnounceImmediate("No landing recorded this session.");
                }
                break;
            }
            case HotkeyAction.ReadLastLandingPeakG:
            {
                double? g = landingRateAnnouncer.LastPeakG;
                if (g.HasValue)
                {
                    announcer.AnnounceImmediate($"Landing g-force {g.Value:F2} g.");
                }
                else
                {
                    announcer.AnnounceImmediate("No landing recorded this session.");
                }
                break;
            }
            case HotkeyAction.ReadVerticalSpeed:
                simConnectManager.RequestVerticalSpeed();
                break;
            case HotkeyAction.ReadBankAngle:
                simConnectManager.RequestBankAngle();
                break;
            case HotkeyAction.ReadPitch:
                simConnectManager.RequestPitch();
                break;
            case HotkeyAction.ReadHeadingMagnetic:
                simConnectManager.RequestHeadingMagnetic();
                break;
            case HotkeyAction.ReadHeadingTrue:
                simConnectManager.RequestHeadingTrue();
                break;
            case HotkeyAction.RunwayTeleport:
                ShowRunwayTeleportDialog();
                break;
            case HotkeyAction.GateTeleport:
                ShowGateTeleportDialog();
                break;
            case HotkeyAction.LocationInfo:
                ShowLocationInfoDialog();
                break;
            case HotkeyAction.ReadNearestCity:
                AnnounceNearestCity();
                break;
            case HotkeyAction.SimBriefBriefing:
                OpenSimBriefBriefing();
                break;
            case HotkeyAction.ShowTcasWindow:
                OpenTcasWindow();
                break;
            case HotkeyAction.AnnounceTcasTraffic:
                AnnounceTrackedTcasTraffic();
                break;
            case HotkeyAction.ShowWeatherRadar:
                OpenWeatherRadarWindow();
                break;
            case HotkeyAction.ReadOutsideTemperature:
                simConnectManager.RequestOutsideTemperature();
                break;
            case HotkeyAction.ReadSquawkCode:
                simConnectManager.RequestSquawkCode();
                break;
            case HotkeyAction.SelectDestinationRunway:
                ShowDestinationRunwayDialog();
                break;
            case HotkeyAction.ReadDestinationRunwayDistance:
                RequestDestinationRunwayDistance();
                break;
            case HotkeyAction.ReadILSGuidance:
                RequestILSGuidance();
                break;
            case HotkeyAction.ReadWindInfo:
                RequestWindInfo();
                break;
            case HotkeyAction.ReadNavRadioInfo:
                RequestNavRadioInfo();
                break;
            case HotkeyAction.ShowMETARReport:
                ShowMETARReportDialog();
                break;
            case HotkeyAction.ShowColdTempCorrection:
                ShowColdTempCorrectionDialog();
                break;
            case HotkeyAction.ShowChecklistECL:
                ShowChecklistECLDialog();
                break;
            case HotkeyAction.ShowChecklist:
                ShowChecklistDialog();
                break;
            case HotkeyAction.ShowElectronicFlightBag:
                ShowElectronicFlightBagDialog();
                break;
            case HotkeyAction.ShowFenixMCDU:
                // Single "show MCDU" hotkey routed by the currently-selected
                // aircraft. The action's enum name is historical (it was added
                // for Fenix first); FBW A380 reuses the same chord.
                if (currentAircraft is IPMDGAircraft && simConnectManager.PMDGDataManager != null)
                {
                    ShowPMDGCDUDialog();
                }
                else if (currentAircraft?.AircraftCode == "FBW_A380")
                {
                    ShowFBWA380MCDUDialog();
                }
                else if (currentAircraft?.AircraftCode == "HS_787")
                {
                    ShowHS787FMCDialog();
                }
                else if (currentAircraft?.AircraftCode == "A320")
                {
                    ShowFlyByWireMCDUDialog();
                }
                else
                {
                    ShowFenixMCDUDialog();
                }
                break;
            case HotkeyAction.ShowPMDGEFB:
                if (currentAircraft is IPMDGAircraft pmdgEFB && pmdgEFB.HasEFBSupport)
                {
                    ShowPmdgCoherentEfbDialog(firstOfficer: false);
                }
                else if (currentAircraft?.AircraftCode == "FBW_A380")
                {
                    ShowFbwEfbDialog();
                }
                else if (currentAircraft?.AircraftCode == "HS_787")
                {
                    announcer.AnnounceImmediate("787 E F B not available.");
                }
                else if (currentAircraft?.AircraftCode == "A320")
                {
                    // Unified flyPad: the A320 uses the SAME generic WebView2 form +
                    // CoherentEFBClient as the A380 (both drive the one shared
                    // coherent-flypad-agent.js over the "- EFB" Coherent view).
                    ShowFbwEfbDialog();
                }
                else if (currentAircraft?.AircraftCode == "FENIX_A320CEO")
                {
                    // The Fenix EFB web UI is already screen-reader accessible, so we
                    // host the live site directly (no scraping/shim like the FBW flyPad).
                    ShowFenixEFBDialog();
                }
                break;
            case HotkeyAction.ShowPMDGEFBFirstOfficer:
                if (currentAircraft is IPMDGAircraft pmdgEfbFo && pmdgEfbFo.HasEFBSupport)
                    ShowPmdgCoherentEfbDialog(firstOfficer: true);
                break;
            case HotkeyAction.ShowRMP:
                if (currentAircraft is FlyByWireA380Definition)
                {
                    ShowFBWA380RmpDialog();
                }
                else
                {
                    announcer.AnnounceImmediate("The Radio Management Panel window is only available on the A380.");
                }
                break;
            case HotkeyAction.ShowDCDU:
                if (currentAircraft?.AircraftCode == "A320")
                {
                    ShowA32NXDcduDialog();
                }
                else
                {
                    announcer.AnnounceImmediate("The DCDU window is only available on the A32NX.");
                }
                break;
            case HotkeyAction.ShowOANS:
                if (currentAircraft?.AircraftCode == "FBW_A380")
                {
                    ShowFBWA380OansDialog();
                }
                else
                {
                    announcer.AnnounceImmediate("OANS airport map is only available on the A380.");
                }
                break;
            case HotkeyAction.ShowTrackFixWindow:
                ShowTrackFixDialog();
                break;
            case HotkeyAction.ToggleTakeoffAssist:
                ToggleTakeoffAssist();
                break;
            case HotkeyAction.ToggleHandFlyMode:
                ToggleHandFlyMode();
                break;
            case HotkeyAction.ToggleVisualGuidance:
                ToggleVisualGuidance();
                break;
            case HotkeyAction.ReadTargetFPM:
                if (visualGuidanceManager.IsActive)
                {
                    double targetFPM = visualGuidanceManager.GetTargetFPM();
                    double altitudeDeviation = visualGuidanceManager.GetAltitudeDeviation();

                    // Format: "target -600, 1200 high" or "target -200, 1580 low"
                    string deviationText = altitudeDeviation >= 0
                        ? $"{Math.Abs(altitudeDeviation):F0} high"
                        : $"{Math.Abs(altitudeDeviation):F0} low";

                    announcer.AnnounceImmediate($"target {targetFPM:F0}, {deviationText}");
                }
                else
                {
                    announcer.AnnounceImmediate("Visual guidance not active");
                }
                break;
            case HotkeyAction.ReadTrackSlot1:
                ReadTrackedWaypoint(1);
                break;
            case HotkeyAction.ReadTrackSlot2:
                ReadTrackedWaypoint(2);
                break;
            case HotkeyAction.ReadTrackSlot3:
                ReadTrackedWaypoint(3);
                break;
            case HotkeyAction.ReadTrackSlot4:
                ReadTrackedWaypoint(4);
                break;
            case HotkeyAction.ReadTrackSlot5:
                ReadTrackedWaypoint(5);
                break;
            case HotkeyAction.DescribeScene:
                DescribeSceneAsync();
                break;
            case HotkeyAction.TaxiAssistForm:
                ShowTaxiAssistForm();
                break;
            case HotkeyAction.TaxiStatus:
                // Y — rolling current status from live position (current taxiway, next turn,
                // distance to destination). Recomputed on every press from the route + position.
                // While docking owns the final approach, report ITS distance to the precise
                // stop — not taxi's distance to the route-end node — so the status never
                // contradicts the live docking countdown ("25 m to gate" vs "20 m to stop").
                if (dockingGuidanceManager.IsActive)
                {
                    string dockStatus = dockingGuidanceManager.GetStatusAnnouncement();
                    announcer.AnnounceImmediate(string.IsNullOrEmpty(dockStatus)
                        ? taxiGuidanceManager.GetStatusAnnouncement()
                        : dockStatus);
                }
                else
                {
                    announcer.AnnounceImmediate(taxiGuidanceManager.GetStatusAnnouncement());
                }
                break;
            case HotkeyAction.TaxiRepeat:
                // Ctrl+Y — replays the most recent actionable instruction (turn callout,
                // hold-short, taxiway change, lineup, arrival, etc.) verbatim. Distinct from
                // TaxiStatus: that recomputes a snapshot; this gives back exactly what the
                // pilot just heard, useful when the announcement was clipped by another sound.
                announcer.AnnounceImmediate(taxiGuidanceManager.RepeatLastInstruction());
                break;
            case HotkeyAction.TaxiContinue:
                taxiGuidanceManager.ContinuePastHoldShort();
                break;
            case HotkeyAction.TaxiStop:
                taxiGuidanceManager.StopGuidance();
                dockingGuidanceManager?.SetDestinationGate(null);
                simConnectManager.StopTaxiGuidanceMonitoring();
                announcer.AnnounceImmediate("Taxi guidance stopped.");
                break;
            case HotkeyAction.TaxiWhereAmI:
                AnnounceWhereAmI();
                break;
            case HotkeyAction.AnnounceGroundTraffic:
                groundTrafficMonitor.AnnounceNearestTrafficSummary();
                break;
            case HotkeyAction.LandingExitPlanner:
                ShowLandingExitForm();
                break;
            case HotkeyAction.ShowAccessGSX:
                ShowAccessGSXForm();
                break;
            case HotkeyAction.ReadGsxTooltip:
                ReadLatestGsxTooltip();
                break;
            // Note: FCU push/pull, autopilot toggles, FCU set value dialogs, and A32NX-specific hotkeys
            // are now handled by the aircraft definition via HandleHotkeyAction()
        }
    }

    private void OnOutputHotkeyModeChanged(object? sender, HotkeyModeEventArgs e)
    {
        if (e.Status == HotkeyModeStatus.Activated)
            announcer.AnnounceImmediate("output");
        else if (e.Status == HotkeyModeStatus.Cancelled)
            announcer.AnnounceImmediate("cancelled");
    }

    private void OnInputHotkeyModeChanged(object? sender, HotkeyModeEventArgs e)
    {
        if (e.Status == HotkeyModeStatus.Activated)
            announcer.AnnounceImmediate("input");
        else if (e.Status == HotkeyModeStatus.Cancelled)
            announcer.AnnounceImmediate("cancelled");
    }

    private void ToggleTakeoffAssist()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        // Request current position for takeoff assist toggle
        simConnectManager.RequestPositionForTakeoffAssist();
    }

    private void OnTakeoffAssistActiveChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            // If taxi guidance is still running (e.g. pilot stayed in LiningUp state after
            // reaching the runway), stop it now — otherwise both systems compete for the
            // steering tone channel during takeoff roll and the pilot hears two tones.
            if (taxiGuidanceManager.State != TaxiGuidanceState.Inactive)
            {
                taxiGuidanceManager.StopGuidance();
            }

            // The flight is departing — clear any docking destination from the previous
            // arrival. Without this the gate (and a latched Docking/Stopped state) survived
            // the whole flight: on landing, the rollout's position frames fed docking a stale
            // departure-airport gate and could keep the landing-exit steering tone muted.
            // (The Stopped state also self-heals on absolute distance now, but takeoff is the
            // unambiguous "previous arrival is over" boundary — clear it here.)
            dockingGuidanceManager?.SetDestinationGate(null);

            // Start monitoring position, pitch, and IAS for takeoff assist
            simConnectManager.StartTakeoffAssistMonitoring();

            // If Fenix aircraft, read V1/VR speeds from MCDU performance data (already continuously monitored)
            if (currentAircraft.AircraftCode == "FENIX_A320CEO")
            {
                // Use N_MISC_PERF_TO_V1/VR (MCDU performance data), not FNX2PLD_speedV1/VR (display variables)
                bool foundV1 = currentSimVarValues.TryGetValue("N_MISC_PERF_TO_V1", out double v1Val);
                bool foundVR = currentSimVarValues.TryGetValue("N_MISC_PERF_TO_VR", out double vrVal);

                double? v1 = foundV1 ? v1Val : null;
                double? vr = foundVR ? vrVal : null;
                takeoffAssistManager.SetFenixVSpeeds(v1, vr);

                System.Diagnostics.Debug.WriteLine($"[TakeoffAssist] Fenix V-speeds from MCDU: V1={v1Val}, VR={vrVal}");
            }
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopTakeoffAssistMonitoring();

            // Clear Fenix V-speeds on deactivation
            takeoffAssistManager.ClearFenixVSpeeds();
        }
    }

    private void OnTakeoffRunwayReferenceSet(object? sender, TakeoffRunwayReferenceEventArgs e)
    {
        // Set the runway reference in the takeoff assist manager when user teleports to a runway
        takeoffAssistManager.SetRunwayReference(e.ThresholdLat, e.ThresholdLon,
            e.RunwayHeadingTrue, e.RunwayHeadingMagnetic,
            e.RunwayID, e.AirportICAO);
    }

    /// <summary>
    /// Fires when TaxiGuidanceManager detects the aircraft has become lined up
    /// on its destination runway (one-shot per route). Auto-activates Takeoff
    /// Assist when the user setting permits, via the standard CTRL+T flow.
    /// </summary>
    private void OnTaxiGuidanceRequestTakeoffAssistAutoActivate(
        object? sender, TakeoffAssistAutoActivateEventArgs e)
    {
        // Marshal to the UI thread — the event is raised from a SimConnect-
        // thread UpdatePosition callback (inside _stateLock), but we touch
        // takeoffAssistManager / announcer / SettingsManager.Current here.
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
                OnTaxiGuidanceRequestTakeoffAssistAutoActivate(sender, e)));
            return;
        }

        if (!SettingsManager.Current.TakeoffAssistAutoActivateOnLineup) return;
        if (takeoffAssistManager.IsActive) return;
        if (!_lastOnGround) return;

        // e.RunwayId / e.AirportIcao are informational only; the actual
        // reference seeding goes through TryGetRunwayLineupReference in the
        // POSITION_FOR_TAKEOFF_ASSIST reply handler.

        // Tell the pilot WHY takeoff assist is coming on — they didn't press
        // a key, and a sudden system-initiated activation needs a verbal
        // breadcrumb. The standard "Takeoff assist active, runway X at Y"
        // callout follows from Toggle() once the position request returns.
        announcer.AnnounceImmediate("Lined up. Activating takeoff assist.");

        // Re-uses the same path as CTRL+T: the POSITION_FOR_TAKEOFF_ASSIST
        // reply handler will see takeoffAssistManager.HasRunwayReference == false,
        // probe TryGetRunwayLineupReference (which succeeds because the event
        // fires AT the lineup-aligned moment), seed the reference, and call
        // Toggle. No special-case wiring needed.
        simConnectManager.RequestPositionForTakeoffAssist();
    }

    private void ToggleHandFlyMode()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        handFlyManager.Toggle();
    }

    private void OnHandFlyModeActiveChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            // Start monitoring pitch, bank, and optionally heading/VS
            simConnectManager.StartHandFlyMonitoring(handFlyManager.MonitorHeading, handFlyManager.MonitorVerticalSpeed);

            // Register global H, V, Q hotkeys for quick access during hand fly mode
            bool hotkeysRegistered = hotkeyManager.RegisterHandFlyHotkeys();
            if (!hotkeysRegistered)
            {
                // Registration failed - likely another application is using H, V, or Q keys
                announcer.Announce("Hand fly mode active. Quick access keys unavailable. Use output mode for H, V, Q.");
            }
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopHandFlyMonitoring();

            // Unregister global H, V, Q hotkeys
            hotkeyManager.UnregisterHandFlyHotkeys();

            // Visual guidance is now independent of HandFly mode — do NOT stop it just
            // because HandFly is being toggled off. VG runs its own attitude monitoring
            // (VISUAL_GUIDANCE_PITCH / VISUAL_GUIDANCE_BANK) and has nothing to lose from
            // HandFly going inactive. If anything, HandFly turning off makes VG audio
            // cleaner because there are now only two tones playing instead of three.
        }
    }

    private void ToggleVisualGuidance()
    {
        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator");
            return;
        }

        visualGuidanceManager.Toggle();
    }

    private void OnVisualGuidanceActiveChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            // Validation: visual guidance no longer requires HandFly mode — it monitors its
            // own pitch/bank/heading via VISUAL_GUIDANCE_DATA. Decoupled per pilot feedback that
            // HandFly's single tone interfered with VG's dual tones, making it hard to tell
            // which tone to follow. If HandFly happens to also be active, its tone is paused
            // for the duration of VG (see HandFlyManager.SuppressAudio).
            // Use Stop(announce: false) — Toggle has already flipped isActive=true but the user
            // never actually had a running guidance session, so the public "Visual guidance off"
            // callout would be misleading after a validation error.
            var runway = simConnectManager.GetDestinationRunway();
            var airport = simConnectManager.GetDestinationAirport();
            if (runway == null)
            {
                announcer.Announce("No destination runway selected");
                visualGuidanceManager.Stop(announce: false);
                return;
            }
            // Defensive: Initialize() dereferences the airport (MagVar / Altitude). Runway and
            // airport are set as a pair today, so this won't currently fire, but guarding here
            // mirrors the runway check and prevents an NPE if that invariant ever changes.
            if (airport == null)
            {
                announcer.Announce("No destination airport selected");
                visualGuidanceManager.Stop(announce: false);
                return;
            }

            // Task 1 — Destination prefetch (silent, fire-and-forget)
            if (_augmentPrefetched.Add(airport.ICAO))
                _ = _augmentingProvider?.PrefetchAsync(airport.ICAO, force: true);

            // Get user preferences from settings
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            var guidanceToneWaveform = settings.VisualGuidanceToneWaveform;
            var guidanceVolume = settings.VisualGuidanceToneVolume;

            // Initialize visual guidance with runway, audio preferences (desired + optional follower tone),
            // and aircraft-specific tunables from the current aircraft definition.
            visualGuidanceManager.Initialize(
                runway, airport,
                guidanceToneWaveform, guidanceVolume,
                settings.VisualGuidanceCurrentToneWaveform,
                settings.VisualGuidanceCurrentToneVolume,
                settings.VisualGuidanceHardPanTone,
                currentAircraft.GetVisualGuidanceProfile());

            // PMDG 777: if the FMC has a pilot-entered landing Vref, push it as a live
            // override of the profile-default reference Vref. The PMDG SDK doesn't expose
            // AoA (which we read via the standard SimConnect INCIDENCE ALPHA simvar) but
            // it DOES publish FMC_LandingVREF in its CDA broadcast — snapshot it at VG
            // activation time. FBW / Fenix A320 have no equivalent SDK field, so they
            // continue to use the A320 profile default. Snapshot rather than live: if the
            // pilot re-enters Vref mid-approach (rare), they re-toggle VG to pick it up.
            if (currentAircraft?.AircraftCode == "PMDG_777" &&
                simConnectManager?.PMDGDataManager != null)
            {
                double fmcVref = simConnectManager.PMDGDataManager.GetFieldValue("FMC_LandingVREF");
                if (fmcVref > 0)
                {
                    visualGuidanceManager.UpdateReferenceVref(fmcVref);
                    System.Diagnostics.Debug.WriteLine($"[MainForm] VG: pushed PMDG FMC_LandingVREF={fmcVref:F0}kt as ReferenceVref");
                }
            }

            // Start monitoring position variables at 1 Hz
            simConnectManager!.StartVisualGuidanceMonitoring();

            // Silence HandFly's tone if it's also active — VG's two tones use the same
            // Hz/pan mapping as HandFly's single tone, and pilots reported the three tones
            // together were impossible to follow. Announcements (if HandFly's feedback mode
            // includes them) still fire. Idempotent — no-op if HandFly was already silent.
            handFlyManager.SuppressAudio();

            // Register the quick-access hotkey set (H, V, Q, S, D, B, P, A, F). The set is
            // shared with HandFly — VG is a hand-flying scenario with extra audio guidance, so
            // the same per-key readouts apply. The shared registration is reference-counted
            // inside HotkeyManager, so activating both modes is conflict-free; whichever
            // deactivates last releases the keys. If a key fails to register (some other app
            // is holding it globally), the user is told to fall back to output mode.
            bool allQuickKeysRegistered = hotkeyManager.RegisterVisualGuidanceHotkeys();
            if (!allQuickKeysRegistered)
            {
                announcer.Announce("Visual guidance active. Some quick-access keys unavailable; use output mode.");
            }
        }
        else
        {
            // Stop monitoring
            simConnectManager.StopVisualGuidanceMonitoring();

            // Release VG's claim on the quick-access hotkey set. If HandFly is still active,
            // its claim keeps the keys registered; if not, this drops the ref count to zero
            // and unregisters all 9 keys.
            hotkeyManager.UnregisterVisualGuidanceHotkeys();

            // Resume HandFly's tone if HandFly is still active and its feedback mode wants
            // tones. Idempotent — no-op if HandFly is off or in announcements-only mode.
            handFlyManager.ResumeAudio();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle local navigation hotkeys first (only work when app is in focus)
        if (keyData == (Keys.Control | Keys.D1))
        {
            sectionsListBox.Focus();
            return true;
        }
        else if (keyData == (Keys.Control | Keys.D2))
        {
            panelsListBox.Focus();
            return true;
        }
        // Ctrl+3 jumps straight to the current panel's Status Display field, mirroring
        // Ctrl+1 (sections list) / Ctrl+2 (panels list). Status displays are the primary
        // readout for the A320/A380, so a one-key jump to them is high-value.
        //
        // No conflict with the FCU "Pull Speed" global hotkey (also Ctrl+3): that hotkey
        // is only registered while INPUT mode is active, and a registered global hotkey
        // consumes the keystroke before ProcessCmdKey sees it — so this branch only fires
        // when input mode is OFF. This is the exact same coexistence the existing Ctrl+1/
        // Ctrl+2 panel-nav already relies on against FCU Pull-Heading/Pull-Altitude.
        else if (keyData == (Keys.Control | Keys.D3))
        {
            if (currentControls.TryGetValue("_DISPLAY_", out var dispCtrl) && dispCtrl is ListBox dispBox)
            {
                dispBox.Focus();
                // If the list is empty (OnRequest display vars don't auto-update until a
                // refresh), pull live content so the user lands on real status rather than a
                // blank list. The refresh is silent; the screen reader reads the list itself.
                // If it already has content (continuously-monitored vars / a prior refresh),
                // leave it untouched so NVDA reads the current value immediately.
                if (dispBox.Items.Count == 0 &&
                    currentControls.TryGetValue("_REFRESH_", out var refreshOnJump) &&
                    refreshOnJump is Button jumpRefreshBtn && jumpRefreshBtn.Enabled)
                {
                    jumpRefreshBtn.PerformClick();
                }
            }
            else
            {
                announcer.AnnounceImmediate("No status display on this panel.");
            }
            return true;
        }
        // F5 refreshes the current panel's Status Display without leaving the
        // edit field/combo you're on (easier than tabbing to the Refresh button).
        else if (keyData == Keys.F5 &&
                 currentControls.TryGetValue("_REFRESH_", out var refreshCtrl) &&
                 refreshCtrl is Button refreshBtn && refreshBtn.Enabled)
        {
            // F5 must not steal focus from the status box the user is reading. Capture the
            // box as the focus-return target BEFORE PerformClick (the async refresh moves
            // focus onto the Refresh button); refreshButton.Click restores it when done.
            if (currentControls.TryGetValue("_DISPLAY_", out var dispCtrl) && dispCtrl is Control dispC && dispC.Focused)
                _refreshFocusReturn = dispC;
            refreshBtn.PerformClick();
            return true;
        }

        // Let hotkey manager process other hotkeys
        if (hotkeyManager.ProcessKeyDown(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void WndProc(ref Message m)
    {
        // Process hotkey messages first
        if (hotkeyManager != null && hotkeyManager.ProcessWindowMessage(ref m))
        {
            return;
        }
        
        // Then process SimConnect messages
        if (simConnectManager != null)
        {
            simConnectManager.ProcessWindowMessage(ref m);
        }

        // Route messages destined for the GSX SimConnect client (distinct
        // WM_USER id 0x0403). Safe to call unconditionally; it filters on id.
        _gsxService?.ProcessWindowMessage(ref m);

        base.WndProc(ref m);
    }
}
