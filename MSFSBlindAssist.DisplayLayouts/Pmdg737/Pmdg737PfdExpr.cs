using System.Text.Json.Serialization;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// A value or condition PMDG's PFD works out from its data objects, as the layout file gives it, and worked out on
/// each reading by <see cref="PfdEval"/> (a condition is 1 or 0).
/// Binary nodes only, so record equality is content equality. The layout file names each node's type by its
/// <c>$kind</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(PfdConst), "const")]
[JsonDerivedType(typeof(PfdField), "field")]
[JsonDerivedType(typeof(PfdUnitPanel), "unitPanel")]
[JsonDerivedType(typeof(PfdPointed), "pointed")]
[JsonDerivedType(typeof(PfdUnary), "unary")]
[JsonDerivedType(typeof(PfdBinary), "binary")]
[JsonDerivedType(typeof(PfdSelect), "select")]
public abstract record PfdExpr;

public sealed record PfdConst(double Value) : PfdExpr;

/// <summary>A field of one of PMDG's objects (behind its global): at the object + the field's offset + the
/// index × its stride. With no index the field is the Captain's (side 0).</summary>
public sealed record PfdField(GlassObject Owner, GlassField Field, PfdExpr? Index = null) : PfdExpr;

/// <summary>The EFIS panel the display unit showing the Captain's PFD follows (the panel the unit passes to the PFD page).</summary>
public sealed record PfdUnitPanel : PfdExpr;

/// <summary>A field of an object one of PMDG's objects points at: <see cref="Pointer"/> holds its address, the object is
/// <see cref="Size"/> bytes (a reader reads it only inside PMDG's module), <see cref="Field"/> lies in it (the IRS
/// object's IRU pointer, then the IRU's condition).</summary>
public sealed record PfdPointed(PfdField Pointer, GlassField Field, uint Size) : PfdExpr;

public enum PfdOp
{
    Add, Sub, Mul, Div, DivInt, Rem, Shl, BitAnd, BitOr, BitXor, Atan2,
    Eq, Ne, Lt, Le, Gt, Ge,
    Neg, Abs, Eqz, Trunc, Floor, Ceil, Nearest,
}

public sealed record PfdUnary(PfdOp Op, PfdExpr Operand) : PfdExpr;

public sealed record PfdBinary(PfdOp Op, PfdExpr Left, PfdExpr Right) : PfdExpr;

public sealed record PfdSelect(PfdExpr Condition, PfdExpr IfTrue, PfdExpr IfFalse) : PfdExpr;

/// <summary>Works out a <see cref="PfdExpr"/> as PMDG's code does: doubles throughout, i32 operations on the
/// truncated values, comparisons 1 or 0 (false for NaN). Pure.</summary>
public static class PfdEval
{
    /// <summary>The value, reading each field through <paramref name="read"/> (the field and its index's value),
    /// taking <paramref name="unitPanel"/> for <see cref="PfdUnitPanel"/>, and each <see cref="PfdPointed"/> through
    /// <paramref name="pointed"/> (the leaf and the address its pointer holds).</summary>
    /// <exception cref="InvalidOperationException">A <see cref="PfdPointed"/> with no <paramref name="pointed"/>.</exception>
    public static double Value(PfdExpr e, Func<PfdField, long, double> read, int unitPanel, Func<PfdPointed, long, double>? pointed = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        double V(PfdExpr x) => Value(x, read, unitPanel, pointed);
        return e switch
        {
            PfdConst c => c.Value,
            PfdUnitPanel => unitPanel,
            PfdField f => read(f, f.Index is null ? 0 : (long)Math.Truncate(V(f.Index))),
            PfdPointed p => pointed is null
                ? throw new InvalidOperationException("a pointed field with no reader for the object it points at")
                : pointed(p, (long)V(p.Pointer)),
            PfdSelect s => V(s.Condition) != 0 ? V(s.IfTrue) : V(s.IfFalse),
            PfdUnary u => Unary(u.Op, V(u.Operand)),
            PfdBinary b => Binary(b.Op, V(b.Left), V(b.Right)),
            _ => throw new ArgumentException($"unknown expression {e.GetType().Name}", nameof(e)),
        };
    }

    public static bool Holds(PfdExpr e, Func<PfdField, long, double> read, int unitPanel, Func<PfdPointed, long, double>? pointed = null) =>
        Value(e, read, unitPanel, pointed) != 0;

