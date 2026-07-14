using System.Threading;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Sends PMDG 777 switch events on behalf of the flow engine.
/// Uses the same PMDG event IDs and parameter rules as the rest of BA Assist.
///
/// THREADING/PACING (2026-07-02, mirrors the 737 executor): PMDG polls the control
/// CDA once per frame, so two writes in the same frame COALESCE — only the last
/// survives. Every dispatch goes through a serializing gate that frame-spaces each
/// write from the previous one. Before this, every Multi() flow step (bus ties,
/// engine anti-ice, demand pumps, fuel pumps, …) and every multi-write convenience
/// method fired its events in ONE frame, silently applying only the LAST switch.
/// </summary>
public class AircraftActionExecutor : IFoActionExecutor
{
    private SimConnectManager? _simConnect;

    // MOUSE_FLAG_LEFTSINGLE — required for FD/AT Arm switches and the per-detent
    // flap-lever click events (see CLAUDE.md).
    private const int MouseFlagLeftSingle = 0x20000000;

    // Held FIRE/OVHT TEST button: transmit press/release with the hold between. The 777
    // CDA doctrine has NO release semantics (param 0 also registers as a press — see
    // docs/pmdg-777.md), so the held test uses the '#id' transmit path like the VC click.
    private const uint MouseFlagLeftSingleU  = 0x20000000u;
    private const uint MouseFlagLeftReleaseU = 0x00020000u;
    public const int FireOvhtTestHoldMs = 3000;   // Fenix-parity hold

    /// <summary>System-test timings (live-verified on the 777 2026-07-13, user-tunable).
    /// The 777 TCAS TEST commits ONLY via the transmit press/release — the def's usual CDA
    /// param-1 momentary was SILENT for it (same finding family as EVT_OH_FIRE_OVHT_TEST).</summary>
    public const int QuickTestHoldMs = 150;
    public const int WxrOverlaySettleMs = 4000;
    public const int WxrTestWaitMs = 20000;

    public void SetSimConnect(SimConnectManager? sc) => _simConnect = sc;

    public bool IsAvailable => _simConnect is { IsConnected: true };

    // -----------------------------------------------------------------------
    // Core dispatch — serialized + frame-paced (see class doc)
    // -----------------------------------------------------------------------

    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private DateTime _lastWriteUtc = DateTime.MinValue;

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(CdaWriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    /// <summary>
    /// IFoActionExecutor — acquire+release the dispatch gate. SemaphoreSlim async
    /// waiters queue FIFO, so by the time this acquires, every dispatch queued before
    /// the call has fully completed (ChecklistManager's post-tick grace re-stamp).
    /// </summary>
    public async Task WaitForDispatchDrainAsync()
    {
        await _dispatchGate.WaitAsync();
        _dispatchGate.Release();
    }

    /// <summary>Interface implementation: dispatches an <see cref="IFlowStepDispatch"/>,
    /// awaited by the flow engine so steps stay ordered and paced.</summary>
    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return Task.FromResult(false);
        // Pseudo-keys: held self-completing system tests (Fenix FIRE_TEST_* precedent).
        if (step.ActionType == Models.FlowStepActionType.SetSwitch)
        {
            switch (step.EventName)
            {
                case "FIRE_OVHT_TEST": return FireOvhtTestAsync();
                case "TCAS_TEST":      return TcasTestAsync();
                case "WXR_TEST":       return WxrTestAsync();
            }
        }
        return step.ActionType switch
        {
            Models.FlowStepActionType.SetSwitch =>
                DispatchAsync(step.EventName!, step.TargetValue, step.UsesMouseFlag, step.IsMomentary),
            Models.FlowStepActionType.SetSwitchMultiple => MultiAsync(step.MultiActions),
            _ => Task.FromResult(false),
        };
    }

