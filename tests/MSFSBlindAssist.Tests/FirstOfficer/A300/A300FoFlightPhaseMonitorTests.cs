using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.A300;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

public class A300FoFlightPhaseMonitorTests
{
    private readonly Dictionary<string, double> _cache = new();
    private readonly FakeA300FoPort _port = new();
    private readonly SpeechCapture _speech = new();
    private readonly A300FoActionExecutor _exec = new();
    private readonly A300FoFlightPhaseMonitor _monitor;

    public A300FoFlightPhaseMonitorTests()
    {
        _exec.SetState(new A300FoStateEvaluator { Read = key => _cache.TryGetValue(key, out var v) ? v : null });
        _exec.SetAnnouncer(_speech);
        _exec.SetPort(_port);
        _port.OnApply = (key, value) => _cache[key] = value;
        foreach (var key in new[] { A300FoKeys.LandingL, A300FoKeys.LandingR }) _cache[key] = 0;
        foreach (var key in new[] { A300FoKeys.TurnoffL, A300FoKeys.TurnoffR }) _cache[key] = 0;
        _monitor = new A300FoFlightPhaseMonitor(_exec, _speech);
    }

    [Fact]
    public void Climbing_through_ten_thousand_retracts_the_landing_lights()
    {
        _monitor.Update(9_000, 2000, onGround: false);
        _monitor.Update(10_500, 2000, onGround: false);
        Assert.Contains((A300FoKeys.LandingL, 2), _port.Applied);
        Assert.Contains("Above ten thousand. Landing lights retracted.", _speech.All);
    }

    [Fact]
    public void Descending_through_ten_thousand_turns_on_landing_and_turnoff_lights()
    {
        _cache[A300FoKeys.LandingL] = 2; _cache[A300FoKeys.LandingR] = 2;
        _monitor.Update(11_000, -2000, onGround: false);
        _monitor.Update(9_500, -2000, onGround: false);
        Assert.Contains((A300FoKeys.LandingL, 0), _port.Applied);
        Assert.Contains((A300FoKeys.TurnoffR, 1), _port.Applied);
        Assert.Contains("Below ten thousand. Landing and runway turnoff lights on.", _speech.All);
    }

    [Fact]
    public void The_switch_is_never_touched_with_ten_thousand_lights_disabled()
    {
        _monitor.AutoLights10kEnabled = false;
        _monitor.Update(9_000, 2000, onGround: false);
        _monitor.Update(10_500, 2000, onGround: false);
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public void Climbing_through_the_transition_altitude_sets_standard_through_ctrl_b()
    {
        _monitor.SetThresholds(18_000, 18_000);
        _monitor.Update(17_000, 2000, onGround: false);
        _monitor.Update(18_400, 2000, onGround: false);
        Assert.Equal(1, _port.StandardCalls);
        Assert.Contains("Transition altitude.", _speech.All);
    }

    [Fact]
    public void With_automatic_altimeters_off_the_transition_altitude_is_a_reminder_and_nothing_is_set()
    {
        _monitor.AutoAltimetersEnabled = false;
        _monitor.SetThresholds(18_000, 18_000);
        _monitor.Update(17_000, 2000, onGround: false);
        _monitor.Update(18_400, 2000, onGround: false);
        Assert.Equal(0, _port.StandardCalls);
        Assert.Contains(TransitionAltimeters.ReminderInstead(TransitionCrossingDetector.Crossing.ClimbToStd, false), _speech.All);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void The_transition_level_is_announce_only_and_says_what_the_reminder_says(bool auto)
    {
        _monitor.AutoAltimetersEnabled = auto;
        _monitor.SetThresholds(18_000, 18_000);
        _monitor.Update(19_000, -2000, onGround: false);
        _monitor.Update(17_500, -2000, onGround: false);
        Assert.Equal(0, _port.StandardCalls);
        Assert.Contains(TransitionAltimeters.ReminderInstead(TransitionCrossingDetector.Crossing.DescendToQnh, false), _speech.All);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void With_no_plan_the_reminder_is_the_fleets_and_promises_nothing_when_automatic_altimeters_are_off(bool auto)
    {
        _monitor.AutoAltimetersEnabled = auto;
        _monitor.Update(17_000, 2000, onGround: false);
        _monitor.Update(18_400, 2000, onGround: false);
        Assert.Equal(new[] { TransitionAltimeters.NoTransitionLoadedReminder(auto) }, _speech.All);
    }

    [Fact]
    public void Its_calls_queue_behind_other_speech_and_never_interrupt()
    {
        _monitor.SetThresholds(18_000, 18_000);
        _monitor.Update(9_000, 2000, onGround: false);
        _monitor.Update(10_500, 2000, onGround: false);
        _monitor.Update(17_000, 2000, onGround: false);
        _monitor.Update(18_400, 2000, onGround: false);
        _monitor.Update(19_000, -2000, onGround: false);
        _monitor.Update(17_500, -2000, onGround: false);
        _monitor.Update(9_500, -2000, onGround: false);
        Assert.True(_speech.All.Count >= 4);
        Assert.Empty(_speech.Interrupts);
    }

    [Fact]
    public void The_profile_names_the_aircraft_and_builds_its_flows_and_checklists()
    {
        var profile = new A300FoProfile(new IniA300Definition(), _speech);
        Assert.Equal("First Officer — iniBuilds A300", profile.Title);
        Assert.Same(profile.CreateEvaluator(), profile.CreateEvaluator());
        Assert.Equal(14, profile.BuildFlows().Count);
        Assert.Equal(25, profile.BuildChecklists().Count);
    }
}
