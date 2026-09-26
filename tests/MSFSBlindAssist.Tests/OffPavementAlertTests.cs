using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class OffPavementAlertTests
{
    private static readonly DateTime T0 = new(2026, 9, 26, 1, 23, 33, DateTimeKind.Utc);
    private static DateTime At(double s) => T0.AddSeconds(s);

    [Fact]
    public void Speaks_after_one_second_off_then_every_six()
    {
        var a = new OffPavementAlert();
        Assert.False(a.Update(true, 45, At(0.0)));
        Assert.False(a.Update(true, 45, At(0.9)));
        Assert.True(a.Update(true, 45, At(1.0)));
        Assert.False(a.Update(true, 45, At(1.5)));
        Assert.True(a.Update(true, 45, At(7.0)));
        Assert.False(a.Update(true, 45, At(12.9)));
        Assert.True(a.Update(true, 45, At(13.0)));
    }

    [Fact]
    public void Stopped_in_the_grass_is_not_spoken_until_moving_again()
    {
        var a = new OffPavementAlert();
        Assert.False(a.Update(true, 3, At(0)));
        Assert.False(a.Update(true, 3, At(5)));
        Assert.True(a.Update(true, 10, At(5.1)));
    }

    [Fact]
    public void A_brief_return_to_pavement_does_not_restart_the_alert()
    {
        var a = new OffPavementAlert();
        a.Update(true, 40, At(0));
        Assert.True(a.Update(true, 40, At(1)));
        Assert.False(a.Update(false, 40, At(2)));
        Assert.False(a.Update(true, 40, At(3.5)));   // back off after 1.5 s on: no fresh onset
        Assert.True(a.Update(true, 40, At(7.0)));    // the repeat timer carried on
    }

    [Fact]
    public void Two_seconds_back_on_pavement_re_arms_it()
    {
        var a = new OffPavementAlert();
        a.Update(true, 40, At(0));
        Assert.True(a.Update(true, 40, At(1)));
        a.Update(false, 40, At(2));
        a.Update(false, 40, At(4));
        Assert.False(a.Update(true, 40, At(4.5)));
        Assert.True(a.Update(true, 40, At(5.5)));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var a = new OffPavementAlert();
        a.Update(true, 40, At(0));
        Assert.True(a.Update(true, 40, At(1)));
        a.Reset();
        Assert.False(a.Update(true, 40, At(1.2)));
        Assert.True(a.Update(true, 40, At(2.2)));
    }

    [Theory]
    // Airborne - a touch-and-go or a go-around after touchdown, with the rollout still running - is never off
    // the pavement, whatever lies below the climb-out.
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void Only_an_aircraft_on_the_ground_can_be_off_the_pavement(
        bool onGround, bool onRolloutRunway, bool onMappedPavement, bool off)
        => Assert.Equal(off, OffPavementAlert.IsOffPavement(onGround, onRolloutRunway, onMappedPavement));
}
