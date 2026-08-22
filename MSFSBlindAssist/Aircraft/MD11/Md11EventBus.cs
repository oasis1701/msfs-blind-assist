using System.Collections.Concurrent;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's one and only actuation channel.
///
/// TFDi's Integration Guide is explicit about the architecture:
///
///   "The TFDi Design MD-11 is primarily event-driven. This means that variables and systems
///    are driven by an event, not by reading the state of an L:VAR or similar. Writing directly
///    to any of the variables will bypass our integrity checks and allow potentially incorrect
///    or conflicting states. To trigger a custom event, you can write the value of the event ID
///    to the L:VAR named CEVENT and our code will translate it. Please note that the aircraft
///    itself also uses this event, so do not overuse it."
///
/// So: every switch, knob and button on this aircraft is actuated by writing ONE integer — the
/// event id — to <c>L:CEVENT</c>. There is no per-control L:var to set, and setting one anyway
/// is explicitly unsupported. (The one sanctioned exception is the <c>MD11_EXTCTL_*</c> family,
/// which TFDi documents as "designed for external control" — those are direct writes by design.)
///
/// Three constraints fall out of that paragraph, and all three are load-bearing:
///
/// 1. ANTI-COALESCE. MobiFlight's command channel silently drops a calc string identical to the
///    one before it — this repo has been bitten twice already (the A380 RMP repeated-digit drop
///    and the DCDU WILCO→SEND two-step). Here it would be worse than cosmetic: pressing the same
///    button twice, or stepping a knob N times, emits the SAME string every time, so every repeat
///    after the first would vanish. Every write therefore carries a <c>{seq} 0 *</c> prefix,
///    which computes a discarded zero and exists purely to make the string textually unique.
///
/// 2. PACING. "Do not overuse it" is a real warning, not boilerplate: CEVENT is a single shared
///    slot that the aircraft's own code also writes. Blasting a burst of writes at frame rate
///    risks ours landing between the aircraft's own and being lost — or clobbering theirs. Writes
///    are serialized through one queue with a minimum gap, so a 5-step knob walk paces out over
///    ~150 ms instead of racing.
///
/// 3. PRESS *AND* RELEASE. Buttons carry a DOWN id and an UP id. Sending only DOWN leaves the
///    button held for the rest of the session — the exact Fenix stuck-button bug that re-fired
///    the takeoff-config test after touchdown (see the A32NX invariants). Always send both.
/// </summary>
public sealed class Md11EventBus : IDisposable
{
    /// <summary>The L:var TFDi's Integration Guide names as the event channel.</summary>
    public const string CEventVar = "CEVENT";

    /// <summary>
    /// Minimum gap between consecutive CEVENT writes. The channel is shared with the aircraft's
    /// own code ("do not overuse it"), so this is deliberately conservative rather than tuned for
    /// speed: a knob walk that takes an extra 100 ms is invisible to a pilot, whereas a dropped
    /// or clobbered event is a control that silently doesn't work.
    ///
    /// This is ALSO the gap the pump leaves between a button's DOWN and its UP, so it must be long
    /// enough that the aircraft samples the pressed state on its own tick before the release lands
    /// (the FBW Rust sampler misses same-tick pulses; assume the MD-11's WASM tick is no faster).
    /// It was 30 ms, which is under one frame at 30 fps — short enough that a CDU key's press and
    /// release could fall in the same tick and the key silently do nothing (the FMC-paging bug).
    /// </summary>
    private const int MinGapMs = 60;

    /// <summary>Legacy alias — the down/up gap is now the single <see cref="MinGapMs"/> pacing gap.</summary>
    private const int PressReleaseGapMs = MinGapMs;

    /// <summary>
    /// Bound on the queue. A runaway producer (a stuck key repeat, a walk that never converges)
    /// must not grow this without limit; dropping the overflow is strictly better than pumping a
    /// thousand stale events at the aircraft seconds later.
    /// </summary>
    private const int MaxQueued = 256;

    private readonly SimConnectManager _sim;
    private readonly BlockingCollection<int> _queue = new(new ConcurrentQueue<int>(), MaxQueued);
    private readonly Task _pump;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Makes each calc string unique — see constraint 1. Only ever incremented, never read for
    /// meaning; the value is multiplied by zero and discarded by the RPN.
    /// </summary>
    private int _seq;

    private int _dropped;

