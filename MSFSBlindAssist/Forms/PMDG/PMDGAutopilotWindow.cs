using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.PMDG;

/// <summary>
/// A multi-position cockpit switch row, rendered as a ComboBox.
/// <para>
/// Deliberately NOT a ToggleButtonDef: rendering a 5-6 position selector as a cycling
/// button would force the pilot to step through positions and listen for the one they
/// want, with no way to jump. Multi-position switches stay multi-position combos;
/// buttons are for true one-shot momentary actions and two-position toggles.
/// </para>
/// <para>
/// <paramref name="GetCurrentValue"/> returns null when the CDA snapshot has not
/// arrived, which disables the combo rather than showing a false position 0.
/// </para>
/// </summary>
public record SelectorRowDef(
    string Label,
    IReadOnlyDictionary<double, string> Positions,
    Func<double?> GetCurrentValue,
    Action<double> OnSelected);

/// <summary>
/// Shared Ctrl+P autopilot engage-cluster window for the PMDG 737 and 777. Aircraft-
/// agnostic: it owns layout, refresh and lifecycle only, and is driven entirely by the
/// row lists it is constructed with (built per aircraft by PMDGAutopilotRowBinder).
/// <para>
/// No explicit announcements. The screen reader announces the click, and the label
/// refresh means the new state reads on focus — the same contract as
/// IFly737AutopilotWindow. Unlike that window there is no verified-click retry: the
/// iFly one exists because the iFly plugin demonstrably drops Click commands, a failure
/// mode PMDG's CDA transport does not have, and a speculative retry would risk
/// double-actuating an engage button.
/// </para>
/// </summary>
public class PMDGAutopilotWindow : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IReadOnlyList<ToggleButtonDef> _buttons;
    private readonly IReadOnlyList<SelectorRowDef> _selectors;
    private readonly List<Button> _buttonControls = new();
    private readonly List<(SelectorRowDef Def, ComboBox Combo, double[] Values)> _selectorControls = new();

    private System.Windows.Forms.Timer _refreshTimer = null!;
    private Button _closeButton = null!;
    private IntPtr _previousWindow;

    public PMDGAutopilotWindow(
        string title,
        IReadOnlyList<ToggleButtonDef> buttons,
        IReadOnlyList<SelectorRowDef> selectors)
    {
        _buttons = buttons;
        _selectors = selectors;
        BuildForm(title);
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        RefreshStates();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        if (_buttonControls.Count > 0) _buttonControls[0].Focus();
        _refreshTimer.Start();
    }

    private void BuildForm(string title)
    {
        Text = title;
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

        // Buttons: two per row, filling left column then right.
        for (int i = 0; i < _buttons.Count; i++)
        {
            var def = _buttons[i];
            var btn = new Button
            {
                Location = new Point(i % 2 == 0 ? col1 : col2, row),
                Size = new Size(btnW, btnH),
                TabIndex = tab++,
            };
            btn.Click += (_, _) =>
            {
                def.OnPressed();
                RefreshSoon();
            };
            _buttonControls.Add(btn);
            Controls.Add(btn);
            if (i % 2 == 1) row += rowH;
        }
        if (_buttons.Count % 2 == 1) row += rowH;

        // Selectors: each on its own row, label + combo.
        foreach (var def in _selectors)
        {
            var label = new Label
            {
                Text = def.Label,
                Location = new Point(col1, row + 6),
                Size = new Size(btnW, 20),
            };
            var values = def.Positions.Keys.OrderBy(v => v).ToArray();
            var combo = new ComboBox
            {
                Location = new Point(col2, row),
                Size = new Size(btnW, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = def.Label,
                TabIndex = tab++,
            };
            foreach (double v in values) combo.Items.Add(def.Positions[v]);

            var captured = def;
            var capturedValues = values;
            // SelectionChangeCommitted, NOT SelectedIndexChanged — the same choice every
            // panel combo in this app makes (MainForm.PanelBuilder.cs wires its
            // DropDownList combos on this event, with the reasoning in full).
            // SelectedIndexChanged ALSO fires on programmatic assignment AND on a deferred
            // replay when the combo is parented and its native handle is created, which
            // fires a phantom user-action write during form build. No sync-guard flag can
            // cover that replay here: ShowForm calls RefreshStates() BEFORE Show(), so
            // SelectedIndex is assigned while no handle exists and any such flag is long
            // reset by the time the handle is created and the replay lands.
            // SelectionChangeCommitted fires only on a genuine user commit — which is also
            // why RefreshStates can assign SelectedIndex with no guard at all.
            //
            // Keyboard-safe: the native combo sends CBN_SELCHANGE (what WinForms surfaces
            // as SelectionChangeCommitted) for arrow-key selection on a closed
            // DropDownList, not only for mouse picks in the dropdown. This app's blind
            // users arrow through every panel combo on exactly this event.
            combo.SelectionChangeCommitted += (_, _) =>
            {
                if (combo.SelectedIndex < 0) return;
                captured.OnSelected(capturedValues[combo.SelectedIndex]);
                RefreshSoon();
            };

            _selectorControls.Add((def, combo, values));
            Controls.Add(label);
            Controls.Add(combo);
            row += rowH;
        }

        _closeButton = new Button
        {
            Text = "Close",
            Location = new Point(col1, row),
            Size = new Size(col2 + btnW - col1, btnH),
            AccessibleName = "Close",
            TabIndex = tab,
        };
        _closeButton.Click += (_, _) => HideWindow();
        Controls.Add(_closeButton);

        // Escape presses Close (HideWindow) via ProcessDialogKey.
        CancelButton = _closeButton;

        ClientSize = new Size(col2 + btnW + col1, row + btnH + 15);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshStates();

        // Hide-on-close so the cached instance survives reopen; the refresh timer stops
        // while hidden and restarts in ShowForm. ONLY CloseReason.UserClosing — the
        // title-bar X and Alt+F4 — is cancelled into a Hide.
        //
        // Every other close reason deliberately falls through and really disposes:
        // Close() called from code reports CloseReason.None, and an owner-form close
        // reports CloseReason.FormOwnerClosing (NOT UserClosing). That fall-through is
        // load-bearing in both directions. Escape and the Close button avoid Close()
        // entirely and call HideWindow() directly, because their CloseReason.None would
        // slip past this UserClosing-only guard and dispose the form instead of hiding
        // it. Conversely the aircraft-switch teardown (DisposeTrackedWindows, called on
        // the outgoing def by MainForm.SwitchAircraft) DOES call Close(), and depends on
        // CloseReason.None NOT being cancelled so the window and its refresh timer really
        // go away with the def that owns them.
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

    /// <summary>Shared hide path for Escape and the Close button — mirrors what
    /// FormClosing does for CloseReason.UserClosing, but calls Hide() directly so
    /// CloseReason.None can never slip past the UserClosing-only guard.</summary>
    private void HideWindow()
    {
        Hide();
        _refreshTimer.Stop();
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    private void RefreshSoon()
    {
        Task.Delay(300).ContinueWith(_ =>
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(RefreshStates);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });
    }

    private void RefreshStates()
    {
        for (int i = 0; i < _buttonControls.Count; i++)
        {
            string state = _buttons[i].GetCurrentState();
            var btn = _buttonControls[i];
            btn.Text = string.IsNullOrEmpty(state) ? _buttons[i].Label : $"{_buttons[i].Label}: {state}";
            btn.AccessibleName = btn.Text;
        }

        foreach (var (def, combo, values) in _selectorControls)
        {
            // Never repaint a combo the pilot is currently in — a background timer must
            // not move their selection mid-interaction.
            if (combo.Focused) continue;

            double? current = def.GetCurrentValue();
            combo.Enabled = current.HasValue;
            if (!current.HasValue) continue;

            int idx = Array.IndexOf(values, current.Value);
            if (idx < 0 || combo.SelectedIndex == idx) continue;

            // Unguarded on purpose: the handler is SelectionChangeCommitted, which a
            // programmatic assignment never raises.
            combo.SelectedIndex = idx;
        }
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
