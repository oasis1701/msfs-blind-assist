using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class HorizonSim787Definition
{
    // =========================================================================
    // HandleUIVariableSet — panel control actions
    // =========================================================================

    public override bool HandleUIVariableSet(string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        // InputEvent (B:) first-pass — for switches whose real subsystem is wired through
        // InputEvents in the WT/Asobo 787 model (AT arm, ext power, battery, fuel pumps).
        // If the mapped name is in the catalog we win immediately; otherwise fall through
        // to the per-var K-event/SetLVar branches below so older models (or base sim
        // without HS787 loaded) keep working.
        if (HS787_INPUT_EVENT_MAP.TryGetValue(varKey, out var candidates) &&
            TryFireInputEvent(simConnect, value, candidates))
        {
            return true;
        }

        // AP master — toggle via K event (AUTOPILOT MASTER is a SimVar, not settable via SetLVar).
        // State-aware: AP_MASTER flips the current state, so only fire when the combo's
        // target differs from the live state, or re-selecting the current value inverts it.
        if (varKey == "HS787_APMaster")
        {
            double? current = simConnect.GetCachedVariableValue("HS787_APMaster");
            if (current == null || (int)current.Value != (int)value)
                simConnect.SendEvent("AP_MASTER");
            return true;
        }

        // Flight Director — TOGGLE_FLIGHT_DIRECTOR flips AUTOPILOT FLIGHT DIRECTOR ACTIVE; only
        // fire when the desired Off/On state differs from the live state (so the combo is a true set).
        if (varKey == "HS787_FlightDirector")
        {
            int desired = value > 0.5 ? 1 : 0;
            int current;
            if (_lastFdSetTicks != 0 && (Environment.TickCount64 - _lastFdSetTicks) < 700)
                current = _lastFdDesired;   // recent command — trust intent, the cache still lags
            else
                current = (simConnect.GetCachedVariableValue("HS787_FlightDirector") ?? 0) > 0.5 ? 1 : 0;
            if (desired != current) simConnect.SendEvent("TOGGLE_FLIGHT_DIRECTOR");
            _lastFdDesired = desired;
            _lastFdSetTicks = Environment.TickCount64;
            return true;
        }

        // Transponder IDENT — momentary button; fire XPNDR_IDENT_ON on press.
        if (varKey == "HS787_XpndrIdent")
        {
            if (value > 0.5)
            {
                simConnect.SendEvent("XPNDR_IDENT_ON");
                announcer.Announce("Ident");
            }
            return true;
        }

        // Autothrottle arm — fallback when the InputEvent path above didn't match.
        // State-aware: AUTO_THROTTLE_ARM flips the current arm state, so only fire when
        // the combo's target differs from the live state, or re-selecting the current
        // value disarms an armed autothrottle.
        if (varKey == "HS787_ATStatus")
        {
            double? current = simConnect.GetCachedVariableValue("HS787_ATStatus");
            if (current == null || (int)current.Value != (int)value)
                simConnect.SendEvent("AUTO_THROTTLE_ARM");
            return true;
        }

        // Engine autostart fallback — if PROCEDURE_AUTOSTART isn't in the catalog,
        // fire the standard MSFS ENGINE_AUTO_START K event (the Ctrl+E binding).
        // Works on any aircraft, including the base Asobo 787-10.
        if (varKey == "HS787_EngineAutoStart")
        {
            simConnect.SendEvent("ENGINE_AUTO_START");
            return true;
        }

        // Approach + Localizer fallback — K events for non-WT aircraft.
        if (varKey == "HS787_APP")
        {
            simConnect.SendEvent("AP_APR_HOLD");
            return true;
        }
        if (varKey == "HS787_LOC")
        {
            simConnect.SendEvent("AP_LOC_HOLD");
            return true;
        }

        // External power — state-aware toggle. AIRLINER_EXT_PWR_N is a momentary
        // pushbutton on the WT 787 (press = 1, release = 0; each press flips state).
        // Combo passes target value (0/1); we read current state from EXTERNAL POWER
        // ON:N and only fire the press+release cycle when state needs to change. Falls
        // back to the legacy EXT_PWR_COMMANDED L-var pulse if the InputEvent isn't in
        // the catalog (older models or base Asobo 787). The 200 ms release delay gives
        // the cockpit avionics time to latch the press before we drop it back to 0.
        if (varKey == "HS787_ExtPwr1" || varKey == "HS787_ExtPwr2")
        {
            string stateVar = varKey == "HS787_ExtPwr1" ? "HS787_ExtPwrOn1" : "HS787_ExtPwrOn2";
            int currentState = (int)(simConnect.GetCachedVariableValue(stateVar) ?? 0);
            int targetState  = (int)value;
            if (currentState == targetState)
                return true; // no-op — state already matches request

            string eventName = varKey == "HS787_ExtPwr1" ? "AIRLINER_EXT_PWR_1" : "AIRLINER_EXT_PWR_2";
            bool usedInputEvent = simConnect.HasInputEvent(eventName) &&
                                  simConnect.TrySetInputEvent(eventName, 1);
            if (!usedInputEvent)
            {
                // Fallback path for non-WT models.
                string lvarName = varKey == "HS787_ExtPwr1" ? "EXT_PWR_COMMANDED:1" : "EXT_PWR_COMMANDED:2";
                simConnect.SetLVar(lvarName, 1);
            }

            var releaseTimer = new System.Windows.Forms.Timer { Interval = 200 };
            releaseTimer.Tick += (_, __) =>
            {
                releaseTimer.Stop();
                releaseTimer.Dispose();
                if (usedInputEvent)
                    simConnect.TrySetInputEvent(eventName, 0);
                else
                {
                    string lvarName = varKey == "HS787_ExtPwr1" ? "EXT_PWR_COMMANDED:1" : "EXT_PWR_COMMANDED:2";
                    simConnect.SetLVar(lvarName, 0);
                }
            };
            releaseTimer.Start();
            return true;
        }

        // APU knob — use K:events to drive the WT Boeing APU state machine.
        // Direct LVar writes to XMLVAR_APU_StarterKnob_Pos are ignored by the WT system.
        // K:APU_ON_SWITCH doesn't exist in standard MSFS — the knob's "On" position
        // is reached by firing APU_STARTER (knob goes Start→On after spring release).
        if (varKey == "HS787_APU_Knob")
        {
            string apuEvent = (int)value switch
            {
                2 => "APU_STARTER",
                1 => "APU_STARTER",
                _ => "APU_OFF_SWITCH"
            };
            simConnect.SendEvent(apuEvent);
            return true;
        }

        // APU Generators. The per-index APU_GEN1/2_SWITCH_SET events are no-ops on the WT 787
        // (live-verified). The stock un-indexed APU_GENERATOR_SWITCH_SET works and drives BOTH
        // APU GENERATOR SWITCH:1 AND :2 (the two gens are ganged on this model), so both combos
        // fire it; their read-backs (:1 / :2) stay in sync.
        if (varKey == "HS787_ApuGen1" || varKey == "HS787_ApuGen2")
        {
            simConnect.ExecuteCalculatorCode($"{(int)value} (>K:APU_GENERATOR_SWITCH_SET)");
            return true;
        }

        // Autobrakes (0 Off .. 6 MAX). SET_AUTOBRAKE_CONTROL is a no-op on the WT 787; the working
        // path is the relative rotary events INCREASE/DECREASE_AUTOBRAKE_CONTROL stepped to the
        // target (they clamp at the ends, so over-stepping is safe). Live-verified.
        if (varKey == "HS787_Autobrake")
        {
            int target = Math.Max(0, Math.Min(6, (int)Math.Round(value)));
            int current = (int)Math.Round(simConnect.GetCachedVariableValue("HS787_Autobrake") ?? 0);
            // Latch the TARGET VALUE (not a |target-current| step count) so ProcessSimVarUpdate
            // swallows exactly the one update that lands on it. The selector is Continuous at 1 Hz,
            // and the sim applies a multi-detent jump within a single frame, so MSFSBA observes only
            // ONE change (straight to target) — a step count would be decremented just once and the
            // leftover (count-1) would then silently swallow that many LATER, legitimate selector
            // callouts (e.g. autobrake disarming on the rollout). INCREASE/DECREASE clamp at the
            // ends, so the selector always reaches target even if the cached `current` is stale.
            if (target != current)
            {
                _autobrakeSuppressTarget = target;
                _autobrakeSuppressTicks = Environment.TickCount64;
                string ev = target > current ? "INCREASE_AUTOBRAKE_CONTROL" : "DECREASE_AUTOBRAKE_CONTROL";
                for (int i = 0; i < Math.Abs(target - current); i++) simConnect.SendEvent(ev);
            }
            return true;
        }

        // IRS knob — LVar: 0=Off, 1=On. Aligned LVars are read-only (written by WT Boeing IRS system).
        if (varKey == "HS787_IRS_Knob1")
        {
            simConnect.SetLVar("B787_IRS_Knob_State:1", value);
            return true;
        }
        if (varKey == "HS787_IRS_Knob2")
        {
            simConnect.SetLVar("B787_IRS_Knob_State:2", value);
            return true;
        }

        // Anti-ice — WT Boeing LVars, 3-state: 0=Off, 1=Auto, 2=On
        if (varKey == "HS787_AntiIceEng1")
        {
            simConnect.SetLVar("B787_Engine_AntiIce_Knob_State:1", value);
            return true;
        }
        if (varKey == "HS787_AntiIceEng2")
        {
            simConnect.SetLVar("B787_Engine_AntiIce_Knob_State:2", value);
            return true;
        }
        if (varKey == "HS787_AntiIceWing")
        {
            simConnect.SetLVar("B787_Wing_AntiIce_Knob_State", value);
            return true;
        }

        // No smoking sign — direct LVar set
        if (varKey == "HS787_NoSmoking")
        {
            simConnect.SetLVar("XMLVAR_NO_SMOKING_MODE", value);
            return true;
        }

        // Parking brake — toggle if target differs from current state
        if (varKey == "HS787_ParkBrake")
        {
            double? current = simConnect.GetCachedVariableValue("HS787_ParkBrake");
            if (current == null || (int)current.Value != (int)value)
                simConnect.SendEvent("PARKING_BRAKES");
            return true;
        }

        // Flaps — K:FLAPS_SET is silently ignored on HS787 (WT Boeing intercepts).
        // Walk to the target detent using FLAPS_INCR / FLAPS_DECR. Each event moves
        // by one detent. We fire enough events to reach the desired index. RPN loop
        // would be cleaner but ExecuteCalculatorCode caps string length, so we
        // generate a flat sequence and let SimConnect queue them.
        if (varKey == "HS787_FlapsHandle")
        {
            int target = (int)value;
            double? cur = simConnect.GetCachedVariableValue("HS787_FlapsHandle");
            int from = cur.HasValue ? (int)cur.Value : 0;
            int delta = target - from;
            string evt = delta > 0 ? "FLAPS_INCR" : "FLAPS_DECR";
            int steps = System.Math.Abs(delta);
            for (int i = 0; i < steps; i++)
                simConnect.SendEvent(evt);
            return true;
        }

        // Pitot heat — TOGGLE if state differs.
        if (varKey == "HS787_PitotHeat")
        {
            simConnect.ExecuteCalculatorCode($"(A:PITOT HEAT,Bool) {(int)value} != if{{ (>K:PITOT_HEAT_TOGGLE) }}");
            return true;
        }

        // Yaw damper — YAW_DAMPER_SET takes 0/1 directly.
        if (varKey == "HS787_YawDamper")
        {
            simConnect.SendEvent("YAW_DAMPER_SET", (uint)(int)value);
            return true;
        }

        // Antiskid — TOGGLE if state differs.
        if (varKey == "HS787_AntiSkid")
        {
            simConnect.ExecuteCalculatorCode($"(A:ANTISKID BRAKES ACTIVE,Bool) {(int)value} != if{{ (>K:ANTISKID_BRAKES_TOGGLE) }}");
            return true;
        }

        // Avionics master — TOGGLE if state differs.
        if (varKey == "HS787_AvionicsMaster")
        {
            simConnect.ExecuteCalculatorCode($"(A:AVIONICS MASTER SWITCH,Bool) {(int)value} != if{{ (>K:TOGGLE_AVIONICS_MASTER) }}");
            return true;
        }

        // ===== Radio: COM standby SET (textbox value in MHz) =====
        // Intercept here so MainForm's generic path doesn't ALSO announce
        // "Standby frequency set to 121.500" (duplicate with our ProcessSimVarUpdate
        // announce of "COM1 standby 121.500" when the SimVar changes a frame later).
        if (varKey == "COM_STANDBY_FREQUENCY_SET:1" || varKey == "COM_STANDBY_FREQUENCY_SET:2")
        {
            if (value < 118.0 || value > 136.975)
            {
                announcer.Announce("Invalid COM frequency. Range: 118.000 to 136.975 MHz.");
                return true;
            }
            uint hz = (uint)System.Math.Round(value * 1_000_000.0);
            string evt = varKey.EndsWith(":2") ? "COM2_STBY_RADIO_SET_HZ" : "COM_STBY_RADIO_SET_HZ";
            simConnect.SendEvent(evt, hz);
            return true; // SimVar change -> ProcessSimVarUpdate fires the spoken announce
        }

        // ===== Radio: COM swap buttons (no "swap pressed" — value-change announce fires post-swap) =====
        if (varKey == "COM1_RADIO_SWAP")
        {
            simConnect.SendEvent("COM_STBY_RADIO_SWAP");
            return true;
        }
        if (varKey == "COM2_RADIO_SWAP")
        {
            simConnect.SendEvent("COM2_RADIO_SWAP");
            return true;
        }

        // TRANSPONDER_CODE_SET stays with MainForm's generic path (it doesn't
        // announce there per the existing comment in MainForm — the announce
        // fires from our ProcessSimVarUpdate handler when the SimVar changes).

        // Gear — GEAR_SET: 0=up, 1=down
        if (varKey == "HS787_GearHandle")
        {
            simConnect.SendEvent("GEAR_SET", (uint)(int)value);
            return true;
        }

        // Master battery switches — toggle via calc code only if target differs from current.
        // TOGGLE_MASTER_BATTERY takes a 1-based battery index parameter.
        if (varKey == "HS787_BatSwitch1")
        {
            simConnect.ExecuteCalculatorCode($"(A:ELECTRICAL MASTER BATTERY:1,Bool) {(int)value} != if{{ 1 (>K:TOGGLE_MASTER_BATTERY) }}");
            return true;
        }
        if (varKey == "HS787_BatSwitch2")
        {
            simConnect.ExecuteCalculatorCode($"(A:ELECTRICAL MASTER BATTERY:2,Bool) {(int)value} != if{{ 2 (>K:TOGGLE_MASTER_BATTERY) }}");
            return true;
        }

        // Engine-driven hydraulic pump switches — toggle via calc code only if target differs.
        // HYDRAULIC_SWITCH_TOGGLE takes a 1-based engine index parameter.
        if (varKey == "HS787_HydEngL")
        {
            simConnect.ExecuteCalculatorCode($"(A:HYDRAULIC SWITCH:1,Bool) {(int)value} != if{{ 1 (>K:HYDRAULIC_SWITCH_TOGGLE) }}");
            return true;
        }
        if (varKey == "HS787_HydEngR")
        {
            simConnect.ExecuteCalculatorCode($"(A:HYDRAULIC SWITCH:2,Bool) {(int)value} != if{{ 2 (>K:HYDRAULIC_SWITCH_TOGGLE) }}");
            return true;
        }

        // Generators — toggle if target differs (TOGGLE_MASTER_ALTERNATOR:N not standard; use calc code)
        if (varKey == "HS787_Gen1")
        {
            simConnect.ExecuteCalculatorCode($"(A:GENERAL ENG GENERATOR SWITCH:1,Bool) {(int)value} != if{{ (>K:TOGGLE_MASTER_ALTERNATOR) }}");
            return true;
        }
        if (varKey == "HS787_Gen2")
        {
            simConnect.ExecuteCalculatorCode($"(A:GENERAL ENG GENERATOR SWITCH:2,Bool) {(int)value} != if{{ (>K:TOGGLE_MASTER_ALTERNATOR2) }}");
            return true;
        }

        // Lights — use SET events for beacon/strobe/nav/landing/taxi; toggle approach for logo/wing
        if (varKey == "HS787_LightBeacon")
        {
            simConnect.SendEvent("BEACON_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightStrobe")
        {
            // STROBE_LIGHTS_SET is a no-op on this Asobo-template lighting; the stock STROBES_SET
            // drives LIGHT STROBE (live-verified 0->1), same as the A380 ext-lighting fix.
            simConnect.SendEvent("STROBES_SET", value > 0.5 ? 1u : 0u);
            return true;
        }
        if (varKey == "HS787_LightNav")
        {
            simConnect.SendEvent("NAV_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightLanding")
        {
            simConnect.SendEvent("LANDING_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightTaxi")
        {
            simConnect.SendEvent("TAXI_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightLogo")
        {
            // Guard on LIGHT LOGO ON (immediate), not LIGHT LOGO (lags a frame) — a rapid set-after-set
            // could otherwise mis-decide off the stale value and double-toggle.
            simConnect.ExecuteCalculatorCode($"(A:LIGHT LOGO ON,Bool) {(int)value} != if{{ (>K:TOGGLE_LOGO_LIGHTS) }}");
            return true;
        }
        if (varKey == "HS787_LightWing")
        {
            simConnect.ExecuteCalculatorCode($"(A:LIGHT WING,Bool) {(int)value} != if{{ (>K:TOGGLE_WING_LIGHTS) }}");
            return true;
        }

        // Doors — toggled via K:TOGGLE_AIRCRAFT_EXIT with 1-based index. Internally this
        // operates on EXIT OPEN:N-1 (zero-based) which is what HS787_Door_* now reads.
        // Passenger: 1=1L, 2=1R, 3=2L, 4=2R, 5=3L, 6=3R, 7=4L, 8=4R  Cargo: 9=Fwd, 10=Aft
        int? doorIdx = varKey switch
        {
            "HS787_Door_1L"       => 1,
            "HS787_Door_1R"       => 2,
            "HS787_Door_2L"       => 3,
            "HS787_Door_2R"       => 4,
            "HS787_Door_3L"       => 5,
            "HS787_Door_3R"       => 6,
            "HS787_Door_4L"       => 7,
            "HS787_Door_4R"       => 8,
            "HS787_Door_FwdCargo" => 9,
            "HS787_Door_AftCargo" => 10,
            _                     => null
        };
        if (doorIdx.HasValue)
        {
            simConnect.SendEvent("TOGGLE_AIRCRAFT_EXIT", (uint)doorIdx.Value);
            return true;
        }

        // Interactive points (refuel cable @ 11, GPU cable @ 12) — these are NOT exits.
        // K:TOGGLE_AIRCRAFT_EXIT silently ignores parameters > 10. K:2:SET_INTERACTIVE_POINT
        // works for CONNECT (0→100) but not for DISCONNECT in our testing — direct SimVar
        // write `<percent> (>A:INTERACTIVE POINT OPEN:N, percent)` works in both directions
        // (the cockpit animates the value from current to target over ~1 second).
        int? interactivePointIdx = varKey switch
        {
            "HS787_RefuelDoor" => 11,
            "HS787_GPUPipe"    => 12,
            _                  => null
        };
        if (interactivePointIdx.HasValue)
        {
            int percent = (int)value > 0 ? 100 : 0;
            simConnect.ExecuteCalculatorCode($"{percent} (>A:INTERACTIVE POINT OPEN:{interactivePointIdx.Value}, percent)");
            return true;
        }

        // ===== Fuel pumps — FUELSYSTEM_PUMP_TOGGLE is param-indexed. WT/Asobo 787
        //       has an unusual order: 1=CtrL, 2=CtrR, 3=LAft, 4=LFwd, 5=RAft,
        //       6=RFwd, 7=APU. Verified empirically. TOGGLE flips state, so we
        //       check the current SimVar value and only fire if a flip is needed.
        int? fuelPumpIdx = varKey switch
        {
            "HS787_FuelPump_CtrL" => 1,
            "HS787_FuelPump_CtrR" => 2,
            "HS787_FuelPump_LAft" => 3,
            "HS787_FuelPump_LFwd" => 4,
            "HS787_FuelPump_RAft" => 5,
            "HS787_FuelPump_RFwd" => 6,
            "HS787_FuelPump_APU"  => 7,
            _                     => null
        };
        if (fuelPumpIdx.HasValue)
        {
            double? current = simConnect.GetCachedVariableValue(varKey);
            if (current == null || (int)current.Value != (int)value)
                simConnect.SendEvent("FUELSYSTEM_PUMP_TOGGLE", (uint)fuelPumpIdx.Value);
            return true;
        }

        // ===== Bleed valves =====
        if (varKey == "HS787_BleedEng1")
        {
            simConnect.ExecuteCalculatorCode($"(A:BLEED AIR ENGINE:1,Bool) {(int)value} != if{{ 1 (>K:TOGGLE_BLEED_AIR_SOURCE) }}");
            return true;
        }
        if (varKey == "HS787_BleedEng2")
        {
            simConnect.ExecuteCalculatorCode($"(A:BLEED AIR ENGINE:2,Bool) {(int)value} != if{{ 2 (>K:TOGGLE_BLEED_AIR_SOURCE) }}");
            return true;
        }
        if (varKey == "HS787_BleedAPU")
        {
            simConnect.ExecuteCalculatorCode($"(A:APU BLEED AIR SWITCH,Bool) {(int)value} != if{{ (>K:APU_BLEED_AIR_SOURCE_TOGGLE) }}");
            return true;
        }
        if (varKey == "HS787_BleedIso")
        {
            simConnect.SetLVar("XMLVAR_Bleed_Air_Isolation", value);
            return true;
        }

        // ===== Fuel crossfeed valves — L-var direct write =====
        // Crossfeed fallback for non-WT models — directly write the mirror L-var.
        if (varKey == "HS787_FuelXfeed") { simConnect.SetLVar("XMLVAR_Fuel_XFEED_Fwd", value); return true; }

        // ===== Fire/OVHT Test — momentary push: write 1, auto-release to 0 after 4 s =====
        // Click engages the test; the cockpit runs its fire-test sequence and lights up
        // the appropriate indicators for the duration. Auto-releasing the L-var after
        // ~4 s lets ProcessSimVarUpdate fire both "in progress" and "complete" announces
        // without the user having to manually release. Note: the test ANIMATION/AUDIO is
        // the WT cockpit's responsibility — if no chime fires in your sim, the WT B787
        // model just doesn't implement that audio (we can verify the L-var is set, but
        // we can't synthesize a cockpit sound from MSFSBA).
        if (varKey == "HS787_FireTest")
        {
            simConnect.SetLVar("XMLVAR_Fire_Test_Pushed", 1);
            var releaseTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            releaseTimer.Tick += (_, __) =>
            {
                releaseTimer.Stop();
                releaseTimer.Dispose();
                simConnect.SetLVar("XMLVAR_Fire_Test_Pushed", 0);
            };
            releaseTimer.Start();
            return true;
        }

        // ===== Engine + APU fire handles (pulled = armed for discharge) =====
        if (varKey == "HS787_EngFireHandle1") { simConnect.SetLVar("XMLVAR_Eng_Fire_Pulled_1", value); return true; }
        if (varKey == "HS787_EngFireHandle2") { simConnect.SetLVar("XMLVAR_Eng_Fire_Pulled_2", value); return true; }
        if (varKey == "HS787_APUFireHandle")  { simConnect.SetLVar("XMLVAR_APU_Fire_Pulled",   value); return true; }

        // ===== Cargo Fire =====
        if (varKey == "HS787_CargoFireFwd")   { simConnect.SetLVar("XMLVAR_Cargo_Fire_Arm_Fwd",   value); return true; }
        if (varKey == "HS787_CargoFireAft")   { simConnect.SetLVar("XMLVAR_Cargo_Fire_Arm_Aft",   value); return true; }
        if (varKey == "HS787_CargoFireDisch") { simConnect.SetLVar("XMLVAR_Cargo_Fire_Discharge", value); return true; }

        // ===== Standby Power Selector =====
        if (varKey == "HS787_StandbyPower")
        {
            simConnect.SetLVar("XMLVAR_StandbyPower", value);
            return true;
        }

        // ===== Baro STD pushbutton =====
        // Momentary push: toggle STD ↔ QNH. MainForm's button-click path always passes
        // value=1, so we compute the new state ourselves by inverting the current cached
        // state. Going to STD: write the L-var flag AND fire K:BAROMETRIC_STD_PRESSURE
        // (jumps the altimeter to 29.92). Going to QNH: just clear the L-var flag; the
        // WT cockpit's panel logic restores the previously-set baro value when the flag
        // drops to 0. K event is NOT fired on QNH return — it only sets 29.92, never QNH.
        if (varKey == "HS787_BaroSTD")
        {
            double? cur = simConnect.GetCachedVariableValue("HS787_BaroSTD");
            int newState = (cur.HasValue && (int)cur.Value == 1) ? 0 : 1;
            simConnect.SetLVar("XMLVAR_Baro_Selector_STD_1", newState);
            if (newState == 1)
                simConnect.SendEvent("BAROMETRIC_STD_PRESSURE");
            return true;
        }

        // ===== Master Caution / Master Warning reset buttons =====
        // Momentary push: fire the standard MSFS acknowledge K event. Driving the
        // L:Generic_Master_*_Active L-var to 0 happens via the K event's internal
        // path — we don't have to write the L-var ourselves.
        if (varKey == "HS787_MasterCautionReset")
        {
            simConnect.SendEvent("MASTER_CAUTION_ACKNOWLEDGE");
            return true;
        }
        if (varKey == "HS787_MasterWarningReset")
        {
            simConnect.SendEvent("MASTER_WARNING_ACKNOWLEDGE");
            return true;
        }

        // ===== Engine fuel control switches (RUN / CUTOFF) =====
        // MIXTUREn_RICH = RUN (value 1), MIXTUREn_LEAN = CUTOFF (value 0).
        if (varKey == "HS787_FuelControl1")
        {
            simConnect.SendEvent((int)value == 1 ? "MIXTURE1_RICH" : "MIXTURE1_LEAN");
            return true;
        }
        if (varKey == "HS787_FuelControl2")
        {
            simConnect.SendEvent((int)value == 1 ? "MIXTURE2_RICH" : "MIXTURE2_LEAN");
            return true;
        }

        return false;
    }

    // =========================================================================
    // Hotkey Handling
    // =========================================================================

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm,
        HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // ------------------------------------------------------------------
            // MCP Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadHeading:
            {
                double? hdg = simConnect.GetCachedVariableValue("HS787_MCP_Heading");
                if (hdg == null)
                {
                    announcer.AnnounceImmediate("Heading not available");
                    return true;
                }
                bool lnavOn = (simConnect.GetCachedVariableValue("HS787_LNAV") ?? 0) > 0;
                bool hdgHold = (simConnect.GetCachedVariableValue("HS787_HDGHold") ?? 0) > 0;
                bool trkMode = (simConnect.GetCachedVariableValue("HS787_TRKMode") ?? 0) > 0;
                string mode = lnavOn ? "LNAV" : hdgHold ? "HDG Hold" : trkMode ? "TRK" : "HDG";
                announcer.AnnounceImmediate($"{(trkMode ? "Track" : "Heading")} {(int)hdg.Value}, {mode}");
                return true;
            }

            case HotkeyAction.ReadSpeed:
            {
                bool isMach = (simConnect.GetCachedVariableValue("HS787_MCP_IsMach") ?? 0) > 0;
                bool spdManual = (simConnect.GetCachedVariableValue("HS787_MCP_SpdManual") ?? 0) > 0;
                bool flchOn = (simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0) > 0;

                if (!spdManual)
                {
                    string mode = flchOn ? "FLCH" : "FMC speed";
                    announcer.AnnounceImmediate($"Speed managed by {mode}");
                    return true;
                }

                if (isMach)
                {
                    double? mach = simConnect.GetCachedVariableValue("HS787_MCP_Mach");
                    string machStr = mach != null ? $"Mach {mach.Value:0.00}" : "Mach unavailable";
                    string mode = flchOn ? " FLCH" : "";
                    announcer.AnnounceImmediate($"{machStr}{mode}");
                }
                else
                {
                    double? ias = simConnect.GetCachedVariableValue("HS787_MCP_IAS");
                    string iasStr = ias != null ? $"{(int)ias.Value} knots" : "Speed unavailable";
                    string mode = flchOn ? " FLCH" : "";
                    announcer.AnnounceImmediate($"{iasStr}{mode}");
                }
                return true;
            }

            case HotkeyAction.ReadAltitude:
            {
                double? alt = simConnect.GetCachedVariableValue("HS787_MCP_Altitude");
                if (alt == null)
                {
                    announcer.AnnounceImmediate("Altitude not available");
                    return true;
                }
                bool vnavOn = (simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0) > 0;
                bool altHold = (simConnect.GetCachedVariableValue("HS787_ALTHold") ?? 0) > 0;
                bool flchOn = (simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0) > 0;
                string mode = vnavOn ? " VNAV" : altHold ? " ALT Hold" : flchOn ? " FLCH" : "";
                announcer.AnnounceImmediate($"{(int)alt.Value} feet{mode}");
                return true;
            }

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                bool isFPA = (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0;
                bool vsActive = (simConnect.GetCachedVariableValue("HS787_VS_Active") ?? 0) > 0;
                bool appOn = (simConnect.GetCachedVariableValue("HS787_APP") ?? 0) > 0;

                if (appOn)
                {
                    bool gsActive  = (simConnect.GetCachedVariableValue("HS787_GS_Active") ?? 0) > 0;
                    bool locActive = (simConnect.GetCachedVariableValue("HS787_LOC")       ?? 0) > 0;
                    string phase   = gsActive  ? "Glideslope active"
                                   : locActive ? "Localizer active"
                                   : "Approach armed";
                    announcer.AnnounceImmediate(phase);
                    return true;
                }

                if (!vsActive && !isFPA)
                {
                    announcer.AnnounceImmediate("V/S not engaged");
                    return true;
                }

                if (isFPA)
                {
                    double? fpa = simConnect.GetCachedVariableValue("HS787_MCP_FPA");
                    if (fpa != null)
                        announcer.AnnounceImmediate($"FPA {fpa.Value:+0.0;-0.0} degrees");
                    else
                        announcer.AnnounceImmediate("FPA not available");
                }
                else
                {
                    double? vs = simConnect.GetCachedVariableValue("HS787_MCP_VS");
                    if (vs != null)
                        announcer.AnnounceImmediate($"V/S {(int)vs.Value} feet per minute");
                    else
                        announcer.AnnounceImmediate("V/S not available");
                }
                return true;
            }

            // ------------------------------------------------------------------
            // MCP Set Dialogs
            // ------------------------------------------------------------------

            case HotkeyAction.FCUSetHeading:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowHeadingDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetSpeed:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowSpeedDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetAltitude:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltitudeDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetVS:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowVSDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetBaro:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowBaroDialog(simConnect, announcer, parentForm);
                return true;
            }

            // ------------------------------------------------------------------
            // Aircraft State Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadFlaps:
            {
                double? idx = simConnect.GetCachedVariableValue("HS787_FlapsHandle");
                if (idx == null)
                {
                    announcer.AnnounceImmediate("Flaps not available");
                    return true;
                }
                string position = (int)idx.Value switch
                {
                    0 => "Up",
                    1 => "1",
                    2 => "5",
                    3 => "10",
                    4 => "15",
                    5 => "17",
                    6 => "18",
                    7 => "20",
                    8 => "25",
                    9 => "30",
                    _ => idx.Value.ToString("F0")
                };
                announcer.AnnounceImmediate($"Flaps {position}");
                return true;
            }

            case HotkeyAction.ReadGear:
            {
                double? gear = simConnect.GetCachedVariableValue("HS787_GearHandle");
                if (gear == null)
                {
                    announcer.AnnounceImmediate("Gear not available");
                    return true;
                }
                string gearState = gear.Value > 0.5 ? "Down" : "Up";
                announcer.AnnounceImmediate($"Gear {gearState}");
                return true;
            }

            case HotkeyAction.ReadFuelQuantity:
            {
                double? lh  = simConnect.GetCachedVariableValue("HS787_FuelLH");
                double? rh  = simConnect.GetCachedVariableValue("HS787_FuelRH");
                double? ctr = simConnect.GetCachedVariableValue("HS787_FuelCtr");
                double? wtPerGal = simConnect.GetCachedVariableValue("HS787_FuelWtPerGal");

                if (lh == null || rh == null || ctr == null || wtPerGal == null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available");
                    return true;
                }

                int lhLbs  = (int)Math.Round(lh.Value  * wtPerGal.Value);
                int rhLbs  = (int)Math.Round(rh.Value  * wtPerGal.Value);
                int ctrLbs = (int)Math.Round(ctr.Value * wtPerGal.Value);
                int total  = lhLbs + rhLbs + ctrLbs;

                announcer.AnnounceImmediate($"Left {lhLbs}, Center {ctrLbs}, Right {rhLbs}, Total {total} pounds");
                return true;
            }

            case HotkeyAction.ReadFuelInfo:
            {
                double? lh  = simConnect.GetCachedVariableValue("HS787_FuelLH");
                double? rh  = simConnect.GetCachedVariableValue("HS787_FuelRH");
                double? ctr = simConnect.GetCachedVariableValue("HS787_FuelCtr");
                double? wtPerGal = simConnect.GetCachedVariableValue("HS787_FuelWtPerGal");

                if (lh == null || rh == null || ctr == null || wtPerGal == null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available");
                    return true;
                }

                double kgPerGal = wtPerGal.Value / 2.20462;
                int lhKg  = (int)Math.Round(lh.Value  * kgPerGal);
                int rhKg  = (int)Math.Round(rh.Value  * kgPerGal);
                int ctrKg = (int)Math.Round(ctr.Value * kgPerGal);
                int total  = lhKg + rhKg + ctrKg;

                announcer.AnnounceImmediate($"Left {lhKg}, Center {ctrKg}, Right {rhKg}, Total {total} kilograms");
                return true;
            }

            case HotkeyAction.ReadAltimeter:
            {
                double? inHg = simConnect.GetCachedVariableValue("HS787_Altimeter");
                if (inHg == null)
                {
                    announcer.AnnounceImmediate("Altimeter not available");
                    return true;
                }
                if (Math.Abs(inHg.Value - 29.92) < 0.005)
                {
                    announcer.AnnounceImmediate("Altimeter standard");
                    return true;
                }
                int hpa = (int)Math.Round(inHg.Value * 33.8639);
                announcer.AnnounceImmediate($"Altimeter: {hpa}, {inHg.Value:0.00}");
                return true;
            }

            case HotkeyAction.ReadDistanceToDest:
            {
                double? meters = simConnect.GetCachedVariableValue("HS787_DistDest");
                double gs = simConnect.GetCachedVariableValue("HS787_GroundSpeed") ?? 0;
                var parts = new System.Collections.Generic.List<string>();

                if (meters != null && meters.Value > 0)
                {
                    double nm = meters.Value / 1852.0;
                    string ete = FormatEte(nm, gs);
                    parts.Add(ete.Length > 0
                        ? $"Next waypoint, {(int)nm} miles, {ete}"
                        : $"Next waypoint, {(int)nm} miles");
                }

                double? eteSec = simConnect.GetCachedVariableValue("HS787_EteDest");
                if (eteSec != null && eteSec.Value > 0 && gs >= 30)
                {
                    double destNm = (eteSec.Value / 3600.0) * gs;
                    string destEte = FormatEteSeconds(eteSec.Value);
                    parts.Add($"Destination, {(int)destNm} miles, {destEte}");
                }

                announcer.AnnounceImmediate(parts.Count > 0
                    ? string.Join("; ", parts)
                    : "Distance to destination not available");
                return true;
            }

            case HotkeyAction.ReadDistanceToTOD:
            {
                double? todMeters = simConnect.GetCachedVariableValue("HS787_DistTOD");
                if (todMeters == null || todMeters.Value <= 0)
                {
                    announcer.AnnounceImmediate("Top of descent not available");
                    return true;
                }
                double todNm = todMeters.Value / 1852.0;
                double gs = simConnect.GetCachedVariableValue("HS787_GroundSpeed") ?? 0;
                string ete = FormatEte(todNm, gs);
                announcer.AnnounceImmediate(ete.Length > 0
                    ? $"{(int)todNm} miles to top of descent, {ete}"
                    : $"{(int)todNm} miles to top of descent");
                return true;
            }

            case HotkeyAction.ReadWaypointInfo:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;
            }

            case HotkeyAction.ReadGrossWeightKg:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;
            }

            // EICAS (Alt+E) reads back every active Crew-Alerting-System message (warnings /
            // cautions / advisories) from the always-on CAS monitor (which also AUTO-announces each
            // new alert as it posts). The monitor owns the MFD_1 Coherent view.
            case HotkeyAction.ReadDisplayUpperECAM:        // Alt+E — EICAS crew alerts
                (parentForm as MainForm)?.AnnounceHs787CasAlerts();
                return true;

            // The lower-MFD system synoptic (HYD / ELEC / FUEL / AIR / APU / OXYGEN / status) scrapes
            // cleanly as text over the Coherent debugger (MFD_2) — open a live read-out window. No key.
            case HotkeyAction.ReadDisplayLowerECAM:        // Alt+S — system synoptic
                ShowHs787Display("787 System Synoptic Display", "HSB789_MFD_2", announcer, hotkeyManager);
                return true;

            // The ND / PFD / standby are positional (a flat scrape returns scale ticks), so read them
            // with the AI-vision path (needs a Gemini key + the Ctrl+2 cockpit view). Their data is
            // also on the SimVar read-outs (B / Shift+S / Shift+H / A / Q, D / Shift+D, waypoint info).
            // The CAS monitor owns MFD_1 and the IRS reader owns the PFD view, so neither is scraped.
            case HotkeyAction.ReadDisplayND:               // Alt+N
                ReadDisplay(Services.GeminiService.DisplayType.ND, "Navigation Display", announcer, parentForm);
                return true;

            case HotkeyAction.ReadDisplayPFD:              // Alt+P
                ReadDisplay(Services.GeminiService.DisplayType.PFD, "PFD", announcer, parentForm);
                return true;

            case HotkeyAction.ReadDisplayISIS:             // Alt+I — standby instrument
                ReadDisplay(Services.GeminiService.DisplayType.ISIS, "Standby Instrument", announcer, parentForm);
                return true;

            // FMC keyboard not available in Phase 1 (requires JS bridge)
            // MainForm will handle ShowFenixMCDU for other aircraft; return false here
            case HotkeyAction.ShowFenixMCDU:
                return false;

            // Ctrl+M — per-aircraft monitor manager (mute/unmute the auto-announced vars).
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowHS787MonitorManagerDialog();
                return true;

            case HotkeyAction.FCUSetAutopilot:
            {
                hotkeyManager.ExitInputHotkeyMode();
                if (!simConnect.IsConnected)
                {
                    announcer.AnnounceImmediate("Not connected to simulator.");
                    return true;
                }

                if (_autopilotWindow != null && !_autopilotWindow.IsDisposed)
                {
                    _autopilotWindow.ShowForm();
                    return true;
                }

                _autopilotWindow = new Forms.HS787.HS787AutopilotWindow(simConnect, announcer);
                _autopilotWindow.FormClosed += (_, _) => _autopilotWindow = null;
                _autopilotWindow.ShowForm();
                return true;
            }

            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }
}
