namespace MSFSBlindAssist.Services;

// The fields only these eight pages of the SD window read: the APU, BLEED, PRESS, COND, CRUISE, DOOR, WHEEL and F/CTL
// pages. The APU message (24), the engines (129), the fuel used (134.13), the weight unit (154) and the valve states are
// declared in FenixChannelFields.cs and are not repeated here. PneumaticState (151) is read by
// APU, BLEED, PRESS, COND and CRUISE alike. A nested message is a class named for its contract type.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int WheelBrakeFlags = 137;     // wheelBrakeFlags: nested WheelBrakeFlags, the WHEEL page's labels
        public const int WheelTemp = 138;           // wheelTemp: nested WheelTempData, the brake temperatures
        public const int TyrePressure = 139;        // tyrePressure: nested TyrePressureData, the tire pressures
        // 140 is checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata tool ([FNX-23]).
        public const int WheelAmber = 140;          // wheelAmber: nested WheelAmberData, the brake temperatures drawn amber
        public const int BrakeReleased = 141;       // brakeReleased: nested BrakeReleasedData, which brakes are released
        public const int GearPosition = 143;        // gearPosition: nested GearPositionData, the three gears
        public const int GearDoor = 144;            // gearDoor: nested GearDoorData, the three gear doors
        public const int FctlSurfaces = 145;        // fctlSurfaces: nested FctlSurfaceData, the flight control surfaces
        public const int RudderData = 146;          // rudderData: nested RudderData, the rudder trim
        public const int FctlStatus = 147;          // fctlStatus: nested FctlStatusData
        public const int FccStatus = 148;           // fccStatus: nested FccStatusData, which computers are available
        public const int PneumaticState = 151;      // pneumaticState: nested PneumaticState, the bleed, packs, zones and pressurization
        public const int Doors = 179;               // doors: nested DoorStatus
        public const int CrewOxy = 180;             // crewOxy: nested CrewOxygenStatus
    }

    /// <summary>WheelBrakeFlags, the WHEEL page's labels and modes (<see cref="Remote.WheelBrakeFlags"/>).</summary>
    public static partial class WheelBrakeFlags
    {
        public const int AlternateBrakeGreen = 2;   // AlternateBrakeGreen: varint (bool)
        public const int AutoBrakeLow = 4;          // AutoBrakeLow: varint (bool)
        public const int AutoBrakeMed = 5;          // AutoBrakeMed: varint (bool)
        public const int AutoBrakeMax = 6;          // AutoBrakeMax: varint (bool)
        // 10, 11 and 13 are checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata tool ([FNX-23]).
        public const int Bscu1Amber = 10;           // Bscu1Amber: varint (bool)
        public const int Bscu2Amber = 11;           // Bscu2Amber: varint (bool)
        public const int AutoBrakeAmber = 13;       // AutoBrakeAmber: varint (bool)
        public const int NormBrakeGreen = 7;       // NormBrakeGreen: varint (bool)
        public const int NormBrakeAmber = 8;        // NormBrakeAmber: varint (bool)
        public const int SteeringLabel = 9;         // SteeringLabel: varint (bool)
        public const int LgCtlLabel = 12;           // LgCtlLabel: varint (bool)
        public const int AlternateBrakeAmber = 15;  // AlternateBrakeAmber: varint (bool)
        public const int AntiSkidGreen = 16;        // AntiSkidGreen: varint (bool)
        public const int AntiSkidAmber = 17;        // AntiSkidAmber: varint (bool)
        public const int AccuOnly = 18;             // AccuOnly: varint (bool)
        public const int TpisFitted = 19;           // TpisFitted: varint (bool)
    }

    /// <summary>WheelTempData, the brake temperatures (<see cref="Remote.WheelTemp"/>).</summary>
    public static partial class WheelTempData
    {
        public const int Wheel1 = 1;                // Wheel1: fixed64 (double)
        public const int Wheel2 = 2;                // Wheel2: fixed64 (double)
        public const int Wheel3 = 3;                // Wheel3: fixed64 (double)
        public const int Wheel4 = 4;                // Wheel4: fixed64 (double)
    }

    /// <summary>WheelAmberData, the brake temperatures drawn amber (<see cref="Remote.WheelAmber"/>).</summary>
    public static partial class WheelAmberData
    {
        public const int Wheel1 = 1;                // Wheel1: varint (bool)
        public const int Wheel2 = 2;                // Wheel2: varint (bool)
        public const int Wheel3 = 3;                // Wheel3: varint (bool)
        public const int Wheel4 = 4;                // Wheel4: varint (bool)
    }

    /// <summary>TyrePressureData, the tire pressures (<see cref="Remote.TyrePressure"/>).</summary>
    public static partial class TyrePressureData
    {
        public const int NoseLh = 1;                // NoseLh: fixed64 (double)
        public const int NoseRh = 2;                // NoseRh: fixed64 (double)
        public const int LeftMain1 = 3;             // LeftMain1: fixed64 (double)
        public const int LeftMain2 = 4;             // LeftMain2: fixed64 (double)
        public const int RightMain1 = 5;            // RightMain1: fixed64 (double)
        public const int RightMain2 = 6;            // RightMain2: fixed64 (double)
    }

    /// <summary>BrakeReleasedData, which brakes are released (<see cref="Remote.BrakeReleased"/>).</summary>
    public static partial class BrakeReleasedData
    {
        public const int Left1 = 1;                 // Left1: varint (bool)
        public const int Left2 = 2;                 // Left2: varint (bool)
        public const int Right1 = 3;                // Right1: varint (bool)
        public const int Right2 = 4;                // Right2: varint (bool)
    }

    /// <summary>GearPositionData, the three gears (<see cref="Remote.GearPosition"/>).</summary>
    public static partial class GearPositionData
    {
        public const int Nose = 1;                  // Nose: nested GearUnitStatus
        public const int Left = 2;                  // Left: nested GearUnitStatus
        public const int Right = 3;                 // Right: nested GearUnitStatus
    }

    /// <summary>GearUnitStatus, one gear as each of the two LGCIUs reports it (<see cref="GearPositionData.Nose"/>).</summary>
    public static partial class GearUnitStatus
    {
        public const int Lgciu1 = 1;                // Lgciu1: fixed64 (double); see GearUnit
        public const int Lgciu2 = 2;                // Lgciu2: fixed64 (double); see GearUnit
    }

    /// <summary>GearDoorData, the three gear doors (<see cref="Remote.GearDoor"/>).</summary>
    public static partial class GearDoorData
    {
        public const int Nose = 1;                  // Nose: fixed64 (double); see GearDoorPosition
        public const int LeftMain = 2;              // LeftMain: fixed64 (double); see GearDoorPosition
        public const int RightMain = 3;             // RightMain: fixed64 (double); see GearDoorPosition
    }

    /// <summary>
    /// The values of a <see cref="GearUnitStatus"/> field the app knows. Any other value reads "not available".
    /// </summary>
    public static class GearUnit
    {
        // Measured on the 2026-11-03 gear extension (A321, 22:44:46-22:45:05); the contract names none of these values.
        public const double Up = 2;                 // every LGCIU value read 2.0 with the gear up, until the gear was selected down
        public const double InTransit = 1;          // every LGCIU value read 1.0 for 10 s while the gear extended
        public const double Down = 0;               // the field went absent (0) once the gear was down and stayed so
    }

    /// <summary>
    /// The values of a <see cref="GearDoorData"/> field the app knows. Any other value reads "not available".
    /// </summary>
    public static class GearDoorPosition
    {
        // Measured on the 2026-11-03 gear extension (A321, 22:44:46-22:45:05); the contract does not name what each means.
        public const double Closed = 1;             // every door read 1.0 with the gear up and again once the doors closed
        public const double Opening = 0;            // absent (0) for 3-5 s after a door left closed
        public const double Open = 2;               // 2.0 for 13-15 s while the gear extended, until 8 s after it locked down
    }

    /// <summary>
    /// The values of a <see cref="ZoneTemperatureData.HotAirValve"/> field the app knows. Any other value reads "not available".
    /// </summary>
    public static class HotAirStates
    {
        // Measured at the CYYZ gate on 2026-11-03 (A321 CFM, build 3.0.0.7613); the contract names none of these values.
        public const double Open = 0;               // the field was left out in cruise with HOT AIR on, and after the 2.0 phase at the gate
        public const double Closed = 1;             // read 1.0 as HOT AIR was turned on
        public const double InTransit = 2;          // read 2.0 for 3 s after the 1.0, before the field was left out
    }

    /// <summary>FctlSurfaceData, the flight control surfaces (<see cref="Remote.FctlSurfaces"/>); each is a nested SurfaceState.</summary>
    public static partial class FctlSurfaceData
    {
        public const int LeftAileron = 1;           // LeftAileron: nested SurfaceState
        public const int LeftSpoiler1 = 2;          // LeftSpoiler1: nested SurfaceState
        public const int LeftSpoiler2 = 3;          // LeftSpoiler2: nested SurfaceState
        public const int LeftSpoiler3 = 4;          // LeftSpoiler3: nested SurfaceState
        public const int LeftSpoiler4 = 5;          // LeftSpoiler4: nested SurfaceState
        public const int LeftSpoiler5 = 6;          // LeftSpoiler5: nested SurfaceState
        public const int RightAileron = 7;          // RightAileron: nested SurfaceState
        public const int RightSpoiler1 = 8;         // RightSpoiler1: nested SurfaceState
        public const int RightSpoiler2 = 9;         // RightSpoiler2: nested SurfaceState
        public const int RightSpoiler3 = 10;        // RightSpoiler3: nested SurfaceState
        public const int RightSpoiler4 = 11;        // RightSpoiler4: nested SurfaceState
        public const int RightSpoiler5 = 12;        // RightSpoiler5: nested SurfaceState
        public const int LeftElevator = 13;         // LeftElevator: nested SurfaceState
        public const int RightElevator = 14;        // RightElevator: nested SurfaceState
        public const int Ths = 15;                  // Ths: nested SurfaceState
        public const int Rudder = 16;               // Rudder: nested SurfaceState
    }

    /// <summary>SurfaceState, one flight control surface (<see cref="FctlSurfaceData.LeftAileron"/> and the rest).</summary>
    public static partial class SurfaceState
    {
        public const int Position = 1;              // Position: fixed64 (double)
        public const int Fault = 2;                 // Fault: varint (bool)
    }

    /// <summary>RudderData, the rudder trim (<see cref="Remote.RudderData"/>).</summary>
    public static partial class RudderData
    {
        public const int Trim = 2;                  // Trim: fixed64 (double)
    }

    /// <summary>FctlStatusData (<see cref="Remote.FctlStatus"/>).</summary>
    public static partial class FctlStatusData
    {
        public const int FcdcAvail = 3;             // FcdcAvail: varint (bool)
    }

    /// <summary>FccStatusData, which flight control computers are available (<see cref="Remote.FccStatus"/>).</summary>
    public static partial class FccStatusData
    {
        public const int Elac1 = 1;                 // Elac1: varint (bool)
        public const int Elac2 = 2;                 // Elac2: varint (bool)
        public const int Sec1 = 3;                  // Sec1: varint (bool)
        public const int Sec2 = 4;                  // Sec2: varint (bool)
        public const int Sec3 = 5;                  // Sec3: varint (bool)
    }

    /// <summary>PneumaticState, the bleed, packs, zones and pressurization (<see cref="Remote.PneumaticState"/>).</summary>
    public static partial class PneumaticState
    {
        public const int Valves = 1;                // Valves: nested PneumaticValveData
        public const int BleedPressure = 2;         // BleedPressure: nested BleedPressureData
        public const int BleedTemperature = 3;      // BleedTemperature: nested BleedTemperatureData
        public const int Flags = 4;                 // Flags: nested PneumaticFlagsData
        public const int Pack1 = 5;                 // Pack1: nested PackStateData
        public const int Pack2 = 6;                 // Pack2: nested PackStateData
        public const int Zone = 7;                  // Zone: nested ZoneTemperatureData
        public const int CondStates = 8;            // CondStates: nested ConditioningStateData
        public const int PressFlags = 9;            // PressFlags: nested PressurizationFlagsData
        public const int PressData = 10;            // PressData: nested PressurizationData
    }

    /// <summary>PneumaticValveData, the bleed valves (<see cref="PneumaticState.Valves"/>). Each is a <see cref="ValveStateEnum"/>; Open is left out on the wire.</summary>
    public static partial class PneumaticValveData
    {
        public const int Eng1Bleed = 1;             // Eng1Bleed: varint (ValveStateEnum)
        public const int Eng1Hp = 2;                // Eng1Hp: varint (ValveStateEnum)
        public const int Eng2Bleed = 3;             // Eng2Bleed: varint (ValveStateEnum)
        public const int Eng2Hp = 4;                // Eng2Hp: varint (ValveStateEnum)
        public const int ApuBleed = 5;              // ApuBleed: varint (ValveStateEnum)
        public const int Crossbleed = 6;            // Crossbleed: varint (ValveStateEnum)
        public const int WaiLeft = 7;               // WaiLeft: varint (ValveStateEnum)
        public const int WaiRight = 8;              // WaiRight: varint (ValveStateEnum)
        public const int Pack1Flow = 9;             // Pack1Flow: varint (ValveStateEnum)
        public const int Pack2Flow = 10;            // Pack2Flow: varint (ValveStateEnum)
        public const int RamAir = 11;               // RamAir: varint (ValveStateEnum)
    }

    /// <summary>BleedPressureData (<see cref="PneumaticState.BleedPressure"/>).</summary>
    public static partial class BleedPressureData
    {
        public const int Left = 1;                  // Left: fixed64 (double)
        public const int Right = 2;                 // Right: fixed64 (double)
        public const int Apu = 3;                   // Apu: fixed64 (double)
    }

    /// <summary>BleedTemperatureData (<see cref="PneumaticState.BleedTemperature"/>).</summary>
    public static partial class BleedTemperatureData
    {
        public const int Left = 1;                  // Left: fixed64 (double)
        public const int Right = 2;                 // Right: fixed64 (double)
    }

    /// <summary>PneumaticFlagsData (<see cref="PneumaticState.Flags"/>).</summary>
    public static partial class PneumaticFlagsData
    {
        public const int WaiSwitchOn = 1;           // WaiSwitchOn: varint (bool)
        // 2, 3 and 9 to 14 are checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata tool
        // ([FNX-23]): the BLEED page's amber sides and start air lights.
        public const int WaiValveAmberLeft = 2;     // WaiValveAmberLeft: varint (bool)
        public const int WaiValveAmberRight = 3;    // WaiValveAmberRight: varint (bool)
        public const int StartAirAmberLeft = 9;     // StartAirAmberLeft: varint (bool)
        public const int StartAirAmberRight = 10;   // StartAirAmberRight: varint (bool)
        public const int PrecoolerTempAmberLeft = 11;   // PrecoolerTempAmberLeft: varint (bool)
        public const int PrecoolerTempAmberRight = 12;  // PrecoolerTempAmberRight: varint (bool)
        public const int PrecoolerPressAmberLeft = 13;  // PrecoolerPressAmberLeft: varint (bool)
        public const int PrecoolerPressAmberRight = 14; // PrecoolerPressAmberRight: varint (bool)
    }

    /// <summary>PackStateData, one air conditioning pack (<see cref="PneumaticState.Pack1"/> or <see cref="PneumaticState.Pack2"/>).</summary>
    public static partial class PackStateData
    {
        public const int CompressorTemp = 3;        // CompressorTemp: fixed64 (double)
        public const int OutputTemp = 4;            // OutputTemp: fixed64 (double)
    }

    /// <summary>ZoneTemperatureData, the cabin zones and cargo compartments (<see cref="PneumaticState.Zone"/>).</summary>
    public static partial class ZoneTemperatureData
    {
        public const int CockpitTemp = 1;           // CockpitTemp: fixed64 (double)
        public const int FwdCabinTemp = 2;          // FwdCabinTemp: fixed64 (double)
        public const int AftCabinTemp = 3;          // AftCabinTemp: fixed64 (double)
        public const int CockpitDuctTemp = 4;       // CockpitDuctTemp: fixed64 (double)
        public const int FwdCabinDuctTemp = 5;      // FwdCabinDuctTemp: fixed64 (double)
        public const int AftCabinDuctTemp = 6;      // AftCabinDuctTemp: fixed64 (double)
        public const int CockpitTrimValve = 7;      // CockpitTrimValve: fixed64 (double)
        public const int FwdCabinTrimValve = 8;     // FwdCabinTrimValve: fixed64 (double)
        public const int AftCabinTrimValve = 9;     // AftCabinTrimValve: fixed64 (double)
        public const int HotAirValve = 10;          // HotAirValve: fixed64 (double)
        public const int FwdCargoTemp = 11;         // FwdCargoTemp: fixed64 (double)
        public const int AftCargoTemp = 12;         // AftCargoTemp: fixed64 (double)
        public const int FwdCargoDuctTemp = 13;     // FwdCargoDuctTemp: fixed64 (double)
        public const int AftCargoDuctTemp = 14;     // AftCargoDuctTemp: fixed64 (double)
        public const int FwdCargoTrimValve = 15;    // FwdCargoTrimValve: fixed64 (double)
        public const int AftCargoTrimValve = 16;    // AftCargoTrimValve: fixed64 (double)
    }

    /// <summary>ConditioningStateData (<see cref="PneumaticState.CondStates"/>).</summary>
    public static partial class ConditioningStateData
    {
        public const int CabFan1Off = 1;            // CabFan1Off: varint (bool)
        public const int Alternate = 2;             // Alternate: varint (bool)
        public const int PackReg = 3;               // PackReg: varint (bool)
        public const int HasCargoHeatingFwd = 5;    // HasCargoHeatingFwd: varint (bool)
        public const int HasCargoHeatingAft = 6;    // HasCargoHeatingAft: varint (bool)
        public const int CabFan2Off = 7;            // CabFan2Off: varint (bool)
    }

    /// <summary>PressurizationFlagsData (<see cref="PneumaticState.PressFlags"/>).</summary>
    public static partial class PressurizationFlagsData
    {
        // 1 to 11, 17 to 20 and 22 are checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata
        // tool ([FNX-23]). A field named for a color speaks it; a ...Pulse field is the SD's flash ("pulsing").
        public const int Pack1Amber = 1;            // Pack1Amber: varint (bool)
        public const int Pack2Amber = 2;            // Pack2Amber: varint (bool)
        public const int VentAmber = 3;             // VentAmber: varint (bool)
        public const int InletAmber = 4;            // InletAmber: varint (bool)
        public const int ExtractAmber = 5;          // ExtractAmber: varint (bool)
        public const int CabinAltRed = 6;           // CabinAltRed: varint (bool)
        public const int CabinAltPulse = 7;         // CabinAltPulse: varint (bool)
        public const int DeltaPAmber = 8;           // DeltaPAmber: varint (bool)
        public const int DeltaPPulse = 9;           // DeltaPPulse: varint (bool)
        public const int VsAmber = 10;              // VsAmber: varint (bool)
        public const int VsPulse = 11;              // VsPulse: varint (bool)
        public const int BlowerFault = 17;          // BlowerFault: varint (bool)
        public const int ExtractFault = 18;         // ExtractFault: varint (bool)
        public const int Sys1Amber = 19;            // Sys1Amber: varint (bool)
        public const int Sys2Amber = 20;            // Sys2Amber: varint (bool)
        public const int OutflowValveAmber = 22;    // OutflowValveAmber: varint (bool)
        public const int ManualMode = 13;           // ManualMode: varint (bool)
        public const int Sys1Active = 14;           // Sys1Active: varint (bool)
        public const int Sys2Active = 15;           // Sys2Active: varint (bool)
        public const int ManualLandingElev = 16;    // ManualLandingElev: varint (bool)
    }

    /// <summary>PressurizationData (<see cref="PneumaticState.PressData"/>).</summary>
    public static partial class PressurizationData
    {
        public const int DifferentialPressure = 1;  // DifferentialPressure: fixed64 (double)
        public const int CabinVs = 2;               // CabinVs: fixed64 (double)
        public const int CabinAlt = 3;              // CabinAlt: fixed64 (double)
        public const int InletValve = 4;            // InletValve: varint (ValveStateEnum)
        public const int ExtractValve = 5;          // ExtractValve: varint (ValveStateEnum)
        public const int OutflowValve = 6;          // OutflowValve: fixed64 (double)
        public const int LandingCabinAlt = 7;       // LandingCabinAlt: fixed64 (double)
        public const int SafetyValve = 9;           // SafetyValve: varint (ValveStateEnum)
    }

    /// <summary>DoorStatus, the doors, exits and slides (<see cref="Remote.Doors"/>).</summary>
    public static partial class DoorStatus
    {
        public const int Avionics1 = 1;             // Avionics1: varint (bool)
        public const int Avionics2 = 2;             // Avionics2: varint (bool)
        public const int Avionics3 = 3;             // Avionics3: varint (bool)
        public const int FwdEntryLeft = 4;          // FwdEntryLeft: varint (bool)
        public const int FwdEntryRight = 5;         // FwdEntryRight: varint (bool)
        public const int Avionics4 = 6;             // Avionics4: varint (bool)
        public const int FwdCargo = 7;              // FwdCargo: varint (bool)
        public const int LeftWing1 = 8;             // LeftWing1: varint (bool)
        public const int LeftWing2 = 9;             // LeftWing2: varint (bool)
        public const int RightWing1 = 10;           // RightWing1: varint (bool)
        public const int RightWing2 = 11;           // RightWing2: varint (bool)
        public const int AftCargo = 12;             // AftCargo: varint (bool)
        public const int Bulk = 13;                 // Bulk: varint (bool)
        public const int AftEntryLeft = 14;         // AftEntryLeft: varint (bool)
        public const int AftEntryRight = 15;        // AftEntryRight: varint (bool)
        public const int SlideFwdLeft = 16;         // SlideFwdLeft: varint (bool)
        public const int SlideFwdRight = 17;        // SlideFwdRight: varint (bool)
        public const int SlideCenterLeft = 18;      // SlideCenterLeft: varint (bool)
        public const int SlideCenterRight = 19;     // SlideCenterRight: varint (bool)
        public const int SlideAftLeft = 20;         // SlideAftLeft: varint (bool)
        public const int SlideAftRight = 21;        // SlideAftRight: varint (bool)
        public const int SlideFwdWingLeft = 22;     // SlideFwdWingLeft: varint (bool)
        public const int SlideFwdWingRight = 23;    // SlideFwdWingRight: varint (bool)
        public const int SlideAftWingLeft = 24;     // SlideAftWingLeft: varint (bool)
        public const int SlideAftWingRight = 25;    // SlideAftWingRight: varint (bool)
        public const int HasDoubleOverwingExit = 26; // HasDoubleOverwingExit: varint (bool)
        public const int HasBulkCargoDoor = 27;     // HasBulkCargoDoor: varint (bool)
    }

    /// <summary>CrewOxygenStatus (<see cref="Remote.CrewOxy"/>).</summary>
    public static partial class CrewOxygenStatus
    {
        public const int SdacHasData = 2;           // SdacHasData: varint (bool)
        public const int LeftPressure = 5;          // LeftPressure: fixed64 (double)
    }
}
