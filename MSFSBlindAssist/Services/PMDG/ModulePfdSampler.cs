using System.Buffers.Binary;
using MSFSBlindAssist.Services.WasmMemory;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Reads one PFD sample of the Captain's side from the 737 module's memory through a derived layout. Each object is
/// taken from its global afresh on every reading, the first time the reading needs it, and range-checked
/// against the module's committed memory (WASM-4); objects are read in blocks, never field by field, and one reading
/// asks Windows about each region once (WASM-15). While <see cref="Full"/> is off it reads only what the messages and
/// the checks need (the display units, the panel, IAS, altitude, the message conditions); with it on, every expression
/// of the layout and the approach ident. Both branches of every choice are read, but a field outside its object, or an
/// object a pointer leads out of the module, makes the reading corrupt only where PMDG's choices reach it; IAS and
/// altitude are checked only while PMDG draws them, and nothing else is (WASM-14). Unreadable memory is lost. Owns its
/// memory source (a lease on the shared simulator handle).
/// </summary>
public sealed class ModulePfdSampler : IPfdSampler
{
    /// <summary>The bytes of an object read at once, from the object's start.</summary>
    internal const int BlockSize = 4096;

    /// <summary>The Captain's side.</summary>
    private const int Side = 0;

    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;
    private readonly IReadOnlyList<PfdExpr> _full;
    private readonly IReadOnlyList<PfdExpr> _light;
    private readonly IReadOnlyList<PfdText> _texts;
    private volatile bool _fullOn;

    public Pmdg737PfdLayout Layout { get; }

    public bool Full
    {
        get => _fullOn;
        set => _fullOn = value;
    }

    public ModulePfdSampler(ModuleMemory memory, IDisposable ownedSource, Pmdg737PfdLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
        var flaps = layout.Speed.FlapBugs;
        _full = PfdLayoutWalk.Expressions(layout).Select(x => x.Expression)
            .Concat(Enumerable.Range(0, flaps.Labels.Count).Select(k => (PfdExpr)(flaps.Speeds with { Index = new PfdConst(k) })))
            .ToList();
        _light = new[] { layout.Speed.Ias, layout.Speed.TapeShown, layout.Altitude.Altitude, layout.Altitude.TapeShown }
            .Concat(layout.AllMessages().Select(m => m.Shown)).ToList();
        _texts = PfdLayoutWalk.Texts(layout).Select(x => x.Text).ToList();
    }

    public bool TryRead(out PfdSample sample, out string? corrupt)
    {
        sample = null!;
        corrupt = null;
        bool full = Full;
        // One reading asks Windows about each region once (a VirtualQueryEx costs ~200 µs on the sim).
        using var regions = _memory.RememberRegions();
        try
        {
            sample = new Reading(this).Read(full);
            return true;
        }
        catch (ReadFailed)
        {
            return false;
        }
        catch (Corrupt ex)
        {
            corrupt = ex.Message;
            return false;
        }
    }

    public void Dispose() => _ownedSource.Dispose();

    /// <summary>Thrown when memory cannot be read: the memory went away (lost).</summary>
    private sealed class ReadFailed : Exception;

    /// <summary>Thrown when a check fails: the reading is discarded as corrupt.</summary>
    private sealed class Corrupt(string reason) : Exception(reason);

    /// <summary>Thrown when a value is worked out from one the reading could not read (outside its object).</summary>
    private sealed class Unreadable(string reason) : Exception(reason);

    private static int Width(GlassFieldKind kind) => kind switch { GlassFieldKind.Double => 8, GlassFieldKind.Int => 4, _ => 1 };

    /// <summary>A value as PMDG stores it: a double, a signed int, an unsigned byte.</summary>
    private static double Decode(ReadOnlySpan<byte> bytes, GlassFieldKind kind) => kind switch
    {
        GlassFieldKind.Double => BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        GlassFieldKind.Int => BinaryPrimitives.ReadInt32LittleEndian(bytes),
        _ => bytes[0],
    };

    /// <summary>One reading's state: the objects found, the blocks read, every value read and every one that could
    /// not be, and the panel.</summary>
    private sealed class Reading(ModulePfdSampler owner)
    {
        private readonly ModuleMemory _memory = owner._memory;
        private readonly Dictionary<GlassObject, uint> _objects = new();
        private readonly Dictionary<(uint Object, long Block), byte[]> _blocks = new();
        private readonly Dictionary<PfdFieldKey, double> _fields = new();
        private readonly Dictionary<PfdFieldKey, string> _unreadableFields = new();
        private readonly Dictionary<(PfdPointed Leaf, long Address), double> _pointed = new();
        private readonly Dictionary<(PfdPointed Leaf, long Address), string> _unreadablePointed = new();
        private readonly HashSet<PfdExpr> _filled = new(ReferenceEqualityComparer.Instance);
        private int _panel;

