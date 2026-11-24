using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using MSFSBlindAssist.Services.WasmMemory;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>One message-list entry as read: its id, its text, its raw level and whether its cancelled byte is set.</summary>
public readonly record struct Bbs757EicasRawEntry(int Id, string Text, int Level, bool Cancelled);

/// <summary>An engine anti-ice valve, the pneumatic component with that side's id: its position (% open) and state.</summary>
public readonly record struct Bbs757EicasRawValve(double Position, int State);

/// <summary>One in-flight start envelope row as stored (FL 0 = unused).</summary>
public readonly record struct Bbs757EicasRawEnvelopeRow(int Fl, int Min, int Max);

/// <summary>One engine's fields as read, with its side's max-exceedance memories, reverser state, fire switch and
/// anti-ice valve (null when no component carries that side's id). The byte fields read as set (non-zero) or not.</summary>
public readonly record struct Bbs757EicasRawEngine(
    int Valid,
    int Type,
    double Egt,
    double N1,
    double N2,
    double Epr,
    bool AlternateFlagged,
    double N1Alternate,
    bool Combusting,
    float MaxN1,
    float MaxEgt,
    int Reverser,
    bool FireSwitch,
    Bbs757EicasRawValve? AntiIce);

/// <summary>The upper EICAS's fields as read from memory, before BlueBird's draw rules
/// (<see cref="Bbs757UpperEicasReader.Decode"/>). <see cref="Torn"/>: a message list or the component vector read torn
/// (its lists are then empty). The byte fields read as set (non-zero) or not; <see cref="Bbs757EicasRawEngine.AlternateFlagged"/>
/// is any of the alternate N1 flags set, <see cref="DtoFlagged"/> every D-TO flag set.</summary>
public sealed record Bbs757UpperEicasRaw(
    bool Torn,
    IReadOnlyList<Bbs757EicasRawEntry> Main,
    IReadOnlyList<Bbs757EicasRawEntry> Communication,
    int Page,
    double RecallTime,
    int Blank,
    double Clock,
    bool Powered,
    bool Failed,
    bool TimedBlank,
    bool Compact,
    float EgtRed,
    float EgtAmber,
    float N1Red,
    float N1Amber,
    Bbs757EicasRawEngine Left,
    Bbs757EicasRawEngine Right,
    bool TmcFailed,
    bool TmcChannelLeftFailed,
    bool TmcChannelRightFailed,
    int ThrustMode,
    int ThrustNumber,
    int Derate,
    bool DtoFlagged,
    bool DerateShown,
    double ReferenceEpr,
    double ManualEprLeft,
    double ManualEprRight,
    double Tat,
    bool OnGround,
    int LowerMode,
    IReadOnlyList<Bbs757EicasRawEnvelopeRow> EnvelopeRows);

/// <summary>
/// Reads the upper EICAS from the 757 module's memory through a derived layout (BBS757-D2, WASM-2: every address and
/// field offset from the layout): the upper EICAS reader's <see cref="IBbs757DisplaySampler{TReading}"/>. The object is
/// the one pointer in the gauge manager's span whose target starts with the class vptr; the computer, engines, TMC and
/// the rest hang off it, and the two message lists are statics. Owns its lease of the simulator handle. Every read is
/// bounds-checked against committed module memory (WASM-4), re-walked each call; the regions are asked about once per
/// reading (WASM-15).
/// <para>False when the object is not found (none or two), an object pointer is null or outside the module, or a fixed
/// field cannot be read: the memory moved or went away. A list read torn while BlueBird changes it is a reading, but
/// <see cref="Bbs757UpperEicasReading.Corrupt"/>: never a partial list.</para>
/// </summary>
public sealed class Bbs757UpperEicasReader : IBbs757DisplaySampler<Bbs757UpperEicasReading>
{
    /// <summary>The manual thrust reference flag, BlueBird's L:var (no prefix), as the draw reads it (S10).</summary>
    public const string ManualKey = "BBS757_THRREF_PULL";

    // A wasm32 pointer, i32 and UTF-32 code point are 4 bytes; a std::vector's layout (begin, end, capacity) is libc++
    // ABI, known to the reader, as Bbs757EfbMemLayout states for its vectors. Neither is BlueBird's: every offset into
    // BlueBird's own objects and structs is a layout field.
    private const int Word = 4;
    private const int VectorBegin = 0, VectorEnd = Word, VectorCap = 2 * Word;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The out value of a failed read: never shown, spoken or a baseline.</summary>
    private static readonly Bbs757UpperEicasReading NotRead = new(Array.Empty<Bbs757EicasMessage>(), 0, false, null, false,
        null, 0, 0, Array.Empty<Bbs757EicasValue>(), null, Array.Empty<string>(), false, Corrupt: true);

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Bbs757UpperEicasLayout Layout { get; }

