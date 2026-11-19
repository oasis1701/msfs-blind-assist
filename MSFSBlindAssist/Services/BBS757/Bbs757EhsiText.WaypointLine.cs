namespace MSFSBlindAssist.Services.BBS757;

/// <summary>The Ctrl+W line (plan 26), from the same reading the rows come from.</summary>
public static partial class Bbs757EhsiText
{
    /// <summary>
    /// The Ctrl+W line from a reading, the owner's wording: the active waypoint's name, then the top line's distance and
    /// ETA as drawn: "KOLM, 37.5 NM, 1533.3z"; "KOLM, 37.5 NM, no ETA"; a VM or FM leg "KOLM, no distance, no ETA"; no leg
    /// "No active waypoint". In VOR and ILS the top line shows DME (plan 28's): "KOLM, EHSI in VOR mode". Not powered:
    /// "EHSI waypoint unavailable: display dark"; a corrupt reading: "EHSI waypoint unavailable: no reading yet"
    /// (<see cref="Bbs757EhsiDisplay.WaypointLine"/> names the monitor's reason instead).
    /// </summary>
    public static string WaypointLine(Bbs757EhsiReading r)
    {
        if (r.Corrupt) return Unavailable + NoReadingYet;
        if (!r.Powered) return Unavailable + DisplayDark;
        if (r.Active is not { } a) return NoActiveWaypoint;
        string name = WaypointName(a);
        if (Is(r, Bbs757EhsiMode.Vor) || Is(r, Bbs757EhsiMode.Ils)) return name + ", EHSI in " + ModeWord(r) + " mode";
        if (Distance(r) is not { } nm) return name + ", no distance, no ETA";
        return name + ", " + Bbs757EhsiGeometry.TopLineText(nm) + " " + Word(r.Words.Nm) + ", "
            + (Eta(r, nm) is { } eta ? eta + Word(r.Words.EtaSuffix) : "no ETA");
    }

    /// <summary>The Ctrl+W line while the monitor may have no reading: <paramref name="unavailableReason"/> (the monitor's
    /// own, "no reading yet" when null) when there is none or it is corrupt.</summary>
    internal static string WaypointLine(Bbs757EhsiReading? latest, string? unavailableReason) =>
        latest is null || latest.Corrupt ? Unavailable + (unavailableReason ?? NoReadingYet) : WaypointLine(latest);
}
