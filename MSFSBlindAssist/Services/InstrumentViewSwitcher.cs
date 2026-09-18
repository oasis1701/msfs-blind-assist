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

    /// <summary>Writes <c>CAMERA VIEW TYPE AND INDEX:0</c> (the type) then <c>:1</c> (the index).</summary>
    void Set(int viewType, int viewIndex);
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

    private readonly ICameraViewIo _io;
    private readonly Func<int, Task> _delay;
    private readonly Func<long> _now;
    private readonly int _readTimeoutMs;
    private readonly int _pollStepMs;
    private readonly int _verifyCapMs;
    private readonly int _settleMs;

    /// <param name="delay">Task.Delay in production; tests pass a recorder that completes at once.</param>
    /// <param name="now">Monotonic milliseconds — Environment.TickCount64 in production; tests pass a virtual clock the delay advances.</param>
    public InstrumentViewSwitcher(
        ICameraViewIo io,
        Func<int, Task>? delay = null,
        Func<long>? now = null,
        int readTimeoutMs = DefaultReadTimeoutMs,
        int pollStepMs = DefaultPollStepMs,
        int verifyCapMs = DefaultVerifyCapMs,
        int settleMs = DefaultSettleMs)
    {
        _io = io;
        _delay = delay ?? (ms => Task.Delay(ms));
        _now = now ?? (() => Environment.TickCount64);
        _readTimeoutMs = readTimeoutMs;
        _pollStepMs = pollStepMs;
        _verifyCapMs = verifyCapMs;
        _settleMs = settleMs;
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

        bool verified = false;
        long started = _now();
        while (true)
        {
            var now = await TryReadAsync();
            if (now is { } reading && InstrumentViewPlan.IsOn(reading, wantedIndex))
            {
                verified = true;
                break;
            }
            // Elapsed time, not poll steps: a read that waits out its own timeout counts against
            // the budget too, so a sim that never answers costs about a second, not seven.
            if (_now() - started >= _verifyCapMs) break;
            await _delay(_pollStepMs);
        }

        if (verified) await _delay(_settleMs);
        else Log.Debug("Camera", $"Instrument view {wantedIndex} did not verify within {_verifyCapMs} ms (outcome {plan.Outcome})");

        return new InstrumentViewSession(plan.Outcome, verified, before);
    }

    /// <summary>
    /// Puts the pilot's camera back where <see cref="EnterAsync"/> found it, verifying by
    /// read-back. No settle: nothing is captured afterwards. Never throws.
    ///
    /// The write goes through <see cref="ICameraViewIo.Set"/>, which writes the TYPE register
    /// before the INDEX — load-bearing here, not incidental. While the camera is still in the
    /// instrument type an index write acts immediately, so writing the index first slides the
    /// camera to THAT instrument view and leaves the pilot somewhere they never chose if the type
    /// write is then refused (measured on the MD-11, 2026-09-09).
    /// </summary>
    /// <returns>
    /// True when the camera is back, or when nothing needed putting back. False only when a
    /// restore was attempted and the read-back never came back to it — which is detectable
    /// because the sim CLAMPS an index it will not take rather than ignoring the write, and
    /// reports the clamped value (measured 2026-09-18: a write of 20 landed on 8, read back as 8).
    /// </returns>
    public async Task<bool> RestoreAsync(InstrumentViewSession session)
    {
        if (InstrumentViewPlan.RestoreWrites(session.Outcome, session.Before) is not { } writes)
            return true;

        try
        {
            _io.Set(writes.Type, writes.Index);
        }
        catch (Exception ex)
        {
            Log.Debug("Camera", $"Restoring camera view type {writes.Type} index {writes.Index} failed: {ex.Message}");
        }

        long started = _now();
        while (true)
        {
            var now = await TryReadAsync();
            if (now is { } reading && reading.ViewType == writes.Type && reading.ViewIndex == writes.Index)
                return true;
            // Elapsed time, not poll steps — the same rule EnterAsync verifies under.
            if (_now() - started >= _verifyCapMs) break;
            await _delay(_pollStepMs);
        }

        Log.Debug("Camera", $"Camera did not return to view type {writes.Type} index {writes.Index} within {_verifyCapMs} ms");
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
