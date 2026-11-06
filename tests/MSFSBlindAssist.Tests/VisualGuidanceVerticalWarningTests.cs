using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What BA says about the path on its own (visual guidance stage 5, plan Task 6), through the real manager
/// (<see cref="VisualGuidanceTestRig"/>, runway 09, 3°, the 60 ft glidepath bias, 140 kt, 30 Hz): "Too low"
/// and "Too high" on final, "You'll be ... high at final" while joining, and the mile calls with the path
/// once established. Each is queued, never cutting BA off.
/// </summary>
public class VisualGuidanceVerticalWarningTests
{
    private static readonly double FtPerNm3Deg = 6076.12 * Math.Tan(3.0 * Math.PI / 180);   // 318.4 ft a mile

    private static (VisualGuidanceTestRig Rig, List<string> Lines) OnFinalAt(double alongNm, double altOffsetFt)
    {
        var lines = new List<string>();
        var rig = new VisualGuidanceTestRig(record: lines.Add);
        rig.Place(alongNm, 0, 0, altOffsetFt: altOffsetFt);
        rig.UpdateAt(0);
        Assert.Equal("Final", rig.Phase);
        return (rig, lines);
    }

    private static List<(double T, bool Immediate, string Text)> Said(List<string> recording, string startsWith)
    {
        var say = new Regex(@"^VGREC say t=(\S+) imm=([01]) text=(.*)$", RegexOptions.CultureInvariant);
        return recording.Select(l => say.Match(l)).Where(m => m.Success)
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value == "1", m.Groups[3].Value))
            .Where(s => s.Item3.StartsWith(startsWith, StringComparison.Ordinal)).ToList();
    }

    // 400 ft low at 6 nm, where half a degree is 319 ft: said at once, queued; once more 10 s later while
    // still that low; never on a timer after that (plan decision 5).
    [Fact]
    public void Too_low_is_said_then_once_more_after_ten_seconds()
    {
        var (rig, lines) = OnFinalAt(6, -400);
        rig.FlyFor(30, 30);
        var low = Said(lines, "Too low");
        Assert.Equal(2, low.Count);
        Assert.Equal("Too low, 400 feet below the path", low[0].Text);
        Assert.False(low[0].Immediate);
        Assert.InRange(low[1].T - low[0].T, 10.0 - 1e-6, 10.0 + 1.0 / 30 + 1e-6);
        Assert.StartsWith("On final, 6.0 miles. On the centreline. Too low, 400 feet below the path", rig.Readout);
    }

    // Flying in at 2 nm a minute, 300 ft low: at once, 10 s later, and once more as the datum comes down
    // through the 1,000 ft stable gate (3.89 nm out, 33 s in).
    [Fact]
    public void Too_low_again_at_the_gate()
    {
        var (rig, lines) = OnFinalAt(5, -300);
        rig.FlyFor(50, 30, t => rig.Place(5 - t / 30.0, 0, 0, altOffsetFt: -300));
        double gateT = (5 - (1000 + 300 - 60) / FtPerNm3Deg) * 30;
        var low = Said(lines, "Too low");
        Assert.Equal(3, low.Count);
        Assert.InRange(low[1].T - low[0].T, 10.0 - 1e-6, 10.0 + 1.0 / 30 + 1e-6);
        Assert.InRange(low[2].T, gateT - 0.1, gateT + 0.1);
    }

    // Established low 2 s of flight above the gate: the gate 2 s later is not a second call.
    [Fact]
    public void Too_low_not_doubled_at_the_gate()
    {
        double start = (1000 + 300 - 60) / FtPerNm3Deg + 2 / 30.0;
        var (rig, lines) = OnFinalAt(start, -300);
        rig.FlyFor(9, 30, t => rig.Place(start - t / 30.0, 0, 0, altOffsetFt: -300));
        Assert.Single(Said(lines, "Too low"));
    }

    // 600 ft high at 4 nm: even 2° steeper removes only 223 ft by the gate, so "Too high", once (plan
    // decision 3); at 7 nm it removes 863, so nothing.
    [Fact]
    public void Too_high_once()
    {
        var (rig, lines) = OnFinalAt(4, 600);
        rig.FlyFor(30, 30);
        var high = Said(lines, "Too high");
        Assert.Single(high);
        Assert.Equal("Too high, 600 feet above the path", high[0].Text);
        Assert.False(high[0].Immediate);
        Assert.StartsWith("On final, 4.0 miles. On the centreline. Too high, 600 feet above the path", rig.Readout);

        var (farRig, farLines) = OnFinalAt(7, 600);
        farRig.FlyFor(10, 30);
        Assert.Empty(Said(farLines, "Too high"));
    }

    // 70 ft low with the wheels 56 ft up (0.245 nm out): under 60 ft the correction stops and so do the
    // warnings. At 0.4 nm the same 70 ft low, wheels 105 ft up, is said. (At 742 fpm the flare starts at
    // 53 ft, 4.3 s out: stage 6.)
    [Fact]
    public void No_warning_under_sixty_feet()
    {
        var (rig, lines) = OnFinalAt(0.245, -70);
        rig.FlyFor(3, 30);
        Assert.Equal("Final", rig.Phase);
        Assert.Empty(Said(lines, "Too low"));

        var (higherRig, higherLines) = OnFinalAt(0.4, -70);
        higherRig.FlyFor(3, 30);
        Assert.Single(Said(higherLines, "Too low"));
    }

    // Once established the mile calls add the path; while vectoring they give the distance only.
    [Fact]
    public void Mile_calls_add_the_path_once_established()
    {
        var (rig, lines) = OnFinalAt(3.2, 80);
        rig.FlyFor(10, 30, t => rig.Place(3.2 - t / 30.0, 0, 0, altOffsetFt: 80));
        Assert.Equal("3 miles out, 80 feet high", Assert.Single(Said(lines, "3 miles out")).Text);

        var vectoringLines = new List<string>();
        var vectoring = new VisualGuidanceTestRig(record: vectoringLines.Add);
        vectoring.Place(3.2, 0.2, 0);
        vectoring.UpdateAt(0);
        Assert.Equal("Vectoring", vectoring.Phase);
        vectoring.FlyFor(10, 30, t => vectoring.Place(3.2 - t / 30.0, 0.2, 0));
        Assert.Equal("3 miles out", Assert.Single(Said(vectoringLines, "3 miles out")).Text);
    }

    // Joining 12 nm out on a 30° cut, 3,880 ft above the threshold: nothing while descending (where ATC
    // will level the aircraft is not known); once level 5 s, the hold, and once, "You'll be ... high at
    // final": the path at the join point (about 6.4 nm out) is near 2,100 ft, beyond 2° steeper by the gate.
    [Fact]
    public void High_at_final_said_once_when_level()
    {
        var lines = new List<string>();
        var rig = new VisualGuidanceTestRig(record: lines.Add);
        rig.Place(12, 3, -30);
        rig.UpdateAt(0);
        Assert.Equal("Joining", rig.Phase);
        rig.FlyFor(10, 30);
        Assert.Empty(Said(lines, "You'll be"));

        rig.FlyFor(30, 30, _ => rig.Place(12, 3, -30, vsFpm: 0, pitchDeg: 5.0));
        var high = Said(lines, "You'll be");
        var line = Assert.Single(high);
        Assert.Matches(@"^You'll be [0-9],[0-9]{2}0 feet high at final$", line.Text);
        Assert.False(line.Immediate);
        Assert.True(line.T >= 15 - 1e-6, $"said at {line.T:0.00} s, before the hold");
        Assert.Contains("you'll be", rig.Readout);
    }
}
