namespace MSFSBlindAssist.Services;

// The fields of the ND's route and positions: the part of RemoteData the route decoder alone reads, and the messages
// nested in it. Measured on 3.0.0.7613 (2026-11-03). Only the captain's side: the first officer's route (3) and
// positions (57, 85) are never read. The classes that name a message's members are partial, so that a feature that reads
// another member of the same message adds it from a file of its own and does not declare the message again. The value
// tables (RouteLegKind, RouteEndReason, RouteSymbol, MapMarkerKind) are not: each holds every value of one member here.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int RouteDataCapt = 2;         // routeDataCapt: nested RouteData, the captain's route
        public const int NdPosCapt = 56;            // ndPosCapt: nested NdPositionData, the position the ND draws (drifts a few meters)
        public const int CduCenterCapt = 84;        // cduCenterCapt: nested LatLon, the PLAN centre (the aircraft in the other modes)
        public const int TruePos = 192;             // truePos: nested LatLon, the present position (fixed: the one the app uses)
        public const int TrueTrack = 193;           // trueTrack: fixed64 (double), the true track
    }

    /// <summary>RouteData, the captain's route (<see cref="Remote.RouteDataCapt"/>).</summary>
    public static partial class RouteData
    {
        public const int Runways = 1;               // Runways: repeated nested RouteRunway (a type whose contract name is scrambled), the departure's then the arrival's
        public const int ActRoute = 4;              // actRoute: repeated nested RouteLeg (scrambled name), the active route
        public const int ModRoute = 5;              // modRoute: repeated nested RouteLeg, the temporary flight plan; sent only while one is pending
        public const int AltRoute = 7;              // altRoute: repeated nested RouteLeg, the alternate
        public const int ModAltRoute = 8;           // modAltRoute: repeated nested RouteLeg, the temporary alternate
        public const int ActiveLeg = 11;            // activeLeg: nested RouteLeg, the TO waypoint's symbol element
        public const int MapMarkers = 12;           // mapMarkers: repeated nested MapMarker
        public const int FixRadials = 13;           // fixRadials: repeated nested FixRadial, the FIX INFO page (four entries, empty with none entered)
    }

    /// <summary>A runway of <see cref="RouteData.Runways"/>: the departure's first, the arrival's second.</summary>
    public static partial class RouteRunway
    {
        public const int Latitude = 1;              // Latitude: fixed64 (double), the threshold
        public const int Longitude = 2;             // Longitude: fixed64 (double), the threshold
        public const int TrueCourse = 3;            // TrueCourse: fixed32 (single)
        public const int Runway = 4;                // Runway: string, "08R"
        public const int LengthFt = 5;              // LengthFt: varint, the length in feet
        public const int Airport = 6;               // Airport: string, "KATL"
        public const int MagneticCourse = 9;        // MagneticCourse: fixed32 (single); not read
        public const int MagneticVariation = 10;    // MagneticVariation: fixed32 (single); not read
    }

    /// <summary>
    /// One element of a route list (<see cref="RouteData.ActRoute"/>, <see cref="RouteData.ModRoute"/>,
    /// <see cref="RouteData.AltRoute"/>, <see cref="RouteData.ModAltRoute"/>) or the active leg
    /// (<see cref="RouteData.ActiveLeg"/>). Member 17 (a varint) and any other number are not read.
    /// </summary>
    public static partial class RouteLeg
    {
        public const int Latitude = 1;              // Latitude: fixed64 (double)
        public const int Longitude = 2;             // Longitude: fixed64 (double)
        public const int Kind = 3;                  // Kind: varint (an enum of a scrambled type, see RouteLegKind); absent is 0
        public const int MissedApproach = 4;        // MissedApproach: varint (bool), the element is on the missed approach
        public const int RemoveOnDeclutter = 5;     // (a scrambled type's member): varint (bool); the ND removes the element's symbol, label and constraint at 160 and above (sitting 1, Step 14)
        public const int Ident = 6;                 // Ident: string
        public const int Symbol = 7;                // Symbol: varint (see RouteSymbol), on kind 4 only
        public const int TurnSide = 8;              // TurnSide: string, "L" or "R", on kind 1
        public const int TurnRadius = 9;            // TurnRadius: fixed32 (single); 1.4715 at a turn, 0.0031 at a fly-by point (nautical miles, inferred)
        public const int CourseIn = 10;             // CourseIn: fixed32 (single), true degrees
        public const int CourseOut = 11;            // CourseOut: fixed32 (single), true degrees
        public const int HoldKind = 12;             // HoldKind: varint; 4 seen
        public const int HoldRightTurns = 13;       // HoldRightTurns: varint (bool)
        public const int HoldCourse = 14;           // HoldCourse: fixed64 (double), the inbound course; 168.1 (true) on the entry arc, 174 (magnetic) after
        public const int EndReason = 16;            // EndReason: varint (see RouteEndReason), on kind 3
    }

    /// <summary>The kinds of <see cref="RouteLeg.Kind"/>, as measured on 3.0.0.7613.</summary>
    public static class RouteLegKind
    {
        public const int Line = 0;                  // a line straight to here; also when the member is absent
        public const int Arc = 1;                   // a turn to here, with 8 to 11
        public const int Start = 2;                 // the line starts here: the first element, and after each break
        public const int End = 3;                   // the line ends here, with 16
        public const int Symbol = 4;                // a waypoint symbol; all symbols come after the line elements
    }

    /// <summary>The reasons of <see cref="RouteLeg.EndReason"/>.</summary>
    public static class RouteEndReason
    {
        public const int Discontinuity = 1;         // measured: each F-PLN discontinuity (five seen)
        public const int FinalApproach = 2;         // seen once, with 17 = 68: the final approach starts as its own line at the same point; inferred from one sample
    }

    /// <summary>The symbols of <see cref="RouteLeg.Symbol"/>.</summary>
    public static class RouteSymbol
    {
        public const int Vor = 3;                   // a VOR (VHP)
        public const int Waypoint = 5;              // every other symbol, airports as waypoints included
    }

    /// <summary>A map marker of <see cref="RouteData.MapMarkers"/>.</summary>
    public static partial class MapMarker
    {
        public const int Lat = 1;                   // Lat: fixed64 (double)
        public const int Lon = 2;                   // Lon: fixed64 (double)
        public const int Label = 3;                 // Label: string, as the ND prints it; "\n" may separate two printed lines ("-12000\n+ 8000")
        public const int MapMarkerType = 4;         // MapMarkerType: varint (MapMarkerEnum, see MapMarkerKind)
        public const int Color = 5;                 // Color: varint (FMCColorEnum)
        public const int IsGoAround = 6;            // IsGoAround: varint (bool)
        public const int RemoveOnDeclutter = 7;     // RemoveOnDeclutter: varint (bool)
    }

    /// <summary>
    /// The values of <see cref="MapMarker.MapMarkerType"/> (MapMarkerEnum), the contract's order as the flight measured it
    /// (evidence, sitting 2, "The markers (2.12)"). SOC 3, TimeMarker 9 and Debug 10 were never sent.
    /// </summary>
    public static class MapMarkerKind
    {
        public const int Tod = 0;                   // TOD: an arrow level then down (white, or cyan with DES)
        public const int Toc = 1;                   // TOC: an arrow climbing then level (cyan)
        public const int Eod = 2;                   // EOD: an arrow descending then level (cyan or magenta)
        public const int Decel = 4;                 // Decel: a circle with a "D" (magenta)
        public const int AltCstr = 5;               // AltCstr: an altitude constraint, with any speed: the ring and the label at its waypoint
        public const int Cstr = 6;                  // Cstr: a speed constraint alone ("250KT"); never seen drawn
        public const int Cross = 7;                 // Cross: a zigzag ending in an arrow (cyan)
        public const int SpeedLimit = 8;            // SpeedLimit: a filled dot (magenta; a filled diamond in ROSE NAV)
        public const int Etp = 11;                  // ETP: a circle with "(ETP)" beside it (green)
    }

    /// <summary>A latitude and longitude (<see cref="Remote.TruePos"/>, <see cref="Remote.CduCenterCapt"/>).</summary>
    public static partial class LatLon
    {
        public const int Latitude = 1;              // Latitude: fixed64 (double)
        public const int Longitude = 2;             // Longitude: fixed64 (double)
    }

    /// <summary>NdPositionData, the position the ND draws (<see cref="Remote.NdPosCapt"/>).</summary>
    public static partial class NdPositionData
    {
        public const int Lat = 1;                   // Lat: fixed64 (double)
        public const int Lon = 2;                   // Lon: fixed64 (double)
    }

    /// <summary>
    /// A fix radial of <see cref="RouteData.FixRadials"/> (the MCDU FIX INFO page), as sent in flight on 3.0.0.7613.
    /// The fix is the GetMapData reply's point type: its members are <see cref="MapPoint"/>'s.
    /// </summary>
    public static partial class FixRadial
    {
        public const int Fix = 1;                   // fix: nested MapPoint (1 kind, 2 latitude, 3 longitude, 5 ident)
        public const int MagVar = 2;                // magVar: fixed64 (double), the variation at the fix
        public const int Radials = 3;               // radials: repeated fixed64 (double), not packed; magnetic degrees as entered
        public const int Radius = 4;                // radius: repeated fixed64 (double), not packed; the rings in nautical miles
    }
}
