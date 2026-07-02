using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// Fenix A320 First Officer action executor. Everything is an L:var write via the base
/// class; the dispatch table lists the momentary pushbuttons (Fenix buttons trigger on the
/// 0 → 1 transition — the def's panel-proven ExecuteButtonTransition convention).
///
/// Fire tests are HELD switches (1 → 3 s → 0), not pulses. The FCU managed pushes go
/// through an atomic read-modify-write calculator string rather than an app-side counter:
/// the def's panel keeps its own rmpCounters for the same L:vars, and a second independent
/// counter would desync deltas (a stale absolute write reads as the WRONG direction).
/// </summary>
public sealed class FenixActionExecutor : LVarActionExecutor
{
    private const int FireTestHoldMs = 3000;
    private const int ApuMasterToStartMs = 3000;

    private static readonly Dictionary<string, LVarDispatchKind> Table = new()
    {
        ["S_OH_ELEC_EXT_PWR"]      = LVarDispatchKind.LVarPulse,
        ["S_OH_ELEC_APU_START"]    = LVarDispatchKind.LVarPulse,
        ["S_OH_RCRD_GND_CTL"]      = LVarDispatchKind.LVarPulse,
        ["S_OH_RCRD_TEST"]         = LVarDispatchKind.LVarPulse,   // CVR test
        ["S_MIP_AUTOBRAKE_LO"]     = LVarDispatchKind.LVarPulse,
        ["S_MIP_AUTOBRAKE_MED"]    = LVarDispatchKind.LVarPulse,
        ["S_MIP_AUTOBRAKE_MAX"]    = LVarDispatchKind.LVarPulse,
        ["S_FCU_AP1"]              = LVarDispatchKind.LVarPulse,
        ["S_FCU_AP2"]              = LVarDispatchKind.LVarPulse,
        ["S_FCU_EFIS1_LS_PRESS"]   = LVarDispatchKind.LVarPulse,
        ["S_FCU_EFIS2_LS_PRESS"]   = LVarDispatchKind.LVarPulse,
        ["S_FC_RUDDER_TRIM_RESET"] = LVarDispatchKind.LVarPulse,
    };

    protected override IReadOnlyDictionary<string, LVarDispatchKind> DispatchTable => Table;

    /// <summary>Pseudo-control keys used by flow steps for actions that aren't a plain
    /// L:var write. Intercepted here; everything else defers to the base dispatch.</summary>
    public override Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (step.ActionType == FlowStepActionType.SetSwitch)
        {
            switch (step.EventName)
            {
                case "FCU_PUSH_SPEED_MANAGED":   return PushFcuManaged("S_FCU_SPEED");
                case "FCU_PUSH_HEADING_MANAGED": return PushFcuManaged("S_FCU_HEADING");
                case "FIRE_TEST_APU":            return FireTest("S_OH_FIRE_APU_TEST");
                case "FIRE_TEST_ENG1":           return FireTest("S_OH_FIRE_ENG1_TEST");
                case "FIRE_TEST_ENG2":           return FireTest("S_OH_FIRE_ENG2_TEST");
            }
        }
        return base.ExecuteStepAsync(step);
    }

    // -----------------------------------------------------------------------
    // Convenience methods (checklist CheckActions, phase monitor, auto manager)
    // -----------------------------------------------------------------------

    public Task<bool> Set(string lvar, int value) => DispatchAsync(lvar, value);

    public Task<bool> Pulse(string lvar) => PulseAsync(lvar);

    /// <summary>Held fire-test switch: TEST for 3 s, back to NORMAL. The fire bell is the
    /// audible verification for a blind pilot.</summary>
    public Task<bool> FireTest(string lvar) => HoldAsync(lvar, FireTestHoldMs);

    /// <summary>Set both EFIS baro references to STD (true) or QNH mode (false). The Fenix
    /// STD state is a plain settable L:var per side — no toggle-push ambiguity.</summary>
    public async Task<bool> SetBaroStdBoth(bool std)
    {
        int v = std ? 1 : 0;
        bool ok = await DispatchAsync("S_FCU_EFIS1_BARO_STD", v);
        ok &= await DispatchAsync("S_FCU_EFIS2_BARO_STD", v);
        return ok;
    }

    /// <summary>Both landing-light switches: 0=Retract, 1=Off, 2=On.</summary>
    public async Task<bool> SetLandingLights(int pos)
    {
        bool ok = await DispatchAsync("S_OH_EXT_LT_LANDING_L", pos);
        ok &= await DispatchAsync("S_OH_EXT_LT_LANDING_R", pos);
        return ok;
    }

    /// <summary>Nose light: 0=Off, 1=Taxi, 2=TO.</summary>
    public Task<bool> SetNoseLight(int pos) => DispatchAsync("S_OH_EXT_LT_NOSE", pos);

    /// <summary>Gear lever: true=Down (1), false=Up (0).</summary>
    public Task<bool> SetGear(bool down) => DispatchAsync("S_MIP_GEAR", down ? 1 : 0);

    /// <summary>Engage autopilot 1 (momentary FCU pushbutton).</summary>
    public Task<bool> EngageAp1() => PulseAsync("S_FCU_AP1");

    /// <summary>Push an FCU knob to managed (Fenix convention: push = value decrement on
    /// the knob L:var, e.g. "S_FCU_SPEED" or "S_FCU_HEADING"). Atomic read-modify-write in
    /// ONE calculator string so it can never desync against the def's own counter.</summary>
    public async Task<bool> PushFcuManaged(string knobLVar)
    {
        var sc = Sc;
        if (sc is not { IsConnected: true }) return false;
        await RunGatedAsync(() =>
        {
            sc.ExecuteCalculatorCode($"(L:{knobLVar}) 1 - (>L:{knobLVar})");
            return Task.CompletedTask;
        });
        return true;
    }

    /// <summary>APU start block: Master ON, dwell, START pulse. The caller (flow/checklist)
    /// separately waits for the AVAIL light.</summary>
    public async Task<bool> StartApuAsync()
    {
        bool ok = await DispatchAsync("S_OH_ELEC_APU_MASTER", 1);
        await Task.Delay(ApuMasterToStartMs);
        ok &= await PulseAsync("S_OH_ELEC_APU_START");
        return ok;
    }
}
