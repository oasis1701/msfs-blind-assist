using System;
using System.Drawing;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Controls;

/// <summary>
/// Manages the "Focus camera here" toggle check button and Alt+Shift+C shortcut in MCDU and EFB forms for Streamer Mode.
/// </summary>
public static class StreamerCameraFocusHelper
{
    /// <summary>
    /// Creates and configures the "Focus camera here" toggle checkbox.
    /// Manages check state, camera focus transitions, Alt+Shift+C shortcut, and auto-release on hide/close.
    /// </summary>
    public static CheckBox CreateFocusCheckBox(
        Form form,
        CockpitCameraTarget target,
        Point location,
        Func<IAircraftDefinition?>? getAircraft = null)
    {
        var checkBox = new CheckBox
        {
            Text = "Focus camera here",
            AutoSize = true,
            Location = location,
            AccessibleName = "Focus camera here",
            AccessibleDescription = "Toggles close-up camera focus for stream viewers (Alt+Shift+C)",
            Visible = SettingsManager.Current.StreamerModeEnabled,
            TabStop = true
        };

        checkBox.CheckedChanged += async (s, e) =>
        {
            var aircraft = getAircraft?.Invoke();
            await StreamerModeService.Instance.SetCameraFocusAsync(target, checkBox.Checked, aircraft);
        };

        // When the form becomes visible, sync visibility with settings.
        // When hidden or closed while focus is active, release camera back to forward pilot view.
        form.VisibleChanged += (s, e) =>
        {
            if (form.Visible)
            {
                checkBox.Visible = SettingsManager.Current.StreamerModeEnabled;
            }
            else if (checkBox.Checked)
            {
                checkBox.Checked = false;
            }
        };

        form.FormClosing += (s, e) =>
        {
            if (checkBox.Checked)
            {
                checkBox.Checked = false;
            }
        };

        form.Disposed += (s, e) =>
        {
            if (checkBox.Checked)
            {
                checkBox.Checked = false;
            }
        };

        return checkBox;
    }

    /// <summary>
    /// Processes the Alt+Shift+C shortcut to toggle camera focus.
    /// Returns true if handled.
    /// </summary>
    public static bool HandleShortcut(Keys keyData, CheckBox? checkBox)
    {
        if (checkBox != null && checkBox.Visible && keyData == (Keys.Alt | Keys.Shift | Keys.C))
        {
            checkBox.Checked = !checkBox.Checked;
            return true;
        }
        return false;
    }
}

