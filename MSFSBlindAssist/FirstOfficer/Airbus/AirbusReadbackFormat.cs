using System.Globalization;

namespace MSFSBlindAssist.FirstOfficer.Airbus;

/// <summary>
/// The value text the A320-family First Officers speak for a read-back line whose Airbus
/// response is a blank the crew fills in ("FLAPS SETTING ... CONF __"). Pure and culture-
/// invariant (CI runs en-US; a comma-decimal culture would otherwise leak into speech).
/// Encodings: the Fenix and the FBW A32NX/A330 agree on every input here (flap lever 0 Up..4
/// Full, engine mode 0 Crank/1 Norm/2 Ign, TCAS 0 Stby/1 TA/2 TA-RA, autobrake 0 Off..3 Max).
/// Null means "nothing to say" — the line then speaks as it did without a value.
/// </summary>
public static class AirbusReadbackFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int? Index(double v, int max)
    {
        if (double.IsNaN(v)) return null;
        int i = (int)Math.Round(v);
        return i >= 0 && i <= max ? i : null;
    }

    public static string? FlapsLever(double index) => Index(index, 4) switch
    {
        0 => "flaps up", 1 => "flaps 1", 2 => "flaps 2", 3 => "flaps 3", 4 => "flaps full", _ => null,
    };

    public static string? EngineMode(double position) => Index(position, 2) switch
    {
        0 => "crank", 1 => "normal", 2 => "ignition start", _ => null,
    };

    public static string? Tcas(double mode) => Index(mode, 2) switch
    {
        0 => "standby", 1 => "TA only", 2 => "TA/RA", _ => null,
    };

    public static string? OnOff(double v) => double.IsNaN(v) ? null : v > 0.5 ? "on" : "off";

    public static string? Packs(double pack1, double pack2)
    {
        string? a = OnOff(pack1), b = OnOff(pack2);
        return a == null || b == null ? null : $"pack 1 {a}, pack 2 {b}";
    }

    public static string? AntiIce(double eng1, double eng2, double wing)
    {
        string? e1 = OnOff(eng1), e2 = OnOff(eng2), w = OnOff(wing);
        if (e1 == null || e2 == null || w == null) return null;
        string engines = e1 == e2 ? $"engine anti-ice {e1}" : $"engine 1 anti-ice {e1}, engine 2 anti-ice {e2}";
        return $"{engines}, wing anti-ice {w}";
    }

    /// <summary>"standard", "QNH 1013" (hPa, whole) or "QNH 29.92" (inHg, two decimals).</summary>
    public static string? Baro(double stdFlag, double value, bool inHg)
    {
        if (double.IsNaN(stdFlag)) return null;
        if (stdFlag > 0.5) return "standard";
        if (double.IsNaN(value)) return null;
        return inHg
            ? "QNH " + value.ToString("0.00", Inv)
            : "QNH " + Math.Round(value).ToString("0", Inv);
    }

    public static string? Autobrake(double mode) => Index(mode, 3) switch
    {
        0 => "off", 1 => "low", 2 => "medium", 3 => "max", _ => null,
    };

    /// <summary>Fenix: the armed mode is whichever LO / MED / MAX lamp is lit.</summary>
    public static double AutobrakeModeFromLamps(double lo, double med, double max)
    {
        if (double.IsNaN(lo) || double.IsNaN(med) || double.IsNaN(max)) return double.NaN;
        if (max > 0.5) return 3;
        if (med > 0.5) return 2;
        if (lo > 0.5) return 1;
        return 0;
    }

    /// <summary>"V1 142, VR 145, V2 149, flex 55"; flex omitted when not set; null when
    /// any V-speed is unknown (NaN); "not set" when a known value is below 1.</summary>
    public static string? TakeoffSpeeds(double v1, double vr, double v2, double flex)
    {
        // If any V-speed is unknown (NaN), return null (nothing to say)
        if (double.IsNaN(v1) || double.IsNaN(vr) || double.IsNaN(v2)) return null;

        // If any V-speed is known but below 1, return "not set"
        if (v1 < 1 || vr < 1 || v2 < 1) return "not set";

        string s = string.Format(Inv, "V1 {0:0}, VR {1:0}, V2 {2:0}", v1, vr, v2);
        // Flex: include only if not NaN and >= 1
        if (!double.IsNaN(flex) && flex >= 1)
        {
            s += string.Format(Inv, ", flex {0:0}", flex);
        }
        return s;
    }

    /// <summary>Total fuel to the nearest 100, in kilograms or pounds.</summary>
    public static string? FuelQuantity(double kilograms, bool pounds)
    {
        if (double.IsNaN(kilograms)) return null;
        double v = pounds ? kilograms * 2.20462262 : kilograms;
        double rounded = Math.Round(v / 100.0) * 100.0;
        return rounded.ToString("#,0", Inv) + (pounds ? " pounds" : " kilograms");
    }
}
