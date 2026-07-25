using System.Runtime.InteropServices;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Per-variable background-announcement manager for the HorizonSim 787 (Ctrl+M).
///
/// Enumerates EVERY auto-announced variable (UpdateFrequency.Continuous + IsAnnounced) from the
/// aircraft definition dynamically — mirroring the Fenix / A380 / A32NX monitor managers — so the
/// 107+ announced HS787 vars can each be muted. Unchecked items are written to
/// UserSettings.HS787DisabledMonitorVariables; MainForm.OnSimVarUpdated skips the announcement for
/// any key in that list.
/// </summary>
public partial class HS787MonitorManagerForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private CheckedListBox variableListBox = null!;
    private readonly List<string> _keys = new();    // parallel to variableListBox.Items
    private readonly List<string> _labels = new();
    private IntPtr previousWindow;
    private static int lastSelectedItemIndex;
    private bool _populating;

    public HS787MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
    {
        // Build the manageable list: every announced continuous var, by display name.
        foreach (var kv in variables)
        {
            if (kv.Value.UpdateFrequency != UpdateFrequency.Continuous || !kv.Value.IsAnnounced) continue;
            _keys.Add(kv.Key);
        }
        _keys.Sort((a, b) =>
            string.Compare(DisplayNameFor(variables, a), DisplayNameFor(variables, b), StringComparison.OrdinalIgnoreCase));
        _labels.AddRange(_keys.Select(k => DisplayNameFor(variables, k)));

        InitializeComponent();
        SetupAccessibility();
        PopulateVariables();
    }

    private static string DisplayNameFor(Dictionary<string, SimVarDefinition> vars, string key) =>
        vars.TryGetValue(key, out var d) && !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : key;

    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();
        // Rebuild check states from the CURRENT persisted set on every open — the
        // form is cached by MainForm (constructed once, reused), so a populate that
        // ran only in the constructor would show stale checkboxes whenever the
        // disabled set changed after first open (a settings reload, or a var whose
        // IsAnnounced flag flipped across an app update, silently dropping it from /
        // re-adding it to _keys). Guarded by _populating so it fires no Save writes.
        PopulateVariables();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        if (variableListBox.Items.Count > 0)
            variableListBox.SelectedIndex = Math.Min(lastSelectedItemIndex, variableListBox.Items.Count - 1);
        variableListBox.Focus();
    }

    private void InitializeComponent()
    {
        Text = "787 Monitor Manager";
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

        variableListBox = new CheckedListBox
        {
            Location = new Point(10, 35),
            Size = new Size(425, 290),
            TabIndex = 0,
            AccessibleName = "Auto-announced variables",
            CheckOnClick = true
        };
        variableListBox.ItemCheck += VariableListBox_ItemCheck;
        variableListBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
        variableListBox.SelectedIndexChanged += (_, _) => { if (variableListBox.SelectedIndex >= 0) lastSelectedItemIndex = variableListBox.SelectedIndex; };

        Controls.Add(label);
        Controls.Add(variableListBox);
    }

    private void SetupAccessibility()
    {
        FormClosing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
            if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
        };
    }

    private void PopulateVariables()
    {
        // _populating suppresses the ItemCheck handler: SetItemChecked raises
        // ItemCheck, and without this guard each populate would fire a (no-op but
        // file-writing) Save per row — a storm on every construct AND every reopen.
        _populating = true;
        try
        {
            var disabledVars = SettingsManager.Current.HS787DisabledMonitorVariables;
            variableListBox.BeginUpdate();
            variableListBox.Items.Clear();
            for (int i = 0; i < _labels.Count; i++)
            {
                variableListBox.Items.Add(_labels[i]);
                variableListBox.SetItemChecked(i, !disabledVars.Contains(_keys[i])); // checked = announcing
            }
            variableListBox.EndUpdate();
        }
        finally
        {
            _populating = false;
        }
    }

    private void VariableListBox_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_populating) return;
        if (e.Index < 0 || e.Index >= _keys.Count) return;
        string key = _keys[e.Index];
        var settings = SettingsManager.Current;
        if (e.NewValue == CheckState.Checked)
            settings.HS787DisabledMonitorVariables.Remove(key);
        else if (!settings.HS787DisabledMonitorVariables.Contains(key))
            settings.HS787DisabledMonitorVariables.Add(key);
        SettingsManager.Save();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
}