    public Bbs757UpperEicasReader(ModuleMemory memory, IDisposable ownedSource, Bbs757UpperEicasLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
    }

    /// <summary>The upper EICAS object: the one manager pointer whose target carries its vptr. False when none in the
    /// manager's span does, or two do (the module is not where the anchor said, not ready, or not this build).</summary>
    public bool TryFindUpperEicas(out uint eicas)
    {
        eicas = 0;
        int found = 0;
        for (uint off = 0; off + Word <= Layout.ManagerSpan; off += Word)
        {
            if (!_memory.TryReadInt32(Layout.ManagerBase + off, out int pv)) continue;
            uint p = unchecked((uint)pv);
            if (p == 0 || !_memory.IsInsideModule(p, Word)) continue;
            if (_memory.TryReadInt32(p, out int vptr) && unchecked((uint)vptr) == Layout.UpperEicasVptr)
            {
                found++;
                eicas = p;
            }
        }
        if (found == 1) return true;
        eicas = 0;
        return false;
    }

    /// <summary>Whether this tick's simulator values have the manual thrust reference set: <see cref="ManualKey"/> read,
    /// finite and over 0.5.</summary>
    public static bool IsManual(Bbs757SimValues sim) => sim[ManualKey] is double v && double.IsFinite(v) && v > 0.5;

    /// <summary>A reading from memory alone: the thrust reference as if the manual flag were off.</summary>
    public bool TryRead(out Bbs757UpperEicasReading reading) => Read(manual: false, out reading);

    /// <summary>The monitor's read: memory, with the manual flag from the simulator (S10).</summary>
    public bool TryRead(Bbs757SimValues sim, out Bbs757UpperEicasReading reading) => Read(IsManual(sim), out reading);

    private bool Read(bool manual, out Bbs757UpperEicasReading reading)
    {
        if (!TryReadRaw(out var raw))
        {
            reading = NotRead;
            return false;
        }
        reading = Decode(Layout, raw, manual);
        return true;
    }