        public PfdSample Read(bool full)
        {
            var l = owner.Layout;
            var u = l.Units;
            var efis = l.Objects.Efis;
            var pages = new int[u.UnitCount];
            for (int k = 0; k < u.UnitCount; k++)
                pages[k] = (int)Raw(efis, (long)k * u.UnitStride + u.PageOffset, GlassFieldKind.Int, "a display unit's page");
            int selector = (int)Raw(efis, u.LowerSelector.Offset + (long)u.LowerSelector.Stride * u.SelectorSide, u.LowerSelector.Kind, "the lower DU selector");

            // The panel the unit showing the Captain's PFD follows; with none showing it, the EFIS object's Captain entry.
            PfdCaptainUnit? shown = null;
            foreach (var c in u.CaptainUnits)
            {
                if (c.Unit < 0 || c.Unit >= pages.Length)
                    throw new Corrupt(Invariant($"the Captain's PFD units: unit {c.Unit} is not one of the {pages.Length} units"));
                if (c.Page != u.PfdPage || pages[c.Unit] != u.PfdPage || (c.WhenSelector is { } when && when != selector)) continue;
                shown = c;
                break;
            }
            var p = l.Panel;
            _panel = shown is { } unit
                ? (int)Raw(efis, (long)unit.Unit * u.UnitStride + u.UnitPanelOffset, GlassFieldKind.Int, "the PFD unit's panel")
                : (int)Raw(efis, p.PanelIndexOffset + 4L * Side, GlassFieldKind.Int, "the Captain's panel");
            if (_panel < 0 || p.PanelBase + (long)(_panel + 1) * p.PanelStride > efis.Size)
                throw new Corrupt(Invariant($"the panel: index {_panel} has no panel in CEFIS"));
            long at = p.PanelBase + (long)_panel * p.PanelStride;
            bool Switch(GlassField f, string what) => Raw(efis, at + f.Offset, f.Kind, what) != 0;
            var panel = new PfdPanelState(Switch(p.Fpv, "FPV"), Switch(p.Mtrs, "MTRS"), Switch(p.BaroStd, "STD"), Switch(p.Powered, "the panel's power"));

            var roots = full ? owner._full : owner._light;
            foreach (var e in roots) Fill(e);
            // Where PMDG's choices reach a field the reading could not read, the reading is corrupt.
            if (full)
                foreach (var e in roots) Checked(e);
            double ias = Checked(l.Speed.Ias), altitude = Checked(l.Altitude.Altitude);
            if (Checked(l.Speed.TapeShown) != 0 && !double.IsFinite(ias))
                throw new Corrupt("the speed tape: IAS is not a number while PMDG draws it");
            if (Checked(l.Altitude.TapeShown) != 0 && !double.IsFinite(altitude))
                throw new Corrupt("the altitude tape: the altitude is not a number while PMDG draws it");
            var messages = l.AllMessages().Select(m => Checked(m.Shown) != 0).ToArray();

            PfdValues? values = null;
            if (full)
            {
                var texts = new Dictionary<PfdText, string?>();
                foreach (var t in owner._texts) texts[t] = Checked(t.Shown) != 0 ? Text(t) : null;
                values = new PfdValues(_panel, _fields, _pointed, texts);
            }
            return new PfdSample(pages, selector, _panel, panel, ias, altitude, messages, full, values);
        }

        /// <summary>Reads every field the expression can read: an index's fields and value first, a pointer's object
        /// after the pointer, both branches of every choice.</summary>
        private void Fill(PfdExpr e)
        {
            if (!_filled.Add(e)) return;
            switch (e)
            {
                case PfdField f:
                    long index = 0;
                    if (f.Index is not null)
                    {
                        Fill(f.Index);
                        // An index that could not be read: working the field out fails at the index first.
                        if (!TryValue(f.Index, out double i)) return;
                        index = (long)Math.Truncate(i);
                    }
                    Field(f, index);
                    break;
                case PfdPointed pointed:
                    Fill(pointed.Pointer);
                    if (!TryValue(pointed.Pointer, out double address)) return;
                    Pointed(pointed, (long)address);
                    break;
                case PfdSelect s:
                    Fill(s.Condition);
                    Fill(s.IfTrue);
                    Fill(s.IfFalse);
                    break;
                case PfdUnary un:
                    Fill(un.Operand);
                    break;
                case PfdBinary b:
                    Fill(b.Left);
                    Fill(b.Right);
                    break;
            }
        }

        private void Field(PfdField f, long index)
        {
            var key = new PfdFieldKey(f.Owner, f.Field, index);
            if (_fields.ContainsKey(key) || _unreadableFields.ContainsKey(key)) return;
            // In doubles: an index read from memory can be anything.
            double at = f.Field.Offset + (double)f.Field.Stride * index;
            if (at < 0 || at + Width(f.Field.Kind) > f.Owner.Size)
                _unreadableFields[key] = Invariant($"{PfdLayoutWalk.Show(f)}: element {index} lies outside its object ({f.Owner.Size} bytes)");
            else
                _fields[key] = Raw(f.Owner, (long)at, f.Field.Kind, "a field");
        }

