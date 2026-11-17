using System.Buffers.Binary;
using System.Text;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// Reads the Captain's EHSI (<c>bbs::nd</c>) from the 757 module's memory through a derived layout (BBS757-D2, WASM-2:
/// every address and field offset from the layout), as the EHSI reader's <see cref="IBbs757DisplaySampler{TReading}"/>.
/// The nd is the first pointer in the gauge manager's span whose target starts with the nd's vptr (the First Officer's
/// follows it); the EFIS panel, the instrument source (and through it the selected IRS), the MCP and the route hang off
/// it. Owns its lease of the simulator handle. Every read is bounds-checked against committed module memory (WASM-4),
/// re-walked each call, and the regions are asked about once per reading (WASM-15). Nothing is compared with a simulator
/// value (B757-30): the map's center is the nd's own copy of the IRS position, which the map draw uses, and the top
/// line's position is the simulator's <c>PLANE LATITUDE</c> and <c>PLANE LONGITUDE</c>, which the top line's draw reads
/// (S10), taken as inputs when the monitor gives them.
/// <para>Each reading holds the light set: the screen's fields and <see cref="Bbs757EhsiReading.Active"/>, the leg the
/// top line is drawn to. The route, the pending modification and the map's points are read only when
/// <see cref="FullWanted"/> says so. The route walk is all or nothing (the same idea as the upper EICAS's, B757-35): any
/// pointer outside the module, torn vector, over-long label, position out of range or more legs than the layout's cap
/// makes the whole reading <see cref="Bbs757EhsiReading.Corrupt"/>, never a partial route. False only when the nd cannot
/// be found or one of its fixed fields cannot be read: the memory moved or went away.</para>
/// </summary>
public sealed partial class Bbs757EhsiReader : IBbs757DisplaySampler<Bbs757EhsiReading>
{
    // A wasm32 pointer, i32 and UTF-32 code point are 4 bytes; a libc++ std::u32string is 12 bytes. Neither is BlueBird's:
    // every offset into BlueBird's own objects is a layout field.
    private const int Word = 4;
    private const int StringBytes = 3 * Word;

    /// <summary>A label's longest length in code points. BlueBird's labels are idents and "(800)"-like altitude names;
    /// a longer one is a torn read.</summary>
    private const int MaxLabelLength = 32;

    /// <summary>The path registry walk's cycle guard: how many nodes it visits before it gives up and finds no path. The
    /// registry keeps a node for each route id a flight's edits made (measured live: 9, 10, 11 and 12 at once), so this is
    /// far above any real list; reaching it is "no path", never corrupt.</summary>
    private const int MaxPathNodes = 4096;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Bbs757EhsiLayout Layout { get; }

    /// <summary>Whether this tick wants the full set (route, modification, points). Asked once per reading.</summary>
    public Func<bool> FullWanted { get; init; } = () => false;

