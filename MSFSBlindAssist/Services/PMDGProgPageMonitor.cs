using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// PMDG 777 PROG-page reader for the Enhanced distance announcements.
///
/// <para><b>Two phases of operation.</b></para>
///
/// 1. <b>Init phase (timer-driven, one-shot).</b> When <see cref="Start"/>
///    is called, a Windows Forms Timer ticks at <see cref="InitTickMs"/>
///    until the right CDU returns parsable rows. If the CDU is unpowered
///    the timer slows to <see cref="InitRetryMs"/> (30 s) until power
///    returns. Once we have rows, the monitor either parses PROG (if the
///    CDU is already on it) or sends a single <c>EVT_CDU_R_PROG</c> to
///    move it there, then stops the timer. After init, the monitor sits
///    idle — no more ticks, no more events, no fighting the user when
///    they navigate the right CDU somewhere else.
///
/// 2. <b>On-demand reads (<see cref="ReadProgPageAsync"/>).</b> Called
///    from the Output D / Shift+D distance handlers. Reuses a recently
///    cached <see cref="LastProgData"/> when fresh; otherwise probes the
///    CDU live. If the user has navigated away from PROG, the probe sends
///    <c>EVT_CDU_R_PROG</c>, waits for the page to render, requests a
///    fresh CDU snapshot, parses, and returns. Worst-case latency
///    (cold-cache, off-PROG) is ~400-500 ms; warm-cache reads are
///    instant.
///
/// <para><b>Why we still init.</b></para>
///
/// Pressing D for the first time while the CDU is on MENU or LEGS would
/// otherwise pay the full ~500 ms penalty. The init pass primes the CDU
/// onto PROG before any user input arrives, so the typical first press
/// gets cached data with zero latency. Init is also the right place to
/// honor the user's spec: "retry every 30 s if the power is off until
/// the page is displayed."
/// </summary>
public class PMDGProgPageMonitor : IDisposable
{
    /// <summary>Right CDU. PMDG SDK indexes: 0=Captain (left), 1=F/O (right), 2=Observer (center).</summary>
    private const int RIGHT_CDU = 1;

    /// <summary>Init-phase tick when the CDU is up. Fast — we're racing to prime the cache before the user asks.</summary>
    private const int InitTickMs = 1_000;

    /// <summary>Init-phase tick when the CDU is unpowered. User's spec: retry every 30 s.</summary>
    private const int InitRetryMs = 30_000;

    /// <summary>How long a cached <see cref="LastProgData"/> can be reused without re-probing.</summary>
    private const int CacheValiditySeconds = 30;

    /// <summary>Time to wait for PMDG to render PROG after sending <c>EVT_CDU_R_PROG</c>.</summary>
    private const int PageRenderDelayMs = 250;

    /// <summary>Time to wait for SimConnect to deliver a CDU snapshot after <c>RequestCDUScreen</c>.</summary>
    private const int SnapshotDelayMs = 150;

    private readonly PMDG777DataManager _dataManager;
    private readonly System.Windows.Forms.Timer _initTimer;

    /// <summary>True once init completes successfully (CDU readable; PROG either visible or requested).</summary>
    private bool _initialized;
    private bool _disposed;

    /// <summary>Most recent successfully-parsed PROG-page snapshot, or null.</summary>
    public PMDGProgPageReader.ProgPageData? LastProgData { get; private set; }

    /// <summary>Last error string, surfaced to the UI / logs. Empty when healthy.</summary>
    public string LastError { get; private set; } = "";

    /// <summary>Init in progress or already complete.</summary>
    public bool IsRunning => _initTimer.Enabled || _initialized;

    public PMDGProgPageMonitor(PMDG777DataManager dataManager)
    {
        _dataManager = dataManager;
        _initTimer = new System.Windows.Forms.Timer { Interval = InitTickMs };
        _initTimer.Tick += OnInitTick;
    }

    /// <summary>
    /// Begins the init phase. Idempotent — a second call while init is
    /// already in progress or complete is a no-op. Safe to call from
    /// MainForm's lifecycle hooks (aircraft swap, settings save, startup).
    /// </summary>
    public void Start()
    {
        if (_disposed || _initialized || _initTimer.Enabled) return;
        // Kick a request now so the first init tick (in 1 s) has data to inspect.
        _dataManager.RequestCDUScreen(RIGHT_CDU);
        _initTimer.Interval = InitTickMs;
        _initTimer.Start();
    }

    /// <summary>
    /// Stops the init timer and clears the initialized flag so a future
    /// <see cref="Start"/> redoes the init pass. Used by MainForm when
    /// the user toggles Enhanced mode off or swaps aircraft. Does NOT
    /// clear <see cref="LastProgData"/> — a recently-stopped monitor's
    /// cache is still useful for one final stale read; consumers that
    /// care about freshness check <see cref="PMDGProgPageReader.ProgPageData.LastUpdated"/>.
    /// </summary>
    public void Stop()
    {
        _initTimer.Stop();
        _initialized = false;
    }

