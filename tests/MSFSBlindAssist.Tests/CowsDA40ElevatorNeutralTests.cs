using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ RETRACTION. THIS FILE USED TO PIN A "MODEL NEUTRAL" THAT WAS NEVER MEASURED AGAINST THE
/// SIMVAR THE READOUT ACTUALLY READS, AND THE PINNED BEHAVIOUR COULD ANNOUNCE "CENTRED" OVER A
/// DEFLECTED SURFACE.
///
/// The old claim: the model's chain is ELEVATOR_ANG_TOT = stick + ELEVATOR_MATH_NEUTRAL and
/// ELEVATOR POSITION = angle / 30 * 100, with the neutral live at 4 degrees, so a centred stick
/// reads 13.3 percent. Those model values are real. What was never checked is whether the STOCK
/// `ELEVATOR POSITION` SimVar this readout reads follows that chain at all.
///
/// Measured afterwards on an A380: `ELEVATOR POSITION` and `YOKE Y POSITION` came back IDENTICAL
/// to every digit - 0.09099859930574894 both, twice, seconds apart - while `AILERON POSITION`
/// read -1.3e-7 and the rudder 6.3e-6. The stock SimVar is a PASS-THROUGH OF THE AXIS, and that
/// 9 percent was the pilot's own elevator axis sitting off centre on an airframe carrying no
/// such model constant. The 13 percent was most likely the same fault, and subtracting a
/// constant from it hid exactly what the surface reading exists to catch.
///
/// So nothing is subtracted any more. The surface reads as it reads, and the STICK is named
/// beside it when the two disagree - which distinguishes the two causes instead of guessing
/// between them: a jam moves the stick and not the surface, an off-centre axis moves both.
/// </summary>
public class CowsDA40ElevatorNeutralTests
{
    private static string Say(DA40Variant v, string key, double value)
    {
        var def = new CowsDA40Definition(v);
        Assert.True(def.TryGetDisplayOverride(key, value, out string text), $"{key} has no override");
        return text;
    }

    [Fact]
    public void TheSurfaceIsReportedAsItReadsWithNothingSubtracted()
    {
        // ⚠️ THE HEART OF THE RETRACTION. This value used to announce "centred". If a pilot's
        // axis really is sitting here, calling it centred is the app telling them their
        // controls are fine while the surface is deflected.
        string text = Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 4.0 / 30.0);
        Assert.NotEqual("centred", text);
        Assert.Contains("13 percent nose up", text);
    }

    [Fact]
    public void TheA380ReadingWouldBeReportedToo()
    {
        // The measured live value, on the aeroplane that exposed the mistake.
        Assert.Contains("9 percent nose up",
            Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 0.09099859930574894));
    }

    [Fact]
    public void AGenuinelyCentredSurfaceStillReadsCentred()
    {
        Assert.Equal("centred", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 0.0));
    }

    [Fact]
    public void FullNoseUpIsStillFullNoseUp()
    {
        Assert.Contains("nose up", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 1.0));
        Assert.Contains("at the stop", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 1.0));
    }

    [Fact]
    public void FullNoseDownIsStillFullNoseDown()
    {
        Assert.Contains("nose down", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", -1.0));
        Assert.Contains("at the stop", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", -1.0));
    }

    [Fact]
    public void ASmallNoseDownIsNoseDownRatherThanAScaledNoseUp()
    {
        // Under the old bias anything below 13.3 percent was renamed "nose down", so a real
        // nose-up deflection of 5 percent was announced as 9 percent NOSE DOWN.
        Assert.Contains("nose up", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 0.05));
        Assert.Contains("nose down", Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", -0.05));
    }

    [Fact]
    public void BothVariantsReportTheSameWayBecauseNeitherIsBiased()
    {
        Assert.Equal(Say(DA40Variant.NG, "DA40_CTL_ELEVATOR", 0.20),
                     Say(DA40Variant.XLS, "DA40_CTL_ELEVATOR", 0.20));
    }

    [Fact]
    public void AileronAndRudderAreUnchangedAndAlwaysWere()
    {
        Assert.Equal("centred", Say(DA40Variant.NG, "DA40_CTL_AILERON", 0.0));
        Assert.Equal("centred", Say(DA40Variant.NG, "DA40_CTL_RUDDER", 0.0));
        Assert.Contains("right wing down", Say(DA40Variant.NG, "DA40_CTL_AILERON", 0.5));
        Assert.Contains("left", Say(DA40Variant.NG, "DA40_CTL_RUDDER", -0.5));
    }
}
