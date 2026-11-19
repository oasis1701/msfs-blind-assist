using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The map's guides (plan 28's <see cref="Bbs757EhsiMapGuides"/>), full reading only. While the map is drawn (MAP with
/// the IRS valid, or PLAN), through the FMS:
/// the two FIX pages (each its on byte, a leg-shaped fix object placed by the leg position rule, its name, its entries
/// vector) and the off-path descent circles (on, both valid bytes, the point, the two radii). In MAP: the altitude range arc
/// while the IRS is valid (the MCP's selected altitude, the air data computer's indicated altitude and vertical speed, the
/// air/ground system's on-ground byte, the selected IRS's ground speed and true track, from the nd's own position; not read
/// otherwise, as the draw uses none of it then) and the VNAV path deviation
/// (the route's path object while its phase is the descent). Walked all or nothing (B757-36): a pointer outside the module,
/// a torn vector, a count over the layout's cap, a name that is not text, a value not finite or out of range is torn. A
/// null object pointer is none. Nothing here is compared with the simulator (B757-30).
/// </summary>
public sealed partial class Bbs757EhsiReader
{
    /// <param name="map">MAP mode: the range arc (while <paramref name="irsValid"/>) and the VNAV deviation are read.</param>
    /// <param name="mapDrawn">The map is drawn (MAP with the IRS valid, or PLAN): the FIX pages and the off-path circles
    /// are read; otherwise no row can show them.</param>
    private bool TryMapGuides(uint nd, uint source, Bbs757EhsiMapGuides g, bool map, bool mapDrawn, bool irsValid, int routeId,
        out Bbs757EhsiMapGuidesReading? reading)
    {
        reading = null;
        var fixes = new List<Bbs757EhsiFix>(2);
        Bbs757EhsiOffpathCircles? offpath = null;
        uint fms = 0;
        if (mapDrawn && !Link(nd, g.NdToFms, out fms)) return false;
        if (fms != 0)
        {
            if (!Good(fms)) return false;
            foreach (var (on, fixAt, entriesAt) in new[] { (g.FmsFix1On, g.FmsFix1, g.FmsFix1Entries), (g.FmsFix2On, g.FmsFix2, g.FmsFix2Entries) })
            {
                if (!TryFix(fms, on, fixAt, entriesAt, g, out var fix)) return false;
                if (fix is not null) fixes.Add(fix);
            }
            if (!TryOffpath(fms, g, out offpath)) return false;
        }

        Bbs757EhsiRangeArc? arc = null;
        double? deviation = null;
        // The range arc's inputs only while the IRS is valid: the draw never uses them otherwise, so they are not read.
        if (map && irsValid && !TryRangeArc(nd, source, g, out arc)) return false;
        if (map && !TryPathDeviation(routeId, g, out deviation)) return false;

        reading = new Bbs757EhsiMapGuidesReading(fixes.AsReadOnly(), offpath, arc, deviation, g.DeviationClamp,
            new Bbs757EhsiRadialDashes(g.RadialStart, g.RadialDashes, g.RadialDashStep, g.RadialDashLength));
        return true;
    }

    /// <summary>
    /// One FIX page: nothing while its on byte is clear or its fix pointer is null. The fix object is leg-shaped (plan 26's
    /// leg block): its kind, its position by the leg position rule (a navaid leg by its capability set,
    /// <see cref="LegPositionOffsets"/>), its name the narrow string at <c>LegNavaidName</c> for a navaid kind, else at
    /// <c>LegName</c>. With the layout lacking the map extras (or this reading taking the legs by plan 26's rule, the
    /// map extras at their tear limit: <see cref="CapabilityRule"/>) the capability rule is unknown, and a navaid fix's own
    /// offsets read (0, 0) for a TACAN (measured: TCM): it is then given no position rather than a wrong one. False when
    /// torn.
    /// </summary>
    private bool TryFix(uint fms, int on, int fixAt, int entriesAt, Bbs757EhsiMapGuides g, out Bbs757EhsiFix? fix)
    {
        fix = null;
        var l = Layout;
        if (!Flag(fms, on, out bool shown)) return false;
        if (!shown) return true;
        if (!Link(fms, fixAt, out uint at)) return false;
        if (at == 0) return true;
        var block = new byte[l.LegStride];
        if (!Good(at) || !_memory.TryRead(at, block) || !BlockInt(block, l.LegKind, out int kind)) return false;
        bool navaid = g.NavaidLegKinds.Contains(kind);
        // Both name offsets are inside the leg, checked once with the layout (Bbs757EhsiReader.Tears.cs).
        int nameAt = navaid ? g.LegNavaidName : g.LegName;
        if (!TryReadNarrow(block.AsSpan(nameAt, StringBytes), out string name)) return false;

        Bbs757EhsiMapPoint? position = null;
        if (!navaid || CapabilityRule is not null)
        {
            if (!LegPositionOffsets(block, kind, out int latitudeAt, out int longitudeAt)
                || !BlockDouble(block, latitudeAt, out double lat) || !BlockDouble(block, longitudeAt, out double lon)
                || !InRange(lat, lon))
                return false;
            position = new Bbs757EhsiMapPoint(lat, lon);
        }
        if (!TryFixEntries(fms, entriesAt, g, out var entries)) return false;
        fix = new Bbs757EhsiFix(name, position, entries);
        return true;
    }

    /// <summary>A FIX page's entries vector: its first entries up to the layout's <c>FixEntries</c> (the draw takes that
    /// many and stops, so a longer vector is not torn: the rest is not on the screen and not read), each a radial and a
    /// distance, the layout's none value read as null. Torn: end before begin, not a whole number of entries, storage
    /// outside the module (all of it, the unread rest too), a vector changed while read, a radial outside 0 to 360 or a
    /// negative distance that is not the none value.</summary>
    private bool TryFixEntries(uint fms, int entriesAt, Bbs757EhsiMapGuides g, out IReadOnlyList<Bbs757EhsiFixEntry> entries)
    {
        entries = Array.Empty<Bbs757EhsiFixEntry>();
        // The stride and the offsets inside it are checked once, with the layout (Bbs757EhsiReader.Tears.cs).
        int stride = g.FixEntryStride;
        if (!Header(fms, entriesAt, out var header) || !Entries(header, stride, int.MaxValue, out uint stored)
            || (stored > 0 && !_memory.IsInsideModule(header.Begin, header.End - header.Begin)))
            return false;
        uint count = Math.Min(stored, (uint)Math.Max(0, g.FixEntries));
        var list = new List<Bbs757EhsiFixEntry>((int)count);
        if (count > 0)
        {
            var bytes = new byte[count * stride];
            if (!_memory.TryRead(header.Begin, bytes)) return false;
            for (int i = 0; i < count; i++)
            {
                var span = bytes.AsSpan(i * stride, stride);
                int radial = BinaryPrimitives.ReadInt32LittleEndian(span[g.FixEntryRadial..]);
                int distance = BinaryPrimitives.ReadInt32LittleEndian(span[g.FixEntryDistance..]);
                int? r = radial == g.FixEntryNone ? null : radial;
                int? d = distance == g.FixEntryNone ? null : distance;
                if (r is < 0 or > 360 || d is < 0) return false;
                list.Add(new Bbs757EhsiFixEntry(r, d));
            }
        }
        if (!Header(fms, entriesAt, out var after) || after != header) return false;
        entries = list.AsReadOnly();
        return true;
    }

    /// <summary>The OFFPATH DES circles: drawn while the on byte and both valid bytes are set and the point is not (0, 0)
    /// (the draw's test); the point then in range and both radii finite and not negative, else torn.</summary>
    private bool TryOffpath(uint fms, Bbs757EhsiMapGuides g, out Bbs757EhsiOffpathCircles? circles)
    {
        circles = null;
        if (!Flag(fms, g.OffpathOn, out bool on) || !Flag(fms, g.OffpathValidA, out bool validA) || !Flag(fms, g.OffpathValidB, out bool validB))
            return false;
        if (!on || !validA || !validB) return true;
        if (!F64(fms, g.OffpathLatitude, out double lat) || !F64(fms, g.OffpathLongitude, out double lon)
            || !F64(fms, g.OffpathCleanRadius, out double clean) || !F64(fms, g.OffpathSpeedbrakeRadius, out double speedbrake))
            return false;
        if (lat == 0 && lon == 0) return true;
        if (!InRange(lat, lon) || !double.IsFinite(clean) || !double.IsFinite(speedbrake) || clean < 0 || speedbrake < 0) return false;
        circles = new Bbs757EhsiOffpathCircles(new Bbs757EhsiMapPoint(lat, lon), clean, speedbrake);
        return true;
    }

    /// <summary>The altitude range arc's inputs, its distance by <see cref="RangeArcDistance"/> and its point that far along
    /// the selected IRS's true track from the nd's own position (the draw's nd+Longitude, not the simulator's). A null air
    /// data computer or air/ground system: no arc. A value that is not a number is torn.</summary>
    private bool TryRangeArc(uint nd, uint source, Bbs757EhsiMapGuides g, out Bbs757EhsiRangeArc? arc)
    {
        arc = null;
        var l = Layout;
        if (!Link(nd, l.NdToMcp, out uint mcp) || !Good(mcp) || !F64(mcp, g.McpAltitude, out double selected)) return false;
        if (!Link(source, g.SourceToAdc, out uint adc) || !Link(nd, g.NdToAirGround, out uint airGround)) return false;
        if (adc == 0 || airGround == 0) return true;
        if (!Good(adc) || !F64(adc, g.AdcIndicatedAltitude, out double altitude) || !F64(adc, g.AdcVerticalSpeed, out double verticalSpeed)
            || !Good(airGround) || !Flag(airGround, g.AirGroundOnGround, out bool onGround)
            || !Link(source, l.SourceToIrs, out uint irs) || !Good(irs)
            || !F64(irs, l.IrsGroundSpeed, out double groundSpeed) || !F64(irs, g.IrsTrueTrack, out double track)
            || !F64(nd, l.Latitude, out double lat) || !F64(nd, l.Longitude, out double lon)
            || !AllFinite(selected, altitude, verticalSpeed, groundSpeed, track) || !InRange(lat, lon))
            return false;
        if (RangeArcDistance(selected, altitude, verticalSpeed, groundSpeed, onGround, g) is not { } nm) return true;
        var (pointLat, pointLon) = Bbs757EhsiGeometry.Destination(lat, lon, track, nm);
        arc = new Bbs757EhsiRangeArc(nm, new Bbs757EhsiMapPoint(pointLat, pointLon));
        return true;
    }

    /// <summary>
    /// BlueBird's altitude range arc (23453): drawn when the selected altitude differs from the indicated one by more than
    /// the layout's <c>ArcMinAltitudeDifference</c>, the vertical speed is at least <c>ArcMinVerticalSpeed</c> in magnitude
    /// and toward the selected altitude, and the aircraft is not on the ground. Its distance in NM is the time to the
    /// selected altitude, |difference ÷ (vertical speed ÷ 60)| seconds, times the ground speed ÷ 3600. Null when not drawn.
    /// </summary>
    internal static double? RangeArcDistance(double selected, double altitude, double verticalSpeed, double groundSpeed,
        bool onGround, Bbs757EhsiMapGuides g)
    {
        double difference = selected - altitude;
        if (onGround || !(Math.Abs(difference) > g.ArcMinAltitudeDifference) || !(Math.Abs(verticalSpeed) >= g.ArcMinVerticalSpeed)
            || Math.Sign(verticalSpeed) != Math.Sign(difference))
            return null;
        return Math.Abs(difference / (verticalSpeed / 60.0)) * groundSpeed / 3600.0;
    }

    /// <summary>The VNAV path deviation in feet as stored: none without a path for the route or while its phase is not the
    /// layout's descent value; a value that is not a number is torn.</summary>
    private bool TryPathDeviation(int routeId, Bbs757EhsiMapGuides g, out double? deviation)
    {
        deviation = null;
        if (!TryFindPath(routeId, out uint path, out bool found)) return false;
        if (!found) return true;
        if (!Int(path, g.PathPhase, out int phase)) return false;
        if (phase != g.DescentPhase) return true;
        if (!F64(path, g.PathDeviation, out double feet) || !double.IsFinite(feet)) return false;
        deviation = feet;
        return true;
    }
}
