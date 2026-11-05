namespace MSFSBlindAssist.Services;

// The fields of the SD window (the lower ECAM): the part of RemoteData the SD feature alone reads, and the messages
// nested in it. The ELEC, HYD and FUEL pages and the STATUS text and bottom line all read these; the engines (129),
// the fuel used (134.13), the weight unit (154), the APU (24) and the valve states are declared in
// FenixChannelFields.cs, and are not repeated here. Every nested class is partial, so that a feature that reads
// another member of the same message adds it from a file of its own and does not declare the message again.
internal static partial class FenixChannelFields
{
    public static partial class Remote
    {
        public const int ElecFlags = 16;            // elecFlags: nested ElecFlags, the electrical system's flags
        public const int BusStatus = 17;            // busStatus: nested BusStatus, which buses are powered
        public const int Gen1 = 18;                 // gen1: nested GeneratorData, engine generator 1
        public const int Gen2 = 19;                 // gen2: nested GeneratorData, engine generator 2
        public const int Tr1 = 20;                  // tr1: nested VoltAmps, transformer-rectifier 1
        public const int Tr2 = 21;                  // tr2: nested VoltAmps, transformer-rectifier 2
        public const int EssTr = 22;                // essTr: nested VoltAmps, the essential transformer-rectifier
        public const int ExtPwr = 23;               // extPwr: nested ExtPwrData, the external power
        public const int Bat1 = 25;                 // bat1: nested VoltAmps, battery 1
        public const int Bat2 = 26;                 // bat2: nested VoltAmps, battery 2
        public const int PtuState = 132;            // ptuState: varint (int32), the power transfer unit's state; see PtuStates
        public const int HydraulicState = 133;      // hydraulicState: nested HydraulicState
        public const int StatusLeft = 175;          // statusLeft: repeated nested NullWrapped string, the STATUS text, left column
        public const int StatusRight = 176;         // statusRight: repeated nested NullWrapped string, the STATUS text, right column
        public const int EcamBottom = 178;          // ecamBottom: repeated nested NullWrapped string, the bottom line; see EcamBottom for its positions
    }

    /// <summary>ElecFlags, the electrical system's flags (<see cref="Remote.ElecFlags"/>).</summary>
    public static partial class ElecFlags
    {
        public const int GalleyShed = 1;            // GalleyShed: varint (bool)
        public const int Bat1Charging = 2;          // Bat1Charging: varint (bool)
        public const int Bat2Charging = 3;          // Bat2Charging: varint (bool)
        public const int Gen1Disconnected = 4;      // Gen1Disconnected: varint (bool)
        public const int Gen2Disconnected = 5;      // Gen2Disconnected: varint (bool)
        public const int Gen1LowOilPressure = 6;    // Gen1LowOilPressure: varint (bool)
        public const int Gen2LowOilPressure = 7;    // Gen2LowOilPressure: varint (bool)
    }

    /// <summary>BusStatus, which buses are powered (<see cref="Remote.BusStatus"/>).</summary>
    public static partial class BusStatus
    {
        public const int DcBus1 = 1;                // DcBus1: varint (bool)
        public const int DcBus2 = 2;                // DcBus2: varint (bool)
        public const int DcEssential = 3;           // DcEssential: varint (bool)
        public const int AcBus1 = 4;                // AcBus1: varint (bool)
        public const int AcBus2 = 5;                // AcBus2: varint (bool)
        public const int DcBat = 6;                 // DcBat: varint (bool)
        public const int AcEssential = 7;           // AcEssential: varint (bool)
        public const int ExternalPowerConnected = 8; // ExternalPowerConnected: varint (bool)
    }

    /// <summary>GeneratorData, one engine generator (<see cref="Remote.Gen1"/> or <see cref="Remote.Gen2"/>).</summary>
    public static partial class GeneratorData
    {
        public const int Load = 1;                  // Load: fixed64 (double)
        public const int Voltage = 2;               // Voltage: fixed64 (double)
        public const int Frequency = 3;             // Frequency: fixed64 (double)
        public const int Selector = 4;              // Selector: varint (bool)
        public const int Temperature = 5;           // Temperature: fixed64 (double)
    }

    /// <summary>VoltAmps, the volts and amps of a transformer-rectifier or a battery (<see cref="Remote.Tr1"/>, <see cref="Remote.Bat1"/>).</summary>
    public static partial class VoltAmps
    {
        public const int Volts = 1;                 // Volts: fixed64 (double)
        public const int Amps = 2;                  // Amps: fixed64 (double)
    }

    /// <summary>ExtPwrData, the external power (<see cref="Remote.ExtPwr"/>).</summary>
    public static partial class ExtPwrData
    {
        public const int Volts = 1;                 // Volts: fixed64 (double)
        public const int Frequency = 2;             // Frequency: fixed64 (double)
    }

