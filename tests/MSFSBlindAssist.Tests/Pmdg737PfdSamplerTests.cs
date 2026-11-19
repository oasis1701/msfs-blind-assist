using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ModulePfdSampler against the PFD fixture's layout in <see cref="Pmdg737PfdTestMemory"/>: the light reading (units,
/// panel, IAS, altitude, the messages), the full one (every expression of the layout), the checks, and lost memory.
/// </summary>
public class Pmdg737PfdSamplerTests
{
    private static Pmdg737PfdLayout L => M.Layout;

    private static PfdField Only(PfdExpr e) => M.Only(e);

    private static int MessageIndex(string text) => L.AllMessages().ToList().FindIndex(x => x.Text == text);

    /// <summary>A memory source that keeps every read it was asked for.</summary>
    private sealed class RecordingMemory(IProcessMemorySource inner) : IProcessMemorySource
    {
        public readonly List<(ulong Address, int Length)> Reads = new();

        public IEnumerable<MemoryRegion> Regions() => inner.Regions();

        public bool TryRead(ulong address, Span<byte> into)
        {
            Reads.Add((address, into.Length));
            return inner.TryRead(address, into);
        }

        public MemoryRegion? RegionAt(ulong address) => inner.RegionAt(address);

        public void Dispose() => inner.Dispose();

        public bool Covered(ulong address) => Reads.Any(r => address >= r.Address && address < r.Address + (ulong)r.Length);
    }

    private static string? Corrupt(M m, Pmdg737PfdLayout? layout = null, bool full = false)
    {
        using var sampler = m.Sampler(layout: layout, full: full);
        Assert.False(sampler.TryRead(out _, out var corrupt));
        return corrupt;
    }

    /// <summary>An 8-byte place in the glass data object that no field of the layout reads.</summary>
    private static int FreeGlassOffset(int skip = 0)
    {
        var g = L.Objects.Glass;
        var used = PfdLayoutWalk.Expressions(L).SelectMany(x => PfdEval.Fields(x.Expression)).Where(f => f.Owner == g)
            .Select(f => (f.Field.Offset, End: f.Field.Offset + 8)).ToList();
        return Enumerable.Range(1, (int)g.Size / 8 - 1).Select(k => k * 8)
            .Where(o => used.All(u => o + 8 <= u.Offset || o >= u.End)).Skip(skip).First();
    }

    // ---- the light reading ----

    [Fact]
    public void Reads_the_reference_reading_through_the_layout()
    {
        var m = new M();
        var autopilot = L.FmaStrip.Extras.Single(x => x.Text == "AUTOPILOT");
        m.Set(Only(autopilot.Shown), 1);
        var u = L.Units;
        m.I(m.EfisAt(u.LowerSelector.Offset + u.LowerSelector.Stride * u.SelectorSide), 2);

        var s = m.Sample();

        Assert.False(s.Full);
        Assert.Null(s.Reading);
        Assert.Equal(u.UnitCount, s.UnitPages.Count);
        Assert.Equal(u.PfdPage, s.UnitPages[m.PfdUnit.Unit]);
        Assert.Equal(2, s.LowerSelector);
        Assert.Equal(0, s.UnitPanel);
        Assert.Equal(new PfdPanelState(Fpv: true, Mtrs: false, BaroStd: false, Powered: true), s.Panel);
        Assert.Equal(160.2, s.Ias);
        Assert.Equal(5000, s.Altitude);
        Assert.True(s.MessagesUp[MessageIndex("AUTOPILOT")]);
        Assert.False(s.MessagesUp[MessageIndex("SPD")]);
        Assert.Equal(L.AllMessages().Select(x => m.Value(x.Shown) != 0), s.MessagesUp);
    }

    [Fact]
    public void The_panel_is_the_one_the_pfd_unit_follows_else_the_captains_entry()
    {
        var m = new M();
        var u = L.Units;
        m.I(m.EfisAt(m.PfdUnit.Unit * u.UnitStride + u.UnitPanelOffset), 1);
        m.I(m.EfisAt(L.Panel.PanelIndexOffset), 0);
        m.Panel(1, mtrs: true, powered: true);
        m.Set((PfdField)L.Altitude.Altitude, 7000, index: 1);

        var s = m.Sample();
        Assert.Equal(1, s.UnitPanel);
        Assert.Equal(new PfdPanelState(Fpv: false, Mtrs: true, BaroStd: false, Powered: true), s.Panel);
        Assert.Equal(7000, s.Altitude);

        // No unit showing the PFD page: the EFIS object's Captain entry.
        m.I(m.EfisAt(m.PfdUnit.Unit * u.UnitStride + u.PageOffset), u.DarkPages[0]);
        var t = m.Sample();
        Assert.Equal(0, t.UnitPanel);
        Assert.Equal(5000, t.Altitude);
    }

