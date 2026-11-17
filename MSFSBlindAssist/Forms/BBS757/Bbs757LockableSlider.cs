using System.ComponentModel;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// A slider (TrackBar) for a setting the tablet sets along a slider: it moves between steps, the screen reader reads its
/// value in the tablet's words (<see cref="ValueText"/>, "2.5x", never the step's number), and it can be held where it is
/// without leaving the tab order. Up and Right move it one step higher, Down and Left one lower, Page Up and Page Down
/// <see cref="TrackBar.LargeChange"/> steps, Home and End to its ends: the keys are the slider's own, as a Win32
/// trackbar lying flat takes Up and Page Up lower; with Control the arrows, Page Up, Page Down, Home and End do nothing
/// (Ctrl+Page Up and Ctrl+Page Down switched the old window's tabs, and the native trackbar would take them as a page the
/// other way: the settings window has no tab control to take them first). From a value
/// between steps (set on the tablet), a key moves from the nearest step. A click or a drag moves it as usual. Each move
/// of the pilot's raises
/// <see cref="StepChosen"/>; a key at an end, or a turn of the wheel that moves nothing, raises nothing. While
/// <see cref="Locked"/>, nothing the pilot does moves it: each attempt (a key, a turn of the wheel, a click) raises
/// <see cref="LockedAttempt"/> once, a held key's repeats and one turn of the wheel being one attempt. The screen reader
/// reads it through MSAA (see <see cref="WndProc"/>). The 757 EFB settings window's Failures section holds the random
/// failure frequency with it, as <see cref="Bbs757LockableComboBox"/> holds the choice lists.
/// </summary>
internal sealed class Bbs757LockableSlider : TrackBar
{
    // Bit 30 of a key message's lParam: the key was down before this message, so the message is a held key's repeat.
    private const long KeyWasDownBit = 1L << 30;

    // Wheel messages closer together than this are one turn of the wheel: one attempt.
    private const long WheelTurnGapMs = 500;

    private const int WmGetObject = 0x003D;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonDblClk = 0x0203;

    // WM_GETOBJECT's object id when UI Automation asks a window for its provider (UiaRootObjectId).
    private const int UiaRootObjectId = -25;

    private bool _locked;
    // Whether the key message being handled is a held key's repeat (set for every key message, before its events).
    private bool _keyRepeat;
    private long? _lastWheelMs;
    // Where ShowStep or the pilot's last move left the slider. TrackBar.OnMouseWheel raises Scroll for every wheel
    // message, one that moves nothing included (part of a notch, a notch at an end): that is no move.
    private int _shownStep = -1;

    public Bbs757LockableSlider()
    {
        AutoSize = false;
        TickStyle = TickStyle.None;
        SmallChange = 1;
        LargeChange = 10;
    }

