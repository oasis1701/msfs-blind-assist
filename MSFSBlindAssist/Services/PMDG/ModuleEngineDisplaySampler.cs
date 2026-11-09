using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Reads one engine display sample from the 737 module's memory through a derived layout. Every read
/// takes the five object pointers afresh and range-checks each object against the module's committed
/// memory (WASM-4), so an object PMDG moved or freed fails the read instead of being misread. Owns its
/// memory source (a lease on the shared simulator handle).
/// </summary>
public sealed class ModuleEngineDisplaySampler : IEngineDisplaySampler
{
    private const int Engines = 2, Tanks = 3, Systems = 2, Brakes = 4;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Pmdg737EngineDisplayLayout Layout { get; }

    public ModuleEngineDisplaySampler(ModuleMemory memory, IDisposable ownedSource, Pmdg737EngineDisplayLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
    }

    public bool TryRead(out EngineDisplaySample sample)
    {
        sample = null!;
        var l = Layout;
        if (!Pointer(l.Engine, out uint engine) || !Pointer(l.Glass, out uint glass)
            || !Pointer(l.Pages.Efis, out uint efis) || !Pointer(l.Options.Options, out uint options)
            || !Pointer(l.Aircraft.Aircraft, out uint aircraft))
            return false;
        try
        {
            var g = l.Gauges;
            var a = l.Alerts;
            var engines = new EngineValues[Engines];
            for (int e = 0; e < Engines; e++)
            {
                engines[e] = new EngineValues(
                    N1: D(engine, g.N1, e), N1Reference: D(engine, g.N1Reference, e), Egt: D(engine, g.Egt, e), N2: D(engine, g.N2, e),
                    FuelFlow: D(engine, g.FuelFlow, e), FuelUsed: D(engine, g.FuelUsed, e),
                    OilPressure: D(engine, g.OilPressure, e), OilPressureAmber: D(engine, g.OilPressureAmber, e),
                    OilTemperature: D(engine, g.OilTemperature, e), OilQuantity: D(engine, g.OilQuantity, e),
                    Vibration: D(engine, g.Vibration, e), Running: B(engine, g.Running, e),
                    StartValve: I(engine, a.StartValve, e), OilFilterBypass: I(engine, a.OilFilterBypass, e),
                    LowOilPressure: I(engine, a.LowOilPressure, e),
                    ReverserShown: B(engine, a.ReverserShown, e), ReverserSecond: B(engine, a.ReverserSecond, e),
                    TaiOn: B(engine, a.TaiOn, e), TaiAmber: B(engine, a.TaiAmber, e),
                    EngineFail: B(engine, a.EngineFail, e), CrossBleedStart: B(engine, a.CrossBleedStart, e),
                    ManualN1: D(efis, l.Pages.ManualN1, e),
                    N1RedLatch: B(engine, g.N1RedLatch, e), EgtRedLatch: B(engine, g.EgtRedLatch, e), N2RedLatch: B(engine, g.N2RedLatch, e));
            }

            var p = l.Pages;
            var indices = new int[p.UnitCount];
            int upper = 0, lower = 0;
            for (int k = 0; k < p.UnitCount; k++)
            {
                uint unit = efis + (uint)(k * p.UnitStride);
                indices[k] = Int(unit + (uint)p.IndexOffset);
                int page = Int(unit + (uint)p.PageOffset);
                if (k == p.UpperUnit) upper = page;
                if (k == p.LowerUnit) lower = page;
            }

            var s = l.Sys;
            var sys = new SysValues(
                HydQuantityA: D(glass, s.HydQuantity, 0), HydQuantityB: D(glass, s.HydQuantity, 1),
                HydPressureA: D(glass, s.HydPressure, 0), HydPressureB: D(glass, s.HydPressure, 1),
                RefillA: B(glass, s.Refill, 0), RefillB: B(glass, s.Refill, 1),
                AileronLeft: D(glass, s.AileronLeft, 0), SpoilerLeft: D(glass, s.SpoilerLeft, 0), Elevator: D(glass, s.Elevator, 0),
                SpoilerRight: D(glass, s.SpoilerRight, 0), AileronRight: D(glass, s.AileronRight, 0), Rudder: D(glass, s.Rudder, 0),
                BrakeTemperatures: Enumerable.Range(0, Brakes).Select(k => D(glass, s.BrakeTemperature, k)).ToArray(),
                BrakeStatus: Enumerable.Range(0, Brakes).Select(k => I(glass, s.BrakeStatus, k)).ToArray());

            var o = l.Options;
            bool percent = B(options, o.OilInPercent, 0), litres = B(options, o.OilInLitres, 0);
            var (unitOfOil, multiplier) = percent ? (OilQuantityUnit.Percent, o.PercentMultiplier)
                : litres ? (OilQuantityUnit.Litres, o.LitresMultiplier)
                : (OilQuantityUnit.Quarts, o.QuartsMultiplier);

            sample = new EngineDisplaySample(
                Tat: D(engine, g.Tat, 0),
                Engines: engines,
                N1RedLine: D(engine, g.N1RedLine, 0), EgtRedLine: D(engine, g.EgtRedLine, 0), EgtMaxContinuous: D(engine, g.EgtMaxContinuous, 0),
                N2Max: D(engine, g.N2Max, 0), OilPressureRed: D(engine, g.OilPressureRed, 0),
                OilTemperatureRed: D(engine, g.OilTemperatureRed, 0), OilTemperatureAmber: D(engine, g.OilTemperatureAmber, 0),
                OilQuantityLow: D(engine, g.OilQuantityLow, 0), VibrationLimit: D(engine, g.VibrationLimit, 0),
                ThrustMode: I(engine, g.ThrustMode, 0), AssumedTempShown: B(engine, g.AssumedTempShown, 0),
                AssumedTemperature: I(engine, g.AssumedTemperature, 0), N1SetSelector: I(engine, g.N1SetSelector, 0),
                ShowFuelUsed: B(engine, g.ShowFuelUsed, 0), AutothrottleLimit: B(engine, a.AutothrottleLimit, 0),
                AutolandAdvisory: I(engine, a.AutolandAdvisory, 0),
                AtcMemo: B(glass, a.AtcMemo, 0), CommMemo: B(glass, a.CommMemo, 0),
                FuelQuantity: Enumerable.Range(0, Tanks).Select(k => D(engine, a.FuelQuantity, k)).ToArray(),
                FuelWarning: Enumerable.Range(0, Tanks).Select(k => I(engine, a.FuelWarning, k)).ToArray(),
                UpperPage: upper, LowerPage: lower, UnitIndices: indices,
                Sys: sys,
                FuelInKg: B(options, o.FuelInKg, 0), OilUnit: unitOfOil, OilMultiplier: multiplier,
                OilLowIndication: B(options, o.OilLowIndication, 0), DerateOption: B(options, g.ThrustRules.DerateOption, 0),
                Initialised: B(aircraft, l.Aircraft.Initialised, 0));
            return true;
        }
        catch (ReadFailed)
        {
            return false;
        }
    }

    public void Dispose() => _ownedSource.Dispose();

    /// <summary>Thrown inside <see cref="TryRead"/> when one field cannot be read; it fails the whole sample.</summary>
    private sealed class ReadFailed : Exception;

    private bool Pointer(GlassObject o, out uint pointer)
    {
        pointer = 0;
        if (!_memory.TryReadInt32(o.Global, out int p) || p == 0) return false;
        pointer = (uint)p;
        return _memory.IsInsideModule(pointer, o.Size);
    }

    private static uint At(uint owner, GlassField f, int element) => owner + (uint)(f.Offset + f.Stride * element);

    private double D(uint owner, GlassField f, int element) =>
        _memory.TryReadDouble(At(owner, f, element), out double v) ? v : throw new ReadFailed();

    private int I(uint owner, GlassField f, int element) => Int(At(owner, f, element));

    private int Int(uint at) => _memory.TryReadInt32(at, out int v) ? v : throw new ReadFailed();

    private bool B(uint owner, GlassField f, int element) =>
        _memory.TryReadByte(At(owner, f, element), out byte v) ? v != 0 : throw new ReadFailed();
}
