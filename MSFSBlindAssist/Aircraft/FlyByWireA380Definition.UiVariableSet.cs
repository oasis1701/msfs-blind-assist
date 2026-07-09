using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA380Definition
{
    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Crew SEAT toggle button (up/down/fwd/aft per pilot): press to start moving that way,
        // press again to stop (+ speak the position); opposite direction reverses. value>0.5 is the
        // press edge (RenderAsButton click sends 1); ignore anything else.
        if (_seatButtonMap.TryGetValue(varKey, out var seatBtn) && value > 0.5)
        {
            ToggleSeatMotor(seatBtn.PosVar, seatBtn.Dir, simConnect, announcer);
            return true;
        }
        // "Signal Cabin Ready" — A32NX_CABIN_READY can't be written directly (FWS-owned,
        // verified). FwsCore sets it to 1 while a CALLS pushbutton is pressed, so pulse
        // CALLS ALL (1→0). The read-only A32NX_CABIN_READY Mon then auto-announces
        // "Cabin Ready: Ready" once the FWS flips it. Handled here (not via the generic
        // _momentaryButtons pulse) so the real CALLS var is pulsed, not the synthetic key.
        if (varKey == "A380X_MSFSBA_SIGNAL_CABIN_READY")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_OVHD_CALLS_ALL)");
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(250); simConnect.ExecuteCalculatorCode("0 (>L:PUSH_OVHD_CALLS_ALL)"); } catch { }
                });
                announcer.Announce("Cabin ready signalled");
            }
            return true;
        }
        // Speed-brake FINE slider — a 0-16383 SPOILERS *axis*, not an L:var position.
        // MUST run BEFORE the generic RenderAsSlider branch below, which ramps the
        // synthetic L:var (that nothing in the sim reads) and clamps to the slider's
        // position range — i.e. the slider would do nothing to the aircraft.
        if (varKey == "A380X_MSFSBA_SPEEDBRAKE_SLIDER")
        {
            int sbAxis = Math.Max(0, Math.Min(16383, (int)Math.Round(value)));
            simConnect.ExecuteCalculatorCode($"{sbAxis} (>K:SPOILERS_SET)");
            return true;
        }
        // Continuous-axis SLIDERS (cockpit seats, armrests, sunshades, forward visors)
        // are FBW L:vars. Don't SNAP them to the target in one write — the 3-D
        // model jumps there and you only hear a single "tick" of the motor. A real motorised
        // seat moves gradually while you hold the switch, so we RAMP the L:var toward the
        // target a few units per 40 ms (calc path, on the UI thread). The FBW then plays the
        // sustained motor sound + smooth animation. (Writing via the calculator path also
        // avoids SetLVar's data-def write, which is unreliable for FBW L:vars.)
        if (varDef.RenderAsSlider)
        {
            RampSliderTo(varDef.Name, value, simConnect, varDef.SliderMin, varDef.SliderMax);
            return true;
        }
        // FCU SPD/MACH toggle from a panel button: the legacy dotted event is inert on the A380's
        // new FCU — switch via the stock K-events instead (see SpdMachToggleRpn). Then re-read.
        if (varKey == "A32NX.FCU_SPD_MACH_TOGGLE_PUSH")
        {
            simConnect.ExecuteCalculatorCode(SpdMachToggleRpn);
            RequestFCUSpeedWithStatus(simConnect);
            return true;
        }
        // Fire Test / Cargo Smoke Test (HOLD on/off tests). Setting ON triggers the fire
        // MASTER WARNING + the continuous repetitive chime (CRC) aural. Writing the var 0
        // ends the test, but the CRC can keep sounding until the master warning is
        // acknowledged — so on TEST OFF, also pulse the (correctly-spelled) MASTERAWARN
        // acknowledge to guarantee the "beep beep beep" cancels. Write via the calc path.
        if (varKey == "A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED" || varKey == "A32NX_FIRE_TEST_CARGO")
        {
            int on = value > 0.5 ? 1 : 0;
            simConnect.ExecuteCalculatorCode($"{on} (>L:{varKey})");
            if (on == 0)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERAWARN_L)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERAWARN_L)");
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERAWARN_R)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERAWARN_R)");
            }
            // No explicit announce here: the screen reader reads the combo change, and
            // the Continuous+IsAnnounced monitor speaks the state change THROUGH the
            // Ctrl+M-gated path (MainForm.OnSimVarUpdated). The old announcer.Announce
            // bypassed that mute — the reported "doesn't respect global suppression" bug.
            return true;
        }
        // System Display PAGE combo: drive the SD to the chosen page, then scrape that
        // page's decoded content off the real SD view INTO the panel "Status display"
        // box (no separate window). The combo's own value change announces the page
        // NAME; the CONTENT populates the box silently and updates on every page switch
        // — NO auto-speech of the content, no manual refresh.
        if (varKey == "A32NX_ECAM_SD_CURRENT_PAGE_INDEX")
        {
            int idx = (int)Math.Round(value);
            // 16 = our synthetic "Upper E/WD" option — scrape the E/WD view instead of an
            // SD page. Still record the combo value (so the box header reads "Upper E/WD"
            // and the selection persists); the real SD view ignores the out-of-range index.
            // UNIQUE-prefix the write ("{seq} 0 *" pushes 0, discarded): re-selecting a page
            // you already visited (e.g. ELEC -> HYD -> ELEC) sends an IDENTICAL calc string,
            // which MobiFlight de-duplicates -> the write never re-fires and the real SD page
            // doesn't switch back (the scraped C/B / STATUS / VIDEO pages then show stale text).
            simConnect.ExecuteCalculatorCode($"{++_sdWriteSeq} 0 * {idx} (>L:{varKey})");
            RefreshSdPageDisplayAsync(simConnect, idx, ewd: idx == 16);
            return true;
        }
        // Annunciator / integral lights knob (Test / Bright / Dim) is handled by the
        // generic catch-all below: it writes the L:var and the combo's Continuous +
        // IsAnnounced monitoring speaks the position ("Test" / "Bright" / "Dim").
        // We deliberately do NOT synthesise a spoken list of lights for the TEST
        // position: the bulb test is render-only in the FBW model (live-verified —
        // setting the knob to TEST changes NO _PB_HAS_FAULT or annunciator L:var), so
        // there is nothing real to announce. The actual annunciator/fault lights are
        // the per-system _PB_HAS_FAULT vars (already registered, announce-on-change),
        // which speak when a genuine fault appears — MSFSBA announces real state, it
        // does not fabricate a bulb-check narration.
        // Chronometer start/stop + reset fire H-EVENTS (the FBW Clock listens for the
        // hEvent, not an L:var write — live-verified: the H-event advances the elapsed
        // time, an L:var write does nothing).
        if (varKey == "A32NX_CHRONO_TOGGLE" || varKey == "A32NX_CHRONO_RST")
        {
            if (value > 0.5)   // only the "Activate" option fires
            {
                simConnect.ExecuteCalculatorCode($"(>H:{varKey})");
                announcer.Announce(varKey == "A32NX_CHRONO_RST" ? "Chronometer reset" : "Chronometer start stop");
            }
            return true;
        }
        // Momentary L:var push-buttons (TEST / ident / ack / trim reset / tiller /
        // rain repellent): pulse the L:var 1→0 so the sim registers the press edge
        // rather than leaving it latched on. ~250 ms is long enough for the FWS /
        // systems to act on the rising edge, then it auto-releases.
        if (_momentaryButtons.Contains(varKey))
        {
            // Combo now (Off / Activate): only the "Activate" option fires; choosing
            // "Off" does nothing (the pulse already returned the var to 0).
            if (value > 0.5)
            {
                PulseMomentaryLVar(simConnect, announcer, varKey, varDef.DisplayName);
            }
            return true;
        }
        if (_extLightSetEvents.TryGetValue(varKey, out var lightEvent))
        {
            simConnect.SendEvent(lightEvent, (uint)Math.Round(value));
            return true;
        }
        // Rudder Trim Reset: fire the stock K-event the cockpit uses (the L:var does
        // nothing). Only the "Reset" option (value 1) fires.
        if (varKey == "A32NX_RUDDER_TRIM_RESET")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("(>K:RUDDER_TRIM_RESET)");
                announcer.Announce("Rudder trim reset");
            }
            return true;
        }
        // Nosewheel-steering PEDAL DISCONNECT. The FBW A380 systems READ the public L:var
        // A32NX_TILLER_PEDAL_DISCONNECT directly every frame (hydraulic/mod.rs:4664) and cut
        // pedal-commanded nose-wheel steering while it is 1 (mod.rs:4567). So WRITE the L:var
        // (live-verified it latches) — the old TOGGLE_WATER_RUDDER fire was wrong (the A380
        // has no water rudder; that event is ignored). It's a HELD toggle (On = disconnected
        // for the rudder check, Off = reconnected), so honour both 0 and 1.
        if (varKey == "A32NX_TILLER_PEDAL_DISCONNECT")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:A32NX_TILLER_PEDAL_DISCONNECT)");
            return true;
        }
        // Flaps lever: the handle index is a computed output; the stock FLAPS_SET
        // event (axis value 0-16383) drives the FBW handle. Map detent 0-4 to the
        // axis value (index/4 * 16383) — live-verified each detent lands correctly.
        if (varKey == "A32NX_FLAPS_HANDLE_INDEX")
        {
            int detent = Math.Max(0, Math.Min(4, (int)Math.Round(value)));
            int axis = (int)Math.Round(detent / 4.0 * 16383.0);
            simConnect.ExecuteCalculatorCode($"{axis} (>K:FLAPS_SET)");
            return true;
        }
        // Speed brake: synthetic Retracted/Half/Full combo -> stock SPOILERS_SET
        // (0 / 8192 / 16383), mirroring the flaps lever. (Speculative — stock event.)
        if (varKey == "A380X_MSFSBA_SPEEDBRAKE")
        {
            int pos = Math.Max(0, Math.Min(2, (int)Math.Round(value)));
            int[] axis = { 0, 8192, 16383 };
            simConnect.ExecuteCalculatorCode($"{axis[pos]} (>K:SPOILERS_SET)");
            return true;
        }
        // Ground-spoiler arm: synthetic Disarm/Arm combo -> SPOILERS_ARM_OFF / _ON.
        if (varKey == "A380X_MSFSBA_SPOILERS_ARM")
        {
            simConnect.ExecuteCalculatorCode(value > 0.5 ? "(>K:SPOILERS_ARM_ON)" : "(>K:SPOILERS_ARM_OFF)");
            return true;
        }
        // ENG GEN 1-4: combo state is the stock GENERAL ENG MASTER ALTERNATOR:n; the
        // working actuator is the stock TOGGLE_MASTER_ALTERNATOR event (engine index).
        // Toggle only when the desired state differs from the live SimVar state.
        if (varKey.StartsWith("ELEC_ENG_GEN:", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan("ELEC_ENG_GEN:".Length), out int genN))
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("TOGGLE_MASTER_ALTERNATOR", (uint)genN);
            return true;
        }
        // APU GEN 1-2: combo state is the stock APU GENERATOR SWITCH:i; the working
        // actuator is the stock indexed APU_GENERATOR_SWITCH_SET event (direct set).
        if (varKey.StartsWith("ELEC_APU_GEN:", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan("ELEC_APU_GEN:".Length), out int apuGenI))
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>K:{apuGenI}:APU_GENERATOR_SWITCH_SET)");
            return true;
        }
        // Taxi light: state mirrors LIGHT TAXI:2; the only working actuator is the
        // stock TOGGLE_TAXI_LIGHTS (no indexed SET reaches the shipping model), so
        // toggle only when the desired state differs from the live state.
        if (varKey == "LIGHT_TAXI_OVHD")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue("LIGHT_TAXI_OVHD") ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("TOGGLE_TAXI_LIGHTS");
            return true;
        }
        // Seat-belt sign: there is no SET event, only CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE,
        // so toggle only when the desired state differs from the current (live) state.
        if (varKey == "SEATBELT_SIGN")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue("SEATBELT_SIGN") ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE");
            return true;
        }
        // Anti-skid: TOGGLE-only event (K:ANTISKID_BRAKES_TOGGLE flips the switch). The
        // stock A:ANTISKID BRAKES ACTIVE state reads UNRELIABLY via the data-def path on
        // the A380 (live-verified: same batch returned 1 AND 0), so the cached "current"
        // got stuck at On and "select On" never fired the toggle (the user's bug). Track
        // the commanded state ourselves: the toggle reliably flips it, so after each set we
        // KNOW the result. Seed to the A380's power-on default (anti-skid ON) rather than the
        // flaky cache — a bad first read could otherwise fire a spurious toggle on the user's
        // first "select On"; thereafter drive off _antiskidOn.
        if (varKey == "ANTISKID_BRAKES_ACTIVE")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = _antiskidOn ?? true;
            if (desiredOn != currentOn) simConnect.SendEvent("ANTISKID_BRAKES_TOGGLE");
            _antiskidOn = desiredOn;
            return true;
        }
        // --- Combos whose STATE is a SimVar but whose CONTROL is a K-event
        // (standardised: every cockpit control is a combo box, no buttons except
        // the FCU push/pull). These route here from the SimVar-combo set path. ---
        // Engine MASTER valves: state = FUELSYSTEM VALVE SWITCH:n (1-4), control =
        // FUELSYSTEM_VALVE_OPEN/CLOSE with the valve id (verified live).
        if (varKey.StartsWith("ENG_VALVE_SWITCH:", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan("ENG_VALVE_SWITCH:".Length), out int engVid))
        {
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", (uint)engVid);
            return true;
        }
        // Crossfeed valves: XFEED_n_STATE -> valve id 45+n.
        if (varKey.StartsWith("XFEED_", StringComparison.Ordinal)
            && varKey.EndsWith("_STATE", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan(6, 1), out int xfn))
        {
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", (uint)(45 + xfn));
            return true;
        }
        // (Baro preselect QNH was removed — the FBW var is display-only and not settable.)
        // (Doors + ground-service action buttons were removed from the panels — jet bridge,
        // stairs, fuel/baggage/catering and all door open/close are done on the flyPad now.
        // Their write handlers are gone with them; the ground STATE still auto-announces.)
        // Momentary ACTION combos: fire only when the action option (value 1) is
        // chosen; the idle option (0) does nothing.
        if (varKey == "XPNDR_IDENT_ON") { if (value > 0.5) simConnect.SendEvent("XPNDR_IDENT_ON"); return true; }
        // Air-cond/cargo target temperature: user enters degrees C; the FBW
        // selector knob is a 0-300 sweep over the zone's range (cockpit/cabin
        // 18-30 C, cargo 5-25 C) plus the per-knob Offset, so
        // knob = (temp - lo) / (hi - lo) * 300 + Offset (cabin Offset = 50).
        if (_tempSelectors.TryGetValue(varKey, out var ts))
        {
            if (value < ts.Lo || value > ts.Hi)
            {
                announcer.AnnounceImmediate($"Temperature must be between {ts.Lo} and {ts.Hi} degrees Celsius.");
                return true;
            }
            // Invariant fixed-point: raw interpolation used CurrentCulture — a fractional
            // temperature on a comma-decimal locale emitted "87,5 (>L:...)", broken RPN.
            simConnect.ExecuteCalculatorCode(
                ((value - ts.Lo) / (ts.Hi - ts.Lo) * 300.0 + ts.Offset).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + $" (>L:{ts.Knob})");
            announcer.Announce($"{ts.Label} temperature set to {value:0} degrees");
            return true;
        }
        // Manual pressurization knobs — pass-through position write (calc path).
        if (varKey == "PRESS_MAN_ALT_SET" || varKey == "PRESS_MAN_VS_SET")
        {
            string knob = varKey == "PRESS_MAN_ALT_SET" ? "A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB" : "A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB";
            simConnect.ExecuteCalculatorCode($"{value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} (>L:{knob})");
            announcer.Announce($"Set to {value:0.0}");
            return true;
        }
        // Cabin lighting (passenger-cabin brightness). Calc-path write (the reliable FBW
        // L:var write). The brightness box also forces Auto-Brightness OFF so the manual
        // value actually takes effect; the auto combo is a plain 0/1 write.
        if (varKey == "CABIN_BRIGHTNESS_SET")
        {
            int b = (int)Math.Max(0, Math.Min(100, Math.Round(value)));
            simConnect.ExecuteCalculatorCode("0 (>L:A32NX_CABIN_USING_AUTOBRIGHTNESS)");
            simConnect.ExecuteCalculatorCode($"{b} (>L:A32NX_CABIN_MANUAL_BRIGHTNESS)");
            announcer.Announce($"Cabin brightness {b} percent");
            return true;
        }
        if (varKey == "A32NX_CABIN_USING_AUTOBRIGHTNESS")
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:A32NX_CABIN_USING_AUTOBRIGHTNESS)");
            return true;   // combo announces its own Off/On
        }
        // Thrust-lever detent combos -> THROTTLEn_AXIS_SET_EX1 with the detent's
        // axis value (-1..1 scaled to +-16384). Values are the FBW default-style
        // detent calibration (Reverse -1.0 / Rev Idle -0.70 / Idle -0.44 /
        // Climb -0.10 / Flex-MCT 0.53 / TOGA 1.0); the throttle mapping snaps the
        // lever to the detent. Assumes default throttle calibration.
        // NOTE (2026-06-12): a live-mapping in-RPN variant was tried and REVERTED
        // at the user's request — it broke detent announcements in their setup
        // (see the A32NX handler note / commit 34a97a2a for the variant).
        if (varKey == "THROTTLE_ALL_DETENT" || (varKey.StartsWith("THROTTLE_") && varKey.EndsWith("_DETENT")))
        {
            int idx = (int)Math.Round(value);
            double[] detentAxis = { -1.0, -0.70, -0.44, -0.10, 0.53, 1.0 };
            string[] names = { "Reverse", "Reverse Idle", "Idle", "Climb", "Flex M C T", "TOGA" };
            if (idx < 0 || idx >= detentAxis.Length) return true;
            uint ex1 = unchecked((uint)(int)Math.Round(detentAxis[idx] * 16384));
            if (varKey == "THROTTLE_ALL_DETENT")
            {
                for (int n = 1; n <= 4; n++) simConnect.SendEvent($"THROTTLE{n}_AXIS_SET_EX1", ex1);
                announcer.Announce($"All thrust levers {names[idx]}");
            }
            else
            {
                int eng = varKey.Length > 9 && char.IsDigit(varKey[9]) ? varKey[9] - '0' : 1;
                simConnect.SendEvent($"THROTTLE{eng}_AXIS_SET_EX1", ex1);
                announcer.Announce($"Thrust lever {eng} {names[idx]}");
            }
            return true;
        }
        if (varKey == "ENGINE_MODE_SELECTOR")
        {
            uint mode = (uint)Math.Round(value);
            // Drive the real ignition state on ALL FOUR engines via the MobiFlight CALC/gauge
            // path — NOT SendEvent. SendEvent (TransmitClientEvent) only actuated SET1/SET2;
            // SimConnect's MapClientEventToSimEvent does NOT resolve TURBINE_IGNITION_SWITCH_
            // SET3/SET4, so the two outboard engines never got IGN and the FADEC left them in
            // SHUTTING (motoring to ~25% N2 with no fuel — the "engines 3/4 spin but never
            // light" bug). Live-verified: the K: gauge event sets ign3/ign4 = 2 and the FADEC
            // then lights them, whereas SendEvent SET3/SET4 silently no-op'd.
            for (int n = 1; n <= 4; n++) simConnect.ExecuteCalculatorCode($"{mode} (>K:TURBINE_IGNITION_SWITCH_SET{n})");
            // Also nudge the knob-position L:var the FWS/EWD reads, so the cockpit
            // display matches (the events above don't touch it).
            simConnect.ExecuteCalculatorCode($"{mode} (>L:XMLVAR_ENG_MODE_SEL)");
            return true;
        }
        // Wipers: ON/OFF by TOGGLING the electrical circuit (the FBW knob template's
        // mechanism) — only toggle when the desired state differs from the live
        // circuit-switch state, then drive a visible speed when turning on. The old
        // code set power only and never toggled the circuit on, so it never started.
        if (varKey == "WIPER_LEFT" || varKey == "WIPER_RIGHT")
        {
            int circuit = varKey == "WIPER_LEFT" ? 141 : 143;
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? 0.0) > 0.5;
            if (desiredOn != currentOn)
                simConnect.ExecuteCalculatorCode($"{circuit} (>K:ELECTRICAL_CIRCUIT_TOGGLE)");
            if (desiredOn)   // percent then circuit index (verified order)
                simConnect.ExecuteCalculatorCode($"100 {circuit} (>K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET)");
            return true;
        }
        // Engine anti-ice combo "ENGn_ANTI_ICE" -> stock K:ANTI_ICE_SET_ENGn
        // (the SimVar / XMLVAR can't be written directly on the A380).
        if (varKey.Length == 13 && varKey.StartsWith("ENG", StringComparison.Ordinal)
            && varKey.EndsWith("_ANTI_ICE", StringComparison.Ordinal)
            && varKey[3] >= '1' && varKey[3] <= '4')
        {
            simConnect.SendEvent($"ANTI_ICE_SET_ENG{varKey[3]}", (uint)Math.Round(value));
            return true;
        }
        // TRK/FPA reference: the A380X has NO toggle event for it — the cockpit
        // button writes the L:var directly. Write the absolute 0/1 via the
        // MobiFlight calculator path (FBW L:var writes over the SimConnect data-def
        // are as unreliable as the reads), not the default SetLVar.
        if (varKey == "A32NX_TRK_FPA_MODE_ACTIVE")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:A32NX_TRK_FPA_MODE_ACTIVE)");
            return true;
        }
        // EFIS baro STD/QNH — LIVE-VERIFIED 2026-06-11 against the installed dev
        // build's fcu.js (MsfsBaroManager): H:A380X_EFIS_CP_BARO_PUSH_{n} = STD
        // (onPush sets Std) and PULL_{n} = QNH (onPull leaves Std) — the OPPOSITE of
        // the A32NX's A32NX.FCU_EFIS_*_BARO_PULL=STD knob events; do NOT "harmonise"
        // the two jets. Fired UNCONDITIONALLY: both are idempotent directional mode
        // sets on this build, so no toggle-if-differs guard (a stale readback would
        // wedge it — the original "combo bounces back to QNH" bug). Index 1=Capt, 2=F/O.
        if (varKey == "A32NX_FCU_LEFT_EIS_BARO_IS_STD" || varKey == "A32NX_FCU_RIGHT_EIS_BARO_IS_STD")
        {
            int side = varKey.Contains("LEFT") ? 1 : 2;
            simConnect.SendEvent($"H:A380X_EFIS_CP_BARO_{(value > 0.5 ? "PUSH" : "PULL")}_{side}", 0);
            return true;
        }
        // Set QNH: the entered value is in the side's current unit (hPa or inHg).
        // Convert to hPa, validate, then fire K:KOHLSMAN_SET with millibars*16
        // (verified live). KOHLSMAN_SET moves both altimeters together.
        if (varKey == "CAPT_QNH_SET" || varKey == "FO_QNH_SET")
        {
            bool inHg = (varKey == "CAPT_QNH_SET" ? _baroInHgL : _baroInHgR) == true;
            double hpa = inHg ? value * 33.8639 : value;
            if (hpa < 900 || hpa > 1100)
            {
                announcer.AnnounceImmediate(inHg
                    ? "QNH must be between 26.6 and 32.5 inches."
                    : "QNH must be between 900 and 1100 hectopascals.");
                return true;
            }
            simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(hpa * 16.0));
            announcer.Announce(inHg
                ? $"Altimeter set {hpa / 33.8639:0.00} inches"
                : $"Altimeter set {hpa:0} hectopascals");
            return true;
        }
        // EFIS Control Panel controls are ALL direct L:var writes on the A380X
        // (no events — confirmed from efis-cp.xml: ND mode/range, navaid 1/2, the
        // LS/VV/CSTR/ARPT/TRAF option buttons, the WPT/VOR/NDB filter + WX/TERR
        // overlay, OANS range, and the hPa/inHg baro-unit selector). The cockpit
        // buttons run RPN that writes the L:var; the SimConnect data-def write is
        // unreliable for FBW L:vars (same as the reads), so route every one of
        // them through the MobiFlight calculator path to guarantee they actuate.
        if (varKey.StartsWith("A32NX_EFIS_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_EFIS_", StringComparison.Ordinal)
            || varKey.StartsWith("XMLVAR_Baro_Selector_HPA_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
        }
        // Flight Director 1 / 2 (CORRECTED 2026-06): state is the stock
        // AUTOPILOT FLIGHT DIRECTOR ACTIVE:n; the working actuator is the cockpit FD
        // button's event K:TOGGLE_FLIGHT_DIRECTOR with the SIDE as the parameter
        // (1 = Capt FD, 2 = F/O FD — live-verified per-side). Toggle only when the
        // desired state differs from the live SimVar. The old _FD_ACTIVE L:var was DEAD.
        if (varKey == "FD_1_CTL" || varKey == "FD_2_CTL")
        {
            uint side = varKey == "FD_1_CTL" ? 1u : 2u;
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("TOGGLE_FLIGHT_DIRECTOR", side);
            return true;
        }
        // Wing anti-ice (CORRECTED 2026-06): the A380's real control is the stock
        // STRUCTURAL DEICE SWITCH, actuated by TOGGLE_STRUCTURAL_DEICE (toggle only when
        // the desired state differs from the live SimVar — the absolute STRUCTURAL_DEICE_SET
        // is a no-op on this build). The old A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED
        // L:var write was DEAD (read by nothing on the A380X — live-verified). Same
        // toggle-if-differs pattern as ENG GEN / taxi light.
        if (varKey == "WING_ANTI_ICE_OVHD")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue("WING_ANTI_ICE_OVHD") ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("TOGGLE_STRUCTURAL_DEICE");
            return true;
        }
        // Probe/window heat: A32NX_MAN_PITOT_HEAT is the var the cockpit button toggles
        // (verified live #56). It auto-forces ON whenever AC2 is powered or an engine is
        // running, so a "set Off" reverts — real A380 behaviour (probe heat is automatic);
        // the Mon auto-announce re-reads the true state. Routed via the calculator path.
        if (varKey == "A32NX_MAN_PITOT_HEAT")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:{varKey})");
            return true;
        }
        // FCU engage/mode toggle combos: the backing L:var is read-only state, so
        // a "set" fires the matching toggle event — but only when the picked state
        // differs from the current one (the events toggle, they don't set an
        // absolute value). Current state comes from the live monitor cache.
        if (_fcuToggleEvents.TryGetValue(varKey, out var fcuEvt))
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (_fcuStateCache.TryGetValue(varKey, out var cur) ? cur : 0) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent(fcuEvt);
            return true; // never SetLVar the read-only state var
        }
        // Catch-all for the remaining settable FBW overhead / system pushbutton +
        // selector L:vars (the OnOff/OffAuto/Press/Sel combos for ELEC, FUEL, HYD,
        // PNEU, COND, PRESS, VENT, anti-ice, lighting). MainForm's generic fallback
        // would SetLVar these over the SimConnect data-def, which is unreliable for
        // FBW L:vars (same as the reads) — so route them through the MobiFlight
        // calculator path, which is the established reliable write for this aircraft.
        //
        // #103 CORRECTION (live Coherent SimVar write-then-readback, 2026-05): the
        // earlier #60 verdict that PACK/HOT-AIR, ENGINE BLEED, CABIN/AIR-EXTRACT
        // FANS, the HYD ENGINE/ELEC PUMP PBs, ELEC BUS-TIE/GALLEY, HYD PTU and the
        // EMERGENCY-EXIT sign are "computed outputs that revert and cannot be set
        // externally" was WRONG. It was an artifact of testing with the MCP's native
        // data-def write (set_lvar / AddToDataDefinition), which is unreliable for
        // FBW L:vars (exactly as the READS are). The MobiFlight CALCULATOR path
        // (`{val} (>L:{var})`) — the one used below — sets ALL of them and they
        // STICK in both directions for 3+ s (re-tested live: pack OFF stayed OFF,
        // bus-tie/PTU/pumps/bleeds/fans all stuck). The Rust `OnOffFaultPushButton`
        // READS `{name}_PB_IS_ON/_IS_AUTO` as the pilot input each frame (it only
        // WRITES the *_HAS_FAULT output), so an external set IS the press. So these
        // overhead combos all actuate correctly through the calculator path — there
        // is NO hard FBW limitation here. (The only PBs that still need a stock event
        // are the seatbelt sign — handled earlier via CABIN_SEATBELTS_ALERT_SWITCH_
        // TOGGLE — and any engine anti-ice, which uses ANTI_ICE_SET_ENGn.)
        // Feed-tank fuel pumps: toggle the pump's electrical circuit only when the
        // desired state differs from the live circuit (the event is a TOGGLE).
        if (_fuelPumpCircuits.TryGetValue(varKey, out int pumpCircuit))
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn)
                simConnect.ExecuteCalculatorCode($"{pumpCircuit} 1 (>K:2:ELECTRICAL_BUS_TO_CIRCUIT_CONNECTION_TOGGLE)");
            return true;
        }
        if (varKey.StartsWith("A32NX_OVHD_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_OVHD_", StringComparison.Ordinal)
            || varKey.StartsWith("A32NX_KNOB_OVHD_", StringComparison.Ordinal)
            // The overhead sign/increment XMLVARs (No Smoking, Emergency Exit,
            // Altitude Increment) also set reliably via the calculator path — they
            // were falling through to the unreliable data-def write before, which is
            // why the Emergency Exit sign appeared to "revert". (Verified live: the
            // EMEREXIT XMLVAR stuck via the calculator path.)
            || varKey.StartsWith("XMLVAR_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
        }
        // General catch-all for every remaining writable FBW L:var combo whose KEY is
        // the L:var itself (e.g. A32NX_TRANSPONDER_MODE, A32NX_SWITCH_ATC_ALT, ND mode/
        // range, ISIS, EFIS filters). Route through the reliable MobiFlight calculator
        // path rather than the base data-def SetLVar. Event-driven controls (engine
        // masters, FCU toggles, seat-belt, lights, …) are all handled in cases above,
        // so anything reaching here is a direct-write L:var. ARINC429/readout vars are
        // never settable, so they never get here.
        // Prefix-less FBW L:vars (cockpit sliding windows + sunshades): their KEY is the
        // L:var but they lack the A32NX_/A380X_/FBW_ prefix the catch-all below keys on, so
        // route them through the calculator path explicitly.
        if (varKey == "CPT_SLIDING_WINDOW" || varKey == "FO_SLIDING_WINDOW"
            || varKey == "SUNSHADE_CPT_OPENING" || varKey == "SUNSHADE_FO_OPENING"
            || varKey == "CPT_OXY_FWD_OPENING" || varKey == "AFT_OXY_OPENING"
            || varKey == "A380_CPT_TABLE" || varKey == "A380_FO_TABLE"
            || varKey == "A380_CPT_FOOTREST" || varKey == "A380_FO_FOOTREST"
            || varKey == "A380_LGPIN_DOOR"
            // MSFS-2024-native-rebuild openables (interactive-parts.xml bool toggles).
            || varKey == "A380_CPT_MEALTABLE" || varKey == "A380_FO_MEALTABLE"
            || varKey == "A380_CPT_KEYBOARD" || varKey == "A380_FO_KEYBOARD"
            || varKey == "CAS_LH_OPENING" || varKey == "CAS_RH_OPENING"
            || varKey == "AFT_OIT_OPENING" || varKey == "COCKPITDOOR_OPEN"
            || varKey == "BIGARMREST_CPT_STOW" || varKey == "BIGARMREST_FO_STOW"
            || varKey == "SMALLARMREST_CPT_STOW" || varKey == "SMALLARMREST_FO_STOW")
        {
            // Write the RAW value (not rounded) so 0..1 slider positions (sliding windows, side
            // sunshades) carry their fraction; the 0/1 combo items still write 0.0/1.0 fine.
            simConnect.ExecuteCalculatorCode($"{value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} (>L:{varKey})");
            return true;
        }
        // (The RMP keypad panel was removed — the RMP is now the dedicated accessible window,
        // FBWA380RmpForm, which calls SendRmpKey / SendRmpKeypad directly.)
        if (varKey.StartsWith("A32NX_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_", StringComparison.Ordinal)
            || varKey.StartsWith("FBW_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
        }
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // Apply a settable UI variable through the A380's existing HandleUIVariableSet
    // routing, looking up its registered definition (so callers without a panel
    // varDef can reuse the proven set paths). Used by the FCU Baro window for the
    // CAPT_QNH_SET / *_EIS_BARO_IS_STD / XMLVAR_Baro_Selector routes.
    public bool ApplyUIVariable(string varKey, double value, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        SimVarDefinition def = GetVariables().TryGetValue(varKey, out var d)
            ? d : new SimVarDefinition { Name = varKey, DisplayName = varKey };
        return HandleUIVariableSet(varKey, value, def, s, a);
    }
}
