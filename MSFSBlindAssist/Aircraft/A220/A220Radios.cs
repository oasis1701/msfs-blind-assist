using System.Globalization;
using System.Text.Json;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The captain's navigation-radio truth, from the displays agent's
/// <c>__a220Displays.radios()</c> (three AFDX CommBus stores the aircraft's own
/// displays render from — src/avionics/lib/afdx/nav.ts, ctp.ts, radio.ts):
///   L Nav Data   source / course / frequency / CDI the captain's HSI is flying,
///   L CTP Data   the captain CTP's course setting,
///   Radio Data   vhf.nav_1 / nav_2 (the CNS NAV CONTROL windows).
/// Pure parsing + wording so it is pinned without a sim.
/// </summary>
public sealed class A220RadioState
{
    public sealed record Nav(double? ActiveMhz, double? PresetMhz, bool? AutoTune);

    public int? NavSource { get; init; }
    public double? Course { get; init; }
    public double? CtpCourse { get; init; }
    public double? SourceFrequencyMhz { get; init; }
    public double? Cdi { get; init; }
    public bool? To { get; init; }
    public Nav? Nav1 { get; init; }
    public Nav? Nav2 { get; init; }
    public bool LinkUp { get; init; }

    /// <summary>nav.ts Ag: the source the HSI is actually flying.</summary>
    public static string SourceName(int? source) => source switch
    {
        0 => "FMS 1",
        1 => "FMS 2",
        2 => "VOR 1",
        3 => "VOR 2",
        4 => "LOC 1",
        5 => "LOC 2",
        _ => "unknown"
    };

    /// <summary>A radio source (VOR/LOC) rather than an FMS one.</summary>
    public static bool IsRadioSource(int? source) => source is >= 2 and <= 5;

    public static A220RadioState? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;

            JsonElement lnav = Obj(r, "lnav"), lctp = Obj(r, "lctp"), radio = Obj(r, "radio");
            JsonElement vhf = radio.ValueKind == JsonValueKind.Object ? Obj(radio, "vhf") : default;

            return new A220RadioState
            {
                LinkUp = lnav.ValueKind == JsonValueKind.Object || radio.ValueKind == JsonValueKind.Object,
                NavSource = Int(lnav, "source"),
                Course = Num(lnav, "course"),
                CtpCourse = Num(lctp, "course"),
                SourceFrequencyMhz = Hz(Num(lnav, "frequency")),
                Cdi = Num(lnav, "cdi"),
                To = Bool(lnav, "to"),
                Nav1 = ParseNav(vhf, "nav_1"),
                Nav2 = ParseNav(vhf, "nav_2")
            };
        }
        catch (JsonException) { return null; }
    }

    // A NAV entry is an object (active / preset-or-standby / autotune) on the builds
    // seen; a bare number is treated as the active frequency so a shape change never
    // throws, it only reads less.
    private static Nav? ParseNav(JsonElement vhf, string key)
    {
        if (vhf.ValueKind != JsonValueKind.Object || !vhf.TryGetProperty(key, out var n)) return null;
        if (n.ValueKind == JsonValueKind.Number) return new Nav(Hz(n.GetDouble()), null, null);
        if (n.ValueKind != JsonValueKind.Object) return null;
        double? preset = Num(n, "preset") ?? Num(n, "standby") ?? Num(n, "preview");
        return new Nav(Hz(Num(n, "active") ?? Num(n, "frequency")), Hz(preset), Bool(n, "autotune"));
    }

    private static JsonElement Obj(JsonElement e, string k)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
    private static double? Num(JsonElement e, string k)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static int? Int(JsonElement e, string k) => Num(e, k) is double d ? (int)Math.Round(d) : null;
    private static bool? Bool(JsonElement e, string k)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v)
           ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
           : null;

    /// <summary>The stores carry Hz (108e6); anything already in the MHz band passes through.</summary>
    private static double? Hz(double? v) => v switch
    {
        null => null,
        > 1_000_000 => v / 1_000_000.0,
        _ => v
    };

    public static string Mhz(double? v) => v is double d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "not set";

    public static string Degrees(double? v)
    {
        if (v is not double d) return "not set";
        int deg = (int)Math.Round(d) % 360;
        return (deg == 0 ? 360 : deg).ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>Course entry: whole degrees 1-360 (0 is accepted as 360).</summary>
    public static bool TryParseCourse(string text, out int course)
    {
        course = 0;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c)) return false;
        if (c < 0 || c > 360) return false;
        course = c == 0 ? 360 : c;
        return true;
    }

    /// <summary>NAV preset entry: 108.00-117.95 MHz on the 50 kHz grid.</summary>
    public static bool TryParseNavMhz(string text, out double mhz)
    {
        mhz = 0;
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double f)) return false;
        f = Math.Round(f * 20.0) / 20.0;
        if (f < 108.0 || f > 117.95) return false;
        mhz = f;
        return true;
    }

    public List<string> Describe()
    {
        var lines = new List<string>();
        if (!LinkUp)
        {
            lines.Add("Waiting for the aircraft's radio data…");
            return lines;
        }
        string src = SourceName(NavSource);
        lines.Add($"Captain nav source: {src}");
        lines.Add($"Captain course: {Degrees(CtpCourse ?? Course)}");
        if (IsRadioSource(NavSource))
            lines.Add($"{src} frequency: {Mhz(SourceFrequencyMhz)}" + (To is bool to ? (to ? ", TO" : ", FROM") : ""));
        lines.Add(DescribeNav("NAV 1", Nav1));
        lines.Add(DescribeNav("NAV 2", Nav2));
        return lines;
    }

    private static string DescribeNav(string name, Nav? n)
    {
        if (n == null) return $"{name}: no data";
        string s = $"{name}: active {Mhz(n.ActiveMhz)}";
        if (n.PresetMhz != null) s += $", preset {Mhz(n.PresetMhz)}";
        if (n.AutoTune is bool a) s += a ? ", tuning AUTO" : ", tuning MANUAL";
        return s;
    }
}
