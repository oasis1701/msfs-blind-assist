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
        // (SimBrief and Gemini have no live effect.)

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

        // Update HandFlyManager if it's active
        handFlyManager?.UpdateSettings(
            settings.HandFlyFeedbackMode,
            settings.HandFlyWaveType,
            settings.HandFlyToneVolume,
            settings.HandFlyMonitorHeading,
            settings.HandFlyMonitorVerticalSpeed);

        // Taxi Guidance / Docking — moved verbatim from the retired TaxiGuidanceOptionsMenuItem_Click.
        // Every other taxi/docking setting (tone type/volume, invert/hard-pan, announce-crossings,
        // ground-speed intervals, distance units, GSX auto-select, docking enabled/beep) is read
        // live from SettingsManager.Current at point of use (TaxiGuidanceManager, DockingGuidanceManager,
        // GroundSpeedAnnouncer, GroundTrafficMonitor, TaxiAssistForm) — no push needed for those.
        // The online taxiway/gate-name augmentation toggle is the one setting with a live service
        // to push into; apply it here so it takes effect immediately (next route build).
        if (_augmentingProvider != null)
            _augmentingProvider.Enabled = settings.TaxiAugmentEnabled;

        // First Officer automation toggles — push the saved settings into any open First
        // Officer window so a change takes effect without reopening it (moved from the
        // retired FOSettingsMenuItem_Click).
        foreach (var foForm in OpenFirstOfficerForms())
            foForm.ApplySettings();
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
