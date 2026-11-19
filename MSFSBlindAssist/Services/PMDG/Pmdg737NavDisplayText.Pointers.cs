using static System.FormattableString;
using G = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayGeometry;

namespace MSFSBlindAssist.Services.PMDG;

// The bearing pointers' rows.
public static partial class Pmdg737NavDisplayText
{
    /// <summary>
    /// The bearing pointers, in every mode but PLN: per receiver the pointer PMDG draws, "VOR 1 pointer" or "ADF 2
    /// pointer" (the corners' labels), by where its head points off the top of the screen. Without the layout's
    /// part, one row saying why while a switch selects a VOR or an ADF, where PMDG would draw one.
    /// </summary>
    private static IEnumerable<string> PointerRows(Context c, string prefix)
    {
        var s = c.S;
        if (c.Plan) yield break;
        if (c.L.Pointers is not { } p || s.Pointers is not { } readings)
        {
            var rx = c.L.Receivers;
            if (c.L.Pointers is null && (s.Receivers.Any(r => r.Switch == rx.VorPosition || r.Switch == rx.AdfPosition) || (c.Map && s.Panel.Pos)))
                yield return "Pointers and POS: not read: " + FmaReasons.FileNotRecognised;
            yield break;
        }
        bool placed = G.UpDirectionDeg(s, c.L) is not null;
        for (int k = 0; k < Math.Min(readings.Count, s.Receivers.Count); k++)
        {
            var r = s.Receivers[k];
            bool vor = p.VorDrawn(r, readings[k].VorValid);
            if (!vor && !p.AdfDrawn(r, readings[k].AdfPowered)) continue;
            string row = prefix + Invariant($"{(vor ? "VOR" : "ADF")} {k + 1} pointer");
            yield return placed ? row + ", " + Direction(G.NoseRelativeDeg(readings[k].Bearing, s, c.L)) : row;
        }
    }

    /// <summary>
    /// POS in MAP while its button is on: a line toward each
    /// tuned VOR while the readout is valid, by the station's place on the map, with PMDG's radial label (full
    /// format) or its receiver label; a line toward each ADF received, by its bearing from the nose, with its
    /// label; the IRS and GPS positions where the map shows them ("IRS position" and "GPS position" are the app's
    /// words: PMDG draws symbols only).
    /// </summary>
    private static IEnumerable<string> PosRows(Placer place, Context c)
    {
        var s = c.S;
        if (!c.Map || !s.Panel.Pos || c.L.Pointers is not { } p || s.Pos is not { } pos || s.Pointers is not { } readings) yield break;
        var f = p.Pos;
        for (int k = 0; k < Math.Min(Math.Min(s.Tuned.Count, s.Receivers.Count), pos.VorRadials.Count); k++)
        {
            var t = s.Tuned[k];
            if (!s.ReadoutValid || !p.VorTuned(s.Receivers[k], t) || t.Position is not { } station) continue;
            string label = c.Full ? RadialLabel(f, pos.VorRadials[k]) : f.VorLabels.For(k);
            yield return Invariant($"Map: VOR {k + 1} line toward {t.Ident}, {label}, {Direction(place.At(station).Relative)}");
        }
        for (int k = 0; k < Math.Min(Math.Min(readings.Count, s.Receivers.Count), pos.AdfBearings.Count); k++)
            if (p.AdfReceiving(s.Receivers[k], readings[k].AdfPowered))
                yield return Invariant($"Map: ADF {k + 1} line, {f.AdfLabels.For(k)}, {Direction(G.NoseRelativeDeg(pos.AdfBearings[k], s, c.L))}");
        foreach (var (name, point) in new[] { ("IRS position", pos.Irs), ("GPS position", pos.Gps) })
            if (point is { } at && place.At(at) is { OnMap: true } on) yield return $"Map: {name}, {place.Where(on)}";
    }

    /// <summary>PMDG's radial label: its format of (int)(wrap360(radial) + 0.5), 0 printed as its constant.</summary>
    private static string RadialLabel(NdPosFields f, double radial)
    {
        int r = (int)Math.Floor((((radial % 360) + 360) % 360) + 0.5);
        if (r == 0) r = f.RadialZeroAs;
        return f.RadialFormat.Replace("%03d", r.ToString("000", Inv), StringComparison.Ordinal);
    }
}
