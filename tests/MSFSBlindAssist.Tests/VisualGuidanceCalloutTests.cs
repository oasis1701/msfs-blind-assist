using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What visual guidance speaks, through the real manager on the shared rig (runway 09, true 090):
/// the direction of the bank callouts.
/// </summary>
public class VisualGuidanceCalloutTests
{
    private static readonly Regex BankErrorCallout = new(@"^\d+ (left|right)$", RegexOptions.CultureInvariant);

    // Right of final and flying parallel to it, the aircraft has to turn LEFT toward the line, so
    // the first bank callout asks for a left bank (the left/right trap, VG-18).
    [Fact]
    public void Right_of_final_the_first_bank_callout_says_left()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(8, 1.0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(3, 30);
        string first = rig.Speech.All.First(s => BankErrorCallout.IsMatch(s));
        Assert.EndsWith(" left", first);
    }

    // The centreline callouts were removed in stage 3 of the redesign: F speaks the sideways
    // position in every phase instead (VG-19, VG-22). Crossing the line from 0.3 nm right to 0.3 nm
    // left says none of them.
    [Fact]
    public void No_centreline_callout_is_ever_spoken()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6, 0.3, 0);
        rig.UpdateAt(0);
        rig.FlyFor(20, 30, t => rig.Place(6, 0.3 - 0.03 * t, 0));
        Assert.DoesNotContain(rig.Speech.All, s => s.EndsWith("of center", StringComparison.Ordinal) || s == "on runway center");
    }
}
