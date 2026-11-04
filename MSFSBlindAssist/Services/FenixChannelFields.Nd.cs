namespace MSFSBlindAssist.Services;

// The fields of the captain's ND header: the part of RemoteData the ND feature alone reads, and the messages nested
// in it. Measured on 3.0.0.7613 (2026-11-03). Only the captain's side: the first officer's (63, 65, 67, 69, 104 and
// 164, beside 62, 64, 66, 68, 103 and 163) are never read. The route and the positions (2, 56, 84, 192, 193) are
// declared in FenixChannelFields.Route.cs, the EFIS list and the heading in FenixChannelFields.cs.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int Crosstrack = 52;               // crosstrack: fixed64 (double), the cross-track error in NM, negative left
        public const int TerrainOnNdCapt = 53;          // terrainOnNDCapt: varint (bool), the captain's TERR ON ND; left out when off
        public const int NdStatusCapt = 58;             // ndStatusCapt: nested NdStatusData, what the captain's ND draws in HDG
        public const int NdCenterMessageCapt = 62;      // ndCenterMessageCapt: string, the center message (channel text)
        public const int NdWindshearMessageCapt = 64;   // ndWindshearMessageCapt: string, the windshear message (channel text)
        public const int NdTcasMessageCapt = 66;        // ndTcasMessageCapt: string, the TCAS message (channel text)
        public const int NavInfoCapt = 68;              // navInfoCapt: nested NdInfoData, the next waypoint's three lines
        public const int NdMessagesCapt = 103;          // ndMessagesCapt: repeated nested NullWrapped string, the captain's ND messages
        public const int GroundWindCapt = 163;          // groundWindCapt: nested GroundWindData, the ground speed and wind texts
        public const int WeatherRadarData = 191;        // weatherRadarData: nested WeatherRadarState, the weather radar and PWS
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
    /// airports and fixes (docs/fenix-channel/evidence/03-nd.md, sitting 2, GetMapData in flight).
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
