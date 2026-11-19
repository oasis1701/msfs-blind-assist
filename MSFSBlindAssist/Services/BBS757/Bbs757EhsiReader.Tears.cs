namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// A plan 28 part that keeps tearing (the whole-plan review's I1). A part's tear makes the reading corrupt, all or nothing
/// (B757-40), until it has torn <see cref="TearLimit"/> readings in a row; from then on, while it keeps tearing, that part
/// alone is null with the reason <see cref="UnexpectedValues"/>, so its one "not read" row shows and the rest of the EHSI
/// (the other parts, the route, Ctrl+W) keeps working: the shared monitor stops a reader after ten corrupt readings in a
/// row, and several parts' tear conditions follow state that can last (an off-path radius, a FIX entry, a traffic kind, a
/// leg's capability set). Each part counts its own tears; its next good read resets the count, and a reader built for a
/// new session starts at none. Plan 26's own walks (route, legs, points) are not counted: they tear the reading every
/// time. A layout value that can never change at run time is checked once, here, when the reader is built: its part is
/// then missing with a reason naming the field, never torn on a read.
/// </summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>How many readings in a row a part tears before it reads "not read: unexpected values" instead of making
    /// the reading corrupt: well under the monitor's ten-corrupt stop.</summary>
    internal const int TearLimit = 4;

    /// <summary>The reason of a part that has torn <see cref="TearLimit"/> readings in a row.</summary>
    internal const string UnexpectedValues = "unexpected values";

    /// <summary>Plan 28's five parts, as the tear counts and the layout checks know them.</summary>
    private enum Part { Approach, Tcas, MapOptions, MapExtras, MapGuides }

    private const int PartCount = 5;

    private readonly Action<string> _log;

    /// <summary>Per part: the layout value that keeps it from being read (checked once), else null.</summary>
    private readonly string?[] _layoutProblems;

    /// <summary>Per part: how many readings in a row it has torn.</summary>
    private readonly int[] _tears = new int[PartCount];

    /// <summary>Set while this reading's legs take plan 26's position rule because the map extras are at their tear limit
    /// on a leg's capability set (as with the part missing).</summary>
    private bool _capabilityRuleOff;

    /// <summary>Set when a capability set tore during this reading's route walk.</summary>
    private bool _capabilityTorn;

    /// <summary>A part of the layout the reader reads: null when the layout lacks it or one of its values is unusable.</summary>
    private T? Usable<T>(T? part, Part p) where T : class => _layoutProblems[(int)p] is null ? part : null;

    /// <summary>The map extras whose leg position rule the legs take this reading: none while the part is unusable or its
    /// capability sets are at their tear limit.</summary>
    private Bbs757EhsiMapExtras? CapabilityRule => _capabilityRuleOff ? null : Usable(Layout.MapExtras, Part.MapExtras);

    /// <summary>Whether one more tear of <paramref name="p"/> reaches the limit (this reading then reads it as not
    /// read).</summary>
    private bool AtLimit(Part p) => _tears[(int)p] + 1 >= TearLimit;

    /// <summary>Counts one reading of <paramref name="p"/>: a tear adds one (logged once, on reaching the limit), a good
    /// read resets the count (logged when it ends an episode).</summary>
    private void Count(Part p, bool torn)
    {
        int i = (int)p;
        if (!torn)
        {
            if (_tears[i] >= TearLimit) _log($"EHSI part {p}: read again after {_tears[i]} torn readings in a row");
            _tears[i] = 0;
            return;
        }
        if (_tears[i] < int.MaxValue) _tears[i]++;
        if (_tears[i] == TearLimit)
            _log($"EHSI part {p}: torn {TearLimit} readings in a row; listed as not read: {UnexpectedValues} until it reads again");
    }

    /// <summary>The reason a part is not in this reading: the layout's own (the part missing), a layout value the reader
    /// cannot use, or <see cref="UnexpectedValues"/> while it is at its tear limit; null when it was read or this reading
    /// does not hold it.</summary>
    private string? Reason(object? layoutPart, string? layoutMissing, Part p, bool overLimit) =>
        layoutPart is null ? layoutMissing : _layoutProblems[(int)p] ?? (overLimit ? UnexpectedValues : null);

    /// <summary>
    /// The layout values a part's reads rely on that can never change at run time, checked once: a divisor, stride, length
    /// or cap the reads divide by, step by or size a buffer with, and the offsets the reads take inside a block of the
    /// layout's own size. A bad one is the part's reason ("layout value TrafficStride not above 0"), never a tear on every
    /// read.
    /// </summary>
    private static string?[] LayoutProblems(Bbs757EhsiLayout l)
    {
        var problems = new string?[PartCount];
        static string NotAbove(string name, double v, double floor) => v > floor ? "" : $"layout value {name} not above {floor}";
        static string Below(string name, int v) => v >= 0 ? "" : $"layout value {name} below 0";
        static string Inside(string name, int offset, int size, int block) =>
            offset >= 0 && (long)offset + size <= block ? "" : $"layout value {name} outside its block";
        // A draw constant in pixels (the radial's dashes): finite and within a generous bound of the screen.
        static string Pixels(string name, double v) =>
            double.IsFinite(v) && Math.Abs(v) <= Bbs757EhsiRadialDashes.MaxPixels
                ? "" : $"layout value {name} not a number within {Bbs757EhsiRadialDashes.MaxPixels.ToString(System.Globalization.CultureInfo.InvariantCulture)} pixels";
        static string? First(params string[] checks) => checks.FirstOrDefault(c => c.Length > 0);

        if (l.Approach is { } a)
            problems[(int)Part.Approach] = First(
                a.StationIdentLength is >= 1 and <= MaxIdentLength ? "" : $"layout value {nameof(a.StationIdentLength)} not 1 to {MaxIdentLength}");
        if (l.Tcas is { } t)
            problems[(int)Part.Tcas] = First(
                NotAbove(nameof(t.TrafficStride), t.TrafficStride, 0), Below(nameof(t.MaxTraffic), t.MaxTraffic));
        if (l.MapOptions is { } o)
            problems[(int)Part.MapOptions] = First(
                o.NavaidIdentLength is >= 1 and <= MaxIdentLength ? "" : $"layout value {nameof(o.NavaidIdentLength)} not 1 to {MaxIdentLength}",
                Below(nameof(o.NavaidIdent), o.NavaidIdent), Below(nameof(o.NavaidFrequency), o.NavaidFrequency),
                Below(nameof(o.NavaidKind), o.NavaidKind), Below(nameof(o.NavaidClass), o.NavaidClass),
                Below(nameof(o.NavaidLatitude), o.NavaidLatitude), Below(nameof(o.NavaidLongitude), o.NavaidLongitude),
                NotAbove(nameof(o.AirportStride), o.AirportStride, 0),
                NotAbove(nameof(o.AirportLabelLength), o.AirportLabelLength, 0),
                Inside(nameof(o.AirportName), o.AirportName, StringBytes, o.AirportStride),
                Inside(nameof(o.AirportRecordLatitude), o.AirportRecordLatitude, 2 * Word, o.AirportStride),
                Inside(nameof(o.AirportRecordLongitude), o.AirportRecordLongitude, 2 * Word, o.AirportStride),
                Below(nameof(o.WaypointIdent), o.WaypointIdent), Below(nameof(o.WaypointLatitude), o.WaypointLatitude),
                Below(nameof(o.WaypointLongitude), o.WaypointLongitude),
                Below(nameof(o.NdbIdent), o.NdbIdent), Below(nameof(o.NdbLatitude), o.NdbLatitude),
                Below(nameof(o.NdbLongitude), o.NdbLongitude),
                Below(nameof(o.MaxItems), o.MaxItems), Below(nameof(o.RoundLimit), o.RoundLimit));
        if (l.MapExtras is { } x)
            problems[(int)Part.MapExtras] = First(
                NotAbove(nameof(x.TerrainDivisor), x.TerrainDivisor, 0),
                NotAbove(nameof(x.DecelerationStride), x.DecelerationStride, 0),
                Inside(nameof(x.DecelerationLatitude), x.DecelerationLatitude, 2 * Word, x.DecelerationStride),
                Inside(nameof(x.DecelerationLongitude), x.DecelerationLongitude, 2 * Word, x.DecelerationStride),
                Below(nameof(x.MaxDecelerationPoints), x.MaxDecelerationPoints),
                Below(nameof(x.MaxCapabilities), x.MaxCapabilities));
        if (l.MapGuides is { } g)
            problems[(int)Part.MapGuides] = First(
                NotAbove(nameof(g.FixEntryStride), g.FixEntryStride, 0),
                Inside(nameof(g.FixEntryRadial), g.FixEntryRadial, Word, g.FixEntryStride),
                Inside(nameof(g.FixEntryDistance), g.FixEntryDistance, Word, g.FixEntryStride),
                Below(nameof(g.FixEntries), g.FixEntries),
                Inside(nameof(g.LegName), g.LegName, StringBytes, l.LegStride),
                Inside(nameof(g.LegNavaidName), g.LegNavaidName, StringBytes, l.LegStride),
                g.RadialDashes is >= 0 and <= Bbs757EhsiRadialDashes.MaxCount
                    ? "" : $"layout value {nameof(g.RadialDashes)} not 0 to {Bbs757EhsiRadialDashes.MaxCount}",
                Pixels(nameof(g.RadialStart), g.RadialStart), Pixels(nameof(g.RadialDashStep), g.RadialDashStep),
                Pixels(nameof(g.RadialDashLength), g.RadialDashLength));
        return problems;
    }
}