    /// <param name="log">Where a part's lasting tear and its recovery go (CORE-15): the EHSI channel unless a test injects
    /// one.</param>
    public Bbs757EhsiReader(ModuleMemory memory, IDisposable ownedSource, Bbs757EhsiLayout layout, Action<string>? log = null)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
        _log = log ?? (line => Log.Channel(Bbs757EhsiDisplay.LogChannel).Info(line));
        _layoutProblems = LayoutProblems(layout);
        _constants = new Bbs757EhsiConstants(layout.RadiusFactor, layout.AircraftYFactor, layout.ClipInset, layout.MarkerMargin,
            layout.MinEtaGroundSpeed, layout.ExcessLimit);
        _words = new Bbs757EhsiWords(layout.HdgWord, layout.TrkWord, layout.MagWord, layout.TrueWord, layout.MapWord,
            layout.NmWord, layout.EtaSuffix, layout.TopOfClimbWord, layout.TopOfDescentWord, layout.EndOfDescentWord,
            layout.ExcessWord);
    }

    /// <summary>The layout's map constants, carried in every reading (powered or not) so the rows need no layout.</summary>
    private readonly Bbs757EhsiConstants _constants;

    /// <summary>The layout's words, carried in every reading (powered or not) so the rows need no layout.</summary>
    private readonly Bbs757EhsiWords _words;

    /// <summary>The top line's position inputs (S10): BlueBird's top line reads these simulator variables fresh on each
    /// draw, so the reader takes them where the monitor gives them, never comparing them with memory (B757-30).</summary>
    public const string LatitudeKey = "PLANE LATITUDE", LongitudeKey = "PLANE LONGITUDE";

    /// <summary>The Captain's nd: the first manager pointer whose target carries its vptr. False when none in the
    /// manager's span does (the module is not where the anchor said, not ready, or not this build).</summary>
    public bool TryFindNd(out uint nd)
    {
        nd = 0;
        for (uint off = 0; off + Word <= Layout.ManagerSpan; off += Word)
        {
            if (!_memory.TryReadInt32(Layout.ManagerBase + off, out int pv)) continue;
            uint p = unchecked((uint)pv);
            if (p == 0 || !_memory.IsInsideModule(p, Word)) continue;
            if (_memory.TryReadInt32(p, out int vptr) && unchecked((uint)vptr) == Layout.NdVptr)
            {
                nd = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>The reading of a screen that is not powered: every field its default, not corrupt (a dark screen's
    /// fields hold anything, and it is never spoken).</summary>
    private Bbs757EhsiReading Dark => new(false, null, -1, false, 0, false, false, false, 0, false, false, 0, 0, 0,
        0, 0, default, _constants, _words, null, -1, false, false, 0, null, null, null, Corrupt: false);

    /// <summary>The reading of a torn or out-of-range read: powered (it is lit), nothing else, and an empty route and
    /// points in a full reading, never a partial one.</summary>
    private Bbs757EhsiReading Bad(bool full) => new(true, null, -1, false, 0, false, false, false, 0, false, false, 0, 0, 0,
        0, 0, default, _constants, _words, null, -1, false, false, 0, full ? Array.Empty<Bbs757EhsiLeg>() : null, null,
        full ? Array.Empty<Bbs757EhsiPoint>() : null, Corrupt: true);

    /// <summary>A reading with the top line's position from the nd's own copy (no simulator values).</summary>
    public bool TryRead(out Bbs757EhsiReading reading) => Read(null, out reading);

    /// <summary>A reading with the top line's position from the simulator's <see cref="LatitudeKey"/> and
    /// <see cref="LongitudeKey"/> when both are given, finite and in range; else the nd's own copy. The map's center stays
    /// the nd's. Plan 28's map extras take the wind and the gear from <paramref name="sim"/> too
    /// (Bbs757EhsiReader.MapExtras.cs).</summary>
    public bool TryRead(Bbs757SimValues sim, out Bbs757EhsiReading reading) => Read(sim, out reading);

    private static (double Latitude, double Longitude)? SimPosition(Bbs757SimValues sim) =>
        sim[LatitudeKey] is double lat && sim[LongitudeKey] is double lon && InRange(lat, lon) ? (lat, lon) : null;

    private bool Read(Bbs757SimValues? sim, out Bbs757EhsiReading reading)
    {
        reading = null!;
        var position = sim is { } values ? SimPosition(values) : null;
        var l = Layout;
        bool full = FullWanted();
        using var regions = _memory.RememberRegions();
        if (!TryFindNd(out uint nd)) return false;
        if (!Flag(nd, l.Powered, out bool powered) || !Flag(nd, l.Failed, out bool failed) || !Flag(nd, l.Blanked, out bool blanked))
            return false;
        if (!powered || failed || blanked)
        {
            reading = Dark;
            return true;
        }

        // The fields at the nd itself and the static clock: one that cannot be read means the memory moved or went away.
        // A read through a pointer (the EFIS panel, the source, the MCP, the IRS, the route) that fails is only a torn
        // pointer (the selected IRS changes with the source switch): a corrupt reading, never a re-locate.
        if (!Link(nd, l.NdToEfis, out uint efis) || !Link(nd, l.NdToSource, out uint source) || !Link(nd, l.NdToMcp, out uint mcp)
            || !Link(nd, l.NdToRoute, out uint route)
            || !F64(nd, l.Range, out double range) || !Flag(nd, l.RangeChanging, out bool rangeChanging)
            || !Flag(nd, l.Valid, out bool valid) || !Flag(nd, l.Aligning, out bool aligning)
            || !F32(nd, l.Number, out float number) || !Int(nd, l.TrackUp, out int trackUp)
            || !Int(nd, l.TrueReference, out int trueReference) || !F32(nd, l.Width, out float width)
            || !F32(nd, l.Height, out float height) || !F64(nd, l.Longitude, out double longitude)
            || !F64(nd, l.Latitude, out double latitude) || !F64(nd, l.CenterLatitude, out double centerLatitude)
            || !F64(nd, l.CenterLongitude, out double centerLongitude) || !F64(nd, l.Rotation, out double rotation)
            || !F64(nd, l.ScreenX, out double screenX) || !F64(nd, l.ScreenY, out double screenY)
            || !Flag(nd, l.DataSwitch, out bool dataSwitch) || !Int(nd, l.ExcessCount, out int excessRaw)
            || !_memory.TryReadDouble(l.Clock, out double clock))
            return false;
        // The count is a u32: a wild value clamps, never reads negative.
        int excessCount = (int)Math.Min((uint)excessRaw, int.MaxValue);

        if (!Good(efis) || !Good(source) || !Good(mcp) || !Good(route)
            || !Int(efis, l.EfisMode, out int modeValue) || !Flag(efis, l.EfisCentered, out bool centered)
            || !F64(mcp, l.McpHeading, out double bug) || !Link(source, l.SourceToIrs, out uint irs))
        {
            reading = Bad(full);
            return true;
        }

        // A null selected IRS pointer is no IRS: BlueBird's draw returns a black screen, so the reading is the dark one
        // (not corrupt). One outside the module, or a ground speed that cannot be read through it, is torn.
        if (irs == 0)
        {
            reading = Dark;
            return true;
        }
        if (!Good(irs) || !F64(irs, l.IrsGroundSpeed, out double groundSpeed))
        {
            reading = Bad(full);
            return true;
        }

        if (!l.Modes.Words.TryGetValue(modeValue, out string? mode) || !AllFinite(range, number, width, height, longitude, latitude,
                centerLatitude, centerLongitude, rotation, screenX, screenY, bug, groundSpeed, clock))
        {
            reading = Bad(full);
            return true;
        }

        if (!Flag(route, l.RouteActive, out bool routeActive) || !Flag(route, l.RouteModified, out bool modified)
            || !Int(route, l.RouteModification, out int modificationValue) || !Int(route, l.RouteId, out int routeId))
        {
            reading = Bad(full);
            return true;
        }
        uint modification = unchecked((uint)modificationValue);
        bool pending = modified && Good(modification);

        // The route the top line uses: the pending modification, else the route itself. In the light reading only the
        // leg 0 is read, and the route is not walked at all while a modification is pending.
        RouteRead? routeRead = null, modificationRead = null;
        _capabilityRuleOff = _capabilityTorn = false;
        bool WalkRoutes()
        {
            routeRead = modificationRead = null;
            if (pending && (modificationRead = Walk(modification, full)) is null) return false;
            return (pending && !full) || (routeRead = Walk(route, full)) is not null;
        }
        bool torn = !WalkRoutes();
        // A leg's capability set is the map extras' (Bbs757EhsiReader.Tears.cs): below their tear limit it tears the reading;
        // at it, the legs are walked again by plan 26's rule, as with the part missing.
        bool extrasTorn = false;
        if (torn && _capabilityTorn)
        {
            extrasTorn = true;
            if (AtLimit(Part.MapExtras))
            {
                _capabilityRuleOff = true;
                torn = !WalkRoutes();
            }
            if (torn) Count(Part.MapExtras, true);
        }
        if (torn)
        {
            reading = Bad(full);
            return true;
        }
        var activeLeg = pending ? modificationRead!.First : routeRead!.First;
        // The route's own leg 0 in the full list, after the flown legs; none in a light reading or an empty route.
        int activeIndex = full && routeRead!.First is not null ? routeRead.Flown : -1;

        IReadOnlyList<Bbs757EhsiLeg>? routeLegs = null, modificationLegs = null;
        IReadOnlyList<Bbs757EhsiPoint>? points = null;
        if (full)
        {
            routeLegs = routeRead!.Legs;
            if (pending) modificationLegs = modificationRead!.Legs;
            if (!TryPoints(nd, routeId, routeRead, out var list))
            {
                reading = Bad(full);
                return true;
            }
            points = list;
        }

        var map = new Bbs757EhsiMap(centerLatitude, centerLongitude, rotation, screenX, screenY, width, height);

        // Plan 28's parts (Bbs757EhsiReader.Parts.cs), in the same regions scope: one torn part is a corrupt reading until it
        // has torn the limit of readings in a row (Bbs757EhsiReader.Tears.cs). The map options' loop measures from the nd's
        // own map and aircraft position (never the simulator's), against the routes and airports just read.
        var mapScene = full ? MapScene(map, range, latitude, longitude, routeLegs, modificationLegs, points) : null;
        if (!TryParts(nd, source, modeValue, full, valid, mapScene, routeId, sim, extrasTorn, out var parts))
        {
            reading = Bad(full);
            return true;
        }

        reading = new Bbs757EhsiReading(
            Powered: true, Mode: mode, ModeValue: modeValue, Centered: centered, Range: range, RangeChanging: rangeChanging,
            Valid: valid, Aligning: aligning, Number: number, TrackUp: trackUp != 0, TrueReference: trueReference != 0,
            HeadingBug: bug, Latitude: position?.Latitude ?? latitude, Longitude: position?.Longitude ?? longitude,
            GroundSpeed: groundSpeed, Clock: clock,
            Map: map,
            Constants: _constants, Words: _words, Active: activeLeg, ActiveIndex: activeIndex, RouteActive: routeActive, DataSwitch: dataSwitch, ExcessCount: excessCount,
            Route: routeLegs, Modification: modificationLegs, Points: points, Corrupt: false);
        reading = WithParts(reading, parts);
        return true;
    }

    private static bool AllFinite(params double[] values) => values.All(double.IsFinite);

    // ---- the route ----

    /// <summary>One route as walked: every leg (the flown ones, then the route's own) in a full read, and the route's own
    /// leg 0 in either (null when it has none). <see cref="Flown"/> is how many of <see cref="Legs"/> are flown ones.</summary>
    private sealed record RouteRead(IReadOnlyList<Bbs757EhsiLeg> Legs, int Flown, Bbs757EhsiLeg? First);

    /// <summary>A leg vector's begin and end.</summary>
    private readonly record struct Vector(uint Begin, uint End);

    private bool Header(uint route, int offset, out Vector v)
    {
        v = default;
        Span<byte> bytes = stackalloc byte[2 * Word];
        if (!At(route, offset, out uint a) || !_memory.TryRead(a, bytes)) return false;
        v = new Vector(BinaryPrimitives.ReadUInt32LittleEndian(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes[Word..]));
        return true;
    }

    /// <summary>How many legs a vector holds; false when torn: end before begin, not whole legs, storage null, or more
    /// legs than the layout's cap.</summary>
    private bool Count(Vector v, out int count)
    {
        count = 0;
        var l = Layout;
        if (l.LegStride <= 0 || v.End < v.Begin || (v.End - v.Begin) % (uint)l.LegStride != 0) return false;
        uint n = (v.End - v.Begin) / (uint)l.LegStride;
        if (n > (uint)Math.Max(0, l.MaxLegs) || (n > 0 && v.Begin == 0)) return false;
        count = (int)n;
        return true;
    }

    /// <summary>A route's legs: the flown vector then the route's own, checked all or nothing. Null when any check fails,
    /// or a vector changed while it was walked. A light read takes only the route's own leg 0.</summary>
    private RouteRead? Walk(uint route, bool full)
    {
        var l = Layout;
        if (!Header(route, l.RouteFlownLegs, out var flownHeader) || !Header(route, l.RouteLegs, out var ownHeader)
            || !Count(flownHeader, out int flownCount) || !Count(ownHeader, out int ownCount) || flownCount + ownCount > l.MaxLegs)
            return null;

        var block = new byte[l.LegStride];
        var legs = new List<Bbs757EhsiLeg>(full ? flownCount + ownCount : 0);
        Bbs757EhsiLeg? first = null;
        if (full)
            for (int i = 0; i < flownCount; i++)
            {
                if (!TryReadLeg(flownHeader.Begin + (uint)i * (uint)l.LegStride, block, out var leg)) return null;
                legs.Add(leg);
            }
        for (int i = 0; i < ownCount; i++)
        {
            if (!full && i > 0) break;
            if (!TryReadLeg(ownHeader.Begin + (uint)i * (uint)l.LegStride, block, out var leg)) return null;
            if (i == 0) first = leg;
            if (full) legs.Add(leg);
        }

        // A vector BlueBird changed while it was walked (a reallocation, or an erase) reads torn, whatever the walk found.
        if (!Header(route, l.RouteFlownLegs, out var flownAfter) || !Header(route, l.RouteLegs, out var ownAfter)
            || flownAfter != flownHeader || ownAfter != ownHeader)
            return null;
        return new RouteRead(legs.AsReadOnly(), flownCount, first);
    }

    /// <summary>One leg, its whole block in one read. False when the block is outside the module, a field the layout
    /// names lies beyond the stride, the label is torn or too long, or the position is not finite and in range
    /// (a discontinuity's is 0, 0 whatever its fields hold).</summary>
    private bool TryReadLeg(uint at, byte[] block, out Bbs757EhsiLeg leg)
    {
        leg = default;
        var l = Layout;
        if (!_memory.TryRead(at, block)) return false;
        if (!BlockInt(block, l.LegKind, out int kind) || !BlockInt(block, l.LegTerminator, out int terminator)
            || !BlockInt(block, l.LegConstraintKind, out int cKind) || !BlockInt(block, l.LegConstraintFirst, out int cFirst)
            || !BlockInt(block, l.LegConstraintSecond, out int cSecond) || !BlockDouble(block, l.LegEta, out double eta)
            || !double.IsFinite(eta) || !TryReadLabel(block, out string label))
            return false;

        bool discontinuity = kind == l.DiscontinuityKind;
        double latitude = 0, longitude = 0;
        if (!discontinuity)
        {
            // Plan 26's offsets by kind; a navaid leg by its capability set when the layout has plan 28's map extras
            // (Bbs757EhsiReader.MapExtras.cs).
            if (!LegPositionOffsets(block, kind, out int latitudeAt, out int longitudeAt)
                || !BlockDouble(block, latitudeAt, out latitude) || !BlockDouble(block, longitudeAt, out longitude)
                || !InRange(latitude, longitude))
                return false;
        }

        leg = new Bbs757EhsiLeg(label, latitude, longitude, discontinuity, terminator,
            Manual: l.ManualTerminators.Contains(terminator),
            Symbol: !discontinuity && !l.NoSymbolTerminators.Contains(terminator),
            cKind, cFirst, cSecond, eta);
        return true;
    }

    private bool TryReadLabel(byte[] block, out string label)
    {
        label = "";
        int at = Layout.LegLabel;
        return at >= 0 && at + StringBytes <= block.Length && TryReadString(block.AsSpan(at, StringBytes), out label);
    }

    /// <summary>A <c>std::u32string</c> given its 12 bytes, in the libc++ alternate layout this build uses for its strings
    /// (measured live 2026-11-11; the narrow strings read the same way in <c>ModuleEfbMemory.ReadStdString</c>): byte +11's
    /// high bit is the long flag. Long: data pointer +0, size in code points +4, capacity with the top bit +8. Short:
    /// byte +11 is the size (0 to 2) and the code points are inline from +0. Either way the text is at most
    /// <see cref="MaxLabelLength"/> code points, each a Unicode scalar value other than 0, followed by the 0 terminator
    /// libc++ keeps. False when torn.</summary>
    private bool TryReadString(ReadOnlySpan<byte> s, out string text)
    {
        text = "";
        int length;
        byte[] units;
        if ((s[StringBytes - 1] & 0x80) == 0)
        {
            length = s[StringBytes - 1];
            if (length > 2) return false;
            // The terminator word follows the last code point and nothing beyond it is read: a moved-from string keeps
            // stale words after it (a discontinuity's label is 0, 4, 8 with byte 11 = 0). After two code points the
            // terminator word shares its last byte with the size byte (+11), so only the bytes before it count.
            units = new byte[(length + 1) * Word];
            s[..(length * Word)].CopyTo(units);
            int terminatorEnd = Math.Min((length + 1) * Word, StringBytes - 1);
            if (s[(length * Word)..terminatorEnd].IndexOfAnyExcept((byte)0) >= 0) return false;
        }
        else
        {
            uint data = BinaryPrimitives.ReadUInt32LittleEndian(s);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(s[Word..]);
            if (size > MaxLabelLength) return false;
            length = (int)size;
            // An empty long string still has its pointer and terminator: checked like any other.
            units = new byte[(length + 1) * Word];
            if (!_memory.TryRead(data, units)) return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(units.AsSpan(length * Word)) != 0) return false;
        var sb = new StringBuilder(length);
        Span<char> utf16 = stackalloc char[2];
        for (int i = 0; i < length; i++)
        {
            uint codePoint = BinaryPrimitives.ReadUInt32LittleEndian(units.AsSpan(i * Word));
            if (codePoint == 0 || !Rune.TryCreate(codePoint, out Rune r)) return false;
            sb.Append(utf16[..r.EncodeToUtf16(utf16)]);
        }
        text = sb.ToString();
        return true;
    }

    private static bool BlockInt(byte[] block, int offset, out int v)
    {
        v = 0;
        if (offset < 0 || offset + Word > block.Length) return false;
        v = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(offset));
        return true;
    }

    private static bool BlockDouble(byte[] block, int offset, out double v)
    {
        v = 0;
        if (offset < 0 || offset + 2 * Word > block.Length) return false;
        v = BinaryPrimitives.ReadDoubleLittleEndian(block.AsSpan(offset));
        return true;
    }

    private static bool InRange(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;

    // ---- the points ----

    /// <summary>The map's points in the order the screen draws them: T/C (its flag set), T/D (both flags set), E/D (the
    /// route's own leg the path names; none for one beyond the legs), then the origin and destination airports (one with no
    /// name is not drawn). With no path registry entry for the route there are no T/C, T/D or E/D. False when torn.</summary>
    private bool TryPoints(uint nd, int routeId, RouteRead route, out IReadOnlyList<Bbs757EhsiPoint> points)
    {
        points = Array.Empty<Bbs757EhsiPoint>();
        var l = Layout;
        var list = new List<Bbs757EhsiPoint>();
        if (!TryFindPath(routeId, out uint path, out bool found)) return false;
        if (found)
        {
            if (!Flag(path, l.TopOfClimbFlag, out bool climb) || !Flag(path, l.TopOfDescentFlagA, out bool descentA)
                || !Flag(path, l.TopOfDescentFlagB, out bool descentB) || !Int(path, l.EndOfDescentLeg, out int endLeg))
                return false;
            if (climb)
            {
                if (!Position(path, l.TopOfClimbLatitude, l.TopOfClimbLongitude, out double lat, out double lon)) return false;
                list.Add(new Bbs757EhsiPoint(l.TopOfClimbWord, false, lat, lon));
            }
            if (descentA && descentB)
            {
                if (!Position(path, l.TopOfDescentLatitude, l.TopOfDescentLongitude, out double lat, out double lon)) return false;
                list.Add(new Bbs757EhsiPoint(l.TopOfDescentWord, false, lat, lon));
            }
            // The index counts the route's own legs, after any flown ones in the list. The path is recomputed apart from
            // the legs and can lag them: an index past the route's own legs draws no E/D (not torn).
            int own = route.Legs.Count - route.Flown;
            if (endLeg >= 0 && endLeg < own)
            {
                var leg = route.Legs[route.Flown + endLeg];
                // A discontinuity has no position: nothing to draw there (not torn).
                if (!leg.Discontinuity)
                    list.Add(new Bbs757EhsiPoint(l.EndOfDescentWord, false, leg.Latitude, leg.Longitude));
            }
        }
        Span<byte> header = stackalloc byte[StringBytes];
        foreach (int record in new[] { l.OriginAirport, l.DestinationAirport })
        {
            if (!At(nd, record, out uint at)) return false;
            if (!_memory.TryRead(at, header) || !TryReadString(header, out string name)) return false;
            if (name.Length == 0) continue;
            if (!Position(at, l.AirportLatitude, l.AirportLongitude, out double lat, out double lon)) return false;
            list.Add(new Bbs757EhsiPoint(name, true, lat, lon));
        }
        points = list.AsReadOnly();
        return true;
    }

    /// <summary>The route's path object: the registry's node whose key is the route's id, its value the path. Not found
    /// is not torn, and neither is the walk reaching its cycle guard (<see cref="MaxPathNodes"/>): both are no path. A node
    /// or the value outside the module is torn.</summary>
    private bool TryFindPath(int routeId, out uint path, out bool found)
    {
        path = 0;
        found = false;
        var l = Layout;
        if (!At(l.PathRegistry, l.PathRegistryFirst, out uint firstAt) || !_memory.TryReadInt32(firstAt, out int firstValue)) return false;
        uint node = unchecked((uint)firstValue);
        for (int visited = 0; visited < MaxPathNodes; visited++)
        {
            if (node == 0) return true;
            if (!Good(node) || !Link(node, 0, out uint next) || !Int(node, l.PathNodeKey, out int key)) return false;
            if (key == routeId)
            {
                if (!Link(node, l.PathNodeValue, out path) || !Good(path)) return false;
                found = true;
                return true;
            }
            node = next;
        }
        // The cycle guard: a list this long (or one that loops) holds no path for the route.
        return true;
    }

    private bool Position(uint b, int latitudeAt, int longitudeAt, out double latitude, out double longitude)
    {
        longitude = 0;
        return F64(b, latitudeAt, out latitude) && F64(b, longitudeAt, out longitude) && InRange(latitude, longitude);
    }

    // ---- fixed fields ----

    /// <summary><paramref name="b"/> plus <paramref name="offset"/>, false when the offset is negative or the sum leaves the
    /// 32-bit address space (it never wraps into the module).</summary>
    private static bool At(uint b, long offset, out uint address)
    {
        long a = b + offset;
        address = unchecked((uint)a);
        return offset >= 0 && a <= uint.MaxValue;
    }

    private bool Int(uint b, int offset, out int v)
    {
        v = 0;
        return At(b, offset, out uint a) && _memory.TryReadInt32(a, out v);
    }

    /// <summary>A pointer field: false when it cannot be read; the pointer itself may still be null or outside the
    /// module (<see cref="Good"/>).</summary>
    private bool Link(uint b, int offset, out uint p)
    {
        p = 0;
        if (!Int(b, offset, out int v)) return false;
        p = unchecked((uint)v);
        return true;
    }

    /// <summary>Whether a pointer names something inside the module: not null, and its first word committed.</summary>
    private bool Good(uint p) => p != 0 && _memory.IsInsideModule(p, Word);

    private bool F64(uint b, int offset, out double v)
    {
        v = 0;
        return At(b, offset, out uint a) && _memory.TryReadDouble(a, out v);
    }

    /// <summary>An f32 (ModuleMemory reads no f32: four bytes, little-endian).</summary>
    private bool F32(uint b, int offset, out float v)
    {
        v = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!At(b, offset, out uint a) || !_memory.TryRead(a, bytes)) return false;
        v = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return true;
    }

    private bool Flag(uint b, int offset, out bool set)
    {
        set = false;
        if (!At(b, offset, out uint a) || !_memory.TryReadByte(a, out byte v)) return false;
        set = v != 0;
        return true;
    }

    public void Dispose() => _ownedSource.Dispose();
}
