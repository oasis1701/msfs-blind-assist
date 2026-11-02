using System.Buffers.Binary;
using MSFSBlindAssist.Services.WasmMemory;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Reads one navigation display sample from the 737 module's memory through a derived layout. Every read
/// takes the object pointers afresh and range-checks each object against the module's committed memory
/// (WASM-4). The route is a bounded walk through heap pointers, as PMDG walks it: each node read as one
/// block, every pointer inside the module, no node twice, at most the layout's cap; the nodes the sample
/// carries (the TO corner's, the ones the map draws) must look real, while the ones PMDG passes over need
/// only be inside the module. The nearby lists, the traffic and the CDU FIX page get the same treatment.
/// A failed check discards the whole reading as corrupt (never half a list); unreadable memory is lost.
/// Owns its memory source (a lease on the shared simulator handle).
/// </summary>
public sealed class ModuleNavDisplaySampler : INavDisplaySampler
{
    /// <summary>The most elements read from one nearby list: PMDG draws 40 of each, its vectors hold far more
    /// (441 fixes at KATL at range 40, live 2026-10-25).</summary>
    internal const int ListCap = 4096;

    /// <summary>The Captain's side.</summary>
    private const int Side = 0;

    /// <summary>Half the earth's circumference: no distance PMDG holds is longer.</summary>
    private const double MaxDistanceNm = 10_800;

    /// <summary>The longest name read from an object's or node's inline name field (the position follows it).</summary>
    private const int NameField = 24;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Pmdg737NavDisplayLayout Layout { get; }

    public ModuleNavDisplaySampler(ModuleMemory memory, IDisposable ownedSource, Pmdg737NavDisplayLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
    }

    public bool TryRead(out NavDisplaySample sample, out string? corrupt)
    {
        sample = null!;
        corrupt = null;
        // One reading asks Windows about each region once (a VirtualQueryEx costs ~200 µs on the sim).
        using var regions = _memory.RememberRegions();
        var o = Layout.Objects;
        var pointers = new Dictionary<GlassObject, uint>();
        foreach (var obj in new[] { o.Efis, o.Fms, o.Glass, o.Sim, o.Tcas, o.Gpws, o.Weather, o.Isdu, o.Options, o.Misc })
        {
            if (!Pointer(obj, out uint p)) return false;
            pointers[obj] = p;
        }
        try
        {
            sample = Read(pointers);
            return true;
        }
        catch (ReadFailed)
        {
            return false;
        }
        catch (Corrupt ex)
        {
            corrupt = ex.Message;
            return false;
        }
    }

    public void Dispose() => _ownedSource.Dispose();

