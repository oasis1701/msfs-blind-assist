using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The ND's traffic: the targets the Fenix sends (field 5) drawn as white diamonds with a relative altitude label and a
/// trend arrow, and the TCAS ring. Measured on 3.0.0.7613 (docs/fenix-channel/evidence/03-nd.md, sitting 2, "Traffic").
/// Only what the glass draws is listed: no callsign, no count, no selector, standby or TA-only row (the lists are
/// already filtered, and 103 carries "TA ONLY"). Every number is invariant-culture.
/// </summary>
internal static class FenixChannelNdTraffic
{
    /// <summary>The name of the traffic layer.</summary>
    public const string LayerName = "Traffic";

    // The trend arrow is drawn from about 600 fpm: seen at -615, not at -562 or +551 (a value chosen inside the gap).
    private const double TrendFpm = 600;

    // The ring is a fixed distance: 62.5 px, which is 2.6 NM at ROSE 20 and 2.64 at ARC 20; at range 10 about 124 px in
    // ARC (47.8 px per NM) and 120-124 px in ROSE (48.3), 2.6 NM again (sitting 3).
    private const double RingRangeNm = 20;
    private const double InnerRingRangeNm = 10;

    /// <summary>The look items of the traffic layer, each a <see cref="FenixChannelNdLook"/> entry.</summary>
    internal static class LookItem
    {
        public const string OtherTraffic = "other traffic";
        public const string ProximateTraffic = "proximate traffic";
        public const string TcasRing = "TCAS ring";
    }

    private sealed record Target(string? Kind, string? Fill, FenixLatLon At, double? AltitudeFt, double VerticalSpeed, double Distance);

    /// <summary>
    /// Layer "Traffic": in ARC, ROSE NAV, ROSE LS and ROSE VOR, the ring first (range 20 or 10, out of standby), then the
    /// targets nearest first, each as "Map: other traffic, hollow white diamond, 8.1 NM, 103° left, +08, climbing". PLAN is
    /// not measured: nothing. A field in another shape gives one row in the layer's place.
    /// </summary>
    public static FenixNdLayer Rows(FenixChannelPush push, FenixNdData header)
    {
        if (header.Mode is not ("NAV" or "ARC" or "LS" or "VOR")) return FenixNdLayer.Of([]);

        var read = new FenixChannelNdRadio.Reader();
        if (!push.TryGet(Remote.Traffic, out _)) return FenixNdLayer.Of([]);

        FenixChannelPush? traffic = read.Part(push, Remote.Traffic, "5");
        bool standby = read.Flag(traffic, TrafficData.Standby, "5.7") ?? false;

        FenixChannelPush? air = read.Part(push, Remote.AdirsCapt, "28");
        double pressure = read.Double(air, AdirsData.PressureAltitude, "28.15") ?? double.NaN;

        var targets = new List<Target>();
        if (traffic is not null)
        {
            Collect(read, traffic, TrafficData.ProximateTraffic, "5.3", LookItem.ProximateTraffic, "filled", targets);
            Collect(read, traffic, TrafficData.OtherTraffic, "5.2", LookItem.OtherTraffic, "hollow", targets);

            // A nested member of 5 other than 2 and 3 was never sent (no TA or RA happened): its targets are drawn, its
            // look is not measured.
            for (int number = 1; number <= 32; number++)
            {
                if (number is TrafficData.OtherTraffic or TrafficData.ProximateTraffic) continue;
                if (traffic.TryGet(number, out FenixChannelField field) && field.Type == FenixWireType.LengthDelimited)
                {
                    Collect(read, traffic, number, $"5.{number}", null, null, targets);
                }
            }
        }

        if (read.Wrong is { } wrong) return FenixNdLayer.Failed(LayerName, $"field {wrong} has the wrong shape");
        if (standby) return FenixNdLayer.Of([]);

        FenixNdFrame? frame = FenixNdFrame.From(header);
        if (frame is null) return FenixNdLayer.Failed(LayerName, "the ND's view is not known");

        var rows = new List<string>();
        if (RingRow(frame) is { } ring) rows.Add(ring);

        foreach (Target target in targets.OrderBy(t => t.Distance))
        {
            if (FenixChannelNdGeometry.PlaceTraffic(frame, target.At) is not { } place) continue;

            var parts = new List<string>();
            parts.Add(target.Kind is null ? "traffic" : target.Kind);
            if (target.Fill is { } fill)
            {
                parts.Add($"{fill} {FenixChannelNdRows.Words(target.Kind!)}");
            }

            parts.Add(FenixChannelNdGeometry.Where(frame, place, null));
            if (Label(target.AltitudeFt, pressure) is { } label) parts.Add(label);
            if (target.VerticalSpeed >= TrendFpm) parts.Add("climbing");
            else if (target.VerticalSpeed <= -TrendFpm) parts.Add("descending");
            rows.Add("Map: " + string.Join(", ", parts));
        }

        return FenixNdLayer.Of(rows);
    }

    private static void Collect(
        FenixChannelNdRadio.Reader read, FenixChannelPush traffic, int number, string path, string? kind, string? fill,
        List<Target> into)
    {
        int index = 0;
        foreach (FenixChannelField entry in traffic.All(number))
        {
            index++;
            FenixChannelPush? target = entry.AsMessage();
            if (target is null)
            {
                read.Fail(path);
                continue;
            }

            string at = $"{path}.";
            float? latitude = read.Float(target, TrafficTarget.Latitude, at + "1");
            float? longitude = read.Float(target, TrafficTarget.Longitude, at + "2");
            float? altitude = read.Float(target, TrafficTarget.Altitude, at + "3");
            float? vertical = read.Float(target, TrafficTarget.VerticalSpeed, at + "4");
            float? distance = read.Float(target, TrafficTarget.DistanceNm, at + "8");

            // A target with no place, or not a number for one, cannot be drawn.
            if (latitude is not { } lat || longitude is not { } lon || !float.IsFinite(lat) || !float.IsFinite(lon)) continue;

            into.Add(new Target(kind, fill, new FenixLatLon(lat, lon),
                altitude is { } a && float.IsFinite(a) ? a : null,
                vertical is { } v && float.IsFinite(v) ? v : 0,
                distance is { } d && float.IsFinite(d) ? d : double.PositiveInfinity));
        }
    }

    // The ring is drawn at ranges 20 and 10, 2.6 NM at both; not at 40 and above.
    private static string? RingRow(FenixNdFrame frame)
    {
        if (frame.RangeNm != RingRangeNm && frame.RangeNm != InnerRingRangeNm) return null;

        string ticks = "12 " + FenixChannelNdRows.Words(LookItem.TcasRing);
        return $"Map: {LookItem.TcasRing}, {ticks} 2.6 NM around the aircraft";
    }

    // round((altitude - pressure altitude) / 100), half away from zero, a sign and two digits; zero is "00".
    private static string? Label(double? altitude, double pressure)
    {
        if (altitude is not { } feet || !double.IsFinite(pressure)) return null;

        int hundreds = (int)Math.Round((feet - pressure) / 100, MidpointRounding.AwayFromZero);
        if (hundreds == 0) return "00";

        return (hundreds > 0 ? "+" : "-") + Math.Abs(hundreds).ToString("D2", CultureInfo.InvariantCulture);
    }
}
