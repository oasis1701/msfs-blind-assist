using System.Runtime.InteropServices;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.MD11;

/// <summary>
/// Per-variable background-announcement manager for the TFDi MD-11 (Ctrl+M).
///
/// This matters more here than on any other aircraft in the app. The MD-11's six display units are
/// WASM-rendered and unreadable, so its 532 announcing annunciator lamps ARE the instrument panel —
/// a blind pilot has no other way to know a light came on. That is exactly why they all announce,
/// and equally why they need to be individually mutable: 532 lamps is a lot of voice in a busy
/// phase, and one chatty lamp can bury the one that matters.
///
/// Enumerates EVERY auto-announced variable (UpdateFrequency.Continuous + IsAnnounced) from the
/// aircraft definition dynamically — mirroring the Fenix / A380 / A32NX / HS787 / iFly managers —
/// so the list needs no maintenance as the definition grows. Unchecked items are written to
/// UserSettings.Md11DisabledMonitorVariables. MainForm.OnSimVarUpdated honours the list TWICE: via
/// the Suppressed-wrap (the MD-11 announces its composed flap read-out from INSIDE
/// ProcessSimVarUpdate, where the generic gate never runs — the HS787 pattern) and via the generic
/// gate (the annunciators, AP/APU state and everything else on the normal path).
/// </summary>
public partial class Md11MonitorManagerForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private CheckedListBox variableListBox = null!;
    private readonly List<string> _keys = new();    // parallel to variableListBox.Items
    private readonly List<string> _labels = new();
    private IntPtr previousWindow;
    private static int lastSelectedItemIndex;

    public Md11MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
    {
        foreach (var kv in variables)
        {
            if (kv.Value.UpdateFrequency != UpdateFrequency.Continuous || !kv.Value.IsAnnounced) continue;
            _keys.Add(kv.Key);
        }
        // Sorted by the SPOKEN name, not the node id: the pilot is looking for "Left fuel light",
        // not MD11_THR_L_FUEL_LT, and with ~530 entries the ordering is the only way to find one.
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
        Text = "MD-11 Monitor Manager";
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
        var disabledVars = SettingsManager.Current.Md11DisabledMonitorVariables;
        variableListBox.BeginUpdate();
        variableListBox.Items.Clear();
        for (int i = 0; i < _labels.Count; i++)
        {
            variableListBox.Items.Add(_labels[i]);
            variableListBox.SetItemChecked(i, !disabledVars.Contains(_keys[i])); // checked = announcing
        }
        variableListBox.EndUpdate();
    }

    private void VariableListBox_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _keys.Count) return;
        string key = _keys[e.Index];
        var settings = SettingsManager.Current;
        if (e.NewValue == CheckState.Checked)
            settings.Md11DisabledMonitorVariables.Remove(key);
        else if (!settings.Md11DisabledMonitorVariables.Contains(key))
            settings.Md11DisabledMonitorVariables.Add(key);
        // Save rebuilds the HashSet sidecar the announcement gate actually reads, so a mute takes
        // effect on the very next update rather than at the next launch.
        SettingsManager.Save();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
}
