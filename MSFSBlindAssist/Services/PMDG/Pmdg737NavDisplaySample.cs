namespace MSFSBlindAssist.Services.PMDG;

/// <summary>A position in radians, north and east positive (the sampler turns PMDG's west-positive longitude).</summary>
public readonly record struct GeoPoint(double LatitudeRad, double LongitudeRad);

/// <summary>A PMDG clock's time (PMDG prints it "HHMM.m" with the minute's tenths from the seconds).</summary>
public readonly record struct NdClock(int Hours, int Minutes, int Seconds);

/// <summary>The Captain's EFIS control panel: the mode and range selector positions, the VOR/ADF switches,
/// the map buttons and the panel's power.</summary>
public sealed record NdPanelState(int Mode, int Range, int VorAdfLeft, int VorAdfRight, bool Centered, bool Tfc, bool Wxr, bool Sta,
    bool Wpt, bool Arpt, bool Data, bool Pos, bool Terr, bool Vsd, bool Powered);

/// <summary>A waypoint's altitude constraint as the node holds it (its type character, the at, upper and lower
/// altitudes, the flight phase, the third line's number); the DATA text is formatted from it.</summary>
public readonly record struct NdConstraint(char Type, int At, int Upper, int Lower, int Phase, int ThirdLine);

/// <summary>
/// A route node the ND shows: its name (the TO corner's) and the name the map prints, its leg type, its
/// distance (the TO node's from the aircraft, a later node's the leg's), its ETA when the clock is valid,
/// its position while valid (always for the map's nodes) and magnetic variation, its constraint, whether
/// PMDG hides its ETA, whether it is the TO node, whether it is the plan's key node PMDG prints the
/// constraint of without DATA (NdDataTextFields.ActiveSlot). A hold node (<see cref="IsHold"/>: drawn as a hold, not a
/// waypoint) carries its <see cref="Hold"/> (null without the layout's hold).
/// </summary>
public sealed record NdWaypoint(string Name, string MapName, int LegType, double Distance, NdClock? Eta, GeoPoint? Position,
    double MagneticVariationDeg, NdConstraint Constraint, bool EtaHidden, bool IsTo, bool IsActiveWaypoint = false, bool IsHold = false,
    NdHold? Hold = null);

/// <summary>A hold node's hold as the node holds it: its turn byte, its inbound course (true, degrees), the HOLD page's
/// leg time (minutes) and leg distance (NM), each PMDG's invalid value kept (the text leaves those out).</summary>
public readonly record struct NdHold(byte Turn, double InboundTrueDeg, double LegMinutes, double LegNm);

/// <summary>A VNAV profile point drawn on the route (T/C, T/D, DECEL …).</summary>
public sealed record NdMarker(string Label, GeoPoint Position);

/// <summary>A route airport PMDG draws: its name, its runway's (null with none), where the symbol
/// sits (the runway's position when one is set, else the airport's; null while not valid: PMDG tests none
/// there), whether it is the departure (else the arrival); with a runway its heading (a magnetic course, degrees)
/// and length (feet), 0 without.</summary>
public sealed record NdRouteAirport(string Name, string? Runway, GeoPoint? Position, bool Departure, int RunwayHeadingDeg = 0, int RunwayLengthFt = 0);

/// <summary>A straight piece of the route line, between two positions.</summary>
public readonly record struct NdPathLine(GeoPoint From, GeoPoint To);

/// <summary>The waypoint a piece of the route line heads for: the first waypoint of the map's route at or after
/// the point's node in the route's chain (<see cref="RouteIndex"/>, its index in the route), else the node itself
/// (no index; its position null while not valid).</summary>
public sealed record NdPathToward(int? RouteIndex, string Name, GeoPoint? Position);

/// <summary>One point of a plan's route line as PMDG draws it: whether its turn arc is drawn (the
/// arc's own geometry is not read), its two straight lines while drawn (null: not drawn), and the waypoint
/// they head for (null: the point holds no node).</summary>
public sealed record NdPathPoint(bool Turn, NdPathLine? FirstLine, NdPathLine? SecondLine, NdPathToward? Toward);

public enum NdObjectKind { Navaid, Fix, Airport }

/// <summary>A nearby navaid, fix or airport from PMDG's lists, in list order, with its station flags and
/// class (navaids; 0 for the others).</summary>
public sealed record NdMapObject(NdObjectKind Kind, string Name, GeoPoint Position, int Flags, int Class);

/// <summary>A VOR receiver as PMDG's tuned stations read it: its ident, the station's position (null while not
/// valid), the receiver's state, the DME distance (negative: none) and the selected course.</summary>
public sealed record NdTunedStation(string Ident, GeoPoint? Position, int State, double DmeNm, double CourseDeg);

