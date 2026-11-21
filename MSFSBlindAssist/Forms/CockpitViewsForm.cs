using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Represents an available camera view option displayed in the <see cref="CockpitViewsForm"/>.
/// </summary>
public sealed record CockpitViewOption(string DisplayName, CockpitCameraTarget Target, string Description);

/// <summary>
/// Allows the pilot to browse and select cockpit camera viewpoints for stream viewers in Streamer Mode.
/// Accessible via keyboard navigation (Input Shift+V) and the Extras menu.
/// </summary>
public class CockpitViewsForm : Form
{
    private readonly StreamerModeService _streamerService;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<IAircraftDefinition?> _getAircraft;

    private ListBox _viewsListBox = null!;
    private CheckBox _holdCheckBox = null!;
    private Button _selectButton = null!;
    private Button _returnForwardButton = null!;
    private Button _closeButton = null!;
    private Label _statusLabel = null!;

    private readonly List<CockpitViewOption> _options = new();

    public CockpitViewsForm(
        StreamerModeService streamerService,
        ScreenReaderAnnouncer announcer,
        Func<IAircraftDefinition?> getAircraft)
    {
        _streamerService = streamerService;
        _announcer = announcer;
        _getAircraft = getAircraft;

        InitializeComponent();
        PopulateViews();
    }

