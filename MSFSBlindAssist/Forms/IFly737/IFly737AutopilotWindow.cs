using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Forms.IFly737;

/// <summary>
/// Autopilot engage panel for the iFly 737 MAX8 (Ctrl+P, input mode) — the
/// Salty747AutopilotWindow shape. Carries the ENGAGE/DISENGAGE cluster: CMD A/B,
/// CWS A/B, both flight directors, autothrottle arm, the disengage bar, and the
/// momentary A/P + A/T disconnects. The per-value mode toggles (LNAV, VNAV, APP,
/// VOR LOC, LVL CHG, ALT HOLD, VS, N1, SPD/ALT INTV) live in the Ctrl+S/H/A/V
/// dialogs and on the MCP panel — this window is the engage cluster only.
///
/// Button labels show live state from the SDK snapshot (mode buttons use the
/// 0-5 switch+light encoding — value mod 3 &gt; 0 = light on = engaged); state
/// refreshes on a 500 ms timer and ~300 ms after each click. No explicit
/// announcements — the screen reader announces the click, and the label refresh
/// means the new state reads on focus (screen-reader rule).
/// </summary>
public class IFly737AutopilotWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IFlySdkClient _sdk;
    private readonly SimConnect.SimConnectManager? _simConnect;
    private readonly ScreenReaderAnnouncer? _announcer;
    private IntPtr _previousWindow;

    // Cockpit clickspot press/release trigger values for the engage buttons
    // (from iFly737Max_INTERIOR.xml: each button's mouse callback writes these
    // to L:VC_Automatic_Flight_trigger_VAL on LeftSingle / LeftRelease).
    private const string EngageTriggerLvar = "VC_Automatic_Flight_trigger_VAL";

    private Button _cmdAButton = null!;
    private Button _cmdBButton = null!;
    private Button _cwsAButton = null!;
    private Button _cwsBButton = null!;
    private Button _fdCaptButton = null!;
    private Button _fdFoButton = null!;
    private Button _atArmButton = null!;
    private Button _disengageBarButton = null!;
    private Button _closeButton = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    public IFly737AutopilotWindow(
        IFlySdkClient sdk,
        SimConnect.SimConnectManager? simConnect = null,
        ScreenReaderAnnouncer? announcer = null)
    {
        _sdk = sdk;
        _simConnect = simConnect;
        _announcer = announcer;
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
        _cmdAButton.Focus();
        _refreshTimer.Start();
    }

    private void BuildForm()
    {
        Text = "737 Autopilot";
        Size = new Size(430, 370);
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
        int tab = 0;

        _cmdAButton = MakeBtn(col1, row, btnW, btnH, tab++,
            () => EngageClick(IFlyKeyCommand.AUTOMATICFLIGHT_CMD_A,
                IFlySdkOffsets.CMD_A_Switch_Status, 7, 8, "CMD A",
                otherCmdOffset: IFlySdkOffsets.CMD_B_Switch_Status));
        _cmdBButton = MakeBtn(col2, row, btnW, btnH, tab++,
            () => EngageClick(IFlyKeyCommand.AUTOMATICFLIGHT_CMD_B,
                IFlySdkOffsets.CMD_B_Switch_Status, 9, 10, "CMD B",
                otherCmdOffset: IFlySdkOffsets.CMD_A_Switch_Status));
        row += rowH;
        _cwsAButton = MakeBtn(col1, row, btnW, btnH, tab++,
            () => EngageClick(IFlyKeyCommand.AUTOMATICFLIGHT_CWS_A,
                IFlySdkOffsets.CWS_A_Switch_Status, 37, 38, "CWS A"));
        _cwsBButton = MakeBtn(col2, row, btnW, btnH, tab++,
            () => EngageClick(IFlyKeyCommand.AUTOMATICFLIGHT_CWS_B,
                IFlySdkOffsets.CWS_B_Switch_Status, 39, 40, "CWS B"));
        row += rowH;
        // Flight directors + A/T arm are 2-position SET switches — write the opposite
        // of the current SDK state (the SET's Value2 matches the status encoding).
        _fdCaptButton = MakeBtn(col1, row, btnW, btnH, tab++,
            () => _sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_LEFT_FD_SET,
                State(IFlySdkOffsets.FD_1_Switch_Status) == 0 ? 1 : 0));
        _fdFoButton = MakeBtn(col2, row, btnW, btnH, tab++,
            () => _sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_RIGHT_FD_SET,
                State(IFlySdkOffsets.FD_2_Switch_Status) == 0 ? 1 : 0));
        row += rowH;
        _atArmButton = MakeBtn(col1, row, btnW, btnH, tab++,
            () => _sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_AUTOTHROTTLE_ARM_SET,
                State(IFlySdkOffsets.AT_Switch_Status) == 0 ? 1 : 0));
        // Disengage bar: status 0 = pulled DOWN (disengaged), 1 = lifted UP (normal);
        // the SET's Value2 is INVERTED vs the status (0 = up, 1 = down) — so sending
        // Value2 = current status toggles the bar.
        _disengageBarButton = MakeBtn(col2, row, btnW, btnH, tab++,
            () => _sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_AUTOPILOT_DISENGAGE_BAR_SET,
                State(IFlySdkOffsets.DISENGAGE_Bar_Switch_Status)));
        row += rowH;
        // Momentary disconnects — always disconnect, distinct from the CMD toggles
        // which would RE-engage if pressed while off.
        var apDiscButton = new Button
        {
            Text = "A/P Disconnect",
            Location = new Point(col1, row),
            Size = new Size(btnW, btnH),
            AccessibleName = "Autopilot Disconnect",
            TabIndex = tab++,
        };
        apDiscButton.Click += (_, _) =>
        {
            _sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_AUTOPILOT_DISCONNECT_1);
            RefreshSoon();
        };
        var atDiscButton = new Button
        {
            Text = "A/T Disconnect",
            Location = new Point(col2, row),
            Size = new Size(btnW, btnH),
            AccessibleName = "Autothrottle Disconnect",
            TabIndex = tab++,
        };
        atDiscButton.Click += (_, _) =>
        {
            _sdk.SendCommand(IFlyKeyCommand.AUTOMATICFLIGHT_AUTOTHROTTLE_DISCONNECT_1);
            RefreshSoon();
        };
        row += rowH;

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(col1, row),
            Size = new Size(col2 + btnW - col1, btnH),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            TabIndex = tab,
        };
        _closeButton.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            _cmdAButton, _cmdBButton, _cwsAButton, _cwsBButton,
            _fdCaptButton, _fdFoButton, _atArmButton, _disengageBarButton,
            apDiscButton, atDiscButton, _closeButton,
        });

        CancelButton = _closeButton;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshButtonStates();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); }
        };

        // Hide-on-close so the cached instance survives reopen; the refresh timer
        // stops while hidden and restarts in ShowForm.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            _refreshTimer.Stop();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };
    }

    private Button MakeBtn(int x, int y, int w, int h, int tab, Action fire)
    {
        var btn = new Button
        {
            Location = new Point(x, y),
            Size = new Size(w, h),
            TabIndex = tab,
        };
        btn.Click += (_, _) =>
        {
            fire();
            RefreshSoon();
        };
        return btn;
    }

    private void RefreshSoon()
    {
        Task.Delay(300).ContinueWith(_ =>
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(RefreshButtonStates);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
    }

    /// <summary>
    /// Verified engage-button click (CMD A/B, CWS A/B). Fires the SDK Click
    /// command, then checks ~700 ms later (3 SDK poll cycles) whether the
    /// button light actually changed. If it didn't, replays the cockpit
    /// clickspot's OWN actuation — the press/release trigger-L:var sequence
    /// from the model XML — which bypasses the plugin's Click handling
    /// entirely, and re-checks. Only a total no-response is announced
    /// (error-condition rule); a successful change reads from the refreshed
    /// button label as usual. Live report 2026-07: CMD B "doesn't turn on"
    /// during an autoland attempt — same class as the GRD PWR press-cycle bug.
    /// </summary>
    private async void EngageClick(
        IFlyKeyCommand cmd, int statusOffset, int pressTrigger, int releaseTrigger,
        string label, int otherCmdOffset = -1)
    {
        bool before = Lit(statusOffset);
        if (!_sdk.SendCommand(cmd))
        {
            _announcer?.AnnounceImmediate("iFly plugin not responding.");
            return;
        }
        await Task.Delay(700);
        if (IsDisposed) return;
        RefreshButtonStates();
        if (Lit(statusOffset) != before) return; // took effect — label refresh covers it

        if (_simConnect != null)
        {
            _simConnect.SetLVar(EngageTriggerLvar, pressTrigger);
            await Task.Delay(150);
            if (IsDisposed) return;
            _simConnect.SetLVar(EngageTriggerLvar, releaseTrigger);
            await Task.Delay(700);
            if (IsDisposed) return;
            RefreshButtonStates();
            if (Lit(statusOffset) != before) return;
        }

        string msg = before ? $"{label} did not disengage." : $"{label} did not engage.";
        if (!before && otherCmdOffset >= 0 && Lit(otherCmdOffset))
            msg += " For dual channel, APP mode must be armed with the localizer captured before the second autopilot can engage.";
        _announcer?.AnnounceImmediate(msg);
    }

    private int State(int offset) => _sdk.Snapshot?.ByteAt(offset) ?? 0;

    /// <summary>Mode-button light truth: 0-5 switch+light encoding, mod 3 &gt; 0 = lit.</summary>
    private bool Lit(int offset) => State(offset) % 3 > 0;

    private void RefreshButtonStates()
    {
        UpdateBtn(_cmdAButton, "CMD A", Lit(IFlySdkOffsets.CMD_A_Switch_Status) ? "Engaged" : "Off");
        UpdateBtn(_cmdBButton, "CMD B", Lit(IFlySdkOffsets.CMD_B_Switch_Status) ? "Engaged" : "Off");
        UpdateBtn(_cwsAButton, "CWS A", Lit(IFlySdkOffsets.CWS_A_Switch_Status) ? "Engaged" : "Off");
        UpdateBtn(_cwsBButton, "CWS B", Lit(IFlySdkOffsets.CWS_B_Switch_Status) ? "Engaged" : "Off");
        UpdateBtn(_fdCaptButton, "F/D Captain", State(IFlySdkOffsets.FD_1_Switch_Status) != 0 ? "On" : "Off");
        UpdateBtn(_fdFoButton, "F/D First Officer", State(IFlySdkOffsets.FD_2_Switch_Status) != 0 ? "On" : "Off");
        UpdateBtn(_atArmButton, "A/T Arm", State(IFlySdkOffsets.AT_Switch_Status) != 0 ? "Armed" : "Off");
        UpdateBtn(_disengageBarButton, "Disengage Bar",
            State(IFlySdkOffsets.DISENGAGE_Bar_Switch_Status) != 0 ? "Up, normal" : "Down, disengaged");
    }

    private static void UpdateBtn(Button btn, string label, string status)
    {
        btn.Text = $"{label}: {status}";
        btn.AccessibleName = btn.Text;
    }

    protected override void Dispose(bool disposing)
    {
        // Hide-on-close form: teardown must live here — Form.Dispose() skips
        // OnFormClosed and Close() is cancelled by the hide guard (RMP precedent).
        if (disposing)
            _refreshTimer?.Dispose();
        base.Dispose(disposing);
    }
}
