using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.Generic;

/// <summary>How a control key dispatches in <see cref="LVarActionExecutor"/>.</summary>
public enum LVarDispatchKind
{
    /// <summary>Held switch/selector: SetLVar(name, value). The default for unlisted keys.</summary>
    LVar,
    /// <summary>Momentary pushbutton: SetLVar 0 → ~200 ms → SetLVar 1 (the Fenix
    /// ExecuteButtonTransition convention — buttons trigger on the 0→1 transition).</summary>
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

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private SimConnectManager? _sc;

    protected SimConnectManager? Sc => _sc;

    /// <summary>Per-aircraft control-key → dispatch-kind table. Keys not present are LVar.</summary>
    protected abstract IReadOnlyDictionary<string, LVarDispatchKind> DispatchTable { get; }

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;

    public bool IsAvailable => _sc is { IsConnected: true };

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
                // WaitSeconds / WaitForCondition / CaptainReminder / WalkAround are
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

    /// <summary>Gated momentary pulse (0 → gap → 1) regardless of table entry.</summary>
    protected async Task<bool> PulseAsync(string name)
    {
        await _gate.WaitAsync();
        try { return await PulseCoreAsync(name); }
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

    private async Task<bool> PulseCoreAsync(string name)
    {
        var sc = _sc;
        if (sc is not { IsConnected: true }) return false;
        await PaceAsync();
        sc.SetLVar(name, 0);
        _lastWriteUtc = DateTime.UtcNow;
        await Task.Delay(PulseGapMs);
        sc.SetLVar(name, 1);
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
