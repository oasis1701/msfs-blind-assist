using System.Buffers.Binary;
using System.Text;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The 757 lower EICAS sampler (plan 25, Task 4): it finds the lower EICAS in the gauge manager by its vptr,
/// follows the layout to the engines, hydraulics, APU, oxygen, computer and the STATUS list, and returns the rows as
/// drawn. Values are those of the evidence's live column (cruise, engines running, STATUS list empty), placed through
/// the sample layout of <see cref="Bbs757LowerEicasLayoutTests"/>. A torn status list is corrupt as a whole.</summary>
public class Bbs757LowerEicasReaderTests
{
    private const ulong Base = 0x20000000;
    private const int Size = 0x100000;
    private const uint Lower = 0x2000, Left = 0x10000, Right = 0x20000, Computer = 0x30000, Hyd = 0x40000, Apu = 0x50000,
        Oxy = 0x60000, Settings = 0x70000, Vector = 0x80000, Records = 0x90000, Texts = 0xA0000,
        HydRecords = 0xB0000;

    private static readonly Bbs757LowerEicasLayout Layout = Bbs757LowerEicasLayoutTests.Sample();

    private static void Put(byte[] b, uint at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan((int)at), v);
    private static void PutD(byte[] b, uint at, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan((int)at), v);
    private static void PutF(byte[] b, uint at, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan((int)at), v);

    /// <summary>Cruise memory: engine page, both engines valid (an RR), pounds, no max boxes, an empty STATUS list, the
    /// evidence's live STATUS values.</summary>
    private static byte[] Memory(int page = 1, bool powered = true, int leftInvalid = 0, string[]? messages = null,
        Action<byte[]>? tweak = null)
    {
        var b = new byte[Size];
        Put(b, Layout.ManagerBase + 8, (int)Lower);
        Put(b, Lower, (int)Layout.LowerVptr);
        b[Lower + Layout.PowerSet] = (byte)(powered ? 1 : 0);
        Put(b, Lower + (uint)Layout.Page, page);
        Put(b, Lower + (uint)Layout.LeftEngine, (int)Left);
        Put(b, Lower + (uint)Layout.RightEngine, (int)Right);
        Put(b, Lower + 132, (int)Computer);
        Put(b, Lower + 276, (int)Hyd);
        Put(b, Lower + 272, (int)Apu);
        Put(b, Lower + 284, (int)Oxy);
        Put(b, Lower + 288, (int)Settings);
        Put(b, Settings + 596, 1);

        Engine(b, Left, leftInvalid, 81.99, 77.0, 3600, 91, 114, 0.58, 1.8);
        Engine(b, Right, 0, 82.09, 77.0, 3600, 89, 109, 0.67, 1.7);

        // Hydraulics: capacity, quantity per system; pressure per system.
        double[] cap = { 1, 1, 1 }, qty = { 0.98, 0.97, 1.00 }, press = { 3000, 2880, 2950 };
        // The quantities are a vector's records in a separate block; hyd+2996 holds its begin pointer.
        Put(b, Hyd + 2996, (int)HydRecords);
        for (int i = 0; i < 3; i++)
        {
            PutD(b, HydRecords + 40u * (uint)i, cap[i]);
            PutD(b, HydRecords + 40u * (uint)i + 8, qty[i]);
            PutD(b, Hyd + 2896 + 8u * (uint)i, press[i]);
        }
        PutD(b, Apu + 1544, -55);
        PutD(b, Apu + 1528, 0);
        PutD(b, Apu + 1600, 0.75);
        b[Oxy + 946] = 1;
        Put(b, Oxy + 440, 1850);
        b[Oxy + 456] = 1;
        PutD(b, Oxy + 464, 0);
        PutD(b, Oxy + 472, 1760);
        PutF(b, Lower + 548, 0f);
        PutF(b, Lower + 536, -0.003f);
        PutF(b, Lower + 544, 0.007f);
        PutF(b, Lower + 540, 0.003f);

        WriteList(b, messages ?? Array.Empty<string>());
        tweak?.Invoke(b);
        return b;
    }

