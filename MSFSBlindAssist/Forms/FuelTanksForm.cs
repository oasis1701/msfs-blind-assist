using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Live per-tank fuel quantities, one row per tank plus a Total (output Alt+U).
///
/// The speed of a per-tank chord is bought back by TYPE-AHEAD: <see cref="DisplayListBox"/>
/// deliberately leaves the native ListBox incremental search on, so "c" jumps to Center and
/// "o" to Outer. Open, type the tank's first letter, hear the number — and it scales to any
/// tank count. That is why <c>FuelTankReadout.FormatRow</c> puts the LABEL FIRST on every line.
///
/// The list refreshes in place while open: fuel burns down continuously, and a snapshot
/// would quietly go stale in front of a pilot who left the window up. The reconcile is
/// <c>DisplayList.UpdateInPlace</c> (via <c>SetLines</c>), so only changed rows are rewritten
/// and the screen-reader cursor never jumps — the same behaviour as every other live display
/// window in the app.
///
/// STALENESS IS ANNOUNCED, NOT HIDDEN. A mid-session SimConnect drop makes the readings stop
/// arriving; the last good rows are KEPT (blanking them would wipe numbers the pilot is
/// reading) but a marker row is appended, because a frozen fuel figure that still looks live
/// is the one failure this window must not have. The marker is appended, never inserted, so
/// it cannot shift the row the reader's cursor is on.
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

    /// <summary>
    /// How long the rows may go without a fresh reading before the window says so. Three
    /// ticks rather than one: a single missed reply (a PMDG CDA gap, a dropped request) is
    /// routine and must not flap a warning on and off in front of a screen reader.
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

    private const string UnavailableLine = "Per-tank fuel is not available on this aircraft.";

    private readonly IAircraftDefinition _aircraft;
    private readonly SimConnect.SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer? _announcer;
    private readonly IntPtr _previousWindow;

    private DisplayListBox _list = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    // The last rows that actually carried numbers, and when they arrived. Kept so a
    // transient miss does not blank the list and so staleness can be measured in TIME —
    // the request can fail by never calling back at all (a disconnect returns without
    // invoking the callback), which a "count the nulls" latch would never notice.
    private IReadOnlyList<string>? _lastGoodLines;
    private DateTime _lastGoodUtc;
    private readonly DateTime _openedUtc = DateTime.UtcNow;
    private bool _announcedUnavailable;

    public FuelTanksForm(
        IAircraftDefinition aircraft,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer? announcer = null)
    {
        _aircraft = aircraft;
        _simConnect = simConnect;
        _announcer = announcer;
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
    }

    /// <summary>
    /// First read and timer start live HERE, not in the constructor. IsHandleCreated is
    /// FALSE until Show(), and both the PMDG overrides and the "not wired" base path invoke
    /// their callback INLINE — so a first read issued from the constructor is delivered
    /// before the handle exists and is discarded by the marshal guard, opening the window
    /// empty. Same idiom as WeatherRadarForm and FbwEwdWindow.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = RefreshMs };
        _refreshTimer.Tick += (_, _) => RefreshReadings();
        _refreshTimer.Start();

        RefreshReadings();
    }

    /// <summary>
    /// Pulls live quantities and reconciles the list. NOT named Refresh: that would sit
    /// beside the inherited Control.Refresh() repaint, and a later this.Refresh() meaning
    /// "get new numbers" would silently just invalidate the window instead.
    /// </summary>
    private void RefreshReadings()
    {
        // Re-render from what we already hold FIRST, so a request that never calls back
        // still surfaces as stale rather than as a live-looking freeze.
        RenderFromState();

        _aircraft.RequestFuelTankReadings(_simConnect, readings =>
        {
            if (IsDisposed) return;

            // The stock-fuel path completes on a SimConnect callback; the PMDG path
            // completes inline on this very thread. When we are already on the UI thread
            // apply directly — that needs no window handle, so an inline callback lands
            // even before the handle exists, and it costs no message round-trip.
            if (!InvokeRequired) { Apply(readings); return; }
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() => Apply(readings)));
            }
            catch (InvalidOperationException) { /* handle died mid-close */ }
        });
    }

    private void Apply(IReadOnlyList<FuelTankReading>? readings)
    {
        if (IsDisposed) return;

        if (readings != null && readings.Count > 0)
        {
            _lastGoodLines = FuelTankReadout.BuildLines(readings);
            _lastGoodUtc = DateTime.UtcNow;
        }

        RenderFromState();
    }

    /// <summary>
    /// Paints whatever the window currently knows: live rows, live rows plus a staleness
    /// marker, or the not-available notice. Every exit selects row 0 when the list came
    /// back with no selection — at -1 a screen reader announces the list with no current
    /// item and the pilot cannot tell it from a broken window.
    /// </summary>
    private void RenderFromState()
    {
        if (IsDisposed) return;

        if (_lastGoodLines == null)
        {
            // "Not available" is a VERDICT, so it waits until the readings have had a
            // moment to arrive. The stock-fuel path answers asynchronously and a PMDG's
            // CDA snapshot may still be in flight, so rendering it immediately would
            // flash — and then announce — "this aircraft has no per-tank fuel" at a
            // pilot whose numbers are about to appear.
            bool concluded = DateTime.UtcNow - _openedUtc >= StaleAfter;
            _list.SetLines(new[] { concluded ? UnavailableLine : "Reading fuel quantities..." });
            if (concluded) AnnounceUnavailableOnce();
        }
        else if (DateTime.UtcNow - _lastGoodUtc > StaleAfter)
        {
            // Appended, never inserted — the rows above keep their indices, so the
            // reader's cursor does not move onto a different tank.
            var stale = _lastGoodLines.ToList();
            stale.Add($"Not updating. Figures are from {_lastGoodUtc.ToLocalTime():HH:mm:ss}.");
            _list.SetLines(stale);
        }
        else
        {
            _list.SetLines(_lastGoodLines);
        }

        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    /// <summary>
    /// Says once that this aircraft has no per-tank readout. The list row alone is not
    /// enough — a pilot who never arrows down never learns why the window is empty, and
    /// silence here is indistinguishable from a broken window. The caller decides WHEN
    /// the verdict is safe to speak.
    /// </summary>
    private void AnnounceUnavailableOnce()
    {
        if (_announcedUnavailable) return;
        _announcedUnavailable = true;
        _announcer?.Announce(UnavailableLine);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopRefresh();
        base.OnFormClosed(e);
        // Hand the foreground back to whatever had it (normally the simulator), so the
        // pilot is not left with focus on a dead window and Windows picking a winner.
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    // Form.Dispose() does NOT raise OnFormClosed, so an aircraft-swap Dispose of this
    // window must stop the refresh timer here too (both paths are idempotent).
    protected override void Dispose(bool disposing)
    {
        if (disposing) StopRefresh();
        base.Dispose(disposing);
    }

    private void StopRefresh()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
        _refreshTimer = null!;
    }
}