    /// <summary>The expression as short infix text in the invariant culture, for logs: a field as its object's name, a
    /// dot, its offset (with its kind: d double, i int, b byte) and any index in brackets.</summary>
    public static string Show(PfdExpr e, Func<GlassObject, string> name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string S(PfdExpr x) => Show(x, name);
        return e switch
        {
            PfdConst c => c.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            PfdUnitPanel => "panel",
            PfdField f => name(f.Owner) + "." + (f.Field.Kind switch { GlassFieldKind.Double => "d", GlassFieldKind.Int => "i", _ => "b" })
                + f.Field.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (f.Index is null ? "" : "[" + S(f.Index) + "×" + f.Field.Stride.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]"),
            PfdPointed p => S(p.Pointer) + "→" + (p.Field.Kind switch { GlassFieldKind.Double => "d", GlassFieldKind.Int => "i", _ => "b" })
                + p.Field.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PfdSelect s => "(" + S(s.Condition) + " ? " + S(s.IfTrue) + " : " + S(s.IfFalse) + ")",
            PfdUnary u => u.Op switch { PfdOp.Neg => "-", PfdOp.Eqz => "!", _ => u.Op.ToString().ToLowerInvariant() } + "(" + S(u.Operand) + ")",
            PfdBinary { Op: PfdOp.Atan2 } b => "atan2(" + S(b.Left) + ", " + S(b.Right) + ")",
            PfdBinary b => "(" + S(b.Left) + " " + b.Op switch
            {
                PfdOp.Add => "+", PfdOp.Sub => "-", PfdOp.Mul => "*", PfdOp.Div => "/", PfdOp.DivInt => "div", PfdOp.Rem => "%", PfdOp.Shl => "<<",
                PfdOp.BitAnd => "&", PfdOp.BitOr => "|", PfdOp.BitXor => "^", PfdOp.Eq => "==", PfdOp.Ne => "!=", PfdOp.Lt => "<", PfdOp.Le => "<=",
                PfdOp.Gt => ">", _ => ">=",
            } + " " + S(b.Right) + ")",
            _ => e.GetType().Name,
        };
    }

    /// <summary>Every field the expression reads (an index's fields before its own, a pointed field's pointer), each
    /// once, in order.</summary>
    public static IReadOnlyList<PfdField> Fields(PfdExpr e)
    {
        var found = new List<PfdField>();
        void Walk(PfdExpr x)
        {
            switch (x)
            {
                case PfdField f:
                    if (f.Index is not null) Walk(f.Index);
                    if (!found.Contains(f)) found.Add(f);
                    break;
                case PfdPointed p: Walk(p.Pointer); break;
                case PfdSelect s: Walk(s.Condition); Walk(s.IfTrue); Walk(s.IfFalse); break;
                case PfdUnary u: Walk(u.Operand); break;
                case PfdBinary b: Walk(b.Left); Walk(b.Right); break;
            }
        }
        Walk(e);
        return found;
    }

    private static double Unary(PfdOp op, double v) => op switch
    {
        PfdOp.Neg => -v,
        PfdOp.Abs => Math.Abs(v),
        PfdOp.Eqz => v == 0 ? 1 : 0,
        PfdOp.Trunc => double.IsFinite(v) ? Math.Truncate(v) : 0,
        PfdOp.Floor => Math.Floor(v),
        PfdOp.Ceil => Math.Ceiling(v),
        PfdOp.Nearest => Math.Round(v, MidpointRounding.ToEven),
        _ => throw new ArgumentException($"{op} is not a unary operation", nameof(op)),
    };

    private static double Binary(PfdOp op, double a, double b)
    {
        static long I(double x) => double.IsFinite(x) ? (long)Math.Truncate(x) : 0;
        return op switch
        {
            PfdOp.Add => a + b,
            PfdOp.Sub => a - b,
            PfdOp.Mul => a * b,
            PfdOp.Div => a / b,
            PfdOp.DivInt => I(b) == 0 ? 0 : I(a) / I(b),
            PfdOp.Rem => I(b) == 0 ? 0 : I(a) % I(b),
            PfdOp.Shl => I(a) << (int)(I(b) & 31),
            PfdOp.BitAnd => I(a) & I(b),
            PfdOp.BitOr => I(a) | I(b),
            PfdOp.BitXor => I(a) ^ I(b),
            PfdOp.Atan2 => Math.Atan2(a, b),
            PfdOp.Eq => a == b ? 1 : 0,
            PfdOp.Ne => a != b ? 1 : 0,
            PfdOp.Lt => a < b ? 1 : 0,
            PfdOp.Le => a <= b ? 1 : 0,
            PfdOp.Gt => a > b ? 1 : 0,
            PfdOp.Ge => a >= b ? 1 : 0,
            _ => throw new ArgumentException($"{op} is not a binary operation", nameof(op)),
        };
    }
}
