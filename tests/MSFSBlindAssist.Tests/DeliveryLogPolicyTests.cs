// Which individual-variable deliveries write a debug.log line.
//
// PR #255: an UNCHANGED, unforced delivery of a 1 Hz individual-def var was logged on every
// response (the Fenix registers ~50), so the whole 20 MB debug.log retention held about 35
// minutes of flying. Those are now logged once per var per cache lifetime. But an answer to a
// READ someone is waiting on — a fresh read (ReadFreshAsync: the MD-11 walker, every spoken
// read-back verdict) or a seed read — is low volume and is exactly the line that tells "the sim
// answered with the old value" from "the sim never answered", so it is always logged.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class DeliveryLogPolicyTests
{
    [Fact]
    public void A_changed_value_is_logged()
        => Assert.Equal(DeliveryLogLine.Firing, DeliveryLogPolicy.For(
            highFrequency: false, hasChanged: true, isForceUpdate: false, targetedRead: false, unchangedAlreadyLogged: true));

    [Fact]
    public void A_forced_read_is_logged_even_unchanged()
        => Assert.Equal(DeliveryLogLine.Firing, DeliveryLogPolicy.For(
            highFrequency: false, hasChanged: false, isForceUpdate: true, targetedRead: false, unchangedAlreadyLogged: true));

    [Fact]
    public void An_unchanged_answer_to_a_fresh_or_seed_read_is_always_logged()
        => Assert.Equal(DeliveryLogLine.Firing, DeliveryLogPolicy.For(
            highFrequency: false, hasChanged: false, isForceUpdate: false, targetedRead: true, unchangedAlreadyLogged: true));

    [Fact]
    public void The_first_unchanged_periodic_delivery_of_a_var_is_logged_once()
    {
        Assert.Equal(DeliveryLogLine.FirstUnchanged, DeliveryLogPolicy.For(
            highFrequency: false, hasChanged: false, isForceUpdate: false, targetedRead: false, unchangedAlreadyLogged: false));
        Assert.Equal(DeliveryLogLine.None, DeliveryLogPolicy.For(
            highFrequency: false, hasChanged: false, isForceUpdate: false, targetedRead: false, unchangedAlreadyLogged: true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void A_sim_frame_var_never_writes_the_per_fire_line(bool hasChanged, bool isForceUpdate, bool targetedRead)
        => Assert.Equal(DeliveryLogLine.None, DeliveryLogPolicy.For(
            highFrequency: true, hasChanged, isForceUpdate, targetedRead, unchangedAlreadyLogged: false));
}
