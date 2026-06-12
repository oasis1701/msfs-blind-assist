using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FlyByWireA320;

/// <summary>
/// Per-variable background-announcement manager for the FlyByWire A32NX — the
/// A320 equivalent of the A380 / Fenix / PMDG monitor managers (opened with Ctrl+M).
///
/// Enumerates every auto-announced variable (UpdateFrequency.Continuous +
/// IsAnnounced) from the aircraft definition dynamically, so it always matches
/// what the A32NX actually announces. Unchecked items are written to
/// UserSettings.A32NXDisabledMonitorVariables; MainForm.OnSimVarUpdated skips the
/// announcement for any key in that list (when AircraftCode == "A320").
/// </summary>
public partial class FlyByWireA320MonitorManagerForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private CheckedListBox _list = null!;
    private readonly List<string> _keys = new();     // parallel to _list.Items
    private readonly List<string> _labels;
    private IntPtr _previousWindow;
    private static int _lastIndex;

    public FlyByWireA320MonitorManagerForm(ScreenReaderAnnouncer announcer, Dictionary<string, SimVarDefinition> variables)
    {

        // Build the manageable list: every announced continuous var, by display name.
        foreach (var kv in variables)
        {
            if (kv.Value.UpdateFrequency != UpdateFrequency.Continuous || !kv.Value.IsAnnounced
                || kv.Value.ExcludeFromMonitorManager) continue;
            _keys.Add(kv.Key);
        }
        _keys.Sort((a, b) =>
            string.Compare(DisplayNameFor(variables, a), DisplayNameFor(variables, b), StringComparison.OrdinalIgnoreCase));
        _labels = _keys.Select(k => DisplayNameFor(variables, k)).ToList();

        InitializeComponent();
        SetupAccessibility();
        Populate();
    }

    private static string DisplayNameFor(Dictionary<string, SimVarDefinition> vars, string key) =>
        vars.TryGetValue(key, out var d) && !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : key;

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Min(_lastIndex, _list.Items.Count - 1);
        _list.Focus();
    }

    private void InitializeComponent()
    {
        Text = "A320 Monitor Manager";
        Size = new Size(460, 380);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;

        var label = new Label
        {
            Text = "Uncheck a variable to stop announcing it as it changes:",
            Location = new Point(10, 10),
            Size = new Size(430, 20),
            AccessibleName = "Instructions"
        };

        _list = new CheckedListBox
        {
            Location = new Point(10, 35),
            Size = new Size(425, 290),
            TabIndex = 0,
            AccessibleName = "Auto-announced variables",
            CheckOnClick = true
        };
        _list.ItemCheck += OnItemCheck;
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
        _list.SelectedIndexChanged += (_, _) => { if (_list.SelectedIndex >= 0) _lastIndex = _list.SelectedIndex; };

        Controls.Add(label);
        Controls.Add(_list);
    }

    private void SetupAccessibility()
    {
        FormClosing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    private void Populate()
    {
        var disabled = SettingsManager.Current.A32NXDisabledMonitorVariables;
        _list.BeginUpdate();
        _list.Items.Clear();
        for (int i = 0; i < _labels.Count; i++)
        {
            _list.Items.Add(_labels[i]);
            _list.SetItemChecked(i, !disabled.Contains(_keys[i])); // checked = announcing
        }
        _list.EndUpdate();
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _keys.Count) return;
        string key = _keys[e.Index];
        var settings = SettingsManager.Current;
        if (e.NewValue == CheckState.Checked)
            settings.A32NXDisabledMonitorVariables.Remove(key);
        else if (!settings.A32NXDisabledMonitorVariables.Contains(key))
            settings.A32NXDisabledMonitorVariables.Add(key);
        SettingsManager.Save();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
}
