using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// Each MCDU's four annunciators (the cockpit's <c>CPT_FMS_*_LIGHT</c> and <c>FO_FMS_*_LIGHT</c> lamps, on the AC
/// light bus), shown in the MCDU window's status box and spoken as one comes on, as the MD-11's MCDU window does
/// (except the captain's MSG, which its fault light speaks).
/// </summary>
public class A300McduLightsTests
{
    [Theory]
    [InlineData(A300McduUnit.Captain, "INI_FMS1_message_light", "INI_FMS1_fail_light", "INI_FMS1_display_light", "INI_FMS1_offset_light")]
    [InlineData(A300McduUnit.FirstOfficer, "INI_FMS2_message_light", "INI_FMS2_fail_light", "INI_FMS2_display_light", "INI_FMS2_offset_light")]
    public void Each_mcdu_has_the_md11s_four_annunciators(A300McduUnit unit, string msg, string fail, string dspy, string ofst)
    {
        var lights = A300McduLights.For(unit);
        Assert.Equal(new[] { "MSG", "FAIL", "DSPY", "OFST" }, lights.Select(l => l.Label));
        Assert.Equal(new[] { msg, fail, dspy, ofst }, lights.Select(l => l.Var));
    }

    [Fact]
    public void A_light_is_lit_only_with_its_light_power_on()
    {
        var cache = new Dictionary<string, double>
        {
            [A300McduLights.For(A300McduUnit.FirstOfficer)[0].Key] = 1,
            [A300McduLights.For(A300McduUnit.FirstOfficer)[2].Key] = 1,
            [A300LampBoard.AcPowerKey] = 1,
        };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.Equal(new[] { "MSG", "DSPY" }, def.McduAnnunciators(A300McduUnit.FirstOfficer));
        Assert.Empty(def.McduAnnunciators(A300McduUnit.Captain));
        cache[A300LampBoard.AcPowerKey] = 0;
        Assert.Empty(def.McduAnnunciators(A300McduUnit.FirstOfficer));
    }

    [Fact]
    public void Each_light_streams_on_its_own_subscription_and_is_consumed_silently()
    {
        // The captain's MSG lamp is also the fault light "MCDU message light", whose own key streams the variable: that
        // one is read from the fault light's key instead of a second subscription.
        var def = new IniA300Definition();
        var speech = new SpeechCapture();
        var vars = def.GetVariables();
        Assert.Equal(new[] { "INI_FMS1_message_light" },
            A300McduLights.All.Where(light => def.StreamingKey(light.Key) != light.Key).Select(light => light.Var));
        foreach (var light in A300McduLights.All.Where(light => def.StreamingKey(light.Key) == light.Key))
        {
            Assert.Equal(light.Var, vars[light.Key].Name);
            Assert.True(vars[light.Key].ExcludeFromBatch && vars[light.Key].ExcludeFromMonitorManager, light.Key);
            Assert.True(def.ProcessSimVarUpdate(light.Key, 1, speech), light.Key);
        }
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_captains_msg_light_is_read_from_the_fault_light_that_streams_it()
    {
        var msg = A300McduLights.For(A300McduUnit.Captain).Single(l => l.Label == "MSG");
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        var fault = A300FaultLights.All.Single(l => l.Var == msg.Var);
        Assert.Equal(fault.Key, def.StreamingKey(msg.Key));
        Assert.DoesNotContain(msg.Key, def.GetVariables().Keys);
        cache[A300LampBoard.AcPowerKey] = 1;
        cache[fault.Key] = 1;
        Assert.Equal(new[] { "MSG" }, def.McduAnnunciators(A300McduUnit.Captain));
        cache[fault.Key] = 0;
        Assert.Empty(def.McduAnnunciators(A300McduUnit.Captain));
    }

    [Theory]
    [InlineData(A300McduUnit.FirstOfficer, new string[0], new[] { "MSG" }, "MSG")]
    [InlineData(A300McduUnit.FirstOfficer, new[] { "MSG" }, new[] { "MSG", "DSPY" }, "DSPY")]
    [InlineData(A300McduUnit.FirstOfficer, new[] { "MSG", "DSPY" }, new[] { "DSPY" }, null)]
    [InlineData(A300McduUnit.FirstOfficer, new[] { "MSG" }, new[] { "MSG" }, null)]
    [InlineData(A300McduUnit.Captain, new string[0], new[] { "FAIL", "OFST" }, "FAIL, OFST")]
    [InlineData(A300McduUnit.Captain, new[] { "MSG", "DSPY" }, new[] { "DSPY" }, null)]
    public void Only_a_light_coming_on_is_spoken(A300McduUnit unit, string[] before, string[] now, string? spoken) =>
        Assert.Equal(spoken, A300McduLights.ComingOn(before, now, unit));

    [Fact]
    public void The_captains_msg_is_left_to_the_lights()
    {
        // The captain's MSG lamp is also the fault light "MCDU message light", which speaks anywhere: the window
        // saying it too put the same news in the pilot's ear twice. The first officer's MSG has no fault light.
        Assert.Null(A300McduLights.ComingOn(new[] { "FAIL" }, new[] { "FAIL", "MSG" }, A300McduUnit.Captain));
        Assert.Equal("DSPY", A300McduLights.ComingOn(Array.Empty<string>(), new[] { "MSG", "DSPY" }, A300McduUnit.Captain));
        Assert.Equal("MSG", A300McduLights.ComingOn(Array.Empty<string>(), new[] { "MSG" }, A300McduUnit.FirstOfficer));

        var captainsMsg = A300McduLights.For(A300McduUnit.Captain).Single(l => l.Label == "MSG");
        var officersMsg = A300McduLights.For(A300McduUnit.FirstOfficer).Single(l => l.Label == "MSG");
        Assert.Contains(A300FaultLights.All, l => l.Var == captainsMsg.Var && l.Name == "MCDU message light");
        Assert.DoesNotContain(A300FaultLights.All, l => l.Var == officersMsg.Var);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_call_out_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("FAIL, OFST",
                A300McduLights.ComingOn(new[] { "MSG" }, new[] { "MSG", "FAIL", "OFST" }, A300McduUnit.Captain));
            Assert.Equal("MSG", A300McduLights.ComingOn(Array.Empty<string>(), new[] { "MSG" }, A300McduUnit.FirstOfficer));
            Assert.Equal("Captain MCDU: MSG, FAIL", A300McduLights.Status("Captain MCDU", new[] { "MSG", "FAIL" }));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(new string[0], "Captain MCDU: connected")]
    [InlineData(new[] { "MSG", "FAIL" }, "Captain MCDU: MSG, FAIL")]
    public void The_status_box_lists_the_lit_annunciators(string[] lit, string status) =>
        Assert.Equal(status, A300McduLights.Status("Captain MCDU", lit));
}
