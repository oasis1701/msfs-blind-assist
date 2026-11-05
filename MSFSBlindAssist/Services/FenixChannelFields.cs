namespace MSFSBlindAssist.Services;

/// <summary>
/// The calls the Fenix display channel client may open. All four only read: the display data stream, the
/// ECAM entries stream and the two map reads. The service has calls that write values, run actions and hand out credentials
/// too; none has a member here, and <see cref="FenixChannelFields.PathOf"/> gives a path for no other
/// value.
/// </summary>
internal enum FenixChannelCall
{
    /// <summary>The display data: a stream of AircraftDataPush messages, about 17 a second.</summary>
    DisplayData,

    /// <summary>The ECAM entries: a stream of EcamUpdate messages, sent when the list changes.</summary>
    EcamEntries,

    /// <summary>The ND map points' version: one request, one reply, read-only (a unary call).</summary>
    MapDataVersion,

    /// <summary>The ND map points: one request, one reply, read-only (a unary call).</summary>
    MapData
}

/// <summary>
/// The app's own copy of what it uses of the Fenix display channel (gRPC on 127.0.0.1:18083): where the
/// channel is, the four calls that read it, the field numbers the decoders read and the wire shape of each.
/// The service is protobuf-net "code first", so there is no .proto file to take them from: they were read
/// off the live channel, and after a Fenix update this file, with the <c>FenixChannelFields.&lt;Feature&gt;.cs</c>
/// partials of this class, is what to compare with it. The field numbers live in this file and in those
/// partials, and nowhere else: a field that two features read is declared here, once; a field that only one
/// feature reads is declared by that feature, in its own partial. The call paths live in this file alone, and
/// no other file in the app names one.
/// </summary>
/// <remarks>
/// A field's contract name and wire shape sit in the comment beside its number. The shape is not looked
/// up at run time: a decoder enforces it (<see cref="FenixChannelDecoder"/> for the fields the gate reads, each
/// feature's own decoder for the fields it reads), and a field that arrives in another shape
/// is recorded by its path and never read as a value.
/// </remarks>
internal static partial class FenixChannelFields
{
    /// <summary>
    /// The numeric loopback address, never the name "localhost" (as [A320-40] has it for the data service).
    /// </summary>
    public const string Host = "127.0.0.1";

    public const int Port = 18083;

    /// <summary>
    /// The gRPC path of a call: <c>/service/method</c>, the service named without the leading I of its
    /// interface. An enum member that is not one of the four throws, so no other path can come out of here.
    /// </summary>
    public static string PathOf(FenixChannelCall call) => call switch
    {
        FenixChannelCall.DisplayData => "/ProsimRemoting.GrpcAircraftDataSource/Subscribe",
        FenixChannelCall.EcamEntries => "/ProsimRemoting.GrpcSdk/SubscribeEcamEntries",
        FenixChannelCall.MapDataVersion => "/ProsimRemoting.GrpcAircraftDataSource/GetMapDataVersion",
        FenixChannelCall.MapData => "/ProsimRemoting.GrpcAircraftDataSource/GetMapData",
        _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Not a Fenix channel call.")
    };

    /// <summary>
    /// The Fenix builds the channel has been checked against in the sim, spelled as
    /// <c>FenixDataService.GetSoftwareVersionAsync</c> gives them, and matched exactly. The trust gate trusts no
    /// display on a build outside this set. 3.0.0.7613 is the first, confirmed by the owner on 2026-11-02 after that
    /// day's in-sim run: the field numbers here were checked against it ([FNX-23]).
    /// </summary>
    public static readonly IReadOnlySet<string> VerifiedBuilds = new HashSet<string>(StringComparer.Ordinal)
    {
        "3.0.0.7613"
    };

    /// <summary>AircraftDataPush, the message of the display data call.</summary>
    public static class Push
    {
        public const int PrimaryData = 1;           // PrimaryData: nested RemoteData
    }

