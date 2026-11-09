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
        rig.FlyFor(6, 30);   // past the 4.4 s the phase line holds the callouts (VG-21)
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

    // A bank callout interrupts, which drops whatever BA has queued; so BA's own lines hold the bank
    // callouts back for their measured speaking time (VG-21), each queued line's hold starting where
    // the line before it ends. Right of final and parallel the 25° error is due 1 s after activation;
    // the activation sentence (4.0 s) and then "Vectoring to final" (1.4 s) come first: 5.4 s.
    [Fact]
    public void A_bank_callout_waits_for_the_activation_and_phase_lines()
    {
        var lines = new List<string>();
        var rig = new VisualGuidanceTestRig(record: lines.Add);
        rig.Place(8, 1.0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(7, 30);
        var said = Said(lines);
        var vectoring = said.Single(s => !s.Immediate && s.Text == "Vectoring to final");
        var firstBank = said.First(s => BankErrorCallout.IsMatch(s.Text));
        Assert.True(vectoring.T < firstBank.T);
        Assert.InRange(firstBank.T, 5.4 - 0.05, 5.4 + 0.05);
    }

    // "Flare" (0.49 s spoken) holds a due bank callout for 0.6 s, no longer.
    [Fact]
    public void A_bank_callout_waits_for_flare_to_be_spoken()
    {
        var lines = new List<string>();
        var rig = new VisualGuidanceTestRig(record: lines.Add);
        rig.Place(0.3, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(6, 30);
        // 0.1 nm out, the main gear about 25 ft up: Flare; rolled 3° right with the command level.
        rig.FlyFor(2, 30, _ => rig.Place(0.1, 0, 0, altOffsetFt: -55, bankDeg: 3));
        var said = Said(lines);
        var flare = said.Single(s => s.Text == "Flare");
        var next = said.First(s => s.T > flare.T && s.Immediate);
        Assert.Equal("3 left", next.Text);
        Assert.InRange(next.T - flare.T, 0.6 - 1e-6, 0.65);
    }

    private static List<(double T, bool Immediate, string Text)> Said(List<string> recording)
    {
        var say = new Regex(@"^VGREC say t=(\S+) imm=([01]) text=(.*)$", RegexOptions.CultureInvariant);
        return recording.Select(l => say.Match(l)).Where(m => m.Success)
            .Select(m => (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Value == "1", m.Groups[3].Value))
            .ToList();
    }

    // F pressed in the first moment after Ctrl+V, before any update has placed the aircraft.
    [Fact]
    public void Before_the_first_update_the_readout_says_starting()
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(8, 0, 0);
        Assert.Equal("Visual guidance starting.", rig.Readout);
    }

    // Cross-track is positive right of the centreline facing the runway (for runway 09, south): on final
    // (within 0.1 nm) F says the side facing the runway; vectoring, flying parallel inbound, it says where the
    // centreline is from the cockpit, so right of it the centreline is to the left.
    [Theory]
    [InlineData(6, 0.08, "On final, 6.0 miles. 0.1 miles right of the centreline.")]
    [InlineData(6, -0.08, "On final, 6.0 miles. 0.1 miles left of the centreline.")]
    [InlineData(10, 2.0, ". Centreline 2.0 miles to your left.")]
    [InlineData(10, -2.0, ". Centreline 2.0 miles to your right.")]
    public void Right_of_the_centreline_reads_right(double alongNm, double crossNm, string expected)
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(alongNm, crossNm, 0);
        rig.UpdateAt(0);
        if (expected.StartsWith("On final", StringComparison.Ordinal))
            Assert.StartsWith(expected, rig.Readout);
        else
        {
            Assert.StartsWith("Vectoring to a", rig.Readout);
            Assert.Contains(expected, rig.Readout);
        }
    }
}
