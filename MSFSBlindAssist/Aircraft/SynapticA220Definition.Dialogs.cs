using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// FCP set dialogs (Ctrl+S/H/A/V, Ctrl+P, Ctrl+N; Ctrl+B is the Avionics partial's altimeter window). No direct-set FCP surface exists —
/// re-proven 2026-08-10 against the live WASM and again 2026-09-21 (12000 written to
/// AP_ALT_VAR_SET_ENGLISH, selector did not move): HEADING_BUG_SET / AP_SPD_VAR_SET /
/// AP_ALT_VAR_SET_ENGLISH / AP_VS_VAR_SET_ENGLISH all leave the FCP untouched, the
/// inc/dec events ignore their parameter, and the aircraft's whole JS→WASM CommBus call
/// surface (grep `.call("A22X.` across html_ui) has no FCP channel. So every value entry
/// drives a knob walk with read-back verification. Two walk flavors: the ALTITUDE walk is
/// GRID-based (the selector snaps to round thousands/hundreds per ring — proven live),
/// speed and heading are the EXACT walk (one unit a click, known), and everything else is
/// the MEASURED walk (a probe click measures the step, then aimed bursts with a read-back,
/// A220Afdx.RunKnobWalkAsync — the standby baro walk's decision, shared).
///
/// The READ-BACK prefers the 30 Hz "A22X.FCP Data" CommBus block ([A220-8]). Without it,
/// altitude and heading read their stock AUTOPILOT ALTITUDE LOCK VAR / HEADING LOCK DIR,
/// which track the knob click for click (measured 2026-09-21), by a DELIVERED read
/// (ReadFreshAsync), never a sleep and the cache; so a session with no Coherent display
/// link still gets the same walks, just with a slower read. Do not re-introduce the belief
/// that those two are not published — that is what used to strand link-less sessions on
/// the degraded fallback walk. The stock AIRSPEED HOLD VAR is the exception: it does NOT
/// follow this FCP (445 kt with 142 in the window, 2026-09-24), so the speed walk refuses
/// without the block; so do the V/S and FPA walks (the FCP never writes the stock V/S var, and
/// L:A22X Selected FPA was never shown to track) and the stab trim walk ([A220-13]);
/// A220Afdx.KnobWalkMayUseStockVar is the one decision.
///
/// Every walk has its OWN cancellation per knob (speed, heading, altitude, the V/S-FPA wheel,
/// stab trim): a new entry stops only the previous walk of the same knob, which then ends
/// silently because the newer one speaks; walks of different knobs run side by side, and their
/// results queue rather than cut each other off (KnobWalkVoice). Every way out of a walk is
/// spoken once (CORE-7): the result, a refusal, "not confirmed", or "walk stopped" after a
/// logged failure.
///
/// Dialog completeness rule (user ruling 2026-07-27): each dialog carries buttons for
/// ALL functions of its physical control, and every mode press announces the RESULT.
/// Actual state CHANGES are spoken by the FG monitor; the dialogs' deferred check adds
/// the honest "no change" call so a press is never followed by silence.
/// </summary>
public partial class SynapticA220Definition
{
    private Forms.A220.A220AutopilotWindow? _autopilotWindow;

    private void ShowAutopilotWindow(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (_autopilotWindow == null || _autopilotWindow.IsDisposed)
            _autopilotWindow = new Forms.A220.A220AutopilotWindow(this, simConnect, announcer);
        _autopilotWindow.ShowForm();
    }

    /// <summary>Pulse an L:var press flag from a dialog toggle (VNAV, XFR, baro STD…).</summary>
    internal void PulsePanelFlag(SimConnectManager simConnect, string bareName) => PulseLVar(simConnect, bareName);

    internal void FireFcpEvent(SimConnectManager simConnect, string eventName) => FireKeyEvent(simConnect, eventName);

    internal void AnnounceModeResultPublic(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        string stateKey, string engagedText, string offText)
        => AnnounceModeResult(simConnect, announcer, stateKey, engagedText, offText);

    internal double CachedPublic(SimConnectManager simConnect, string key, double fallback = 0)
        => Cached(simConnect, key, fallback);

    /// <summary>
    /// FD buttons are MOMENTARY (A220_ButtonMomentary in Interior/Glareshield/
    /// Autopilot.xml): the L:var is the press pulse, not the FD's state. So this presses
    /// (1 then 0) — it must NOT read the L:var back and write the opposite level, which
    /// is what it used to do and why the button state never matched the real FD.
    /// </summary>
    internal void ToggleFlightDirector(SimConnectManager simConnect, bool left)
        => PulseLVar(simConnect, left ? "A22X L Flight Director" : "A22X R Flight Director");

    /// <summary>Real FD state (FG CommBus), for the autopilot window's button labels.</summary>
    internal string FlightDirectorStateText(bool left)
        => A220.A220Afdx.FdStateText(left ? LatestAutoflight?.l_fd : LatestAutoflight?.r_fd);

    /// <summary>Normalize a heading delta to [-180, 180] so 340→020 walks +40, never -320.</summary>
    internal static double WrapHeadingDelta(double delta)
    {
        while (delta > 180) delta -= 360;
        while (delta < -180) delta += 360;
        return delta;
    }

    // ---- one walk per knob ---------------------------------------------------

    /// <summary>Each knob's walk in flight, by its own cancellation (R1 of the 2026-10-16 walk
    /// review). Every caller of <see cref="BeginKnobWalk"/> and <see cref="CancelAllKnobWalks"/> is
    /// on the UI thread (a dialog's entry, HandleUIVariableSet, the ECL form's actuation,
    /// StopAllMotion); the walks that continue on a pool thread take the token they were handed
    /// and never begin one. The lock is cheap insurance, not a known need.</summary>
    private readonly Dictionary<A220.A220Afdx.FcpKnob, System.Threading.CancellationTokenSource> _knobWalks = new();

    /// <summary>
    /// Start a walk of <paramref name="knob"/>: cancels that knob's previous walk only (it ends
    /// silently; this one speaks) and returns the new walk's token. A walk of another knob runs on
    /// — the shared token this replaced let "heading 240, then altitude 5000" abandon the heading
    /// partway with no word (the standby baro walk had the same bug, 6685f0df). The old walk is
    /// cancelled outside the lock, since a cancelled await can resume inline on this thread.
    /// </summary>
    private System.Threading.CancellationToken BeginKnobWalk(A220.A220Afdx.FcpKnob knob)
    {
        var cts = new System.Threading.CancellationTokenSource();
        System.Threading.CancellationTokenSource? previous;
        lock (_knobWalks)
        {
            _knobWalks.TryGetValue(knob, out previous);
            _knobWalks[knob] = cts;
        }
        previous?.Cancel();
        return cts.Token;
    }

    /// <summary>Stops every knob walk (StopAllMotion, aircraft swap).</summary>
    private void CancelAllKnobWalks()
    {
        List<System.Threading.CancellationTokenSource> all;
        lock (_knobWalks)
        {
            all = _knobWalks.Values.ToList();
            _knobWalks.Clear();
        }
        foreach (var cts in all) cts.Cancel();
    }

    /// <summary>
    /// Refuses ALOUD, before any click, when the simulator connection cannot carry the walk's
    /// clicks ([MD11-16]'s pattern): without it a dead connection said "knob is not responding",
    /// the wrong cause. The clicks are calc-path writes (<see cref="FireKnobBurstAsync"/>), so this
    /// asks <see cref="SimConnectManager.CanExecuteCalculatorCode"/>, the gate
    /// <c>ExecuteCalculatorCode</c> itself uses. Not <c>CalcWriteCanLand</c>: the A220 registers no
    /// bridge probe, so its verdict concludes unverified at once and would refuse every walk, while
    /// every A220 write goes through the calculator unconditionally ([A220-1]). The refusal is said
    /// through the walk's own token-checked <paramref name="say"/>, so a walk superseded meanwhile
    /// (one continued on a pool thread, such as V/S after its engage wait) stays silent.
    /// </summary>
    private static bool KnobWalkCanSend(SimConnectManager simConnect, string name, Action<string> say)
    {
        if (simConnect.CanExecuteCalculatorCode) return true;
        FcpLog.Warn($"{name}: not connected, walk refused");
        say(A220.A220Afdx.KnobWalkNotConnectedText(name));
        return false;
    }