    /// <summary>
    /// RemoteData, the part of a push the app reads. Partial: a field that only one feature reads is added to it by
    /// that feature, from its own <c>FenixChannelFields.&lt;Feature&gt;.cs</c>.
    /// </summary>
    public static partial class Remote
    {
        public const int Efis = 1;                  // efis: repeated nested EFIS, captain then first officer
        public const int FwcPage = 10;              // fwcPage: varint (enum), the lower ECAM page shown
        public const int FwcPhase = 11;             // fwcPhase: varint (int32), the flight warning phase
        public const int DuData = 12;               // duData: nested DUData, the display units' power and content
        public const int ApuData = 24;              // apuData: nested ApuData
        public const int FdData = 110;              // fdData: nested FdData, the flight director
        public const int Fma = 111;                 // fma: repeated nested FmaEntry, one per slot
        public const int EngineState = 129;         // engineState: nested EngineControlState
        public const int FuelState = 134;           // fuelState: nested FuelState
        public const int FobString = 153;           // fobString: string, the E/WD's FOB line
        public const int IsKg = 154;                // isKg: varint (bool); false, pounds, is left out on the wire, so an absent field means LBS
        public const int UpperEcamLeft = 173;       // upperEcamLeft: repeated nested NullWrapped string, E/WD left
        public const int UpperEcamRight = 174;      // upperEcamRight: repeated nested NullWrapped string, E/WD right
        public const int Isis = 182;                // isis: nested IsisState
        public const int Heading = 194;             // heading: nested HeadingData
    }

    /// <summary>
    /// EFIS, one of the two EFIS control panels of a push: <see cref="Remote.Efis"/> is repeated, the captain's panel
    /// first and then the first officer's. Shared by the PFD feature (the baro, the LS and the meters settings) and the
    /// ND feature (the mode, the range, the options).
    /// </summary>
    public static class Efis
    {
        public const int Mode = 1;                  // Mode: varint (EfisModeEnum), left out at its default: an absent field means ROSE NAV (2), and LS is sent as 0
        public const int MapOption = 2;             // MapOption: varint (EfisMapOption, nullable: 0 CSTR to 4 ARPT), left out for none
        public const int ZoomSelection = 3;         // ZoomSelection: varint (int32), default 40; left out at 40, so an absent field means 40
        public const int Fpv = 4;                   // Fpv: varint (bool)
        public const int Meters = 5;                // Meters: varint (bool)
        public const int Ls = 6;                    // Ls: varint (bool)
        public const int QfeMode = 7;               // QfeMode: varint (bool)
        public const int VerticalTrack = 8;         // VerticalTrack: varint (bool)
        public const int Traffic = 9;               // Traffic: varint (bool)
        public const int QnhStd = 10;               // QnhStd: varint (bool)
        public const int QnhInches = 11;            // QnhInches: varint (bool)
        public const int ShowQnh = 12;              // ShowQnh: varint (bool)
        public const int Update = 13;               // Update: varint (int32)
        public const int Qnh = 14;                  // Qnh: fixed32 (single), default 1013; left out at 1013, so an absent field means 1013
        public const int QnhInch = 15;              // QnhInch: fixed32 (single); 30.0025 in the 2026-11-01 cruise capture
        public const int QnhMillibar = 16;          // QnhMillibar: fixed32 (single); 1016 in the 2026-11-01 cruise capture
        public const int EfisSelectors = 17;        // EfisSelectors: repeated varint (EfisSelectorEnum), sent unpacked
    }

    /// <summary>
    /// DUData, the display units: entry i of each list is unit i. The Fenix sends both lists unpacked, one varint
    /// field per entry; protobuf's packed form of the same lists reads the same.
    /// </summary>
    public static class DuData
    {
        public const int Power = 1;                 // Power: repeated varint (bool), one per unit
        public const int Content = 2;               // Content: repeated varint (DisplayTypeEnum), one per unit
    }

    /// <summary>
    /// DisplayTypeEnum, what a display unit shows: the values the app reads (DisplayTypeEnum values, measured on
    /// 3.0.0.7613, 2026-11-02). A display shown only on another unit type, such as PFDFO or EcamOnND, counts as off.
    /// </summary>
    public static class DisplayContent
    {
        public const int PfdCapt = 0;               // PFDCapt: the captain's PFD
        public const int NdCapt = 2;                // NDCapt: the captain's ND
        public const int EcamUpper = 4;             // ECAMUpper: the upper ECAM, the E/WD
        public const int EcamLower = 5;             // ECAMLower: the lower ECAM, the SD
        public const int DcduCapt = 9;              // DCDUCapt: the captain's DCDU
    }

    /// <summary>
    /// IsisState, the ISIS message of a push (<see cref="Remote.Isis"/>). The ISIS is not one of the display units
    /// <see cref="DuData"/> lists, so this message says whether it is on. Partial: it holds the one member the trust
    /// gate reads; a feature that reads another member of the message declares it from its own
    /// <c>FenixChannelFields.&lt;Feature&gt;.cs</c>.
    /// </summary>
    public static partial class IsisState
    {
        public const int Powered = 1;               // Powered: varint (bool)
    }

