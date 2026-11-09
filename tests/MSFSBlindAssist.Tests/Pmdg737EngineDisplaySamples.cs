using MSFSBlindAssist.Services.PMDG;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Engine display samples for the reader's tests: the live idle reading of the owner's engine start
/// (2026-10-23/24, 737-800), both engines running, upper display on the primary page and lower on the
/// secondary, fuel in pounds, oil quantity in percent. Tests change what they need with <c>with</c>.
/// </summary>
internal static class EngineDisplaySampleFactory
{
    public static EngineValues IdleEngine(int engine) => new(
        N1: 21.0, N1Reference: 95.5, Egt: engine == 1 ? 466 : 466, N2: 60.6, FuelFlow: 674, FuelUsed: 0,
        OilPressure: engine == 1 ? 31 : 32, OilPressureAmber: 20, OilTemperature: 76, OilQuantity: 0.8,
        Vibration: engine == 1 ? 0.9 : 0.5, Running: true,
        StartValve: 0, OilFilterBypass: 0, LowOilPressure: 0,
        ReverserShown: false, ReverserSecond: false, TaiOn: false, TaiAmber: false, EngineFail: false, CrossBleedStart: false,
        ManualN1: 95.5, N1RedLatch: false, EgtRedLatch: false, N2RedLatch: false);

    public static EngineDisplaySample Idle() => new(
        Tat: 19,
        Engines: new[] { IdleEngine(1), IdleEngine(2) },
        N1RedLine: 104, EgtRedLine: 950, EgtMaxContinuous: 925, N2Max: 105,
        OilPressureRed: 13, OilTemperatureRed: 160, OilTemperatureAmber: 140, OilQuantityLow: 0.207792, VibrationLimit: 4,
        ThrustMode: 0, AssumedTempShown: false, AssumedTemperature: -1000000, N1SetSelector: 2,
        ShowFuelUsed: false, AutothrottleLimit: false, AutolandAdvisory: 2, AtcMemo: false, CommMemo: false,
        FuelQuantity: new[] { 7782.0, 8626.0, 8628.0 },
        FuelWarning: new[] { 0, 0, 0 },
        UpperPage: 5, LowerPage: 6, UnitIndices: new[] { 0, 1, 2, 3, 4, 5 },
        Sys: new SysValues(92, 92, 3000, 3000, false, false, 0, 0, 0, 0, 0, 0, new[] { 0.0, 0.0, 0.0, 0.0 }, new[] { 0, 0, 0, 0 }),
        FuelInKg: false, OilUnit: OilQuantityUnit.Percent, OilMultiplier: 100,
        OilLowIndication: true, DerateOption: false, Initialised: true);
}
