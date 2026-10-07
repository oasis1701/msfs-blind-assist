using System.Diagnostics;
using MSFSBlindAssist.Services.SimPerformance;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// File → Sim Performance: the figures a sighted pilot reads off the in-sim developer FPS overlay,
/// as far as they can be measured from outside the sim. One value per row in a
/// <see cref="DisplayListBox"/>: the sim's own frame rate and simulation rate (SimConnect "Frame"
/// event, subscribed only while this window is open), and the simulator process's CPU, memory and
/// GPU use (Windows process and performance counters, sampled in the background). Rows reconcile
/// in place once a second, so the reader's cursor stays put and only the focused row re-reads when
/// its value changes; there is no manual refresh, since nothing can be fresher than that timer
/// (the background sampler itself publishes once a second). Escape closes. The overlay's
/// per-thread millisecond timings and its "limited by" verdict are internal to the sim and are
/// not available here.
/// </summary>
public sealed class SimPerformanceForm : Form
{
    private readonly SimConnectManager _sim;
    private readonly SimResourceSampler _sampler = new();
    private readonly DisplayListBox _list;
    private System.Windows.Forms.Timer? _timer;
    private bool _monitoring;

    public SimPerformanceForm(SimConnectManager sim)
    {
        _sim = sim;

        Text = "Sim Performance";
        Size = new Size(640, 380);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11, FontStyle.Regular),
            TabIndex = 0,
            AccessibleName = "Sim performance",
            AccessibleDescription = "Simulator frame rate and resource usage, one value per row, updated every second. "
                                    + "Read with the arrow keys; a focused row re-reads when its value changes. Escape closes.",
        };
        _list.SetText("Measuring…");

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        var closeButton = new Button
        {
            Text = "&Close", Location = new Point(535, 8), Size = new Size(85, 30), TabIndex = 1, AccessibleName = "Close",
        };
        closeButton.Click += (_, _) => Close();
        bottom.Controls.Add(closeButton);

        Controls.Add(_list);
        Controls.Add(bottom);
        CancelButton = closeButton;

        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            _list.Focus();
            _sim.StartFrameRateMonitoring();
            _monitoring = true;
            _sampler.Start();
            RefreshRows();
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (_, _) => RefreshRows();
            _timer.Start();
        };
    }

    /// <summary>Compose the latest readings and reconcile the rows in place (no-op when unchanged).</summary>
    private void RefreshRows()
    {
        if (IsDisposed) return;
        FrameRateSample frame = _sim.FrameRateMeter.Sample(Stopwatch.GetTimestamp(), Stopwatch.Frequency * 2);
        SimPerformanceSnapshot snapshot = _sampler.Latest with
        {
            SimConnected = _sim.IsConnected,
            FrameRate = frame.AverageFrameRate,
            SimRate = frame.SimRate,
        };
        _list.SetLines(SimPerformanceFormatter.Lines(snapshot));
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Teardown();
        base.OnFormClosed(e);
    }

    // Form.Dispose() does not raise OnFormClosed, so an owner-driven Dispose must release the
    // frame subscription and the sampler here too (both paths are idempotent).
    protected override void Dispose(bool disposing)
    {
        if (disposing) Teardown();
        base.Dispose(disposing);
    }

    private void Teardown()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        if (_monitoring)
        {
            _monitoring = false;
            _sim.StopFrameRateMonitoring();
        }
        _sampler.Dispose();
    }
}
