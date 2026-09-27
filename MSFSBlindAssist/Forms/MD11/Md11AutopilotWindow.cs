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
///
/// STATE. A status list (first in the tab order) reads the four windows, the autopilot and the
/// autothrottle; the buttons whose state the aircraft exports carry it in their caption —
/// "Autoflight: AP 1, ATS on", "NAV: engaged", "IAS / Mach: Mach" — refreshed twice a second and
/// only rewritten on change. NAV and FMS Speed come from the FCP's own dashed windows (TFDi
/// document them as the engagement cue); PROF and Approach / Land stay plain because their
/// engagement exists only on the FMA, which the aircraft does not export. Every word comes from
/// Md11AutoflightState, shared with the dialogs and the hotkey read-outs.
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

    /// <summary>
    /// Marks a combo pick in MainForm's echo window, (key, value), before it is written — so the
    /// delivered change is not spoken over the screen reader's own read-out of the pick. The
    /// window writes through <c>SetControl</c>, past the panel path that marks it; null leaves the
    /// picks unmarked.
    /// </summary>
    private readonly Action<string, double>? _suppressEcho;
    private IntPtr _previousWindow;

    private ListBox _status = null!;
    private Button _autoflight = null!;
    /// <summary>Buttons whose caption carries live state ("Autoflight: AP 1, ATS on"), refreshed on the timer.</summary>
    private readonly List<(Button Button, string Label, Func<string> State)> _liveCaptions = new();
    private ComboBox _bankLimit = null!;
    private ComboBox _dialAFlap = null!;
    private System.Windows.Forms.Timer _refresh = null!;

    private bool _populating;

    // ---- node ids, all verified present in the embedded map by Md11FlightControlPanelTests ----
    // Captions (and their Alt accelerators) live on Md11FcpButtons, pinned unique by Md11FcpButtonsTests.
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

    public Md11AutopilotWindow(TFDiMD11Definition def, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        Action<string, double>? suppressEcho = null)
    {
        _def = def;
        _sim = sim;
        _announcer = announcer;
        _suppressEcho = suppressEcho;
        BuildForm();
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildForm()
    {
        SuspendLayout();

        Text = "MD-11 Flight Control Panel";
        ClientSize = new Size(560, 680);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        // Escape closes the window, as on every other autoflight window in this app (the FBW FCU
        // base, Fenix, HS787, A380). Close() runs the hide-on-close handler below, which also
        // hands focus back to the window the pilot came from. A combo with its list dropped
        // keeps Escape for itself, so a pilot backing out of a selection does not lose the window.
        KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            if (ActiveControl is ComboBox { DroppedDown: true }) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Close();
        };
        AccessibleName = "MD-11 Flight Control Panel";
        AccessibleDescription = "Autoflight modes, selected values, and the flight control panel knobs.";

        var y = 10;

        // The FCP windows and the engagement state, as a read-only list the pilot can Tab to and
        // arrow through. It replaced a Label, which is not in the tab order — the reason the
        // window "had no state indications": the state was there, unreachable.
        _status = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(540, 100),
            IntegralHeight = false,
            SelectionMode = SelectionMode.One,
            TabStop = true,
            AccessibleName = "Flight control panel status",
            AccessibleDescription = "The four FCP windows with their modes, the autopilot and the autothrottle; updates live.",
        };
        Controls.Add(_status);
        y += 108;

        // ---- Engage buttons ----
        // State where the aircraft exports one (Md11AutoflightState): AUTO FLIGHT from
        // MD11_AP_STATE + MD11_ATS_STATE, NAV and FMS SPD from the FCP's own dashed windows.
        // PROF and APPR/LAND have no readable state (their engagement is FMA-only) and Go Around
        // is a one-shot, so those stay plain.
        y = AddSection("Autoflight", y);
        y = AddButtonRow(y,
            (Md11FcpButtons.Autoflight, Autoflight, () => Md11AutoflightState.Autoflight(Val("MD11_AP_STATE"), Val("MD11_ATS_STATE"))),
            (Md11FcpButtons.Prof, Prof, null),
            (Md11FcpButtons.Nav, Nav, () => Md11AutoflightState.Engaged(Md11AutoflightState.NavEngaged(Val(Md11Fcp.ReadHeading)))));
        y = AddButtonRow(y,
            (Md11FcpButtons.ApproachLand, ApprLand, null),
            (Md11FcpButtons.FmsSpeed, FmsSpd, () => Md11AutoflightState.Engaged(Md11AutoflightState.FmsSpeedEngaged(Val(Md11Fcp.ReadSpeed)))),
            (Md11FcpButtons.GoAround, GoAround, null));

        // ---- Mode selects ---- each names its current mode, from the aircraft's own unit vars.
        y = AddSection("Mode select", y);
        y = AddButtonRow(y,
            (Md11FcpButtons.IasMach, IasMachSel, () => Md11AutoflightState.SpeedMode(Val(Md11Fcp.ModeSpeedIsMach))),
            (Md11FcpButtons.HeadingTrack, HdgTrkSel, () => Md11AutoflightState.HeadingMode(Val(Md11Fcp.ModeHeadingIsTrack))));
        y = AddButtonRow(y,
            (Md11FcpButtons.VsFpa, VsFpaSel, () => Md11AutoflightState.VerticalMode(Val(Md11Fcp.ModeVerticalIsFpa))),
            (Md11FcpButtons.AltitudeUnit, AltUnitSel, () => Md11AutoflightState.AltitudeUnit(Val(Md11Fcp.ModeAltitudeIsMetres))));

        // ---- Vertical speed wheel ----
        // The MD-11 has no "engage V/S" button: rotating the V/S / FPA wheel is what engages the
        // pitch mode. So the wheel is the way to ACTIVATE and adjust V/S (or FPA) — one detent per
        // click. (Typing a value via Ctrl+V also engages it; see SetVerticalSpeedEngaged.)
        y = AddSection("Vertical speed wheel (engages V/S / FPA)", y);
        y = AddWheelRow(y, Md11Fcp.VerticalSpeedName, VsKnob);

        // ---- Knob push/pull ----
        y = AddSection("Knobs — push and pull", y);
        y = AddPushPullRow(y, Md11Fcp.SpeedKnobName, SpdKnob);
        y = AddPushPullRow(y, Md11Fcp.HeadingKnobName, HdgKnob);
        y = AddPushPullRow(y, Md11Fcp.AltitudeKnobName, AltKnob);

        // ---- Autothrust disconnect ----
        y = AddSection("Autothrust", y);
        y = AddButtonRow(y, (Md11FcpButtons.AtsDisconnectLeft, AtsDiscL, null), (Md11FcpButtons.AtsDisconnectRight, AtsDiscR, null));

        // ---- Combos ----
        y = AddSection("Selectors", y);

        AddLabel("Bank angle limiter", 10, y + 4);
        _bankLimit = NewCombo(new Point(170, y), "Bank angle limiter",
            "Autopilot bank angle limit: Auto, or a fixed limit from 5 to 25 degrees.");
        // The positions are the definition's own descriptions for the knob — TFDi's value map,
        // Auto then 5 to 25 degrees — ordered by key, and a pick writes the map KEY, never the row
        // index: a hand-kept copy of the map was one TFDi update away from writing the wrong position.
        if (_def.GetVariables().TryGetValue(BankLimitKnob, out var bankDef))
            foreach (var kvp in bankDef.ValueDescriptions.OrderBy(k => k.Key))
                _bankLimit.Items.Add(new ComboItem(kvp.Value, kvp.Key));
        _bankLimit.SelectedIndexChanged += (s, e) =>
        {
            if (_populating || _bankLimit.SelectedItem is not ComboItem item) return;
            _suppressEcho?.Invoke(BankLimitKnob, item.Value);
            _def.SetControl(BankLimitKnob, item.Value, _sim, _announcer);
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
            _suppressEcho?.Invoke(Md11FlapSystem.DialKey, item.Value);
            _def.SetControl(Md11FlapSystem.DialKey, item.Value, _sim, _announcer);
        };
        y += 42;

        var close = new Button
        {
            Text = Md11FcpButtons.Close,
            Location = new Point(10, y),
            Size = new Size(100, 30),
        };
        close.Click += (s, e) => Close();   // same path as Escape: hide and restore focus
        Controls.Add(close);

        // Hide on close (Escape and &Close both route through Close()) — but let a real process
        // shutdown through. The house wiring gates on CloseReason: an unconditional cancel here
        // made Application.Exit abort after MainForm's own teardown had run, and the updater's
        // restart then waited on an exe that never closed.
        MonitorManagerShared.HideOnClose(this, () =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        });

        _refresh = new System.Windows.Forms.Timer { Interval = 500 };
        _refresh.Tick += (s, e) => RefreshStates();

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

    /// <summary>
    /// A row of buttons. A button with a state getter shows "Label: state" and is refreshed on
    /// the timer; a null getter is a plain one-shot action.
    /// </summary>
    private int AddButtonRow(int y, params (string Label, string Node, Func<string>? State)[] items)
    {
        var x = 10;
        foreach (var (label, node, state) in items)
        {
            var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(170, 30) };
            var captured = node;
            var name = label.Replace("&", "");
            b.Click += (s, e) => Press(captured, name);
            if (state != null)
            {
                _liveCaptions.Add((b, label, state));
                ApplyCaption(b, label, state());
            }
            if (node == Autoflight) _autoflight = b;
            Controls.Add(b);
            x += 178;
        }
        return y + 34;
    }

    /// <summary>"Label: state" on the button and its accessible name — assigned only when it changes, so a screen reader is never re-read a stable caption.</summary>
    private static void ApplyCaption(Button b, string label, string state)
    {
        var text = $"{label}: {state}";
        if (b.Text == text) return;
        b.Text = text;
        b.AccessibleName = text.Replace("&", "");
    }

    private int AddPushPullRow(int y, string name, string node)
    {
        AddLabel(name, 10, y + 5);

        var push = new Button
        {
            Text = $"Push", Location = new Point(170, y), Size = new Size(100, 30),
            AccessibleName = $"{name} knob push",
        };
        push.Click += (s, e) => PressEvents(node, "PUSH_DOWN", "PUSH_UP", Md11Fcp.PushAction(name));

        var pull = new Button
        {
            Text = $"Pull", Location = new Point(278, y), Size = new Size(100, 30),
            AccessibleName = $"{name} knob pull",
        };
        pull.Click += (s, e) => PressEvents(node, "PULL_DOWN", "PULL_UP", Md11Fcp.PullAction(name));

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
            Text = Md11FcpButtons.WheelUp, Location = new Point(170, y), Size = new Size(100, 30),
            AccessibleName = $"{name} wheel up",
        };
        up.Click += (s, e) => FireEvent(node, "WHEEL_UP", Md11Fcp.WheelAction(name, up: true));

        var down = new Button
        {
            Text = Md11FcpButtons.WheelDown, Location = new Point(278, y), Size = new Size(100, 30),
            AccessibleName = $"{name} wheel down",
        };
        down.Click += (s, e) => FireEvent(node, "WHEEL_DOWN", Md11Fcp.WheelAction(name, up: false));

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
        if (!_def.PressControl(node)) _announcer.Announce(Md11Fcp.Unavailable(name));
    }

    private void PressEvents(string node, string down, string up, string name)
    {
        if (!_def.PressControlEvents(node, down, up)) _announcer.Announce(Md11Fcp.Unavailable(name));
    }

    private void FireEvent(string node, string eventName, string name)
    {
        if (!_def.FireControlEvent(node, eventName)) _announcer.Announce(Md11Fcp.Unavailable(name));
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The status list and every live caption, from the cache. The list is reconciled in place —
    /// only a changed row is rewritten and the cursor never moves — so a pilot reading it while
    /// the aircraft flies is not thrown off the row.
    /// </summary>
    private void RefreshStates()
    {
        var mach = Val(Md11Fcp.ModeSpeedIsMach) > 0.5;
        var trk = Val(Md11Fcp.ModeHeadingIsTrack) > 0.5;
        var fpa = Val(Md11Fcp.ModeVerticalIsFpa) > 0.5;
        var metres = Val(Md11Fcp.ModeAltitudeIsMetres) > 0.5;

        var rows = new List<string>(6)
        {
            Md11AutoflightState.Row(Md11AutoflightState.Speed, Md11AutoflightState.SpeedValue(Val(Md11Fcp.ReadSpeed), mach)),
            Md11AutoflightState.Row(Md11AutoflightState.HeadingNoun(trk), Md11AutoflightState.HeadingValue(Val(Md11Fcp.ReadHeading))),
            Md11AutoflightState.Row(Md11AutoflightState.Altitude, Md11AutoflightState.AltitudeValue(Val(Md11Fcp.ReadAltitude), metres)),
            Md11AutoflightState.Row(Md11AutoflightState.VerticalNoun(fpa), Md11AutoflightState.VerticalValue(Val(Md11Fcp.ReadVerticalSpeed), fpa)),
            $"Autopilot: {Md11AutoflightState.Autopilot(Val("MD11_AP_STATE"))}",
            $"Autothrottle: {Md11AutoflightState.Autothrottle(Val("MD11_ATS_STATE"))}",
        };
        DisplayList.UpdateInPlace(_status, rows);

        // A list with no selection reads as just "list" when focus lands on it, and the first Space
        // or Down does nothing useful — the same -1 the monitor manager had to fix. The list is not
        // focused while this runs, so selecting row 0 speaks nothing.
        if (_status.SelectedIndex < 0 && _status.Items.Count > 0) _status.SelectedIndex = 0;

        foreach (var (button, label, state) in _liveCaptions)
            ApplyCaption(button, label, state());
    }

    /// <summary>
    /// The cached value, or null while it has not been delivered — after a SimConnect drop the
    /// cache is empty and this window keeps refreshing, so every row and caption goes through a
    /// composer that says "not available" rather than reading a stand-in 0 as "Autopilot: off".
    /// </summary>
    private double? Val(string key) => _sim.GetCachedVariableValue(key);

    /// <summary>
    /// The row of <paramref name="combo"/> carrying this key, or -1. Both combos' rows carry the
    /// definition's own keys — the Dial-A-Flap's the same rounded doubles its classifier returns
    /// (<see cref="TFDiMD11Definition.NearestDialAFlapChoice"/>), the bank limiter's the map's whole
    /// positions — so the equality is exact, and a value that is no key selects nothing.
    /// </summary>
    private static int IndexOfValue(ComboBox combo, double key)
    {
        for (var i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is ComboItem item && item.Value == key) return i;
        return -1;
    }

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();

        RefreshStates();
        _refresh.Start();

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;

        // Land on the Autoflight button: its caption is the state a pilot opens this window for
        // ("Autoflight: AP 1, ATS on"). Shift+Tab reaches the status list. BEFORE the seed below,
        // deliberately: the form only Hides on close, so it reopens with whatever control was last
        // active — and a screen reader DOES speak a programmatic selection change on the control it
        // is sitting on. Focus first and every combo the seed touches is unfocused, so it says
        // nothing.
        ActiveControl = _autoflight;
        _autoflight.Focus();

        // Snap the combos to the aircraft's current state without firing a write back at it.
        // AFTER Show(), deliberately: on the session's first open the combos have no native handle
        // until Show() creates it, and MainForm.PanelBuilder documents a SelectedIndexChanged replay
        // at handle creation that fired phantom user-action writes during panel build. With the
        // handle alive an assignment is one immediate CB_SETCURSEL raised under _populating, and
        // there is nothing left for handle creation to replay.
        // try/finally, not a bare pair of assignments: _populating is the ONLY thing both
        // SelectedIndexChanged handlers test before writing to the aircraft, and the form is
        // hide-on-close and reused for the life of the aircraft. Anything throwing between the two
        // lines below would latch it true forever, and from then on every Bank Limiter and
        // Dial-A-Flap pick would be a silent no-op — the screen reader still reading the selection,
        // nothing sent, nothing announced. The peer site in FbwEfbForm.UpdateControlsInPlace wraps
        // the identical pattern the same way.
        _populating = true;
        try
        {
            // The knob's value is a map KEY: an empty cache, or a value that is no key, leaves the
            // combo unselected rather than showing a position the knob is not in.
            var bank = _sim.GetCachedVariableValue(BankLimitKnob);
            _bankLimit.SelectedIndex = bank is double b ? IndexOfValue(_bankLimit, b) : -1;
            // The wheel's var is CONTINUOUS (raw 33.0 for TFDi's shipped 14.95°) while the rows are keyed
            // by whole degrees, so an exact-key match misses on every real value and the combo opened
            // with NO selection — where the first Down-arrow selects row 0 and writes 10° to the
            // take-off wheel. Seed the nearest listed degree; only an empty cache leaves it empty.
            var dialRaw = _sim.GetCachedVariableValue(Md11FlapSystem.DialKey);
            _dialAFlap.SelectedIndex = dialRaw is double raw ? IndexOfValue(_dialAFlap, _def.NearestDialAFlapChoice(raw)) : -1;
        }
        finally
        {
            _populating = false;
        }
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