    /// <summary>
    /// How every knob walk speaks: only while its own walk is live (a walk superseded by a newer
    /// entry for its knob, or stopped by an aircraft swap, says nothing — the newer walk, or the
    /// next aircraft, speaks for itself), and WITHOUT interrupting. Walks of different knobs run
    /// side by side, so "heading 240, then altitude 5000" can finish within a moment of each other:
    /// with AnnounceImmediate the second result cut the first off, and the pilot heard only one. A
    /// queued line is posted to the UI thread (the one this walk began on, else the display pump's),
    /// where MainForm's announcer.Suppressed wrap is never in progress; the token is checked again
    /// when it is spoken.
    /// </summary>
    private Action<string> KnobWalkVoice(ScreenReaderAnnouncer announcer, System.Threading.CancellationToken token)
    {
        var ui = SynchronizationContext.Current ?? _displaySyncCtx;
        return text =>
        {
            if (token.IsCancellationRequested) return;
            void Speak()
            {
                try { if (!token.IsCancellationRequested) announcer.Announce(text); }
                catch (Exception ex) { Utils.Logging.Log.Warn("A220", $"Knob walk speech: {ex.Message}"); }
            }
            if (ui != null) ui.Post(_ => Speak(), null);
            else Speak();
        };
    }

    // ---- the measured FCP knob walk (V/S, FPA, stab trim, metres altitude) -----

    /// <summary>How long one delivered read of a stock var may take: the 1 Hz batch's next
    /// sample, plus margin. Past it the read is "not confirmed", never a guess.</summary>
    private const int KnobFreshReadTimeoutMs = 1500;

    /// <summary>One delivered read of a stock var for a knob walk, or null when none came back.</summary>
    private static async Task<double?> ReadKnobStockVarAsync(SimConnectManager simConnect, string key,
        System.Threading.CancellationToken token)
    {
        double? value = await simConnect.ReadFreshAsync(key, KnobFreshReadTimeoutMs, token);
        return value is { } v && !double.IsNaN(v) ? v : null;
    }

    /// <summary>The pace the measured and altitude walks start their bursts at. The aircraft takes
    /// about one knob event per sim frame and drops faster ones; the walks' old 8 ms (later the calc
    /// path's 25 ms) and 45 ms were below the ~70-77 ms a click it took live on 2026-09-24, so a
    /// burst lost clicks by design. From here <see cref="A220.A220Afdx.NextKnobPaceMs"/> slows the
    /// pace in proportion to the clicks a burst lost (up to 120 ms).</summary>
    private const int KnobWalkStartPaceMs = 60;

    /// <summary>
    /// The value once the knob has STOPPED moving: 250 ms after the burst, then reads 120 ms apart
    /// until three in a row agree (2.5 s at most), so a burst still being applied is never mistaken
    /// for a short one (the speed, heading and altitude walks' lesson, 2026-09-24). On a stock var
    /// (<paramref name="fast"/> false) ONE delivered read: its next 1 Hz sample is taken after the
    /// burst. A link lost mid-loop has already given a delivered read: stop there.
    /// </summary>
    private static async Task<double?> ReadSteadyAsync(Func<Task<double?>> read, Func<bool> fast,
        System.Threading.CancellationToken token)
    {
        await System.Threading.Tasks.Task.Delay(250, token);
        if (!fast()) return await read();
        double? last = await read();
        int stable = 0;
        var until = Environment.TickCount64 + 2500;
        while (fast() && stable < 2 && Environment.TickCount64 < until)
        {
            await System.Threading.Tasks.Task.Delay(120, token);
            double? v = await read();
            stable = v == last ? stable + 1 : 0;
            last = v;
        }
        return last;
    }

