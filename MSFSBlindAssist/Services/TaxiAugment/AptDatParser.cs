namespace MSFSBlindAssist.Services.TaxiAugment;

public static class AptDatParser
{
    // apt.dat 12.00 row formats (verified against OMDB fixture 2026-06-22):
    //   1201 <lat> <lon> <usage> <node_id> [name...]
    //         p[1]  p[2]   p[3]     p[4]    p[5..]
    //   1202 <node1> <node2> <direction> <type>     [name...]
    //         p[1]    p[2]     p[3]       p[4]       p[5..]
    //        type is taxiway_D/taxiway_E/taxiway_F or runway
    //   1300 <lat> <lon> <heading> <type> <airlines> <name...>
    //         p[1]  p[2]   p[3]     p[4]    p[5]      p[6..]

    public static AirportTaxiData Parse(string text)
    {
        var data = new AirportTaxiData { Source = "aptdat" };
        var nodes = new Dictionary<long, (double lat, double lon)>();
        var lines = text.Replace("\r", "").Split('\n');

        // First pass: build node dictionary from 1201 rows
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!line.StartsWith("1201 ")) continue;
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // need at least: 1201 lat lon usage id
            if (p.Length >= 5
                && double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var la)
                && double.TryParse(p[2], System.Globalization.CultureInfo.InvariantCulture, out var lo)
                && long.TryParse(p[4], out var id))
            {
                nodes[id] = (la, lo);
            }
        }

        // Second pass: emit taxiway edges from 1202 rows and parking from 1300 rows
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("1202 "))
            {
                var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                // need at least: 1202 node1 node2 direction type
                if (p.Length < 5) continue;
                if (!long.TryParse(p[1], out var n1) || !long.TryParse(p[2], out var n2)) continue;
                if (!nodes.TryGetValue(n1, out var a) || !nodes.TryGetValue(n2, out var b)) continue;
                // p[4] is the edge type: taxiway_D, taxiway_E, taxiway_F, or runway
                var edgeType = p[4];
                if (!edgeType.StartsWith("taxiway", StringComparison.OrdinalIgnoreCase)) continue;
                // name starts at p[5]
                if (p.Length < 6) continue;
                var name = string.Join(" ", p, 5, p.Length - 5);
                if (string.IsNullOrWhiteSpace(name)) continue;
                data.Taxiways.Add(new NamedTaxiSegment
                {
                    Name = name,
                    Lat1 = a.lat, Lon1 = a.lon,
                    Lat2 = b.lat, Lon2 = b.lon
                });
            }
            else if (line.StartsWith("1300 "))
            {
                var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                // need at least: 1300 lat lon heading type airlines name
                if (p.Length < 7) continue;
                if (!double.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var la)) continue;
                if (!double.TryParse(p[2], System.Globalization.CultureInfo.InvariantCulture, out var lo)) continue;
                var spotName = string.Join(" ", p, 6, p.Length - 6);
                data.Parking.Add((spotName, la, lo));
            }
        }

        return data;
    }
}
