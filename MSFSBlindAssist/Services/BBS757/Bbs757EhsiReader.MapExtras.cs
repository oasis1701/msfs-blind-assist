using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The rest of the map (plan 28's <see cref="Bbs757EhsiMapExtras"/>). Light, only where they are drawn (B757-49): TERR
/// while the IRS is valid, in every mode (the button's L:var from its handle's cached value, the picture byte, the two
/// elevations and, while the button is on, their colors from the air data computer's altitude and vertical speed,
/// BlueBird's thresholds and the simulator's <see cref="GearKey"/>), and the wind in MAP, VOR and ILS while the IRS is
/// valid (the static show flag as BlueBird left it, which alone decides, the simulator's <see cref="WindSpeedKey"/> and
/// <see cref="WindDirectionKey"/>). Full, while the map is drawn (MAP with the IRS valid, or PLAN): the two runway records and the route path's two deceleration
/// vectors, walked all or nothing (B757-36). The simulator values are inputs where BlueBird's draw takes them (S10), never
/// compared with memory (B757-30). A null object pointer is none; one outside the module, a name that is not text, a
/// count over the cap or a position not finite and in range is torn.
/// </summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>The draw's simulator inputs (S10): the wind arrow and number, and the gear for TERR's amber threshold.</summary>
    public const string WindSpeedKey = "AMBIENT WIND VELOCITY", WindDirectionKey = "AMBIENT WIND DIRECTION", GearKey = "GEAR POSITION";

    /// <param name="mapItems">Whether to read the runways and the deceleration points: a full reading while the map is
    /// drawn (no row could show them otherwise, so they are not read).</param>
    /// <param name="terrDrawn">Whether TERR can show: while the IRS is valid, in every mode. Otherwise none of its values
    /// is read (TERR off, no picture, zeros).</param>
    /// <param name="windDrawn">Whether the wind can show: in MAP, VOR and ILS while the IRS is valid. Otherwise its show
    /// flag is not read (clear).</param>
    private bool TryMapExtras(uint nd, uint source, Bbs757EhsiMapExtras x, bool terrDrawn, bool windDrawn, bool mapItems,
        int routeId, Bbs757SimValues? sim, out Bbs757EhsiMapExtrasReading? reading)
    {
        reading = null;
        // The wind's show flag alone says what the screen shows: BlueBird's draw clears it under its minimum ground speed
        // itself, so the reader never reads the nd's ground speed for it.
        byte windShown = 0;
        if (windDrawn && !_memory.TryReadByte(x.WindShown, out windShown)) return false;

        bool terr = false, picture = false;
        Bbs757EhsiTerrainNumber highestNumber = new(0, Bbs757EhsiTerrainColor.None), lowestNumber = highestNumber;
        if (terrDrawn)
        {
            if (!Flag(nd, x.TerrainPicture, out picture) || !Int(nd, x.DisplaySide, out int side)) return false;
            // The nd keeps its own copy of the TERR button's handle (66 bytes, at the nd): its value is at +HandleValue.
            if (!At(nd, x.TerrHandle, out uint handle) || !F64(handle, x.HandleValue, out double terrValue) || !double.IsFinite(terrValue))
                return false;

            // The two elevations: the side's record in the terrain static, s16 feet.
            long record = (long)x.TerrainStatic + x.TerrainRecords + (side == x.CaptainSide ? 0 : 1) * (long)x.TerrainSideStride;
            if (record < 0 || record > uint.MaxValue || !U16((uint)record, x.TerrainHighest, out ushort highestRaw)
                || !U16((uint)record, x.TerrainLowest, out ushort lowestRaw))
                return false;
            short highest = unchecked((short)highestRaw), lowest = unchecked((short)lowestRaw);

            // The adc's altitude and vertical speed only color the numbers TERR shows: read only while its button is on.
            terr = terrValue != 0;
            double? altitude = null;
            if (terr && !TryTerrainAltitude(source, x, out altitude)) return false;
            double? gear = Input(sim, GearKey);
            highestNumber = TerrainNumber(highest, altitude, gear, x);
            lowestNumber = TerrainNumber(lowest, altitude, gear, x);
        }

        IReadOnlyList<Bbs757EhsiRunway>? runways = null;
        IReadOnlyList<Bbs757EhsiMapPoint>? starts = null, ends = null;
        if (mapItems)
        {
            var list = new List<Bbs757EhsiRunway>(2);
            foreach (int at in new[] { x.DepartureRunway, x.ArrivalRunway })
            {
                if (!TryRunway(nd, at, x, out var runway)) return false;
                if (runway is { } r) list.Add(r);
            }
            runways = list.AsReadOnly();
            if (!TryDeceleration(routeId, x, out starts, out ends)) return false;
        }

        reading = new Bbs757EhsiMapExtrasReading(
            Terr: terr, TerrainPicture: picture, Highest: highestNumber, Lowest: lowestNumber,
            WindShown: windShown != 0, WindSpeed: Input(sim, WindSpeedKey), WindDirection: Input(sim, WindDirectionKey),
            Runways: runways, DecelerationStarts: starts, DecelerationEnds: ends,
            TerrWord: x.TerrWord, NoWindWord: x.NoWindWord);
        return true;
    }

    /// <summary>A simulator input's value, null when the monitor has not delivered it or it is not a number.</summary>
    private static double? Input(Bbs757SimValues? sim, string key) =>
        sim?[key] is double v && double.IsFinite(v) ? v : null;

    /// <summary>The altitude TERR's colors compare with: the air data computer's (through the instrument source), predicted
    /// the layout's seconds ahead while descending faster than its rate, never below its floor. No air data computer:
    /// null (no color word). A value that is not a number is torn.</summary>
    private bool TryTerrainAltitude(uint source, Bbs757EhsiMapExtras x, out double? altitude)
    {
        altitude = null;
        if (!Link(source, x.SourceToAdc, out uint adc)) return false;
        if (adc == 0) return true;
        if (!Good(adc) || !F64(adc, x.AdcAltitude, out double alt) || !F64(adc, x.AdcVerticalSpeed, out double vs)
            || !double.IsFinite(alt) || !double.IsFinite(vs))
            return false;
        if (vs < x.LookAheadBelow) alt = Math.Max(x.LookAheadFloor, alt + vs / 60.0 * x.LookAheadSeconds);
        altitude = alt;
        return true;
    }

    /// <summary>
    /// One elevation as drawn: the feet divided by the layout's divisor (integer division, as BlueBird's), never below 0;
    /// its color by the feet above the altitude: above <c>RedAbove</c> red, above the amber threshold (the gear-down one
    /// when <paramref name="gear"/> is the layout's gear-down value) amber, else green. With the gear not delivered yet the
    /// color is still told where both amber thresholds agree; between them it is <see cref="Bbs757EhsiTerrainColor.None"/>.
    /// No altitude: no color.
    /// </summary>
    internal static Bbs757EhsiTerrainNumber TerrainNumber(short feet, double? altitude, double? gear, Bbs757EhsiMapExtras x)
    {
        int shown = Math.Max(0, feet / x.TerrainDivisor);
        if (altitude is not { } alt) return new(shown, Bbs757EhsiTerrainColor.None);
        double d = feet - alt;
        Bbs757EhsiTerrainColor color;
        if (d > x.RedAbove) color = Bbs757EhsiTerrainColor.Red;
        else if (gear is { } g)
            color = d > (g == x.GearDown ? x.AmberAboveGearDown : x.AmberAbove) ? Bbs757EhsiTerrainColor.Amber : Bbs757EhsiTerrainColor.Green;
        else if (d > Math.Max(x.AmberAbove, x.AmberAboveGearDown)) color = Bbs757EhsiTerrainColor.Amber;
        else if (d <= Math.Min(x.AmberAbove, x.AmberAboveGearDown)) color = Bbs757EhsiTerrainColor.Green;
        else color = Bbs757EhsiTerrainColor.None;
        return new(shown, color);
    }

    /// <summary>A runway record in the nd: none when its narrow name is empty (BlueBird draws nothing then); otherwise
    /// the name and both ends, which must be finite and in range. False when torn.</summary>
    private bool TryRunway(uint nd, int offset, Bbs757EhsiMapExtras x, out Bbs757EhsiRunway? runway)
    {
        runway = null;
        Span<byte> name = stackalloc byte[StringBytes];
        if (!At(nd, offset, out uint at) || !At(at, x.RunwayName, out uint nameAt) || !_memory.TryRead(nameAt, name)
            || !TryReadNarrow(name, out string text))
            return false;
        if (text.Length == 0) return true;
        if (!Position(at, x.RunwayFirstLatitude, x.RunwayFirstLongitude, out double lat1, out double lon1)
            || !Position(at, x.RunwaySecondLatitude, x.RunwaySecondLongitude, out double lat2, out double lon2))
            return false;
        runway = new Bbs757EhsiRunway(text, lat1, lon1, lat2, lon2);
        return true;
    }

    /// <summary>The route path's two deceleration vectors (where the next deceleration starts and ends), each at most the
    /// layout's cap, every point finite and in range. No path for the route: none. False when torn (also a vector that
    /// changed while it was read).</summary>
    private bool TryDeceleration(int routeId, Bbs757EhsiMapExtras x, out IReadOnlyList<Bbs757EhsiMapPoint>? starts,
        out IReadOnlyList<Bbs757EhsiMapPoint>? ends)
    {
        starts = ends = Array.Empty<Bbs757EhsiMapPoint>();
        if (!TryFindPath(routeId, out uint path, out bool found)) return false;
        if (!found) return true;
        starts = PointVector(path, x.DecelerationStart, x);
        ends = PointVector(path, x.DecelerationEnd, x);
        return starts is not null && ends is not null;
    }

    private IReadOnlyList<Bbs757EhsiMapPoint>? PointVector(uint path, int offset, Bbs757EhsiMapExtras x)
    {
        // The stride and the offsets inside it are checked once, with the layout (Bbs757EhsiReader.Tears.cs).
        int stride = x.DecelerationStride;
        if (!Header(path, offset, out var header) || !Entries(header, stride, x.MaxDecelerationPoints, out uint count))
            return null;
        var points = new List<Bbs757EhsiMapPoint>((int)count);
        if (count > 0)
        {
            var bytes = new byte[count * stride];
            if (!_memory.TryRead(header.Begin, bytes)) return null;
            for (int i = 0; i < count; i++)
            {
                var span = bytes.AsSpan(i * stride, stride);
                double lon = BinaryPrimitives.ReadDoubleLittleEndian(span[x.DecelerationLongitude..]);
                double lat = BinaryPrimitives.ReadDoubleLittleEndian(span[x.DecelerationLatitude..]);
                if (!InRange(lat, lon)) return null;
                points.Add(new Bbs757EhsiMapPoint(lat, lon));
            }
        }
        if (!Header(path, offset, out var after) || after != header) return null;
        return points.AsReadOnly();
    }

    // ---- the leg position rule (plan 26's fix) ----

    /// <summary>
    /// Where a leg's position is: for a leg whose kind is the layout's <c>CapabilityLegKind</c> (a navaid leg) while the
    /// layout has <see cref="Bbs757EhsiMapExtras"/>, BlueBird's rule over the leg's capability set: the first of
    /// <c>CapabilityChoices</c> whose capability the set holds gives the offsets; one marked <c>UnlessZero</c> falls back
    /// to the kind's own offsets when its position reads (0, 0), as does a set matching none. Every other leg (and every
    /// leg without the part, or while its capability sets are at their tear limit: <see cref="CapabilityRule"/>): plan
    /// 26's rule, the kind's entry of <c>LegPositions</c>, else the plain offsets. False when the set is torn.
    /// </summary>
    private bool LegPositionOffsets(byte[] block, int kind, out int latitudeAt, out int longitudeAt)
    {
        var l = Layout;
        latitudeAt = l.LegLatitude;
        longitudeAt = l.LegLongitude;
        foreach (var p in l.LegPositions)
            if (p.Kind == kind)
            {
                latitudeAt = p.Latitude;
                longitudeAt = p.Longitude;
                break;
            }
        if (CapabilityRule is not { } x || kind != x.CapabilityLegKind) return true;
        if (!TryCapabilities(block, x, out var set))
        {
            // Counted against the map extras when it tore a route walk (Bbs757EhsiReader.Tears.cs).
            _capabilityTorn = true;
            return false;
        }
        foreach (var choice in x.CapabilityChoices)
        {
            if (!set.Contains(choice.Capability)) continue;
            if (choice.UnlessZero)
            {
                if (!BlockDouble(block, choice.Latitude, out double lat) || !BlockDouble(block, choice.Longitude, out double lon))
                    return false;
                if (lat == 0 && lon == 0) return true;
            }
            latitudeAt = choice.Latitude;
            longitudeAt = choice.Longitude;
            return true;
        }
        return true;
    }

    /// <summary>A leg's capability set, an unordered set of ints: its first node and size in the leg, each node's next
    /// pointer and value. All or nothing: a size over the layout's cap, a node outside the module, a list ending before
    /// the size or going on past it is torn.</summary>
    private bool TryCapabilities(byte[] block, Bbs757EhsiMapExtras x, out HashSet<int> set)
    {
        set = new HashSet<int>();
        if (!BlockInt(block, x.LegCapabilityFirst, out int firstValue) || !BlockInt(block, x.LegCapabilityCount, out int sizeValue))
            return false;
        uint size = unchecked((uint)sizeValue);
        if (size > (uint)Math.Max(0, x.MaxCapabilities)) return false;
        uint node = unchecked((uint)firstValue);
        for (uint i = 0; i < size; i++)
        {
            if (!Good(node) || !Link(node, x.CapabilityNodeNext, out uint next) || !Int(node, x.CapabilityNodeValue, out int value))
                return false;
            set.Add(value);
            node = next;
        }
        return node == 0;
    }
}
