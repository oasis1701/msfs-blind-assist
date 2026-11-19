using System.Runtime.InteropServices;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.PMDG737;

/// <summary>The Captain's EFIS map buttons the window presses (each a press and release, by the caller).</summary>
public enum NdButton { Ctr, Tfc, Wxr, Sta, Wpt, Arpt, Data, Pos, Terr }

/// <summary>
/// The window's controls, wired by the 737 definition: the mode, range and VOR/ADF selectors as combos whose
/// index is the SDK position (each read from the PMDG SDK, null when unread), the Captain's panel as the
/// latest good ND reading holds it (null while there is none), the map buttons, and the non-interrupting
/// announce for a button's read-back.
/// </summary>
public sealed record NavDisplayControls(
    Func<double?> ReadMode,
    Action<double> SetMode,
    IReadOnlyList<string> ModePositions,
    Func<double?> ReadRange,
    Action<double> SetRange,
    IReadOnlyList<string> RangePositions,
    Func<double?> ReadVorAdf1,
    Action<double> SetVorAdf1,
    Func<double?> ReadVorAdf2,
    Action<double> SetVorAdf2,
    IReadOnlyList<string> VorAdfPositions,
    Func<NdPanelState?> ReadPanel,
    Action<NdButton> Press,
    Action<string> Speak);

/// <summary>
/// A map button's read-back after a press, once: only a press that changed nothing by <see cref="GiveUpMs"/>,
/// with the state as it is then, so a press that did not take is never silent (the owner, 2026-10-30; the 737
/// MCP windows' rule, P737-7). A change is the button's new text, which NVDA reads itself ("FPV: on" in the PFD
/// window, ground session 2; [CORE-7]), as it does when a button pressed while unread gets its state. A later
/// press replaces an earlier one. Pure.
/// </summary>
internal sealed class NdButtonReadback
{
    internal const long SettleMs = 1_000, GiveUpMs = 3_000;

    private NdButton _button;
    private string? _before;
    private long? _pressedAtMs;

    public void Pressed(NdButton button, NdPanelState? before, long nowMs)
    {
        _button = button;
        _before = before is null ? null : PMDG737NavDisplayForm.Readback(button, before);
        _pressedAtMs = nowMs;
    }

    /// <summary>The line to say now, once, or null.</summary>
    public string? Poll(NdPanelState? now, long nowMs)
    {
        if (_pressedAtMs is not { } pressed || now is null || nowMs - pressed < SettleMs) return null;
        string state = PMDG737NavDisplayForm.Readback(_button, now);
        bool changed = state != _before;
        if (!changed && nowMs - pressed < GiveUpMs) return null;
        _pressedAtMs = null;
        return changed ? null : state;
    }
}

