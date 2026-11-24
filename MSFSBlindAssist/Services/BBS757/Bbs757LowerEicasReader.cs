using System.Buffers.Binary;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// Reads lower EICAS samples from the 757 module's memory through a derived layout (BBS757-D2, WASM-2: no hardcoded
/// offset): the lower EICAS's <see cref="IBbs757DisplaySampler{TReading}"/>. The object is the first pointer in the gauge
/// manager whose target carries <see cref="Bbs757LowerEicasLayout.LowerVptr"/>; the engines, hydraulics, APU, oxygen,
/// computer and settings hang off it. Owns its lease of the simulator handle. Every read is bounds-checked against
/// committed module memory (WASM-4) and the regions are asked about once per reading (WASM-15). A read outside the module
/// is "lost" (false); the STATUS list walk is all-or-nothing (WASM-14's spirit): any bad count, pointer, length or code
/// point makes the whole reading corrupt, never a partial list.
/// </summary>
public sealed class Bbs757LowerEicasReader : IBbs757DisplaySampler<Bbs757LowerEicasReading>
{
    /// <summary>The longest status message text taken: BlueBird's are short caps lines.</summary>
    internal const int MaxMessageLength = 64;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Bbs757LowerEicasLayout Layout { get; }

    private readonly Action<string> _log;
    private string? _cause; // why the last reading was corrupt, until a good one: logged once per change

