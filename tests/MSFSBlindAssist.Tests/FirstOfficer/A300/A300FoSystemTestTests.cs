using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.FirstOfficer.A300;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>Every test reports pass or fail from the lights the aircraft lights (design decision), held as
/// long as the aircraft's checklist holds it, with the definition quiet for its whole run.</summary>
public class A300FoSystemTestTests
{
    private readonly Dictionary<string, double> _cache = new();
    private readonly FakeA300FoPort _port = new();
    private readonly SpeechCapture _speech = new();
    private readonly A300FoActionExecutor _exec = new();
    private readonly A300FoStateEvaluator _state;

    public A300FoSystemTestTests()
    {
        _state = new A300FoStateEvaluator { Read = key => _cache.TryGetValue(key, out var v) ? v : null };
        _exec.SetState(_state);
        _exec.SetAnnouncer(_speech);
        _exec.SetPort(_port);
    }

    /// <summary>Lights or darkens a board light under its own rule (A300FoTestLights, Task 4).</summary>
    private void Light(string id, bool on) => A300FoTestLights.Set(_cache, id, on);

    [Fact]
    public async Task The_apu_fire_test_passes_when_its_handle_and_squib_light_while_held()
    {
        Light(A300FoKeys.FireHandleApuLight, false);
        Light(A300FoKeys.ApuSquibLight, false);
        _port.Holds[A300FoKeys.ApuLoopTest] = 5000;
        _port.OnApply = (key, _) =>
        {
            if (key == A300FoKeys.ApuLoopTest) Light(A300FoKeys.FireHandleApuLight, true);
            if (key == A300FoKeys.ApuSquibTest) Light(A300FoKeys.ApuSquibLight, true);
        };
        Assert.True(await _exec.Set(A300FoSystemTests.ApuFire.Key, 1));
        Assert.Equal(new[] { "APU fire test: pass." }, _speech.All);
        Assert.True(_port.QuietMs >= 5000);
    }

    [Fact]
    public async Task A_test_whose_light_never_lights_fails_and_names_it()
    {
        Light(A300FoKeys.FireHandleApuLight, false);
        Light(A300FoKeys.ApuSquibLight, false);
        _port.OnApply = (key, _) => { if (key == A300FoKeys.ApuSquibTest) Light(A300FoKeys.ApuSquibLight, true); };
        Assert.False(await _exec.Set(A300FoSystemTests.ApuFire.Key, 1));
        Assert.Equal(new[] { $"APU fire test: fail. {A300FoSystemTests.LightName(A300FoKeys.FireHandleApuLight)} did not light." }, _speech.All);
    }

    [Fact]
    public async Task A_light_already_lit_before_the_press_is_not_a_pass()
    {
        Light(A300FoKeys.FireHandleApuLight, true);   // stuck on before the test
        Light(A300FoKeys.ApuSquibLight, false);
        _port.OnApply = (key, _) => { if (key == A300FoKeys.ApuSquibTest) Light(A300FoKeys.ApuSquibLight, true); };
        Assert.False(await _exec.Set(A300FoSystemTests.ApuFire.Key, 1));
    }

    [Fact]
    public async Task The_takeoff_config_test_passes_only_with_the_master_warning_dark()
    {
        _cache[A300FoKeys.MasterWarning] = 0;
        Assert.True(await _exec.Set(A300FoSystemTests.TakeoffConfig.Key, 1));
        Assert.Equal(new[] { "Takeoff config test: pass." }, _speech.All);

        _speech.All.Clear();
        _port.OnApply = (key, _) =>
        {
            if (key == A300FoKeys.TakeoffConfigTest) _cache[A300FoKeys.MasterWarning] = 1;
            if (key == A300FoKeys.MasterWarningButton) _cache[A300FoKeys.MasterWarning] = 0;
        };
        Assert.False(await _exec.Set(A300FoSystemTests.TakeoffConfig.Key, 1));
        Assert.Equal("Takeoff config test: fail. Master warning came on.", _speech.All[0]);
        Assert.Contains((A300FoKeys.MasterWarningButton, 1), _port.Applied);   // reset afterwards
    }

    [Fact]
    public async Task The_engine_light_up_turns_the_starter_off_when_n2_never_reaches_20()
    {
        _cache[A300FoKeys.Starter2] = 1;
        _port.OnApply = (key, value) => _cache[key] = value;
        _state.SetEngineN2(0, 12);
        Assert.False(await _exec.Set(A300FoKeys.LightUp2, 1));
        Assert.Contains((A300FoKeys.Starter2, 0), _port.Applied);
        Assert.Equal("Engine 2 did not reach 20 percent N2. Starter off.", _speech.All.Last());
    }

