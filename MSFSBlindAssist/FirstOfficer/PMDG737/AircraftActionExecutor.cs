using System.Threading;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// Dispatch category for a PMDG 737 NG3 control. Mirrors the proven dispatch in
/// <c>PMDG737Definition.HandleUIVariableSet</c> (verified against its
/// <c>_guardedMap</c>, <c>_directionalPushMap</c>, <c>_momentaryToggleMap</c>,
/// <c>_simpleEventMap</c> and the engine-start / fuel-lever special cases).
/// </summary>
public enum Dispatch
{
    /// <summary>CDA direct: <c>SendPMDGEvent(event, id, target)</c> — 2-position switches AND
    /// multi-position selectors (EFIS/isolation/pressurization/IRS send the position as target),
    /// spring selectors (APU/fire-test) and momentary EFIS/MCP buttons (target = 1 press).</summary>
    Simple,
    /// <summary>Guarded: <c>SendPMDGGuardedSet(guard, switch, target)</c>.</summary>
    Guarded,
    /// <summary>Mouse-flag direction from target sign: LEFTSINGLE = on, RIGHTSINGLE = off.
    /// GEN 1/2, GRD PWR, APU GEN 1/2 (PMDG NG3 ignores a bare 0/1 parameter on these).</summary>
    Directional,
    /// <summary>Absolute rotary: <c>SendPMDGEventViaTransmitWithTarget(id, target)</c> —
    /// engine start selectors + ignition (LEFTSINGLE click-walk is directionally inverted).</summary>
    AbsoluteSelector,
    /// <summary>Fuel-control lever: transmit with a directional mouse flag (Run=LEFTSINGLE, Cutoff=RIGHTSINGLE).</summary>
    FuelLever,
    /// <summary>MCP FD / AT Arm: <c>SendPMDGEvent(event, id, MOUSE_FLAG_LEFTSINGLE)</c>.</summary>
    MouseFlag,
}

/// <summary>
/// Sends PMDG 737 NG3 switch events for the flow engine, implementing the shared
/// <see cref="IFoActionExecutor"/> contract. Unlike the 777 (one synchronous event per
/// switch), the 737 needs per-control dispatch categories, several asynchronous — so the
/// engine awaits <see cref="ExecuteStepAsync"/>. All event names are verified against
/// <c>PMDG737Definition.EventIds</c>.
/// </summary>
public class AircraftActionExecutor : IFoActionExecutor
{
    private SimConnectManager? _sc;
    private const int MouseFlagLeftSingle  = unchecked((int)0x20000000);
    private const int MouseFlagRightSingle = unchecked((int)0x80000000);

    // PMDG CDA control writes are COALESCED when issued in the same frame — only the LAST
    // survives (documented PMDG gotcha). So every helper that fires more than one switch
    // (generators, probe heat, fuel pumps, window heat, …) must SPACE its writes, or the
    // first is silently lost (the "generators / probe heat didn't come on" bugs). Matches
    // the flow's PostActionDelayMs and the 777 executor's CdaWriteSpacingMs.
    private const int CdaWriteSpacingMs = 350;
    // APU selector ON → START needs a dwell so the model registers ON before the momentary
    // START, otherwise the APU never spools up (same fix as the 777 StartApuAsync).
    private const int ApuOnToStartMs = 2000;

