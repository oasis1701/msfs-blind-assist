using System.Reflection;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The Captain's EFIS panel the PFD follows: FPV, MTRS and STD pressed, and its power.</summary>
public sealed record PfdPanelState(bool Fpv, bool Mtrs, bool BaroStd, bool Powered);

/// <summary>
/// One reading of the Captain's PFD sources: each display unit's page, the lower DU selector, the EFIS panel the unit
/// showing the Captain's PFD follows (the EFIS object's Captain entry when none shows it) and that panel's switches, PMDG's IAS
/// and altitude, and whether each message is up (in <see cref="Pmdg737PfdLayout.AllMessages"/> order). Raw numbers
/// only. <see cref="Reading"/> holds every value the layout reads, and is null unless the reading was
/// <see cref="Full"/>.
/// </summary>
public sealed record PfdSample(IReadOnlyList<int> UnitPages, int LowerSelector, int UnitPanel, PfdPanelState Panel, double Ias, double Altitude,
    IReadOnlyList<bool> MessagesUp, bool Full, PfdValues? Reading);

/// <summary>One element of a field: the object, the field, and the element's index (0 for a field without one).</summary>
public readonly record struct PfdFieldKey(GlassObject Owner, GlassField Field, long Index);

/// <summary>
/// Every value one full reading read: each field element the layout's expressions read (by its evaluated index, so
/// <c>Speeds with { Index = … }</c> finds the same element), each field of an object a pointer leads to (by the address
/// the pointer held), and each text PMDG draws (null while it draws none). Works the layout's expressions out from
/// them as PMDG's code does (<see cref="PfdEval"/>); a field the reading did not read throws, never reads as a number.
/// </summary>
public sealed class PfdValues
{
    private readonly IReadOnlyDictionary<PfdFieldKey, double> _fields;
    private readonly IReadOnlyDictionary<(PfdPointed Leaf, long Address), double> _pointed;
    private readonly IReadOnlyDictionary<PfdText, string?> _texts;

    public PfdValues(int unitPanel, IReadOnlyDictionary<PfdFieldKey, double> fields,
        IReadOnlyDictionary<(PfdPointed Leaf, long Address), double> pointed, IReadOnlyDictionary<PfdText, string?> texts)
    {
        UnitPanel = unitPanel;
        _fields = fields;
        _pointed = pointed;
        _texts = texts;
    }

    /// <summary>The panel <see cref="PfdUnitPanel"/> stands for.</summary>
    public int UnitPanel { get; }

    /// <exception cref="InvalidOperationException">The expression reads a field the reading did not read.</exception>
    public double Value(PfdExpr e) => PfdEval.Value(e, Field, UnitPanel, Pointed);

    /// <exception cref="InvalidOperationException">The expression reads a field the reading did not read.</exception>
    public bool Holds(PfdExpr e) => Value(e) != 0;

    /// <summary>The text as PMDG draws it, null while it draws none.</summary>
    /// <exception cref="InvalidOperationException">A text the reading did not read.</exception>
    public string? Text(PfdText t) =>
        _texts.TryGetValue(t, out var s) ? s : throw new InvalidOperationException(Invariant($"the text at {t.Offset} was not read"));

    private double Field(PfdField f, long index) =>
        _fields.TryGetValue(new PfdFieldKey(f.Owner, f.Field, index), out double v)
            ? v
            : throw new InvalidOperationException(Invariant($"{PfdLayoutWalk.Show(f)} element {index} was not read"));

    private double Pointed(PfdPointed p, long address) =>
        _pointed.TryGetValue((p, address), out double v)
            ? v
            : throw new InvalidOperationException(Invariant($"{PfdLayoutWalk.Show(p)} at 0x{address:x} was not read"));
}

/// <summary>
/// Every expression and text of a layout with its path (the records' member names, a list's elements by index:
/// "Speed.Bugs[1].Value"), found by reflection over the layout's records and lists as Describe lists them, and every
/// object they read. Pure.
/// </summary>
public static class PfdLayoutWalk
{
    public static IReadOnlyList<(string Path, PfdExpr Expression)> Expressions(Pmdg737PfdLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var found = new List<(string, PfdExpr)>();
        Walk(layout, "", found, new List<(string, PfdText)>());
        return found;
    }

    public static IReadOnlyList<(string Path, PfdText Text)> Texts(Pmdg737PfdLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var found = new List<(string, PfdText)>();
        Walk(layout, "", new List<(string, PfdExpr)>(), found);
        return found;
    }

    /// <summary>Every object the layout's expressions and texts read (an index's and a pointer's included), the
    /// layout's own four first.</summary>
    public static IReadOnlyList<GlassObject> Owners(Pmdg737PfdLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var o = layout.Objects;
        var found = new List<GlassObject> { o.Glass, o.Efis, o.Sim, o.Options };
        void Add(GlassObject owner)
        {
            if (!found.Contains(owner)) found.Add(owner);
        }
        foreach (var (_, e) in Expressions(layout))
            foreach (var f in PfdEval.Fields(e)) Add(f.Owner);
        foreach (var (_, t) in Texts(layout)) Add(t.Owner);
        return found;
    }

    /// <summary>An expression for logs, each object as its global.</summary>
    public static string Show(PfdExpr e) => PfdEval.Show(e, o => Invariant($"0x{o.Global:x}"));

    private static void Walk(object value, string path, List<(string, PfdExpr)> expressions, List<(string, PfdText)> texts)
    {
        switch (value)
        {
            case PfdExpr e:
                expressions.Add((path, e));
                return;
            case string or byte[] or GlassObject or GlassField:
                return;
            case System.Collections.IEnumerable list:
                int i = 0;
                foreach (var x in list)
                {
                    if (x is not null) Walk(x, Invariant($"{path}[{i}]"), expressions, texts);
                    i++;
                }
                return;
            case PfdText t:
                texts.Add((path, t));
                break;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace != typeof(PfdExpr).Namespace) return;
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetValue(value) is not { } v) continue;
            Walk(v, path.Length == 0 ? p.Name : path + "." + p.Name, expressions, texts);
        }
    }
}

/// <summary>Reads one <see cref="PfdSample"/> through a derived layout: while <see cref="Full"/> is off, only what the
/// messages and the checks need. <c>false</c> with a null <c>corrupt</c>: the memory is not readable (lost); with
/// <c>corrupt</c> set: a check failed, named there, and nothing of the reading is used.</summary>
public interface IPfdSampler : IDisposable
{
    Pmdg737PfdLayout Layout { get; }

    /// <summary>Read every expression of the layout (the window is open), not only the light set.</summary>
    bool Full { get; set; }

    bool TryRead(out PfdSample sample, out string? corrupt);
}
