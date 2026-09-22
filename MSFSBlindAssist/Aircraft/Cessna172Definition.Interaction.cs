// MSFSBlindAssist/Aircraft/Cessna172Definition.Interaction.cs
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.C172;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

public partial class Cessna172Definition
{
    // The manager, captured on the first panel write or hotkey so the engine-start machine can
    // return the key to BOTH from the batch hook (which receives no manager). The MD-11's
    // Attach(simConnect) pattern; nulled by nothing — a switch disposes the definition.
    private SimConnectManager? _sim;

    private readonly Cessna172EngineStart _engineStart = new();

    // The definition's own combo-pick echo window for the magneto position callout, which
    // MainForm's _uiSetEcho cannot cover: it keys on the var the combo SET (C172_MAGNETOS) and
    // the callout is composed from three OTHER vars (the A380 ND-filter precedent).
    private long _magnetoPickTicks = long.MinValue;
    private const int MagnetoEchoMs = 3000;
    private bool MagnetoEchoActive => Environment.TickCount64 - _magnetoPickTicks < MagnetoEchoMs;

    private static uint OnOff(double value) => value > 0.5 ? 1u : 0u;

    /// <summary>
    /// Two-state switch with only a TOGGLE event: fire only when the pick differs from the live
    /// value, treat an unknown value as differing, and force-read the key when nothing is sent so
    /// the combo snaps back (A380ToggleCommand's contract).
    /// </summary>
    private static void ToggleIfDiffers(SimConnectManager sim, string varKey, double desired, string toggleEvent, uint param = 0)
    {
        if (A380ToggleCommand.ShouldFire(desired, sim.GetCachedVariableValue(varKey)))
            sim.SendEvent(toggleEvent, param);
        else
            sim.RequestVariable(varKey, forceUpdate: true);
    }

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _sim = simConnect;
        switch (varKey)
        {
            // ---- SET-event switches: the pick is the parameter ----
            case "C172_BEACON":        simConnect.SendEvent("BEACON_LIGHTS_SET", OnOff(value)); return true;
            case "C172_NAV_LIGHTS":    simConnect.SendEvent("NAV_LIGHTS_SET", OnOff(value)); return true;
            // STROBE_LIGHTS_SET is a no-op on Asobo-template lighting; STROBES_SET drives LIGHT STROBE (HS787 finding).
            case "C172_STROBE":        simConnect.SendEvent("STROBES_SET", OnOff(value)); return true;
            case "C172_TAXI_LIGHT":    simConnect.SendEvent("TAXI_LIGHTS_SET", OnOff(value)); return true;
            case "C172_LANDING_LIGHT": simConnect.SendEvent("LANDING_LIGHTS_SET", OnOff(value)); return true;
            case "C172_AVIONICS_1":    simConnect.SendEvent("AVIONICS_MASTER_1_SET", OnOff(value)); return true;
            case "C172_AVIONICS_2":    simConnect.SendEvent("AVIONICS_MASTER_2_SET", OnOff(value)); return true;
            case "C172_FUEL_SELECTOR": simConnect.SendEvent("FUEL_SELECTOR_SET", (uint)Math.Round(value)); return true;
            case "C172_FLAPS":
                simConnect.SendEvent((int)Math.Round(value) switch { 0 => "FLAPS_UP", 1 => "FLAPS_1", 2 => "FLAPS_2", _ => "FLAPS_DOWN" });
                return true;
            case MagnetoComboKey:
                if (!Cessna172Magnetos.IsSelectable(value)) return true;   // START is never a pick
                if (_engineStart.IsInFlight) return true;                  // the start owns the key
                _magnetoPickTicks = Environment.TickCount64;
                simConnect.SendEvent(Cessna172Magnetos.SetEvent, (uint)Math.Round(value));
                return true;

            // ---- Toggle-only switches ----
            case "C172_MASTER_BATTERY":  ToggleIfDiffers(simConnect, varKey, value, "TOGGLE_MASTER_BATTERY", 1); return true;
            case "C172_STANDBY_BATTERY": ToggleIfDiffers(simConnect, varKey, value, "TOGGLE_MASTER_BATTERY", 2); return true;
            case "C172_ALTERNATOR":      ToggleIfDiffers(simConnect, varKey, value, "TOGGLE_MASTER_ALTERNATOR"); return true;
            case "C172_FUEL_PUMP":       ToggleIfDiffers(simConnect, varKey, value, "TOGGLE_ELECT_FUEL_PUMP1"); return true;
            case "C172_FUEL_SHUTOFF":    ToggleIfDiffers(simConnect, varKey, value, "TOGGLE_FUEL_VALVE_ENG1"); return true;
            case "C172_PITOT_HEAT":      ToggleIfDiffers(simConnect, varKey, value, "PITOT_HEAT_TOGGLE"); return true;
            case "C172_PARKING_BRAKE":   ToggleIfDiffers(simConnect, varKey, value, "PARKING_BRAKES"); return true;

            // ---- Buttons (value is always 1) ----
            case EngineStartKey:   StartEngine(simConnect, announcer); return true;
            case "C172_COM1_SWAP": simConnect.SendEvent("COM_STBY_RADIO_SWAP"); return true;   // the active-frequency change announces itself
            case "C172_COM2_SWAP": simConnect.SendEvent("COM2_RADIO_SWAP"); return true;
            case "C172_XPNDR_IDENT": simConnect.SendEvent("XPNDR_IDENT_ON"); return true;

            // ---- Entry boxes (value is the parsed text) ----
            case Com1StandbyKey:
            case Com2StandbyKey:
                if (!Cessna172Readouts.IsValidComMhz(value))
                {
                    announcer.Announce("Invalid COM frequency. Range: 118.000 to 136.975 MHz.");
                    return true;
                }
                simConnect.SendEvent(varKey == Com2StandbyKey ? "COM2_STBY_RADIO_SET_HZ" : "COM_STBY_RADIO_SET_HZ",
                    (uint)Math.Round(value * 1_000_000.0));
                return true;   // success is silent: nothing the sim does not already show
            case SquawkKey:
                // Claimed so the definition's ProcessSimVarUpdate "Squawk 1200" (a real change, also
                // heard for a G1000 knob change) is the ONE confirmation, instead of MainForm's
                // "Squawk set to" followed by it a frame later.
                if (!Cessna172Squawk.TryToBcd(value, out uint bcd, out string error))
                {
                    announcer.Announce(error);
                    return true;
                }
                simConnect.SendEvent("XPNDR_SET", bcd);
                return true;
            case "C172_MIXTURE_SET":
                if (double.IsNaN(value) || value < 0 || value > 100)
                {
                    announcer.Announce("Invalid mixture. Range: 0 to 100 percent.");
                    return true;
                }
                // SendEvent silently no-ops when disconnected; refuse aloud rather than confirm a
                // write that never reached the aircraft (the "no false trace" rule).
                if (!simConnect.CanSendEvent)
                {
                    announcer.Announce("Mixture unavailable. Not connected to the simulator.");
                    return true;
                }
                simConnect.SendEvent("MIXTURE1_SET", Cessna172Readouts.MixtureSetParam(value));
                announcer.Announce($"Mixture {Cessna172Readouts.Percent(value)}");   // numeric confirmation: no sim echo
                return true;
            case AltimeterKey:
                if (!Cessna172Readouts.IsValidAltimeterInHg(value))
                {
                    announcer.Announce("Invalid altimeter setting. Range: 27.00 to 31.50 inches.");
                    return true;
                }
                // SendEvent silently no-ops when disconnected; refuse aloud rather than confirm a
                // write that never reached the aircraft (the "no false trace" rule).
                if (!simConnect.CanSendEvent)
                {
                    announcer.Announce("Altimeter unavailable. Not connected to the simulator.");
                    return true;
                }
                simConnect.SendEvent("KOHLSMAN_SET", Cessna172Readouts.KohlsmanSetParam(value));
                announcer.Announce($"Altimeter set to {value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
                return true;
        }
        return false;
    }

