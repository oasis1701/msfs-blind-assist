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
    internal const int MinGapMs = 60;

    /// <summary>Legacy alias — the down/up gap is now the single <see cref="MinGapMs"/> pacing gap.</summary>
    private const int PressReleaseGapMs = MinGapMs;

    /// <summary>
    /// Bound on the queue. A runaway producer (a stuck key repeat, a walk that never converges)
    /// must not grow this without limit; dropping the overflow is strictly better than pumping a
    /// thousand stale events at the aircraft seconds later.
    /// </summary>
    private const int MaxQueued = 256;

    private readonly Action<string> _write;
    private readonly BlockingCollection<QueuedEvent> _queue = new(new ConcurrentQueue<QueuedEvent>(), MaxQueued);
    private readonly Task _pump;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Held by the pump around each TAKE and by <see cref="Dispose"/> while it empties the queue, so
    /// the two are never concurrent consumers. A <c>BlockingCollection</c> take reserves a count
    /// permit before it checks cancellation: a take caught in that gap by the cancel gives the permit
    /// back only after Dispose's own takes have stopped at a count of zero, stranding the queue's last
    /// id (measured: one disposal in 640 lost it); and a take already past the check can dequeue a
    /// LATER id behind Dispose's back and write it ahead of the ids Dispose took — an UP ahead of its
    /// DOWN. Under this lock a take either finishes first (its id is the queue's head, and the pump
    /// writes it) or begins after the cancel and takes nothing. The pump holds it through an UNBOUNDED
    /// wait for the next id and <c>lock</c> has no timeout, so Dispose may take it only after
    /// <c>_cts.Cancel()</c> (or <c>CompleteAdding</c> on an empty queue) has ended that wait — any
    /// earlier and the UI thread hangs before any disposal bound applies (<see cref="TakeQueued"/>).
    /// </summary>
    private readonly object _takeLock = new();

    /// <summary>
    /// One queued CEVENT. <see cref="Hold"/> is set only on the two halves
    /// <see cref="PressAndHoldAsync"/> queued: its DOWN, and the UP it queued when the hold ended,
    /// flagged <see cref="IsRelease"/>. The pump records the DOWN's write alone — never the release's,
    /// never an identical id some other path queued — so <see cref="Dispose"/> can tell a held button
    /// whose DOWN reached the aircraft from one whose DOWN never left the queue, and put a release
    /// still queued behind a written DOWN first, where the drain bound cannot drop it.
    /// </summary>
    private readonly record struct QueuedEvent(int Id, HeldPress? Hold, bool IsRelease = false);

    /// <summary>
    /// One hold in flight: the UP it owes and whether the pump has WRITTEN its DOWN. Compared by
    /// reference — two holds on one button are two entries. <see cref="DownWritten"/> is set by the
    /// pump under the <see cref="_held"/> lock; <see cref="Dispose"/> reads it under that lock, or once
    /// the pump has stopped, when it is final.
    /// </summary>
    private sealed class HeldPress
    {
        public HeldPress(int upId) => UpId = upId;
        public int UpId { get; }
        public bool DownWritten;
    }

    /// <summary>
    /// Bound on how long <see cref="Dispose"/> spends writing, on the calling thread, what the pump
    /// left queued — owed releases first. Sixteen ids drain inside it at <see cref="MinGapMs"/>
    /// pacing, more than the eleven hold-to-test buttons can ever owe. It sits on the UI thread (an
    /// aircraft switch, or window close when the definition is disposed at exit), so it is a bound,
    /// not a target: an idle bus returns within one pacing gap. With <see cref="PumpStopTimeoutMs"/>
    /// it makes the same total bound disposal always had.
    /// </summary>
    internal const int DrainTimeoutMs = 1000;

    /// <summary>
    /// Bound on how long <see cref="Dispose"/> waits for the cancelled pump to finish the one write it
    /// may have in hand. A pump still inside a write past this is stuck in the channel itself:
    /// nothing is written beside it (two writers would interleave on one slot), and everything
    /// queued is logged as discarded — the same outcome a stuck pump always had.
    /// </summary>
    internal const int PumpStopTimeoutMs = 500;

    /// <summary>
    /// Every hold whose DOWN has been queued and whose UP has not. <see cref="PressAndHoldAsync"/>
    /// registers one as it queues the DOWN and takes it back as it queues the UP. Once
    /// <see cref="Dispose"/> has closed the table, a hold that ends leaves its entry where it is, and
    /// Dispose — after stopping the pump — writes the UP of every hold whose DOWN was WRITTEN, ahead
    /// of anything still queued, and takes a never-written DOWN back out of the queue with nothing
    /// owed: a hold that outlives the bus never leaves the button held in the aircraft, and never
    /// releases a button it never pressed (constraint 3). Guarded by its own lock — a hold ends on a
    /// pool thread while Dispose runs on the UI thread — and every register-and-queue,
    /// take-back-and-queue and "DOWN written" record happens under it
    /// (<see cref="TrackHeldAndFireDown"/>, <see cref="ReleaseHeldAndFireUp"/>, the pump), which is
    /// what makes each one atomic with respect to Dispose.
    /// </summary>
    private readonly List<HeldPress> _held = new();

    /// <summary>
    /// Set by <see cref="Dispose"/> under the <see cref="_held"/> lock before it stops accepting ids:
    /// no further hold may register, and a hold that ends from here on leaves its UP to Dispose. The
    /// guarded test button (the hydraulic test) lifts its cover on a pool thread first, so its DOWN
    /// can be queued AFTER disposal began — a DOWN accepted there would leave the button held with
    /// no UP owed to anyone.
    /// </summary>
    private bool _closed;

    /// <summary>When the pump last wrote (monotonic ms): Dispose's first write keeps one <see cref="MinGapMs"/> behind it.</summary>
    private long _lastWriteAt;

    /// <summary>
    /// Makes each calc string unique — see constraint 1. Only ever incremented, never read for
    /// meaning; the value is multiplied by zero and discarded by the RPN.
    /// </summary>
    private int _seq;

    private int _dropped;

    /// <summary>CEVENTs queued and not yet written. The walker adds <see cref="Pending"/> × <see cref="MinGapMs"/> to a click's timestamp so a click behind a burst is judged when it lands, not when it was queued.</summary>
    public int Pending
    {
        get
        {
            // Same disposal tolerance Fire gets, for the same reason: a walk or a hold can outlive
            // the bus by a tick, and reading Pending on the way out must not surface as an Error-level
            // "set threw" line from SafeWalk's generic catch for a benign shutdown race.
            try { return _queue.Count; }
            catch (ObjectDisposedException) { return 0; }
        }
    }

    /// <summary>
    /// How long the next write has to wait for the queue ahead of it: <see cref="Pending"/> ×
    /// <see cref="MinGapMs"/>. The one spelling of the write-time stamp — it was written out at
    /// half a dozen call sites, one of which is the guarded press's echo-window budget, where the
    /// term has to be recognisable as the same quantity in both places it is added.
    /// </summary>
    public int BacklogMs => Pending * MinGapMs;

    public Md11EventBus(SimConnectManager sim)
        : this(rpn => sim.ExecuteCalculatorCode(rpn, quiet: true)) { }

    /// <summary>
    /// The write seam. Production wires it to <c>ExecuteCalculatorCode(…, quiet: true)</c>; the
    /// tests hand in a recorder so the real pump, its pacing and <see cref="Dispose"/> can be
    /// driven without a sim. Every calc string still carries the <c>{seq} 0 *</c> prefix.
    /// </summary>
    internal Md11EventBus(Action<string> write)
    {
        _write = write;
        _pump = Task.Run(PumpAsync);
    }

    /// <summary>
    /// Queues one CEVENT id. Non-blocking: returns immediately, the pump does the pacing.
    /// Ids are what <c>md11_control_map.json</c> carries in each control's <c>events</c> map.
    /// </summary>
    public void Fire(int eventId)
    {
        if (eventId <= 0) return;
        TryEnqueue(new QueuedEvent(eventId, null));
    }

    /// <summary>Queues one event; false when the bus is closing or the queue is full — both logged, neither thrown.</summary>
    private bool TryEnqueue(QueuedEvent item)
    {
        bool queued;
        try
        {
            queued = _queue.TryAdd(item);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The bus has been disposed (or is closing): a hold or walk that outlived it. Dropped,
            // not thrown — Dispose has already decided what any held button is owed.
            Log.Debug("MD11", $"CEVENT id {item.Id} fired after the bus closed — dropped.");
            return false;
        }
        if (!queued)
        {
            // Log the first drop only; a flooding producer would otherwise flood the log too.
            if (Interlocked.Increment(ref _dropped) == 1)
                Log.Warn("MD11", $"CEVENT queue full ({MaxQueued}) — dropping events. First dropped id {item.Id}.");
        }
        return queued;
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
            while (true)
            {
                QueuedEvent item;
                lock (_takeLock)
                {
                    // Blocks until an id arrives; false once disposal has completed an empty queue,
                    // and throws once cancelled. Under the take lock — see _takeLock.
                    if (!_queue.TryTake(out item, Timeout.Infinite, _cts.Token)) break;
                }

                // A hold's DOWN is recorded as written — never its release, which carries the same
                // record — under the held table's lock, BEFORE the write. Dispose reads the record
                // only after this pump has stopped, so it is final by then; and a write that throws
                // still counts, because a release the aircraft did not need is harmless while a press
                // left unreleased is the stuck button of constraint 3.
                if (item.Hold is { } hold && !item.IsRelease)
                    lock (_held) hold.DownWritten = true;
                WriteLogged(item.Id);
                Volatile.Write(ref _lastWriteAt, Environment.TickCount64);

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
        _write($"{seq} 0 * {eventId} (>L:{CEventVar})");
    }

    /// <summary>One <see cref="Write"/>, a failure logged and swallowed — the pump's step, and Dispose's on the calling thread.</summary>
    private void WriteLogged(int eventId)
    {
        try
        {
            Write(eventId);
        }
        catch (Exception ex)
        {
            Log.Debug("MD11", $"CEVENT write failed for id {eventId}: {ex.Message}");
        }
    }

    /// <summary>Fires a press/release pair and waits for the queue to drain past it.</summary>
    public async Task PressAndSettleAsync(Md11Control control, int settleMs = 120)
    {
        Press(control);
        await Task.Delay(PressReleaseGapMs + settleMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds a momentary control: DOWN now, UP after <paramref name="holdMs"/>. Both ids ride the
    /// same paced queue, so the release can never overtake the press — and the hold is measured
    /// from when the DOWN will be WRITTEN, not from when it is queued: the queue's backlog at
    /// that moment (<see cref="Pending"/> × <see cref="MinGapMs"/>, the same stamp the walker
    /// gives a click) is added to the wait, so a press queued behind a guard lift or a burst of
    /// walker clicks is still held for the full time once it lands. With the queue idle the
    /// backlog is zero and the hold is within one pacing gap of exact. For the hold-to-test
    /// buttons (see Md11TestButtons), whose lights are only on while the button is down.
    /// </summary>
    public async Task PressAndHoldAsync(Md11Control control, int holdMs)
    {
        var down = control.Event("LEFT_BUTTON_DOWN");
        var up = control.Event("LEFT_BUTTON_UP");
        // A button with no DOWN was never pressed, so it cannot be released: writing its UP alone
        // sends the aircraft a release for a press that never happened. (Nothing is tracked either
        // — Dispose must not release, or log, a button it never held.)
        if (down is not > 0) return;
        var backlogMs = BacklogMs;   // sampled before the DOWN joins the queue
        HeldPress? hold = null;
        if (up is > 0)
        {
            // Registered AND queued under the held table's lock, so Dispose can neither miss this
            // DOWN nor be beaten by it. Refused once Dispose has closed the table, and when the
            // queue would not take the DOWN: a DOWN that never goes out owes nothing, so neither
            // half is written at all.
            hold = TrackHeldAndFireDown(up.Value, down.Value);
            if (hold == null) return;
        }
        else Fire(down.Value);   // no UP id at all: pressed, with nothing owed to release
        await Task.Delay(HoldDelayMs(holdMs, backlogMs)).ConfigureAwait(false);
        // Before Dispose this queues the UP; after it the entry is Dispose's, which writes the UP
        // only if the pump wrote the DOWN. Exactly one of the two, either way.
        if (hold != null) ReleaseHeldAndFireUp(hold);
    }

    /// <summary>
    /// How long to wait between queuing the DOWN and queuing the UP so the button is down for
    /// <paramref name="holdMs"/> from the time the DOWN is written: the hold itself plus the
    /// backlog the DOWN has to wait behind. The one-gap floor is belt-and-braces — the pump paces
    /// every write by <see cref="MinGapMs"/> regardless of what a caller waits, so the UP can never
    /// share a tick with the DOWN — kept so a zero hold still reads as "one paced press".
    /// </summary>
    internal static int HoldDelayMs(int holdMs, int backlogMs) => Math.Max(holdMs, MinGapMs) + backlogMs;

    /// <summary>
    /// How long a read-back waits after QUEUING a click so that the aircraft's settle
    /// (<paramref name="settleMs"/>) is measured from when the click is WRITTEN: the backlog it
    /// waits behind (<see cref="Pending"/> × <see cref="MinGapMs"/>, sampled before the Fire) plus
    /// the settle. The same stamp the walker gives every click and <see cref="PressAndHoldAsync"/>
    /// gives its DOWN, generalised to the ground spoiler, guard-lift and press-feedback read-backs.
    ///
    /// It is the general RULE, not the fix for the false "Ground spoilers did not arm." — on
    /// today's paths the backlog is at most a couple of events (the walker queues one click per
    /// step; the MCDU scratchpad send is the one real burst), so with the queue idle this is the
    /// bare settle and costs the pilot nothing. What removed the false verdict is reading on
    /// DELIVERY instead of sleeping and reading the cache (see <c>SimConnectManager.ReadFreshAsync</c>).
    /// </summary>
    internal static int ReadBackDelayMs(int settleMs, int backlogMs) => settleMs + backlogMs;

    /// <summary>
    /// Registers one hold owing <paramref name="upId"/> and queues its <paramref name="downId"/> under
    /// the same lock; null once <see cref="Dispose"/> has closed the table, or when the queue would
    /// not take the DOWN (nothing pressed, so nothing owed).
    ///
    /// The two used to be separate steps, and the gap between them was a real window rather than a
    /// theoretical one: the guarded hold-to-test button lifts its cover on a POOL thread, so a
    /// <see cref="Dispose"/> on the UI thread (an aircraft switch) could sweep a table that did not
    /// yet owe this UP and then let the DOWN through — the button held in the aircraft with no
    /// release owed to anyone, which is exactly what <see cref="_closed"/> exists to prevent. Under
    /// one lock the pair is atomic against Dispose: either Dispose closed first and this refuses, or
    /// the DOWN is queued and the hold is in the table for Dispose to judge once the pump has stopped.
    ///
    /// Queuing under the lock is safe: it is the queue's non-blocking TryAdd, and the pump takes this
    /// lock only to record a written DOWN, never while it holds anything else.
    /// </summary>
    private HeldPress? TrackHeldAndFireDown(int upId, int downId)
    {
        lock (_held)
        {
            if (_closed) return null;
            var hold = new HeldPress(upId);
            if (!TryEnqueue(new QueuedEvent(downId, hold))) return null;
            _held.Add(hold);
            return hold;
        }
    }

    /// <summary>
    /// Takes <paramref name="hold"/> back and queues its UP, under the same lock and for the
    /// mirror-image reason: taking the entry and then queuing outside the lock let Dispose stop
    /// accepting in between, and the UP the hold then fired was dropped, leaving the button held.
    /// False once Dispose has closed the table — from then on the entry is Dispose's, and it writes
    /// the UP only if the pump wrote this hold's DOWN.
    ///
    /// The UP is queued carrying the hold, as its release: a Dispose that comes before the pump
    /// reaches it — behind an MCDU scratchpad send, say — still writes it FIRST when the pump wrote
    /// this hold's DOWN (<see cref="DisposalOrder"/>). Queued bare, it sat at the tail, the drain bound
    /// dropped it, and the button stayed held after a hold that had ended just before disposal.
    /// </summary>
    private bool ReleaseHeldAndFireUp(HeldPress hold)
    {
        lock (_held)
        {
            if (_closed || !_held.Remove(hold)) return false;
            TryEnqueue(new QueuedEvent(hold.UpId, hold, IsRelease: true));
            return true;
        }
    }

    /// <summary>
    /// Writes one L:var directly through the calc path — the shared channel for the three writes on
    /// this aircraft that are NOT a CEVENT: the sanctioned <c>MD11_EXTCTL_*</c> inboxes (the
    /// contract below), the Dial-A-Flap wheel's own backing var (<c>Md11FlapSystem.SetDialRawAsync</c>)
    /// and the walk's gated direct-write fallback (<c>Md11DirectSet</c>). Nothing else may call it.
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
        _write($"{seq} 0 * {literal} (>L:{varName})");
    }

    public void Dispose()
    {
        try
        {
            // 1. Stop accepting. The held table first, under its own lock: from here no hold can
            //    register, and a hold that ends leaves its UP to step 3 instead of queuing it. Then
            //    the queue, so any Fire from here on is dropped.
            lock (_held) _closed = true;
            _queue.CompleteAdding();

            // 2. Stop the pump FIRST. Cancelled, it takes nothing more; what it had not yet taken is
            //    taken here, in order, and the bounded wait lets it finish the one write it may have
            //    in hand — so nothing below is ever written beside it. Draining THROUGH the pump put
            //    a held button's UP at the TAIL of the paced queue: behind an MCDU scratchpad send
            //    (48 ids) it fell past the drain bound and a 3 s test button stayed held in the
            //    aircraft.
            _cts.Cancel();
            var queued = TakeQueued();
            if (!_pump.Wait(TimeSpan.FromMilliseconds(PumpStopTimeoutMs)))
            {
                // Stuck inside a write: the channel itself is not answering. A second writer would
                // interleave on the one CEVENT slot, so nothing is written — the outcome a hung pump
                // always had, now logged with what it cost.
                int owed;
                lock (_held) { owed = _held.Count(h => h.DownWritten); _held.Clear(); }
                Log.Warn("MD11", $"CEVENT pump did not stop within {PumpStopTimeoutMs} ms — {queued.Count} queued id(s) and {owed} owed release(s) discarded rather than written beside it.");
                return;
            }

            // 3. The pump has stopped, so which holds' DOWNs reached the aircraft is final.
            List<HeldPress> holds;
            lock (_held) { holds = _held.ToList(); _held.Clear(); }
            WriteRemaining(DisposalOrder(holds, queued));
        }
        catch (Exception ex)
        {
            // Still best effort — but a throw here can leave a held test button pressed in the
            // aircraft, so it must leave evidence rather than vanish.
            Log.Warn("MD11", $"CEVENT bus dispose failed: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _queue.Dispose();
        }
    }

    /// <summary>
    /// Everything still queued, in FIFO order. Called after <c>CompleteAdding</c>, so nothing can join
    /// behind it, and under <see cref="_takeLock"/>, so the pump is never a second consumer beside it:
    /// its take has either finished — its id is ahead of all of these — or, starting after the cancel,
    /// takes nothing. It MUST come after <c>_cts.Cancel()</c> (or after <c>CompleteAdding</c> on an
    /// empty queue): the pump holds <see cref="_takeLock"/> through an unbounded wait and <c>lock</c>
    /// has no timeout, so called any earlier this would hang the UI thread before any bound applies.
    /// </summary>
    private List<QueuedEvent> TakeQueued()
    {
        var items = new List<QueuedEvent>();
        lock (_takeLock)
            while (_queue.TryTake(out var item)) items.Add(item);
        return items;
    }

    /// <summary>
    /// What <see cref="Dispose"/> writes, in order. FIRST the UP of every hold whose DOWN the pump
    /// wrote — a hold still registered, and a hold that already ENDED whose release is still queued —
    /// because behind a long backlog the drain bound would otherwise drop it and leave the button
    /// held; overlapping holds on one button owe one UP between them, so each is written once. THEN
    /// the rest of the queue in FIFO order, less the DOWN of every hold still registered whose DOWN
    /// never left the queue: never pressed, so nothing is owed, and its UP is not written either. A
    /// hold that ENDED with its DOWN still queued keeps both halves there, in order, and both are
    /// written — which is why an UP never goes out ahead of its own DOWN.
    /// </summary>
    private static List<int> DisposalOrder(List<HeldPress> holds, List<QueuedEvent> queued)
    {
        var owedUps = holds.Where(h => h.DownWritten).Select(h => h.UpId)
            .Concat(queued.Where(IsOwedRelease).Select(item => item.Id))
            .Distinct().ToList();
        var neverPressed = holds.Where(h => !h.DownWritten).ToHashSet();
        var order = new List<int>(owedUps);
        var dropped = 0;
        foreach (var item in queued)
        {
            if (IsOwedRelease(item)) continue;   // already first, above
            if (item.Hold != null && neverPressed.Contains(item.Hold)) { dropped++; continue; }
            order.Add(item.Id);
        }
        if (owedUps.Count > 0)
            Log.Info("MD11", $"CEVENT bus disposing with {owedUps.Count} held button(s) — releasing UP {string.Join(", ", owedUps)} first.");
        if (dropped > 0)
            Log.Info("MD11", $"CEVENT bus disposing — {dropped} held button(s) whose DOWN never left the queue: neither half written.");
        return order;
    }

    /// <summary>The release of a hold that has ended, still queued behind a DOWN the pump wrote: owed, so it goes out first.</summary>
    private static bool IsOwedRelease(QueuedEvent item) => item.IsRelease && item.Hold is { DownWritten: true };

    /// <summary>
    /// Writes <paramref name="ids"/> on the calling thread, <see cref="MinGapMs"/> apart — the first
    /// one gap behind the pump's last write — until <see cref="DrainTimeoutMs"/>, and logs whatever
    /// is left as discarded.
    /// </summary>
    private void WriteRemaining(List<int> ids)
    {
        var deadline = Environment.TickCount64 + DrainTimeoutMs;
        var last = Volatile.Read(ref _lastWriteAt);
        for (var i = 0; i < ids.Count; i++)
        {
            var wait = Math.Max(0, last + MinGapMs - Environment.TickCount64);
            if (Environment.TickCount64 + wait > deadline)
            {
                Log.Warn("MD11", $"CEVENT bus did not drain within {DrainTimeoutMs} ms — {ids.Count - i} id(s) discarded.");
                return;
            }
            if (wait > 0) Thread.Sleep((int)wait);
            WriteLogged(ids[i]);
            last = Environment.TickCount64;
        }
    }
}