    private NavDisplaySample Read(IReadOnlyDictionary<GlassObject, uint> pointers)
    {
        var l = Layout;
        var o = l.Objects;
        uint efis = pointers[o.Efis], fms = pointers[o.Fms], glass = pointers[o.Glass], sim = pointers[o.Sim], tcas = pointers[o.Tcas],
            isdu = pointers[o.Isdu], options = pointers[o.Options], misc = pointers[o.Misc];

        // The Captain's panel and the display units.
        var p = l.Panel;
        int panelIndex = Int(efis + (uint)(p.PanelIndexOffset + 4 * Side));
        if (panelIndex < 0 || p.PanelBase + (long)(panelIndex + 1) * p.PanelStride > o.Efis.Size)
            throw new Corrupt(Invariant($"the panel: index {panelIndex} has no panel in CEFIS"));
        uint panel = efis + (uint)(p.PanelBase + panelIndex * p.PanelStride);
        var panelState = new NdPanelState(I(panel, p.Mode), I(panel, p.Range), I(panel, p.VorAdfLeft), I(panel, p.VorAdfRight),
            B(panel, p.Centered), B(panel, p.ShowTfc), B(panel, p.ShowWxr), B(panel, p.ShowSta), B(panel, p.ShowWpt), B(panel, p.ShowArpt),
            B(panel, p.ShowData), B(panel, p.ShowPos), B(panel, p.ShowTerr), B(panel, p.ShowVsd), B(panel, p.Powered));
        var u = l.Units;
        var pages = new int[u.UnitCount];
        var indices = new int[u.UnitCount];
        for (int k = 0; k < u.UnitCount; k++)
        {
            pages[k] = Int(efis + (uint)(k * u.UnitStride + u.PageOffset));
            indices[k] = Int(efis + (uint)(k * u.UnitStride + u.IndexOffset));
        }

        // Which way the map points.
        var or = l.Orientation;
        int iruIndex = I(efis, or.IruIndex, Side);
        long iruSlot = or.IruPointers.Offset + (long)or.IruPointers.Stride * iruIndex;
        if (iruIndex < 0 || iruSlot + 4 > o.Isdu.Size) throw new Corrupt(Invariant($"the IRU: index {iruIndex} has no IRU in CISDU"));
        uint iru = UInt(isdu + (uint)iruSlot);
        if (iru == 0 || !_memory.IsInsideModule(iru, or.IruSize)) throw new Corrupt("the IRU: its pointer lies outside the module");
        bool readoutValid = B(glass, or.ReadoutValid, Side);

        // The map's validity: the FMC the Captain's ND shows, and whether it has an FMS position.
        var r = l.Route;
        int lowerSelector = I(efis, u.LowerSelector, u.SelectorSide);
        var aircraftBlock = Block(fms + (uint)r.FmsPosition, r.Position.Valid + 1, "the FMS position");
        var (fmc, fmsPositionValid) = MapFmc(efis, fms, pages, lowerSelector, (aircraftBlock[r.Position.Valid] & 1) != 0);

        // The route: the TO corner's waypoint (PMDG shows none while the map is invalid) and the
        // map's walk of the reference route.
        int refRoute = Int(fms + (uint)r.RefRoute);
        uint refPlan = Plan(fms, refRoute);
        NdWaypoint? active = null;
        if (readoutValid && fmsPositionValid)
        {
            int toPlan = Bool(Plan(fms, r.ToNodePlan) + (uint)r.PlanActive) ? r.ToNodePlan : refRoute;
            var walk = new Walk(r.MaxNodes);
            for (uint node = UInt(Plan(fms, toPlan) + (uint)(r.KeyNodes + r.ToNodeSlot)); node != 0;)
            {
                var n = Node(node, walk);
                if (n[r.NodeName] == r.PseudoPrefix)
                {
                    node = n.UInt(r.NodeNext);
                    continue;
                }
                active = Waypoint(n, isTo: true);
                break;
            }
        }
        var routeWalk = MapRoute(refPlan);
        // A modification pending (the reference route's MOD plan active): PMDG draws it too.
        int mod = refRoute + r.ModOffset;
        bool modActive = IsPlan(mod) && Bool(Plan(fms, mod) + (uint)r.PlanActive);
        var modifiedWalk = modActive ? MapRoute(Plan(fms, mod)) : null;
        // Each route's line (PMDG draws the reference route's and the MOD's).
        var routePath = RoutePath(refPlan, routeWalk);
        var modifiedPath = modifiedWalk is null ? null : RoutePath(Plan(fms, mod), modifiedWalk);
        // The route's airports (PMDG draws the reference route's and the MOD's).
        var airports = RouteAirports(refPlan);
        var modifiedAirports = modActive ? RouteAirports(Plan(fms, mod)) : null;

        // The PLN centre: the stepped-to node of the route the Captain's CDU edits (its MOD when active).
        var pc = r.PlnCentre;
        GeoPoint? plnCentre = null;
        string? plnCentreName = null;
        int edited = Int(fms + (uint)(pc.EditedRoute + pc.EditedRouteStride * Side));
        if (IsPlan(edited) && IsPlan(edited + r.ModOffset))
        {
            if (Bool(Plan(fms, edited + r.ModOffset) + (uint)r.PlanActive)) edited += r.ModOffset;
            uint centre = UInt(Plan(fms, edited) + (uint)(pc.RouteInfo + pc.CentreSlot + pc.CentreSlotStride * Side));
            if (centre != 0)
            {
                var n = Node(centre, new Walk(1));
                plnCentre = Position(n, r.NodePosition, "the PLN centre");
                // Named as the map labels it ("INTC", not the corner's "(INTC)"): the owner's choice, 2026-10-25.
                if (plnCentre is not null) plnCentreName = Name(n, r.NodeMapName, "the PLN centre");
            }
        }

        // The markers (the reference route's VNAV profile).
        var mk = r.Markers;
        var markers = new List<NdMarker>();
        for (int k = mk.FirstIndex; k <= mk.LastIndex; k++)
        {
            if (!mk.Labels.TryGetValue(k, out var label)) continue;
            uint at = refPlan + (uint)(mk.LateralPath + mk.Array + k * mk.Stride);
            var block = Block(at, r.Position.Valid + 1, "a marker");
            if (Position(block, 0, $"the marker {label}") is { } position) markers.Add(new NdMarker(label, position));
        }

        var (objects, counts) = MapObjects(efis, panelState);
        var (trafficState, traffic) = Traffic(tcas, options);
        var (tuned, receivers) = Receivers(sim, glass, misc);
        var corners = l.Corners;
        var symbols = l.Symbols;
        var rnp = l.Messages.Rnp;
        var approach = Approach(panelState, efis, sim, glass);
        var bearingPointers = Pointers(panelState, receivers, glass, misc);

        return new NavDisplaySample(
            panelState, pages, indices, lowerSelector,
            D(glass, or.Heading, Side), D(glass, or.Track, Side), B(efis, or.Magnetic, 0), D(sim, or.MagneticVariation, 0),
            Int(iru + (uint)or.IruCondition.Offset), B(options, or.TrackUpOption, 0), readoutValid, B(glass, or.ReadoutFailed, Side),
            fmc, fmsPositionValid, Position(aircraftBlock, 0, "the aircraft"),
            active, refRoute, routeWalk.Route, modifiedWalk?.Route, airports, modifiedAirports, routePath, modifiedPath,
            Int(fms + (uint)(r.DataText.Performance + r.DataText.TransitionAltitude)), Int(fms + (uint)(r.DataText.Performance + r.DataText.TransitionLevel)),
            B(options, r.DataText.ActiveOption, 0),
            plnCentre, plnCentreName,
            Double(fms + (uint)r.VnavDeviation), Int(fms + (uint)r.VnavPhase), Bool(refPlan + (uint)(r.PerfInfo + r.PerfPath)),
            markers, objects, counts[0], counts[1], counts[2], trafficState, traffic, tuned, B(options, l.Tuned.CourseOption, 0),
            FixPage(),
            I(glass, symbols.SelectedHeading, 0), I(glass, symbols.McpAltitude, 0), D(glass, symbols.Altitude, panelIndex), D(sim, symbols.VerticalSpeed, 0),
            receivers,
            D(glass, corners.GroundSpeed, 0), D(sim, corners.TrueAirspeed, 0), D(sim, corners.WindDirection, 0), D(sim, corners.WindSpeed, 0),
            ConditionValues(pointers, panel, fmc),
            B(options, rnp.Option, 0), Double(fms + (uint)(rnp.PosData + rnp.Rnp)), Double(fms + (uint)(rnp.PosData + rnp.Anp)),
            Xtk(panelState, fms, options, refRoute, refPlan, (aircraftBlock[r.Position.Valid] & 1) != 0),
            approach, bearingPointers, Pos(panelState, readoutValid, tuned, receivers, bearingPointers, sim, glass, isdu, iru),
            Details(tcas, options, glass, sim, pointers[o.Weather], pointers[o.Gpws], panelIndex, fms, panelState,
                indices[Pmdg737NavDisplayCrossCheck.CaptainUnit(pages, lowerSelector, u)]));
    }

    /// <summary>What the details read: whether the TCAS object's traffic list exists, the TCAS ring's option byte; the
    /// heading line's option and the FMA roll mode, and only while they time the line the sim's clock and the ND symbol
    /// generator's time of the last heading change (PMDG's own, kept nowhere else); the radar's tilt and its two bytes for
    /// the panel; the vertical RNP and ANP (the bottom RNP's CDU position data); the trend vector's turn rate, at its
    /// simulator interface source (PMDG smooths it 5 % a frame into the ND symbol generator, unread as the TAS's is), in
    /// MAP a number or corrupt; the track line's range-arcs option; the terrain numbers' option and the GPWS test, and
    /// while the panel's TERR is on with the option set and no test, the ND symbol generator's terrain record for the slot
    /// of the unit the window describes (by its index), PMDG's own and kept nowhere else.</summary>
    private NdDetailReading? Details(uint tcas, uint options, uint glass, uint sim, uint weather, uint gpws, int panelIndex, uint fms, NdPanelState panel,
        int unitIndex)
    {
        bool map = panel.Mode == Layout.Geometry.Modes.Map;
        var l = Layout;
        if (l.Details is not { } d) return null;
        var h = l.Symbols.HeadingLine;
        bool lineOption = B(options, h.Option, 0);
        int roll = I(glass, h.RollMode, 0);
        double now = 0, changed = 0;
        if (lineOption && h.TimedRollModes.Contains(roll))
        {
            if (!Pointer(h.Generator, out uint generator)) throw new ReadFailed();
            now = D(sim, h.SimTime, 0);
            changed = Double(generator + (uint)h.ChangedAt.Offset);
        }
        var w = l.Messages.Wxr;
        if (panelIndex > 1) throw new Corrupt(Invariant($"the radar: panel {panelIndex} has no tilt"));
        double turnRate = d.TrendVector is { } trend ? D(sim, trend.TurnRate, 0) : 0;
        if (map && !double.IsFinite(turnRate)) throw new Corrupt("the trend vector: the turn rate is not a number");
        var terrain = l.Messages.Terrain;
        bool terrainOption = B(options, terrain.Option, 0), gpwsTest = B(gpws, terrain.GpwsTest, 0);
        int highest = 0, highestColour = 0, lowest = 0, lowestColour = 0;
        if (panel.Terr && terrainOption && !gpwsTest)
        {
            if (unitIndex < 0 || unitIndex >= terrain.SlotByUnitIndex.Count) throw new Corrupt(Invariant($"the terrain numbers: unit {unitIndex} has no slot"));
            int slot = terrain.SlotByUnitIndex[unitIndex];
            if (!Pointer(terrain.Generator, out uint generator)) throw new ReadFailed();
            highest = I(generator, terrain.Highest, slot);
            highestColour = I(generator, terrain.HighestColour, slot);
            lowest = I(generator, terrain.Lowest, slot);
            lowestColour = I(generator, terrain.LowestColour, slot);
        }
        return new NdDetailReading(UInt(tcas + (uint)l.Traffic.List) != 0, d.TcasRing is { } ring && B(options, ring.Option, 0), lineOption, roll, now, changed,
            I(weather, w.Tilt, panelIndex), B(weather, w.Auto, 0), B(weather, w.Variable, panelIndex),
            Int(fms + (uint)(l.Messages.Rnp.PosData + l.Messages.Vdev.VerticalRnp.Rnp)), Int(fms + (uint)(l.Messages.Rnp.PosData + l.Messages.Vdev.VerticalRnp.Anp)),
            turnRate, d.TrackLine is { } track && B(options, track.RangeArcsOption, 0), terrainOption, gpwsTest, highest, highestColour, lowest, lowestColour);
    }

