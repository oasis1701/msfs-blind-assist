using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

public partial class FenixMonitorManagerForm : Form
{
    // Windows API declarations for focus management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // Static list of manageable variables - easy to expand later
    private static readonly (string Key, string DisplayName)[] ManageableVariables = new[]
    {
        ("I_MIP_MASTER_WARNING_CAPT", "Master Warning Captain Fault"),
        ("I_MIP_MASTER_WARNING_CAPT_L", "Master Warning Captain Lower"),
        ("I_MIP_MASTER_WARNING_FO", "Master Warning FO Fault"),
        ("I_MIP_MASTER_WARNING_FO_L", "Master Warning FO Lower"),
        ("I_MIP_MASTER_CAUTION_CAPT", "Master Caution Captain Fault"),
        ("I_MIP_MASTER_CAUTION_CAPT_L", "Master Caution Captain Lower"),
        ("I_MIP_MASTER_CAUTION_FO", "Master Caution FO Fault"),
        ("I_MIP_MASTER_CAUTION_FO_L", "Master Caution FO Lower"),
        ("I_ECAM_CLR_LEFT", "ECAM CLR Left"),
        ("I_ECAM_CLR_RIGHT", "ECAM CLR Right"),
    };

    private CheckedListBox variableListBox = null!;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow;

    // Static field to persist focus position across show/hide cycles
    private static int lastSelectedItemIndex = 0;

    public FenixMonitorManagerForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;
        InitializeComponent();
        SetupAccessibility();
        PopulateVariables();
    }

    public void ShowForm()
    {
        // Capture the current foreground window before showing
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false; // Flash to bring to front

        // Restore focus to last position
        if (variableListBox.Items.Count > 0)
        {
            int itemIndex = Math.Min(lastSelectedItemIndex, variableListBox.Items.Count - 1);
            variableListBox.SelectedIndex = itemIndex;
        }

        variableListBox.Focus();
    }

    private void InitializeComponent()
    {
        Text = "Fenix Monitor Manager";
        Size = new Size(450, 350);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;

        // Create label
        var label = new Label
        {
            Text = "Uncheck variables to disable monitoring:",
            Location = new Point(10, 10),
            Size = new Size(420, 20),
            AccessibleName = "Instructions"
        };

        // Create CheckedListBox for variables
        variableListBox = new CheckedListBox
        {
            Location = new Point(10, 35),
            Size = new Size(415, 260),
            TabIndex = 0,
            AccessibleName = "Monitored Variables",
            CheckOnClick = true
        };

        // Event handlers
        variableListBox.ItemCheck += VariableListBox_ItemCheck;
        variableListBox.KeyDown += VariableListBox_KeyDown;
        variableListBox.SelectedIndexChanged += VariableListBox_SelectedIndexChanged;

        Controls.Add(label);
        Controls.Add(variableListBox);
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
            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        };
    }

    private void PopulateVariables()
    {
        var disabledVars = SettingsManager.Current.FenixDisabledMonitorVariables;

        variableListBox.BeginUpdate();
        variableListBox.Items.Clear();

        foreach (var (key, displayName) in ManageableVariables)
        {
            int index = variableListBox.Items.Add(displayName);
            // Checkbox checked = monitoring ENABLED (not in disabled list)
            bool isEnabled = !disabledVars.Contains(key);
            variableListBox.SetItemChecked(index, isEnabled);
        }

        variableListBox.EndUpdate();
    }

    private void VariableListBox_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (e.Index < 0 || e.Index >= ManageableVariables.Length)
            return;

        var variableKey = ManageableVariables[e.Index].Key;
        var settings = SettingsManager.Current;

        // Checked = enabled (remove from disabled list)
        // Unchecked = disabled (add to disabled list)
        if (e.NewValue == CheckState.Checked)
        {
            settings.FenixDisabledMonitorVariables.Remove(variableKey);
        }
        else
        {
            if (!settings.FenixDisabledMonitorVariables.Contains(variableKey))
            {
                settings.FenixDisabledMonitorVariables.Add(variableKey);
            }
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
        {
            lastSelectedItemIndex = variableListBox.SelectedIndex;
        }
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