        private void Pointed(PfdPointed p, long address)
        {
            var key = (p, address);
            if (_pointed.ContainsKey(key) || _unreadablePointed.ContainsKey(key)) return;
            int width = Width(p.Field.Kind);
            if (address <= 0 || address > uint.MaxValue || !_memory.IsInsideModule((uint)address, p.Size))
            {
                _unreadablePointed[key] = Invariant($"{PfdLayoutWalk.Show(p.Pointer)}: its pointer 0x{address:x} lies outside the module");
                return;
            }
            if (p.Field.Offset < 0 || p.Field.Offset + width > p.Size)
            {
                _unreadablePointed[key] = Invariant($"{PfdLayoutWalk.Show(p)} lies outside the object it points at ({p.Size} bytes)");
                return;
            }
            var bytes = new byte[width];
            if (!_memory.TryRead((uint)address + (uint)p.Field.Offset, bytes)) throw new ReadFailed();
            _pointed[key] = Decode(bytes, p.Field.Kind);
        }

        /// <summary>A text PMDG draws: a NUL within its length and printable ASCII before it.</summary>
        private string Text(PfdText t)
        {
            long at = t.Offset + (long)t.Stride * Side;
            if (at < 0 || at + t.Length > t.Owner.Size)
                throw new Corrupt(Invariant($"the text at {t.Offset} lies outside its object ({t.Owner.Size} bytes)"));
            var bytes = new byte[t.Length];
            if (!_memory.TryRead(Object(t.Owner) + (uint)at, bytes)) throw new ReadFailed();
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) throw new Corrupt(Invariant($"the text at {t.Offset} has no end within {t.Length} bytes"));
            foreach (byte b in bytes.AsSpan(0, end))
                if (b is < 0x20 or > 0x7E) throw new Corrupt(Invariant($"the text at {t.Offset} holds a byte that is not printable"));
            return System.Text.Encoding.ASCII.GetString(bytes, 0, end);
        }

        private double Value(PfdExpr e) => PfdEval.Value(e, ReadField, _panel, ReadPointed);

        private bool TryValue(PfdExpr e, out double value)
        {
            try
            {
                value = Value(e);
                return true;
            }
            catch (Unreadable)
            {
                value = 0;
                return false;
            }
        }

        /// <summary>The value; corrupt when it reaches a field the reading could not read.</summary>
        private double Checked(PfdExpr e)
        {
            try
            {
                return Value(e);
            }
            catch (Unreadable ex)
            {
                throw new Corrupt(ex.Message);
            }
        }

        private double ReadField(PfdField f, long index)
        {
            var key = new PfdFieldKey(f.Owner, f.Field, index);
            if (_fields.TryGetValue(key, out double v)) return v;
            if (_unreadableFields.TryGetValue(key, out var why)) throw new Unreadable(why);
            throw new InvalidOperationException(Invariant($"{PfdLayoutWalk.Show(f)} element {index} was never filled"));
        }

        private double ReadPointed(PfdPointed p, long address)
        {
            if (_pointed.TryGetValue((p, address), out double v)) return v;
            if (_unreadablePointed.TryGetValue((p, address), out var why)) throw new Unreadable(why);
            throw new InvalidOperationException(Invariant($"{PfdLayoutWalk.Show(p)} at 0x{address:x} was never filled"));
        }

        /// <summary>A value at <paramref name="at"/> in an object, from its block (read once per reading), or read on
        /// its own when it lies across two blocks; corrupt when outside the object.</summary>
        private double Raw(GlassObject o, long at, GlassFieldKind kind, string what)
        {
            int width = Width(kind);
            if (at < 0 || at + width > o.Size) throw new Corrupt(Invariant($"{what} at {at} lies outside its object ({o.Size} bytes)"));
            uint obj = Object(o);
            long block = at / BlockSize;
            int within = (int)(at % BlockSize);
            if (within + width <= Math.Min(BlockSize, o.Size - block * BlockSize))
                return Decode(Block(obj, o.Size, block).AsSpan(within, width), kind);
            var bytes = new byte[width];
            return _memory.TryRead(obj + (uint)at, bytes) ? Decode(bytes, kind) : throw new ReadFailed();
        }

        private byte[] Block(uint obj, uint size, long block)
        {
            if (_blocks.TryGetValue((obj, block), out var bytes)) return bytes;
            bytes = new byte[Math.Min(BlockSize, size - block * BlockSize)];
            if (!_memory.TryRead(obj + (uint)(block * BlockSize), bytes)) throw new ReadFailed();
            _blocks[(obj, block)] = bytes;
            return bytes;
        }

        /// <summary>The object behind its global, inside the module (else the memory is lost).</summary>
        private uint Object(GlassObject o)
        {
            if (_objects.TryGetValue(o, out uint pointer)) return pointer;
            if (!_memory.TryReadInt32(o.Global, out int p) || p == 0 || !_memory.IsInsideModule((uint)p, o.Size)) throw new ReadFailed();
            _objects[o] = (uint)p;
            return (uint)p;
        }
    }
}
