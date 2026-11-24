namespace MSFSBlindAssist.Services.BBS757;

/// <summary>Plan 28's parts: each read from its optional layout part, null with the layout's own reason when the part is
/// missing, and read only in the modes that draw it (a value no row can show is never read: it could only add a way to turn
/// corrupt). A part's walk is all or nothing (B757-36): a torn read makes the whole reading corrupt until the part has torn
/// <see cref="TearLimit"/> readings in a row, then the part alone is not read (Bbs757EhsiReader.Tears.cs).</summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>The parts of one reading and their reasons.</summary>
    private sealed record PartsRead(
        Bbs757EhsiApproachReading? Approach,
        string? ApproachMissing,
        Bbs757EhsiTcasReading? Tcas,
        string? TcasMissing,
        Bbs757EhsiMapOptionsReading? MapOptions,
        string? MapOptionsMissing,
        Bbs757EhsiMapExtrasReading? MapExtras,
        string? MapExtrasMissing,
        Bbs757EhsiMapGuidesReading? MapGuides,
        string? MapGuidesMissing);

    /// <summary>
    /// Every part the layout holds, each only where a row can show it: the VOR and ILS picture in VOR and ILS, the bearing
    /// pointers in every mode but PLAN while <paramref name="irsValid"/>; the TCAS messages in every reading (drawn in every
    /// mode), its traffic in a full reading in MAP while the IRS is valid; TERR while the IRS is valid and the wind in MAP,
    /// VOR and ILS while the IRS is valid (as drawn); while the
    /// map is drawn (MAP with the IRS valid, or PLAN) and the reading is full, the map options (against
    /// <paramref name="map"/>), the runways and deceleration points (the route's path by <paramref name="routeId"/>), the
    /// FIX pages and the off-path circles; in MAP the range arc (while the IRS is valid) and the VNAV deviation. The wind and
    /// gear come from <paramref name="sim"/>. <paramref name="extrasTorn"/>: a leg's capability set tore this reading's
    /// route walk, a tear of the map extras. False when a part tore below its limit (the reading is then corrupt).
    /// </summary>
    private bool TryParts(uint nd, uint source, int modeValue, bool full, bool irsValid, Bbs757EhsiLoopScene? map, int routeId,
        Bbs757SimValues? sim, bool extrasTorn, out PartsRead parts)
    {
        parts = null!;
        var l = Layout;
        bool vor = modeValue == (int)Bbs757EhsiMode.Vor, ils = modeValue == (int)Bbs757EhsiMode.Ils;
        bool mapMode = modeValue == (int)Bbs757EhsiMode.Map, plan = modeValue == (int)Bbs757EhsiMode.Plan;
        bool mapDrawn = (mapMode && irsValid) || plan;
        var read = new bool[PartCount];
        var torn = new bool[PartCount];

        Bbs757EhsiApproachReading? approach = null;
        if (Usable(l.Approach, Part.Approach) is { } a && !plan)
        {
            read[(int)Part.Approach] = true;
            torn[(int)Part.Approach] = !TryApproach(nd, source, a, vor || ils, ils, irsValid, out approach);
        }
        Bbs757EhsiTcasReading? tcas = null;
        if (Usable(l.Tcas, Part.Tcas) is { } t)
        {
            read[(int)Part.Tcas] = true;
            torn[(int)Part.Tcas] = !TryTcas(nd, t, full && mapMode && irsValid, out tcas);
        }
        Bbs757EhsiMapOptionsReading? options = null;
        if (Usable(l.MapOptions, Part.MapOptions) is { } o && full && map is not null && mapDrawn)
        {
            read[(int)Part.MapOptions] = true;
            torn[(int)Part.MapOptions] = !TryMapOptions(nd, o, map, out options);
        }
        Bbs757EhsiMapExtrasReading? extras = null;
        if (Usable(l.MapExtras, Part.MapExtras) is { } x)
        {
            read[(int)Part.MapExtras] = true;
            // A capability tear on a leg already counts for this reading: the rest is not read.
            torn[(int)Part.MapExtras] = extrasTorn
                || !TryMapExtras(nd, source, x, irsValid, irsValid && (mapMode || vor || ils), full && mapDrawn, routeId, sim, out extras);
        }
        Bbs757EhsiMapGuidesReading? guides = null;
        if (Usable(l.MapGuides, Part.MapGuides) is { } g && full && (mapMode || plan))
        {
            read[(int)Part.MapGuides] = true;
            torn[(int)Part.MapGuides] = !TryMapGuides(nd, source, g, mapMode, mapDrawn, irsValid, routeId, out guides);
        }

        // All or nothing until a part's tear has lasted: any part torn below its limit makes the reading corrupt.
        bool corrupt = false;
        for (int i = 0; i < PartCount; i++)
            if (read[i] && torn[i] && !AtLimit((Part)i)) corrupt = true;
        for (int i = 0; i < PartCount; i++)
            if (read[i]) Count((Part)i, torn[i]);
        if (corrupt) return false;

        bool Over(Part p) => torn[(int)p];
        parts = new PartsRead(
            Over(Part.Approach) ? null : approach, Reason(l.Approach, l.ApproachMissing, Part.Approach, Over(Part.Approach)),
            Over(Part.Tcas) ? null : tcas, Reason(l.Tcas, l.TcasMissing, Part.Tcas, Over(Part.Tcas)),
            Over(Part.MapOptions) ? null : options, Reason(l.MapOptions, l.MapOptionsMissing, Part.MapOptions, Over(Part.MapOptions)),
            Over(Part.MapExtras) ? null : extras, Reason(l.MapExtras, l.MapExtrasMissing, Part.MapExtras, Over(Part.MapExtras)),
            Over(Part.MapGuides) ? null : guides, Reason(l.MapGuides, l.MapGuidesMissing, Part.MapGuides, Over(Part.MapGuides)));
        return true;
    }

    /// <summary>The reading with its parts and their reasons.</summary>
    private static Bbs757EhsiReading WithParts(Bbs757EhsiReading reading, PartsRead parts) => reading with
    {
        Approach = parts.Approach,
        ApproachMissing = parts.ApproachMissing,
        Tcas = parts.Tcas,
        TcasMissing = parts.TcasMissing,
        MapOptions = parts.MapOptions,
        MapOptionsMissing = parts.MapOptionsMissing,
        MapExtras = parts.MapExtras,
        MapExtrasMissing = parts.MapExtrasMissing,
        MapGuides = parts.MapGuides,
        MapGuidesMissing = parts.MapGuidesMissing,
    };

    /// <summary>A byte as stored (<see cref="Flag"/> reads one as set or clear).</summary>
    private bool U8(uint b, int offset, out byte v)
    {
        v = 0;
        return At(b, offset, out uint a) && _memory.TryReadByte(a, out v);
    }
}
