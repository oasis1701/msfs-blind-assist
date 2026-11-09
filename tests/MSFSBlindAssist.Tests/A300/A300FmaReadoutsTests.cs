using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// What Shift+A, Shift+S and Shift+H add to their FCU value: the FMA mode on that key's axis, and what is
/// armed on it. The suffixes carry the decoder's own words ([A300-9]), so the armed-axis theory reads its
/// readings through <see cref="A300Fma.Read"/> and a word the decoder gains has to be given an axis here.
/// </summary>
public class A300FmaReadoutsTests
{
    private static A300FmaReading Shown(string? thrust = null, string? pitch = null, string? roll = null,
        string? common = null, string[]? armed = null) =>
        new(true, thrust, pitch, roll, common, armed ?? Array.Empty<string>(), null);

    [Fact]
    public void NotShown_AddsNothing()
    {
        // Columns the decoder would have blanked, filled in: the guard is the flag, not the blanks.
        var fma = new A300FmaReading(false, "Speed", "Vertical speed", "Heading select", "Land",
            new[] { "Altitude", "NAV" }, "CMD 1");
        Assert.Equal("", A300FmaReadouts.AltitudeModes(fma));
        Assert.Equal("", A300FmaReadouts.SpeedModes(fma));
        Assert.Equal("", A300FmaReadouts.HeadingModes(fma));
    }

    [Fact]
    public void ShownButBlank_AddsNothing()
    {
        var fma = Shown();
        Assert.Equal("", A300FmaReadouts.AltitudeModes(fma));
        Assert.Equal("", A300FmaReadouts.SpeedModes(fma));
        Assert.Equal("", A300FmaReadouts.HeadingModes(fma));
    }

    [Fact]
    public void PitchAndRoll_WithTheirArmedModes()
    {
        var fma = Shown(pitch: "Vertical speed", roll: "Heading select", armed: new[] { "Altitude", "NAV" });
        Assert.Equal(", pitch mode Vertical speed, Altitude armed", A300FmaReadouts.AltitudeModes(fma));
        Assert.Equal(", roll mode Heading select, NAV armed", A300FmaReadouts.HeadingModes(fma));
    }

    [Fact]
    public void CombinedMode_StandsAloneOnBothAxes()
    {
        var fma = Shown(common: "Land");
        Assert.Equal(", Land", A300FmaReadouts.AltitudeModes(fma));
        Assert.Equal(", Land", A300FmaReadouts.HeadingModes(fma));
    }

    [Fact]
    public void GoAround_KeepsArmedAltitude()
    {
        var fma = Shown(common: "Go around", armed: new[] { "Altitude" });
        Assert.Equal(", Go around, Altitude armed", A300FmaReadouts.AltitudeModes(fma));
        Assert.Equal(", Go around", A300FmaReadouts.HeadingModes(fma));
    }

    [Fact]
    public void Speed_IsTheThrustColumn()
    {
        Assert.Equal(", thrust mode Profile speed", A300FmaReadouts.SpeedModes(Shown(thrust: "Profile speed")));
        Assert.Equal("", A300FmaReadouts.SpeedModes(Shown(thrust: null)));
    }

    [Fact]
    public void Speed_IgnoresTheOtherColumnsAndWhatIsArmed()
    {
        var fma = Shown(thrust: "Speed", pitch: "Vertical speed", roll: "Heading select", common: "Land",
            armed: new[] { "Altitude", "NAV" });
        Assert.Equal(", thrust mode Speed", A300FmaReadouts.SpeedModes(fma));
    }

    [Fact]
    public void BlankPitch_SaysArmedOnly()
    {
        Assert.Equal(", Glide slope armed",
            A300FmaReadouts.AltitudeModes(Shown(pitch: null, armed: new[] { "Glide slope" })));
    }

    [Fact]
    public void BlankRoll_SaysArmedOnly()
    {
        Assert.Equal(", Localizer armed",
            A300FmaReadouts.HeadingModes(Shown(roll: null, armed: new[] { "Localizer" })));
    }

