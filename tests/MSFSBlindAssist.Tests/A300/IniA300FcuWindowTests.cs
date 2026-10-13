using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>The four FCU windows in words, and the altitude window's call-out tracker.</summary>
public class A300FcuWindowsTests
{
    [Fact]
    public void The_windows_read_as_the_typed_values_confirm()
    {
        Assert.Equal("Speed 250 knots", A300FcuWindows.Speed(250.2, isMach: false));
        Assert.Equal("Mach 0.78", A300FcuWindows.Speed(0.78, isMach: true));
        Assert.Equal("Heading 005", A300FcuWindows.Heading(5.4));
        Assert.Equal("Heading 000", A300FcuWindows.Heading(360));
        Assert.Equal("Altitude 12,000 feet", A300FcuWindows.Altitude(12000));
        Assert.Equal("Vertical speed -1,500 feet per minute", A300FcuWindows.VerticalSpeed(-1500));
        Assert.Equal("Vertical speed 0 feet per minute", A300FcuWindows.VerticalSpeed(-0.2));
    }

    [Fact]
    public void A_typed_value_and_its_window_say_the_same_words()
    {
        Assert.Equal(A300TypedValues.Plan(A300TypedValues.HeadingKey, 270, false).Confirmation, A300FcuWindows.Heading(270));
        Assert.Equal(A300TypedValues.Plan(A300TypedValues.AltitudeKey, 12000, false).Confirmation, A300FcuWindows.Altitude(12000));
        Assert.Equal(A300TypedValues.Plan(A300TypedValues.VerticalSpeedKey, -1500, false).Confirmation, A300FcuWindows.VerticalSpeed(-1500));
        Assert.Equal(A300TypedValues.Plan(A300TypedValues.SpeedKey, 250, false).Confirmation, A300FcuWindows.Speed(250, isMach: false));
        Assert.Equal(A300TypedValues.Plan(A300TypedValues.SpeedKey, 0.78, true).Confirmation, A300FcuWindows.Speed(0.78, isMach: true));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_windows_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Mach 0.78", A300FcuWindows.Speed(0.78, isMach: true));
            Assert.Equal("Altitude 12,000 feet", A300FcuWindows.Altitude(12000));
            Assert.Equal("Vertical speed -1,500 feet per minute", A300FcuWindows.VerticalSpeed(-1500));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_tracker_is_baseline_first_and_skips_its_own_echo()
    {
        var t = new A300WindowTracker();
        Assert.Null(t.Observe("Altitude 10,000 feet", 0));
        Assert.Equal("Altitude 12,000 feet", t.Observe("Altitude 12,000 feet", 100));
        Assert.Null(t.Observe("Altitude 12,000 feet", 200));
        t.SuppressEcho(1000);
        Assert.Null(t.Observe("Altitude 15,000 feet", 1000 + A300WindowTracker.EchoWindowMs));
        Assert.Equal("Altitude 16,000 feet", t.Observe("Altitude 16,000 feet", 1001 + A300WindowTracker.EchoWindowMs));
    }

    [Fact]
    public void A_seed_fills_only_a_missing_baseline()
    {
        var t = new A300WindowTracker();
        Assert.True(t.Seed("Altitude 10,000 feet"));
        Assert.False(t.Seed("Altitude 11,000 feet"));
        Assert.Equal("Altitude 12,000 feet", t.Observe("Altitude 12,000 feet", 0));
        t.Reset();
        Assert.Null(t.Observe("Altitude 13,000 feet", 0));
    }
}

/// <summary>
/// The altitude window speaks a change MSFSBA did not make; a knob step from the FCU panel is read
/// back once it lands. The heading, speed and vertical speed windows never speak on their own (the
/// autopilot's own logic writes them too).
/// </summary>
public class IniA300FcuWindowBehaviourTests
{
    private long _now = 10_000;
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();

    public IniA300FcuWindowBehaviourTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ => Task.CompletedTask,
            ReadFresh = (_, key, _) => Task.FromResult(_cache.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
    }

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    [Fact]
    public void The_altitude_window_rides_the_batch_and_has_a_ctrl_m_row()
    {
        var def = _def.GetVariables()[A300Readouts.AltitudeKey];
        Assert.True(ContinuousBatchLayout.RidesBatch(def));
        Assert.False(def.ExcludeFromMonitorManager);
        Assert.Equal("Altitude window", def.DisplayName);
    }

    [Fact]
    public void The_other_windows_stay_on_request()
    {
        var vars = _def.GetVariables();
        foreach (var key in new[] { A300Readouts.SpeedKey, A300Readouts.HeadingKey, A300Readouts.VerticalSpeedKey })
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
    }

