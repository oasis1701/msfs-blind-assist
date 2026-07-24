using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// Fenix A320 First Officer action executor. Everything is an L:var write via the base
/// class; the dispatch table lists the momentary pushbuttons (Fenix buttons trigger on the
/// 0 → 1 transition — the def's panel-proven ExecuteButtonTransition convention).
///
/// Fire tests AND the CVR test are HELD switches (1 → 3 s → 0), not pulses — the box being
/// ticked means "test performed" and the test auto-ends, so it never sticks in TEST and the
/// user never has to un-tick to stop it. The cabin CALL pushbuttons are also held (press,
/// brief hold, release — "hit and release"). The FCU managed pushes go
/// through an atomic read-modify-write calculator string rather than an app-side counter:
/// the def's panel (FenixA320Definition.HandleUIVariableSet, S_FCU_SPEED/HEADING_PUSH/PULL)
/// ALSO writes these same L:vars via its own atomic RPN read-modify-write
/// (AdjustFcuPushPullCounter) rather than its rmpCounters absolute counter — both writers
/// now read the live sim value before modifying, so they stay coherent no matter which one
/// fires. Each call's RPN string is prefixed with a per-instance sequence number
/// ("{seq} 0 *", a numeric no-op) so MobiFlight's command channel — which coalesces two
/// consecutive IDENTICAL calc strings — never drops a repeated push (same anti-dedup idiom
/// as FlyByWireA380Definition.SendRmpKey).
/// </summary>
public sealed class FenixActionExecutor : LVarActionExecutor
{
    private const int TestHoldMs = 3000;       // CVR + fire tests: TEST for 3 s, then back to normal.
    private const int CabinCallHoldMs = 600;   // Cabin CALL pushbutton: press, brief hold, release.
    private const int ApuMasterToStartMs = 3000;

    /// <summary>Hold time (ms) for the TO CONFIG test button. Longer than the default pulse
    /// hold because the test is level-triggered (active only while held): the button must
    /// stay pressed long enough for the FWC to evaluate the config and drive the master
    /// warning before we read the result and release (matches the panel path's
    /// FenixA320Definition.TakeoffConfigTestHoldMs).</summary>
    private const int TakeoffConfigTestHoldMs = 1500;

    // Anti-dedup sequence for the FCU push/pull atomic RPN write (see PushFcuManaged).
    private long _fcuPushSeq;

    // The app announcer, injected by FenixFoProfile (FbwA380ActionExecutor precedent) —
    // used only for the TO CONFIG result readout; all step narration stays FlowManager's.
    private Accessibility.ScreenReaderAnnouncer? _announcer;

    public void SetAnnouncer(Accessibility.ScreenReaderAnnouncer a) => _announcer = a;

