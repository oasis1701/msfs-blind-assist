namespace MSFSBlindAssist.Services;

// The fields of the captain's ND header: the part of RemoteData the ND feature alone reads, and the messages nested
// in it. Measured on 3.0.0.7613 (2026-11-03). Only the captain's side: the first officer's (63, 65, 67, 69, 104 and
// 164, beside 62, 64, 66, 68, 103 and 163) are never read. The route and the positions (2, 56, 84, 192, 193) are
// declared in FenixChannelFields.Route.cs, the EFIS list and the heading in FenixChannelFields.cs.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int Traffic = 5;                   // traffic: nested TrafficData, the TCAS traffic the ND draws (sitting 2)
        public const int RouteOptionsCapt = 44;         // routeOptionsCapt: nested RouteOptionsData, the captain's map visibility flags (the first officer's is 45)
        public const int Crosstrack = 52;             // crosstrack: fixed64 (double), the cross-track error in NM, negative left
        public const int TerrainOnNdCapt = 53;          // terrainOnNDCapt: varint (bool), the captain's TERR ON ND; left out when off
        public const int NdStatusCapt = 58;             // ndStatusCapt: nested NdStatusData, what the captain's ND draws in HDG
        public const int VorBlockCapt = 70;             // vorBlockCapt: nested NdRadioBlock, ROSE VOR's top right block (the first officer's is 71)
        public const int LsBlockCapt = 72;              // lsBlockCapt: nested NdRadioBlock, ROSE LS's top right block (the first officer's is 73)
        public const int VorCourseCapt = 80;            // vorCourseCapt: nested NdVorCourse, the captain's VOR 1 course block (81, the first officer's or VOR 2's, is never read)
        public const int CornersCapt = 76;             // cornersCapt: nested NdCorners, the bottom left (NAV 1) and bottom right (NAV 2) texts (the first officer's is 77)
        public const int BearingPointersCapt = 78;      // bearingPointersCapt: nested NdBearingPointers, the VOR and ADF pointers (the first officer's is 79)
        public const int NdFlagLabelsCapt = 102;       // ndFlagLabels: nested NdFlagLabels, the captain's ND flag labels (the approach message at top center)
        public const int NdCenterMessageCapt = 62;    // ndCenterMessageCapt: string, the center message (channel text)
        public const int NdWindshearMessageCapt = 64;   // ndWindshearMessageCapt: string, the windshear message (channel text)
        public const int NdTcasMessageCapt = 66;        // ndTcasMessageCapt: string, the TCAS message (channel text)
        public const int NavInfoCapt = 68;              // navInfoCapt: nested NdInfoData, the next waypoint's three lines
        public const int NdMessagesCapt = 103;          // ndMessagesCapt: repeated nested NullWrapped string, the captain's ND messages
        public const int GroundWindCapt = 163;          // groundWindCapt: nested GroundWindData, the ground speed and wind texts
        public const int WeatherRadarData = 191;        // weatherRadarData: nested WeatherRadarState, the weather radar and PWS
    }

    /// <summary>
    /// RouteOptionsData (<see cref="Remote.RouteOptionsCapt"/>), measured in flight on 3.0.0.7613 (sitting 3): sent empty
    /// until 44.1 came with the approach; 44.2 was never sent.
    /// </summary>
    public static partial class RouteOptionsData
    {
        public const int GoAroundVisible = 1;       // GoAroundVisible: varint (bool), the ND draws the missed approach (its elements are sent whether it does or not)
        public const int AlternateVisible = 2;      // AlternateVisible: varint (bool), the alternate; never sent, the alternate was never drawn in flight
    }

    /// <summary>AdirsData, added to the PFD's partial: the pressure altitude the traffic labels are taken from.</summary>
    public static partial class AdirsData
    {
        public const int PressureAltitude = 15;     // PressureAltitude: fixed64 (double), the captain's ADR pressure altitude
    }

    /// <summary>
    /// TrafficData (<see cref="Remote.Traffic"/>), measured in flight on 3.0.0.7613. Member 6 (TA only) is not read:
    /// 103 carries "TA ONLY". Only the targets the Fenix admits are sent; no member carries the selector.
    /// </summary>
    public static partial class TrafficData
    {
        public const int OtherTraffic = 2;          // OtherTraffic: repeated nested TrafficTarget, a hollow white diamond
        public const int ProximateTraffic = 3;      // ProximateTraffic: repeated nested TrafficTarget, within 6 NM and 1200 ft: a filled white diamond
        public const int TrafficAdvisory = 4;       // TrafficAdvisory: repeated nested TrafficTarget, the TA list: an amber filled circle (sitting 3, 311 pushes on the ground); 5.1 is sent with it and not read
        public const int Standby = 7;              // Standby: varint (bool), the TCAS in standby; left out otherwise
    }

    /// <summary>
    /// TrafficTarget, one target of <see cref="TrafficData"/>. Members 5, 6, 7, 10, 12 and 15 (direction, speeds,
    /// callsign, wake category, id) are never drawn and not read.
    /// </summary>
    public static partial class TrafficTarget
    {
        public const int Latitude = 1;              // Latitude: fixed32 (float), the symbol's place
        public const int Longitude = 2;             // Longitude: fixed32 (float)
        public const int Altitude = 3;              // Altitude: fixed32 (float), feet; the label is taken against AdirsData.PressureAltitude
        public const int VerticalSpeed = 4;         // VerticalSpeed: fixed32 (float), feet per minute: the arrow
        public const int DistanceNm = 8;            // DistanceNm: fixed32 (float), the great circle from the aircraft
    }

    /// <summary>
    /// NdStatusData, what the captain's ND draws (<see cref="Remote.NdStatusCapt"/>), measured in flight on 3.0.0.7613.
    /// Members 4 to 6 are not read (4, TaOnly, does not follow 103's "TA ONLY").
    /// </summary>
    public static partial class NdStatusData
    {
        public const int ShowTrackLine = 1;         // ShowTrackLine: varint (bool), the green track line (in HDG)
        public const int ShowSelectedHeading = 2;   // ShowSelectedHeading: varint (bool), the heading bug at 113.2
        public const int ShowTrackSolid = 3;        // ShowTrackSolid: varint (bool), the route solid (NAV); left out, dashed (HDG)
    }

    /// <summary>
    /// WeatherRadarState (<see cref="Remote.WeatherRadarData"/>): the members the ND's bottom right label follows. The
    /// others (mode, gain, tilt, the sweep's bearing) are not read.
    /// </summary>
    public static partial class WeatherRadarState
    {
        public const int SysOn = 4;                 // SysOn: varint (bool), the radar on: "WX+T+H"
        public const int PredWindshearScan = 8;     // PredWindshearScan: varint (bool), "PWS SCAN"
        public const int PredWindshearEnabled = 9;  // PredWindshearEnabled: varint (bool), alone: nothing printed
    }

    /// <summary>
    /// NdFlagLabels, the captain's ND flag labels (<see cref="Remote.NdFlagLabelsCapt"/>), measured in flight on
    /// 3.0.0.7613. Member 1 is the corner flags' nested message, whose members are not read here.
    /// </summary>
    public static partial class NdFlagLabels
    {
        public const int CornerFlags = 1;           // nested NdCornerFlags, the flags drawn at the corners (the radio layer reads its members)
        public const int ApproachMessage = 2;       // ApproachMessage: string (channel text), "gILS18L" at top center
    }

    /// <summary>NdRadioBlock, the top right block of ROSE LS (<see cref="Remote.LsBlockCapt"/>) and ROSE VOR (<see cref="Remote.VorBlockCapt"/>).</summary>
    public static partial class NdRadioBlock
    {
        public const int Line1 = 1;                 // Line1: string (channel text), the receiver and its frequency ("wILS2 m110.s15 ")
        public const int Line2 = 2;                 // Line2: string (channel text), the course ("CRS m186c°")
        public const int Line3 = 3;                 // Line3: string (channel text), the frequency, then the ident (" mICIZ "); empty or left out between
    }

    /// <summary>NdCornerFlags (<see cref="NdFlagLabels.CornerFlags"/>): a flag drawn in place of its corner, spoken as sent.</summary>
    public static partial class NdCornerFlags
    {
        public const int Vor1 = 1;                  // Vor1: string (channel text), the bottom left flag ("rVOR1")
        public const int Vor2 = 2;                  // Vor2: string (channel text), the bottom right flag; placed by its side, not measured alone
        public const int Adf1 = 3;                  // Adf1: string (channel text), the bottom left flag; placed by its side, not measured
        public const int Adf2 = 4;                  // Adf2: string (channel text), the bottom right flag; placed by its side, not measured alone
    }

    /// <summary>NdCorners (<see cref="Remote.CornersCapt"/>): the two corner texts of the captain's ND.</summary>
    public static partial class NdCorners
    {
        public const int Nav1 = 1;                  // Nav1: nested NdCornerText, the NAV 1 side, drawn bottom left
        public const int Nav2 = 2;                  // Nav2: nested NdCornerText, the NAV 2 side, drawn bottom right
    }

    /// <summary>NdCornerText, one corner of <see cref="NdCorners"/>: three lines, an empty entry with its selector at OFF.</summary>
    public static partial class NdCornerText
    {
        public const int Line1 = 1;                 // Line1: string (channel text), the receiver and its number ("wVOR1")
        public const int Line2 = 2;                 // Line2: string (channel text), the ident ("wCVG"); none for an ADF's number
        public const int Line3 = 3;                 // Line3: string (channel text), the DME distance ("g8s.4  scNM")
    }

    /// <summary>
    /// NdVorCourse (<see cref="Remote.VorCourseCapt"/>), measured at the gate (sitting 3): members 1 (the course ROSE
    /// VOR's pointer is drawn at, set with nothing received), 2 (the deviation) and 5 (TO/FROM) are read; 2 and 5 were
    /// measured in sitting 3 with VOR 1 receiving.
    /// </summary>
    public static partial class NdVorCourse
    {
        public const int Course = 1;                // Course: fixed64 (double), the VOR 1 course on RAD NAV (185 read 185.0); NaN with none
        public const int Deviation = 2;             // Deviation: fixed64 (double), the VOR deviation in dots, negative = bar right of the course pointer, stops at 2.5; NaN with none
        public const int Bearing = 3;               // Bearing: fixed64 (double), the VOR 1 bearing; not read here
        public const int DmeDistance = 4;           // DmeDistance: fixed64 (double), the VOR 1 DME distance in NM; not read here
        public const int ToFrom = 5;                // ToFrom: fixed64 (double), 1 FROM (arrow toward the pointer's tail), 2 TO (toward its head); absent with none
    }

    /// <summary>NdBearingPointers (<see cref="Remote.BearingPointersCapt"/>): the pointers of NAV 1 and NAV 2.</summary>
    public static partial class NdBearingPointers
    {
        public const int Nav1 = 1;                  // Nav1: fixed64 (double), the magnetic bearing; NaN for no pointer
        public const int Nav2 = 2;                  // Nav2: fixed64 (double), the magnetic bearing; NaN for no pointer
    }

    /// <summary>
    /// The values of <see cref="Efis.EfisSelectors"/>, one entry per NAV side (NAV 1 then NAV 2): what the side's
    /// selector is set to (evidence03-nd.md, sitting 1 Part A).
    /// </summary>
    public static class EfisSelectorValue
    {
        public const int Off = 0;
        public const int Vor = 1;
        public const int Adf = 2;
    }

    /// <summary>LsInputData, added to the PFD's partial: the course the ND's pointer is drawn at (82.6).</summary>
    public static partial class LsInputData
    {
        public const int Course = 6;                // Course: fixed64 (double), the localizer course; NaN with no ILS tuned (82.7 equals it)
    }

    /// <summary>NdInfoData, the next waypoint's lines (<see cref="Remote.NavInfoCapt"/>).</summary>
    public static partial class NdInfoData
    {
        public const int Line1 = 1;                 // Line1: string (channel text), the ident and bearing; "PPOS" with no flight plan
        public const int Line2 = 2;                 // Line2: string (channel text), the distance
        public const int Line3 = 3;                 // Line3: string (channel text), the time; left out at the gate
    }

    /// <summary>GroundWindData, the ground speed and wind texts (<see cref="Remote.GroundWindCapt"/>).</summary>
    public static partial class GroundWindData
    {
        public const int GroundSpeed = 1;           // GroundSpeed: string (channel text), ground speed and TAS
        public const int Wind = 2;                  // Wind: string (channel text), wind direction and speed
    }

    /// <summary>FcuData, added to the PFD's partial: the heading bug.</summary>
    public static partial class FcuData
    {
        public const int SelectedHeading = 2;       // SelectedHeading: fixed64 (double), the heading bug
    }

    /// <summary>
    /// The values of <see cref="Efis.MapOption"/>, a varint on the wire (measured on 3.0.0.7613); the field left out
    /// means no option.
    /// </summary>
    public static class EfisMapOptionValue
    {
        public const int Cstr = 0;                  // CSTR
        public const int Wpt = 1;                   // WPT
        public const int VorD = 2;                  // VOR.D
        public const int Ndb = 3;                   // NDB
        public const int Arpt = 4;                  // ARPT
    }

    /// <summary>
    /// The values of <see cref="Efis.Mode"/> (measured on 3.0.0.7613). The field left out means ROSE NAV, the value
    /// protobuf-net leaves out as its default; LS is sent explicitly as 0.
    /// </summary>
    public static class EfisModeValue
    {
        public const int Ls = 0;                    // LS
        public const int Vor = 1;                   // VOR
        public const int Nav = 2;                   // NAV
        public const int Arc = 3;                   // ARC
        public const int Plan = 4;                  // PLAN
    }

    /// <summary>MapData, the reply of GetMapData (measured on 3.0.0.7613). Its repeated parts are <see cref="MapPoint"/>.</summary>
    public static partial class MapData
    {
        public const int Airports = 1;              // Airports: repeated nested MapPoint, kind 1
        public const int Navaids = 2;               // Navaids: repeated nested MapPoint, kinds 2, 3 and 4
        public const int Fixes = 3;                 // Fixes: repeated nested MapPoint, kind 5
        public const int Ndbs = 4;                  // Ndbs: repeated nested MapPoint, kind 7
        public const int CenterLatitude = 5;        // CenterLatitude: fixed64 (double)
        public const int CenterLongitude = 6;       // CenterLongitude: fixed64 (double)
        public const int TrueTrack = 7;             // TrueTrack: fixed64 (double); left out in PLAN
        public const int Range = 8;                 // Range: varint, the ND's reach in nautical miles; left out reads 0
        public const int Mode = 9;                  // Mode: varint (2 ROSE NAV, 3 ARC, 4 PLAN), sent explicitly
        public const int Version = 10;              // Version: varint
        public const int Partial = 11;              // Partial: varint 1 when only a patch near the aircraft is sent; left out otherwise
        public const int MapOption = 12;            // MapOption: varint (an EfisMapOptionValue); left out for none
    }

    /// <summary>
    /// MapPoint, one point of <see cref="MapData"/>, and the FIX INFO fix (<see cref="FixRadial.Fix"/>, the same contract
    /// type). Member 4 is the tuned flag: 1 on the one navaid tuned, 3 on every other navaid and NDB, left out on
    /// airports and fixes (evidence03-nd.md, sitting 2, GetMapData in flight).
    /// </summary>
    public static partial class MapPoint
    {
        public const int Kind = 1;                  // Kind: varint (1 airport, 2, 3 or 4 a navaid, 5 a fix, 7 an NDB)
        public const int Latitude = 2;              // Latitude: fixed64 (double)
        public const int Longitude = 3;             // Longitude: fixed64 (double)
        public const int Tuned = 4;                 // Tuned: varint, 1 for the tuned navaid, 3 for every other navaid and NDB; left out on airports and fixes
        public const int Ident = 5;                 // Ident: string
    }

    /// <summary>The reply of GetMapDataVersion.</summary>
    public static partial class MapDataVersionReply
    {
        public const int Version = 1;               // Version: varint
    }
}
