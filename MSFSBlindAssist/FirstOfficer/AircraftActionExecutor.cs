using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Sends PMDG 777 switch events on behalf of the flow engine.
/// Uses the same PMDG event IDs and parameter rules as the rest of BA Assist.
/// </summary>
public class AircraftActionExecutor : IFoActionExecutor
{
    private SimConnectManager? _simConnect;

    // MOUSE_FLAG_LEFTSINGLE — required for FD and AT Arm switches (see CLAUDE.md)
    private const int MouseFlagLeftSingle = 0x20000000;

    public void SetSimConnect(SimConnectManager? sc) => _simConnect = sc;

    public bool IsAvailable => _simConnect is { IsConnected: true };

    // -----------------------------------------------------------------------
    // Core dispatch
    // -----------------------------------------------------------------------

    /// <summary>
    /// Interface implementation: dispatches an <see cref="IFlowStepDispatch"/> asynchronously.
    /// Returns a completed Task because all 777 events are synchronous fire-and-forget.
    /// </summary>
    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        bool ok = step.ActionType switch
        {
            Models.FlowStepActionType.SetSwitch =>
                ExecuteSingle(step.EventName!, step.TargetValue, step.UsesMouseFlag, step.IsMomentary),
            Models.FlowStepActionType.SetSwitchMultiple =>
                step.MultiActions.Aggregate(true, (acc, m) => acc & ExecuteSingle(m.EventName, m.TargetValue, false, false)),
            _ => false
        };
        return Task.FromResult(ok);
    }

    private bool ExecuteSingle(string eventName, int? targetValue, bool usesMouseFlag, bool isMomentary)
    {
        if (!PMDG777Definition.EventIds.TryGetValue(eventName, out int evId)) return false;
        uint eventId = (uint)evId;

        if (usesMouseFlag)
        {
            _simConnect!.SendPMDGEvent(eventName, eventId, MouseFlagLeftSingle);
        }
        else if (isMomentary)
        {
            _simConnect!.SendPMDGEvent(eventName, eventId, 1);
        }
        else if (targetValue.HasValue)
        {
            _simConnect!.SendPMDGEvent(eventName, eventId, targetValue.Value);
        }
        else
        {
            _simConnect!.SendPMDGEvent(eventName, eventId);
        }
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
    public bool SetLandingLights(int position)  => ExecuteSingle("EVT_OH_LIGHTS_LANDING_LNR", position, false, false);
    public bool SetRunwayTurnoff(int position)  => ExecuteSingle("EVT_OH_LIGHTS_LR_TURNOFF", position, false, false);
    public bool SetMasterLights(int position)   => ExecuteSingle("EVT_OH_LIGHTS_IND_LTS_SWITCH", position, false, false);

    // Signs
    public bool SetSeatBelts(int position)      => ExecuteSingle("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", position, false, false);
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

    // Flap lever (use position-specific events)
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
        return ExecuteSingle(eventName, null, false, true);
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
}
