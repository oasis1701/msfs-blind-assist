using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Per-variable announcement toggle for PMDG aircraft.
///
/// Mirrors <see cref="FenixMonitorManagerForm"/> but populates the list
/// dynamically from the loaded aircraft's variable definitions instead of a
/// hard-coded key list. Any variable flagged <c>IsAnnounced = true</c> with
/// <c>UpdateFrequency.Continuous</c> shows up here. Items are sorted by
/// display name so the alphabetical order the user expects holds even when
/// new variables are added to the aircraft definition.
///
/// Unticked items go into <c>UserSettings.PMDGDisabledMonitorVariables</c>
/// (persisted to disk). MainForm consults that list in the
/// continuous-monitoring branch and silently skips the announcement —
/// state changes still update internal caches, only the speech is
/// suppressed. Re-ticking removes the key from the disabled list so the
/// announcement resumes immediately, no app restart required.
/// </summary>
public partial class PMDGAnnouncementMonitorForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private CheckedListBox variableListBox = null!;
    private TextBox filterTextBox = null!;

    /// <summary>
    /// Snapshot of (key, displayName) for every PMDG variable surfaceable
    /// in this form, sorted alphabetically by display name. Built once at
    /// form-construction so the list order is stable even if the aircraft
    /// definition mutates the underlying dictionary later.
    /// </summary>
    private readonly List<(string key, string displayName)> _items;

    /// <summary>
    /// Filter-applied items currently shown in the list box. Tracked
    /// separately because the index in `variableListBox.Items` no longer
    /// maps directly to `_items` once a filter is applied.
    /// </summary>
    private List<(string key, string displayName)> _visibleItems = new();

    /// <summary>
    /// Suppresses the side-effects of <see cref="VariableListBox_ItemCheck"/>
    /// while ApplyFilter is rebuilding the listbox. Without this, the
    /// SetItemChecked calls fire ItemCheck once per item — for ~400 PMDG
    /// variables that meant 400 SettingsManager.Save() (disk write) calls
    /// per filter keystroke, all on the UI thread. Set true around the
    /// rebuild loop, false after.
    /// </summary>
    private bool _suppressItemCheck;

    private IntPtr previousWindow;

    // Persist focus position across show/hide cycles. Static so a fresh form
    // instance (e.g. after aircraft swap re-creates it) restores the user's
    // last position.
    private static int lastSelectedItemIndex = 0;
    private static string lastFilterText = "";

    public PMDGAnnouncementMonitorForm(ScreenReaderAnnouncer announcer, Dictionary<string, SimVarDefinition> variables)
    {
        // Build the canonical sorted list once. We surface variables whose
        // definitions say they're meant to auto-announce continuously —
        // anything OnRequest, momentary buttons, etc. is excluded because
        // they aren't in the auto-announce path so toggling them here would
        // be a no-op.
        _items = variables
            .Where(kvp => kvp.Value.IsAnnounced
                       && kvp.Value.UpdateFrequency == UpdateFrequency.Continuous)
            .Select(kvp => (kvp.Key,
                            displayName: string.IsNullOrEmpty(kvp.Value.DisplayName) ? kvp.Key : kvp.Value.DisplayName))
            .OrderBy(t => t.displayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        InitializeComponent();
        SetupAccessibility();
        ApplyFilter(lastFilterText);
    }

    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;

        if (variableListBox.Items.Count > 0)
        {
            int itemIndex = Math.Min(lastSelectedItemIndex, variableListBox.Items.Count - 1);
            variableListBox.SelectedIndex = itemIndex;
        }

        // Focus the filter box rather than the list — typing a few letters is
        // the fastest way to find a specific variable when there are 300+ of
        // them. Tab moves to the list.
        filterTextBox.Focus();
    }

    private void InitializeComponent()
    {
        Text = "PMDG Announcement Monitor";
        Size = new Size(560, 460);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        var instructions = new Label
        {
            Text = "Untick variables to silence their automatic announcements. Re-ticking restores them.",
            Location = new Point(10, 10),
            Size = new Size(530, 30),
            AccessibleName = "Instructions"
        };

        var filterLabel = new Label
        {
            Text = "&Filter:",
            Location = new Point(10, 50),
            Size = new Size(60, 22),
            AccessibleName = "Filter label"
        };

        filterTextBox = new TextBox
        {
            Location = new Point(75, 48),
            Size = new Size(380, 22),
            AccessibleName = "Filter",
            AccessibleDescription = "Type to narrow the list to variables whose name contains the typed text",
            TabIndex = 0,
            Text = lastFilterText
        };
        filterTextBox.TextChanged += (s, e) =>
        {
            lastFilterText = filterTextBox.Text;
            ApplyFilter(filterTextBox.Text);
        };

        var countLabel = new Label
        {
            Text = "",
            Location = new Point(465, 50),
            Size = new Size(80, 22),
            Name = "countLabel",
            AccessibleName = "Result count",
            TabStop = false
        };

        variableListBox = new CheckedListBox
        {
            Location = new Point(10, 80),
            Size = new Size(530, 330),
            TabIndex = 1,
            AccessibleName = "Monitored Variables",
            CheckOnClick = true
        };

        variableListBox.ItemCheck += VariableListBox_ItemCheck;
        variableListBox.KeyDown += VariableListBox_KeyDown;
        variableListBox.SelectedIndexChanged += VariableListBox_SelectedIndexChanged;

        Controls.Add(instructions);
        Controls.Add(filterLabel);
        Controls.Add(filterTextBox);
        Controls.Add(countLabel);
        Controls.Add(variableListBox);
    }

    private void SetupAccessibility()
    {
        // Hide instead of dispose so checkbox state isn't lost between opens.
        FormClosing += (sender, e) =>
        {
            e.Cancel = true;
            Hide();
            if (previousWindow != IntPtr.Zero)
                SetForegroundWindow(previousWindow);
        };
    }

    /// <summary>
    /// Rebuilds the list box from `_items` filtered by a substring match
    /// against the display name (case-insensitive). Empty filter shows all.
    /// </summary>
    private void ApplyFilter(string filterText)
    {
        var disabledVars = SettingsManager.Current.PMDGDisabledMonitorVariables;
        string filterLower = (filterText ?? "").Trim().ToLowerInvariant();

        _visibleItems = string.IsNullOrEmpty(filterLower)
            ? _items
            : _items.Where(t => t.displayName.ToLowerInvariant().Contains(filterLower)).ToList();

        // SetItemChecked fires the ItemCheck handler, which would call
        // SettingsManager.Save() once per row (~400 disk writes per filter
        // keystroke). Suppress while rebuilding — the persisted state is
        // the source of truth, the listbox is just a view of it.
        _suppressItemCheck = true;
        variableListBox.BeginUpdate();
        try
        {
            variableListBox.Items.Clear();
            foreach (var item in _visibleItems)
            {
                int index = variableListBox.Items.Add(item.displayName);
                variableListBox.SetItemChecked(index, !disabledVars.Contains(item.key));
            }
        }
        finally
        {
            variableListBox.EndUpdate();
            _suppressItemCheck = false;
        }

        var countLabel = (Label?)Controls.Find("countLabel", true).FirstOrDefault();
        if (countLabel != null)
            countLabel.Text = $"{_visibleItems.Count} of {_items.Count}";
    }

    private void VariableListBox_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressItemCheck) return;
        if (e.Index < 0 || e.Index >= _visibleItems.Count)
            return;

        string variableKey = _visibleItems[e.Index].key;
        var settings = SettingsManager.Current;

        if (e.NewValue == CheckState.Checked)
        {
            settings.PMDGDisabledMonitorVariables.Remove(variableKey);
        }
        else
        {
            if (!settings.PMDGDisabledMonitorVariables.Contains(variableKey))
                settings.PMDGDisabledMonitorVariables.Add(variableKey);
        }

        SettingsManager.Save();
    }

    private void VariableListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void VariableListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (variableListBox.SelectedIndex >= 0)
            lastSelectedItemIndex = variableListBox.SelectedIndex;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}
