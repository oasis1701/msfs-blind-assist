using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The text of one row of a panel's Status Display (MainForm's read-only list, Ctrl+3). Pure:
/// the definition supplies the cache reads, this decides the words. A row renders as
/// "{DisplayName}: {text}", so the text never repeats the name.
/// </summary>
public static class Md11StatusRow
{
    /// <summary>
    /// A lamp row: the same composed state the lamp SPEAKS — its lit legend, "unpowered" under
    /// the DC gate, or its dark meaning. <paramref name="ownValue"/> is the value just delivered
    /// for the lamp's own L:var (fresher than the cache); <paramref name="readOther"/> reads any
    /// other var by name. Null when the state block has nothing to say, so the caller falls back
    /// to the definition's ValueDescriptions ("off"/"on").
    /// </summary>
    public static string? Lamp(Md11StateSpec? spec, string ownVar, double ownValue, Func<string, double?> readOther, bool powered)
        => Md11ControlState.Compose(spec,
            v => string.Equals(v, ownVar, StringComparison.OrdinalIgnoreCase) ? ownValue : readOther(v),
            powered);

    /// <summary>
    /// A read-out row: the exported number with its unit, "not set" for an unentered speed or
    /// minimum, both units for an altimeter ("not available" until one is delivered), the
    /// composer's panel wording for the AFS windows.
    /// Null for a key this class does not format (the caller then uses ValueDescriptions, its own
    /// overrides, or the bare number).
    /// </summary>
    public static string? Readout(string key, double value, Func<string, double?> read)
    {
        switch (key)
        {
            case "MD11_V1":
            case "MD11_VR":
            case "MD11_V2":
            case "MD11_VSR":
            case "MD11_VFR":
                return value <= 0 ? "not set" : $"{Whole(value)} knots";

            case "MD11_CAP_MINIMUMS":
            case "MD11_FO_MINIMUMS":
                // The export shows the RADIO or the BARO minimums depending on the side's mode
                // switch (measured — see Md11Minimums), so the row names the mode when the switch
                // has been read; before that it is just the feet.
                return value <= 0 ? "not set" : Md11Minimums.DescribeReading(value, read(Md11Minimums.ModeKeyFor(key)));

            case Md11Fcp.ReadCaptainBaro:
            case "MD11_FO_ALTIMETER":
            case "MD11_STBY_ALTIMETER":
                // A setting is never zero or below: that is an export not yet delivered, and
                // "0, 0.00" is neither true nor actionable (Md11Fcp.DescribeAltimeterShortfall's
                // rule for the same three exports).
                return value <= 0 ? "not available" : Md11Fcp.DescribeAltimeter(value);

            case "MD11_ATS_STATE":
                return Md11AutoflightState.Autothrottle(value);

            case Md11Fcp.ReadSpeed:
                return Md11AutoflightState.SpeedValue(value, Mode(read, Md11Fcp.ModeSpeedIsMach));

            case Md11Fcp.ReadHeading:
            {
                var noun = Md11AutoflightState.HeadingNoun(Mode(read, Md11Fcp.ModeHeadingIsTrack));
                return Md11AutoflightState.PanelValue(Md11AutoflightState.Heading, noun, Md11AutoflightState.HeadingValue(value));
            }

            case Md11Fcp.ReadAltitude:
                return Md11AutoflightState.AltitudeValue(value, Mode(read, Md11Fcp.ModeAltitudeIsMetres));

            case Md11Fcp.ReadVerticalSpeed:
            {
                bool fpa = Mode(read, Md11Fcp.ModeVerticalIsFpa);
                return Md11AutoflightState.PanelValue(Md11AutoflightState.VerticalSpeed, Md11AutoflightState.VerticalNoun(fpa),
                    Md11AutoflightState.VerticalValue(value, fpa));
            }

            // Engine N1 to a tenth, the EAD's own resolution — and the only way a blind pilot gets
            // N1 on this aircraft, the EAD being WASM-rendered.
            case "MD11_ENG1_N1":
            case "MD11_ENG2_N1":
            case "MD11_ENG3_N1":
                return $"{value.ToString("0.0", CultureInfo.InvariantCulture)} percent";

            case "MD11_APU_N1":
            case "MD11_APU_N2":
                return $"{Whole(value)} percent";

            case "MD11_OVHD_TANK_1_VAL":
            case "MD11_OVHD_TANK_2_VAL":
            case "MD11_OVHD_TANK_3_VAL":
            case "MD11_OVHD_TANK_AUX_VAL":
            case "MD11_OVHD_TANK_TAIL_VAL":
                return $"{Whole(value)} pounds";

            default:
                return null;
        }
    }

    private static string Whole(double v) => v.ToString("0", CultureInfo.InvariantCulture);

    private static bool Mode(Func<string, double?> read, string key) => (read(key) ?? 0) > 0.5;
}
