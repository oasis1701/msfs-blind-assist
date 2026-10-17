using MSFSBlindAssist.FirstOfficer;
using Xunit;
using X = MSFSBlindAssist.FirstOfficer.TransitionCrossingDetector.Crossing;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class TransitionCrossingDetectorTests
{
    private static TransitionCrossingDetector Make(int ta, int tl)
    {
        var d = new TransitionCrossingDetector();
        d.SetThresholds(ta, tl);
        return d;
    }

    // A steady climb from below the TA to above it fires ClimbToStd exactly once.
    [Fact]
    public void Climb_FiresStdOnce()
    {
        var d = Make(6000, 6000);
        Assert.Equal(X.None, d.Update(3000, climbing: true, descending: false)); // arm below
        Assert.Equal(X.ClimbToStd, d.Update(6500, climbing: true, descending: false));
        Assert.Equal(X.None, d.Update(9000, climbing: true, descending: false)); // no re-fire
        Assert.Equal(X.None, d.Update(12000, climbing: true, descending: false));
    }

    // A steady descent from above the TL to below it fires DescendToQnh exactly once.
    [Fact]
    public void Descent_FiresQnhOnce()
    {
        var d = Make(6000, 6000);
        d.Update(12000, climbing: true, descending: false);  // arm above (via climb path)
        Assert.Equal(X.None, d.Update(9000, climbing: false, descending: true));
        Assert.Equal(X.DescendToQnh, d.Update(5500, climbing: false, descending: true));
        Assert.Equal(X.None, d.Update(4000, climbing: false, descending: true)); // no re-fire
        Assert.Equal(X.None, d.Update(2000, climbing: false, descending: true));
    }

    // REGRESSION (the reported bug): destination TL far above origin TA => overlapping
    // hysteresis bands. The old single-latch monitor re-fired the QNH call-out on every
    // tick through the overlap band on descent. With independent detectors it fires ONCE.
    [Fact]
    public void Descent_WithTlWellAboveTa_DoesNotSpamQnh()
    {
        var d = Make(4000, 6000);   // overlap band is [4300, 5700]
        d.Update(12000, climbing: true, descending: false);   // cruise: arms QNH

        int qnh = 0;
        // Descend one 100 ft step at a time straight through the overlap band.
        for (int alt = 6300; alt >= 3000; alt -= 100)
            if (d.Update(alt, climbing: false, descending: true) == X.DescendToQnh)
                qnh++;

        Assert.Equal(1, qnh);
    }

    // Levelling off inside the overlap band must not flip-flop STD/QNH (VS ~ 0 => neither
    // climbing nor descending, so both direction gates are open).
    [Fact]
    public void LevelOffInOverlapBand_IsSilent()
    {
        var d = Make(4000, 6000);
        d.Update(12000, climbing: true, descending: false);   // arms QNH
        Assert.Equal(X.DescendToQnh, FirstDescendHit(d));     // fire the single QNH

        for (int i = 0; i < 20; i++)
            Assert.Equal(X.None, d.Update(5000, climbing: false, descending: false)); // level in band
    }

    // Climb through the overlap band fires STD once and never a spurious QNH.
    [Fact]
    public void Climb_WithTlWellAboveTa_FiresStdOnceNoQnh()
    {
        var d = Make(4000, 6000);
        d.Update(1000, climbing: true, descending: false);   // arm below TA

        int std = 0, qnh = 0;
        for (int alt = 1000; alt <= 9000; alt += 100)
        {
            var c = d.Update(alt, climbing: true, descending: false);
            if (c == X.ClimbToStd)   std++;
            if (c == X.DescendToQnh) qnh++;
        }
        Assert.Equal(1, std);
        Assert.Equal(0, qnh);
    }

    // Starting mid-flight above both thresholds must not false-fire before a real crossing.
    [Fact]
    public void StartAboveThresholds_NoImmediateFire()
    {
        var d = Make(6000, 6000);
        Assert.Equal(X.None, d.Update(30000, climbing: false, descending: false));
    }

    // A go-around (climb back up after the QNH) re-arms so the next descent fires again.
    [Fact]
    public void GoAround_ReArmsQnh()
    {
        var d = Make(6000, 6000);
        d.Update(12000, true, false);
        Assert.Equal(X.DescendToQnh, d.Update(5000, false, true));   // QNH #1
        Assert.Equal(X.ClimbToStd, d.Update(9000, true, false));     // climb back through TA
        Assert.Equal(X.DescendToQnh, d.Update(5000, false, true));   // QNH #2
    }

    private static X FirstDescendHit(TransitionCrossingDetector d)
    {
        for (int alt = 6300; alt >= 3000; alt -= 100)
        {
            var c = d.Update(alt, climbing: false, descending: true);
            if (c != X.None) return c;
        }
        return X.None;
    }
}
