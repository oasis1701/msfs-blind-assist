using System.Text;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 737 module's memory for the navigation display's tests: every object the layout names at its real
/// size, each field placed through the ND fixture's golden layout (never by literal offsets),
/// holding the live reading of 2026-10-25 (the -800 cold on the ground, the Captain's ND in MAP at range
/// index 2: the route LPPT, then EMPAD 6.8 NM 1533.3Z (the TO waypoint), PT753 23.0 1537.2Z, IDBID 30.5
/// 1541.5Z, ORVED 18.1 1543.9Z; the aircraft at 38.77427 N 9.13153 W (the spike read PMDG's west-positive
/// longitude as east) with the sim's variation, -1.5; EMPAD 8.1 NM straight-line at 335° true from it, as
/// measured; heading 79.4, wind 351 / 13.8).
/// Tests change it through the helpers, then read it with <see cref="Source"/>.
/// </summary>
internal sealed class NdTestMemory
{
    public const ulong Base = 0x10000000;
    public const uint Efis = 0x300000, Glass = 0x320000, Sim = 0x330000, Tcas = 0x360000, Gpws = 0x370000, Weather = 0x380000,
        Isdu = 0x390000, Options = 0x3A0000, Misc = 0x3B0000, Cdu = 0x3C0000, Generator = 0x3E8000, Iru = 0x3F0000, Nodes = 0x400000, ListObjects = 0x500000,
        NavaidElements = 0x580000, FixElements = 0x590000, AirportElements = 0x5A0000, TrafficList = 0x600000, FixPageArea = 0x700000,
        Fms = 0x800000;
    public const int Size = 0xE50000;

    /// <summary>Room for a route of more than the walk's cap below the list objects.</summary>
    public const uint NodeSpacing = 0x500;

    /// <summary>The live aircraft (degrees, east positive).</summary>
    public const double AircraftLatitude = 38.77427, AircraftLongitude = -9.13153;

