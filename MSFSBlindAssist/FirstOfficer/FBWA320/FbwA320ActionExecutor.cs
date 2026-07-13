using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

/// <summary>
/// Drives FlyByWire A320 controls for the First Officer by delegating to
/// <see cref="FlyByWireA320Definition.ApplyUIVariable"/> — the panels' verified
/// write path — plus a small set of pseudo-keys for non-combo actions (FCU
/// managed pushes, baro STD/QNH, AP engage, cabin call, ECAM page select). A
/// suppressed announcer keeps the FO's own step callouts the single voice.
/// </summary>
public sealed class FbwA320ActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 200;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SimConnectManager? _sc;
    private FlyByWireA320Definition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(FlyByWireA320Definition def) => _def = def;
    // The REAL app announcer (ScreenReaderAnnouncer is heavyweight — inits NVDA/Tolk —
    // and has no parameterless ctor; NEVER construct a second instance). Writes wrap
    // ApplyUIVariable in a Suppressed toggle (the HS787/monitor pattern) so any
    // set-path Announce() the def makes is dropped and the FO's step callouts stay
    // the single voice.
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
                // Held self-completing fire tests (Fenix/A380 FIRE_TEST_* precedent) — must not
                // reach the generic dispatch (its SetLVar fallback would write a bogus L:var).
                if (step.EventName == "FIRE_TEST_APU" || step.EventName == "FIRE_TEST_ENG1"
                    || step.EventName == "FIRE_TEST_ENG2")
                    return await FireTestAsync(step.EventName);
                // Held self-completing recorder/config tests — same rationale as the fire
                // tests above (must not fall through to the generic SetLVar dispatch).
                if (step.EventName == "CVR_TEST") return await CvrTestAsync();
                if (step.EventName == "TO_CONFIG_TEST") return await TakeoffConfigTestAsync();
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
            "BARO_STD"          => FireBaro(std: true),   // A320: PULL = STD
            "BARO_QNH"          => FireBaro(std: false),  // A320: PUSH = QNH
            "FCU_PUSH_SPEED"    => FireFcu("A32NX.FCU_SPD_PUSH"),
            "FCU_PUSH_HEADING"  => FireFcu("A32NX.FCU_HDG_PUSH"),   // NOT the A380's FCU_TO_AP_HDG_PUSH
            "FCU_PUSH_ALT"      => FireFcu("A32NX.FCU_ALT_PUSH"),
            "AP1_ENGAGE"        => FireFcu("A32NX.FCU_AP_1_PUSH"),
            "CABIN_CALL_ALL"    => await FireCabinCallAsync(),
            _ when name.StartsWith("ECAM_PAGE_") => await FireEcamPageAsync(name),
            _                   => ApplySilent(name, target ?? 1),
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
            // branch and no catch-all prefix would silently no-op through ApplyUIVariable alone.
            _sc!.SetLVar(varKey, value);
            return true;
        }
        finally { _announcer.Suppressed = prior; }
    }

    private bool FireBaro(bool std)
    {
        // A320 PULL = STD, PUSH = QNH (opposite the A380). The _EIS_BARO_IS_STD L:vars are dead.
        string l = std ? "A32NX.FCU_EFIS_L_BARO_PULL" : "A32NX.FCU_EFIS_L_BARO_PUSH";
        string r = std ? "A32NX.FCU_EFIS_R_BARO_PULL" : "A32NX.FCU_EFIS_R_BARO_PUSH";
        _def!.FireFCUButton(l, _sc!, _announcer!, readback: false);
        _def!.FireFCUButton(r, _sc!, _announcer!, readback: false);
        return true;
    }

    private bool FireFcu(string evt)
    {
        _def!.FireFCUButton(evt, _sc!, _announcer!, readback: false);
        return true;
    }

    // Cabin call: release pulse. A stuck 1 = endless horn; write 1, delay, write 0
    // as two separate writes.
    private async Task<bool> FireCabinCallAsync()
    {
        ApplySilent("PUSH_OVHD_CALLS_ALL", 1);
        await Task.Delay(400);
        ApplySilent("PUSH_OVHD_CALLS_ALL", 0);
        return true;
    }

    private static readonly Dictionary<string, int> EcamPageIndex = new()
    {
        ["ECAM_PAGE_ENG"]=0, ["ECAM_PAGE_BLEED"]=1, ["ECAM_PAGE_PRESS"]=2,
        ["ECAM_PAGE_ELEC"]=3, ["ECAM_PAGE_HYD"]=4, ["ECAM_PAGE_FUEL"]=5,
        ["ECAM_PAGE_APU"]=6, ["ECAM_PAGE_COND"]=7, ["ECAM_PAGE_DOOR"]=8,
        ["ECAM_PAGE_WHEEL"]=9, ["ECAM_PAGE_FCTL"]=10, ["ECAM_PAGE_STS"]=12,
    };

    // Direct SD page-index write — held-safe (the earlier ECP press/release pulse could
    // leave a button stuck). PagesContainer reads A32NX_ECAM_SD_CURRENT_PAGE_INDEX as the
    // displayed page; auto-SD logic overrides a manual index on the next auto event (fine
    // for a checklist callout, same as the A380's real page-index write).
    private Task<bool> FireEcamPageAsync(string pseudoKey)
    {
        if (!EcamPageIndex.TryGetValue(pseudoKey, out int idx)) return Task.FromResult(false);
        return Task.FromResult(ApplySilent("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", idx));
    }

    public enum CockpitLightScene { DayPrep, DimFlight, ParkingBright, Off }

    /// <summary>Batched, spaced cockpit-lighting scene write (per spec §4.1). Values tunable
    /// in-sim. Uses the def's brightness-knob keys (ann/dome/compass/integ/flood).</summary>
    public async Task<bool> SetCockpitLighting(CockpitLightScene scene)
    {
        (int ann, int dome, int compass, int integ, int flood) = scene switch
        {
            CockpitLightScene.DayPrep       => (1, 100, 1, 100, 50),
            CockpitLightScene.DimFlight     => (2, 20,  1, 50,  30),
            CockpitLightScene.ParkingBright => (1, 100, 1, 100, 50),
            CockpitLightScene.Off           => (1, 0,   0, 0,   0),
            _                               => (1, 100, 1, 100, 50),
        };
        await Set("A32NX_OVHD_INTLT_ANN", ann);
        await Set("A32NX_OVHD_INTLT_DOME", dome);
        await Set("A32NX_STBY_COMPASS_LIGHT_TOGGLE", compass);
        await Set("BRIGHT_GLARESHIELD_INTEG_SET", integ);
        await Set("BRIGHT_OVERHEAD_INTEG_SET", integ);
        await Set("BRIGHT_MAINPANEL_SET", flood);
        await Set("BRIGHT_PEDESTAL_SET", flood);
        await Set("BRIGHT_GLARESHIELD_CAPT_SET", flood);
        await Set("BRIGHT_GLARESHIELD_FO_SET", flood);
        return true;
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    // ---- Public write used by checklist CheckActions + convenience methods ----
    /// <summary>Write one control by its A320 varKey (checklist CheckAction path).</summary>
    public Task<bool> Set(string varKey, int value) => DispatchAsync(varKey, value);

    /// <summary>Fenix/A380-parity hold for the per-source fire TEST L:vars.</summary>
    public const int FireTestHoldMs = 3000;

    /// <summary>Held fire test: write the per-source A32NX_FIRE_TEST_{APU,ENG1,ENG2} L:var
    /// 1 → hold → 0 through the def's dedicated branch (UiVariableSet.cs), which on the
    /// 0-write also pulses the MASTERAWARN acknowledge so the continuous repetitive chime
    /// cancels. Master warning + CRC while held are the blind-pilot verification. Holds the
    /// gate for the whole test so WaitForDispatchDrainAsync covers the checklist grace.</summary>
    public async Task<bool> FireTestAsync(string source)
    {
        string varKey = source switch
        {
            "FIRE_TEST_APU"  => "A32NX_FIRE_TEST_APU",
            "FIRE_TEST_ENG1" => "A32NX_FIRE_TEST_ENG1",
            "FIRE_TEST_ENG2" => "A32NX_FIRE_TEST_ENG2",
            _                => source,
        };
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            ApplySilent(varKey, 1);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(FireTestHoldMs);
            ApplySilent(varKey, 0);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Fenix/A380-parity hold for the CVR test button.</summary>
    public const int CvrTestHoldMs = 3000;

    /// <summary>Held CVR test: A32NX_RCDR_TEST 1 → hold → 0 (needs
    /// A32NX_RCDR_GROUND_CONTROL_ON on to actually sound the test tone). Holds the gate
    /// for the whole test so WaitForDispatchDrainAsync covers the checklist grace.</summary>
    public async Task<bool> CvrTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            ApplySilent("A32NX_RCDR_TEST", 1);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(CvrTestHoldMs);
            ApplySilent("A32NX_RCDR_TEST", 0);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _gate.Release(); }
    }
    public Task<bool> CvrTest() => CvrTestAsync();

    /// <summary>FWC latch hold for the ECP TO CONFIG TEST button.</summary>
    public const int ToConfigHoldMs = 2000;

    /// <summary>TO CONFIG test: fire the ECP TO_CONF_TEST press H-event, hold, release
    /// (FWC latches the test result at ≥1.5s held). No direct L:var exists for this
    /// button — it is genuinely an ECP momentary, unlike the SD page keys above.</summary>
    public async Task<bool> TakeoffConfigTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            _sc.SendHVar("A32NX_ECP_TO_CONF_TEST_PRESSED");
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(ToConfigHoldMs);
            _sc.SendHVar("A32NX_ECP_TO_CONF_TEST_RELEASED");
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _gate.Release(); }
    }
    public Task<bool> TakeoffConfigTest() => TakeoffConfigTestAsync();

    // ---- Convenience methods for the auto-manager / phase monitor ----
    // GEAR_HANDLE_POSITION is a read-only stock SimVar (no HandleUIVariableSet write branch) —
    // set the gear via the stock GEAR_SET K-event instead (0=up, 1=down), the same pattern
    // HorizonSim787Definition.UiAndHotkeys.cs uses. Bypasses the dispatch gate/ApplySilent
    // (whose SetLVar fallback would write a nonexistent L:var and silently no-op).
    public Task<bool> SetGear(bool down)
    {
        _sc?.SendEvent("GEAR_SET", (uint)(down ? 1 : 0));
        return Task.FromResult(true);
    }
    public Task<bool> EngageAp1()              => DispatchAsync("AP1_ENGAGE", 1);
    public Task<bool> SetBaroStd(bool std)     => DispatchAsync(std ? "BARO_STD" : "BARO_QNH", 1);
    /// <summary>All-landing-lights buttons: On extends+illuminates both LAND lights, Off
    /// retracts them (LANDING_LIGHTS_ON_THIRD_PARTY/OFF_THIRD_PARTY are RenderAsButton events —
    /// any nonzero value fires the press; the nose light is independent, see SetNoseLight).</summary>
    public Task<bool> SetLandingLights(int on) =>
        DispatchAsync(on != 0 ? "LANDING_LIGHTS_ON_THIRD_PARTY" : "LANDING_LIGHTS_OFF_THIRD_PARTY", 1);
    /// <summary>Nose light (LIGHTING_LANDING_1): 0=T.O., 1=Taxi, 2=Off.</summary>
    public Task<bool> SetNoseLight(int pos)    => DispatchAsync("LIGHTING_LANDING_1", pos);
    public Task<bool> CabinCall()              => DispatchAsync("CABIN_CALL_ALL", 1);
}