/// <summary>A CDU FIX page entry: the fix's name, AIRAC type and position, its distance circles and radials
/// (PMDG's invalid value kept: the text leaves those out), its abeam point on the route (null while not valid: the
/// FIX page's ABEAM key not pressed). Equality compares the lists by content.</summary>
public sealed record NdFixRing(string Name, int Type, GeoPoint Position, IReadOnlyList<double> RingsNm, IReadOnlyList<double> RadialsDeg, GeoPoint? Abeam = null)
{
    public bool Equals(NdFixRing? other) =>
        other is not null && Name == other.Name && Type == other.Type && Position == other.Position
        && RingsNm.SequenceEqual(other.RingsNm) && RadialsDeg.SequenceEqual(other.RadialsDeg) && Abeam == other.Abeam;

    public override int GetHashCode() => HashCode.Combine(Name, Type, Position, RingsNm.Count, RadialsDeg.Count);
}

/// <summary>
/// One bottom corner's receiver as PMDG reads it: the side's VOR/ADF switch, the VOR's
/// ident and frequency text, its state, the systems object's power byte, the VOR, DME and ADF fail flags, the DME
/// distance, the ADF's ident, frequency text and valid count.
/// </summary>
public sealed record NdReceiver(int Switch, string VorIdent, string VorFrequency, int VorState, bool Powered, bool VorFailed,
    bool DmeFailed, double DmeNm, string AdfIdent, string AdfFrequency, int AdfValid, bool AdfFailed);

/// <summary>What PMDG's traffic gates read: the list's flag byte, the TCAS object's Powered, the self-test and its
/// pattern flag, the list's test value, TA ONLY, and PMDG's traffic filter options.</summary>
public sealed record NdTrafficState(bool ListShown, bool Powered, bool SelfTest, bool SelfTestShown, int TestValue, bool TaOnly,
    bool ShowAllTraffic, int FilterRangeNm, int FilterSeparationFt);

/// <summary>A present TCAS target: relative altitude (feet), range (NM), bearing (radians from the nose),
/// threat (0 other, 1 proximate, 2 TA, 3 RA), trend (signed) and its index in the list, counting the targets
/// not present (the count PMDG picks a target's place in the TCAS test by).</summary>
public sealed record NdTraffic(double RelativeAltitudeFt, double RangeNm, double BearingRad, int Threat, int Trend, int Index);

/// <summary>
/// One reading of everything the Captain's ND draws, raw: the panel and display units; which way the map
/// points (heading, track, the magnetic reference, the IRU's condition, PMDG's track-up option, the
/// readout's flags); the FMC the Captain's ND shows (<see cref="Fmc"/>: the EFIS object's entry for the DEU
/// of the unit the window describes) and whether that FMC has a valid FMS position, the
/// map valid only with both that and the readout (<see cref="MapValid"/>); the aircraft;
/// the TO corner's waypoint (none while the map is invalid), the reference route's plan (<see cref="RefRoute"/>; its MOD is that + the
/// layout's ModOffset) and the map's route, with <see cref="ModifiedRoute"/> the map's walk of the
/// reference route's MOD while a modification is pending (null otherwise: PMDG draws both, live at KATL
/// 2026-10-25); the route's airports, the MOD's beside it; each route's line, its lateral path's points in
/// order (empty while PMDG draws none); the DATA text's transition altitude and level and PMDG's
/// ND_ActWptData option (<see cref="ActiveWaypointData"/>: the active waypoint's constraint without DATA); the
/// PLN centre; the VNAV
/// deviation with PMDG's blanking inputs; the markers; the nearby lists with their full counts; the
/// traffic; the tuned stations; the CDU FIX page; the heading bug's and the altitude arc's inputs; the
/// bottom corners; the top-left values; each message condition's source value; RNP and ANP; in APP or VOR,
/// the APP/VOR part's reading (<see cref="Approach"/>, null in the other modes or without the part); outside
/// PLN, the bearing pointers per receiver (<see cref="Pointers"/>, null in PLN or without the part); what the
/// layout's details read (<see cref="Details"/>). Formatting, gating and geometry are the text's and the geometry's.
/// </summary>
public sealed record NavDisplaySample(
    NdPanelState Panel,
    IReadOnlyList<int> UnitPages,
    IReadOnlyList<int> UnitIndices,
    int LowerSelector,
    double HeadingDeg,
    double TrackDeg,
    bool MagneticReference,
    double MagneticVariationDeg,
    int IruCondition,
    bool TrackUpOption,
    bool ReadoutValid,
    bool ReadoutFailed,
    int Fmc,
    bool FmsPositionValid,
    GeoPoint? Aircraft,
    NdWaypoint? Active,
    int RefRoute,
    IReadOnlyList<NdWaypoint> Route,
    IReadOnlyList<NdWaypoint>? ModifiedRoute,
    IReadOnlyList<NdRouteAirport> RouteAirports,
    IReadOnlyList<NdRouteAirport>? ModifiedRouteAirports,
    IReadOnlyList<NdPathPoint> RoutePath,
    IReadOnlyList<NdPathPoint>? ModifiedRoutePath,
    int TransitionAltitude,
    int TransitionLevel,
    bool ActiveWaypointData,
    GeoPoint? PlnCentre,
    string? PlnCentreName,
    double VnavDeviationFt,
    int VnavPhase,
    bool PerfPath,
    IReadOnlyList<NdMarker> Markers,
    IReadOnlyList<NdMapObject> Objects,
    int NavaidCount,
    int FixCount,
    int AirportCount,
    NdTrafficState TrafficState,
    IReadOnlyList<NdTraffic> Traffic,
    IReadOnlyList<NdTunedStation> Tuned,
    bool CourseLineOption,
    IReadOnlyList<NdFixRing> FixPage,
    int SelectedHeadingDeg,
    int McpAltitudeFt,
    double AltitudeFt,
    double VerticalSpeedFpm,
    IReadOnlyList<NdReceiver> Receivers,
    double GroundSpeed,
    double TrueAirspeed,
    double WindDirection,
    double WindSpeed,
    IReadOnlyDictionary<NdSource, long> ConditionValues,
    bool RnpShown,
    double Rnp,
    double Anp,
    double? Xtk,
    NdApproachSample? Approach = null,
    IReadOnlyList<NdPointerReading>? Pointers = null,
    NdPosReading? Pos = null,
    NdDetailReading? Details = null)
{
    /// <summary>PMDG's map validity (its map-invalid flag clear): the readout valid and an FMS position.</summary>
    public bool MapValid => ReadoutValid && FmsPositionValid;
}