    private async Task<bool> MultiAsync(IReadOnlyList<(string EventName, int? TargetValue)> actions)
    {
        // Hold the gate across the whole sequence so a concurrent dispatch can't slip a
        // write into one of our frame gaps; inter-write spacing comes from PaceAsync.
        await _dispatchGate.WaitAsync();
        try
        {
            bool ok = true;
            foreach (var (ev, tv) in actions)
                ok &= await DispatchCoreAsync(ev, tv, false, false);
            return ok;
        }
        finally { _dispatchGate.Release(); }
    }

    private async Task<bool> DispatchAsync(string eventName, int? target, bool usesMouseFlag, bool isMomentary)
    {
        await _dispatchGate.WaitAsync();
        try { return await DispatchCoreAsync(eventName, target, usesMouseFlag, isMomentary); }
        finally { _dispatchGate.Release(); }
    }

    private async Task<bool> DispatchCoreAsync(string eventName, int? targetValue, bool usesMouseFlag, bool isMomentary)
    {
        if (_simConnect == null || !PMDG777Definition.EventIds.TryGetValue(eventName, out int evId)) return false;
        uint eventId = (uint)evId;

        // Per-detent flap-lever AND speed-brake-lever events are SDK mouse-click events
        // that ONLY commit with MOUSE_FLAG_LEFTSINGLE (the panel's proven FCTL_Flaps
        // convention; the speed-brake detents are the same sub-detent click family —
        // live-verified on the 737 2026-07-03, where CDA+LEFTSINGLE on _ARM lit the
        // ARMED annunciator while the bare param was a silent no-op). Force the flag
        // here so EVERY caller shape is corrected — the flows' flap steps and the
        // "Speedbrake: ARMED"/"DOWN" steps were built as IsMomentary (param 1) and
        // silently did nothing. (The trailing underscore keeps the bare
        // EVT_CONTROL_STAND_SPEED_BRAKE_LEVER drag-axis event out of this.)
        if (eventName.StartsWith("EVT_CONTROL_STAND_FLAPS_LEVER_", StringComparison.Ordinal) ||
            eventName.StartsWith("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_", StringComparison.Ordinal))
        {
            usesMouseFlag = true;
            isMomentary = false;
        }

        await PaceAsync();
        try
        {
            if (usesMouseFlag)
                _simConnect.SendPMDGEvent(eventName, eventId, MouseFlagLeftSingle);
            else if (isMomentary)
                _simConnect.SendPMDGEvent(eventName, eventId, 1);
            else if (targetValue.HasValue)
                _simConnect.SendPMDGEvent(eventName, eventId, targetValue.Value);
            else
                _simConnect.SendPMDGEvent(eventName, eventId);
            return true;
        }
        finally { _lastWriteUtc = DateTime.UtcNow; }
    }

    // Synchronous entry used by the ~60 convenience methods below (checklist
    // CheckAction lambdas are synchronous). Queues a gated, paced write and returns:
    // sequential calls queue in order (SemaphoreSlim async waiters are FIFO), so a
    // lambda firing several switches gets them written one frame apart.
    private bool ExecuteSingle(string eventName, int? targetValue, bool usesMouseFlag, bool isMomentary)
    {
        if (!PMDG777Definition.EventIds.ContainsKey(eventName)) return false;
        _ = DispatchAsync(eventName, targetValue, usesMouseFlag, isMomentary);
        return true;
    }

    // -----------------------------------------------------------------------
    // Named convenience actions (used by flow definitions)
    // -----------------------------------------------------------------------

    // Battery
    public bool SetBattery(int position)
        => ExecuteSingle("EVT_OH_ELEC_BATTERY_SWITCH", position, false, false);

    // APU selector: 0=OFF, 1=ON, 2=START
    public bool SetApuSelector(int position)
        => ExecuteSingle("EVT_OH_ELEC_APU_SEL_SWITCH", position, false, false);

    // Ground power (momentary)
    public bool PushGroundPowerPrimary()
        => ExecuteSingle("EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH", null, false, true);
    public bool PushGroundPowerSecondary()
        => ExecuteSingle("EVT_OH_ELEC_GRD_PWR_SEC_SWITCH", null, false, true);

