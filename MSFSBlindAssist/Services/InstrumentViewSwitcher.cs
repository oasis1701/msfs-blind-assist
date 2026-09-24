using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The camera SimVars as the switcher sees them. <c>SimConnectManager</c> implements it
/// (SimConnectManager.Camera.cs); tests fake it.
/// </summary>
public interface ICameraViewIo
{
    /// <summary>One-shot read of the camera; null when nothing arrives within <paramref name="timeoutMs"/> or the sim is not connected.</summary>
    Task<CameraViewReading?> ReadAsync(int timeoutMs);

    /// <summary>
    /// Writes <c>CAMERA VIEW TYPE AND INDEX:0</c> (the type) then <c>:1</c> (the index), back to
    /// back. True when the write was DISPATCHED to the simulator — not that the camera moved,
    /// which only a read-back can say. False means nothing was sent at all (not connected, or the
    /// call threw), which is what lets the caller tell "the camera may have moved" from "nothing
    /// happened"; see <see cref="InstrumentViewPlan.MovedWithNoWayBack"/>.
    /// </summary>
    bool Set(int viewType, int viewIndex);

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:0</c> (the type) alone, so the caller can space the pair. True when dispatched.</summary>
    bool SetViewType(int viewType);

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:1</c> (the index) alone, so the caller can space the pair. True when dispatched.</summary>
    bool SetViewIndex(int viewIndex);
}

/// <summary>
/// What an aircraft definition hands <c>BaseAircraftDefinition.ReadDisplay</c> to have the sim
/// moved to a particular instrument view before the capture: the camera to move (the
/// SimConnectManager) and the 0-based instrument view index from the aircraft's cameras.cfg.
/// </summary>
public sealed record InstrumentViewRequest(ICameraViewIo Camera, int ViewIndex);

/// <summary>
/// The result of <see cref="InstrumentViewSwitcher.EnterAsync"/>, and the way back:
/// <see cref="RestoreTarget"/> is the view <see cref="InstrumentViewSwitcher.RestoreAsync"/>
/// writes again after the capture. Created by the switcher only.
/// </summary>
public sealed class InstrumentViewSession
{
    internal InstrumentViewSession(
        InstrumentViewOutcome outcome,
        bool verified,
        CameraViewReading? restoreTarget,
        int wantedIndex,
        bool moved)
    {
        Outcome = outcome;
        Verified = verified;
        RestoreTarget = restoreTarget;
        WantedIndex = wantedIndex;
        Moved = moved;
    }

    public InstrumentViewOutcome Outcome { get; }

    /// <summary>True when the camera was seen on the wanted view — including when it was there already.</summary>
    public bool Verified { get; }

    /// <summary>
    /// The view the restore should aim at: the camera as it read before the switch, or a home
    /// owed by an earlier FAILED restore that this read has not settled
    /// (<see cref="CameraHomePlan"/>). Null when the camera could not be read and nothing is owed.
    /// </summary>
    public CameraViewReading? RestoreTarget { get; }

    /// <summary>The instrument view index this read asked for — what a restore must NOT leave the camera on.</summary>
    public int WantedIndex { get; }

    /// <summary>
    /// True when a write that would move the camera was DISPATCHED to the simulator. A fact about
    /// what was sent, never about what the camera did — that is <see cref="Verified"/>'s job, and
    /// conflating the two is what let a landed write with unreadable read-backs strand the pilot
    /// in silence.
    /// </summary>
    public bool Moved { get; }
}

/// <summary>
/// Moves the simulator camera to an instrument view for an AI display read and puts it back
/// afterwards: read, plan (<see cref="InstrumentViewPlan"/>), write, verify by read-back, settle
/// for a rendered frame — then <see cref="RestoreAsync"/> writes the pilot's own view again and
/// verifies that too.
/// Live-measured on MSFS 2024 (2026-09-08): the cut is instantaneous, so the read-back normally
/// matches on its first poll and the whole entry costs one settle. The verify cap is elapsed
/// wall-clock time, including the read timeouts it spends polling — not a count of poll steps —
/// so a sim that never answers costs about a second, not the cap times the read timeout.
/// </summary>
public sealed class InstrumentViewSwitcher
{
    public const int DefaultReadTimeoutMs = 500;
    public const int DefaultPollStepMs = 100;
    public const int DefaultVerifyCapMs = 1000;
    public const int DefaultSettleMs = 250;

