using System;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Drives FlyByWire A380 controls for the First Officer by delegating to
/// <see cref="FlyByWireA380Definition.ApplyUIVariable"/> — the panels' verified
/// write path — plus a small set of pseudo-keys for non-combo actions (FCU
/// managed pushes, baro STD/QNH, AP engage). A suppressed announcer keeps the
/// FO's own step callouts the single voice.
/// </summary>
public sealed class FbwA380ActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 200;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimConnectManager? _sc;
    private FlyByWireA380Definition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(FlyByWireA380Definition def) => _def = def;
    // The REAL app announcer (ScreenReaderAnnouncer is heavyweight — inits NVDA/Tolk —
    // and has no parameterless ctor; NEVER construct a second instance). Writes wrap
    // ApplyUIVariable in a Suppressed toggle (the HS787/monitor pattern) so the def's
    // own Announce() calls ("<name> pressed", "Rudder trim reset") are dropped and the
    // FO's step callouts stay the single voice. All FO-relevant HandleUIVariableSet
    // paths announce via the suppressible Announce(), verified 2026-07.
    public void SetAnnouncer(ScreenReaderAnnouncer a) => _announcer = a;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                return await DispatchAsync(step.EventName, step.TargetValue);

            case FlowStepActionType.SetSwitchMultiple:
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
                return false;
        }
    }

    private async Task<bool> DispatchAsync(string name, int? target)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(name, target); }
        finally { _gate.Release(); }
    }

    // Must be called inside _gate.
    private async Task<bool> DispatchCoreAsync(string name, int? target)
    {
        if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
        await PaceAsync();
        bool ok = name switch
        {
            "BARO_STD"        => FireBaro(std: true),
            "BARO_QNH"        => FireBaro(std: false),
            "FCU_PUSH_SPEED"  => FireFcu("A32NX.FCU_SPD_PUSH"),
            "FCU_PUSH_HEADING"=> FireFcu("A32NX.FCU_TO_AP_HDG_PUSH"),
            "FCU_PUSH_ALT"    => FireFcu("A32NX.FCU_ALT_PUSH"),
            "AP1_ENGAGE"      => FireFcu("A32NX.FCU_AP_1_PUSH"),
            _                 => ApplySilent(name, target ?? 1),
        };
        _lastWriteUtc = DateTime.UtcNow;
        return ok;
    }

    // ApplyUIVariable under a Suppressed wrap: the def's internal Announce() calls are
    // dropped; prior Suppressed state restored (don't clobber the startup grace period).
    private bool ApplySilent(string varKey, double value)
    {
        bool prior = _announcer!.Suppressed;
        _announcer.Suppressed = true;
        try { return _def!.ApplyUIVariable(varKey, value, _sc!, _announcer); }
        finally { _announcer.Suppressed = prior; }
    }

    private bool FireBaro(bool std)
    {
        // Both sides via the def's verified baro branch (fires H:A380X_EFIS_CP_BARO_PUSH/PULL_{1,2}).
        bool ok = ApplySilent("A32NX_FCU_LEFT_EIS_BARO_IS_STD", std ? 1 : 0);
        ok &= ApplySilent("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", std ? 1 : 0);
        return ok;
    }

    private bool FireFcu(string evt)
    {
        _def!.FireFCUButton(evt, _sc!, _announcer!, readback: false);
        return true;
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    // ---- Public write used by checklist CheckActions + convenience methods ----
    /// <summary>Write one control by its A380 varKey (checklist CheckAction path).</summary>
    public Task<bool> Set(string varKey, int value) => DispatchAsync(varKey, value);

    // ---- Convenience methods for the auto-manager / phase monitor ----
    public Task<bool> SetGear(bool down)       => DispatchAsync("A32NX_GEAR_HANDLE_POSITION", down ? 1 : 0);
    public Task<bool> EngageAp1()              => DispatchAsync("AP1_ENGAGE", 1);
    public Task<bool> SetBaroStd(bool std)     => DispatchAsync(std ? "BARO_STD" : "BARO_QNH", 1);
    public Task<bool> SetLandingLights(int on) => DispatchAsync("LIGHT_LANDING", on);
    public Task<bool> SetTaxiLight(int on)     => DispatchAsync("LIGHT_TAXI_OVHD", on);
}