/// <summary>What the details read (null on the sample without the layout's details): whether the TCAS object's traffic
/// list exists and PMDG's TCAS ring option (false without the ring's layout); the heading line's option, the FMA
/// roll mode, and while both make the line timed, the sim's clock and the ND symbol generator's time of the last heading
/// change (0 otherwise); the radar's tilt, auto-tilt byte and variable-gain byte for the Captain's panel; the vertical RNP
/// and ANP of the CDU position data; the trend vector's turn rate (simulator interface, radians a second; 0 without its
/// layout); the track line's range-arcs option (false without its layout); the terrain numbers' option and the GPWS
/// test, and while the panel's TERR is on with the option and no test, the ND symbol generator's terrain record for the unit (the
/// highest and lowest elevation in feet and their colour codes; 0 otherwise).</summary>
public sealed record NdDetailReading(bool TrafficList, bool TcasRingOption, bool HeadingLineOption = false, int RollMode = 0, double SimTime = 0,
    double HeadingChangedAt = 0, int WxrTilt = 0, bool WxrAuto = false, bool WxrVariable = false, int VerticalRnp = 0, int VerticalAnp = 0,
    double TurnRate = 0, bool RangeArcsOption = false, bool TerrainOption = false, bool GpwsTest = false, int TerrainHighest = 0,
    int TerrainHighestColour = 0, int TerrainLowest = 0, int TerrainLowestColour = 0);

/// <summary>POS's readings in MAP with the POS button on (null otherwise or without the pointer part): per
/// receiver the VOR line's radial while the VOR is tuned and the ADF line's bearing from the nose while the ADF
/// flag is set (0 otherwise); the side's IRS position and the GPS position, null while not valid.</summary>
public sealed record NdPosReading(IReadOnlyList<double> VorRadials, IReadOnlyList<double> AdfBearings, GeoPoint? Irs, GeoPoint? Gps);

/// <summary>One receiver's bearing pointer, beside its <see cref="NdReceiver"/>: the VOR pointer's glass data
/// valid flag, the ADF's systems object power byte, and the bearing from the nose of the pointer PMDG draws (0
/// while it draws none). Null on the sample in PLN or without the layout's pointer part.</summary>
public sealed record NdPointerReading(bool VorValid, bool AdfPowered, double Bearing);

/// <summary>Reads one <see cref="NavDisplaySample"/> through a derived layout. <c>false</c> with a null
/// <c>corrupt</c>: the memory is not readable (lost); with <c>corrupt</c> set: a check failed, named there,
/// and nothing of the reading is used.</summary>
public interface INavDisplaySampler : IDisposable
{
    Pmdg737NavDisplayLayout Layout { get; }

    bool TryRead(out NavDisplaySample sample, out string? corrupt);
}
