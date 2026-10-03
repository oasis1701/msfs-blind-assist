using System.Runtime.InteropServices;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Per-variable background-announcement manager for the Synaptic A220 (Ctrl+M).
///
/// Enumerates every auto-announced variable (UpdateFrequency.Continuous + IsAnnounced,
/// not ExcludeFromMonitorManager) from the definition dynamically — the iFly/Fenix
/// pattern — so every annunciator lamp and FG mode announcement can be muted
/// individually. Unchecked items go to UserSettings.A220DisabledMonitorVariables; the
/// generic announce gate in MainForm.OnSimVarUpdated honours it for lamp/combo vars,
/// and SynapticA220Definition honours it itself for the vars it self-announces from
/// inside ProcessSimVarUpdate (FG modes, APU, reversers, master caution/warning).
/// </summary>
public partial class A220MonitorManagerForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private CheckedListBox variableListBox = null!;
    private readonly List<string> _keys = new();    // parallel to variableListBox.Items
    private readonly List<string> _labels = new();
    private IntPtr previousWindow;
    private static int lastSelectedItemIndex;
    private bool _populating;

    public A220MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
    {
        foreach (var kv in variables)
        {
            if (kv.Value.UpdateFrequency != UpdateFrequency.Continuous || !kv.Value.IsAnnounced
                || kv.Value.ExcludeFromMonitorManager) continue;
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
        Text = "A220 Monitor Manager";
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
        _populating = true;
        try
        {
            var disabledVars = SettingsManager.Current.A220DisabledMonitorVariables;
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
            settings.A220DisabledMonitorVariables.Remove(key);
        else if (!settings.A220DisabledMonitorVariables.Contains(key))
            settings.A220DisabledMonitorVariables.Add(key);
        SettingsManager.Save();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
}