    /// <summary>
    /// The gap between the restore's TYPE write and its INDEX write, so the sim takes them on
    /// separate frames — see <see cref="RestoreAsync"/> for why one frame does not work.
    ///
    /// NOT a minimised value: zero is measured to FAIL and a hand-spaced round-trip (hundreds of
    /// milliseconds) is measured to WORK, so the smallest gap that suffices is somewhere between
    /// and was never narrowed. It is deliberately its own constant rather than a second use of
    /// <see cref="DefaultSettleMs"/>, which measures a rendered frame before a screenshot — a
    /// different quantity that happens to share a number today. The restore no longer touches
    /// <see cref="DefaultSettleMs"/> at all: it used to spend one before its verify poll, which
    /// was that very conflation, and the confirm after the poll made it redundant anyway.
    /// </summary>
    public const int DefaultWriteGapMs = 250;

    /// <summary>
    /// How long after a matching read-back the restore waits before re-reading to confirm the
    /// camera HELD. A refused write reads back as success for under ~150 ms (measured: one round
    /// trip already returns the settled wrong state), so this sits past that transient.
    ///
    /// The ENTRY path confirms with <see cref="DefaultSettleMs"/> instead, because it is already
    /// spending that wait on a rendered frame before the capture and the confirm rides along for
    /// the price of one read. Two constants for one rule because the two paths pay for the wait
    /// differently — not because the rule differs.
    /// </summary>
    public const int DefaultHoldConfirmMs = 250;

    /// <summary>
    /// The index the restore drops to before it writes the view TYPE. Index 0 exists in every
    /// view type that has any camera at all, which is what makes it safe as a waypoint.
    ///
    /// <para>
    /// ⚠️ On SOME aircraft the sim REFUSES a write to the view TYPE register while the index
    /// register holds a high value, and dropping the index first is what makes the identical
    /// write succeed. Measured on the live PMDG 737-800 (2026-09-20, MSFS 2024): sitting on
    /// instrument view index 7, writing type 1 was refused — repeatedly, with a 3 s settle and
    /// nothing else touching the camera, so it is neither the ~150 ms transient nor a timing
    /// confound. Dropping the index to 3 first made the identical type write succeed, and the
    /// index could then be written back to 7.
    /// </para>
    ///
    /// <para>
    /// That was a defect the PMDG 737 hit on EVERY Alt+P and Alt+N read, because its
    /// captain-panel view is index 7: the type write was refused, the camera stayed in the
    /// instrument type, and the pilot's index was then written INTO it — sliding them to an
    /// instrument view they never chose. It is also what <c>docs/md11.md</c> had recorded as an
    /// unreconciled observation (a spaced type-only write coming back unverified).
    /// </para>
    ///
    /// <para>
    /// ⚠️⚠️ WHY it refuses is NOT known, and the obvious explanation is DISPROVEN. The first
    /// version of this comment said the sim validates the (type, index) pair and refuses one whose
    /// current index is out of range for the target type, since
    /// <c>CAMERA VIEW TYPE AND INDEX MAX:1</c> reads 6 on that aircraft. The Fenix A320 refutes it
    /// (measured 2026-09-21, same sim build, also MAX:1 = 6, also a custom pilot camera at index
    /// 7): from instrument index 7 the identical type write is ACCEPTED and lands on 1/7, and from
    /// instrument index 8 it is accepted and lands on 1/8 — an index beyond that same ceiling. So
    /// the refusal is aircraft-specific and index-versus-MAX does not predict it. Do not restate
    /// the pair-check theory as fact; this is the second time an inviting explanation of a camera
    /// refusal has turned out wrong, the first being the MAX theory in
    /// <see cref="CameraViewReading"/>.
    /// </para>
    ///
    /// <para>
    /// The write is kept anyway because what it BUYS is measured on both aircraft: it rescues the
    /// PMDG, where the direct write cannot work, and on the Fenix, where the direct write already
    /// works, it costs one extra register write and one frame gap and changes nothing else. A
    /// workaround with a known cost and an unknown trigger is worth more than a tidy model that
    /// predicts the wrong thing.
    /// </para>
    /// </summary>
    public const int NeutralViewIndex = 0;

