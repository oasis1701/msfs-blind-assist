using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class FlyByWireA380Definition
{
    public override bool HandleHotkeyAction(
        HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // D / Shift+D — FMS flight progress (distance to destination / top of
            // descent). The A380 has no PMDG-style SDK struct and no stock SimVar for
            // these; they come from the MFD's FMS guidance controller, read live via
            // the Coherent debugger. Delegate to MainForm, which owns that client.
            case HotkeyAction.ReadDistanceToDest:
                if (parentForm is MainForm mfDest) { mfDest.AnnounceA380FlightInfo(false); return true; }
                return false;
            case HotkeyAction.ReadDistanceToTOD:
                if (parentForm is MainForm mfTod) { mfTod.AnnounceA380FlightInfo(true); return true; }
                return false;

            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA380.FBWA380HeadingWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA380.FBWA380SpeedWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA380.FBWA380AltitudeWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA380.FBWA380VSWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA380.FBWA380AutopilotWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            // FCU knob push/pull (Shift+1..4 push, Ctrl+1..4 pull). Drive the FCU via
            // the legacy cockpit H-events `A320_Neo_FCU_<axis>_PUSH/PULL` — the same
            // path the physical knob uses. The A380X FCU is a self-contained instrument
            // whose managers listen to these H-events (verified live: firing
            // A320_Neo_FCU_HDG_PUSH/PULL and _ALT_PUSH/PULL on the FCU bus moves the
            // autopilot slot index 1<->2; SPEED/VS names confirmed from the FBW FCU
            // source). The previously-fired `A32NX.FCU_TO_AP_*` events were the FCU's
            // *internal* downstream events and do NOT route to the autopilot via
            // TransmitClientEvent — a live probe confirmed A32NX.FCU_TO_AP_HDG_PUSH left
            // the slot unchanged while the H-event moved it. Fired via the calculator
            // (>H:) path (same as the clock CHR H-event and other cockpit controls).
            //
            // NO readback here (Fenix-style): the managed<->selected RESULT is announced
            // by the always-on managed-state monitor (Mon "…_MANAGED…" -> "Heading Mode:
            // Managed/Selected"), which fires only on a REAL transition. The old
            // RequestFCU*WithStatus readback spoke the value on every press regardless of
            // whether anything changed, which was identical to the output-mode read query
            // and masked the dead actuation.
            // CRITICAL: the A380's NEW FCU consumes K-events (K:A32NX.FCU_*), NOT the dotted
            // A320 H-events — firing the H-event does NOTHING (this is why one push of the ALT
            // knob "did nothing" and you had to push again: the first push was the dead H-event).
            // Route through FireFCUButton, exactly like the FCU window Push/Pull buttons, so a
            // single push actually fires. readback:false keeps the actuation SILENT (Fenix-style)
            // — only the managed-state monitor speaks, and only on a real Managed<->Selected
            // transition. (The FCU value-entry windows still pass readback:true.)
            case HotkeyAction.FCUHeadingPush: FireFCUButton("A32NX.FCU_TO_AP_HDG_PUSH", simConnect, announcer, readback: false); return true;
            case HotkeyAction.FCUHeadingPull: FireFCUButton("A32NX.FCU_TO_AP_HDG_PULL", simConnect, announcer, readback: false); return true;
            case HotkeyAction.FCUSpeedPush: FireFCUButton("A32NX.FCU_SPD_PUSH", simConnect, announcer, readback: false); return true;
            case HotkeyAction.FCUSpeedPull: FireFCUButton("A32NX.FCU_SPD_PULL", simConnect, announcer, readback: false); return true;
            case HotkeyAction.FCUAltitudePush: FireFCUButton("A32NX.FCU_ALT_PUSH", simConnect, announcer, readback: false); return true;
            case HotkeyAction.FCUAltitudePull: FireFCUButton("A32NX.FCU_ALT_PULL", simConnect, announcer, readback: false); return true;
            // The A380X V/S knob is pull-to-engage (managed vertical is armed via the ALT knob),
            // so VS push is a no-op on the jet; fire the K-event anyway for consistency.
            case HotkeyAction.FCUVSPush: FireFCUButton("A32NX.FCU_VS_PUSH", simConnect, announcer, readback: false); return true;
            case HotkeyAction.FCUVSPull: FireFCUButton("A32NX.FCU_TO_AP_VS_PULL", simConnect, announcer, readback: false); return true;

            case HotkeyAction.ReadFlaps:
            {
                // Announce straight from the live cache — the handle index is a monitored
                // combo, so a forced request of an UNCHANGED value never re-fires
                // ProcessSimVarUpdate and the read stayed silent (the "] L does nothing"
                // bug). Fall back to a request only if it isn't cached yet.
                double? fv = simConnect.GetCachedVariableValue("A32NX_FLAPS_HANDLE_INDEX");
                if (fv.HasValue)
                {
                    string[] detents = { "Up", "1", "2", "3", "Full" };
                    int i = (int)Math.Round(fv.Value);
                    announcer.AnnounceImmediate("Flaps " + (i >= 0 && i < detents.Length ? detents[i] : fv.Value.ToString()));
                }
                else if (simConnect.IsConnected) { _reqFlaps = true; simConnect.RequestVariable("A32NX_FLAPS_HANDLE_INDEX", forceUpdate: true); }
                return true;
            }
            case HotkeyAction.ReadGear:
            {
                double? gv = simConnect.GetCachedVariableValue("A32NX_GEAR_HANDLE_POSITION");
                if (gv.HasValue) announcer.AnnounceImmediate(gv.Value > 0.5 ? "Gear down" : "Gear up");
                else if (simConnect.IsConnected) { _reqGear = true; simConnect.RequestVariable("A32NX_GEAR_HANDLE_POSITION", forceUpdate: true); }
                return true;
            }
            // On-demand readouts ported from the A320 (vars shared / already defined).
            case HotkeyAction.ReadSpeedVLS: RequestReadout(simConnect, "A32NX_SPEEDS_VLS", "V L S", "knots"); return true;
            case HotkeyAction.ReadSpeedF: RequestReadout(simConnect, "A32NX_SPEEDS_F", "F speed", "knots"); return true;
            case HotkeyAction.ReadSpeedGD: RequestReadout(simConnect, "A32NX_SPEEDS_GD", "Green Dot speed", "knots"); return true;
            case HotkeyAction.ReadSpeedS: RequestReadout(simConnect, "A32NX_SPEEDS_S", "S speed", "knots"); return true;
            case HotkeyAction.ReadSpeedVS: RequestReadout(simConnect, "A32NX_SPEEDS_VS", "V S", "knots"); return true;
            case HotkeyAction.ReadSpeedVFE: RequestReadout(simConnect, "A32NX_SPEEDS_VFEN", "V F E next", "knots"); return true;
            // Fuel + gross weight are spoken fleet-consistently (matching PMDG / Fenix):
            // plain key = pounds, Shift key = kilograms, via the shared SimConnectManager
            // requests (identical phrasing across all aircraft). Deterministic units —
            // NOT the EFB-following _metricWeight path, so they never surprise the pilot.
            case HotkeyAction.ReadFuelQuantity: // F -> "Fuel on board N pounds"
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY, "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY"); return true;
            // Phase 4 parity with the A320: ReadFuelInfo (same as ReadFuelQuantity) + a
            // Ctrl+B "Set Altimeter" dialog (the A380 baro uses the stock KOHLSMAN_SET,
            // unit = millibars*16, NOT the A320's A32NX.FCU_EFIS_*_BARO_SET events).
            case HotkeyAction.ReadFuelInfo: // Shift+F -> "Fuel on board N kilograms"
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG, "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY_KG"); return true;
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA380.FBWA380BaroWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
            case HotkeyAction.ReadApproachCapability:
            {
                // A32NX_APPROACH_CAPABILITY doesn't exist in FBW — decode the FCDC FG
                // word 4 (same source as the PFD_AUTOLAND display + the PFD FMA).
                var w4 = simConnect.GetCachedVariableValue("PFD_AUTOLAND");
                string cap = "not available";
                if (w4.HasValue)
                {
                    var w = new SimConnect.Arinc429Word(w4.Value);
                    cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none computed"
                        : w.BitValueOr(25, false) ? "LAND 3 dual"
                        : w.BitValueOr(24, false) ? "LAND 3 single"
                        : w.BitValueOr(23, false) ? "LAND 2" : "none computed";
                }
                announcer.AnnounceImmediate($"Approach capability {cap}");
                return true;
            }
            // Dedicated display WINDOWS were removed for the FBW aircraft: the SD reads
            // via the ECAM Control Panel page combo + status box, the E/WD has its own
            // status box (Displays > E/WD panel), and PFD/ND/ISIS flight values stay on
            // the individual readout hotkeys (ReadAltimeter/ReadSpeed/... — kept). The
            // ShowPFD/ShowNavigationDisplay/ShowECAM/ShowStatusPage hotkeys were
            // retired app-wide (enum entries + registrations deleted — no aircraft ever
            // handled them besides the deleted A32NX windows). The shared ReadDisplay*
            // actions remain (used by PMDG/Fenix) and fall through to no-op on the FBW
            // jets. Alt+E still speaks the current E/WD lines.
            // Alt+E now opens the E/WD as a pop-out WINDOW (auto-refreshing, F5 to
            // refresh, Escape to close) showing the whole E/WD — engine parameters plus
            // the live ECAM memo / warning lines — instead of speaking it once. The old
            // one-shot spoken read (ReadAllEwdWarnings) was removed as dead code once
            // the window fully replaced it.
            case HotkeyAction.ReadDisplayUpperECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(
                    () => new Forms.FbwEwdWindow("A380 E/WD — Engine / Warning Display",
                        () => BuildEwdWindowTextAsync(simConnect), announcer),
                    w => { w.Show(); w.BringToFront(); w.Activate(); });
                return true;
            // W repurposed to gross weight in pounds (matches PMDG / Fenix, which also
            // repurpose the waypoint key). The MCDU/MFD covers waypoint data.
            case HotkeyAction.ReadWaypointInfo: // W -> "Gross weight N pounds, center of gravity X% MAC"
                announcer.AnnounceImmediate(_gwKgCache > 0
                    ? $"Gross weight {_gwKgCache * 2.204625:0} pounds{CgMacPhrase()}"
                    : "Gross weight not available");
                return true;
            case HotkeyAction.ReadAltimeter:
                // Announce the captain's FBW EIS baro — STD/unit-aware, the SAME value + phrasing
                // as the live auto-announce and the set (PMDG/Fenix-style), instead of the stock
                // KOHLSMAN in inches (which ignored STD + the selected unit = the "funky" read).
                // Fenix/PMDG-style: terse, no "Captain" prefix, both units (the A380 EFIS is
                // split per side but every sibling reads ONE altimeter with no side prefix, so
                // we speak the captain side only, like the existing handler already chose).
                // Read STD + the baro value LIVE from the cache so a stale change-tracked
                // flag can never make this say "standard" while the EFIS is actually on QNH
                // (same class of fix as the flaps cache read). NOTE: above the transition
                // altitude the EFIS genuinely IS standard, so "standard" there is correct.
                double? isStdNow = simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_IS_STD");
                bool stdNow = isStdNow.HasValue ? isStdNow.Value > 0.5 : (_baroStdL == true);
                if (stdNow) { announcer.AnnounceImmediate("Altimeter standard"); return true; }
                double hpaNow = _lastBaroL;
                double? baroWord = simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_HPA");
                if (baroWord.HasValue && BaroHpa(new Arinc429Word(baroWord.Value).ValueOr(0), out double hpaDec) && hpaDec > 0) hpaNow = hpaDec;
                if (hpaNow > 0) { announcer.AnnounceImmediate($"Altimeter: {hpaNow:0}, {hpaNow / 33.8639:0.00}"); return true; }
                // Fallback (EIS baro not seeded yet): stock KOHLSMAN.
                if (simConnect.IsConnected) { _reqBaro = true; simConnect.RequestVariable("KOHLSMAN_HG", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadGrossWeightKg: // Shift+W -> "Gross weight N kilograms, center of gravity X% MAC"
                announcer.AnnounceImmediate(_gwKgCache > 0
                    ? $"Gross weight {_gwKgCache:0} kilograms{CgMacPhrase()}"
                    : "Gross weight not available");
                return true;
            case HotkeyAction.ReadHeading: RequestFCUHeadingWithStatus(simConnect); return true;
            case HotkeyAction.ReadSpeed: RequestFCUSpeedWithStatus(simConnect); return true;
            case HotkeyAction.ReadAltitude: RequestFCUAltitudeWithStatus(simConnect); return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA: RequestFCUVSWithStatus(simConnect); return true;
            // Ctrl+W (output): ND TO-waypoint name/distance/bearing via SimVars (no Coherent — see NdWaypointReadout).
            case HotkeyAction.ReadNDWaypoint: Services.NdWaypointReadout.Announce(simConnect, announcer); return true;
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                if (parentForm is MainForm mf) mf.ShowA380MonitorManagerDialog();
                return true;
            // Toggle live ECAM E/WD memo/warning call-outs (A32NX-style). The A380
            // monitors A32NX_EWD_LOWER_* lines in ProcessSimVarUpdate and mutes them
            // when the FBWA380_ECAM_MEMOS sentinel is in A380DisabledMonitorVariables;
            // this flips it (also reflected in the Ctrl+M Monitor Manager) and persists.
            case HotkeyAction.ToggleECAMMonitoring:
            {
                var disabled = Settings.SettingsManager.Current.A380DisabledMonitorVariables;
                string key = Forms.FBWA380.FBWA380MonitorManagerForm.EcamMemosKey;
                bool turnedOff;
                if (disabled.Contains(key)) { disabled.Remove(key); turnedOff = false; }
                else { disabled.Add(key); turnedOff = true; }
                Settings.SettingsManager.Save();
                announcer.AnnounceImmediate(turnedOff ? "E W D monitoring disabled" : "E W D monitoring enabled");
                return true;
            }
            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    public void RequestFCUHeadingWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqHdg = true; _pHdgVal = _pHdgMgd = null;
        s.RequestVariable("A32NX_AUTOPILOT_HEADING_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_HDG_MANAGED_DASHES", forceUpdate: true);
    }

    public void RequestFCUSpeedWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqSpd = true; _pSpdVal = _pSpdMgd = null;
        s.RequestVariable("A32NX_AUTOPILOT_SPEED_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_SPD_MANAGED_DOT", forceUpdate: true);
    }

    public void RequestFCUAltitudeWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqAlt = true; _pAltVal = _pAltMgd = null;
        s.RequestVariable("FCU_ALT_VALUE", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_ALT_MANAGED", forceUpdate: true);
    }

    public void RequestFCUVSWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqVs = true; _pVsVal = _pFpaVal = _pVsMode = null;
        // All three via the SimConnect data-def path, same as the heading/speed/alt
        // readouts that work. (VS is non-angular so it reads back unscaled — the
        // earlier "15 vs -2000" that prompted a MobiFlight read was a paused-sim
        // artifact, and routing VS through MobiFlight ReadLedVariable regressed the
        // read-out to silence: its numeric response was being consumed by the
        // shared ECAM string-read channel before the LED-value handler saw it.)
        s.RequestVariable("A32NX_TRK_FPA_MODE_ACTIVE", forceUpdate: true);
        s.RequestVariable("A32NX_AUTOPILOT_VS_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_AUTOPILOT_FPA_SELECTED", forceUpdate: true);
    }

    // Base FCU readout virtuals (rarely used path) → route to the paired readout.
    public override void RequestFCUHeading(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUHeadingWithStatus(simConnect);

    public override void RequestFCUSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUSpeedWithStatus(simConnect);

    public override void RequestFCUAltitude(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUAltitudeWithStatus(simConnect);

    public override void RequestFCUVerticalSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUVSWithStatus(simConnect);

    // Panel FCU knob push/pull buttons fire their A32NX.FCU_* event (which works),
    // but the generic panel-button path doesn't read anything back, so they were
    // silent. Speak the resulting selected/managed value here — identical to what
    // the Shift+1-4 (push) / Ctrl+1-4 (pull) hotkeys announce.
    public override void OnPanelButtonFired(string varKey, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "A32NX.FCU_TO_AP_HDG_PUSH":
            case "A32NX.FCU_TO_AP_HDG_PULL": RequestFCUHeadingWithStatus(simConnect); break;
            case "A32NX.FCU_SPD_PUSH":
            case "A32NX.FCU_SPD_PULL": RequestFCUSpeedWithStatus(simConnect); break;
            case "A32NX.FCU_ALT_PUSH":
            case "A32NX.FCU_ALT_PULL": RequestFCUAltitudeWithStatus(simConnect); break;
            case "A32NX.FCU_VS_PUSH":
            case "A32NX.FCU_TO_AP_VS_PULL": RequestFCUVSWithStatus(simConnect); break;
            // SPD/MACH toggle: re-read the speed — the read-out already says
            // "mach 0.78" vs "280 knots", so it announces the new mode.
            case "A32NX.FCU_SPD_MACH_TOGGLE_PUSH": RequestFCUSpeedWithStatus(simConnect); break;
            // HDG·V/S <-> TRK·FPA toggle: re-read heading (its label flips HDG<->TRK).
            case "A32NX.FCU_TRK_FPA_TOGGLE_PUSH": RequestFCUHeadingWithStatus(simConnect); break;
        }
    }

    // hdg: 0-360 whole degrees.
    public bool SetFCUHeadingValue(int hdg, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_HDG_SET", (uint)hdg);
        SuppressFcuValueChangeEcho();   // the explicit readback below is the single confirmation
        // Clean Fenix-style readback (NOT the racy RequestFCUHeadingWithStatus, which read the
        // cache via forceUpdate and spoke the STALE value first): announce the value we just
        // set plus the cached managed dot, once. The window's SelectAll gives the field echo.
        string hdgStatus = (s.GetCachedVariableValue("A32NX_FCU_HDG_MANAGED_DASHES") ?? 0) > 0.5 ? "managed" : "selected";
        a.AnnounceImmediate($"FCU heading {hdg:000}, {hdgStatus}");
        return true;
    }

    // internalSpeed: knots (100-399) OR Mach*100 (10-99). Caller does the *100.
    public bool SetFCUSpeedValue(int internalSpeed, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_SPD_SET", (uint)internalSpeed);
        SuppressFcuValueChangeEcho();
        // Clean Fenix-style readback (NOT the racy RequestFCUSpeedWithStatus): the value we set
        // plus the cached managed dot, once. internalSpeed < 100 is Mach*100 (e.g. 78 = 0.78).
        string spdStatus = (s.GetCachedVariableValue("A32NX_FCU_SPD_MANAGED_DOT") ?? 0) > 0.5 ? "managed" : "selected";
        if (internalSpeed < 100)
            a.AnnounceImmediate($"FCU speed mach {internalSpeed / 100.0:0.00}, {spdStatus}");
        else
            a.AnnounceImmediate($"FCU speed {internalSpeed}, {spdStatus}");
        return true;
    }

    // feet: already converted from metres by the caller if metric.
    public bool SetFCUAltitudeValue(double feet, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        uint rounded = (uint)(Math.Round(feet / 100) * 100);
        // FCU_ALT_SET snaps the target onto the current knob increment (100/1000 ft), so a
        // non-thousand altitude (e.g. 4500) would land on the 1000-grid. Force 100-ft
        // granularity FIRST — but ONLY when the target isn't already a 1000-multiple, so the
        // common FLxx0 case (e.g. 36000) never needlessly fires the increment. When we do
        // change it, suppress its "Altitude Increment: 100" side-effect auto-announce.
        if (rounded % 1000 != 0)
        {
            _altIncrAnnounceSuppressUntil = DateTime.UtcNow.AddMilliseconds(750);
            s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
            System.Threading.Thread.Sleep(50);
        }
        s.SendEvent("A32NX.FCU_ALT_SET", rounded);
        SuppressFcuValueChangeEcho();
        // Fenix-style readback: speak the FCU altitude + managed/selected state once, using the
        // value we just set (no racy cache re-read) plus the cached managed dot — mirroring the
        // "FCU altitude 36000, managed/selected" Fenix announces. The window's SelectAll
        // separately gives NVDA's "36000 selected" field echo.
        string altStatus = (s.GetCachedVariableValue("A32NX_FCU_ALT_MANAGED") ?? 0) > 0.5 ? "managed" : "selected";
        if (_metricAlt)
        {
            int m = (int)Math.Round(rounded * 0.3048);
            a.AnnounceImmediate($"FCU altitude {m} metres, {altStatus}");
        }
        else a.AnnounceImmediate($"FCU altitude {rounded}, {altStatus}");
        return true;
    }

    // value: signed V/S (-6000..6000 fpm) OR FPA (-9.9..9.9 deg). Same calc-code
    // path the old dialog used (negatives overflow SendEvent's uint).
    public bool SetFCUVSValue(double value, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        // FPA is sent ×10 per the FBW protocol (the A380 FCU's VerticalSpeedManager
        // reads A320_Neo_FCU_VS_SET_DATA and does Math.round(value)/10 in FPA mode,
        // gated on |value| < 100 — an ×100 encoding was silently IGNORED, not clamped).
        int toSend = Math.Abs(value) < 100 ? (int)Math.Round(value * 10) : (int)Math.Round(value);
        s.ExecuteCalculatorCode($"{toSend} (>K:A32NX.FCU_VS_SET)");
        SuppressFcuValueChangeEcho();
        // Consistent Fenix-style readback (V/S has no managed/selected dot, so just the value).
        if (Math.Abs(value) < 100)
            a.AnnounceImmediate($"FCU flight path angle {value:0.0}");
        else
            a.AnnounceImmediate($"FCU vertical speed {value:0}");
        return true;
    }

    // Fire a push/pull/toggle event. When readback is true (the default — used by
    // the dedicated FCU value-entry windows, where a value confirmation is wanted),
    // also speak the resulting value via OnPanelButtonFired's switch. The input-mode
    // FCU hotkey chords (Ctrl/Shift+1-4) pass readback:false so the knob actuates
    // SILENTLY and only the always-on managed-state monitor (Mon "…_MANAGED…" ->
    // "Heading/Speed/Altitude Mode: Managed/Selected", which fires only on a REAL
    // transition) speaks — the Fenix-style behaviour the user asked for. The old
    // unconditional readback spoke the full value on EVERY press, identical to the
    // output-mode read query and far too verbose for a knob nudge.
    public void FireFCUButton(string evt, SimConnectManager s, ScreenReaderAnnouncer a, bool readback = true)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return; }
        // A UI-origin knob push/pull often flips the value var (managed dashes <-> value);
        // the readout/mode-monitor owns that confirmation — mute the change announcer briefly.
        SuppressFcuValueChangeEcho();
        if (evt == "A32NX.FCU_SPD_MACH_TOGGLE_PUSH") s.ExecuteCalculatorCode(SpdMachToggleRpn);
        // The A380's NEW FCU consumes EVERY A32NX.FCU_* button as a K-EVENT, not the A320-era
        // H-event the SendEvent path produces — live-verified: (>H:A32NX.FCU_SPD_PUSH) left the
        // managed dot at 0, (>K:A32NX.FCU_SPD_PUSH) set it to 1. The Speed/Alt/Hdg/VS push-pull
        // windows already pass the correct A380 event names (incl. the TO_AP_HDG/VS variants), so
        // firing them as K-events makes all those FCU knob buttons work. (SPD/MACH stays the
        // conditional RPN above; TRK/FPA toggle goes through here too — verify it separately.)
        else if (evt.StartsWith("A32NX.FCU_", StringComparison.Ordinal)) s.ExecuteCalculatorCode($"(>K:{evt})");
        else s.SendEvent(evt);
        if (readback) OnPanelButtonFired(evt, s, a);
    }

    private void RampSliderTo(string lvar, double target, SimConnectManager simConnect,
                              double rangeMin = 0.0, double rangeMax = 100.0)
    {
        _sliderRampSim = simConnect;
        target = Math.Max(rangeMin, Math.Min(rangeMax, target));
        _sliderTarget[lvar] = target;
        // Step scales with the var's range so a 0-1 slider ramps over the same ~1.3 s
        // as a 0-100 one (fixed 3.0 snapped 0-1 sliders to the target in one tick).
        _sliderStep[lvar] = Math.Max(0.0005, (rangeMax - rangeMin) * 0.03);
        if (!_sliderCurrent.ContainsKey(lvar))
            _sliderCurrent[lvar] = simConnect.GetCachedVariableValue(lvar) ?? target;
        if (_sliderRampTimer == null)
        {
            _sliderRampTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _sliderRampTimer.Tick += (s, e) => SliderRampTick();
            _sliderRampTimer.Start();
        }
    }

    private void SliderRampTick()
    {
        var sim = _sliderRampSim;
        if (sim == null || !sim.IsConnected) { StopSliderRamp(); return; }
        foreach (var lvar in _sliderTarget.Keys.ToList())
        {
            double step = _sliderStep.TryGetValue(lvar, out var st) ? st : 3.0;
            double target = _sliderTarget[lvar];
            double cur = _sliderCurrent.TryGetValue(lvar, out var c) ? c : target;
            if (Math.Abs(target - cur) <= step) { cur = target; _sliderTarget.Remove(lvar); }
            else cur += Math.Sign(target - cur) * step;
            _sliderCurrent[lvar] = cur;
            sim.ExecuteCalculatorCode(cur.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + " (>L:" + lvar + ")");
        }
        if (_sliderTarget.Count == 0) StopSliderRamp();
    }

    private void StopSliderRamp() { _sliderRampTimer?.Stop(); _sliderRampTimer?.Dispose(); _sliderRampTimer = null; }

    /// <summary>
    /// Halt the seat-motor and slider-ramp timers immediately. Called by MainForm on
    /// aircraft swap — these UI-thread timers stop themselves only on target-reached /
    /// sim-disconnect / the 8 s cap, and sim.IsConnected stays TRUE across a swap, so
    /// a discarded def instance kept firing (>L:SEAT_...) calc writes at the NEW
    /// aircraft for up to ~8 s.
    /// </summary>
    public void StopAllMotion()
    {
        try
        {
            _seatMotorTimer?.Stop();
            _seatMotorTimer?.Dispose();
            _seatMotorTimer = null;
            _seatMotorDir.Clear();
        }
        catch { }
        try
        {
            StopSliderRamp();
            _sliderTarget.Clear();
        }
        catch { }
        // TCAS RA deferred-compose timer: a discarded def instance must not keep a
        // UI-thread timer alive (it would speak stale guidance through the captured
        // announcer at the NEW aircraft). Also drop the announcer reference.
        try
        {
            _tcasRaComposeTimer?.Stop();
            _tcasRaComposeTimer?.Dispose();
            _tcasRaComposeTimer = null;
            _tcasRaAnnouncer = null;
        }
        catch { }
        // Hotkey windows created by this def (FCU/Baro/E/WD): dispose so they don't
        // survive the swap holding this def + the E/WD refresh timer.
        try { DisposeTrackedWindows(); } catch { }
    }

    // Toggle button: press a direction -> if already moving that way, STOP (+ speak position);
    // otherwise start moving that way (reversing if it was going the other way). No combo re-read,
    // so no start/stop/start and no spurious read-out.
    private void ToggleSeatMotor(string posVar, int dir, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _seatMotorSim = simConnect;
        _seatMotorAnnouncer = announcer;
        if (_seatMotorDir.TryGetValue(posVar, out var cur) && cur == dir)
        {
            // pressing the SAME direction again -> stop this axis + say where it ended up
            _seatMotorDir.Remove(posVar);
            AnnounceSeatPosition(posVar);
            simConnect.RequestVariable(posVar, forceUpdate: true);   // refresh cache for the read-out + next seed
            if (_seatMotorDir.Count == 0) _seatMotorTimer?.Stop();
            return;
        }
        // start (or reverse) -> seed the tracked position from the live var on a fresh start
        // (movement itself reads the live value each tick, so the seed only feeds the spoken read-out)
        if (!_seatMotorDir.ContainsKey(posVar))
            _seatMotorPos[posVar] = Math.Max(0.0, Math.Min(100.0,
                simConnect.GetCachedVariableValue(posVar) ?? (_seatMotorPos.TryGetValue(posVar, out var lp) ? lp : 50.0)));
        _seatMotorDir[posVar] = dir;
        _seatMotorTicks = 0;
        if (_seatMotorTimer == null)
        {
            _seatMotorTimer = new System.Windows.Forms.Timer { Interval = 20 };
            _seatMotorTimer.Tick += (s, e) => SeatMotorTick();
        }
        if (!_seatMotorTimer.Enabled) _seatMotorTimer.Start();
    }

    private void SeatMotorTick()
    {
        var sim = _seatMotorSim;
        if (sim == null || !sim.IsConnected || _seatMotorDir.Count == 0) { _seatMotorTimer?.Stop(); return; }
        const double step = 0.4;   // ~100 units in ~5 s
        bool hitSafety = ++_seatMotorTicks > 400;   // ~8 s cap so a forgotten "moving" combo can't drive forever
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var v in _seatMotorDir.Keys.ToList())
        {
            long seq = ++_seatMotorSeq;   // makes each tick's calc string unique so MobiFlight re-fires it
            string expr = _seatMotorDir[v] > 0
                ? seq + " 0 * (L:" + v + ") " + step.ToString(inv) + " + 100 min (>L:" + v + ")"
                : seq + " 0 * (L:" + v + ") " + step.ToString(inv) + " - 0 max (>L:" + v + ")";
            sim.ExecuteCalculatorCode(expr);
            double pos = Math.Max(0.0, Math.Min(100.0, (_seatMotorPos.TryGetValue(v, out var p) ? p : 50.0) + _seatMotorDir[v] * step));
            _seatMotorPos[v] = pos;
            if (pos <= 0.01 || pos >= 99.99 || hitSafety) { _seatMotorDir.Remove(v); AnnounceSeatPosition(v); }
        }
        if (_seatMotorDir.Count == 0) _seatMotorTimer?.Stop();
    }

    // The user asked, after the seat stops, to hear WHERE it is as a position (not just a number).
    // Spoken band + percent, derived from the approximate tracked position. Queued (not Immediate)
    // so it follows NVDA's own "Stopped" combo read-out instead of cutting it off.
    private void AnnounceSeatPosition(string posVar)
    {
        var ann = _seatMotorAnnouncer;
        if (ann == null) return;
        double pos = _seatMotorPos.TryGetValue(posVar, out var p) ? p : 50.0;
        var (disp, hi, lo) = _seatMotorMeta.TryGetValue(posVar, out var m) ? m : ("Seat", "high", "low");
        ann.Announce($"{disp}: {SeatBand(pos, hi, lo)}, {(int)Math.Round(pos)} percent");
    }

    private static string SeatBand(double pos, string hi, string lo) =>
        pos >= 97 ? "fully " + hi :
        pos >= 70 ? hi :
        pos >= 56 ? "slightly " + hi :
        pos >= 44 ? "mid travel" :
        pos >= 30 ? "slightly " + lo :
        pos >= 3 ? lo :
        "fully " + lo;

    // Request the live AP/mode state vars so a window can refresh its button labels.
    public void RequestAutopilotStates(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        foreach (var v in new[] {
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE",
            "A32NX_FCU_LOC_MODE_ACTIVE", "A32NX_FCU_APPR_MODE_ACTIVE",
            "A32NX_FMA_EXPEDITE_MODE", "FD_1_CTL", "FD_2_CTL" })
            s.RequestVariable(v, forceUpdate: true);
    }

    // Toggle the FCU metric-altitude pushbutton (cockpit does !L then write-back).
    public void ToggleMetricAltitude(SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) return;
        s.ExecuteCalculatorCode($"{(_metricAlt ? 0 : 1)} (>L:A32NX_METRIC_ALT_TOGGLE)");
    }

    // Set the FCU altitude increment (100 or 1000 ft).
    public void SetAltIncrement(int inc, SimConnectManager s)
    {
        if (!s.IsConnected) return;
        s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", (uint)inc);
    }
}