/// <summary>
/// "Navigation display" (Output Alt+N on the PMDG 737): the Captain ND as one live list that follows the real
/// display (each row starts with where it sits), then the EFIS controls that change it: Mode, Range, the CTR
/// button (normal, centred, VSD on: PMDG cycles it), TFC, WXR, STA, WPT, ARPT, DATA, POS, TERR, and VOR/ADF 1
/// and 2. A 1 s timer reconciles the list in place (the reader's row is kept), refreshes the buttons' state
/// texts and re-reads each combo unless it has focus. NVDA reads a button's new text itself; a press that changed
/// nothing is read back once, non-interrupting, after 3 s (the 737 MCP windows' rule, P737-7); the
/// combos act on SelectionChangeCommitted only and the screen reader speaks them (CORE-7). A provider failure
/// keeps the last lines. Escape closes and returns to the previous window (the HS787EicasForm pattern).
/// </summary>
public sealed class PMDG737NavDisplayForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string LogCategory = "PMDG737NavDisplay";

    private readonly Func<NavDisplayView> _view;
    private readonly NavDisplayControls _controls;
    private readonly List<(ComboBox Combo, Func<double?> Read)> _combos = new();
    private readonly List<(Button Button, NdButton Which)> _buttons = new();
    private readonly NdButtonReadback _readback = new();
    private DisplayListBox _list = null!;
    private System.Windows.Forms.Timer _timer = null!;
    private IntPtr _previousWindow;
    private IReadOnlyList<string> _lines = Array.Empty<string>();

    public PMDG737NavDisplayForm(Func<NavDisplayView> view, NavDisplayControls controls)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        Build();
    }

    /// <summary>A button's text: its name and state ("STA: on", "CTR: centred"), the name alone while the
    /// panel is unread.</summary>
    internal static string ButtonText(NdButton b, NdPanelState? p)
    {
        string name = ButtonLabel(b);
        if (p is null) return name;
        return b == NdButton.Ctr ? Readback(b, p) : $"{name}: {(IsOn(b, p) ? "on" : "off")}";
    }

    /// <summary>What is said once after a press: "STA on", "CTR: centred", "CTR: VSD on", "CTR: normal".</summary>
    internal static string Readback(NdButton b, NdPanelState p) =>
        b == NdButton.Ctr ? "CTR: " + (p.Vsd ? "VSD on" : p.Centered ? "centred" : "normal") : $"{ButtonLabel(b)} {(IsOn(b, p) ? "on" : "off")}";

    /// <summary>The button's name as the EFIS panel prints it.</summary>
    private static string ButtonLabel(NdButton b) => b.ToString().ToUpperInvariant();

    private static bool IsOn(NdButton b, NdPanelState p) => b switch
    {
        NdButton.Tfc => p.Tfc,
        NdButton.Wxr => p.Wxr,
        NdButton.Sta => p.Sta,
        NdButton.Wpt => p.Wpt,
        NdButton.Arpt => p.Arpt,
        NdButton.Data => p.Data,
        NdButton.Pos => p.Pos,
        NdButton.Terr => p.Terr,
        _ => p.Centered,
    };

    /// <summary>The list's next lines: the provider's, or the current ones when it fails.</summary>
    internal static IReadOnlyList<string> NextLines(Func<NavDisplayView> provider, IReadOnlyList<string> current)
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

    private void Build()
    {
        Text = "Navigation display";
        Size = new Size(620, 640);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        int tab = 0;
        _list = new DisplayListBox
        {
            Location = new Point(12, 12), Size = new Size(580, 300), Font = new Font("Consolas", 10f),
            AccessibleName = "Captain ND", TabIndex = tab++,
        };
        Controls.Add(_list);

        int row = 324;
        AddSelector("Mode", _controls.ModePositions, _controls.ReadMode, _controls.SetMode, ref row, ref tab);
        AddSelector("Range", _controls.RangePositions, _controls.ReadRange, _controls.SetRange, ref row, ref tab);

        int x = 12;
        foreach (var which in Enum.GetValues<NdButton>())
        {
            var button = new Button { Text = ButtonText(which, null), Location = new Point(x, row), Size = new Size(which == NdButton.Ctr ? 130 : 100, 32), TabIndex = tab++ };
            button.Click += (_, _) => Press(which);
            Controls.Add(button);
            _buttons.Add((button, which));
            x += button.Width + 8;
            if (x > 500)
            {
                x = 12;
                row += 40;
            }
        }
        row += 44;
        AddSelector("VOR/ADF 1", _controls.VorAdfPositions, _controls.ReadVorAdf1, _controls.SetVorAdf1, ref row, ref tab);
        AddSelector("VOR/ADF 2", _controls.VorAdfPositions, _controls.ReadVorAdf2, _controls.SetVorAdf2, ref row, ref tab);

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
        FormClosed += (_, _) => _timer.Dispose();
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

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        RefreshAll();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _list.Focus();
        _timer.Start();
    }

    private void Press(NdButton which)
    {
        _readback.Pressed(which, ReadPanel(), Environment.TickCount64);
        Run(() => _controls.Press(which));
    }

    private void RefreshAll()
    {
        _lines = NextLines(_view, _lines);
        _list.SetLines(_lines);

        var panel = ReadPanel();
        foreach (var (button, which) in _buttons)
        {
            string text = ButtonText(which, panel);
            if (button.Text != text) button.Text = text;
        }
        if (_readback.Poll(panel, Environment.TickCount64) is { } line) Run(() => _controls.Speak(line));

        foreach (var (combo, read) in _combos)
        {
            // Never repaint a combo the pilot is in: a background refresh would move the caret under them.
            if (combo.Focused) continue;
            double? value = Read(read);
            combo.Enabled = value.HasValue;
            int index = value is { } v ? (int)Math.Round(v) : -1;
            if (index >= 0 && index < combo.Items.Count && combo.SelectedIndex != index) combo.SelectedIndex = index;
        }
    }

    private NdPanelState? ReadPanel()
    {
        try
        {
            return _controls.ReadPanel();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"reading the EFIS panel failed: {ex.Message}");
            return null;
        }
    }

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

    /// <summary>A control's action; a failure (the sim gone mid-press) must not take the window down: the
    /// next refresh shows the real state.</summary>
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
