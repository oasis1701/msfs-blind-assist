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
/// The result of <see cref="InstrumentViewSwitcher.EnterAsync"/>. Created by the switcher only.
/// There is no way back on purpose — see <see cref="InstrumentViewPlan"/>: the sim will not
/// accept a user-saved custom camera's own reading as a write, so the pilot returns to their
/// view with their own view key.
/// </summary>
public sealed class InstrumentViewSession
{
    internal InstrumentViewSession(InstrumentViewOutcome outcome, bool verified)
    {
        Outcome = outcome;
        Verified = verified;
    }

    public InstrumentViewOutcome Outcome { get; }

    /// <summary>True when the camera was seen on the wanted view — including when it was there already.</summary>
    public bool Verified { get; }
}

/// <summary>
/// Moves the simulator camera to an instrument view for an AI display read: read, plan
/// (<see cref="InstrumentViewPlan"/>), write, verify by read-back, settle for a rendered frame.
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
        var plan = InstrumentViewPlan.For(await TryReadAsync(), wantedIndex);
        if (plan.Writes is not { } writes)
            return new InstrumentViewSession(plan.Outcome, plan.Outcome == InstrumentViewOutcome.AlreadyThere);

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

        return new InstrumentViewSession(plan.Outcome, verified);
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
