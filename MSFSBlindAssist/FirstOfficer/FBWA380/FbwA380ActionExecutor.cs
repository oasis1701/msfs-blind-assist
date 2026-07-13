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
    // ApplyUIVariable in a Suppressed toggle (the HS787/monitor pattern) so any
    // set-path Announce() the def makes ("<name> pressed", "Rudder trim reset") is
    // dropped and the FO's step callouts stay the single voice. (Most branches the FO
    // drives announce nothing; none uses the unsuppressible AnnounceImmediate.)
    public void SetAnnouncer(ScreenReaderAnnouncer a) => _announcer = a;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

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

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                // Held self-completing fire test (Fenix FIRE_TEST_* precedent) — must not
                // reach the generic dispatch (its SetLVar fallback would write a bogus L:var).
                if (step.EventName == "FIRE_TEST") return await FireTestAsync();
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
        try
        {
            if (_def!.ApplyUIVariable(varKey, value, _sc!, _announcer)) return true;
            // Mirror MainForm's combo-set fallback: a key HandleUIVariableSet doesn't
            // claim (returns false) is written as a plain L:var via SetLVar (calc-path
            // routed when MobiFlight is connected). Without this, a key with no def
            // branch and no catch-all prefix — e.g. PUSH_OVHD_OXYGEN_CREW — would
            // silently no-op through ApplyUIVariable alone.
            _sc!.SetLVar(varKey, value);
            return true;
        }
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

    /// <summary>Fenix-parity hold for the overhead fire TEST pushbutton.</summary>
    public const int FireTestHoldMs = 3000;

    /// <summary>Held fire test: write A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED 1 → hold → 0
    /// through the def's dedicated branch (UiVariableSet.cs), which on the 0-write also
    /// pulses the MASTERAWARN acknowledge so the continuous repetitive chime cancels.
    /// Master warning + CRC while held are the blind-pilot verification. Holds the gate
    /// for the whole test so WaitForDispatchDrainAsync covers the checklist grace.</summary>
    public async Task<bool> FireTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            ApplySilent("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 1);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(FireTestHoldMs);
            ApplySilent("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 0);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _gate.Release(); }
    }

    // ---- Convenience methods for the auto-manager / phase monitor ----
    public Task<bool> SetGear(bool down)       => DispatchAsync("A32NX_GEAR_HANDLE_POSITION", down ? 1 : 0);
    public Task<bool> EngageAp1()              => DispatchAsync("AP1_ENGAGE", 1);
    public Task<bool> SetBaroStd(bool std)     => DispatchAsync(std ? "BARO_STD" : "BARO_QNH", 1);
    public Task<bool> SetLandingLights(int on) => DispatchAsync("LIGHT_LANDING", on);
    /// <summary>Nose light 3-position selector: 0=T.O., 1=Taxi, 2=Off (the old on/off
    /// LIGHT_TAXI_OVHD key was removed with the faithful-switch rework, PR #139).</summary>
    public Task<bool> SetNoseLight(int pos)    => DispatchAsync("NOSE_LIGHT", pos);

    /// <summary>Seat-belt sign: SEATBELT_SIGN position — ON = 0, OFF = 2 (1 = Auto). The
    /// def's ApplyUIVariable drives the stock CABIN SEATBELTS ALERT SWITCH toggle from this.</summary>
    public Task<bool> SetSeatbeltSign(bool on) => Set("SEATBELT_SIGN", on ? 0 : 2);
}
