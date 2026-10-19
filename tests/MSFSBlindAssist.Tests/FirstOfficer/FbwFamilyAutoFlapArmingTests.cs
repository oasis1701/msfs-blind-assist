// The FlyByWire-family auto-flap schedules (A32NX, A380, Headwind A330) arm the approach context
// only on a real descent and retract only after a takeoff or go-around they watched. Until
// 2026-10-18 one descending sample below 5000 ft AGL armed extension, so an ALT* overshoot or a gust
// at a departure level-off extended the flaps ("Speed checked. Flaps 2.") with the takeoff flaps
// still out; and a manager first built in flight, or Auto Flaps switched on there, retracted an
// arrival's flaps at once. The second review (2026-10-18) measured the descent in MSL, not AGL
// (terrain), and ended the retraction once the flaps read up in the air.
//
// The two gates are pinned on the source text of all three, as FbwA320VfeNextTests pins the VFE-next
// source; FbwFamilyAutoManagerSecondReviewTests flies the managers themselves (a recording
// announcer, a seeded cache and an executor whose writes land on a recording definition).

using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwFamilyAutoFlapArmingTests
{
    [Theory]
    [InlineData("FBWA320", "FbwA320FOAutoManager.cs")]
    [InlineData("FBWA380", "FbwA380FOAutoManager.cs")]
    [InlineData("HWA330", "HwA330FOAutoManager.cs")]
    public void Extension_arms_only_on_a_descent_below_the_legs_peak(string folder, string fileName)
    {
        string source = Source(folder, fileName);
        Assert.Contains("private const double ApproachArmDescentFt = 300;", source);
        // MSL, never AGL: AGL follows the terrain, so level over rising ground once "descended".
        Assert.Contains("msl <= _peakMsl - ApproachArmDescentFt", source);
        Assert.DoesNotContain("_peakAgl", source);
    }

    [Theory]
    [InlineData("FBWA320", "FbwA320FOAutoManager.cs")]
    [InlineData("FBWA380", "FbwA380FOAutoManager.cs")]
    [InlineData("HWA330", "HwA330FOAutoManager.cs")]
    public void Retraction_runs_only_after_a_takeoff_or_go_around_it_watched(string folder, string fileName)
    {
        string source = Source(folder, fileName);
        Assert.Contains("if (_retractionArmed && !_extensionArmed && !descending)", source);
        // Armed by a ground sample and by both branches of the PRIMARY go-around signature (never by
        // the high fallback, which only clears the approach latch); disarmed when Auto Flaps is
        // switched on while not known to be on the ground, once the flaps read up in the air, and by
        // Reset.
        Assert.Contains("_retractionArmed = true;   // a takeoff from here is watched", source);
        Assert.Contains("if (value && !_autoFlapsEnabled && _lastOnGround != true) _retractionArmed = false;", source);
        Assert.Contains("if (flaps == 0) _retractionArmed = false;", source);
        Assert.Equal(3, Count(source, "_retractionArmed = true"));
    }

    private static int Count(string text, string what)
    {
        int n = 0;
        for (int i = text.IndexOf(what, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(what, i + what.Length, System.StringComparison.Ordinal))
            n++;
        return n;
    }

    private static string Source(string folder, string fileName, [CallerFilePath] string thisTestFilePath = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisTestFilePath)!,
            "..", "..", "..", "MSFSBlindAssist", "FirstOfficer", folder, fileName)));
}
