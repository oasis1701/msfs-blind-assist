using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.Citation680;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Citation680;

/// <summary>
/// The GMC 7200 as a grid of buttons (the Ctrl+P panel). Every button shows its live state from
/// the SimConnect cache and routes its press through the definition's own write router, so the
/// window and the panel can never disagree on a transport. No announcement on press: the
/// screen reader speaks the button, and the label refreshes so the new state reads on focus.
/// </summary>
public class C680AutopilotWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly SkywardC680Definition _def;
    private readonly SimConnectManager _sc;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr _previousWindow;
    private readonly List<(Button button, string label, string key, bool toggle)> _buttons = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 500 };

    public C680AutopilotWindow(SkywardC680Definition def, SimConnectManager sc, ScreenReaderAnnouncer announcer)
    {
        _def = def; _sc = sc; _announcer = announcer;
        _previousWindow = GetForegroundWindow();
        Text = "Citation Sovereign+ Autopilot";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = true; KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        var rows = new (string label, string key, bool toggle)[]
        {
            ("AP", "C680_AP_MASTER", true), ("YD", "C680_AP_YD", true),
            ("FD Left", "C680_AP_FD_L", true), ("FD Right", "C680_AP_FD_R", true),
            ("HDG", "C680_AP_HDG", true), ("NAV", "C680_AP_NAV", true),
            ("APR", "C680_AP_APR", true), ("BC", "C680_AP_BC", true),
            ("ALT", "C680_AP_ALT", true), ("VS", "C680_AP_VS", true),
            ("FLC", "C680_AP_FLC", true), ("VNAV", "C680_AP_VNAV", false),
            ("TO/GA", "C680_TOGA", false), ("AT", "C680_AT_ARM", false),
            ("AT Disconnect", "C680_AT_DISC", false), ("AP Disconnect", "C680_AP_DISC", false),
            ("Speed Source", "C680_AP_SPD_MANUAL", true),
        };
        const int col1 = 15, col2 = 215, w = 190, h = 38, rowH = 48;
        int y = 15, tab = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, key, toggle) = rows[i];
            var b = new Button { Text = label, AccessibleName = label, Location = new Point(i % 2 == 0 ? col1 : col2, y), Size = new Size(w, h), TabIndex = tab++ };
            b.Click += (_, _) => Press(key, toggle);
            _buttons.Add((b, label, key, toggle));
            Controls.Add(b);
            if (i % 2 == 1) y += rowH;
        }
        var close = new Button { Text = "Close", AccessibleName = "Close", Location = new Point(col1, y), Size = new Size(col2 + w - col1, h), TabIndex = tab };
        close.Click += (_, _) => Close();
        Controls.Add(close);
        CancelButton = close;
        ClientSize = new Size(col2 + w + 15, y + h + 15);

        _refresh.Tick += (_, _) => RefreshStates();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); } };
        Shown += (_, _) => { RefreshStates(); _buttons[0].button.Focus(); _refresh.Start(); };
        FormClosing += (_, _) => { _refresh.Stop(); _refresh.Dispose(); if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow); };
    }

    private void Press(string key, bool toggle)
    {
        double value = 1;
        if (toggle) value = (_sc.GetCachedVariableValue(key) ?? 0) > 0.5 ? 0 : 1;
        var vars = _def.GetVariables();
        if (vars.TryGetValue(key, out var def)) _def.HandleUIVariableSet(key, value, def, _sc, _announcer);
        Task.Delay(400).ContinueWith(_ =>
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(RefreshStates); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
    }

    private void RefreshStates()
    {
        foreach (var (button, label, key, toggle) in _buttons)
        {
            string text = label;
            if (toggle)
            {
                var v = _sc.GetCachedVariableValue(key);
                text = v == null ? $"{label}: --" : $"{label}: {(v > 0.5 ? "ON" : "OFF")}";
            }
            else if (key == "C680_AT_ARM")
            {
                var s = _sc.GetCachedVariableValue("C680_AT_STATUS");
                text = "AT: " + (s == null ? "--" : (int)Math.Round(s.Value) switch { 1 => "Disconnected", 2 => "Armed", 3 => "On", _ => "Off" });
            }
            if (button.Text != text) { button.Text = text; button.AccessibleName = text; }
        }
    }
}
