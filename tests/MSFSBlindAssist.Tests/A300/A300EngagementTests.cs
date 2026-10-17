using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>The autopilot and autothrottle engaging and disconnecting, which the FMA columns do not say.</summary>
public class A300EngagementTrackerTests
{
    private readonly A300EngagementTracker _tracker = new();

    private IReadOnlyList<string> Observe(string? ap, bool at, bool apOwn = false, bool atOwn = false) =>
        _tracker.Observe(new A300Engagement(ap, at), apOwn, atOwn).Select(c => c.Phrase).ToList();

    [Fact]
    public void The_first_reading_is_silent() => Assert.Empty(Observe("CMD 1", true));

    [Fact]
    public void A_disconnect_is_spoken()
    {
        Observe("CMD 1", true);
        Assert.Equal(new[] { "Autopilot disconnected", "Autothrottle disconnected" }, Observe(null, false));
    }

    [Fact]
    public void An_engagement_names_the_autopilot()
    {
        Observe(null, false);
        Assert.Equal(new[] { "Autopilot CMD 1", "Autothrottle armed" }, Observe("CMD 1", true));
        Assert.Equal(new[] { "Autopilot Dual" }, Observe("Dual", true));
        Assert.Empty(Observe("Dual", true));
    }

    [Fact]
    public void The_pilots_own_pick_is_silent()
    {
        Observe("CMD 1", true);
        Assert.Empty(Observe(null, false, apOwn: true, atOwn: true));
    }

    [Fact]
    public void Each_callout_carries_its_own_column()
    {
        Observe("CMD 1", true);
        var callouts = _tracker.Observe(new A300Engagement(null, false), false, false);
        Assert.Equal(new[] { A300FmaColumn.Autopilot, A300FmaColumn.Autothrottle }, callouts.Select(c => c.Column));
    }

    [Fact]
    public void A_reset_makes_the_next_reading_a_baseline()
    {
        Observe("CMD 1", true);
        _tracker.Reset();
        Assert.Empty(Observe(null, false));
    }
}

/// <summary>The definition speaks them at the FMA's batch end, never for the pilot's own MSFSBA pick.</summary>
public class IniA300EngagementBehaviourTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly HashSet<string> _muted = new();
    private readonly List<string> _sent = new();

    public IniA300EngagementBehaviourTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = key => _muted.Contains(key),
            Send = (_, rpn) => _sent.Add(rpn),
        };
        _def.Attach(_sim);
        foreach (var source in A300FmaSources.All)
            _cache[source.Key] = 0;
        Var("INI_PITCH_MODE", 8);
        Var("INI_ROLL_MODE", 3);
        Var("INI_IRS1_ATTITUDE_ALIGNED", 1);
        Var("INI_pitch_trim2", 1);
        Var("INI_ap1_on", 1);
        Var("INI_autothrottle_master_switch1", 1);
        BatchEnd();   // the baseline
    }

    private void Var(string var, double value) =>
        _cache[A300FmaSources.All.Single(s => string.Equals(s.Var, var, StringComparison.OrdinalIgnoreCase)).Key] = value;

    private void BatchEnd() => _def.OnDeferredFlushBatchDelivered(_speech);

    [Fact]
    public void Both_engagement_rows_are_in_ctrl_m_and_ride_the_fma_batch()
    {
        var vars = _def.GetVariables();
        Assert.Equal("INI_AUTOPILOT_DISCONNECT", vars[A300FmaSources.AutopilotMuteKey].Name);
        Assert.Equal("Autopilot engagement", vars[A300FmaSources.AutopilotMuteKey].DisplayName);
        Assert.False(vars[A300FmaSources.AutopilotMuteKey].ExcludeFromMonitorManager);
        Assert.Equal("INI_AUTOTHROTTLE_DISCONNECTED", vars[A300FmaSources.AutothrottleMuteKey].Name);
        Assert.Equal("Autothrottle engagement", vars[A300FmaSources.AutothrottleMuteKey].DisplayName);
        Assert.False(vars[A300FmaSources.AutothrottleMuteKey].ExcludeFromMonitorManager);
        Assert.Contains(A300FmaSources.AutopilotMuteKey, A300FmaSources.MuteKeys);
        Assert.Equal(A300FmaSources.AutopilotMuteKey, A300FmaSources.MuteKeyFor(A300FmaColumn.Autopilot));
        Assert.Equal(A300FmaSources.AutothrottleMuteKey, A300FmaSources.MuteKeyFor(A300FmaColumn.Autothrottle));
    }

    [Fact]
    public void An_autopilot_disconnect_is_spoken()
    {
        Var("INI_ap1_on", 0);
        BatchEnd();
        Assert.Equal(new[] { "Autopilot disconnected" }, _speech.All);
    }

    [Fact]
    public void Switching_it_off_from_msfsba_is_not_spoken_again()
    {
        Assert.True(_def.HandleUIVariableSet("A300_AP_SWITCH_1", 0, _def.GetVariables()["A300_AP_SWITCH_1"], _sim, _speech));
        Assert.Single(_sent);
        Var("INI_ap1_on", 0);
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void An_autothrottle_disconnect_is_spoken_and_mutable()
    {
        Var("INI_autothrottle_master_switch1", 0);
        BatchEnd();
        Assert.Equal(new[] { "Autothrottle disconnected" }, _speech.All);
        _muted.Add(A300FmaSources.AutothrottleMuteKey);
        Var("INI_autothrottle_master_switch1", 1);
        BatchEnd();
        Assert.DoesNotContain("Autothrottle armed", _speech.All);
        Assert.Contains("Thrust mode: Manual thrust", _speech.All);   // the FMA's own column, not muted
    }

    [Fact]
    public void A_flight_load_reading_is_a_baseline()
    {
        _def.OnSimContextReset();
        Var("INI_ap1_on", 0);
        BatchEnd();
        Assert.Empty(_speech.All);
    }
}