    /// <summary>
    /// Whether the slider is held: nothing the pilot does moves it, and each attempt raises <see cref="LockedAttempt"/>. A
    /// drag under way when the lock comes on is let go. The slider can still be set in code (<see cref="ShowStep"/>).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            if (value && Capture) Capture = false;
        }
    }

    /// <summary>What the screen reader reads as the slider's value: the tablet's words for where it is.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ValueText { get; private set; } = "";

    /// <summary>The words of each step from <see cref="TrackBar.Minimum"/> up: what a move of the pilot's reads.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> StepTexts { get; set; } = Array.Empty<string>();

    /// <summary>Raised after each move of the pilot's, with the slider at its new step.</summary>
    public event EventHandler? StepChosen;

    /// <summary>Raised once for each attempt to move the slider while <see cref="Locked"/>.</summary>
    public event EventHandler? LockedAttempt;

    /// <summary>
    /// Puts the slider at <paramref name="step"/> (left where it is when that is no step) reading
    /// <paramref name="text"/>, which can be a value between steps ("8.5x"). Set in code: no move of the pilot's, and
    /// nothing is said.
    /// </summary>
    public void ShowStep(int step, string text)
    {
        ValueText = text;
        if (step >= Minimum && step <= Maximum && step != Value) Value = step;
        _shownStep = Value;
    }

    /// <summary>
    /// The step a key moves the slider to from <paramref name="value"/>, kept between <paramref name="minimum"/> and
    /// <paramref name="maximum"/>, or null for a key that does not move a slider: any with Alt or Control, which
    /// <see cref="IsSliderKeyWithControl"/> and the window sort out. Pure; internal for the tests.
    /// </summary>
    internal static int? Target(Keys keyData, int value, int minimum, int maximum, int largeChange)
    {
        if ((keyData & (Keys.Alt | Keys.Control)) != Keys.None) return null;
        int? target = (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Right => value + 1,
            Keys.Down or Keys.Left => value - 1,
            Keys.PageUp => value + largeChange,
            Keys.PageDown => value - largeChange,
            Keys.Home => minimum,
            Keys.End => maximum,
            _ => null,
        };
        return target is int step ? Math.Clamp(step, minimum, maximum) : null;
    }

    /// <summary>
    /// Whether a key is an arrow, Page Up, Page Down, Home or End held with Control (not Alt): the native trackbar would
    /// act on it, taking Up, Down, Page Up and Page Down the other way, so the slider takes the key and does nothing (an
    /// attempt while locked). Ctrl+Page Up and Ctrl+Page Down are the old window's tab switch, which a pilot presses out of
    /// habit: nothing takes them first since the window lost its tab control, and the trackbar's move would be saved.
    /// Ctrl+Tab is not taken here; with Alt the native trackbar ignores every key. Pure; internal for the tests.
    /// </summary>
    internal static bool IsSliderKeyWithControl(Keys keyData) =>
        (keyData & (Keys.Control | Keys.Alt)) == Keys.Control
        && (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown
            or Keys.Home or Keys.End;

    protected override bool ProcessKeyMessage(ref Message m)
    {
        _keyRepeat = (m.LParam.ToInt64() & KeyWasDownBit) != 0;
        return base.ProcessKeyMessage(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int? target = Target(e.KeyData, Value, Minimum, Maximum, LargeChange);
        if (target is null && !IsSliderKeyWithControl(e.KeyData))
        {
            base.OnKeyDown(e);
            return;
        }
        // Handled: the native trackbar never sees the key, so it never moves the other way.
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (_locked)
        {
            Attempt(_keyRepeat);
            return;
        }
        if (target is not int step || step == Value) return;
        // The words first: the screen reader asks for the value when the slider moves.
        ValueText = TextAt(step);
        Value = step;
        _shownStep = step;
        Moved();
    }

    // A click or a drag, or an unlocked wheel through TrackBar's own OnMouseWheel, which raises Scroll for every wheel
    // message: a move only where the slider is no longer where it was left.
    protected override void OnScroll(EventArgs e)
    {
        base.OnScroll(e);
        if (Value == _shownStep) return;
        ValueText = TextAt(Value);
        _shownStep = Value;
        Moved();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!_locked)
        {
            base.OnMouseWheel(e);
            return;
        }
        // Handled: the native trackbar never sees the wheel.
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        long now = Environment.TickCount64;
        bool sameTurn = _lastWheelMs is long last && now - last < WheelTurnGapMs;
        _lastWheelMs = now;
        Attempt(sameTurn);
    }

    /// <summary>
    /// UI Automation's request for the window's provider goes to the native trackbar, which has none, so the screen reader
    /// reads the slider through MSAA, where its value is <see cref="ValueText"/>. NVDA reads a window that has a UI
    /// Automation provider through UI Automation (a trackbar's window class is not on its list of those it reads through
    /// MSAA), and there this slider's object can give no value: WinForms keeps the UI Automation patterns internal.
    /// Measured 2026-10-28 on a WinForms 10 TrackBar: with this, no UI Automation provider, and MSAA reads the name, the
    /// slider role and "2.5x". A reader that only uses UI Automation (Narrator) then gets less.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmGetObject && unchecked((int)(long)m.LParam) == UiaRootObjectId)
        {
            DefWndProc(ref m);
            return;
        }
        if (_locked && m.Msg is WmLButtonDown or WmLButtonDblClk)
        {
            // Not passed on: the native trackbar would move the thumb to the click. The focus a click gives stays.
            if (CanFocus && !Focused) Focus();
            Attempt(repeat: false);
            return;
        }
        base.WndProc(ref m);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessibleObject(this);

    // The screen reader is told the value changed, and the page saves the step.
    private void Moved()
    {
        AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        StepChosen?.Invoke(this, EventArgs.Empty);
    }

    private string TextAt(int step) => step >= Minimum && step - Minimum < StepTexts.Count ? StepTexts[step - Minimum] : "";

    private void Attempt(bool repeat)
    {
        if (!repeat) LockedAttempt?.Invoke(this, EventArgs.Empty);
    }

    // The slider for the screen reader: a slider whose value is the tablet's words, not the step's number.
    private sealed class SliderAccessibleObject : ControlAccessibleObject
    {
        private readonly Bbs757LockableSlider _slider;

        public SliderAccessibleObject(Bbs757LockableSlider slider) : base(slider) => _slider = slider;

        public override AccessibleRole Role => AccessibleRole.Slider;

        public override string? Value
        {
            get => _slider.ValueText;
            set { }
        }
    }
}
