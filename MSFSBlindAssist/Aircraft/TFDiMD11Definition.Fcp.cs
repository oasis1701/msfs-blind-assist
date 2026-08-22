using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The four FCP type-in dialogs (input mode: H heading, S speed, A altitude, V vertical speed).
///
/// These exist because <c>MD11_EXTCTL_FCP_*</c> takes a direct value — see <see cref="Md11Fcp"/>
/// for the live-probe evidence. The aircraft is otherwise entirely relative/event-driven, so this
/// family is the one place a value can be typed rather than walked.
///
/// Each dialog carries the toggle for its own window's UNIT, because value and unit are one
/// decision to a pilot: "set Mach 0.82" is a unit change and a value in one breath, and the mode
/// changes what the number even means.
/// </summary>
public partial class TFDiMD11Definition
{
    // ---------------------------------------------------------------------------------
    // Heading
    // ---------------------------------------------------------------------------------

    private void ShowHeadingDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("&Track / Heading", () => Mode(sim, Md11Fcp.ModeHeadingIsTrack) ? "Track" : "Heading",
                () => PressControl("MD11_CGS_HDGTRK_BT")),
            new("&NAV", () => "Press", () => PressControl("MD11_CGS_NAV_BT")),
            // The knob itself pushes and pulls, and both are real actions on the aircraft with
            // their own events — so they belong wherever the pilot is working this window, not
            // only in the full panel. Same for speed and altitude below.
            new("P&ush knob", () => "Press", () => PressControlEvents(Md11Fcp.HeadingKnob, "PUSH_DOWN", "PUSH_UP")),
            new("Pu&ll knob", () => "Press", () => PressControlEvents(Md11Fcp.HeadingKnob, "PULL_DOWN", "PULL_UP")),
        };

        var dialog = new ValueInputForm(
            "FCP Heading", "heading", "0-359", announcer,
            input => int.TryParse(input, out var v) && v >= 0 && v <= 359
                ? (true, "")
                : (false, "Enter a heading between 0 and 359"),
            toggles,
            input =>
            {
                if (!int.TryParse(input, out var hdg)) return;
                SetFcpValue(Md11Fcp.WriteHeading, Md11Fcp.NormaliseHeading(hdg), sim);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---------------------------------------------------------------------------------
    // Speed
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Accepts EITHER a knots value or a Mach value and picks the unit from the number's shape:
    /// anything below 10 is a Mach number (the FCP's Mach range is ~0.10-0.95 and its IAS range
    /// starts around 100 kt, so the two cannot overlap). That means "0.82" and "250" both just
    /// work, and the pilot never has to set the unit as a separate step — which matters here
    /// because the unit write and the value write are two different variables.
    /// </summary>
    private void ShowSpeedDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("&IAS / Mach", () => Mode(sim, Md11Fcp.ModeSpeedIsMach) ? "Mach" : "IAS",
                () => PressControl("MD11_CGS_IASMACH_BT")),
            new("&FMS Speed", () => "Press", () => PressControl("MD11_CGS_FMSSPD_BT")),
            new("P&ush knob", () => "Press", () => PressControlEvents(Md11Fcp.SpeedKnob, "PUSH_DOWN", "PUSH_UP")),
            new("Pu&ll knob", () => "Press", () => PressControlEvents(Md11Fcp.SpeedKnob, "PULL_DOWN", "PULL_UP")),
        };

        var dialog = new ValueInputForm(
            "FCP Speed", "speed", "100-365 knots, or a Mach number such as 0.82", announcer,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return (false, "Enter a speed in knots, or a Mach number such as 0.82");

                return LooksLikeMach(v)
                    ? v is >= Md11Fcp.MinMach and <= Md11Fcp.MaxMach
                        ? (true, "")
                        : (false, $"Enter a Mach between {Md11Fcp.MinMach:0.00} and {Md11Fcp.MaxMach:0.00}")
                    : v >= Md11Fcp.MinSpeedKnots && v <= Md11Fcp.MaxSpeedKnots
                        ? (true, "")
                        : (false, $"Enter a speed between {Md11Fcp.MinSpeedKnots} and {Md11Fcp.MaxSpeedKnots} knots, or a Mach such as 0.82");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;

                var mach = LooksLikeMach(v);
                SetFcpValue(Md11Fcp.WriteSpeed, mach ? v : Math.Round(v), sim,
                    Md11Fcp.WriteSpeedUnit, mach ? 1 : 0);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// The FCP's Mach band (0.10-0.95) and its IAS band (from ~100 kt) do not overlap, so the
    /// magnitude alone identifies the unit unambiguously. 10 is the split point: comfortably above
    /// any Mach the FCP takes, comfortably below any airspeed it takes.
    /// </summary>
    private static bool LooksLikeMach(double v) => v < 10;

    // ---------------------------------------------------------------------------------
    // Altitude
    // ---------------------------------------------------------------------------------

    private void ShowAltitudeDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("Feet / &Metres", () => Mode(sim, Md11Fcp.ModeAltitudeIsMetres) ? "Metres" : "Feet",
                () => PressControl("MD11_CGS_FTM_BT")),
            new("&PROF", () => "Press", () => PressControl("MD11_CGS_PROF_BT")),
            new("P&ush knob", () => "Press", () => PressControlEvents(Md11Fcp.AltitudeKnob, "PUSH_DOWN", "PUSH_UP")),
            new("Pu&ll knob", () => "Press", () => PressControlEvents(Md11Fcp.AltitudeKnob, "PULL_DOWN", "PULL_UP")),
        };

        var dialog = new ValueInputForm(
            "FCP Altitude", "altitude", $"{Md11Fcp.MinAltitudeFt}-{Md11Fcp.MaxAltitudeFt} feet", announcer,
            input => int.TryParse(input, out var v) && v >= Md11Fcp.MinAltitudeFt && v <= Md11Fcp.MaxAltitudeFt
                ? (true, "")
                : (false, $"Enter an altitude between {Md11Fcp.MinAltitudeFt} and {Md11Fcp.MaxAltitudeFt} feet"),
            toggles,
            input =>
            {
                if (!int.TryParse(input, out var alt)) return;
                // The typed number is feet, so the unit is written alongside it — otherwise a
                // window left in metres would read the value as metres and climb to the wrong level.
                SetFcpValue(Md11Fcp.WriteAltitude, alt, sim, Md11Fcp.WriteAltitudeUnit, 0);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---------------------------------------------------------------------------------
    // Vertical speed / FPA
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Same shape-based unit pick as speed: a V/S is thousands of feet per minute, an FPA is a
    /// single-digit angle, so "-1500" and "-3" cannot be confused for one another.
    /// </summary>
    private void ShowVSDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            new("&VS / FPA", () => Mode(sim, Md11Fcp.ModeVerticalIsFpa) ? "FPA" : "V/S",
                () => PressControl("MD11_CGS_VS_FPA_BT")),
            // The MD-11 has no engage-V/S button — turning the V/S / FPA wheel is what engages the
            // pitch mode. Exposed here so the pilot can engage and fine-tune it by hand; submitting
            // a typed value engages it too (see SetVerticalSpeedEngaged). One click per press.
            new("Wheel &up", () => "Turn", () => FireControlEvent(Md11Fcp.VerticalSpeedKnob, "WHEEL_UP")),
            new("Wheel &down", () => "Turn", () => FireControlEvent(Md11Fcp.VerticalSpeedKnob, "WHEEL_DOWN")),
        };

        var dialog = new ValueInputForm(
            "FCP Vertical Speed", "vertical speed",
            $"plus or minus up to {Md11Fcp.MaxVerticalSpeedFpm} feet per minute, or an FPA such as -3", announcer,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var v))
                    return (false, "Enter a vertical speed in feet per minute, or an FPA such as -3");

                return LooksLikeFpa(v)
                    ? Math.Abs(v) <= Md11Fcp.MaxFpaDegrees
                        ? (true, "")
                        : (false, $"Enter an FPA between -{Md11Fcp.MaxFpaDegrees} and {Md11Fcp.MaxFpaDegrees} degrees")
                    : Math.Abs(v) <= Md11Fcp.MaxVerticalSpeedFpm
                        ? (true, "")
                        : (false, $"Enter a vertical speed within {Md11Fcp.MaxVerticalSpeedFpm} feet per minute");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var v)) return;

                var fpa = LooksLikeFpa(v);
                // Engage the pitch mode (nudge the wheel) AND set the value — a plain value-set
                // leaves it in a window the FCC is not flying. See SetVerticalSpeedEngaged.
                SetVerticalSpeedEngaged(fpa ? v : Math.Round(v), fpa ? 1 : 0, sim);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    /// <summary>
    /// An FPA is at most ±9.9°; a usable V/S is hundreds of fpm. 20 splits them with room to
    /// spare in both directions.
    /// </summary>
    private static bool LooksLikeFpa(double v) => Math.Abs(v) <= 20;

    // ---------------------------------------------------------------------------------
    // Altimeter (Ctrl+B)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Sets the captain's altimeter. Accepts hPa (900-1100) or inHg (26-32) and figures out which
    /// from the number's magnitude — then converts to whatever unit the display is CURRENTLY in
    /// before writing, so "1013" and "29.92" each do the right thing regardless of the PFD's unit.
    /// </summary>
    private void ShowBaroDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!Connected(sim, announcer)) return;

        var toggles = new List<ToggleButtonDef>
        {
            // STD is a toggle with no readable state (the "STD" flag is on the WASM PFD), so this
            // shows the action, not a live value. Pushing the baro knob is the real mechanism.
            new("&Standard (toggle)", () => "Push", () => PressControl(Md11Fcp.BaroKnob)),
        };

        var dialog = new ValueInputForm(
            "Captain Altimeter", "altimeter", "hPa (900-1100) or inHg such as 29.92", announcer,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return (false, "Enter hPa (900-1100) or inHg such as 29.92");

                return Md11Fcp.LooksLikeHpa(v)
                    ? v is >= Md11Fcp.MinHpa and <= Md11Fcp.MaxHpa
                        ? (true, "")
                        : (false, $"Enter hPa between {Md11Fcp.MinHpa} and {Md11Fcp.MaxHpa}, or inHg such as 29.92")
                    : v is >= Md11Fcp.MinInHg and <= Md11Fcp.MaxInHg
                        ? (true, "")
                        : (false, $"Enter inHg between {Md11Fcp.MinInHg:0.00} and {Md11Fcp.MaxInHg:0.00}, or hPa such as 1013");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
                var display = sim.GetCachedVariableValue(Md11Fcp.ReadCaptainBaro) ?? v;
                SetFcpValue(Md11Fcp.WriteCaptainBaro, Md11Fcp.BaroToDisplayUnit(v, display), sim);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // ---------------------------------------------------------------------------------
    // Read-outs (output mode: Shift+H / Shift+S / Shift+A / Shift+V)
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// What the FCP's heading window is showing, with its mode.
    ///
    /// The mode is NOT decoration: the same window shows a HEADING or a TRACK, and 123 means a
    /// different thing in each. A blind pilot cannot glance at the window to see which, so the
    /// word is the only cue that exists.
    ///
    /// What this deliberately does NOT say is which lateral mode is ENGAGED (NAV, heading hold…).
    /// The MD-11 exports no FMA — see docs/md11.md §2c — and MD11_CGS_NAV_BT only reports that the
    /// button was pressed, which is not the same as the mode arming. Announcing a pressed button as
    /// an engaged mode would be a confident lie, so the read-out stops at what is knowable.
    /// </summary>
    private string DescribeHeading(SimConnectManager sim)
    {
        var v = Val(sim, Md11Fcp.ReadHeading);
        var word = Mode(sim, Md11Fcp.ModeHeadingIsTrack) ? "track" : "heading";
        return Md11Fcp.IsDashed(v)
            ? $"Selected {word} dashed"
            : $"Selected {word} {v.ToString("000", CultureInfo.InvariantCulture)}";
    }

    private string DescribeSpeed(SimConnectManager sim)
    {
        var v = Val(sim, Md11Fcp.ReadSpeed);
        if (Md11Fcp.IsDashed(v)) return "Selected speed dashed";

        // Mach is a real number stored as float32 (0.81999999 for 0.82) — round it, never compare.
        return Mode(sim, Md11Fcp.ModeSpeedIsMach)
            ? $"Selected Mach {v.ToString("0.00", CultureInfo.InvariantCulture)}"
            : $"Selected speed {v.ToString("0", CultureInfo.InvariantCulture)} knots";
    }

    private string DescribeAltitude(SimConnectManager sim)
    {
        var v = Val(sim, Md11Fcp.ReadAltitude);
        if (Md11Fcp.IsDashed(v)) return "Selected altitude dashed";

        var unit = Mode(sim, Md11Fcp.ModeAltitudeIsMetres) ? "metres" : "feet";
        return $"Selected altitude {v.ToString("0", CultureInfo.InvariantCulture)} {unit}";
    }

    private string DescribeVertical(SimConnectManager sim)
    {
        var v = Val(sim, Md11Fcp.ReadVerticalSpeed);
        var fpa = Mode(sim, Md11Fcp.ModeVerticalIsFpa);
        if (Md11Fcp.IsDashed(v)) return fpa ? "Selected FPA dashed" : "Selected vertical speed dashed";

        return fpa
            ? $"Selected FPA {v.ToString("0.0", CultureInfo.InvariantCulture)} degrees"
            : $"Selected vertical speed {v.ToString("0", CultureInfo.InvariantCulture)} feet per minute";
    }

    /// <summary>Autopilot engagement — the one autoflight STATE the aircraft does export.</summary>
    private static string DescribeAutopilot(SimConnectManager sim) => Val(sim, "MD11_AP_STATE") switch
    {
        >= 2.5 => "Autopilot 1 and 2",
        >= 1.5 => "Autopilot 2",
        >= 0.5 => "Autopilot 1",
        _ => "Autopilot off",
    };

    // ---------------------------------------------------------------------------------
    // Shared
    // ---------------------------------------------------------------------------------

    private static double Val(SimConnectManager sim, string varKey)
        => sim.GetCachedVariableValue(varKey) ?? 0;

    private static bool Mode(SimConnectManager sim, string varKey)
        => (sim.GetCachedVariableValue(varKey) ?? 0) > 0.5;

    private static bool Connected(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (sim.IsConnected) return true;
        announcer.AnnounceImmediate("Not connected to simulator.");
        return false;
    }
}
