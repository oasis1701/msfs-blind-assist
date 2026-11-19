namespace MSFSBlindAssist.Services.BBS757;

/// <summary>The TCAS (plan 28's <see cref="Bbs757EhsiTcas"/>): its messages' sources in every reading (drawn in every
/// mode), the traffic list in a full one in MAP while the IRS is valid. The list is walked all or nothing (B757-36): a torn vector, an entry outside the module, more entries
/// than the layout's cap, a kind that is not one of BlueBird's four, an altitude that is not a number or a position not
/// finite and in range makes the reading corrupt. A null computer is none (the nd draws no TCAS), an empty list no
/// traffic.</summary>
public sealed partial class Bbs757EhsiReader
{
    /// <param name="full">Whether to walk the traffic list: a full reading in MAP while the IRS is valid, the only place
    /// the screen draws it (a list no row can show is not read).</param>
    private bool TryTcas(uint nd, Bbs757EhsiTcas t, bool full, out Bbs757EhsiTcasReading? reading)
    {
        reading = null;
        var words = new Bbs757EhsiTcasWords(t.TcasWord, t.FailWord, t.OffWord, t.TfcWord, t.TaOnlyWord, t.TestWord, t.TrafficWord);
        if (!Link(nd, t.NdToTcas, out uint tcas) || !Flag(nd, t.TrafficSwitch, out bool tfc)
            || !Flag(nd, t.MessagesAllowed, out bool gate))
            return false;
        if (tcas == 0)
        {
            reading = new Bbs757EhsiTcasReading(false, false, false, Bbs757EhsiTcasLevel.Off, false, false, false, tfc, gate,
                full ? Array.Empty<Bbs757EhsiTraffic>() : null, words, t.TagDivisor);
            return true;
        }
        if (!Good(tcas) || !Flag(tcas, t.TcasPowered, out bool powered) || !Flag(tcas, t.TcasFailed, out bool failed)
            || !Int(tcas, t.TcasLevel, out int levelValue) || !Flag(tcas, t.TrafficAdvisory, out bool ta)
            || !Flag(tcas, t.ResolutionAdvisory, out bool ra) || !Flag(tcas, t.TcasTest, out bool test))
            return false;
        var level = levelValue == t.TaOnlyLevel ? Bbs757EhsiTcasLevel.TaOnly
            : levelValue == t.TaRaLevel ? Bbs757EhsiTcasLevel.TaRa
            : Bbs757EhsiTcasLevel.Off;

        IReadOnlyList<Bbs757EhsiTraffic>? traffic = null;
        if (full && (traffic = WalkTraffic(tcas, t)) is null) return false;
        reading = new Bbs757EhsiTcasReading(true, powered, failed, level, ta, ra, test, tfc, gate, traffic, words, t.TagDivisor);
        return true;
    }

    /// <summary>The traffic list in BlueBird's order, or null when torn (also when the vector changed while it was
    /// walked).</summary>
    private IReadOnlyList<Bbs757EhsiTraffic>? WalkTraffic(uint tcas, Bbs757EhsiTcas t)
    {
        // The stride and the cap are checked once, with the layout (Bbs757EhsiReader.Tears.cs).
        if (!Header(tcas, t.TrafficList, out var header)) return null;
        if (header.End < header.Begin || (header.End - header.Begin) % (uint)t.TrafficStride != 0) return null;
        uint count = (header.End - header.Begin) / (uint)t.TrafficStride;
        if (count > (uint)Math.Max(0, t.MaxTraffic) || (count > 0 && header.Begin == 0)) return null;

        var list = new List<Bbs757EhsiTraffic>((int)count);
        var block = new byte[t.TrafficStride];
        for (uint i = 0; i < count; i++)
        {
            if (!_memory.TryRead(header.Begin + i * (uint)t.TrafficStride, block)
                || !BlockInt(block, t.TrafficKind, out int kindValue) || !BlockInt(block, t.TrafficTrend, out int trendValue)
                || !BlockDouble(block, t.TrafficAltitude, out double feet) || !BlockDouble(block, t.TrafficLongitude, out double lon)
                || !BlockDouble(block, t.TrafficLatitude, out double lat)
                || !double.IsFinite(feet) || !InRange(lat, lon))
                return null;
            Bbs757EhsiTrafficKind kind;
            if (kindValue == t.KindOther) kind = Bbs757EhsiTrafficKind.Other;
            else if (kindValue == t.KindProximate) kind = Bbs757EhsiTrafficKind.Proximate;
            else if (kindValue == t.KindAdvisory) kind = Bbs757EhsiTrafficKind.Advisory;
            else if (kindValue == t.KindResolution) kind = Bbs757EhsiTrafficKind.Resolution;
            else return null;
            var trend = trendValue == t.TrendUp ? Bbs757EhsiTrafficTrend.Climbing
                : trendValue == t.TrendDown ? Bbs757EhsiTrafficTrend.Descending
                : Bbs757EhsiTrafficTrend.None;
            list.Add(new Bbs757EhsiTraffic(kind, trend, feet, lat, lon));
        }

        // BlueBird rebuilds the list on its own thread: one that changed while it was walked reads torn.
        if (!Header(tcas, t.TrafficList, out var after) || after != header) return null;
        return list.AsReadOnly();
    }
}