    // Minimum gap between CDA writes (same-frame writes coalesce — see class doc).
    private const int CdaWriteSpacingMs = 350;
    // APU selector ON → START needs the longer gap the flow uses (BS_APU_ON_WAIT = 2 s)
    // so the model registers ON before START.
    private const int ApuOnToStartMs = 2000;

    /// <summary>Push BOTH ground-power buttons — the paced gate frame-spaces them.</summary>
    public async Task PushBothGroundPowerAsync()
    {
        PushGroundPowerPrimary();
        await Task.Delay(CdaWriteSpacingMs);
        PushGroundPowerSecondary();
    }

    /// <summary>Held transmit press/release for buttons with no CDA release semantics
    /// (param 0 also registers as a press on the 777 — docs/pmdg-777.md).</summary>
    public async Task<bool> HeldTransmitTestAsync(string eventName, int holdMs)
    {
        var sc = _simConnect;
        if (sc == null || !PMDG777Definition.EventIds.TryGetValue(eventName, out int evId))
            return false;
        await _dispatchGate.WaitAsync();
        try
        {
            await PaceAsync();
            sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftSingleU);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(holdMs);
            sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftReleaseU);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _dispatchGate.Release(); }
    }

    /// <summary>FIRE/OVHT TEST button, HELD: transmit LEFTSINGLE, hold, LEFTRELEASE —
    /// fire bell + fire warning lights while held, self-releasing. NOT the panel's
    /// momentary CDA param-1 press (that is a bare blip; the test wants a real hold,
    /// and CDA has no release). In-sim verification: 737 test plan Part U (the 777
    /// shares that plan); fallback if transmit proves inert on this button is the
    /// CDA param-1 press.</summary>
    public Task<bool> FireOvhtTestAsync() =>
        HeldTransmitTestAsync("EVT_OH_FIRE_OVHT_TEST", FireOvhtTestHoldMs);

    /// <summary>TCAS self-test — quick press; "TCAS TEST" → ~8 s → "TCAS TEST PASS".</summary>
    public Task<bool> TcasTestAsync() => HeldTransmitTestAsync("EVT_TCAS_TEST", QuickTestHoldMs);

    /// <summary>WXR/PWS test — same managed sequence as the 737 (see that executor's doc):
    /// EFIS WXR overlay on (blind toggle, assumed OFF) → settle → TEST → callout wait →
    /// WX mode (never leave TEST latched) → overlay off. Live-verified on the 777 2026-07-13.</summary>
    public async Task<bool> WxrTestAsync()
    {
        // Overlay on first; if that fails (no SimConnect) nothing was touched — bail
        // before the try, no restore owed.
        if (!await HeldTransmitTestAsync("EVT_EFIS_CPT_WXR", QuickTestHoldMs)) return false;
        try
        {
            await Task.Delay(WxrOverlaySettleMs);
            return await HeldTransmitTestAsync("EVT_WXR_TEST", QuickTestHoldMs)
                && await DelayThenTrueAsync(WxrTestWaitMs);
        }
        finally
        {
            // Restore UNCONDITIONALLY once the overlay is on — never leave the radar
            // latched in TEST or the overlay stuck on, even if the TEST press failed.
            await HeldTransmitTestAsync("EVT_WXR_L_WX", QuickTestHoldMs);
            await HeldTransmitTestAsync("EVT_EFIS_CPT_WXR", QuickTestHoldMs);
        }
    }

    private static async Task<bool> DelayThenTrueAsync(int ms)
    {
        await Task.Delay(ms);
        return true;
    }

    /// <summary>APU start sequence: selector ON, wait, then START (springs back to ON).</summary>
    public async Task StartApuAsync()
    {
        SetApuSelector(1);                  // ON
        await Task.Delay(ApuOnToStartMs);
        SetApuSelector(2);                  // START
    }

    // APU Generator
    public bool SetApuGenerator(int position)
        => ExecuteSingle("EVT_OH_ELEC_APU_GEN_SWITCH", position, false, false);

    // Bus Ties
    public bool SetBusTies(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ELEC_BUS_TIE1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ELEC_BUS_TIE2_SWITCH", position, false, false);
        return ok;
    }

    // Generators
    public bool SetGenerators(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ELEC_GEN1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ELEC_GEN2_SWITCH", position, false, false);
        return ok;
    }

    // Backup Generators
    public bool SetBackupGenerators(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ELEC_BACKUP_GEN1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ELEC_BACKUP_GEN2_SWITCH", position, false, false);
        return ok;
    }

    // IFE / Cabin
    public bool SetIFEPassSeats(int position) => ExecuteSingle("EVT_OH_ELEC_IFE", position, false, false);
    public bool SetCabinUtility(int position)  => ExecuteSingle("EVT_OH_ELEC_CAB_UTIL", position, false, false);

    // Hydraulic engine pumps
    public bool SetEngPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_HYD_ENG1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_ENG2", position, false, false);
        return ok;
    }

    // Hydraulic electric pumps
    public bool SetElecPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_HYD_ELEC1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_ELEC2", position, false, false);
        return ok;
    }

    // Hydraulic demand pumps (electric + air)
    public bool SetDemandPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_HYD_DEMAND_ELEC1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_DEMAND_ELEC2", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_AIR1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_HYD_AIR2", position, false, false);
        return ok;
    }

    // Fuel pumps (all wing pumps)
    public bool SetWingFuelPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_PUMP_1_FORWARD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_2_FORWARD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_1_AFT", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_2_AFT", position, false, false);
        return ok;
    }

    // Center fuel pumps
    public bool SetCenterFuelPumps(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_PUMP_L_CENTER", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_PUMP_R_CENTER", position, false, false);
        return ok;
    }

    // Fuel control levers (PMDG inverted: 1=Cutoff, 0=Run in the event param)
    // Here we take logical values: 0=Cutoff, 1=Run
    public bool SetFuelControlLevers(int logicalPosition)
    {
        int pmdgParam = logicalPosition == 1 ? 0 : 1; // invert
        bool ok = ExecuteSingle("EVT_CONTROL_STAND_ENG1_START_LEVER", pmdgParam, false, false);
        ok &= ExecuteSingle("EVT_CONTROL_STAND_ENG2_START_LEVER", pmdgParam, false, false);
        return ok;
    }

    public bool SetFuelControl1(int logicalPosition)
    {
        int pmdgParam = logicalPosition == 1 ? 0 : 1;
        return ExecuteSingle("EVT_CONTROL_STAND_ENG1_START_LEVER", pmdgParam, false, false);
    }

    public bool SetFuelControl2(int logicalPosition)
    {
        int pmdgParam = logicalPosition == 1 ? 0 : 1;
        return ExecuteSingle("EVT_CONTROL_STAND_ENG2_START_LEVER", pmdgParam, false, false);
    }

    // Engine start selectors
    public bool SetEngStartSelector1(int position) => ExecuteSingle("EVT_OH_ENGINE_L_START", position, false, false);
    public bool SetEngStartSelector2(int position) => ExecuteSingle("EVT_OH_ENGINE_R_START", position, false, false);

    // EEC Mode
    public bool SetEECMode(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_EEC_L_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_EEC_R_SWITCH", position, false, false);
        return ok;
    }

    // Autostart
    public bool SetAutoStart(int position) => ExecuteSingle("EVT_OH_ENGINE_AUTOSTART", position, false, false);

    // Anti-ice
    public bool SetWindowHeat(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_2", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_3", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_WINDOW_HEAT_4", position, false, false);
        return ok;
    }

    public bool SetWingAntiIce(int position) => ExecuteSingle("EVT_OH_ICE_WING_ANTIICE", position, false, false);
    public bool SetEngAntiIce(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_ICE_ENGINE_ANTIICE_1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_ICE_ENGINE_ANTIICE_2", position, false, false);
        return ok;
    }

    // Packs
    public bool SetPacks(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_AIRCOND_PACK_SWITCH_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_AIRCOND_PACK_SWITCH_R", position, false, false);
        return ok;
    }

    // Bleed
    public bool SetEngBleeds(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_BLEED_ENG_1_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_BLEED_ENG_2_SWITCH", position, false, false);
        return ok;
    }
    public bool SetApuBleed(int position) => ExecuteSingle("EVT_OH_BLEED_APU_SWITCH", position, false, false);
    public bool SetBleedIsolationValves(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_C", position, false, false);
        ok &= ExecuteSingle("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_R", position, false, false);
        return ok;
    }

    // Pressurization
    public bool SetOutflowValves(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_PRESS_VALVE_SWITCH_1", position, false, false);
        ok &= ExecuteSingle("EVT_OH_PRESS_VALVE_SWITCH_2", position, false, false);
        return ok;
    }

    // Trim air
    public bool SetTrimAir(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R", position, false, false);
        return ok;
    }

    // Equip cooling / Gasper / Recirc fans
    public bool SetEquipCooling(int position) => ExecuteSingle("EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH", position, false, false);
    public bool SetGasper(int position)       => ExecuteSingle("EVT_OH_AIRCOND_GASPER_SWITCH", position, false, false);
    public bool SetRecircFans(int position)   => ExecuteSingle("EVT_OH_AIRCOND_RECIRC_FANS_SWITCH", position, false, false);

    // Lights
    public bool SetBeacon(int position)         => ExecuteSingle("EVT_OH_LIGHTS_BEACON", position, false, false);
    public bool SetNavLights(int position)      => ExecuteSingle("EVT_OH_LIGHTS_NAV", position, false, false);
    public bool SetStrobeLights(int position)   => ExecuteSingle("EVT_OH_LIGHTS_STROBE", position, false, false);
    public bool SetLogoLights(int position)     => ExecuteSingle("EVT_OH_LIGHTS_LOGO", position, false, false);
    public bool SetWingLights(int position)     => ExecuteSingle("EVT_OH_LIGHTS_WING", position, false, false);
    public bool SetStormLights(int position)    => ExecuteSingle("EVT_OH_LIGHTS_STORM", position, false, false);
    public bool SetTaxiLights(int position)     => ExecuteSingle("EVT_OH_LIGHTS_TAXI", position, false, false);
    // All THREE landing lights via the individual per-switch events the panel's proven
    // controls use (EVT_OH_LIGHTS_LANDING_L/NOSE/R) — the ganged LANDING_LNR event was
    // never live-verified. Paced by the dispatch gate.
    public bool SetLandingLights(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_LIGHTS_LANDING_L", position, false, false);
        ok &= ExecuteSingle("EVT_OH_LIGHTS_LANDING_NOSE", position, false, false);
        ok &= ExecuteSingle("EVT_OH_LIGHTS_LANDING_R", position, false, false);
        return ok;
    }
    public bool SetRunwayTurnoff(int position)  => ExecuteSingle("EVT_OH_LIGHTS_LR_TURNOFF", position, false, false);
    public bool SetMasterLights(int position)   => ExecuteSingle("EVT_OH_LIGHTS_IND_LTS_SWITCH", position, false, false);

    // Signs
    public bool SetSeatBelts(int position)      => ExecuteSingle("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", position, false, false);

    /// <summary>Seat-belt sign: ON = position 2, OFF = position 0 (0=OFF,1=AUTO,2=ON).</summary>
    public bool SetSeatbeltSign(bool on) => SetSeatBelts(on ? 2 : 0);
    public bool SetNoSmoking(int position)      => ExecuteSingle("EVT_OH_NO_SMOKING_LIGHT_SWITCH", position, false, false);

    // Emer exit lights (guarded switch — guard must be closed/open first)
    // Guard: 0=open, 1=closed. Switch: 0=Off, 1=Armed
    public bool CloseEmerExitLightGuard()       => ExecuteSingle("EVT_OH_EMER_EXIT_LIGHT_GUARD", 1, false, false);
    public bool SetEmerExitLights(int position)  => ExecuteSingle("EVT_OH_EMER_EXIT_LIGHT_SWITCH", position, false, false);

    // ADIRU
    public bool SetAdiru(int position) => ExecuteSingle("EVT_OH_ADIRU_SWITCH", position, false, false);

    // Thrust Asym Comp
    public bool SetThrustAsymComp(int position) => ExecuteSingle("EVT_OH_THRUST_ASYM_COMP", position, false, false);

    // CVR test (momentary)
    public bool PushCvrTest() => ExecuteSingle("EVT_OH_CVR_TEST", null, false, true);

    // Cargo fire arm (both)
    public bool SetCargoFireArm(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FIRE_CARGO_ARM_FWD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FIRE_CARGO_ARM_AFT", position, false, false);
        return ok;
    }

    // MCP — Flight Directors (require mouse flag)
    public bool SetFDLeft(int targetOn, AircraftStateEvaluator state)
    {
        int current = state.IsFDLeftOn() ? 1 : 0;
        if (current == targetOn) return true;
        return ExecuteSingle("EVT_MCP_FD_SWITCH_L", null, true, false);
    }
    public bool SetFDRight(int targetOn, AircraftStateEvaluator state)
    {
        int current = state.IsFDRightOn() ? 1 : 0;
        if (current == targetOn) return true;
        return ExecuteSingle("EVT_MCP_FD_SWITCH_R", null, true, false);
    }

    // MCP — AT Arm (require mouse flag)
    public bool SetATArmLeft(int targetOn, AircraftStateEvaluator state)
    {
        int current = state.IsATArmLeftOn() ? 1 : 0;
        if (current == targetOn) return true;
        return ExecuteSingle("EVT_MCP_AT_ARM_SWITCH_L", null, true, false);
    }
    public bool SetATArmRight(int targetOn, AircraftStateEvaluator state)
    {
        int current = state.IsATArmRightOn() ? 1 : 0;
        if (current == targetOn) return true;
        return ExecuteSingle("EVT_MCP_AT_ARM_SWITCH_R", null, true, false);
    }

    // MCP — LNAV/VNAV (momentary push)
    public bool PushLNAV() => ExecuteSingle("EVT_MCP_LNAV_SWITCH", null, false, true);
    public bool PushVNAV() => ExecuteSingle("EVT_MCP_VNAV_SWITCH", null, false, true);

    // MCP — Autopilot CMD (left seat — momentary push)
    public bool PushAPCmd() => ExecuteSingle("EVT_MCP_AP_L_SWITCH", null, false, true);

    // EFIS mode / range
    public bool SetEFISModeCapt(int position) => ExecuteSingle("EVT_EFIS_CPT_MODE", position, false, false);
    public bool SetEFISModeFO(int position)   => ExecuteSingle("EVT_EFIS_FO_MODE", position, false, false);
    public bool SetEFISRangeCapt(int position) => ExecuteSingle("EVT_EFIS_CPT_RANGE", position, false, false);
    public bool SetEFISRangeFO(int position)  => ExecuteSingle("EVT_EFIS_FO_RANGE", position, false, false);

    // EFIS Baro STD toggle (momentary push-button, toggles between STD and QNH)
    // Check IsBaroSTDCapt()/IsBaroSTDFO() before calling to avoid toggling the wrong way.
    public bool PushBaroSTDCapt() => ExecuteSingle("EVT_EFIS_CPT_BARO_STD", null, false, true);
    public bool PushBaroSTDFO()   => ExecuteSingle("EVT_EFIS_FO_BARO_STD",  null, false, true);

    // Autobrake
    // 0=RTO, 1=Off, 2=1, 3=2, 4=3, 5=4, 6=Auto
    public bool SetAutobrake(int position) => ExecuteSingle("EVT_ABS_AUTOBRAKE_SELECTOR", position, false, false);

    // Gear lever
    // 0=Up, 1=Down
    public bool SetGearLever(int position) => ExecuteSingle("EVT_GEAR_LEVER", position, false, false);

    // Flap lever (use position-specific events). The per-detent
    // EVT_CONTROL_STAND_FLAPS_LEVER_<deg> events are SDK mouse-click events that
    // expect MOUSE_FLAG_LEFTSINGLE as the CDA parameter — the same convention the
    // panel's proven FCTL_Flaps dispatch uses (PMDG777Definition.HandleUIVariableSet
    // 2f: "they expect MOUSE_FLAG_LEFTSINGLE ... matching the SDK's mouse-click
    // convention"). The old isMomentary param=1 shape was silently ignored by the
    // SDK, which is why FO auto-flaps never moved the lever.
    public bool SetFlapsPosition(int position)
    {
        string? eventName = position switch
        {
            0 => "EVT_CONTROL_STAND_FLAPS_LEVER_0",
            1 => "EVT_CONTROL_STAND_FLAPS_LEVER_1",
            2 => "EVT_CONTROL_STAND_FLAPS_LEVER_5",
            3 => "EVT_CONTROL_STAND_FLAPS_LEVER_15",
            4 => "EVT_CONTROL_STAND_FLAPS_LEVER_20",
            5 => "EVT_CONTROL_STAND_FLAPS_LEVER_25",
            6 => "EVT_CONTROL_STAND_FLAPS_LEVER_30",
            _ => null
        };
        if (eventName == null) return false;
        return ExecuteSingle(eventName, null, usesMouseFlag: true, isMomentary: false);
    }

    // Speedbrake lever
    public bool SetSpeedbrakeDown()  => ExecuteSingle("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", null, false, true);
    public bool SetSpeedbrakeArmed() => ExecuteSingle("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null, false, true);

    // Parking brake
    public bool SetParkingBrake(int position) => ExecuteSingle("EVT_CONTROL_STAND_PARK_BRAKE_LEVER", position, false, false);

    // Transponder mode via the PMDG TCAS-mode event (NOT the stock XPNDR_SET, which
    // sets the squawk CODE — mode 2 there wrote squawk 0002). Values:
    // 0=STBY, 1=ALT-OFF, 2=XPNDR, 3=TA, 4=TA/RA
    public bool SetTransponderMode(int mode)
        => ExecuteSingle("EVT_TCAS_MODE", mode, false, false);

    // Wipers
    public bool SetWipers(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_WIPER_LEFT_SWITCH", position, false, false);
        ok &= ExecuteSingle("EVT_OH_WIPER_RIGHT_SWITCH", position, false, false);
        return ok;
    }

    // Alternate flaps
    public bool SetAltFlaps(int position)
        => ExecuteSingle("EVT_ALTN_FLAPS_POS", position, false, false);

    // Display select panel — only the buttons BA Assist can reliably control
    // CANC/RCL (useful for flow completion check)
    public bool PushCancelRecall() => ExecuteSingle("EVT_DSP_CANC_RCL_SWITCH", null, false, true);

    // Fuel jettison — nozzles + arm OFF (mirrors the Cockpit Prep flow's CP_JETT
    // steps; the paced gate frame-spaces the three writes).
    public bool SetFuelJettisonOff()
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_JETTISON_NOZZLE_L", 0, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_JETTISON_NOZZLE_R", 0, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_JETTISON_ARM", 0, false, false);
        return ok;
    }

    // Fuel crossfeed valves (forward + aft)
    public bool SetCrossfeeds(int position)
    {
        bool ok = ExecuteSingle("EVT_OH_FUEL_CROSSFEED_FORWARD", position, false, false);
        ok &= ExecuteSingle("EVT_OH_FUEL_CROSSFEED_AFT", position, false, false);
        return ok;
    }

    /// <summary>Map SimBrief takeoff-flap DEGREES (1/5/15/20/25) to the 777 lever
    /// position index SetFlapsPosition takes (0=UP,1=1,2=5,3=15,4=20,5=25,6=30).</summary>
    public static int FlapDegreesToPosition(int degrees) => degrees switch
    {
        1 => 1, 5 => 2, 15 => 3, 20 => 4, 25 => 5, 30 => 6,
        _ => 2, // default: flaps 5 — the common 777 takeoff setting
    };
}
