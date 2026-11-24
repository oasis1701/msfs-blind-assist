using System.Buffers.Binary;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The 757 upper EICAS sampler finds its object in the gauge manager by its vptr, walks the two message lists,
/// the engines, the TMC and the rest through the layout, and decodes what BlueBird draws: the messages in order with
/// their cancel marks and what shows, PAGE and RECALL, the gauge rows with BlueBird's blanking and colors, the thrust
/// reference and the in-flight start block. A torn list is one corrupt reading; an object that is gone is a failed
/// read.</summary>
public class Bbs757UpperEicasReaderTests
{
    private const ulong Base = 0x20000000;
    private const int Size = 0x130000;

    // Where this test's memory puts each object; the statics (manager, lists, clock) are the sample layout's own.
    private const uint Upper = 0x10000, Computer = 0x11000, EngineLeft = 0x14000, EngineRight = 0x18000, Lower = 0x1C000,
        Pneumatic = 0x1D000, Components = 0x1E000, Fms = 0x20000, Fire = 0x21000, ReverserLeft = 0x22000,
        ReverserRight = 0x22800, Tmc = 0x23000, Tat = 0x24000, Decoy = 0x25000, MainStorage = 0x30000,
        CommStorage = 0x31000, MainEntries = 0x40000, CommEntries = 0x48000, MainTexts = 0x50000, CommTexts = 0x58000,
        Outside = 0x200000;
    private const uint EntryPitch = 0x40, TextPitch = 0x100, UpperSlot = 12 * 8, DecoySlot = 13 * 8, DecoyVptr = 0x133d8;

    private static readonly Bbs757UpperEicasLayout L = Bbs757UpperEicasLayoutTests.Sample();

    /// <summary>The pneumatic components in the order BlueBird adds them (16: the left engine anti-ice valve, 22: the
    /// right).</summary>
    private static readonly int[] ComponentIds =
        { 30, 31, 16, 17, 15, 14, 12, 13, 11, 22, 23, 21, 20, 18, 19, 10, 1, 2, 35, 36, 3, 4, 5, 7, 8, 9, 24, 25, 32, 33, 34, 40, 26, 27, 28, 41 };

    private static Bbs757EicasRawEntry Advisory(int id, string text) => new(id, text, 2, false);

    private static Bbs757EicasRawEntry Communication(int id, string text) => new(id, text, 1, false);

    /// <summary>The second capture's list (both engines at idle, packs off): five advisories, newest first.</summary>
    private static readonly Bbs757EicasRawEntry[] LiveList =
    {
        Advisory(141, " L ENG BLEED OFF"), Advisory(199, " R ENG BLEED OFF"), Advisory(222, " R PACK OFF"),
        Advisory(164, " L PACK OFF"), Advisory(160, " PARKING BRAKE"),
    };

    private static readonly Bbs757EicasRawEngine IdleLeft = new(Valid: 0, Type: 0, Egt: 357.84, N1: 19.930, N2: 35.1,
        Epr: 1.0300, AlternateFlagged: false, N1Alternate: 0, Combusting: true, MaxN1: 0, MaxEgt: 0, Reverser: 0,
        FireSwitch: false, AntiIce: new Bbs757EicasRawValve(0, 2));

    /// <summary>The live screen at the gate: the RB211 at idle, TMC valid in TO, TAT +11c, the lower ENGINE page.</summary>
    private static Bbs757UpperEicasRaw Live() => new(
        Torn: false, Main: LiveList, Communication: Array.Empty<Bbs757EicasRawEntry>(), Page: 1, RecallTime: 4023.89,
        Blank: 0, Clock: 4096.29, Powered: true, Failed: false, TimedBlank: false, Compact: false, EgtRed: 850,
        EgtAmber: 795, N1Red: 108.8f, N1Amber: 108.4f, Left: IdleLeft, Right: IdleLeft with { Egt = 360.68 },
        TmcFailed: false, TmcChannelLeftFailed: false, TmcChannelRightFailed: false, ThrustMode: 0, ThrustNumber: 0,
        Derate: 0, DtoFlagged: false, DerateShown: false, ReferenceEpr: 1.7073, ManualEprLeft: 1.5, ManualEprRight: 1.5,
        Tat: 11.992, OnGround: true, LowerMode: 1, EnvelopeRows: new Bbs757EicasRawEnvelopeRow[3]);

    // ---- memory ----

    private static void I32(byte[] b, long at, long v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(checked((int)at)), unchecked((int)v));

    private static void F64(byte[] b, long at, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(checked((int)at)), v);

    private static void F32(byte[] b, long at, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(checked((int)at)), v);

    private static void Flag(byte[] b, long at, bool v) => b[checked((int)at)] = v ? (byte)1 : (byte)0;