    public Md11EventBus(SimConnectManager sim)
    {
        _sim = sim;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Queues one CEVENT id. Non-blocking: returns immediately, the pump does the pacing.
    /// Ids are what <c>md11_control_map.json</c> carries in each control's <c>events</c> map.
    /// </summary>
    public void Fire(int eventId)
    {
        if (eventId <= 0) return;
        if (!_queue.TryAdd(eventId))
        {
            // Log the first drop only; a flooding producer would otherwise flood the log too.
            if (Interlocked.Increment(ref _dropped) == 1)
                Log.Warn("MD11", $"CEVENT queue full ({MaxQueued}) — dropping events. First dropped id {eventId}.");
        }
    }

    /// <summary>
    /// Fires a full press→release pair for a momentary control. Both ids go through the same
    /// queue in order, so the release can never overtake the press. See constraint 3 — a press
    /// without its release leaves the button held down for the session.
    /// </summary>
    public void FirePressRelease(int? downId, int? upId)
    {
        if (downId is > 0) Fire(downId.Value);
        if (upId is > 0) Fire(upId.Value);
    }

    /// <summary>Convenience overload for a control from the map.</summary>
    public void Press(Md11Control control)
        => FirePressRelease(control.Event("LEFT_BUTTON_DOWN"), control.Event("LEFT_BUTTON_UP"));

    private async Task PumpAsync()
    {
        try
        {
            foreach (var id in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    Write(id);
                }
                catch (Exception ex)
                {
                    Log.Debug("MD11", $"CEVENT write failed for id {id}: {ex.Message}");
                }

                // Pace even the last event of a burst: a follow-up burst may arrive immediately.
                await Task.Delay(MinGapMs, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            Log.Error("MD11", $"CEVENT pump died: {ex.Message}");
        }
    }

    /// <summary>
    /// The actual write. Goes through ExecuteCalculatorCode rather than SetLVar because SetLVar
    /// would emit the bare string <c>"86018 (>L:CEVENT)"</c> — byte-identical on every repeat of
    /// the same event, which is precisely what MobiFlight coalesces away (constraint 1). The
    /// <c>{seq} 0 *</c> prefix pushes seq, pushes 0, multiplies to a discarded zero, and leaves
    /// the stack clean for the real write; MSFS's RPN ignores the residual value.
    /// </summary>
    private void Write(int eventId)
    {
        var seq = Interlocked.Increment(ref _seq);
        _sim.ExecuteCalculatorCode($"{seq} 0 * {eventId} (>L:{CEventVar})", quiet: true);
    }

    /// <summary>Fires a press/release pair and waits for the queue to drain past it.</summary>
    public async Task PressAndSettleAsync(Md11Control control, int settleMs = 120)
    {
        Press(control);
        await Task.Delay(PressReleaseGapMs + settleMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one <c>MD11_EXTCTL_*</c> variable — the sanctioned direct-write family, and the only
    /// thing on this aircraft that is NOT a CEVENT.
    ///
    /// VERIFIED AGAINST THE LIVE AIRCRAFT (2026-07-17), because none of this is documented:
    /// writing 123 to <c>MD11_EXTCTL_FCP_HDG</c> put 123 into <c>MD11_AFS_HDG</c> (the FCP window
    /// read-back) and left <c>MD11_EXTCTL_FCP_HDG</c> back at <c>-1</c>. So each of these is a
    /// one-shot COMMAND INBOX, not a mirror: the FCC consumes the value, applies it, and resets the
    /// var to the -1 idle sentinel. That is also why writing here does not "bypass integrity
    /// checks" — the value goes THROUGH TFDi's own FCC exactly as a knob turn would.
    ///
    /// Deliberately NOT queued behind the CEVENT pump: that queue is paced because CEVENT is one
    /// shared slot the aircraft also writes. These are per-quantity vars with no such contention,
    /// and a type-in box should land now, not after a knob walk drains.
    ///
    /// Still seq-prefixed, though — the self-clear to -1 is what makes this necessary rather than
    /// optional: setting the SAME value twice (250 kt → 250 kt) emits a byte-identical calc string,
    /// which MobiFlight coalesces away. The first write would land, the var would reset to -1, and
    /// the second would silently vanish.
    /// </summary>
    public void WriteExternal(string varName, double value)
    {
        var seq = Interlocked.Increment(ref _seq);
        // Invariant fixed-point: default interpolation can emit scientific notation or a
        // comma-decimal, both of which the MSFS RPN parser rejects. Six decimals carries Mach
        // (0.820) and FPA (-3.00) without ever reaching an exponent.
        var literal = value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        _sim.ExecuteCalculatorCode($"{seq} 0 * {literal} (>L:{varName})", quiet: true);
    }

    public void Dispose()
    {
        try
        {
            _queue.CompleteAdding();
            _cts.Cancel();
            // Bounded wait: a hung pump must not hold up an aircraft switch.
            _pump.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { /* best effort */ }
        finally
        {
            _cts.Dispose();
            _queue.Dispose();
        }
    }
}
