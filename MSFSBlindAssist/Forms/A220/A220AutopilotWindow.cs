using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Ctrl+P FCP window for the Synaptic A220 (FCUSetAutopilot). Engage cluster in the
/// iFly autopilot-window idiom: state-labelled buttons refreshed on a timer, no
/// value entry. Per the 2026-07-27 ruling, TOGA lives HERE (it is a required
/// before-takeoff action: on the ground it arms the TO/TO takeoff modes) and every
/// press is followed by an audible result — mode CHANGES are spoken by the def's FG
/// monitor; this window adds the deferred "no change" call, and TOGA always speaks.
/// </summary>
public class A220AutopilotWindow : Form
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly string[] TogaWatchKeys =
    {
        "A22X_AP_MASTER", "A22X_AT_MASTER", "A22X_FG_HEADING", "A22X_FG_LNAV",
        "A22X_FG_APPROACH", "A22X_FG_FLC", "A22X_FG_ALT", "A22X_FG_VNAV",
        "A22X_FG_VS", "A22X_FG_FPA"
    };

    private readonly SynapticA220Definition _def;
    private readonly SimConnect.SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly List<(Button btn, string label, Func<string> state)> _stateButtons = new();
    private IntPtr _previousWindow;
    private Button _closeButton = null!;

    public A220AutopilotWindow(SynapticA220Definition def, SimConnect.SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _sim = sim;
        _announcer = announcer;
        BuildForm();
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshButtonStates();
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
        if (Controls.Count > 0) Controls[0].Focus();
        _refreshTimer.Start();
    }

    private void BuildForm()
    {
        Text = "A220 Flight Control Panel";
        Size = new Size(440, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        const int col1 = 15, col2 = 220, btnW = 195, btnH = 38, rowH = 46;
        int row = 0, tab = 0;

        string OnOff(string key) => _def.CachedPublic(_sim, key) > 0.5 ? "Engaged" : "Off";

        void Mode(string label, int x, int y, string fireEvent, string? stateKey,
            string engaged, string off, bool pulseLvar = false, string? pulseName = null)
        {
            var btn = new Button
            {
                Location = new Point(x, y), Size = new Size(btnW, btnH), TabIndex = tab++, Text = label
            };
            if (stateKey != null)
                _stateButtons.Add((btn, label, () => OnOff(stateKey)));
            btn.Click += (_, _) =>
            {
                if (pulseLvar && pulseName != null) _def.PulsePanelFlag(_sim, pulseName);
                else _def.FireFcpEvent(_sim, fireEvent);
                if (stateKey != null)
                    _def.AnnounceModeResultPublic(_sim, _announcer, stateKey, engaged, off);
                RefreshSoon();
            };
            Controls.Add(btn);
        }

        int Y() => 15 + row * rowH;

        Mode("&Autopilot", col1, Y(), "AP_MASTER", "A22X_AP_MASTER", "Autopilot engaged", "Autopilot off");
        Mode("A/T A&rm", col2, Y(), "AUTO_THROTTLE_ARM", "A22X_AT_MASTER", "Autothrottle active", "Autothrottle off");
        row++;
        Mode("AP &Disconnect", col1, Y(), "AUTOPILOT_OFF", null, "", "");
        Mode("A/T Disconnec&t", col2, Y(), "AUTO_THROTTLE_DISCONNECT", null, "", "");
        row++;
        // FD buttons are MOMENTARY presses; their state comes from the FG's CommBus
        // block, NEVER from the "A22X L/R Flight Director" L:var (that is the press
        // pulse, so it reads 0 whatever the flight director is doing).
        var fdL = new Button { Location = new Point(col1, Y()), Size = new Size(btnW, btnH), TabIndex = tab++, Text = "FD Left" };
        _stateButtons.Add((fdL, "FD Left", () => _def.FlightDirectorStateText(left: true)));
        fdL.Click += (_, _) => { _def.ToggleFlightDirector(_sim, left: true); RefreshSoon(); };
        Controls.Add(fdL);
        var fdR = new Button { Location = new Point(col2, Y()), Size = new Size(btnW, btnH), TabIndex = tab++, Text = "FD Right" };
        _stateButtons.Add((fdR, "FD Right", () => _def.FlightDirectorStateText(left: false)));
        fdR.Click += (_, _) => { _def.ToggleFlightDirector(_sim, left: false); RefreshSoon(); };
        Controls.Add(fdR);
        row++;
        Mode("&HDG", col1, Y(), "AP_HDG_HOLD", "A22X_FG_HEADING", "HDG mode", "HDG mode off");
        Mode("&NAV", col2, Y(), "AP_NAV1_HOLD", "A22X_FG_LNAV", "NAV mode", "NAV mode off");
        row++;
        Mode("A&PPR", col1, Y(), "AP_APR_HOLD", "A22X_FG_APPROACH", "Approach mode armed", "Approach mode off");
        Mode("F&LC", col2, Y(), "FLIGHT_LEVEL_CHANGE", "A22X_FG_FLC", "FLC engaged", "FLC off");
        row++;
        Mode("AL&T Hold", col1, Y(), "AP_ALT_HOLD", "A22X_FG_ALT", "Altitude hold engaged", "Altitude hold off");
        Mode("&VS", col2, Y(), "AP_VS_HOLD", "A22X_FG_VS", "Vertical speed mode engaged", "Vertical speed mode off");
        row++;
        Mode("&FPA", col1, Y(), "AP_ATT_HOLD", "A22X_FG_FPA", "Flight path angle mode engaged", "Flight path angle mode off");
        Mode("V&NAV", col2, Y(), "", "A22X_FG_VNAV", "VNAV on", "VNAV off",
            pulseLvar: true, pulseName: "A22X FG VNAV Toggle");
        row++;
        Mode("Half &Bank", col1, Y(), "AP_MAX_BANK_ANGLE_SET", "A22X_FG_HALF_BANK", "Half bank on", "Half bank off");
        Mode("FG Source &Transfer", col2, Y(), "", null, "", "", pulseLvar: true, pulseName: "A22X FG Source Transfer");
        row++;

        var toga = new Button
        {
            Location = new Point(col1, Y()), Size = new Size(btnW + btnW + 10, btnH), TabIndex = tab++,
            Text = "TO&GA — Takeoff / Go-Around",
            AccessibleDescription = "Arms takeoff modes on the ground; go-around in flight. Also the only exit from a latched approach mode."
        };
        toga.Click += (_, _) => PressToga();
        Controls.Add(toga);
        row++;

        _closeButton = new Button
        {
            Location = new Point(col1, Y() + 6), Size = new Size(btnW + btnW + 10, 32), TabIndex = tab++,
            Text = "&Close"
        };
        _closeButton.Click += (_, _) => HideWindow();
        Controls.Add(_closeButton);
        CancelButton = _closeButton;

        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideWindow();
            }
        };
    }

    /// <summary>
    /// TOGA must NEVER be followed by silence (user ruling): compare the FG snapshot
    /// before/after and speak either the modes that changed or an explicit
    /// no-change report. The real FMA line ("TO TO") arrives with the P3 PFD scrape.
    /// </summary>
    private void PressToga()
    {
        var before = TogaWatchKeys.ToDictionary(k => k, k => _def.CachedPublic(_sim, k) > 0.5);
        _def.FireFcpEvent(_sim, "AUTO_THROTTLE_TO_GA");
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(1500);
            var changed = TogaWatchKeys
                .Where(k => (_def.CachedPublic(_sim, k) > 0.5) != before[k])
                .Select(k => k.Replace("A22X_FG_", "").Replace("A22X_", "").Replace('_', ' '))
                .ToList();
            _announcer.AnnounceImmediate(changed.Count > 0
                ? $"TOGA — changed: {string.Join(", ", changed)}"
                : "TOGA pressed — no flight guidance change detected. If on approach below 1500 feet, modes may be inhibited.");
        });
        RefreshSoon();
    }

    private void RefreshSoon()
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(600);
            try { if (IsHandleCreated) BeginInvoke(RefreshButtonStates); }
            catch (InvalidOperationException) { }
        });
    }

    private void RefreshButtonStates()
    {
        foreach (var (btn, label, state) in _stateButtons)
        {
            string text = $"{label.Replace("&", "")}: {state()}";
            if (btn.Text != text)
            {
                btn.Text = text;
                btn.AccessibleName = text;
            }
        }
    }

    private void HideWindow()
    {
        _refreshTimer.Stop();
        Hide();
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    protected override void Dispose(bool disposing)
    {
        // Close() is cancelled by the hide guard, so the timer must die here.
        if (disposing) _refreshTimer.Dispose();
        base.Dispose(disposing);
    }
}
