using System.Buffers.Binary;
using System.Globalization;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// Reads what the Captain's ADI draws besides its flight mode annunciator from the 757 module's memory through a derived
/// layout (BBS757-D2, WASM-2: no hardcoded offset): the ADI's <see cref="IBbs757DisplaySampler{TReading}"/> (plan 27).
/// The object is the first pointer in the gauge manager whose target carries <see cref="Bbs757AdiLayout.PfdVptr"/>, the
/// Captain's pfd (the first officer's follows it); the instrument source selector with its irs, adc and ra, the mcp, the
/// afds and the fms hang off it. Owns its lease of the simulator handle. Every read is bounds-checked against committed
/// module memory (WASM-4) and the regions are asked about once per reading (WASM-15).
/// <para>Where BlueBird's draw takes a simulator variable (the pitch, the bank, the ground speed and the Mach number:
/// its handles hold no value in memory), the reader takes the same one from the monitor (S10): an input, never a check
/// (B757-30), and one that is not given leaves its item out. A part the layout lacks is skipped, with its objects, and
/// the rest still reads. False when the pfd or an object a part reads through cannot be reached: the memory moved or
/// went away. The FMC's reference speeds alone are never a reason to fail: they are not drawn, so a lost read of them
/// leaves them null.</para>
/// <para>The one thing kept between readings is whether the tape's Mach is showing, as BlueBird's draw keeps it (shown
/// from .40, kept until .38).</para>
/// </summary>
public sealed class Bbs757AdiReader : IBbs757DisplaySampler<Bbs757AdiReading>
{
    /// <summary>The named log of the ADI reader and its monitor.</summary>
    public const string LogChannel = "bbs757-adi";

    /// <summary>The simulator variables BlueBird's draw takes (S10), each looked up by its own name: the pitch and the
    /// bank in radians, the ground speed in knots and the Mach number.</summary>
    public const string PitchKey = "PLANE PITCH DEGREES", BankKey = "PLANE BANK DEGREES",
        GroundSpeedKey = "GROUND VELOCITY", MachKey = "AIRSPEED MACH";

    /// <summary>The reason of a part the layout lacks without saying why.</summary>
    internal const string NotInLayout = "not in this layout file";

    // BlueBird's draw constants, read from its draw code with the layout's offsets (docs/b757/evidence/displays-adi.md):
    // none is an address or an offset.

    /// <summary>The draw's degrees per radian (f23610).</summary>
    internal const double DegreesPerRadian = 57.2958;

    /// <summary>The horizon stops moving at this many degrees nose up.</summary>
    internal const double MaxPitchUp = 18;

    /// <summary>The localizer pointer's ±1 in dots: on the normal scale, and on the expanded one.</summary>
    internal const double LocalizerDotsNormal = 2, LocalizerDotsExpanded = 0.5;

    /// <summary>The glideslope pointer is pinned at this many dots.</summary>
    internal const double GlideslopePin = 2.2;

    /// <summary>A speed, altitude, deviation or command beyond this size is no reading.</summary>
    internal const double MaxSize = 1e6;

    /// <summary>The decision height's range, in feet.</summary>
    internal const int MaxDecisionHeight = 5000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;
    private readonly Action<string> _log;
    private string? _cause;    // why the last reading was corrupt, until a good one: logged once per change
    private bool _machShown;   // the tape's Mach is showing (BlueBird's own hysteresis)

    public Bbs757AdiLayout Layout { get; }

