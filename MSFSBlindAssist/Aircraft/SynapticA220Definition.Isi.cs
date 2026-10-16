using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Standby altimeter (ISI) controls ([A220-43]). The ISI has no set event and no CommBus
/// action: its baro knob (A220_KnobContinuous) writes a per-click delta to
/// "L:A22X ISI Altimeter Set" (±1, which the WASM applies and clears) and its push writes
/// "L:A22X ISI Altimeter STD". The only read-back is the ISI's own "A22X.ISI Data" store
/// (qnh_hpa), exposed by the displays agent as isi(). So the baro entry is WALKED against that
/// read-back — the FCP walks' rules ([A220-7], [A220-9], [A220-10]) — and never clicked blind.
/// </summary>
public partial class SynapticA220Definition
{
    private const string IsiKnobVar = "A22X ISI Altimeter Set";

    /// <summary>Gap between two knob writes, so two never land in one WASM tick and the second
    /// overwrites the first before it is applied ([A220-10]'s batching lesson).</summary>
    private const int IsiClickPaceMs = 60;

    /// <summary>Settle before a read-back: the store broadcasts at 20-30 Hz.</summary>
    private const int IsiSettleMs = 350;

    /// <summary>The standby baro walk's OWN cancellation: the ISI knob is a separate instrument,
    /// so an FCP walk or a flap pick (which share <c>_walkCancel</c>) never stops it, and it never
    /// stops theirs. Cancelled by a newer standby baro entry and by StopAllMotion.</summary>
    private System.Threading.CancellationTokenSource? _isiWalkCancel;

    /// <summary>
    /// The ISI's QNH (hPa) straight from its CommBus store, asking the WASM for a whole copy
    /// when the store has not arrived yet (the stores broadcast only changes). With no QNH the
    /// read says why: the ISI reports itself unpowered (returned at once — a resync will not
    /// power it), or there is no link, store or value.
    /// </summary>
    private async Task<A220.A220Panels.IsiRead> ReadIsiAsync(System.Threading.CancellationToken token)
    {
        var read = new A220.A220Panels.IsiRead(A220.A220Panels.IsiReadStatus.NoReading, null);
        for (int attempt = 0; attempt < 6 && !token.IsCancellationRequested; attempt++)
        {
            var client = _displaysClient;
            if (client == null) return read;
            read = A220.A220Panels.ParseIsiRead(await client.CallAgentAsync("isi()"));
            if (read.Status != A220.A220Panels.IsiReadStatus.NoReading) return read;
            if (attempt == 0 || attempt == 3) await client.CallAgentAsync("afdxResync()");
            await System.Threading.Tasks.Task.Delay(150, token);
        }
        return read;
    }

    /// <summary>
    /// Walk the ISI baro knob to a typed setting (hPa 940-1090 or inHg 27.00-32.00). AIMS, never
    /// hunts: the first click measures the step and the direction on THIS walk, every later
    /// burst is round(|delta|/step) clicks, a straddle ends on the NEARER of the two bracketing
    /// values (one click back when the value before the last burst was nearer), and a missing
    /// read-back or a knob that does not move is said aloud. The per-round decision is pure:
    /// <c>A220Panels.IsiWalkDecision</c>. A walk superseded by a newer entry, or stopped by an
    /// aircraft swap, ends silently: every announcement checks its own token first.
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
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            // A cancelled walk says nothing: the newer entry, or the next aircraft, speaks for itself.
            void Say(string text)
            {
                if (!token.IsCancellationRequested) announcer.AnnounceImmediate(text);
            }
            try
            {
                var first = await ReadIsiAsync(token);
                if (first.QnhHpa is not { } start)
                {
                    Say(A220.A220Panels.IsiUnavailableText(first.Status));
                    return;
                }
                double cur = start;
                int up = 1;               // the delta sign that RAISES the setting — calibrated on the first move
                double? step = null;      // hPa per click, measured on this walk only
                double prevDelta = double.NaN;
                bool retried = false;

                for (int round = 0; round < 8 && !token.IsCancellationRequested; round++)
                {
                    double delta = target.Value - cur;
                    var move = A220.A220Panels.IsiWalkDecision(delta, prevDelta, step);
                    if (move.Action == A220.A220Panels.IsiWalkAction.Done)
                    {
                        Say(A220.A220Panels.FormatIsiBaro(cur, inHg));
                        return;
                    }
                    if (move.Action == A220.A220Panels.IsiWalkAction.NearestHere)
                    {
                        Say(A220.A220Panels.IsiNearestText(cur, inHg));
                        return;
                    }
                    // StepBack: the last burst overshot and the value before it was nearer, so ONE
                    // click back towards the target; what lands is read and spoken below.
                    bool stepBack = move.Action == A220.A220Panels.IsiWalkAction.StepBack;
                    prevDelta = delta;
                    int clicks = stepBack ? 1 : move.Clicks;
                    int sign = delta > 0 ? up : -up;
                    for (int i = 0; i < clicks && !token.IsCancellationRequested; i++)
                    {
                        WriteLVar(simConnect, IsiKnobVar, sign, quiet: true);
                        await System.Threading.Tasks.Task.Delay(IsiClickPaceMs, token);
                    }
                    await System.Threading.Tasks.Task.Delay(IsiSettleMs, token);
                    var after = await ReadIsiAsync(token);
                    if (after.QnhHpa is not { } now)
                    {
                        Say(A220.A220Panels.IsiReadLostText(after.Status));
                        return;
                    }
                    double moved = now - cur;
                    if (Math.Abs(moved) < 0.01)
                    {
                        if (!retried && step == null) { retried = true; continue; }
                        Say(A220.A220Panels.FormatIsiBaro(cur, inHg) + " — the standby altimeter knob is not responding.");
                        return;
                    }
                    if (stepBack)
                    {
                        // The step and the direction are already measured; just say where it landed.
                        cur = now;
                        Say(A220.A220Panels.IsiWithinTolerance(target.Value - cur, step)
                            ? A220.A220Panels.FormatIsiBaro(cur, inHg)
                            : A220.A220Panels.IsiNearestText(cur, inHg));
                        return;
                    }
                    if (Math.Sign(moved) != Math.Sign(delta)) up = -up;
                    step = Math.Abs(moved) / clicks;
                    cur = now;
                }
                Say(A220.A220Panels.IsiRoundLimitText(target.Value, cur, step, inHg));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // A walk never throws into the UI, and never stops without saying so (CORE-15 log).
                Utils.Logging.Log.Warn("A220", $"Standby baro walk: {ex.Message}");
                Say("Standby altimeter walk stopped — check the standby baro.");
            }
        });
    }
}
