using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Standby altimeter (ISI) controls ([A220-43]). The ISI has no set event and no CommBus
/// action: its baro knob (A220_KnobContinuous) writes a per-click delta to
/// "L:A22X ISI Altimeter Set" (±1, which the WASM applies and clears; it applies any whole-number
/// delta written there just as exactly, so MSFSBA sends a burst as one write) and its push writes
/// "L:A22X ISI Altimeter STD". Both are read back from the stock altimeter 3, which the
/// aircraft's systems keep for the ISI: KOHLSMAN SETTING MB:3 follows every click and STD:3 says
/// STD (live 2026-10-16). The ISI's own "A22X.ISI Data" store, exposed by the displays agent as
/// isi(), holds only a whole-number qnh_hpa, which cannot see one 0.01-inch click two times in
/// three, so it is asked only whether the ISI is powered. The baro entry is WALKED against
/// altimeter 3 — the FCP walks' rules ([A220-7], [A220-9], [A220-10]) — and never clicked blind.
/// </summary>
public partial class SynapticA220Definition
{
    private const string IsiKnobVar = "A22X ISI Altimeter Set";

    /// <summary>The ISI's setting in hPa: stock KOHLSMAN SETTING MB:3 (Variables partial).</summary>
    private const string IsiKohlsmanMbKey = "A22X_ISI_KOHLSMAN_MB";

    /// <summary>The ISI's STD state: stock KOHLSMAN SETTING STD:3 (Variables partial).</summary>
    private const string IsiStdKey = "A22X_ISI_STD";

    /// <summary>How long one fresh read of altimeter 3 may take before it counts as no reading.</summary>
    private const int IsiReadTimeoutMs = 1500;

    /// <summary>Settle before a read-back: the write's calc string, then a WASM tick (live
    /// 2026-10-16, MB:3 showed a single click within about 150 ms).</summary>
    private const int IsiSettleMs = 350;

    /// <summary>Wait before the STD push's read-back: the 200 ms press pulse, then a WASM tick.</summary>
    private const int IsiStdReadBackDelayMs = 800;

    /// <summary>The standby baro walk's OWN cancellation: the ISI knob is a separate instrument,
    /// so no FCP knob walk (each has its own, Dialogs partial) or flap pick ever stops it, and it
    /// never stops theirs. Cancelled by a newer standby baro entry, the STD push and StopAllMotion.</summary>
    private System.Threading.CancellationTokenSource? _isiWalkCancel;

    /// <summary>
    /// Whether the ISI's own CommBus store says it is unpowered — the walk's pre-check, its only
    /// use of the store. Anything short of an explicit <c>powered:false</c> (no display link, a
    /// store that has not arrived after one resync) lets the walk go on: the stock altimeter 3
    /// read decides whether there is a reading.
    /// </summary>
    private async Task<bool> IsiReportsUnpoweredAsync(System.Threading.CancellationToken token)
    {
        // The same wall-clock limit as the FCP walks' reads: a hung-but-open view answers no eval
        // for 5 s, and this check must not keep the walk silent for tens of seconds.
        long deadline = Environment.TickCount64 + A220.A220Afdx.AfdxReadDeadlineMs;
        int Left() => (int)Math.Max(0, deadline - Environment.TickCount64);
        for (int attempt = 0; attempt < 3 && Left() > 0; attempt++)
        {
            string? json = await CallAgentWithinAsync("isi()", Left(), token);
            if (json == null) return false;   // no display link (or no answer in time): no power state to read
            var status = A220.A220Panels.ParseIsiRead(json).Status;
            if (status != A220.A220Panels.IsiReadStatus.NoReading) return status == A220.A220Panels.IsiReadStatus.Unpowered;
            if (attempt == 0 && Left() > 0) await CallAgentWithinAsync("afdxResync()", Left(), token);   // the stores broadcast only changes
            await System.Threading.Tasks.Task.Delay(Math.Min(150, Left()), token);
        }
        return false;
    }

    /// <summary>One delivered read of altimeter 3's setting in hPa, or null when none came back.</summary>
    private static async Task<double?> ReadIsiHpaAsync(SimConnectManager simConnect, System.Threading.CancellationToken token)
    {
        double? hpa = await simConnect.ReadFreshAsync(IsiKohlsmanMbKey, IsiReadTimeoutMs, token);
        return hpa is { } v && !double.IsNaN(v) ? v : null;
    }