    // Serializes ALL CDA dispatch so two concurrent spaced sequences (e.g. a checklist tick
    // landing while a flow's MultiAsync is mid-spacing, or two quick multi-write ticks) can't
    // interleave their writes into the same sim frame — which would re-introduce the exact
    // same-frame coalescing this executor spaces to avoid. A whole sequence (MultiAsync /
    // FireSpacedAsync) holds the gate across its inter-write delays; single writes hold it for
    // their one write. Sequences call DispatchCoreAsync (no gate) to avoid re-entrant deadlock.
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);

    // UTC of the last write sent through DispatchCoreAsync. PaceAsync (called at the top of
    // every dispatch, INSIDE the gate) delays until CdaWriteSpacingMs has elapsed since the
    // previous write — so ANY two consecutive writes are frame-spaced, including two
    // back-to-back single Fire() calls from one CheckAction lambda (e.g. "start switches:
    // CONT" firing both selectors), which previously shared a frame and coalesced.
    private DateTime _lastWriteUtc = DateTime.MinValue;

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(CdaWriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public bool IsAvailable => _sc is { IsConnected: true };

    private sealed record Spec(Dispatch Kind, string? Guard = null);

    // Event -> dispatch category. Events NOT listed default to Simple.
    // Built by inverting PMDG737Definition's dispatch maps.
    private static readonly Dictionary<string, Spec> Table = new()
    {
        // --- Guarded (switch event -> guard event), from _guardedMap ---
        ["EVT_OH_ELEC_BATTERY_SWITCH"]       = new(Dispatch.Guarded, "EVT_OH_ELEC_BATTERY_GUARD"),
        ["EVT_OH_ELEC_DISCONNECT_1_SWITCH"]  = new(Dispatch.Guarded, "EVT_OH_ELEC_DISCONNECT_1_GUARD"),
        ["EVT_OH_ELEC_DISCONNECT_2_SWITCH"]  = new(Dispatch.Guarded, "EVT_OH_ELEC_DISCONNECT_2_GUARD"),
        ["EVT_OH_ELEC_STBY_PWR_SWITCH"]      = new(Dispatch.Guarded, "EVT_OH_ELEC_STBY_PWR_GUARD"),
        ["EVT_OH_ELEC_BUS_TRANSFER_SWITCH"]  = new(Dispatch.Guarded, "EVT_OH_ELEC_BUS_TRANSFER_GUARD"),
        ["EVT_OH_FUEL_GND_XFR_SW"]           = new(Dispatch.Guarded, "EVT_OH_FUEL_GND_XFR_GUARD"),
        ["EVT_OH_FCTL_A_SWITCH"]             = new(Dispatch.Guarded, "EVT_OH_FCTL_A_GUARD"),
        ["EVT_OH_FCTL_B_SWITCH"]             = new(Dispatch.Guarded, "EVT_OH_FCTL_B_GUARD"),
        ["EVT_OH_SPOILER_A_SWITCH"]          = new(Dispatch.Guarded, "EVT_OH_SPOILER_A_GUARD"),
        ["EVT_OH_SPOILER_B_SWITCH"]          = new(Dispatch.Guarded, "EVT_OH_SPOILER_B_GUARD"),
        ["EVT_OH_ALT_FLAPS_MASTER_SWITCH"]   = new(Dispatch.Guarded, "EVT_OH_ALT_FLAPS_MASTER_GUARD"),
        ["EVT_OH_EEC_L_SWITCH"]              = new(Dispatch.Guarded, "EVT_OH_EEC_L_GUARD"),
        ["EVT_OH_EEC_R_SWITCH"]              = new(Dispatch.Guarded, "EVT_OH_EEC_R_GUARD"),
        ["EVT_OH_OXY_PASS_SWITCH"]           = new(Dispatch.Guarded, "EVT_OH_OXY_PASS_GUARD"),
        ["EVT_OH_FLTREC_SWITCH"]             = new(Dispatch.Guarded, "EVT_OH_FLTREC_GUARD"),
        ["EVT_OH_EMER_EXIT_LIGHT_SWITCH"]    = new(Dispatch.Guarded, "EVT_OH_EMER_EXIT_LIGHT_GUARD"),
        ["EVT_GPWS_FLAP_INHIBIT_SWITCH"]     = new(Dispatch.Guarded, "EVT_GPWS_FLAP_INHIBIT_GUARD"),
        ["EVT_GPWS_GEAR_INHIBIT_SWITCH"]     = new(Dispatch.Guarded, "EVT_GPWS_GEAR_INHIBIT_GUARD"),
        ["EVT_GPWS_TERR_INHIBIT_SWITCH"]     = new(Dispatch.Guarded, "EVT_GPWS_TERR_INHIBIT_GUARD"),
        ["EVT_NOSE_WHEEL_STEERING_SWITCH"]   = new(Dispatch.Guarded, "EVT_NOSE_WHEEL_STEERING_SWITCH_GUARD"),
        ["EVT_CARGO_FIRE_ARM_SWITCH_FWD"]    = new(Dispatch.Guarded, "EVT_CARGO_FIRE_DISC_SWITCH_GUARD"),
        ["EVT_CARGO_FIRE_ARM_SWITCH_AFT"]    = new(Dispatch.Guarded, "EVT_CARGO_FIRE_DISC_SWITCH_GUARD"),

        // --- Directional mouse-flag (from _directionalPushMap + _momentaryToggleMap) ---
        ["EVT_OH_ELEC_GEN1_SWITCH"]          = new(Dispatch.Directional),
        ["EVT_OH_ELEC_GEN2_SWITCH"]          = new(Dispatch.Directional),
        ["EVT_OH_ELEC_GRD_PWR_SWITCH"]       = new(Dispatch.Directional),
        ["EVT_OH_ELEC_APU_GEN1_SWITCH"]      = new(Dispatch.Directional),
        ["EVT_OH_ELEC_APU_GEN2_SWITCH"]      = new(Dispatch.Directional),

        // --- Absolute rotary selectors (engine start + ignition special-cased) ---
        ["EVT_OH_LIGHTS_L_ENGINE_START"]     = new(Dispatch.AbsoluteSelector),
        ["EVT_OH_LIGHTS_R_ENGINE_START"]     = new(Dispatch.AbsoluteSelector),
        ["EVT_OH_LIGHTS_IGN_SEL"]            = new(Dispatch.AbsoluteSelector),

        // --- Fuel-control levers ---
        ["EVT_CONTROL_STAND_ENG1_START_LEVER"] = new(Dispatch.FuelLever),
        ["EVT_CONTROL_STAND_ENG2_START_LEVER"] = new(Dispatch.FuelLever),

        // --- MCP mouse-flag momentary toggles ---
        ["EVT_MCP_FD_SWITCH_L"]              = new(Dispatch.MouseFlag),
        ["EVT_MCP_FD_SWITCH_R"]              = new(Dispatch.MouseFlag),
        ["EVT_MCP_AT_ARM_SWITCH"]            = new(Dispatch.MouseFlag),

        // --- Flap-lever per-detent clicks ---
        // SDK mouse-click events: the CDA parameter must be MOUSE_FLAG_LEFTSINGLE
        // (the 777 panel's live-verified convention for this same event family —
        // see PMDG777Definition.HandleUIVariableSet 2f). A plain param=1 is
        // silently ignored, which is why FO auto-flaps never moved the lever.
        ["EVT_CONTROL_STAND_FLAPS_LEVER_0"]  = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_1"]  = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_2"]  = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_5"]  = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_10"] = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_15"] = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_25"] = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_30"] = new(Dispatch.MouseFlag),
        ["EVT_CONTROL_STAND_FLAPS_LEVER_40"] = new(Dispatch.MouseFlag),
    };

    // -----------------------------------------------------------------------
    // Core dispatch (IFoActionExecutor)
    // -----------------------------------------------------------------------

    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return Task.FromResult(false);
        return step.ActionType switch
        {
            Models.FlowStepActionType.SetSwitch         => DispatchAsync(step.EventName!, step.TargetValue),
            Models.FlowStepActionType.SetSwitchMultiple => MultiAsync(step.MultiActions),
            _                                           => Task.FromResult(false),
        };
    }

    private async Task<bool> MultiAsync(IReadOnlyList<(string EventName, int? TargetValue)> actions)
    {
        // Hold the gate across the WHOLE sequence so a concurrent dispatch can't slip a
        // write into one of our frame gaps and coalesce. Inter-write spacing comes from
        // PaceAsync inside DispatchCoreAsync.
        await _dispatchGate.WaitAsync();
        try
        {
            bool ok = true;
            foreach (var (ev, tv) in actions)
                ok &= await DispatchCoreAsync(ev, tv);
            return ok;
        }
        finally { _dispatchGate.Release(); }
    }

    // Single-write public entry — gated so it can't land in the same frame as a concurrent
    // sequence's write. Sequences must NOT call this (they call DispatchCoreAsync) or they
    // would deadlock re-acquiring the non-reentrant gate.
    private async Task<bool> DispatchAsync(string eventName, int? target)
    {
        await _dispatchGate.WaitAsync();
        try { return await DispatchCoreAsync(eventName, target); }
        finally { _dispatchGate.Release(); }
    }

    private async Task<bool> DispatchCoreAsync(string eventName, int? target)
    {
        if (_sc == null || !PMDG737Definition.EventIds.TryGetValue(eventName, out int evId)) return false;
        uint id = (uint)evId;
        var spec = Table.GetValueOrDefault(eventName);
        Dispatch kind = spec?.Kind ?? Dispatch.Simple;
        int t = target ?? 1;

        // Frame-space EVERY write relative to the previous one (see _lastWriteUtc) —
        // callers no longer need their own inter-write delays.
        await PaceAsync();
        try
        {
            switch (kind)
            {
                case Dispatch.Simple:
                    _sc.SendPMDGEvent(eventName, id, target);
                    return true;

                case Dispatch.MouseFlag:
                    _sc.SendPMDGEvent(eventName, id, MouseFlagLeftSingle);
                    return true;

                case Dispatch.Directional:
                    _sc.SendPMDGEvent(eventName, id, t > 0 ? MouseFlagLeftSingle : MouseFlagRightSingle);
                    // Engine GEN 1/2 state is locally tracked in PMDGNG3DataManager (PMDG's
                    // raw ELEC_GenSw bool lies — always ~1 at rest). The panel path notifies
                    // it on every dispatch; without this mirror the FO's generator writes were
                    // invisible to the checklist auto-detect (BT_GEN never ticked) and to the
                    // panel combos. GPU / APU GENs are stateless push pairs — nothing to track.
                    if (eventName == "EVT_OH_ELEC_GEN1_SWITCH" || eventName == "EVT_OH_ELEC_GEN2_SWITCH")
                        (_sc.PMDGDataManager as PMDGNG3DataManager)?.NotifyLocalSwitchState(
                            eventName == "EVT_OH_ELEC_GEN1_SWITCH" ? "ELEC_GenSw_0" : "ELEC_GenSw_1", t > 0);
                    return true;

                case Dispatch.AbsoluteSelector:
                    _sc.SendPMDGEventViaTransmitWithTarget(id, (uint)(target ?? 0));
                    return true;

                case Dispatch.FuelLever:
                    _sc.SendPMDGEventViaTransmitWithTarget(id, (uint)(t != 0 ? MouseFlagLeftSingle : MouseFlagRightSingle));
                    return true;

                case Dispatch.Guarded:
                    if (spec?.Guard != null && PMDG737Definition.EventIds.TryGetValue(spec.Guard, out int gId))
                        await _sc.SendPMDGGuardedSet(spec.Guard, (uint)gId, eventName, id, target ?? 0);
                    return true;

                default:
                    _sc.SendPMDGEvent(eventName, id, target);
                    return true;
            }
        }
        finally
        {
            // Stamp AFTER the dispatch so multi-write mechanisms (guarded sequences) get a
            // full frame gap from their LAST write, not their first.
            _lastWriteUtc = DateTime.UtcNow;
        }
    }

    // -----------------------------------------------------------------------
    // Named convenience actions (used by checklist CheckAction lambdas).
    // Fire-and-forget the async dispatch; ordering only matters inside an awaited flow.
    // -----------------------------------------------------------------------

    private bool Fire(string ev, int? target) { _ = DispatchAsync(ev, target); return true; }

    // Fire two or more switch events with a frame gap between each so the PMDG CDA writes
    // don't coalesce (only the last same-frame write survives). Fire-and-forget: the spaced
    // sequence runs in the background while the synchronous CheckAction returns.
    private bool FireBoth(string a, string b, int? t) => FireSpaced((a, t), (b, t));

    private bool FireSpaced(params (string ev, int? target)[] actions)
    {
        _ = FireSpacedAsync(actions);
        return true;
    }

    private async Task FireSpacedAsync((string ev, int? target)[] actions)
    {
        // Same gate as MultiAsync — hold it across the whole sequence (see _dispatchGate).
        // Inter-write spacing comes from PaceAsync inside DispatchCoreAsync.
        await _dispatchGate.WaitAsync();
        try
        {
            foreach (var (ev, t) in actions)
                await DispatchCoreAsync(ev, t);
        }
        finally { _dispatchGate.Release(); }
    }

    // Momentary MCP / EFIS push-buttons (baro STD, AP CMD) commit on the NG3 ONLY via a
    // LEFTSINGLE+LEFTRELEASE pair (SendPMDGMomentaryToggle). A bare CDA param=1 plays the
    // click but never commits — see PMDG737Definition's _simpleEventMap momentary branch.
    // Routed through the same gate (held across the internal press→release) so it can't
    // collide with a concurrent dispatch — and so back-to-back Capt+FO baro pushes serialize.
    private bool FireMomentaryToggle(string eventName)
    {
        var sc = _sc;
        if (sc != null && PMDG737Definition.EventIds.TryGetValue(eventName, out int evId))
            _ = FireMomentaryToggleAsync(sc, (uint)evId);
        return true;
    }

    private async Task FireMomentaryToggleAsync(SimConnectManager sc, uint evId)
    {
        await _dispatchGate.WaitAsync();
        try
        {
            await PaceAsync();
            await sc.SendPMDGMomentaryToggle(evId, 1);
        }
        finally
        {
            _lastWriteUtc = DateTime.UtcNow;
            _dispatchGate.Release();
        }
    }

    /// <summary>APU start sequence: selector ON, dwell, then momentary START (springs back to ON).
    /// A direct write to START without first passing through ON does not spool the APU up.</summary>
    public async Task StartApuAsync()
    {
        SetApuSelector(1);                  // ON
        await Task.Delay(ApuOnToStartMs);
        SetApuSelector(2);                  // START
    }

    /// <summary>Both flight directors to a target state — the checklist-tick equivalent of
    /// the Preflight flow's PF_FD1/PF_FD2 mouse-flag steps. FD switches are TOGGLES, so each
    /// side is pressed only when its current state differs from the target (the same guard
    /// the flow's SkipCondition applies); presses are frame-spaced via the paced gate.</summary>
    public async Task SetFlightDirectorsAsync(int targetOn, AircraftStateEvaluator state)
    {
        var presses = new List<(string ev, int? target)>();
        if ((state.IsFDLeftOn()  ? 1 : 0) != targetOn) presses.Add(("EVT_MCP_FD_SWITCH_L", null));
        if ((state.IsFDRightOn() ? 1 : 0) != targetOn) presses.Add(("EVT_MCP_FD_SWITCH_R", null));
        if (presses.Count > 0) await FireSpacedAsync(presses.ToArray());
    }

    // (The former StartEngineAsync monolithic checklist start was removed 2026-07-02:
    // the checklist Engine Start group is now pilot-paced — separate start-switch and
    // start-lever items, 777 convention — and the FLOW remains the automated path with
    // its own N2/start-valve guards and spoken waits.)

    /// <summary>Press the System Annunciator six-pack (RECALL) — latches every active
    /// annunciator + master caution so the app's annunciator monitors announce them; the
    /// captain resets with the Master Caution light (panel control WARN_ResetMasterCaution).</summary>
    public bool PressRecall() => FireMomentaryToggle("EVT_SYSTEM_ANNUNCIATOR_PANEL_LEFT");

    /// <summary>Set pressurization FLT ALT + LAND ALT from the stored SimBrief plan
    /// (values pre-rounded/clamped at evaluator storage; the direct-control events take
    /// literal feet). SPACED — two CDA writes must not share a sim frame. A missing plan
    /// value is skipped; with no plan at all this is a no-op (the checklist item then
    /// behaves like the old manual reminder).</summary>
    public async Task SetPressurizationAltitudesAsync(AircraftStateEvaluator state)
    {
        var actions = new List<(string ev, int? target)>();
        if (state.PlannedFltAltFt  is int f) actions.Add(("EVT_OH_PRESS_FLT_ALT_SET", f));
        if (state.PlannedLandAltFt is int l) actions.Add(("EVT_OH_PRESS_LAND_ALT_SET", l));
        if (actions.Count > 0) await FireSpacedAsync(actions.ToArray());
    }

    // Electrical
    public bool SetBattery(int p)        => Fire("EVT_OH_ELEC_BATTERY_SWITCH", p);          // 2=ON
    public bool SetStandbyPower(int p)   => Fire("EVT_OH_ELEC_STBY_PWR_SWITCH", p);         // 2=AUTO
    public bool SetGroundPower(int p)    => Fire("EVT_OH_ELEC_GRD_PWR_SWITCH", p);
    public bool SetGenerators(int p)     => FireBoth("EVT_OH_ELEC_GEN1_SWITCH", "EVT_OH_ELEC_GEN2_SWITCH", p);
    public bool SetApuGenerators(int p)  => FireBoth("EVT_OH_ELEC_APU_GEN1_SWITCH", "EVT_OH_ELEC_APU_GEN2_SWITCH", p);

    // APU
    public bool SetApuSelector(int p)    => Fire("EVT_OH_LIGHTS_APU_START", p);             // 0=OFF,1=ON,2=START

    // Fuel
    public bool SetWingFuelPumps(int p) => FireSpaced(
        ("EVT_OH_FUEL_PUMP_1_FORWARD", p), ("EVT_OH_FUEL_PUMP_2_FORWARD", p),
        ("EVT_OH_FUEL_PUMP_1_AFT", p),     ("EVT_OH_FUEL_PUMP_2_AFT", p));
    public bool SetCenterFuelPumps(int p) => FireBoth("EVT_OH_FUEL_PUMP_L_CENTER", "EVT_OH_FUEL_PUMP_R_CENTER", p);
    public bool SetCrossfeed(int p)       => Fire("EVT_OH_FUEL_CROSSFEED", p);

    // Engine start selectors / ignition / fuel-control levers (1=Run, 0=Cutoff)
    public bool SetEngStartSelector1(int p) => Fire("EVT_OH_LIGHTS_L_ENGINE_START", p);     // 0=GRD,1=OFF,2=CONT,3=FLT
    public bool SetEngStartSelector2(int p) => Fire("EVT_OH_LIGHTS_R_ENGINE_START", p);
    public bool SetIgnition(int p)          => Fire("EVT_OH_LIGHTS_IGN_SEL", p);
    public bool SetFuelControl1(int run)    => Fire("EVT_CONTROL_STAND_ENG1_START_LEVER", run);
    public bool SetFuelControl2(int run)    => Fire("EVT_CONTROL_STAND_ENG2_START_LEVER", run);

    // Hydraulics
    public bool SetEngHydPumps(int p)    => FireBoth("EVT_OH_HYD_ENG1", "EVT_OH_HYD_ENG2", p);
    public bool SetElecHydPumps(int p)   => FireBoth("EVT_OH_HYD_ELEC1", "EVT_OH_HYD_ELEC2", p);

    // Air / bleed / pressurization
    public bool SetPacks(int p)          => FireBoth("EVT_OH_BLEED_PACK_L_SWITCH", "EVT_OH_BLEED_PACK_R_SWITCH", p);
    public bool SetEngBleeds(int p)      => FireBoth("EVT_OH_BLEED_ENG_1_SWITCH", "EVT_OH_BLEED_ENG_2_SWITCH", p);
    public bool SetApuBleed(int p)       => Fire("EVT_OH_BLEED_APU_SWITCH", p);
    public bool SetIsolationValve(int p) => Fire("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH", p); // 0=CLOSE,1=AUTO,2=OPEN
    public bool SetTrimAir(int p)        => Fire("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_800", p);
    public bool SetRecircFans(int p)     => FireBoth("EVT_OH_BLEED_RECIRC_FAN_L_SWITCH", "EVT_OH_BLEED_RECIRC_FAN_R_SWITCH", p);

    // Anti-ice
    public bool SetWindowHeat(int p) => FireSpaced(
        ("EVT_OH_ICE_WINDOW_HEAT_1", p), ("EVT_OH_ICE_WINDOW_HEAT_2", p),
        ("EVT_OH_ICE_WINDOW_HEAT_3", p), ("EVT_OH_ICE_WINDOW_HEAT_4", p));
    public bool SetProbeHeat(int p)      => FireBoth("EVT_OH_ICE_PROBE_HEAT_1", "EVT_OH_ICE_PROBE_HEAT_2", p);
    public bool SetWingAntiIce(int p)    => Fire("EVT_OH_ICE_WING_ANTIICE", p);
    public bool SetEngAntiIce(int p)     => FireBoth("EVT_OH_ICE_ENGINE_ANTIICE_1", "EVT_OH_ICE_ENGINE_ANTIICE_2", p);

    // Lights
    public bool SetBeacon(int p)         => Fire("EVT_OH_LIGHTS_ANT_COL", p);
    public bool SetPositionLights(int p) => Fire("EVT_OH_LIGHTS_POS_STROBE", p);            // 0=STEADY,1=OFF,2=STROBE&STEADY
    public bool SetLogo(int p)           => Fire("EVT_OH_LIGHTS_LOGO", p);
    public bool SetWingLights(int p)     => Fire("EVT_OH_LIGHTS_WING", p);
    public bool SetTaxiLights(int p)     => Fire("EVT_OH_LIGHTS_TAXI", p);
    public bool SetRunwayTurnoff(int p)  => FireBoth("EVT_OH_LIGHTS_L_TURNOFF", "EVT_OH_LIGHTS_R_TURNOFF", p);
    // ALL FOUR landing lights: the two retractables (0=RETRACT,1=EXTEND,2=ON) get p,
    // the two FIXED inboard lights (2-position bool) get ON only when p==2. The fixed
    // pair was previously never touched by any FO path — "landing lights on/off" left
    // the inboards wherever they were.
    public bool SetLandingLights(int p)  => FireSpaced(
        ("EVT_OH_LIGHTS_L_RETRACT", p), ("EVT_OH_LIGHTS_R_RETRACT", p),
        ("EVT_OH_LIGHTS_L_FIXED", p >= 2 ? 1 : 0), ("EVT_OH_LIGHTS_R_FIXED", p >= 2 ? 1 : 0));

    // Signs
    public bool SetSeatBelts(int p)      => Fire("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", p);    // 0=OFF,1=AUTO,2=ON
    public bool SetNoSmoking(int p)      => Fire("EVT_OH_NO_SMOKING_LIGHT_SWITCH", p);
    public bool SetEmerExitLights(int p) => Fire("EVT_OH_EMER_EXIT_LIGHT_SWITCH", p);       // 0=OFF,1=ARMED,2=ON (guarded)

    // Flight controls / pedestal
    public bool SetYawDamper(int p)      => Fire("EVT_OH_YAW_DAMPER", p);
    public bool SetGearLever(int p)      => Fire("EVT_GEAR_LEVER", p);                      // 0=UP,2=DOWN
    public bool SetAutobrake(int p)      => Fire("EVT_MPM_AUTOBRAKE_SELECTOR", p);          // 0=RTO,1=OFF..5=MAX
    public bool SetAltFlapsPos(int p)    => Fire("EVT_OH_ALT_FLAPS_POS_SWITCH", p);
    public bool SetSpeedbrakeArmed()     => Fire("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", 1);
    public bool SetSpeedbrakeDown()      => Fire("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", 1);

    // Flap lever (per-detent momentary press): 0=UP,1,2,5,10,15,25,30,40
    public bool SetFlapsPosition(int detent)
    {
        string? ev = detent switch
        {
            0 => "EVT_CONTROL_STAND_FLAPS_LEVER_0",
            1 => "EVT_CONTROL_STAND_FLAPS_LEVER_1",
            2 => "EVT_CONTROL_STAND_FLAPS_LEVER_2",
            5 => "EVT_CONTROL_STAND_FLAPS_LEVER_5",
            10 => "EVT_CONTROL_STAND_FLAPS_LEVER_10",
            15 => "EVT_CONTROL_STAND_FLAPS_LEVER_15",
            25 => "EVT_CONTROL_STAND_FLAPS_LEVER_25",
            30 => "EVT_CONTROL_STAND_FLAPS_LEVER_30",
            40 => "EVT_CONTROL_STAND_FLAPS_LEVER_40",
            _ => null,
        };
        return ev != null && Fire(ev, 1);
    }

    // MCP / EFIS (FD + AT Arm are toggles — press only if the target differs from current)
    public bool SetFDLeft(int targetOn, AircraftStateEvaluator state)
        => (state.IsFDLeftOn() ? 1 : 0) == targetOn || Fire("EVT_MCP_FD_SWITCH_L", null);
    public bool SetFDRight(int targetOn, AircraftStateEvaluator state)
        => (state.IsFDRightOn() ? 1 : 0) == targetOn || Fire("EVT_MCP_FD_SWITCH_R", null);
    public bool SetATArm(int targetOn, AircraftStateEvaluator state)
        => (state.IsATArmOn() ? 1 : 0) == targetOn || Fire("EVT_MCP_AT_ARM_SWITCH", null);
    public bool PushAPCmd()              => FireMomentaryToggle("EVT_MCP_CMD_A_SWITCH");
    public bool SetEFISModeCapt(int p)   => Fire("EVT_EFIS_CPT_MODE", p);
    public bool SetEFISModeFO(int p)     => Fire("EVT_EFIS_FO_MODE", p);
    public bool SetEFISRangeCapt(int p)  => Fire("EVT_EFIS_CPT_RANGE", p);
    public bool SetEFISRangeFO(int p)    => Fire("EVT_EFIS_FO_RANGE", p);
    public bool PushBaroSTDCapt()        => FireMomentaryToggle("EVT_EFIS_CPT_BARO_STD");
    public bool PushBaroSTDFO()          => FireMomentaryToggle("EVT_EFIS_FO_BARO_STD");

    // IRS / display / transponder
    public bool SetIrsMode(int p)        => FireBoth("EVT_IRU_MSU_LEFT", "EVT_IRU_MSU_RIGHT", p); // 2=NAV
    public bool SetLowerDUCapt(int p)    => Fire("EVT_DSP_CPT_LOWER_DU_SELECTOR", p);
    public bool SetLowerDUFO(int p)      => Fire("EVT_DSP_FO_LOWER_DU_SELECTOR", p);
    public bool SetTransponderMode(int m)=> Fire("EVT_TCAS_MODE", m);                       // 0=STBY..4=TA/RA
    public bool SetWipers(int p)         => FireBoth("EVT_OH_WIPER_LEFT_CONTROL", "EVT_OH_WIPER_RIGHT_CONTROL", p);
}
