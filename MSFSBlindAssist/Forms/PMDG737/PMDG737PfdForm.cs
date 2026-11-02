using System.Runtime.InteropServices;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.PMDG737;

/// <summary>The Captain's buttons the PFD window presses (each a press and release, by the caller).</summary>
public enum PfdButton { MinsReset, Std, Fpv, Mtrs, FlightDirector }

/// <summary>What the buttons show: STD, FPV and MTRS from the latest good PFD reading's EFIS panel, the Captain's FD
/// switch from the SDK (null unread), and the minimums as the PFD shows them ("BARO 200"; null while it shows none).</summary>
public sealed record PfdButtonState(bool BaroStd, bool Fpv, bool Mtrs, bool? FlightDirector, string? Minimums = null);

/// <summary>
/// The window's controls, wired by the 737 definition: the minimums and baro selectors as combos whose index is the
/// SDK position (each read from the PMDG SDK, null when unread), the minimums value and the altimeter (the existing
/// paths; the altimeter returns an error line or null, its confirmation is the existing code's own read-back), the
/// speed reference selector and its value (the closed-loop knob turn: the line to say, or null when a newer value or
/// the window's close replaced it), the buttons' state and press, the non-interrupting announce, and the window's
/// open state (the reader's full reading).
/// </summary>
public sealed record PfdControls(
    Func<double?> ReadMinsMode,
    Action<double> SetMinsMode,
    Action<double> SetMinimums,
    Func<double?> ReadBaroUnits,
    Action<double> SetBaroUnits,
    Func<string, string?> SetAltimeter,
    Func<double?> ReadSpeedReference,
    Action<double> SetSpeedReference,
    IReadOnlyList<string> SpeedReferencePositions,
    Func<string, Task<string?>> SetSpeedReferenceValue,
    Func<PfdButtonState?> ReadButtons,
    Action<PfdButton> Press,
    Action<string> Speak,
    Action<bool> WindowOpen);

/// <summary>
/// A button's read-back after a press, once. A press that changed nothing by <see cref="GiveUpMs"/> is read back with
/// the state as it is then, so a press that did not take is never silent (the owner's read-back, 2026-10-30; the 737
/// MCP windows' rule, P737-7). A change, at the first poll at least <see cref="SettleMs"/> after the press, is said only
/// for MINS RST, whose text never changes (the minimums as the PFD then shows them); a stateful button's change is its
/// new text, which NVDA reads itself ([CORE-7]), as it does when a button pressed while unread gets its state. A later
/// press replaces an earlier one. Pure.
/// </summary>
internal sealed class PfdButtonReadback
{
    internal const long SettleMs = 1_000, GiveUpMs = 3_000;

    private PfdButton _button;
    private string? _before;
    private long? _pressedAtMs;

    public void Pressed(PfdButton button, PfdButtonState? before, long nowMs)
    {
        _button = button;
        _before = before is null ? null : PMDG737PfdForm.Readback(button, before);
        _pressedAtMs = nowMs;
    }

    /// <summary>The line to say now, once, or null.</summary>
    public string? Poll(PfdButtonState? now, long nowMs)
    {
        if (_pressedAtMs is not { } pressed || now is null || nowMs - pressed < SettleMs) return null;
        string state = PMDG737PfdForm.Readback(_button, now);
        bool changed = state != _before;
        if (!changed && nowMs - pressed < GiveUpMs) return null;
        _pressedAtMs = null;
        return changed && _button != PfdButton.MinsReset ? null : state;
    }
}

