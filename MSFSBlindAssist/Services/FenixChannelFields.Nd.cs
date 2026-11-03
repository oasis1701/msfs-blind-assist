namespace MSFSBlindAssist.Services;

// The fields of the captain's ND header: the part of RemoteData the ND feature alone reads, and the messages nested
// in it. Measured on 3.0.0.7613 (2026-11-03). Only the captain's side: the first officer's (63, 65, 67, 69, 104 and
// 164, beside 62, 64, 66, 68, 103 and 163) are never read. The route and the positions (2, 56, 84, 192, 193) are
// declared in FenixChannelFields.Route.cs, the EFIS list and the heading in FenixChannelFields.cs.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int NdCenterMessageCapt = 62;      // ndCenterMessageCapt: string, the center message (channel text)
        public const int NdWindshearMessageCapt = 64;   // ndWindshearMessageCapt: string, the windshear message (channel text)
        public const int NdTcasMessageCapt = 66;        // ndTcasMessageCapt: string, the TCAS message (channel text)
        public const int NavInfoCapt = 68;              // navInfoCapt: nested NdInfoData, the next waypoint's three lines
        public const int NdMessagesCapt = 103;          // ndMessagesCapt: repeated nested NullWrapped string, the captain's ND messages
        public const int GroundWindCapt = 163;          // groundWindCapt: nested GroundWindData, the ground speed and wind texts
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
}
