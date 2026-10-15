using System.Globalization;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>
/// Every word the Volanta window shows or speaks about a flight or a controller. Pure, and invariant-culture
/// throughout: a German Windows still reads "4.3 nm" and "6,900 feet", never "4,3" or "6.900". Distance and
/// relative altitude follow the TCAS window's thresholds (one decimal under 10 nm, "same altitude" within 200
/// feet) so the two traffic windows agree.
/// </summary>
public static class VolantaTrafficFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Faster than this up or down counts as climbing or descending.</summary>
    public const double TrendFpm = 300;

    public static string SourceName(VolantaSource source) => source switch
    {
        VolantaSource.Volanta => "Volanta",
        VolantaSource.Vatsim => "VATSIM",
        VolantaSource.Ivao => "IVAO",
        VolantaSource.PilotEdge => "PilotEdge",
        VolantaSource.Apoc => "APOC",
        _ => "other network",
    };

    /// <summary>The name to lead with: the callsign spaced for speech, else the registration, else the user.</summary>
    public static string DisplayName(VolantaFlight f)
    {
        if (f.Callsign.Length > 0) return GroundTrafficLogic.SpokenCallsign(f.Callsign);
        if (f.Registration.Length > 0) return f.Registration;
        if (f.UserName.Length > 0) return f.UserName;
        return "Unknown aircraft";
    }

    public static string Distance(double nm) => nm < 10
        ? nm.ToString("0.0", Inv) + " nm"
        : Math.Round(nm).ToString("0", Inv) + " nm";

    public static string RelativeAltitude(double diffFt)
    {
        double abs = Math.Abs(diffFt);
        if (abs < 200) return "same altitude";
        string feet = (Math.Round(abs / 100) * 100).ToString("N0", Inv);
        return diffFt > 0 ? $"{feet} feet above" : $"{feet} feet below";
    }

    /// <summary>"climbing", "descending", or "" when level or the feed has no vertical speed.</summary>
    public static string Trend(VolantaFlight f)
    {
        if (f.OnGround || f.VerticalSpeedFpm is not double vs) return "";
        if (vs > TrendFpm) return "climbing";
        if (vs < -TrendFpm) return "descending";
        return "";
    }

    /// <summary>Who is flying it: "Volanta user name", with the network a Volanta user also flies on, or the
    /// network for a network pilot.</summary>
    public static string Pilot(VolantaFlight f)
    {
        if (f.Source != VolantaSource.Volanta)
            return f.UserName.Length > 0 ? $"{SourceName(f.Source)}, {f.UserName}" : SourceName(f.Source);
        string who = f.UserName.Length > 0 ? $"Volanta user {f.UserName}" : "Volanta user";
        return f.AlsoOnNetwork is VolantaSource n ? $"{who}, on {SourceName(n)}" : who;
    }

    public static string RouteText(string origin, string destination)
    {
        bool from = origin.Length > 0, to = destination.Length > 0;
        if (from && to) return $"{origin} to {destination}";
        if (from) return $"from {origin}";
        if (to) return $"to {destination}";
        return "";
    }

    /// <summary>One list line: name, where it is from the pilot, altitude, speed, type, pilot and route.</summary>
    public static string ListLine(VolantaTrafficEntry e)
    {
        var f = e.Flight;
        var parts = new List<string>
        {
            DisplayName(f),
            $"{e.ClockPosition.ToString(Inv)} o'clock",
            Distance(e.DistanceNm),
        };
        if (f.OnGround)
            parts.Add("on the ground");
        else
        {
            parts.Add(RelativeAltitude(e.AltitudeDiffFt));
            string trend = Trend(f);
            if (trend.Length > 0) parts.Add(trend);
            parts.Add($"{Math.Round(f.GroundSpeedKt).ToString("0", Inv)} knots");
        }
        if (f.AircraftIcao.Length > 0) parts.Add(f.AircraftIcao);
        parts.Add(Pilot(f));
        string route = RouteText(f.Origin, f.Destination);
        if (route.Length > 0) parts.Add(route);
        return string.Join(", ", parts);
    }

    /// <summary>Everything known about one flight, one "Label: value" line each, for the details box.</summary>
    public static string Details(VolantaTrafficEntry e)
    {
        var f = e.Flight;
        var lines = new List<string>();
        void Add(string label, string value) { if (value.Length > 0) lines.Add($"{label}: {value}"); }

        Add("Callsign", f.Callsign);
        Add("Flight number", f.FlightNumber);
        Add("Pilot", Pilot(f));
        Add("Aircraft", f.AircraftIcao);
        Add("Registration", f.Registration);
        Add("From", f.Origin);
        Add("To", f.Destination);
        Add("Route", f.Route);
        Add("Simulator", SimulatorName(f.Simulator));
        Add("Position", $"{e.ClockPosition.ToString(Inv)} o'clock, {Distance(e.DistanceNm)}, bearing {Heading(e.BearingTrue)} true");
        if (f.OnGround)
            Add("Altitude", "on the ground");
        else
        {
            string trend = Trend(f);
            Add("Altitude", $"{Feet(f.AltitudeFt)}, {RelativeAltitude(e.AltitudeDiffFt)}" + (trend.Length > 0 ? $", {trend}" : ""));
            if (f.AltitudeAglFt is double agl) Add("Height above ground", Feet(agl));
        }
        Add("Ground speed", $"{Math.Round(f.GroundSpeedKt).ToString("0", Inv)} knots");
        if (f.VerticalSpeedFpm is double vs && !f.OnGround)
        {
            double rounded = Math.Round(vs / 10) * 10;
            if (rounded == 0) rounded = 0; // never "-0"
            Add("Vertical speed", $"{rounded.ToString("0", Inv)} feet per minute");
        }
        Add("Heading", $"{Heading(f.HeadingTrue)} true");
        Add("Squawk", f.Transponder);
        if (e.PositionAgeSeconds is double age) Add("Position reported", Age(age));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Feet(double ft) => $"{Math.Round(ft).ToString("N0", Inv)} feet";

    /// <summary>A heading or bearing as three digits: "005", "327"; 360 for north, as a pilot says it.</summary>
    public static string Heading(double deg)
    {
        int h = (int)Math.Round(RelativeDirection.Normalize360(deg));
        if (h == 0) h = 360;
        return h.ToString("000", Inv);
    }

    /// <summary>"just now", "12 seconds ago", "1 minute ago", "3 minutes ago".</summary>
    public static string Age(double seconds)
    {
        if (seconds < 2) return "just now";
        if (seconds < 90) return $"{Math.Round(seconds).ToString("0", Inv)} seconds ago";
        int minutes = (int)Math.Round(seconds / 60);
        return minutes == 1 ? "1 minute ago" : $"{minutes.ToString(Inv)} minutes ago";
    }

    public static string SimulatorName(string abbreviation) => abbreviation.ToLowerInvariant() switch
    {
        "" => "",
        "msfs24" => "Microsoft Flight Simulator 2024",
        "msfs" => "Microsoft Flight Simulator 2020",
        "xp12" => "X-Plane 12",
        "xp11" => "X-Plane 11",
        "p3dv4" => "Prepar3D v4",
        "p3dv5" => "Prepar3D v5",
        "p3dv6" => "Prepar3D v6",
        "fsx" => "FSX",
        "dcs" => "DCS",
        _ => abbreviation,
    };

    /// <summary>Volanta's facility word as a pilot reads it: "Atis" is "ATIS", and joined words are split
    /// ("ApproachDeparture" is "Approach Departure").</summary>
    public static string FacilityName(string facility)
    {
        if (facility.Equals("Atis", StringComparison.OrdinalIgnoreCase)) return "ATIS";
        if (facility.Equals("Fss", StringComparison.OrdinalIgnoreCase)) return "Flight Service";
        var sb = new StringBuilder(facility.Length + 4);
        for (int i = 0; i < facility.Length; i++)
        {
            if (i > 0 && char.IsUpper(facility[i]) && char.IsLower(facility[i - 1])) sb.Append(' ');
            sb.Append(facility[i]);
        }
        return sb.ToString();
    }

    /// <summary>One controller list line: callsign, facility, frequency, network and, when known, distance.</summary>
    public static string ControllerLine(VolantaController c, double? distanceNm)
    {
        var parts = new List<string> { c.Callsign.Length > 0 ? c.Callsign.Replace('_', ' ') : "Unknown station" };
        if (c.Facility.Length > 0) parts.Add(FacilityName(c.Facility));
        if (c.Frequency.Length > 0) parts.Add(c.Frequency);
        parts.Add(SourceName(c.Network));
        if (distanceNm is double d) parts.Add(Distance(d));
        return string.Join(", ", parts);
    }

    public static string ControllerDetails(VolantaController c, double? distanceNm)
    {
        var lines = new List<string>();
        void Add(string label, string value) { if (value.Length > 0) lines.Add($"{label}: {value}"); }
        Add("Station", c.Callsign);
        Add("Facility", FacilityName(c.Facility));
        Add("Frequency", c.Frequency);
        Add("Network", SourceName(c.Network));
        Add("Controller", c.UserName);
        Add("Airport", c.AirportIcao);
        if (distanceNm is double d) Add("Distance", Distance(d));
        if (c.OnlineSince is DateTimeOffset t) Add("Online since", t.UtcDateTime.ToString("HH:mm", Inv) + " UTC");
        if (c.Atis.Length > 0)
        {
            lines.Add("ATIS:");
            lines.AddRange(c.Atis.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
