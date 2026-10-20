using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// DA40 V-speeds from the POH. These back the characteristic-speed hotkeys, which on an
/// airliner carry green dot / S / F / VLS - none of which exist on this aeroplane.
/// </summary>
public class DA40SpeedsTests
{
    [Fact]
    public void NgCarriesTheNgFigures()
    {
        var s = DA40Speeds.For(DA40Variant.NG);

        Assert.Equal(67, s.Vr);
        Assert.Equal(88, s.VbestGlide);
        Assert.Equal(172, s.Vne);
        Assert.Equal(98, s.VfeLanding);
        Assert.Equal(110, s.VfeTakeoff);
    }

    [Fact]
    public void XlsCarriesTheXlsFigures()
    {
        var s = DA40Speeds.For(DA40Variant.XLS);

        Assert.Equal(59, s.Vr);
        Assert.Equal(178, s.Vne);
        Assert.Equal(91, s.VfeLanding);
    }

    [Fact]
    public void TheTwoVariantsGenuinelyDiffer()
    {
        // Vne is HIGHER on the lighter XLS - an easy thing to get backwards.
        Assert.True(DA40Speeds.For(DA40Variant.XLS).Vne > DA40Speeds.For(DA40Variant.NG).Vne);
        Assert.True(DA40Speeds.For(DA40Variant.NG).Vr > DA40Speeds.For(DA40Variant.XLS).Vr);
    }

    [Theory]
    [InlineData(DA40Variant.NG, 173, true)]
    [InlineData(DA40Variant.NG, 170, false)]
    [InlineData(DA40Variant.XLS, 173, false)]
    public void ExceedsVneUsesTheVariantLimit(DA40Variant v, double kias, bool expected)
        => Assert.Equal(expected, DA40Speeds.For(v).ExceedsVne(kias));

    [Fact]
    public void ExceedsVfeUsesTheSelectedFlapLimit()
    {
        var s = DA40Speeds.For(DA40Variant.NG);

        Assert.False(s.ExceedsVfe(105, flapIndex: 1));   // T/O limit 110
        Assert.True(s.ExceedsVfe(105, flapIndex: 2));    // LDG limit 98
        Assert.False(s.ExceedsVfe(160, flapIndex: 0));   // clean, no flap limit
    }
}
