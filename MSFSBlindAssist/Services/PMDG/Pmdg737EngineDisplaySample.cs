namespace MSFSBlindAssist.Services.PMDG;

/// <summary>One engine's values on the engine displays: readouts (fuel flow and used in pounds per hour
/// and pounds, oil quantity as PMDG's stored fraction), the crew alert states (0 off, 1 flashing,
/// 2 steady), the gauge message flags, its manual N1, and the exceedance latches that keep a readout red.</summary>
public sealed record EngineValues(
    double N1,
    double N1Reference,
    double Egt,
    double N2,
    double FuelFlow,
    double FuelUsed,
    double OilPressure,
    double OilPressureAmber,
    double OilTemperature,
    double OilQuantity,
    double Vibration,
    bool Running,
    int StartValve,
    int OilFilterBypass,
    int LowOilPressure,
    bool ReverserShown,
    bool ReverserSecond,
    bool TaiOn,
    bool TaiAmber,
    bool EngineFail,
    bool CrossBleedStart,
    double ManualN1,
    bool N1RedLatch,
    bool EgtRedLatch,
    bool N2RedLatch);

/// <summary>The SYS page: hydraulic systems A and B, the flight control positions, four brakes.</summary>
public sealed record SysValues(
    double HydQuantityA,
    double HydQuantityB,
    double HydPressureA,
    double HydPressureB,
    bool RefillA,
    bool RefillB,
    double AileronLeft,
    double SpoilerLeft,
    double Elevator,
    double SpoilerRight,
    double AileronRight,
    double Rudder,
    IReadOnlyList<double> BrakeTemperatures,
    IReadOnlyList<int> BrakeStatus);

/// <summary>
/// One reading of the engine displays from module memory: both engines, the limits PMDG compares them
/// with, the thrust mode inputs, the alert column, the three tanks (center, 1, 2; pounds) and their
/// warnings, the page each display unit shows, the SYS page and the options. Nothing here is trusted
/// until it passes the gate (<see cref="Pmdg737EngineDisplayCrossCheck"/>, WASM-10), and none of it is a
/// reading until <see cref="Initialised"/>: before PMDG's aircraft initialises, the objects hold their
/// constructors' values while the displays are dark.
/// </summary>
public sealed record EngineDisplaySample(
    double Tat,
    IReadOnlyList<EngineValues> Engines,
    double N1RedLine,
    double EgtRedLine,
    double EgtMaxContinuous,
    double N2Max,
    double OilPressureRed,
    double OilTemperatureRed,
    double OilTemperatureAmber,
    double OilQuantityLow,
    double VibrationLimit,
    int ThrustMode,
    bool AssumedTempShown,
    int AssumedTemperature,
    int N1SetSelector,
    bool ShowFuelUsed,
    bool AutothrottleLimit,
    int AutolandAdvisory,
    bool AtcMemo,
    bool CommMemo,
    IReadOnlyList<double> FuelQuantity,
    IReadOnlyList<int> FuelWarning,
    int UpperPage,
    int LowerPage,
    IReadOnlyList<int> UnitIndices,
    SysValues Sys,
    bool FuelInKg,
    OilQuantityUnit OilUnit,
    double OilMultiplier,
    bool OilLowIndication,
    bool DerateOption,
    bool Initialised);

/// <summary>Reads engine display samples through one layout from one located module memory.</summary>
public interface IEngineDisplaySampler : IDisposable
{
    Pmdg737EngineDisplayLayout Layout { get; }

    /// <summary>False when a memory read fails or an object is no longer inside the module.</summary>
    bool TryRead(out EngineDisplaySample sample);
}