    /// <summary>
    /// POS, in MAP with the POS button on and the layout's pointer part: per receiver the VOR line's radial while
    /// the VOR is tuned and the readout valid, as PMDG draws it (the receiver's field, corrected while
    /// the station's position is valid, as PMDG does), the ADF line's bearing while the ADF flag is set
    /// (each a number, else corrupt); the side's IRS position (the IRU the orientation reads) and the GPS position
    /// (the IRS object's array pointer inside the module).
    /// </summary>
    private NdPosReading? Pos(NdPanelState panel, bool readoutValid, IReadOnlyList<NdTunedStation> tuned, IReadOnlyList<NdReceiver> receivers,
        IReadOnlyList<NdPointerReading>? pointers, uint sim, uint glass, uint isdu, uint iru)
    {
        var l = Layout;
        if (l.Pointers is not { } p || pointers is null || panel.Mode != l.Geometry.Modes.Map || !panel.Pos) return null;
        var f = p.Pos;
        var tu = l.Tuned;
        var position = l.Route.Position;
        var radials = new List<double>();
        var bearings = new List<double>();
        for (int k = 0; k < Math.Min(Math.Min(tuned.Count, receivers.Count), pointers.Count); k++)
        {
            double radial = 0, bearing = 0;
            if (readoutValid && p.VorTuned(receivers[k], tuned[k]))
            {
                uint station = sim + (uint)(tu.Stations + k * tu.Stride);
                radial = Double(station + (uint)f.StationRadial);
                // No correction in the layout (the MSFS 2020 file): PMDG prints the radial as the simulator gives it.
                if (tuned[k].Position is not null && f.StationCorrection is { } correction)
                    radial = Wrap360(radial + Double(station + (uint)correction) - Double(station + (uint)(tu.Position + position.MagneticVariationDeg)));
                if (!double.IsFinite(radial)) throw new Corrupt(Invariant($"POS: VOR {k + 1}'s radial is not a number"));
            }
            if (p.AdfReceiving(receivers[k], pointers[k].AdfPowered))
            {
                bearing = D(glass, p.AdfBearing, k);
                if (!double.IsFinite(bearing)) throw new Corrupt(Invariant($"POS: ADF {k + 1}'s bearing is not a number"));
            }
            radials.Add(radial);
            bearings.Add(bearing);
        }
        uint irsAt = iru + (uint)f.IruPosition;
        var irs = Position(Block(irsAt, position.Valid + 1, "the IRS position"), 0, "the IRS position");
        uint array = UInt(isdu + (uint)f.GpsArray);
        uint gpsAt = array + (uint)(Side * f.GpsStride + f.GpsPosition);
        if (array == 0 || !_memory.IsInsideModule(gpsAt, (uint)(position.Valid + 1))) throw new Corrupt("POS: the GPS position lies outside the module");
        var gps = Position(Block(gpsAt, position.Valid + 1, "the GPS position"), 0, "the GPS position");
        return new NdPosReading(radials, bearings, irs, gps);
    }

    private static double Wrap360(double deg) => ((deg % 360) + 360) % 360;

    /// <summary>
    /// The cross-track error, read only while PMDG draws it (MAP, the RNP option, the reference route
    /// active): the FMS's cross-track error of the TO waypoint's plan (<see cref="NdXtkFields"/>); null where it gives none
    /// (no FMS position, the path's int negative, no key node, the node's byte set with its count below the limit)
    /// or computes one (a conditional key node: not read); a number, else corrupt.
    /// </summary>
    private double? Xtk(NdPanelState panel, uint fms, uint options, int refRoute, uint refPlan, bool positionValid)
    {
        var l = Layout;
        var x = l.Xtk;
        var r = l.Route;
        if (panel.Mode != l.Geometry.Modes.Map || !B(options, l.Messages.Rnp.Option, 0) || !Bool(refPlan + (uint)r.PlanActive) || !positionValid) return null;
        uint plan = Plan(fms, Bool(Plan(fms, r.ToNodePlan) + (uint)r.PlanActive) ? r.ToNodePlan : refRoute);
        uint path = plan + (uint)r.Markers.LateralPath;
        if (Int(path + (uint)x.PathInvalid) < 0) return null;
        uint node = UInt(plan + (uint)(r.KeyNodes + x.KeySlot));
        if (node == 0) return null;
        var n = new NodeBlock(node, Block(node, new[] { x.NodeFlag + 1, x.NodeCount + 4, r.Validity.Conditional + 1 }.Max(), "the XTK's node"));
        if ((n[x.NodeFlag] & 1) != 0 && n.Int(x.NodeCount) < x.NodeCountBelow) return null;
        if ((n[r.Validity.Conditional] & 1) != 0) return null;
        double xtk = Double(path + (uint)x.Value);
        return double.IsFinite(xtk) ? xtk : throw new Corrupt("the XTK is not a number");
    }

    /// <summary>
    /// The map's FMC and whether its position is valid (<see cref="NdMapValidity"/>): the EFIS object's entry for the
    /// DEU of the unit the window describes; then, while the FMS position is valid, the FMS object's byte at the EFIS
    /// object's entry for that FMC (the
    /// master FMC's for the layout's NoFmc), as PMDG reads it only then. An index with no entry or byte in its
    /// object is corrupt.
    /// </summary>
    private (int Fmc, bool PositionValid) MapFmc(uint efis, uint fms, IReadOnlyList<int> pages, int lowerSelector, bool positionValid)
    {
        var l = Layout;
        var v = l.MapValidity;
        var u = l.Units;
        int unit = Pmdg737NavDisplayCrossCheck.CaptainUnit(pages, lowerSelector, u);
        int fmc = Int(efis + Entry(Int(efis + (uint)(unit * u.UnitStride + u.Deu)), "DEU"));
        if (!positionValid) return (fmc, false);
        int index = Int(efis + (fmc == v.NoFmc ? (uint)v.MasterFmc : Entry(fmc, "FMC")));
        if (index < 0 || v.FmcValid + (long)index >= l.Objects.Fms.Size)
            throw new Corrupt(Invariant($"the map's FMC: entry {index} has no byte in CFMS"));
        return (fmc, (Byte(fms + (uint)(v.FmcValid + index)) & 1) != 0);

        uint Entry(int k, string what)
        {
            long at = v.FmcIndex.Offset + (long)v.FmcIndex.Stride * k;
            return k >= 0 && at + 4 <= l.Objects.Efis.Size ? (uint)at : throw new Corrupt(Invariant($"the map's FMC: {what} {k} has no entry in CEFIS"));
        }
    }