    private static void Engine(byte[] b, uint e, int invalid, double n2, double n3, double ff, double oilP, double oilT,
        double oilQ, double vib)
    {
        Put(b, e + (uint)Layout.EngineInvalid, invalid == 0 ? 0 : 2);
        Put(b, e + (uint)Layout.EngineType, 0);
        uint d = e + (uint)Layout.EngineToData;
        PutD(b, d + 16, n2);
        PutD(b, d + 24, n3);
        PutD(b, d + 88, ff);
        PutD(b, d + 96, oilP);
        PutD(b, d + 104, oilT);
        PutD(b, d + 112, oilQ);
        PutD(b, d + 120, vib);
    }

    /// <summary>The list: vector of record pointers; each record 48 bytes with a UTF-32 text pointer and length.</summary>
    private static void WriteList(byte[] b, string[] messages)
    {
        uint list = Layout.Status!.ListAddress;
        Put(b, list, (int)Vector);
        Put(b, list + 4, (int)(Vector + 4u * (uint)messages.Length));
        uint text = Texts;
        for (int i = 0; i < messages.Length; i++)
        {
            uint rec = Records + 48u * (uint)i;
            Put(b, Vector + 4u * (uint)i, (int)rec);
            Put(b, rec, (int)text);
            Put(b, rec + 4, messages[i].Length);
            Put(b, rec + 8, 200 - i);
            Encoding.UTF32.GetBytes(messages[i] + "\0").CopyTo(b, (int)text);
            text += 4u * (uint)(messages[i].Length + 1);
        }
    }

    private static Bbs757LowerEicasReader Reader(byte[] bytes, Bbs757LowerEicasLayout? layout = null)
    {
        var src = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        return new Bbs757LowerEicasReader(new ModuleMemory(src, Base), src, layout ?? Layout);
    }

    private static Bbs757LowerEicasReading Read(byte[] bytes, Bbs757LowerEicasLayout? layout = null)
    {
        using var r = Reader(bytes, layout);
        Assert.True(r.TryRead(default, out var reading));
        return reading;
    }

    [Fact]
    public void Reads_the_page_shown()
    {
        var engine = Read(Memory(page: 1));
        Assert.Equal((1, "engine", true, false), (engine.PageValue, engine.Page, engine.Powered, engine.Corrupt));
        Assert.Equal("STATUS", Read(Memory(page: 2)).Page);
        Assert.Equal("engine off", Read(Memory(page: 3)).Page);
        var blank = Read(Memory(page: 0));
        Assert.Equal("blank", blank.Page);
        Assert.Empty(blank.PageRows);
        Assert.Equal("blank", Read(Memory(page: 9)).Page);
    }

    [Fact]
    public void Reads_the_engine_page_rows_as_drawn()
    {
        var reading = Read(Memory());
        Assert.Equal(new[]
        {
            "N2: 81, 82",
            "N3: 77, 77",
            "FF: 3.6, 3.6",
            "OIL PRESS: 91, 89",
            "OIL TEMP: 114, 109",
            "OIL QTY: 12, 14",
            "VIB: 1.8, 1.7",
        }, reading.PageRows);
    }

    [Fact]
    public void Reads_the_status_page_rows_as_drawn()
    {
        var reading = Read(Memory(page: 2));
        Assert.Equal(new[]
        {
            "HYD QTY: L 0.98, C 0.97, R 1.00",
            "HYD PRESS: L 3000, C 2880, R 2950",
            "APU EGT: -55",
            "RPM: 0",
            "OIL Q: 0.75",
            "OXY PRESS: 1760",
            "RUD: centered",
            "AIL left: centered",
            "ELEV: 1 % down",
            "AIL right: centered",
            "FF: 3.6, 3.6",
            "BRAKE TEMP: 1: 0, 2: 0, 3: 0, 4: 0, 5: 0, 6: 0, 7: 0, 8: 0",
        }, reading.PageRows);
    }

    [Fact]
    public void The_hydraulic_quantities_are_read_through_the_vectors_begin_pointer()
    {
        var qty = Read(Memory(page: 2)).PageRows.First(r => r.StartsWith("HYD QTY"));
        Assert.Equal("HYD QTY: L 0.98, C 0.97, R 1.00", qty);
    }

