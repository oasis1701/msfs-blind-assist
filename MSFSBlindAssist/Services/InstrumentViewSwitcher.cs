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

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:0</c> (the type) then <c>:1</c> (the index), back to back.</summary>
    void Set(int viewType, int viewIndex);

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:0</c> (the type) alone, so the caller can space the pair.</summary>
    void SetViewType(int viewType);

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:1</c> (the index) alone, so the caller can space the pair.</summary>
    void SetViewIndex(int viewIndex);
}

/// <summary>
/// What an aircraft definition hands <c>BaseAircraftDefinition.ReadDisplay</c> to have the sim
/// moved to a particular instrument view before the capture: the camera to move (the
/// SimConnectManager) and the 0-based instrument view index from the aircraft's cameras.cfg.
/// </summary>
public sealed record InstrumentViewRequest(ICameraViewIo Camera, int ViewIndex);

/// <summary>
/// The result of <see cref="InstrumentViewSwitcher.EnterAsync"/>, and the way back:
/// <see cref="Before"/> is the camera as it read before the switch, which
/// <see cref="InstrumentViewSwitcher.RestoreAsync"/> writes again after the capture. Created by
/// the switcher only.
/// </summary>
public sealed class InstrumentViewSession
{
    internal InstrumentViewSession(InstrumentViewOutcome outcome, bool verified, CameraViewReading? before)
    {
        Outcome = outcome;
        Verified = verified;
        Before = before;
    }

    public InstrumentViewOutcome Outcome { get; }

    /// <summary>True when the camera was seen on the wanted view — including when it was there already.</summary>
    public bool Verified { get; }

    /// <summary>The camera as it read before the switch, or null when it could not be read.</summary>
    public CameraViewReading? Before { get; }
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
    /// different quantity that happens to share a number today.
    /// </summary>
    public const int DefaultWriteGapMs = 250;

    /// <summary>
    /// How long after a matching read-back the restore waits before re-reading to confirm the
    /// camera HELD. A refused write reads back as success for under ~150 ms (measured: one round
    /// trip already returns the settled wrong state), so this sits past that transient.
    /// </summary>
    public const int DefaultHoldConfirmMs = 250;

    private readonly ICameraViewIo _io;
    private readonly Func<int, Task> _delay;
    private readonly Func<long> _now;
    private readonly int _readTimeoutMs;
    private readonly int _pollStepMs;
    private readonly int _verifyCapMs;
    private readonly int _settleMs;
    private readonly int _writeGapMs;
    private readonly int _holdConfirmMs;

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
        int holdConfirmMs = DefaultHoldConfirmMs)
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
    }

    /// <summary>
    /// Reads the camera, writes the wanted instrument view when a write is called for, polls the
    /// read-back until it matches (giving up after the cap), then waits one settle so the sim has
    /// rendered a frame of the new view. Never throws; the session says what happened.
    /// </summary>
    public async Task<InstrumentViewSession> EnterAsync(int wantedIndex)
    {
        var before = await TryReadAsync();
        var plan = InstrumentViewPlan.For(before, wantedIndex);
        if (plan.Writes is not { } writes)
            return new InstrumentViewSession(plan.Outcome, plan.Outcome == InstrumentViewOutcome.AlreadyThere, before);

        try
        {
            _io.Set(writes.Type, writes.Index);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Setting camera view type {writes.Type} index {writes.Index} failed: {ex.Message}");
        }

        bool verified = await PollUntilAsync(reading => InstrumentViewPlan.IsOn(reading, wantedIndex));

        if (verified) await _delay(_settleMs);
        else Log.Debug("Camera", $"Instrument view {wantedIndex} did not verify within {_verifyCapMs} ms (outcome {plan.Outcome})");

        return new InstrumentViewSession(plan.Outcome, verified, before);
    }

    /// <summary>
    /// Puts the pilot's camera back where <see cref="EnterAsync"/> found it: write the TYPE, let
    /// the sim take it, write the INDEX, let it settle, then read back and CONFIRM IT HELD.
    /// Never throws.
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
    ///     instrument view heard nothing at all. The bogus value lives under ~150 ms (one MCP
    ///     round-trip already reads the settled wrong state), which is why the confirm waits a
    ///     settle and then re-reads rather than trusting the first matching poll.
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// TYPE before INDEX is load-bearing independently of the spacing: while the camera is still
    /// in the instrument type an index write acts immediately, so writing the index first slides
    /// the camera to THAT instrument view and strands the pilot there if the type write is then
    /// refused (measured on the MD-11, 2026-09-09; reproduced on the iFly above).
    /// </para>
    ///
    /// <para>
    /// The ENTRY path deliberately keeps its single back-to-back <see cref="ICameraViewIo.Set"/>:
    /// it is measured working on two aircraft, and it moves INTO the instrument type, where both
    /// registers are in range. Do not "harmonise" the two — the failure is specific to leaving it.
    /// </para>
    /// </summary>
    /// <returns>
    /// True when the camera is where the pilot left it, or was never moved. False when it is
    /// somewhere they did not choose: a restore was attempted and the read-back either never
    /// reached the wanted view or did not HOLD it; or the entry wrote the instrument view with no
    /// reading to remember and that write is confirmed to have landed
    /// (<see cref="InstrumentViewPlan.MovedWithNoWayBack"/>).
    /// </returns>
    public async Task<bool> RestoreAsync(InstrumentViewSession session)
    {
        if (InstrumentViewPlan.RestoreWrites(session.Outcome, session.Before) is not { } writes)
            return !InstrumentViewPlan.MovedWithNoWayBack(session.Outcome, session.Verified);

        try
        {
            _io.SetViewType(writes.Type);
            await _delay(_writeGapMs);
            _io.SetViewIndex(writes.Index);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Restoring camera view type {writes.Type} index {writes.Index} failed: {ex.Message}");
        }

        await _delay(_settleMs);

        if (!await PollUntilAsync(reading => reading.IsAt(writes.Type, writes.Index)))
        {
            Log.Debug("Camera", $"Camera did not return to view type {writes.Type} index {writes.Index} within {_verifyCapMs} ms");
            return false;
        }

        // The match above is not proof: a refused write reads back correct for a moment and then
        // reverts. Only a reading that still agrees after a second settle says the camera stayed.
        await _delay(_holdConfirmMs);
        var held = await TryReadAsync();
        if (held is { } reading && reading.IsAt(writes.Type, writes.Index))
            return true;

        Log.Debug("Camera", $"Camera reached view type {writes.Type} index {writes.Index} but did not hold it after {_holdConfirmMs} ms (read back {Describe(held)})");
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
