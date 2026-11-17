using System.ComponentModel;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// A choice list (DropDownList) that can be held at its selection without leaving the tab order. While
/// <see cref="Locked"/>, what would change the selection does nothing: the arrow keys, Page Up, Page Down, Home, End,
/// F4, Alt+Down and Alt+Up, a character (which selects the next item it begins; one typed with AltGr too), the mouse
/// wheel and the drop-down (opened by a click or the screen reader, and closed again at once). Each attempt raises
/// <see cref="LockedAttempt"/> once: a held key's repeats and one turn of the wheel are one attempt, so the window says
/// why once, not on every repeat. Focus, the screen reader's reading and the selection shown stay as they are, where a
/// disabled control would drop out of the tab order. The 757 EFB settings window holds its choice lists with it
/// while BlueBird's settings are read-only. Held or not, Ctrl+Page Up and Ctrl+Page Down do nothing
/// (<see cref="IsTabKeyWithControl"/>: the old window's tab switch); held, they are an attempt like the others.
/// </summary>
internal sealed class Bbs757LockableComboBox : ComboBox
{
    // Bit 30 of a key message's lParam: the key was down before this message, so the message is a held key's repeat.
    private const long KeyWasDownBit = 1L << 30;

    // Wheel messages closer together than this are one turn of the wheel: one attempt.
    private const long WheelTurnGapMs = 500;

    private bool _locked;
    // Whether the key message being handled is a held key's repeat (set for every key message, before its events).
    private bool _keyRepeat;
    private long? _lastWheelMs;

    public Bbs757LockableComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
    }

    /// <summary>
    /// Whether the selection is held: nothing the pilot does changes it, and each attempt raises
    /// <see cref="LockedAttempt"/>. A list that is open when the lock comes on is closed. The selection can still be set in
    /// code.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            if (value && DroppedDown) DroppedDown = false;
        }
    }

    /// <summary>Raised once for each attempt to change the selection while <see cref="Locked"/>.</summary>
    public event EventHandler? LockedAttempt;

    protected override bool ProcessKeyMessage(ref Message m)
    {
        _keyRepeat = (m.LParam.ToInt64() & KeyWasDownBit) != 0;
        return base.ProcessKeyMessage(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Ctrl+Page Up and Ctrl+Page Down do nothing, held or not (IsTabKeyWithControl); held, so does every key that would
        // change the selection. Each is an attempt while held.
        if (IsTabKeyWithControl(e.KeyData) || (_locked && ChangesSelection(e.KeyData)))
        {
            // Handled: the native list never sees the key.
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (_locked) Attempt(_keyRepeat);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        // A character selects the next item it begins; Enter, Escape and Backspace change nothing, and neither does a
        // plain Alt+letter (WM_SYSCHAR, a mnemonic no control took). AltGr is Ctrl+Alt and types a character (@ on a
        // German keyboard), so only Alt without Ctrl gets through.
        if (_locked && !char.IsControl(e.KeyChar) && (ModifierKeys & (Keys.Alt | Keys.Control)) != Keys.Alt)
        {
            e.Handled = true;
            Attempt(_keyRepeat);
            return;
        }
        base.OnKeyPress(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!_locked)
        {
            base.OnMouseWheel(e);
            return;
        }
        // Handled: the native list never sees the wheel.
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        long now = Environment.TickCount64;
        bool sameTurn = _lastWheelMs is long last && now - last < WheelTurnGapMs;
        _lastWheelMs = now;
        Attempt(sameTurn);
    }

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        if (!_locked) return;
        Attempt(repeat: false);
        // Closed once it has opened: a close asked for inside the drop-down's own notification does not take.
        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed && DroppedDown) DroppedDown = false;
            });
        }
        catch (InvalidOperationException)
        {
            // The handle is gone, and the list with it.
        }
    }

    private void Attempt(bool repeat)
    {
        if (!repeat) LockedAttempt?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether a key is Page Up or Page Down held with Control (not Alt): the old window's tab switch, which a pilot presses
    /// out of habit. The native list would move its selection a page (and the window save it), as nothing takes the key first
    /// since the window lost its tab control, so the list takes it and does nothing, held or not. Pure; internal for the tests.
    /// </summary>
    internal static bool IsTabKeyWithControl(Keys keyData) =>
        (keyData & (Keys.Control | Keys.Alt)) == Keys.Control && (keyData & Keys.KeyCode) is Keys.PageUp or Keys.PageDown;

    /// <summary>The keys a closed DropDownList answers by changing its selection or opening its list; Alt+F4 is the window's.
    /// Pure; internal for the tests.</summary>
    internal static bool ChangesSelection(Keys keyData)
    {
        bool alt = (keyData & Keys.Alt) == Keys.Alt;
        return (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down => true,
            Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End or Keys.F4 => !alt,
            _ => false,
        };
    }
}