    private void InitializeComponent()
    {
        Text = "Cockpit Views";
        AccessibleName = "Cockpit Views";
        AccessibleDescription = "Select and focus cockpit camera views for stream viewers";
        Size = new Size(420, 440);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var listLabel = new Label
        {
            Location = new Point(20, 15),
            Size = new Size(365, 20),
            Text = "Available Views:",
            AccessibleName = "Available Views"
        };

        _viewsListBox = new ListBox
        {
            Location = new Point(20, 38),
            Size = new Size(365, 220),
            AccessibleName = "Cockpit Views List",
            AccessibleDescription = "Use Up and Down arrow keys to browse views. Press Enter to select the view.",
            TabIndex = 0
        };
        _viewsListBox.DoubleClick += (s, e) => SelectCurrentView();
        _viewsListBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                SelectCurrentView();
            }
        };

        _holdCheckBox = new CheckBox
        {
            Location = new Point(20, 270),
            Size = new Size(365, 24),
            Text = "Hold camera on selected view",
            Checked = true,
            AccessibleName = "Hold camera on selected view",
            AccessibleDescription = "When checked, the camera stays on the view until changed. When unchecked, it returns forward after 2 seconds.",
            TabIndex = 1
        };

        _selectButton = new Button
        {
            Location = new Point(20, 305),
            Size = new Size(100, 32),
            Text = "&Select",
            AccessibleName = "Select View",
            AccessibleDescription = "Moves the camera to the selected view",
            TabIndex = 2
        };
        _selectButton.Click += (s, e) => SelectCurrentView();

        _returnForwardButton = new Button
        {
            Location = new Point(130, 305),
            Size = new Size(150, 32),
            Text = "&Return to Forward",
            AccessibleName = "Return to Forward",
            AccessibleDescription = "Restores camera to forward-looking pilot eye level",
            TabIndex = 3
        };
        _returnForwardButton.Click += (s, e) => ReturnForward();

        _closeButton = new Button
        {
            Location = new Point(290, 305),
            Size = new Size(95, 32),
            Text = "&Close",
            AccessibleName = "Close",
            AccessibleDescription = "Closes this window",
            TabIndex = 4
        };
        _closeButton.Click += (s, e) => Close();

        _statusLabel = new Label
        {
            Location = new Point(20, 350),
            Size = new Size(365, 40),
            AccessibleName = "Status",
            Text = "Press Enter or click Select to move camera to the chosen view."
        };

        AcceptButton = _selectButton;
        CancelButton = _closeButton;

        Controls.Add(listLabel);
        Controls.Add(_viewsListBox);
        Controls.Add(_holdCheckBox);
        Controls.Add(_selectButton);
        Controls.Add(_returnForwardButton);
        Controls.Add(_closeButton);
        Controls.Add(_statusLabel);
    }

    /// <summary>
    /// Displays this window, bringing it to the foreground and focusing the views list.
    /// </summary>
    public void ShowForm()
    {
        if (!SettingsManager.Current.StreamerModeEnabled)
        {
            _announcer.Announce("Streamer mode is disabled. Enable it in Settings to access cockpit views.");
            return;
        }

        PopulateViews();
        Show();
        BringToFront();
        Activate();
        _viewsListBox.Focus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!SettingsManager.Current.StreamerModeEnabled)
        {
            Close();
            return;
        }
        PopulateViews();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void PopulateViews()
    {
        _options.Clear();
        _viewsListBox.Items.Clear();

        var aircraft = _getAircraft();

        // 1. Forward is always available
        _options.Add(new CockpitViewOption(
            "Forward (Pilot eye level)",
            CockpitCameraTarget.Forward,
            "Default pilot forward view looking at front panel and windshield"));

        // 2. Overhead Panel
        if (aircraft == null || aircraft.GetCameraViewForTarget(CockpitCameraTarget.Overhead).HasValue)
        {
            _options.Add(new CockpitViewOption(
                "Overhead Panel",
                CockpitCameraTarget.Overhead,
                "Overhead panel (electrical, fuel, hydraulics, lights, APU)"));
        }

        // 3. Glareshield
        if (aircraft == null || aircraft.GetCameraViewForTarget(CockpitCameraTarget.Glareshield).HasValue)
        {
            _options.Add(new CockpitViewOption(
                "Glareshield (FCU / MCP)",
                CockpitCameraTarget.Glareshield,
                "Autopilot, flight director, FCU / MCP and EFIS panels"));
        }

        // 4. Center Pedestal
        if (aircraft == null || aircraft.GetCameraViewForTarget(CockpitCameraTarget.Pedestal).HasValue)
        {
            _options.Add(new CockpitViewOption(
                "Center Pedestal",
                CockpitCameraTarget.Pedestal,
                "Throttles, engine masters, radios, transponder, trim, radar"));
        }

        // 5. Main Instrument Panel (wide forward captain view)
        _options.Add(new CockpitViewOption(
            "Main Instrument Panel",
            CockpitCameraTarget.MainPanel,
            "Captain main panel (PFD, ND, standby instruments, gear)"));

        // 6. MCDU / FMC
        if (aircraft == null || aircraft.GetCameraViewForTarget(CockpitCameraTarget.MCDU).HasValue)
        {
            _options.Add(new CockpitViewOption(
                "MCDU / FMC",
                CockpitCameraTarget.MCDU,
                "Flight management computer (close-up view)"));
        }

        // 7. EFB / Tablet
        if (aircraft != null && aircraft.GetCameraViewForTarget(CockpitCameraTarget.EFB).HasValue)
        {
            _options.Add(new CockpitViewOption(
                "EFB / Tablet",
                CockpitCameraTarget.EFB,
                "Electronic flight bag tablet (close-up view)"));
        }

        // 8. Cockpit Door
        if (aircraft != null && aircraft.GetCameraViewForTarget(CockpitCameraTarget.CockpitDoor).HasValue)
        {
            _options.Add(new CockpitViewOption(
                "Cockpit Door",
                CockpitCameraTarget.CockpitDoor,
                "Cockpit entry door and cabin view"));
        }

        int selectIndex = 0;
        for (int i = 0; i < _options.Count; i++)
        {
            var opt = _options[i];
            bool isCurrent = _streamerService.CurrentTarget == opt.Target;
            string label = isCurrent ? $"{opt.DisplayName} (Active)" : opt.DisplayName;
            _viewsListBox.Items.Add(label);
            if (isCurrent) selectIndex = i;
        }

        if (_viewsListBox.Items.Count > 0)
        {
            _viewsListBox.SelectedIndex = selectIndex;
        }
    }

    private async void SelectCurrentView()
    {
        int index = _viewsListBox.SelectedIndex;
        if (index < 0 || index >= _options.Count) return;

        var option = _options[index];
        var aircraft = _getAircraft();
        bool hold = _holdCheckBox.Checked;

        _statusLabel.Text = $"Moving camera to {option.DisplayName}...";
        bool success = await _streamerService.SelectViewAsync(option.Target, hold, aircraft);
        if (success)
        {
            string msg = hold
                ? $"Camera holding on {option.DisplayName}."
                : $"Camera set to {option.DisplayName} (returns forward in 2s).";
            _statusLabel.Text = msg;
            _announcer.AnnounceImmediate(msg);
            PopulateViews();
        }
        else
        {
            string msg = $"Could not move camera to {option.DisplayName}.";
            _statusLabel.Text = msg;
            _announcer.AnnounceImmediate(msg);
        }
    }

    private async void ReturnForward()
    {
        _statusLabel.Text = "Returning camera forward...";
        await _streamerService.SelectViewAsync(CockpitCameraTarget.Forward, hold: false, _getAircraft());
        string msg = "Camera returned to Forward view.";
        _statusLabel.Text = msg;
        _announcer.AnnounceImmediate(msg);
        PopulateViews();
    }
}
