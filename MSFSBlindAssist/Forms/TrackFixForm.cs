using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

public partial class TrackFixForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly WaypointTracker _waypointTracker;
    private readonly SimConnectManager _simConnectManager;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly NavigationDatabaseProvider _dbProvider;
    private IntPtr _previousWindow;

    private List<WaypointFix> _duplicateWaypoints = new List<WaypointFix>();
    private bool _isDuplicateMode = false;

    public TrackFixForm(
        WaypointTracker waypointTracker,
        SimConnectManager simConnectManager,
        ScreenReaderAnnouncer announcer,
        string databasePath)
    {
        _waypointTracker = waypointTracker;
        _simConnectManager = simConnectManager;
        _announcer = announcer;
        _dbProvider = new NavigationDatabaseProvider(databasePath);

        InitializeComponent();
        SetupAccessibility();
    }

    public void ShowForm()
    {
        // Capture the current foreground window before showing
        _previousWindow = GetForegroundWindow();

        // Reset to search mode
        SwitchToSearchMode();

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to bring to front

        // Focus on waypoint textbox
        waypointTextBox.Focus();
    }

    private void SetupAccessibility()
    {
        // Handle form closing to hide instead of dispose
        FormClosing += (sender, e) =>
        {
            // Cancel the close and hide instead
            e.Cancel = true;
            Hide();

            // Restore focus to the previous window (likely the simulator)
            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
        };

        // Handle Escape key
        KeyPreview = true;
        KeyDown += (sender, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void TrackButton_Click(object? sender, EventArgs e)
    {
        string waypointName = waypointTextBox.Text.Trim().ToUpper();

        if (string.IsNullOrEmpty(waypointName))
        {
            _announcer.Announce("Please enter a waypoint name");
            waypointTextBox.Focus();
            return;
        }

        int slotNumber = slotComboBox.SelectedIndex + 1; // Convert 0-4 to 1-5

        // Query database for waypoints
        var waypoints = _dbProvider.GetWaypointsByIdent(waypointName);

        if (waypoints.Count == 0)
        {
            _announcer.Announce($"Waypoint {waypointName} not found");
            waypointTextBox.Focus();
            return;
        }
        else if (waypoints.Count == 1)
        {
            // Single match - track it immediately
            TrackWaypoint(waypoints[0], slotNumber);
        }
        else
        {
            // Multiple matches - show duplicate resolution UI
            ShowDuplicateResolution(waypoints, waypointName, slotNumber);
        }
    }

    private void ShowDuplicateResolution(List<WaypointFix> waypoints, string waypointName, int slotNumber)
    {
        _duplicateWaypoints = waypoints;
        _isDuplicateMode = true;

        // Get current aircraft position to calculate distances
        _simConnectManager.RequestAircraftPositionAsync(position =>
        {
            // Calculate distances for all waypoints
            foreach (var waypoint in _duplicateWaypoints)
            {
                waypoint.DistanceFromAircraft = NavigationCalculator.CalculateDistance(
                    position.Latitude,
                    position.Longitude,
                    waypoint.Latitude,
                    waypoint.Longitude
                );
            }

            // Sort by distance
            _duplicateWaypoints = _duplicateWaypoints.OrderBy(w => w.DistanceFromAircraft ?? double.MaxValue).ToList();

            // Populate ListView on UI thread
            if (InvokeRequired)
            {
                Invoke(() => PopulateDuplicateList(slotNumber));
            }
            else
            {
                PopulateDuplicateList(slotNumber);
            }
        });

        _announcer.Announce($"{waypoints.Count} waypoints found for {waypointName}, select one");
    }

    private void PopulateDuplicateList(int slotNumber)
    {
        duplicateListView.Items.Clear();

        foreach (var waypoint in _duplicateWaypoints)
        {
            string distanceText = waypoint.DistanceFromAircraft.HasValue
                ? $"{waypoint.DistanceFromAircraft.Value:F1} NM"
                : "Unknown";

            // Combine all information into single readable string for screen readers
            string itemText = $"{waypoint.Ident} - Region {waypoint.Region} - {distanceText}";
            var item = new ListViewItem(itemText);

            item.Tag = waypoint;
            duplicateListView.Items.Add(item);
        }

        // Select first item
        if (duplicateListView.Items.Count > 0)
        {
            duplicateListView.Items[0].Selected = true;
        }

        // Switch UI to duplicate mode
        SwitchToDuplicateMode();

        // Store slot number for later use
        selectButton.Tag = slotNumber;
    }

    private void SelectButton_Click(object? sender, EventArgs e)
    {
        if (duplicateListView.SelectedItems.Count == 0)
        {
            _announcer.Announce("Please select a waypoint from the list");
            duplicateListView.Focus();
            return;
        }

        var selectedItem = duplicateListView.SelectedItems[0];
        var waypoint = selectedItem.Tag as WaypointFix;
        int slotNumber = (int)(selectButton.Tag ?? 1);

        if (waypoint != null)
        {
            TrackWaypoint(waypoint, slotNumber);
        }
    }

    private void DuplicateListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            SelectButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void TrackWaypoint(WaypointFix waypoint, int slotNumber)
    {
        _waypointTracker.TrackWaypoint(slotNumber, waypoint);
        _announcer.Announce($"Waypoint {waypoint.Ident} tracked in slot {slotNumber}");

        // Close the form
        Hide();

        // Restore focus to the previous window
        if (_previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_previousWindow);
        }
    }

    private void SwitchToSearchMode()
    {
        _isDuplicateMode = false;

        // Show search controls
        waypointLabel.Visible = true;
        waypointTextBox.Visible = true;
        slotLabel.Visible = true;
        slotComboBox.Visible = true;
        trackButton.Visible = true;

        // Hide duplicate controls
        duplicateLabel.Visible = false;
        duplicateListView.Visible = false;
        selectButton.Visible = false;

        // Reset form size
        ClientSize = new Size(384, 211);

        // Clear waypoint text
        waypointTextBox.Clear();
    }

    private void SwitchToDuplicateMode()
    {
        _isDuplicateMode = true;

        // Hide search controls
        waypointLabel.Visible = false;
        waypointTextBox.Visible = false;
        slotLabel.Visible = false;
        slotComboBox.Visible = false;
        trackButton.Visible = false;

        // Show duplicate controls
        duplicateLabel.Visible = true;
        duplicateListView.Visible = true;
        selectButton.Visible = true;

        // Resize form for duplicate list
        ClientSize = new Size(584, 430);

        // Focus on ListView
        duplicateListView.Focus();
    }
}
