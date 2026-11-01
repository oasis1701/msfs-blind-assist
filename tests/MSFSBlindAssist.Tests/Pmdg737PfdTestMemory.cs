using System.Text;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 737 module's memory for the PFD sampler's tests: every object the PFD fixture's layout reads, one after another
/// from <see cref="FirstObject"/>, each global holding its object, and the IRU object the IRS object's IRU pointer
/// leads to; every value placed through the layout's own fields (never by literal offsets). It holds a reading of the
/// Captain's PFD: the first unit that always draws it showing the PFD page, following panel 0 (powered, FPV on); IAS
/// 160.2 and altitude 5000 with both tapes drawn; the IRU's condition 6. Tests change it through the helpers, then read
/// it with <see cref="Sample"/> or <see cref="Sampler"/>; <see cref="Value"/> works an expression out from these bytes
/// without the sampler.
/// </summary>
internal sealed class Pmdg737PfdTestMemory
{
    public const ulong Base = 0x10000000;
    public const uint FirstObject = 0x300000, ObjectSpacing = 0x40000;
    public const int Size = 0x800000;

    public static readonly Pmdg737PfdLayout Layout = GoldenLayouts.Pfd;

    /// <summary>The ATT flag's IRU condition: the IRS object's IRU pointer, then the IRU's field.</summary>
    public static readonly PfdPointed IruCondition =
        PfdLayoutWalk.Expressions(Layout).SelectMany(x => PointedIn(x.Expression)).Distinct().Single();

    public readonly byte[] Bytes = new byte[Size];

    /// <summary>Where each object the layout reads lies.</summary>
    public readonly IReadOnlyDictionary<GlassObject, uint> Objects;

    /// <summary>The IRU object the IRS object's IRU pointer holds.</summary>
    public readonly uint Iru;

    /// <summary>The display unit showing the Captain's PFD.</summary>
    public readonly PfdCaptainUnit PfdUnit;

    public Pmdg737PfdTestMemory()
    {
        var objects = new Dictionary<GlassObject, uint>();
        uint at = FirstObject;
        foreach (var o in PfdLayoutWalk.Owners(Layout))
        {
            if (o.Size > ObjectSpacing) throw new InvalidOperationException($"object 0x{o.Global:x} is larger than the spacing");
            objects[o] = at;
            U(o.Global, at);
            at += ObjectSpacing;
        }
        Objects = objects;
        Iru = at;
        U(At(IruCondition.Pointer, (long)Value(IruCondition.Pointer.Index ?? new PfdConst(0))), Iru);
        Put(Iru + (uint)IruCondition.Field.Offset, IruCondition.Field.Kind, 6);

        var u = Layout.Units;
        PfdUnit = u.CaptainUnits.First(c => c.Page == u.PfdPage && c.WhenSelector is null);
        I(EfisAt(PfdUnit.Unit * u.UnitStride + u.PageOffset), u.PfdPage);
        Panel(0, fpv: true, powered: true);

        Set(Only(Layout.Speed.TapeShown), 1);
        Set((PfdField)Layout.Speed.Ias, 160.2);
        Set((PfdField)Layout.Altitude.Altitude, 5000);
    }

    /// <summary>The one field an expression reads.</summary>
    public static PfdField Only(PfdExpr e) => PfdEval.Fields(e).Single();

    private static IEnumerable<PfdPointed> PointedIn(PfdExpr e) => e switch
    {
        PfdPointed p => new[] { p },
        PfdField { Index: { } i } => PointedIn(i),
        PfdSelect s => PointedIn(s.Condition).Concat(PointedIn(s.IfTrue)).Concat(PointedIn(s.IfFalse)),
        PfdUnary u => PointedIn(u.Operand),
        PfdBinary b => PointedIn(b.Left).Concat(PointedIn(b.Right)),
        _ => Array.Empty<PfdPointed>(),
    };

    public uint Address(GlassObject o) => Objects[o];

    public uint EfisAt(int offset) => Address(Layout.Objects.Efis) + (uint)offset;

    /// <summary>Where a field's element lies.</summary>
    public uint At(PfdField f, long index = 0) => (uint)(Address(f.Owner) + f.Field.Offset + f.Field.Stride * index);

    /// <summary>Places a field's element by its kind.</summary>
    public void Set(PfdField f, double v, long index = 0) => Put(At(f, index), f.Field.Kind, v);

    /// <summary>The Captain's EFIS panel <paramref name="panel"/>'s switches and power.</summary>
    public void Panel(int panel, bool fpv = false, bool mtrs = false, bool std = false, bool powered = false)
    {
        var p = Layout.Panel;
        uint at = EfisAt(p.PanelBase + panel * p.PanelStride);
        Put(at + (uint)p.Fpv.Offset, p.Fpv.Kind, fpv ? 1 : 0);
        Put(at + (uint)p.Mtrs.Offset, p.Mtrs.Kind, mtrs ? 1 : 0);
        Put(at + (uint)p.BaroStd.Offset, p.BaroStd.Kind, std ? 1 : 0);
        Put(at + (uint)p.Powered.Offset, p.Powered.Kind, powered ? 1 : 0);
    }

    public void Put(uint at, GlassFieldKind kind, double v)
    {
        switch (kind)
        {
            case GlassFieldKind.Double: D(at, v); break;
            case GlassFieldKind.Int: I(at, (int)v); break;
            default: Bytes[at] = (byte)v; break;
        }
    }

    public void I(uint at, int v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    public void U(uint at, uint v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    public void D(uint at, double v) => BitConverter.TryWriteBytes(Bytes.AsSpan((int)at), v);

    /// <summary>A NUL-terminated ASCII text.</summary>
    public void Text(uint at, string s)
    {
        Encoding.ASCII.GetBytes(s).CopyTo(Bytes, (int)at);
        Bytes[at + s.Length] = 0;
    }

    /// <summary>A field's element as these bytes hold it (a byte unsigned, an int signed).</summary>
    public double FieldValue(PfdField f, long index) => Get(At(f, index), f.Field.Kind);

    private double Get(uint at, GlassFieldKind kind) => kind switch
    {
        GlassFieldKind.Double => BitConverter.ToDouble(Bytes, (int)at),
        GlassFieldKind.Int => BitConverter.ToInt32(Bytes, (int)at),
        _ => Bytes[at],
    };

    /// <summary>An expression worked out from these bytes, without the sampler.</summary>
    public double Value(PfdExpr e, int panel = 0) =>
        PfdEval.Value(e, FieldValue, panel, (p, address) => Get((uint)address + (uint)p.Field.Offset, p.Field.Kind));

    /// <summary>A memory source holding these bytes as one committed allocation at <see cref="Base"/>.</summary>
    public FakeProcessMemory Source() =>
        new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)Size, true, true, true), Bytes);

    public ModulePfdSampler Sampler(IProcessMemorySource? source = null, Pmdg737PfdLayout? layout = null, bool full = false)
    {
        var src = source ?? Source();
        return new ModulePfdSampler(new ModuleMemory(src, Base), src, layout ?? Layout) { Full = full };
    }

    /// <summary>One reading through the sampler; throws when it is not a good one.</summary>
    public PfdSample Sample(bool full = false, Pmdg737PfdLayout? layout = null)
    {
        using var sampler = Sampler(layout: layout, full: full);
        return sampler.TryRead(out var sample, out var corrupt) ? sample : throw new InvalidOperationException(corrupt ?? "the memory was not readable");
    }
}
