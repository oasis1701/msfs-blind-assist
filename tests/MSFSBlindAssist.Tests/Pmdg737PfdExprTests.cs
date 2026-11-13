// The PFD layout keeps PMDG's own conditions and values as small expression trees, as the layout file gives
// them; the sampler works them out on each reading. These pin the arithmetic PMDG's code relies on: i32
// truncation, its rounding (floor(x + 0.5)), comparisons as 1 or 0, the side index folded away, the unit's
// panel as an index.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdExprTests
{
    private static readonly GlassObject Glass = new(0x27d2c8, 0x2de0);
    private static readonly GlassObject Efis = new(0x27be08, 0x1f360);
    private static readonly PfdField Ias = new(Glass, new GlassField(1560, 0, GlassFieldKind.Double));
    private static readonly PfdField V1 = new(Glass, new GlassField(1928, 0, GlassFieldKind.Int));
    private static readonly PfdField Fpv = new(Efis, new GlassField(3656 + 572, 0x9d30, GlassFieldKind.Bool), new PfdUnitPanel());

    private static PfdExpr C(double v) => new PfdConst(v);

    private static double Eval(PfdExpr e, double ias = 160, double v1 = 150, int panel = 0) =>
        PfdEval.Value(e, (f, index) => f == Ias ? ias : f == V1 ? v1 : f.Field.Offset == 3656 + 572 ? (index == 1 ? 1 : 0) : double.NaN, panel);

    [Fact]
    public void Compares_give_one_or_zero()
    {
        var inWindow = new PfdBinary(PfdOp.Lt, new PfdUnary(PfdOp.Abs, new PfdBinary(PfdOp.Sub, V1, Ias)), C(57));
        Assert.Equal(1, Eval(inWindow));
        Assert.Equal(0, Eval(inWindow, ias: 220));
        Assert.True(PfdEval.Holds(inWindow, (f, _) => f == Ias ? 160 : 150, 0));
    }

    [Fact]
    public void O0_rounding_is_floor_of_x_plus_a_half_truncated()
    {
        // PMDG prints opaa(floor(x + 0.5)): 0.7805 Mach → 781, 151.5 kt → 152.
        var mach = new PfdUnary(PfdOp.Trunc, new PfdUnary(PfdOp.Floor, new PfdBinary(PfdOp.Add, new PfdBinary(PfdOp.Mul, Ias, C(1000)), C(0.5))));
        Assert.Equal(781, Eval(mach, ias: 0.7805));
        Assert.Equal(-3, Eval(new PfdUnary(PfdOp.Trunc, C(-3.9))));
    }

    [Fact]
    public void Integer_operations_and_select()
    {
        Assert.Equal(6, Eval(new PfdBinary(PfdOp.Rem, C(16), C(10))));
        Assert.Equal(1, Eval(new PfdBinary(PfdOp.BitAnd, C(3), C(1))));
        Assert.Equal(0, Eval(new PfdUnary(PfdOp.Eqz, C(5))));
        Assert.Equal(100, Eval(new PfdSelect(C(1), C(100), C(80))));
        Assert.Equal(80, Eval(new PfdSelect(C(0), C(100), C(80))));
    }

    [Fact]
    public void A_nan_compares_false()
    {
        var missing = new PfdField(Glass, new GlassField(9999, 0, GlassFieldKind.Double));
        Assert.Equal(0, Eval(new PfdBinary(PfdOp.Gt, missing, C(0))));
        Assert.Equal(0, Eval(new PfdBinary(PfdOp.Le, missing, C(0))));
    }

    [Fact]
    public void The_unit_panel_indexes_a_field()
    {
        Assert.Equal(0, Eval(Fpv, panel: 0));
        Assert.Equal(1, Eval(Fpv, panel: 1));
    }

    [Fact]
    public void Fields_lists_every_field_read_once()
    {
        var e = new PfdSelect(new PfdBinary(PfdOp.Gt, V1, C(79)), new PfdBinary(PfdOp.Sub, V1, Ias), Fpv);
        Assert.Equal(new[] { V1, Ias, Fpv }, PfdEval.Fields(e));
    }

    [Fact]
    public void A_pointed_field_reads_the_object_its_pointer_names()
    {
        // The Captain's IRU condition: the IRS object's IRU pointer at the index the EFIS object keeps, then the IRU's condition.
        var isdu = new GlassObject(0x27bebc, 0x9d78);
        var index = new PfdField(Efis, new GlassField(0x14938 + 0x1fe0, 4, GlassFieldKind.Int));
        var pointer = new PfdField(isdu, new GlassField(4, 4, GlassFieldKind.Int), index);
        var condition = new PfdPointed(pointer, new GlassField(760, 0, GlassFieldKind.Int), 768);
        double Read(PfdField f, long i) => f == index ? 2 : f == pointer && i == 2 ? 0x500000 : double.NaN;
        double Pointed(PfdPointed p, long address) => p == condition && address == 0x500000 ? 6 : double.NaN;

        Assert.Equal(6, PfdEval.Value(condition, Read, 0, Pointed));
        Assert.Equal(1, PfdEval.Value(new PfdBinary(PfdOp.Eq, condition, C(6)), Read, 0, Pointed));
        Assert.Equal(new[] { index, pointer }, PfdEval.Fields(condition));
        Assert.Throws<InvalidOperationException>(() => PfdEval.Value(condition, Read, 0));
    }

    [Fact]
    public void Atan2_is_the_angle_in_radians()
    {
        // The TCAS zones' edge: atan2(vertical speed limit, ground speed in feet per minute).
        Assert.Equal(Math.PI / 4, Eval(new PfdBinary(PfdOp.Atan2, C(1000), C(1000))), 12);
        Assert.Equal(-Math.PI / 2, Eval(new PfdBinary(PfdOp.Atan2, C(-5), C(0))), 12);
        Assert.Equal("atan2(1000, 2)", PfdEval.Show(new PfdBinary(PfdOp.Atan2, C(1000), C(2)), _ => "G"));
    }

    [Fact]
    public void Equal_trees_are_equal()
    {
        Assert.Equal(new PfdBinary(PfdOp.Sub, V1, Ias), new PfdBinary(PfdOp.Sub, new PfdField(Glass, new GlassField(1928, 0, GlassFieldKind.Int)), Ias));
        Assert.NotEqual(new PfdBinary(PfdOp.Sub, V1, Ias), new PfdBinary(PfdOp.Sub, Ias, V1));
    }
}
