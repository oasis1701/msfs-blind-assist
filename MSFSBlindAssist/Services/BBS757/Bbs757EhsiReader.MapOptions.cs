using System.Buffers.Binary;
using System.Text;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The map options (plan 28's <see cref="Bbs757EhsiMapOptions"/>), read in a full reading in MAP and PLAN: the EFIS
/// switches, the tuned stations, the navaid, airport, waypoint and NDB lists each switched-on list needs (walked all or
/// nothing, B757-36), the VOR course lines' sources, and BlueBird's loop replayed over them
/// (<see cref="Bbs757EhsiMapOptionsLoop"/>). A list's vector is checked whole (end before begin, not whole entries,
/// storage null with entries, more entries than the layout's <c>MaxItems</c>: torn) and read in one block, but only its
/// first <c>RoundLimit</c> entries, which are all the loop can reach; each record is read once. A pointer outside the
/// module, a record that is not text where a name is or a position not finite and in range is torn; a null object
/// (navigation database, FMS, panel, station) or a null list entry is none, not torn.
/// </summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>The app's own read size for a NUL-terminated ident: this many code points in the record's first read, the
    /// rest (up to <see cref="MaxLabelLength"/>) read only when no NUL is among them. Not a BlueBird value.</summary>
    private const int IdentChunk = 8;

    /// <summary>One navaid record as stored.</summary>
    private readonly record struct NavaidRecord(uint Address, string Ident, ushort Frequency, int Kind, int Class,
        double Latitude, double Longitude)
    {
        public Bbs757EhsiMapOption Item => new(Bbs757EhsiMapItemKind.Navaid, Ident, Kind, Latitude, Longitude);
    }

    private delegate bool RecordReader<T>(uint at, out T value);

    /// <summary>What the loop measures against, from a full reading's own values: the nd's map and aircraft position, the
    /// points of the route (flown and own legs) and of a pending modification (discontinuities have none), and the
    /// origin's and destination's names (an empty name is not among the points, and BlueBird compares with it too).</summary>
    private static Bbs757EhsiLoopScene MapScene(Bbs757EhsiMap map, double range, double latitude, double longitude,
        IReadOnlyList<Bbs757EhsiLeg>? route, IReadOnlyList<Bbs757EhsiLeg>? modification, IReadOnlyList<Bbs757EhsiPoint>? points)
    {
        var routePoints = new HashSet<(double Latitude, double Longitude)>();
        foreach (var leg in (route ?? Array.Empty<Bbs757EhsiLeg>()).Concat(modification ?? Array.Empty<Bbs757EhsiLeg>()))
            if (!leg.Discontinuity) routePoints.Add((leg.Latitude, leg.Longitude));
        var airports = (points ?? Array.Empty<Bbs757EhsiPoint>()).Where(p => p.Airport).Select(p => p.Word).ToList();
        if (airports.Count < 2) airports.Add("");
        return new Bbs757EhsiLoopScene(map, range, latitude, longitude, airports, routePoints);
    }

    private bool TryMapOptions(uint nd, Bbs757EhsiMapOptions o, Bbs757EhsiLoopScene scene, out Bbs757EhsiMapOptionsReading? reading)
    {
        reading = null;
        if (!Flag(nd, o.NavAidSwitch, out bool navAid) || !Flag(nd, o.AirportSwitch, out bool airport)
            || !Flag(nd, o.WaypointSwitch, out bool waypoint)
            || !Link(nd, o.TunedLeft, out uint tunedLeft) || !Link(nd, o.TunedRight, out uint tunedRight)
            || !Link(nd, o.NdToSuper, out uint super) || !Link(nd, o.NdToFms, out uint fms))
            return false;
        if ((super != 0 && !Good(super)) || (fms != 0 && !Good(fms))) return false;

        // The tuned stations: drawn green with tuned set, every drawn kind at any range, never counted.
        var tuned = new List<Bbs757EhsiMapOption>(2);
        foreach (uint station in new[] { tunedLeft, tunedRight })
        {
            if (station == 0) continue;
            if (!Good(station) || !TryNavaid(station, out var record)) return false;
            if (o.DrawnKinds.Contains(record.Kind)) tuned.Add(record.Item);
        }

        // The navigation database is embedded in the b757_super: an address, not a pointer.
        uint navdb = 0;
        if (super != 0 && !At(super, o.SuperToNavdb, out navdb)) return false;

        IReadOnlyList<Bbs757EhsiLoopNavaid?> navaids = Array.Empty<Bbs757EhsiLoopNavaid?>();
        IReadOnlyList<Bbs757EhsiLoopAirport> airports = Array.Empty<Bbs757EhsiLoopAirport>();
        IReadOnlyList<Bbs757EhsiMapOption?> waypoints = Array.Empty<Bbs757EhsiMapOption?>();
        IReadOnlyList<Bbs757EhsiMapOption?> ndbs = Array.Empty<Bbs757EhsiMapOption?>();
        if (navAid && navdb != 0)
        {
            var list = WalkPointers<NavaidRecord>(navdb, o.Navaids, o, TryNavaid);
            if (list is null) return false;
            navaids = list.Select(n => n is { } r ? new Bbs757EhsiLoopNavaid(r.Address, r.Class, r.Item) : (Bbs757EhsiLoopNavaid?)null).ToArray();
        }
        if (airport && fms != 0)
        {
            var list = WalkAirports(fms, o);
            if (list is null) return false;
            airports = list;
        }
        if (waypoint && navdb != 0)
        {
            bool ndbList = scene.Range > o.NdbsAbove;
            RecordReader<Bbs757EhsiMapOption> place = ndbList
                ? (uint at, out Bbs757EhsiMapOption item) => TryPlace(at, Bbs757EhsiMapItemKind.Ndb, o.NdbIdent, o.NdbLatitude, o.NdbLongitude, out item)
                : (uint at, out Bbs757EhsiMapOption item) => TryPlace(at, Bbs757EhsiMapItemKind.Waypoint, o.WaypointIdent, o.WaypointLatitude, o.WaypointLongitude, out item);
            var list = WalkPointers(navdb, ndbList ? o.Ndbs : o.Waypoints, o, place);
            if (list is null) return false;
            if (ndbList) ndbs = list;
            else waypoints = list;
        }

        var courseLines = new List<Bbs757EhsiCourseLine>(2);
        if (fms != 0 && !TryCourseLines(fms, o, courseLines)) return false;

        var (drawn, count) = Bbs757EhsiMapOptionsLoop.Run(
            new Bbs757EhsiLoopLists(navAid, airport, waypoint, navaids, airports, waypoints, ndbs, tunedLeft, tunedRight),
            scene,
            new Bbs757EhsiLoopRules(o.DrawnKinds, o.HighClass, o.HighClassAbove, o.NdbsAbove, o.NdbDistance,
                Layout.MarkerMargin, Layout.ExcessLimit, o.RoundLimit));
        reading = new Bbs757EhsiMapOptionsReading(navAid, airport, waypoint, tuned.AsReadOnly(), drawn, count,
            courseLines.AsReadOnly(), new Bbs757EhsiNavaidKinds(o.KindVor, o.KindVorDme, o.KindVortac, o.KindTacan, o.KindDme));
        return true;
    }

    /// <summary>A vector of record pointers at <paramref name="b"/> + <paramref name="offset"/>: its first
    /// <c>RoundLimit</c> entries (null for a null pointer), or null when torn, also when the vector changed while it was
    /// walked.</summary>
    private List<T?>? WalkPointers<T>(uint b, int offset, Bbs757EhsiMapOptions o, RecordReader<T> read) where T : struct
    {
        if (!Header(b, offset, out var header) || !Entries(header, Word, o.MaxItems, out uint count)) return null;
        int take = (int)Math.Min(count, (uint)Math.Max(0, o.RoundLimit));
        var list = new List<T?>(take);
        if (take > 0)
        {
            var pointers = new byte[take * Word];
            if (!_memory.TryRead(header.Begin, pointers)) return null;
            for (int i = 0; i < take; i++)
            {
                uint p = BinaryPrimitives.ReadUInt32LittleEndian(pointers.AsSpan(i * Word));
                if (p == 0)
                {
                    list.Add(null);
                    continue;
                }
                if (!Good(p) || !read(p, out T value)) return null;
                list.Add(value);
            }
        }
        if (!Header(b, offset, out var after) || after != header) return null;
        return list;
    }

    /// <summary>How many <paramref name="stride"/>-byte entries a vector holds; false when torn.</summary>
    private static bool Entries(Vector v, int stride, int max, out uint count)
    {
        count = 0;
        if (stride <= 0 || v.End < v.Begin || (v.End - v.Begin) % (uint)stride != 0) return false;
        count = (v.End - v.Begin) / (uint)stride;
        return count <= (uint)Math.Max(0, max) && (count == 0 || v.Begin != 0);
    }

    /// <summary>A navaid record, in one read: its ident (the layout's length in UTF-32 code points, ended by the first 0),
    /// frequency, kind, class and position.</summary>
    private bool TryNavaid(uint at, out NavaidRecord record)
    {
        record = default;
        // The ident's length and the offsets are checked once, with the layout (Bbs757EhsiReader.Tears.cs).
        var o = Layout.MapOptions!;
        int size = Max(o.NavaidIdent + o.NavaidIdentLength * Word, o.NavaidFrequency + 2, o.NavaidKind + 1, o.NavaidClass + 1,
            o.NavaidLongitude + 2 * Word, o.NavaidLatitude + 2 * Word);
        var block = new byte[size];
        if (!_memory.TryRead(at, block)
            || !BlockDouble(block, o.NavaidLatitude, out double lat) || !BlockDouble(block, o.NavaidLongitude, out double lon)
            || !InRange(lat, lon) || !TryCodePoints(block.AsSpan(o.NavaidIdent, o.NavaidIdentLength * Word), out string ident, out _))
            return false;
        record = new NavaidRecord(at, ident, BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(o.NavaidFrequency)),
            block[o.NavaidKind], block[o.NavaidClass], lat, lon);
        return true;
    }

    /// <summary>A waypoint or NDB record: its position and its NUL-terminated UTF-32 ident (at most
    /// <see cref="MaxLabelLength"/> code points; none within that is torn), the record and the ident's first
    /// <see cref="IdentChunk"/> code points in one read.</summary>
    private bool TryPlace(uint at, Bbs757EhsiMapItemKind kind, int identAt, int latitudeAt, int longitudeAt, out Bbs757EhsiMapOption item)
    {
        item = default;
        var block = new byte[Max(identAt + IdentChunk * Word, latitudeAt + 2 * Word, longitudeAt + 2 * Word)];
        if (!_memory.TryRead(at, block) || !BlockDouble(block, latitudeAt, out double lat)
            || !BlockDouble(block, longitudeAt, out double lon) || !InRange(lat, lon))
            return false;
        if (!TryCodePoints(block.AsSpan(identAt, IdentChunk * Word), out string ident, out bool ended)) return false;
        if (!ended)
        {
            // No NUL in the first read: the whole guard's length, once.
            var longer = new byte[(MaxLabelLength + 1) * Word];
            if (!At(at, identAt, out uint identAddress) || !_memory.TryRead(identAddress, longer)
                || !TryCodePoints(longer, out ident, out ended) || !ended)
                return false;
        }
        item = new Bbs757EhsiMapOption(kind, ident, -1, lat, lon);
        return true;
    }

    /// <summary>UTF-32 code points up to the first 0 (<paramref name="ended"/>: one was found) or the span's end; false
    /// for a code point that is not a Unicode scalar value.</summary>
    private static bool TryCodePoints(ReadOnlySpan<byte> bytes, out string text, out bool ended)
    {
        text = "";
        ended = false;
        var sb = new StringBuilder();
        Span<char> utf16 = stackalloc char[2];
        for (int i = 0; i + Word <= bytes.Length; i += Word)
        {
            uint codePoint = BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]);
            if (codePoint == 0)
            {
                ended = true;
                break;
            }
            if (!Rune.TryCreate(codePoint, out Rune r)) return false;
            sb.Append(utf16[..r.EncodeToUtf16(utf16)]);
        }
        text = sb.ToString();
        return true;
    }

    /// <summary>The airport records (fms + <c>Airports</c>, records inline): the first <c>RoundLimit</c> in one read, or null
    /// when torn. Each one's name is a narrow <c>std::string</c>; its label is the layout's first characters.</summary>
    private List<Bbs757EhsiLoopAirport>? WalkAirports(uint fms, Bbs757EhsiMapOptions o)
    {
        int stride = o.AirportStride;
        if (!Header(fms, o.Airports, out var header) || !Entries(header, stride, o.MaxItems, out uint count))
            return null;
        int take = (int)Math.Min(count, (uint)Math.Max(0, o.RoundLimit));
        var list = new List<Bbs757EhsiLoopAirport>(take);
        if (take > 0)
        {
            var records = new byte[take * stride];
            if (!_memory.TryRead(header.Begin, records)) return null;
            for (int i = 0; i < take; i++)
            {
                var record = records.AsSpan(i * stride, stride).ToArray();
                if (!BlockDouble(record, o.AirportRecordLatitude, out double lat) || !BlockDouble(record, o.AirportRecordLongitude, out double lon)
                    || !InRange(lat, lon) || !TryReadNarrow(record.AsSpan(o.AirportName, StringBytes), out string name))
                    return null;
                string label = name.Length > o.AirportLabelLength ? name[..o.AirportLabelLength] : name;
                list.Add(new Bbs757EhsiLoopAirport(name, new Bbs757EhsiMapOption(Bbs757EhsiMapItemKind.Airport, label, -1, lat, lon)));
            }
        }
        if (!Header(fms, o.Airports, out var after) || after != header) return null;
        return list;
    }

    /// <summary>A narrow <c>std::string</c> given its 12 bytes, in the same libc++ alternate layout as the UTF-32 ones
    /// (<see cref="TryReadString"/>): byte +11's high bit is the long flag (data pointer +0, size +4), else byte +11 is the
    /// size and the characters are inline. At most <see cref="MaxLabelLength"/> characters, each printable ASCII; false
    /// when torn.</summary>
    private bool TryReadNarrow(ReadOnlySpan<byte> s, out string text)
    {
        text = "";
        byte[] chars;
        if ((s[StringBytes - 1] & 0x80) == 0)
        {
            int length = s[StringBytes - 1];
            if (length > StringBytes - 2) return false;
            chars = s[..length].ToArray();
        }
        else
        {
            uint data = BinaryPrimitives.ReadUInt32LittleEndian(s);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(s[Word..]);
            if (size > MaxLabelLength) return false;
            chars = new byte[size];
            if (size > 0 && !_memory.TryRead(data, chars)) return false;
        }
        foreach (byte c in chars)
            if (c is < 0x20 or >= 0x7f) return false;
        text = Encoding.ASCII.GetString(chars);
        return true;
    }

    /// <summary>
    /// The VOR course lines (f23431), VOR L then VOR R: drawn only for a manually tuned receiver (the autotune byte clear),
    /// through its station when the station's kind is one of the layout's course line kinds, the course is a number and the
    /// station's frequency equals the receiver's manual one (whole × 100 + fraction). VOR R is skipped when it has VOR L's
    /// station and the same course modulo 180 (one line), as measured in the draw (23431): the second receiver is compared
    /// only with a line already drawn, and BlueBird's "same station" is the same frequency, the same ident and a position
    /// within 1e-6, which the reader takes as the same station record (pointer). Two conditions BlueBird also tests are not
    /// modeled: the station in reception (f47465), and the VOR panel's electrical power (f36730, tested in BlueBird's
    /// station getter: without power the getter returns no station while the panel's field keeps it). So a station out of
    /// range, or on an unpowered panel, still reads as drawn. A null panel or station is no line; one outside the module
    /// is torn.
    /// </summary>
    private bool TryCourseLines(uint fms, Bbs757EhsiMapOptions o, List<Bbs757EhsiCourseLine> lines)
    {
        uint firstStation = 0;
        double firstCourse = double.NaN;
        foreach (var (offset, left) in new[] { (o.FmsToVorLeft, true), (o.FmsToVorRight, false) })
        {
            if (!Link(fms, offset, out uint panel)) return false;
            if (panel == 0) continue;
            if (!Good(panel) || !Flag(panel, o.PanelAutotune, out bool autotune)) return false;
            if (autotune) continue;
            if (!Link(panel, o.PanelStation, out uint station) || !F64(panel, o.PanelCourse, out double course)
                || !Int(panel, o.PanelManualWhole, out int whole) || !Int(panel, o.PanelManualFraction, out int fraction))
                return false;
            if (station == 0) continue;
            if (!Good(station) || !TryNavaid(station, out var record)) return false;
            if (!o.CourseLineKinds.Contains(record.Kind) || !double.IsFinite(course)
                || record.Frequency != (long)whole * 100 + fraction)
                continue;
            double rounded = Math.Round(course, MidpointRounding.AwayFromZero);
            if (!left && station == firstStation && Mod(rounded, 180) == Mod(firstCourse, 180)) continue;
            if (left)
            {
                firstStation = station;
                firstCourse = rounded;
            }
            lines.Add(new Bbs757EhsiCourseLine(left, course, record.Ident, record.Latitude, record.Longitude));
        }
        return true;
    }

    private static double Mod(double value, double by) => ((value % by) + by) % by;

    private static int Max(params int[] values) => values.Max();
}