    /// <param name="log">Where a corrupt reading's cause goes (CORE-15): the ADI channel unless a test injects one.</param>
    public Bbs757AdiReader(ModuleMemory memory, IDisposable ownedSource, Bbs757AdiLayout layout, Action<string>? log = null)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
        _log = log ?? (line => Log.Channel(LogChannel).Debug(line));
    }

    /// <summary>The Captain's pfd: the first manager pointer whose target carries the pfd vptr. False when none in the
    /// manager's span does (the module is not where the anchor said, or not ready).</summary>
    public bool TryFindPfd(out uint pfd)
    {
        pfd = 0;
        for (uint off = 0; off + 4 <= Layout.ManagerSpan; off += 4)
        {
            if (!_memory.TryReadInt32(Layout.ManagerBase + off, out int pv)) continue;
            uint p = unchecked((uint)pv);
            if (p == 0 || !_memory.IsInsideModule(p, 4)) continue;
            if (_memory.TryReadInt32(p, out int vptr) && unchecked((uint)vptr) == Layout.PfdVptr)
            {
                pfd = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>A reading from memory alone: every item a simulator value feeds is left out.</summary>
    public bool TryRead(out Bbs757AdiReading reading) => TryRead(default, out reading);

    /// <summary>The monitor's read: memory, with this tick's simulator values (S10). False when the pfd or an object a
    /// part reads through cannot be reached, otherwise a reading, <see cref="Bbs757AdiReading.Corrupt"/> when a value is
    /// out of range.</summary>
    public bool TryRead(Bbs757SimValues sim, out Bbs757AdiReading reading)
    {
        reading = null!;
        using var _ = _memory.RememberRegions();
        if (!TryReadRaw(out var raw)) return false;

        var inputs = SimOf(sim);
        // BlueBird's Mach draw runs only while the tape is drawn, so its hysteresis moves only then.
        if (raw.Lit && Layout.Speed is { } speed && raw.Speed is { AdcFailed: false } && inputs.Mach is double mach)
            _machShown = MachShown(speed, _machShown, mach);

        string? cause = CorruptCause(Layout, raw);
        if (cause != _cause)
        {
            _cause = cause;
            if (cause is not null) _log("corrupt reading: " + cause);
        }
        reading = Decode(Layout, raw, inputs, _machShown);
        return true;
    }

    /// <summary>This tick's four inputs; one not given, or not a number, is null.</summary>
    public static Bbs757AdiSim SimOf(Bbs757SimValues sim) =>
        new(Number(sim[PitchKey]), Number(sim[BankKey]), Number(sim[GroundSpeedKey]), Number(sim[MachKey]));

    private static double? Number(double? v) => v is double d && double.IsFinite(d) ? d : null;

    // ---- memory --------------------------------------------------------------------------------------------------

    /// <summary>The pfd's power and every part the layout has, as stored. The objects' pointers are followed whatever the
    /// display's power: a null one, or one outside the module, is a lost read.</summary>
    private bool TryReadRaw(out Bbs757AdiRaw raw)
    {
        raw = null!;
        var l = Layout;
        if (!TryFindPfd(out uint pfd)) return false;
        if (!Flag(pfd, l.Powered, out bool powered) || !Flag(pfd, l.Failed, out bool failed) || !Flag(pfd, l.Blanked, out bool blanked))
            return false;

        // Each object only where a part of this layout reads through it.
        uint source = 0, irs = 0, adc = 0, ra = 0, mcp = 0, afds = 0, fms = 0;
        bool needsAdc = l.Speed is not null || l.RadioAltitude is not null;
        if ((l.Attitude is not null || needsAdc) && !Ptr(pfd, l.PfdToSource, out source)) return false;
        if (l.Attitude is not null && !Ptr(source, l.SourceToIrs, out irs)) return false;
        if (needsAdc && !Ptr(source, l.SourceToAdc, out adc)) return false;
        if (l.RadioAltitude is not null && !Ptr(source, l.SourceToRa, out ra)) return false;
        if (l.Speed is not null && !Ptr(pfd, l.PfdToMcp, out mcp)) return false;
        if ((l.Speed is not null || l.Ils is not null || l.FlightDirector is not null) && !Ptr(pfd, l.PfdToAfds, out afds)) return false;
        if (l.Speed is not null && !Ptr(pfd, l.PfdToFms, out fms)) return false;
        uint ils = pfd + (uint)l.IlsBlock, data = adc + (uint)l.AdcBlock;

        Bbs757AdiAttitudeRaw? attitude = null;
        if (l.Attitude is { } a)
        {
            if (!Flag(irs, a.IrsAttitude, out bool valid) || !Int(irs, a.IrsMode, out int mode) || !Int(irs, a.IrsState, out int state))
                return false;
            attitude = new Bbs757AdiAttitudeRaw(valid, mode, state);
        }

        Bbs757AdiSpeedRaw? speed = null;
        if (l.Speed is { } s)
        {
            if (!Device(adc, out bool adcFailed)
                || !Dbl(data, s.AdcAirspeed, out double airspeed) || !Dbl(data, s.AdcAltitude, out double altitude)
                || !Dbl(mcp, s.McpSpeed, out double mcpSpeed)
                || !Int(afds, s.AfdsPitchMode, out int pitchMode) || !Flag(afds, s.AfdsSpeedIntervene, out bool intervene)
                || !Int(fms, s.FmsV1, out int v1) || !Int(fms, s.FmsVr, out int vr) || !Flag(fms, s.FmsV1Inop, out bool v1Inop)
                || !Dbl(afds, s.AfdsTimer, out double kept) || !Long(afds, s.AfdsTimer + s.TimerStart, out long start)
                || !Flag(afds, s.AfdsTimer + s.TimerStopped, out bool stopped)
                || !_memory.TryReadDouble(s.Clock, out double clock)
                || !Dbl(afds, s.AfdsFlapSpeed, out double flapSpeed))
                return false;
            speed = new Bbs757AdiSpeedRaw(adcFailed, airspeed, altitude, mcpSpeed, pitchMode, intervene, v1, vr, v1Inop,
                kept, start, stopped, clock, flapSpeed);
        }

        Bbs757AdiIlsRaw? ilsRaw = null;
        if (l.Ils is { } i)
        {
            if (!Dbl(ils, i.LocPointer, out double loc) || !Dbl(ils, i.GsPointer, out double gs)
                || !Flag(ils, i.LocScale, out bool locScale) || !Flag(ils, i.GsScale, out bool gsScale)
                || !Flag(ils, i.LocValid, out bool locValid) || !Flag(ils, i.GsValid, out bool gsValid)
                || !Flag(ils, i.Expanded, out bool expanded) || !Flag(ils, i.LocFlag, out bool locFlag)
                || !Flag(ils, i.GsFlag, out bool gsFlag) || !Int(afds, i.AfdsRollMode, out int rollMode))
                return false;
            ilsRaw = new Bbs757AdiIlsRaw(loc, gs, locScale, gsScale, locValid, gsValid, expanded, locFlag, gsFlag, rollMode);
        }

        Bbs757AdiRadioAltitudeRaw? radio = null;
        if (l.RadioAltitude is { } r)
        {
            if (!Device(ra, out bool raFailed)
                || !Dbl(ra, r.RaAltitude, out double raAltitude) || !Flag(ra, r.RaTest, out bool test)
                || !Int(ra, r.RaTestState, out int testState) || !Dbl(ra, r.RaTestAltitude, out double testAltitude)
                || !Int(ils, r.DhValue, out int dh) || !Flag(ils, r.DhShown, out bool dhShown)
                || !Dbl(data, r.AdcVerticalSpeed, out double verticalSpeed))
                return false;
            radio = new Bbs757AdiRadioAltitudeRaw(raFailed, raAltitude, test, testState, testAltitude, dh, dhShown, verticalSpeed);
        }

        Bbs757AdiFlightDirectorRaw? director = null;
        if (l.FlightDirector is { } f)
        {
            if (!Byte(afds, f.FdCaptain, out byte fd) || !Dbl(afds, f.FdPitch, out double pitch) || !Dbl(afds, f.FdRoll, out double roll))
                return false;
            // BlueBird's F/D getter returns the byte "& 1", as the FMA reader reads it.
            director = new Bbs757AdiFlightDirectorRaw((fd & 1) != 0, pitch, roll);
        }

        Bbs757AdiTcasRaw? tcas = null;
        if (l.Tcas is { } t)
        {
            if (!Dbl(ils, t.TcasAbove, out double above) || !Dbl(ils, t.TcasBelow, out double below)
                || !Dbl(ils, t.TcasAbovePitch, out double abovePitch) || !Dbl(ils, t.TcasBelowPitch, out double belowPitch))
                return false;
            tcas = new Bbs757AdiTcasRaw(above, below, abovePitch, belowPitch);
        }

        // Not drawn, so never a reason to lose the reading: a lost read leaves them out.
        Bbs757AdiReferenceSpeedsRaw? reference = null;
        if (l.ReferenceSpeeds is { } v && (fms != 0 || Ptr(pfd, l.PfdToFms, out fms))
            && Dbl(fms, v.FmsVref30, out double vref30) && Dbl(fms, v.FmsVref25, out double vref25))
            reference = new Bbs757AdiReferenceSpeedsRaw(vref30, vref25);

        raw = new Bbs757AdiRaw(powered && !failed && !blanked, attitude, speed, ilsRaw, radio, director, tcas, reference);
        return true;
    }

    /// <summary>A pointer field: non-null and inside the module.</summary>
    private bool Ptr(uint baseAddr, int offset, out uint pointer)
    {
        pointer = 0;
        if (!_memory.TryReadInt32(baseAddr + (uint)offset, out int v)) return false;
        pointer = unchecked((uint)v);
        return pointer != 0 && _memory.IsInsideModule(pointer, 4);
    }

    /// <summary>Whether a device (the adc, the ra) is failed or unpowered: its failed byte set, or its powered byte
    /// clear.</summary>
    private bool Device(uint device, out bool failed)
    {
        failed = false;
        if (!Flag(device, Layout.DeviceFailed, out bool failedByte) || !Flag(device, Layout.DevicePowered, out bool powered)) return false;
        failed = failedByte || !powered;
        return true;
    }

    private bool Int(uint baseAddr, int offset, out int v) => _memory.TryReadInt32(baseAddr + (uint)offset, out v);

    private bool Byte(uint baseAddr, int offset, out byte v) => _memory.TryReadByte(baseAddr + (uint)offset, out v);

    private bool Flag(uint baseAddr, int offset, out bool set)
    {
        bool ok = Byte(baseAddr, offset, out byte v);
        set = v != 0;
        return ok;
    }

    private bool Dbl(uint baseAddr, int offset, out double v) => _memory.TryReadDouble(baseAddr + (uint)offset, out v);

    private bool Long(uint baseAddr, int offset, out long v)
    {
        Span<byte> b = stackalloc byte[8];
        bool ok = _memory.TryRead(baseAddr + (uint)offset, b);
        v = ok ? BinaryPrimitives.ReadInt64LittleEndian(b) : 0;
        return ok;
    }

    // ---- BlueBird's draw rules, pure ----------------------------------------------------------------------------

    /// <summary>Whether the tape's Mach shows after <paramref name="mach"/>: from the layout's
    /// <see cref="Bbs757AdiSpeed.MachShownFrom"/> up and, once <paramref name="shown"/>, until it falls below
    /// <see cref="Bbs757AdiSpeed.MachShownUntil"/>.</summary>
    public static bool MachShown(Bbs757AdiSpeed speed, bool shown, double mach) =>
        mach >= speed.MachShownFrom || (shown && mach >= speed.MachShownUntil);

    /// <summary>The AFDS timer in seconds: the time kept while it is stopped, else the time kept plus the clock since its
    /// start.</summary>
    public static double Timer(Bbs757AdiSpeedRaw raw) =>
        raw.TimerStopped ? raw.TimerKept : raw.TimerKept + (raw.Clock - raw.TimerStart);

    /// <summary>The radio altitude in feet as BlueBird's getter gives it (f46419): the ra's altitude less the layout's
    /// offset; in the self-test, by its state, the test's altitude instead, or both added.</summary>
    public static double RadioAltitude(Bbs757AdiRadioAltitude part, Bbs757AdiRadioAltitudeRaw raw)
    {
        double antenna = raw.RaAltitude - part.RaOffset;
        if (!raw.RaTest) return antenna;
        if (part.RaTestAlone.Contains(raw.RaTestState)) return raw.RaTestAltitude;
        return raw.RaTestState == part.RaTestAdded ? antenna + raw.RaTestAltitude : antenna;
    }

    /// <summary>
    /// Why a lit reading is no reading, or null: a mode or state value below 0 or above 255; a speed, altitude, deviation
    /// or command that is not a number or beyond <see cref="MaxSize"/> in size; an AFDS timer that is not a number; a
    /// decision height below 0 or above <see cref="MaxDecisionHeight"/>. Nothing is judged while the display is dark
    /// (nothing is drawn), nor a value no row can show: the decision height while its shown byte is clear (BlueBird
    /// clears it while the knob's value is negative), and the ra's self-test state and altitude outside the self-test.
    /// </summary>
    public static string? CorruptCause(Bbs757AdiLayout layout, Bbs757AdiRaw raw)
    {
        if (!raw.Lit) return null;
        string? cause = null;
        void Mode(string name, int v)
        {
            if (v < 0 || v > 255) cause ??= string.Create(Inv, $"{name} = {v} is out of range");
        }
        void Size(string name, double v)
        {
            if (!double.IsFinite(v) || Math.Abs(v) > MaxSize) cause ??= string.Create(Inv, $"{name} = {v:R} is out of range");
        }

        if (raw.Attitude is { } a)
        {
            Mode("irs mode", a.IrsMode);
            Mode("irs state", a.IrsState);
        }
        if (raw.Speed is { } s)
        {
            Size("adc airspeed", s.Airspeed);
            Size("adc altitude", s.Altitude);
            Size("mcp speed", s.McpSpeed);
            Mode("afds pitch mode", s.PitchMode);
            Size("V1", s.V1);
            Size("VR", s.Vr);
            double timer = Timer(s);
            if (!double.IsFinite(timer)) cause ??= string.Create(Inv, $"afds timer = {timer:R} is out of range");
            Size("flap speed", s.FlapSpeed);
        }
        if (raw.Ils is { } i)
        {
            Size("localizer pointer", i.LocPointer);
            Size("glideslope pointer", i.GsPointer);
            Mode("afds roll mode", i.RollMode);
        }
        if (raw.RadioAltitude is { } r)
        {
            Size("radio altitude", r.RaAltitude);
            if (r.RaTest)
            {
                Mode("ra self-test state", r.RaTestState);
                if (layout.RadioAltitude is { } part && (part.RaTestAlone.Contains(r.RaTestState) || r.RaTestState == part.RaTestAdded))
                    Size("ra self-test altitude", r.RaTestAltitude);
            }
            if (r.DhShown && (r.DhValue < 0 || r.DhValue > MaxDecisionHeight))
                cause ??= string.Create(Inv, $"decision height = {r.DhValue} is out of range");
            Size("adc vertical speed", r.VerticalSpeed);
        }
        if (raw.FlightDirector is { } f)
        {
            Size("flight director pitch command", f.Pitch);
            Size("flight director roll command", f.Roll);
        }
        if (raw.Tcas is { } t)
        {
            Size("TCAS above", t.Above);
            Size("TCAS below", t.Below);
            Size("TCAS above pitch", t.AbovePitch);
            Size("TCAS below pitch", t.BelowPitch);
        }
        if (raw.ReferenceSpeeds is { } v)
        {
            Size("VREF 30", v.Vref30);
            Size("VREF 25", v.Vref25);
        }
        return cause;
    }

    /// <summary>
    /// The reading the Captain's ADI draws from <paramref name="raw"/> and this tick's simulator values, by BlueBird's own
    /// draw rules (S10; docs/b757/evidence/displays-adi.md, "Step 3"). <paramref name="machShown"/> is the tape's Mach
    /// hysteresis after this tick (<see cref="MachShown"/>).
    /// <list type="bullet">
    /// <item>Dark: nothing but the FMC's reference speeds, which are read whatever the power.</item>
    /// <item>Out of range (<see cref="CorruptCause"/>): corrupt, with nothing else.</item>
    /// <item>The attitude is valid while the irs is in NAV, its attitude byte is set and its state is the valid one; else
    /// the ATT flag, or nothing while it aligns. The pitch is held at <see cref="MaxPitchUp"/> nose up, as the horizon.</item>
    /// <item>The ground speed, the flight director, a TCAS advisory, the rising runway and the decision height are drawn
    /// only while the attitude is valid; with no attitude part, none is known to be drawn.</item>
    /// <item>The adc failed or unpowered: the SPD flag and nothing else of the tape. The ra failed or unpowered: the RA
    /// flag in the number's place.</item>
    /// </list>
    /// A part the layout lacks is null with its reason.
    /// </summary>
    public static Bbs757AdiReading Decode(Bbs757AdiLayout layout, Bbs757AdiRaw raw, Bbs757AdiSim sim, bool machShown)
    {
        if (!raw.Lit)
            return new Bbs757AdiReading(false, null, null, null, null, null, null,
                Carried(raw.ReferenceSpeeds?.Vref30), Carried(raw.ReferenceSpeeds?.Vref25), Corrupt: false);
        if (CorruptCause(layout, raw) is not null)
            return new Bbs757AdiReading(true, null, null, null, null, null, null, null, null, Corrupt: true);

        // The simulator's own signs: pitch positive nose down, bank positive left.
        double? pitch = sim.Pitch * DegreesPerRadian, bank = sim.Bank * DegreesPerRadian;

        bool? attitudeValid = null;
        Bbs757AdiAttitudeReading? attitude = null;
        if (layout.Attitude is { } ap && raw.Attitude is { } ar)
        {
            bool valid = ar.IrsMode == ap.IrsModeNav && ar.IrsAttitude && ar.IrsState == ap.IrsStateValid;
            attitudeValid = valid;
            var state = valid ? Bbs757AdiAttitudeState.Valid
                : ar.IrsState == ap.IrsStateAligning ? Bbs757AdiAttitudeState.Blank
                : Bbs757AdiAttitudeState.Flag;
            attitude = new Bbs757AdiAttitudeReading(state,
                valid && pitch is double p ? Math.Min(-p, MaxPitchUp) : null,
                valid ? bank : null,
                ap.AttWord);
        }
        bool drawnWithAttitude = attitudeValid == true;

        double? radioAltitude = layout.RadioAltitude is { } rp && raw.RadioAltitude is { } rr ? RadioAltitude(rp, rr) : null;
        bool raFailed = raw.RadioAltitude?.RaFailed ?? false;

        Bbs757AdiSpeedReading? speed = null;
        if (layout.Speed is { } sp && raw.Speed is { } sr)
        {
            if (sr.AdcFailed)
            {
                speed = new Bbs757AdiSpeedReading(true, null, null, false, null, null, null, null, null, sp.SpdWord, sp.V1Word, sp.InopWord);
            }
            else
            {
                double shown = Math.Max(sr.Airspeed, sp.MinAirspeed);
                bool mcpIsSource = !sp.VnavModes.Contains(sr.PitchMode) || sr.SpeedIntervene;
                double timer = Timer(sr);
                bool bugTime = timer < sp.BugSeconds;
                bool flapSpeedSet = sr.FlapSpeed != sp.NoFlapSpeed;
                bool bugsHidden = flapSpeedSet && Math.Abs(sr.Vr - sr.FlapSpeed) <= sp.FlapSpeedNearVr;
                bool v1Inop = sr.V1 == 0 && sr.V1Inop;
                // V1 is on the tape as its bug, or printed at the tape's top while it is beyond the tape.
                bool v1Shown = !v1Inop && bugTime && sr.V1 != 0 && (!bugsHidden || sr.V1 - shown > sp.TapeHalfRange);
                bool vrShown = bugTime && sr.Vr != 0 && !bugsHidden;
                bool flapBug = flapSpeedSet && sr.Altitude <= sp.FlapSpeedMaxAltitude && timer >= sp.FlapSpeedAfterSeconds;
                speed = new Bbs757AdiSpeedReading(
                    SpdFlag: false,
                    Airspeed: shown,
                    TargetSpeed: mcpIsSource ? sr.McpSpeed : null,
                    V1Inop: v1Inop,
                    V1: v1Shown ? sr.V1 : null,
                    Vr: vrShown ? sr.Vr : null,
                    FlapSpeed: flapBug ? sr.FlapSpeed : null,
                    Mach: machShown ? sim.Mach : null,
                    GroundSpeed: drawnWithAttitude ? sim.GroundSpeed : null,
                    sp.SpdWord, sp.V1Word, sp.InopWord);
            }
        }

        Bbs757AdiIlsReading? ils = null;
        if (layout.Ils is { } ip && raw.Ils is { } ir)
        {
            var glideslope = ir.GsFlag ? Bbs757AdiPointer.Flag
                : !ir.GsScale ? Bbs757AdiPointer.None
                : ir.GsValid ? Bbs757AdiPointer.Pointer : Bbs757AdiPointer.ScaleOnly;
            var localizer = ir.LocFlag ? Bbs757AdiPointer.Flag
                : !ir.LocScale ? Bbs757AdiPointer.None
                : ir.LocValid ? Bbs757AdiPointer.Pointer : Bbs757AdiPointer.ScaleOnly;
            bool risingRunway = drawnWithAttitude && ir.Expanded && ir.LocScale && ir.LocValid
                && ip.RunwayModes.Contains(ir.RollMode)
                && radioAltitude is double feet && !raFailed && feet <= ip.RunwayBelow;
            ils = new Bbs757AdiIlsReading(
                glideslope,
                ir.GsPointer,
                Math.Abs(ir.GsPointer) >= GlideslopePin,
                localizer,
                ir.LocPointer * (ir.Expanded ? LocalizerDotsExpanded : LocalizerDotsNormal),
                !ir.Expanded && Math.Abs(ir.LocPointer) >= 1,
                ir.Expanded,
                risingRunway,
                ip.LocWord, ip.GlideslopeWord);
        }

        Bbs757AdiRadioAltitudeReading? radio = null;
        if (layout.RadioAltitude is { } rap && raw.RadioAltitude is { } rar && radioAltitude is double altitude)
        {
            radio = new Bbs757AdiRadioAltitudeReading(
                RaFlag: rar.RaFailed,
                RadioAltitude: !rar.RaFailed && altitude <= rap.RaMaxShown ? RadioAltitudeDigits(rap, altitude) : null,
                DecisionHeight: rar.DhShown && drawnWithAttitude ? rar.DhValue : null,
                // The draw returns after the DH while the ra is failed or unpowered: no ALT then.
                Alt: !rar.RaFailed && altitude >= rap.AltFrom && altitude <= rap.RaMaxShown && rar.VerticalSpeed <= rap.AltDescent,
                rap.DhWord, rap.RaWord, rap.AltWord);
        }

        Bbs757AdiFlightDirectorReading? director = null;
        if (layout.FlightDirector is not null && raw.FlightDirector is { } fr)
        {
            // The pitch bar sits the command less the nose-up pitch above the symbol; the roll bar the bank less the
            // command (both positive left) right of center.
            director = drawnWithAttitude && fr.FdCaptain && pitch is double p && bank is double b
                ? new Bbs757AdiFlightDirectorReading(true, fr.Pitch + p, b - fr.Roll)
                : new Bbs757AdiFlightDirectorReading(false, 0, 0);
        }

        Bbs757AdiTcasReading? tcas = null;
        if (layout.Tcas is not null && raw.Tcas is { } tr)
        {
            tcas = new Bbs757AdiTcasReading(
                drawnWithAttitude && tr.Above != 0 ? tr.AbovePitch : null,
                drawnWithAttitude && tr.Below != 0 ? tr.BelowPitch : null);
        }

        return new Bbs757AdiReading(true, attitude, speed, ils, radio, director, tcas,
            raw.ReferenceSpeeds?.Vref30, raw.ReferenceSpeeds?.Vref25, Corrupt: false)
        {
            AttitudeMissing = layout.Attitude is null ? layout.AttitudeMissing ?? NotInLayout : null,
            SpeedMissing = layout.Speed is null ? layout.SpeedMissing ?? NotInLayout : null,
            IlsMissing = layout.Ils is null ? layout.IlsMissing ?? NotInLayout : null,
            RadioAltitudeMissing = layout.RadioAltitude is null ? layout.RadioAltitudeMissing ?? NotInLayout : null,
            FlightDirectorMissing = layout.FlightDirector is null ? layout.FlightDirectorMissing ?? NotInLayout : null,
            TcasMissing = layout.Tcas is null ? layout.TcasMissing ?? NotInLayout : null,
        };
    }

    /// <summary>The radio altitude's digits: truncated to a whole number and, from the layout's
    /// <see cref="Bbs757AdiRadioAltitude.RaRoundFrom"/> up, rounded to the nearest 10.</summary>
    internal static int RadioAltitudeDigits(Bbs757AdiRadioAltitude part, double feet)
    {
        double whole = Math.Truncate(feet);
        if (whole >= part.RaRoundFrom) whole = Math.Round(whole / 10, MidpointRounding.AwayFromZero) * 10;
        return (int)whole;
    }

    /// <summary>A reference speed of a dark reading, where nothing is range-checked: carried when it is a number of a
    /// speed's size, else left out.</summary>
    private static double? Carried(double? v) => v is double d && double.IsFinite(d) && Math.Abs(d) <= MaxSize ? d : null;

    public void Dispose() => _ownedSource.Dispose();
}