    /// <param name="log">Where a corrupt reading's cause goes (CORE-15): the lower EICAS channel unless a test injects one.</param>
    public Bbs757LowerEicasReader(ModuleMemory memory, IDisposable ownedSource, Bbs757LowerEicasLayout layout,
        Action<string>? log = null)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
        _log = log ?? (line => Log.Channel(Bbs757LowerEicasDisplay.LogChannel).Debug(line));
    }

    /// <summary>The lower EICAS object: the first manager pointer whose target carries the class vptr.</summary>
    public bool TryFindLower(out uint lower)
    {
        lower = 0;
        for (uint off = 0; off + 4 <= Layout.ManagerSpan; off += 4)
        {
            if (!_memory.TryReadInt32(Layout.ManagerBase + off, out int pv)) continue;
            uint p = unchecked((uint)pv);
            if (p == 0 || !_memory.IsInsideModule(p, 4)) continue;
            if (_memory.TryReadInt32(p, out int vptr) && unchecked((uint)vptr) == Layout.LowerVptr)
            {
                lower = p;
                return true;
            }
        }
        return false;
    }

    public bool TryRead(out Bbs757LowerEicasReading reading) => TryRead(default, out reading);

    /// <summary>The sampler takes no simulator value (memory alone); false when the object or a pointer it needs cannot be
    /// reached, otherwise a reading, <see cref="Bbs757LowerEicasReading.Corrupt"/> when a value or the status list is out
    /// of range.</summary>
    public bool TryRead(Bbs757SimValues sim, out Bbs757LowerEicasReading reading)
    {
        reading = default!;
        using var _ = _memory.RememberRegions();
        var L = Layout;
        if (!TryFindLower(out uint lower)) return false;
        if (!Ptr(lower, L.LeftEngine, out uint left) || !Ptr(lower, L.RightEngine, out uint right)) return false;

        // The draw takes the type from the left engine (lower+264 = left type == 0). Each engine's HP spool (an RR's N3,
        // a PW's N2: the FF draw's input) is a fixed field, read and range-checked here whatever the page.
        if (!Int(left, L.EngineType, out int type)) return false;
        bool rr = type == 0;
        int hpOffset = rr ? L.HpSpoolRr : L.HpSpoolPw;
        if (!Dbl(left, L.EngineToData + hpOffset, out double leftHp) || !Dbl(right, L.EngineToData + hpOffset, out double rightHp))
            return false;

        if (!Byte(lower, L.PowerSet, out byte set) || !Byte(lower, L.PowerClear, out byte clear)
            || !Byte(lower, L.BlankClear, out byte blank) || !Int(lower, L.Page, out int page) || !Byte(lower, L.NLatch, out byte latch))
            return false;
        bool powered = set != 0 && clear == 0 && blank == 0;

        if (!ReadMessages(out List<string> messages, out bool listCorrupt)) return false;

        // The value-range checks apply only while the display draws: nothing is drawn while it is not powered. The status
        // list walk's structural checks (pointers inside the module, count within its cap) are always on.
        bool bad = listCorrupt;
        string? cause = listCorrupt ? "the status list walk failed (a pointer, count, length or character out of range)" : null;
        double C(string name, double v)
        {
            if (powered && (!double.IsFinite(v) || Math.Abs(v) > 1e9))
            {
                bad = true;
                cause ??= string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} = {v:R} is out of range");
            }
            return v;
        }
        C("left HP spool", leftHp);
        C("right HP spool", rightHp);

        bool pounds = false;
        Bbs757LowerEicasEngineRaw[]? engines = null;
        if (Layout.Engine is { } ep)
        {
            if (!Ptr(lower, ep.LowerToSettings, out uint settings) || !Int(settings, ep.PoundsFlag, out int poundsFlag)) return false;
            pounds = poundsFlag != 0;
            engines = new Bbs757LowerEicasEngineRaw[2];
            for (int i = 0; i < 2; i++)
            {
                if (!ReadEngine(lower, i == 0 ? left : right, i == 0, ep, hpOffset, C, out engines[i])) return false;
            }
        }

        Bbs757LowerEicasStatusRaw? status = null;
        if (Layout.Status is { } sp)
        {
            if (!ReadStatus(lower, sp, C, out status)) return false;
            if (powered)
            {
                for (int i = 0; i < status.BrakeTemperatures.Count; i++)
                {
                    if (status.BrakeTemperatures[i] <= 9) continue;
                    bad = true;
                    cause ??= string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"brake {i + 1} digit = {status.BrakeTemperatures[i]} is above 9");
                    break;
                }
            }
        }

        if (cause != _cause)
        {
            _cause = cause;
            if (cause is not null) _log("corrupt reading: " + cause);
        }

        IReadOnlyList<string> rows = Array.Empty<string>();
        if (powered && !bad)
        {
            var raw = new Bbs757LowerEicasRaw(page, rr, latch != 0, pounds, engines, status, messages);
            rows = Bbs757LowerEicasText.PageRows(Layout, raw);
        }

        reading = new Bbs757LowerEicasReading(
            PageValue: page,
            Page: powered ? Bbs757LowerEicasText.PageWord(Layout, page) : null,
            PageRows: rows,
            StatusMessages: bad ? Array.Empty<string>() : messages,
            Powered: powered,
            Corrupt: bad);
        return true;
    }

    private bool ReadEngine(uint lower, uint engine, bool isLeft, Bbs757LowerEicasEnginePage e, int hpOffset,
        Func<string, double, double> check, out Bbs757LowerEicasEngineRaw raw)
    {
        raw = default!;
        uint data = engine + (uint)Layout.EngineToData;
        if (!Int(engine, Layout.EngineInvalid, out int invalid)
            || !Dbl(data, e.N2, out double n2) || !Dbl(data, e.N3, out double n3) || !Dbl(data, e.FuelFlow, out double ff)
            || !Dbl(data, e.OilPressure, out double oilP) || !Dbl(data, e.OilTemperature, out double oilT)
            || !Dbl(data, e.OilQuantity, out double oilQ) || !Dbl(data, e.Vibration, out double vib)
            || !Dbl(data, hpOffset, out double hp)
            || !Float(lower, isLeft ? e.MaxN2Left : e.MaxN2Right, out double maxN2)
            || !Float(lower, isLeft ? e.MaxN3Left : e.MaxN3Right, out double maxN3)
            || !Byte(engine, e.StartFlagA, out byte sa) || !Byte(engine, e.StartFlagB, out byte sb)
            || !Byte(engine, e.StartFlagC, out byte sc)
            || !Dbl(engine, e.HoldTimer, out double timer) || !Dbl(engine, e.HoldDelay, out double delay)
            || !Byte(engine, e.HoldByteA, out byte ha) || !Byte(engine, e.HoldByteB, out byte hb))
            return false;
        // BlueBird's hold: the timer has not reached its delay, or either hold byte is set.
        bool hold = timer < delay || ha != 0 || hb != 0;
        string side = isLeft ? "left" : "right";
        var checks = new (string Name, double V)[]
        {
            ("N2", n2), ("N3", n3), ("FF", ff), ("oil pressure", oilP), ("oil temperature", oilT), ("oil quantity", oilQ),
            ("vibration", vib), ("HP spool", hp), ("max N2", maxN2), ("max N3", maxN3), ("hold timer", timer), ("hold delay", delay),
        };
        foreach (var (name, v) in checks) check($"{side} engine {name}", v);
        raw = new Bbs757LowerEicasEngineRaw(invalid != Layout.EngineInvalidValue, n2, n3, ff, oilP, oilT, oilQ, vib,
            maxN2, maxN3, sa != 0 || sb != 0 || sc != 0, hold, hp);
        return true;
    }

    private bool ReadStatus(uint lower, Bbs757LowerEicasStatusPage s, Func<string, double, double> check, out Bbs757LowerEicasStatusRaw raw)
    {
        raw = default!;
        if (!Ptr(lower, s.LowerToHydraulics, out uint hyd) || !Ptr(lower, s.LowerToApu, out uint apu)
            || !Ptr(lower, s.LowerToOxygen, out uint oxy) || !Ptr(lower, s.LowerToComputer, out uint computer))
            return false;
        var cap = new double[3];
        var qty = new double[3];
        var press = new double[3];
        // The quantities are a std::vector's records: the field holds its begin pointer (BlueBird's 23018 calls 23032, which
        // returns *(hydraulics + 2996) + 40 * i). A null or out-of-module begin is a lost read, like any bad pointer.
        if (!Ptr(hyd, s.HydraulicQuantities, out uint begin)) return false;
        for (int i = 0; i < 3; i++)
        {
            uint rec = begin + (uint)(s.HydraulicQuantityStride * i);
            if (!Dbl(rec, s.HydraulicCapacity, out cap[i]) || !Dbl(rec, s.HydraulicQuantity, out qty[i])
                || !Dbl(hyd, s.HydraulicPressures + s.HydraulicPressureStride * i, out press[i]))
                return false;
        }
        if (!Dbl(apu, s.ApuEgt, out double egt) || !Dbl(apu, s.ApuRpm, out double rpm) || !Dbl(apu, s.ApuOilQuantity, out double oilQ)
            || !Byte(oxy, s.OxygenShown, out byte shown) || !Byte(oxy, s.OxygenActive, out byte active)
            || !Int(oxy, s.OxygenCapacity, out int oxyCap) || !Dbl(oxy, s.OxygenFull, out double full)
            || !Dbl(oxy, s.OxygenLevel, out double level)
            || !Float(lower, s.Rudder, out double rud) || !Float(lower, s.AileronLeft, out double ailL)
            || !Float(lower, s.Elevator, out double elev) || !Float(lower, s.AileronRight, out double ailR))
            return false;
        var brakes = new uint[s.BrakeWheels];
        for (int i = 0; i < brakes.Length; i++)
        {
            if (!Int(computer, s.BrakeTemperatures + 4 * i, out int d)) return false;
            brakes[i] = unchecked((uint)d);
        }
        for (int i = 0; i < 3; i++)
        {
            check($"hydraulic {i + 1} capacity", cap[i]);
            check($"hydraulic {i + 1} quantity", qty[i]);
            check($"hydraulic {i + 1} pressure", press[i]);
        }
        foreach (var (name, v) in new (string, double)[] { ("APU EGT", egt), ("APU RPM", rpm), ("APU oil quantity", oilQ),
            ("oxygen full", full), ("oxygen level", level), ("rudder", rud), ("left aileron", ailL), ("elevator", elev),
            ("right aileron", ailR) })
            check(name, v);
        raw = new Bbs757LowerEicasStatusRaw(cap, qty, press, egt, rpm, oilQ, shown != 0, active != 0, oxyCap, full, level,
            rud, ailL, elev, ailR, brakes);
        return true;
    }

    /// <summary>The STATUS list, newest first. False only when the list's header cannot be read (the module moved);
    /// <paramref name="corrupt"/> when the walk fails any check (then <paramref name="messages"/> is empty).</summary>
    private bool ReadMessages(out List<string> messages, out bool corrupt)
    {
        messages = new List<string>();
        corrupt = false;
        if (Layout.Status is not { } s) return true;
        if (!Int(s.ListAddress, s.ListBegin, out int beginRaw) || !Int(s.ListAddress, s.ListEnd, out int endRaw)) return false;
        uint begin = unchecked((uint)beginRaw), end = unchecked((uint)endRaw);
        if (end < begin || s.ListStride <= 0 || (end - begin) % (uint)s.ListStride != 0 || (end - begin) / (uint)s.ListStride > (uint)s.ListCap)
            return Torn(out corrupt, messages);
        uint count = (end - begin) / (uint)s.ListStride;
        if (count == 0) return true;
        if (!_memory.IsInsideModule(begin, count * (uint)s.ListStride)) return Torn(out corrupt, messages);

        var found = new List<string>();
        for (uint i = 0; i < count; i++)
        {
            if (!_memory.TryReadInt32(begin + i * (uint)s.ListStride, out int recRaw)) return Torn(out corrupt, messages);
            uint rec = unchecked((uint)recRaw);
            if (rec == 0 || !_memory.IsInsideModule(rec, (uint)Math.Max(Math.Max(s.RecordText, s.RecordLength), s.RecordSequence) + 4))
                return Torn(out corrupt, messages);
            if (!Int(rec, s.RecordText, out int textRaw) || !Int(rec, s.RecordLength, out int length)
                || !Int(rec, s.RecordSequence, out _))
                return Torn(out corrupt, messages);
            uint text = unchecked((uint)textRaw);
            if (length < 1 || length > MaxMessageLength || text == 0 || !_memory.IsInsideModule(text, (uint)length * 4))
                return Torn(out corrupt, messages);
            var bytes = new byte[length * 4];
            if (!_memory.TryRead(text, bytes)) return Torn(out corrupt, messages);
            var sb = new System.Text.StringBuilder(length);
            for (int c = 0; c < length; c++)
            {
                uint cp = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(c * 4));
                if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF) || cp < 0x20 || (cp >= 0x7F && cp <= 0x9F))
                    return Torn(out corrupt, messages);
                sb.Append(char.ConvertFromUtf32((int)cp));
            }
            found.Add(sb.ToString());
        }
        messages = found;
        return true;
    }

    /// <summary>A failed walk: the whole list is discarded, never a partial one.</summary>
    private static bool Torn(out bool corrupt, List<string> messages)
    {
        messages.Clear();
        corrupt = true;
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

    private bool Int(uint baseAddr, int offset, out int v) => _memory.TryReadInt32(baseAddr + (uint)offset, out v);

    private bool Byte(uint baseAddr, int offset, out byte v) => _memory.TryReadByte(baseAddr + (uint)offset, out v);

    private bool Dbl(uint baseAddr, int offset, out double v) => _memory.TryReadDouble(baseAddr + (uint)offset, out v);

    private bool Float(uint baseAddr, int offset, out double v)
    {
        Span<byte> b = stackalloc byte[4];
        bool ok = _memory.TryRead(baseAddr + (uint)offset, b);
        v = ok ? BinaryPrimitives.ReadSingleLittleEndian(b) : 0;
        return ok;
    }

    public void Dispose() => _ownedSource.Dispose();
}