    [Fact]
    public void A_panel_index_with_no_panel_in_cefis_is_corrupt()
    {
        var m = new M();
        m.I(m.EfisAt(m.PfdUnit.Unit * L.Units.UnitStride + L.Units.UnitPanelOffset), (int)(L.Objects.Efis.Size / L.Panel.PanelStride));
        Assert.NotNull(Corrupt(m));
    }

    [Fact]
    public void A_captain_unit_outside_the_units_is_corrupt_not_a_crash()
    {
        var m = new M();
        var u = L.Units;
        var layout = L with { Units = u with { CaptainUnits = new[] { u.CaptainUnits[0] with { Unit = u.UnitCount } } } };
        Assert.Contains("not one of the", Corrupt(m, layout));
    }

    [Fact]
    public void A_light_reading_reads_only_the_units_panel_checks_and_messages()
    {
        var m = new M();
        // The V1 speed reference value: only the window reads it.
        ulong v1 = M.Base + m.At(L.SpeedReference.Positions[0].Value);

        var light = new RecordingMemory(m.Source());
        using (var sampler = m.Sampler(light))
            Assert.True(sampler.TryRead(out _, out var corrupt), corrupt);
        Assert.False(light.Covered(v1));

        var full = new RecordingMemory(m.Source());
        using (var sampler = m.Sampler(full, full: true))
            Assert.True(sampler.TryRead(out _, out var corrupt), corrupt);
        Assert.True(full.Covered(v1));
    }

    // ---- the full reading ----

    [Fact]
    public void Full_reading_fills_every_part()
    {
        var m = new M();
        // Every field the layout reads holds its own value (fields an index or the IRU pointer is worked out from
        // keep theirs), then the reference reading's IAS, tapes and IRU.
        var expressions = PfdLayoutWalk.Expressions(L);
        var kept = expressions.SelectMany(x => PfdEval.Fields(x.Expression)).Where(f => f.Index is not null)
            .SelectMany(f => PfdEval.Fields(f.Index!)).Append(M.IruCondition.Pointer).Select(f => (f.Owner, f.Field)).ToHashSet();
        int k = 0;
        foreach (var f in expressions.SelectMany(x => PfdEval.Fields(x.Expression)).DistinctBy(f => (f.Owner, f.Field)))
        {
            if (kept.Contains((f.Owner, f.Field))) continue;
            k++;
            m.Set(f, f.Field.Kind switch { GlassFieldKind.Double => 1000 + k + 0.25, GlassFieldKind.Int => 100 + k, _ => 1 },
                f.Index is null ? 0 : (long)m.Value(f.Index));
        }
        Assert.True(k > 100);
        m.Set(Only(L.Speed.TapeShown), 1);
        m.Set((PfdField)L.Speed.Ias, 160.2);
        // The approach ident drawn.
        var ident = L.Optional.Approach!.Ident;
        m.Set(Only(ident.Shown), 1);
        m.Text(m.Address(ident.Owner) + (uint)ident.Offset, "IKJF");

        var s = m.Sample(full: true);

        Assert.True(s.Full);
        var r = Assert.IsType<PfdValues>(s.Reading);
        Assert.Equal(160.2, s.Ias);
        foreach (var (path, e) in expressions)
        {
            double expected = m.Value(e, s.UnitPanel), read = r.Value(e);
            Assert.True(expected.Equals(read), $"{path}: expected {expected}, read {read}");
        }
        var flaps = L.Speed.FlapBugs;
        for (int i = 0; i < flaps.Labels.Count; i++)
        {
            var speed = flaps.Speeds with { Index = new PfdConst(i) };
            Assert.Equal(m.Value(speed), r.Value(speed));
        }
        Assert.Equal("IKJF", r.Text(ident));
    }

    [Fact]
    public void An_optional_part_missing_from_the_layout_reads_null()
    {
        var m = new M();
        var layout = L with { Optional = L.Optional with { Tcas = null } };
        var others = PfdLayoutWalk.Expressions(layout).SelectMany(x => PfdEval.Fields(x.Expression)).Select(f => (f.Owner, f.Field)).ToHashSet();
        var tcasOnly = L.Optional.Tcas!.PitchZones.SelectMany(z => new[] { z.From, z.Shown }).SelectMany(PfdEval.Fields)
            .Where(f => !others.Contains((f.Owner, f.Field))).ToList();
        Assert.NotEmpty(tcasOnly);

        var s = m.Sample(full: true, layout: layout);

        foreach (var f in tcasOnly) Assert.Throws<InvalidOperationException>(() => s.Reading!.Value(f));
        Assert.Equal(m.Value(layout.Attitude.Pitch), s.Reading!.Value(layout.Attitude.Pitch));
    }

    [Fact]
    public void A_field_across_two_blocks_is_read_whole()
    {
        var m = new M();
        var across = new PfdField(L.Objects.Glass, new GlassField(ModulePfdSampler.BlockSize - 4, 0, GlassFieldKind.Double));
        m.Set(across, 123.4);
        var s = m.Sample(layout: L with { Speed = L.Speed with { Ias = across } });
        Assert.Equal(123.4, s.Ias);
    }

