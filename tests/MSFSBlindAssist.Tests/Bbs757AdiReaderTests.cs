using System.Buffers.Binary;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The 757 ADI sampler (plan 27, Task 4): it finds the Captain's pfd in the gauge manager by its vptr, follows
/// the layout to the source selector's irs, adc and ra, the mcp, the afds and the fms, and returns each part as drawn.
/// The memory is the evidence's live reading at the gate (docs/b757/evidence/displays-adi.md: the IRS aligned, the ILS
/// tuned to 110.30 and received on the ramp, the Captain's F/D on, PERF INIT done), placed through the sample layout of
/// <see cref="Bbs757AdiLayoutTests"/>; the draw rules that the gate cannot show are tested on
/// <see cref="Bbs757AdiReader.Decode"/> alone.</summary>
public class Bbs757AdiReaderTests
{
    internal const ulong Base = 0x20000000;
    private const int Size = 0x200000;
    internal const uint Pfd = 0x2000;
    private const uint FirstOfficerPfd = 0x3000, Source = 0x4000, Irs = 0x10000, Adc = 0x20000, Ra = 0x30000,
        Mcp = 0x40000, Afds = 0x50000, Fms = 0x60000;

    private const double DegreesPerRadian = 57.2958;

    private static readonly Bbs757AdiLayout Layout = Bbs757AdiLayoutTests.Sample();
    private static readonly Bbs757AdiAttitude A = Layout.Attitude!;
    private static readonly Bbs757AdiSpeed S = Layout.Speed!;
    private static readonly Bbs757AdiIls I = Layout.Ils!;
    private static readonly Bbs757AdiRadioAltitude R = Layout.RadioAltitude!;
    private static readonly Bbs757AdiFlightDirector F = Layout.FlightDirector!;
    private static readonly Bbs757AdiTcas T = Layout.Tcas!;
    private static readonly Bbs757AdiReferenceSpeeds V = Layout.ReferenceSpeeds!;

    /// <summary>The pfd's ILS block and the adc's data block.</summary>
    private static readonly uint G = Pfd + (uint)Layout.IlsBlock, D = Adc + (uint)Layout.AdcBlock;

