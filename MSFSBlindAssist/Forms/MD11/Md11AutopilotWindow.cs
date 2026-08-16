using System.Globalization;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.MD11;

/// <summary>
/// The MD-11's Flight Control Panel — the glareshield autoflight panel, this aircraft's MCP
/// equivalent (Ctrl+P in input mode).
///
/// Every label here comes from TFDi's OWN tooltips in Glareshield.xml, not from the map
/// generator's derived guesses: "FTM" is the aircraft's name for the Altitude Unit Select
/// (feet/metres), which reads like "flight test mode" if you go by the node id alone.
///
/// The four FCP windows each carry a VALUE and, independently, a MODE — speed is IAS or Mach,
/// heading is heading or track, vertical is V/S or FPA, altitude is feet or metres. Both halves
/// are spoken together, because "selected speed 250" means something different in Mach mode and a
/// blind pilot has no window to glance at. The mode vars (MD11_AP_IAS_MACH, MD11_AP_HDG_TRK,
/// MD11_AP_VS_FPA, MD11_AP_FT_M) are the aircraft's own, not inferred.
///
/// Push and pull are real, separate actions on the SPD/HDG/ALT knobs (the map's knob_pp kind,
/// with distinct PUSH_/PULL_ event pairs), so they get their own buttons. Their exact autoflight
/// meaning on this airframe is NOT asserted here — the pilot gets the same two actions a sighted
/// pilot has, labelled as what they physically are.
/// </summary>
public class Md11AutopilotWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly TFDiMD11Definition _def;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr _previousWindow;

    private Label _values = null!;
    private ComboBox _bankLimit = null!;
    private ComboBox _dialAFlap = null!;
    private System.Windows.Forms.Timer _refresh = null!;

    private bool _populating;

    // ---- node ids, all verified present in the embedded map by Md11AutopilotWindowTests ----
    private const string Autoflight = "MD11_CGS_AUTOFLIGHT_BT";
    private const string Prof = "MD11_CGS_PROF_BT";
    private const string Nav = "MD11_CGS_NAV_BT";
    private const string ApprLand = "MD11_CGS_APPRLAND_BT";
    private const string FmsSpd = "MD11_CGS_FMSSPD_BT";
    private const string IasMachSel = "MD11_CGS_IASMACH_BT";
    private const string HdgTrkSel = "MD11_CGS_HDGTRK_BT";
    private const string AltUnitSel = "MD11_CGS_FTM_BT";
    private const string VsFpaSel = "MD11_CGS_VS_FPA_BT";
    // The knobs live on Md11Fcp so the panel and the type-in dialogs cannot drift apart.
    private const string SpdKnob = Md11Fcp.SpeedKnob;
    private const string HdgKnob = Md11Fcp.HeadingKnob;
    private const string AltKnob = Md11Fcp.AltitudeKnob;
    private const string VsKnob = Md11Fcp.VerticalSpeedKnob;   // wheel only — engages V/S / FPA
    private const string BankLimitKnob = "MD11_CGS_HDG_BASE_KB";

    // Go Around lives on the throttle levers. The glareshield clickspot GA_BT_ALT fires the SAME
    // event ids (77851/77852), so it is the same button reached from a second place — offering
    // both would be two buttons that do one thing.
    private const string GoAround = "MD11_THR_GA_BT";
    private const string AtsDiscL = "MD11_THR_L_ATS_BT";
    private const string AtsDiscR = "MD11_THR_R_ATS_BT";

    public Md11AutopilotWindow(TFDiMD11Definition def, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _sim = sim;
        _announcer = announcer;
        BuildForm();
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildForm()
    {
        SuspendLayout();

        Text = "MD-11 Flight Control Panel";
        ClientSize = new Size(560, 640);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AccessibleName = "MD-11 Flight Control Panel";
        AccessibleDescription = "Autoflight modes, selected values, and the flight control panel knobs.";

        var y = 10;

        _values = new Label
        {
            Location = new Point(10, y),
            Size = new Size(540, 60),
            AccessibleName = "Selected values",
            AccessibleDescription = "The four FCP windows and the autoflight state.",
        };
        y += 68;

        // ---- Engage buttons ----
        y = AddSection("Autoflight", y);
        y = AddButtonRow(y, ("&Autoflight", Autoflight), ("&PROF", Prof), ("&NAV", Nav));
        y = AddButtonRow(y, ("A&pproach / Land", ApprLand), ("&FMS Speed", FmsSpd), ("&Go Around", GoAround));

        // ---- Mode selects ----
        y = AddSection("Mode select", y);
        y = AddButtonRow(y, ("&IAS / Mach", IasMachSel), ("&Heading / Track", HdgTrkSel));
        y = AddButtonRow(y, ("&VS / FPA", VsFpaSel), ("Altitude &Unit", AltUnitSel));

        // ---- Vertical speed wheel ----
        // The MD-11 has no "engage V/S" button: rotating the V/S / FPA wheel is what engages the
        // pitch mode. So the wheel is the way to ACTIVATE and adjust V/S (or FPA) — one detent per
        // click. (Typing a value via Ctrl+V also engages it; see SetVerticalSpeedEngaged.)
        y = AddSection("Vertical speed wheel (engages V/S / FPA)", y);
        y = AddWheelRow(y, "Vertical speed", VsKnob);

        // ---- Knob push/pull ----
        y = AddSection("Knobs — push and pull", y);
        y = AddPushPullRow(y, "Speed", SpdKnob);
        y = AddPushPullRow(y, "Heading", HdgKnob);
        y = AddPushPullRow(y, "Altitude", AltKnob);

        // ---- Autothrust disconnect ----
        y = AddSection("Autothrust", y);
        y = AddButtonRow(y, ("Disconnect &Left", AtsDiscL), ("Disconnect &Right", AtsDiscR));

        // ---- Combos ----
        y = AddSection("Selectors", y);

        AddLabel("Bank angle limiter", 10, y + 4);
        _bankLimit = NewCombo(new Point(170, y), "Bank angle limiter",
            "Autopilot bank angle limit: Auto, or a fixed limit from 5 to 25 degrees.");
        _bankLimit.SelectedIndexChanged += (s, e) =>
        {
            if (_populating) return;
            _def.SetControl(BankLimitKnob, _bankLimit.SelectedIndex, _sim, _announcer);
        };
        y += 34;

        // The Dial-A-Flap wheel lives on the pedestal, not the FCP — it is here because the
        // take-off flap angle is set while working the autoflight panel, and making the pilot
        // leave this window to reach a single value is friction with no upside.
        AddLabel("Dial-A-Flap (take-off angle)", 10, y + 4);
        _dialAFlap = NewCombo(new Point(170, y), "Dial-A-Flap take-off angle",
            "The take-off flap angle the Dial-A-Flap detent extends to, 10 to 25 degrees.");
        // Ordered by angle, explicitly: DialAFlapChoices returns a Dictionary, whose enumeration
        // order is an implementation detail. A combo a blind pilot arrows through must run
        // 10, 11, 12 … 25 — an arbitrary order would be unusable even though every entry is present.
        foreach (var kvp in _def.DialAFlapChoices().OrderBy(k => k.Key))
            _dialAFlap.Items.Add(new ComboItem(kvp.Value, kvp.Key));
        _dialAFlap.SelectedIndexChanged += (s, e) =>
        {
            if (_populating || _dialAFlap.SelectedItem is not ComboItem item) return;
            _def.SetControl(Md11FlapSystem.DialKey, item.Value, _sim, _announcer);
        };
        y += 42;

        var close = new Button
        {
            Text = "&Close",
            Location = new Point(10, y),
            Size = new Size(100, 30),
        };
        close.Click += (s, e) => Hide();
        Controls.Add(close);

        // Auto/5/10/15/20/25 — the aircraft's own value map for the limiter knob.
        _bankLimit.Items.AddRange(new object[] { "Auto", "5 degrees", "10 degrees", "15 degrees", "20 degrees", "25 degrees" });

        Controls.Add(_values);

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };

        _refresh = new System.Windows.Forms.Timer { Interval = 500 };
        _refresh.Tick += (s, e) => RefreshValues();

        ResumeLayout(false);
    }

    private sealed record ComboItem(string Text, double Value)
    {
        public override string ToString() => Text;
    }

    private int AddSection(string title, int y)
    {
        var l = new Label
        {
            Text = title,
            Location = new Point(10, y),
            Size = new Size(540, 18),
            Font = new Font(Font, FontStyle.Bold),
        };
        Controls.Add(l);
        return y + 22;
    }

    private void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(160, 20) });
    }

    private ComboBox NewCombo(Point at, string name, string description)
    {
        var c = new ComboBox
        {
            Location = at,
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = name,
            AccessibleDescription = description,
        };
        Controls.Add(c);
        return c;
    }

    private int AddButtonRow(int y, params (string Label, string Node)[] items)
    {
        var x = 10;
        foreach (var (label, node) in items)
        {
            var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(170, 30) };
            var captured = node;
            var name = label.Replace("&", "");
            b.Click += (s, e) => Press(captured, name);
            Controls.Add(b);
            x += 178;
        }
        return y + 34;
    }

    private int AddPushPullRow(int y, string name, string node)
    {
        AddLabel(name, 10, y + 5);

        var push = new Button
        {
            Text = $"Push", Location = new Point(170, y), Size = new Size(100, 30),
            AccessibleName = $"{name} knob push",
        };
        push.Click += (s, e) => PressEvents(node, "PUSH_DOWN", "PUSH_UP", $"{name} push");

        var pull = new Button
        {
            Text = $"Pull", Location = new Point(278, y), Size = new Size(100, 30),
            AccessibleName = $"{name} knob pull",
        };
        pull.Click += (s, e) => PressEvents(node, "PULL_DOWN", "PULL_UP", $"{name} pull");

        Controls.Add(push);
        Controls.Add(pull);
        return y + 34;
    }

    /// <summary>
    /// A knob that turns but does not push or pull — the V/S / FPA wheel. Each click fires ONE
    /// wheel event (there is no DOWN/UP pair), which on this aircraft is how the pitch mode is
    /// engaged and its rate adjusted.
    /// </summary>
    private int AddWheelRow(int y, string name, string node)
    {
        AddLabel(name, 10, y + 5);

        var up = new Button
        {
            Text = "Wheel &up", Location = new Point(170, y), Size = new Size(100, 30),
            AccessibleName = $"{name} wheel up",
        };
        up.Click += (s, e) => FireEvent(node, "WHEEL_UP", $"{name} wheel up");

        var down = new Button
        {
            Text = "Wheel &down", Location = new Point(278, y), Size = new Size(100, 30),
            AccessibleName = $"{name} wheel down",
        };
        down.Click += (s, e) => FireEvent(node, "WHEEL_DOWN", $"{name} wheel down");

        Controls.Add(up);
        Controls.Add(down);
        return y + 34;
    }

    // ---------------------------------------------------------------------------------
    // Actuation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A press is silent on success — the screen reader already announced the button — but a press
    /// that could not be delivered MUST speak. On an aircraft with no readable FCP window, a
    /// dropped press is indistinguishable from an accepted one.
    /// </summary>
    private void Press(string node, string name)
    {
        if (!_def.PressControl(node)) _announcer.Announce($"{name} unavailable");
    }

    private void PressEvents(string node, string down, string up, string name)
    {
        if (!_def.PressControlEvents(node, down, up)) _announcer.Announce($"{name} unavailable");
    }

    private void FireEvent(string node, string eventName, string name)
    {
        if (!_def.FireControlEvent(node, eventName)) _announcer.Announce($"{name} unavailable");
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The four FCP windows, each with its own mode, plus autopilot and autothrust state.
    ///
    /// TFDi publish -999 / -9999 for a window that is showing dashes; those are rendered as
    /// "dashed" rather than read out as a large negative number.
    /// </summary>
    private void RefreshValues()
    {
        var spd = Val("MD11_AFS_SPD");
        var hdg = Val("MD11_AFS_HDG");
        var alt = Val("MD11_AFS_ALT");
        var vs = Val("MD11_AFS_VS");

        var mach = Val("MD11_AP_IAS_MACH") > 0.5;
        var trk = Val("MD11_AP_HDG_TRK") > 0.5;
        var fpa = Val("MD11_AP_VS_FPA") > 0.5;
        var metres = Val("MD11_AP_FT_M") > 0.5;

        var speed = Dashed(spd)
            ? "Speed dashed"
            : mach
                ? $"Mach {spd.ToString("0.00", CultureInfo.InvariantCulture)}"
                : $"Speed {spd.ToString("0", CultureInfo.InvariantCulture)} knots";

        var heading = Dashed(hdg)
            ? (trk ? "Track dashed" : "Heading dashed")
            : $"{(trk ? "Track" : "Heading")} {hdg.ToString("0", CultureInfo.InvariantCulture)}";

        var altitude = Dashed(alt)
            ? "Altitude dashed"
            : $"Altitude {alt.ToString("0", CultureInfo.InvariantCulture)} {(metres ? "metres" : "feet")}";

        var vertical = Dashed(vs)
            ? (fpa ? "FPA dashed" : "Vertical speed dashed")
            : fpa
                ? $"FPA {vs.ToString("0.0", CultureInfo.InvariantCulture)} degrees"
                : $"Vertical speed {vs.ToString("0", CultureInfo.InvariantCulture)} feet per minute";

        var ap = Val("MD11_AP_STATE") switch
        {
            >= 2.5 => "AP 1 and 2",
            >= 1.5 => "AP 2",
            >= 0.5 => "AP 1",
            _ => "AP off",
        };

        _values.Text = $"{speed}   {heading}{Environment.NewLine}{altitude}   {vertical}{Environment.NewLine}{ap}";
    }

    /// <summary>TFDi's "this window is showing dashes" sentinels, per their Variables doc.</summary>
    private static bool Dashed(double v) => v <= -999;

    private double Val(string key) => _sim.GetCachedVariableValue(key) ?? 0;

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();

        // Snap the combos to the aircraft's current state without firing a write back at it.
        _populating = true;
        var bank = _sim.GetCachedVariableValue(BankLimitKnob);
        if (bank is >= 0 && bank < _bankLimit.Items.Count) _bankLimit.SelectedIndex = (int)bank.Value;
        _populating = false;

        RefreshValues();
        _refresh.Start();

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // Nothing to read while hidden — a 500 ms tick against a hidden window is pure waste.
        if (!Visible) _refresh?.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        // Close() is cancelled by the hide-on-close guard, so OnFormClosed never runs; the timer
        // has to be torn down here or it outlives the aircraft switch.
        if (disposing)
        {
            _refresh?.Stop();
            _refresh?.Dispose();
        }
        base.Dispose(disposing);
    }
}
