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

    private void GeoNamesSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new Forms.GeoNamesApiKeyForm())
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                statusLabel.Text = "GeoNames settings saved successfully";
                announcer.Announce("GeoNames settings saved successfully");
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
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(() => announcer.AnnounceImmediate(msg));
            };
        }

        using var dlg = new Forms.Settings.SettingsForm(refreshTaxiwayNames: refreshCallback);
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
        // (SimBrief has no live effect. Later tasks add handfly/taxi re-apply here.)

        // Announcements: mode, nearest-city timer, weather monitor interval, GSX background toggle.
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        var mode = Enum.TryParse(settings.AnnouncementMode, out AnnouncementMode parsedMode)
            ? parsedMode
            : AnnouncementMode.ScreenReader;
        announcer.SetAnnouncementMode(mode);
        RestartNearestCityAnnouncementTimer();

        if (activeSkyWeatherMonitor != null)
            activeSkyWeatherMonitor.IntervalMinutes = settings.WeatherAutoAnnounceIntervalMinutes;

        // GSX background-monitoring toggle. Push the new value into the live
        // service. The form's VisibleChanged handler will overwrite this
        // when the form is open/hidden — that's intentional (form open =
        // form drives speech). When the form is hidden the saved setting wins.
        if (_gsxService != null && (_accessGsxForm == null || !_accessGsxForm.Visible))
            _gsxService.AnnounceWhenFormHidden = settings.GsxBackgroundMonitoring;
    }

    private void GeminiSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        using (var settingsForm = new Forms.GeminiSettingsForm())
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                statusLabel.Text = "Gemini settings saved successfully";
                announcer.Announce("Gemini settings saved successfully");
            }
        }
    }

    private void HandFlyOptionsMenuItem_Click(object? sender, EventArgs e)
    {
        var currentSettings = SettingsManager.Current;
        using (var settingsForm = new Forms.HandFlyOptionsForm(
            currentSettings.HandFlyFeedbackMode,
            currentSettings.HandFlyWaveType,
            currentSettings.HandFlyToneVolume,
            currentSettings.HandFlyMonitorHeading,
            currentSettings.HandFlyMonitorVerticalSpeed,
            currentSettings.VisualGuidanceToneWaveform,
            currentSettings.VisualGuidanceToneVolume,
            currentSettings.VisualGuidanceCurrentToneWaveform,
            currentSettings.VisualGuidanceCurrentToneVolume,
            currentSettings.VisualGuidanceHardPanTone,
            currentSettings.TakeoffAssistToneWaveform,
            currentSettings.TakeoffAssistToneVolume,
            currentSettings.TakeoffAssistMuteCenterlineAnnouncements,
            currentSettings.TakeoffAssistSteerTowardTone,
            currentSettings.TakeoffAssistHardPanTone,
            currentSettings.TakeoffAssistHeadingToneThreshold,
            currentSettings.TakeoffAssistLegacyMode,
            currentSettings.TakeoffAssistEnableCallouts,
            currentSettings.TakeoffAssistAutoActivateOnLineup))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                // Update settings
                currentSettings.HandFlyFeedbackMode = settingsForm.SelectedFeedbackMode;
                currentSettings.HandFlyWaveType = settingsForm.SelectedWaveType;
                currentSettings.HandFlyToneVolume = settingsForm.SelectedVolume;
                currentSettings.HandFlyMonitorHeading = settingsForm.MonitorHeading;
                currentSettings.HandFlyMonitorVerticalSpeed = settingsForm.MonitorVerticalSpeed;
                currentSettings.VisualGuidanceToneWaveform = settingsForm.GuidanceToneWaveform;
                currentSettings.VisualGuidanceToneVolume = settingsForm.SelectedGuidanceVolume;
                currentSettings.VisualGuidanceCurrentToneWaveform = settingsForm.VisualGuidanceCurrentToneWaveform;
                currentSettings.VisualGuidanceCurrentToneVolume = settingsForm.VisualGuidanceCurrentToneVolume;
                currentSettings.VisualGuidanceHardPanTone = settingsForm.VisualGuidanceHardPanTone;
                currentSettings.TakeoffAssistToneWaveform = settingsForm.TakeoffToneWaveform;
                currentSettings.TakeoffAssistToneVolume = settingsForm.TakeoffToneVolume;
                currentSettings.TakeoffAssistMuteCenterlineAnnouncements = settingsForm.TakeoffAssistMuteCenterlineAnnouncements;
                currentSettings.TakeoffAssistSteerTowardTone = settingsForm.TakeoffAssistSteerTowardTone;
                currentSettings.TakeoffAssistHardPanTone = settingsForm.TakeoffAssistHardPanTone;
                currentSettings.TakeoffAssistHeadingToneThreshold = settingsForm.TakeoffAssistHeadingToneThreshold;
                currentSettings.TakeoffAssistLegacyMode = settingsForm.TakeoffAssistLegacyMode;
                currentSettings.TakeoffAssistEnableCallouts = settingsForm.TakeoffAssistEnableCallouts;
                currentSettings.TakeoffAssistAutoActivateOnLineup = settingsForm.TakeoffAssistAutoActivateOnLineup;
                SettingsManager.Save();

                // Recreate TakeoffAssistManager to pick up new settings (steer-toward tone, legacy mode, tone, volume)
                // The manager's mode is set at construction time
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
                        currentSettings.TakeoffAssistToneWaveform, currentSettings.TakeoffAssistToneVolume,
                        currentSettings.TakeoffAssistMuteCenterlineAnnouncements,
                        currentSettings.TakeoffAssistSteerTowardTone,
                        currentSettings.TakeoffAssistHeadingToneThreshold, currentSettings.TakeoffAssistLegacyMode,
                        currentSettings.TakeoffAssistEnableCallouts);
                    takeoffAssistManager.TakeoffAssistActiveChanged += OnTakeoffAssistActiveChanged;

                    if (hadRunwayRef)
                    {
                        takeoffAssistManager.SetRunwayReference(refLat, refLon,
                            refHdgTrue, refHdgMag, refRunwayId, refIcao);
                    }
                }

                // Update HandFlyManager if it's active
                handFlyManager?.UpdateSettings(
                    settingsForm.SelectedFeedbackMode,
                    settingsForm.SelectedWaveType,
                    settingsForm.SelectedVolume,
                    settingsForm.MonitorHeading,
                    settingsForm.MonitorVerticalSpeed);

                statusLabel.Text = "Hand fly options saved successfully";
                announcer.Announce("Hand fly options saved successfully");
            }
        }
    }

    private void TaxiGuidanceOptionsMenuItem_Click(object? sender, EventArgs e)
    {
        var currentSettings = SettingsManager.Current;
        // Task 4 — Manual taxiway-name refresh callback.
        // Only wired when the augmenting provider is available; null otherwise
        // (the button in the dialog disables itself when the callback is null).
        // The callback runs on a thread-pool thread and marshals the announce
        // back to the UI thread via BeginInvoke so it is always SILENT unless
        // the user explicitly pressed the button.
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

                // Tell the pilot HOW MANY names the online sources added (new feature).
                var cov = provider.GetLastCoverage(icao);
                int added = cov == null ? 0
                    : cov.NamesAdoptedFromOsm + cov.NamesAdoptedFromAptDat + cov.AliasesAdded;
                string msg = added > 0
                    ? $"Taxiway names refreshed for {icao}: {added} added."
                    : $"Taxiway names refreshed for {icao}. No new names found.";
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(() => announcer.AnnounceImmediate(msg));
            };
        }

        using (var settingsForm = new Forms.TaxiGuidanceOptionsForm(
            currentSettings.TaxiGuidanceToneWaveform,
            currentSettings.TaxiGuidanceToneVolume,
            currentSettings.TaxiGuidanceInvertSteeringTone,
            currentSettings.TaxiGuidanceHardPanTone,
            currentSettings.TaxiGuidanceAnnounceCrossings,
            currentSettings.TaxiGuidanceGroundSpeedAnnounceInterval,
            currentSettings.TakeoffAssistGroundSpeedAnnounceInterval,
            currentSettings.GroundTrafficUseMetres,
            currentSettings.GsxAutoSelectGateOnRoute,
            currentSettings.DockingGuidanceEnabled,
            currentSettings.DockingBeepWaveform,
            currentSettings.DockingBeepVolume,
            onRefreshTaxiwayNames: refreshCallback,
            taxiAugmentEnabled: currentSettings.TaxiAugmentEnabled))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                currentSettings.TaxiGuidanceToneWaveform = settingsForm.SelectedToneWaveform;
                currentSettings.TaxiGuidanceToneVolume = settingsForm.SelectedVolume;
                currentSettings.TaxiGuidanceInvertSteeringTone = settingsForm.InvertSteeringTone;
                currentSettings.TaxiGuidanceHardPanTone = settingsForm.HardPanSteeringTone;
                currentSettings.TaxiGuidanceAnnounceCrossings = settingsForm.AnnounceCrossings;
                currentSettings.TaxiGuidanceGroundSpeedAnnounceInterval = settingsForm.GroundSpeedAnnounceInterval;
                currentSettings.TakeoffAssistGroundSpeedAnnounceInterval = settingsForm.TakeoffGroundSpeedAnnounceInterval;
                currentSettings.GroundDistanceUnit = settingsForm.SelectedDistanceUnit;
                currentSettings.GroundTrafficUseMetres = settingsForm.GroundTrafficUseMetres;
                currentSettings.GsxAutoSelectGateOnRoute = settingsForm.GsxAutoSelectGateOnRoute;
                currentSettings.DockingGuidanceEnabled = settingsForm.DockingGuidanceEnabled;
                currentSettings.DockingBeepWaveform = settingsForm.DockingBeepWaveform;
                currentSettings.DockingBeepVolume = settingsForm.DockingBeepVolume;
                currentSettings.TaxiAugmentEnabled = settingsForm.TaxiAugmentEnabled;
                // Apply live (next route build) — no restart needed.
                if (_augmentingProvider != null)
                    _augmentingProvider.Enabled = settingsForm.TaxiAugmentEnabled;
                SettingsManager.Save();

                statusLabel.Text = "Taxi guidance options saved successfully";
                announcer.Announce("Taxi guidance options saved successfully");
            }
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

    private async void UpdateApplicationMenuItem_Click(object? sender, EventArgs e)
    {
        try
        {
            announcer.AnnounceImmediate("Checking for updates...");

            var updateService = new UpdateService();
            var result = await updateService.CheckForUpdatesAsync();

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                announcer.AnnounceImmediate($"Update check failed: {result.ErrorMessage}");
                MessageBox.Show(
                    result.ErrorMessage,
                    "Update Check Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                announcer.AnnounceImmediate("You are running the latest version.");
                MessageBox.Show(
                    $"You are running the latest version ({result.CurrentVersion}).",
                    "No Updates Available",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            // Show update dialog
            announcer.AnnounceImmediate($"Update available: version {result.LatestVersion}");
            using (var updateDialog = new UpdateAvailableForm(result, updateService))
            {
                if (updateDialog.ShowDialog(this) == DialogResult.OK && updateDialog.ShouldUpdate)
                {
                    try
                    {
                        // Launch updater
                        announcer.AnnounceImmediate("Launching updater. Application will close and restart.");
                        updateService.LaunchUpdater(updateDialog.DownloadedZipPath);

                        // Close the main application
                        Application.Exit();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Failed to launch updater: {ex.Message}",
                            "Update Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            announcer.AnnounceImmediate($"Update failed: {ex.Message}");
            MessageBox.Show(
                $"An error occurred while checking for updates: {ex.Message}",
                "Update Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
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