    [Fact]
    public void Armed_KeepsTheDecodersOrderOnEachAxis()
    {
        var fma = Shown(pitch: "Speed", roll: "Heading hold",
            armed: new[] { "Glide slope", "Heading select", "Altitude", "NAV" });
        Assert.Equal(", pitch mode Speed, Glide slope armed, Altitude armed", A300FmaReadouts.AltitudeModes(fma));
        Assert.Equal(", roll mode Heading hold, Heading select armed, NAV armed", A300FmaReadouts.HeadingModes(fma));
    }

    // ---- Which axis an armed word is on, read through the decoder ----

    /// <summary>A shown FMA (an IRS aligned, pitch trim 2 on) in vertical speed, with one armed slot set.</summary>
    private static A300FmaInputs WithArmed(string slot, int number)
    {
        var inputs = new A300FmaInputs { PitchMode = 8, Irs1Aligned = true, PitchTrim2 = true };
        return slot switch
        {
            "RollArmed" => inputs with { RollArmed = number },
            "PitchArmed" => inputs with { PitchArmed = number },
            "PitchArmed2" => inputs with { PitchArmed2 = number },
            "CommonArmed" => inputs with { CommonArmed = number },
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
        };
    }

    /// <summary>The axis' own suffix first, then the other axis' suffix.</summary>
    private static (string Here, string There) OnAxis(A300FmaReading fma, bool lateral) =>
        lateral
            ? (A300FmaReadouts.HeadingModes(fma), A300FmaReadouts.AltitudeModes(fma))
            : (A300FmaReadouts.AltitudeModes(fma), A300FmaReadouts.HeadingModes(fma));

    [Theory]
    [InlineData("RollArmed", 1, "NAV", true)]
    [InlineData("RollArmed", 2, "Heading select", true)]
    [InlineData("RollArmed", 5, "Localizer", true)]
    [InlineData("RollArmed", 7, "VOR", true)]
    [InlineData("PitchArmed", 2, "Speed", false)]
    [InlineData("PitchArmed", 3, "Profile climb", false)]
    [InlineData("PitchArmed", 5, "Profile descent", false)]
    [InlineData("PitchArmed", 6, "Altitude", false)]
    [InlineData("PitchArmed", 7, "Glide slope", false)]
    [InlineData("PitchArmed", 22, "Profile altitude", false)]
    [InlineData("PitchArmed2", 7, "Glide slope", false)]
    [InlineData("PitchArmed2", 13, "Profile descent", false)]
    [InlineData("CommonArmed", 6, "Altitude", false)]
    public void EveryDecodedArmedWord_LandsOnItsAxis(string slot, int number, string word, bool lateral)
    {
        var fma = A300Fma.Read(WithArmed(slot, number));
        Assert.True(fma.IsShown);
        Assert.Equal(new[] { word }, fma.Armed);   // the decoder still says what this row thinks it does

        var (here, there) = OnAxis(fma, lateral);
        Assert.Contains($", {word} armed", here);
        Assert.DoesNotContain("armed", there);
    }

    [Theory]
    [InlineData("RollArmed", true)]
    [InlineData("PitchArmed", false)]
    [InlineData("PitchArmed2", false)]
    [InlineData("CommonArmed", false)]
    public void EveryValueOfAnArmedSlot_LandsOnItsAxis(string slot, bool lateral)
    {
        // Every number a slot can hold, so a word the decoder is given later cannot sit on the wrong axis
        // unnoticed: the lateral list in A300FmaReadouts has to say so.
        int decoded = 0;
        for (int number = 0; number < 64; number++)
        {
            var fma = A300Fma.Read(WithArmed(slot, number));
            foreach (var word in fma.Armed)
            {
                decoded++;
                var (here, there) = OnAxis(fma, lateral);
                Assert.Contains($", {word} armed", here);
                Assert.DoesNotContain("armed", there);
            }
        }
        Assert.True(decoded > 0, $"{slot} decoded no armed word at all");
    }
}
