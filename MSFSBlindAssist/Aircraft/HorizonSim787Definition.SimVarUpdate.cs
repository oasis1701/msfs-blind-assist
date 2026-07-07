using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class HorizonSim787Definition
{
    // =========================================================================
    // ProcessSimVarUpdate — suppress raw value announcements where needed
    // =========================================================================

    public override bool ProcessSimVarUpdate(string variableKey, double value,
        ScreenReaderAnnouncer announcer)
    {
        if (base.ProcessSimVarUpdate(variableKey, value, announcer))
            return true;

        // =====================================================================
        // BridgeVersion 18+ — new annunciators / status. All use the same pattern:
        // track previous value (-1 = unset → suppresses first-poll), announce only
        // on transitions, optionally distinguish on→off vs off→on phrasing.
        // =====================================================================

        if (variableKey == "HS787_MasterCaution")
        {
            int now = (int)value;
            if (_previousMasterCaution >= 0 && now != _previousMasterCaution)
                announcer.Announce(now == 1 ? "Master Caution" : "Master Caution clear");
            _previousMasterCaution = now;
            return true;
        }

        if (variableKey == "HS787_MasterWarning")
        {
            int now = (int)value;
            if (_previousMasterWarning >= 0 && now != _previousMasterWarning)
                announcer.Announce(now == 1 ? "Master Warning" : "Master Warning clear");
            _previousMasterWarning = now;
            return true;
        }

        if (variableKey == "HS787_StallWarning")
        {
            int now = (int)value;
            if (_previousStallWarning >= 0 && now != _previousStallWarning && now == 1)
                announcer.Announce("STALL");
            _previousStallWarning = now;
            return true;
        }

        if (variableKey == "HS787_IrsOnBat")
        {
            int now = (int)value;
            if (_previousIrsOnBat >= 0 && now != _previousIrsOnBat)
                announcer.Announce(now == 1 ? "IRS on battery" : "IRS on aircraft power");
            _previousIrsOnBat = now;
            return true;
        }

        if (variableKey == "HS787_LightMaster")
        {
            int now = (int)value;
            if (_previousLightMaster >= 0 && now != _previousLightMaster)
                announcer.Announce(now == 1 ? "Light Master on" : "Light Master off");
            _previousLightMaster = now;
            return true;
        }

        if (variableKey == "HS787_EmerLightsCover")
        {
            int now = (int)value;
            if (_previousEmerLightsCover >= 0 && now != _previousEmerLightsCover)
                announcer.Announce(now == 1 ? "Emergency Lights cover open" : "Emergency Lights cover closed");
            _previousEmerLightsCover = now;
            return true;
        }

        if (variableKey == "HS787_EfbPower")
        {
            int now = (int)value;
            if (_previousEfbPower >= 0 && now != _previousEfbPower)
                announcer.Announce(now == 1 ? "EFB on" : "EFB off");
            _previousEfbPower = now;
            return true;
        }

        if (variableKey == "HS787_FlightComputerAuto")
        {
            int now = (int)value;
            if (_previousFlightComputerAuto >= 0 && now != _previousFlightComputerAuto)
                announcer.Announce(now == 1 ? "Flight Computer Auto" : "Flight Computer Manual");
            _previousFlightComputerAuto = now;
            return true;
        }

        if (variableKey == "HS787_PacksAuto")
        {
            int now = (int)value;
            if (_previousPacksAuto >= 0 && now != _previousPacksAuto)
                announcer.Announce(now == 1 ? "Packs on" : "Packs off");
            _previousPacksAuto = now;
            return true;
        }

        if (variableKey == "HS787_FdDoorPower")
        {
            int now = (int)value;
            if (_previousFdDoorPower >= 0 && now != _previousFdDoorPower)
                announcer.Announce(now == 1 ? "Flight Deck Door power on" : "Flight Deck Door power off");
            _previousFdDoorPower = now;
            return true;
        }

        if (variableKey == "HS787_CoolingAft")
        {
            int now = (int)value;
            if (_previousCoolingAft >= 0 && now != _previousCoolingAft)
                announcer.Announce(now == 1 ? "Aft Equipment Cooling auto" : "Aft Equipment Cooling off");
            _previousCoolingAft = now;
            return true;
        }

        if (variableKey == "HS787_EquipFwd")
        {
            int now = (int)value;
            if (_previousEquipFwd >= 0 && now != _previousEquipFwd)
                announcer.Announce(now == 1 ? "Forward Equipment Cooling auto" : "Forward Equipment Cooling off");
            _previousEquipFwd = now;
            return true;
        }

        if (variableKey == "HS787_PressManAltOn")
        {
            int now = (int)value;
            if (_previousPressManAltOn >= 0 && now != _previousPressManAltOn)
                announcer.Announce(now == 1 ? "Pressurization manual" : "Pressurization auto");
            _previousPressManAltOn = now;
            return true;
        }

        // ----- BridgeVersion 19+ -----

        if (variableKey == "HS787_AcBusEnergized")
        {
            int now = (int)value;
            if (_previousAcBusEnergized >= 0 && now != _previousAcBusEnergized)
                announcer.Announce(now == 1 ? "AC Bus energized" : "AC Bus de-energized");
            _previousAcBusEnergized = now;
            return true;
        }

        if (variableKey == "HS787_AutoBacklight")
        {
            int now = (int)value;
            if (_previousAutoBacklight >= 0 && now != _previousAutoBacklight)
                announcer.Announce(now == 1 ? "Auto Backlight on" : "Auto Backlight off");
            _previousAutoBacklight = now;
            return true;
        }

        if (variableKey == "HS787_NextGenFP")
        {
            int now = (int)value;
            if (_previousNextGenFp >= 0 && now != _previousNextGenFp)
                announcer.Announce(now == 1 ? "NextGen Flight Plan on" : "NextGen Flight Plan off");
            _previousNextGenFp = now;
            return true;
        }

        if (variableKey == "HS787_HudDown1")
        {
            int now = (int)value;
            if (_previousHudDown1 >= 0 && now != _previousHudDown1)
                announcer.Announce(now == 1 ? "Captain HUD deployed" : "Captain HUD stowed");
            _previousHudDown1 = now;
            return true;
        }

        if (variableKey == "HS787_HudDown2")
        {
            int now = (int)value;
            if (_previousHudDown2 >= 0 && now != _previousHudDown2)
                announcer.Announce(now == 1 ? "First Officer HUD deployed" : "First Officer HUD stowed");
            _previousHudDown2 = now;
            return true;
        }

        if (variableKey == "HS787_HudAutoBrt1")
        {
            int now = (int)value;
            if (_previousHudAutoBrt1 >= 0 && now != _previousHudAutoBrt1)
                announcer.Announce(now == 1 ? "Captain HUD brightness auto" : "Captain HUD brightness manual");
            _previousHudAutoBrt1 = now;
            return true;
        }

        if (variableKey == "HS787_HudAutoBrt2")
        {
            int now = (int)value;
            if (_previousHudAutoBrt2 >= 0 && now != _previousHudAutoBrt2)
                announcer.Announce(now == 1 ? "First Officer HUD brightness auto" : "First Officer HUD brightness manual");
            _previousHudAutoBrt2 = now;
            return true;
        }

        if (variableKey == "HS787_AirDataSrc1")
        {
            int now = (int)value;
            if (_previousAirDataSrc1 >= 0 && now != _previousAirDataSrc1)
                announcer.Announce(now == 1 ? "Captain Air Data Source Alternate" : "Captain Air Data Source Normal");
            _previousAirDataSrc1 = now;
            return true;
        }

        if (variableKey == "HS787_AirDataSrc2")
        {
            int now = (int)value;
            if (_previousAirDataSrc2 >= 0 && now != _previousAirDataSrc2)
                announcer.Announce(now == 1 ? "First Officer Air Data Source Alternate" : "First Officer Air Data Source Normal");
            _previousAirDataSrc2 = now;
            return true;
        }

        // ----- Batch 5 — yaw damper, antiskid, avionics master, pitot heat, interior lights -----

        if (variableKey == "HS787_YawDamper")
        {
            int now = (int)value;
            if (_previousYawDamper >= 0 && now != _previousYawDamper)
                announcer.Announce(now == 1 ? "Yaw Damper on" : "Yaw Damper off");
            _previousYawDamper = now;
            return true;
        }

        if (variableKey == "HS787_AntiSkid")
        {
            int now = (int)value;
            if (_previousAntiSkid >= 0 && now != _previousAntiSkid)
                announcer.Announce(now == 1 ? "Antiskid armed" : "Antiskid off");
            _previousAntiSkid = now;
            return true;
        }

        if (variableKey == "HS787_AvionicsMaster")
        {
            int now = (int)value;
            if (_previousAvionicsMaster >= 0 && now != _previousAvionicsMaster)
                announcer.Announce(now == 1 ? "Avionics Master on" : "Avionics Master off");
            _previousAvionicsMaster = now;
            return true;
        }

        if (variableKey == "HS787_PitotHeat")
        {
            int now = (int)value;
            if (_previousPitotHeat >= 0 && now != _previousPitotHeat)
                announcer.Announce(now == 1 ? "Pitot Heat on" : "Pitot Heat off");
            _previousPitotHeat = now;
            return true;
        }

        // Interior light bus announce handlers removed alongside the var defs.

        // ----- Batch 6 — COM frequencies + transponder squawk (PMDG-style) -----
        // First sample (baseline 0) is silent; only real changes are spoken.

        if (variableKey == "COM1_ActiveFreq")
        {
            if (_lastComActive1 > 0 && System.Math.Abs(value - _lastComActive1) > 0.001)
                announcer.Announce($"COM1 active {value:F3}");
            _lastComActive1 = value;
            return true;
        }
        if (variableKey == "COM_STANDBY_FREQUENCY_SET:1")
        {
            if (_lastComStandby1 > 0 && System.Math.Abs(value - _lastComStandby1) > 0.001)
                announcer.Announce($"COM1 standby {value:F3}");
            _lastComStandby1 = value;
            return true;
        }
        if (variableKey == "COM2_ActiveFreq")
        {
            if (_lastComActive2 > 0 && System.Math.Abs(value - _lastComActive2) > 0.001)
                announcer.Announce($"COM2 active {value:F3}");
            _lastComActive2 = value;
            return true;
        }
        if (variableKey == "COM_STANDBY_FREQUENCY_SET:2")
        {
            if (_lastComStandby2 > 0 && System.Math.Abs(value - _lastComStandby2) > 0.001)
                announcer.Announce($"COM2 standby {value:F3}");
            _lastComStandby2 = value;
            return true;
        }
        if (variableKey == "TRANSPONDER_CODE_SET")
        {
            if (_lastSquawkCode > 0 && System.Math.Abs(value - _lastSquawkCode) > 0.5)
            {
                int bcd = (int)value;
                int d1 = (bcd >> 12) & 0xF;
                int d2 = (bcd >> 8) & 0xF;
                int d3 = (bcd >> 4) & 0xF;
                int d4 = bcd & 0xF;
                announcer.Announce($"Squawk {d1}{d2}{d3}{d4}");
            }
            _lastSquawkCode = value;
            return true;
        }

        // ----- Batch 3 — AP modes (each announces when it engages/disengages) -----

        if (variableKey == "HS787_ApAltHold")
        {
            int now = (int)value;
            if (_previousApAltHold >= 0 && now != _previousApAltHold)
                announcer.Announce(now == 1 ? "Alt Hold engaged" : "Alt Hold off");
            _previousApAltHold = now;
            return true;
        }

        if (variableKey == "HS787_ApFlch")
        {
            int now = (int)value;
            if (_previousApFlch >= 0 && now != _previousApFlch)
                announcer.Announce(now == 1 ? "FLCH engaged" : "FLCH off");
            _previousApFlch = now;
            return true;
        }

        if (variableKey == "HS787_ApVs")
        {
            int now = (int)value;
            if (_previousApVs >= 0 && now != _previousApVs)
                announcer.Announce(now == 1 ? "V/S engaged" : "V/S off");
            _previousApVs = now;
            return true;
        }

        if (variableKey == "HS787_ApSpd")
        {
            int now = (int)value;
            if (_previousApSpd >= 0 && now != _previousApSpd)
                announcer.Announce(now == 1 ? "Speed engaged" : "Speed off");
            _previousApSpd = now;
            return true;
        }

        if (variableKey == "HS787_ApThr")
        {
            int now = (int)value;
            if (_previousApThr >= 0 && now != _previousApThr)
                announcer.Announce(now == 1 ? "Throttle engaged" : "Throttle off");
            _previousApThr = now;
            return true;
        }

        if (variableKey == "HS787_ApHdgHold")
        {
            int now = (int)value;
            if (_previousApHdgHold >= 0 && now != _previousApHdgHold)
                announcer.Announce(now == 1 ? "HDG Hold engaged" : "HDG Hold off");
            _previousApHdgHold = now;
            return true;
        }

        if (variableKey == "HS787_ApHdgSel")
        {
            int now = (int)value;
            if (_previousApHdgSel >= 0 && now != _previousApHdgSel)
                announcer.Announce(now == 1 ? "HDG Sel engaged" : "HDG Sel off");
            _previousApHdgSel = now;
            return true;
        }

        if (variableKey == "HS787_ApClbCon")
        {
            int now = (int)value;
            if (_previousApClbCon >= 0 && now != _previousApClbCon)
                announcer.Announce(now == 1 ? "Climb Continuous engaged" : "Climb Continuous off");
            _previousApClbCon = now;
            return true;
        }

        if (variableKey == "HS787_ApproachIls")
        {
            int now = (int)value;
            if (_previousApproachIls >= 0 && now != _previousApproachIls)
                announcer.Announce(now == 1 ? "ILS Approach armed" : "ILS Approach disarmed");
            _previousApproachIls = now;
            return true;
        }

        // Flight Timer mode/running and Checklist phase: silently cached so the FMC Status
        // display panel can read them, but no auto-announce. The WT 787 writes these L-vars
        // during FMS phase transitions (engine start, takeoff, climb, etc.) — auto-announcing
        // every transition produces noise the pilot doesn't need. The user reads them on
        // demand from the FMC Status panel display field.
        if (variableKey == "HS787_FltTimerMode")    return true;
        if (variableKey == "HS787_FltTimerRunning") return true;
        if (variableKey == "HS787_ChecklistPhase")  return true;

        if (variableKey == "HS787_HudSymbology1")
        {
            int now = (int)value;
            if (_previousHudSymbology1 >= 0 && now != _previousHudSymbology1)
                announcer.Announce(now == 1 ? "Captain HUD decluttered" : "Captain HUD full symbology");
            _previousHudSymbology1 = now;
            return true;
        }

        if (variableKey == "HS787_HudSymbology2")
        {
            int now = (int)value;
            if (_previousHudSymbology2 >= 0 && now != _previousHudSymbology2)
                announcer.Announce(now == 1 ? "First Officer HUD decluttered" : "First Officer HUD full symbology");
            _previousHudSymbology2 = now;
            return true;
        }

        if (variableKey == "HS787_HudDeclutterInhibit1")
        {
            int now = (int)value;
            if (_previousHudDecInhibit1 >= 0 && now != _previousHudDecInhibit1)
                announcer.Announce(now == 1 ? "Captain HUD declutter inhibit on" : "Captain HUD declutter inhibit off");
            _previousHudDecInhibit1 = now;
            return true;
        }

        if (variableKey == "HS787_HudDeclutterInhibit2")
        {
            int now = (int)value;
            if (_previousHudDecInhibit2 >= 0 && now != _previousHudDecInhibit2)
                announcer.Announce(now == 1 ? "First Officer HUD declutter inhibit on" : "First Officer HUD declutter inhibit off");
            _previousHudDecInhibit2 = now;
            return true;
        }

        // ----- Batch 4 — Fire detection -----
        // First-poll suppressed to avoid spurious "FIRE ENGINE N" on MSFSBA startup
        // (the WT Boeing fire SimVar transiently returns 1 during aircraft init in
        // some states). Real fires happen in flight, well after the baseline is
        // established, so suppressing first poll loses nothing in practice.

        if (variableKey == "HS787_EngFire1")
        {
            int now = (int)value;
            if (_previousEngFire1 >= 0 && now != _previousEngFire1)
                announcer.Announce(now == 1 ? "FIRE ENGINE 1" : "Engine 1 fire cleared");
            _previousEngFire1 = now;
            return true;
        }

        if (variableKey == "HS787_EngFire2")
        {
            int now = (int)value;
            if (_previousEngFire2 >= 0 && now != _previousEngFire2)
                announcer.Announce(now == 1 ? "FIRE ENGINE 2" : "Engine 2 fire cleared");
            _previousEngFire2 = now;
            return true;
        }

        if (variableKey == "HS787_FireBottleDisch1")
        {
            int now = (int)value;
            if (_previousFireBottleDisch1 >= 0 && now != _previousFireBottleDisch1 && now == 1)
                announcer.Announce("Fire Bottle 1 discharged");
            _previousFireBottleDisch1 = now;
            return true;
        }

        if (variableKey == "HS787_FireBottleDisch2")
        {
            int now = (int)value;
            if (_previousFireBottleDisch2 >= 0 && now != _previousFireBottleDisch2 && now == 1)
                announcer.Announce("Fire Bottle 2 discharged");
            _previousFireBottleDisch2 = now;
            return true;
        }

        // FuelBalanceFault: only announce when it turns ON. Suppress first poll
        // so a fault-on-startup (rare) is not re-announced every MSFSBA restart.
        if (variableKey == "HS787_FuelBalanceFault")
        {
            int now = (int)value;
            if (_previousFuelBalanceFault >= 0 && now == 1 && _previousFuelBalanceFault == 0)
                announcer.Announce("Fuel Balance Fault");
            _previousFuelBalanceFault = now;
            return true;
        }

        // EXECActive: announce both activation and deactivation (light on/off).
        // First poll suppressed (tri-state init).
        if (variableKey == "HS787_EXECActive")
        {
            int now = (int)value;
            if (_previousExecActive >= 0 && now != _previousExecActive)
                announcer.Announce(now == 1 ? "EXEC Active" : "EXEC Off");
            _previousExecActive = now;
            return true;
        }

        // TOGA: announce activation only. Suppress first poll.
        if (variableKey == "HS787_TOGA")
        {
            int now = (int)value;
            if (_previousTOGA >= 0 && now == 1 && _previousTOGA == 0)
                announcer.Announce("TOGA Active");
            _previousTOGA = now;
            return true;
        }

        // APDisconnected: announce disconnect only. Suppress first poll.
        if (variableKey == "HS787_APDisconnected")
        {
            int now = (int)value;
            if (_previousAPDisconnected >= 0 && now == 1 && _previousAPDisconnected == 0)
                announcer.Announce("Autopilot Disconnected");
            _previousAPDisconnected = now;
            return true;
        }

        // Approach mode: announce arm and disengage transitions.
        if (variableKey == "HS787_APP")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousAppHold >= 0 && now != _previousAppHold)
                announcer.Announce(now == 1 ? "Approach armed" : "Approach disengaged");
            _previousAppHold = now;
            return true;
        }

        // GS capture: announce once when glideslope becomes active.
        if (variableKey == "HS787_GS_Active")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousGSActive >= 0 && now == 1 && _previousGSActive == 0)
                announcer.Announce("Glideslope active");
            _previousGSActive = now;
            return true;
        }

        // Autopilot and autothrottle state — only actual transitions are announced.
        if (variableKey == "HS787_APMaster")
        {
            int now = (int)value;
            if (_previousAPMaster >= 0 && now != _previousAPMaster)
                announcer.Announce(now == 1 ? "Autopilot 1 On" : "Autopilot 1 Off");
            _previousAPMaster = now;
            return true;
        }

        if (variableKey == "HS787_ATStatus")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousATStatus >= 0 && now != _previousATStatus)
                announcer.Announce(now == 1 ? "Autothrottle Armed" : "Autothrottle Disarmed");
            _previousATStatus = now;
            return true;
        }

        // External power — announce changes only; suppress startup announcement.
        if (variableKey == "HS787_ExtPwrOn1")
        {
            int now = (int)value;
            if (_previousExtPwr1On >= 0 && now != _previousExtPwr1On)
                announcer.Announce(now == 1 ? "External Power 1 On" : "External Power 1 Off");
            _previousExtPwr1On = now;
            return true;
        }

        if (variableKey == "HS787_ExtPwrOn2")
        {
            int now = (int)value;
            if (_previousExtPwr2On >= 0 && now != _previousExtPwr2On)
                announcer.Announce(now == 1 ? "External Power 2 On" : "External Power 2 Off");
            _previousExtPwr2On = now;
            return true;
        }

        // Speedbrake: WT_SPEEDBRAKE_LEVER_POS is 0-16384; announce on state band changes.
        // DOWN_LIMIT=410, ARM_LIMIT=1230 (from BoeingSpeedbrakeSystem constants).
        // First poll suppressed via _previousSpeedbrakeState >= 0 guard.
        if (variableKey == "HS787_Speedbrake")
        {
            int state = value <= 410 ? 0 : value <= 1230 ? 1 : 2;
            if (_previousSpeedbrakeState >= 0 && state != _previousSpeedbrakeState)
            {
                string msg = state switch
                {
                    1 => "Speedbrake Armed",
                    2 => "Speedbrake Deployed",
                    _ => "Speedbrake Down"
                };
                announcer.Announce(msg);
            }
            _previousSpeedbrakeState = state;
            return true;
        }

        // MCP mode engagement/disengagement — announce both on and off transitions.
        // First poll suppressed via tri-state init.
        if (variableKey == "HS787_FLCH")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousFLCH >= 0 && now != _previousFLCH)
                announcer.Announce(now == 1 ? "Level Change Engaged" : "Level Change Off");
            _previousFLCH = now;
            return true;
        }

        if (variableKey == "HS787_ALTHold")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousALTHold >= 0 && now != _previousALTHold)
                announcer.Announce(now == 1 ? "Altitude Hold" : "Altitude Hold Off");
            _previousALTHold = now;
            return true;
        }

        if (variableKey == "HS787_LNAV")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousLNAV >= 0 && now != _previousLNAV)
                announcer.Announce(now == 1 ? "LNAV Engaged" : "LNAV Off");
            _previousLNAV = now;
            return true;
        }

        if (variableKey == "HS787_VNAV")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousVNAV >= 0 && now != _previousVNAV)
                announcer.Announce(now == 1 ? "VNAV Engaged" : "VNAV Off");
            _previousVNAV = now;
            return true;
        }

        if (variableKey == "HS787_HDGHold")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousHDGHold >= 0 && now != _previousHDGHold)
                announcer.Announce(now == 1 ? "Heading Hold" : "Heading Hold Off");
            _previousHDGHold = now;
            return true;
        }

        if (variableKey == "HS787_VS_Active")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousVSActive >= 0 && now != _previousVSActive)
                announcer.Announce(now == 1 ? "V/S Engaged" : "V/S Off");
            _previousVSActive = now;
            return true;
        }

        // APU knob — announce transitions; suppress first poll (startup state)
        if (variableKey == "HS787_APU_Knob")
        {
            int now = (int)value;
            if (_previousApuKnob >= 0 && now != _previousApuKnob)
            {
                string msg = now switch { 1 => "APU On", 2 => "APU Starting", _ => "APU Off" };
                announcer.Announce(msg);
            }
            _previousApuKnob = now;
            return true;
        }

        // Engine start states
        if (variableKey == "HS787_EngStartState1")
        {
            int now = (int)value;
            if (_previousEngState1 >= 0 && now != _previousEngState1)
            {
                string msg = now switch { 1 => "Engine 1 Starting", 2 => "Engine 1 Running", _ => "Engine 1 Stopped" };
                announcer.Announce(msg);
            }
            _previousEngState1 = now;
            return true;
        }

        if (variableKey == "HS787_EngStartState2")
        {
            int now = (int)value;
            if (_previousEngState2 >= 0 && now != _previousEngState2)
            {
                string msg = now switch { 1 => "Engine 2 Starting", 2 => "Engine 2 Running", _ => "Engine 2 Stopped" };
                announcer.Announce(msg);
            }
            _previousEngState2 = now;
            return true;
        }

        // Pack switches
        if (variableKey == "HS787_PackL")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousPackL >= 0 && now != _previousPackL)
                announcer.Announce(now == 1 ? "Pack Left Auto" : "Pack Left Off");
            _previousPackL = now;
            return true;
        }

        if (variableKey == "HS787_PackR")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousPackR >= 0 && now != _previousPackR)
                announcer.Announce(now == 1 ? "Pack Right Auto" : "Pack Right Off");
            _previousPackR = now;
            return true;
        }

        // Hydraulic demand pumps
        if (variableKey == "HS787_HydDemandLeft")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousHydDemandL >= 0 && now != _previousHydDemandL)
                announcer.Announce(now == 1 ? "Hydraulic Demand Left On" : "Hydraulic Demand Left Off");
            _previousHydDemandL = now;
            return true;
        }

        if (variableKey == "HS787_HydDemandRight")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousHydDemandR >= 0 && now != _previousHydDemandR)
                announcer.Announce(now == 1 ? "Hydraulic Demand Right On" : "Hydraulic Demand Right Off");
            _previousHydDemandR = now;
            return true;
        }

        // Emergency lights
        if (variableKey == "HS787_EmerLights")
        {
            int now = (int)value;
            if (_previousEmerLights >= 0 && now != _previousEmerLights)
            {
                string msg = now switch { 1 => "Emergency Lights Armed", 2 => "Emergency Lights On", _ => "Emergency Lights Off" };
                announcer.Announce(msg);
            }
            _previousEmerLights = now;
            return true;
        }

        // Seat belts sign
        if (variableKey == "HS787_Seatbelts")
        {
            int now = (int)value;
            if (_previousSeatbelts >= 0 && now != _previousSeatbelts)
            {
                string msg = now switch { 1 => "Seat Belts Auto", 2 => "Seat Belts On", _ => "Seat Belts Off" };
                announcer.Announce(msg);
            }
            _previousSeatbelts = now;
            return true;
        }

        // IRS knobs — announce On/Off transitions, suppress first poll
        if (variableKey == "HS787_IRS_Knob1")
        {
            int now = (int)value;
            if (_previousIrsKnob1 >= 0 && now != _previousIrsKnob1)
                announcer.Announce(now == 1 ? "IRS Left On" : "IRS Left Off");
            _previousIrsKnob1 = now;
            return true;
        }
        if (variableKey == "HS787_IRS_Knob2")
        {
            int now = (int)value;
            if (_previousIrsKnob2 >= 0 && now != _previousIrsKnob2)
                announcer.Announce(now == 1 ? "IRS Right On" : "IRS Right Off");
            _previousIrsKnob2 = now;
            return true;
        }

        // IRS position accepted (WT_IRS_POS_SET_N) — NOT alignment complete.
        // Announce the honest position semantics. (The true "TIME TO ALIGN" countdown
        // is not on any L-var; see the IRS section in GetVariables.) Suppress first poll.
        // IRS alignment state (synthetic L-var from the Coherent IRS client): 0 Off / 1 Aligning /
        // 2 Aligned. Announce as a clean phrase ("IRS aligned", not the generic "IRS: Aligned" which
        // read as "IRS alignment aligned" before the rename). First value is the silent baseline so
        // the client's connect-time write doesn't blurt "IRS off".
        if (variableKey == "HS787_IRS_Align")
        {
            int now = (int)Math.Round(value);
            if (_previousIrsAlignState >= 0 && now != _previousIrsAlignState)
                announcer.Announce(now == 2 ? "IRS aligned" : now == 1 ? "IRS aligning" : "IRS off");
            _previousIrsAlignState = now;
            return true;
        }

        if (variableKey == "HS787_IRS_Aligned1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousIrsAligned1 >= 0 && now != _previousIrsAligned1)
                announcer.Announce(now == 1 ? "IRS Left position set" : "IRS Left position lost");
            _previousIrsAligned1 = now;
            return true;
        }
        if (variableKey == "HS787_IRS_Aligned2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousIrsAligned2 >= 0 && now != _previousIrsAligned2)
                announcer.Announce(now == 1 ? "IRS Right position set" : "IRS Right position lost");
            _previousIrsAligned2 = now;
            return true;
        }

        // Anti-ice — 3-state (Off/Auto/On), suppress first poll
        if (variableKey == "HS787_AntiIceEng1")
        {
            int now = (int)value;
            if (_previousAntiIceEng1 >= 0 && now != _previousAntiIceEng1)
            {
                string msg = now switch { 1 => "Engine 1 Anti-Ice Auto", 2 => "Engine 1 Anti-Ice On", _ => "Engine 1 Anti-Ice Off" };
                announcer.Announce(msg);
            }
            _previousAntiIceEng1 = now;
            return true;
        }
        if (variableKey == "HS787_AntiIceEng2")
        {
            int now = (int)value;
            if (_previousAntiIceEng2 >= 0 && now != _previousAntiIceEng2)
            {
                string msg = now switch { 1 => "Engine 2 Anti-Ice Auto", 2 => "Engine 2 Anti-Ice On", _ => "Engine 2 Anti-Ice Off" };
                announcer.Announce(msg);
            }
            _previousAntiIceEng2 = now;
            return true;
        }
        if (variableKey == "HS787_AntiIceWing")
        {
            int now = (int)value;
            if (_previousAntiIceWing >= 0 && now != _previousAntiIceWing)
            {
                string msg = now switch { 1 => "Wing Anti-Ice Auto", 2 => "Wing Anti-Ice On", _ => "Wing Anti-Ice Off" };
                announcer.Announce(msg);
            }
            _previousAntiIceWing = now;
            return true;
        }

        // No smoking sign
        if (variableKey == "HS787_NoSmoking")
        {
            int now = (int)value;
            if (_previousNoSmoking >= 0 && now != _previousNoSmoking)
            {
                string msg = now switch { 1 => "No Smoking Auto", 2 => "No Smoking On", _ => "No Smoking Off" };
                announcer.Announce(msg);
            }
            _previousNoSmoking = now;
            return true;
        }

        // Parking brake
        if (variableKey == "HS787_ParkBrake")
        {
            int now = (int)value;
            if (_previousParkBrake >= 0 && now != _previousParkBrake)
                announcer.Announce(now == 1 ? "Parking Brake Set" : "Parking Brake Released");
            _previousParkBrake = now;
            return true;
        }

        // Flaps — announce position on lever movement, suppress first poll
        if (variableKey == "HS787_FlapsHandle")
        {
            int now = (int)value;
            if (_previousFlapsHandle >= 0 && now != _previousFlapsHandle)
            {
                string pos = now switch
                {
                    0 => "Up", 1 => "1", 2 => "5", 3 => "10",
                    4 => "15", 5 => "17", 6 => "18", 7 => "20",
                    8 => "25", 9 => "30", _ => now.ToString()
                };
                announcer.Announce($"Flaps {pos}");
            }
            _previousFlapsHandle = now;
            return true;
        }

        // Gear handle — announce Up/Down transitions, suppress first poll
        if (variableKey == "HS787_GearHandle")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousGearHandleI >= 0 && now != _previousGearHandleI)
                announcer.Announce(now == 1 ? "Gear Down" : "Gear Up");
            _previousGearHandleI = now;
            return true;
        }

        // Exterior lights — announce primary lights on change, suppress first poll
        if (variableKey == "HS787_LightBeacon")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightBeacon >= 0 && now != _previousLightBeacon)
                announcer.Announce(now == 1 ? "Beacon On" : "Beacon Off");
            _previousLightBeacon = now;
            return true;
        }
        if (variableKey == "HS787_LightStrobe")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightStrobe >= 0 && now != _previousLightStrobe)
                announcer.Announce(now == 1 ? "Strobe On" : "Strobe Off");
            _previousLightStrobe = now;
            return true;
        }
        if (variableKey == "HS787_LightNav")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightNav >= 0 && now != _previousLightNav)
                announcer.Announce(now == 1 ? "Nav Lights On" : "Nav Lights Off");
            _previousLightNav = now;
            return true;
        }
        if (variableKey == "HS787_LightLanding")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightLanding >= 0 && now != _previousLightLanding)
                announcer.Announce(now == 1 ? "Landing Lights On" : "Landing Lights Off");
            _previousLightLanding = now;
            return true;
        }

        // ----- Batch 7 — panel switches promoted to Continuous+Announced -----

        if (variableKey == "HS787_BatSwitch1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBatSwitch1 >= 0 && now != _previousBatSwitch1)
                announcer.Announce(now == 1 ? "Battery 1 On" : "Battery 1 Off");
            _previousBatSwitch1 = now;
            return true;
        }
        if (variableKey == "HS787_BatSwitch2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBatSwitch2 >= 0 && now != _previousBatSwitch2)
                announcer.Announce(now == 1 ? "Battery 2 On" : "Battery 2 Off");
            _previousBatSwitch2 = now;
            return true;
        }
        if (variableKey == "HS787_Gen1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousGen1 >= 0 && now != _previousGen1)
                announcer.Announce(now == 1 ? "Generator 1 On" : "Generator 1 Off");
            _previousGen1 = now;
            return true;
        }
        if (variableKey == "HS787_Gen2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousGen2 >= 0 && now != _previousGen2)
                announcer.Announce(now == 1 ? "Generator 2 On" : "Generator 2 Off");
            _previousGen2 = now;
            return true;
        }
        if (variableKey == "HS787_ApuGen1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousApuGen1 >= 0 && now != _previousApuGen1)
                announcer.Announce(now == 1 ? "APU Generator 1 On" : "APU Generator 1 Off");
            _previousApuGen1 = now;
            return true;
        }
        if (variableKey == "HS787_ApuGen2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousApuGen2 >= 0 && now != _previousApuGen2)
                announcer.Announce(now == 1 ? "APU Generator 2 On" : "APU Generator 2 Off");
            _previousApuGen2 = now;
            return true;
        }
        // HS787_ExtPwr1/2 announcements are suppressed via the cache-only switch at the
        // bottom of ProcessSimVarUpdate. HS787_ExtPwrOn1/2 (delivered-power SimVar) owns
        // the user-facing "External Power N On/Off" callout, so handling these here would
        // double-talk.
        if (variableKey == "HS787_UtilityCabin")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousUtilityCabin >= 0 && now != _previousUtilityCabin)
                announcer.Announce(now == 1 ? "Cabin Utility On" : "Cabin Utility Off");
            _previousUtilityCabin = now;
            return true;
        }
        if (variableKey == "HS787_UtilityIfe")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousUtilityIfe >= 0 && now != _previousUtilityIfe)
                announcer.Announce(now == 1 ? "IFE Utility On" : "IFE Utility Off");
            _previousUtilityIfe = now;
            return true;
        }
        if (variableKey == "HS787_HydEngL")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydEngL >= 0 && now != _previousHydEngL)
                announcer.Announce(now == 1 ? "Hydraulic Engine Left On" : "Hydraulic Engine Left Off");
            _previousHydEngL = now;
            return true;
        }
        if (variableKey == "HS787_HydEngR")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydEngR >= 0 && now != _previousHydEngR)
                announcer.Announce(now == 1 ? "Hydraulic Engine Right On" : "Hydraulic Engine Right Off");
            _previousHydEngR = now;
            return true;
        }
        if (variableKey == "HS787_HydC1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydC1 >= 0 && now != _previousHydC1)
                announcer.Announce(now == 1 ? "Hydraulic Center 1 On" : "Hydraulic Center 1 Off");
            _previousHydC1 = now;
            return true;
        }
        if (variableKey == "HS787_HydC2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydC2 >= 0 && now != _previousHydC2)
                announcer.Announce(now == 1 ? "Hydraulic Center 2 On" : "Hydraulic Center 2 Off");
            _previousHydC2 = now;
            return true;
        }
        if (variableKey == "HS787_FuelBalance")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelBalance >= 0 && now != _previousFuelBalance)
                announcer.Announce(now == 1 ? "Fuel Balance On" : "Fuel Balance Off");
            _previousFuelBalance = now;
            return true;
        }
        if (variableKey == "HS787_FuelBalanceActive")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelBalanceActive >= 0 && now != _previousFuelBalanceActive)
                announcer.Announce(now == 1 ? "Fuel Balance Active" : "Fuel Balance Inactive");
            _previousFuelBalanceActive = now;
            return true;
        }
        if (variableKey == "HS787_TrimAirL")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousTrimAirL >= 0 && now != _previousTrimAirL)
                announcer.Announce(now == 1 ? "Trim Air Left On" : "Trim Air Left Off");
            _previousTrimAirL = now;
            return true;
        }
        if (variableKey == "HS787_TrimAirR")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousTrimAirR >= 0 && now != _previousTrimAirR)
                announcer.Announce(now == 1 ? "Trim Air Right On" : "Trim Air Right Off");
            _previousTrimAirR = now;
            return true;
        }
        if (variableKey == "HS787_RecircUpper")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousRecircUpper >= 0 && now != _previousRecircUpper)
                announcer.Announce(now == 1 ? "Upper Recirc Fan On" : "Upper Recirc Fan Off");
            _previousRecircUpper = now;
            return true;
        }
        if (variableKey == "HS787_RecircLower")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousRecircLower >= 0 && now != _previousRecircLower)
                announcer.Announce(now == 1 ? "Lower Recirc Fan On" : "Lower Recirc Fan Off");
            _previousRecircLower = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice1 >= 0 && now != _previousWshldDeice1)
                announcer.Announce(now == 1 ? "Window Heat 1 On" : "Window Heat 1 Off");
            _previousWshldDeice1 = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice2 >= 0 && now != _previousWshldDeice2)
                announcer.Announce(now == 1 ? "Window Heat 2 On" : "Window Heat 2 Off");
            _previousWshldDeice2 = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice3")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice3 >= 0 && now != _previousWshldDeice3)
                announcer.Announce(now == 1 ? "Window Heat 3 On" : "Window Heat 3 Off");
            _previousWshldDeice3 = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice4")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice4 >= 0 && now != _previousWshldDeice4)
                announcer.Announce(now == 1 ? "Window Heat 4 On" : "Window Heat 4 Off");
            _previousWshldDeice4 = now;
            return true;
        }
        if (variableKey == "HS787_AltnFlapsArmed")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousAltnFlapsArmed >= 0 && now != _previousAltnFlapsArmed)
                announcer.Announce(now == 1 ? "Alternate Flaps Armed" : "Alternate Flaps Disarmed");
            _previousAltnFlapsArmed = now;
            return true;
        }
        if (variableKey == "HS787_BaroSelector")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBaroSelector >= 0 && now != _previousBaroSelector)
                announcer.Announce(now == 1 ? "Baro Selector HPa" : "Baro Selector Inches");
            _previousBaroSelector = now;
            return true;
        }
        if (variableKey == "HS787_MinsMode")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousMinsMode >= 0 && now != _previousMinsMode)
                announcer.Announce(now == 1 ? "Minimums Baro" : "Minimums Radio");
            _previousMinsMode = now;
            return true;
        }
        if (variableKey == "HS787_FPVMode")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFPVMode >= 0 && now != _previousFPVMode)
                announcer.Announce(now == 1 ? "FPV On" : "FPV Off");
            _previousFPVMode = now;
            return true;
        }
        if (variableKey == "HS787_TransponderMode")
        {
            int now = (int)value;
            if (_previousTransponderMode >= 0 && now != _previousTransponderMode)
            {
                string msg = now switch
                {
                    0 => "Transponder Standby",
                    1 => "Transponder Alt Off",
                    2 => "Transponder XPNDR",
                    3 => "Transponder TA",
                    4 => "Transponder TA/RA",
                    _ => $"Transponder Mode {now}"
                };
                announcer.Announce(msg);
            }
            _previousTransponderMode = now;
            return true;
        }
        if (variableKey == "HS787_SATCOM")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousSATCOM >= 0 && now != _previousSATCOM)
                announcer.Announce(now == 1 ? "SATCOM On" : "SATCOM Off");
            _previousSATCOM = now;
            return true;
        }
        if (variableKey == "HS787_VBar")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousVBar >= 0 && now != _previousVBar)
                announcer.Announce(now == 1 ? "V-Bar On" : "V-Bar Off");
            _previousVBar = now;
            return true;
        }
        if (variableKey == "HS787_Autobrake")
        {
            int now = (int)value;
            // Swallow exactly the update that lands on the value MSFSBA just commanded via the combo
            // (the screen reader already speaks the selection). Matched by VALUE within a short window
            // and one-shot, so it can NEVER permanently mute callouts: any other change — including a
            // later external knob turn — falls through and announces, and the latch self-expires.
            if (_autobrakeSuppressTarget >= 0 && now == _autobrakeSuppressTarget &&
                Environment.TickCount64 - _autobrakeSuppressTicks < 3000)
            {
                _autobrakeSuppressTarget = -1;
                _previousAutobrake = now;
                return true;
            }
            if (_previousAutobrake >= 0 && now != _previousAutobrake)
            {
                // Positions MUST match HS787_Autobrake.ValueDescriptions and the INCREASE/DECREASE
                // step-write in HandleUIVariableSet: the AUTO BRAKE SWITCH CB scale is 0..6 =
                // Off / RTO / 1 / 2 / 3 / 4 / MAX (live-verified 7 detents). Keep all three in sync.
                string msg = now switch
                {
                    0 => "Autobrakes Off",
                    1 => "Autobrakes RTO",
                    2 => "Autobrakes 1",
                    3 => "Autobrakes 2",
                    4 => "Autobrakes 3",
                    5 => "Autobrakes 4",
                    6 => "Autobrakes Max",
                    _ => $"Autobrakes {now}"
                };
                announcer.Announce(msg);
            }
            _previousAutobrake = now;
            return true;
        }
        if (variableKey == "HS787_LightTaxi")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightTaxi >= 0 && now != _previousLightTaxi)
                announcer.Announce(now == 1 ? "Taxi Light On" : "Taxi Light Off");
            _previousLightTaxi = now;
            return true;
        }
        if (variableKey == "HS787_LightLogo")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightLogo >= 0 && now != _previousLightLogo)
                announcer.Announce(now == 1 ? "Logo Light On" : "Logo Light Off");
            _previousLightLogo = now;
            return true;
        }
        if (variableKey == "HS787_LightWing")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightWing >= 0 && now != _previousLightWing)
                announcer.Announce(now == 1 ? "Runway Turnoff On" : "Runway Turnoff Off");
            _previousLightWing = now;
            return true;
        }

        // ----- Batch 8 — fuel pumps, bleeds, fire, cargo, standby, fuel control -----

        if (variableKey == "HS787_FuelPump_LFwd")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_LFwd >= 0 && now != _previousFuelPump_LFwd)
                announcer.Announce(now == 1 ? "Left Forward Fuel Pump On" : "Left Forward Fuel Pump Off");
            _previousFuelPump_LFwd = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_LAft")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_LAft >= 0 && now != _previousFuelPump_LAft)
                announcer.Announce(now == 1 ? "Left Aft Fuel Pump On" : "Left Aft Fuel Pump Off");
            _previousFuelPump_LAft = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_RFwd")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_RFwd >= 0 && now != _previousFuelPump_RFwd)
                announcer.Announce(now == 1 ? "Right Forward Fuel Pump On" : "Right Forward Fuel Pump Off");
            _previousFuelPump_RFwd = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_RAft")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_RAft >= 0 && now != _previousFuelPump_RAft)
                announcer.Announce(now == 1 ? "Right Aft Fuel Pump On" : "Right Aft Fuel Pump Off");
            _previousFuelPump_RAft = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_CtrL")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_CtrL >= 0 && now != _previousFuelPump_CtrL)
                announcer.Announce(now == 1 ? "Left Center Fuel Pump On" : "Left Center Fuel Pump Off");
            _previousFuelPump_CtrL = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_CtrR")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_CtrR >= 0 && now != _previousFuelPump_CtrR)
                announcer.Announce(now == 1 ? "Right Center Fuel Pump On" : "Right Center Fuel Pump Off");
            _previousFuelPump_CtrR = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_APU")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_APU >= 0 && now != _previousFuelPump_APU)
                announcer.Announce(now == 1 ? "APU Fuel Pump On" : "APU Fuel Pump Off");
            _previousFuelPump_APU = now;
            return true;
        }
        if (variableKey == "HS787_FuelXfeed")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelXfeedFwd >= 0 && now != _previousFuelXfeedFwd)
                announcer.Announce(now == 1 ? "Fuel Crossfeed Open" : "Fuel Crossfeed Closed");
            _previousFuelXfeedFwd = now;
            return true;
        }
        if (variableKey == "HS787_BleedEng1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedEng1 >= 0 && now != _previousBleedEng1)
                announcer.Announce(now == 1 ? "Engine 1 Bleed On" : "Engine 1 Bleed Off");
            _previousBleedEng1 = now;
            return true;
        }
        if (variableKey == "HS787_BleedEng2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedEng2 >= 0 && now != _previousBleedEng2)
                announcer.Announce(now == 1 ? "Engine 2 Bleed On" : "Engine 2 Bleed Off");
            _previousBleedEng2 = now;
            return true;
        }
        if (variableKey == "HS787_BleedAPU")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedAPU >= 0 && now != _previousBleedAPU)
                announcer.Announce(now == 1 ? "APU Bleed On" : "APU Bleed Off");
            _previousBleedAPU = now;
            return true;
        }
        if (variableKey == "HS787_BleedIso")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedIso >= 0 && now != _previousBleedIso)
                announcer.Announce(now == 1 ? "Bleed Isolation Open" : "Bleed Isolation Closed");
            _previousBleedIso = now;
            return true;
        }
        if (variableKey == "HS787_FireTest")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFireTest >= 0 && now != _previousFireTest)
                announcer.Announce(now == 1 ? "Fire and overheat test in progress" : "Fire and overheat test complete");
            _previousFireTest = now;
            return true;
        }
        if (variableKey == "HS787_EngFireHandle1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousEngFireHandle1 >= 0 && now != _previousEngFireHandle1)
                announcer.Announce(now == 1 ? "Engine 1 Fire Handle Pulled" : "Engine 1 Fire Handle Stowed");
            _previousEngFireHandle1 = now;
            return true;
        }
        if (variableKey == "HS787_EngFireHandle2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousEngFireHandle2 >= 0 && now != _previousEngFireHandle2)
                announcer.Announce(now == 1 ? "Engine 2 Fire Handle Pulled" : "Engine 2 Fire Handle Stowed");
            _previousEngFireHandle2 = now;
            return true;
        }
        if (variableKey == "HS787_APUFireHandle")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousAPUFireHandle >= 0 && now != _previousAPUFireHandle)
                announcer.Announce(now == 1 ? "APU Fire Handle Pulled" : "APU Fire Handle Stowed");
            _previousAPUFireHandle = now;
            return true;
        }
        if (variableKey == "HS787_CargoFireFwd")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousCargoFireFwd >= 0 && now != _previousCargoFireFwd)
                announcer.Announce(now == 1 ? "Cargo Fire Arm Forward On" : "Cargo Fire Arm Forward Off");
            _previousCargoFireFwd = now;
            return true;
        }
        if (variableKey == "HS787_CargoFireAft")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousCargoFireAft >= 0 && now != _previousCargoFireAft)
                announcer.Announce(now == 1 ? "Cargo Fire Arm Aft On" : "Cargo Fire Arm Aft Off");
            _previousCargoFireAft = now;
            return true;
        }
        if (variableKey == "HS787_CargoFireDisch")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousCargoFireDisch >= 0 && now != _previousCargoFireDisch && now == 1)
                announcer.Announce("Cargo Fire Discharged");
            _previousCargoFireDisch = now;
            return true;
        }
        if (variableKey == "HS787_StandbyPower")
        {
            int now = (int)value;
            if (_previousStandbyPower >= 0 && now != _previousStandbyPower)
            {
                string msg = now switch
                {
                    1 => "Standby Power Auto",
                    2 => "Standby Power Battery",
                    _ => "Standby Power Off"
                };
                announcer.Announce(msg);
            }
            _previousStandbyPower = now;
            return true;
        }
        if (variableKey == "HS787_BaroSTD")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBaroSTD >= 0 && now != _previousBaroSTD)
                announcer.Announce(now == 1 ? "Baro Standard" : "Baro QNH");
            _previousBaroSTD = now;
            return true;
        }
        // INTERACTIVE POINT OPEN:N is a 0/100 percent value, so anything above 50 = open/connected.
        if (variableKey == "HS787_GPUPipe")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousGPUPipe >= 0 && now != _previousGPUPipe)
                announcer.Announce(now == 1 ? "GPU Cable Connected" : "GPU Cable Disconnected");
            _previousGPUPipe = now;
            return true;
        }
        if (variableKey == "HS787_RefuelDoor")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousRefuelDoor >= 0 && now != _previousRefuelDoor)
                announcer.Announce(now == 1 ? "Refuel Door Open" : "Refuel Door Closed");
            _previousRefuelDoor = now;
            return true;
        }
        // Passenger + cargo doors (EXIT OPEN:0-9). Open ≥ 50 % is announced as "Open";
        // below as "Closed". The HS787 doesn't simulate door arming / slide deployment —
        // doors are just open/closed; no arm/disarm cycle required before takeoff or
        // disembark for this aircraft.
        if (variableKey == "HS787_Door_1L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor1L >= 0 && now != _previousDoor1L)
                announcer.Announce(now == 1 ? "Door 1 left open" : "Door 1 left closed");
            _previousDoor1L = now;
            return true;
        }
        if (variableKey == "HS787_Door_1R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor1R >= 0 && now != _previousDoor1R)
                announcer.Announce(now == 1 ? "Door 1 right open" : "Door 1 right closed");
            _previousDoor1R = now;
            return true;
        }
        if (variableKey == "HS787_Door_2L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor2L >= 0 && now != _previousDoor2L)
                announcer.Announce(now == 1 ? "Door 2 left open" : "Door 2 left closed");
            _previousDoor2L = now;
            return true;
        }
        if (variableKey == "HS787_Door_2R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor2R >= 0 && now != _previousDoor2R)
                announcer.Announce(now == 1 ? "Door 2 right open" : "Door 2 right closed");
            _previousDoor2R = now;
            return true;
        }
        if (variableKey == "HS787_Door_3L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor3L >= 0 && now != _previousDoor3L)
                announcer.Announce(now == 1 ? "Door 3 left open" : "Door 3 left closed");
            _previousDoor3L = now;
            return true;
        }
        if (variableKey == "HS787_Door_3R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor3R >= 0 && now != _previousDoor3R)
                announcer.Announce(now == 1 ? "Door 3 right open" : "Door 3 right closed");
            _previousDoor3R = now;
            return true;
        }
        if (variableKey == "HS787_Door_4L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor4L >= 0 && now != _previousDoor4L)
                announcer.Announce(now == 1 ? "Door 4 left open" : "Door 4 left closed");
            _previousDoor4L = now;
            return true;
        }
        if (variableKey == "HS787_Door_4R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor4R >= 0 && now != _previousDoor4R)
                announcer.Announce(now == 1 ? "Door 4 right open" : "Door 4 right closed");
            _previousDoor4R = now;
            return true;
        }
        if (variableKey == "HS787_Door_FwdCargo")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoorFwdCargo >= 0 && now != _previousDoorFwdCargo)
                announcer.Announce(now == 1 ? "Forward cargo door open" : "Forward cargo door closed");
            _previousDoorFwdCargo = now;
            return true;
        }
        if (variableKey == "HS787_Door_AftCargo")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoorAftCargo >= 0 && now != _previousDoorAftCargo)
                announcer.Announce(now == 1 ? "Aft cargo door open" : "Aft cargo door closed");
            _previousDoorAftCargo = now;
            return true;
        }
        if (variableKey == "HS787_FuelControl1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelControl1 >= 0 && now != _previousFuelControl1)
                announcer.Announce(now == 1 ? "Engine 1 Fuel Control Run" : "Engine 1 Fuel Control Cutoff");
            _previousFuelControl1 = now;
            return true;
        }
        if (variableKey == "HS787_FuelControl2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelControl2 >= 0 && now != _previousFuelControl2)
                announcer.Announce(now == 1 ? "Engine 2 Fuel Control Run" : "Engine 2 Fuel Control Cutoff");
            _previousFuelControl2 = now;
            return true;
        }

        // Altimeter setting — announce changes, suppress first-poll value
        if (variableKey == "HS787_Altimeter")
        {
            double inHg = value;
            if (double.IsNaN(_lastAnnouncedAltimeter))
            {
                _lastAnnouncedAltimeter = inHg;
                return true;
            }
            if (Math.Abs(inHg - _lastAnnouncedAltimeter) < 0.005)
                return true;
            _lastAnnouncedAltimeter = inHg;
            if (Math.Abs(inHg - 29.92) < 0.005)
                announcer.Announce("Altimeter standard");
            else
            {
                int hpa = (int)Math.Round(inHg * 33.8639);
                announcer.Announce($"Altimeter {hpa}");
            }
            return true;
        }

        // MCP selected-value CHANGES are announced so an external hardware-knob turn is spoken
        // (the user's request). MSFSBA's own set dialogs fire the value SILENTLY via events
        // (ShowHeadingDialog etc. send HEADING_BUG_SET with no announce), so this auto-announce
        // is the single confirmation for both paths — no double-speak. First value is the silent
        // baseline; deadbands kill jitter. Speed is gated on MANUAL mode so the FMC's managed-speed
        // drift in VNAV/climb doesn't chatter (the user wants knob turns, not managed targets).
        if (variableKey == "HS787_MCP_Heading")
        {
            int hdg = ((int)Math.Round(value) % 360 + 360) % 360;
            if (_prevMcpHeading >= 0 && Math.Abs(hdg - _prevMcpHeading) >= 1) announcer.Announce($"Heading {hdg:000}");
            _prevMcpHeading = hdg;
            return true;
        }
        if (variableKey == "HS787_MCP_Altitude")
        {
            int alt = (int)Math.Round(value);
            if (_prevMcpAlt >= 0 && Math.Abs(alt - _prevMcpAlt) >= 10) announcer.Announce($"Altitude {alt}");
            _prevMcpAlt = alt;
            return true;
        }
        if (variableKey == "HS787_MCP_VS")
        {
            int vs = (int)Math.Round(value);
            if (_prevMcpVs != int.MinValue && Math.Abs(vs - _prevMcpVs) >= 50)
                announcer.Announce(vs == 0 ? "Vertical speed zero" : $"Vertical speed {(vs > 0 ? "plus " : "minus ")}{Math.Abs(vs)}");
            _prevMcpVs = vs;
            return true;
        }
        if (variableKey == "HS787_MCP_IsMach")   { _mcpIsMach = value > 0.5; return true; }
        if (variableKey == "HS787_MCP_SpdManual") { _mcpSpdManual = value > 0.5; return true; }
        if (variableKey == "HS787_MCP_IAS")
        {
            int ias = (int)Math.Round(value);
            if (_prevMcpIas >= 0 && Math.Abs(ias - _prevMcpIas) >= 1 && _mcpSpdManual && !_mcpIsMach) announcer.Announce($"Speed {ias}");
            _prevMcpIas = ias;
            return true;
        }
        if (variableKey == "HS787_MCP_Mach")
        {
            if (_prevMcpMach >= 0 && Math.Abs(value - _prevMcpMach) >= 0.005 && _mcpSpdManual && _mcpIsMach)
                announcer.Announce($"Mach {value:0.00}");
            _prevMcpMach = value;
            return true;
        }

        // Cache-only variables — suppress all automatic announcements.
        // These are IsAnnounced=true purely so the monitoring engine caches them;
        // hotkey readouts and dialog toggles read the cached values on demand.
        switch (variableKey)
        {
            // EICAS engine indications — cached for the Alt+E window, never auto-announced.
            case "HS787_EicasN1_1":
            case "HS787_EicasN1_2":
            case "HS787_EicasN2_1":
            case "HS787_EicasN2_2":
            case "HS787_EicasEGT_1":
            case "HS787_EicasEGT_2":
            case "HS787_EicasFuelKg":
            case "HS787_EicasGwKg":
            case "HS787_EicasOilP_1":
            case "HS787_EicasOilP_2":
            case "HS787_EicasOilT_1":
            case "HS787_EicasOilT_2":
            case "HS787_EicasTat":
            case "HS787_MCP_FPA":
            case "HS787_FPAMode":
            case "HS787_TRKMode":
            case "HS787_GS_Armed":
            case "HS787_LOC":
            case "HS787_AltManual":
            case "HS787_FuelLH":
            case "HS787_FuelRH":
            case "HS787_FuelCtr":
            case "HS787_FuelWtPerGal":
            case "HS787_DistDest":
            case "HS787_GroundSpeed":
            case "HS787_DistTOD":
            case "HS787_EteDest":
            // Ground-power combos: monitored continuously so the combo's displayed
            // state matches reality from MSFSBA connect; HS787_ExtPwrOn1/2 owns the
            // user-facing announcement, so suppress here to avoid duplicate speech.
            case "HS787_ExtPwr1":
            case "HS787_ExtPwr2":
            // IRS time-to-align minutes: cached for the read-only display field only.
            // The Aligning->Aligned transition is announced via HS787_IRS_Align's
            // ValueDescriptions; a per-minute spoken countdown would be noise.
            case "HS787_IRS_AlignMinutes":
                return true; // cached — no announcement
        }

        return false;
    }
}