/// <summary>
/// "Primary flight display" (Output Alt+P on the PMDG 737): the Captain PFD as one live list that follows the real
/// display (each row starts with where it sits), then the Captain's controls that change it: MINS mode, Minimums, MINS
/// RST, Baro units, Altimeter, STD, FPV, MTRS, Flight director, Speed reference and its value. A 1 s timer reconciles
/// the list in place (the reader's row is kept), refreshes the buttons' state texts, re-reads each combo unless it has
/// focus, and enables the speed reference value only where the knob sets something (never while it has focus). NVDA
/// reads a stateful button's new text itself, and a press that changed nothing is read back after 3 s; MINS RST's press
/// is read back once, non-interrupting, with the minimums the PFD then shows; the combos act on
/// SelectionChangeCommitted only and the screen reader speaks them (CORE-7); the fields act on Enter and a value that
/// does not parse is said. A provider failure keeps the last lines. Escape closes and returns to the previous window;
/// the reader reads its full set while the window is open.
/// </summary>
public sealed class PMDG737PfdForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string LogCategory = "PMDG737Pfd";

    private static readonly string[] MinsModePositions = { "RADIO", "BARO" };
    private static readonly string[] BaroUnitPositions = { "IN", "HPA" };

    private readonly Func<PfdView> _view;
    private readonly PfdControls _controls;
    private readonly List<(ComboBox Combo, Func<double?> Read)> _combos = new();
    private readonly List<(Button Button, PfdButton Which)> _buttons = new();
    private readonly PfdButtonReadback _readback = new();
    private DisplayListBox _list = null!;
    private TextBox _speedReferenceValue = null!;
    private System.Windows.Forms.Timer _timer = null!;
    private IntPtr _previousWindow;
    private IReadOnlyList<string> _lines = Array.Empty<string>();
    private PfdButtonState? _state;

    public PMDG737PfdForm(Func<PfdView> view, PfdControls controls)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        Build();
    }

    /// <summary>A button's text: its name and state ("STD: on", "Flight director: off"), the name alone while unread;
    /// MINS RST has no state.</summary>
    internal static string ButtonText(PfdButton b, PfdButtonState? s) =>
        s is not null && IsOn(b, s) is { } on ? $"{ButtonLabel(b)}: {(on ? "on" : "off")}" : ButtonLabel(b);

    /// <summary>What is said once after a press: "FPV on", "Flight director off", "Minimums: BARO 200" (MINS RST, the
    /// minimums as the PFD then shows them; "Minimums: off" while it shows none).</summary>
    internal static string Readback(PfdButton b, PfdButtonState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (b == PfdButton.MinsReset) return "Minimums: " + (s.Minimums ?? "off");
        return IsOn(b, s) is { } on ? $"{ButtonLabel(b)} {(on ? "on" : "off")}" : ButtonLabel(b);
    }

    /// <summary>The button's name as the EFIS panel prints it (the FD switch spelled out).</summary>
    private static string ButtonLabel(PfdButton b) => b switch
    {
        PfdButton.MinsReset => "MINS RST",
        PfdButton.Std => "STD",
        PfdButton.Fpv => "FPV",
        PfdButton.Mtrs => "MTRS",
        _ => "Flight director",
    };

    /// <summary>On or off, null for MINS RST (no state) and an unread FD switch.</summary>
    private static bool? IsOn(PfdButton b, PfdButtonState s) => b switch
    {
        PfdButton.Std => s.BaroStd,
        PfdButton.Fpv => s.Fpv,
        PfdButton.Mtrs => s.Mtrs,
        PfdButton.FlightDirector => s.FlightDirector,
        _ => null,
    };

    /// <summary>The list's next lines: the provider's, or the current ones when it fails.</summary>
    internal static IReadOnlyList<string> NextLines(Func<PfdView> provider, IReadOnlyList<string> current)
    {
        try
        {
            return provider().Lines;
        }
        catch (Exception)
        {
            return current;
        }
    }

    /// <summary>The speed reference value is usable only where the knob sets something: the selector's SDK position is
    /// one of the derived positions (a disabled field drops out of the Tab order, as Manual N1's).</summary>
    internal static bool SpeedReferenceFieldEnabled(double? selector, Pmdg737PfdLayout? layout) =>
        layout is not null && selector is { } p && layout.SpeedReference.Positions.Any(x => x.Position == (int)Math.Round(p));

    private void Build()
    {
        Text = "Primary flight display";
        Size = new Size(620, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        int tab = 0;
        _list = new DisplayListBox
        {
            Location = new Point(12, 12), Size = new Size(580, 300), Font = new Font("Consolas", 10f),
            AccessibleName = "Captain PFD", TabIndex = tab++,
        };
        Controls.Add(_list);

        int row = 324;
        AddSelector("MINS mode", MinsModePositions, _controls.ReadMinsMode, _controls.SetMinsMode, ref row, ref tab);
        AddField("Minimums", OnMinimums, ref row, ref tab);
        AddButtons(new[] { PfdButton.MinsReset }, ref row, ref tab);
        AddSelector("Baro units", BaroUnitPositions, _controls.ReadBaroUnits, _controls.SetBaroUnits, ref row, ref tab);
        AddField("Altimeter", OnAltimeter, ref row, ref tab);
        AddButtons(new[] { PfdButton.Std, PfdButton.Fpv, PfdButton.Mtrs, PfdButton.FlightDirector }, ref row, ref tab);
        AddSelector("Speed reference", _controls.SpeedReferencePositions, _controls.ReadSpeedReference, _controls.SetSpeedReference, ref row, ref tab);
        _speedReferenceValue = AddField("Speed reference value", OnSpeedReferenceValue, ref row, ref tab);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshAll();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            e.Handled = true;
            Close();
        };
        FormClosing += (_, _) =>
        {
            _timer.Stop();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
        FormClosed += (_, _) =>
        {
            _timer.Dispose();
            Run(() => _controls.WindowOpen(false));
        };
    }

    private void AddSelector(string name, IReadOnlyList<string> positions, Func<double?> read, Action<double> set, ref int row, ref int tab)
    {
        // The label's mnemonic-free text names the combo; the combo's AccessibleName is what the reader says.
        Controls.Add(new Label { Text = name, Location = new Point(12, row + 4), Size = new Size(180, 20), TabIndex = tab++ });
        var combo = new ComboBox
        {
            Location = new Point(200, row), Size = new Size(160, 28), DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = name, TabIndex = tab++,
        };
        foreach (var position in positions) combo.Items.Add(position);
        // SelectionChangeCommitted, never SelectedIndexChanged: only a real pick writes (PMDGAutopilotWindow).
        combo.SelectionChangeCommitted += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) Run(() => set(combo.SelectedIndex));
        };
        Controls.Add(combo);
        _combos.Add((combo, read));
        row += 36;
    }

    private TextBox AddField(string name, Action<string> act, ref int row, ref int tab)
    {
        Controls.Add(new Label { Text = name, Location = new Point(12, row + 4), Size = new Size(180, 20), TabIndex = tab++ });
        var field = new TextBox { Location = new Point(200, row), Size = new Size(160, 24), AccessibleName = name, TabIndex = tab++ };
        field.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            act(field.Text);
        };
        Controls.Add(field);
        row += 36;
        return field;
    }

    private void AddButtons(IEnumerable<PfdButton> which, ref int row, ref int tab)
    {
        int x = 12;
        foreach (var b in which)
        {
            var button = new Button { Text = ButtonText(b, null), Location = new Point(x, row), Size = new Size(b == PfdButton.FlightDirector ? 180 : 120, 32), TabIndex = tab++ };
            button.Click += (_, _) => Press(b);
            Controls.Add(button);
            _buttons.Add((button, b));
            x += button.Width + 8;
        }
        row += 40;
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Run(() => _controls.WindowOpen(true));
        RefreshAll();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _list.Focus();
        _timer.Start();
    }

    private void OnMinimums(string typed)
    {
        // CORE-17: the pilot's number; the existing minimums path confirms the value it sets.
        if (NumberParsing.TryParseTyped(typed, out double feet)) Run(() => _controls.SetMinimums(feet));
        else Say("Minimums: enter a number of feet");
    }

    private void OnAltimeter(string typed)
    {
        string? error = null;
        Run(() => error = _controls.SetAltimeter(typed));
        if (error is not null) Say(error);
    }

    private async void OnSpeedReferenceValue(string typed)
    {
        // A turn runs for seconds; the window may close meanwhile (the definition cancels it then, and it says nothing).
        try
        {
            if (await _controls.SetSpeedReferenceValue(typed) is { } line) Say(line);
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"setting the speed reference value failed: {ex}");
        }
    }

    private void Press(PfdButton which)
    {
        _readback.Pressed(which, _state, Environment.TickCount64);
        Run(() => _controls.Press(which));
    }

    private void RefreshAll()
    {
        PfdView? view = null;
        _lines = NextLines(() => view = _view(), _lines);
        _list.SetLines(_lines);

        _state = ReadState(view);
        foreach (var (button, which) in _buttons)
        {
            string text = ButtonText(which, _state);
            if (button.Text != text) button.Text = text;
        }
        if (_readback.Poll(_state, Environment.TickCount64) is { } line) Say(line);

        foreach (var (combo, read) in _combos)
        {
            // Never repaint a combo the pilot is in: a background refresh would move the caret under them.
            if (combo.Focused) continue;
            double? value = Read(read);
            combo.Enabled = value.HasValue;
            int index = value is { } v ? (int)Math.Round(v) : -1;
            if (index >= 0 && index < combo.Items.Count && combo.SelectedIndex != index) combo.SelectedIndex = index;
        }

        // Never disable the field the pilot is in: focus would jump.
        if (!_speedReferenceValue.Focused)
            _speedReferenceValue.Enabled = SpeedReferenceFieldEnabled(Read(_controls.ReadSpeedReference), view?.Layout);
    }

    /// <summary>The buttons' state while the reader runs (the view has a reading), the minimums from its rows; else
    /// null (unread).</summary>
    private PfdButtonState? ReadState(PfdView? view)
    {
        if (view?.Sample is null) return null;
        try
        {
            return _controls.ReadButtons() is { } s ? s with { Minimums = Pmdg737PfdText.MinimumsShown(view.Lines) } : null;
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"reading the buttons' state failed: {ex.Message}");
            return null;
        }
    }

    private void Say(string line) => Run(() => _controls.Speak(line));

    private static double? Read(Func<double?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"reading a selector failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>A control's action; a failure (the sim gone mid-press) must not take the window down: the next refresh
    /// shows the real state.</summary>
    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"a control's action failed: {ex}");
        }
    }
}
