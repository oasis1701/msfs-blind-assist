namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI map's NAV AID, ARPT and WPT items, the tuned stations and the VOR course lines, an optional part of
/// <see cref="Bbs757EhsiLayout"/> (plan 28), all from the layout file. The switch bytes and tuned station pointers are
/// offsets in the nd; the navigation database is <c>[nd + NdToSuper] + SuperToNavdb</c> and holds the navaid,
/// waypoint and NDB lists (vectors of record pointers at <see cref="Navaids"/>, <see cref="Waypoints"/>,
/// <see cref="Ndbs"/>); the FMS at <see cref="NdToFms"/> holds the airport list (a vector of
/// <see cref="AirportStride"/>-byte records at <see cref="Airports"/>) and the two VOR panels the course lines come
/// from. The record and panel fields are offsets in their object. <see cref="DrawnKinds"/> to <see cref="RoundLimit"/>
/// are the draw's constants (the margin and the count limit are the layout's <c>MarkerMargin</c> and
/// <c>ExcessLimit</c>): <see cref="DrawnKinds"/> the navaid kinds the draw's switch draws, <see cref="CourseLineKinds"/>
/// the station kinds a VOR course line is drawn through, and <see cref="KindVor"/> to <see cref="KindDme"/> the kind
/// values the app names in its rows (BlueBird prints no kind word: it draws a symbol per kind; the words are the app's
/// names for the symbols). <see cref="NavaidIdentLength"/> is how many UTF-32 code points of a navaid's ident its label
/// draws, <see cref="AirportLabelLength"/> how many characters of an airport's name. <see cref="MaxItems"/> caps each list's walk. Equality compares every field by content.
/// </summary>
public sealed record Bbs757EhsiMapOptions(
    int NavAidSwitch,
    int AirportSwitch,
    int WaypointSwitch,
    int TunedLeft,
    int TunedRight,
    int NdToSuper,
    int SuperToNavdb,
    int NdToFms,
    int Navaids,
    int NavaidIdent,
    int NavaidIdentLength,
    int NavaidFrequency,
    int NavaidKind,
    int NavaidClass,
    int NavaidLongitude,
    int NavaidLatitude,
    int Airports,
    int AirportStride,
    int AirportName,
    int AirportLabelLength,
    int AirportRecordLongitude,
    int AirportRecordLatitude,
    int Waypoints,
    int WaypointLongitude,
    int WaypointLatitude,
    int WaypointIdent,
    int Ndbs,
    int NdbIdent,
    int NdbLongitude,
    int NdbLatitude,
    int FmsToVorLeft,
    int FmsToVorRight,
    int PanelAutotune,
    int PanelStation,
    int PanelCourse,
    int PanelManualFraction,
    int PanelManualWhole,
    IReadOnlyList<int> DrawnKinds,
    IReadOnlyList<int> CourseLineKinds,
    int KindVor,
    int KindVorDme,
    int KindVortac,
    int KindTacan,
    int KindDme,
    int HighClass,
    double HighClassAbove,
    double NdbsAbove,
    double NdbDistance,
    int RoundLimit,
    int MaxItems)
{
    public bool Equals(Bbs757EhsiMapOptions? other) =>
        other is not null
        && NavAidSwitch == other.NavAidSwitch
        && AirportSwitch == other.AirportSwitch
        && WaypointSwitch == other.WaypointSwitch
        && TunedLeft == other.TunedLeft
        && TunedRight == other.TunedRight
        && NdToSuper == other.NdToSuper
        && SuperToNavdb == other.SuperToNavdb
        && NdToFms == other.NdToFms
        && Navaids == other.Navaids
        && NavaidIdent == other.NavaidIdent
        && NavaidIdentLength == other.NavaidIdentLength
        && NavaidFrequency == other.NavaidFrequency
        && NavaidKind == other.NavaidKind
        && NavaidClass == other.NavaidClass
        && NavaidLongitude == other.NavaidLongitude
        && NavaidLatitude == other.NavaidLatitude
        && Airports == other.Airports
        && AirportStride == other.AirportStride
        && AirportName == other.AirportName
        && AirportLabelLength == other.AirportLabelLength
        && AirportRecordLongitude == other.AirportRecordLongitude
        && AirportRecordLatitude == other.AirportRecordLatitude
        && Waypoints == other.Waypoints
        && WaypointLongitude == other.WaypointLongitude
        && WaypointLatitude == other.WaypointLatitude
        && WaypointIdent == other.WaypointIdent
        && Ndbs == other.Ndbs
        && NdbIdent == other.NdbIdent
        && NdbLongitude == other.NdbLongitude
        && NdbLatitude == other.NdbLatitude
        && FmsToVorLeft == other.FmsToVorLeft
        && FmsToVorRight == other.FmsToVorRight
        && PanelAutotune == other.PanelAutotune
        && PanelStation == other.PanelStation
        && PanelCourse == other.PanelCourse
        && PanelManualFraction == other.PanelManualFraction
        && PanelManualWhole == other.PanelManualWhole
        && DrawnKinds.SequenceEqual(other.DrawnKinds)
        && CourseLineKinds.SequenceEqual(other.CourseLineKinds)
        && KindVor == other.KindVor
        && KindVorDme == other.KindVorDme
        && KindVortac == other.KindVortac
        && KindTacan == other.KindTacan
        && KindDme == other.KindDme
        && HighClass == other.HighClass
        && HighClassAbove == other.HighClassAbove
        && NdbsAbove == other.NdbsAbove
        && NdbDistance == other.NdbDistance
        && RoundLimit == other.RoundLimit
        && MaxItems == other.MaxItems;

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(NavAidSwitch);
        h.Add(AirportSwitch);
        h.Add(WaypointSwitch);
        h.Add(TunedLeft);
        h.Add(TunedRight);
        h.Add(NdToSuper);
        h.Add(SuperToNavdb);
        h.Add(NdToFms);
        h.Add(Navaids);
        h.Add(NavaidIdent);
        h.Add(NavaidIdentLength);
        h.Add(NavaidFrequency);
        h.Add(NavaidKind);
        h.Add(NavaidClass);
        h.Add(NavaidLongitude);
        h.Add(NavaidLatitude);
        h.Add(Airports);
        h.Add(AirportStride);
        h.Add(AirportName);
        h.Add(AirportLabelLength);
        h.Add(AirportRecordLongitude);
        h.Add(AirportRecordLatitude);
        h.Add(Waypoints);
        h.Add(WaypointLongitude);
        h.Add(WaypointLatitude);
        h.Add(WaypointIdent);
        h.Add(Ndbs);
        h.Add(NdbIdent);
        h.Add(NdbLongitude);
        h.Add(NdbLatitude);
        h.Add(FmsToVorLeft);
        h.Add(FmsToVorRight);
        h.Add(PanelAutotune);
        h.Add(PanelStation);
        h.Add(PanelCourse);
        h.Add(PanelManualFraction);
        h.Add(PanelManualWhole);
        foreach (int k in DrawnKinds) h.Add(k);
        h.Add(-1);   // the two lists' boundary, so [0, 1] + [2] and [0] + [1, 2] hash apart
        foreach (int k in CourseLineKinds) h.Add(k);
        h.Add(KindVor);
        h.Add(KindVorDme);
        h.Add(KindVortac);
        h.Add(KindTacan);
        h.Add(KindDme);
        h.Add(HighClass);
        h.Add(HighClassAbove);
        h.Add(NdbsAbove);
        h.Add(NdbDistance);
        h.Add(RoundLimit);
        h.Add(MaxItems);
        return h.ToHashCode();
    }
}
