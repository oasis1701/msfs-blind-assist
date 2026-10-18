// The FlyByWire-family auto-flap schedules (A32NX, A380, Headwind A330) arm the approach context
// only on a real descent and retract only after a takeoff or go-around they watched. Until
// 2026-10-18 one descending sample below 5000 ft AGL armed extension, so an ALT* overshoot or a gust
// at a departure level-off extended the flaps ("Speed checked. Flaps 2.") with the takeoff flaps
// still out; and a manager first built in flight, or Auto Flaps switched on there, retracted an
// arrival's flaps at once.
//
// The managers cannot be constructed in a unit test (see FbwA320VfeNextTests' header: they take a
// ScreenReaderAnnouncer and an executor wired to a live definition), so the two gates are pinned on
// the source text of all three, as FbwA320VfeNextTests pins the VFE-next source.

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
        Assert.Contains("agl <= _peakAgl - ApproachArmDescentFt", source);
    }

    [Theory]
    [InlineData("FBWA320", "FbwA320FOAutoManager.cs")]
    [InlineData("FBWA380", "FbwA380FOAutoManager.cs")]
    [InlineData("HWA330", "HwA330FOAutoManager.cs")]
    public void Retraction_runs_only_after_a_takeoff_or_go_around_it_watched(string folder, string fileName)
    {
        string source = Source(folder, fileName);
        Assert.Contains("if (_retractionArmed && !_extensionArmed && !descending)", source);
        // Armed by a ground sample and by both go-around branches; disarmed when Auto Flaps is
        // switched on while not known to be on the ground, and by Reset.
        Assert.Contains("_retractionArmed = true;   // a takeoff from here is watched", source);
        Assert.Contains("if (value && !_autoFlapsEnabled && _lastOnGround != true) _retractionArmed = false;", source);
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