    /// <summary>The memory walk, inside one <see cref="ModuleMemory.RememberRegions"/> scope (WASM-15): every object and
    /// fixed field first (any failure: false), then the two lists and the component vector (any failure: torn), then
    /// the three vector headers again (changed: torn).</summary>
    internal bool TryReadRaw(out Bbs757UpperEicasRaw raw)
    {
        raw = null!;
        var l = Layout;
        using var regions = _memory.RememberRegions();
        if (!TryFindUpperEicas(out uint eicas)) return false;
        if (!Object(eicas, l.ToComputer, out uint computer) || !Object(eicas, l.ToEngineLeft, out uint engineLeft)
            || !Object(eicas, l.ToEngineRight, out uint engineRight) || !Object(eicas, l.ToLower, out uint lower)
            || !Object(eicas, l.ToPneumatic, out uint pneumatic) || !Object(eicas, l.ToFms, out uint fms)
            || !Object(eicas, l.ToFire, out uint fire) || !Object(eicas, l.ToReverserLeft, out uint reverserLeft)
            || !Object(eicas, l.ToReverserRight, out uint reverserRight) || !Object(eicas, l.ToTmc, out uint tmc)
            || !Object(eicas, l.ToTat, out uint tat))
            return false;

        if (!Flag(eicas, l.Powered, out bool powered) || !Flag(eicas, l.Failed, out bool failed)
            || !Flag(eicas, l.TimedBlank, out bool timedBlank) || !Flag(eicas, l.Compact, out bool compact)
            || !F32(eicas, l.EgtRed, out float egtRed) || !F32(eicas, l.EgtAmber, out float egtAmber)
            || !F32(eicas, l.N1Red, out float n1Red) || !F32(eicas, l.N1Amber, out float n1Amber)
            || !F32(eicas, l.MaxN1Left, out float maxN1Left) || !F32(eicas, l.MaxN1Right, out float maxN1Right)
            || !F32(eicas, l.MaxEgtLeft, out float maxEgtLeft) || !F32(eicas, l.MaxEgtRight, out float maxEgtRight)
            || !TryReadEnvelope(eicas, out var rows)
            || !Int(computer, l.ComputerPage, out int page) || !F64(computer, l.ComputerRecallTime, out double recallTime)
            || !Int(computer, l.ComputerBlank, out int blank) || !_memory.TryReadDouble(l.Clock, out double clock)
            || !TryReadEngine(engineLeft, out var left) || !TryReadEngine(engineRight, out var right)
            || !Int(reverserLeft, l.ReverserState, out int revLeft) || !Int(reverserRight, l.ReverserState, out int revRight)
            || !Flag(fire, l.FireSwitchLeft, out bool fireLeft) || !Flag(fire, l.FireSwitchRight, out bool fireRight)
            || !Flag(tmc, l.TmcFailed, out bool tmcFailed) || !Flag(tmc, l.TmcChannelLeft, out bool channelLeft)
            || !Flag(tmc, l.TmcChannelRight, out bool channelRight) || !Int(tmc, l.ThrustMode, out int mode)
            || !Int(tmc, l.ThrustNumber, out int number) || !Int(tmc, l.Derate, out int derate)
            || !Flags(tmc, l.DtoFlags, out int dtoSet) || !Flag(tmc, l.DerateShown, out bool derateShown)
            || !F64(tmc, l.ReferenceEpr, out double referenceEpr) || !F64(tmc, l.ManualEprLeft, out double manualLeft)
            || !F64(tmc, l.ManualEprRight, out double manualRight) || !F64(tat, l.TatValue, out double tatValue)
            || !Flag(fms, l.FmsOnGround, out bool onGround) || !Int(lower, l.LowerMode, out int lowerMode)
            || !At(pneumatic, l.PneumaticComponents, out uint components)
            || !Header(components, out var componentsHeader)
            || !Header(l.MainList, out var mainHeader)
            || !Header(l.CommunicationList, out var commHeader))
            return false;

        var main = Entries(mainHeader);
        var communication = Entries(commHeader);
        bool valvesRead = TryFindValves(componentsHeader.Begin, componentsHeader.End, out var valveLeft, out var valveRight);
        // A vector BlueBird changed while it was walked (a reallocation, or an erase lowering its end after shifting its
        // elements down) reads torn, whatever the walk found.
        if (!Header(l.MainList, out var mainAfter) || !Header(l.CommunicationList, out var commAfter)
            || !Header(components, out var componentsAfter))
            return false;
        bool torn = main is null || communication is null || !valvesRead
            || mainAfter != mainHeader || commAfter != commHeader || componentsAfter != componentsHeader;

        raw = new Bbs757UpperEicasRaw(
            Torn: torn,
            Main: torn ? Array.Empty<Bbs757EicasRawEntry>() : main!,
            Communication: torn ? Array.Empty<Bbs757EicasRawEntry>() : communication!,
            Page: page, RecallTime: recallTime, Blank: blank, Clock: clock,
            Powered: powered, Failed: failed, TimedBlank: timedBlank, Compact: compact,
            EgtRed: egtRed, EgtAmber: egtAmber, N1Red: n1Red, N1Amber: n1Amber,
            Left: left with { MaxN1 = maxN1Left, MaxEgt = maxEgtLeft, Reverser = revLeft, FireSwitch = fireLeft, AntiIce = valveLeft },
            Right: right with { MaxN1 = maxN1Right, MaxEgt = maxEgtRight, Reverser = revRight, FireSwitch = fireRight, AntiIce = valveRight },
            TmcFailed: tmcFailed, TmcChannelLeftFailed: channelLeft, TmcChannelRightFailed: channelRight,
            ThrustMode: mode, ThrustNumber: number, Derate: derate, DtoFlagged: dtoSet == l.DtoFlags.Length,
            DerateShown: derateShown, ReferenceEpr: referenceEpr, ManualEprLeft: manualLeft, ManualEprRight: manualRight,
            Tat: tatValue, OnGround: onGround, LowerMode: lowerMode, EnvelopeRows: rows);
        return true;
    }

    /// <summary>An engine's own fields (every alternate N1 flag read, any set counting).</summary>
    private bool TryReadEngine(uint engine, out Bbs757EicasRawEngine e)
    {
        e = default;
        var l = Layout;
        if (!Int(engine, l.EngineValid, out int valid) || !Int(engine, l.EngineType, out int type)
            || !At(engine, l.EngineBlock, out uint block)
            || !F64(block, l.BlockEgt, out double egt) || !F64(block, l.BlockN1, out double n1)
            || !F64(block, l.BlockN2Pw, out double n2) || !F64(block, l.BlockEpr, out double epr)
            || !Flags(engine, l.EngineN1AlternateFlags, out int alternateSet)
            || !F64(engine, l.EngineN1Alternate, out double alternate) || !Flag(engine, l.EngineCombustion, out bool combusting))
            return false;
        e = new Bbs757EicasRawEngine(valid, type, egt, n1, n2, epr, alternateSet > 0, alternate, combusting,
            MaxN1: 0, MaxEgt: 0, Reverser: 0, FireSwitch: false, AntiIce: null);
        return true;
    }

