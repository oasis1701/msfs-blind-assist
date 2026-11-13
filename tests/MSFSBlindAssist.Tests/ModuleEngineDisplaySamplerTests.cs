// The engine display sampler reads one sample through a derived layout: the four object pointers again
// on every read, each object range-checked against the module's committed memory (WASM-4), then every
// field per engine, tank, unit, hydraulic system and brake.

using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class ModuleEngineDisplaySamplerTests
{
    private const ulong B = 0x10000000;
    private const uint Engine = 0x10000, Glass = 0x20000, Efis = 0x30000, Options = 0x60000, Aircraft = 0x70000;

    private static readonly Pmdg737EngineDisplayLayout Layout = GoldenLayouts.EngineDisplays;

    private static void D(byte[] m, uint at, double v) => BitConverter.TryWriteBytes(m.AsSpan((int)at), v);
    private static void I(byte[] m, uint at, int v) => BitConverter.TryWriteBytes(m.AsSpan((int)at), v);

    /// <summary>The module's memory with the four objects and the live idle values (engine 2 slightly different).</summary>
    private static byte[] Memory()
    {
        var m = new byte[0x280000];
        I(m, Layout.Engine.Global, (int)Engine);
        I(m, Layout.Glass.Global, (int)Glass);
        I(m, Layout.Pages.Efis.Global, (int)Efis);
        I(m, Layout.Options.Options.Global, (int)Options);
        I(m, Layout.Aircraft.Aircraft.Global, (int)Aircraft);
        m[Aircraft] = 1;

        D(m, Engine + 64, 19);
        for (uint e = 0; e < 2; e++)
        {
            D(m, Engine + 136 + 32 * e, 21.0 + e);
            D(m, Engine + 320 + 32 * e, 95.5);
            D(m, Engine + 640 + 32 * e, 466 + e);
            D(m, Engine + 896 + 32 * e, 60.6);
            D(m, Engine + 1056 + 32 * e, 674);
            D(m, Engine + 1120 + 32 * e, 1230);
            D(m, Engine + 1208 + 32 * e, 31 + e);
            D(m, Engine + 1352 + 32 * e, 20);
            D(m, Engine + 1416 + 32 * e, 76);
            D(m, Engine + 1568 + 32 * e, 0.8);
            D(m, Engine + 1776 + 32 * e, 0.9 - 0.4 * e);
            m[Engine + 1296 + 12 * e] = 1;
            I(m, Engine + 2616 + 16 * e, 0);
            I(m, Engine + 2680 + 16 * e, 2);
            m[Engine + 612 + 12 * e] = (byte)e;
            m[Engine + 848 + 12 * e] = (byte)e;
            D(m, Efis + 127320 + 8 * e, 95.5);
        }
        D(m, Engine + 200, 104); D(m, Engine + 728, 950); D(m, Engine + 760, 925); D(m, Engine + 960, 105);
        D(m, Engine + 1320, 13); D(m, Engine + 1504, 160); D(m, Engine + 1536, 140); D(m, Engine + 1632, 0.2078); D(m, Engine + 1840, 4);
        I(m, Engine + 304, 0); m[Engine + 108] = 0; I(m, Engine + 120, -1000000); I(m, Engine + 548, 2);
        I(m, Engine + 2176, 2);
        D(m, Engine + 1872, 7812.977); D(m, Engine + 1904, 8620.771); D(m, Engine + 1936, 8628.594);
        I(m, Engine + 2128, 2);

        for (uint k = 0; k < 6; k++)
        {
            I(m, Efis + 608 * k + 568, (int)k);
            I(m, Efis + 608 * k + 564, k == 2 ? 5 : k == 3 ? 6 : 1);
        }
        m[Glass + 9144] = 1;
        for (uint s = 0; s < 2; s++)
        {
            D(m, Glass + 8480 + 32 * s, 92);
            D(m, Glass + 8544 + 32 * s, 3000);
        }
        D(m, Glass + 8920, -1);
        for (uint k = 0; k < 4; k++) D(m, Glass + 8952 + 32 * k, 0.5 * k);
        m[Options + 44] = 1;
        m[Options + 37] = 1;
        return m;
    }

    private static (ModuleEngineDisplaySampler Sampler, FakeProcessMemory Source) Sampler(byte[] bytes)
    {
        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)bytes.Length, true, true, true), bytes);
        return (new ModuleEngineDisplaySampler(new ModuleMemory(src, B), src, Layout), src);
    }

    [Fact]
    public void Reads_every_field_through_the_layout()
    {
        var (sampler, _) = Sampler(Memory());
        Assert.True(sampler.TryRead(out var s));

        Assert.Equal(19, s.Tat);
        Assert.Equal(2, s.Engines.Count);
        var e1 = s.Engines[0];
        var e2 = s.Engines[1];
        Assert.Equal((21.0, 22.0), (e1.N1, e2.N1));
        Assert.Equal(95.5, e1.N1Reference);
        Assert.Equal((466.0, 467.0), (e1.Egt, e2.Egt));
        Assert.Equal(60.6, e1.N2);
        Assert.Equal((674.0, 1230.0), (e1.FuelFlow, e1.FuelUsed));
        Assert.Equal((31.0, 32.0, 20.0), (e1.OilPressure, e2.OilPressure, e1.OilPressureAmber));
        Assert.Equal((76.0, 0.8), (e1.OilTemperature, e1.OilQuantity));
        Assert.Equal(0.5, e2.Vibration, 6);
        Assert.True(e1.Running);
        Assert.Equal((0, 2), (e1.StartValve, e1.LowOilPressure));
        Assert.Equal((false, true), (e1.TaiAmber, e2.TaiAmber));
        Assert.Equal((false, true), (e1.EgtRedLatch, e2.EgtRedLatch));
        Assert.False(e2.N1RedLatch || e2.N2RedLatch);
        Assert.Equal(95.5, e2.ManualN1);

        Assert.Equal((104.0, 950.0, 925.0, 105.0), (s.N1RedLine, s.EgtRedLine, s.EgtMaxContinuous, s.N2Max));
        Assert.Equal((13.0, 160.0, 140.0, 4.0), (s.OilPressureRed, s.OilTemperatureRed, s.OilTemperatureAmber, s.VibrationLimit));
        Assert.Equal(0.2078, s.OilQuantityLow);
        Assert.Equal((0, false, -1000000, 2), (s.ThrustMode, s.AssumedTempShown, s.AssumedTemperature, s.N1SetSelector));
        Assert.Equal(2, s.AutolandAdvisory);
        Assert.Equal(new[] { 7812.977, 8620.771, 8628.594 }, s.FuelQuantity);
        Assert.Equal(new[] { 2, 0, 0 }, s.FuelWarning);
        Assert.Equal((5, 6), (s.UpperPage, s.LowerPage));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, s.UnitIndices);
        Assert.True(s.AtcMemo);
        Assert.False(s.CommMemo);
        Assert.Equal((92.0, 3000.0), (s.Sys.HydQuantityA, s.Sys.HydPressureB));
        Assert.Equal(-1, s.Sys.Rudder);
        Assert.Equal(new[] { 0, 0.5, 1, 1.5 }, s.Sys.BrakeTemperatures);
        Assert.False(s.FuelInKg);
        Assert.Equal((OilQuantityUnit.Percent, 100.0), (s.OilUnit, s.OilMultiplier));
        Assert.Equal((true, false, true), (s.OilLowIndication, s.DerateOption, s.Initialised));
    }

    [Fact]
    public void An_aircraft_not_yet_initialised_reads_as_such()
    {
        // Before PMDG's aircraft initialises, PMDG updates none of the display objects: they hold their
        // starting values (live, 2026-10-24), which the text and the monitor must not take as real.
        var m = Memory();
        m[Aircraft] = 0;
        Assert.True(Sampler(m).Sampler.TryRead(out var s));
        Assert.False(s.Initialised);
        I(m, Layout.Aircraft.Aircraft.Global, 0);
        Assert.False(Sampler(m).Sampler.TryRead(out _));
    }

    [Fact]
    public void A_pointer_outside_the_module_fails_the_read()
    {
        var m = Memory();
        I(m, Layout.Glass.Global, 0x27FF00); // its object would run past the end
        Assert.False(Sampler(m).Sampler.TryRead(out _));
        I(m, Layout.Glass.Global, 0);
        Assert.False(Sampler(m).Sampler.TryRead(out _));
    }

    [Fact]
    public void An_object_committed_after_start_is_read()
    {
        // The module grows: the EFIS object lies in a region committed only after the sampler was made.
        var m = Memory();
        var low = m.AsSpan(0, (int)Efis).ToArray();
        var high = m.AsSpan((int)Efis).ToArray();
        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)low.Length, true, true, true), low);
        var sampler = new ModuleEngineDisplaySampler(new ModuleMemory(src, B), src, Layout);
        Assert.False(sampler.TryRead(out _));
        src.Add(new MemoryRegion(B + Efis, B, (ulong)high.Length, true, true, true), high);
        Assert.True(sampler.TryRead(out var s));
        Assert.Equal(5, s.UpperPage);
    }

    [Fact]
    public void Disposing_the_sampler_releases_its_source()
    {
        var owned = new CountingDisposable();
        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, 0x1000, true, true, true), new byte[0x1000]);
        new ModuleEngineDisplaySampler(new ModuleMemory(src, B), owned, Layout).Dispose();
        Assert.Equal(1, owned.Count);
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int Count { get; private set; }
        public void Dispose() => Count++;
    }
}
