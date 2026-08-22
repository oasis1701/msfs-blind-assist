using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Live per-tank fuel quantities, one row per tank plus a Total (output Alt+U).
///
/// This replaced eighteen hotkey chords (Ctrl+1-9 pounds, Alt+1-9 kilograms). Those read
/// one tank per press with no window to open, which is genuinely fast — but eighteen of
/// the app's scarcest bindings for one readout is a poor trade, and nine digits is a hard
/// ceiling an eleven-tank A380 was already pressed against.
///
/// The speed is bought back by TYPE-AHEAD rather than by chords: <see cref="DisplayListBox"/>
/// deliberately leaves the native ListBox incremental search on, so "c" jumps to Centre and
/// "ou" to Outer. Open, type the tank's first letter, hear the number — three keystrokes for
/// any tank, against two for the nine that fitted on digits, and it scales to any tank count.
/// That is why <c>FuelTankReadout.FormatRow</c> puts the LABEL FIRST on every line.
///
/// The list refreshes in place while open: fuel burns down continuously, and a snapshot
/// would quietly go stale in front of a pilot who left the window up. The reconcile is
/// <c>DisplayList.UpdateInPlace</c> (via <c>SetLines</c>), so only changed rows are rewritten
/// and the screen-reader cursor never jumps — the same behaviour as every other live display
/// window in the app.
/// </summary>
public class FuelTanksForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Refresh cadence. Fuel flow is slow, so this is about not going stale rather than
    /// about smoothness — a second is far below the rate at which any tank figure moves,
    /// and each tick is one on-demand SimConnect read (or a CDA field read on PMDG),
    /// never a monitored stream.
    /// </summary>
    private const int RefreshMs = 1000;

    private readonly IAircraftDefinition _aircraft;
    private readonly SimConnect.SimConnectManager _simConnect;
    private readonly IntPtr _previousWindow;

    private DisplayListBox _list = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;
    private bool _hadReadings;

    public FuelTanksForm(IAircraftDefinition aircraft, SimConnect.SimConnectManager simConnect)
    {
        _aircraft = aircraft;
        _simConnect = simConnect;
        _previousWindow = GetForegroundWindow();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Fuel Tanks";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(560, 340);
        KeyPreview = true;

        // The heading is a real Label for sighted users AND the list's AccessibleName, so
        // tabbing in announces what the list is before its first row — the same pairing
        // the SayIntentions info window uses.
        var heading = new Label
        {
            Text = "Fuel by tank",
            Location = new Point(12, 12),
            Size = new Size(400, 20),
            TabStop = false
        };

        _list = new DisplayListBox
        {
            Location = new Point(12, 36),
            Size = new Size(ClientSize.Width - 24, ClientSize.Height - 48),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AccessibleName = "Fuel by tank",
            TabIndex = 0
            // SuppressTypeAhead deliberately left FALSE: first-letter navigation is the
            // whole point of this window. See the class remarks.
        };

        Controls.Add(heading);
        Controls.Add(_list);
        ActiveControl = _list;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = RefreshMs };
        _refreshTimer.Tick += (_, _) => Refresh(announceIfUnavailable: false);
        _refreshTimer.Start();

        // First read immediately — waiting a tick would open the window empty.
        Refresh(announceIfUnavailable: true);
    }

    /// <summary>
    /// Pulls live quantities and reconciles the list. <paramref name="announceIfUnavailable"/>
    /// is true only for the FIRST read: an aircraft with no per-tank readout (or a PMDG whose
    /// CDA has not arrived) should say so once on open, not once per second.
    /// </summary>
    private void Refresh(bool announceIfUnavailable)
    {
        _aircraft.RequestFuelTankReadings(_simConnect, readings =>
        {
            // The stock-fuel path completes on a SimConnect callback, which is not
            // guaranteed to be the UI thread; the PMDG path completes inline. Marshal
            // either way. IsHandleCreated alone races a concurrent handle-destroy on
            // window close, so the BeginInvoke is guarded — the same SafeBeginInvoke
            // reasoning the A380 bridge forms use.
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() => Apply(readings, announceIfUnavailable)));
            }
            catch (InvalidOperationException) { /* handle died mid-close */ }
        });
    }

    private void Apply(IReadOnlyList<FuelTankReading>? readings, bool announceIfUnavailable)
    {
        if (IsDisposed) return;

        if (readings == null || readings.Count == 0)
        {
            // Don't blank a list that HAS been populated: a transient miss (a PMDG CDA gap,
            // a dropped request) would otherwise wipe the numbers the pilot is reading and
            // put them back a second later.
            if (_hadReadings) return;
            _list.SetLines(new[] { "Per-tank fuel is not available on this aircraft." });
            if (announceIfUnavailable) _list.AccessibleDescription = null;
            return;
        }

        _hadReadings = true;
        _list.SetLines(FuelTankReadout.BuildLines(readings));
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        base.OnFormClosed(e);
        // Hand the foreground back to whatever had it (normally the simulator), so the
        // pilot is not left with focus on a dead window and Windows picking a winner.
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }
}