    private readonly ICameraViewIo _io;
    private readonly Func<int, Task> _delay;
    private readonly Func<long> _now;
    private readonly int _readTimeoutMs;
    private readonly int _pollStepMs;
    private readonly int _verifyCapMs;
    private readonly int _settleMs;
    private readonly int _writeGapMs;
    private readonly int _holdConfirmMs;
    private readonly CameraHome _home;

    /// <param name="delay">Task.Delay in production; tests pass a recorder that completes at once.</param>
    /// <param name="now">Monotonic milliseconds — Environment.TickCount64 in production; tests pass a virtual clock the delay advances.</param>
    public InstrumentViewSwitcher(
        ICameraViewIo io,
        Func<int, Task>? delay = null,
        Func<long>? now = null,
        int readTimeoutMs = DefaultReadTimeoutMs,
        int pollStepMs = DefaultPollStepMs,
        int verifyCapMs = DefaultVerifyCapMs,
        int settleMs = DefaultSettleMs,
        int writeGapMs = DefaultWriteGapMs,
        int holdConfirmMs = DefaultHoldConfirmMs,
        CameraHome? home = null)
    {
        _io = io;
        _delay = delay ?? (ms => Task.Delay(ms));
        _now = now ?? (() => Environment.TickCount64);
        _readTimeoutMs = readTimeoutMs;
        _pollStepMs = pollStepMs;
        _verifyCapMs = verifyCapMs;
        _settleMs = settleMs;
        _writeGapMs = writeGapMs;
        _holdConfirmMs = holdConfirmMs;
        _home = home ?? CameraHome.Shared;
    }

    /// <summary>
    /// Reads the camera, writes the wanted instrument view when a write is called for, polls the
    /// read-back until it matches, then settles and CONFIRMS the camera is still there before the
    /// capture. Never throws; the session says what happened.
    ///
    /// <para>
    /// The confirm is the same <see cref="ConfirmHeldAsync"/> the restore uses, and on this path
    /// it is FREE: the settle it spends is the one this method already spent waiting for the sim
    /// to render a frame of the new view, so the only added cost is one read. That matters
    /// because a refused write reads back as success for a moment first (see
    /// <see cref="RestoreAsync"/>), and without the confirm this path could match that transient,
    /// report Verified, and hand the AI a frame of a view the camera had already left — a
    /// confident wrong reading with nothing spoken.
    /// </para>
    /// </summary>
    public async Task<InstrumentViewSession> EnterAsync(int wantedIndex)
    {
        // Retried once: a single timed-out read makes the outcome Unknown, which costs the pilot
        // the way back and can make the app confess to a move that never happened. A second
        // attempt turns almost every transient miss into a real outcome.
        var before = await TryReadAsync() ?? await TryReadAsync();

        var home = CameraHomePlan.For(_home.Owed, before);
        if (home.ClearOwedHome) _home.Clear();

        var plan = InstrumentViewPlan.For(before, wantedIndex);
        if (plan.Writes is not { } writes)
            return new InstrumentViewSession(
                plan.Outcome,
                plan.Outcome == InstrumentViewOutcome.AlreadyThere,
                home.RestoreTarget,
                wantedIndex,
                moved: false);

        bool moved;
        try
        {
            moved = _io.Set(writes.Type, writes.Index);
        }
        catch (Exception ex)
        {
            moved = false;
            Log.Debug("Camera", $"Setting camera view type {writes.Type} index {writes.Index} failed: {ex.Message}");
        }

        bool verified = await PollUntilAsync(reading => InstrumentViewPlan.IsOn(reading, wantedIndex))
                        && await ConfirmHeldAsync(_settleMs, reading => InstrumentViewPlan.IsOn(reading, wantedIndex));

        if (!verified)
            Log.Debug("Camera", $"Instrument view {wantedIndex} did not verify within {_verifyCapMs} ms (outcome {plan.Outcome})");

        return new InstrumentViewSession(plan.Outcome, verified, home.RestoreTarget, wantedIndex, moved);
    }

