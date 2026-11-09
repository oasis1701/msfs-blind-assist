using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>The universal automation's autopilot engage on the A300: AP 1's own panel row, from a known off,
/// confirmed on the two AP lights ([FO-18]); the stock AUTOPILOT_ON was never verified on this aircraft.</summary>
public class A300AutopilotEngageTests
{
    private readonly IniA300Definition _def;
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);

    public A300AutopilotEngageTests()
    {
        _def = new IniA300Definition
        {
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            CanLand = _ => true,
            Send = (_, rpn) => _sent.Add(rpn),
        };
        _def.Attach(_sim);
    }

    [Fact]
    public void Engage_presses_ap_1_once_from_a_known_off()
    {
        _cache["A300_AP_SWITCH_1"] = 0;
        _def.EngageAutopilot(_sim);
        Assert.Single(_sent);
        Assert.Contains("AIRLINER_AP_Switch_1_Set", _sent[0]);
    }

    [Fact]
    public void A_second_engage_before_ap_1_reads_back_never_flips_it_off_again()
    {
        // The press is remembered as commanded (2.5 s, [A300-1]) under the same key the row's own writes use.
        _cache["A300_AP_SWITCH_1"] = 0;
        _def.EngageAutopilot(_sim);
        _def.EngageAutopilot(_sim);
        Assert.Single(_sent);
    }

    [Fact]
    public void Engage_sends_nothing_when_ap_1_is_already_on_or_unread()
    {
        _cache["A300_AP_SWITCH_1"] = 1;
        _def.EngageAutopilot(_sim);
        _cache.Remove("A300_AP_SWITCH_1");
        _def.EngageAutopilot(_sim);
        Assert.Empty(_sent);
    }

    [Fact]
    public void Engaged_is_null_until_both_lights_are_read()
    {
        Assert.Null(_def.IsAutopilotEngaged(_sim));
        _cache["A300_AP_SWITCH_1"] = 0;
        Assert.Null(_def.IsAutopilotEngaged(_sim));
        _cache["A300_AP_SWITCH_2"] = 0;
        Assert.False(_def.IsAutopilotEngaged(_sim));
        _cache["A300_AP_SWITCH_2"] = 1;
        Assert.True(_def.IsAutopilotEngaged(_sim));
    }

    [Fact]
    public void Ap_1_toggles_so_the_automation_waits_for_its_reading_and_presses_3_s_apart()
    {
        // Its Set flips AP 1 ([A300-1]): never pressed blind, never pressed again before a press can read back
        // ([FO-18], the Fenix, iFly and HS787 toggles' spacing).
        Assert.True(_def.AutopilotEngageWaitsForState);
        Assert.Equal(3000, _def.AutopilotEngageRetryIntervalMs);
    }
}
