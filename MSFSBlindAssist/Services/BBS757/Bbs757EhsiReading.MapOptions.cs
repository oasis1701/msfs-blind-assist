namespace MSFSBlindAssist.Services.BBS757;

/// <summary>Which of the map options' lists an item comes from: a VHF navaid (NAV AID, and the tuned stations), an airport
/// (ARPT), an enroute waypoint or an enroute NDB (WPT, by the range).</summary>
public enum Bbs757EhsiMapItemKind { Navaid, Airport, Waypoint, Ndb }

/// <summary>One map option item as the draw takes it: its list, its name as the label draws it (a navaid's ident, an
/// airport's first characters, a waypoint's or NDB's ident), the navaid's kind byte as stored (-1 for the other lists) and
/// its position.</summary>
public readonly record struct Bbs757EhsiMapOption(
    Bbs757EhsiMapItemKind Kind,
    string Name,
    int NavaidKind,
    double Latitude,
    double Longitude);

/// <summary>The navaid kind values the app names in its rows (the layout's): BlueBird draws a symbol per kind and prints no
/// kind word, so "VOR", "VOR/DME", "VORTAC", "TACAN" and "DME" are the app's names for the symbols.</summary>
public readonly record struct Bbs757EhsiNavaidKinds(int Vor, int VorDme, int Vortac, int Tacan, int Dme);

/// <summary>One VOR course line: the receiver (left or right), the course as the panel holds it, the station's ident and
/// position (the line runs through it).</summary>
public readonly record struct Bbs757EhsiCourseLine(bool Left, double Course, string Station, double Latitude, double Longitude);

/// <summary>
/// The map options as the nd draws them, in a full reading in MAP and PLAN: the three EFIS switches; the tuned stations
/// drawn (green, never counted, with every switch off); the items BlueBird's loop draws and counts, in draw order
/// (<see cref="Bbs757EhsiMapOptionsLoop"/>), and the loop's count; the VOR course lines drawn (with every switch off).
/// <see cref="Kinds"/> are the layout's kind values for the rows' words.
/// </summary>
public sealed record Bbs757EhsiMapOptionsReading(
    bool NavAid,
    bool Airport,
    bool Waypoint,
    IReadOnlyList<Bbs757EhsiMapOption> Tuned,
    IReadOnlyList<Bbs757EhsiMapOption> Drawn,
    int Count,
    IReadOnlyList<Bbs757EhsiCourseLine> CourseLines,
    Bbs757EhsiNavaidKinds Kinds);