    [Fact]
    public void Quantities_laid_out_inline_no_longer_read_as_the_live_values()
    {
        // The old layout: the records inline at hyd+2996, so the field holds a double, not a pointer.
        var inline = Memory(page: 2, tweak: b =>
        {
            for (int i = 0; i < 3; i++)
            {
                PutD(b, Hyd + 2996 + 40u * (uint)i, 1);
                PutD(b, Hyd + 2996 + 40u * (uint)i + 8, new[] { 0.98, 0.97, 1.00 }[i]);
            }
        });
        using var r = Reader(inline);
        Assert.False(r.TryRead(default, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x7fff0000)]
    public void A_null_or_outside_begin_pointer_is_a_lost_read(int begin)
    {
        using var r = Reader(Memory(page: 2, tweak: b => Put(b, Hyd + 2996, begin)));
        Assert.False(r.TryRead(default, out _));
    }

    [Fact]
    public void Reads_the_status_messages_in_order()
    {
        var reading = Read(Memory(messages: new[] { "WARN ELEX", "L EICAS CMPTR", "CABIN ALT AUTO 1" }));
        Assert.False(reading.Corrupt);
        Assert.Equal(new[] { "WARN ELEX", "L EICAS CMPTR", "CABIN ALT AUTO 1" }, reading.StatusMessages);
        // The engine page shows the STATUS cue while the list is not empty.
        Assert.Contains("STATUS", reading.PageRows);
        // Read whatever the page, and listed on the STATUS page after FF.
        var status = Read(Memory(page: 2, messages: new[] { "WARN ELEX" }));
        Assert.Equal(new[] { "WARN ELEX" }, status.StatusMessages);
        int ff = status.PageRows.ToList().FindIndex(r => r.StartsWith("FF:"));
        Assert.Equal("WARN ELEX", status.PageRows[ff + 1]);
    }

    [Fact]
    public void A_blanked_value_reads_as_drawn()
    {
        var reading = Read(Memory(leftInvalid: 1));
        Assert.StartsWith("N2: blank, 82", reading.PageRows[0]);
        Assert.Contains("FF: blank, 3.6", reading.PageRows);
        Assert.Contains("OIL TEMP: blank, 109", reading.PageRows);
    }

    [Fact]
    public void A_torn_status_list_is_corrupt()
    {
        string[] one = { "WARN ELEX" };
        // Count above the layout's cap.
        var tooMany = Memory(messages: one, tweak: b => Put(b, Layout.Status!.ListAddress + 4, (int)(Vector + 4u * 165)));
        // A record pointer outside the module.
        var wild = Memory(messages: one, tweak: b => Put(b, Vector, 0x7fff0000));
        // A zero length, and a length beyond the bound.
        var zeroLength = Memory(messages: one, tweak: b => Put(b, Records + 4, 0));
        var longLength = Memory(messages: one, tweak: b => Put(b, Records + 4, 65));
        // A surrogate and a control character in the UTF-32 text.
        var surrogate = Memory(messages: one, tweak: b => Put(b, Texts, 0xD800));
        var control = Memory(messages: one, tweak: b => Put(b, Texts, 7));
        // end before begin.
        var inverted = Memory(messages: one, tweak: b => Put(b, Layout.Status!.ListAddress + 4, (int)(Vector - 4)));
        // Corrupt as a whole: the second of two messages torn discards the first too.
        var second = Memory(messages: new[] { "WARN ELEX", "L EICAS CMPTR" }, tweak: b => Put(b, Records + 48 + 4, 0));

        foreach (var bytes in new[] { tooMany, wild, zeroLength, longLength, surrogate, control, inverted, second })
        {
            var reading = Read(bytes);
            Assert.True(reading.Corrupt);
            Assert.Empty(reading.StatusMessages);
            Assert.Empty(reading.PageRows);
        }
    }

    [Fact]
    public void A_display_that_is_not_drawing_is_unpowered_with_no_rows()
    {
        var off = Read(Memory(powered: false));
        Assert.False(off.Powered);
        Assert.False(off.Corrupt);
        Assert.Empty(off.PageRows);
        Assert.Null(off.Page);

        var blankByte = Read(Memory(tweak: b => b[Lower + Layout.BlankClear] = 1));
        Assert.False(blankByte.Powered);
        var cleared = Read(Memory(tweak: b => b[Lower + Layout.PowerClear] = 1));
        Assert.False(cleared.Powered);
    }

    [Fact]
    public void An_absent_page_part_is_its_one_row()
    {
        var layout = Layout with { Engine = null, EngineMissing = "no engine page", Status = null, StatusMissing = "no list" };
        var bytes = Memory();
        Assert.Equal(new[] { "engine page: not read: no engine page" }, Read(bytes, layout).PageRows);
        Put(bytes, Lower + (uint)Layout.Page, 2);
        var status = Read(bytes, layout);
        Assert.Equal(new[] { "STATUS page: not read: no list" }, status.PageRows);
        Assert.Empty(status.StatusMessages);
        Assert.False(status.Corrupt);   // the HP spools are still read and range-checked without an engine page part
        var badSpool = Read(Memory(tweak: b => PutD(b, Left + (uint)Layout.EngineToData + 24, double.NaN)), layout);
        Assert.True(badSpool.Corrupt);
    }

    [Fact]
    public void A_pointer_outside_the_module_or_no_lower_eicas_is_a_failed_read()
    {
        using (var r = Reader(Memory(tweak: b => Put(b, Lower + (uint)Layout.LeftEngine, 0x7fff0000))))
            Assert.False(r.TryRead(default, out _));
        using (var r = Reader(Memory(tweak: b => Put(b, Lower, 0x1234))))
            Assert.False(r.TryRead(default, out _));
    }

    [Fact]
    public void A_non_finite_value_is_corrupt()
    {
        var reading = Read(Memory(tweak: b => PutD(b, Left + (uint)Layout.EngineToData + 16, double.NaN)));
        Assert.True(reading.Corrupt);
        Assert.Empty(reading.PageRows);
    }

    [Fact]
    public void An_unpowered_reading_with_a_non_finite_field_is_not_corrupt()
    {
        // Nothing is drawn while the display is not powered, so the value-range checks are off.
        var reading = Read(Memory(powered: false, tweak: b => PutD(b, Left + (uint)Layout.EngineToData + 16, double.NaN)));
        Assert.False(reading.Powered);
        Assert.False(reading.Corrupt);
        // The status list's structural checks stay on.
        var torn = Read(Memory(powered: false, messages: new[] { "WARN ELEX" }, tweak: b => Put(b, Vector, 0x7fff0000)));
        Assert.True(torn.Corrupt);
    }

    [Fact]
    public void A_powered_corrupt_reading_logs_its_cause_once_per_change()
    {
        var lines = new List<string>();
        var bytes = Memory(tweak: b => PutD(b, Left + (uint)Layout.EngineToData + 16, double.NaN));
        var src = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        using var r = new Bbs757LowerEicasReader(new ModuleMemory(src, Base), src, Layout, lines.Add);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(r.TryRead(default, out var reading));
            Assert.True(reading.Corrupt);
        }
        var line = Assert.Single(lines);
        Assert.Contains("left engine N2", line);
        Assert.Contains("NaN", line);

        // Another cause is a change: logged again.
        PutD(bytes, Left + (uint)Layout.EngineToData + 16, 81.0);
        PutD(bytes, Right + (uint)Layout.EngineToData + 24, double.PositiveInfinity);
        Assert.True(r.TryRead(default, out _));
        Assert.True(r.TryRead(default, out _));
        Assert.Equal(2, lines.Count);
        Assert.Contains("right HP spool", lines[1]); // an RR's N3 is its HP spool
    }

    [Fact]
    public void A_pratt_and_whitney_engines_hp_spool_is_its_n2_and_it_has_no_n3_row()
    {
        var reading = Read(Memory(tweak: b =>
        {
            Put(b, Left + (uint)Layout.EngineType, 1);
            PutD(b, Left + (uint)Layout.EngineToData + 16, 65.5);
        }));
        Assert.False(reading.Corrupt);
        Assert.DoesNotContain(reading.PageRows, r => r.StartsWith("N3"));

        // Its HP spool, range-checked first, is the N2 field: a PW's N2 that is not a number is that spool's fault.
        var lines = new List<string>();
        var bytes = Memory(tweak: b =>
        {
            Put(b, Left + (uint)Layout.EngineType, 1);
            PutD(b, Left + (uint)Layout.EngineToData + 16, double.NaN);
        });
        var src = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        using var r = new Bbs757LowerEicasReader(new ModuleMemory(src, Base), src, Layout, lines.Add);
        Assert.True(r.TryRead(default, out var bad));
        Assert.True(bad.Corrupt);
        Assert.Contains("left HP spool", Assert.Single(lines));
    }
}