    /// <summary>The Start Engine button. Silent on press; the outcome speaks (Task 7's tick).</summary>
    private void StartEngine(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!sim.CanSendEvent)
        {
            announcer.Announce("Engine start unavailable. Not connected to the simulator.");
            return;
        }
        // An error condition, so it is speakable: a start on a running engine grinds the starter.
        if (_combustion == true)
        {
            announcer.Announce("Engine already running");
            return;
        }
        if (!_engineStart.Begin(Environment.TickCount64)) return;   // a start is already in flight: ignored
        _magnetoPickTicks = Environment.TickCount64;                // the START position is our own write
        sim.SendEvent(Cessna172Magnetos.SetEvent, (uint)Cessna172Magnetos.Start);
        Log.Debug("C172", "Engine start: key to START");
    }

    /// <summary>Returns the key to BOTH after a start ends or is abandoned.</summary>
    private void ReturnKeyToBoth()
    {
        if (_sim?.CanSendEvent != true) return;
        _magnetoPickTicks = Environment.TickCount64;
        _sim.SendEvent(Cessna172Magnetos.SetEvent, (uint)Cessna172Magnetos.Both);
        Log.Debug("C172", "Engine start: key to BOTH");
    }

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        switch (action)
        {
            // Ctrl+M — mute individual background callouts.
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowC172MonitorManagerDialog();
                return true;

            // Ctrl+N — the universal NAV radio dialog (PMDG 737 / iFly precedent). No NAV boxes on
            // the panel: one way to do it.
            case HotkeyAction.SetNavRadios:
                hotkeyManager.ExitInputHotkeyMode();
                ShowNavRadiosDialog(simConnect, announcer, parentForm);
                return true;

            // B — the altimeter, from the cached Kohlsman setting.
            case HotkeyAction.ReadAltimeter:
                announcer.AnnounceImmediate(Cessna172Readouts.AltimeterSpoken(simConnect.GetCachedVariableValue(AltimeterKey)));
                return true;

            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    private void ShowNavRadiosDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        simConnect.RequestNavRadioInfo(navData =>
        {
            void Open()
            {
                var form = new NavRadiosForm(
                    announcer,
                    navData.Nav1Freq, (int)Math.Round(navData.Nav1Obs),
                    navData.Nav2Freq, (int)Math.Round(navData.Nav2Obs),
                    settings =>
                    {
                        simConnect.SendEvent("NAV1_RADIO_SET_HZ", (uint)Math.Round(settings.Nav1FreqMHz * 1_000_000.0));
                        simConnect.SendEvent("VOR1_SET", (uint)settings.Nav1Course);
                        simConnect.SendEvent("NAV2_RADIO_SET_HZ", (uint)Math.Round(settings.Nav2FreqMHz * 1_000_000.0));
                        simConnect.SendEvent("VOR2_SET", (uint)settings.Nav2Course);
                        announcer.AnnounceImmediate(
                            $"NAV 1 {settings.Nav1FreqMHz:0.00}, course {settings.Nav1Course}. " +
                            $"NAV 2 {settings.Nav2FreqMHz:0.00}, course {settings.Nav2Course}.");
                    });
                form.Show(parentForm);
            }

            if (parentForm.IsDisposed) return;
            if (parentForm.InvokeRequired) parentForm.BeginInvoke((Action)Open);
            else Open();
        });
    }
}