    private void OnInitTick(object? sender, EventArgs e)
    {
        if (_disposed) return;
        try
        {
            // Prime the request before reading so this tick sees fresh data,
            // not a stale cache carried over from a prior session.
            _dataManager.RequestCDUScreen(RIGHT_CDU);

            var rows = _dataManager.GetCDURows(RIGHT_CDU);

            if (rows == null)
            {
                // Powered off (or no snapshot received yet) — slow the retry
                // to 30 s. The next tick will retry.
                LastError = "Right CDU not powered yet";
                _initTimer.Interval = InitRetryMs;
                return;
            }

            // CDU is readable. Either we're on PROG already, or we need to
            // switch once. Either way init is done after this tick — we
            // explicitly stop the timer so there's no further background
            // activity until the user requests data.
            LastError = "";

            if (PMDGProgPageReader.IsProgPage(rows))
            {
                var parsed = PMDGProgPageReader.Parse(rows);
                if (parsed != null) LastProgData = parsed;
            }
            else
            {
                // Send PROG once and let the page render. We don't wait or
                // re-fetch — the next user-driven ReadProgPageAsync call
                // will see the updated state. If the user is fast enough to
                // press D before PMDG renders the page, ReadProgPageAsync's
                // own retry-with-delay handles it.
                //
                // CDA path (SendEvent with parameter 1 = pressed). PROG
                // navigates correctly via CDA. NOTE: this is NOT universal —
                // FMCCOMM/HOLD must use TransmitClientEvent + a single-click
                // flag (they go dead on the second press via CDA; issue #46).
                // Don't generalize "CDA works" to those keys.
                if (PMDG777Definition.EventIds.TryGetValue("EVT_CDU_R_PROG", out int progEventId))
                {
                    _dataManager.SendEvent("EVT_CDU_R_PROG", (uint)progEventId, 1);
                }
            }

            _initialized = true;
            _initTimer.Stop();
        }
        catch (Exception ex)
        {
            LastError = $"PROG init: {ex.GetType().Name}: {ex.Message}";
            Log.Debug("Services", $"[PMDGProgPageMonitor] {LastError}");
        }
    }

    /// <summary>
    /// On-demand probe used by the Output D / Shift+D handlers. Reuses
    /// <see cref="LastProgData"/> if it was updated within
    /// <see cref="CacheValiditySeconds"/>; otherwise lives-fetches.
    ///
    /// Live-fetch flow:
    ///   1. Request a fresh CDU 1 snapshot, wait <see cref="SnapshotDelayMs"/>
    ///      for SimConnect to deliver it.
    ///   2. If still null → CDU is off → return null (caller falls back to SDK).
    ///   3. If on PROG → parse and return.
    ///   4. If on a different page → send <c>EVT_CDU_R_PROG</c>, wait
    ///      <see cref="PageRenderDelayMs"/> for PMDG to render, request a
    ///      fresh snapshot, wait again, parse, return.
    ///
    /// This is the only place we re-trigger PMDG navigation outside of
    /// init — keeping it user-driven means we never fight the user when
    /// they're working on a different page.
    /// </summary>
    public async Task<PMDGProgPageReader.ProgPageData?> ReadProgPageAsync()
    {
        if (_disposed) return null;

        // Cache reuse: rapid back-to-back D / Shift+D presses don't re-probe.
        if (LastProgData != null
            && LastProgData.IsValid
            && (DateTime.Now - LastProgData.LastUpdated).TotalSeconds < CacheValiditySeconds)
        {
            return LastProgData;
        }

        // Step 1 — fresh snapshot of whatever the right CDU is currently showing.
        _dataManager.RequestCDUScreen(RIGHT_CDU);
        await Task.Delay(SnapshotDelayMs);
        var rows = _dataManager.GetCDURows(RIGHT_CDU);
        if (rows == null)
        {
            LastError = "Right CDU not powered";
            return null;
        }

        // Step 2 — if not on PROG, switch and re-fetch.
        if (!PMDGProgPageReader.IsProgPage(rows))
        {
            if (!PMDG777Definition.EventIds.TryGetValue("EVT_CDU_R_PROG", out int progEventId))
            {
                LastError = "EVT_CDU_R_PROG event id missing";
                return null;
            }
            // CDA path (parameter 1 = pressed) — same as PMDG777CDUForm's
            // PROG button. See init-tick comment.
            _dataManager.SendEvent("EVT_CDU_R_PROG", (uint)progEventId, 1);
            await Task.Delay(PageRenderDelayMs);
            _dataManager.RequestCDUScreen(RIGHT_CDU);
            await Task.Delay(SnapshotDelayMs);
            rows = _dataManager.GetCDURows(RIGHT_CDU);
            if (rows == null)
            {
                LastError = "Right CDU went unpowered mid-probe";
                return null;
            }
            if (!PMDGProgPageReader.IsProgPage(rows))
            {
                // PMDG didn't switch in time — could be a ground-event
                // suppression, could be slow rendering. Caller falls back
                // to the SDK readout.
                LastError = "PROG page did not render in time";
                return null;
            }
        }

        var parsed = PMDGProgPageReader.Parse(rows);
        if (parsed != null) LastProgData = parsed;
        LastError = "";
        return parsed;
    }

    public void Dispose()
    {
        _disposed = true;
        _initTimer.Stop();
        _initTimer.Tick -= OnInitTick;
        _initTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
