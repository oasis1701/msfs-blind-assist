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

    // When the dialog was opened pre-populated from the EFB, the already-resolved fix is carried here
    // so Track uses its exact coordinates (incl. navaid/runway fixes not in the waypoint table) instead
    // of re-searching the ident. Cleared whenever the dialog is reset or the pilot changes the name.
    private WaypointFix? _prefilledFix;

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

        // The Upper Altitude field is only meaningful for a BETWEEN constraint — show it only then.
        constraintComboBox.SelectedIndexChanged += (s, e) => UpdateUpperAltVisibility();
        UpdateUpperAltVisibility();
    }

    public void ShowForm()
    {
        // Capture the current foreground window before showing
        _previousWindow = GetForegroundWindow();

        // Reset to search mode
        SwitchToSearchMode();

        // Plain open — drop any per-fix caption a previous pre-filled open left behind.
        Text = DefaultCaption;

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to bring to front

        // Focus on waypoint textbox
        waypointTextBox.Focus();
    }

    private const string DefaultCaption = "Track Fix Window";

    /// <summary>
    /// Open the dialog PRE-POPULATED from an EFB-selected route fix — its ident, the chosen slot, and
    /// the altitude constraint + course mapped from the fix's own navdata (<see cref="WaypointConstraintMapper"/>).
    /// The pilot reviews/edits and presses Track to commit, so the tracked constraint is visible and
    /// editable (instead of a silent direct-track). The resolved fix is carried so Track uses its exact
    /// coordinates rather than re-searching the ident.
    /// </summary>
    public void ShowFormPrefilled(WaypointFix fix, int slotNumber)
    {
        _previousWindow = GetForegroundWindow();

        SwitchToSearchMode();          // clears fields + resets the constraint (and clears _prefilledFix)
        _prefilledFix = fix;

        waypointTextBox.Text = fix.Ident;
        if (slotNumber >= 1 && slotNumber <= 5)
            slotComboBox.SelectedIndex = slotNumber - 1;

        var (crossingAlt, upperAlt, constraint, course) = WaypointConstraintMapper.FromFix(fix);
        crossingAltTextBox.Text = crossingAlt.HasValue ? crossingAlt.Value.ToString("F0") : "";
        constraintComboBox.SelectedIndex = ConstraintToComboIndex(constraint);
        upperAltTextBox.Text = upperAlt.HasValue ? upperAlt.Value.ToString("F0") : "";
        courseTextBox.Text = course.HasValue ? course.Value.ToString("F0") : "";
        // Speed restriction: pre-filled straight off the leg (VCBI ANUT1D codes 240 kt at BI551),
        // and editable — a pilot may be given a different speed by ATC, or want one where the
        // procedure has none.
        speedTextBox.Text = fix.SpeedLimit.HasValue ? fix.SpeedLimit.Value.ToString() : "";
        UpdateUpperAltVisibility();

        // Which fix and slot this is goes in the CAPTION, not a spoken announcement. The screen
        // reader already speaks the window title and then the focused field on activation, so an
        // Announce here just talks over its own context (and the project rule is to leave UI
        // interactions to the reader). The caption carries the same information, and the pilot can
        // re-read it at any time — an announcement is gone once spoken.
        Text = $"{DefaultCaption} — {fix.Ident}, slot {slotNumber}";

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to bring to front

        // Focus the crossing-altitude field — the value most likely to be added or edited.
        crossingAltTextBox.Focus();
    }

    /// <summary>Maps an <see cref="AltitudeConstraintType"/> to the constraint combo's item index
    /// (None=0, At=1, At-or-above=2, At-or-below=3, Between=4).</summary>
    private static int ConstraintToComboIndex(AltitudeConstraintType c) => c switch
    {
        AltitudeConstraintType.At => 1,
        AltitudeConstraintType.AtOrAbove => 2,
        AltitudeConstraintType.AtOrBelow => 3,
        AltitudeConstraintType.Between => 4,
        _ => 0
    };

    /// <summary>The Upper Altitude label + box are shown only when the "Between" constraint is selected;
    /// hidden otherwise (a screen reader / Tab then skips them, and they can't be filled by mistake).</summary>
    private void UpdateUpperAltVisibility()
    {
        bool between = constraintComboBox.SelectedIndex == 4;   // "Between"
        upperAltLabel.Visible = between;
        upperAltTextBox.Visible = between;
    }

    private void SetupAccessibility()
    {
        // Handle form closing to hide instead of dispose
        FormClosing += (sender, e) =>
        {
            // Let real app/OS shutdown through: Application.Exit raises FormClosing on
            // every open form (hidden included) and ABORTS the whole exit if any form
            // cancels — an unconditional cancel here left the auto-updater stalled
            // against a still-running exe. Everything else still hides.
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }

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

        // Pre-filled from the EFB: track the already-resolved fix directly (preserves its exact
        // coordinates — incl. navaid/runway fixes not in the waypoint table — and skips the duplicate
        // re-search), as long as the pilot didn't change the name.
        if (_prefilledFix != null && string.Equals(waypointName, _prefilledFix.Ident, StringComparison.OrdinalIgnoreCase))
        {
            TrackWaypoint(_prefilledFix, slotNumber);
            return;
        }
        _prefilledFix = null;   // name changed → fall back to a fresh database search

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
        var (crossingAlt, upperAlt, constraint) = ParseCrossingConstraint();

        // Optional course to capture/hold through the fix (magnetic). Blank → direct-to.
        double? course = null;
        if (double.TryParse(courseTextBox.Text.Trim(), out double c) && c >= 0 && c <= 360)
            course = c % 360.0;

        // Reference variation the magnetic course is defined against (navaid station declination / fix
        // local variation, from navdata). Lets the FD convert the course to true against the RIGHT magvar
        // instead of the aircraft's live one; null → the FD falls back to the aircraft magvar.
        // Speed restriction (knots INDICATED — ARINC 424 §5.72 codes it in IAS, and the FD compares
        // it against IAS). Blank clears it; an edit overrides whatever the procedure carried.
        if (int.TryParse(speedTextBox.Text.Trim(), out int spd) && spd > 0 && spd <= 1000)
            waypoint.SpeedLimit = spd;
        else
            waypoint.SpeedLimit = null;

        _waypointTracker.TrackWaypoint(slotNumber, waypoint, crossingAlt, upperAlt, constraint, course,
            waypoint.ReferenceMagVar);

        string detail = "";
        if (course.HasValue)
            detail += $", tracking course {course.Value:F0}";
        if (crossingAlt.HasValue)
        {
            detail += $", {ConstraintPhrase(constraint)} {crossingAlt.Value:F0} feet";
            if (constraint == AltitudeConstraintType.Between && upperAlt.HasValue)
                detail += $" and {upperAlt.Value:F0} feet";
        }
        if (waypoint.SpeedLimit.HasValue)
            detail += $", speed {waypoint.SpeedLimit.Value} knots";
        _announcer.Announce($"Waypoint {waypoint.Ident} tracked in slot {slotNumber}{detail}");

        // Close the form
        Hide();

        // Restore focus to the previous window
        if (_previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_previousWindow);
        }
    }

    /// <summary>
    /// Reads the optional crossing-altitude + constraint inputs for the Waypoint Flight Director's
    /// vertical guidance. Blank/unparseable altitude → lateral-only (constraint forced to None).
    /// </summary>
    private (double? crossingAlt, double? upperAlt, AltitudeConstraintType constraint) ParseCrossingConstraint()
    {
        double? crossingAlt = null, upperAlt = null;
        // Only a POSITIVE altitude is a real constraint (matches WaypointConstraintMapper, which drops
        // <= 0). A typed 0 / negative would otherwise leave the constraint set and make the FD's
        // vertical guidance command a descent toward sea level.
        if (double.TryParse(crossingAltTextBox.Text.Trim(), out double ca) && ca > 0) crossingAlt = ca;
        if (double.TryParse(upperAltTextBox.Text.Trim(), out double ua) && ua > 0) upperAlt = ua;

        var constraint = constraintComboBox.SelectedIndex switch
        {
            1 => AltitudeConstraintType.At,
            2 => AltitudeConstraintType.AtOrAbove,
            3 => AltitudeConstraintType.AtOrBelow,
            4 => AltitudeConstraintType.Between,
            _ => AltitudeConstraintType.None
        };

        // No altitude entered → lateral-only. Constraint None → drop any stray altitude.
        if (crossingAlt == null) constraint = AltitudeConstraintType.None;
        if (constraint == AltitudeConstraintType.None) { crossingAlt = null; upperAlt = null; }
        // Upper bound is only meaningful for Between; drop it otherwise so a value left in the (now
        // hidden) box from a prior Between selection can't leak into an At/above/below constraint.
        if (constraint != AltitudeConstraintType.Between) upperAlt = null;
        return (crossingAlt, upperAlt, constraint);
    }

    private static string ConstraintPhrase(AltitudeConstraintType c) => c switch
    {
        AltitudeConstraintType.At => "at",
        AltitudeConstraintType.AtOrAbove => "at or above",
        AltitudeConstraintType.AtOrBelow => "at or below",
        AltitudeConstraintType.Between => "between",
        _ => ""
    };

    private void SwitchToSearchMode()
    {
        // Show search controls
        waypointLabel.Visible = true;
        waypointTextBox.Visible = true;
        slotLabel.Visible = true;
        slotComboBox.Visible = true;
        crossingAltLabel.Visible = true;
        crossingAltTextBox.Visible = true;
        constraintLabel.Visible = true;
        constraintComboBox.Visible = true;
        // upperAltLabel / upperAltTextBox visibility is governed by UpdateUpperAltVisibility() (Between only).
        courseLabel.Visible = true;
        courseTextBox.Visible = true;
        trackButton.Visible = true;

        // Hide duplicate controls
        duplicateLabel.Visible = false;
        duplicateListView.Visible = false;
        selectButton.Visible = false;

        // Reset form size — must fit the Course field (to y=368) + the Track button (to y=415).
        ClientSize = new Size(384, 425);

        // Clear inputs
        waypointTextBox.Clear();
        crossingAltTextBox.Clear();
        upperAltTextBox.Clear();
        courseTextBox.Clear();
        constraintComboBox.SelectedIndex = 0;

        _prefilledFix = null;          // fresh manual entry — not carrying an EFB fix
        UpdateUpperAltVisibility();    // constraint back to None → hide the Between-only upper box
    }

    private void SwitchToDuplicateMode()
    {
        // Hide search controls
        waypointLabel.Visible = false;
        waypointTextBox.Visible = false;
        slotLabel.Visible = false;
        slotComboBox.Visible = false;
        crossingAltLabel.Visible = false;
        crossingAltTextBox.Visible = false;
        constraintLabel.Visible = false;
        constraintComboBox.Visible = false;
        upperAltLabel.Visible = false;
        upperAltTextBox.Visible = false;
        courseLabel.Visible = false;
        courseTextBox.Visible = false;
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
