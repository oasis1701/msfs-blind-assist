using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The unit the Ctrl+V box writes for a typed value. The FPA band (±0-9.9°) and the V/S band
/// (0, ±100-6000 fpm) share exactly one value, 0 — the level-off entry — and there the CURRENT
/// mode decides. Before this rule existed, 0 was classed as FPA by magnitude alone, so levelling
/// off in V/S mode switched the window to FPA and the wheel to tenths of a degree.
/// </summary>
public class Md11VerticalUnitTests
{
    /// <summary>The one ambiguous value keeps whatever mode the window is already in.</summary>
    [Theory]
    [InlineData(0, false, Md11VerticalUnit.VerticalSpeed)]   // the reported case: level off in V/S stays V/S
    [InlineData(0, true, Md11VerticalUnit.Fpa)]
    public void Zero_KeepsTheCurrentMode(double typed, bool currentIsFpa, Md11VerticalUnit expected)
        => Assert.Equal(expected, Md11Fcp.ResolveVerticalUnit(typed, currentIsFpa));

    /// <summary>A number that fits only one window goes to that window, whichever mode is showing.</summary>
    [Theory]
    [InlineData(-3, false, Md11VerticalUnit.Fpa)]       // the prompt's own example, from V/S mode
    [InlineData(-3, true, Md11VerticalUnit.Fpa)]
    [InlineData(2.5, false, Md11VerticalUnit.Fpa)]      // a fraction can only be an angle
    [InlineData(-9.9, false, Md11VerticalUnit.Fpa)]
    [InlineData(9.9, true, Md11VerticalUnit.Fpa)]
    [InlineData(-1500, true, Md11VerticalUnit.VerticalSpeed)]   // from FPA mode
    [InlineData(-1500, false, Md11VerticalUnit.VerticalSpeed)]
    [InlineData(100, true, Md11VerticalUnit.VerticalSpeed)]     // the smallest non-zero V/S
    [InlineData(-100, true, Md11VerticalUnit.VerticalSpeed)]
    [InlineData(6000, false, Md11VerticalUnit.VerticalSpeed)]
    [InlineData(-6000, true, Md11VerticalUnit.VerticalSpeed)]
    public void AValueValidInOnlyOneWindow_SwitchesToIt(double typed, bool currentIsFpa, Md11VerticalUnit expected)
        => Assert.Equal(expected, Md11Fcp.ResolveVerticalUnit(typed, currentIsFpa));

    /// <summary>10-99 fits neither window (too big for an angle, too small for a V/S); so does anything past 6000.</summary>
    [Theory]
    [InlineData(10, false)]
    [InlineData(10, true)]
    [InlineData(-15, true)]
    [InlineData(50, false)]
    [InlineData(99, false)]
    [InlineData(-99.9, true)]
    [InlineData(6001, false)]
    [InlineData(-7000, true)]
    public void AValueThatFitsNeitherWindow_IsRefused(double typed, bool currentIsFpa)
        => Assert.Null(Md11Fcp.ResolveVerticalUnit(typed, currentIsFpa));

    /// <summary>The enum's numbers are the MD11_EXTCTL_FCP_VR_U inbox's, so the dialog can cast it straight onto the wire.</summary>
    [Fact]
    public void Unit_IsNumberedAsTheInboxTakesIt()
    {
        Assert.Equal(0, (int)Md11VerticalUnit.VerticalSpeed);
        Assert.Equal(1, (int)Md11VerticalUnit.Fpa);
    }
}
