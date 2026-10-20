using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the XLS mag-check drop is read: once the needle has stopped moving. The samples are the
/// live run-up at VCBI (2026-10-20), where a fixed two seconds read the right magneto at 1900 for
/// a drop of 90 while it was settling at 1870, a drop of 120.
/// </summary>
public class DA40MagnetoSettleTests
{
    [Fact]
    public void The_drop_is_read_once_the_needle_has_stopped_falling()
    {
        var s = new DA40MagnetoSettle();
        s.Sample(1990, 0);                 // on BOTH
        s.Start(0);                        // key to RIGHT
        s.Sample(1900, 1000);
        Assert.False(s.IsDue(2000));       // the old fixed read: still falling
        s.Sample(1880, 2000);
        Assert.False(s.IsDue(3000));
        s.Sample(1870, 3000);              // within 10 of the last: settling
        Assert.False(s.IsDue(3400));
        Assert.True(s.IsDue(3500));        // quiet for 1.5 s since the last real change
    }

    [Fact]
    public void A_steady_needle_that_sends_no_samples_is_read_after_the_minimum()
    {
        var s = new DA40MagnetoSettle();
        s.Start(0);
        Assert.False(s.IsDue(DA40MagnetoSettle.MinMs - 1));
        Assert.True(s.IsDue(DA40MagnetoSettle.MinMs));
    }

    [Fact]
    public void A_needle_that_keeps_wandering_is_read_at_the_ceiling()
    {
        var s = new DA40MagnetoSettle();
        s.Start(0);
        for (long t = 500; t < DA40MagnetoSettle.MaxMs; t += 500)
        {
            s.Sample(1900 + (t / 500 % 2) * 40, t);
            Assert.False(s.IsDue(t));
        }
        Assert.True(s.IsDue(DA40MagnetoSettle.MaxMs));
    }

    [Fact]
    public void Nothing_is_due_when_no_side_is_pending()
    {
        var s = new DA40MagnetoSettle();
        Assert.False(s.IsDue(100_000));
        s.Start(0);
        s.Cancel();
        Assert.False(s.IsDue(100_000));
    }
}
