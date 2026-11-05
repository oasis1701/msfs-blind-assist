namespace MSFSBlindAssist.Services;

// The fields of the captain's PFD: the part of RemoteData the PFD feature alone reads, and the messages nested in it.
// The numbers are the captain's side; the first officer's sit beside them (29 beside 28, 47 beside 46) and are not
// read. Every nested class is partial, so that a feature that reads another member of the same message adds it from a
// file of its own and does not declare the message again.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int AdirsCapt = 28;            // adirsCapt: nested AdirsData, the captain's air data
        public const int AdirsAvailCapt = 46;       // adirsAvailCapt: nested AdirsAvailData, which of the captain's air data the PFD draws
        public const int LsInfoPfdCapt = 74;        // lsInfoPfdCapt: nested PfdLsInfoData, the LS information texts
        public const int LsDataCapt = 82;           // lsDataCapt: nested LsInputData, the localizer and glideslope deviations
        public const int SpeedTrend = 98;           // speedTrend: fixed64 (double), 10 x acceleration, knots per 10 s
        public const int ApproachLabelsCapt = 100;  // approachLabelsCapt: nested ApproachLabelData, the approach labels
        public const int MachCapt = 107;            // machCapt: string, the Mach line (channel text)
        public const int FcuData = 113;             // fcuData: nested FcuData, the speed target the FCU sends
        public const int PfdFlags = 117;            // pfdFlags: nested PfdFlagsData, the marker beacon and LS flag texts
        public const int AltStatus = 122;           // altStatus: nested AltStatusData, how the altitude readout is drawn
        public const int SpeedLimitsCapt = 125;     // speedLimitsCapt: nested SpeedLimitsData
        public const int Speeds = 127;              // speeds: nested SpeedsData, NaN for a speed that is not drawn
        public const int RadioAltString = 157;      // radioAltString: string, the radio altitude text (channel text)
        public const int DhMessage = 205;           // dhMessage: varint (bool), the DH message above the radio altitude
    }

    /// <summary>AdirsData, the captain's air data (<see cref="Remote.AdirsCapt"/>).</summary>
    public static partial class AdirsData
    {
        public const int Pitch = 1;                 // Pitch: fixed64 (double)
        public const int Bank = 2;                  // Bank: fixed64 (double)
        public const int Altitude = 4;              // Altitude: fixed64 (double), the PFD's baro altitude
        public const int Airspeed = 5;              // Airspeed: fixed64 (double), the indicated airspeed
        public const int VerticalSpeed = 6;         // VerticalSpeed: fixed64 (double)
    }

    /// <summary>AdirsAvailData, which of the captain's air data the PFD draws (<see cref="Remote.AdirsAvailCapt"/>).</summary>
    public static partial class AdirsAvailData
    {
        public const int Ir = 1;                    // Ir: nested IrAvailData
        public const int Adr = 2;                   // Adr: nested AdrAvailData
        public const int VsAvail = 8;               // VsAvail: varint (bool)
    }

    /// <summary>IrAvailData, the inertial data the PFD draws (<see cref="AdirsAvailData.Ir"/>).</summary>
    public static partial class IrAvailData
    {
        public const int AttAvail = 1;              // AttAvail: varint (bool)
        public const int HeadingAvail = 2;          // HeadingAvail: varint (bool)
    }

    /// <summary>AdrAvailData, the air data the PFD draws (<see cref="AdirsAvailData.Adr"/>).</summary>
    public static partial class AdrAvailData
    {
        public const int SpeedAvail = 1;            // SpeedAvail: varint (bool)
        public const int AltAvail = 2;              // AltAvail: varint (bool)
    }

    /// <summary>PfdLsInfoData, the LS information texts (<see cref="Remote.LsInfoPfdCapt"/>).</summary>
    public static partial class PfdLsInfoData
    {
        public const int Ident = 1;                 // Ident: string (channel text)
        public const int Freq = 2;                  // Freq: string (channel text)
        public const int Dme = 3;                   // Dme: string (channel text)
    }

    /// <summary>ApproachLabelData, the approach labels (<see cref="Remote.ApproachLabelsCapt"/>).</summary>
    public static partial class ApproachLabelData
    {
        public const int Center = 1;                // Center: string (channel text)
        public const int Loc = 2;                   // Loc: string (channel text)
        public const int Gs = 3;                    // Gs: string (channel text)
    }

    /// <summary>LsInputData, the deviations the LS scales draw (<see cref="Remote.LsDataCapt"/>).</summary>
    public static partial class LsInputData
    {
        public const int PfdHorDots = 1;            // PfdHorDots: fixed64 (double), the localizer deviation
        public const int PfdVertDots = 2;           // PfdVertDots: fixed64 (double), the glideslope deviation
    }

    /// <summary>FcuData, what the FCU sends of its speed target (<see cref="Remote.FcuData"/>).</summary>
    public static partial class FcuData
    {
        public const int TargetIas = 4;             // TargetIas: fixed64 (double)
        public const int BugsVisible = 5;           // BugsVisible: varint (bool)
        public const int SpeedManaged = 9;          // SpeedManaged: varint (bool)
    }

    /// <summary>PfdFlagsData, the flag texts of the PFD (<see cref="Remote.PfdFlags"/>).</summary>
    public static partial class PfdFlagsData
    {
        public const int MarkerCapt = 1;            // MarkerCapt: string (channel text)
        public const int LsFlagCapt = 3;            // LsFlagCapt: string (channel text)
    }

    /// <summary>AltStatusData, how the altitude readout is drawn (<see cref="Remote.AltStatus"/>).</summary>
    public static partial class AltStatusData
    {
        public const int Metric = 3;                // Metric: varint (bool), a metric altitude is drawn under the baro box
        public const int AltAmber = 8;              // AltAmber: varint (bool), the altitude readout is drawn amber
    }

    /// <summary>SpeedLimitsData, the speed limits the tape draws (<see cref="Remote.SpeedLimitsCapt"/>).</summary>
    public static partial class SpeedLimitsData
    {
        public const int VMax = 1;                  // VMax: fixed64 (double)
        public const int Vls = 2;                   // Vls: fixed64 (double)
        public const int AlphaProt = 3;             // AlphaProt: fixed64 (double)
        public const int Vsw = 5;                   // Vsw: fixed64 (double), NaN in normal law
    }

    /// <summary>SpeedsData, the speeds the tape draws as marks (<see cref="Remote.Speeds"/>).</summary>
    public static partial class SpeedsData
    {
        public const int V1 = 1;                    // V1: fixed64 (double)
        public const int V2 = 2;                    // V2: fixed64 (double)
        public const int Vr = 3;                    // Vr: fixed64 (double)
        public const int FlapRetraction = 4;        // FlapRetraction: fixed64 (double), the F speed
        public const int SlatRetraction = 5;        // SlatRetraction: fixed64 (double), the S speed
        public const int GreenDot = 6;              // GreenDot: fixed64 (double)
        public const int VfeNext = 7;               // VfeNext: fixed64 (double)
    }
}
