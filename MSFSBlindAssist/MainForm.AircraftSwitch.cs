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
    private void BridgeProbeTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Gate on FULL detection, not just the socket: a probe started while the
            // sim is still in the menu burns its attempts against an empty world
            // (calc writes no-op without an aircraft) and falsely logs "gave up" —
            // observed live 2026-06-12 (all 40 attempts spent before the A320 loaded).
            // Treating not-fully-connected like disconnected also re-arms the probe
            // on every aircraft detection / swap.
            if (simConnectManager == null || !simConnectManager.IsConnected || !simConnectManager.IsFullyConnected)
            {
                _bridgeProbeWasDisconnected = true;
                return;
            }
            if (_bridgeProbeWasDisconnected)
            {
                // Fresh connection/aircraft: re-arm the probe (CalcPathVerified resets on teardown).
                _bridgeProbeWasDisconnected = false;
                _bridgeProbeAttempts = 0;
                _bridgeProbeAwaitingRead = false;
                _bridgeProbeRebound = false;
                simConnectManager.ResetCalcPathProbe();
            }
            if (simConnectManager.CalcPathVerified || simConnectManager.CalcPathProbeConcluded) return;
            // Only the FBW defs register the probe var; other aircraft can never verify —
            // conclude immediately so dotted events route via the legacy transport.
            if (currentAircraft is not (Aircraft.FlyByWireA320Definition or Aircraft.FlyByWireA380Definition))
            {
                simConnectManager.MarkCalcPathProbeConcluded();
                return;
            }

            if (_bridgeProbeAwaitingRead)
            {
                double cached = simConnectManager.GetCachedVariableValue("MSFSBA_BRIDGE_PROBE") ?? 0;
                if ((int)Math.Round(cached) == _bridgeProbeNonce)
                {
                    simConnectManager.MarkCalcPathVerified();
                    return;
                }
                // First mismatch: the probe L:var did not exist when the data
                // definitions were registered at connect, and a def bound to a
                // nonexistent L:var never delivers — re-bind it now that the
                // first write has created the var (live finding 2026-06-12:
                // writes held at the sim while our reads stayed empty).
                if (!_bridgeProbeRebound)
                {
                    _bridgeProbeRebound = true;
                    simConnectManager.RebindVariableDataDefinition("MSFSBA_BRIDGE_PROBE");
                }
                _bridgeProbeNonce = (_bridgeProbeNonce % 16384) + 1; // new nonce each retry
            }
            if (_bridgeProbeAttempts >= 40)
            {
                // All 40 writes have been issued and attempt 40's read-back (above) just
                // failed — give up: module absent or data-def read failing.
                simConnectManager.MarkCalcPathProbeConcluded();
                return;
            }
            _bridgeProbeAttempts++;
            simConnectManager.ExecuteCalculatorCode($"{_bridgeProbeNonce} (>L:MSFSBA_BRIDGE_PROBE)");
            simConnectManager.RequestVariable("MSFSBA_BRIDGE_PROBE", forceUpdate: true);
            _bridgeProbeAwaitingRead = true;
        }
        catch { /* a probe fault must never disturb the app */ }
    }

    private IAircraftDefinition LoadAircraftFromCode(string aircraftCode)
    {
        return aircraftCode switch
        {
            "A320" => new FlyByWireA320Definition(),
            "FENIX_A320CEO" => new FenixA320Definition(),
            "PMDG_777" => new PMDG777Definition(),
            "FBW_A380" => new FlyByWireA380Definition(),
            "PMDG_737" => new PMDG737Definition(),
            "HS_787" => new HorizonSim787Definition(),
            // Future aircraft will be added here
            _ => new FlyByWireA320Definition() // Default to A320
        };
    }

    private void OnSimulatorVersionDetected(object? sender, string version)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnSimulatorVersionDetected(sender, version)));
            return;
        }

        // Announce the detected simulator version
        announcer.Announce(version);
    }

    private void OnConnectionStatusChanged(object? sender, string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnConnectionStatusChanged(sender, status)));
            return;
        }

        statusLabel.Text = status;

        if (status.StartsWith("Connected to"))
        {
            // Start event batching timer for high-volume variable updates
            eventBatchTimer?.Start();
            Log.Debug("MainForm", "Event batching timer started");

            announcer.Announce(status);
            announcer.Announce($"{currentAircraft.AircraftName} Profile and panels active");

            // Start the Access GSX service alongside the main SimConnect
            // client. Safe to call repeatedly — it no-ops if already open.
            try { _gsxService?.Start(); }
            catch (Exception ex)
            {
                Log.Debug("MainForm", $"GsxService.Start failed: {ex.Message}");
            }

            // After SimConnect connects, if current aircraft is a PMDG type, initialize data manager.
            // Use IPMDGAircraft (not == "PMDG_777") so the 737 NG3 is initialized too.
            if (currentAircraft is IPMDGAircraft)
            {
                simConnectManager.InitializePMDG(currentAircraft);
                if (simConnectManager.PMDGDataManager != null)
                {
                    simConnectManager.PMDGDataManager.VariableChanged += OnPMDGVariableChanged;
                }
                // Dispose any existing PROG monitor — it holds a reference
                // to the previous data-manager instance (which is now
                // disposed if this is a reconnect, or never existed if
                // this is the first connect). EnsurePMDGProgPageMonitor
                // will recreate against the fresh data manager.
                if (pmdgProgPageMonitor != null)
                {
                    pmdgProgPageMonitor.Dispose();
                    pmdgProgPageMonitor = null;
                }
                EnsurePMDGProgPageMonitor();
            }

            // Automatically switch database if simulator version doesn't match
            CheckAndSwitchDatabase();

            // Request all current values when connected
            RequestAllCurrentValues();

            // Start a grace period before enabling continuous variable announcements
            // This prevents initial ECAM messages and other variables from being announced
            // when connecting to a cold and dark aircraft. Also mute the announcer's
            // automatic paths so aircraft-specific ProcessSimVarUpdate branches (which
            // announce directly, bypassing simVarMonitor) stay silent on first detect —
            // e.g. the A380 altimeter setting. User hotkeys (AnnounceImmediate) still talk.
            // GATED TO THE A380: this extra announcer-level mute was added for the A380's
            // direct-announce branches; other aircraft keep their prior behaviour (the
            // simVarMonitor + ECAM grace below already applies to every aircraft).
            if (announcer != null && currentAircraft?.AircraftCode == "FBW_A380") announcer.Suppressed = true;
            System.Windows.Forms.Timer announcementGracePeriodTimer = new System.Windows.Forms.Timer();
            announcementGracePeriodTimer.Interval = 5000; // 5 second grace period
            announcementGracePeriodTimer.Tick += (s, e) =>
            {
                announcementGracePeriodTimer.Stop();
                announcementGracePeriodTimer.Dispose();
                if (announcer != null) announcer.Suppressed = false;
                simVarMonitor.EnableAnnouncements();
                simConnectManager.EnableECAMAnnouncements();
            };
            announcementGracePeriodTimer.Start();

            // Start nearest city announcement timer if enabled in settings
            StartNearestCityAnnouncementTimer();

            // Start weather auto-announcement timer
            _prevPrecipState = -1;
            _prevPrecipRate = -1;
            _prevInCloud = -1;
            _prevVisibility = -1;
            _prevVisLow = false;
            _prevAsPrecip = null;
            _iceAccretionTracker.Reset();
            _announcedSigmetKeys.Clear();
            _announcedPirepKeys.Clear();
            _sigmetKeysClearedAt = DateTime.UtcNow;
            weatherAnnouncementTimer?.Start();
        }
        else if (status.Contains("Disconnected"))
        {
            // Stop event batching timer and clear queue
            eventBatchTimer?.Stop();

            // Stop nearest city announcement timer
            nearestCityAnnouncementTimer?.Stop();

            // Stop weather auto-announcement timer
            weatherAnnouncementTimer?.Stop();

            // A disconnect voids any pending liftoff → Hand Fly handoff. The
            // fire-time gates would abort anyway (IsConnected check), but don't
            // leave a dead timer pending across a reconnect — and bump the
            // confirm token: a fresh-position confirm whose response was lost
            // to this disconnect leaks its one-shot handler, which would
            // otherwise fire on the NEXT flight's first position response and
            // bypass the debounce.
            _liftoffHandoffTimer?.Stop();
            _liftoffHandoffConfirmToken++;

            // Clear event queue and reset counters
            while (eventQueue.TryDequeue(out _)) { }
            queuedEventCount = 0;
            droppedEventCount = 0;
            Log.Debug("MainForm", "Event batching timer stopped, queue cleared");

            announcer.Announce(status);
            // Reset window title when disconnected
            this.Text = "MSFS Blind Assist";
            // Disable announcements when disconnected
            simVarMonitor.Reset();
            // Reset ECAM suppression flag for next connection
            simConnectManager.SuppressECAMAnnouncements = true;

            // Stop the GSX SimConnect client so we don't leak it across
            // reconnects. Start() will be called again on the next connect.
            try { _gsxService?.Stop(); }
            catch (Exception ex)
            {
                Log.Debug("MainForm", $"GsxService.Stop failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Called when SimConnect resolves the loaded aircraft's ICAO type designator.
    /// Looks up the aircraft's gsx.cfg geometry and feeds the preferred-door SIDE to the
    /// docking guidance manager (the "jetway on your left/right" cue). The longitudinal
    /// door offset is deliberately NOT plumbed anywhere — the stop math aligns the aircraft
    /// DATUM to the stop position (see the comment block in DockingGuidanceManager); the
    /// offset's only former consumer was a telemetry column.
    /// </summary>
    private void OnAircraftIcaoTypeDetected(object? sender, string icaoType)
    {
        // Run on a background thread so neither the UI thread nor the SimConnect
        // thread is blocked by the GsxAirplaneProfile disk scan (~12 s on first call).
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var geom = _gsxAirplaneProfile.GetGeometry(icaoType);
                bool shouldRefresh;
                if (geom == null && GsxLikelyInstalled())
                {
                    // _refreshedIcaos is mutated from concurrently-running Task.Run handlers
                    // (the event fires on connect, on every AircraftLoaded, and again from the
                    // aircraft.cfg catalog fallback) — HashSet is not thread-safe, so lock it.
                    lock (_refreshedIcaos) { shouldRefresh = _refreshedIcaos.Add(icaoType ?? ""); }
                }
                else shouldRefresh = false;

                if (shouldRefresh)
                {
                    // First miss for this ICAO — rebuild the map in case a gsx.cfg was
                    // written after startup (e.g. user just installed a GSX profile via
                    // the GSX installer into %APPDATA%\Virtuali\Airplanes). Only useful
                    // when GSX is actually installed; skip the disk scan otherwise.
                    _gsxAirplaneProfile.Refresh();
                    geom = _gsxAirplaneProfile.GetGeometry(icaoType);
                }

                // Stale-task guard: the Refresh() scan can take seconds; if the user swapped
                // aircraft meanwhile, a late-finishing task for the OLD ICAO must not clobber
                // the NEW aircraft's door side (the spoken "jetway on your left/right" cue).
                // Mirrors the title-snapshot recheck in SimConnectManager's catalog fallback.
                if (!string.Equals(simConnectManager.CurrentAircraftIcaoType, icaoType, StringComparison.OrdinalIgnoreCase))
                    return;

                string side = geom?.Side switch
                {
                    MSFSBlindAssist.Services.Gsx.DoorSide.Left => "left",
                    MSFSBlindAssist.Services.Gsx.DoorSide.Right => "right",
                    _ => ""
                };
                dockingGuidanceManager.SetDoorSide(side);
                Log.Debug("MainForm", $"Door side for ICAO '{icaoType}': '{side}'");

                try { _dockingAircraftLog.Info($"ICAO=\"{icaoType}\"  doorSide={side}"); }
                catch { /* never propagate log failures */ }
            }
            catch { }
        });
    }

    /// <summary>
    /// Validates that the selected database matches the running simulator version
    /// </summary>
    /// <returns>True if validation passes or user chooses to continue anyway, false otherwise</returns>
    private bool ValidateDatabaseSimulatorMatch()
    {
        // If no database provider, skip validation
        if (airportDataProvider == null)
            return true;

        string simVersion = simConnectManager.DetectedSimulatorVersion;
        string dbType = airportDataProvider.DatabaseType;

        // Unknown simulator version - allow operation
        if (simVersion == "Unknown")
            return true;

        bool isFs2024Sim = simVersion.Contains("2024");
        bool isFs2024Db = dbType.Contains("FS2024");

        // Check for mismatch
        if (isFs2024Sim && !isFs2024Db)
        {
            // FS2024 sim with FS2020 database
            var result = DatabaseMismatchDialog.ShowMismatchWarning("FS2024", dbType);

            if (result == DialogResult.Yes)
            {
                // Open database settings
                DatabaseSettingsMenuItem_Click(null, EventArgs.Empty);
                return false; // Cancel teleport
            }
            else if (result == DialogResult.Cancel)
            {
                return false; // Cancel teleport
            }
            // If "No", continue anyway
        }
        else if (!isFs2024Sim && isFs2024Db)
        {
            // FS2020 sim with FS2024 database
            var result = DatabaseMismatchDialog.ShowMismatchWarning("FS2020", dbType);

            if (result == DialogResult.Yes)
            {
                DatabaseSettingsMenuItem_Click(null, EventArgs.Empty);
                return false;
            }
            else if (result == DialogResult.Cancel)
            {
                return false;
            }
        }

        return true; // Match is valid or user chose to continue
    }

    /// <summary>
    /// Public accessor for the PROG-page monitor. PMDG777Definition's distance
    /// handlers read its <see cref="PMDGProgPageMonitor.LastProgData"/> when
    /// Enhanced distance mode is on. Returns null when the monitor isn't
    /// running (non-PMDG aircraft or Enhanced mode off).
    /// </summary>
    public MSFSBlindAssist.Services.PMDGProgPageMonitor? GetPMDGProgPageMonitor() => pmdgProgPageMonitor;

    /// <summary>
    /// Starts or stops the PROG-page monitor to match current state. Called
    /// at startup, on aircraft swap, and after the FMC Settings dialog
    /// closes with OK. The monitor only runs when both conditions hold:
    /// (a) a PMDG aircraft is loaded, (b) Enhanced distance mode is on.
    /// </summary>
    private void EnsurePMDGProgPageMonitor()
    {
        bool wantRunning = currentAircraft != null
            && currentAircraft.AircraftCode.StartsWith("PMDG_", StringComparison.Ordinal)
            && Settings.SettingsManager.Current.PMDGEnhancedDistanceMode;

        if (wantRunning)
        {
            // Lazy-create on first need. Recreated whenever the
            // PMDG data manager changes (e.g., after aircraft swap)
            // because the monitor holds a reference to a specific
            // data-manager instance.
            // The PROG-page monitor is currently 777-specific; cast
            // through the interface slot. Non-777 PMDG aircraft will
            // need their own monitor wiring (Phase D).
            var dm = simConnectManager?.PMDGDataManager as PMDG777DataManager;
            if (dm == null) return;
            if (pmdgProgPageMonitor == null)
            {
                pmdgProgPageMonitor = new MSFSBlindAssist.Services.PMDGProgPageMonitor(dm);
            }
            if (!pmdgProgPageMonitor.IsRunning)
            {
                pmdgProgPageMonitor.Start();
            }
        }
        else if (pmdgProgPageMonitor != null)
        {
            pmdgProgPageMonitor.Stop();
        }
    }

    // Start the background A380X E/WD failure monitor. The sensed abnormal/warning
    // PROCEDURES (failure titles + ECAM action items) have NO SimVar — the FwsCore
    // publishes them on an in-process EventBus and only the E/WD instrument renders
    // them — so they are scraped from the E/WD Coherent view and announced here.
    // Memos (PARK BRK, etc.) are NOT announced by this client; the SimVar EWD_LOWER
    // path already covers them.
    private void StartA380EWDMonitor()
    {
        if (coherentEWDClient != null) return;
        // Hand E/WD call-outs to the scrape: suppress the SimVar EWD_LOWER memo
        // auto-announce so failures AND memos come from the one DOM source.
        if (currentAircraft is FlyByWireA380Definition a380def) a380def.EwdScrapeHandlesAnnounce = true;
        coherentEWDClient = new CoherentEWDClient();
        // Let the SD "Upper E/WD" page read the live E/WD content through this one shared
        // socket (a second client on A380X_EWD is rejected — one inspector per page).
        if (currentAircraft is FlyByWireA380Definition a380ewd) a380ewd.EwdMonitor = coherentEWDClient;
        coherentEWDClient.LineAnnounced += line =>
        {
            // Honour the Ctrl+M / Ctrl+E ECAM-monitor mute (same sentinel the
            // SimVar EWD memo path consults), so the user can silence E/WD chatter.
            if (Settings.SettingsManager.Current.A380DisabledMonitorVariablesSet.Contains(
                    Forms.FBWA380.FBWA380MonitorManagerForm.EcamMemosKey))
                return;
            // Audio dedup: skip a memo the FwsFailureClient already calls out as an active
            // warning (e.g. XPDR STBY — amber in the FWS list AND green in the memos).
            if (currentAircraft is FlyByWireA380Definition a380dd && a380dd.IsTextAnActiveWarning(line))
                return;
            announcer.Announce(line);
        };
        coherentEWDClient.Start();

        // Authoritative failure announcer — reads the FwsCore (presentedFailures) directly,
        // so a master caution always names its cause even for WIP procedures the E/WD DOM
        // doesn't render (ENG 3/4 FAIL). It OWNS failure call-outs; the DOM scrape above
        // therefore stops announcing warning lines (no double-speak) but keeps memos/PFD/
        // status. The live list is pushed into the A380 def for the displays panel.
        coherentEWDClient.AnnounceWarnings = false;
        coherentFwsFailureClient = new CoherentFwsFailureClient();
        coherentFwsFailureClient.FailureAnnounced += line =>
        {
            if (Settings.SettingsManager.Current.A380DisabledMonitorVariablesSet.Contains(
                    Forms.FBWA380.FBWA380MonitorManagerForm.EcamMemosKey))
                return;
            announcer.Announce(line);
        };
        coherentFwsFailureClient.FailuresChanged += (ewd, status) =>
        {
            if (currentAircraft is FlyByWireA380Definition a380f) a380f.SetActiveFwsFailures(ewd, status);
        };
        coherentFwsFailureClient.Start();
    }

    private void StopA380EWDMonitor(IAircraftDefinition? owner = null)
    {
        // `owner` lets the aircraft-swap cleanup clear the flag on the OUTGOING def —
        // by the time the cleanup runs, currentAircraft is already the NEW aircraft,
        // so the no-arg form would silently no-op when leaving the A380.
        if ((owner ?? currentAircraft) is FlyByWireA380Definition a380def) a380def.EwdScrapeHandlesAnnounce = false;
        coherentFwsFailureClient?.Dispose();
        coherentFwsFailureClient = null;
        if (coherentEWDClient == null) return;
        coherentEWDClient.Dispose();
        coherentEWDClient = null;
    }

    // Start the always-on HS787 Coherent monitors (idempotent): the IRS-alignment reader (writes
    // MSFSBA_IRS_ALIGN_STATE/_MINUTES from the PFD view) and the EICAS CAS alert monitor (announces
    // new cautions/warnings from the MFD_1 view).
    private void StartHS787IrsMonitor()
    {
        if (hs787IrsClient == null)
        {
            hs787IrsClient = new SimConnect.CoherentHS787IrsClient();
            hs787IrsClient.Start();
        }
        if (hs787CasClient == null)
        {
            hs787CasClient = new SimConnect.CoherentHS787CasClient();
            hs787CasClient.Start();
        }
    }

    // Open the EICAS alert window on demand (the HS787 Alt+E key). A navigable read-only window
    // (arrow keys to read every active warning/caution/advisory, Escape to close), refreshed live —
    // not a one-shot spoken read-back.
    public void AnnounceHs787CasAlerts()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        // The EICAS window reads engine indications + the CAS monitor's alert list. If the monitor
        // isn't up yet (HS787 not fully initialised, or a startup failure left it null), speak a cue
        // rather than returning in total silence — a blind pilot can't otherwise tell whether there
        // are zero alerts or the feature is broken.
        if (hs787CasClient == null)
        {
            announcer.AnnounceImmediate("EICAS not available.");
            return;
        }
        if (hs787EicasForm == null || hs787EicasForm.IsDisposed)
            hs787EicasForm = new Forms.HS787.HS787EicasForm(BuildHs787EicasText);
        hs787EicasForm.ShowForm();
    }

    // Full EICAS read-out for the Alt+E window: the primary/secondary engine indications (per
    // engine N1 / EGT / N2 / oil), fuel + gross weight + TAT, then the live crew-alert list — i.e.
    // what the real 787 EICAS shows, not just the alert messages. Values come from the cached
    // HS787_Eicas* SimVars (see GetVariables); the alerts from the always-on CAS monitor.
    private string BuildHs787EicasText()
    {
        double GV(string k) => simConnectManager?.GetCachedVariableValue(k) ?? 0;
        int Pct(string k) => (int)Math.Round(GV(k) * 100);     // N1/N2 are 0..1 ratios
        int C(string k) => (int)Math.Round(GV(k));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("EICAS");
        sb.AppendLine($"Engine 1 N1: {Pct("HS787_EicasN1_1")}%");
        sb.AppendLine($"Engine 1 EGT: {C("HS787_EicasEGT_1")} C");
        sb.AppendLine($"Engine 1 N2: {Pct("HS787_EicasN2_1")}%");
        sb.AppendLine($"Engine 1 Oil Pressure: {C("HS787_EicasOilP_1")} psi");
        sb.AppendLine($"Engine 1 Oil Temp: {C("HS787_EicasOilT_1")} C");
        sb.AppendLine($"Engine 2 N1: {Pct("HS787_EicasN1_2")}%");
        sb.AppendLine($"Engine 2 EGT: {C("HS787_EicasEGT_2")} C");
        sb.AppendLine($"Engine 2 N2: {Pct("HS787_EicasN2_2")}%");
        sb.AppendLine($"Engine 2 Oil Pressure: {C("HS787_EicasOilP_2")} psi");
        sb.AppendLine($"Engine 2 Oil Temp: {C("HS787_EicasOilT_2")} C");
        sb.AppendLine($"Total Fuel: {C("HS787_EicasFuelKg")} kg");
        sb.AppendLine($"Gross Weight: {Math.Round(GV("HS787_EicasGwKg") / 1000.0, 1)} t");
        sb.AppendLine($"TAT: {C("HS787_EicasTat")} C");
        sb.AppendLine();
        sb.Append(hs787CasClient?.GetAlertsText() ?? "");
        return sb.ToString();
    }

    // Dispose the HS787 CDU / EFB windows + the IRS monitor (e.g. on aircraft swap)
    // so their Coherent debugger connections close. There is no HTTP bridge to stop.
    private void DisposeHS787Forms()
    {
        if (hs787FMCForm != null && !hs787FMCForm.IsDisposed)
        {
            hs787FMCForm.Dispose();
            hs787FMCForm = null;
        }

        hs787IrsClient?.Dispose();
        hs787IrsClient = null;
        if (hs787EicasForm != null && !hs787EicasForm.IsDisposed) hs787EicasForm.Dispose();
        hs787EicasForm = null;
        hs787CasClient?.Dispose();
        hs787CasClient = null;
    }

    private void SwitchAircraft(IAircraftDefinition newAircraft)
    {
        // Capture the OUTGOING definition BEFORE reassignment — several cleanup steps
        // below must act on the old instance (its motion timers, its EWD-announce flag),
        // and `currentAircraft` already points at the new aircraft by the time the
        // cleanup block runs.
        var oldAircraft = currentAircraft;
        // Halt the old A380 def's seat-motor / slider-ramp timers — they keep firing
        // calc-path L:var writes at the new aircraft otherwise (sim stays connected).
        // Both FBW defs' StopAllMotion also dispose their TCAS RA compose timer and
        // any tracked hotkey windows (FCU/Baro/E/WD) the old def instance created.
        (oldAircraft as FlyByWireA380Definition)?.StopAllMotion();
        (oldAircraft as FlyByWireA320Definition)?.StopAllMotion();
        // The HS787 def owns its synoptic-display window (a live MFD_2 Coherent socket) + the
        // autopilot window (a refresh timer) — dispose them so they don't outlive the def.
        (oldAircraft as HorizonSim787Definition)?.CloseAuxWindows();

        // An armed liftoff → Hand Fly handoff must not survive the switch — its
        // confirm could otherwise fire against the new aircraft in the middle of
        // the re-registration churn below (same hygiene as the disconnect path
        // in OnConnectionStatusChanged). The token bump also invalidates an
        // already-in-flight confirm callback.
        _liftoffHandoffTimer?.Stop();
        _liftoffHandoffConfirmToken++;

        // Update the aircraft instance
        currentAircraft = newAircraft;

        taxiGuidanceManager.TurnLeadSeconds = newAircraft.TaxiTurnLeadSeconds;

        // Refresh aircraft-conditional menu items (FMC Settings is PMDG-only).
        UpdateAircraftSpecificMenuItems();

        // Dispose the old PROG-page monitor — it references the previous
        // aircraft's data manager. Recreation happens later, AFTER
        // InitializePMDG() has produced a fresh data manager for the new
        // aircraft (see EnsurePMDGProgPageMonitor call near the end of this
        // method). Calling EnsurePMDGProgPageMonitor here would no-op for a
        // PMDG-to-PMDG swap because the new data manager doesn't yet exist.
        if (pmdgProgPageMonitor != null)
        {
            pmdgProgPageMonitor.Dispose();
            pmdgProgPageMonitor = null;
        }

        // Invalidate PMDG field map so it rebuilds for the new aircraft
        _pmdgFieldToKeyMap = null;

        // Update SimConnectManager
        simConnectManager.CurrentAircraft = currentAircraft;

        // Reset monitor to clear cache and disable announcements during transition
        // This prevents flooding TTS with hundreds of "initial" values when switching aircraft
        simVarMonitor.Reset();
        simConnectManager.SuppressECAMAnnouncements = true;

        // Hazard announcers re-baseline on switch — stale category/ice baselines
        // from the previous airframe must not announce as "changes".
        activeSkyWeatherMonitor?.ResetTurbulenceTracker();
        _iceAccretionTracker.Reset();

        // Re-register variables and restart continuous monitoring for new aircraft
        if (simConnectManager.IsConnected)
        {
            simConnectManager.ReregisterAllVariables();
            simConnectManager.RestartContinuousMonitoring();
            // Re-read ATC MODEL / ICAO for the newly selected aircraft definition so
            // the door-offset map is refreshed for the new aircraft profile.
            simConnectManager.RequestAircraftInfo();

            // First-detect announcer grace for a mid-session switch TO the A380 — the
            // connect handler arms this on initial connection, but a swap reaches the
            // A380's direct-announce ProcessSimVarUpdate branches (E/WD memo codes,
            // flight phase) during batch re-registration without it. AnnounceImmediate
            // (user hotkeys) is unaffected.
            if (newAircraft is FlyByWireA380Definition) announcer.Suppressed = true;

            // Start grace period for new aircraft variables to populate
            // This prevents announcement flood when hundreds of continuous variables send initial values
            System.Windows.Forms.Timer gracePeriodTimer = new System.Windows.Forms.Timer();
            gracePeriodTimer.Interval = 5000; // 5 second grace period (same as initial connection)
            gracePeriodTimer.Tick += (s, e) =>
            {
                gracePeriodTimer.Stop();
                gracePeriodTimer.Dispose();
                simVarMonitor.EnableAnnouncements();
                simConnectManager.EnableECAMAnnouncements();
                // Unconditional: clearing an already-false flag is harmless, and NOT
                // clearing it would mute every queued announce forever.
                announcer.Suppressed = false;
                Log.Debug("MainForm", "Aircraft switch grace period ended - announcements enabled");
            };
            gracePeriodTimer.Start();
            Log.Debug("MainForm", "Aircraft switch grace period started (5 seconds)");
        }

        // Update window title
        this.Text = "MSFS Blind Assist";

        // Clear existing UI
        sectionsListBox.Items.Clear();
        panelsListBox.Items.Clear();
        controlsContainer.Controls.Clear();

        // Dispose checklistForm so it reloads for new aircraft
        if (checklistForm != null && !checklistForm.IsDisposed)
        {
            checklistForm.Dispose();
            checklistForm = null;
        }

        // Dispose A380 monitor manager when switching aircraft
        if (fbwA380MonitorManagerForm != null && !fbwA380MonitorManagerForm.IsDisposed)
        {
            fbwA380MonitorManagerForm.Dispose();
            fbwA380MonitorManagerForm = null;
        }
        // Dispose fenixMonitorManagerForm when switching aircraft
        if (fenixMonitorManagerForm != null && !fenixMonitorManagerForm.IsDisposed)
        {
            fenixMonitorManagerForm.Dispose();
            fenixMonitorManagerForm = null;
        }

        // Same for PMDGAnnouncementMonitorForm — its variable list is
        // snapshotted at construction time, so a stale instance would show
        // the previous aircraft's variables after a swap.
        if (pmdgAnnouncementMonitorForm != null && !pmdgAnnouncementMonitorForm.IsDisposed)
        {
            pmdgAnnouncementMonitorForm.Dispose();
            pmdgAnnouncementMonitorForm = null;
        }

        // Dispose Fenix MCDU form and service when switching aircraft
        if (fenixMCDUForm != null && !fenixMCDUForm.IsDisposed)
        {
            fenixMCDUForm.Dispose();
            fenixMCDUForm = null;
        }
        if (fenixEFBForm != null && !fenixEFBForm.IsDisposed)
        {
            fenixEFBForm.Dispose();
            fenixEFBForm = null;
        }
        if (fenixMCDUService != null)
        {
            fenixMCDUService.Dispose();
            fenixMCDUService = null;
        }

        // Dispose FlyByWire MCDU form and service when switching aircraft
        if (flyByWireMCDUForm != null && !flyByWireMCDUForm.IsDisposed)
        {
            flyByWireMCDUForm.Dispose();
            flyByWireMCDUForm = null;
        }
        if (flyByWireMCDUService != null)
        {
            flyByWireMCDUService.Dispose();
            flyByWireMCDUService = null;
        }

        // Dispose PMDG CDU form when switching aircraft
        if (pmdgCDUForm != null && !pmdgCDUForm.IsDisposed)
        {
            pmdgCDUForm.Dispose();
            pmdgCDUForm = null;
        }

        // Dispose FBW A380 MCDU + EFB forms on swap; disposing the forms clears
        // their state-update wiring so the next aircraft doesn't get cross-talk.
        if (fbwA380MCDUForm != null && !fbwA380MCDUForm.IsDisposed)
        {
            fbwA380MCDUForm.Dispose();
            fbwA380MCDUForm = null;
        }
        if (fbwEfbForm != null && !fbwEfbForm.IsDisposed)
        {
            fbwEfbForm.Dispose();
            fbwEfbForm = null;
        }
        // Tear down the Coherent debugger client on every swap; it is
        // recreated below only when the new aircraft is the A380X.
        if (coherentClient != null)
        {
            coherentClient.Dispose();
            coherentClient = null;
        }
        if (coherentEFBClient != null)
        {
            coherentEFBClient.Dispose();
            coherentEFBClient = null;
        }
        // Tear down the PMDG EFB Coherent clients + windows on swap.
        pmdgCoherentEfbCaptainForm?.Dispose(); pmdgCoherentEfbCaptainForm = null;
        pmdgCoherentEfbFirstOfficerForm?.Dispose(); pmdgCoherentEfbFirstOfficerForm = null;
        coherentPmdgEfbCaptain?.Dispose(); coherentPmdgEfbCaptain = null;
        coherentPmdgEfbFirstOfficer?.Dispose(); coherentPmdgEfbFirstOfficer = null;
        if (coherentNDClient != null)
        {
            coherentNDClient.Dispose();
            coherentNDClient = null;
        }
        // The OANS window holds the ND client by reference; dispose it too so a later
        // reopen rebuilds it against the freshly-created client (otherwise it would keep
        // the now-disposed client and never update).
        if (fbwA380OansForm != null && !fbwA380OansForm.IsDisposed)
        {
            fbwA380OansForm.Dispose();
            fbwA380OansForm = null;
        }
        // The RMP window owns its own CoherentDisplayClient + three timers and hides
        // (not closes) on user-close, so it survives a swap polling the old aircraft's
        // view — and on return it would be reused bound to the DISCARDED def instance.
        // Its Dispose(bool) override runs the full teardown.
        if (fbwA380RmpForm != null && !fbwA380RmpForm.IsDisposed)
        {
            fbwA380RmpForm.Dispose();
            fbwA380RmpForm = null;
        }
        // The DCDU window polls the A32NX DCDU view via one-shot evals on a timer —
        // dispose on swap so it can't keep evaluating against the wrong aircraft.
        if (fbwDcduForm != null && !fbwDcduForm.IsDisposed)
        {
            fbwDcduForm.Dispose();
            fbwDcduForm = null;
        }
        // The ECL checklist form is bound (readonly ctor field) to the CoherentEWDClient
        // that StopA380EWDMonitor disposes below; left alive it would be reused on the
        // next A380 load permanently blank against the dead client.
        if (fbwA380ChecklistForm != null && !fbwA380ChecklistForm.IsDisposed)
        {
            fbwA380ChecklistForm.Dispose();
            fbwA380ChecklistForm = null;
        }
        // A32NX monitor manager — same stale-snapshot reason as its A380/Fenix/PMDG
        // siblings, which are already disposed in this block.
        if (fbwA320MonitorManagerForm != null && !fbwA320MonitorManagerForm.IsDisposed)
        {
            fbwA320MonitorManagerForm.Dispose();
            fbwA320MonitorManagerForm = null;
        }
        if (hs787MonitorManagerForm != null && !hs787MonitorManagerForm.IsDisposed)
        {
            hs787MonitorManagerForm.Dispose();
            hs787MonitorManagerForm = null;
        }
        StopA380EWDMonitor(oldAircraft);

        // Dispose HS 787 forms when switching aircraft
        if (hs787FMCForm != null && !hs787FMCForm.IsDisposed)
        {
            hs787FMCForm.Dispose();
            hs787FMCForm = null;
        }

        // PMDG data manager lifecycle
        if (newAircraft is IPMDGAircraft && simConnectManager.IsConnected)
        {
            simConnectManager.InitializePMDG(newAircraft);
            if (simConnectManager.PMDGDataManager != null)
            {
                simConnectManager.PMDGDataManager.VariableChanged += OnPMDGVariableChanged;
            }
        }
        else
        {
            // Unwire events before disposing
            if (simConnectManager.PMDGDataManager != null)
            {
                simConnectManager.PMDGDataManager.VariableChanged -= OnPMDGVariableChanged;
            }
            simConnectManager.DisposePMDG();
        }

        // Start the PROG-page monitor now that the new aircraft's data
        // manager exists (or stop it cleanly if we just left PMDG). This
        // must happen AFTER InitializePMDG so EnsurePMDGProgPageMonitor
        // can see the freshly-created data manager — calling it before the
        // init would silently no-op (see comment above the dispose block).
        EnsurePMDGProgPageMonitor();

        // EFB transport per aircraft. PMDG (Shift+T) and FBW A320/A380 flyPad all
        // read live through the Coherent GT debugger now — the PMDG/A320 clients are
        // created lazily when the user opens the EFB and are disposed by the swap
        // cleanup above. The A380 MCDU client is pre-started so it is connected by
        // the time the user opens the MCDU.
        if (newAircraft.AircraftCode == "FBW_A380")
        {
            coherentClient = new CoherentDebuggerClient();
            coherentClient.Start();
            coherentClient.SetActive(false);   // connect + install agent now; scrape only while the MCDU window is open
            StartA380EWDMonitor();
        }

        // The HS787 CDU + EFB open their own Coherent debugger connections on demand (from
        // their forms) — no HTTP bridge to start. The IRS-alignment monitor runs continuously
        // (so it catches the alignment countdown). When leaving the HS787, dispose its forms +
        // the IRS monitor so their Coherent connections close.
        if (newAircraft.AircraftCode == "HS_787")
            StartHS787IrsMonitor();
        else
            DisposeHS787Forms();

        // Rebuild sections from new aircraft structure
        foreach (var section in currentAircraft.GetPanelStructure().Keys)
        {
            sectionsListBox.Items.Add(section);
        }

        // Update all aircraft menu items' checked state
        UpdateAircraftMenuItems();

        // Announce the switch
        announcer.AnnounceImmediate($"Switched to {currentAircraft.AircraftName}");

        // Save preference
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        settings.LastAircraft = currentAircraft.AircraftCode;
        MSFSBlindAssist.Settings.SettingsManager.Save();

        // Update aircraft-specific menu items visibility
        UpdateAircraftSpecificMenuItems();
    }

    /// <summary>
    /// Toggles visibility of menu items that are only meaningful for specific
    /// aircraft. Called whenever the loaded aircraft changes (and at initial
    /// MainForm_Load). Gates the FMC Settings item — it's hidden for any
    /// aircraft that doesn't have an MCDU/CDU the dialog applies to (PMDG
    /// or Fenix), so the screen reader doesn't surface a settings option
    /// the user can't act on.
    /// </summary>
    private void UpdateAircraftSpecificMenuItems()
    {
        bool isPmdg = currentAircraft != null &&
                      currentAircraft.AircraftCode.StartsWith("PMDG_", StringComparison.Ordinal);
        bool isFenix = currentAircraft != null &&
                       currentAircraft.AircraftCode.StartsWith("FENIX_", StringComparison.Ordinal);
        bool isHs787 = currentAircraft != null &&
                       currentAircraft.AircraftCode.StartsWith("HS_", StringComparison.Ordinal);
        fmcSettingsMenuItem.Visible = isPmdg || isFenix || isHs787;
    }

    /// <summary>
    /// Updates aircraft menu item check states to match the current aircraft.
    /// </summary>
    private void UpdateAircraftMenuItems()
    {
        // Clear all menu item checks first
        flyByWireA320MenuItem.Checked = false;
        fenixA320MenuItem.Checked = false;
        pmdg777MenuItem.Checked = false;
        flyByWireA380MenuItem.Checked = false;
        pmdg737MenuItem.Checked = false;
        horizonSim787MenuItem.Checked = false;

        // Set the check on the current aircraft's menu item
        if (currentAircraft is FlyByWireA320Definition)
        {
            flyByWireA320MenuItem.Checked = true;
        }
        else if (currentAircraft is FenixA320Definition)
        {
            fenixA320MenuItem.Checked = true;
        }
        else if (currentAircraft is PMDG777Definition)
        {
            pmdg777MenuItem.Checked = true;
        }
        else if (currentAircraft is FlyByWireA380Definition)
        {
            flyByWireA380MenuItem.Checked = true;
        }
        else if (currentAircraft is PMDG737Definition)
        {
            pmdg737MenuItem.Checked = true;
        }
        else if (currentAircraft is HorizonSim787Definition)
        {
            horizonSim787MenuItem.Checked = true;
        }
    }

    private void UpdateDatabaseStatusDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateDatabaseStatusDisplay()));
            return;
        }

        try
        {
            if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
            {
                // Database status will be shown in file menu or on-demand
                return;
            }

            // Database info is available but not shown in status bar by default
            // It can be queried when needed (e.g., in database settings dialog)
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error updating database status: {ex.Message}");
        }
    }

    private void RefreshDatabaseProvider()
    {
        // Save current flight plan state before recreating managers
        var savedFlightPlan = flightPlanManager?.CurrentFlightPlan;

        // Reload database provider based on current settings (can be null if not built yet)
        airportDataProvider = DatabaseSelector.SelectProvider();

        // Recreate flight plan manager with new navigation database path
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
        flightPlanManager = new MSFSBlindAssist.Navigation.FlightPlanManager(navigationDatabasePath, airportDataProvider);

        // Restore flight plan state if one existed
        if (savedFlightPlan != null && !savedFlightPlan.IsEmpty())
        {
            // Copy all flight plan data to the new manager's flight plan
            var newFlightPlan = flightPlanManager.CurrentFlightPlan;

            // Copy metadata
            newFlightPlan.DepartureICAO = savedFlightPlan.DepartureICAO;
            newFlightPlan.DepartureRunway = savedFlightPlan.DepartureRunway;
            newFlightPlan.ArrivalICAO = savedFlightPlan.ArrivalICAO;
            newFlightPlan.ArrivalRunway = savedFlightPlan.ArrivalRunway;
            newFlightPlan.SIDName = savedFlightPlan.SIDName;
            newFlightPlan.STARName = savedFlightPlan.STARName;
            newFlightPlan.ApproachName = savedFlightPlan.ApproachName;
            newFlightPlan.SimBriefUsername = savedFlightPlan.SimBriefUsername;
            newFlightPlan.LoadedTime = savedFlightPlan.LoadedTime;

            // Copy all waypoint sections
            newFlightPlan.DepartureAirportWaypoints = new List<WaypointFix>(savedFlightPlan.DepartureAirportWaypoints);
            newFlightPlan.SIDWaypoints = new List<WaypointFix>(savedFlightPlan.SIDWaypoints);
            newFlightPlan.EnrouteWaypoints = new List<WaypointFix>(savedFlightPlan.EnrouteWaypoints);
            newFlightPlan.STARWaypoints = new List<WaypointFix>(savedFlightPlan.STARWaypoints);
            newFlightPlan.ApproachWaypoints = new List<WaypointFix>(savedFlightPlan.ApproachWaypoints);
            newFlightPlan.ArrivalAirportWaypoints = new List<WaypointFix>(savedFlightPlan.ArrivalAirportWaypoints);
        }

        // Close EFB window if open - it will be recreated with the new manager when reopened
        if (electronicFlightBagForm != null && !electronicFlightBagForm.IsDisposed)
        {
            electronicFlightBagForm.Close();
            electronicFlightBagForm = null;
        }

        UpdateDatabaseStatusDisplay();
    }

    /// <summary>
    /// Closes all database connections to allow file operations (like rebuilding databases).
    /// Saves the current flight plan state to restore after reconnection.
    /// </summary>
    public void CloseDatabaseConnections()
    {
        try
        {
            Log.Debug("MainForm", "Closing database connections...");

            // Close EFB window if open - it holds database connections
            if (electronicFlightBagForm != null && !electronicFlightBagForm.IsDisposed)
            {
                electronicFlightBagForm.Close();
                electronicFlightBagForm = null;
            }

            // Set providers to null to release connections
            airportDataProvider = null;
            flightPlanManager = null!;

            // Force garbage collection to ensure connections are fully released
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Log.Debug("MainForm", "Database connections closed");
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error closing database connections: {ex.Message}");
        }
    }

    /// <summary>
    /// Reopens database connections after file operations complete.
    /// Uses RefreshDatabaseProvider() to restore connections and flight plan state.
    /// </summary>
    public void ReopenDatabaseConnections()
    {
        try
        {
            Log.Debug("MainForm", "Reopening database connections...");
            RefreshDatabaseProvider();
            Log.Debug("MainForm", "Database connections reopened");
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error reopening database connections: {ex.Message}");
        }
    }

    /// <summary>
    /// Automatically switches database setting if detected simulator version doesn't match
    /// </summary>
    private void CheckAndSwitchDatabase()
    {
        try
        {
            string detectedSim = simConnectManager.DetectedSimulatorVersion;

            // Unknown simulator - no action needed
            if (detectedSim == "Unknown")
            {
                Log.Debug("MainForm", "Simulator version unknown, keeping current database setting");
                return;
            }

            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            string currentDbSetting = settings.SimulatorVersion ?? "FS2020";

            // Check if database setting matches detected simulator
            bool needsSwitch = false;
            string? targetVersion = null;

            if (detectedSim == "FS2024" && currentDbSetting != "FS2024")
            {
                needsSwitch = true;
                targetVersion = "FS2024";
            }
            else if (detectedSim == "FS2020" && currentDbSetting != "FS2020")
            {
                needsSwitch = true;
                targetVersion = "FS2020";
            }

            if (needsSwitch && targetVersion != null)
            {
                Log.Debug("MainForm", $"Auto-switching database from {currentDbSetting} to {targetVersion}");

                // Update settings
                settings.SimulatorVersion = targetVersion;
                MSFSBlindAssist.Settings.SettingsManager.Save(settings);

                // Reload database provider
                RefreshDatabaseProvider();

                // Announce the change to the user
                string announcement = $"Database automatically switched to {targetVersion}";
                Log.Debug("MainForm", $"{announcement}");
                announcer.Announce(announcement);
            }
            else
            {
                Log.Debug("MainForm", $"Database setting ({currentDbSetting}) already matches detected simulator ({detectedSim})");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error in CheckAndSwitchDatabase: {ex.Message}");
        }
    }
}