    /// <summary>HydraulicState, the three hydraulic systems and their pumps (<see cref="Remote.HydraulicState"/>).</summary>
    public static partial class HydraulicState
    {
        public const int Green = 1;                 // Green: nested HydSystemState
        public const int Blue = 2;                  // Blue: nested HydSystemState
        public const int Yellow = 3;                // Yellow: nested HydSystemState
        public const int EnginePumpLeftSwitch = 4;  // EnginePumpLeftSwitch: varint (bool)
        public const int EnginePumpLeftOutput = 5;  // EnginePumpLeftOutput: varint (bool)
        public const int EnginePumpRightSwitch = 6; // EnginePumpRightSwitch: varint (bool)
        public const int EnginePumpRightOutput = 7; // EnginePumpRightOutput: varint (bool)
        public const int ElecPumpBlue = 8;          // ElecPumpBlue: nested HydElecPumpState
        public const int ElecPumpYellow = 9;        // ElecPumpYellow: nested HydElecPumpState
        public const int ShutoffLeft = 10;          // ShutoffLeft: varint (bool)
        public const int ShutoffRight = 11;         // ShutoffRight: varint (bool)
        public const int RatDeployed = 12;          // RatDeployed: varint (bool)
    }

    /// <summary>HydSystemState, one hydraulic system (<see cref="HydraulicState.Green"/>, <see cref="HydraulicState.Blue"/> or <see cref="HydraulicState.Yellow"/>).</summary>
    public static partial class HydSystemState
    {
        public const int Pressure = 1;              // Pressure: fixed64 (double)
        public const int Quantity = 2;              // Quantity: fixed64 (double)
        public const int LowAirPressure = 6;        // LowAirPressure: varint (bool)
        public const int ReservoirOverheat = 7;     // ReservoirOverheat: varint (bool)
    }

    /// <summary>HydElecPumpState, an electric hydraulic pump (<see cref="HydraulicState.ElecPumpBlue"/> or <see cref="HydraulicState.ElecPumpYellow"/>).</summary>
    public static partial class HydElecPumpState
    {
        public const int Switch = 1;                // Switch: varint (bool)
        public const int Output = 2;                // Output: varint (bool)
        public const int Overheat = 3;              // Overheat: varint (bool)
    }

    /// <summary>
    /// The values of <see cref="Remote.PtuState"/> the app knows, each named for what the SD showed, as measured in the sim
    /// (Fenix 3.0.0.7613, 2026-11-03). Any other value reads as a state the app does not know.
    /// </summary>
    public static class PtuStates
    {
        public const int Off = -1;                  // measured: pushbutton off, both engines running; AUTO, parking brake set, one engine master on (the inhibit): no transfer
        public const int Idle = 0;                  // measured: both engines running, nothing to transfer (the Fenix leaves the field out)
        public const int YellowToGreen = 1;         // measured: engines off, yellow electric pump on (green 0 to 2650 PSI); engine 1 starting, engine 2 running
        public const int GreenToYellow = 2;         // measured: about 50 s after engine 1's master went off, engine 2 already off (yellow 0 PSI, green falling)
    }

    /// <summary>
    /// The rest of <see cref="FuelState"/>, the fuel message of a push (<see cref="Remote.FuelState"/>): the pumps, the
    /// valves, the quantities and the temperatures. <see cref="FuelState.Used"/> is declared beside the class, in
    /// FenixChannelFields.cs, because the SD pages read it too.
    /// </summary>
    public static partial class FuelState
    {
        public const int JetPumps = 1;              // JetPumps: nested JetPumpData
        public const int LeftWing1 = 2;             // LeftWing1: nested FuelPumpState
        public const int LeftWing2 = 3;             // LeftWing2: nested FuelPumpState
        public const int Center1 = 4;               // Center1: nested FuelPumpState
        public const int Center2 = 5;               // Center2: nested FuelPumpState
        public const int RightWing1 = 6;            // RightWing1: nested FuelPumpState
        public const int RightWing2 = 7;            // RightWing2: nested FuelPumpState
        public const int Advisory = 8;              // Advisory: nested
        public const int ValveAmberEngine1 = 9;     // ValveAmberEngine1: varint (bool)
        public const int ValveAmberEngine2 = 10;    // ValveAmberEngine2: varint (bool)
        public const int ValveState = 11;           // ValveState: nested FuelValveStates
        public const int Quantity = 12;             // Quantity: nested FuelQuantityData
        public const int Temperature = 14;          // Temperature: nested FuelTemperatureData
        public const int ActPump = 15;              // ActPump: nested
    }

