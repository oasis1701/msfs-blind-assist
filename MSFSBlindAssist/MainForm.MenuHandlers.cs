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
    private void DatabaseSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new DatabaseSettingsForm(announcer, this))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                // Reload database provider with new settings
                RefreshDatabaseProvider();

                // Announce the change
                var status = DatabaseSelector.GetDatabaseStatus();
                if (status.hasDatabase)
                {
                    announcer.AnnounceImmediate($"Database settings saved. Using {status.message}");
                }
                else
                {
                    announcer.AnnounceImmediate($"Database settings saved. {status.message}");
                }
            }
        }
    }

    private void SettingsMenuItem_Click(object? sender, EventArgs e)
    {
        // Same inline refresh-taxiway-names callback TaxiGuidanceOptionsMenuItem_Click builds
        // today; only wired when the augmenting provider is available (Task 6 moves this into
        // the TaxiGuidancePanel wiring).
        Func<Task>? refreshCallback = null;
        if (_augmentingProvider != null && airportDataProvider != null)
        {
            var provider = _augmentingProvider;
            var dataProvider = airportDataProvider;
            refreshCallback = async () =>
            {
                var pos = simConnectManager.LastKnownPosition;
                if (pos == null) return;

                string? icao = await Task.Run(() =>
                    dataProvider.GetNearbyAirportICAOs(pos.Value.Latitude, pos.Value.Longitude, 50.0)
                        .Where(c => c != null && c.Length == 4)
                        .FirstOrDefault());

                if (icao == null) return;

                await provider.PrefetchAsync(icao, force: true);

                var cov = provider.GetLastCoverage(icao);
                int added = cov == null ? 0
                    : cov.NamesAdoptedFromOsm + cov.NamesAdoptedFromAptDat + cov.AliasesAdded;
                string msg = added > 0
                    ? $"Taxiway names refreshed for {icao}: {added} added."
                    : $"Taxiway names refreshed for {icao}. No new names found.";
                // No marshal needed: this callback is invoked from
                // TaxiGuidancePanel's Button.Click handler (UI thread), and neither await
                // above uses ConfigureAwait(false), so we're still on the UI thread here.
                if (IsHandleCreated && !IsDisposed)
                    announcer.AnnounceImmediate(msg);
            };
        }

        using var dlg = new Forms.Settings.SettingsForm(
            refreshTaxiwayNames: refreshCallback,
            vatsimStatus: () => vatsimService?.GetStatus());
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            ApplyRuntimeSettings();
            statusLabel.Text = "Settings saved";
            announcer.Announce("Settings saved");
        }
    }

    /// <summary>Re-applies saved UserSettings to the live runtime managers after the Settings
    /// dialog is accepted, so changes take effect without restarting. Each settings section that
    /// has a live effect adds its re-apply here (populated as panels are migrated).</summary>
    private void ApplyRuntimeSettings()
    {
        // (SimBrief and Gemini have no live effect.)

        // Announcements: mode, nearest-city timer, weather monitor interval, GSX background toggle.
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        var mode = Enum.TryParse(settings.AnnouncementMode, out AnnouncementMode parsedMode)
            ? parsedMode
            : AnnouncementMode.ScreenReader;
        announcer.SetAnnouncementMode(mode);
        RestartNearestCityAnnouncementTimer();

        if (activeSkyWeatherMonitor != null)
        {
            activeSkyWeatherMonitor.IntervalMinutes = settings.WeatherAutoAnnounceIntervalMinutes;
            // Both Weather-tab switches take effect without restart. The Enabled setter
            // maps to Start()/Stop(), and System.Windows.Forms.Timer.Start/Stop are
            // idempotent, so an unchanged setting is a no-op.
            activeSkyWeatherMonitor.Enabled =
                MSFSBlindAssist.Services.ActiveSkyWeatherMonitor.ShouldRun(settings);
        }

        // GSX background-monitoring toggle. Push the new value into the live
        // service. The form's VisibleChanged handler will overwrite this
        // when the form is open/hidden — that's intentional (form open =
        // form drives speech). When the form is hidden the saved setting wins.
        if (_gsxService != null && (_accessGsxForm == null || !_accessGsxForm.Visible))
            _gsxService.AnnounceWhenFormHidden = settings.GsxBackgroundMonitoring;

        // Hand Fly / Visual Guidance / Takeoff Assist — moved verbatim from the retired
        // HandFlyOptionsMenuItem_Click. Recreate TakeoffAssistManager to pick up new
        // settings (steer-toward tone, legacy mode, tone, volume); its mode is set at
        // construction time so there is no in-place setter.
        if (takeoffAssistManager != null)
        {
            // Preserve a teleport/taxi-lineup runway reference across the
            // recreate — Reset() clears it, and losing it here silently
            // downgraded the next Ctrl+T to "no runway selected". Restore
            // is silent (SetRunwayReference only Debug-logs).
            bool hadRunwayRef = takeoffAssistManager.TryGetRunwayReference(
                out double refLat, out double refLon, out double refHdgTrue,
                out double refHdgMag, out string refRunwayId, out string refIcao);

            takeoffAssistManager.Reset();
            takeoffAssistManager.Dispose();
            takeoffAssistManager = new TakeoffAssistManager(announcer,
                settings.TakeoffAssistToneWaveform, settings.TakeoffAssistToneVolume,
                settings.TakeoffAssistMuteCenterlineAnnouncements,
                settings.TakeoffAssistSteerTowardTone,
                settings.TakeoffAssistHeadingToneThreshold, settings.TakeoffAssistLegacyMode,
                settings.TakeoffAssistEnableCallouts);
            takeoffAssistManager.TakeoffAssistActiveChanged += OnTakeoffAssistActiveChanged;

            if (hadRunwayRef)
            {
                takeoffAssistManager.SetRunwayReference(refLat, refLon,
                    refHdgTrue, refHdgMag, refRunwayId, refIcao);
            }
        }

        // Capture whether the heading/VS monitoring choices actually changed
        // BEFORE UpdateSettings overwrites the manager's flags — the restart
        // below is gated on a real change so a no-op Settings-OK doesn't cancel
        // and re-register live SIM_FRAME streams for nothing.
        bool handFlyMonitorFlagsChanged = handFlyManager != null &&
            (handFlyManager.MonitorHeading != settings.HandFlyMonitorHeading ||
             handFlyManager.MonitorVerticalSpeed != settings.HandFlyMonitorVerticalSpeed);

        // Update HandFlyManager if it's active
        handFlyManager?.UpdateSettings(
            settings.HandFlyFeedbackMode,
            settings.HandFlyWaveType,
            settings.HandFlyToneVolume,
            settings.HandFlyMonitorHeading,
            settings.HandFlyMonitorVerticalSpeed);

        // Re-register the SimConnect streams to match the new heading/VS choices.
        // StartHandFlyMonitoring only registers the heading/VS requests whose flag
        // is on AT CALL TIME, so enabling one while Hand Fly is active was a
        // silent no-op (the manager-side gate passed but no data ever flowed)
        // until Hand Fly was toggled off and on again. Matters out of the box now
        // that HandFlyMonitorVerticalSpeed defaults to off. Both calls no-op
        // safely when disconnected.
        if (handFlyMonitorFlagsChanged && handFlyManager!.IsActive)
        {
            simConnectManager.StopHandFlyMonitoring();
            // drainCancelledStreams: the streams cancelled a line above may
            // still have data in flight; the drain (cancel + message pump)
            // guards the ClearDataDefinition-while-request-active crash. Safe
            // here — this runs from the settings OK handler, NOT inside
            // SimConnect dispatch, so the pump can actually process WM_USER.
            simConnectManager.StartHandFlyMonitoring(
                settings.HandFlyMonitorHeading, settings.HandFlyMonitorVerticalSpeed,
                drainCancelledStreams: true);
        }

        // Taxi Guidance / Docking — moved verbatim from the retired TaxiGuidanceOptionsMenuItem_Click.
        // Every other taxi/docking setting (tone type/volume, invert/hard-pan, announce-crossings,
        // ground-speed intervals, distance units, GSX auto-select, docking enabled/beep) is read
        // live from SettingsManager.Current at point of use (TaxiGuidanceManager, DockingGuidanceManager,
        // GroundSpeedAnnouncer, GroundTrafficMonitor, TaxiAssistForm) — no push needed for those.
        // The online taxiway/gate-name augmentation toggle is the one setting with a live service
        // to push into; apply it here so it takes effect immediately (next route build).
        if (_augmentingProvider != null)
            _augmentingProvider.Enabled = settings.TaxiAugmentEnabled;

        // VATSIM: install or refresh the vPilot plugin and start/stop the pipe server.
        var vatsimInstall = vatsimService?.ApplySettings(settings);
        if (vatsimInstall != null)
            AnnounceVatsimInstallOutcome(vatsimInstall, atStartup: false);
    }

    /// <summary>
    /// Speaks what an install attempt actually did. The outcome is spoken because the
    /// pilot cannot see whether a file landed in another application's folder — and
    /// because "Installed" always means "now restart vPilot", which nothing else would
    /// tell them.
    ///
    /// <paramref name="atStartup"/> narrows what gets spoken, but NOT down to "Locked and
    /// Failed only" — that would silently mute the worse of the two silent-VATSIM
    /// outcomes. The same install check runs on every launch, so an ordinary
    /// update-refresh (an already-installed plugin gets overwritten with the same or a
    /// newer copy while vPilot was not holding it) stays quiet there: it needed nothing
    /// from the pilot and vPilot will load the new copy the next time it starts on its
    /// own. A FIRST install (<see cref="MSFSBlindAssist.Services.VPilot.VPilotInstallResult.PluginWasAbsent"/>)
    /// is a different case entirely and must speak even at startup: nothing was there
    /// before this write, so vPilot is GUARANTEED not to have the plugin loaded right
    /// now, and — because vPilot only loads plugins at its OWN startup, never on a
    /// timer — nothing will load it this session unless vPilot itself is restarted.
    /// Staying silent there is exactly what used to leave a pilot flying an entire leg in
    /// total, unexplained VATSIM silence. Locked and Failed speak unconditionally for the
    /// same underlying reason: the plugin the pilot is about to rely on is NOT the one
    /// that is actually there, and nothing else on screen or in speech would say so.
    /// LegacyRemoved (below) is unconditional too, but for an unrelated reason — see its
    /// own comment.
    ///
    /// Startup uses the QUEUED announcer, not the immediate one: its System.Windows.Forms
    /// timer cannot tick until the message pump is running, which naturally defers the
    /// speech until the form is up instead of firing it mid-construction.
    /// </summary>
    private void AnnounceVatsimInstallOutcome(
        MSFSBlindAssist.Services.VPilot.VPilotInstallResult result, bool atStartup)
    {
        // Inherently one-shot — once the old DLL is gone it can never be removed again —
        // so it is worth hearing whenever it happens, startup included. It is the only
        // thing that explains why the duplicate announcements stopped.
        if (result.LegacyRemoved)
        {
            Announce("Removed the old vPilot to TTS plugin. MSFS Blind Assist now handles VATSIM announcements.");
        }

        switch (result.Status)
        {
            case MSFSBlindAssist.Services.VPilot.VPilotInstallStatus.Installed:
                // A first install speaks even at startup — see PluginWasAbsent's doc
                // comment. An update-refresh Installed stays quiet there; it is the
                // ordinary outcome of every app update and asks nothing of the pilot.
                if (!atStartup || result.PluginWasAbsent)
                    Announce("vPilot plugin installed. Restart vPilot to load it.");
                break;
            case MSFSBlindAssist.Services.VPilot.VPilotInstallStatus.Locked:
                // Startup gets its own wording. The Settings-path phrasing tells the
                // pilot to re-open a dialog they just used; at startup they have not
                // opened Settings this session, and the check that produced this result
                // ran on its own — so the accurate instruction is to relaunch the app
                // itself, which re-runs the very check that is speaking right now.
                Announce(atStartup
                    ? "vPilot is running with an older plugin. Close vPilot and restart MSFS Blind Assist to update it."
                    : "vPilot is running with an older plugin. Close vPilot and re-open Settings to update it.");
                break;
            case MSFSBlindAssist.Services.VPilot.VPilotInstallStatus.VPilotNotFound:
                if (!atStartup)
                    Announce("vPilot was not found. Install vPilot, then re-open Settings.");
                break;
            case MSFSBlindAssist.Services.VPilot.VPilotInstallStatus.Failed:
                Announce("The vPilot plugin could not be installed. See the log for details.");
                break;
            // AlreadyCurrent, and an update-refresh Installed, say nothing further —
            // "Settings saved" already covers the Settings path, and at startup both are
            // the normal outcome on every launch.
        }

        void Announce(string text)
        {
            if (atStartup)
                announcer.AnnounceWithQueue(text);
            else
                announcer.Announce(text);
        }
    }

    private void HotkeyListMenuItem_Click(object? sender, EventArgs e)
    {
        using (var hotkeyListForm = new HotkeyListForm(currentAircraft.AircraftCode))
        {
            hotkeyListForm.ShowDialog(this);
        }
    }

    private void FMCSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        var s = SettingsManager.Current;
        using (var settingsForm = new Forms.FMCSettingsForm(
            s.MCDUUseAlternateLSKKeys,
            s.PMDGEnhancedDistanceMode))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                s.MCDUUseAlternateLSKKeys = settingsForm.UseAlternateLSKKeys;
                s.PMDGEnhancedDistanceMode = settingsForm.EnhancedDistanceMode;
                SettingsManager.Save();

                // Toggle the PROG-page monitor in/out of running state to
                // match the new Enhanced-distance setting. Effect is
                // immediate — no app restart needed.
                EnsurePMDGProgPageMonitor();

                statusLabel.Text = "FMC settings saved";
                announcer.Announce("FMC settings saved");
            }
        }
    }

    private void SuspendHotkeysMenuItem_Click(object? sender, EventArgs e)
    {
        if (suspendHotkeysMenuItem.Checked)
        {
            hotkeyManager.Suspend();
            announcer.AnnounceImmediate("Hotkeys suspended");
        }
        else
        {
            if (hotkeyManager.Resume())
            {
                announcer.AnnounceImmediate("Hotkeys resumed");
            }
            else
            {
                announcer.AnnounceImmediate("Warning: failed to re-register hotkeys. Another application may be using the bracket keys.");
            }
        }
    }

    private void FlyByWireA320MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FlyByWireA320Definition());
    }

    private void FenixA320MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FenixA320Definition());
    }

    private void PMDG777MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new PMDG777Definition());
    }

    private void FlyByWireA380MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new FlyByWireA380Definition());
    }

    private void PMDG737MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new PMDG737Definition());
    }

    private void HorizonSim787MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new HorizonSim787Definition());
    }

    private void HeadwindA330MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new HeadwindA330Definition());
    }

    /// <summary>
    /// Guards against the startup check and the menu item running at once — either would
    /// otherwise open its own update dialog.
    /// </summary>
    private int _updateCheckInFlight;

    private async void UpdateApplicationMenuItem_Click(object? sender, EventArgs e)
    {
        await RunUpdateCheckAsync(userInitiated: true);
    }

    /// <summary>
    /// The one update-check path, shared by the Application menu item and the startup
    /// check. The difference between them is only how loud they are:
    ///
    ///   userInitiated true  — reports failures and reports being up to date.
    ///   userInitiated false — silent on both. A pilot who did not ask for a check must
    ///                         never be interrupted to be told nothing happened.
    ///
    /// An available update (or an offered downgrade) is announced and shown either way.
    /// </summary>
    private async Task RunUpdateCheckAsync(bool userInitiated)
    {
        if (Interlocked.CompareExchange(ref _updateCheckInFlight, 1, 0) != 0)
        {
            if (userInitiated) announcer.AnnounceImmediate("An update check is already running.");
            return;
        }

        try
        {
            if (userInitiated) announcer.AnnounceImmediate("Checking for updates...");

            var updateService = new UpdateService();
            var result = await updateService.CheckForUpdatesAsync(SettingsManager.Current.UpdateChannel);

            // The window can be gone by the time the HTTP call returns.
            if (IsDisposed || !IsHandleCreated) return;

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                if (!userInitiated) return;
                announcer.AnnounceImmediate($"Update check failed: {result.ErrorMessage}");
                MessageBox.Show(this, result.ErrorMessage, "Update Check Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                if (!userInitiated) return;

                // NoCandidate is not the same as UpToDate: claiming "you are on the latest
                // version" when the check found no releases at all would be a small lie.
                var message = result.Verdict == UpdateVerdict.NoCandidate
                    ? "No releases were found on GitHub."
                    : $"You are running the latest version ({result.CurrentVersion}).";

                announcer.AnnounceImmediate(message);
                MessageBox.Show(this, message, "No Updates Available",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var announcement = result.IsDowngrade
                ? $"Release build {result.LatestVersion} is available. It is older than the preview build you are running."
                : $"Update available: version {result.LatestVersion}";

            // The startup announcement is QUEUED so it cannot cut off other startup speech;
            // a check the pilot asked for speaks immediately.
            if (userInitiated) announcer.AnnounceImmediate(announcement);
            else announcer.AnnounceWithQueue(announcement);

            using var updateDialog = new UpdateAvailableForm(result, updateService);
            if (updateDialog.ShowDialog(this) == DialogResult.OK && updateDialog.ShouldUpdate)
            {
                try
                {
                    announcer.AnnounceImmediate("Launching updater. Application will close and restart.");
                    updateService.LaunchUpdater(updateDialog.DownloadedZipPath);
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to launch updater: {ex.Message}", "Update Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        catch (Exception ex)
        {
            if (!userInitiated) return;
            announcer.AnnounceImmediate($"Update failed: {ex.Message}");
            MessageBox.Show(this, $"An error occurred while checking for updates: {ex.Message}",
                "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckInFlight, 0);
        }
    }

    private void AboutMenuItem_Click(object? sender, EventArgs e)
    {
        using (var aboutForm = new AboutForm())
        {
            aboutForm.ShowDialog(this);
        }
    }
}