    private static readonly Dictionary<string, LVarDispatchKind> Table = new()
    {
        ["S_OH_ELEC_EXT_PWR"]      = LVarDispatchKind.LVarPulse,
        ["S_OH_ELEC_APU_START"]    = LVarDispatchKind.LVarPulse,
        ["S_OH_RCRD_GND_CTL"]      = LVarDispatchKind.LVarPulse,
        // NOTE: S_OH_RCRD_TEST (CVR test) is NOT a pulse — it is a 3 s HELD test via
        // CvrTest()/the CVR_TEST pseudo-key (see FireTest); a pulse left it stuck in TEST.
        ["S_MIP_AUTOBRAKE_LO"]     = LVarDispatchKind.LVarPulse,
        ["S_MIP_AUTOBRAKE_MED"]    = LVarDispatchKind.LVarPulse,
        ["S_MIP_AUTOBRAKE_MAX"]    = LVarDispatchKind.LVarPulse,
        ["S_FCU_AP1"]              = LVarDispatchKind.LVarPulse,
        ["S_FCU_AP2"]              = LVarDispatchKind.LVarPulse,
        // LS (localizer) + FD toggles: the ACTUATOR is the BASE var (S_FCU_EFISn_LS/FD).
        // The "_PRESS" name is only MSFSBA's synthetic PANEL key — the def's
        // HandleUIVariableSet redirects _PRESS → a button transition on the base var
        // (FenixA320Definition.cs:11664). The FO writes L:vars directly (bypassing that
        // handler), so writing "_PRESS" is a no-op in the sim — that was the "LS not
        // turning on in approach" bug (and the same latent bug on FD).
        ["S_FCU_EFIS1_LS"]         = LVarDispatchKind.LVarPulse,
        ["S_FCU_EFIS2_LS"]         = LVarDispatchKind.LVarPulse,
        ["S_FCU_EFIS1_FD"]         = LVarDispatchKind.LVarPulse,
        ["S_FCU_EFIS2_FD"]         = LVarDispatchKind.LVarPulse,
        ["S_FC_RUDDER_TRIM_RESET"] = LVarDispatchKind.LVarPulse,
        // TO CONFIG normally routes through TakeoffConfigTest() (long hold + spoken
        // result — intercepted in ExecuteStepAsync); this entry is a release-safe
        // fallback for any stray plain dispatch.
        ["S_ECAM_TO"]              = LVarDispatchKind.LVarPulse,   // TO CONFIG test (takeoff)
        ["S_ECAM_STATUS"]          = LVarDispatchKind.LVarPulse,   // STS status page (landing review)
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
                case "CVR_TEST":                 return CvrTest("S_OH_RCRD_TEST");
                case "FIRE_TEST_APU":            return FireTest("S_OH_FIRE_APU_TEST");
                case "FIRE_TEST_ENG1":           return FireTest("S_OH_FIRE_ENG1_TEST");
                case "FIRE_TEST_ENG2":           return FireTest("S_OH_FIRE_ENG2_TEST");
                case "CABIN_CALL_ALL":           return CabinCall("S_OH_CALLS_ALL");
                case "LANDING_LIGHTS_BOTH":      return SetLandingLights(step.TargetValue ?? 2);
                // TO CONFIG is level-triggered: intercept the real key so the flow's
                // BT_CONFIG step gets the long hold + spoken result, not a plain pulse.
                case "S_ECAM_TO":                return TakeoffConfigTest();
            }
        }
        return base.ExecuteStepAsync(step);
    }

    // -----------------------------------------------------------------------
    // Convenience methods (checklist CheckActions, phase monitor, auto manager)
    // -----------------------------------------------------------------------

    public Task<bool> Set(string lvar, int value) => DispatchAsync(lvar, value);

    public Task<bool> Pulse(string lvar) => PulseAsync(lvar);

    /// <summary>ECAM TO CONFIG test: press-hold-release with the result spoken while the
    /// button is still held. The test is level-triggered — it simulates takeoff power and
    /// checks the config only WHILE pressed — so the button is held 1.5 s for the FWC to
    /// evaluate, the cached master-warning outcome is announced (the blind-pilot equivalent
    /// of the sighted "TO CONFIG NORMAL", which is otherwise completely silent on a good
    /// config), then the button is RELEASED. The release is what prevents the held test
    /// re-firing against the landing config after touchdown (FWC phase 9 → spurious red
    /// CONFIG + master-warning aural on rollout — the original stuck-button bug).</summary>
    public Task<bool> TakeoffConfigTest()
        => PulseAsync("S_ECAM_TO", TakeoffConfigTestHoldMs, AnnounceTakeoffConfigResult);

    /// <summary>Reads the master-warning outcome of the TO CONFIG test (while the button is
    /// still held) and announces it. Deliberately reads the SHARED master-warning latch —
    /// live L-var search (2026-07) confirmed the Fenix exposes NO TO-CONFIG-only indicator.
    /// Fail-safe direction: an already-active unrelated warning yields a conservative
    /// "check configuration" (never harmful); disambiguating via a pre-press baseline delta
    /// would mask a genuinely-bad config behind a pre-existing warning as a DANGEROUS false
    /// "normal". Mirrors FenixA320Definition.AnnounceTakeoffConfigResult (panel path).</summary>
    private void AnnounceTakeoffConfigResult()
    {
        try
        {
            double masterWarning = Sc?.GetCachedVariableValue("I_MIP_MASTER_WARNING_CAPT") ?? 0.0;
            _announcer?.AnnounceImmediate(masterWarning >= 0.5
                ? "Takeoff config: check configuration."
                : "Takeoff config normal.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixFO] AnnounceTakeoffConfigResult error: {ex.Message}");
        }
    }

    /// <summary>Held fire-test switch: TEST for 3 s, back to NORMAL. The fire bell is the
    /// audible verification for a blind pilot.</summary>
    public Task<bool> FireTest(string lvar) => HoldAsync(lvar, TestHoldMs);

    /// <summary>Held CVR test: TEST for 3 s, back to NORMAL — the SAME held mechanism as the
    /// fire tests. A plain pulse left it stuck in TEST (it only ever wrote 1); holding then
    /// releasing means ticking the box performs a self-completing 3 s test (audible test
    /// tone), so the box reliably reflects "test performed" and nothing stays testing.</summary>
    public Task<bool> CvrTest(string lvar) => HoldAsync(lvar, TestHoldMs);

    /// <summary>Momentary cabin CALL pushbutton (CALL ALL / FWD / AFT): press then release —
    /// "hit and release". The 0 → 1 write is the transition that triggers the cabin chime;
    /// the button is then returned to 0 (rest). Fire-and-forget like the tests, so the
    /// checkbox records that the cabin was advised.</summary>
    public Task<bool> CabinCall(string lvar) => HoldAsync(lvar, CabinCallHoldMs);

    /// <summary>Open (true) or close (false) the physical cockpit door via
    /// <c>S_COCKPIT_DOOR</c> (0 = closed, 1 = open). Live-verified 2026-07-05: a calc-path
    /// write holds and drives the "Cockpit Door Open" indicator <c>I_PED_COCKPIT_DOOR_U</c>
    /// 0↔1. Do NOT use the pedestal lock selector <c>S_PED_COCKPIT_DOOR</c> — it is a
    /// spring-loaded UNLOCK/NORM/LOCK switch that rests at NORM(1), so an absolute write is
    /// a no-op (1) or a momentary pulse that springs back (0) and never moves the door.</summary>
    public Task<bool> SetCockpitDoor(bool open) => DispatchAsync("S_COCKPIT_DOOR", open ? 1 : 0);

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

    /// <summary>Seat-belt sign: S_OH_SIGNS held L:var (1 = On, 0 = Off).</summary>
    public Task<bool> SetSeatbeltSign(bool on) => Set("S_OH_SIGNS", on ? 1 : 0);

    /// <summary>Gear lever: true=Down (1), false=Up (0).</summary>
    public Task<bool> SetGear(bool down) => DispatchAsync("S_MIP_GEAR", down ? 1 : 0);

    /// <summary>Engage autopilot 1 (momentary FCU pushbutton).</summary>
    public Task<bool> EngageAp1() => PulseAsync("S_FCU_AP1");

    /// <summary>Push an FCU knob to managed (Fenix convention: push = value decrement on
    /// the knob L:var, e.g. "S_FCU_SPEED" or "S_FCU_HEADING"). Atomic read-modify-write in
    /// ONE calculator string so it can never desync against the def's own panel handler
    /// (which now uses the same atomic-RPN mechanism — see the class doc comment). The
    /// leading "{seq} 0 *" makes the string unique per call so MobiFlight's identical-string
    /// coalescing can't drop a repeated push.</summary>
    public async Task<bool> PushFcuManaged(string knobLVar)
    {
        var sc = Sc;
        if (sc is not { IsConnected: true }) return false;
        await RunGatedAsync(() =>
        {
            long seq = ++_fcuPushSeq;
            sc.ExecuteCalculatorCode($"{seq} 0 * (L:{knobLVar}) 1 - (>L:{knobLVar})");
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