    private static uint P(byte[] b, long at) => unchecked((uint)BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(checked((int)at))));

    /// <summary>One module's memory holding <paramref name="raw"/> where the sample layout says BlueBird keeps it.</summary>
    private static byte[] Place(Bbs757UpperEicasRaw raw)
    {
        var b = new byte[Size];
        I32(b, L.ManagerBase + UpperSlot, Upper);
        I32(b, L.ManagerBase + DecoySlot, Decoy);
        I32(b, Decoy, DecoyVptr);
        I32(b, Upper, L.UpperEicasVptr);
        foreach (var (offset, target) in new[]
        {
            (L.ToComputer, Computer), (L.ToEngineLeft, EngineLeft), (L.ToEngineRight, EngineRight), (L.ToLower, Lower),
            (L.ToPneumatic, Pneumatic), (L.ToFms, Fms), (L.ToFire, Fire), (L.ToReverserLeft, ReverserLeft),
            (L.ToReverserRight, ReverserRight), (L.ToTmc, Tmc), (L.ToTat, Tat),
        })
            I32(b, Upper + offset, target);

        Flag(b, Upper + L.Powered, raw.Powered);
        Flag(b, Upper + L.Failed, raw.Failed);
        Flag(b, Upper + L.TimedBlank, raw.TimedBlank);
        Flag(b, Upper + L.Compact, raw.Compact);
        F32(b, Upper + L.EgtRed, raw.EgtRed);
        F32(b, Upper + L.EgtAmber, raw.EgtAmber);
        F32(b, Upper + L.N1Red, raw.N1Red);
        F32(b, Upper + L.N1Amber, raw.N1Amber);
        F32(b, Upper + L.MaxN1Left, raw.Left.MaxN1);
        F32(b, Upper + L.MaxN1Right, raw.Right.MaxN1);
        F32(b, Upper + L.MaxEgtLeft, raw.Left.MaxEgt);
        F32(b, Upper + L.MaxEgtRight, raw.Right.MaxEgt);
        for (int k = 0; k < raw.EnvelopeRows.Count; k++)
        {
            long row = Upper + L.EnvelopeRows + k * L.EnvelopeRowStride;
            I32(b, row + L.EnvelopeRowFl, raw.EnvelopeRows[k].Fl);
            I32(b, row + L.EnvelopeRowMin, raw.EnvelopeRows[k].Min);
            I32(b, row + L.EnvelopeRowMax, raw.EnvelopeRows[k].Max);
        }

        I32(b, Computer + L.ComputerPage, raw.Page);
        F64(b, Computer + L.ComputerRecallTime, raw.RecallTime);
        I32(b, Computer + L.ComputerBlank, raw.Blank);
        F64(b, L.Clock, raw.Clock);

        PlaceEngine(b, EngineLeft, raw.Left);
        PlaceEngine(b, EngineRight, raw.Right);
        Flag(b, Fire + L.FireSwitchLeft, raw.Left.FireSwitch);
        Flag(b, Fire + L.FireSwitchRight, raw.Right.FireSwitch);
        I32(b, ReverserLeft + L.ReverserState, raw.Left.Reverser);
        I32(b, ReverserRight + L.ReverserState, raw.Right.Reverser);
        I32(b, Lower + L.LowerMode, raw.LowerMode);
        Flag(b, Fms + L.FmsOnGround, raw.OnGround);
        F64(b, Tat + L.TatValue, raw.Tat);

        Flag(b, Tmc + L.TmcFailed, raw.TmcFailed);
        Flag(b, Tmc + L.TmcChannelLeft, raw.TmcChannelLeftFailed);
        Flag(b, Tmc + L.TmcChannelRight, raw.TmcChannelRightFailed);
        I32(b, Tmc + L.ThrustMode, raw.ThrustMode);
        I32(b, Tmc + L.ThrustNumber, raw.ThrustNumber);
        I32(b, Tmc + L.Derate, raw.Derate);
        foreach (int flag in L.DtoFlags) Flag(b, Tmc + flag, raw.DtoFlagged);
        Flag(b, Tmc + L.DerateShown, raw.DerateShown);
        F64(b, Tmc + L.ReferenceEpr, raw.ReferenceEpr);
        F64(b, Tmc + L.ManualEprLeft, raw.ManualEprLeft);
        F64(b, Tmc + L.ManualEprRight, raw.ManualEprRight);

        I32(b, Pneumatic + L.PneumaticComponents, Components);
        I32(b, Pneumatic + L.PneumaticComponents + 4, Components + ComponentIds.Length * L.ComponentStride);
        I32(b, Pneumatic + L.PneumaticComponents + 8, Components + 40 * L.ComponentStride);   // reserved for 40
        for (int i = 0; i < ComponentIds.Length; i++)
        {
            long c = Components + i * L.ComponentStride;
            int id = ComponentIds[i];
            Bbs757EicasRawValve? valve = id == L.TaiLeftId ? raw.Left.AntiIce : id == L.TaiRightId ? raw.Right.AntiIce : new(0, 2);
            I32(b, c + L.ComponentId, valve is null ? 99 : id);
            F64(b, c + L.ComponentPosition, valve?.Position ?? 0);
            I32(b, c + L.ComponentState, valve?.State ?? 2);
        }

        PlaceList(b, L.MainList, MainStorage, MainEntries, MainTexts, raw.Main);
        PlaceList(b, L.CommunicationList, CommStorage, CommEntries, CommTexts, raw.Communication);
        return b;
    }

    private static void PlaceEngine(byte[] b, uint engine, Bbs757EicasRawEngine e)
    {
        I32(b, engine + L.EngineValid, e.Valid);
        I32(b, engine + L.EngineType, e.Type);
        F64(b, engine + L.EngineBlock + L.BlockEgt, e.Egt);
        F64(b, engine + L.EngineBlock + L.BlockN1, e.N1);
        F64(b, engine + L.EngineBlock + L.BlockN2Pw, e.N2);
        F64(b, engine + L.EngineBlock + L.BlockEpr, e.Epr);
        F64(b, engine + L.EngineN1Alternate, e.N1Alternate);
        Flag(b, engine + L.EngineN1AlternateFlags[0], e.AlternateFlagged);
        Flag(b, engine + L.EngineCombustion, e.Combusting);
    }

    /// <summary>A list's (begin, end, cap) at <paramref name="header"/>, its entry pointers at <paramref name="storage"/>,
    /// each entry and its UTF-32 text (null-terminated, as the definitions keep it) at its own pitch.</summary>
    private static void PlaceList(byte[] b, uint header, uint storage, uint entries, uint texts, IReadOnlyList<Bbs757EicasRawEntry> list)
    {
        uint end = storage + 4 * (uint)list.Count;
        I32(b, header, list.Count == 0 ? 0 : storage);
        I32(b, header + 4, list.Count == 0 ? 0 : end);
        I32(b, header + 8, list.Count == 0 ? 0 : end + 16);
        for (int i = 0; i < list.Count; i++)
        {
            uint entry = entries + EntryPitch * (uint)i, text = texts + TextPitch * (uint)i;
            I32(b, storage + 4 * i, entry);
            var runes = list[i].Text.EnumerateRunes().ToArray();
            for (int k = 0; k < runes.Length; k++) I32(b, text + 4 * k, runes[k].Value);
            I32(b, entry + L.EntryText, text);
            I32(b, entry + L.EntryLength, runes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan((int)(entry + L.EntryId)), (ushort)list[i].Id);
            Flag(b, entry + L.EntryCancelled, list[i].Cancelled);
            I32(b, entry + L.EntryLevel, list[i].Level);
        }
    }

    private static FakeProcessMemory Source(byte[] bytes) =>
        new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);

    private static Bbs757UpperEicasReader Reader(byte[] bytes, Bbs757UpperEicasLayout? layout = null)
    {
        var src = Source(bytes);
        return new Bbs757UpperEicasReader(new ModuleMemory(src, Base), src, layout ?? L);
    }

    private static Bbs757UpperEicasReading Read(byte[] bytes)
    {
        using var r = Reader(bytes);
        Assert.True(r.TryRead(out var reading));
        return reading;
    }

    private static Bbs757UpperEicasReading Decode(Bbs757UpperEicasRaw raw, bool manual = false) =>
        Bbs757UpperEicasReader.Decode(L, raw, manual);

    private static Bbs757SimValues Sim(double? manual) =>
        new(manual is double v ? new Dictionary<string, double> { [Bbs757UpperEicasReader.ManualKey] = v } : new Dictionary<string, double>());

    private static Bbs757EicasValue? Row(Bbs757UpperEicasReading reading, string label) =>
        reading.Values.Where(v => v.Label == label).Cast<Bbs757EicasValue?>().SingleOrDefault();

    private static Bbs757EicasValue V(string label, string? left, string? right = null, string? leftColor = null, string? rightColor = null) =>
        new(label, left, right, leftColor, rightColor);

    // ---- the memory walk ----

    [Fact]
    public void Reads_the_live_list_in_order()
    {
        var eicas = Read(Place(Live()));
        Assert.False(eicas.Corrupt);
        Assert.Equal(new[] { " L ENG BLEED OFF", " R ENG BLEED OFF", " R PACK OFF", " L PACK OFF", " PARKING BRAKE" },
            eicas.Messages.Select(m => m.Text));
        Assert.Equal(LiveList.Select(e => e.Id), eicas.Messages.Select(m => m.Id));
        Assert.All(eicas.Messages, m =>
        {
            Assert.Equal("advisory", m.Level);
            Assert.False(m.Cancelled);
            Assert.True(m.Shown);
        });
        Assert.Equal((1, false, false, 0), (eicas.Page, eicas.PageShown, eicas.Recall, eicas.Cancelled));
        Assert.Equal(4023.89, eicas.RecallTime);
        Assert.True(eicas.Powered);

        // The rest of that screen: TO, 1.71 over each dial, TAT +11c, the idle readouts.
        Assert.Equal("TO", eicas.ThrustReference);
        Assert.Equal(new[]
        {
            V("EPR", "1.03", "1.03"), V("N1", "19.9", "19.9"), V("EGT", "357", "360"), V("TAT", "+11c"),
            V("EPR reference", "1.71", "1.71"),
        }, eicas.Values);
        Assert.Empty(eicas.Envelope);
    }

    [Fact]
    public void Every_field_is_read_from_its_layout_place()
    {
        var raw = Live() with
        {
            Main = [new(7, "AFT CARGO FIRE", 4, false), new(141, " L ENG BLEED OFF", 2, true)],
            Communication = [new(258, " CABIN ALERT", 1, false), new(260, " FMC", 0, true)],
            Page = 3, RecallTime = 1975.767, Blank = 25, Clock = 1976.0,
            Powered = false, Failed = true, TimedBlank = true, Compact = true,
            EgtRed = 851, EgtAmber = 796.5f, N1Red = 108.9f, N1Amber = 108.5f,
            Left = new(Valid: 2, Type: 1, Egt: 401.5, N1: 25.25, N2: 33.3, Epr: 1.11, AlternateFlagged: true,
                N1Alternate: 26.5, Combusting: false, MaxN1: 109.2f, MaxEgt: 861.5f, Reverser: 1, FireSwitch: true,
                AntiIce: new(100, 0)),
            Right = new(Valid: 1, Type: 0, Egt: 402.5, N1: 27.75, N2: 44.4, Epr: 1.22, AlternateFlagged: false,
                N1Alternate: 3.5, Combusting: true, MaxN1: 0, MaxEgt: 870.25f, Reverser: 2, FireSwitch: false,
                AntiIce: new(50.5, 1)),
            TmcFailed = true, TmcChannelLeftFailed = true, TmcChannelRightFailed = false,
            ThrustMode = 7, ThrustNumber = 2, Derate = 25, DtoFlagged = true, DerateShown = true,
            ReferenceEpr = 1.65, ManualEprLeft = 1.52, ManualEprRight = 1.53, Tat = -15.25, OnGround = false, LowerMode = 3,
            EnvelopeRows = [new(250, 260, 360), new(230, 255, 360), new(210, 250, 360)],
        };
        using var r = Reader(Place(raw));
        Assert.True(r.TryReadRaw(out var read));
        Assert.False(read.Torn);
        Assert.Equal(raw.Main, read.Main);
        Assert.Equal(raw.Communication, read.Communication);
        Assert.Equal(raw.EnvelopeRows, read.EnvelopeRows);
        var noEntries = Array.Empty<Bbs757EicasRawEntry>();
        var noRows = Array.Empty<Bbs757EicasRawEnvelopeRow>();
        Assert.Equal(raw with { Main = noEntries, Communication = noEntries, EnvelopeRows = noRows },
            read with { Main = noEntries, Communication = noEntries, EnvelopeRows = noRows });
    }

    [Fact]
    public void The_envelope_rows_are_read_through_the_layouts_row_fields()
    {
        var raw = Live() with { EnvelopeRows = [new(250, 260, 360), new(230, 255, 360), default] };
        var permuted = L with { EnvelopeRowFl = 8, EnvelopeRowMin = 0, EnvelopeRowMax = 4 };
        var bytes = Place(raw);
        for (int k = 0; k < raw.EnvelopeRows.Count; k++)
        {
            long row = Upper + L.EnvelopeRows + k * L.EnvelopeRowStride;
            I32(bytes, row + permuted.EnvelopeRowFl, raw.EnvelopeRows[k].Fl);
            I32(bytes, row + permuted.EnvelopeRowMin, raw.EnvelopeRows[k].Min);
            I32(bytes, row + permuted.EnvelopeRowMax, raw.EnvelopeRows[k].Max);
        }
        using var r = Reader(bytes, permuted);
        Assert.True(r.TryReadRaw(out var read));
        Assert.Equal(raw.EnvelopeRows, read.EnvelopeRows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Any_alternate_flag_shows_the_alternate_n1(int flag)
    {
        var bytes = Place(Live() with { Left = IdleLeft with { N1Alternate = 26.5 } });
        Flag(bytes, EngineLeft + L.EngineN1AlternateFlags[flag], true);
        var eicas = Read(bytes);
        Assert.Equal(V("N1", "26.5", "19.9"), Row(eicas, "N1"));
    }

    public enum Tear
    {
        EndBeforeBegin, CapBeforeEnd, Misaligned, NullStorage, NullEntry, EntryOutside, NullText, TextOutside, ZeroLength,
        LengthOverMax, Surrogate, BeyondUnicode, UnknownLevel, NoTerminator, TerminatorOutside, EmbeddedNul,
    }

    public static IEnumerable<object[]> Tears() =>
        Enum.GetValues<Tear>().SelectMany(t => new[] { new object[] { t, false }, new object[] { t, true } });

    private static void Apply(byte[] b, Tear tear, bool communication)
    {
        uint header = communication ? L.CommunicationList : L.MainList;
        uint storage = communication ? CommStorage : MainStorage;
        uint entry = communication ? CommEntries : MainEntries, text = communication ? CommTexts : MainTexts;
        uint end = P(b, header + 4);
        switch (tear)
        {
            case Tear.EndBeforeBegin: I32(b, header + 4, storage - 4); break;
            case Tear.CapBeforeEnd: I32(b, header + 8, end - 4); break;
            case Tear.Misaligned: I32(b, header + 4, end + 2); break;
            case Tear.NullStorage:
                // Pointers that would read as two good entries if a null storage pointer were followed.
                I32(b, 0, entry);
                I32(b, 4, entry + EntryPitch);
                I32(b, header, 0);
                I32(b, header + 4, 8);
                I32(b, header + 8, 16);
                break;
            case Tear.NullEntry: I32(b, storage + 4, 0); break;
            case Tear.EntryOutside: I32(b, storage + 4, Outside); break;
            case Tear.NullText: I32(b, entry + L.EntryText, 0); break;
            case Tear.TextOutside: I32(b, entry + L.EntryText, Outside); break;
            case Tear.ZeroLength: I32(b, entry + L.EntryLength, 0); break;
            case Tear.LengthOverMax: I32(b, entry + L.EntryLength, L.MaxTextLength + 1); break;
            case Tear.Surrogate: I32(b, text + 4, 0xD800); break;
            case Tear.BeyondUnicode: I32(b, text + 4, 0x110000); break;
            case Tear.UnknownLevel: I32(b, entry + L.EntryLevel, 5); break;
            case Tear.NoTerminator: I32(b, text + 4 * P(b, entry + L.EntryLength), 'X'); break;
            case Tear.EmbeddedNul: I32(b, text + 4, 0); break;
            case Tear.TerminatorOutside:
                // The whole text inside the module, its terminator just past the end.
                uint bytes = 4 * P(b, entry + L.EntryLength), last = Size - bytes;
                b.AsSpan((int)text, (int)bytes).CopyTo(b.AsSpan((int)last));
                I32(b, entry + L.EntryText, last);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Tears))]
    public void A_torn_list_is_one_corrupt_reading(Tear tear, bool communication)
    {
        var raw = Live() with { Communication = [Communication(262, " SELCAL"), Communication(260, " FMC")] };
        var clean = Place(raw);
        var bytes = (byte[])clean.Clone();
        Apply(bytes, tear, communication);
        using var r = Reader(bytes);

        // A reading, never a failed read (the object is still there) and never a partial list.
        Assert.True(r.TryRead(out var torn));
        Assert.True(torn.Corrupt);
        if (tear != Tear.UnknownLevel) Assert.Empty(torn.Messages);   // a level is checked by Decode, on a whole list

        // The reader keeps nothing: the next reading of a whole list is clean.
        clean.CopyTo(bytes, 0);
        Assert.True(r.TryRead(out var next));
        Assert.False(next.Corrupt);
        Assert.Equal(Decode(raw), next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_list_over_the_cap_is_corrupt(bool communication)
    {
        var bytes = Place(Live() with { Communication = [Communication(262, " SELCAL")] });
        uint header = communication ? L.CommunicationList : L.MainList;
        uint storage = communication ? CommStorage : MainStorage;
        uint entries = communication ? CommEntries : MainEntries;
        // Distinct entries, each a copy of the list's first (one entry twice is torn by itself).
        void Fill(int count)
        {
            for (int i = 0; i < count; i++)
            {
                uint entry = entries + EntryPitch * (uint)i;
                if (i > 0) bytes.AsSpan((int)entries, (int)EntryPitch).CopyTo(bytes.AsSpan((int)entry));
                I32(bytes, storage + 4 * i, entry);
            }
            I32(bytes, header + 4, storage + 4 * count);
            I32(bytes, header + 8, storage + 4 * count);
        }

        Fill(L.MaxMessages);
        var full = Read(bytes);
        Assert.False(full.Corrupt);
        Assert.Equal(L.MaxMessages + (communication ? 5 : 1), full.Messages.Count);

        Fill(L.MaxMessages + 1);
        Assert.True(Read(bytes).Corrupt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_list_holding_one_entry_twice_is_torn(bool communication)
    {
        // An erase shifts the pointers down before it lowers the end: walked between the two, the last entry is there
        // twice (or, the shift half done, another one is).
        var raw = Live() with { Communication = [Communication(262, " SELCAL"), Communication(260, " FMC")] };
        var bytes = Place(raw);
        uint storage = communication ? CommStorage : MainStorage;
        int last = (communication ? raw.Communication.Count : raw.Main.Count) - 1;
        I32(bytes, storage + 4 * (last - 1), P(bytes, storage + 4 * last));
        var torn = Read(bytes);
        Assert.True(torn.Corrupt);
        Assert.Empty(torn.Messages);
    }

    /// <summary>Memory BlueBird changes while the reader walks it: <paramref name="change"/> runs once, just before the
    /// first read that touches the module address <paramref name="trigger"/>.</summary>
    private sealed class ChangingMemory(FakeProcessMemory inner, uint trigger, Action change) : IProcessMemorySource
    {
        private bool _changed;

        public IEnumerable<MemoryRegion> Regions() => inner.Regions();

        public MemoryRegion? RegionAt(ulong address) => inner.RegionAt(address);

        public bool TryRead(ulong address, Span<byte> into)
        {
            ulong at = Base + trigger;
            if (!_changed && address <= at && at < address + (ulong)into.Length)
            {
                _changed = true;
                change();
            }
            return inner.TryRead(address, into);
        }

        public void Dispose() { }
    }

    [Theory]
    [InlineData("main")]
    [InlineData("communication")]
    [InlineData("components")]
    public void A_vector_whose_header_changes_during_the_walk_is_torn(string vector)
    {
        var raw = Live() with { Communication = [Communication(262, " SELCAL"), Communication(260, " FMC")] };
        var bytes = Place(raw);
        // The walk reaches the vector's last element; BlueBird then lowers its end (the erase finishing).
        (uint trigger, long endAt, int step) = vector switch
        {
            "main" => (MainTexts + TextPitch * (uint)(raw.Main.Count - 1), L.MainList + 4, 4),
            "communication" => (CommTexts + TextPitch * (uint)(raw.Communication.Count - 1), L.CommunicationList + 4, 4),
            _ => (Components + (uint)((ComponentIds.Length - 1) * L.ComponentStride + L.ComponentId),
                Pneumatic + L.PneumaticComponents + 4, L.ComponentStride),
        };
        var src = new ChangingMemory(Source(bytes), trigger, () => I32(bytes, endAt, P(bytes, endAt) - step));
        using var r = new Bbs757UpperEicasReader(new ModuleMemory(src, Base), src, L);
        Assert.True(r.TryRead(out var torn));
        Assert.True(torn.Corrupt);
        Assert.Empty(torn.Messages);

        // The vector as it now stands reads whole.
        Assert.True(r.TryRead(out var next));
        Assert.False(next.Corrupt);
        Assert.Equal(raw.Main.Count + raw.Communication.Count - (vector == "components" ? 0 : 1), next.Messages.Count);
    }

    [Fact]
    public void Text_is_read_exactly_with_its_indent()
    {
        var raw = Live() with
        {
            Main = [new(3, "APU FIRE", 4, false), new(31, "L ENG OVHT", 3, false), Advisory(160, " PARKING BRAKE"),
                new(9, "\U0001F6EB WIDE", 3, false)],
            Communication = [Communication(262, " SELCAL")],
        };
        var bytes = Place(raw);
        var eicas = Read(bytes);
        Assert.False(eicas.Corrupt);
        // Warnings and cautions have no indent; an advisory's and a communication message's leading space is kept.
        Assert.Equal(new[] { "APU FIRE", "L ENG OVHT", " PARKING BRAKE", "\U0001F6EB WIDE", " SELCAL" }, eicas.Messages.Select(m => m.Text));
        Assert.Equal(new[] { "warning", "caution", "advisory", "caution", "communication" }, eicas.Messages.Select(m => m.Level));

        // The text ends where BlueBird's literal does, at its stored length with its terminator (where the draw stops):
        // anything else in that place is a torn read, never a longer or a shorter text.
        I32(bytes, MainTexts + 2 * TextPitch + 4 * " PARKING BRAKE".Length, 'X');
        Assert.True(Read(bytes).Corrupt);
    }

    [Theory]
    [InlineData(0, 1, 25, 2, true, "D-TO 1, +25c")]
    [InlineData(0, 1, 5, 2, true, "D-TO 1, +5c")]
    [InlineData(0, 2, 0, 2, true, "D-TO 2, 0c")]
    [InlineData(0, 1, -5, 2, true, "D-TO 1, -5c")]
    [InlineData(0, 1, 25, 1, true, "TO 1, +25c")]
    [InlineData(0, 1, 25, 2, false, "D-TO 1")]
    [InlineData(0, 3, 0, 0, false, "TO")]
    [InlineData(4, 1, 0, 0, false, "CLB")]
    [InlineData(5, 2, 0, 0, false, "CLB1")]
    [InlineData(6, 0, 0, 0, false, "CLB2")]
    [InlineData(8, 2, 0, 0, false, "CRZ 2")]
    [InlineData(9, 1, 10, 0, true, "CRZ 1, +10c")]
    [InlineData(10, 1, 0, 0, false, "CON")]
    [InlineData(11, 2, 0, 0, false, "G/A")]
    [InlineData(2, 1, 25, 2, true, "+25c")]
    [InlineData(12, 1, 0, 0, true, "0c")]
    [InlineData(3, 0, -5, 0, true, "-5c")]
    [InlineData(2, 1, 25, 2, false, null)]
    [InlineData(12, 1, 0, 0, false, null)]
    public void Reads_the_thrust_reference_and_derate(int mode, int number, int derate, int dtoFlags, bool derateShown, string? expected)
    {
        var bytes = Place(Live() with
        {
            ThrustMode = mode, ThrustNumber = number, Derate = derate, DerateShown = derateShown, ReferenceEpr = 1.4472,
        });
        for (int i = 0; i < dtoFlags; i++) Flag(bytes, Tmc + L.DtoFlags[i], true);
        var eicas = Read(bytes);
        Assert.False(eicas.Corrupt);
        Assert.Equal(expected, eicas.ThrustReference);
        // The reference EPR is drawn with the block, a word or none.
        Assert.Equal(V("EPR reference", "1.45", "1.45"), Row(eicas, "EPR reference"));
    }

    [Fact]
    public void The_upper_eicas_is_the_one_manager_pointer_carrying_its_vptr()
    {
        using (var r = Reader(Place(Live())))
        {
            Assert.True(r.TryFindUpperEicas(out uint at));
            Assert.Equal(Upper, at);
        }

        var none = Place(Live());
        I32(none, Upper, DecoyVptr);
        using (var r = Reader(none))
        {
            Assert.False(r.TryFindUpperEicas(out _));
            Assert.False(r.TryRead(out _));
        }

        // Two objects carrying the vptr: neither is taken.
        var two = Place(Live());
        I32(two, Decoy, L.UpperEicasVptr);
        using (var r = Reader(two))
        {
            Assert.False(r.TryFindUpperEicas(out _));
            Assert.False(r.TryRead(out _));
        }

        // A slot past the manager's span is not looked at.
        var beyond = Place(Live());
        I32(beyond, L.ManagerBase + UpperSlot, 0);
        I32(beyond, L.ManagerBase + L.ManagerSpan, Upper);
        using (var r = Reader(beyond))
            Assert.False(r.TryFindUpperEicas(out _));
    }

    [Theory]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToComputer))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToEngineLeft))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToEngineRight))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToLower))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToPneumatic))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToFms))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToFire))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToReverserLeft))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToReverserRight))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToTmc))]
    [InlineData(nameof(Bbs757UpperEicasLayout.ToTat))]
    public void A_missing_object_is_a_failed_read(string pointer)
    {
        int offset = (int)typeof(Bbs757UpperEicasLayout).GetProperty(pointer)!.GetValue(L)!;
        foreach (uint target in new[] { 0u, Outside })
        {
            var bytes = Place(Live());
            I32(bytes, Upper + offset, target);
            using var r = Reader(bytes);
            Assert.False(r.TryRead(out var reading), $"{pointer} = 0x{target:x}");
            Assert.True(reading.Corrupt);   // whatever comes out of a failed read is never usable
        }
    }

    [Fact]
    public void A_fixed_field_outside_the_module_is_a_failed_read()
    {
        var bytes = Place(Live());
        foreach (var (what, layout) in new (string, Bbs757UpperEicasLayout)[]
        {
            ("the main list", L with { MainList = Size }),
            ("the communication list", L with { CommunicationList = Size - 8 }),
            ("the clock", L with { Clock = Size - 4 }),
            ("the computer's page", L with { ComputerPage = (int)Outside }),
            ("the engine block", L with { EngineBlock = (int)Outside }),
            ("the alternate flags", L with { EngineN1AlternateFlags = new[] { 6922, (int)Outside } }),
            ("the TMC's mode", L with { ThrustMode = (int)Outside }),
            ("the D-TO flags", L with { DtoFlags = new[] { 3096, (int)Outside } }),
            ("the TAT", L with { TatValue = (int)Outside }),
            ("the reverser", L with { ReverserState = (int)Outside }),
            ("the component vector", L with { PneumaticComponents = (int)Outside }),
            ("the envelope rows", L with { EnvelopeRows = (int)Outside }),
            ("the envelope row's max", L with { EnvelopeRowMax = (int)Outside }),
            ("the power byte", L with { Powered = (int)Outside }),
            ("the max memories", L with { MaxEgtRight = (int)Outside }),
        })
        {
            using var r = Reader(bytes, layout);
            Assert.False(r.TryRead(out _), what);
        }
    }

    [Fact]
    public void A_torn_component_list_is_corrupt()
    {
        var raw = Live() with { Left = IdleLeft with { AntiIce = new(100, 0) } };
        long begin = Pneumatic + L.PneumaticComponents, endAt = begin + 4;
        uint end = Components + (uint)(ComponentIds.Length * L.ComponentStride);
        foreach (var (what, b, e) in new (string, uint, uint)[]
        {
            ("end before begin", Components, Components - (uint)L.ComponentStride),
            ("not a whole component", Components, end + 4),
            ("over the cap", Components, Components + (uint)((L.MaxComponents + 1) * L.ComponentStride)),
            ("a null storage pointer", 0, (uint)(2 * L.ComponentStride)),
        })
        {
            var bytes = Place(raw);
            I32(bytes, begin, b);
            I32(bytes, endAt, e);
            Assert.True(Read(bytes).Corrupt, what);
        }

        // A valve whose fields run past the module's end.
        var past = Place(raw);
        uint last = Size - 40;
        I32(past, begin, last);
        I32(past, endAt, last + (uint)L.ComponentStride);
        I32(past, last + L.ComponentId, L.TaiLeftId);
        Assert.True(Read(past).Corrupt);

        // An empty vector is no valve: no TAI, not corrupt.
        var empty = Place(raw);
        I32(empty, begin, 0);
        I32(empty, endAt, 0);
        var eicas = Read(empty);
        Assert.False(eicas.Corrupt);
        Assert.Null(Row(eicas, "TAI"));
    }

    [Fact]
    public void The_anti_ice_valves_are_found_by_their_id_the_first_match_counting()
    {
        var bytes = Place(Live() with { Right = IdleLeft with { AntiIce = null } });
        // The left valve moved to the last element, open; an id 16 still at element 2, closed, comes first.
        long lastElement = Components + (ComponentIds.Length - 1) * L.ComponentStride;
        I32(bytes, lastElement + L.ComponentId, L.TaiLeftId);
        F64(bytes, lastElement + L.ComponentPosition, 100);
        I32(bytes, lastElement + L.ComponentState, 0);
        Assert.Null(Row(Read(bytes), "TAI"));

        I32(bytes, Components + 2 * L.ComponentStride + L.ComponentId, 99);
        var eicas = Read(bytes);
        Assert.False(eicas.Corrupt);
        Assert.Equal(V("TAI", "TAI", null), Row(eicas, "TAI"));
    }

    [Fact]
    public void The_manual_flag_is_the_simulators_thrust_reference_pull()
    {
        Assert.Equal("BBS757_THRREF_PULL", Bbs757UpperEicasReader.ManualKey);
        var bytes = Place(Live() with
        {
            ThrustNumber = 1, DtoFlagged = true, DerateShown = true, Derate = 25, ManualEprLeft = 1.52, ManualEprRight = 1.48,
        });
        using var r = Reader(bytes);
        foreach (double on in new[] { 1, 0.75 })
        {
            Assert.True(r.TryRead(Sim(on), out var manual));
            Assert.Equal("MAN", manual.ThrustReference);
            Assert.Equal(V("EPR reference", "1.52", "1.48"), Row(manual, "EPR reference"));
        }
        foreach (double? off in new double?[] { 0, 0.5, double.NaN, double.PositiveInfinity, null })
        {
            Assert.True(r.TryRead(Sim(off), out var auto));
            Assert.Equal("D-TO 1, +25c", auto.ThrustReference);
        }
        Assert.True(r.TryRead(out var plain));
        Assert.Equal("D-TO 1, +25c", plain.ThrustReference);
        Assert.Equal(V("EPR reference", "1.71", "1.71"), Row(plain, "EPR reference"));
    }

    [Fact]
    public void One_reading_asks_windows_about_each_region_once()
    {
        // VirtualQueryEx costs ~200 µs on the simulator: one reading of one region asks about it once (WASM-15).
        var src = Source(Place(Live() with { Communication = [Communication(262, " SELCAL")] }));
        using var r = new Bbs757UpperEicasReader(new ModuleMemory(src, Base), src, L);
        Assert.True(r.TryRead(out _));
        Assert.Equal(1, src.RegionQueries);
    }

    // ---- the draw rules (Decode) ----

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(2, 9)]
    [InlineData(3, 8)]
    public void The_screen_shows_the_first_lines_uncancelled_messages(int communication, int lines)
    {
        var main = Enumerable.Range(0, 14).Select(i => new Bbs757EicasRawEntry(100 + i, $" MESSAGE {i}", 2, i is 1 or 4)).ToArray();
        var comm = Enumerable.Range(0, communication).Select(i => Communication(257 + i, $" CALL {i}")).ToArray();
        var eicas = Decode(Live() with { Main = main, Communication = comm });
        Assert.False(eicas.Corrupt);
        var shown = Enumerable.Range(0, 14).Where(i => i is not (1 or 4)).Take(lines).ToHashSet();
        for (int i = 0; i < 14; i++)
        {
            Assert.Equal(main[i].Text, eicas.Messages[i].Text);
            Assert.Equal(i is 1 or 4, eicas.Messages[i].Cancelled);
            Assert.Equal(shown.Contains(i), eicas.Messages[i].Shown);
        }
        Assert.Equal(2, eicas.Cancelled);
        Assert.Equal(14 + communication, eicas.Messages.Count);
    }

    [Fact]
    public void Communication_messages_follow_the_main_list_bottom_up()
    {
        Bbs757EicasRawEntry[] comm = [new(257, " ATC", 1, true), new(258, " CABIN CALL", 0, false), Communication(262, " SELCAL")];
        var eicas = Decode(Live() with { Communication = comm });
        var tail = eicas.Messages.Skip(LiveList.Length).ToList();
        Assert.Equal(new[] { " SELCAL", " CABIN CALL", " ATC" }, tail.Select(m => m.Text));
        Assert.All(tail, m =>
        {
            Assert.False(m.Cancelled);   // CANC never touches the communication list
            Assert.True(m.Shown);
            Assert.Equal("communication", m.Level);
        });
        Assert.Equal(0, eicas.Cancelled);

        // While RECALL shows, entry 0 (the bottom line) leaves its line to RECALL and PAGE.
        var recalled = Decode(Live() with { Communication = comm, RecallTime = 4096.0, Clock = 4096.29 });
        Assert.True(recalled.Recall);
        Assert.Equal(new[] { true, true, false }, recalled.Messages.Skip(LiveList.Length).Select(m => m.Shown));
    }

    [Theory]
    [InlineData(0, false, 1, 10, false, true)]
    [InlineData(0, false, 1, 12, false, true)]
    [InlineData(0, false, 1, 5, false, false)]
    [InlineData(0, false, 2, 5, false, true)]
    [InlineData(0, false, 3, 5, true, false)]
    [InlineData(1, false, 1, 10, false, false)]
    [InlineData(1, true, 1, 10, false, true)]
    [InlineData(2, true, 1, 9, false, true)]
    [InlineData(2, true, 1, 8, false, false)]
    [InlineData(2, false, 2, 5, false, false)]
    public void Page_shows_by_bluebirds_own_condition(int communication, bool recall, int page, int main, bool allCancelled, bool expected)
    {
        var entries = Enumerable.Range(0, main).Select(i => new Bbs757EicasRawEntry(100 + i, $" MESSAGE {i}", 2, allCancelled)).ToArray();
        var comm = Enumerable.Range(0, communication).Select(i => Communication(257 + i, $" CALL {i}")).ToArray();
        var eicas = Decode(Live() with
        {
            Main = entries, Communication = comm, Page = page, RecallTime = recall ? 4096.0 : 4023.89, Clock = 4096.29,
        });
        Assert.Equal(recall, eicas.Recall);
        Assert.Equal(page, eicas.Page);
        Assert.Equal(expected, eicas.PageShown);
        Assert.Equal(expected ? "PAGE " + page : null, eicas.PageLine);
        Assert.Equal(recall ? "RECALL" : null, eicas.RecallLine);
    }

    [Fact]
    public void The_page_and_recall_lines_are_drawn_in_the_layouts_words()
    {
        // Page 2 with five messages drawn, just after RECALL.
        var raw = Live() with { Page = 2, RecallTime = 4096.0, Clock = 4096.29 };
        var drawn = Decode(raw);
        Assert.Equal(("PAGE 2", "RECALL"), (drawn.PageLine, drawn.RecallLine));
        var other = Bbs757UpperEicasReader.Decode(L with { PageWord = "SEITE", RecallWord = "ZURUECK" }, raw, manual: false);
        Assert.Equal(("SEITE 2", "ZURUECK"), (other.PageLine, other.RecallLine));
        // Neither drawn: page 1 with five of ten lines, no RECALL press within the half second.
        var quiet = Decode(Live());
        Assert.Null(quiet.PageLine);
        Assert.Null(quiet.RecallLine);
    }

    [Theory]
    [InlineData(100.0, 100.0, true)]
    [InlineData(100.25, 100.0, true)]
    [InlineData(100.5, 100.0, false)]
    [InlineData(99.9, 100.0, false)]
    [InlineData(172.4, 100.0, false)]
    public void Recall_shows_for_its_half_second(double clock, double recallTime, bool expected)
    {
        var eicas = Decode(Live() with { Clock = clock, RecallTime = recallTime });
        Assert.Equal(expected, eicas.Recall);
        Assert.Equal(recallTime, eicas.RecallTime);
    }

    [Theory]
    [InlineData(true, false, false, 0, true)]
    [InlineData(false, false, false, 0, false)]
    [InlineData(true, true, false, 0, false)]
    [InlineData(true, false, true, 0, false)]
    [InlineData(true, false, false, 25, false)]
    [InlineData(true, false, false, -1, false)]
    public void Powered_needs_the_gauge_lit_and_the_computer_not_blanking(bool powered, bool failed, bool timedBlank, int blank, bool expected)
    {
        var eicas = Decode(Live() with { Powered = powered, Failed = failed, TimedBlank = timedBlank, Blank = blank });
        Assert.Equal(expected, eicas.Powered);
        // Everything else is read whatever the power.
        Assert.Equal(LiveList.Length, eicas.Messages.Count);
        Assert.Equal("TO", eicas.ThrustReference);
    }

    [Fact]
    public void The_epr_row_blanks_as_bluebird_draws_and_is_never_colored()
    {
        Bbs757EicasValue? Epr(Bbs757EicasRawEngine left) => Row(Decode(Live() with { Left = left }), "EPR");
        Assert.Equal(V("EPR", "1.03", "1.03"), Epr(IdleLeft));
        Assert.Equal(V("EPR", null, "1.03"), Epr(IdleLeft with { Valid = 2 }));
        Assert.Equal(V("EPR", "1.03", "1.03"), Epr(IdleLeft with { Valid = 1 }));
        Assert.Equal(V("EPR", null, "1.03"), Epr(IdleLeft with { Type = 1, N2 = 5.0 }));
        Assert.Equal(V("EPR", "1.03", "1.03"), Epr(IdleLeft with { Type = 1, N2 = 5.01 }));
        Assert.Equal(V("EPR", "1.03", "1.03"), Epr(IdleLeft with { Type = 0, N2 = 1 }));
        Assert.Equal(V("EPR", "1.70", "1.03"), Epr(IdleLeft with { Epr = 1.7 }));
        // Never amber or red, even with the N1 and EGT red.
        Assert.Equal(V("EPR", "1.96", "1.03"), Epr(IdleLeft with { Epr = 1.957, N1 = 112, Egt = 900 }));
        // Formatted as the draw does, from the float: 1.00500001 is 1.01 as a double, 1.0049999952 as a float.
        Assert.Equal(V("EPR", "1.00", "1.03"), Epr(IdleLeft with { Epr = 1.00500001 }));
    }

    [Fact]
    public void The_reference_epr_is_drawn_for_each_valid_engine_from_the_float()
    {
        var invalid = IdleLeft with { Valid = 2 };
        Assert.Equal(V("EPR reference", "1.00", "1.00"), Row(Decode(Live() with { ReferenceEpr = 1.00500001 }), "EPR reference"));
        Assert.Equal(V("EPR reference", null, "1.71"), Row(Decode(Live() with { Left = invalid }), "EPR reference"));
        Assert.Equal(V("EPR reference", "1.00", null),
            Row(Decode(Live() with { Right = invalid, ManualEprLeft = 1.00500001 }, manual: true), "EPR reference"));
        // Both engines invalid: no side is drawn, so no row; the thrust reference itself is still drawn.
        var neither = Decode(Live() with { Left = invalid, Right = invalid });
        Assert.Null(Row(neither, "EPR reference"));
        Assert.Equal("TO", neither.ThrustReference);
        Assert.False(Decode(Live() with { Left = invalid, Right = invalid, ReferenceEpr = double.NaN }).Corrupt);
    }

    [Fact]
    public void N1_and_egt_are_not_drawn_without_their_red_limit()
    {
        foreach (float limit in new[] { 0f, -1f })
        {
            var noN1 = Decode(Live() with { N1Red = limit, Left = IdleLeft with { AntiIce = new(100, 0), MaxN1 = 109.2f } });
            Assert.False(noN1.Corrupt);
            Assert.Equal(V("N1", null, null), Row(noN1, "N1"));
            Assert.Null(Row(noN1, "TAI"));
            Assert.Null(Row(noN1, "N1 max"));   // drawn inside the N1 function (22951)
            Assert.Equal(V("EGT", "357", "360"), Row(noN1, "EGT"));

            var noEgt = Decode(Live() with { EgtRed = limit, Left = IdleLeft with { MaxEgt = 861.5f, Egt = 900 } });
            Assert.False(noEgt.Corrupt);
            Assert.Equal(V("EGT", null, null), Row(noEgt, "EGT"));
            Assert.Null(Row(noEgt, "EGT max"));   // drawn inside the EGT function (22952)
            Assert.Equal(V("N1", "19.9", "19.9"), Row(noEgt, "N1"));
        }
    }

    [Fact]
    public void The_n1_row_blanks_and_colors_as_bluebird_draws()
    {
        // In CLB (mode 4) nothing inhibits the amber; in TO (mode 0) with the TMC valid the RB211's amber is inhibited.
        Bbs757EicasValue? N1(Bbs757EicasRawEngine left, int mode = 4, bool tmcFailed = false) =>
            Row(Decode(Live() with { Left = left, ThrustMode = mode, TmcFailed = tmcFailed }), "N1");
        Assert.Equal(V("N1", "19.9", "19.9"), N1(IdleLeft));
        Assert.Equal(V("N1", null, "19.9"), N1(IdleLeft with { Valid = 2, N1 = 109 }));
        Assert.Equal(V("N1", "26.5", "19.9"), N1(IdleLeft with { AlternateFlagged = true, N1Alternate = 26.5 }));
        Assert.Equal(V("N1", null, "19.9"), N1(IdleLeft with { Type = 1, N1 = 5.0 }));
        Assert.Equal(V("N1", null, "19.9"), N1(IdleLeft with { Type = 1, N1 = 3, AlternateFlagged = true, N1Alternate = 4.9 }));
        Assert.Equal(V("N1", "5.1", "19.9"), N1(IdleLeft with { Type = 1, N1 = 5.06 }));
        Assert.Equal(V("N1", "3.0", "19.9"), N1(IdleLeft with { Type = 0, N1 = 3 }));

        Assert.Equal(V("N1", "108.8", "19.9", "red"), N1(IdleLeft with { N1 = (double)108.8f }));
        Assert.Equal(V("N1", "108.5", "19.9", "amber"), N1(IdleLeft with { N1 = 108.5 }));
        Assert.Equal(V("N1", "108.4", "19.9", "amber"), N1(IdleLeft with { N1 = (double)108.4f }));
        Assert.Equal(V("N1", "108.4", "19.9"), N1(IdleLeft with { N1 = 108.39 }));
        // The mode inhibit: the RB211 loses only its amber, the PW2000 keeps it.
        Assert.Equal(V("N1", "108.5", "19.9"), N1(IdleLeft with { N1 = 108.5 }, mode: 0));
        Assert.Equal(V("N1", "109.0", "19.9", "red"), N1(IdleLeft with { N1 = 109 }, mode: 0));
        Assert.Equal(V("N1", "108.5", "19.9", "amber"), N1(IdleLeft with { N1 = 108.5, Type = 1 }, mode: 0));
        Assert.Equal(V("N1", "108.5", "19.9", "amber"), N1(IdleLeft with { N1 = 108.5 }, mode: 12));
        Assert.Equal(V("N1", "108.5", "19.9"), N1(IdleLeft with { N1 = 108.5 }, mode: 11));
        // No inhibit without a valid TMC.
        Assert.Equal(V("N1", "108.5", "19.9", "amber"), N1(IdleLeft with { N1 = 108.5 }, mode: 0, tmcFailed: true));
        // The alternate value is the one compared.
        Assert.Equal(V("N1", "109.0", "19.9", "red"), N1(IdleLeft with { AlternateFlagged = true, N1Alternate = 109 }));
    }

    [Fact]
    public void The_egt_row_blanks_and_colors_as_bluebird_draws()
    {
        Bbs757EicasValue? Egt(Bbs757EicasRawEngine left, int mode = 4, bool compact = false, float amber = 795) =>
            Row(Decode(Live() with { Left = left, ThrustMode = mode, Compact = compact, EgtAmber = amber }), "EGT");
        Assert.Equal(V("EGT", "357", "360"), Egt(IdleLeft));
        Assert.Equal(V("EGT", "0", "360"), Egt(IdleLeft with { Egt = -12.5 }));
        Assert.Equal(V("EGT", null, "360"), Egt(IdleLeft with { Valid = 2 }));
        Assert.Equal(V("EGT", null, "360"), Egt(IdleLeft with { Type = 1, N2 = 4.5 }));
        Assert.Equal(V("EGT", "357", "360"), Egt(IdleLeft with { Type = 1, N2 = 5.5 }));
        // Compact mode: the EGT is not drawn.
        Assert.Equal(V("EGT", null, null), Egt(IdleLeft with { Egt = 900 }, compact: true));

        Assert.Equal(V("EGT", "850", "360", "red"), Egt(IdleLeft with { Egt = 850 }));
        Assert.Equal(V("EGT", "849", "360", "amber"), Egt(IdleLeft with { Egt = 849.99 }));
        Assert.Equal(V("EGT", "795", "360", "amber"), Egt(IdleLeft with { Egt = 795 }));
        Assert.Equal(V("EGT", "794", "360"), Egt(IdleLeft with { Egt = 794.99 }));
        // The untruncated value is compared: 795.7 is at or over 795.5, though it reads 795.
        Assert.Equal(V("EGT", "795", "360", "amber"), Egt(IdleLeft with { Egt = 795.7 }, amber: 795.5f));
        Assert.Equal(V("EGT", "795", "360"), Egt(IdleLeft with { Egt = 795.4 }, amber: 795.5f));
        // The mode inhibit takes the amber on either engine type, never the red.
        Assert.Equal(V("EGT", "800", "360"), Egt(IdleLeft with { Egt = 800 }, mode: 0));
        Assert.Equal(V("EGT", "800", "360"), Egt(IdleLeft with { Egt = 800, Type = 1 }, mode: 0));
        Assert.Equal(V("EGT", "860", "360", "red"), Egt(IdleLeft with { Egt = 860 }, mode: 0));
    }

    [Theory]
    [InlineData(11.992, "+11c")]
    [InlineData(5.7, "+5c")]
    [InlineData(0.4, "+0c")]
    [InlineData(0.0, "+0c")]
    [InlineData(-0.4, "+0c")]
    [InlineData(-5.9, "-5c")]
    [InlineData(-15.2, "-15c")]
    [InlineData(10.0, "+10c")]
    [InlineData(-10.0, "-10c")]
    public void Tat_reads_with_its_sign_and_c(double tat, string expected)
    {
        Assert.Equal(V("TAT", expected), Row(Decode(Live() with { Tat = tat }), "TAT"));
    }

    [Fact]
    public void Tai_is_a_row_only_while_drawn()
    {
        Bbs757EicasValue? Tai(Bbs757EicasRawValve? left, Bbs757EicasRawValve? right) =>
            Row(Decode(Live() with { Left = IdleLeft with { AntiIce = left }, Right = IdleLeft with { AntiIce = right } }), "TAI");
        Assert.Null(Tai(new(0, 2), new(0, 2)));
        Assert.Equal(V("TAI", "TAI", null), Tai(new(100, 0), new(0, 2)));
        Assert.Equal(V("TAI", null, "TAI"), Tai(new(0, 2), new(7.0, 1)));
        Assert.Equal(V("TAI", "TAI", "TAI"), Tai(new(100, 0), new(100, 0)));
        Assert.Null(Tai(new(6.99, 1), new(0, 2)));
        Assert.Null(Tai(new(100, 2), new(0, 2)));
        Assert.Null(Tai(null, null));
    }

    [Fact]
    public void Rev_is_a_row_only_while_drawn_with_its_color()
    {
        Bbs757EicasValue? Rev(int left, int right) =>
            Row(Decode(Live() with { Left = IdleLeft with { Reverser = left }, Right = IdleLeft with { Reverser = right } }), "REV");
        Assert.Null(Rev(0, 0));
        Assert.Equal(V("REV", "REV", null, "amber"), Rev(1, 0));
        Assert.Equal(V("REV", null, "REV", null, "green"), Rev(0, 2));
        Assert.Equal(V("REV", "REV", "REV", "green", "amber"), Rev(2, 1));
        Assert.Null(Rev(3, -1));
    }

    [Fact]
    public void The_reverser_states_and_the_drawn_words_come_from_the_layout()
    {
        // Another build that numbers the reverser's states 3 (moving) and 4 (deployed) and draws other words.
        var renumbered = L with { ReverserMoving = 3, ReverserDeployed = 4, RevWord = "RV", TaiWord = "AI" };
        Bbs757UpperEicasReading At(int left, int right) => Bbs757UpperEicasReader.Decode(renumbered, Live() with
        {
            Left = IdleLeft with { Reverser = left, AntiIce = new(100, 0) }, Right = IdleLeft with { Reverser = right },
        }, manual: false);
        Assert.Equal(V("REV", "RV", "RV", "amber", "green"), Row(At(3, 4), "REV"));
        Assert.Null(At(3, 4).ThrustReference);   // the deployed state hides the thrust reference
        Assert.Null(At(0, 4).ThrustReference);
        Assert.Equal("TO", At(3, 0).ThrustReference);
        // This build's 1 and 2 mean nothing to that layout.
        Assert.Null(Row(At(1, 2), "REV"));
        Assert.Equal("TO", At(1, 2).ThrustReference);
        Assert.Equal(V("TAI", "AI", null), Row(At(0, 0), "TAI"));
    }

    [Fact]
    public void The_thrust_number_is_drawn_from_the_layouts_table()
    {
        string? Thrust(Bbs757UpperEicasLayout layout, int mode, int number) =>
            Bbs757UpperEicasReader.Decode(layout, Live() with { ThrustMode = mode, ThrustNumber = number }, manual: false).ThrustReference;
        var numbers = L with { ThrustNumbers = new Bbs757WordTable(new Dictionary<int, string> { [1] = "1", [3] = "3" }) };
        Assert.Equal("CRZ 3", Thrust(numbers, 8, 3));
        Assert.Equal("CRZ", Thrust(numbers, 8, 2));
        Assert.Equal("CRZ 1", Thrust(numbers, 8, 1));
        Assert.Equal("CLB", Thrust(numbers, 4, 3));   // a mode the draw does not number
        // Looked up as the table holds it: a 0 entry is a number too, never the blank a word table's 0 reads as.
        var zero = L with { ThrustNumbers = new Bbs757WordTable(new Dictionary<int, string> { [0] = "0" }) };
        Assert.Equal("TO 0", Thrust(zero, 0, 0));
    }

    [Fact]
    public void The_thrust_reference_hides_with_a_deployed_reverser_or_without_the_tmc()
    {
        void Hidden(Bbs757UpperEicasRaw raw, bool manual = false)
        {
            var eicas = Decode(raw, manual);
            Assert.Null(eicas.ThrustReference);
            Assert.Null(Row(eicas, "EPR reference"));
        }
        void Drawn(Bbs757UpperEicasRaw raw, string word, bool manual = false)
        {
            var eicas = Decode(raw, manual);
            Assert.Equal(word, eicas.ThrustReference);
            Assert.NotNull(Row(eicas, "EPR reference"));
        }

        Drawn(Live(), "TO");
        Hidden(Live() with { Left = IdleLeft with { Reverser = 2 } });
        Hidden(Live() with { Right = IdleLeft with { Reverser = 2 } });
        Hidden(Live() with { Right = IdleLeft with { Reverser = 2 } }, manual: true);
        Drawn(Live() with { Left = IdleLeft with { Reverser = 1 } }, "TO");
        Hidden(Live() with { TmcFailed = true });
        Hidden(Live() with { TmcChannelLeftFailed = true, TmcChannelRightFailed = true });
        Drawn(Live() with { TmcChannelLeftFailed = true }, "TO");
        Drawn(Live() with { TmcChannelRightFailed = true }, "TO");
        Drawn(Live() with { TmcFailed = true }, "MAN", manual: true);
    }

    [Fact]
    public void Manual_draws_man_alone_with_the_manual_reference_eprs()
    {
        var eicas = Decode(Live() with
        {
            ThrustNumber = 1, DtoFlagged = true, DerateShown = true, Derate = 25, ManualEprLeft = 1.52, ManualEprRight = 1.476,
        }, manual: true);
        Assert.Equal("MAN", eicas.ThrustReference);
        Assert.Equal(V("EPR reference", "1.52", "1.48"), Row(eicas, "EPR reference"));
    }

    [Fact]
    public void The_max_memories_are_rows_only_while_one_is_set()
    {
        Bbs757UpperEicasReading Max(float n1Left, float n1Right, float egtLeft, float egtRight) => Decode(Live() with
        {
            Left = IdleLeft with { MaxN1 = n1Left, MaxEgt = egtLeft },
            Right = IdleLeft with { MaxN1 = n1Right, MaxEgt = egtRight },
        });
        var none = Max(0, 0, 0, 0);
        Assert.Null(Row(none, "N1 max"));
        Assert.Null(Row(none, "EGT max"));

        // BlueBird draws a memory only while it is set: a zero side is null.
        var n1 = Max(109.2f, 0, 0, 0);
        Assert.Equal(V("N1 max", "109.2", null), Row(n1, "N1 max"));
        Assert.Null(Row(n1, "EGT max"));

        var egt = Max(0, 0, 0, 861.7f);
        Assert.Null(Row(egt, "N1 max"));
        Assert.Equal(V("EGT max", null, "861"), Row(egt, "EGT max"));

        var both = Max(109.2f, 110.5f, 861.7f, 870.2f);
        Assert.Equal(V("N1 max", "109.2", "110.5"), Row(both, "N1 max"));
        Assert.Equal(V("EGT max", "861", "870"), Row(both, "EGT max"));

        // Each memory is drawn for a valid engine only: an invalid engine's side is null, and a row whose only set
        // memory belongs to an invalid engine is not drawn.
        var invalid = IdleLeft with { Valid = 2 };
        var leftInvalid = Decode(Live() with
        {
            Left = invalid with { MaxN1 = 109.2f, MaxEgt = 861.5f },
            Right = IdleLeft with { MaxEgt = 870.2f },
        });
        Assert.Null(Row(leftInvalid, "N1 max"));
        Assert.Equal(V("EGT max", null, "870"), Row(leftInvalid, "EGT max"));
        var rightInvalid = Decode(Live() with { Left = IdleLeft with { MaxN1 = 109.2f }, Right = invalid with { MaxN1 = 110f } });
        Assert.Equal(V("N1 max", "109.2", null), Row(rightInvalid, "N1 max"));
        Assert.False(Decode(Live() with { Left = invalid with { MaxN1 = float.NaN } }).Corrupt);

        // Compact mode draws no EGT, and no EGT max; the N1 max stays.
        var compact = Decode(Live() with { Compact = true, Left = IdleLeft with { MaxN1 = 109.2f, MaxEgt = 861.5f } });
        Assert.Equal(V("N1 max", "109.2", null), Row(compact, "N1 max"));
        Assert.Null(Row(compact, "EGT max"));
    }

    [Fact]
    public void The_rows_come_in_screen_order()
    {
        var eicas = Decode(Live() with
        {
            Left = IdleLeft with { AntiIce = new(100, 0), Reverser = 1, MaxN1 = 109.2f, MaxEgt = 861.5f },
        });
        Assert.Equal(new[] { "EPR", "N1", "EGT", "TAT", "TAI", "REV", "EPR reference", "N1 max", "EGT max" },
            eicas.Values.Select(v => v.Label));
    }

    [Theory]
    [InlineData(false, false, 1, false, false, true, false, true)]
    [InlineData(true, false, 1, false, false, true, false, false)]
    [InlineData(false, true, 1, false, false, true, false, false)]
    [InlineData(false, false, 2, false, false, true, false, false)]
    [InlineData(false, false, 0, false, false, true, false, false)]
    [InlineData(false, false, 1, true, false, true, false, false)]
    [InlineData(false, false, 1, false, true, true, false, false)]
    [InlineData(false, false, 1, true, false, false, false, true)]
    [InlineData(false, false, 1, false, true, false, false, true)]
    [InlineData(false, false, 1, false, true, false, true, false)]
    public void The_in_flight_start_block_shows_by_its_gate(bool onGround, bool compact, int lowerMode,
        bool leftCombusting, bool leftFire, bool rightCombusting, bool rightFire, bool drawn)
    {
        var eicas = Decode(Live() with
        {
            OnGround = onGround, Compact = compact, LowerMode = lowerMode,
            Left = IdleLeft with { Combusting = leftCombusting, FireSwitch = leftFire },
            Right = IdleLeft with { Combusting = rightCombusting, FireSwitch = rightFire },
            EnvelopeRows = [new(250, 260, 360), default, default],
        });
        Assert.Equal(drawn ? new[] { "IN-FLT START", "FL 250 260-360 KTS" } : Array.Empty<string>(), eicas.Envelope);
    }

    [Fact]
    public void The_in_flight_start_rows_read_as_drawn()
    {
        var eicas = Decode(Live() with
        {
            OnGround = false, Left = IdleLeft with { Combusting = false },
            EnvelopeRows = [new(50, 180, 360), default, new(290, 290, 310)],
        });
        Assert.Equal(new[] { "IN-FLT START", "FL 50 180-360 KTS", "FL 290 290-310 KTS" }, eicas.Envelope);
    }

    [Theory]
    [InlineData("clock", true)]
    [InlineData("recall time", true)]
    [InlineData("an invalid engine's own N1", true)]
    [InlineData("EPR", true)]
    [InlineData("EPR beyond a float", true)]
    [InlineData("EGT", true)]
    [InlineData("EGT red", true)]
    [InlineData("EGT amber", true)]
    [InlineData("N1 red", true)]
    [InlineData("N1 amber", true)]
    [InlineData("TAT", true)]
    [InlineData("reference EPR", true)]
    [InlineData("manual EPR, manual", true)]
    [InlineData("N1 max", true)]
    [InlineData("EGT max", true)]
    [InlineData("anti-ice valve", true)]
    [InlineData("alternate N1, flagged", true)]
    [InlineData("PW N2", true)]
    [InlineData("alternate N1, not flagged", false)]
    [InlineData("RB211 N2", false)]
    [InlineData("manual EPR, not manual", false)]
    [InlineData("reference EPR, hidden", false)]
    [InlineData("EGT, compact", false)]
    public void A_value_the_reading_uses_that_is_not_a_number_is_corrupt(string where, bool corrupt)
    {
        double nan = double.NaN;
        var raw = Live();
        bool manual = where == "manual EPR, manual";
        raw = where switch
        {
            "clock" => raw with { Clock = double.PositiveInfinity },
            "recall time" => raw with { RecallTime = nan },
            "an invalid engine's own N1" => raw with { Right = IdleLeft with { N1 = nan, Valid = 2 } },
            "EPR" => raw with { Left = IdleLeft with { Epr = double.NegativeInfinity } },
            "EPR beyond a float" => raw with { Left = IdleLeft with { Epr = 1e39 } },
            "EGT" => raw with { Left = IdleLeft with { Egt = nan } },
            "EGT red" => raw with { EgtRed = float.NaN },
            "EGT amber" => raw with { EgtAmber = float.PositiveInfinity },
            "N1 red" => raw with { N1Red = float.NaN },
            "N1 amber" => raw with { N1Amber = float.NaN },
            "TAT" => raw with { Tat = nan },
            "reference EPR" => raw with { ReferenceEpr = nan },
            "manual EPR, manual" => raw with { ManualEprRight = nan },
            "N1 max" => raw with { Left = IdleLeft with { MaxN1 = float.NaN } },
            "EGT max" => raw with { Right = IdleLeft with { MaxEgt = float.PositiveInfinity } },
            "anti-ice valve" => raw with { Left = IdleLeft with { AntiIce = new(nan, 2) } },
            "alternate N1, flagged" => raw with { Left = IdleLeft with { AlternateFlagged = true, N1Alternate = nan } },
            "PW N2" => raw with { Left = IdleLeft with { Type = 1, N2 = nan } },
            "alternate N1, not flagged" => raw with { Left = IdleLeft with { N1Alternate = nan } },
            "RB211 N2" => raw with { Left = IdleLeft with { N2 = nan } },
            "manual EPR, not manual" => raw with { ManualEprLeft = nan },
            "reference EPR, hidden" => raw with { ReferenceEpr = nan, Left = IdleLeft with { Reverser = 2 } },
            "EGT, compact" => raw with { Compact = true, Left = IdleLeft with { Egt = nan } },
            _ => throw new ArgumentOutOfRangeException(nameof(where)),
        };
        Assert.Equal(corrupt, Decode(raw, manual).Corrupt);
    }

    [Fact]
    public void A_dark_screens_values_that_are_not_numbers_read_blank()
    {
        // BlueBird may leave anything in an unpowered display's fields, and a dark reading is never spoken: read blank,
        // so a shutdown's ten such readings do not stop the reader as corrupt.
        double nan = double.NaN;
        var dark = Live() with
        {
            Powered = false, Clock = nan, Tat = nan, ReferenceEpr = nan, N1Amber = float.NaN, EgtAmber = float.PositiveInfinity,
            Left = IdleLeft with { Epr = nan, N1 = nan, Egt = double.PositiveInfinity, MaxN1 = float.NaN, AntiIce = new(nan, 0) },
            Right = IdleLeft with { MaxEgt = float.NaN, Epr = 1e39 },
        };
        var eicas = Decode(dark);
        Assert.False(eicas.Corrupt);
        Assert.False(eicas.Powered);
        Assert.Equal(new[] { V("EPR", null, null), V("N1", null, "19.9"), V("EGT", null, "357"), V("TAT", null) }, eicas.Values);
        Assert.False(eicas.Recall);
        Assert.Equal("TO", eicas.ThrustReference);
        Assert.Equal(LiveList.Length, eicas.Messages.Count);
        Assert.False(Read(Place(dark)).Corrupt);

        // Without its red limit the N1 or EGT is not drawn at all.
        var noLimits = Decode(Live() with { Powered = false, N1Red = float.NaN, EgtRed = float.NaN });
        Assert.False(noLimits.Corrupt);
        Assert.Equal(V("N1", null, null), Row(noLimits, "N1"));
        Assert.Equal(V("EGT", null, null), Row(noLimits, "EGT"));

        // Each way the screen goes dark reads blank; lit, the same values are corrupt.
        foreach (var off in new[] { dark with { Powered = true, Failed = true }, dark with { Powered = true, TimedBlank = true },
            dark with { Powered = true, Blank = 25 } })
            Assert.False(Decode(off).Corrupt);
        Assert.True(Decode(dark with { Powered = true }).Corrupt);

        // A torn list is corrupt whatever the power, and so is a level missing from the table.
        Assert.True(Decode(dark with { Torn = true }).Corrupt);
        Assert.True(Decode(dark with { Main = [new(500, "WHAT", 7, false)] }).Corrupt);
    }

    [Fact]
    public void A_level_missing_from_the_table_or_a_torn_walk_is_corrupt()
    {
        var unknown = Decode(Live() with { Main = [.. LiveList, new(500, "WHAT", 7, false)] });
        Assert.True(unknown.Corrupt);
        Assert.Equal(Bbs757WordTable.Unrecognised, unknown.Messages[^1].Level);
        // Level 0 is in the table (posted as a communication message).
        Assert.False(Decode(Live() with { Communication = [new(258, " CABIN CALL", 0, false)] }).Corrupt);
        Assert.True(Decode(Live() with { Torn = true }).Corrupt);
    }

    [Fact]
    public void Readings_with_equal_contents_are_equal()
    {
        var a = Decode(Live());
        var b = Decode(Live() with { Main = LiveList.ToArray(), EnvelopeRows = new Bbs757EicasRawEnvelopeRow[3] });
        Assert.NotSame(a.Messages, b.Messages);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(EqualityComparer<Bbs757UpperEicasReading>.Default.Equals(a, b));   // the monitor's Same

        Assert.NotEqual(a, Decode(Live() with { Main = [.. LiveList.Take(4)] }));
        Assert.NotEqual(a, Decode(Live() with { Main = [.. LiveList.Take(4), Advisory(160, " PARKING BRAKE ")] }));
        Assert.NotEqual(a, a with { Values = [.. a.Values.Skip(1)] });
        Assert.NotEqual(a, a with { Envelope = ["IN-FLT START"] });
        Assert.NotEqual(a, a with { ThrustReference = "TO 1" });
        Assert.NotEqual(a, a with { RecallTime = 4023.9 });
        Assert.NotEqual(a, a with { Page = 2 });
        Assert.NotEqual(a, a with { PageShown = true });
        Assert.NotEqual(a, a with { PageLine = "PAGE 1" });
        Assert.NotEqual(a, a with { Recall = true });
        Assert.NotEqual(a, a with { RecallLine = "RECALL" });
        Assert.NotEqual(a, a with { Cancelled = 1 });
        Assert.NotEqual(a, a with { Powered = false });
        Assert.NotEqual(a, a with { Corrupt = true });
    }

    [Fact]
    public void Every_number_reads_the_same_under_de_DE_and_sv_SE()
    {
        var raw = Live() with
        {
            Left = IdleLeft with { Epr = 1.4567, N1 = 87.26, Egt = 612.9, MaxN1 = 109.2f, MaxEgt = 861.5f, Reverser = 1, Combusting = false },
            Right = IdleLeft with { Epr = 1.0, N1 = 101.04, Egt = 598.1 },
            ThrustNumber = 1, DtoFlagged = true, DerateShown = true, Derate = -5, Tat = -15.7, ReferenceEpr = 1.4472,
            OnGround = false, EnvelopeRows = [new(250, 260, 360), new(50, 180, 360), default],
        };
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
        {
            var eicas = TestCulture.Run(culture, () => Decode(raw));
            Assert.False(eicas.Corrupt);
            Assert.Equal(new[]
            {
                V("EPR", "1.46", "1.00"), V("N1", "87.3", "101.0"), V("EGT", "612", "598"), V("TAT", "-15c"),
                V("REV", "REV", null, "amber"), V("EPR reference", "1.45", "1.45"), V("N1 max", "109.2", null),
                V("EGT max", "861", null),
            }, eicas.Values);
            Assert.Equal("D-TO 1, -5c", eicas.ThrustReference);
            Assert.Equal(new[] { "IN-FLT START", "FL 250 260-360 KTS", "FL 50 180-360 KTS" }, eicas.Envelope);
            Assert.Equal("D-TO 1, +25c", TestCulture.Run(culture, () => Decode(raw with { Derate = 25 })).ThrustReference);
            Assert.Equal("+5c", Row(TestCulture.Run(culture, () => Decode(raw with { Tat = 5.2 })), "TAT")!.Value.Left);
        }
    }
}