    /// <summary>The point <paramref name="nm"/> along the great circle from a point at an initial true bearing
    /// (degrees, east positive; earth radius 3440.065 NM).</summary>
    public static (double LatitudeDeg, double LongitudeDeg) Destination(double latitudeDeg, double longitudeDeg, double nm, double bearingTrueDeg)
    {
        double d = nm / 3440.065, b = bearingTrueDeg * Math.PI / 180, lat = latitudeDeg * Math.PI / 180, lon = longitudeDeg * Math.PI / 180;
        double lat2 = Math.Asin(Math.Sin(lat) * Math.Cos(d) + Math.Cos(lat) * Math.Sin(d) * Math.Cos(b));
        double lon2 = lon + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(lat), Math.Cos(d) - Math.Sin(lat) * Math.Sin(lat2));
        return (lat2 * 180 / Math.PI, lon2 * 180 / Math.PI);
    }

    public static readonly Pmdg737NavDisplayLayout Layout = GoldenLayouts.NavDisplay;

    private static NdRouteFields R => Layout.Route;

    public readonly byte[] Bytes = new byte[Size];
    private uint _nextObject = ListObjects;

    /// <summary>The live route's nodes, in route order: LPPT, EMPAD (TO), PT753, IDBID, ORVED.</summary>
    public readonly uint[] RouteNodes = new uint[5];

    public NdTestMemory()
    {
        var o = Layout.Objects;
        foreach (var (obj, at) in new[]
        {
            (o.Efis, Efis), (o.Fms, Fms), (o.Glass, Glass), (o.Sim, Sim), (o.Tcas, Tcas), (o.Gpws, Gpws),
            (o.Weather, Weather), (o.Isdu, Isdu), (o.Options, Options), (o.Misc, Misc),
        })
            U(obj.Global, at);
        U(Layout.FixPage.CduGlobal, Cdu);
        // The ND generator, read only for the heading line's time (its own field, PMDG's alone).
        U(Layout.Symbols.HeadingLine.Generator.Global, Generator);

        // The Captain's panel (index 0): MAP, range index 2, powered.
        I(Efis + (uint)Layout.Panel.PanelIndexOffset, 0);
        I(At(Panel, Layout.Panel.Mode), Layout.Geometry.Modes.Map);
        I(At(Panel, Layout.Panel.Range), 2);
        B(At(Panel, Layout.Panel.Powered), true);
        // The display units: unit 0 shows the full ND, the others page 1.
        var u = Layout.Units;
        for (int k = 0; k < u.UnitCount; k++)
        {
            I(Efis + (uint)(k * u.UnitStride + u.IndexOffset), k);
            I(Efis + (uint)(k * u.UnitStride + u.PageOffset), k == 0 ? u.FullNdPages[0] : 1);
        }
        I(At(Efis, u.LowerSelector, u.SelectorSide), 2);

        // Orientation: heading and track 79.4, MAG, variation -1.5; the IRU in NAV (6); the readout valid;
        // PMDG's track-up option set (the live MAP at KATL printed TRK).
        var or = Layout.Orientation;
        B(At(Options, or.TrackUpOption), true);
        D(At(Glass, or.Heading), 79.4);
        D(At(Glass, or.Track), 79.4);
        B(At(Efis, or.Magnetic), true);
        D(At(Sim, or.MagneticVariation), -1.5);
        I(At(Efis, or.IruIndex), 0);
        U(At(Isdu, or.IruPointers), Iru);
        I(At(Iru, or.IruCondition), 6);
        B(At(Glass, or.ReadoutValid), true);
        // The map's validity: every unit on DEU 0 (the bytes' default), the EFIS object's FMC entries 0 and 1 (FMC L for
        // DEU 0, FMC R for DEU 1), both FMCs with an FMS position (live at KATL: FMC 0, its byte 1).
        var v = Layout.MapValidity;
        I(At(Efis, v.FmcIndex, 1), 1);
        B(Fms + (uint)v.FmcValid, true);
        B(Fms + (uint)v.FmcValid + 1, true);

        // The aircraft, the reference route (plan 0, active) and its nodes.
        Position(Fms + (uint)R.FmsPosition, AircraftLatitude, AircraftLongitude, variationDeg: -1.5);
        I(Fms + (uint)R.RefRoute, 0);
        B(Plan(0) + (uint)R.PlanActive, true);
        RouteNodes[0] = AddNode(0, "LPPT", 0, null, 38.7813, -9.1359);
        var empad = Destination(AircraftLatitude, AircraftLongitude, 8.1, 335);
        RouteNodes[1] = AddNode(1, "EMPAD", 6.8, new NdClock(15, 33, 18), empad.LatitudeDeg, empad.LongitudeDeg);
        RouteNodes[2] = AddNode(2, "PT753", 23.0, new NdClock(15, 37, 12), 39.10, -8.70);
        RouteNodes[3] = AddNode(3, "IDBID", 30.5, new NdClock(15, 41, 30), 39.45, -8.20);
        RouteNodes[4] = AddNode(4, "ORVED", 18.1, new NdClock(15, 43, 54), 39.70, -7.90);
        Chain(RouteNodes);
        SetKeys(0, RouteNodes[0], RouteNodes[1]);
        SetPath(0, RouteNodes);

        // The DATA text's transition altitude and level; RNP and ANP on the CDU's position page.
        I(Fms + (uint)(R.DataText.Performance + R.DataText.TransitionAltitude), 4000);
        I(Fms + (uint)(R.DataText.Performance + R.DataText.TransitionLevel), 6000);
        D(Fms + (uint)(Layout.Messages.Rnp.PosData + Layout.Messages.Rnp.Rnp), 2.0);
        D(Fms + (uint)(Layout.Messages.Rnp.PosData + Layout.Messages.Rnp.Anp), 0.05);

        // The top left, the heading bug and the altitude arc's inputs.
        var c = Layout.Corners;
        D(At(Sim, c.TrueAirspeed), 0.4);
        D(At(Sim, c.WindDirection), 351);
        D(At(Sim, c.WindSpeed), 13.8);
        var s = Layout.Symbols;
        I(At(Glass, s.SelectedHeading), 80);
        I(At(Glass, s.McpAltitude), 5000);
        D(At(Glass, s.Altitude), 374);
    }

    /// <summary>A memory source holding these bytes as one committed allocation at <see cref="Base"/>.</summary>
    public FakeProcessMemory Source() =>
        new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)Size, true, true, true), Bytes);

    public ModuleNavDisplaySampler Sampler(FakeProcessMemory? source = null, Pmdg737NavDisplayLayout? layout = null)
    {
        var src = source ?? Source();
        return new ModuleNavDisplaySampler(new ModuleMemory(src, Base), src, layout ?? Layout);
    }

    /// <summary>One reading through the sampler (of <paramref name="layout"/>, a layout of a fixture whose fields
    /// are this one's, else <see cref="Layout"/>); throws when it is not a good one.</summary>
    public NavDisplaySample Read(Pmdg737NavDisplayLayout? layout = null)
    {
        using var sampler = Sampler(layout: layout);
        return sampler.TryRead(out var sample, out var corrupt) ? sample : throw new InvalidOperationException(corrupt ?? "the memory was not readable");
    }

    /// <summary>Sets the memory a message's (or a map-option label's) first alternative tests.</summary>
    public void SetCondition(string text)
    {
        var o = Layout.Objects;
        var owners = new Dictionary<GlassObject, uint>
        {
            [o.Efis] = Efis, [o.Fms] = Fms, [o.Glass] = Glass, [o.Sim] = Sim, [o.Tcas] = Tcas, [o.Gpws] = Gpws,
            [o.Weather] = Weather, [o.Isdu] = Isdu, [o.Options] = Options, [o.Misc] = Misc,
        };
        var message = Layout.Messages.Messages.Concat(Layout.Messages.MapOptionLabels).First(x => x.Text == text);
        foreach (var c in message.AnyOf[0])
        {
            uint at = c.Source.Kind switch
            {
                // A per-FMC flag: FMC 0's (every unit on DEU 0, the EFIS object's entry 0 for it).
                NdSourceKind.Object or NdSourceKind.ByFmc => owners[c.Source.Owner] + (uint)c.Source.Field.Offset,
                NdSourceKind.Panel => Panel + (uint)c.Source.Field.Offset,
                _ => 0,
            };
            if (at != 0) B(at, !c.Negated);
        }
    }

    // ---- the objects' places ----

    public static uint Panel => Efis + (uint)Layout.Panel.PanelBase;

    public static uint Plan(int p) => Fms + (uint)(R.PlanBase + p * R.PlanStride);

    public static uint Keys(int p) => Plan(p) + (uint)R.KeyNodes;

    public static uint Node(int k) => Nodes + (uint)k * NodeSpacing;

    public static uint At(uint owner, GlassField f, int element = 0) => owner + (uint)(f.Offset + f.Stride * element);

    // ---- writing ----

    public void I(uint at, int v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    public void U(uint at, uint v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    public void D(uint at, double v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    public void F(uint at, float v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    public void B(uint at, bool v) => Bytes[at] = v ? (byte)1 : (byte)0;

    /// <summary>An ASCII text with its NUL (no NUL when <paramref name="terminated"/> is false).</summary>
    public void Text(uint at, string s, bool terminated = true)
    {
        var b = Encoding.Latin1.GetBytes(s);
        b.CopyTo(Bytes, (int)at);
        if (terminated) Bytes[at + b.Length] = 0;
    }

    /// <summary>A PMDG position in degrees, east positive, stored as PMDG holds it (radians, longitude positive
    /// west), valid unless told otherwise.</summary>
    public void Position(uint cposition, double latitudeDeg, double longitudeDeg, bool valid = true, double variationDeg = 0)
    {
        var p = R.Position;
        D(cposition + (uint)p.LatitudeRad, latitudeDeg * Math.PI / 180);
        D(cposition + (uint)p.LongitudeRad, -longitudeDeg * Math.PI / 180);
        D(cposition + (uint)p.MagneticVariationDeg, variationDeg);
        B(cposition + (uint)p.Valid, valid);
        // A node moved: the route line's ends at it move with it (as PMDG rebuilds the line).
        if (_lineEnds.TryGetValue(cposition - (uint)R.NodePosition, out var ends))
            foreach (uint end in ends) Array.Copy(Bytes, (int)cposition, Bytes, (int)end, p.Valid + 1);
    }

    /// <summary>Each node's position copies in the route line: the line ends <see cref="SetPathPoint"/> placed at it.</summary>
    private readonly Dictionary<uint, List<uint>> _lineEnds = new();

    /// <summary>A route node at slot <paramref name="k"/> of the node area: name and map name, a plain leg, the
    /// distance, the ETA (none when null), a valid position, no constraint.</summary>
    public uint AddNode(int k, string name, double distance, NdClock? eta, double latitudeDeg, double longitudeDeg)
    {
        uint n = Node(k);
        Array.Clear(Bytes, (int)n, R.NodeSize);
        Text(n + (uint)R.NodeName, name);
        Text(n + (uint)R.NodeMapName, name);
        I(n + (uint)R.NodeType, 0);
        D(n + (uint)R.NodeDistance, distance);
        if (eta is { } e)
        {
            uint clock = n + (uint)R.NodeEta;
            B(clock + (uint)R.Clock.Valid, true);
            I(clock + (uint)R.Clock.Hours, e.Hours);
            I(clock + (uint)R.Clock.Minutes, e.Minutes);
            I(clock + (uint)R.Clock.Seconds, e.Seconds);
        }
        Position(n + (uint)R.NodePosition, latitudeDeg, longitudeDeg);
        Bytes[n + (uint)R.DataText.ConstraintType] = (byte)' ';
        return n;
    }

    /// <summary>Each node's next pointer to the following one, the last's to none.</summary>
    public void Chain(IReadOnlyList<uint> nodes)
    {
        for (int i = 0; i < nodes.Count; i++) U(nodes[i] + (uint)R.NodeNext, i + 1 < nodes.Count ? nodes[i + 1] : 0);
    }

    /// <summary>Plan <paramref name="p"/>'s lateral path point <paramref name="k"/>: in use, a turn arc or none,
    /// a first line from one node's position to another's (none when null; its ends follow the nodes when they
    /// move later), the node it heads for.</summary>
    public void SetPathPoint(int p, int k, (uint From, uint To)? line, uint toward, bool turn = false, bool inUse = true)
    {
        var lp = R.LateralPath;
        uint point = Plan(p) + (uint)(R.Markers.LateralPath + lp.Points + k * lp.PointStride);
        Array.Clear(Bytes, (int)point, lp.PointStride);
        foreach (var ends in _lineEnds.Values) ends.RemoveAll(e => e >= point && e < point + lp.PointStride);
        B(point + (uint)lp.InUse, inUse);
        B(point + (uint)(lp.Arc + lp.ArcZeroSpan), !turn);
        U(point + (uint)lp.Toward, toward);
        if (line is not { } l) return;
        uint first = point + (uint)lp.FirstLine;
        foreach (var (node, end) in new[] { (l.From, first + (uint)lp.LineStart), (l.To, first + (uint)lp.LineEnd) })
        {
            Array.Copy(Bytes, (int)(node + (uint)R.NodePosition), Bytes, (int)end, R.Position.Valid + 1);
            if (!_lineEnds.TryGetValue(node, out var ends)) _lineEnds[node] = ends = new List<uint>();
            ends.Add(end);
        }
        D(first + (uint)lp.LineLength, 1.0);
    }

    /// <summary>Plan <paramref name="p"/>'s line as PMDG builds a plain route: one point per leg, its first line
    /// from one node to the next, heading for the next; the point after the last free.</summary>
    public void SetPath(int p, IReadOnlyList<uint> nodes)
    {
        var lp = R.LateralPath;
        uint path = Plan(p) + (uint)R.Markers.LateralPath;
        B(path + (uint)lp.Invalid, false);
        I(path + (uint)lp.Count, nodes.Count - 1);
        for (int k = 0; k + 1 < nodes.Count; k++) SetPathPoint(p, k, (nodes[k], nodes[k + 1]), nodes[k + 1]);
        SetPathPoint(p, nodes.Count - 1, null, 0, inUse: false);
    }

    /// <summary>Plan <paramref name="p"/>'s key nodes: the walk's first node and the TO node.</summary>
    public void SetKeys(int p, uint first, uint to)
    {
        U(Keys(p) + (uint)R.FirstNodeSlot, first);
        U(Keys(p) + (uint)R.ToNodeSlot, to);
    }

    /// <summary>Plan <paramref name="p"/>'s departure or arrival airport in its route info: the airport object
    /// (inline name, position) and, when <paramref name="runway"/> is given, a runway object at its threshold;
    /// returns the airport object.</summary>
    public uint AddRouteAirport(int p, bool departure, string name, double latitudeDeg, double longitudeDeg,
        (string Name, double LatitudeDeg, double LongitudeDeg)? runway = null, int runwayHeadingDeg = 0, int runwayLengthFt = 0)
    {
        var a = R.Airports;
        uint info = Plan(p) + (uint)R.PlnCentre.RouteInfo;
        uint airport = _nextObject;
        _nextObject += 0x100;
        Text(airport + (uint)a.AirportName, name);
        Position(airport + (uint)a.AirportPosition, latitudeDeg, longitudeDeg);
        U(info + (uint)(departure ? a.Departure : a.Arrival), airport);
        if (runway is { } r)
        {
            uint rwy = _nextObject;
            _nextObject += 0x100;
            Text(rwy + (uint)a.RunwayName, r.Name);
            Position(rwy + (uint)a.RunwayPosition, r.LatitudeDeg, r.LongitudeDeg);
            I(rwy + (uint)a.RunwayHeading, runwayHeadingDeg);
            I(rwy + (uint)a.RunwayLength, runwayLengthFt);
            U(info + (uint)(departure ? a.DepartureRunway : a.ArrivalRunway), rwy);
        }
        return airport;
    }

    /// <summary>A nearby object (inline name, position at the layout's offset, flags and class) appended to a
    /// list's vector, which grows by one element; returns the object.</summary>
    public uint AddListObject(int vector, string name, double latitudeDeg, double longitudeDeg, int flags = 0, int @class = 0)
    {
        var l = Layout.Lists;
        uint obj = _nextObject;
        _nextObject += 0x100;
        Text(obj + (uint)l.ObjectName, name);
        Position(obj + (uint)l.ObjectPosition, latitudeDeg, longitudeDeg);
        I(obj + (uint)l.StationFlags, flags);
        I(obj + (uint)l.StationClass, @class);

        uint vec = Efis + (uint)vector;
        uint begin = BitConverter.ToUInt32(Bytes, (int)(vec + l.VectorBegin));
        uint end = BitConverter.ToUInt32(Bytes, (int)(vec + l.VectorEnd));
        if (begin == 0)
        {
            begin = end = vector == l.NavaidVector ? NavaidElements : vector == l.FixVector ? FixElements : AirportElements;
            U(vec + (uint)l.VectorBegin, begin);
        }
        U(end + (uint)l.ObjectPointer, obj);
        U(vec + (uint)l.VectorEnd, end + (uint)l.ElementSize);
        return obj;
    }
}
