using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using System.Windows.Forms;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The Sovereign's hotkeys on the house layout. Input mode: Shift+M the MFD touchscreen and
/// Ctrl+Shift+R the PFD touchscreen, both on the Crew Seat; Ctrl+Shift+M and Alt+Shift+R the
/// other seat's; Shift+T the vendor EFB; Ctrl+P the autopilot window; Ctrl+A/H/S/V/B the
/// target dialogs. Output mode: Ctrl+Shift+C opens the MFD touchscreen on its Checklist page;
/// the readouts speak from the definition's cached rows (a row not yet delivered says so).
/// </summary>
public partial class SkywardC680Definition
{
    private bool HandleC680Hotkey(HotkeyAction action, SimConnectManager sc, ScreenReaderAnnouncer ann, Form parent, HotkeyManager hk)
    {
        switch (action)
        {
            // ---- windows
            case HotkeyAction.ShowFenixMCDU: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: true, CurrentSeat, ann); return true;
            case HotkeyAction.ShowRMP: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: false, CurrentSeat, ann); return true;
            case HotkeyAction.ShowC680OtherMfdTouchscreen: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: true, C680Seat.Other(CurrentSeat), ann); return true;
            case HotkeyAction.ShowC680OtherPfdTouchscreen: hk.ExitInputHotkeyMode(); ShowGtc(isMfd: false, C680Seat.Other(CurrentSeat), ann); return true;
            case HotkeyAction.ShowChecklistECL: hk.ExitOutputHotkeyMode(); ShowGtc(isMfd: true, CurrentSeat, ann, openPage: "Checklist"); return true;
            case HotkeyAction.FCUSetAutopilot: hk.ExitInputHotkeyMode(); ShowAutopilotWindow(sc, ann); return true;
            case HotkeyAction.ShowPMDGEFB: hk.ExitInputHotkeyMode(); ShowWindow("efb", () => new Forms.Citation680.C680EfbForm(ann)); return true;   // Shift+T: the vendor EFB

            // ---- output: autopilot targets
            case HotkeyAction.ReadAltitude: RequestFCUAltitude(sc, ann); return true;
            case HotkeyAction.ReadHeading: RequestFCUHeading(sc, ann); return true;
            case HotkeyAction.ReadSpeed: RequestFCUSpeed(sc, ann); return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA: RequestFCUVerticalSpeed(sc, ann); return true;

            // ---- output: speeds
            case HotkeyAction.ReadSpeedGD: ann.AnnounceImmediate(C680Speeds.ComposeTakeoff(ReadNow(sc, "C680_GROSS_WEIGHT"))); return true;
            case HotkeyAction.ReadSpeedS: ann.AnnounceImmediate(C680Speeds.ComposeLimits()); return true;
            case HotkeyAction.ReadSpeedF: ann.AnnounceImmediate(C680Speeds.ComposeFlapLimits()); return true;
            case HotkeyAction.ReadSpeedVLS: ann.AnnounceImmediate(C680Speeds.ComposeLanding(ReadNow(sc, "C680_GROSS_WEIGHT"))); return true;
            case HotkeyAction.ReadSpeedVS: ann.AnnounceImmediate(C680Speeds.ComposeStall(ReadNow(sc, "C680_GROSS_WEIGHT"))); return true;
            case HotkeyAction.ReadSpeedVFE: ann.AnnounceImmediate(C680Speeds.ComposeVfto()); return true;

            // ---- output: engines
            case HotkeyAction.ReadEngineRpm:
                ann.AnnounceImmediate($"N1 {N(ReadNow(sc, "C680_N1_L"), "0.0")} and {N(ReadNow(sc, "C680_N1_R"), "0.0")} percent, N2 {N(ReadNow(sc, "C680_N2_L"), "0.0")} and {N(ReadNow(sc, "C680_N2_R"), "0.0")} percent");
                return true;
            case HotkeyAction.ReadEnginePower:
                ann.AnnounceImmediate($"N1 {N(ReadNow(sc, "C680_N1_L"), "0.0")} and {N(ReadNow(sc, "C680_N1_R"), "0.0")} percent, fuel flow {N(ReadNow(sc, "C680_FF_L"))} and {N(ReadNow(sc, "C680_FF_R"))} pounds per hour, levers {N(ReadNow(sc, "C680_THR_L"))} and {N(ReadNow(sc, "C680_THR_R"))} percent");
                return true;
            case HotkeyAction.ReadEngineTemps:
                ann.AnnounceImmediate($"ITT {N(ReadNow(sc, "C680_ITT_L"))} and {N(ReadNow(sc, "C680_ITT_R"))} degrees, oil pressure {N(ReadNow(sc, "C680_OIL_P_L"))} and {N(ReadNow(sc, "C680_OIL_P_R"))} psi, oil temperature {N(ReadNow(sc, "C680_OIL_T_L"))} and {N(ReadNow(sc, "C680_OIL_T_R"))} degrees");
                return true;

            // ---- output: aircraft
            case HotkeyAction.ReadAltimeter:
            {
                double? mb = ReadNow(sc, C680Seat.PfdIndex(CurrentSeat) == 1 ? "C680_BARO_1" : "C680_BARO_2");
                if (mb == null) ann.AnnounceImmediate("Altimeter not yet read");
                else if (Math.Abs(mb.Value - 1013.25) < 0.6) ann.AnnounceImmediate("Altimeter standard, 1013");
                else ann.AnnounceImmediate($"Altimeter {mb.Value:0} hectopascals, {mb.Value / 33.8639:0.00} inches");
                return true;
            }
            case HotkeyAction.ReadFlaps:
            {
                double? idx = ReadNow(sc, "C680_FLAPS"); double? deg = ReadNow(sc, "C680_FLAP_DEG");
                string pos = idx == null ? "not yet read" : (int)Math.Round(idx.Value) switch { 0 => "up", 1 => "1", 2 => "2", _ => "full" };
                ann.AnnounceImmediate($"Flaps {pos}, {N(deg)} degrees");
                return true;
            }
            case HotkeyAction.ReadGear:
            {
                double? handle = ReadNow(sc, "C680_GEAR");
                string GearPos(string key) { var v = ReadNow(sc, key); return v == null ? "unknown" : v.Value >= 99 ? "down" : v.Value <= 1 ? "up" : $"{v.Value:0} percent"; }
                ann.AnnounceImmediate($"Gear handle {(handle == null ? "not yet read" : handle > 0.5 ? "down" : "up")}; nose {GearPos("C680_GEAR_C")}, left {GearPos("C680_GEAR_L")}, right {GearPos("C680_GEAR_R")}");
                return true;
            }
            case HotkeyAction.ReadFuelQuantity:
                ann.AnnounceImmediate($"Fuel left {N(ReadNow(sc, "C680_FUEL_L_LB"))}, right {N(ReadNow(sc, "C680_FUEL_R_LB"))}, total {N(ReadNow(sc, "C680_FUEL_TOTAL_LB"))} pounds");
                return true;
            case HotkeyAction.ReadFuelInfo:
                ann.AnnounceImmediate($"Fuel left {Kg(ReadNow(sc, "C680_FUEL_L_LB"))}, right {Kg(ReadNow(sc, "C680_FUEL_R_LB"))}, total {Kg(ReadNow(sc, "C680_FUEL_TOTAL_LB"))} kilograms");
                return true;
            case HotkeyAction.ReadGrossWeightKg:
            {
                double? gross = ReadNow(sc, "C680_GROSS_WEIGHT");
                ann.AnnounceImmediate(gross == null ? "Gross weight not yet read" : $"Gross weight {gross:0} pounds, {gross / 2.20462:0} kilograms; {C680Weights.Describe(gross.Value)}");
                return true;
            }
            case HotkeyAction.ReadSquawkCode:
            {
                double? code = ReadNow(sc, "C680_XPDR_CODE"); double? state = ReadNow(sc, "C680_XPDR_STATE");
                string mode = state == null ? "" : (int)Math.Round(state.Value) switch { 0 => "off", 1 => "standby", 2 => "test", 3 => "on", 4 => "alt", 5 => "ground", _ => "" };
                ann.AnnounceImmediate(code == null ? "Squawk not yet read" : $"Squawk {((int)Math.Round(code.Value)).ToString("X4")}{(mode.Length > 0 ? ", " + mode : "")}");
                return true;
            }
            case HotkeyAction.ReadDistanceToDest:
            {
                // The G3000 publishes no destination distance (GPS FLIGHT PLAN TOTAL DISTANCE reads 0,
                // measured in flight 2026-09-15): it is summed from the FMS's own legs on the MFD view.
                double? active = ReadNow(sc, "C680_FMS_ACTIVE");
                if (active != null && active < 0.5) { ann.AnnounceImmediate("No active flight plan"); return true; }
                _ = SpeakDestinationAsync(sc, ann);
                return true;
            }
            case HotkeyAction.ReadDistanceToTOD:
                ann.AnnounceImmediate("Top of descent is on the MFD touchscreen's VNAV page; the G3000 does not publish it.");
                return true;

            // ---- input: targets and toggles
            case HotkeyAction.FCUSetAltitude:
                ShowFCUInputDialog("Set Altitude Preselect", "Altitude", "100 to 47000 feet", "AP_ALT_VAR_SET_ENGLISH", sc, ann, parent,
                    s => double.TryParse(s, out var v) && v >= 100 && v <= 47000 ? (true, "") : (false, "Enter 100 to 47000"),
                    v => (uint)(Math.Round(v / 100) * 100));
                return true;
            case HotkeyAction.FCUSetHeading:
                ShowFCUInputDialog("Set Heading Bug", "Heading", "0 to 359 degrees", "HEADING_BUG_SET", sc, ann, parent,
                    s => double.TryParse(s, out var v) && v >= 0 && v <= 360 ? (true, "") : (false, "Enter 0 to 359"),
                    v => (uint)(((int)Math.Round(v) % 360 + 360) % 360));
                return true;
            case HotkeyAction.FCUSetSpeed:
                ShowTypedTarget(parent, ann, "Set Speed Target", "Speed", "100 to 305 knots, or 0.30 to 0.80 Mach",
                    s => double.TryParse(s, out var v) && ((v >= 0.3 && v <= 0.8) || (v >= 100 && v <= 305)) ? (true, "") : (false, "Enter 100 to 305 knots or 0.30 to 0.80 Mach"),
                    v => { SetSpeedTarget(sc, v); return v < 1 ? $"Mach {v:0.00}" : $"{v:0} knots"; });
                return true;
            case HotkeyAction.FCUSetVS:
                ShowTypedTarget(parent, ann, "Set Vertical Speed Target", "Vertical speed", "-6000 to 6000 feet per minute",
                    s => double.TryParse(s, out var v) && v >= -6000 && v <= 6000 ? (true, "") : (false, "Enter -6000 to 6000"),
                    v => { double r = Math.Round(v / 100) * 100; sc.ExecuteCalculatorCode($"{Rpn(r)} (>K:AP_VS_VAR_SET_ENGLISH)"); return $"{r:0} feet per minute"; });
                return true;
            case HotkeyAction.FCUSetBaro:
                ShowTypedTarget(parent, ann, "Set Altimeters", "Altimeter", "hPa or inches, both PFDs; std for standard",
                    s => s.Trim().Equals("std", StringComparison.OrdinalIgnoreCase) || (double.TryParse(s, out var v) && ((v >= 25 && v <= 32) || (v >= 850 && v <= 1090))) ? (true, "") : (false, "Enter 950 to 1090 hPa, 28.00 to 31.50 inches, or std"),
                    v => { double mb = v < 100 ? v * 33.8639 : v; string v16 = Rpn(Math.Round(mb * 16)); sc.ExecuteCalculatorCode($"1 {v16} (>K:2:KOHLSMAN_SET) 2 {v16} (>K:2:KOHLSMAN_SET)"); return $"{mb:0} hectopascals"; },
                    onStd: () => sc.ExecuteCalculatorCode("1 (>K:BAROMETRIC_STD_PRESSURE) 2 (>K:BAROMETRIC_STD_PRESSURE)"));
                return true;
            case HotkeyAction.ToggleAutopilot1: sc.ExecuteCalculatorCodeUnique("(>K:AP_MASTER)"); return true;
            case HotkeyAction.ToggleApproachMode: sc.ExecuteCalculatorCodeUnique("(>K:AP_APR_HOLD)"); return true;
            case HotkeyAction.ToggleLocalizer: sc.ExecuteCalculatorCodeUnique("(>K:AP_NAV1_HOLD)"); return true;
            case HotkeyAction.ToggleAutothrust: sc.ExecuteCalculatorCodeUnique("(>K:AUTO_THROTTLE_ARM)"); return true;
        }
        return false;
    }

    /// <summary>Output D: the MFD agent's FMS distance, spoken on the UI thread the hotkey came from.</summary>
    private async Task SpeakDestinationAsync(SimConnectManager sc, ScreenReaderAnnouncer ann)
    {
        string json = "";
        try { json = await MfdClient.InvokeAsync("__MSFSBA_C680_EIS && __MSFSBA_C680_EIS.dest ? __MSFSBA_C680_EIS.dest() : ''"); }
        catch (Exception) { json = ""; }
        ann.AnnounceImmediate(C680Destination.Compose(json, ReadNow(sc, "C680_FMS_DEST_ETE"), ReadNow(sc, "C680_FMS_DIST"), ReadNow(sc, "C680_FMS_ETE")));
    }

    private static string Kg(double? lb) => lb == null ? "not read" : (lb.Value / 2.20462).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    private static string Hm(double? seconds)
    {
        if (seconds == null || seconds <= 0) return "time not available";
        var t = TimeSpan.FromSeconds(seconds.Value);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours} hours {t.Minutes} minutes" : $"{t.Minutes} minutes";
    }

    /// <summary>A typed-value dialog whose write is a calculator string (negative and fractional values, or two indices), with an optional "std" word.</summary>
    private static void ShowTypedTarget(Form parent, ScreenReaderAnnouncer ann, string title, string what, string range,
        Func<string, (bool isValid, string message)> validator, Func<double, string> apply, Action? onStd = null)
    {
        var dialog = new Forms.ValueInputForm(title, what, range, ann, validator);
        if (dialog.ShowDialog(parent) != DialogResult.OK || !dialog.IsValidInput) return;
        string text = dialog.InputValue.Trim();
        if (onStd != null && text.Equals("std", StringComparison.OrdinalIgnoreCase)) { onStd(); ann.AnnounceImmediate($"{what} set to standard"); return; }
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
            && !double.TryParse(text, out value)) return;
        ann.AnnounceImmediate($"{what} set to {apply(value)}");
    }

    private void ShowGtc(bool isMfd, C680Seat.Side seat, ScreenReaderAnnouncer ann, string? openPage = null)
    {
        string id = (isMfd ? "MFD" : "PFD") + (int)seat;
        ShowWindow(id, () => new Forms.Citation680.C680GtcForm(isMfd, seat, ann, _ => { }));
        if (openPage != null && _windows.TryGetValue(id, out var w) && w is Forms.Citation680.C680GtcForm f) f.OpenPageWhenReady(openPage);
    }
}