    /// <summary>
    /// Walk the ISI baro knob to a typed setting (hPa 940-1090 or inHg 27.00-32.00). AIMS, never
    /// hunts: the first click measures the step and the direction on THIS walk, every later
    /// burst is round(|delta|/step) clicks sent as ONE write of the signed count (the knob var
    /// applies any whole-number delta exactly, live 2026-10-16), a burst that overshoots aims back,
    /// a straddle after one click ends on the NEARER of the two bracketing values (one click back
    /// when the value before that click was nearer), and a missing read-back or a knob that does
    /// not move is said aloud.
    /// The walk is the shared one, <c>A220Afdx.RunKnobWalkAsync</c> on <c>A220Panels.IsiWalkPolicy</c>
    /// (the FCP walks run it too); this method is its I/O. A walk superseded by a newer entry or the
    /// STD push, or stopped by an aircraft swap, ends silently: every announcement checks its own
    /// token first, and its result does not cut off another knob's (<see cref="KnobWalkVoice"/>).
    /// </summary>
    private void StartIsiBaroWalk(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, double entry)
    {
        double? target = A220.A220Panels.IsiTargetHpa(entry);
        if (target == null)
        {
            announcer.AnnounceImmediate(A220.A220Panels.IsiRangeText);
            return;
        }
        bool inHg = A220.A220Panels.IsInHgEntry(entry);
        _isiWalkCancel?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _isiWalkCancel = cts;
        var token = cts.Token;
        // A cancelled walk says nothing: the newer entry, or the next aircraft, speaks for itself.
        var say = KnobWalkVoice(announcer, token);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (await IsiReportsUnpoweredAsync(token))
                {
                    say(A220.A220Panels.IsiUnavailableText(A220.A220Panels.IsiReadStatus.Unpowered));
                    return;
                }
                if (await ReadIsiHpaAsync(simConnect, token) is not { } start)
                {
                    say(A220.A220Panels.IsiUnavailableText(A220.A220Panels.IsiReadStatus.NoReading));
                    return;
                }
                var result = await A220.A220Afdx.RunKnobWalkAsync(target.Value, start, A220.A220Panels.IsiWalkPolicy,
                    async move =>
                    {
                        // A newer entry or the STD push may have cancelled this walk since the round
                        // began: a write now would move the knob off the setting the pilot just chose.
                        token.ThrowIfCancellationRequested();
                        // The whole move is ONE write of the signed click count (at most
                        // IsiMaxClicksPerWrite; a larger gap takes another round): no per-click pacing.
                        WriteLVar(simConnect, IsiKnobVar, A220.A220Panels.IsiKnobWriteDelta(move.Clicks, move.Direction), quiet: true);
                        await System.Threading.Tasks.Task.Delay(IsiSettleMs, token);
                        return await ReadIsiHpaAsync(simConnect, token);
                    }, token);
                say(A220.A220Panels.IsiWalkResultText(result, target.Value, inHg));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A walk never throws into the UI, and never stops without saying so (CORE-15 log).
                Utils.Logging.Log.Warn("A220", $"Standby baro walk: {ex.Message}");
                say("Standby altimeter walk stopped — check the standby baro.");
            }
        });
    }

    /// <summary>
    /// The STD push ([A220-42], [A220-43]): pulsed like every momentary push, then read back ONCE
    /// from altimeter 3 (CORE-7: the screen reader cannot say what the ISI did). STD:3 set says
    /// "Standby altimeter STD"; clear means the press did not land, so the setting is said
    /// instead; no delivered read says "not available" (<c>A220Panels.IsiStdReadBackText</c>).
    /// A standby baro walk in flight is stopped first, or its next click would leave STD again.
    /// </summary>
    private void PressIsiStd(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, string stdVar)
    {
        _isiWalkCancel?.Cancel();
        PulseLVar(simConnect, stdVar);
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(IsiStdReadBackDelayMs);
                double? std = await simConnect.ReadFreshAsync(IsiStdKey, IsiReadTimeoutMs);
                double? mb = std is { } s && s < 0.5
                    ? await simConnect.ReadFreshAsync(IsiKohlsmanMbKey, IsiReadTimeoutMs)
                    : null;
                announcer.Announce(A220.A220Panels.IsiStdReadBackText(std, mb));
            }
            catch (Exception ex)
            {
                // A read that failed is no reading: said once, never silence and never a guess (CORE-15 log).
                Utils.Logging.Log.Warn("A220", $"Standby STD read-back: {ex.GetType().Name}: {ex.Message}");
                announcer.Announce(A220.A220Panels.IsiNoReadingText);
            }
        });
    }
}