    [Fact]
    public void An_ident_is_checked_only_while_shown()
    {
        var m = new M();
        var ident = L.Optional.Approach!.Ident;
        uint at = m.Address(ident.Owner) + (uint)ident.Offset;
        m.Bytes[at] = 0x07;
        m.Bytes[at + 1] = 0;

        Assert.Null(m.Sample(full: true).Reading!.Text(ident));
        m.Set(Only(ident.Shown), 1);
        Assert.NotNull(Corrupt(m, full: true));
    }

    // ---- the checks ----

    [Fact]
    public void A_nan_ias_while_the_tape_is_drawn_is_corrupt()
    {
        var m = new M();
        m.Set((PfdField)L.Speed.Ias, double.NaN);
        Assert.Contains("IAS", Corrupt(m));
    }

    [Fact]
    public void A_nan_ias_under_the_spd_flag_is_accepted()
    {
        var m = new M();
        m.Set((PfdField)L.Speed.Ias, double.NaN);
        m.Set(Only(L.Speed.TapeShown), 0);

        var s = m.Sample();

        Assert.True(double.IsNaN(s.Ias));
        Assert.True(s.MessagesUp[MessageIndex("SPD")]);
    }

    [Fact]
    public void A_nan_altitude_while_the_tape_is_drawn_is_corrupt()
    {
        var m = new M();
        m.Set((PfdField)L.Altitude.Altitude, double.NaN);
        Assert.Contains("altitude", Corrupt(m));
    }

    [Fact]
    public void A_nan_altitude_under_the_alt_flag_is_accepted()
    {
        var m = new M();
        m.Set((PfdField)L.Altitude.Altitude, double.NaN);
        m.Set(Only(L.Altitude.TapeShown), 1);

        var s = m.Sample();

        Assert.True(double.IsNaN(s.Altitude));
        Assert.True(s.MessagesUp[MessageIndex("ALT")]);
    }

    [Fact]
    public void An_index_past_its_object_is_corrupt_only_where_pmdg_reads_it()
    {
        // A message PMDG draws by a choice whose other branch reads an element past the glass data object's end.
        var m = new M();
        var g = L.Objects.Glass;
        var choice = new PfdField(g, new GlassField(FreeGlassOffset(), 0, GlassFieldKind.Int));
        var index = new PfdField(g, new GlassField(FreeGlassOffset(1), 0, GlassFieldKind.Int));
        var far = new PfdField(g, new GlassField(0, 8, GlassFieldKind.Double), index);
        var test = new PfdMessage("TEST", PfdPlace.Fma, new PfdSelect(choice, new PfdConst(0), new PfdBinary(PfdOp.Gt, far, new PfdConst(0))), false, false);
        var layout = L with { FmaStrip = L.FmaStrip with { Extras = L.FmaStrip.Extras.Append(test).ToList() } };
        m.Set(index, 100_000);
        m.Set(choice, 1);

        Assert.False(m.Sample(layout: layout).MessagesUp[^1]);
        m.Set(choice, 0);
        Assert.NotNull(Corrupt(m, layout));
    }

    // ---- lost memory ----

    [Fact]
    public void A_pointer_outside_the_module_is_lost()
    {
        var m = new M();
        m.U(L.Objects.Glass.Global, M.Size + 0x1000);
        Assert.Null(Corrupt(m));
        m.U(L.Objects.Glass.Global, 0);
        Assert.Null(Corrupt(m));
    }

    [Fact]
    public void An_iru_pointer_outside_the_module_is_corrupt()
    {
        var m = new M();
        m.U(m.At(M.IruCondition.Pointer), M.Size + 0x1000);
        Assert.NotNull(Corrupt(m));
    }

    [Fact]
    public void An_object_committed_after_start_is_read()
    {
        var m = new M();
        uint split = m.Address(L.Objects.Glass);
        var low = m.Bytes.AsSpan(0, (int)split).ToArray();
        var high = m.Bytes.AsSpan((int)split).ToArray();
        var src = new FakeProcessMemory().Add(new MemoryRegion(M.Base, M.Base, split, true, true, true), low);
        using var sampler = m.Sampler(src);

        Assert.False(sampler.TryRead(out _, out var before));
        Assert.Null(before);
        src.Add(new MemoryRegion(M.Base + split, M.Base, (ulong)high.Length, true, true, true), high);
        Assert.True(sampler.TryRead(out var s, out var after), after);
        Assert.Equal(160.2, s.Ias);
    }

    [Fact]
    public void A_reading_asks_for_each_region_once()
    {
        var m = new M();
        var src = m.Source();
        using var sampler = m.Sampler(src, full: true);

        Assert.True(sampler.TryRead(out _, out var corrupt), corrupt);
        Assert.Equal(1, src.RegionQueries);
        Assert.True(sampler.TryRead(out _, out corrupt), corrupt);
        Assert.Equal(2, src.RegionQueries); // asked again by the next reading
    }
}