    /// <summary>
    /// The bearing pointers, outside PLN and with the layout's part: per receiver the VOR pointer's glass data
    /// flag and the ADF's systems object power byte, and the bearing of the pointer PMDG draws, read only then (a
    /// number, else corrupt).
    /// </summary>
    private IReadOnlyList<NdPointerReading>? Pointers(NdPanelState panel, IReadOnlyList<NdReceiver> receivers, uint glass, uint misc)
    {
        var l = Layout;
        if (l.Pointers is not { } p || panel.Mode == l.Geometry.Modes.Plan) return null;
        var readings = new List<NdPointerReading>();
        for (int k = 0; k < Math.Min(p.Count, receivers.Count); k++)
        {
            bool vorValid = B(glass, p.VorValid, k), adfPowered = B(misc, p.AdfPower, k);
            bool vor = p.VorDrawn(receivers[k], vorValid), adf = !vor && p.AdfDrawn(receivers[k], adfPowered);
            double bearing = vor ? D(glass, p.VorBearing, k) : adf ? D(glass, p.AdfBearing, k) : 0;
            if ((vor || adf) && !double.IsFinite(bearing)) throw new Corrupt(Invariant($"the bearing pointers: receiver {k + 1}'s bearing is not a number"));
            readings.Add(new NdPointerReading(vorValid, adfPowered, bearing));
        }
        return readings;
    }

    // ---- the route ----

    /// <summary>Visited nodes, in the order visited, and the cap of one traversal.</summary>
    private sealed class Walk(int cap)
    {
        public HashSet<uint> Visited { get; } = new();

        public List<uint> Order { get; } = new();

        public int Cap { get; } = cap;
    }

    /// <summary>The map's walk of one route: the waypoints drawn, every node visited in chain order, and each
    /// drawn waypoint's node with its index in the route.</summary>
    private sealed record MapWalk(List<NdWaypoint> Route, List<uint> Chain, Dictionary<uint, int> Drawn);

    /// <summary>A node read as one block.</summary>
    private sealed class NodeBlock(uint address, byte[] bytes)
    {
        public uint Address { get; } = address;

        public byte[] Bytes { get; } = bytes;

        public byte this[int offset] => Bytes[offset];

        public int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(offset));

        public uint UInt(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(offset));

