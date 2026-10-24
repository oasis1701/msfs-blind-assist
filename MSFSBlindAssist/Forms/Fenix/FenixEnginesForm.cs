using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The Fenix "Engines" window (Fenix windows list, Ctrl+Shift+F in input mode): both engines' N1, EGT,
/// N2, fuel flow and oil, plus TAT and OAT, from the Fenix's own data-service values, which match its
/// screens where the stock engine SimVars do not. Arrow keys read the rows, F5 reads them again,
/// Escape closes.
/// </summary>
/// <remarks>
/// The subscription's values arrive on a pool thread ([FNX-2]), as the Fenix changes them (measured
/// 2026-10-22: the running flags on change, EGT about every second while it moves, N2 only once, when
/// the subscription opens): its callback writes only the lock-protected <see cref="FenixEngineSnapshot"/>,
/// and a 1 s WinForms timer reads it into the list, so the form never marshals per update. The list is
/// reconciled in place and the refresh never moves focus. While the window is open, the Fenix's running
/// flag edges are spoken. Because N2 is sent only once, a <see cref="FenixDataPoller"/>,
/// started after the open read, also reads every number but the running flag once a second, on the same
/// pool-thread, store-only footing.
/// </remarks>
public sealed class FenixEnginesForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;

    private readonly FenixWindowContext _context;
    private readonly FenixEngineSnapshot _snapshot = new();
    private readonly FenixEngineDisplayHold _hold = new();
    private readonly FenixEngineRunningWatch _watch = new();
    private readonly System.Windows.Forms.Timer _timer;
    private DisplayListBox _list = null!;
    private TextBox _status = null!;
    private IDisposable? _subscription;
    private FenixDataPoller? _poller;
    private FenixEngineWeightUnit _unit = FenixEngineWeightUnit.Kilograms;
    private bool _unitFromFenix;
    private bool _started;
    private bool _tornDown;
    private IntPtr _previousWindow;

    public FenixEnginesForm(FenixWindowContext context)
    {
        _context = context;
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshNow();
        Build();
    }

    private void Build()
    {
        Text = "Fenix engines";
        Size = new Size(520, 440);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = true;
        ShowInTaskbar = true;
        KeyPreview = true;

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Engine values",
            TabIndex = 0
        };
        // Read-only TextBox, never a Label, so it is in the tab order ([VAT-13]).
        _status = new TextBox
        {
            ReadOnly = true,
            Multiline = true,
            Dock = DockStyle.Bottom,
            Height = 2 * Font.Height + 8,
            AccessibleName = "Status",
            TabIndex = 1
        };
        Controls.Add(_list);
        Controls.Add(_status);

        FormClosing += (_, _) =>
        {
            TearDown();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        if (!_started)
        {
            _started = true;
            RefreshNow();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _subscription = _context.Data.Subscribe(FenixEngineNames.All, _snapshot.Apply);
            _timer.Start();
            _ = OpenAsync();
        }
        // The pilot's own open: the only focus calls in the form.
        Show();
        BringToFront();
        Activate();
        _list.Focus();
    }

    // The open: the one-shot read first, then the poll. The poll starts only after the open read has
    // been applied, because that read is dropped whole (running flags included) when anything was stored
    // meanwhile (ApplyAllIfUnchangedSince), and the poller's first read is immediate: started alongside,
    // it could make the open read lose, leaving the running flags unknown until they next change.
    // Continues on the UI thread; ReloadAsync catches its own failures, so the poll starts either way.
    private async Task OpenAsync()
    {
        await ReloadAsync(false);
        StartPoller();
    }

    // Starts the once-a-second poll of the numbers; never twice, and not once the window is torn down
    // (TearDown or Dispose may run before the open read returns). UI thread.
    //
    // Why: the subscription sends N2 only once, when it opens (measured live, 2026-10-22: one value
    // through a whole shutdown), so a subscription alone froze the N2 rows until F5. The numbers are
    // also read by query. Not the running flag: the subscription's is newer, and a stale polled one
    // would speak a false "Engine n stopped". An unknown answer is stored as unknown (N2 answers null
    // while an engine is off, measured; a gateway that stopped answering shows "--", not a frozen
    // number) [FNX-1]. The poll runs on the thread pool and only stores.
    private void StartPoller()
    {
        if (_tornDown || IsDisposed || _poller != null) return;
        _poller = new FenixDataPoller(FenixEngineNames.Polled,
            (names, ct) => _context.Data.ReadManyAsync(names, ct), _snapshot.Apply);
        _poller.Start();
    }

    // The timer's tick (UI thread): rows, running edges and status from the snapshot. Never focuses.
    private void RefreshNow()
    {
        var reading = _snapshot.Read();
        _list.SetLines(FenixEngineFormat.Rows(reading, _unit, _hold));
        foreach (string line in _watch.Observe(reading))
            _context.Announcer.Announce(line);

        var variant = _context.Definition.Variant;
        string status = FenixEngineFormat.Status(variant.IsKnown ? variant.Describe() : null,
            _unit, _unitFromFenix, reading.LastUpdateTick, Environment.TickCount64);
        if (_status.Text != status) _status.Text = status;
    }

    // Reads the weight unit and every value once (on open and on F5). Continues on the UI thread.
    private async Task ReloadAsync(bool announce)
    {
        try
        {
            var unit = FenixEngineFormat.WeightUnitFrom(await _context.Data.GetSettingsAsync());
            _unit = unit ?? FenixEngineWeightUnit.Kilograms;
            _unitFromFenix = unit != null;

            long version = _snapshot.Version;
            var values = await _context.Data.ReadManyAsync(FenixEngineNames.All);
            if (IsDisposed) return;

            bool anyKnown = values.Values.Any(v => v.IsKnown);
            // A read with nothing known (gateway down, Fenix not loaded) leaves the snapshot alone, so
            // the status keeps saying it is waiting or that values stopped. A read the subscription
            // overtook is dropped: its older running flag would speak a false "stopped". Once the poll
            // runs (it starts after the open read), an F5 read can lose this race to a poll answer and
            // be dropped too: acceptable, the poll keeps the numbers fresh and the subscription
            // carries the running flags.
            if (anyKnown)
                _snapshot.ApplyAllIfUnchangedSince(values, version);
            RefreshNow();

            if (!anyKnown)
                _context.Announcer.Announce("No engine data from the Fenix. Is the Fenix loaded?");
            else if (announce)
                _context.Announcer.Announce("Engines refreshed");
        }
        catch (Exception ex)
        {
            Log.Debug("FenixEngines", $"Engines reload failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (msg.Msg == WM_KEYDOWN)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.F5)
            {
                // Bit 30: the key was already down (auto-repeat).
                if ((msg.LParam.ToInt64() & 0x40000000) == 0)
                    _ = ReloadAsync(true);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Stops the timer, the poll and the subscription; safe to call twice.
    private void TearDown()
    {
        if (_tornDown) return;
        _tornDown = true;
        _timer.Stop();
        // Never throws: a read still in flight is cancelled and its answer is dropped.
        _poller?.Dispose();
        _poller = null;
        try
        {
            // The definition may already have disposed the data service on an aircraft switch.
            _subscription?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug("FenixEngines", $"Engines unsubscribe failed: {ex.GetType().Name}: {ex.Message}");
        }
        _subscription = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TearDown();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