    [Fact]
    public async Task A_starter_that_does_not_switch_off_is_never_called_off()
    {
        _cache[A300FoKeys.Starter2] = 1;   // the aircraft ignores the write
        _state.SetEngineN2(0, 12);
        Assert.False(await _exec.Set(A300FoKeys.LightUp2, 1));
        Assert.Equal("Engine 2 did not reach 20 percent N2, and its starter did not switch off.", _speech.All.Last());
    }

    [Fact]
    public async Task A_test_light_that_arrives_seconds_after_a_short_press_still_counts()
    {
        // Lights arrive once a second ([A300-24]): a 250 ms press is watched for seconds, not one delivery.
        Light(A300FoKeys.GpwsLight, false);
        _port.OnDelay = elapsed => { if (elapsed >= 3000) Light(A300FoKeys.GpwsLight, true); };
        Assert.True(await _exec.Set(A300FoSystemTests.Gpws.Key, 1));
        Assert.Equal(new[] { "GPWS test: pass." }, _speech.All);
    }

    [Fact]
    public async Task The_gpws_test_passes_on_the_glide_slope_light_seven_seconds_after_the_press()
    {
        // Measured 2026-10-21, twice: the below glide slope light comes on about 7 s after the press and the
        // GPWS warning light about 25 s after; a 4 s watch on the warning light alone reported a working test
        // as failed.
        Light(A300FoKeys.GlideslopeLight, false);
        Light(A300FoKeys.GpwsLight, false);
        _port.OnDelay = elapsed => { if (elapsed >= 7000) Light(A300FoKeys.GlideslopeLight, true); };
        Assert.True(await _exec.Set(A300FoSystemTests.Gpws.Key, 1));
        Assert.Equal(new[] { "GPWS test: pass." }, _speech.All);
    }

    [Fact]
    public async Task A_gpws_test_with_no_light_fails_and_names_the_glide_slope_light()
    {
        Light(A300FoKeys.GlideslopeLight, false);
        Light(A300FoKeys.GpwsLight, false);
        Assert.False(await _exec.Set(A300FoSystemTests.Gpws.Key, 1));
        Assert.Equal(new[] { $"GPWS test: fail. {A300FoSystemTests.LightName(A300FoKeys.GlideslopeLight)} did not light." }, _speech.All);
        Assert.True(_port.Elapsed >= 10_000, $"watched {_port.Elapsed} ms");
    }

    [Fact]
    public async Task The_engine_light_up_succeeds_at_20_percent()
    {
        _port.OnDelay = elapsed => { if (elapsed >= 10_000) _state.SetEngineN2(0, 21); };
        _state.SetEngineN2(0, 0);
        Assert.True(await _exec.Set(A300FoKeys.LightUp2, 1));
        Assert.Empty(_port.Applied);
    }

    [Fact]
    public async Task The_annunciator_test_goes_to_test_then_back_to_bright_and_reads_the_light_power()
    {
        _cache[A300FoKeys.AnnLight] = 1;
        _cache[A300FoKeys.LightPowerAc] = 1; _cache[A300FoKeys.LightPowerDc] = 1;
        _port.OnApply = (key, value) => _cache[key] = value;
        Assert.True(await _exec.Set(A300FoKeys.AnnunciatorTest, 1));
        Assert.Equal(new[] { (A300FoKeys.AnnLight, 0), (A300FoKeys.AnnLight, 1) }, _port.Applied);
        Assert.Equal(new[] { "Annunciator light test: pass." }, _speech.All);
    }

    [Fact]
    public async Task The_weather_radar_test_says_it_has_no_readable_result()
    {
        _cache[A300FoKeys.WxrSystem] = 1; _cache[A300FoKeys.WxrMode] = 1;
        _port.OnApply = (key, value) => _cache[key] = value;
        Assert.True(await _exec.Set(A300FoKeys.WeatherRadarTest, 1));
        Assert.Equal(new[] { (A300FoKeys.WxrSystem, 2), (A300FoKeys.WxrMode, 0), (A300FoKeys.WxrMode, 1), (A300FoKeys.WxrSystem, 1) }, _port.Applied);
        Assert.Equal(new[] { "Weather radar test done. No result I can read." }, _speech.All);
    }

    [Fact]
    public void Every_test_row_and_light_exists()
    {
        foreach (var test in A300FoSystemTests.ByKey.Values)
            foreach (var press in test.Presses)
            {
                Assert.Contains(press.Row, A300FoKeys.SingleRows);
                foreach (var light in press.Lights)
                    Assert.Contains(light, A300FoKeys.Lights);
            }
    }
}
