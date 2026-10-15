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

    /// <summary>
    /// The ISI's QNH (hPa) straight from its CommBus store, asking the WASM for a whole copy
    /// when the store has not arrived yet (the stores broadcast only changes); null when the
    /// display link is down or the ISI is unpowered.
    /// </summary>
    private async Task<double?> ReadIsiQnhAsync(System.Threading.CancellationToken token)
    {
        for (int attempt = 0; attempt < 6 && !token.IsCancellationRequested; attempt++)
        {
            var client = _displaysClient;
            if (client == null) return null;
            double? qnh = A220.A220Panels.ParseIsiQnhHpa(await client.CallAgentAsync("isi()"));
            if (qnh != null) return qnh;
            if (attempt == 0 || attempt == 3) await client.CallAgentAsync("afdxResync()");
            await System.Threading.Tasks.Task.Delay(150, token);
        }
        return null;
    }

    /// <summary>
    /// Walk the ISI baro knob to a typed setting (hPa 940-1090 or inHg 27.00-32.00). AIMS, never
    /// hunts: the first click measures the step and the direction on THIS walk, every later
    /// burst is round(|delta|/step) clicks, a straddle stops on the nearest selectable value,
    /// and a missing read-back or a knob that does not move is said aloud.
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
        _walkCancel?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _walkCancel = cts;
        var token = cts.Token;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                double? first = await ReadIsiQnhAsync(token);
                if (first == null)
                {
                    announcer.AnnounceImmediate(
                        "Standby altimeter reading not available — the display link is down. Set it on the ISI.");
                    return;
                }
                double cur = first.Value;
                int up = 1;               // the delta sign that RAISES the setting — calibrated on the first move
                double? step = null;      // hPa per click, measured on this walk only
                double prevDelta = double.NaN;
                bool retried = false;
                string Nearest(double v) => A220.A220Panels.FormatIsiBaro(v, inHg) + " — nearest selectable";

                for (int round = 0; round < 8 && !token.IsCancellationRequested; round++)
                {
                    double delta = target.Value - cur;
                    double tolerance = step is { } s ? s / 2 : 0.2;
                    if (Math.Abs(delta) <= tolerance)
                    {
                        announcer.AnnounceImmediate(A220.A220Panels.FormatIsiBaro(cur, inHg));
                        return;
                    }
                    if (step != null && A220.A220Afdx.WalkStraddled(delta, prevDelta))
                    {
                        announcer.AnnounceImmediate(Nearest(cur));
                        return;
                    }
                    prevDelta = delta;
                    int clicks = step == null ? 1 : A220.A220Afdx.AimClicks(delta, step.Value);
                    if (clicks == 0)
                    {
                        announcer.AnnounceImmediate(Nearest(cur));
                        return;
                    }
                    int sign = delta > 0 ? up : -up;
                    for (int i = 0; i < clicks && !token.IsCancellationRequested; i++)
                    {
                        WriteLVar(simConnect, IsiKnobVar, sign, quiet: true);
                        await System.Threading.Tasks.Task.Delay(IsiClickPaceMs, token);
                    }
                    await System.Threading.Tasks.Task.Delay(IsiSettleMs, token);
                    double? after = await ReadIsiQnhAsync(token);
                    if (after == null)
                    {
                        announcer.AnnounceImmediate("Standby altimeter reading lost — check the ISI.");
                        return;
                    }
                    double moved = after.Value - cur;
                    if (Math.Abs(moved) < 0.01)
                    {
                        if (!retried && step == null) { retried = true; continue; }
                        announcer.AnnounceImmediate(
                            A220.A220Panels.FormatIsiBaro(cur, inHg) + " — the standby altimeter knob is not responding.");
                        return;
                    }
                    if (Math.Sign(moved) != Math.Sign(delta)) up = -up;
                    step = Math.Abs(moved) / clicks;
                    cur = after.Value;
                }
                if (!token.IsCancellationRequested) announcer.AnnounceImmediate(Nearest(cur));
            }
            catch (OperationCanceledException) { }
            catch { /* a walk never throws into the UI */ }
        });
    }

    /// <summary>CORE-7: the STD push reads the ISI back once (the screen reader cannot see the
    /// ISI), after the pulse and a WASM tick.</summary>
    private void AnnounceIsiAfterPress(ScreenReaderAnnouncer announcer)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(800);
                double? qnh = await ReadIsiQnhAsync(System.Threading.CancellationToken.None);
                announcer.Announce(A220.A220Panels.IsiReadbackText(qnh));
            }
            catch { /* read-back only */ }
        });
    }
}
