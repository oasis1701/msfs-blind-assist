using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Units for the WT-B78x 787 Flight Data rows (VNAV, LNAV and Progress, Glidepath), shared by
/// the HorizonSim 787-9, the Kuro 787-8 and the stock Asobo 787-10.
///
/// These rows are the avionics' own navigation L:vars. The aircraft writes each one in a real
/// unit (the publisher's <c>SimVarValueType</c> in the MFD bundle: nautical miles, degrees,
/// knots, feet, feet per minute, metres). An L:var read as <c>"number"</c> does not return that
/// unit: it returns the sim's internal base unit, so a distance arrives in METRES, an angle in
/// RADIANS and a speed in METRES PER SECOND. Measured live 2026-11-19 on the HorizonSim 787-9:
/// <c>WTBoeing_LNavData_Destination_Distance_Direct</c> read 1,850,148 as "number" and 999 as
/// nautical miles, and scratch L:vars written as 100 NM / 90° / 100 kt read back as
/// 185,200 / 1.5708 / 51.44. Every row was registered with the default "number", which is how a
/// trans-oceanic flight's destination distance read "13653434".
///
/// Each entry names the unit the row is READ in (SimConnect converts) and how it is SPOKEN.
/// The VNAV distances the aircraft writes in metres are read in nautical miles, because a pilot
/// plans descent in miles. Booleans and indexes are not listed: a bool's base unit is itself,
/// and an index is a plain count.
/// </summary>
public static class HS787FlightData
{
    private enum Kind { Miles, MilesFine, Degrees, PathAngle, Knots, Feet, FeetPerMinute }

    private static readonly Dictionary<string, Kind> Rows = new()
    {
        // LNAV and Progress
        ["HS787_LV_WTAP_LNav_Along_Track_Speed"] = Kind.Knots,
        ["HS787_LV_WTAP_LNav_Course_To_Steer"] = Kind.Degrees,
        ["HS787_LV_WTAP_LNav_DTK"] = Kind.Degrees,
        ["HS787_LV_WTAP_LNav_Leg_Distance_Along"] = Kind.Miles,
        ["HS787_LV_WTAP_LNav_Leg_Distance_Remaining"] = Kind.Miles,
        ["HS787_LV_WTAP_LNav_Vector_Anticipation_Distance"] = Kind.MilesFine,
        ["HS787_LV_WTAP_LNav_Vector_Distance_Along"] = Kind.Miles,
        ["HS787_LV_WTAP_LNav_Vector_Distance_Remaining"] = Kind.Miles,
        ["HS787_LV_WTAP_LNav_XTK"] = Kind.MilesFine,
        ["HS787_LV_WTBoeing_LNavData_Destination_Distance_Direct"] = Kind.Miles,
        ["HS787_LV_WTBoeing_LNavData_Destination_Runway_Distance_Direct"] = Kind.Miles,
        ["HS787_LV_WTBoeing_LNavData_Faf_Distance"] = Kind.Miles,
        ["HS787_LV_WTBoeing_LNavData_Map_Distance"] = Kind.Miles,
        ["HS787_LV_WTBoeing_LNavData_Total_Distance_Direct"] = Kind.Miles,
        ["HS787_LV_WTBoeing_LNavData_Tracked_Leg_End_Distance"] = Kind.Miles,
        ["HS787_LV_WT_LNavData_CDI_Scale"] = Kind.MilesFine,
        ["HS787_LV_WT_LNavData_DTK_Mag"] = Kind.Degrees,
        ["HS787_LV_WT_LNavData_DTK_True"] = Kind.Degrees,
        ["HS787_LV_WT_LNavData_Destination_Distance"] = Kind.Miles,
        ["HS787_LV_WT_LNavData_Waypoint_Bearing_Mag"] = Kind.Degrees,
        ["HS787_LV_WT_LNavData_Waypoint_Bearing_True"] = Kind.Degrees,
        ["HS787_LV_WT_LNavData_Waypoint_Distance"] = Kind.Miles,
        ["HS787_LV_WT_LNavData_XTK"] = Kind.MilesFine,

        // VNAV (the aircraft writes these distances in metres; read in nautical miles)
        ["HS787_LV_WTAP_VNav_Distance_To_BOC"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_Distance_To_BOD"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_Distance_To_Performance_TOD"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_Distance_To_TOC"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_Performance_TOD_Distance_In_Leg"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_TOC_Distance_In_Leg"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_TOD_Distance_In_Leg"] = Kind.Miles,
        ["HS787_LV_WTAP_VNav_Constraint_Altitude"] = Kind.Feet,
        ["HS787_LV_WTAP_VNav_Next_Constraint_Altitude"] = Kind.Feet,
        ["HS787_LV_WTAP_VNav_Target_Altitude"] = Kind.Feet,
        ["HS787_LV_WTAP_VNav_Vertical_Deviation"] = Kind.Feet,
        ["HS787_LV_WTAP_VNav_FPA"] = Kind.PathAngle,
        ["HS787_LV_WTAP_VNAV_Required_VS"] = Kind.FeetPerMinute,

        // Glidepath
        ["HS787_LV_WTAP_GP_Distance"] = Kind.Feet,
        ["HS787_LV_WTAP_GP_FPA"] = Kind.PathAngle,
        ["HS787_LV_WTAP_GP_Required_VS"] = Kind.FeetPerMinute,
        ["HS787_LV_WTAP_GP_Vertical_Deviation"] = Kind.Feet,
    };

    /// <summary>Every Flight Data variable key this table gives a unit to.</summary>
    public static IEnumerable<string> Keys => Rows.Keys;

    /// <summary>The SimConnect unit to READ <paramref name="key"/> in, or null when the row is
    /// not in the table (it keeps its registered unit).</summary>
    public static string? ReadUnit(string key) => Rows.TryGetValue(key, out var kind) ? kind switch
    {
        Kind.Miles or Kind.MilesFine => "nautical miles",
        Kind.Degrees or Kind.PathAngle => "degrees",
        Kind.Knots => "knots",
        Kind.Feet => "feet",
        Kind.FeetPerMinute => "feet per minute",
        _ => null,
    } : null;

    /// <summary>The spoken text for a row read in its <see cref="ReadUnit"/>; false for a key
    /// not in the table.</summary>
    public static bool TryFormat(string key, double value, out string text)
    {
        text = "";
        if (!Rows.TryGetValue(key, out var kind) || double.IsNaN(value) || double.IsInfinity(value))
            return false;
        var inv = CultureInfo.InvariantCulture;
        text = kind switch
        {
            Kind.Miles => $"{value.ToString("0.0", inv)} nautical miles",
            Kind.MilesFine => $"{value.ToString("0.00", inv)} nautical miles",
            Kind.Degrees => $"{Course(value).ToString(inv)} degrees",
            Kind.PathAngle => $"{(Math.Abs(value) < 0.05 ? 0.0 : value).ToString("0.0", inv)} degrees",
            Kind.Knots => $"{Whole(value).ToString(inv)} knots",
            Kind.Feet => $"{Whole(value).ToString(inv)} feet",
            Kind.FeetPerMinute => $"{Whole(value).ToString(inv)} feet per minute",
            _ => "",
        };
        return true;
    }

    // A course or bearing reads 0-359 (a WT course can arrive as -10 or 370).
    private static int Course(double deg)
    {
        int d = Whole(deg) % 360;
        return d < 0 ? d + 360 : d;
    }

    // Through an int, so -0.3 reads "0", never "-0".
    private static int Whole(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