    /// <summary>The envelope rows, each read whatever its FL (the draw skips an unused one).</summary>
    private bool TryReadEnvelope(uint eicas, out IReadOnlyList<Bbs757EicasRawEnvelopeRow> rows)
    {
        rows = Array.Empty<Bbs757EicasRawEnvelopeRow>();
        var l = Layout;
        var read = new Bbs757EicasRawEnvelopeRow[Math.Max(0, l.EnvelopeRowCount)];
        for (int k = 0; k < read.Length; k++)
        {
            if (!At(eicas, (long)l.EnvelopeRows + (long)k * l.EnvelopeRowStride, out uint row)
                || !Int(row, l.EnvelopeRowFl, out int fl) || !Int(row, l.EnvelopeRowMin, out int min)
                || !Int(row, l.EnvelopeRowMax, out int max))
                return false;
            read[k] = new Bbs757EicasRawEnvelopeRow(fl, min, max);
        }
        rows = read;
        return true;
    }

    /// <summary>A std::vector's header: its begin, end and capacity pointers.</summary>
    private readonly record struct VectorHeader(uint Begin, uint End, uint Cap);

    /// <summary>A vector's header at <paramref name="at"/>, its three pointers in one read (a fixed field).</summary>
    private bool Header(uint at, out VectorHeader header)
    {
        header = default;
        Span<byte> bytes = stackalloc byte[3 * Word];
        if (!_memory.TryRead(at, bytes)) return false;
        header = new VectorHeader(BinaryPrimitives.ReadUInt32LittleEndian(bytes[VectorBegin..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[VectorEnd..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[VectorCap..]));
        return true;
    }

    /// <summary>A message list's entries in vector order; null when torn: end before begin, cap before end, not whole
    /// pointers, more than <see cref="Bbs757UpperEicasLayout.MaxMessages"/>, storage, an entry or its text null or
    /// outside the module, one entry twice, a length out of range or a code point that is not a Unicode scalar value.
    /// An empty vector (begin = end, both 0 included) is an empty list.</summary>
    private List<Bbs757EicasRawEntry>? Entries(VectorHeader h)
    {
        if (h.End < h.Begin || h.Cap < h.End || (h.End - h.Begin) % Word != 0) return null;
        uint count = (h.End - h.Begin) / Word;
        if (count == 0) return new List<Bbs757EicasRawEntry>();
        if (h.Begin == 0 || count > (uint)Math.Max(0, Layout.MaxMessages)) return null;
        var list = new List<Bbs757EicasRawEntry>((int)count);
        var seen = new HashSet<uint>();
        for (uint i = 0; i < count; i++)
        {
            if (!Int(h.Begin, (int)(i * Word), out int pv)) return null;
            uint entry = unchecked((uint)pv);
            if (entry == 0 || !seen.Add(entry) || !TryReadEntry(entry, out var e)) return null;
            list.Add(e);
        }
        return list;
    }

    private bool TryReadEntry(uint entry, out Bbs757EicasRawEntry e)
    {
        e = default;
        var l = Layout;
        if (!Int(entry, l.EntryText, out int textPv) || !Int(entry, l.EntryLength, out int length)
            || !U16(entry, l.EntryId, out int id) || !Flag(entry, l.EntryCancelled, out bool cancelled)
            || !Int(entry, l.EntryLevel, out int level))
            return false;
        uint text = unchecked((uint)textPv);
        if (text == 0 || length < 1 || length > l.MaxTextLength || length >= int.MaxValue / Word
            || !TryReadText(text, length, out string s))
            return false;
        e = new Bbs757EicasRawEntry(id, s, level, cancelled);
        return true;
    }

    /// <summary>A UTF-32 text of <paramref name="length"/> code points, each a Unicode scalar value other than 0, followed
    /// by the literal's 0 terminator, where the draw stops: a 0 before it, or anything else in its place, is a torn
    /// read.</summary>
    private bool TryReadText(uint at, int length, out string text)
    {
        text = "";
        var bytes = new byte[(length + 1) * Word];
        if (!_memory.TryRead(at, bytes) || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(length * Word)) != 0) return false;
        var sb = new StringBuilder(length);
        Span<char> utf16 = stackalloc char[2];
        for (int i = 0; i < length; i++)
        {
            uint codePoint = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * Word));
            if (codePoint == 0 || !Rune.TryCreate(codePoint, out Rune r)) return false;
            sb.Append(utf16[..r.EncodeToUtf16(utf16)]);
        }
        text = sb.ToString();
        return true;
    }

    /// <summary>The two engine anti-ice valves: the first component carrying each side's id, as the draw's lookup
    /// finds it. False when the vector is torn: end before begin, not whole components, more than
    /// <see cref="Bbs757UpperEicasLayout.MaxComponents"/>, null storage, or a component outside the module.</summary>
    private bool TryFindValves(uint begin, uint end, out Bbs757EicasRawValve? left, out Bbs757EicasRawValve? right)
    {
        left = right = null;
        var l = Layout;
        if (begin == end) return true;
        if (end < begin || begin == 0 || l.ComponentStride <= 0 || (end - begin) % (uint)l.ComponentStride != 0) return false;
        uint count = (end - begin) / (uint)l.ComponentStride;
        if (count > (uint)Math.Max(0, l.MaxComponents)) return false;
        for (uint i = 0; i < count; i++)
        {
            uint component = begin + i * (uint)l.ComponentStride;
            if (!Int(component, l.ComponentId, out int id)) return false;
            if (left is null && id == l.TaiLeftId)
            {
                if (!TryReadValve(component, out var v)) return false;
                left = v;
            }
            if (right is null && id == l.TaiRightId)
            {
                if (!TryReadValve(component, out var v)) return false;
                right = v;
            }
        }
        return true;
    }

    private bool TryReadValve(uint component, out Bbs757EicasRawValve valve)
    {
        valve = default;
        if (!F64(component, Layout.ComponentPosition, out double position) || !Int(component, Layout.ComponentState, out int state))
            return false;
        valve = new Bbs757EicasRawValve(position, state);
        return true;
    }

    /// <summary>
    /// The reading BlueBird's upper EICAS draw makes of <paramref name="raw"/> (S10), with
    /// <paramref name="manual"/> the simulator's manual thrust reference flag:
    /// <list type="bullet">
    /// <item>Messages: the main list in order, shown while uncancelled and within the screen's lines
    /// (<see cref="Bbs757UpperEicasLayout.CommunicationLinesBase"/> less the communication count from two such messages
    /// on, else <see cref="Bbs757UpperEicasLayout.LinesPerPage"/>); then the communication list from its last entry,
    /// all shown but entry 0 while RECALL shows. RECALL shows for <see cref="Bbs757UpperEicasLayout.RecallShowSeconds"/>
    /// after the press; PAGE while the communication list is empty or RECALL shows, on page 1 with every line drawn or
    /// on another page with any drawn.</item>
    /// <item>The gauges: EPR (the float, 2 decimals), N1 (1 decimal, the alternate when flagged) and EGT (whole, from 0)
    /// blank on an invalid engine and on the PW2000 at low N2 or N1; N1 (with TAI and the N1 max) not drawn while its red
    /// limit is not above 0, EGT (with the EGT max) likewise and in compact mode; N1 and EGT red at their limit, amber at
    /// theirs unless the mode inhibit (N1: RB211 only); TAT signed with its c; TAI and REV only while drawn; the
    /// reference EPR over each valid engine with the thrust reference; the max memories over each valid engine while one
    /// is set.</item>
    /// <item>The thrust reference: hidden with a reverser deployed or without the manual flag or a valid TMC; MAN when
    /// manual; else the mode's word (D-TO for TO with both D-TO flags), its number and the derate, the derate alone
    /// under an empty word.</item>
    /// <item>The in-flight start block: airborne, not compact, the lower ENGINE page, and an engine not running whose
    /// fire switch is in.</item>
    /// </list>
    /// Corrupt when the walk was torn, a level is missing from <see cref="Bbs757UpperEicasLayout.Levels"/>, or a value
    /// the reading uses is not finite while the screen is lit (dark, such a value reads blank). Every number is
    /// formatted with the invariant culture; BlueBird's alignment padding is dropped, as column alignment, not a word.
    /// </summary>
    public static Bbs757UpperEicasReading Decode(Bbs757UpperEicasLayout layout, Bbs757UpperEicasRaw raw, bool manual)
    {
        // The screen's own power: its gauge powered, not failed or timed blank, and the computer not blanking it.
        bool powered = raw.Powered && !raw.Failed && !raw.TimedBlank && raw.Blank == 0;
        bool corrupt = raw.Torn;
        // A value the reading uses, or null when it is not a number: torn while the screen is lit, blank while it is dark,
        // where BlueBird may leave anything in its fields (a dark reading is never spoken). A torn list is torn either way.
        double? Use(double v)
        {
            if (double.IsFinite(v)) return v;
            if (powered) corrupt = true;
            return null;
        }
        string Level(int value)
        {
            if (layout.Levels.Words.TryGetValue(value, out string? word)) return word;
            corrupt = true;
            return Bbs757WordTable.Unrecognised;
        }

        // The messages (22920).
        int n = raw.Communication.Count;
        int lines = n >= 2 ? layout.CommunicationLinesBase - n : layout.LinesPerPage;
        double? sinceRecall = Use(raw.Clock) - Use(raw.RecallTime);
        bool recall = sinceRecall >= 0 && sinceRecall < layout.RecallShowSeconds;
        var messages = new List<Bbs757EicasMessage>(raw.Main.Count + n);
        int drawn = 0, cancelled = 0;
        foreach (var e in raw.Main)
        {
            bool shown = !e.Cancelled && drawn < lines;
            if (e.Cancelled) cancelled++;
            if (shown) drawn++;
            messages.Add(new Bbs757EicasMessage(e.Id, e.Text, Level(e.Level), e.Cancelled, shown));
        }
        for (int j = n - 1; j >= 0; j--)
        {
            var e = raw.Communication[j];
            messages.Add(new Bbs757EicasMessage(e.Id, e.Text, Level(e.Level), Cancelled: false, Shown: !(j == 0 && recall)));
        }
        bool pageShown = (n == 0 || recall) && ((raw.Page == 1 && drawn == lines) || (raw.Page != 1 && drawn != 0));

        // The gauges (22929, 22951, 22952). The N1 function (with TAI and the N1 max) draws nothing while the N1 red
        // limit is not above 0; the EGT function (with the EGT max) likewise, and it is not called in compact mode.
        bool tmcValid = !raw.TmcFailed && (!raw.TmcChannelLeftFailed || !raw.TmcChannelRightFailed);
        bool inhibit = tmcValid && layout.InhibitModes.Contains(raw.ThrustMode);
        bool n1Drawn = Use(raw.N1Red) > 0;
        bool egtDrawn = !raw.Compact && Use(raw.EgtRed) > 0;
        bool Valid(Bbs757EicasRawEngine e) => e.Valid != layout.EngineInvalidValue;
        // EPR is drawn from the float (22929 ;4272): the float's own value, 2 decimals.
        string? EprText(double v) => Use((float)v) is double f ? f.ToString("F2", Inv) : null;
        (string? Epr, string? N1, string? N1Color, string? Egt, string? EgtColor) Gauges(Bbs757EicasRawEngine e)
        {
            if (!Valid(e)) return default;
            bool pw = e.Type == layout.EngineTypePw;
            bool lowN2 = pw && Use(e.N2) <= layout.PwBlankBelow;
            string? epr = lowN2 ? null : EprText(e.Epr);

            string? n1Text = null, n1Color = null;
            if (n1Drawn && Use(e.AlternateFlagged ? e.N1Alternate : e.N1) is double n1 && !(pw && n1 <= layout.PwBlankBelow))
            {
                n1Text = n1.ToString("F1", Inv);
                n1Color = n1 >= raw.N1Red ? Bbs757EicasValue.Red
                    : n1 >= Use(raw.N1Amber) && !(!pw && inhibit) ? Bbs757EicasValue.Amber : null;
            }

            string? egtText = null, egtColor = null;
            if (egtDrawn && !lowN2 && Use(e.Egt) is double egtValue)
            {
                double egt = Math.Max(0, egtValue);
                egtText = Whole(egt);
                egtColor = egt >= raw.EgtRed ? Bbs757EicasValue.Red
                    : egt >= Use(raw.EgtAmber) && !inhibit ? Bbs757EicasValue.Amber : null;
            }
            return (epr, n1Text, n1Color, egtText, egtColor);
        }
        var left = Gauges(raw.Left);
        var right = Gauges(raw.Right);
        var values = new List<Bbs757EicasValue>
        {
            new(Bbs757EicasValue.EprLabel, left.Epr, right.Epr, null, null),
            new(Bbs757EicasValue.N1Label, left.N1, right.N1, left.N1Color, right.N1Color),
            new(Bbs757EicasValue.EgtLabel, left.Egt, right.Egt, left.EgtColor, right.EgtColor),
            new(Bbs757EicasValue.TatLabel, Use(raw.Tat) is double tat ? Tat(tat) : null, null, null, null),
        };

        string? Tai(Bbs757EicasRawValve? valve) =>
            n1Drawn && valve is { } v && Use(v.Position) >= layout.TaiMinOpen && layout.TaiStates.Contains(v.State) ? layout.TaiWord : null;
        string? taiLeft = Tai(raw.Left.AntiIce), taiRight = Tai(raw.Right.AntiIce);
        if (taiLeft is not null || taiRight is not null)
            values.Add(new Bbs757EicasValue(Bbs757EicasValue.TaiLabel, taiLeft, taiRight, null, null));

        // REV (22928): amber while the reverser moves, green once fully deployed, nothing in any other state.
        string? RevColor(int state) =>
            state == layout.ReverserMoving ? Bbs757EicasValue.Amber : state == layout.ReverserDeployed ? Bbs757EicasValue.Green : null;
        string? revLeftColor = RevColor(raw.Left.Reverser), revRightColor = RevColor(raw.Right.Reverser);
        if (revLeftColor is not null || revRightColor is not null)
            values.Add(new Bbs757EicasValue(Bbs757EicasValue.RevLabel, revLeftColor is null ? null : layout.RevWord,
                revRightColor is null ? null : layout.RevWord, revLeftColor, revRightColor));

        // The thrust reference (22929), as drawn: the word, its number, the derate.
        string? thrust = null;
        bool hidden = raw.Left.Reverser == layout.ReverserDeployed || raw.Right.Reverser == layout.ReverserDeployed
            || !(manual || tmcValid);
        if (!hidden)
        {
            if (manual)
                thrust = layout.ManualWord;
            else
            {
                // The word, its number after it, and the derate above it, which is drawn under an empty word too.
                string? word = raw.ThrustMode == 0 && tmcValid && raw.DtoFlagged ? layout.DtoWord
                    : layout.ThrustWords.Words.TryGetValue(raw.ThrustMode, out string? w) ? w : null;
                string? derate = raw.DerateShown && tmcValid ? Derate(raw.Derate) : null;
                if (word is not null && layout.NumberedModes.Contains(raw.ThrustMode)
                    && layout.ThrustNumbers.Words.TryGetValue(raw.ThrustNumber, out string? number))
                    word += " " + number;
                thrust = word is null ? derate : derate is null ? word : word + ", " + derate;
            }
            // The reference EPR over each valid engine's dial.
            string? Reference(Bbs757EicasRawEngine e, double epr) => Valid(e) ? EprText(epr) : null;
            string? referenceLeft = Reference(raw.Left, manual ? raw.ManualEprLeft : raw.ReferenceEpr);
            string? referenceRight = Reference(raw.Right, manual ? raw.ManualEprRight : raw.ReferenceEpr);
            if (referenceLeft is not null || referenceRight is not null)
                values.Add(new Bbs757EicasValue(Bbs757EicasValue.EprReferenceLabel, referenceLeft, referenceRight, null, null));
        }

        // The max-exceedance memories, drawn inside the N1 and EGT functions over each valid engine while set: what a
        // sighted pilot sees after an exceedance, until the MAX IND button clears them. A row while either side is drawn.
        string? MaxText(Bbs757EicasRawEngine e, float memory, Func<double, string> format) =>
            Valid(e) && memory != 0 && Use(memory) is double m ? format(m) : null;
        void AddMax(bool drawn, string label, float leftMemory, float rightMemory, Func<double, string> format)
        {
            if (!drawn) return;
            string? leftText = MaxText(raw.Left, leftMemory, format), rightText = MaxText(raw.Right, rightMemory, format);
            if (leftText is not null || rightText is not null) values.Add(new Bbs757EicasValue(label, leftText, rightText, null, null));
        }
        AddMax(n1Drawn, Bbs757EicasValue.N1MaxLabel, raw.Left.MaxN1, raw.Right.MaxN1, v => v.ToString("F1", Inv));
        AddMax(egtDrawn, Bbs757EicasValue.EgtMaxLabel, raw.Left.MaxEgt, raw.Right.MaxEgt, v => Whole(Math.Max(0, v)));

        // The in-flight start block (22932): its lines as BlueBird draws them, for the window.
        var envelope = new List<string>();
        static bool Out(Bbs757EicasRawEngine e) => !e.Combusting && !e.FireSwitch;
        if (!raw.OnGround && !raw.Compact && raw.LowerMode == layout.EnvelopeLowerMode && (Out(raw.Left) || Out(raw.Right)))
        {
            envelope.Add(layout.EnvelopeTitle);
            foreach (var row in raw.EnvelopeRows)
                if (row.Fl != 0)
                    envelope.Add(Invariant($"{layout.EnvelopeFl} {row.Fl} {row.Min}-{row.Max} {layout.EnvelopeKts}"));
        }

        // Each engine's own N1, whatever is drawn (the alternate, or nothing on an invalid engine), must be a number while
        // the screen is lit: a range check of the reading (B757-30's Corrupt), never compared with the simulator.
        _ = Use(raw.Left.N1);
        _ = Use(raw.Right.N1);
        // PAGE and RECALL in the layout's words; the draw's column padding before the number is dropped.
        string? pageLine = pageShown ? layout.PageWord + " " + raw.Page.ToString(Inv) : null;
        string? recallLine = recall ? layout.RecallWord : null;
        return new Bbs757UpperEicasReading(messages.AsReadOnly(), raw.Page, pageShown, pageLine, recall, recallLine,
            raw.RecallTime, cancelled, values.AsReadOnly(), thrust, envelope.AsReadOnly(), powered, corrupt);
    }

    /// <summary>A readout's whole number, truncated toward zero (EGT, as BlueBird's <c>trunc</c>).</summary>
    private static string Whole(double v) => Math.Truncate(v).ToString("F0", Inv);

    /// <summary>TAT: truncated, signed, then BlueBird's small c ("+11c", "+5c", "-15c"; the alignment space before a
    /// one-digit value is dropped, as column alignment, not a word).</summary>
    private static string Tat(double celsius)
    {
        double v = Math.Truncate(celsius);
        return (v < 0 ? "-" : "+") + Math.Abs(v).ToString("F0", Inv) + "c";
    }

    /// <summary>The derate text: "+25c", "0c", "-5c" (the alignment space is dropped, as column alignment, not a
    /// word).</summary>
    private static string Derate(int d) => (d > 0 ? "+" : "") + d.ToString(Inv) + "c";

    /// <summary><paramref name="b"/> plus <paramref name="offset"/>, false when the offset is negative or the sum
    /// leaves the 32-bit address space (it never wraps into the module).</summary>
    private static bool At(uint b, long offset, out uint address)
    {
        long a = b + offset;
        address = unchecked((uint)a);
        return offset >= 0 && a <= uint.MaxValue;
    }

    private bool Int(uint b, int offset, out int v)
    {
        v = 0;
        return At(b, offset, out uint a) && _memory.TryReadInt32(a, out v);
    }

    private bool F64(uint b, int offset, out double v)
    {
        v = 0;
        return At(b, offset, out uint a) && _memory.TryReadDouble(a, out v);
    }

    /// <summary>An f32 (ModuleMemory reads no f32: four bytes, little-endian).</summary>
    private bool F32(uint b, int offset, out float v)
    {
        v = 0;
        Span<byte> bytes = stackalloc byte[4];
        if (!At(b, offset, out uint a) || !_memory.TryRead(a, bytes)) return false;
        v = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return true;
    }

    private bool U16(uint b, int offset, out int v)
    {
        v = 0;
        Span<byte> bytes = stackalloc byte[2];
        if (!At(b, offset, out uint a) || !_memory.TryRead(a, bytes)) return false;
        v = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        return true;
    }

    private bool Flag(uint b, int offset, out bool set)
    {
        set = false;
        if (!At(b, offset, out uint a) || !_memory.TryReadByte(a, out byte v)) return false;
        set = v != 0;
        return true;
    }

    /// <summary>Every one of the flag bytes read (a fixed field each), and how many are set.</summary>
    private bool Flags(uint b, int[] offsets, out int set)
    {
        set = 0;
        foreach (int offset in offsets)
        {
            if (!Flag(b, offset, out bool on)) return false;
            if (on) set++;
        }
        return true;
    }

    /// <summary>An object pointer at <paramref name="offset"/>: false when null or outside the module.</summary>
    private bool Object(uint b, int offset, out uint p)
    {
        p = 0;
        if (!Int(b, offset, out int v)) return false;
        p = unchecked((uint)v);
        return p != 0 && _memory.IsInsideModule(p, Word);
    }

    public void Dispose() => _ownedSource.Dispose();
}