    /// <summary>
    /// The measured FCP knob walk, for the knobs whose step is not known up front: V/S, FPA, the
    /// stab trim and the altitude selector stuck in metres. No direct-set FCP events exist on this
    /// aircraft ([A220-7]), so the value is walked and verified, reading back from the aircraft's own
    /// AFDX block (30 Hz <c>A22X.FCP Data</c>, 20 Hz <c>A22X.Flight Control Data</c>).
    ///
    /// The walk is <see cref="A220.A220Afdx.RunKnobWalkAsync"/> on
    /// <see cref="A220.A220Afdx.FcpWalkPolicy"/>, the decision the standby baro walk runs too, and
    /// this method is its I/O. Repaired on 2026-10-16 (R2 of the walk review) against the weakest walk
    /// the review found: the step is measured on THIS walk by a probe click, confirmed by a second
    /// single click before the first burst (a probe from off the grid only snaps), and never kept across
    /// walks or shrunk by a lossy burst; a straddle is judged only after one click and ends on the
    /// nearer value; an overshoot after a burst aims back (the old walk stopped there as "nearest
    /// selectable"); out of rounds it says "could not reach … now …"; the pace adapts from 60 ms
    /// instead of firing 8 ms apart; and every decision reads a settled value
    /// (<see cref="ReadSteadyAsync"/>). Direction is calibrated on the probe, never assumed (the
    /// stabilizer is the one that would hurt).
    ///
    /// Where the aircraft's block gives no value, only a knob whose stock var tracks it
    /// (<see cref="A220.A220Afdx.KnobWalkMayUseStockVar"/>: of these, the altitude) reads that var,
    /// by a DELIVERED read. The others — the stab trim ([A220-13]), V/S and FPA (R3) — retry the block
    /// with a resync every read and never latch off; with none they refuse before any click, or stop
    /// part-way as "not confirmed".
    /// </summary>
    /// <param name="knob">Which knob: the fallback decision, and the refusal wording.</param>
    /// <param name="token">This knob's walk (<see cref="BeginKnobWalk"/>): a newer entry for
    /// the same knob cancels it, and a cancelled walk says nothing.</param>
    /// <param name="liveRead">Reads this knob's value out of a fresh AFDX block.</param>
    /// <param name="stockKey">The stock var for a knob allowed to fall back to one, else null.</param>
    /// <param name="note">Optional extra clause appended to the final announcement, given
    /// the landed value, whether it hit the target, and the MEASURED step. The step is passed out
    /// because when a knob cannot reach a round number, its step size is the whole explanation.</param>
    private void StartFcpWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        A220.A220Afdx.FcpKnob knob, System.Threading.CancellationToken token,
        Func<A220.A220Afdx.LiveBlocks, double?> liveRead, string? stockKey,
        string incEvent, string decEvent, double target, double tolerance,
        string valueName, Func<double, string> fmt,
        Func<double, bool, double, string>? note = null)
    {
        string? fallbackKey = A220.A220Afdx.KnobWalkMayUseStockVar(knob) ? stockKey : null;
        var say = KnobWalkVoice(announcer, token);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (!KnobWalkCanSend(simConnect, valueName, say)) return;
                bool live = true;

                // One read of the knob's value, or null when nothing reports it (never a guess).
                // The aircraft's block first, with a resync and retries (a quiet store is not a down
                // link). A knob with a stock var: once the block misses, live latches OFF for the rest
                // of the walk and the stock var is read by a delivered read. A knob with none: null.
                async Task<double?> ReadAsync()
                {
                    if (live)
                    {
                        if (await ReadAfdxFieldAsync(liveRead, token) is { } read) return liveRead(read);
                        if (fallbackKey == null) return null;
                        live = false;
                        FcpLog.Info($"{valueName}: aircraft data unavailable, reading {fallbackKey}");
                    }
                    return await ReadKnobStockVarAsync(simConnect, fallbackKey!, token);
                }

                // Nothing reports the value: refuse before any click, never walk on a guess.
                if (await ReadAsync() is not { } start)
                {
                    FcpLog.Warn($"{valueName}: no read-back, walk refused (target {fmt(target)})");
                    say(A220.A220Afdx.KnobWalkRefusalText(knob, valueName));
                    return;
                }
                int paceMs = KnobWalkStartPaceMs;
                FcpLog.Info($"{valueName}: walk {fmt(start)} -> {fmt(target)} ({(live ? "aircraft data" : fallbackKey)})");
                var result = await A220.A220Afdx.RunKnobWalkAsync(target, start, A220.A220Afdx.FcpWalkPolicy(tolerance),
                    async move =>
                    {
                        string ev = move.Direction > 0 ? incEvent : decEvent;
                        await FireKnobBurstAsync(simConnect, ev, move.Clicks, token, paceMs);
                        double? after = await ReadSteadyAsync(ReadAsync, () => live, token);
                        FcpLog.Info(after is { } a
                            ? $"{valueName}: {ev} x{move.Clicks} @{paceMs}ms: {fmt(move.From)} -> {fmt(a)}"
                            : $"{valueName}: {ev} x{move.Clicks} @{paceMs}ms from {fmt(move.From)}: read-back lost");
                        if (after is { } landed)
                            paceMs = A220.A220Afdx.NextKnobPaceMs(paceMs, move.Clicks,
                                A220.A220Afdx.KnobWalkMovedClicks(landed - move.From, move.Step, move.Clicks));
                        return after;
                    }, token);
                FcpLog.Info($"{valueName}: {result.End} at {fmt(result.Value)} after {result.Rounds} rounds (step {result.Step})");
                string text = A220.A220Afdx.FcpWalkResultText(result, valueName, target, fmt);
                if (note != null && result.End is A220.A220Afdx.KnobWalkEnd.OnTarget
                        or A220.A220Afdx.KnobWalkEnd.NearestSelectable or A220.A220Afdx.KnobWalkEnd.RoundLimit)
                    text += note(result.Value, result.End == A220.A220Afdx.KnobWalkEnd.OnTarget, result.Step ?? 0);
                say(text);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A walk never throws into the UI, and never stops without saying so (CORE-15 log).
                Utils.Logging.Log.Warn("A220", $"{valueName} walk: {ex.GetType().Name}: {ex.Message}");
                say(A220.A220Afdx.KnobWalkFailedText(valueName));
            }
        });
    }

    private static readonly Utils.Logging.LogChannel FcpLog = Utils.Logging.Log.Channel("a220_fcp");

    /// <summary>
    /// Read the AFDX blocks straight from the aircraft until <paramref name="field"/> has a
    /// value, asking for a whole copy when it is missing (<see cref="A220.A220Afdx.AfdxReadAttempts"/>,
    /// <see cref="A220.A220Afdx.AfdxResyncAfterMiss"/>), all within
    /// <see cref="A220.A220Afdx.AfdxReadDeadlineMs"/> of wall clock. The AFDX stores broadcast only
    /// CHANGES and the agent calls a store quiet for 3 s stale, so a missing field is often a quiet
    /// store, not a down link: the FCP store sat at {} until a resync (measured live 2026-09-24), and
    /// the flight-control store goes quiet with the trim at rest. A missing field is what used to
    /// drop the speed walk onto the stock var (445 kt with 142 in the window), and what would
    /// make the trim walk say the link is down while it is up. Null only when every attempt missed
    /// or the deadline passed (a hung-but-open view answers no eval for 5 s); <paramref name="seen"/>
    /// gets every block that did arrive. Cancellation throws, so a superseded walk never reads its
    /// cancellation as a lost read-back.
    /// </summary>
    private async Task<A220.A220Afdx.LiveBlocks?> ReadAfdxFieldAsync(
        Func<A220.A220Afdx.LiveBlocks, double?> field, System.Threading.CancellationToken token,
        Action<A220.A220Afdx.LiveBlocks>? seen = null)
    {
        long deadline = Environment.TickCount64 + A220.A220Afdx.AfdxReadDeadlineMs;
        int Left() => (int)Math.Max(0, deadline - Environment.TickCount64);
        for (int attempt = 0; attempt < A220.A220Afdx.AfdxReadAttempts && Left() > 0; attempt++)
        {
            var blocks = await ReadAfdxLiveAsync(token, Left());
            if (blocks != null)
            {
                seen?.Invoke(blocks);
                if (field(blocks) != null) return blocks;
            }
            if (A220.A220Afdx.AfdxResyncAfterMiss(attempt) && Left() > 0)
                await CallAgentWithinAsync("afdxResync()", Left(), token);
            await System.Threading.Tasks.Task.Delay(Math.Min(150, Left()), token);
        }
        token.ThrowIfCancellationRequested();
        return null;
    }

    /// <summary>The FCP block once <paramref name="field"/> has a value (<see cref="ReadAfdxFieldAsync"/>).</summary>
    private async Task<A220.A220Afdx.FcpBlock?> ReadFcpFieldAsync(
        Func<A220.A220Afdx.FcpBlock, double?> field, System.Threading.CancellationToken token,
        Action<A220.A220Afdx.FcpBlock>? seen = null)
        => (await ReadAfdxFieldAsync(b => b.fcp is { } fcp ? field(fcp) : null, token,
            seen == null ? null : b => { if (b.fcp != null) seen(b.fcp); }))?.fcp;

    /// <summary>
    /// Set a ONE-UNIT-PER-CLICK FCP knob (selected IAS: 1 kt, heading bug: 1°) to an
    /// exact value. Replaces the self-calibrating walk for these two knobs, whose
    /// measured-step and direction-swap logic turned any bad read-back into hundreds of
    /// clicks each way: live 2026-09-24 every speed entry fired 10 rounds of the 220-click
    /// cap, alternating INC/DEC, because the stock read-back (AUTOPILOT AIRSPEED HOLD VAR,
    /// ~445 kt) never moved with the knob. Here:
    ///   * the step is KNOWN (the aircraft's own XML binds the knob to the stock
    ///     ..._INC/_DEC events, one unit each — measured 80→81 kt, 246→249° for 3 clicks),
    ///     so a burst is never larger than the remaining gap and can never overshoot;
    ///   * the read-back waits until the value STOPS changing, so a burst still being
    ///     applied is never mistaken for a short one;
    ///   * lost clicks just cost another round; a knob that does not move at all, or
    ///     moves the wrong way, stops the walk and says so;
    ///   * with no trustworthy read-back the walk REFUSES — it never clicks blind; a
    ///     read-back lost part-way is "not confirmed", never the value before the burst.
    /// Every round is written to a220_fcp.log.
    /// </summary>
    /// <param name="knob">Speed or heading: whether <paramref name="stockKey"/> may stand in
    /// (<see cref="A220.A220Afdx.KnobWalkMayUseStockVar"/>).</param>
    /// <param name="token">This knob's walk (<see cref="BeginKnobWalk"/>).</param>
    /// <param name="liveField">The value in the aircraft's FCP block.</param>
    /// <param name="stockKey">A stock var proven to track this knob click for click, read
    /// (delivered, never the cache) only when the FCP block is unavailable (heading); null =
    /// no fallback (speed).</param>
    private void StartExactKnobWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        A220.A220Afdx.FcpKnob knob, System.Threading.CancellationToken token,
        string name, string incEvent, string decEvent, int target, bool headingWrap,
        Func<A220.A220Afdx.FcpBlock, double?> liveField, string? stockKey,
        Func<double, string> fmt, Func<double, string>? afterNote = null)
    {
        string? cacheKey = A220.A220Afdx.KnobWalkMayUseStockVar(knob) ? stockKey : null;
        // A superseded or swapped-out walk says nothing, and never cuts off another knob's result.
        var say = KnobWalkVoice(announcer, token);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (!KnobWalkCanSend(simConnect, name, say)) return;
                bool live = true;
                async Task<double?> ReadOnceAsync()
                {
                    if (live)
                    {
                        var fcp = await ReadFcpFieldAsync(liveField, token);
                        if (fcp != null) return liveField(fcp);
                        if (cacheKey == null) return null;
                        live = false;
                        FcpLog.Info($"{name}: FCP data unavailable, reading {cacheKey}");
                    }
                    return await ReadKnobStockVarAsync(simConnect, cacheKey!, token);
                }
                // Wait until the value stops changing (live); on the stock var, ONE delivered read.
                Task<double?> ReadSettledAsync() => ReadSteadyAsync(ReadOnceAsync, () => live, token);
                double Delta(double from)
                {
                    double d = target - from;
                    return headingWrap ? WrapHeadingDelta(d) : d;
                }

                double? start = await ReadOnceAsync();
                if (start == null)
                {
                    FcpLog.Warn($"{name}: no read-back, walk refused (target {target})");
                    say(A220.A220Afdx.KnobWalkRefusalText(knob, name));
                    return;
                }
                double cur = start.Value;
                string up = incEvent, down = decEvent;
                bool swapped = false;
                // The aircraft takes about one knob event per sim frame; faster ones are
                // dropped (live 2026-09-24 at 20 ms: speed 49 of 77 clicks landed, heading
                // 21 of 58). Every round still re-aims from the read-back so a loss only
                // costs time, but at that rate a 180° heading change runs out of rounds —
                // so the pace adapts to the delivery the aircraft actually shows.
                int paceMs = 25;
                FcpLog.Info($"{name}: walk {fmt(cur)} -> {fmt(target)} ({(live ? "FCP data" : cacheKey)})");

                for (int round = 0; round < 16 && !token.IsCancellationRequested; round++)
                {
                    double delta = Delta(cur);
                    int clicks = A220.A220Afdx.ExactKnobClicks(delta);
                    if (clicks == 0) break;
                    string ev = delta > 0 ? up : down;
                    await FireKnobBurstAsync(simConnect, ev, clicks, token, paceMs: paceMs);
                    double? after = await ReadSettledAsync();
                    if (after == null)
                    {
                        // The burst may have moved the knob: claim no value (R7).
                        FcpLog.Warn($"{name}: read-back lost after {ev} x{clicks} from {fmt(cur)}");
                        say(A220.A220Afdx.KnobWalkNotConfirmedText(name));
                        return;
                    }
                    double moved = headingWrap ? WrapHeadingDelta(after.Value - cur) : after.Value - cur;
                    FcpLog.Info($"{name}: round {round} {ev} x{clicks} @{paceMs}ms: {fmt(cur)} -> {fmt(after.Value)}");
                    paceMs = A220.A220Afdx.NextKnobPaceMs(paceMs, clicks, Math.Abs(moved));
                    if (Math.Abs(moved) < 0.5)
                    {
                        say($"{name} knob is not responding — still {fmt(cur)}.");
                        return;
                    }
                    if (Math.Sign(moved) != Math.Sign(delta))
                    {
                        // Moved the WRONG way. Correct once; a second time means the
                        // read-back and the knob disagree, and clicking on would chase it.
                        if (swapped)
                        {
                            say($"{name} stopped at {fmt(after.Value)} — the knob is not behaving as expected.");
                            return;
                        }
                        (up, down) = (down, up);
                        swapped = true;
                    }
                    cur = after.Value;
                }
                if (token.IsCancellationRequested) return;
                bool exact = Math.Abs(Delta(cur)) < 0.5;
                string text = exact ? $"{name} {fmt(cur)}" : $"{name} stopped at {fmt(cur)}, asked for {fmt(target)}";
                if (afterNote != null) text += afterNote(cur);
                FcpLog.Info($"{name}: done at {fmt(cur)} ({(exact ? "exact" : "NOT exact")})");
                say(text);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Logged (CORE-15) and said: a walk never ends in silence.
                FcpLog.Warn($"{name}: walk failed: {ex.Message}");
                Utils.Logging.Log.Warn("A220", $"{name} walk: {ex.GetType().Name}: {ex.Message}");
                say(A220.A220Afdx.KnobWalkFailedText(name));
            }
        });
    }

    /// <summary>
    /// Walk the stabilizer trim to a target in the units the EICAS prints — the same
    /// number the EFB's takeoff-performance page gives you (its own code renders that
    /// figure as <c>abs(trimUnitsNoseUp).toFixed(1) + "UP"</c> from a weight/CG table).
    /// There is no set event and no trim L:var: `ELEV_TRIM_UP`/`_DN` are RATE inputs
    /// (documented as "drives the trim rate input this tick"), so this is the same
    /// calibrated walk as the FCP knobs, reading `efcs.pitch_trim` back off the CommBus.
    /// Direction is calibrated on the first move, never assumed — driving a stabilizer
    /// confidently the wrong way is the one failure here that actually matters.
    ///
    /// With the display link down it REFUSES before any click ([A220-13];
    /// <see cref="A220.A220Afdx.StabTrimLinkDownText"/>), and a link lost part-way stops the
    /// walk as "not confirmed" — but only once the resync-and-retry read
    /// (<see cref="ReadAfdxFieldAsync"/>, about 1.5 s and two whole-copy requests, 3 s at most) still finds no
    /// <c>pitch_trim</c>: with the trim at rest the flight-control store goes quiet, and the agent
    /// calls a store quiet for 3 s stale, so one missed read is not a down link.
    /// From 2026-09-22 it walked on a second read-back instead:
    /// "L:A22X Horizontal Stabilizer" is the trim animation ratio over the same 0-17 unit
    /// travel the EICAS draws, and ratio × 17 tracked one 28-click burst monotonically
    /// (<see cref="A220.A220Afdx.StabTrimFullScaleUnits"/>). That is one measurement of a
    /// derived value, against a rule that said refuse; the owner kept the rule on 2026-10-16,
    /// so the stabilizer is never driven on that estimate
    /// (<see cref="A220.A220Afdx.KnobWalkMayUseStockVar"/> is false for it).
    /// </summary>
    internal void StartStabTrimWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, double target)
    {
        var knob = A220.A220Afdx.FcpKnob.StabTrim;
        StartFcpWalk(simConnect, announcer, knob, BeginKnobWalk(knob),
            b => b.fc?.pitch_trim, null, "ELEV_TRIM_UP", "ELEV_TRIM_DN",
            target, 0.05, "Stabilizer trim", A220.A220Afdx.StabTrimUnitsText,
            note: (v, _, _) => A220.A220Afdx.StabTrimWalkBandNote(v, LatestFlightControl));
    }

    /// <summary>The fastest a walk clicks: one calc string every 25 ms at most, the calc
    /// path's pace since the walks began (the exact walk adapts from it, up to 120 ms; the
    /// measured and altitude walks start at <see cref="KnobWalkStartPaceMs"/>).</summary>
    private const int KnobClickMinPaceMs = 25;

    /// <summary>
    /// Fire <paramref name="clicks"/> knob events, ONE PER CALC STRING, at least
    /// <see cref="KnobClickMinPaceMs"/> apart.
    ///
    /// Every click goes through <see cref="FireKeyEvent"/>: the calculator path, with the
    /// <c>{seq} 0 *</c> prefix that keeps MobiFlight from dropping a repeat ([A220-3]). The
    /// walks run on a pool thread, and <see cref="SimConnectManager.SendEvent"/> is safe on the
    /// UI thread only ([SIM-20]: it maps each new event name into an unlocked dictionary); a
    /// calc write is a client-data write, which pool-thread callers already make (the standby
    /// baro walk, the MD-11's CEVENT pump), and it is the transport of every other A220 control.
    ///
    /// Until 2026-10-16 the clicks went by TransmitClientEvent 8 ms apart, with this path as a
    /// fallback when a first burst moved nothing. The 8 ms bought little: three and six
    /// HEADING_BUG events back to back all landed (2026-09-21), but longer bursts did not —
    /// the aircraft takes about one knob event per sim frame and drops faster ones (2026-09-24
    /// at 20 ms: speed 49 of 77, heading 21 of 58), so the exact walk already paces itself from
    /// 25 ms up. With one transport the fallback had nothing to fall back to, and went. The
    /// walks are closed loops: a lost click costs a round, never a wrong claim.
    ///
    /// STILL ONE EVENT PER STRING. Do NOT batch several "(&gt;K:EVENT)" statements into one
    /// calc string ([A220-10]): measured 2026-09-21, four <c>(&gt;K:AP_ALT_VAR_INC)</c> in ONE
    /// string moved the selector by exactly ONE click (2100 → 2200), because a single RPN
    /// evaluation latches the event once.
    /// </summary>
    private async Task FireKnobBurstAsync(SimConnectManager simConnect, string eventName,
        int clicks, System.Threading.CancellationToken token, int paceMs = KnobClickMinPaceMs)
    {
        int pace = Math.Max(KnobClickMinPaceMs, paceMs);
        for (int sent = 0; sent < clicks && !token.IsCancellationRequested; sent++)
        {
            FireKeyEvent(simConnect, eventName);
            await System.Threading.Tasks.Task.Delay(pace, token);
        }
    }

    // ---- Speed (Ctrl+S) ------------------------------------------------------

    /// <summary>
    /// FMS (managed) speed vs manual. The aircraft's own FCP block says it directly:
    /// `spd_fms` is TRUE in FMS speed (the FCP window goes blank) and null in manual
    /// (the window shows the selected number) — FCP/instrument.js Speed.tsx. The ring's
    /// L:var is the fallback, and its polarity is 0 = FMS, 1 = MANUAL (measured live
    /// 2026-09-24: writing 0 made spd_fms true and blanked the window; 1 restored the
    /// number). The old code read it the other way round, so the dialog's toggle said
    /// "Manual" while the aircraft was in FMS speed and vice versa.
    /// </summary>
    internal bool IsFmsSpeed(SimConnectManager simConnect)
    {
        var fcp = LatestFcp;
        if (fcp?.spd_sel_ias != null) return fcp.spd_fms == true;
        return Cached(simConnect, "A22X_FG_SPEED_MODE", 1) < 0.5;
    }

    private bool IsMachSpeed(SimConnectManager simConnect)
        => LatestFcp?.spd_in_mach ?? Cached(simConnect, "A22X_AP_MACH_MODE") > 0.5;

    private void ShowSpeedDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            // Outer ring: FMS SPD <-> MAN SPD, the ring's own L:var (Autopilot.xml:
            // A220_KnobRing on "L:A22X FG Speed Mode"): 0 = FMS, 1 = manual.
            new("&FMS / Manual speed", () => IsFmsSpeed(simConnect) ? "FMS" : "Manual",
                () => WriteLVar(simConnect, "A22X FG Speed Mode", IsFmsSpeed(simConnect) ? 1 : 0)),
            new("&Knots / Mach", () => IsMachSpeed(simConnect) ? "Mach" : "Knots",
                () => FireKeyEvent(simConnect, "AP_MANAGED_SPEED_IN_MACH_TOGGLE")),
        };

        // The stock AUTOPILOT AIRSPEED/MACH HOLD VARs do NOT follow this FCP (live
        // 2026-09-24: 445 kt / M0.69 with 142 kt in the window) — never show them.
        string current = A220.A220Afdx.SpeedPromptNow(LatestFcp, IsMachSpeed(simConnect));
        if (IsFmsSpeed(simConnect)) current += ", FMS speed";
        var dialog = new ValueInputForm(
            "FCP Speed", $"speed (now {current})", "100-350 knots, or Mach 0.10-0.99 (e.g. .78)",
            announcer,
            input =>
            {
                if (!TryParseSpeed(input, out bool isMach, out double val))
                    return (false, "Enter knots 100-350 or Mach like .78");
                return (true, "");
            },
            toggles,
            input =>
            {
                if (!TryParseSpeed(input, out bool isMach, out double val)) return;
                _ = SetSpeedAsync(simConnect, announcer, isMach, val);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>Set the selected speed: put the FCP in the unit that was typed (the knob
    /// moves whichever the window shows), then walk. Says so when the aircraft is still
    /// flying FMS speed, because a selected speed does nothing until Manual is selected.</summary>
    private async Task SetSpeedAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, bool isMach, double val)
    {
        // The speed knob's walk starts here, not at the knob: a newer speed entry also stops
        // this one's knots/Mach switch.
        var knob = A220.A220Afdx.FcpKnob.Speed;
        var token = BeginKnobWalk(knob);
        var say = KnobWalkVoice(announcer, token);
        if (!KnobWalkCanSend(simConnect, "Speed", say)) return;
        try
        {
            var fcp = await ReadFcpFieldAsync(f => f.spd_sel_ias, token);
            if (fcp == null)
            {
                FcpLog.Warn("Speed: no FCP data, set refused");
                say(A220.A220Afdx.KnobWalkRefusalText(knob, "Speed"));
                return;
            }
            if ((fcp.spd_in_mach == true) != isMach)
            {
                FireKeyEvent(simConnect, "AP_MANAGED_SPEED_IN_MACH_TOGGLE");
                bool switched = false;
                for (int i = 0; i < 12 && !switched; i++)
                {
                    await Task.Delay(150, token);
                    var now = await ReadFcpFieldAsync(f => f.spd_sel_ias, token);
                    switched = now != null && (now.spd_in_mach == true) == isMach;
                }
                if (!switched)
                {
                    say($"Speed not set: the FCP did not switch to {(isMach ? "Mach" : "knots")}.");
                    return;
                }
            }
            if (token.IsCancellationRequested) return;
            Func<double, string> note = _ => IsFmsSpeed(simConnect)
                ? ". FMS speed is still active — choose Manual to fly it" : "";
            if (isMach)
                StartExactKnobWalk(simConnect, announcer, knob, token, "Speed", "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC",
                    (int)Math.Round(val * 100), false, f => f.spd_sel_mach is { } m ? Math.Round(m * 100) : null,
                    null, A220.A220Afdx.MachHundredthsText, note);
            else
                StartExactKnobWalk(simConnect, announcer, knob, token, "Speed", "AP_SPD_VAR_INC", "AP_SPD_VAR_DEC",
                    (int)Math.Round(val), false, f => f.spd_sel_ias, null, A220.A220Afdx.KnotsText, note);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Logged (CORE-15) and said: a walk never ends in silence.
            FcpLog.Warn($"Speed: {ex.Message}");
            Utils.Logging.Log.Warn("A220", $"Speed walk: {ex.GetType().Name}: {ex.Message}");
            say(A220.A220Afdx.KnobWalkFailedText("Speed"));
        }
    }

    /// <summary>A typed speed: knots 100-350, or a Mach 0.10-0.99 (".78", "M.78", "0,78"). Parsed as
    /// the pilot typed it on any Windows culture ([CORE-17]): a comma decimal reads as one, and
    /// what could be a thousands group is refused, never guessed.</summary>
    internal static bool TryParseSpeed(string input, out bool isMach, out double value)
    {
        isMach = false;
        value = 0;
        input = input.Trim().ToUpperInvariant().TrimStart('M');
        if (!Utils.NumberParsing.TryParseTyped(input, out value)) return false;
        if (value < 1.0)
        {
            isMach = true;
            return value >= 0.10 && value <= 0.99;
        }
        return value >= 100 && value <= 350;
    }

    // ---- Heading (Ctrl+H) ----------------------------------------------------

    private void ShowHeadingDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            // Full lateral-mode cluster (dialog completeness rule): the same
            // events/state keys as the Ctrl+P FCP window's HDG/NAV/APPR buttons.
            new("&HDG select mode", () => Cached(simConnect, "A22X_FG_HEADING") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_HDG_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_HEADING", "HDG mode", "HDG mode off"); }),
            new("&NAV mode", () => Cached(simConnect, "A22X_FG_LNAV") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_NAV1_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_LNAV", "NAV mode", "NAV mode off"); }),
            new("&Approach mode", () => Cached(simConnect, "A22X_FG_APPROACH") > 0.5 ? "Armed" : "Off",
                () => { FireKeyEvent(simConnect, "AP_APR_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_APPROACH", "Approach mode armed", "Approach mode off"); }),
            // Knob push = sync to current heading (HEADING_BUG_SET is documented as
            // sync, it takes no target).
            new("&Sync to current heading", () => "",
                () => FireKeyEvent(simConnect, "HEADING_BUG_SET")),
            new("Half &bank", () => Cached(simConnect, "A22X_FG_HALF_BANK") > 0.5 ? "On" : "Off",
                () => FireKeyEvent(simConnect, "AP_MAX_BANK_ANGLE_SET")),
        };

        int now = (int)Math.Round(Cached(simConnect, "A22X_AP_HDG"));
        var dialog = new ValueInputForm(
            "FCP Heading", $"heading (now {now:000})", "1-360 degrees", announcer,
            input => int.TryParse(input, out int h) && h >= 1 && h <= 360
                ? (true, "") : (false, "Enter a heading between 1 and 360"),
            toggles,
            input =>
            {
                if (!int.TryParse(input, out int hdg)) return;
                // AUTOPILOT HEADING LOCK DIR tracks this bug exactly (178 = 178 live
                // 2026-09-24), so it is a sound fallback here — unlike the speed vars.
                var knob = A220.A220Afdx.FcpKnob.Heading;
                StartExactKnobWalk(simConnect, announcer, knob, BeginKnobWalk(knob),
                    "Heading", "HEADING_BUG_INC", "HEADING_BUG_DEC",
                    hdg % 360, true, f => f.hdg_sel, "A22X_AP_HDG",
                    d => $"{(int)Math.Round(((d % 360) + 360) % 360 == 0 ? 360 : ((d % 360) + 360) % 360):000}");
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---- Altitude (Ctrl+A) ---------------------------------------------------

    private void ShowAltitudeDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            // The book's default for altitude changes.
            new("F&LC", () => Cached(simConnect, "A22X_FG_FLC") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "FLIGHT_LEVEL_CHANGE");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_FLC", "FLC engaged", "FLC off"); }),
            new("&VNAV", () => Cached(simConnect, "A22X_FG_VNAV") > 0.5 ? "On" : "Off",
                () => { PulseLVar(simConnect, "A22X FG VNAV Toggle");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_VNAV", "VNAV on", "VNAV off"); }),
            new("Altitude &hold", () => Cached(simConnect, "A22X_FG_ALT") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_ALT_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_ALT", "Altitude hold engaged", "Altitude hold off"); }),
            // Label from the aircraft's OWN alt_in_m when we have it. The official docs
            // give no values for "L:A22X FG Altitude Unit" ("enum: feet or meters"), and a
            // label read off an assumed polarity is worse than none: it reported "Feet"
            // while the selector was measurably stepping 150 m / 50 m (live 2026-07-31).
            new("&Unit feet / meters",
                () => LatestFcp?.alt_in_m is { } m
                    ? (m ? "Meters" : "Feet")
                    : (Cached(simConnect, "A22X_FG_ALT_UNIT") > 0.5 ? "Meters (assumed)" : "Feet (assumed)"),
                () => WriteLVar(simConnect, "A22X FG Altitude Unit",
                        Cached(simConnect, "A22X_FG_ALT_UNIT") > 0.5 ? 0 : 1)),
            // The ALT knob's PUSH is the fine/coarse ring (Autopilot.xml: FCU_ALT_PUSH's
            // BTN_VAR is "L:A22X FG Altitude Fine" with BTN_TOGGLE). On coarse the selector
            // steps ~492 ft, so most round altitudes are simply not selectable — this is
            // the control that makes them reachable, and the walk selects it automatically.
            new("&Fine / coarse steps", () => Cached(simConnect, "A22X_FG_ALT_FINE") > 0.5 ? "Fine" : "Coarse",
                () => WriteLVar(simConnect, "A22X FG Altitude Fine",
                        Cached(simConnect, "A22X_FG_ALT_FINE") > 0.5 ? 0 : 1)),
        };

        int now = (int)Math.Round(Cached(simConnect, "A22X_AP_ALT"));
        // Name the metres mode UP FRONT. In metres the selector snaps to metric steps, so
        // a round number of feet usually isn't selectable and the result looks broken
        // rather than explained (live: asked 6000, got 5085 = 1550 m). The "Unit feet /
        // meters" button below is the same latched ring the real FCP has.
        string unitNote = LatestFcp?.alt_in_m == true
            ? " — FCP is in METRES; use the Unit button below for feet"
            : "";
        var dialog = new ValueInputForm(
            "FCP Altitude", $"altitude (now {now} feet){unitNote}", "0-41000 feet", announcer,
            input => int.TryParse(input, out int a) && a >= 0 && a <= 41000
                ? (true, "") : (false, "Enter an altitude between 0 and 41000 feet"),
            toggles,
            input =>
            {
                if (!int.TryParse(input, out int alt)) return;
                StartAltitudeSetAsync(simConnect, announcer, alt);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// Set the ALT selector: feet first, then a GRID walk — coarse ring for the
    /// thousands, fine ring for the hundreds, exactly like a pilot's hand.
    ///
    /// The selector SNAPS TO ITS GRID on every click (proven live 2026-08-10: 2625 ft +
    /// one coarse inc lands on 3000, not 3625; re-proven 2026-09-21: 2300 + one coarse
    /// inc lands on 3000), so in feet mode every multiple of 100 ft is exactly reachable:
    /// coarse clicks land on round thousands, fine clicks on round hundreds. Two
    /// consequences the pre-2026-08 measured walk got wrong:
    ///   * SPEED — it selected FINE up front and crept the WHOLE distance 100 ft at a
    ///     time (2000→35000 = 330 clicks ≈ 8+ s of "turning the wheel one by one").
    ///     Coarse covers the same distance in 33 clicks.
    ///   * ACCURACY — its cur+n×step model can't represent snapping, so any off-grid
    ///     start (metres-mode leftovers) "converged" to a value that was never on the
    ///     100 ft grid: the "always something close but not quite right" report.
    ///
    /// THREE THINGS WERE MEASURED LIVE ON 2026-09-21 (Synaptic A220-300, EGNT, engines
    /// running) and each one fixes a way this walk could report failure on a perfectly
    /// reachable altitude:
    ///
    ///   1. The stock AUTOPILOT ALTITUDE LOCK VAR DOES track the FCP selected altitude —
    ///      0 → 1000 → 2000 → 2100 → 2200 → 2300 → 3000 across single clicks, matching the
    ///      knob exactly. The old comment here ("the stock AUTOPILOT * VAR SimVars are NOT
    ///      what the A220's FCP publishes") was wrong for altitude, and it cost every
    ///      session with no Coherent display link the whole grid walk: ReadAfdxLiveAsync()
    ///      returning null dropped straight to the inferior measured fallback. The
    ///      read-back now PREFERS the 30 Hz CommBus and falls back to a delivered read of
    ///      that stock var (ReadFreshAsync, since 2026-10-16; the 1 Hz cache before), so the
    ///      grid walk is the path EVERYONE gets — the display agent only makes it faster.
    ///      (Heading tracks its stock var too, 245→246 per click. Speed read 80→81 that day
    ///      but 445 kt with 142 in the window on 2026-09-24, so it is never a fallback.
    ///      AP_ALT_VAR_SET_ENGLISH is still inert — 12000 written, the selector did not
    ///      move — so the walk itself is still required.)
    ///
    ///   2. L:A22X FG Altitude Fine is a LEVEL, not a pulse: writing 1 gives 100 ft
    ///      clicks, writing 0 gives 1000 ft clicks, and the value persists across clicks
    ///      (the WASM resets it to 0 at power-up, not per click). So the ring writes below
    ///      are sound — but they were never VERIFIED, and a ring that did not flip makes
    ///      every later click a tenth, or ten times, the assumed size. The walk now
    ///      measures what a burst actually moved and re-selects the ring once if the step
    ///      is off by more than 2x, instead of grinding against the wrong grid until it
    ///      runs out of rounds.
    ///
    ///   3. The coarse stage must not stop "close enough". Its exit test was
    ///      |delta| &lt; step/2 — 500 ft on the coarse ring — while the final verdict
    ///      demands 50 ft. So an off-grid start within 500 ft of a round thousand fired
    ///      ZERO clicks and then reported "Altitude did not reach 5000 — selector reads
    ///      5085 feet", for a target one single click away. Every stage now exits only
    ///      when it is genuinely ON its value (5% of a step).
    ///
    /// FEET is ensured first by TRUTH where truth exists (write, re-read the aircraft's
    /// own alt_in_m, try the other polarity if it didn't flip): in metres the grid is
    /// metric and round feet genuinely don't exist. With no display link there is no truth
    /// to check, and feet is the overwhelmingly common case, so the walk proceeds and
    /// simply says nothing about units rather than refusing. The ring is restored to its
    /// prior state afterwards — the walk's ring-flipping is an implementation detail, not
    /// a cockpit selection the pilot made, so it must not leak.
    /// </summary>
    private void StartAltitudeSetAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, int alt)
    {
        var knob = A220.A220Afdx.FcpKnob.Altitude;
        var token = BeginKnobWalk(knob);
        // A superseded or swapped-out walk says nothing, and never cuts off another knob's result.
        var say = KnobWalkVoice(announcer, token);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (!KnobWalkCanSend(simConnect, "Altitude", say)) return;
                string prep = "";
                try { prep = await EnsureAltitudeUnitFeetAsync(simConnect, token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Best-effort: the walk goes on, still reads alt_in_m below, and speaks its result.
                    Utils.Logging.Log.Warn("A220", $"Altitude walk: feet check failed: {ex.GetType().Name}: {ex.Message}");
                }

                // Ask for a whole FCP copy if alt_sel_ft is missing (the stores broadcast
                // only changes, so it can simply not be there yet).
                var fcpStart = await ReadFcpFieldAsync(f => f.alt_sel_ft, token);
                var blocks = fcpStart != null ? new A220.A220Afdx.LiveBlocks { fcp = fcpStart } : null;
                bool live = blocks?.fcp?.alt_sel_ft != null;

                // METRES is the one state that genuinely cannot do this, and only the
                // aircraft's own alt_in_m can say so. A link that is simply DOWN says
                // nothing either way — it must never be read as "in metres".
                if (blocks?.fcp?.alt_in_m == true)
                {
                    if (!token.IsCancellationRequested)
                        StartAltitudeFallbackWalk(simConnect, announcer, token, alt, prep);
                    return;
                }
                if (token.IsCancellationRequested) return;

                int target = (int)Math.Round(alt / 100.0) * 100;
                if (target != alt)
                    prep += $" — {alt} is between selector steps, aiming for {target}";

                // Read-back: the 30 Hz CommBus when the display agent is up, else a DELIVERED
                // read of AUTOPILOT ALTITUDE LOCK VAR, which tracks the selector exactly
                // (measured 2026-09-21). Null = no reading: "not confirmed", never a guess.
                // Latches OFF live on the first miss. The delivered read needs no top-up
                // sleep: the 1 Hz cache it replaced could be sampled MID-BURST after a
                // downgrade (a 28000 -> 6000 walk read "12000", measured a bogus 727 ft per
                // click from it and reported having stopped there, user report 2026-09-22),
                // but the batch's next delivery is sampled after the burst.
                async Task<double?> ReadAltAsync()
                {
                    if (live)
                    {
                        var b = await ReadFcpFieldAsync(f => f.alt_sel_ft, token);
                        if (b?.alt_sel_ft is { } v) return v;
                        live = false;
                    }
                    return await ReadKnobStockVarAsync(simConnect, "A22X_AP_ALT", token);
                }
                // Read back only once the selector has STOPPED moving. A fixed 300 ms after
                // a big burst could read mid-burst: the walk then aimed from a partial value,
                // mistook it for the wrong ring, and the pilot had to enter the altitude a
                // second time (user report 2026-09-24). On the stock var, one delivered read.
                Task<double?> ReadSettledAltAsync() => ReadSteadyAsync(ReadAltAsync, () => live, token);

                // Where the selector starts: nothing reporting it means no walk, said
                // before the ring or the knob is touched.
                if ((blocks?.fcp?.alt_sel_ft ?? await ReadAltAsync()) is not { } startAlt)
                {
                    FcpLog.Warn($"Altitude: no read-back, walk refused (target {target})");
                    say(A220.A220Afdx.KnobWalkRefusalText(knob, "Altitude"));
                    return;
                }

                // Ring bookkeeping: coarse for the thousands, fine for the hundreds, the
                // pilot's own ring selection put back afterwards.
                bool ringWasFine = Cached(simConnect, "A22X_FG_ALT_FINE") > 0.5;
                bool ringNowFine = ringWasFine;
                async Task SelectRingAsync(bool fine)
                {
                    WriteLVar(simConnect, "A22X FG Altitude Fine", fine ? 1 : 0, quiet: true);
                    ringNowFine = fine;
                    await System.Threading.Tasks.Task.Delay(250, token);
                }

                double cur = startAlt;
                bool readLost = false;
                int t1000 = (int)Math.Round(target / 1000.0, MidpointRounding.AwayFromZero) * 1000;
                // The aircraft takes about one knob event per frame (~70 ms live, 2026-09-24):
                // the old fixed 45 ms lost clicks by design, and a burst that lost over half of
                // them looked like the wrong ring and spent the one ring retry. The pace now
                // slows in proportion to the clicks a burst lost, as the other walks' does.
                int paceMs = KnobWalkStartPaceMs;

                // Walk one grid stage. `step` is what a click is EXPECTED to move; if a
                // burst says otherwise the ring did not take, so re-select it once and
                // carry on rather than grinding against the wrong grid (finding 2).
                async Task<bool> WalkToAsync(int walkTarget, double step, bool wantFine)
                {
                    bool ringRetried = false;
                    // 5% of a step, not half of one: "close enough" on the coarse ring
                    // left the value up to 499 ft out and then failed the 50 ft verdict
                    // (finding 3).
                    double reached = step * 0.05;
                    for (int round = 0; round < 12 && !token.IsCancellationRequested; round++)
                    {
                        if (Math.Abs(walkTarget - cur) <= reached) return true;
                        int clicks = A220.A220Afdx.GridClicks(cur, walkTarget, step);
                        if (clicks == 0) return Math.Abs(walkTarget - cur) <= reached;
                        bool up = walkTarget > cur;
                        await FireKnobBurstAsync(simConnect,
                            up ? "AP_ALT_VAR_INC" : "AP_ALT_VAR_DEC", clicks, token, paceMs: paceMs);

                        if (await ReadSettledAltAsync() is not { } after)
                        {
                            // The burst may have moved the selector: claim no value (R7).
                            FcpLog.Warn($"Altitude: read-back lost after {(up ? "INC" : "DEC")} x{clicks} from {cur:F0}");
                            readLost = true;
                            return false;
                        }
                        FcpLog.Info($"Altitude: {(up ? "INC" : "DEC")} x{clicks} @{paceMs}ms (step {step}): {cur:F0} -> {after:F0}");
                        double moved = Math.Abs(after - cur);
                        cur = after;
                        if (moved < 1e-6) return false;   // knob not responding
                        paceMs = A220.A220Afdx.NextKnobPaceMs(paceMs, clicks, moved / step);

                        double perClick = moved / clicks;
                        if (!ringRetried && A220.A220Afdx.StepIsWrongRing(perClick, step))
                        {
                            ringRetried = true;
                            await SelectRingAsync(wantFine);
                        }
                    }
                    return Math.Abs(walkTarget - cur) <= reached;
                }

                bool ok;
                try
                {
                    await SelectRingAsync(fine: false);
                    ok = await WalkToAsync(t1000, 1000, wantFine: false);
                    if (ok && target != t1000)
                    {
                        await SelectRingAsync(fine: true);
                        ok = await WalkToAsync(target, 100, wantFine: true);
                    }
                }
                finally
                {
                    // The pilot's ring, put back however the walk ends: a newer entry, an aircraft
                    // swap or a failure part-way used to leave the walk's own ring selected.
                    if (ringNowFine != ringWasFine)
                        WriteLVar(simConnect, "A22X FG Altitude Fine", ringWasFine ? 1 : 0, quiet: true);
                }

                if (token.IsCancellationRequested) return;
                if (readLost)
                {
                    say(A220.A220Afdx.KnobWalkNotConfirmedText("Altitude"));
                    return;
                }
                int landed = (int)Math.Round(cur);
                say(ok && Math.Abs(landed - target) < 50
                    ? $"Altitude {landed} feet{prep}"
                    : $"Altitude did not reach {target} — selector reads {landed} feet{prep}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Logged (CORE-15) and said: a walk never ends in silence.
                Utils.Logging.Log.Warn("A220", $"Altitude walk: {ex.GetType().Name}: {ex.Message}");
                say(A220.A220Afdx.KnobWalkFailedText("Altitude"));
            }
        });
    }

    /// <summary>
    /// The pre-2026-08 measured walk, now reached in exactly ONE case: the selector is
    /// in METRES and would not switch to feet. There the grid is metric, so no round
    /// number of feet is on it and the grid walk has nothing to aim at — this selects
    /// FINE (so at least some round values are reachable), converges on whatever the
    /// knob will give, and names the step and the metres mode in the result instead of
    /// pretending. A session with merely no Coherent link no longer comes here: the
    /// stock SimVar is a real read-back (measured 2026-09-21), so it gets the grid walk
    /// like everyone else, just with a slower read. It continues the altitude walk that
    /// called it, on the same <paramref name="token"/>.
    /// </summary>
    private void StartAltitudeFallbackWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        System.Threading.CancellationToken token, int alt, string prep)
    {
        if (Cached(simConnect, "A22X_FG_ALT_FINE") < 0.5)
        {
            WriteLVar(simConnect, "A22X FG Altitude Fine", 1);
            prep += ", fine steps selected";
        }
        string note = prep;
        StartFcpWalk(simConnect, announcer, A220.A220Afdx.FcpKnob.Altitude, token,
            b => b.fcp?.alt_sel_ft, "A22X_AP_ALT", "AP_ALT_VAR_INC", "AP_ALT_VAR_DEC",
            alt, 55, "Altitude", d => FormattableString.Invariant($"{(int)Math.Round(d)} feet"),
            note: (v, exact, stepFt) =>
            {
                string s = note;
                var fcp = LatestFcp;
                if (fcp?.alt_in_m == true)
                    s += FormattableString.Invariant($". FCP altitude is in metres — {(int)Math.Round(fcp.alt_sel_m ?? v * 0.3048)} metres selected");
                // When the knob CANNOT reach the requested altitude, its step size is
                // the entire explanation — and both the pilot and the next person
                // debugging this need it stated, not inferred from two attempts.
                if (!exact && stepFt > 0)
                {
                    s += FormattableString.Invariant($". Selector step {stepFt:0} feet");
                    if (fcp?.alt_sel_m is { } m) s += FormattableString.Invariant($", selector reads {m:0} metres");
                    if (fcp?.alt_in_m == false) s += ", units are feet";
                }
                return s;
            });
    }

    /// <summary>
    /// Put the ALT selector in FEET, verifying against the aircraft's own
    /// <c>alt_in_m</c> rather than trusting a polarity for
    /// <c>L:A22X FG Altitude Unit</c> that the official docs never state ("enum: feet or
    /// meters", no values). Writes one value, checks, and writes the other if that was the
    /// wrong way round. Returns the clause to append to the result — including an honest
    /// "could not switch" when the write does not take, and nothing at all when the
    /// display link is down and there is no truth to check against.
    /// </summary>
    private async Task<string> EnsureAltitudeUnitFeetAsync(SimConnectManager simConnect, System.Threading.CancellationToken token)
    {
        // Each read waits at most the walks' AFDX deadline: a hung view must not hold the walk.
        var blocks = await ReadAfdxLiveAsync(token, A220.A220Afdx.AfdxReadDeadlineMs);
        if (blocks?.fcp?.alt_in_m is not { } inMetres) return "";   // no truth available — say nothing
        if (!inMetres) return "";

        foreach (double candidate in new[] { 0.0, 1.0 })
        {
            WriteLVar(simConnect, "A22X FG Altitude Unit", candidate);
            await System.Threading.Tasks.Task.Delay(400, token);
            var after = await ReadAfdxLiveAsync(token, A220.A220Afdx.AfdxReadDeadlineMs);
            if (after?.fcp?.alt_in_m == false) return ", altitude units switched to feet";
        }
        return ", but the selector is in METRES and would not switch — a round number of feet is not selectable in that mode";
    }

    // ---- VS / FPA (Ctrl+V) ---------------------------------------------------

    private void ShowVerticalDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var toggles = new List<ToggleButtonDef>
        {
            new("&VS mode", () => Cached(simConnect, "A22X_FG_VS") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_VS_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_VS", "Vertical speed mode engaged", "Vertical speed mode off"); }),
            new("&FPA mode", () => Cached(simConnect, "A22X_FG_FPA") > 0.5 ? "Engaged" : "Off",
                () => { FireKeyEvent(simConnect, "AP_ATT_HOLD");
                        AnnounceModeResult(simConnect, announcer, "A22X_FG_FPA", "Flight path angle mode engaged", "Flight path angle mode off"); }),
        };

        // The wheel's own selection from the FCP block when it shows one (R3); with no block, the
        // dialog's old stock reads, which the walk itself never trusts.
        string now = A220.A220Afdx.VerticalPromptNow(LatestFcp,
            Cached(simConnect, "A22X_AP_VS"), Cached(simConnect, "A22X_SELECTED_FPA"));
        var dialog = new ValueInputForm(
            "FCP Vertical", $"target ({now})",
            "VS -8000 to 8000 fpm, or FPA -9.9 to 9.9 (decimal = FPA)", announcer,
            input => TryParseVerticalTarget(input, out _, out _)
                ? (true, "")
                : Utils.NumberParsing.TryParseTyped(input, out _)
                    ? (false, "VS must be between -8000 and 8000")
                    : (false, "Enter a vertical speed or a flight path angle"),
            toggles,
            input =>
            {
                if (!TryParseVerticalTarget(input, out double t, out bool isFpa)) return;
                StartVerticalTargetSetAsync(simConnect, announcer, t, isFpa);
            });
        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>A typed V/S-FPA target: within ±9.9 it is a flight path angle in degrees, else a
    /// vertical speed of 100 to 8000 fpm either way. Parsed as the pilot typed it on any Windows
    /// culture ([CORE-17]): "-3,0" is -3.0 degrees, and what could be a thousands group ("1.500") is
    /// refused, never read as 1.5 degrees.</summary>
    internal static bool TryParseVerticalTarget(string input, out double target, out bool isFpa)
    {
        isFpa = false;
        if (!Utils.NumberParsing.TryParseTyped(input, out target)) return false;
        if (Math.Abs(target) <= 9.9) { isFpa = true; return true; }
        return Math.Abs(target) >= 100 && Math.Abs(target) <= 8000;
    }

    /// <summary>
    /// ENGAGE the vertical mode first, THEN walk the wheel — the A220's VS thumbwheel is
    /// DEAD while neither VS nor FPA mode is engaged (proven live 2026-08-10: with no
    /// vertical mode, AP_VS_VAR_INC changes nothing and the FCP publishes vs_sel = null;
    /// after AP_VS_HOLD the very next click moves it). The old walk fired clicks at the
    /// dead wheel, read null back, and fell through to the stock SimVar — which this FCP
    /// never writes for VS — so "setting a VS" quietly did nothing. Same lesson as the
    /// LVFR A330's engage-before-write rule. The engagement is spoken in the result and
    /// the FG monitor announces the mode transition itself.
    ///
    /// The wheel is read back ONLY from the FCP block (R3 of the 2026-10-16 walk review):
    /// <c>vs_sel</c> in feet per minute in VS mode and in tenths of a degree in FPA mode
    /// (<see cref="A220.A220Afdx.SelectedVsFpm"/>, <see cref="A220.A220Afdx.SelectedFpaDegrees"/>).
    /// With the display link down it refuses, as the speed walk does
    /// (<see cref="A220.A220Afdx.VerticalWheelRefusal"/>): it used to walk V/S on AUTOPILOT VERTICAL
    /// HOLD VAR, which this FCP never writes, so the wheel moved and the walk said "not responding",
    /// and FPA on L:A22X Selected FPA, which was never shown to track the wheel.
    /// </summary>
    private void StartVerticalTargetSetAsync(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        double target, bool isFpa)
    {
        // V/S and FPA are one knob (the thumbwheel): a newer entry of either stops this one,
        // engage wait included.
        var knob = A220.A220Afdx.FcpKnob.Vertical;
        var token = BeginKnobWalk(knob);
        string name = isFpa ? "Flight path angle" : "Vertical speed";
        // A superseded or swapped-out walk says nothing, and never cuts off another knob's result.
        var say = KnobWalkVoice(announcer, token);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                // Before the engage write, the walk's first write.
                if (!KnobWalkCanSend(simConnect, name, say)) return;
                // The wheel is read back only from the FCP block (R3): with no block, refuse before
                // the engage write, as the speed walk refuses before its knots/Mach switch — the
                // walk must not leave the aircraft in a mode it then cannot set.
                if (await ReadFcpFieldAsync(f => A220.A220Afdx.FcpBlockHasData(f) ? 1 : null, token) == null)
                {
                    FcpLog.Warn($"{name}: no FCP data, walk refused before engaging (target {target})");
                    say(A220.A220Afdx.KnobWalkRefusalText(knob, name));
                    return;
                }
                string engagedNote = "";
                string modeKey = isFpa ? "A22X_FG_FPA" : "A22X_FG_VS";
                if (Cached(simConnect, modeKey) < 0.5)
                {
                    FireFcpEvent(simConnect, isFpa ? "AP_ATT_HOLD" : "AP_VS_HOLD");
                    engagedNote = isFpa ? ", FPA mode selected" : ", vertical speed mode selected";
                    await System.Threading.Tasks.Task.Delay(900, token);
                }

                // The wheel must show a target in the mode asked for, in the aircraft's own FCP
                // block (read with a resync and retries: the store at {} is not a down link). No
                // block refuses; no target means the mode did not engage; the other mode would
                // walk the wrong value. Say which, before any click.
                A220.A220Afdx.FcpBlock? lastSeen = null;
                var ready = await ReadFcpFieldAsync(f => f.vs_sel, token, b => lastSeen = b);
                if (A220.A220Afdx.VerticalWheelRefusal(ready ?? lastSeen, isFpa) is { } refusal)
                {
                    FcpLog.Warn($"{name}: walk refused (target {target}): {refusal}");
                    say(refusal);
                    return;
                }

                string note = engagedNote;
                if (isFpa)
                    StartFcpWalk(simConnect, announcer, knob, token,
                        b => A220.A220Afdx.SelectedFpaDegrees(b.fcp), null, "AP_VS_VAR_INC", "AP_VS_VAR_DEC",
                        target, 0.06, name, A220.A220Afdx.FpaDegreesText, note: (_, _, _) => note);
                else
                    StartFcpWalk(simConnect, announcer, knob, token,
                        b => A220.A220Afdx.SelectedVsFpm(b.fcp), null, "AP_VS_VAR_INC", "AP_VS_VAR_DEC",
                        target, 55, name, A220.A220Afdx.VerticalSpeedText, note: (_, _, _) => note);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Logged (CORE-15) and said: a walk never ends in silence.
                Utils.Logging.Log.Warn("A220", $"{name} walk: {ex.GetType().Name}: {ex.Message}");
                say(A220.A220Afdx.KnobWalkFailedText(name));
            }
        });
    }

    // ---- NAV radios (Ctrl+N) -------------------------------------------------

    private Forms.A220.A220RadiosForm? _radiosForm;

    /// <summary>
    /// Ctrl+N: the A220 radios window — captain course, nav source, NAV 1/2 preset
    /// frequencies and AUTO/MAN tuning, all through the aircraft's own CommBus calls
    /// (A220RadiosForm). Replaced the stock NAV*_STBY_SET dialog, which had no course,
    /// nav-source or AUTO/MAN control.
    /// </summary>
    private void ShowNavRadiosDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        _sim = simConnect;
        if (_radiosForm != null && !_radiosForm.IsDisposed)
        {
            _radiosForm.Activate();
            return;
        }
        _radiosForm = new Forms.A220.A220RadiosForm(
            DisplaysAgentCallAsync,
            () => PulseLVar(simConnect, "A22X L Nav Source"),
            announcer);
        _ = _radiosForm.OpenAsync();
    }

    private void DisposeRadiosForm()
    {
        try { _radiosForm?.Dispose(); } catch { }
        _radiosForm = null;
    }
}
