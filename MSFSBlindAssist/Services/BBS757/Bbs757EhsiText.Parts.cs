namespace MSFSBlindAssist.Services.BBS757;

/// <summary>What plan 28's five parts share in the rows: their names as the pilot knows them, the one row of a part not
/// read, and whether the map's items are drawn.</summary>
public static partial class Bbs757EhsiText
{
    // Plan 28's parts as the pilot knows them, for the row of one not read.
    private const string ApproachPart = "VOR and ILS", TcasPart = "TCAS", MapOptionsPart = "Map options",
        MapExtrasPart = "TERR, wind and runways", MapGuidesPart = "FIX, descent and altitude guides";

    /// <summary>A part's one row where its rows would start when it is not read and a reason says why: "TCAS: not read:
    /// not in this layout file". No reason (a reading that does not hold the part): nothing.</summary>
    private static void NotRead(List<string> rows, string part, string? reason)
    {
        if (reason is not null) rows.Add(part + ": not read: " + reason);
    }

    /// <summary>Whether the map's items are drawn: MAP while the IRS is valid, or PLAN.</summary>
    private static bool MapDrawn(Bbs757EhsiReading r) => (Is(r, Bbs757EhsiMode.Map) && r.Valid) || Is(r, Bbs757EhsiMode.Plan);
}