    private static void Put(byte[] b, uint at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan((int)at), v);
    private static void PutL(byte[] b, uint at, long v) => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan((int)at), v);
    private static void PutD(byte[] b, uint at, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan((int)at), v);
    private static uint At(uint obj, int offset) => obj + (uint)offset;

    /// <summary>The gate: powered, the attitude valid, the adc and the ra powered, airspeed 2.5 kt, the MCP at 200, V1
    /// not set (INOP), the AFDS timer stopped at 0, the flap speed 201 (not drawn: the timer), the localizer and the
    /// glideslope pinned right and up on the normal scale, radio altitude 10.56 − 16.93, DH 200, the F/D on with the
    /// take-off pitch command, no TCAS advisory, VREF 30 121 and VREF 25 123.</summary>
    internal static byte[] Gate(Action<byte[]>? tweak = null)
    {
        var b = new byte[Size];
        // The manager: the Captain's pfd first, the first officer's after it (the same class).
        Put(b, Layout.ManagerBase + 16, (int)Pfd);
        Put(b, Layout.ManagerBase + 24, (int)FirstOfficerPfd);
        Put(b, Pfd, (int)Layout.PfdVptr);
        Put(b, FirstOfficerPfd, (int)Layout.PfdVptr);
        b[At(Pfd, Layout.Powered)] = 1;
        Put(b, At(Pfd, Layout.PfdToSource), (int)Source);
        Put(b, At(Pfd, Layout.PfdToMcp), (int)Mcp);
        Put(b, At(Pfd, Layout.PfdToAfds), (int)Afds);
        Put(b, At(Pfd, Layout.PfdToFms), (int)Fms);
        Put(b, At(Source, Layout.SourceToIrs), (int)Irs);
        Put(b, At(Source, Layout.SourceToAdc), (int)Adc);
        Put(b, At(Source, Layout.SourceToRa), (int)Ra);

        // The irs: in NAV, its attitude valid.
        b[At(Irs, A.IrsAttitude)] = 1;
        Put(b, At(Irs, A.IrsMode), A.IrsModeNav);
        Put(b, At(Irs, A.IrsState), A.IrsStateValid);

        // The adc, powered: airspeed, altitude, vertical speed.
        b[At(Adc, Layout.DevicePowered)] = 1;
        PutD(b, At(D, S.AdcAirspeed), 2.5);
        PutD(b, At(D, S.AdcAltitude), 430);
        PutD(b, At(D, R.AdcVerticalSpeed), 0);

        // The speed tape's other sources.
        PutD(b, At(Mcp, S.McpSpeed), 200);
        Put(b, At(Afds, S.AfdsPitchMode), 1);   // TO
        b[At(Fms, S.FmsV1Inop)] = 1;
        b[At(Afds, S.AfdsTimer + S.TimerStopped)] = 1;
        PutD(b, S.Clock, 5000);
        PutD(b, At(Afds, S.AfdsFlapSpeed), 201);

        // The ILS block: 110.30 received on the ramp.
        PutD(b, At(G, I.LocPointer), 1.0);
        PutD(b, At(G, I.LocDots), 2.2);
        PutD(b, At(G, I.GsPointer), 2.2);
        b[At(G, I.LocScale)] = b[At(G, I.GsScale)] = b[At(G, I.LocValid)] = b[At(G, I.GsValid)] = 1;
        Put(b, At(Afds, I.AfdsRollMode), 8);   // TO

        // The ra, powered, and the decision height.
        b[At(Ra, Layout.DevicePowered)] = 1;
        PutD(b, At(Ra, R.RaAltitude), 10.56);
        Put(b, At(G, R.DhValue), 200);
        b[At(G, R.DhShown)] = 1;

        // The Captain's F/D on, the take-off pitch command.
        b[At(Afds, F.FdCaptain)] = 1;
        PutD(b, At(Afds, F.FdPitch), 7.941);

        // The FMC's reference speeds.
        PutD(b, At(Fms, V.FmsVref30), 121);
        PutD(b, At(Fms, V.FmsVref25), 123);

        tweak?.Invoke(b);
        return b;
    }

    /// <summary>The simulator's four values, given in degrees (the simulator's signs: pitch positive nose down, bank
    /// positive left), knots and Mach; a null one is not given at all.</summary>
    internal static Bbs757SimValues Sim(double? pitch = 0.53, double? bank = -0.40, double? groundSpeed = 0, double? mach = 0.004)
    {
        var values = new Dictionary<string, double>();
        if (pitch is { } p) values[Bbs757AdiReader.PitchKey] = p / DegreesPerRadian;
        if (bank is { } k) values[Bbs757AdiReader.BankKey] = k / DegreesPerRadian;
        if (groundSpeed is { } g) values[Bbs757AdiReader.GroundSpeedKey] = g;
        if (mach is { } m) values[Bbs757AdiReader.MachKey] = m;
        return new Bbs757SimValues(values);
    }

    private static Bbs757AdiReader Reader(byte[] bytes, Bbs757AdiLayout? layout = null, Action<string>? log = null)
    {
        var src = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        return new Bbs757AdiReader(new ModuleMemory(src, Base), src, layout ?? Layout, log ?? (_ => { }));
    }

    private static Bbs757AdiReading Read(byte[] bytes, Bbs757AdiLayout? layout = null, Bbs757SimValues? sim = null)
    {
        using var r = Reader(bytes, layout);
        Assert.True(r.TryRead(sim ?? Sim(), out var reading));
        return reading;
    }

    private static void Lost(byte[] bytes, Bbs757AdiLayout? layout = null)
    {
        using var r = Reader(bytes, layout);
        Assert.False(r.TryRead(Sim(), out _));
    }

    // ---- through memory ------------------------------------------------------------------------------------------

    [Fact]
    public void Reads_each_part_as_drawn()
    {
        var r = Read(Gate());

        Assert.True(r.Powered);
        Assert.False(r.Corrupt);

        var attitude = r.Attitude!;
        Assert.Equal(Bbs757AdiAttitudeState.Valid, attitude.State);
        Assert.Equal(-0.53, attitude.PitchUp!.Value, 6);   // the simulator's 0.53 is nose down
        Assert.Equal(-0.40, attitude.BankLeft!.Value, 6);
        Assert.Equal("ATT", attitude.AttWord);

        var speed = r.Speed!;
        Assert.False(speed.SpdFlag);
        Assert.Equal(30, speed.Airspeed);   // 2.5 kt in memory: the tape never reads below 30
        Assert.Equal(200, speed.TargetSpeed);
        Assert.True(speed.V1Inop);
        Assert.Null(speed.V1);
        Assert.Null(speed.Vr);
        Assert.Null(speed.FlapSpeed);       // the timer reads 0: the F bug waits for 10 s
        Assert.Null(speed.Mach);            // dashes below .40
        Assert.Equal(0, speed.GroundSpeed);
        Assert.Equal(("SPD", "V1", "INOP"), (speed.SpdWord, speed.V1Word, speed.InopWord));

        var ils = r.Ils!;
        Assert.Equal(Bbs757AdiPointer.Pointer, ils.Glideslope);
        Assert.Equal(2.2, ils.GlideslopeDots);
        Assert.True(ils.GlideslopePinned);
        Assert.Equal(Bbs757AdiPointer.Pointer, ils.Localizer);
        Assert.Equal(2.0, ils.LocalizerDots);
        Assert.True(ils.LocalizerPinned);
        Assert.False(ils.Expanded);
        Assert.False(ils.RisingRunway);
        Assert.Equal(("LOC", "G/S"), (ils.LocWord, ils.GlideslopeWord));

        var ra = r.RadioAltitude!;
        Assert.False(ra.RaFlag);
        Assert.Equal(-6, ra.RadioAltitude);   // 10.56 − 16.93 = −6.37, truncated
        Assert.Equal(200, ra.DecisionHeight);
        Assert.False(ra.Alt);
        Assert.Equal(("DH", "RA", "ALT"), (ra.DhWord, ra.RaWord, ra.AltWord));

        var fd = r.FlightDirector!;
        Assert.True(fd.Drawn);
        Assert.Equal(8.471, fd.PitchBar, 6);   // the command 7.941 less the nose-up pitch −0.53
        Assert.Equal(-0.40, fd.RollBar, 6);

        Assert.Equal(new Bbs757AdiTcasReading(null, null), r.Tcas);
        Assert.Equal((121.0, 123.0), (r.Vref30, r.Vref25));
        Assert.Null(r.AttitudeMissing);
        Assert.Null(r.SpeedMissing);
        Assert.Null(r.IlsMissing);
        Assert.Null(r.RadioAltitudeMissing);
        Assert.Null(r.FlightDirectorMissing);
        Assert.Null(r.TcasMissing);
    }

    [Fact]
    public void The_captains_pfd_is_the_first_manager_pointer_with_the_pfd_vptr()
    {
        // The first officer's pfd is dark and its pointers lead nowhere: it is never read.
        using var r = Reader(Gate());
        Assert.True(r.TryFindPfd(out uint pfd));
        Assert.Equal(Pfd, pfd);

        // Other gauges before it in the manager are passed over.
        var bytes = Gate(b =>
        {
            Put(b, Layout.ManagerBase, 0x9000);
            Put(b, 0x9000, 0x1234);
        });
        Assert.True(Read(bytes).Powered);
    }

    [Fact]
    public void A_read_outside_the_module_is_lost()
    {
        // No pfd in the manager.
        Lost(Gate(b => Put(b, Pfd, 0x1234)));
        Lost(Gate(b => Put(b, Layout.ManagerBase + 16, 0x7fff0000)));
        // The source selector, and each object the parts read through, null or outside the module.
        foreach (int wild in new[] { 0, 0x7fff0000 })
        {
            Lost(Gate(b => Put(b, At(Pfd, Layout.PfdToSource), wild)));
            Lost(Gate(b => Put(b, At(Pfd, Layout.PfdToMcp), wild)));
            Lost(Gate(b => Put(b, At(Pfd, Layout.PfdToAfds), wild)));
            Lost(Gate(b => Put(b, At(Pfd, Layout.PfdToFms), wild)));
            Lost(Gate(b => Put(b, At(Source, Layout.SourceToIrs), wild)));
            Lost(Gate(b => Put(b, At(Source, Layout.SourceToAdc), wild)));
            Lost(Gate(b => Put(b, At(Source, Layout.SourceToRa), wild)));
        }
        // A field beyond the module's end: the fms sits where its V1 INOP byte falls outside.
        Lost(Gate(b => Put(b, At(Pfd, Layout.PfdToFms), Size - 16)));
        // The pointers' structure is checked while the display is dark too.
        Lost(Gate(b =>
        {
            b[At(Pfd, Layout.Powered)] = 0;
            Put(b, At(Source, Layout.SourceToIrs), 0x7fff0000);
        }));
    }

    [Fact]
    public void A_dark_display_reads_unpowered_and_still_carries_the_reference_speeds()
    {
        foreach (var dark in new Action<byte[]>[]
                 {
                     b => b[At(Pfd, Layout.Powered)] = 0,
                     b => b[At(Pfd, Layout.Failed)] = 1,
                     b => b[At(Pfd, Layout.Blanked)] = 1,
                 })
        {
            var r = Read(Gate(dark));
            Assert.False(r.Powered);
            Assert.False(r.Corrupt);
            Assert.Null(r.Attitude);
            Assert.Null(r.Speed);
            Assert.Null(r.Ils);
            Assert.Null(r.RadioAltitude);
            Assert.Null(r.FlightDirector);
            Assert.Null(r.Tcas);
            Assert.Equal((121.0, 123.0), (r.Vref30, r.Vref25));
        }
    }

    [Fact]
    public void A_dark_display_holds_anything_without_being_corrupt()
    {
        // Nothing is drawn while the display is dark, so no value is range-checked.
        var r = Read(Gate(b =>
        {
            b[At(Pfd, Layout.Powered)] = 0;
            PutD(b, At(D, S.AdcAirspeed), double.NaN);
            Put(b, At(Irs, A.IrsState), -4);
            Put(b, At(G, R.DhValue), 90000);
            PutD(b, At(Fms, V.FmsVref30), double.NaN);
        }));
        Assert.False(r.Powered);
        Assert.False(r.Corrupt);
        // A reference speed that is not a number is not carried.
        Assert.Null(r.Vref30);
        Assert.Equal(123.0, r.Vref25);
    }

    [Fact]
    public void A_value_out_of_range_is_corrupt_while_lit()
    {
        foreach (var damage in new Action<byte[]>[]
                 {
                     b => Put(b, At(Irs, A.IrsMode), -1),
                     b => Put(b, At(Irs, A.IrsState), 256),
                     b => PutD(b, At(D, S.AdcAirspeed), double.NaN),
                     b => PutD(b, At(D, S.AdcAltitude), 2e6),
                     b => PutD(b, At(Mcp, S.McpSpeed), double.PositiveInfinity),
                     b => Put(b, At(Afds, S.AfdsPitchMode), 300),
                     b => Put(b, At(Fms, S.FmsV1), 2_000_000),
                     b => Put(b, At(Fms, S.FmsVr), -2_000_000),
                     b => PutD(b, At(Afds, S.AfdsTimer), double.NaN),
                     b => PutD(b, At(Afds, S.AfdsFlapSpeed), -1e7),
                     b => PutD(b, At(G, I.LocPointer), double.NaN),
                     b => PutD(b, At(G, I.GsPointer), 1e7),
                     b => Put(b, At(Afds, I.AfdsRollMode), -2),
                     b => PutD(b, At(Ra, R.RaAltitude), double.NegativeInfinity),
                     b => PutD(b, At(D, R.AdcVerticalSpeed), double.NaN),
                     b => Put(b, At(G, R.DhValue), -1),
                     b => Put(b, At(G, R.DhValue), 5001),
                     b => PutD(b, At(Afds, F.FdPitch), double.NaN),
                     b => PutD(b, At(Afds, F.FdRoll), 1e7),
                     b => PutD(b, At(G, T.TcasAbove), double.NaN),
                     b => PutD(b, At(G, T.TcasBelowPitch), 1e7),
                     b => PutD(b, At(Fms, V.FmsVref30), double.NaN),
                     b => PutD(b, At(Fms, V.FmsVref25), 1e7),
                 })
        {
            var r = Read(Gate(damage));
            Assert.True(r.Corrupt);
            Assert.True(r.Powered);
            Assert.Null(r.Attitude);
            Assert.Null(r.Speed);
            Assert.Null(r.Ils);
            Assert.Null(r.RadioAltitude);
            Assert.Null(r.FlightDirector);
            Assert.Null(r.Tcas);
            Assert.Null(r.Vref30);
            Assert.Null(r.Vref25);
        }
    }

    [Fact]
    public void A_value_no_row_can_show_is_not_range_checked()
    {
        // The decision height while its shown byte is clear (BlueBird clears it while the knob's value is negative), and
        // the ra's self-test state and altitude while the self-test byte is clear.
        var r = Read(Gate(b =>
        {
            b[At(G, R.DhShown)] = 0;
            Put(b, At(G, R.DhValue), -20);
            Put(b, At(Ra, R.RaTestState), 70000);
            PutD(b, At(Ra, R.RaTestAltitude), double.NaN);
        }));
        Assert.False(r.Corrupt);
        Assert.Null(r.RadioAltitude!.DecisionHeight);
        Assert.Equal(-6, r.RadioAltitude.RadioAltitude);

        // In the self-test both are used, so both are checked.
        Assert.True(Read(Gate(b =>
        {
            b[At(Ra, R.RaTest)] = 1;
            Put(b, At(Ra, R.RaTestState), 70000);
        })).Corrupt);
        Assert.True(Read(Gate(b =>
        {
            b[At(Ra, R.RaTest)] = 1;
            Put(b, At(Ra, R.RaTestState), 2);
            PutD(b, At(Ra, R.RaTestAltitude), double.NaN);
        })).Corrupt);
    }

    [Fact]
    public void A_corrupt_reading_logs_its_cause_once_per_change()
    {
        var lines = new List<string>();
        var bytes = Gate(b => PutD(b, At(D, S.AdcAirspeed), double.NaN));
        using var r = Reader(bytes, log: lines.Add);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(r.TryRead(Sim(), out var reading));
            Assert.True(reading.Corrupt);
        }
        var line = Assert.Single(lines);
        Assert.Contains("airspeed", line);
        Assert.Contains("NaN", line);

        // Another cause is a change: logged again; a good reading between forgets the cause.
        PutD(bytes, At(D, S.AdcAirspeed), 2.5);
        Put(bytes, At(G, R.DhValue), 9000);
        Assert.True(r.TryRead(Sim(), out _));
        Assert.True(r.TryRead(Sim(), out _));
        Assert.Equal(2, lines.Count);
        Assert.Contains("decision height", lines[1]);
        Assert.Contains("9000", lines[1]);

        Put(bytes, At(G, R.DhValue), 200);
        Assert.True(r.TryRead(Sim(), out var good));
        Assert.False(good.Corrupt);
        Put(bytes, At(G, R.DhValue), 9000);
        Assert.True(r.TryRead(Sim(), out _));
        Assert.Equal(3, lines.Count);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_cause_is_written_the_same_under_a_comma_decimal_culture(string culture)
    {
        var lines = new List<string>();
        using var r = Reader(Gate(b => PutD(b, At(D, S.AdcAltitude), 2500000.5)), log: lines.Add);
        TestCulture.Run(culture, () => r.TryRead(Sim(), out _));
        Assert.Contains("2500000.5", Assert.Single(lines));
    }

    [Fact]
    public void The_afds_timer_running_reads_the_time_kept_plus_the_clock_since_its_start()
    {
        // Running since the clock read 4950, with 3 s kept, at clock 5000: 53 s. V1 and VR are set: both on the tape,
        // and the F bug (201) is drawn (10 s have passed).
        var r = Read(Gate(b =>
        {
            b[At(Afds, S.AfdsTimer + S.TimerStopped)] = 0;
            PutD(b, At(Afds, S.AfdsTimer), 3);
            PutL(b, At(Afds, S.AfdsTimer + S.TimerStart), 4950);
            Put(b, At(Fms, S.FmsV1), 135);
            Put(b, At(Fms, S.FmsVr), 140);
        }));
        Assert.False(r.Speed!.V1Inop);
        Assert.Equal((135, 140), (r.Speed.V1, r.Speed.Vr));
        Assert.Equal(201, r.Speed.FlapSpeed);

        // The same timer 200 s on: past the bugs' 120 s.
        var later = Read(Gate(b =>
        {
            b[At(Afds, S.AfdsTimer + S.TimerStopped)] = 0;
            PutD(b, At(Afds, S.AfdsTimer), 3);
            PutL(b, At(Afds, S.AfdsTimer + S.TimerStart), 4950);
            PutD(b, S.Clock, 5200);
            Put(b, At(Fms, S.FmsV1), 135);
            Put(b, At(Fms, S.FmsVr), 140);
        }));
        Assert.Null(later.Speed!.V1);
        Assert.Null(later.Speed.Vr);
        Assert.Equal(201, later.Speed.FlapSpeed);
    }

    [Fact]
    public void A_failed_or_unpowered_device_shows_its_flag()
    {
        // The adc: failed, or unpowered.
        foreach (var fail in new Action<byte[]>[] { b => b[At(Adc, Layout.DeviceFailed)] = 1, b => b[At(Adc, Layout.DevicePowered)] = 0 })
            Assert.True(Read(Gate(fail)).Speed!.SpdFlag);
        // The ra alike.
        foreach (var fail in new Action<byte[]>[] { b => b[At(Ra, Layout.DeviceFailed)] = 1, b => b[At(Ra, Layout.DevicePowered)] = 0 })
        {
            var ra = Read(Gate(fail)).RadioAltitude!;
            Assert.True(ra.RaFlag);
            Assert.Null(ra.RadioAltitude);
        }
    }

    [Fact]
    public void A_missing_simulator_input_makes_its_item_absent()
    {
        // No values at all (a read without the simulator): every item an input feeds is absent, nothing fails.
        using (var reader = Reader(Gate()))
        {
            Assert.True(reader.TryRead(out var none));
            Assert.False(none.Corrupt);
            Assert.Equal(Bbs757AdiAttitudeState.Valid, none.Attitude!.State);
            Assert.Null(none.Attitude.PitchUp);
            Assert.Null(none.Attitude.BankLeft);
            Assert.Null(none.Speed!.GroundSpeed);
            Assert.Null(none.Speed.Mach);
            Assert.False(none.FlightDirector!.Drawn);
            Assert.Equal(30, none.Speed.Airspeed);   // the rest still reads
            Assert.Equal(200, none.RadioAltitude!.DecisionHeight);
        }

        // One at a time.
        var noPitch = Read(Gate(), sim: Sim(pitch: null));
        Assert.Null(noPitch.Attitude!.PitchUp);
        Assert.Equal(-0.40, noPitch.Attitude.BankLeft!.Value, 6);
        Assert.False(noPitch.FlightDirector!.Drawn);   // the bars need both

        var noBank = Read(Gate(), sim: Sim(bank: null));
        Assert.Equal(-0.53, noBank.Attitude!.PitchUp!.Value, 6);
        Assert.Null(noBank.Attitude.BankLeft);
        Assert.False(noBank.FlightDirector!.Drawn);

        Assert.Null(Read(Gate(), sim: Sim(groundSpeed: null)).Speed!.GroundSpeed);
        Assert.Null(Read(Gate(), sim: Sim(mach: null)).Speed!.Mach);

        // A value that is not a number is not given.
        var nan = Read(Gate(), sim: new Bbs757SimValues(new Dictionary<string, double>
        {
            [Bbs757AdiReader.PitchKey] = double.NaN, [Bbs757AdiReader.BankKey] = double.PositiveInfinity,
            [Bbs757AdiReader.GroundSpeedKey] = double.NaN, [Bbs757AdiReader.MachKey] = double.NaN,
        }));
        Assert.False(nan.Corrupt);   // never judged by a simulator value (B757-30)
        Assert.Null(nan.Attitude!.PitchUp);
        Assert.Null(nan.Attitude.BankLeft);
        Assert.Null(nan.Speed!.GroundSpeed);
        Assert.Null(nan.Speed.Mach);
    }

    [Fact]
    public void Mach_is_shown_from_0_40_and_kept_until_0_38()
    {
        using var r = Reader(Gate());
        double? MachAt(double? mach)
        {
            Assert.True(r.TryRead(Sim(mach: mach), out var reading));
            return reading.Speed!.Mach;
        }

        Assert.Null(MachAt(0.39));           // climbing: dashes below .40
        Assert.Equal(0.40, MachAt(0.40));
        Assert.Equal(0.78, MachAt(0.78));
        Assert.Equal(0.39, MachAt(0.39));    // descending: kept
        Assert.Equal(0.38, MachAt(0.38));
        Assert.Null(MachAt(0.379));          // below .38: dashes
        Assert.Null(MachAt(0.39));           // and dashes again until .40
        Assert.Equal(0.41, MachAt(0.41));
        Assert.Null(MachAt(null));           // not given: absent, and the state stays
        Assert.Equal(0.39, MachAt(0.39));
    }

    // ---- a part the layout lacks: skipped, the rest still reads -------------------------------------------------

    [Fact]
    public void A_missing_attitude_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { Attitude = null, AttitudeMissing = "attitude: no irs state" };
        // Its object is not followed: the irs pointer may lead nowhere.
        var r = Read(Gate(b => Put(b, At(Source, Layout.SourceToIrs), 0)), layout);
        Assert.False(r.Corrupt);
        Assert.Null(r.Attitude);
        Assert.Equal("attitude: no irs state", r.AttitudeMissing);
        // What needs a valid attitude is not known to be drawn.
        Assert.Null(r.Speed!.GroundSpeed);
        Assert.False(r.FlightDirector!.Drawn);
        Assert.Null(r.RadioAltitude!.DecisionHeight);
        // The rest reads.
        Assert.Equal(30, r.Speed.Airspeed);
        Assert.True(r.Ils!.LocalizerPinned);
        Assert.Equal(-6, r.RadioAltitude.RadioAltitude);
        Assert.Equal(121.0, r.Vref30);
    }

    [Fact]
    public void A_missing_speed_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { Speed = null, SpeedMissing = "speed: no stopwatch in the afds" };
        // Its objects are not followed: the mcp pointer may lead nowhere.
        var r = Read(Gate(b => Put(b, At(Pfd, Layout.PfdToMcp), 0)), layout);
        Assert.False(r.Corrupt);
        Assert.Null(r.Speed);
        Assert.Equal("speed: no stopwatch in the afds", r.SpeedMissing);
        Assert.Equal(Bbs757AdiAttitudeState.Valid, r.Attitude!.State);
        Assert.True(r.FlightDirector!.Drawn);
        Assert.Equal(200, r.RadioAltitude!.DecisionHeight);
        Assert.Equal(Bbs757AdiPointer.Pointer, r.Ils!.Glideslope);
        Assert.Equal((121.0, 123.0), (r.Vref30, r.Vref25));
    }

    [Fact]
    public void A_missing_ils_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { Ils = null, IlsMissing = "ils: no receiver writes the block" };
        var r = Read(Gate(b => PutD(b, At(G, I.LocPointer), double.NaN)), layout);   // not read, so not judged
        Assert.False(r.Corrupt);
        Assert.Null(r.Ils);
        Assert.Equal("ils: no receiver writes the block", r.IlsMissing);
        Assert.Equal(30, r.Speed!.Airspeed);
        Assert.Equal(200, r.RadioAltitude!.DecisionHeight);
        Assert.True(r.FlightDirector!.Drawn);
    }

    [Fact]
    public void A_missing_radio_altitude_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { RadioAltitude = null, RadioAltitudeMissing = "radio altitude: no ra getter" };
        // Its object is not followed: the ra pointer may lead nowhere.
        var r = Read(Gate(b => Put(b, At(Source, Layout.SourceToRa), 0)), layout);
        Assert.False(r.Corrupt);
        Assert.Null(r.RadioAltitude);
        Assert.Equal("radio altitude: no ra getter", r.RadioAltitudeMissing);
        Assert.Equal(30, r.Speed!.Airspeed);
        Assert.Equal(Bbs757AdiPointer.Pointer, r.Ils!.Localizer);
        Assert.False(r.Ils.RisingRunway);   // it needs the ra: not known to be drawn
        Assert.True(r.FlightDirector!.Drawn);
    }

    [Fact]
    public void A_missing_flight_director_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { FlightDirector = null, FlightDirectorMissing = "flight director: no command" };
        var r = Read(Gate(b => PutD(b, At(Afds, F.FdPitch), double.NaN)), layout);   // not read, so not judged
        Assert.False(r.Corrupt);
        Assert.Null(r.FlightDirector);
        Assert.Equal("flight director: no command", r.FlightDirectorMissing);
        Assert.Equal(Bbs757AdiAttitudeState.Valid, r.Attitude!.State);
        Assert.Equal(200, r.Speed!.TargetSpeed);
    }

    [Fact]
    public void A_missing_tcas_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { Tcas = null, TcasMissing = "tcas: nothing writes the PFD's block" };
        var r = Read(Gate(b => PutD(b, At(G, T.TcasAbove), double.NaN)), layout);   // not read, so not judged
        Assert.False(r.Corrupt);
        Assert.Null(r.Tcas);
        Assert.Equal("tcas: nothing writes the PFD's block", r.TcasMissing);
        Assert.Equal(Bbs757AdiAttitudeState.Valid, r.Attitude!.State);
        Assert.Equal(Bbs757AdiPointer.Pointer, r.Ils!.Glideslope);
    }

    [Fact]
    public void A_missing_reference_speeds_part_is_skipped_and_the_rest_reads()
    {
        var layout = Layout with { ReferenceSpeeds = null, ReferenceSpeedsMissing = "reference speeds: no VREF getter" };
        var r = Read(Gate(b => PutD(b, At(Fms, V.FmsVref30), double.NaN)), layout);   // not read, so not judged
        Assert.False(r.Corrupt);
        Assert.Null(r.Vref30);
        Assert.Null(r.Vref25);
        Assert.Equal(30, r.Speed!.Airspeed);
        Assert.Equal(Bbs757AdiAttitudeState.Valid, r.Attitude!.State);
    }

    [Fact]
    public void A_part_with_no_reason_is_missing_all_the_same()
    {
        // The layout record does not hold a part and its reason to each other: a null part is missing whatever the reason.
        var layout = Layout with { Tcas = null, TcasMissing = null, Speed = null, SpeedMissing = null };
        var r = Read(Gate(), layout);
        Assert.Null(r.Tcas);
        Assert.Null(r.Speed);
        Assert.Equal("not in this layout file", r.TcasMissing);
        Assert.Equal("not in this layout file", r.SpeedMissing);
    }

    [Fact]
    public void Every_part_missing_reads_the_power_alone()
    {
        var layout = Layout with
        {
            Attitude = null, AttitudeMissing = "a", Speed = null, SpeedMissing = "b", Ils = null, IlsMissing = "c",
            RadioAltitude = null, RadioAltitudeMissing = "d", FlightDirector = null, FlightDirectorMissing = "e",
            Tcas = null, TcasMissing = "f", ReferenceSpeeds = null, ReferenceSpeedsMissing = "g",
        };
        // No object but the pfd is followed.
        var r = Read(Gate(b =>
        {
            Put(b, At(Pfd, Layout.PfdToSource), 0);
            Put(b, At(Pfd, Layout.PfdToMcp), 0);
            Put(b, At(Pfd, Layout.PfdToAfds), 0);
            Put(b, At(Pfd, Layout.PfdToFms), 0);
        }), layout);
        Assert.True(r.Powered);
        Assert.False(r.Corrupt);
        Assert.Equal(("a", "b", "c", "d", "e", "f"),
            (r.AttitudeMissing, r.SpeedMissing, r.IlsMissing, r.RadioAltitudeMissing, r.FlightDirectorMissing, r.TcasMissing));
    }

    [Fact]
    public void A_lost_read_of_the_reference_speeds_leaves_them_null_and_the_rest_reads()
    {
        // With the speed tape's part gone only the reference speeds read the fms: a pointer that leads nowhere loses
        // them alone, never the reading (they are never a row).
        var layout = Layout with { Speed = null, SpeedMissing = "no speed" };
        foreach (int wild in new[] { 0, 0x7fff0000, Size - 16 })
        {
            var r = Read(Gate(b => Put(b, At(Pfd, Layout.PfdToFms), wild)), layout);
            Assert.False(r.Corrupt);
            Assert.Null(r.Vref30);
            Assert.Null(r.Vref25);
            Assert.Equal(Bbs757AdiAttitudeState.Valid, r.Attitude!.State);
        }
    }

    // ---- BlueBird's draw rules, on Decode alone -----------------------------------------------------------------

    /// <summary>An ILS approach, 1,500 ft above the runway: 250 kt, the MCP at 180, the timer long past its bugs, the
    /// flap speed 161, on the localizer's expanded scale, descending at 700 ft/min.</summary>
    internal static Bbs757AdiRaw Approach() => new(
        Lit: true,
        Attitude: new Bbs757AdiAttitudeRaw(IrsAttitude: true, IrsMode: A.IrsModeNav, IrsState: A.IrsStateValid),
        Speed: new Bbs757AdiSpeedRaw(AdcFailed: false, Airspeed: 250, Altitude: 3000, McpSpeed: 180, PitchMode: 9,
            SpeedIntervene: false, V1: 135, Vr: 140, V1Inop: false, TimerKept: 3600, TimerStart: 0, TimerStopped: true,
            Clock: 0, FlapSpeed: 161),
        Ils: new Bbs757AdiIlsRaw(LocPointer: 0.6, GsPointer: -0.5, LocScale: true, GsScale: true, LocValid: true,
            GsValid: true, Expanded: true, LocFlag: false, GsFlag: false, RollMode: 3),
        RadioAltitude: new Bbs757AdiRadioAltitudeRaw(RaFailed: false, RaAltitude: 1516.93, RaTest: false, RaTestState: 0,
            RaTestAltitude: 0, DhValue: 200, DhShown: true, VerticalSpeed: -700),
        FlightDirector: new Bbs757AdiFlightDirectorRaw(FdCaptain: true, Pitch: 2.5, Roll: 5),
        Tcas: new Bbs757AdiTcasRaw(0, 0, 0, 0),
        ReferenceSpeeds: new Bbs757AdiReferenceSpeedsRaw(121, 123));

    /// <summary>The simulator's values in degrees (the simulator's signs), knots and Mach.</summary>
    internal static Bbs757AdiSim In(double? pitch = -2.5, double? bank = 10, double? groundSpeed = 240, double? mach = 0.45) =>
        new(pitch / DegreesPerRadian, bank / DegreesPerRadian, groundSpeed, mach);

    /// <summary>The gate's reading, read from <see cref="Gate"/> through the real sampler.</summary>
    internal static Bbs757AdiReading GateReading() => Read(Gate());

    private static Bbs757AdiReading Decode(Bbs757AdiRaw raw, Bbs757AdiSim? sim = null, bool machShown = true,
        Bbs757AdiLayout? layout = null) =>
        Bbs757AdiReader.Decode(layout ?? Layout, raw, sim ?? In(), machShown);

    private static Bbs757AdiRaw WithSpeed(Func<Bbs757AdiSpeedRaw, Bbs757AdiSpeedRaw> change) =>
        Approach() with { Speed = change(Approach().Speed!) };

    private static Bbs757AdiRaw WithIls(Func<Bbs757AdiIlsRaw, Bbs757AdiIlsRaw> change) =>
        Approach() with { Ils = change(Approach().Ils!) };

    private static Bbs757AdiRaw WithRa(Func<Bbs757AdiRadioAltitudeRaw, Bbs757AdiRadioAltitudeRaw> change) =>
        Approach() with { RadioAltitude = change(Approach().RadioAltitude!) };

    private static Bbs757AdiRaw WithAttitude(Func<Bbs757AdiAttitudeRaw, Bbs757AdiAttitudeRaw> change) =>
        Approach() with { Attitude = change(Approach().Attitude!) };

    [Fact]
    public void Decodes_an_approach()
    {
        var r = Decode(Approach());

        Assert.True(r.Powered);
        Assert.False(r.Corrupt);
        Assert.Equal(Bbs757AdiAttitudeState.Valid, r.Attitude!.State);
        Assert.Equal(2.5, r.Attitude.PitchUp!.Value, 6);
        Assert.Equal(10, r.Attitude.BankLeft!.Value, 6);
        Assert.Equal(new Bbs757AdiSpeedReading(false, 250, 180, false, null, null, 161, 0.45, 240, "SPD", "V1", "INOP"), r.Speed);
        Assert.Equal(new Bbs757AdiRadioAltitudeReading(false, 1500, 200, true, "DH", "RA", "ALT"), r.RadioAltitude);
        Assert.True(r.FlightDirector!.Drawn);
        Assert.Equal(0, r.FlightDirector.PitchBar, 6);    // the command 2.5 at 2.5 nose up: on the symbol
        Assert.Equal(5, r.FlightDirector.RollBar, 6);     // 10 left, 5 left commanded: fly right
        Assert.Equal(new Bbs757AdiTcasReading(null, null), r.Tcas);
        Assert.Equal(Bbs757AdiPointer.Pointer, r.Ils!.Glideslope);
        Assert.Equal(-0.5, r.Ils.GlideslopeDots);
        Assert.False(r.Ils.GlideslopePinned);
        Assert.Equal(0.3, r.Ils.LocalizerDots, 6);        // 0.6 of the expanded scale's half width: half a dot each side
        Assert.True(r.Ils.Expanded);
        Assert.False(r.Ils.LocalizerPinned);
        Assert.True(r.Ils.RisingRunway);
        Assert.Equal((121.0, 123.0), (r.Vref30, r.Vref25));
    }

    [Fact]
    public void The_att_flag_shows_while_the_attitude_is_not_valid_and_the_irs_is_not_aligning()
    {
        // Each of the three conditions lost in turn: the flag.
        foreach (var raw in new[]
                 {
                     WithAttitude(a => a with { IrsAttitude = false }),
                     WithAttitude(a => a with { IrsMode = 4 }),
                     WithAttitude(a => a with { IrsState = 0 }),
                 })
        {
            var r = Decode(raw);
            Assert.Equal(Bbs757AdiAttitudeState.Flag, r.Attitude!.State);
            Assert.Null(r.Attitude.PitchUp);
            Assert.Null(r.Attitude.BankLeft);
            // Nothing that needs a valid attitude is drawn.
            Assert.Null(r.Speed!.GroundSpeed);
            Assert.False(r.FlightDirector!.Drawn);
            Assert.Null(r.RadioAltitude!.DecisionHeight);
            Assert.False(r.Ils!.RisingRunway);
            // The tape, the scales and the radio altitude are.
            Assert.Equal(250, r.Speed.Airspeed);
            Assert.Equal(Bbs757AdiPointer.Pointer, r.Ils.Localizer);
            Assert.Equal(1500, r.RadioAltitude.RadioAltitude);
        }
    }

    [Fact]
    public void The_center_is_blank_while_the_irs_aligns()
    {
        var r = Decode(WithAttitude(a => a with { IrsState = A.IrsStateAligning, IrsMode = 2, IrsAttitude = false }));
        Assert.Equal(Bbs757AdiAttitudeState.Blank, r.Attitude!.State);
        Assert.Null(r.Attitude.PitchUp);
        Assert.False(r.FlightDirector!.Drawn);
    }

    [Fact]
    public void The_horizon_stops_at_18_degrees_nose_up()
    {
        Assert.Equal(18, Decode(Approach(), In(pitch: -25)).Attitude!.PitchUp!.Value, 6);
        Assert.Equal(17.5, Decode(Approach(), In(pitch: -17.5)).Attitude!.PitchUp!.Value, 6);
        Assert.Equal(-25, Decode(Approach(), In(pitch: 25)).Attitude!.PitchUp!.Value, 6);   // nose down is not held
    }

    [Fact]
    public void The_spd_flag_replaces_the_whole_tape()
    {
        var r = Decode(WithSpeed(s => s with { AdcFailed = true, V1 = 0, V1Inop = true }));
        Assert.Equal(new Bbs757AdiSpeedReading(true, null, null, false, null, null, null, null, null, "SPD", "V1", "INOP"), r.Speed);
    }

    [Fact]
    public void The_target_speed_is_the_mcps_unless_vnav_flies_the_fmcs()
    {
        Assert.Equal(180, Decode(WithSpeed(s => s with { PitchMode = 9 })).Speed!.TargetSpeed);
        foreach (int vnav in S.VnavModes)
        {
            Assert.Null(Decode(WithSpeed(s => s with { PitchMode = vnav })).Speed!.TargetSpeed);
            // Speed intervention: the MCP's again.
            Assert.Equal(180, Decode(WithSpeed(s => s with { PitchMode = vnav, SpeedIntervene = true })).Speed!.TargetSpeed);
        }
    }

    [Fact]
    public void V1_and_vr_show_while_the_timer_reads_under_120_seconds()
    {
        // The take-off roll: 100 kt, the timer at 20 s, flaps 15 (the F speed 201 is far from VR).
        Bbs757AdiRaw Roll(Func<Bbs757AdiSpeedRaw, Bbs757AdiSpeedRaw>? change = null) => WithSpeed(s =>
        {
            var roll = s with { Airspeed = 100, TimerKept = 20, FlapSpeed = 201 };
            return change is null ? roll : change(roll);
        });

        var r = Decode(Roll());
        Assert.False(r.Speed!.V1Inop);
        Assert.Equal((135, 140), (r.Speed.V1, r.Speed.Vr));

        // At 120 s both are gone.
        var late = Decode(Roll(s => s with { TimerKept = 120 }));
        Assert.Null(late.Speed!.V1);
        Assert.Null(late.Speed.Vr);
        Assert.Equal((135, 140), (Decode(Roll(s => s with { TimerKept = 119.9 })).Speed!.V1, Decode(Roll(s => s with { TimerKept = 119.9 })).Speed!.Vr));

        // A speed not set is not drawn.
        Assert.Null(Decode(Roll(s => s with { Vr = 0 })).Speed!.Vr);
        var noV1 = Decode(Roll(s => s with { V1 = 0 }));
        Assert.Null(noV1.Speed!.V1);
        Assert.False(noV1.Speed.V1Inop);   // the INOP byte is clear
        Assert.Equal(140, noV1.Speed.Vr);

        // V1 INOP: V1 0 with the byte set, whatever the timer; VR's bug is drawn all the same while it is set.
        var inop = Decode(Roll(s => s with { V1 = 0, V1Inop = true }));
        Assert.True(inop.Speed!.V1Inop);
        Assert.Null(inop.Speed.V1);
        Assert.Equal(140, inop.Speed.Vr);
        Assert.True(Decode(Roll(s => s with { V1 = 0, V1Inop = true, TimerKept = 500 })).Speed!.V1Inop);
        // The byte alone, with a V1 set, is not INOP.
        var set = Decode(Roll(s => s with { V1Inop = true }));
        Assert.False(set.Speed!.V1Inop);
        Assert.Equal(135, set.Speed.V1);
    }

    [Fact]
    public void The_bugs_are_hidden_while_vr_is_within_4_knots_of_the_flap_speed()
    {
        // Flaps 15 at 121 VREF 30: the F speed 141, VR 140. The 1 and R bugs are hidden.
        Bbs757AdiRaw Roll(double airspeed, double flapSpeed = 141) =>
            WithSpeed(s => s with { Airspeed = airspeed, TimerKept = 20, FlapSpeed = flapSpeed });

        // V1 135 is within the tape at 100 kt: nothing prints it.
        var hidden = Decode(Roll(100));
        Assert.Null(hidden.Speed!.V1);
        Assert.Null(hidden.Speed.Vr);
        // More than 41 kt above the airspeed shown, the tape prints V1's number at its top: 135 − 93 = 42.
        Assert.Equal(135, Decode(Roll(93)).Speed!.V1);
        Assert.Null(Decode(Roll(93)).Speed!.Vr);
        Assert.Null(Decode(Roll(94)).Speed!.V1);   // 41: not more
        // The airspeed shown is never below 30: at 20 kt V1 is 105 above it.
        Assert.Equal(135, Decode(Roll(20)).Speed!.V1);

        // 4 kt apart still hides them; further apart, or with no flap speed (100), they show.
        Assert.Null(Decode(Roll(100, flapSpeed: 144)).Speed!.Vr);
        Assert.Equal(140, Decode(Roll(100, flapSpeed: 144.5)).Speed!.Vr);
        Assert.Equal(140, Decode(Roll(100, flapSpeed: S.NoFlapSpeed)).Speed!.Vr);
    }

    [Fact]
    public void The_flap_speed_bug_needs_a_speed_the_altitude_and_ten_seconds()
    {
        Assert.Equal(161, Decode(Approach()).Speed!.FlapSpeed);
        Assert.Null(Decode(WithSpeed(s => s with { FlapSpeed = S.NoFlapSpeed })).Speed!.FlapSpeed);
        Assert.Null(Decode(WithSpeed(s => s with { Altitude = 20001 })).Speed!.FlapSpeed);
        Assert.Equal(161, Decode(WithSpeed(s => s with { Altitude = 20000 })).Speed!.FlapSpeed);
        Assert.Null(Decode(WithSpeed(s => s with { TimerKept = 9.9 })).Speed!.FlapSpeed);
        Assert.Equal(161, Decode(WithSpeed(s => s with { TimerKept = 10 })).Speed!.FlapSpeed);
    }

    [Fact]
    public void Mach_and_ground_speed_follow_their_rules()
    {
        Assert.Equal(0.45, Decode(Approach(), machShown: true).Speed!.Mach);
        Assert.Null(Decode(Approach(), machShown: false).Speed!.Mach);
        Assert.Null(Decode(Approach(), In(mach: null), machShown: true).Speed!.Mach);
        Assert.Equal(240, Decode(Approach()).Speed!.GroundSpeed);
        Assert.Null(Decode(Approach(), In(groundSpeed: null)).Speed!.GroundSpeed);

        // The hysteresis itself.
        Assert.False(Bbs757AdiReader.MachShown(S, shown: false, 0.399));
        Assert.True(Bbs757AdiReader.MachShown(S, shown: false, 0.40));
        Assert.True(Bbs757AdiReader.MachShown(S, shown: true, 0.38));
        Assert.False(Bbs757AdiReader.MachShown(S, shown: true, 0.3799));
    }

    [Fact]
    public void The_flight_director_shows_while_the_attitude_is_valid_and_the_captains_switch_is_on()
    {
        Assert.False(Decode(Approach() with { FlightDirector = new Bbs757AdiFlightDirectorRaw(false, 2.5, 5) }).FlightDirector!.Drawn);

        // The pitch bar: the command less the nose-up pitch. 8 commanded at 2.5 up: 5.5 above; 0 commanded: 2.5 below.
        var above = Decode(Approach() with { FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 8, 5) });
        Assert.Equal(5.5, above.FlightDirector!.PitchBar, 6);
        var below = Decode(Approach() with { FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 0, 5) });
        Assert.Equal(-2.5, below.FlightDirector!.PitchBar, 6);
        // The roll bar: the bank less the command, both positive left. Banked 10 left with 25 left commanded: 15 left.
        var left = Decode(Approach() with { FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 2.5, 25) });
        Assert.Equal(-15, left.FlightDirector!.RollBar, 6);
        // The bar follows the pitch itself past the horizon's stop.
        var steep = Decode(Approach() with { FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 20, 5) }, In(pitch: -25));
        Assert.Equal(-5, steep.FlightDirector!.PitchBar, 6);
    }

    [Fact]
    public void A_tcas_advisory_shows_while_the_attitude_is_valid()
    {
        var above = Decode(Approach() with { Tcas = new Bbs757AdiTcasRaw(1, 0, 5, 0) });
        Assert.Equal(new Bbs757AdiTcasReading(5, null), above.Tcas);
        var below = Decode(Approach() with { Tcas = new Bbs757AdiTcasRaw(0, 1, 0, -2) });
        Assert.Equal(new Bbs757AdiTcasReading(null, -2), below.Tcas);
        var both = Decode(Approach() with { Tcas = new Bbs757AdiTcasRaw(1, 1, 1, 6) });
        Assert.Equal(new Bbs757AdiTcasReading(1, 6), both.Tcas);
        // Not drawn without a valid attitude.
        var flagged = Decode(Approach() with
        {
            Tcas = new Bbs757AdiTcasRaw(1, 1, 1, 6),
            Attitude = new Bbs757AdiAttitudeRaw(false, A.IrsModeNav, 0),
        });
        Assert.Equal(new Bbs757AdiTcasReading(null, null), flagged.Tcas);
    }

    [Fact]
    public void The_localizer_reads_in_dots_of_its_scale()
    {
        // Normal scale: the pointer's ±1 is 2 dots; pinned at the scale's end.
        var normal = Decode(WithIls(i => i with { Expanded = false, LocPointer = -0.6 })).Ils!;
        Assert.Equal(-1.2, normal.LocalizerDots, 6);
        Assert.False(normal.LocalizerPinned);
        Assert.False(normal.Expanded);
        var pinned = Decode(WithIls(i => i with { Expanded = false, LocPointer = -1 })).Ils!;
        Assert.Equal(-2, pinned.LocalizerDots, 6);
        Assert.True(pinned.LocalizerPinned);

        // Expanded: ±1 is half a dot, and its end is no pin of the normal scale's kind.
        var expanded = Decode(WithIls(i => i with { Expanded = true, LocPointer = 1 })).Ils!;
        Assert.Equal(0.5, expanded.LocalizerDots, 6);
        Assert.False(expanded.LocalizerPinned);
        Assert.True(expanded.Expanded);

        // Flag, scale alone, nothing.
        Assert.Equal(Bbs757AdiPointer.Flag, Decode(WithIls(i => i with { LocFlag = true })).Ils!.Localizer);
        Assert.Equal(Bbs757AdiPointer.ScaleOnly, Decode(WithIls(i => i with { LocValid = false })).Ils!.Localizer);
        Assert.Equal(Bbs757AdiPointer.None, Decode(WithIls(i => i with { LocScale = false })).Ils!.Localizer);
    }

    [Fact]
    public void The_glideslope_reads_in_dots_and_pins_at_2_2()
    {
        var up = Decode(WithIls(i => i with { GsPointer = 0.5 })).Ils!;
        Assert.Equal(0.5, up.GlideslopeDots);
        Assert.False(up.GlideslopePinned);
        Assert.True(Decode(WithIls(i => i with { GsPointer = 2.2 })).Ils!.GlideslopePinned);
        Assert.True(Decode(WithIls(i => i with { GsPointer = -2.2 })).Ils!.GlideslopePinned);
        Assert.False(Decode(WithIls(i => i with { GsPointer = 2.19 })).Ils!.GlideslopePinned);

        Assert.Equal(Bbs757AdiPointer.Flag, Decode(WithIls(i => i with { GsFlag = true })).Ils!.Glideslope);
        Assert.Equal(Bbs757AdiPointer.ScaleOnly, Decode(WithIls(i => i with { GsValid = false })).Ils!.Glideslope);
        Assert.Equal(Bbs757AdiPointer.None, Decode(WithIls(i => i with { GsScale = false })).Ils!.Glideslope);
    }

    [Fact]
    public void The_rising_runway_needs_every_one_of_its_conditions()
    {
        Assert.True(Decode(Approach()).Ils!.RisingRunway);
        foreach (int mode in I.RunwayModes)
            Assert.True(Decode(WithIls(i => i with { RollMode = mode })).Ils!.RisingRunway);

        Assert.False(Decode(WithIls(i => i with { Expanded = false })).Ils!.RisingRunway);
        Assert.False(Decode(WithIls(i => i with { LocScale = false })).Ils!.RisingRunway);
        Assert.False(Decode(WithIls(i => i with { LocValid = false })).Ils!.RisingRunway);
        Assert.False(Decode(WithIls(i => i with { RollMode = 2 })).Ils!.RisingRunway);
        Assert.False(Decode(WithAttitude(a => a with { IrsState = 0 })).Ils!.RisingRunway);
        Assert.False(Decode(WithRa(r => r with { RaFailed = true })).Ils!.RisingRunway);
        Assert.False(Decode(WithRa(r => r with { RaAltitude = 2517.5 })).Ils!.RisingRunway);   // 2,500.57 ft
        Assert.True(Decode(WithRa(r => r with { RaAltitude = 2516.9 })).Ils!.RisingRunway);    // 2,499.97 ft
    }

    [Theory]
    [InlineData(10.56, -6)]        // the gate: −6.37, truncated toward zero
    [InlineData(16.93, 0)]
    [InlineData(116.9, 99)]        // 99.97: below 100 a whole number
    [InlineData(117.0, 100)]       // 100.07
    [InlineData(172.0, 160)]       // 155.07 is 155: from 100 up to the nearest 10, a 5 away from zero
    [InlineData(171.9, 150)]       // 154.97 is 154
    [InlineData(1017.0, 1000)]
    [InlineData(2516.9, 2500)]     // 2,499.97 is 2,499
    public void Radio_altitude_reads_as_its_digits(double stored, int shown)
    {
        Assert.Equal(shown, Decode(WithRa(r => r with { RaAltitude = stored })).RadioAltitude!.RadioAltitude);
    }

    [Fact]
    public void Radio_altitude_above_2500_is_not_drawn()
    {
        Assert.Null(Decode(WithRa(r => r with { RaAltitude = 2517.0 })).RadioAltitude!.RadioAltitude);   // 2,500.07
        Assert.Null(Decode(WithRa(r => r with { RaAltitude = 12000 })).RadioAltitude!.RadioAltitude);
        Assert.False(Decode(WithRa(r => r with { RaAltitude = 12000 })).RadioAltitude!.RaFlag);
    }

    [Fact]
    public void The_ra_self_test_reads_its_own_altitude()
    {
        // States 2 and 4: the test's altitude alone; 3: both; any other: the antenna's.
        foreach (int alone in R.RaTestAlone)
            Assert.Equal(40, Decode(WithRa(r => r with { RaTest = true, RaTestState = alone, RaTestAltitude = 40 })).RadioAltitude!.RadioAltitude);
        Assert.Equal(1540, Decode(WithRa(r => r with { RaTest = true, RaTestState = R.RaTestAdded, RaTestAltitude = 40 })).RadioAltitude!.RadioAltitude);
        Assert.Equal(1500, Decode(WithRa(r => r with { RaTest = true, RaTestState = 1, RaTestAltitude = 40 })).RadioAltitude!.RadioAltitude);
        // The byte clear: the state means nothing.
        Assert.Equal(1500, Decode(WithRa(r => r with { RaTest = false, RaTestState = 2, RaTestAltitude = 40 })).RadioAltitude!.RadioAltitude);
    }

    [Fact]
    public void The_decision_height_shows_while_its_byte_is_set_and_the_attitude_is_valid()
    {
        Assert.Equal(200, Decode(Approach()).RadioAltitude!.DecisionHeight);
        Assert.Null(Decode(WithRa(r => r with { DhShown = false })).RadioAltitude!.DecisionHeight);
        Assert.Equal(0, Decode(WithRa(r => r with { DhValue = 0 })).RadioAltitude!.DecisionHeight);
    }

    [Fact]
    public void Alt_shows_from_500_to_2500_feet_while_descending_150_feet_a_minute_or_more()
    {
        Assert.True(Decode(Approach()).RadioAltitude!.Alt);
        Assert.True(Decode(WithRa(r => r with { VerticalSpeed = -150 })).RadioAltitude!.Alt);
        Assert.False(Decode(WithRa(r => r with { VerticalSpeed = -149 })).RadioAltitude!.Alt);
        Assert.True(Decode(WithRa(r => r with { RaAltitude = 517.0 })).RadioAltitude!.Alt);      // 500.07
        Assert.False(Decode(WithRa(r => r with { RaAltitude = 516.0 })).RadioAltitude!.Alt);     // 499.07
        Assert.True(Decode(WithRa(r => r with { RaAltitude = 2516.9 })).RadioAltitude!.Alt);     // 2,499.97
        Assert.False(Decode(WithRa(r => r with { RaAltitude = 2518 })).RadioAltitude!.Alt);      // 2,501.07
    }

    [Fact]
    public void The_reference_speeds_are_carried_whatever_the_power_and_never_drawn()
    {
        var dark = Decode(Approach() with { Lit = false });
        Assert.False(dark.Powered);
        Assert.Equal((121.0, 123.0), (dark.Vref30, dark.Vref25));
        // Before PERF INIT both read 0: carried as read.
        var zero = Decode(Approach() with { ReferenceSpeeds = new Bbs757AdiReferenceSpeedsRaw(0, 0) });
        Assert.Equal((0.0, 0.0), (zero.Vref30, zero.Vref25));
        // The part missing, or its read lost.
        var none = Decode(Approach() with { ReferenceSpeeds = null });
        Assert.False(none.Corrupt);
        Assert.Null(none.Vref30);
        Assert.Null(none.Vref25);
    }

    [Fact]
    public void The_corrupt_cause_names_the_first_value_out_of_range()
    {
        Assert.Null(Bbs757AdiReader.CorruptCause(Layout, Approach()));
        Assert.Null(Bbs757AdiReader.CorruptCause(Layout, Approach() with { Lit = false, Speed = Approach().Speed! with { Airspeed = double.NaN } }));
        Assert.Equal("glideslope pointer = NaN is out of range",
            Bbs757AdiReader.CorruptCause(Layout, WithIls(i => i with { GsPointer = double.NaN })));
        Assert.Equal("irs state = 256 is out of range",
            Bbs757AdiReader.CorruptCause(Layout, WithAttitude(a => a with { IrsState = 256 })));
        // A value within the rule's size is no cause, however unlikely.
        Assert.Null(Bbs757AdiReader.CorruptCause(Layout, WithSpeed(s => s with { Airspeed = 999_999, V1 = -5 })));
        // The clock is a time, not a speed: its size is not judged, only the timer it gives.
        Assert.Null(Bbs757AdiReader.CorruptCause(Layout, WithSpeed(s => s with { TimerStopped = false, Clock = 3e9, TimerStart = 3_000_000_000 - 50 })));
        Assert.Equal(50 + 3600, Bbs757AdiReader.Timer(Approach().Speed! with { TimerStopped = false, Clock = 3e9, TimerStart = 3_000_000_000 - 50 }));
    }
}