    [Fact]
    public void A_change_made_outside_msfsba_is_spoken()
    {
        Assert.True(_def.ProcessSimVarUpdate(A300Readouts.AltitudeKey, 10000, _speech));
        Assert.True(_def.ProcessSimVarUpdate(A300Readouts.AltitudeKey, 12000, _speech));
        Assert.Equal(new[] { "Altitude 12,000 feet" }, _speech.All);
        Assert.Empty(_speech.Interrupts);   // queued, like every other background change
    }

    [Fact]
    public void A_typed_altitude_is_confirmed_once()
    {
        Deliver(A300Readouts.AltitudeKey, 10000);
        Assert.True(Set(A300TypedValues.AltitudeKey, 12000));
        _now += 1000;
        Deliver(A300Readouts.AltitudeKey, 12000);
        Assert.Equal(new[] { "Altitude 12,000 feet" }, _speech.All);   // the typed confirmation, not a second call-out
    }

    [Fact]
    public void An_altitude_typed_in_the_value_box_is_confirmed_once()
    {
        Deliver(A300Readouts.AltitudeKey, 10000);
        _def.SubmitBoxValue(A300TypedValues.AltitudeKey, "Altitude", 12000, _sim, _speech);
        _now += 1000;
        Deliver(A300Readouts.AltitudeKey, 12000);
        Assert.Equal(new[] { "Altitude 12,000 feet" }, _speech.All);   // the box's confirmation, not a second call-out
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void An_altitude_typed_in_the_value_box_is_confirmed_once_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Deliver(A300Readouts.AltitudeKey, 10000);
            _def.SubmitBoxValue(A300TypedValues.AltitudeKey, "Altitude", 12000, _sim, _speech);
            _now += 1000;
            Deliver(A300Readouts.AltitudeKey, 12000);
            Assert.Equal(new[] { "Altitude 12,000 feet" }, _speech.All);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void An_altitude_the_box_refuses_does_not_mute_the_call_out()
    {
        Deliver(A300Readouts.AltitudeKey, 10000);
        _def.SubmitBoxValue(A300TypedValues.AltitudeKey, "Altitude", 60000, _sim, _speech);   // out of range
        _def.CanLand = _ => false;
        _def.SubmitBoxValue(A300TypedValues.AltitudeKey, "Altitude", 12000, _sim, _speech);   // cannot land
        _now += 1000;
        Deliver(A300Readouts.AltitudeKey, 15000);   // the pilot's own knob, not MSFSBA's write
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Altitude: 100 to 49000 feet", "Altitude unavailable", "Altitude 15,000 feet" }, _speech.All);
    }

    [Fact]
    public async Task A_knob_step_is_read_back_once_it_lands()
    {
        _cache[A300Readouts.HeadingKey] = 271;
        Assert.True(Set("A300_HEADING_KNOB#INC", 1));
        await Task.Yield();
        Assert.Equal(new[] { "1 (>B:AIRLINER_HEADING_KNOB_Set)" }, _sent);
        Assert.Equal(new[] { "Heading 271" }, _speech.Interrupts);
    }

    [Fact]
    public async Task A_speed_knob_step_reads_mach_in_mach_mode()
    {
        _cache[A300FcuState.SpeedMachLightKey] = 1;
        _cache[A300Readouts.SpeedKey] = 0.79;
        Assert.True(Set("A300_SPEED_KNOB#DEC", 1));
        await Task.Yield();
        Assert.Equal(new[] { "-1 (>B:AIRLINER_SPEED_KNOB_Set)" }, _sent);
        Assert.Equal(new[] { "Mach 0.79" }, _speech.Interrupts);
    }

    [Fact]
    public async Task An_altitude_knob_step_is_read_back_and_not_spoken_twice()
    {
        Deliver(A300Readouts.AltitudeKey, 10000);
        _cache[A300Readouts.AltitudeKey] = 10100;
        Assert.True(Set("A300_ALT_KNOB#INC", 1));
        await Task.Yield();
        _now += 1000;
        Deliver(A300Readouts.AltitudeKey, 10100);
        Assert.Equal(new[] { "Altitude 10,100 feet" }, _speech.All);
    }

    [Fact]
    public async Task A_step_that_cannot_land_is_not_read_back()
    {
        _def.CanLand = _ => false;
        _cache[A300Readouts.HeadingKey] = 271;
        Assert.True(Set("A300_HEADING_KNOB#INC", 1));
        await Task.Yield();
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Heading knob increase unavailable" }, _speech.All);
    }

    [Fact]
    public void A_flight_load_wipes_the_baseline()
    {
        Deliver(A300Readouts.AltitudeKey, 10000);
        _def.OnSimContextReset();
        Deliver(A300Readouts.AltitudeKey, 35000);   // the loaded flight's window: a baseline, not a change
        Assert.Empty(_speech.All);
    }
}
