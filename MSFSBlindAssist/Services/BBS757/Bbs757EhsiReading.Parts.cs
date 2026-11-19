namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// Plan 28's five parts of the reading, each the reading of one optional layout part. A part is null when its layout part
/// is missing, when this reading does not hold it, or (for a full-only part) in a light reading; its <c>…Missing</c>
/// reason then says why where it is not simply "not in this reading": the layout part's own reason. A part with neither a
/// reading nor a reason lists nothing (a dark or
/// corrupt reading). The reasons travel in the reading so the rows need no layout, as <see cref="Words"/> and
/// <see cref="Constants"/> do.
/// </summary>
public sealed partial record Bbs757EhsiReading
{
    /// <summary>The VOR and ILS picture (light: VOR and ILS only; its values the defaults in MAP) and the bearing pointers
    /// (every mode but PLAN while the IRS is valid); null in PLAN. A reason of "unexpected values": the part has torn the
    /// reader's limit of readings in a row (the same for every part).</summary>
    public Bbs757EhsiApproachReading? Approach { get; init; }
    public string? ApproachMissing { get; init; }

    /// <summary>The TCAS messages (light) and the traffic (full, MAP while the IRS is valid; null otherwise).</summary>
    public Bbs757EhsiTcasReading? Tcas { get; init; }
    public string? TcasMissing { get; init; }

    /// <summary>The NAV AID, ARPT and WPT items, the tuned stations and the course lines (full, while the map is drawn: MAP
    /// with the IRS valid, or PLAN).</summary>
    public Bbs757EhsiMapOptionsReading? MapOptions { get; init; }
    public string? MapOptionsMissing { get; init; }

    /// <summary>TERR and the wind (light: every reading), the runways and the deceleration points (full, while the map is
    /// drawn).</summary>
    public Bbs757EhsiMapExtrasReading? MapExtras { get; init; }
    public string? MapExtrasMissing { get; init; }

    /// <summary>The FIX pages, the off-path descent circles (full, while the map is drawn), the altitude range arc and the
    /// VNAV path deviation (full: MAP).</summary>
    public Bbs757EhsiMapGuidesReading? MapGuides { get; init; }
    public string? MapGuidesMissing { get; init; }
}

// ---- VOR and ILS ----

/// <summary>The VOR's TO/FROM as the nd holds it: none (0, or any value but the layout's two).</summary>
public enum Bbs757EhsiToFrom { None, To, From }

/// <summary>BlueBird's VOR and ILS words as the screen prints them (the layout's): the bottom-left labels, TO and FROM,
/// DME, the dashes for no ILS frequency and for no DME, and the LOC and G/S flags.</summary>
public readonly record struct Bbs757EhsiApproachWords(
    string VorLeft,
    string VorRight,
    string Ils,
    string To,
    string From,
    string Dme,
    string NoFrequency,
    string NoDme,
    string Loc,
    string Glideslope);

/// <summary>One bearing pointer: what it points at (<see cref="Source"/>: the VOR's own word "VOR L", or "ADF L"), the
/// station's ident (null for an ADF) and its position (null when the layout gives no ADF position).</summary>
public readonly record struct Bbs757EhsiBearingPointer(string Source, string? Ident, double? Latitude, double? Longitude);

/// <summary>
/// The VOR and ILS picture as the nd holds it, every value as stored (the rows decide what a value that is not a number
/// draws): the VOR course, its deviation bar (<see cref="VorBar"/> drawn) and deviation in units of full scale, TO/FROM, the
/// VOR DME; the ILS course, its localizer bar and deviation, its dots, the dots' amber test, its frequency (null where the
/// screen prints dashes: no frequency set, or the selected receiver has no localizer), the LOC and G/S flags; the display's
/// side (the Captain's labels its VOR "VOR L"); the PFD's glideslope (scale, pointer, deviation in dots, positive up, the
/// test); the ILS DME (valid as BlueBird's draw tests it: not failed, powered, an ILS station, a finite distance not
/// negative); the two bearing pointers (null: none drawn). <see cref="DeviationFullScale"/>, <see cref="DotsPerUnit"/> and
/// <see cref="GlideslopePin"/> are the draw's constants (the layout's).
/// </summary>
public sealed record Bbs757EhsiApproachReading(
    double VorCourse,
    bool VorBar,
    double VorDeviation,
    Bbs757EhsiToFrom ToFrom,
    bool VorDmeValid,
    double VorDme,
    double IlsCourse,
    bool LocalizerBar,
    double LocalizerDeviation,
    bool IlsDots,
    bool IlsTest,
    double? IlsFrequency,
    bool LocFlag,
    bool GlideslopeFlag,
    bool CaptainSide,
    bool GlideslopeScale,
    bool GlideslopePointer,
    double GlideslopeDeviation,
    bool GlideslopeTest,
    bool IlsDmeValid,
    double IlsDme,
    Bbs757EhsiBearingPointer? Pointer1,
    Bbs757EhsiBearingPointer? Pointer2,
    Bbs757EhsiApproachWords Words,
    double DeviationFullScale,
    double DotsPerUnit,
    double GlideslopePin);

// ---- TCAS ----

/// <summary>The TCAS level the screen draws by: off (anything but BlueBird's two), TA ONLY, TA/RA.</summary>
public enum Bbs757EhsiTcasLevel { Off, TaOnly, TaRa }

/// <summary>A traffic symbol's kind: other (hollow diamond), proximate (filled diamond), a traffic advisory (amber circle),
/// a resolution advisory (red square).</summary>
public enum Bbs757EhsiTrafficKind { Other, Proximate, Advisory, Resolution }

/// <summary>A traffic symbol's arrow: none, climbing (up), descending (down).</summary>
public enum Bbs757EhsiTrafficTrend { None, Climbing, Descending }

/// <summary>One entry of the TCAS traffic list: its kind, trend, altitude relative to the aircraft in feet and position.</summary>
public readonly record struct Bbs757EhsiTraffic(
    Bbs757EhsiTrafficKind Kind,
    Bbs757EhsiTrafficTrend Trend,
    double RelativeFeet,
    double Latitude,
    double Longitude);

/// <summary>BlueBird's TCAS words as the screen prints them (the layout's; TEST without its leading spaces).</summary>
public readonly record struct Bbs757EhsiTcasWords(
    string Tcas,
    string Fail,
    string Off,
    string Tfc,
    string TaOnly,
    string Test,
    string Traffic);

/// <summary>
/// The TCAS computer as the nd draws it. <see cref="Computer"/> false: the nd has none and draws no TCAS at all. Otherwise
/// its power and failure, level, the TA and RA present, the self-test, the nd's TFC switch and its message gate (the
/// messages the EFIS mode allows). <see cref="Traffic"/> is the list in BlueBird's order, read in a full reading only (null
/// in a light one). <see cref="TagDivisor"/> is the tag's divisor (the layout's).
/// </summary>
public sealed record Bbs757EhsiTcasReading(
    bool Computer,
    bool Powered,
    bool Failed,
    Bbs757EhsiTcasLevel Level,
    bool TrafficAdvisory,
    bool ResolutionAdvisory,
    bool Test,
    bool TrafficSwitch,
    bool MessagesAllowed,
    IReadOnlyList<Bbs757EhsiTraffic>? Traffic,
    Bbs757EhsiTcasWords Words,
    double TagDivisor);