    /// <summary>JetPumpData, the fuel jet pumps (<see cref="FuelState.JetPumps"/>).</summary>
    public static partial class JetPumpData
    {
        // Fields 1, 2 and 5 to 8 checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata
        // tool ([FNX-23]): the center transfer valves' flow and the amber each is drawn in.
        public const int LeftFlow = 1;              // LeftFlow: varint (bool)
        public const int RightFlow = 2;             // RightFlow: varint (bool)
        public const int LeftValveOpen = 3;         // LeftValveOpen: varint (bool)
        public const int RightValveOpen = 4;        // RightValveOpen: varint (bool)
        public const int LeftValveAmber = 5;        // LeftValveAmber: varint (bool)
        public const int RightValveAmber = 6;       // RightValveAmber: varint (bool)
        public const int LeftFlowAmber = 7;         // LeftFlowAmber: varint (bool)
        public const int RightFlowAmber = 8;        // RightFlowAmber: varint (bool)
    }

    /// <summary>
    /// ActPumpData, the A321's ACT pump (<see cref="FuelState.ActPump"/>, 134.15). Field 3 checked against the installed
    /// Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata tool ([FNX-23]).
    /// </summary>
    public static partial class ActPumpData
    {
        public const int Amber = 3;                 // Amber: varint (bool), the pump drawn amber
    }

    /// <summary>FuelPumpState, one fuel pump (<see cref="FuelState.LeftWing1"/> to <see cref="FuelState.RightWing2"/>).</summary>
    public static partial class FuelPumpState
    {
        public const int Switch = 1;                // Switch: varint (bool)
        public const int Amber = 2;                 // Amber: varint (bool), the pump drawn amber (checked against the installed Fenix 3.0.0.7613 on 2026-11-03 with the team's metadata tool, [FNX-23])
        public const int LowPressure = 3;           // LowPressure: varint (bool)
    }

    /// <summary>
    /// FuelValveStates, the fuel valves (<see cref="FuelState.ValveState"/>). Each is a <see cref="ValveStateEnum"/>;
    /// Open is left out on the wire, so a valve with no field is open.
    /// </summary>
    public static partial class FuelValveStates
    {
        public const int Engine1 = 1;               // Engine1: varint (ValveStateEnum)
        public const int Engine2 = 2;               // Engine2: varint (ValveStateEnum)
        public const int Apu = 3;                   // Apu: varint (ValveStateEnum)
        public const int Crossfeed = 4;             // Crossfeed: varint (ValveStateEnum)
        public const int LeftTransfer1 = 5;         // LeftTransfer1: varint (ValveStateEnum)
        public const int LeftTransfer2 = 6;         // LeftTransfer2: varint (ValveStateEnum)
        public const int RightTransfer1 = 7;        // RightTransfer1: varint (ValveStateEnum)
        public const int RightTransfer2 = 8;        // RightTransfer2: varint (ValveStateEnum)
    }

    /// <summary>FuelQuantityData, the fuel quantities (<see cref="FuelState.Quantity"/>).</summary>
    public static partial class FuelQuantityData
    {
        public const int LeftOuter = 1;             // LeftOuter: fixed64 (double)
        public const int LeftInner = 2;             // LeftInner: fixed64 (double)
        public const int Center = 3;                // Center: fixed64 (double)
        public const int RightInner = 4;            // RightInner: fixed64 (double)
        public const int RightOuter = 5;            // RightOuter: fixed64 (double)
        public const int Total = 6;                 // Total: fixed64 (double)
        public const int Act1 = 7;                  // Act1: fixed64 (double)
        public const int Act2 = 8;                  // Act2: fixed64 (double)
    }

    /// <summary>FuelTemperatureData, the fuel temperatures (<see cref="FuelState.Temperature"/>).</summary>
    public static partial class FuelTemperatureData
    {
        public const int LeftOuter = 1;             // LeftOuter: fixed64 (double)
        public const int LeftInner = 2;             // LeftInner: fixed64 (double)
        public const int RightInner = 3;            // RightInner: fixed64 (double)
        public const int RightOuter = 4;            // RightOuter: fixed64 (double)
    }

    /// <summary>
    /// The elements of <see cref="Remote.EcamBottom"/>, the SD's bottom line, by position (the contract names none): the
    /// numbers are indexes into the repeated field, from 0, and each element is a NullWrapped string. Positions 2, 3, 7
    /// and 8 are unlabelled and not read. A bottom line of another length than <see cref="EcamBottom.Count"/> is not the
    /// shape the app was checked against.
    /// </summary>
    public static class EcamBottom
    {
        public const int Tat = 0;                   // TAT, the total air temperature
        public const int Sat = 1;                   // SAT, the static air temperature
        public const int UtcHour = 4;               // the UTC hour
        public const int UtcMinute = 5;             // the UTC minute
        public const int GrossWeight = 6;           // GW, the gross weight, in the weight unit of Remote.IsKg
        public const int Count = 9;                 // the number of elements the bottom line has: the shape the app was checked against
    }
}