    /// <summary>
    /// Puts the pilot's camera back where <see cref="EnterAsync"/> found it: drop the INDEX to
    /// <see cref="NeutralViewIndex"/>, write the TYPE, write the pilot's INDEX — each on its own
    /// frame — then poll and CONFIRM IT HELD. Never throws.
    ///
    /// <para>
    /// ⚠️ The two registers must be written on SEPARATE frames, and the read-back must be
    /// confirmed after a settle. Both halves are measured, on the live iFly 737 MAX8
    /// (2026-09-20, MSFS 2024 1.8.16.0), and the first shipped restore had neither:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     Written BACK TO BACK (one frame, which is what <see cref="ICameraViewIo.Set"/> does),
    ///     the restore does not take. Reproduced exactly with a single calculator write from
    ///     instrument view 0: the camera settled at type 2 / index 7 — the TYPE write refused,
    ///     the INDEX write applied — leaving the pilot in an instrument view they never chose.
    ///     Written as separate round-trips, the identical pair restores the pilot's cabin view
    ///     and still reads back correctly 1.5 s later.
    ///   </description></item>
    ///   <item><description>
    ///     The refused write is READABLE AS SUCCESS for a moment first. The shipped code polled
    ///     immediately, matched that transient, and reported success — so a pilot left on the
    ///     instrument view heard nothing at all. The bogus value lives under ~150 ms, which is
    ///     why <see cref="ConfirmHeldAsync"/> settles and re-reads rather than trusting the first
    ///     matching poll.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// TYPE before INDEX is load-bearing independently of the spacing: while the camera is still
    /// in the instrument type an index write acts immediately, so writing the index first slides
    /// the camera to THAT instrument view and strands the pilot there if the type write is then
    /// refused (measured on the MD-11, 2026-09-09; reproduced on the iFly above). For the same
    /// reason a type write that was never DISPATCHED skips the index write entirely — sending the
    /// index alone is that failure done deliberately.
    /// </para>
    ///
    /// <para>
    /// The ENTRY path deliberately keeps its single back-to-back <see cref="ICameraViewIo.Set"/>:
    /// it is measured working on two aircraft, and it moves INTO the instrument type, where both
    /// registers are in range. Do not "harmonise" the two — the failure is specific to leaving it.
    /// The two paths DO share one settle-then-confirm rule; it is only the write that differs.
    /// </para>
    /// </summary>
    /// <returns>
    /// True when the camera is where the pilot left it, or was never moved. False when it is
    /// somewhere they did not choose: a restore was attempted and the read-back either never
    /// reached the wanted view or did not HOLD it; or the entry dispatched a move with no view to
    /// go back to (<see cref="InstrumentViewPlan.MovedWithNoWayBack"/>). A false return publishes
    /// the view still owed to the pilot, so the NEXT read can finish the job.
    ///
    /// Where a FAILED restore leaves the camera is not promised: the neutral-index write lands
    /// first, so a failure after it leaves the pilot on instrument view
    /// <see cref="NeutralViewIndex"/> rather than on the view the read used. Neither is a view
    /// they chose, the pilot is told either way, and the owed home is what actually gets them
    /// back — so do not write code that assumes the camera sits on the read's own view afterwards.
    /// </returns>
    public async Task<bool> RestoreAsync(InstrumentViewSession session)
    {
        if (InstrumentViewPlan.RestoreWrites(session.RestoreTarget, session.WantedIndex, session.Outcome)
            is not { } writes)
        {
            bool stranded = InstrumentViewPlan.MovedWithNoWayBack(session.RestoreTarget, session.Moved);
            if (!stranded) _home.Clear();
            return !stranded;
        }

        try
        {
            // Drop the index to a value every view type has before touching the TYPE register.
            // The sim validates the PAIR on a type write and refuses one whose CURRENT index is
            // out of range for the type being written — see NeutralViewIndex.
            _io.SetViewIndex(NeutralViewIndex);
            await _delay(_writeGapMs);

            if (_io.SetViewType(writes.Type))
            {
                await _delay(_writeGapMs);
                _io.SetViewIndex(writes.Index);
            }
            else
            {
                Log.Debug("Camera", $"Camera view type {writes.Type} was not dispatched; not sending the index alone");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Restoring camera view type {writes.Type} index {writes.Index} failed: {ex.Message}");
        }

        bool held = await PollUntilAsync(reading => reading.IsAt(writes.Type, writes.Index))
                    && await ConfirmHeldAsync(_holdConfirmMs, reading => reading.IsAt(writes.Type, writes.Index));

        if (held)
        {
            _home.Clear();
            return true;
        }

        // The pilot is not home. Remember where home was, so the next display read aims there
        // instead of at the instrument view this one left them on — without this a failed restore
        // is permanent AND silent after its one warning.
        _home.Owe(session.RestoreTarget);
        Log.Debug("Camera", $"Camera did not return to view type {writes.Type} index {writes.Index}; home is owed");
        return false;
    }

    /// <summary>
    /// Settles, then re-reads and requires the camera to STILL satisfy <paramref name="isThere"/>.
    /// The one rule both <see cref="EnterAsync"/> and <see cref="RestoreAsync"/> use to turn a
    /// matching poll into a verdict, so it can only be stated once — and can only drift once.
    ///
    /// <para>
    /// A poll match is not proof: a refused camera write reads back correct for under ~150 ms and
    /// then reverts. The settle outlasts that, and the read after it is what separates a write
    /// that took from one that only looked like it did.
    /// </para>
    ///
    /// <para>
    /// ⚠️ A read that returns NOTHING is not evidence of reversion. It is a 500 ms SimConnect
    /// timeout, a disconnect, or an aircraft switch — none of which say anything about the
    /// camera. One retry, and then the poll's own verdict stands. Treating an unreadable camera
    /// as a failure is how a correct restore came to announce "Could not return to your previous
    /// view", which is the false alarm that teaches a pilot to ignore the real one.
    /// </para>
    /// </summary>
    private async Task<bool> ConfirmHeldAsync(int settleMs, Func<CameraViewReading, bool> isThere)
    {
        await _delay(settleMs);

        var held = await TryReadAsync() ?? await TryReadAsync();
        if (held is not { } reading)
        {
            Log.Debug("Camera", "Could not re-read the camera to confirm; keeping the verdict the poll reached");
            return true;
        }

        if (isThere(reading)) return true;

        Log.Debug("Camera", $"Camera did not hold after {settleMs} ms (read back {Describe(held)})");
        return false;
    }

    private static string Describe(CameraViewReading? reading)
        => reading is { } r ? $"state {r.State} type {r.ViewType} index {r.ViewIndex}" : "nothing";

    /// <summary>
    /// Polls the camera until <paramref name="isThere"/> matches a reading or the verify cap is
    /// spent, whichever comes first. Elapsed time, not poll steps: a read that waits out its own
    /// timeout counts against the budget too, so a sim that never answers costs about a second,
    /// not seven. Shared by <see cref="EnterAsync"/> and <see cref="RestoreAsync"/> so this rule
    /// can only be stated once — and can only drift once.
    /// </summary>
    private async Task<bool> PollUntilAsync(Func<CameraViewReading, bool> isThere)
    {
        long started = _now();
        while (true)
        {
            var now = await TryReadAsync();
            if (now is { } reading && isThere(reading))
                return true;
            if (_now() - started >= _verifyCapMs) break;
            await _delay(_pollStepMs);
        }
        return false;
    }

    /// <summary>A read that throws is a read that returned nothing: the caller must always get its session.</summary>
    private async Task<CameraViewReading?> TryReadAsync()
    {
        try
        {
            return await _io.ReadAsync(_readTimeoutMs);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Reading the camera view failed: {ex.Message}");
            return null;
        }
    }
}
