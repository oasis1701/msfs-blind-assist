using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Autopilot mode toggle panel for the HorizonSim 787-9.
/// Opened via left-bracket + Ctrl+P.
/// Each button shows the current engaged state from the SimConnect cache and
/// sends the appropriate SimConnect event when pressed.
/// </summary>
public class HS787AutopilotWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly SimConnectManager _simConnect;
    private IntPtr _previousWindow;

    private Button _apButton = null!;
    private Button _atButton = null!;
    private Button _lnavButton = null!;
    private Button _vnavButton = null!;
    private Button _apprButton = null!;
    private Button _flchButton = null!;
    private Button _altHoldButton = null!;
    private Button _hdgHoldButton = null!;
    private Button _vsButton = null!;
    private Button _closeButton = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    public HS787AutopilotWindow(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _simConnect = simConnect;
        BuildForm();
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        RefreshButtonStates();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _apButton.Focus();
        _refreshTimer.Start();
    }

    // ------------------------------------------------------------------
    // Form construction
    // ------------------------------------------------------------------

    private void BuildForm()
    {
        Text = "787 Autopilot";
        Size = new Size(430, 305);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;

        const int col1 = 15;
        const int col2 = 215;
        const int btnW = 190;
        const int btnH = 38;
        const int rowH = 48;
        int row = 15;

        _apButton      = MakeBtn(col1, row,  btnW, btnH, "A/P",          "AP_MASTER",           0);
        _atButton      = MakeBtn(col2, row,  btnW, btnH, "Autothrottle", "AUTO_THROTTLE_ARM",    1, atMode: true);
        row += rowH;
        _lnavButton    = MakeBtn(col1, row,  btnW, btnH, "LNAV",         "AP_NAV1_HOLD",         2);
        _vnavButton    = MakeBtn(col2, row,  btnW, btnH, "VNAV",         "__VNAV_LVAR",          3);
        row += rowH;
        _apprButton    = MakeBtn(col1, row,  btnW, btnH, "Approach",     "AP_APR_HOLD",          4);
        _flchButton    = MakeBtn(col2, row,  btnW, btnH, "Level Change", "FLIGHT_LEVEL_CHANGE",  5);
        row += rowH;
        _altHoldButton = MakeBtn(col1, row,  btnW, btnH, "ALT Hold",     "AP_ALT_HOLD",          6);
        _hdgHoldButton = MakeBtn(col2, row,  btnW, btnH, "HDG Hold",     "AP_HDG_HOLD",          7);
        row += rowH;
        _vsButton      = MakeBtn(col1, row,  btnW, btnH, "V/S",          "AP_VS_HOLD",           8);
        row += rowH;

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(col1, row),
            Size = new Size(col2 + btnW - col1, btnH),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            TabIndex = 9
        };
        _closeButton.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            _apButton, _atButton, _lnavButton, _vnavButton, _apprButton,
            _flchButton, _altHoldButton, _hdgHoldButton, _vsButton, _closeButton
        });

        CancelButton = _closeButton;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshButtonStates();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
        };

        FormClosing += (_, _) =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };
    }

    private Button MakeBtn(int x, int y, int w, int h, string label, string action, int tab,
                           bool atMode = false)
    {
        var btn = new Button
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            TabIndex = tab,
            Tag = (label, action, atMode)
        };
        btn.Click += BtnClick;
        return btn;
    }

    // ------------------------------------------------------------------
    // State display
    // ------------------------------------------------------------------

    private void RefreshButtonStates()
    {
        UpdateBtn(_apButton,       "A/P",          GetOn("HS787_APMaster"));
        UpdateBtn(_atButton,       "A/T",          null, GetATStatus());
        UpdateBtn(_lnavButton,     "LNAV",         GetOn("HS787_LNAV"));
        UpdateBtn(_vnavButton,     "VNAV",         GetOn("HS787_VNAV"));
        UpdateBtn(_apprButton,     "Approach",     null, GetApprStatus());
        UpdateBtn(_flchButton,     "Level Change", GetOn("HS787_FLCH"));
        UpdateBtn(_altHoldButton,  "ALT Hold",     GetOn("HS787_ALTHold"));
        UpdateBtn(_hdgHoldButton,  "HDG Hold",     GetOn("HS787_HDGHold"));
        UpdateBtn(_vsButton,       "V/S",          GetOn("HS787_VS_Active"));
    }

    private string GetApprStatus()
    {
        bool gsActive = (_simConnect.GetCachedVariableValue("HS787_GS_Active") ?? 0) > 0;
        bool locActive = (_simConnect.GetCachedVariableValue("HS787_LOC")      ?? 0) > 0;
        bool gsArmed  = (_simConnect.GetCachedVariableValue("HS787_GS_Armed")  ?? 0) > 0;
        bool appHold  = (_simConnect.GetCachedVariableValue("HS787_APP")       ?? 0) > 0;

        if (gsActive)           return "GS Active";
        if (locActive)          return "LOC Active";
        if (gsArmed || appHold) return "Armed";
        return "OFF";
    }

    private bool GetOn(string varKey) =>
        (_simConnect.GetCachedVariableValue(varKey) ?? 0) > 0;

    private string GetATStatus()
    {
        double? v = _simConnect.GetCachedVariableValue("HS787_ATStatus");
        return (v ?? 0) > 0 ? "Armed" : "Off";
    }

    private static void UpdateBtn(Button btn, string label, bool? onOff, string? customStatus = null)
    {
        string status = customStatus ?? (onOff == true ? "ON" : "OFF");
        btn.Text = $"{label}: {status}";
        btn.AccessibleName = btn.Text;
    }

    // ------------------------------------------------------------------
    // Button click — send event then refresh after 300 ms
    // ------------------------------------------------------------------

    private void BtnClick(object? sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        var (label, action, _) = ((string, string, bool))btn.Tag!;

        if (action == "__VNAV_LVAR")
        {
            // VNAV — toggle the LVar directly (no K event available)
            double current = _simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0;
            _simConnect.SetLVar("XMLVAR_VNAVButtonValue", current > 0 ? 0 : 1);
        }
        else
        {
            _simConnect.SendEvent(action);
        }

        // Refresh button labels after sim has had time to update.
        // No explicit announcement here — screen readers announce the click,
        // and RefreshButtonStates will update the label so the new state is read on focus.
        Task.Delay(300).ContinueWith(_ =>
        {
            // try/catch closes the TOCTOU window: the form can be disposed between the
            // IsHandleCreated check and BeginInvoke, which would throw on this threadpool
            // continuation as an unobserved task exception.
            try
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(RefreshButtonStates);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
    }
}
