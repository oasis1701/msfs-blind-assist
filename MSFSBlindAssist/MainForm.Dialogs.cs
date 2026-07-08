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
    /// <summary>
    /// Open (or refocus) the Access GSX form. The underlying GsxService runs
    /// from connect-time, independently of this form, so the form is just a
    /// UI surface for the existing connection.
    /// </summary>
    private void ShowAccessGSXForm()
    {
        if (_gsxService == null)
        {
            announcer.AnnounceImmediate("Access GSX: service not initialized.");
            return;
        }

        if (!_gsxService.IsConnected)
        {
            announcer.AnnounceImmediate("Access GSX: not connected to the simulator.");
            return;
        }

        if (_accessGsxForm == null || _accessGsxForm.IsDisposed)
        {
            _accessGsxForm = new Forms.AccessGSXForm(_gsxService, announcer);
        }

        // Show ownerless so the window is an independent top-level — MainForm
        // stays usable, and the GSX window gets its own taskbar entry. The
        // brief TopMost flash brings it to the foreground without keeping it
        // pinned (same pattern as HS787FMCForm.ShowForm).
        if (!_accessGsxForm.Visible)
            _accessGsxForm.Show();
        _accessGsxForm.TopMost = true;
        _accessGsxForm.TopMost = false;
        _accessGsxForm.BringToFront();
        _accessGsxForm.Activate();
    }

    private void ShowRunwayTeleportDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
            return;
        }

        // Validate database matches simulator
        if (!ValidateDatabaseSimulatorMatch())
            return;

        var dialog = new RunwayTeleportForm(airportDataProvider, announcer);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.SelectedRunway != null && dialog.SelectedAirport != null)
            {
                simConnectManager.TeleportToRunway(dialog.SelectedRunway, dialog.SelectedAirport);
            }
        }
    }

    private void ShowGateTeleportDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
            return;
        }

        // Validate database matches simulator
        if (!ValidateDatabaseSimulatorMatch())
            return;

        var dialog = new GateTeleportForm(airportDataProvider, announcer, simConnectManager.AircraftWingSpan, BuildGateDataSource());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.SelectedParkingSpot != null && dialog.SelectedAirport != null)
            {
                simConnectManager.TeleportToParkingSpot(dialog.SelectedParkingSpot, dialog.SelectedAirport);
            }
        }
    }

    private void ShowLocationInfoDialog()
    {
        // Deactivate output hotkey mode before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        if (!simConnectManager.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator. Cannot get location information.");
            return;
        }

        try
        {
            announcer.AnnounceImmediate("Requesting aircraft position...");

            simConnectManager.RequestAircraftPositionAsync((position) =>
            {
                // This callback runs when position data is received
                try
                {
                    var locationForm = new Forms.LocationInfoForm(position.Latitude, position.Longitude, announcer);
                    locationForm.Show();
                }
                catch (Exception ex)
                {
                    announcer.AnnounceImmediate($"Error displaying location information: {ex.Message}");
                    Log.Debug("MainForm", $"Error in position callback: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Error requesting location information: {ex.Message}");
            Log.Debug("MainForm", $"Error in ShowLocationInfoDialog: {ex.Message}");
        }
    }

    private void OpenWeatherRadarWindow()
    {
        try
        {
            if (weatherRadarForm == null || weatherRadarForm.IsDisposed)
                weatherRadarForm = new Forms.WeatherRadarForm(announcer, simConnectManager);
            weatherRadarForm.ShowForm();
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Error opening weather radar: {ex.Message}");
        }
    }

    private void OpenTcasWindow()
    {
        try
        {
            if (tcasForm == null || tcasForm.IsDisposed)
            {
                var gateResolver = new Services.GateResolver(Database.DatabaseSelector.SelectProvider());
                tcasForm = new Forms.TcasForm(tcasService!, announcer, gateResolver);
            }
            tcasForm.ShowForm();
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Error opening TCAS: {ex.Message}");
        }
    }

    private void OpenSimBriefBriefing()
    {
        try
        {
            announcer.AnnounceImmediate("Opening your SimBrief briefing");
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://dispatch.simbrief.com/briefing/latest",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Error opening SimBrief briefing: {ex.Message}");
            Log.Debug("MainForm", $"Error in OpenSimBriefBriefing: {ex.Message}");
        }
    }

    private void ShowDestinationRunwayDialog()
    {
        // Ensure output hotkey mode is deactivated before showing modal dialog
        hotkeyManager.ExitOutputHotkeyMode();

        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not found. Configure database from File menu first.");
            return;
        }

        // Validate database matches simulator
        if (!ValidateDatabaseSimulatorMatch())
            return;

        var dialog = new DestinationRunwayForm(airportDataProvider, announcer);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.SelectedRunway != null && dialog.SelectedAirport != null)
            {
                simConnectManager.SetDestinationRunway(dialog.SelectedRunway, dialog.SelectedAirport);
                // Destination just set → force-fresh the online taxiway names + gate aliases NOW, so
                // they're ready well before the approach (instead of waiting until ILS guidance or the
                // landing-exit planner is opened).
                if (_augmentPrefetched.Add(dialog.SelectedAirport.ICAO))
                    _ = _augmentingProvider?.PrefetchAsync(dialog.SelectedAirport.ICAO, force: true);
                announcer.AnnounceImmediate($"Destination runway set: {dialog.SelectedAirport.ICAO} Runway {dialog.SelectedRunway.RunwayID}");
            }
        }
    }

    private void ShowMETARReportDialog()
    {
        // Ensure output hotkey mode is deactivated before showing window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new METARReportForm(announcer);
        dialog.ShowForm();
    }

    private void ShowColdTempCorrectionDialog()
    {
        // Ensure output hotkey mode is deactivated before showing the window
        hotkeyManager.ExitOutputHotkeyMode();

        var dialog = new ColdTemperatureCorrectionForm(announcer);
        dialog.ShowForm();
    }

    private void ShowChecklistDialog()
    {
        // Ensure output hotkey mode is deactivated before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // Shift+C opens the static text checklist (same for every aircraft, including
        // the A380). The A380's LIVE Electronic Checklist is on its own key,
        // Ctrl+Shift+C (ShowChecklistECLDialog).
        if (checklistForm == null || checklistForm.IsDisposed)
        {
            checklistForm = new ChecklistForm(announcer, currentAircraft.AircraftCode);
        }

        // Show the form (reuses same instance to preserve checkbox states)
        checklistForm.ShowForm();
    }

    // Ctrl+Shift+C on the A380: the LIVE Electronic Checklist (ECL) read from the
    // E/WD — the real normal checklists + active ECAM procedures, with sensed
    // auto-completion. A380-only; other aircraft have no ECL to drive.
    private void ShowChecklistECLDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();

        if (currentAircraft?.AircraftCode != "FBW_A380")
        {
            announcer.AnnounceImmediate("The live Electronic Checklist is only on the A380. Use Shift+C for the text checklist.");
            return;
        }
        // The live ECL reads through the SHARED A380X_EWD monitor connection (only
        // one Coherent inspector socket per page is allowed). Ensure it's running.
        if (coherentEWDClient == null) StartA380EWDMonitor();
        if (fbwA380ChecklistForm == null || fbwA380ChecklistForm.IsDisposed)
            fbwA380ChecklistForm = new Forms.FBWA380.FBWA380ChecklistForm(announcer, simConnectManager, coherentEWDClient);
        fbwA380ChecklistForm.Show();
        fbwA380ChecklistForm.BringToFront();
        fbwA380ChecklistForm.Activate();
    }

    public void ShowFenixMonitorManagerDialog()
    {
        // Deactivate output hotkey mode before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // Create form if it doesn't exist or has been disposed
        if (fenixMonitorManagerForm == null || fenixMonitorManagerForm.IsDisposed)
        {
            fenixMonitorManagerForm = new FenixMonitorManagerForm(currentAircraft.GetVariables());
        }

        // Show the form (reuses same instance to preserve state)
        fenixMonitorManagerForm.ShowForm();
    }

    public void ShowA380MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwA380MonitorManagerForm == null || fbwA380MonitorManagerForm.IsDisposed)
        {
            fbwA380MonitorManagerForm = new Forms.FBWA380.FBWA380MonitorManagerForm(
                announcer, currentAircraft.GetVariables());
        }
        fbwA380MonitorManagerForm.ShowForm();
    }

    public void ShowA320MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwA320MonitorManagerForm == null || fbwA320MonitorManagerForm.IsDisposed)
        {
            fbwA320MonitorManagerForm = new Forms.FlyByWireA320.FlyByWireA320MonitorManagerForm(
                announcer, currentAircraft.GetVariables());
        }
        fbwA320MonitorManagerForm.ShowForm();
    }

    public void ShowHS787MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (hs787MonitorManagerForm == null || hs787MonitorManagerForm.IsDisposed)
        {
            hs787MonitorManagerForm = new Forms.HS787.HS787MonitorManagerForm(currentAircraft.GetVariables());
        }
        hs787MonitorManagerForm.ShowForm();
    }

    public void ShowPMDGAnnouncementMonitorDialog()
    {
        // Deactivate output hotkey mode before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // The form snapshots the variables dictionary at construction time,
        // so we recreate it whenever the loaded aircraft might have changed.
        // CleanupAircraftSpecificForms() disposes this on aircraft swap, so
        // a stale instance from the previous aircraft never lingers.
        if (pmdgAnnouncementMonitorForm == null || pmdgAnnouncementMonitorForm.IsDisposed)
        {
            pmdgAnnouncementMonitorForm = new PMDGAnnouncementMonitorForm(announcer, currentAircraft.GetVariables());
        }

        pmdgAnnouncementMonitorForm.ShowForm();
    }

    private void ShowFenixMCDUDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        // Create service if it doesn't exist
        if (fenixMCDUService == null)
        {
            fenixMCDUService = new FenixMCDUService();
            fenixMCDUService.Connect();
        }

        // Create form if it doesn't exist or has been disposed
        if (fenixMCDUForm == null || fenixMCDUForm.IsDisposed)
        {
            fenixMCDUForm = new FenixMCDUForm(fenixMCDUService, announcer);
        }

        // Show the form (reuses same instance to preserve state)
        fenixMCDUForm.ShowForm();
    }

    private void ShowFenixEFBDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        // Reuse a single instance; recreate after it has been disposed (close / swap).
        if (fenixEFBForm == null || fenixEFBForm.IsDisposed)
        {
            fenixEFBForm = new Forms.Fenix.FenixEFBForm(announcer);
        }

        fenixEFBForm.ShowForm();
    }

    private void ShowFlyByWireMCDUDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (flyByWireMCDUService == null)
        {
            flyByWireMCDUService = new MSFSBlindAssist.Services.FlyByWireMCDUService();
            flyByWireMCDUService.Connect();
        }

        if (flyByWireMCDUForm == null || flyByWireMCDUForm.IsDisposed)
        {
            flyByWireMCDUForm = new MSFSBlindAssist.Forms.FlyByWireA320.FlyByWireMCDUForm(flyByWireMCDUService, announcer);
        }

        flyByWireMCDUForm.ShowForm();
    }

    private void ShowPMDGCDUDialog()
    {
        // Deactivate input hotkey mode before showing dialog
        hotkeyManager.ExitInputHotkeyMode();

        if (simConnectManager?.PMDGDataManager == null) return;

        // Create form if it doesn't exist or has been disposed.
        // Dispatch by aircraft code: the 777 form takes a concrete
        // PMDG777DataManager (cast through the abstraction); the 737
        // form accepts IPMDGDataManager directly.
        if (pmdgCDUForm == null || pmdgCDUForm.IsDisposed)
        {
            if (currentAircraft?.AircraftCode == "PMDG_737")
            {
                pmdgCDUForm = new PMDG737CDUForm(simConnectManager.PMDGDataManager, announcer);
            }
            else
            {
                pmdgCDUForm = new PMDG777CDUForm((PMDG777DataManager)simConnectManager.PMDGDataManager, announcer);
            }
        }

        // Show the form (reuses same instance to preserve state)
        switch (pmdgCDUForm)
        {
            case PMDG737CDUForm f737: f737.ShowForm(); break;
            case PMDG777CDUForm f777: f777.ShowForm(); break;
        }
    }

    private void ShowFBWA380MCDUDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        if (coherentClient == null) { coherentClient = new CoherentDebuggerClient(); coherentClient.Start(); }
        IMcduBridge bridge = coherentClient;

        if (fbwA380MCDUForm == null || fbwA380MCDUForm.IsDisposed)
        {
            fbwA380MCDUForm = new Forms.FBWA380.FBWA380MCDUForm(
                bridge, announcer,
                currentAircraft as Aircraft.FlyByWireA380Definition);
            // Idle-gate the 350 ms MFD scrape to the window's visibility. The form hides
            // (not closes) on user-close, so VisibleChanged fires on every open/close; the
            // form and client are both disposed on aircraft swap, so the closure can't
            // dangle. The connection itself stays warm for D / Shift+D flight info.
            var form = fbwA380MCDUForm;
            form.VisibleChanged += (_, _) => coherentClient?.SetActive(!form.IsDisposed && form.Visible);
        }
        coherentClient.SetActive(true);   // covers the already-visible re-Show path (no VisibleChanged)
        fbwA380MCDUForm.ShowForm();
    }

    private void ShowFbwEfbDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        if (coherentEFBClient == null) { coherentEFBClient = new CoherentEFBClient(); coherentEFBClient.Start(); }
        IMcduBridge bridge = coherentEFBClient;

        if (fbwEfbForm == null || fbwEfbForm.IsDisposed)
        {
            // One generic flyPad form serves both FBW aircraft; only the window
            // title differs. The form is disposed on aircraft swap (see the swap
            // handler), so it is always recreated with the correct title.
            string title = currentAircraft?.AircraftCode == "A320"
                ? "A320 flyPad EFB" : "A380X flyPad EFB";
            fbwEfbForm = new Forms.FBWA380.FbwEfbForm(bridge, announcer, title, "flyPad");
            // Idle-gate the 600 ms flyPad scrape to the window's visibility (same pattern
            // as the MCDU window above); the connection + powerOn handshake stay warm.
            var form = fbwEfbForm;
            form.VisibleChanged += (_, _) => coherentEFBClient?.SetActive(!form.IsDisposed && form.Visible);
        }
        coherentEFBClient.SetActive(true);   // covers the already-visible re-Show path (no VisibleChanged)
        fbwEfbForm.ShowForm();
    }

    // PMDG 737/777 EFB tablet over the Coherent debugger — one client + window per
    // crew side, reusing the generic FbwEfbForm. Mirrors ShowFbwEfbDialog's lazy
    // client/form creation + non-modal ShowForm pattern.
    private void ShowPmdgCoherentEfbDialog(bool firstOfficer)
    {
        hotkeyManager.ExitInputHotkeyMode();
        string side = firstOfficer ? "FO" : "CA";
        string title = (currentAircraft?.AircraftCode == "PMDG_737" ? "PMDG 737 EFB" : "PMDG 777 EFB") + (firstOfficer ? " (First Officer)" : "");

        if (firstOfficer)
        {
            if (coherentPmdgEfbFirstOfficer == null) { coherentPmdgEfbFirstOfficer = new CoherentPmdgEfbClient(side); coherentPmdgEfbFirstOfficer.Start(); }
            if (pmdgCoherentEfbFirstOfficerForm == null || pmdgCoherentEfbFirstOfficerForm.IsDisposed)
            {
                pmdgCoherentEfbFirstOfficerForm = new Forms.FBWA380.FbwEfbForm(coherentPmdgEfbFirstOfficer, announcer, title, "EFB", "Universal Flight Tablet");
                // Idle-gate the 600 ms tablet scrape to the window's visibility (same pattern as
                // the flyPad form above); the inspector socket + installed agent stay warm. Without
                // this the scrape runs forever after the first open until aircraft swap.
                var foForm = pmdgCoherentEfbFirstOfficerForm;
                foForm.VisibleChanged += (_, _) => coherentPmdgEfbFirstOfficer?.SetActive(!foForm.IsDisposed && foForm.Visible);
            }
            coherentPmdgEfbFirstOfficer.SetActive(true);   // covers the already-visible re-Show path (no VisibleChanged)
            pmdgCoherentEfbFirstOfficerForm.ShowForm();
        }
        else
        {
            if (coherentPmdgEfbCaptain == null) { coherentPmdgEfbCaptain = new CoherentPmdgEfbClient(side); coherentPmdgEfbCaptain.Start(); }
            if (pmdgCoherentEfbCaptainForm == null || pmdgCoherentEfbCaptainForm.IsDisposed)
            {
                pmdgCoherentEfbCaptainForm = new Forms.FBWA380.FbwEfbForm(coherentPmdgEfbCaptain, announcer, title, "EFB", "Universal Flight Tablet");
                var caForm = pmdgCoherentEfbCaptainForm;
                caForm.VisibleChanged += (_, _) => coherentPmdgEfbCaptain?.SetActive(!caForm.IsDisposed && caForm.Visible);
            }
            coherentPmdgEfbCaptain.SetActive(true);
            pmdgCoherentEfbCaptainForm.ShowForm();
        }
    }

    // A380 ND OANS / BTV control panel — reuses the WebView2 EFB form, but driven
    // by the ND Coherent view through CoherentNDClient. Used for BTV (Brake-To-
    // Vacate) exit selection and airport/runway/exit search.
    // Open the accessible A380 RMP window (Ctrl+Shift+R in input mode) — replaces the old
    // per-key RMP button panel. Scrapes A380X_RMP_1/2 live; one window, Captain ↔ FO combo.
    private void ShowA32NXDcduDialog()
    {
        // Opened by an INPUT-mode hotkey — release the mode hotkeys so the
        // form's Ctrl+1/2 / Alt+1/2 soft keys and PageUp/Down navigation reach
        // the window instead of the global registrations (RMP/OANS precedent).
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwDcduForm == null || fbwDcduForm.IsDisposed)
        {
            fbwDcduForm = new Forms.FlyByWireA320.FlyByWireDcduForm(announcer, simConnectManager);
        }
        fbwDcduForm.Show();
        fbwDcduForm.BringToFront();
        fbwDcduForm.Activate();
    }

    private void ShowFBWA380RmpDialog()
    {
        if (currentAircraft is not FlyByWireA380Definition a380rmp) return;
        // CRITICAL: release the mode hotkeys before showing. The RMP window is opened by an
        // INPUT-mode hotkey (Ctrl+Shift+R), so input mode is still active and its global
        // RegisterHotKey shortcuts (Ctrl+1/2/3 = FCU pulls, Alt+n, digits via Track Slots,
        // etc.) would be consumed system-wide and NEVER reach the RMP window — making the
        // RMP soft keys, page switching and digit entry all appear dead. Exiting both modes
        // unregisters those, so every keystroke flows to the form. (Mirrors the OANS dialog.)
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();
        if (fbwA380RmpForm == null || fbwA380RmpForm.IsDisposed)
        {
            fbwA380RmpForm = new Forms.FBWA380.FBWA380RmpForm(announcer, a380rmp, simConnectManager);
        }
        fbwA380RmpForm.Show();
        fbwA380RmpForm.BringToFront();
        fbwA380RmpForm.Activate();
    }

    private void ShowFBWA380OansDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();

        if (coherentNDClient == null) { coherentNDClient = new CoherentNDClient(); coherentNDClient.Start(); }

        if (fbwA380OansForm == null || fbwA380OansForm.IsDisposed)
        {
            fbwA380OansForm = new Forms.FBWA380.FBWA380OansForm(coherentNDClient, announcer);
        }
        fbwA380OansForm.ShowForm();
    }

    private void ShowHS787FMCDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        // The CDU now reads + drives over the Coherent debugger (HSB789_MFD_3) — no HTTP
        // bridge server, no injected JS, no mod-package HTML patching required.
        if (hs787FMCForm == null || hs787FMCForm.IsDisposed)
        {
            hs787FMCForm = new HS787FMCForm(simConnectManager, announcer);
        }

        hs787FMCForm.ShowForm();
    }

    private void ShowElectronicFlightBagDialog()
    {
        // Ensure output hotkey mode is deactivated before showing dialog
        hotkeyManager.ExitOutputHotkeyMode();

        // Create form if it doesn't exist or has been disposed
        if (electronicFlightBagForm == null || electronicFlightBagForm.IsDisposed)
        {
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            electronicFlightBagForm = new ElectronicFlightBagForm(flightPlanManager, simConnectManager, announcer, settings.SimbriefUsername ?? "");
            // "Track Slot N" on a route waypoint opens the Track Fix dialog pre-populated (so the
            // mapped altitude/constraint/course is visible + editable) instead of tracking silently.
            electronicFlightBagForm.TrackToSlotRequested += OnEfbTrackToSlotRequested;
        }

        // Show the form (reuses same instance to preserve flight plan data)
        electronicFlightBagForm.ShowForm();
    }

    private void ShowTaxiAssistForm()
    {
        if (airportDataProvider == null)
        {
            announcer.AnnounceImmediate("Airport database not available. Configure database in settings.");
            return;
        }

        // Ensure input and output hotkey modes are deactivated before showing dialog
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        // Get current aircraft position
        simConnectManager.RequestAircraftPositionAsync(position =>
        {
            if (this.InvokeRequired)
            {
                this.Invoke(() => OpenTaxiForm(position));
            }
            else
            {
                OpenTaxiForm(position);
            }
        });
    }

    private Services.GateDataSource? BuildGateDataSource()
    {
        if (airportDataProvider == null) return null;
        // GSX gates only when GSX is running this session (Couatl started) AND a profile matches.
        return new Services.GateDataSource(
            airportDataProvider,
            () => _gsxService != null && _gsxService.CouatlStarted);
    }

    /// <summary>
    /// Constructs a <see cref="Services.Gsx.GsxGateSelector"/> when GSX is
    /// available in this session. Returns <c>null</c> when there is no GSX
    /// service (GSX not installed / not yet started), so callers can simply
    /// null-check before using it.
    /// </summary>
    private Services.Gsx.GsxGateSelector? BuildGsxGateSelector()
    {
        if (_gsxService == null) return null;
        return new Services.Gsx.GsxGateSelector(
            _gsxService,
            new Services.Gsx.GsxMenuAutomation(_gsxService),
            announcer);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the GSX installation folder
    /// (%APPDATA%\Virtuali) exists, indicating GSX is likely installed.
    /// Used to gate the per-ICAO profile rescan: the Refresh() call is only
    /// useful to pick up a gsx.cfg written by GSX to %APPDATA%\Virtuali\Airplanes,
    /// which never happens on a machine without GSX.
    /// </summary>
    private static bool GsxLikelyInstalled()
    {
        try { return System.IO.Directory.Exists(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtuali")); }
        catch { return false; }
    }

    private void OpenTaxiForm(SimConnectManager.AircraftPosition position)
    {
        if (taxiAssistForm == null || taxiAssistForm.IsDisposed)
        {
            taxiAssistForm = new TaxiAssistForm(
                airportDataProvider!, announcer, taxiGuidanceManager, simConnectManager, tcasService,
                simConnectManager.AircraftWingSpan, BuildGateDataSource(), BuildGsxGateSelector(), dockingGuidanceManager);
        }

        // Find nearest airport. Filter to 4-char canonical ICAO at the call site —
        // GetNearbyAirportICAOs may return 3-char idents (used by GateResolver's
        // TCAS lookup). The taxi-graph builder needs canonical ICAOs.
        string nearestIcao = "";
        var nearbyAirports = airportDataProvider!.GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
            .Where(c => c != null && c.Length == 4)
            .ToList();
        if (nearbyAirports.Count > 0)
            nearestIcao = nearbyAirports[0];

        // Task 2 — Departure prefetch: when on the ground and we've resolved a nearest
        // airport, prefetch once per session so taxiway names are cached before taxi starts.
        // SILENT (fire-and-forget, debounced via _augmentPrefetched).
        if (_lastOnGround && !string.IsNullOrEmpty(nearestIcao) && _augmentPrefetched.Add(nearestIcao))
            _ = _augmentingProvider?.PrefetchAsync(nearestIcao, force: true);

        taxiAssistForm.SetAircraftPosition(position.Latitude, position.Longitude, position.HeadingMagnetic, nearestIcao);

        // (StateChanged is subscribed once in InitializeManagers. We deliberately do NOT
        // re-subscribe here — re-subscribing on every form open would either double-fire
        // the handler or, with the -=/+= pattern previously used here, hide the fact
        // that other entry points like the Landing Exit Planner were never wired up.)

        taxiAssistForm.Show();
        taxiAssistForm.BringToFront();
    }

    /// <summary>
    /// Opens the Landing Exit Planner form. Pre-fills the airport + runway from the
    /// pilot's existing ILS destination selection (SimConnectManager.GetDestinationRunway)
    /// so there's no duplicate UI for picking the destination — the pilot only picks the
    /// exit taxiway here.
    /// </summary>
    private void ShowLandingExitForm()
    {
        if (airportDataProvider == null)
        {
            announcer.AnnounceImmediate("Airport database not available. Configure database in settings.");
            return;
        }

        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        // Reuse the existing ILS destination selection (already settable via the
        // "select runway as destination" hotkey). If nothing is set, the form still
        // opens empty so the pilot can type an ICAO + pick a runway manually.
        string? presetIcao = null;
        Database.Models.Runway? presetRunway = null;
        if (simConnectManager.HasDestinationRunway())
        {
            presetRunway = simConnectManager.GetDestinationRunway();
            var destAp = simConnectManager.GetDestinationAirport();
            presetIcao = destAp?.ICAO;
            // Task 1 — Destination prefetch (silent, fire-and-forget)
            if (!string.IsNullOrEmpty(presetIcao) && _augmentPrefetched.Add(presetIcao))
                _ = _augmentingProvider?.PrefetchAsync(presetIcao, force: true);
        }

        // Always rebuild the form so the preset (ICAO + runway from the current
        // ILS destination selection) is fresh. The preset is only consumed by
        // the constructor/Load handler; reusing a prior instance would show
        // stale values if the user changed ILS destination between opens.
        if (landingExitForm != null && !landingExitForm.IsDisposed)
        {
            landingExitForm.Close();
            landingExitForm.Dispose();
        }

        landingExitForm = new LandingExitForm(
            airportDataProvider, announcer, landingExitPlanner, presetIcao, presetRunway,
            simConnectManager);

        landingExitForm.Show();
        landingExitForm.BringToFront();
        landingExitForm.Activate();
    }

    private void ShowTrackFixDialog()
    {
        // Ensure input and output hotkey modes are deactivated before showing dialog
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        EnsureTrackFixForm();
        trackFixForm!.ShowForm();
    }

    /// <summary>Lazily creates the Track Fix dialog (shared by the Shift+F path and the EFB
    /// "Track Slot N" pre-fill path).</summary>
    private void EnsureTrackFixForm()
    {
        if (trackFixForm == null || trackFixForm.IsDisposed)
        {
            var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
            string navigationDatabasePath = NavdataReaderBuilder.GetDefaultDatabasePath(settings.SimulatorVersion ?? "FS2020");
            trackFixForm = new TrackFixForm(waypointTracker, simConnectManager, announcer, navigationDatabasePath);
        }
    }

    /// <summary>EFB "Track Slot N" → open the Track Fix dialog pre-populated with the fix, slot, and its
    /// mapped altitude constraint + course, so the pilot reviews/edits before committing (the constraint
    /// is then visible and editable, unlike a silent direct-track).</summary>
    private void OnEfbTrackToSlotRequested(Database.Models.WaypointFix fix, int slotNumber)
    {
        hotkeyManager.ExitInputHotkeyMode();
        hotkeyManager.ExitOutputHotkeyMode();

        EnsureTrackFixForm();
        trackFixForm!.ShowFormPrefilled(fix, slotNumber);
    }
}