    /// <summary>
    /// ApuData, the auxiliary power unit. Shared by the SD window (the APU generator on the ELEC page) and the SD
    /// pages (the APU page).
    /// </summary>
    public static class ApuData
    {
        public const int Load = 1;                  // Load: fixed64 (double)
        public const int Volts = 2;                 // Volts: fixed64 (double)
        public const int Frequency = 3;             // Frequency: fixed64 (double)
        public const int N1 = 4;                    // N1: fixed64 (double)
        public const int Egt = 5;                   // Egt: fixed64 (double)
        public const int EgtCaution = 6;            // EgtCaution: fixed64 (double)
        public const int EgtMax = 7;                // EgtMax: fixed64 (double)
        public const int Avail = 8;                 // Avail: varint (bool)
        public const int FuelLowPressure = 9;       // FuelLowPressure: varint (bool)
        public const int FlapOpen = 10;             // FlapOpen: varint (bool)
        public const int LowOil = 11;               // LowOil: varint (bool)
        public const int MasterPower = 12;          // MasterPower: varint (bool)
        public const int AmberTitle = 13;           // AmberTitle: varint (bool)
        public const int Off = 14;                  // Off: varint (bool)
    }

    /// <summary>FdData, the flight director.</summary>
    public static class FdData
    {
        public const int Pitch = 1;                 // Pitch: fixed64 (double), NaN when the bar is not drawn
        public const int Fpa = 2;                   // Fpa: fixed64 (double)
        public const int Roll = 3;                  // Roll: fixed64 (double)
        public const int Yawbar = 4;                // Yawbar: fixed64 (double)
        public const int FdOnCapt = 5;              // FdOnCapt: varint (bool)
        public const int FdOnFo = 6;                // FdOnFo: varint (bool)
        public const int TrkFpa = 7;                // TrkFpa: varint (bool)
        public const int RollGuidance = 8;          // RollGuidance: varint (bool)
        public const int FdFlash = 9;               // FdFlash: varint (bool)
        public const int FdInhibit = 10;            // FdInhibit: varint (bool)
        public const int InhibitRoll = 11;          // InhibitRoll: varint (bool)
        public const int InhibitPitch = 12;         // InhibitPitch: varint (bool)
    }

    /// <summary>FmaEntry, one slot of the FMA.</summary>
    public static class FmaEntry
    {
        public const int Text = 1;                  // Text: string, the coded line
        public const int ChangeBox = 2;             // ChangeBox: varint (bool)

        /// <summary>
        /// The FMA is 16 slots. A push with another count is not the shape the app was checked against, except
        /// a push with none, which is how protobuf-net sends an empty or null array (see FenixChannelDecoder).
        /// </summary>
        public const int SlotCount = 16;
    }

    /// <summary>
    /// EngineControlState, the engine message of a push (<see cref="Remote.EngineState"/>): one
    /// <see cref="SingleEngineState"/> for each engine, and what goes with both. Shared by the E/WD window, the SD
    /// window and the SD pages.
    /// </summary>
    public static class EngineControlState
    {
        public const int Left = 1;                  // Left: nested SingleEngineState
        public const int Right = 2;                 // Right: nested SingleEngineState
        public const int EprLimit = 3;              // EprLimit: fixed64 (double)
        public const int EngineModeSwitch = 4;      // EngineModeSwitch: varint (int32)
        public const int IdleString = 5;            // IdleString: string
        public const int ShowStarting = 6;          // ShowStarting: varint (bool)
        public const int ShowNacelle = 7;           // ShowNacelle: varint (bool)
        public const int EngLimit = 8;              // EngLimit: repeated nested NullWrapped string
    }

    /// <summary>SingleEngineState, the state of one engine (<see cref="EngineControlState.Left"/> or <see cref="EngineControlState.Right"/>).</summary>
    public static class SingleEngineState
    {
        public const int Config = 1;                // Config: nested EngineConfigData
        public const int N1 = 2;                    // N1: fixed64 (double)
        public const int N2 = 3;                    // N2: fixed64 (double)
        public const int Ff = 4;                    // Ff: fixed64 (double)
        public const int Epr = 5;                   // Epr: fixed64 (double)
        public const int Egt = 6;                   // Egt: fixed64 (double)
        public const int NacelleTemp = 7;           // NacelleTemp: fixed64 (double)
        public const int StartValve = 8;            // StartValve: varint (enum)
        public const int StarterPsi = 9;            // StarterPsi: fixed64 (double)
        public const int CoolingTime = 10;          // CoolingTime: fixed64 (double)
        public const int VibrationN1 = 11;          // VibrationN1: fixed64 (double)
        public const int VibrationN2 = 12;          // VibrationN2: fixed64 (double)
        public const int Starting = 13;             // Starting: varint (bool)
        public const int N1Mode = 14;               // N1Mode: varint (bool)
        public const int EngineStatus = 15;         // EngineStatus: varint (bool)
        public const int Avail = 16;                // Avail: varint (bool)
        public const int Igniter1 = 17;             // Igniter1: varint (bool)
        public const int Igniter2 = 18;             // Igniter2: varint (bool)
        public const int RevShown = 19;             // RevShown: varint (bool)
        public const int RevAmber = 20;             // RevAmber: varint (bool)
        public const int RevFlashing = 21;          // RevFlashing: varint (bool)
        public const int EngineString = 22;         // EngineString: string
        public const int OilQuantity = 23;          // OilQuantity: fixed64 (double)
        public const int OilPressure = 24;          // OilPressure: fixed64 (double)
        public const int OilTemperature = 25;       // OilTemperature: fixed64 (double)
        public const int OilQtyPulse = 26;          // OilQtyPulse: varint (bool)
        public const int OilTempHigh = 27;          // OilTempHigh: varint (bool)
        public const int OilTempAdvisory = 28;      // OilTempAdvisory: varint (bool)
        public const int OilPressPulse = 29;        // OilPressPulse: varint (bool)
        public const int NacelleTempHigh = 30;      // NacelleTempHigh: varint (bool)
        public const int EiuFault = 31;             // EiuFault: varint (bool)
        public const int LowN2 = 32;                // LowN2: varint (bool)
    }

