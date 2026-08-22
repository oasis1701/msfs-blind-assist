using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.Generic;

/// <summary>How a control key dispatches in <see cref="LVarActionExecutor"/>.</summary>
public enum LVarDispatchKind
{
    /// <summary>Held switch/selector: SetLVar(name, value). The default for unlisted keys.</summary>
    LVar,
    /// <summary>Momentary pushbutton: SetLVar 0 → ~200 ms → SetLVar 1 → brief hold →
    /// SetLVar 0 (the Fenix ExecuteButtonTransition convention — buttons trigger on the
    /// 0→1 rising edge and latch their effect into a separate I_* indicator, so the
    /// trailing RELEASE loses no state). Never leave a pulse held at 1: level-triggered
    /// functions (the ECAM TO CONFIG test) re-fire a held button against the landing
    /// config after touchdown (FWC phase 9) — the spurious CONFIG warning on rollout.</summary>
    LVarPulse,
    /// <summary>Stock K-event via SendEvent(name, param) — for future FBW controls
    /// (FUELSYSTEM_VALVE_*, ANTI_ICE_SET_ENGn, ...).</summary>
    KEvent,
    /// <summary>H-event via SendEvent (SendEvent routes H: names through MobiFlight).</summary>
    HEvent,
}

/// <summary>
/// Aircraft-agnostic First Officer action executor for L:var-driven aircraft (Fenix now,
/// FBW later). A flow step's EventName is a control key resolved through the subclass's
/// dispatch table; unlisted keys default to a plain held L:var write. All writes go through
/// SimConnectManager.SetLVar, which routes true L:vars through the MobiFlight calculator
/// path when verified — the reliable transport for add-on L:vars.
///
/// A single SemaphoreSlim gate + ~150 ms pacing serialize every dispatch (including
/// SetSwitchMultiple bundles and subclass convenience methods), so two concurrent
/// sequences can never interleave their writes.
/// </summary>
public abstract class LVarActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 150;
    private const int PulseGapMs = 200;

    /// <summary>Default time a pulsed button is held at 1 before being RELEASED back to 0.
    /// The release is the fix for the stuck-at-1 bug (a held S_ECAM_TO re-fired the takeoff-
    /// config check after landing); edge-triggered buttons keep their latched I_* state.</summary>
    private const int PulsePressHoldMs = 200;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private SimConnectManager? _sc;

    protected SimConnectManager? Sc => _sc;

    /// <summary>Per-aircraft control-key → dispatch-kind table. Keys not present are LVar.</summary>
    protected abstract IReadOnlyDictionary<string, LVarDispatchKind> DispatchTable { get; }

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;

    public bool IsAvailable => _sc is { IsConnected: true };

    /// <summary>
    /// IFoActionExecutor — acquire+release the serialize gate. SemaphoreSlim async
    /// waiters queue FIFO, so by the time this acquires, every dispatch queued before
    /// the call has fully completed (ChecklistManager's post-tick grace re-stamp).
    /// </summary>
    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public virtual async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case Models.FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                return await DispatchAsync(step.EventName, step.TargetValue);

            case Models.FlowStepActionType.SetSwitchMultiple:
                await _gate.WaitAsync();
                try
                {
                    bool ok = true;
                    foreach (var (ev, tv) in step.MultiActions)
                        ok &= await DispatchCoreAsync(ev, tv);
                    return ok;
                }
                finally { _gate.Release(); }

            default:
                // WaitSeconds / WaitForCondition / CaptainReminder are
                // handled by FlowManager, never dispatched here.
                return false;
        }
    }

    /// <summary>Single gated dispatch — the entry point for one control write.</summary>
    protected async Task<bool> DispatchAsync(string name, int? target)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(name, target); }
        finally { _gate.Release(); }
    }

    /// <summary>Gated momentary pulse (0 → gap → 1 → hold → 0) regardless of table entry.</summary>
    /// <param name="pressHoldMs">How long to hold the button at 1 before releasing. Use a
    /// longer value for level-triggered tests whose result must be observed while held
    /// (e.g. the Fenix TO CONFIG test).</param>
    /// <param name="onHeld">Optional callback invoked while the button is still held at 1,
    /// just before the release — lets a caller read the resulting state (e.g. the master-
    /// warning outcome of the TO CONFIG test) before the level-triggered effect clears.</param>
    protected async Task<bool> PulseAsync(string name, int pressHoldMs = PulsePressHoldMs,
        Action? onHeld = null)
    {
        await _gate.WaitAsync();
        try { return await PulseCoreAsync(name, pressHoldMs, onHeld); }
        finally { _gate.Release(); }
    }

    /// <summary>Gated held test (1 → holdMs → 0) — fire-test style switches.</summary>
    protected async Task<bool> HoldAsync(string name, int holdMs)
    {
        await _gate.WaitAsync();
        try
        {
            var sc = _sc;
            if (sc is not { IsConnected: true }) return false;
            await PaceAsync();
            sc.SetLVar(name, 1);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(holdMs);
            sc.SetLVar(name, 0);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Run an arbitrary body under the dispatch gate (multi-write helpers).</summary>
    protected async Task RunGatedAsync(Func<Task> body)
    {
        await _gate.WaitAsync();
        try { await body(); }
        finally { _gate.Release(); }
    }

    /// <summary>Ungated single write for use INSIDE RunGatedAsync bodies only.</summary>
    protected async Task<bool> DispatchCoreAsync(string name, int? target)
    {
        var sc = _sc;
        if (sc is not { IsConnected: true }) return false;

        var kind = DispatchTable.GetValueOrDefault(name, LVarDispatchKind.LVar);
        switch (kind)
        {
            case LVarDispatchKind.LVarPulse:
                return await PulseCoreAsync(name);

            case LVarDispatchKind.KEvent:
            case LVarDispatchKind.HEvent:
                await PaceAsync();
                sc.SendEvent(name, (uint)(target ?? 0));
                _lastWriteUtc = DateTime.UtcNow;
                return true;

            default:
                await PaceAsync();
                sc.SetLVar(name, target ?? 1);
                _lastWriteUtc = DateTime.UtcNow;
                return true;
        }
    }

    private async Task<bool> PulseCoreAsync(string name, int pressHoldMs = PulsePressHoldMs,
        Action? onHeld = null)
    {
        var sc = _sc;
        if (sc is not { IsConnected: true }) return false;
        await PaceAsync();
        sc.SetLVar(name, 0);
        _lastWriteUtc = DateTime.UtcNow;
        await Task.Delay(PulseGapMs);
        sc.SetLVar(name, 1);
        _lastWriteUtc = DateTime.UtcNow;
        await Task.Delay(pressHoldMs > 0 ? pressHoldMs : 1);
        // Read the resulting state while the button is still held (e.g. TO CONFIG result).
        // Gated on IsConnected so a disconnect mid-hold can't fire a reassuring
        // "Takeoff config normal." off a stale/zero cache (main-branch fix parity).
        if (sc.IsConnected) onHeld?.Invoke();
        // RELEASE back to 0 — a Fenix pushbutton latches its effect on the 0→1 rising
        // edge, so the release loses no state; leaving it held at 1 was the stuck TO
        // CONFIG bug (spurious CONFIG + master warning after landing).
        sc.SetLVar(name, 0);
        _lastWriteUtc = DateTime.UtcNow;
        return true;
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }
}
