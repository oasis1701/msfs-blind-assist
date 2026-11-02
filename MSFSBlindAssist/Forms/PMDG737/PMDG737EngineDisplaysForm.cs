using System.Runtime.InteropServices;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.PMDG737;

/// <summary>
/// The window's controls, wired by the 737 definition: the MFD ENG and SYS buttons and the Fuel Flow Used
/// and Reset buttons (press-and-release, read back by the caller; the Fuel Flow switch is spring-loaded),
/// the two selectors as combos whose index is the SDK position (each read from the PMDG SDK, null when
/// unread), and the manual N1 (null until it is supplied: the field stays hidden).
/// </summary>
public sealed record EngineDisplaysControls(
    Action PressMfdEng,
    Action PressMfdSys,
    Func<double?> ReadLowerDu,
    Action<double> SetLowerDu,
    IReadOnlyList<string> LowerDuPositions,
    Action PressFuelFlowUsed,
    Action PressFuelFlowReset,
    Func<double?> ReadN1Set,
    Action<double> SetN1Set,
    IReadOnlyList<string> N1SetPositions,
    Func<bool> ManualN1Available,
    Action<string>? SetManualN1);

/// <summary>
/// "Engine displays" (Output Alt+E on the PMDG 737): the upper and lower display units as two live lists
/// that follow the real displays (each names its page, then the page's rows), then the controls that change
/// them: MFD ENG, MFD SYS, Lower DU Captain, Fuel Flow Used and Reset, N1 SET and the manual N1. A 1 s timer reconciles
/// both lists in place (the reader's row is kept) and re-reads each combo unless it has focus. A provider
/// failure keeps the last lines. Escape closes and returns to the previous window (the HS787EicasForm
/// pattern). The combos act on SelectionChangeCommitted only (the PMDGAutopilotWindow reasoning): the
/// screen reader speaks the new value and the window adds nothing (CORE-7).
/// </summary>
public sealed class PMDG737EngineDisplaysForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string LogCategory = "PMDG737EngineDisplays";

    private readonly Func<EngineDisplaysView> _view;
    private readonly EngineDisplaysControls _controls;
    private readonly List<(ComboBox Combo, Func<double?> Read)> _combos = new();
    private DisplayListBox _upperBox = null!;
    private DisplayListBox _lowerBox = null!;
    private TextBox _manualN1 = null!;
    private System.Windows.Forms.Timer _timer = null!;
    private IntPtr _previousWindow;
    private IReadOnlyList<string> _upper = Array.Empty<string>();
    private IReadOnlyList<string> _lower = Array.Empty<string>();

    public PMDG737EngineDisplaysForm(Func<EngineDisplaysView> view, EngineDisplaysControls controls)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        Build();
    }

    /// <summary>The manual N1 field is usable only when N1 SET (SDK positions 0 "2", 1 "1", 2 "AUTO", 3 "BOTH")
    /// is on 1, 2 or BOTH and the reader can read the reference back (a disabled field drops out of the Tab
    /// order, as in FenixBaroWindow).</summary>
    internal static bool ManualN1FieldEnabled(double? n1SetPosition, bool available) =>
        available && n1SetPosition is { } p && (int)Math.Round(p) is 0 or 1 or 3;

    /// <summary>The lists' next lines: the provider's, or the current ones when it fails.</summary>
    internal static (IReadOnlyList<string> Upper, IReadOnlyList<string> Lower) NextLines(
        Func<EngineDisplaysView> provider, IReadOnlyList<string> upper, IReadOnlyList<string> lower)
    {
        try
        {
            var view = provider();
            return (view.Upper, view.Lower);
        }
        catch (Exception)
        {
            return (upper, lower);
        }
    }

    private void Build()
    {
        Text = "Engine displays";
        Size = new Size(560, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        int tab = 0;
        _upperBox = new DisplayListBox
        {
            Location = new Point(12, 12), Size = new Size(520, 190), Font = new Font("Consolas", 10f),
            AccessibleName = "Upper display", TabIndex = tab++,
        };
        _lowerBox = new DisplayListBox
        {
            Location = new Point(12, 210), Size = new Size(520, 170), Font = new Font("Consolas", 10f),
            AccessibleName = "Lower display", TabIndex = tab++,
        };
        Controls.Add(_upperBox);
        Controls.Add(_lowerBox);

        var eng = new Button { Text = "MFD &ENG", Location = new Point(12, 392), Size = new Size(120, 32), TabIndex = tab++ };
        eng.Click += (_, _) => Run(_controls.PressMfdEng);
        var sys = new Button { Text = "MFD &SYS", Location = new Point(144, 392), Size = new Size(120, 32), TabIndex = tab++ };
        sys.Click += (_, _) => Run(_controls.PressMfdSys);
        Controls.Add(eng);
        Controls.Add(sys);

        int row = 436;
        AddSelector("Lower DU Captain", _controls.LowerDuPositions, _controls.ReadLowerDu, _controls.SetLowerDu, ref row, ref tab);
        var used = new Button { Text = "Fuel Flow &Used", Location = new Point(12, row), Size = new Size(150, 32), TabIndex = tab++ };
        used.Click += (_, _) => Run(_controls.PressFuelFlowUsed);
        var reset = new Button { Text = "Fuel Flow &Reset", Location = new Point(174, row), Size = new Size(150, 32), TabIndex = tab++ };
        reset.Click += (_, _) => Run(_controls.PressFuelFlowReset);
        Controls.Add(used);
        Controls.Add(reset);
        row += 40;
        AddSelector("N1 SET", _controls.N1SetPositions, _controls.ReadN1Set, _controls.SetN1Set, ref row, ref tab);

        var manualLabel = new Label { Text = "Manual N1", Location = new Point(12, row + 4), Size = new Size(180, 20), TabIndex = tab++ };
        _manualN1 = new TextBox { Location = new Point(200, row), Size = new Size(120, 24), AccessibleName = "Manual N1", TabIndex = tab++ };
        _manualN1.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            if (_controls.SetManualN1 is { } set) Run(() => set(_manualN1.Text));
        };
        // Shown only with a setter (the PMDG 737 definition supplies one: the closed-loop knob turn).
        manualLabel.Visible = _manualN1.Visible = _controls.SetManualN1 is not null;
        Controls.Add(manualLabel);
        Controls.Add(_manualN1);

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
        _upperBox.Focus();
        _timer.Start();
    }

    private void RefreshAll()
    {
        (_upper, _lower) = NextLines(_view, _upper, _lower);
        _upperBox.SetLines(_upper);
        _lowerBox.SetLines(_lower);

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
        if (_manualN1.Visible && !_manualN1.Focused)
        {
            bool available;
            try { available = _controls.ManualN1Available(); } catch (Exception) { available = false; }
            _manualN1.Enabled = ManualN1FieldEnabled(Read(_controls.ReadN1Set), available);
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