    /// <summary>EngineConfigData, the configuration of one engine (<see cref="SingleEngineState.Config"/>).</summary>
    public static class EngineConfigData
    {
        public const int MaxEgt = 1;                // MaxEgt: fixed64 (double)
        public const int EgtRedline = 2;            // EgtRedline: fixed64 (double)
        public const int EgtEndOfBand = 3;          // EgtEndOfBand: fixed64 (double)
        public const int AmberLine = 4;             // AmberLine: fixed64 (double)
    }

    /// <summary>
    /// FuelState, the fuel message of a push (<see cref="Remote.FuelState"/>). Partial: it holds the one member two
    /// features read, the fuel used (the SD window and the SD pages); a feature that reads another member of the
    /// message declares it from its own <c>FenixChannelFields.&lt;Feature&gt;.cs</c>.
    /// </summary>
    public static partial class FuelState
    {
        public const int Used = 13;                 // Used: nested FuelUsedData
    }

    /// <summary>FuelUsedData, the fuel used (<see cref="FuelState.Used"/>): each engine's and the total.</summary>
    public static class FuelUsedData
    {
        public const int Engine1 = 1;               // Engine1: fixed64 (double)
        public const int Engine2 = 2;               // Engine2: fixed64 (double)
        public const int Total = 3;                 // Total: fixed64 (double)
    }

    /// <summary>
    /// ValveStateEnum, the state of a valve, in the order the contract declares them. Named for the contract's enum,
    /// so that it never clashes with a message member named ValveState. Open, the default, is left out on the wire
    /// (a cruise push carried the closed valves and none of the open ones), so a valve with no state field is open.
    /// Shared by the SD window (the engine start valves, the fuel valves) and the SD pages (bleed, pressurization,
    /// air conditioning).
    /// </summary>
    public static class ValveStateEnum
    {
        public const int Open = 0;                  // Open: the default, left out on the wire, so an absent valve state is open
        public const int Closed = 1;                // Closed
        public const int Transit = 2;               // Transit
        public const int FailedOpen = 3;            // FailedOpen
        public const int FailedClose = 4;           // FailedClose
        public const int DataInvalid = 5;           // DataInvalid
    }

    /// <summary>HeadingData, the heading the push carries.</summary>
    public static class HeadingData
    {
        public const int MagHeading = 1;            // MagHeading: fixed64 (double)
        public const int MagneticTrack = 2;         // MagneticTrack: fixed64 (double)
        public const int MagVar = 3;                // MagVar: fixed64 (double)
    }

    /// <summary>NullWrapped, the element of a string[] the contract marks NullWrapped.</summary>
    public static class NullWrapped
    {
        public const int Value = 1;                 // Value: string; an element without it is a null string
    }

    /// <summary>EcamUpdate, the message of the ECAM entries call.</summary>
    public static class EcamUpdate
    {
        public const int Entries = 1;               // Entries: repeated nested EcamEntryInfo
        public const int Heartbeat = 2;             // Heartbeat: varint (bool)
    }

    /// <summary>EcamEntryInfo (the contract's GrpcEcamEntryInfo), one entry of the ECAM list.</summary>
    public static class EcamEntryInfo
    {
        public const int Ata = 1;                   // Ata: string
        public const int Name = 2;                  // Name: string
        public const int Title = 3;                 // Title: string
        public const int IsAlert = 4;               // IsAlert: varint (bool)
    }
}