        public double Double(int offset) => BinaryPrimitives.ReadDoubleLittleEndian(Bytes.AsSpan(offset));
    }

    private NodeBlock Node(uint address, Walk walk)
    {
        if (!walk.Visited.Add(address)) throw new Corrupt("the route: a node visited twice (a loop)");
        if (walk.Visited.Count > walk.Cap) throw new Corrupt(Invariant($"the route: more than {walk.Cap} nodes"));
        walk.Order.Add(address);
        return new NodeBlock(address, Block(address, Layout.Route.NodeSize, "the route: a node"));
    }

    /// <summary>A route node PMDG counts as a waypoint: not hidden, not the no-data leg type, not conditional unless
    /// shown, the position valid.</summary>
    private bool IsValid(NodeBlock n)
    {
        var r = Layout.Route;
        var v = r.Validity;
        return n[v.Hidden] == 0 && n.Int(r.NodeType) != r.NoDataLegType && (n[v.Conditional] == 0 || n[v.ConditionalShown] != 0)
            && n[r.NodePosition + r.Position.Valid] != 0;
    }

    /// <summary>The next node <see cref="IsValid"/> accepts (already read), or none.</summary>
    private NodeBlock? NextValid(NodeBlock n, Walk walk)
    {
        for (uint next = n.UInt(Layout.Route.NodeNext); next != 0;)
        {
            var m = Node(next, walk);
            if (IsValid(m)) return m;
            next = m.UInt(Layout.Route.NodeNext);
        }
        return null;
    }

    /// <summary>PMDG's walk of the reference route for the map, one walk with one cap: from the first key node,
    /// past the nodes its start search skips; then every node PMDG draws as a waypoint (the TO node always, any
    /// other while its UndrawnUnlessTo byte is clear, none of the undrawn kind), the key node whose constraint
    /// it prints without DATA marked; and every hold node, which it draws as a hold instead.</summary>
    private MapWalk MapRoute(uint plan)
    {
        var r = Layout.Route;
        var v = r.Validity;
        uint keys = plan + (uint)r.KeyNodes;
        uint to = UInt(keys + (uint)r.ToNodeSlot);
        uint activeWaypoint = UInt(keys + (uint)r.DataText.ActiveSlot);
        var walk = new Walk(r.MaxNodes);
        uint first = UInt(keys + (uint)r.FirstNodeSlot);
        var n = first == 0 ? null : Node(first, walk);
        while (n is not null && !(n[v.StartSkip] == 0 && n[v.Conditional] == 0 && IsValid(n))) n = NextValid(n, walk);
        var route = new List<NdWaypoint>();
        var drawn = new Dictionary<uint, int>();
        for (; n is not null; n = NextValid(n, walk))
        {
            if (!IsValid(n)) continue;
            bool isTo = n.Address == to;
            // A hold PMDG made, a copy of its fix: drawn as a holding pattern, never as a waypoint.
            if (n[v.Hold] != 0 && n[v.HoldSkip] == 0)
            {
                drawn[n.Address] = route.Count;
                var w = Waypoint(n, isTo);
                route.Add(w with { IsHold = true, Hold = HoldOf(n, w) });
                continue;
            }
            if ((isTo || n[v.UndrawnUnlessTo] == 0) && n.Int(v.Kind) != v.UndrawnKind)
            {
                drawn[n.Address] = route.Count;
                route.Add(Waypoint(n, isTo) with { IsActiveWaypoint = n.Address == activeWaypoint });
            }
        }
        return new MapWalk(route, walk.Order, drawn);
    }

    /// <summary>
    /// The route line of one plan: nothing while the path is not valid; its points from 0 to the count,
    /// stopping at the first not in use (the draw leaves there), the whole array read as one block within the
    /// plan; per point whether its turn arc is drawn, each line drawn (length above the minimum; a line whose
    /// position is not valid cannot be placed and is left out) and the waypoint it heads for.
    /// </summary>
    private List<NdPathPoint> RoutePath(uint plan, MapWalk walk)
    {
        var r = Layout.Route;
        var lp = r.LateralPath;
        uint path = plan + (uint)r.Markers.LateralPath;
        var points = new List<NdPathPoint>();
        if (Bool(path + (uint)lp.Invalid)) return points;
        int count = Int(path + (uint)lp.Count);
        if (count < 0 || r.Markers.LateralPath + lp.Points + (long)(count + 1) * lp.PointStride > r.PlanStride)
            throw new Corrupt(Invariant($"the route line: {count} points do not fit a flight plan"));
        var block = Block(path + (uint)lp.Points, (count + 1) * lp.PointStride, "the route line");
        NdPathLine? Line(int at, string what)
        {
            if (!(BinaryPrimitives.ReadDoubleLittleEndian(block.AsSpan(at + lp.LineLength)) > lp.LineMinLength)) return null;
            var from = Position(block, at + lp.LineStart, what);
            var to = Position(block, at + lp.LineEnd, what);
            return from is { } f && to is { } t ? new NdPathLine(f, t) : null;
        }
        for (int k = 0; k <= count; k++)
        {
            int at = k * lp.PointStride;
            if (block[at + lp.InUse] == 0) break;
            uint node = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(at + lp.Toward));
            points.Add(new NdPathPoint(block[at + lp.Arc + lp.ArcZeroSpan] == 0, Line(at + lp.FirstLine, "the route line"),
                Line(at + lp.SecondLine, "the route line"), node == 0 ? null : Toward(node, walk)));
        }
        return points;
    }

    /// <summary>The waypoint a line heads for: the first drawn waypoint at or after its node in the map's chain,
    /// else the node itself. PMDG prints nothing of that node (only its direct-to intercept drawing takes it),
    /// so only its pointer is checked (WASM-14): a name it could not print heads for nothing, a position
    /// out of range is none.</summary>
    private NdPathToward? Toward(uint node, MapWalk walk)
    {
        int from = walk.Chain.IndexOf(node);
        if (from >= 0)
        {
            for (int i = from; i < walk.Chain.Count; i++)
            {
                if (!walk.Drawn.TryGetValue(walk.Chain[i], out int index)) continue;
                var w = walk.Route[index];
                return new NdPathToward(index, w.MapName, w.Position);
            }
        }
        var n = new NodeBlock(node, Block(node, Layout.Route.NodeSize, "the route line: a node"));
        string name;
        try { name = Name(n, Layout.Route.NodeMapName, "the route line: a node"); }
        catch (Corrupt) { return null; }
        if (name.Length == 0) return null;
        GeoPoint? at;
        try { at = Position(n, Layout.Route.NodePosition, $"the route line: {name}"); }
        catch (Corrupt) { at = null; }
        return new NdPathToward(null, name, at);
    }

    /// <summary>A node the sample carries, checked: a printable name and a valid position in range (or none) on
    /// every node; a finite distance within half the earth and a clock within the day on the TO node, the one
    /// PMDG prints them for (the top-right corner). A later node's distance is not printed, and its clock
    /// (DATA's ETA) out of the day reads as no ETA.</summary>
    private NdWaypoint Waypoint(NodeBlock n, bool isTo)
    {
        var r = Layout.Route;
        string name = Name(n, r.NodeName, "the route: a waypoint");
        if (name.Length == 0) throw new Corrupt("the route: a waypoint's name is empty");
        string mapName = Name(n, r.NodeMapName, $"the route: {name}'s map");
        int legType = n.Int(r.NodeType);
        double distance = n.Double(r.NodeDistance);
        NdClock? eta = null;
        if (legType != r.NoDataLegType)
        {
            if (isTo && (!double.IsFinite(distance) || distance < 0 || distance > MaxDistanceNm))
                throw new Corrupt(Invariant($"the route: {name}'s distance {distance} is not 0 to {MaxDistanceNm} NM"));
            int clock = r.NodeEta;
            if (n[clock + r.Clock.Valid] != 0)
            {
                var c = new NdClock(n.Int(clock + r.Clock.Hours), n.Int(clock + r.Clock.Minutes), n.Int(clock + r.Clock.Seconds));
                bool timeOfDay = c.Hours is >= 0 and <= 23 && c.Minutes is >= 0 and <= 59 && c.Seconds is >= 0 and <= 59;
                if (!timeOfDay && isTo)
                    throw new Corrupt(Invariant($"the route: {name}'s clock {c.Hours}:{c.Minutes}:{c.Seconds} is not a time of day"));
                if (timeOfDay) eta = c;
            }
        }
        var d = r.DataText;
        var constraint = new NdConstraint((char)n[d.ConstraintType], n.Int(d.AtAltitude), n.Int(d.UpperAltitude), n.Int(d.LowerAltitude),
            n.Int(d.Phase), n.Int(d.ThirdLine));
        return new NdWaypoint(name, mapName, legType, distance, eta, Position(n, r.NodePosition, $"the route: {name}"),
            n.Double(r.NodePosition + r.Position.MagneticVariationDeg), constraint, n[d.EtaHidden] != 0, isTo);
    }

    /// <summary>A hold node's hold (none without the layout's hold): its turn byte, its inbound course, checked a number with
    /// the node's variation (PMDG draws the pattern by the course and the HOLD page prints it less the variation), and the
    /// HOLD page's two legs as the node holds them (the text leaves out a value PMDG prints dashes for).</summary>
    private NdHold? HoldOf(NodeBlock n, NdWaypoint w)
    {
        if (Layout.Details?.Hold is not { } h) return null;
        double inbound = n.Double(h.Record + h.Inbound);
        if (!double.IsFinite(inbound) || !double.IsFinite(w.MagneticVariationDeg))
            throw new Corrupt(Invariant($"the route: {w.Name}'s hold has the inbound course {inbound} with the variation {w.MagneticVariationDeg}"));
        return new NdHold(n[h.Record + h.Turn], inbound, n.Double(h.LegTime), n.Double(h.LegDistance));
    }

    /// <summary>The two airports PMDG draws for a plan, departure then arrival, from its route info: none while
    /// the airport pointer is null; the symbol at the runway's position while a runway is set, else at the
    /// airport's.</summary>
    private List<NdRouteAirport> RouteAirports(uint plan)
    {
        var a = Layout.Route.Airports;
        uint info = plan + (uint)Layout.Route.PlnCentre.RouteInfo;
        var airports = new List<NdRouteAirport>();
        foreach (var (airportSlot, runwaySlot, departure) in new[] { (a.Departure, a.DepartureRunway, true), (a.Arrival, a.ArrivalRunway, false) })
        {
            uint airportAt = UInt(info + (uint)airportSlot);
            if (airportAt == 0) continue;
            var airport = InlineObject(airportAt, a.AirportName, a.AirportPosition, "the route's airport");
            string name = Name(airport, a.AirportName, "the route's airport");
            if (name.Length == 0) throw new Corrupt("the route's airport: its name is empty");
            uint runwayAt = UInt(info + (uint)runwaySlot);
            if (runwayAt == 0)
            {
                airports.Add(new NdRouteAirport(name, null, Position(airport, a.AirportPosition, $"the airport {name}"), departure));
                continue;
            }
            var runway = InlineObject(runwayAt, a.RunwayName, a.RunwayPosition, $"the airport {name}'s runway", Math.Max(a.RunwayHeading, a.RunwayLength) + 4);
            string runwayName = Name(runway, a.RunwayName, $"the airport {name}'s runway");
            if (runwayName.Length == 0) throw new Corrupt($"the airport {name}'s runway: its name is empty");
            airports.Add(new NdRouteAirport(name, runwayName, Position(runway, a.RunwayPosition, $"the runway {runwayName}"), departure,
                runway.Int(a.RunwayHeading), runway.Int(a.RunwayLength)));
        }
        return airports;
    }

    /// <summary>An object with an inline name and a position (and fields up to <paramref name="end"/>), read as one block.</summary>
    private NodeBlock InlineObject(uint at, int name, int position, string what, int end = 0) =>
        new(at, Block(at, Math.Max(Math.Max(name + NameField, position + Layout.Route.Position.Valid + 1), end), what));

    // ---- the nearby lists, the traffic, the receivers, the CDU FIX page, the message conditions ----

    /// <summary>Each list's count, and its elements only while PMDG draws that list (its button on; fixes only up to
    /// their range): PMDG keeps the vectors filled after a button goes off (live, KATL 2026-10-25: 441 fixes with
    /// WPT off), and reading them every tick cost 90 ms a reading on the UI thread.</summary>
    private (List<NdMapObject> Objects, int[] Counts) MapObjects(uint efis, NdPanelState panel)
    {
        var li = Layout.Lists;
        var objects = new List<NdMapObject>();
        var counts = new int[3];
        int objectSize = new[] { li.ObjectName + NameField, li.ObjectPosition + Layout.Route.Position.Valid + 1, li.StationFlags + 4, li.StationClass + 4 }.Max();
        bool fixesDrawn = panel.Wpt && Pmdg737NavDisplayGeometry.SelectedRangeNm(panel, Layout.Geometry) <= li.FixesUpTo;
        foreach (var (kind, vector, index, drawn) in new[]
        {
            (NdObjectKind.Navaid, li.NavaidVector, 0, panel.Sta), (NdObjectKind.Fix, li.FixVector, 1, fixesDrawn), (NdObjectKind.Airport, li.AirportVector, 2, panel.Arpt),
        })
        {
            uint begin = UInt(efis + (uint)(vector + li.VectorBegin)), end = UInt(efis + (uint)(vector + li.VectorEnd));
            if (end < begin || (end - begin) % (uint)li.ElementSize != 0)
                throw new Corrupt(Invariant($"the {kind} list: its end 0x{end:x} does not follow its begin 0x{begin:x}"));
            long count = (end - begin) / li.ElementSize;
            if (count > ListCap) throw new Corrupt(Invariant($"the {kind} list: {count} elements, more than {ListCap}"));
            counts[index] = (int)count;
            if (count == 0 || !drawn) continue;
            var elements = Block(begin, (int)(end - begin), $"the {kind} list");
            for (int k = 0; k < count; k++)
            {
                uint obj = BinaryPrimitives.ReadUInt32LittleEndian(elements.AsSpan(k * li.ElementSize + li.ObjectPointer));
                var n = new NodeBlock(obj, Block(obj, objectSize, $"the {kind} list: an object"));
                string name = Name(n, li.ObjectName, $"the {kind} list: an object");
                if (name.Length == 0) throw new Corrupt($"the {kind} list: an object's name is empty");
                var position = Position(n, li.ObjectPosition, $"the {kind} list: {name}")
                    ?? throw new Corrupt($"the {kind} list: {name}'s position is not valid");
                objects.Add(kind == NdObjectKind.Navaid
                    ? new NdMapObject(kind, name, position, n.Int(li.StationFlags), n.Int(li.StationClass))
                    : new NdMapObject(kind, name, position, 0, 0));
            }
        }
        return (objects, counts);
    }

    private (NdTrafficState State, List<NdTraffic> Targets) Traffic(uint tcas, uint options)
    {
        var t = Layout.Traffic;
        var targets = new List<NdTraffic>();
        uint list = UInt(tcas + (uint)t.List);
        bool listShown = false;
        int testValue = 0;
        if (list != 0)
        {
            // The head, then the target pointers up to its count (PMDG reads no further).
            var head = Block(list, new[] { t.ListShown + 1, t.TestField + 4, t.Count + 1 }.Max(), "the traffic list");
            listShown = head[t.ListShown] != 0;
            testValue = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(t.TestField));
            int count = head[t.Count];
            if (count > t.MaxTargets) throw new Corrupt(Invariant($"the traffic list: {count} targets, more than {t.MaxTargets}"));
            var pointers = count == 0 ? Array.Empty<byte>() : Block(list + (uint)t.Targets, 4 * count, "the traffic list's targets");
            int size = new[] { t.RelativeAltitude, t.Range, t.Bearing }.Max() + 4;
            size = Math.Max(size, Math.Max(t.ThreatAndTrend, t.Present) + 1);
            for (int k = 0; k < count; k++)
            {
                uint at = BinaryPrimitives.ReadUInt32LittleEndian(pointers.AsSpan(4 * k));
                var target = Block(at, size, "the traffic list: a target");
                if (target[t.Present] == 0) continue;
                float altitude = BinaryPrimitives.ReadSingleLittleEndian(target.AsSpan(t.RelativeAltitude));
                float range = BinaryPrimitives.ReadSingleLittleEndian(target.AsSpan(t.Range));
                float bearing = BinaryPrimitives.ReadSingleLittleEndian(target.AsSpan(t.Bearing));
                if (!float.IsFinite(altitude) || !float.IsFinite(range) || !float.IsFinite(bearing) || range < 0)
                    throw new Corrupt("the traffic list: a target's altitude, range or bearing is not a number");
                byte b = target[t.ThreatAndTrend];
                targets.Add(new NdTraffic(altitude, range, bearing, b >> t.ThreatShift, (sbyte)(b << 4) >> 4, k));
            }
        }
        var state = new NdTrafficState(listShown, B(tcas, t.Powered, 0), B(tcas, t.SelfTest, 0), B(tcas, t.SelfTestShown, 0), testValue,
            B(tcas, t.TaOnly, 0), B(options, t.ShowAllTraffic, 0), I(options, t.FilterRange, 0), I(options, t.FilterSeparation, 0));
        return (state, targets);
    }

    private (List<NdTunedStation> Tuned, List<NdReceiver> Receivers) Receivers(uint sim, uint glass, uint misc)
    {
        var tu = Layout.Tuned;
        var rx = Layout.Receivers;
        var tuned = new List<NdTunedStation>();
        var receivers = new List<NdReceiver>();
        for (int k = 0; k < tu.Count; k++)
        {
            uint vorAt = sim + (uint)(tu.Stations + k * tu.Stride);
            var vor = new NodeBlock(vorAt, Block(vorAt, tu.Stride, "a VOR receiver"));
            string ident = Name(vor, tu.Ident, "a VOR receiver's ident", rx.VorFrequencyText - tu.Ident);
            int state = vor.Int(tu.State);
            double dme = BinaryPrimitives.ReadSingleLittleEndian(vor.Bytes.AsSpan(tu.DmeDistance));
            tuned.Add(new NdTunedStation(ident, Position(vor, tu.Position, $"the VOR {ident}"), state, dme, D(glass, tu.Course, k)));

            uint adfAt = sim + (uint)(rx.AdfStations + k * rx.AdfStride);
            var adf = new NodeBlock(adfAt, Block(adfAt, rx.AdfStride, "an ADF receiver"));
            receivers.Add(new NdReceiver(
                Int(glass + (uint)(rx.Switch.Offset + rx.Switch.Stride * k + rx.SwitchSideStride * Side)),
                ident, Name(vor, rx.VorFrequencyText, "a VOR receiver's frequency", tu.Position - rx.VorFrequencyText), state,
                Bool(misc + (uint)(rx.Powered.Offset + rx.Powered.Stride * k)), B(glass, rx.VorFailed, k), B(glass, rx.DmeFailed, k), D(glass, rx.Dme, k),
                Name(adf, rx.AdfIdent, "an ADF receiver's ident", rx.AdfFrequencyText - rx.AdfIdent),
                Name(adf, rx.AdfFrequencyText, "an ADF receiver's frequency", rx.AdfValid - rx.AdfFrequencyText), adf.Int(rx.AdfValid),
                B(glass, rx.AdfFailed, k)));
        }
        return (tuned, receivers);
    }

    /// <summary>
    /// The APP/VOR part, read only while PMDG draws it (APP or VOR) and the layout has it: the receiver the
    /// Captain's block shows (inside the simulator interface's stations, else corrupt), its frequency, the FMC sources
    /// and index; the APP ident only while APP prints it (an FMC source or an ILS frequency; its record inside the
    /// glass data object), the station's ident or frequency text only while VOR prints one (no ILS frequency); the
    /// course and DME (a number where printed), the DME's fail flag; in VOR the VOR's fail flag and TO/FROM.
    /// </summary>
    private NdApproachSample? Approach(NdPanelState panel, uint efis, uint sim, uint glass)
    {
        var l = Layout;
        var modes = l.Geometry.Modes;
        if (l.Approach is not { } part || (panel.Mode != modes.App && panel.Mode != modes.Vor)) return null;
        var k = part.Block;
        var o = l.Objects;
        bool app = panel.Mode == modes.App;
        int nav = I(efis, k.NavIndex, Side);
        if (nav < 0 || nav >= l.Tuned.Count || k.Stations + (long)(nav + 1) * k.StationStride > o.Sim.Size)
            throw new Corrupt(Invariant($"the ILS/VOR block: NAV index {nav} is none of the {l.Tuned.Count} receivers"));
        uint stationAt = sim + (uint)(k.Stations + nav * k.StationStride);
        int frequency = Int(stationAt + (uint)k.Frequency);
        var sources = k.FmcSources.Select(f => I(glass, f, Side)).ToList();
        int fmcIndex = I(efis, k.FmcIndex, Side);
        bool fmc = sources.Contains(k.FmcSourceValue), ils = k.IsIls(frequency);
        string? approachIdent = null, vorIdent = null, vorFrequency = null;
        if (app && (fmc || ils))
        {
            int record = fmc ? fmcIndex : nav;
            if (record < 0 || k.ApproachIdents + (long)(record + 1) * k.ApproachIdentStride > o.Glass.Size)
                throw new Corrupt(Invariant($"the ILS/VOR block: the approach ident's record {record} lies outside CGlassData"));
            uint at = glass + (uint)(k.ApproachIdents + record * k.ApproachIdentStride);
            approachIdent = Name(new NodeBlock(at, Block(at, k.ApproachIdentStride, "the approach ident")), 0, "the approach ident", k.ApproachIdentStride);
        }
        else if (!app && !ils)
        {
            var station = new NodeBlock(stationAt, Block(stationAt, k.StationStride, "the ILS/VOR block's station"));
            if (station.Bytes[k.Ident] != 0) vorIdent = Name(station, k.Ident, "the ILS/VOR block's ident", k.FrequencyText - k.Ident);
            else vorFrequency = Name(station, k.FrequencyText, "the ILS/VOR block's frequency", k.StationStride - k.FrequencyText);
        }
        double course = D(glass, k.Course, nav), dme = D(glass, k.Dme, nav);
        bool dmeFailed = B(glass, k.DmeFailed, nav);
        if (!double.IsFinite(course)) throw new Corrupt("the ILS/VOR block: the course is not a number");
        // PMDG prints the DME's number only from its shown-from value up (NaN fails that test: " ---").
        if (!dmeFailed && dme >= k.DmeShownFrom && !double.IsFinite(dme)) throw new Corrupt("the ILS/VOR block: the DME is not a number");
        bool vorFailed = B(glass, k.VorFailed, nav);
        int toFrom = app ? 0 : I(glass, k.ToFrom, nav);

        // The course pointer and CDI: the side's course; the receiver's state; the mode's deviation (APP of the
        // record its FMC source chooses), a number while PMDG draws the bar.
        var cdi = part.Cdi;
        double pointerCourse = D(glass, cdi.PointerCourse, Side);
        int state = I(glass, cdi.ReceiverState, nav);
        bool cdiSource = I(glass, cdi.FmcSource, Side) == k.FmcSourceValue;
        int deviationRecord = app && cdiSource ? fmcIndex : nav;
        var deviationField = app ? cdi.ApproachDeviation : cdi.VorDeviation;
        if (deviationRecord < 0 || deviationField.Offset + (long)(deviationRecord + 1) * deviationField.Stride > o.Glass.Size)
            throw new Corrupt(Invariant($"the deviation bar: record {deviationRecord} lies outside CGlassData"));
        double deviation = D(glass, deviationField, deviationRecord);
        bool barDrawn = (cdiSource || !vorFailed) && state >= cdi.ReceiverStateFrom;
        if (barDrawn && !double.IsFinite(deviation)) throw new Corrupt("the deviation bar: the deviation is not a number");

        // The glideslope, APP only: the receiver's deviation (a number while PMDG draws the pointer) and fail flag.
        var gs = part.Glideslope;
        double glideslope = 0;
        bool glideslopeFailed = false;
        if (app)
        {
            glideslope = D(glass, gs.Deviation, nav);
            glideslopeFailed = B(glass, gs.Failed, nav);
            bool scale = !glideslopeFailed || I(glass, gs.FmcSource, Side) == k.FmcSourceValue;
            if (scale && state == gs.PointerState && !double.IsFinite(glideslope)) throw new Corrupt("the glideslope: the deviation is not a number");
        }
        return new NdApproachSample(nav, fmcIndex, sources, frequency, approachIdent, vorIdent, vorFrequency, course, dme, dmeFailed, vorFailed, toFrom,
            pointerCourse, state, deviation, glideslope, glideslopeFailed, app && B(glass, part.Messages.LocFlag.Failed, nav));
    }

    private List<NdFixRing> FixPage()
    {
        var f = Layout.FixPage;
        var rings = new List<NdFixRing>();
        uint cdu = UInt(f.CduGlobal + (uint)(4 * Side));
        if (cdu == 0) return rings;
        if (!_memory.IsInsideModule(cdu, f.CduSize)) throw new Corrupt("the CDU: its object lies outside the module");
        uint page = UInt(cdu + (uint)(f.Pages + 4 * f.FixPage));
        if (page == 0) return rings;
        var entries = Block(page + (uint)f.Entries, 4 * f.EntryCount, "the CDU FIX page");
        int entrySize = new[] { f.Rings + 8 * f.RingCount, f.Radials + 8 * f.RadialCount, f.FixPointer + 4, f.Abeam + Layout.Route.Position.Valid + 1 }.Max();
        int objectSize = new[] { NameField, f.ObjectPosition + Layout.Route.Position.Valid + 1, f.ObjectType + 4 }.Max();
        for (int j = 0; j < f.EntryCount; j++)
        {
            uint entryAt = BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(4 * j));
            if (entryAt == 0) continue;
            var entry = new NodeBlock(entryAt, Block(entryAt, entrySize, "the CDU FIX page: an entry"));
            uint objectAt = entry.UInt(f.FixPointer);
            if (objectAt == 0) continue;
            var fix = new NodeBlock(objectAt, Block(objectAt, objectSize, "the CDU FIX page: a fix"));
            string name = Name(fix, 0, "the CDU FIX page: a fix");
            if (name.Length == 0) throw new Corrupt("the CDU FIX page: a fix's name is empty");
            // PMDG places the fix without testing its position: one not valid cannot be placed, so is left out.
            if (Position(fix, f.ObjectPosition, $"the CDU FIX page: {name}") is not { } position) continue;
            rings.Add(new NdFixRing(name, fix.Int(f.ObjectType), position,
                Enumerable.Range(0, f.RingCount).Select(k => entry.Double(f.Rings + 8 * k)).ToList(),
                Enumerable.Range(0, f.RadialCount).Select(k => entry.Double(f.Radials + 8 * k)).ToList(),
                Position(entry, f.Abeam, $"the CDU FIX page: {name}'s abeam point")));
        }
        return rings;
    }

    /// <summary>Each message condition's (and the VDEV scale's) source: an object's field, its element for the FMC
    /// the ND shows (the ND page's FMC index, unchecked by PMDG: one outside the object is corrupt), or the Captain's
    /// panel's.</summary>
    private Dictionary<NdSource, long> ConditionValues(IReadOnlyDictionary<GlassObject, uint> pointers, uint panel, int fmc)
    {
        var values = new Dictionary<NdSource, long>();
        var m = Layout.Messages;
        foreach (var source in m.Messages.Concat(m.MapOptionLabels).SelectMany(t => t.AnyOf.SelectMany(a => a)).Concat(m.Vdev.DrawnWhile)
            .Select(c => c.Source).Distinct())
        {
            if (source.Kind == NdSourceKind.ByFmc && pointers.TryGetValue(source.Owner, out uint fmcOwner))
            {
                long element = source.Field.Offset + (long)source.Field.Stride * fmc;
                if (element < 0 || element + 1 > source.Owner.Size)
                    throw new Corrupt(Invariant($"the map's FMC: FMC {fmc} has no flag {source.Field.Offset} in its object"));
                values[source] = Byte(fmcOwner + (uint)element);
                continue;
            }
            uint owner;
            if (source.Kind == NdSourceKind.Object && pointers.TryGetValue(source.Owner, out uint at)) owner = at;
            else if (source.Kind == NdSourceKind.Panel) owner = panel;
            else continue;
            uint field = owner + (uint)source.Field.Offset;
            values[source] = source.Field.Kind == GlassFieldKind.Int ? Int(field) : Byte(field);
        }
        return values;
    }

    // ---- reading ----

    /// <summary>Thrown when a field cannot be read: the memory went away (lost).</summary>
    private sealed class ReadFailed : Exception;

    /// <summary>Thrown when a check fails: the reading is discarded as corrupt.</summary>
    private sealed class Corrupt(string reason) : Exception(reason);

    private bool Pointer(GlassObject o, out uint pointer)
    {
        pointer = 0;
        if (!_memory.TryReadInt32(o.Global, out int p) || p == 0) return false;
        pointer = (uint)p;
        return _memory.IsInsideModule(pointer, o.Size);
    }

    private bool IsPlan(int p) => p >= 0 && Layout.Route.PlanBase + (long)(p + 1) * Layout.Route.PlanStride <= Layout.Objects.Fms.Size;

    private uint Plan(uint fms, int p) =>
        IsPlan(p) ? fms + (uint)(Layout.Route.PlanBase + p * Layout.Route.PlanStride) : throw new Corrupt(Invariant($"the route: plan {p} is no plan in CFMS"));

    /// <summary>A block inside the module (corrupt when it is not: a pointer that left the module).</summary>
    private byte[] Block(uint at, int length, string what)
    {
        if (at == 0 || !_memory.IsInsideModule(at, (uint)length)) throw new Corrupt(Invariant($"{what} at 0x{at:x} lies outside the module"));
        var bytes = new byte[length];
        return _memory.TryRead(at, bytes) ? bytes : throw new ReadFailed();
    }

    /// <summary>An inline C string of at most <paramref name="field"/> bytes with its NUL, printable ASCII.</summary>
    private static string Name(NodeBlock n, int offset, string what, int field = NameField)
    {
        var span = n.Bytes.AsSpan(offset, Math.Min(field, n.Bytes.Length - offset));
        int end = span.IndexOf((byte)0);
        if (end < 0) throw new Corrupt(Invariant($"{what}'s name has no end within {field} bytes"));
        foreach (byte b in span[..end])
            if (b is < 0x20 or > 0x7E) throw new Corrupt($"{what}'s name holds a byte that is not printable");
        return System.Text.Encoding.ASCII.GetString(span[..end]);
    }

    /// <summary>A PMDG position at <paramref name="offset"/>, its longitude turned east positive (PMDG holds it
    /// positive west: live, KATL at 84.4 W reads +84.4); null while not valid; corrupt when out of range.</summary>
    private GeoPoint? Position(NodeBlock n, int offset, string what) => Position(n.Bytes, offset, what);

    private GeoPoint? Position(byte[] bytes, int offset, string what)
    {
        var f = Layout.Route.Position;
        if (bytes[offset + f.Valid] == 0) return null;
        double lat = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset + f.LatitudeRad));
        double lon = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(offset + f.LongitudeRad));
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > Math.PI / 2 || Math.Abs(lon) > Math.PI)
            throw new Corrupt($"{what}'s position is out of range");
        return new GeoPoint(lat, -lon);
    }

    private static uint At(uint owner, GlassField f, int element) => owner + (uint)(f.Offset + f.Stride * element);

    private double D(uint owner, GlassField f, int element) => Double(At(owner, f, element));

    private int I(uint owner, GlassField f, int element = 0) => Int(At(owner, f, element));

    private bool B(uint owner, GlassField f, int element = 0) => Bool(At(owner, f, element));

    private double Double(uint at) => _memory.TryReadDouble(at, out double v) ? v : throw new ReadFailed();

    private int Int(uint at) => _memory.TryReadInt32(at, out int v) ? v : throw new ReadFailed();

    private uint UInt(uint at) => (uint)Int(at);

    private byte Byte(uint at) => _memory.TryReadByte(at, out byte v) ? v : throw new ReadFailed();

    private bool Bool(uint at) => Byte(at) != 0;
}
