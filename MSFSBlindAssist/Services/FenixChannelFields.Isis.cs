namespace MSFSBlindAssist.Services;

// The fields of the ISIS, the integrated standby instrument: the members of IsisState the ISIS feature reads beside the
// gate's Powered (FenixChannelFields.cs), and the bug message nested in it. Read from FenixDisplay.exe 3.0.0.7613 on
// 2026-11-04 with the team's metadata tool: every number and wire shape below as the plan gave it. Robin's ground push of
// 2026-11-01 sent 182 powered, in hPa, new style, with QNH 1013, the Mach and both deviations NaN, and six empty bugs.
internal static partial class FenixChannelFields
{
    public static partial class IsisState
    {
        public const int Initializing = 2;          // Initializing: varint (bool)
        public const int Standard = 3;              // Standard: varint (bool), the baro at STD
        public const int App = 4;                   // App: varint (bool), the LS scales shown
        public const int InBugsPage = 5;            // InBugsPage: varint (bool)
        public const int HectoPascal = 6;           // HectoPascal: varint (bool); false is inches
        public const int LocFailed = 7;             // LocFailed: varint (bool)
        public const int GsFailed = 8;              // GsFailed: varint (bool)
        public const int NewStyle = 9;              // NewStyle: varint (bool)
        public const int Pitch = 10;                // Pitch: fixed64 (double)
        public const int Bank = 11;                 // Bank: fixed64 (double)
        public const int Airspeed = 12;             // Airspeed: fixed64 (double)
        public const int Altitude = 13;             // Altitude: fixed64 (double)
        public const int Mach = 14;                 // Mach: fixed64 (double)
        // 15, Brightness: fixed64 (double), not read.
        public const int Init = 16;                 // Init: Nullable<int32>, a varint while it has a value, absent while null
        public const int QuickRealign = 17;         // QuickRealign: Nullable<int32>, as Init
        public const int Qnh = 18;                  // Qnh: fixed64 (double)
        public const int LocDots = 19;              // LocDots: fixed64 (double), NaN while not drawn
        public const int GsDots = 20;               // GsDots: fixed64 (double), NaN while not drawn
        public const int SelectedBug = 21;          // SelectedBug: varint (int32)
        public const int Bugs = 22;                 // Bugs: repeated nested BugState
        public const int Slip = 23;                 // Slip: fixed64 (double)
    }

    /// <summary>
    /// BugState, one of the ISIS's bugs (<see cref="IsisState.Bugs"/>). Six were sent on the ground, each empty: off at 0.
    /// </summary>
    public static class BugState
    {
        public const int Enabled = 1;               // Enabled: varint (bool)
        public const int Value = 2;                 // Value: fixed64 (double)
    }
}
