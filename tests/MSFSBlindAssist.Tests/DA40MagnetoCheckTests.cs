using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS magneto check spoken as numbers. The positions are pinned to the MEASURED
/// encoding (1 RIGHT, 2 LEFT — from the per-cylinder firing map, not from the order a key
/// is usually labelled), and the limits are the AFM's 175 / 50.
/// </summary>
public class DA40MagnetoCheckTests
{
    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "Right")]
    [InlineData(2, "Left")]
    [InlineData(3, "Both")]
    [InlineData(4, "Start")]
    public void PositionsAreTheMeasuredEncoding(int position, string expected)
        => Assert.Equal(expected, DA40MagnetoCheck.PositionName(position));

    [Fact]
    public void OnlyRightAndLeftAreSingleMagnetoPositions()
    {
        Assert.True(DA40MagnetoCheck.IsSingleMagneto(DA40MagnetoCheck.PositionRight));
        Assert.True(DA40MagnetoCheck.IsSingleMagneto(DA40MagnetoCheck.PositionLeft));
        Assert.False(DA40MagnetoCheck.IsSingleMagneto(DA40MagnetoCheck.PositionBoth));
        Assert.False(DA40MagnetoCheck.IsSingleMagneto(DA40MagnetoCheck.PositionOff));
        Assert.False(DA40MagnetoCheck.IsSingleMagneto(DA40MagnetoCheck.PositionStart));
    }

    [Theory]
    // The live measurement, verbatim.
    [InlineData(1, 2192.5, 2114.1, "Right magneto, drop 78")]
    [InlineData(2, 2192.5, 2076.4, "Left magneto, drop 116")]
    // Over the AFM limit is said, not merely implied by a big number.
    [InlineData(1, 2000, 1800, "Right magneto, drop 200, exceeds 175")]
    // A magneto that does nothing when switched off is a hot magneto, and the worst outcome.
    [InlineData(2, 2000, 2000, "Left magneto, drop 0, no drop")]
    [InlineData(2, 2000, 2004, "Left magneto, drop -4, no drop")]
    public void EachSideIsSpokenAsItsDrop(int position, double both, double now, string expected)
        => Assert.Equal(expected, DA40MagnetoCheck.DescribeSide(position, both, now));

    [Theory]
    [InlineData(116, 78, "Both. Differential 38")]
    [InlineData(78, 116, "Both. Differential 38")]
    [InlineData(200, 100, "Both. Differential 100, exceeds 50")]
    public void TheDifferentialIsSpokenOnReturnToBoth(int left, int right, string expected)
        => Assert.Equal(expected, DA40MagnetoCheck.DescribeDifferential(left, right));

    [Fact]
    public void ADropIsRoundedToWholeRpm()
        => Assert.Equal(78, DA40MagnetoCheck.Drop(2192.5, 2114.1));
}
