using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// How the A300 definition reads its flight mode annunciator: every source rides the continuous
/// batches once, the deliveries are consumed silently, and the changes are spoken when the FMA's
/// batch has finished dispatching, so a mode change that moves several variables is read once,
/// complete.
/// </summary>
public class IniA300FmaBehaviourTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly HashSet<string> _muted = new();

    public IniA300FmaBehaviourTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = key => _muted.Contains(key),
        };
        _def.Attach(_sim);
    }

    /// <summary>A powered, aligned aircraft in SPD / V/S / HDG with autopilot 1 on.</summary>
    private void Flying()
    {
        foreach (var source in A300FmaSources.All)
            _cache[source.Key] = 0;
        Set("INI_PITCH_MODE", 8);
        Set("INI_ROLL_MODE", 3);
        Set("INI_at_mode", 1);
        Set("INI_AT_ON", 1);
        Set("INI_autothrottle_master_switch1", 1);
        Set("INI_IRS1_ATTITUDE_ALIGNED", 1);
        Set("INI_pitch_trim2", 1);
        Set("INI_ap1_on", 1);
    }

    private void Set(string var, double value) =>
        _cache[A300FmaSources.All.Single(s => string.Equals(s.Var, var, StringComparison.OrdinalIgnoreCase)).Key] = value;

    /// <summary>MainForm calls this when the batch carrying <see cref="IniA300Definition.DeferredFlushWatchVariable"/>
    /// has finished dispatching.</summary>
    private void BatchEnd() => _def.OnDeferredFlushBatchDelivered(_speech);

    [Fact]
    public void Every_source_is_registered_under_its_key_and_rides_the_batch()
    {
        var vars = _def.GetVariables();
        foreach (var source in A300FmaSources.All)
        {
            Assert.True(vars.TryGetValue(source.Key, out var def), $"{source.Key} is not registered");
            Assert.Equal(source.Var, def!.Name, ignoreCase: true);
            Assert.True(ContinuousBatchLayout.RidesBatch(def), $"{source.Key} does not ride the batch");
        }
    }

    [Fact]
    public void Every_source_shares_one_batch()
    {
        // The FMA is composed when ITS batch has finished dispatching; a source in another batch
        // would be one delivery stale.
        var batches = ContinuousBatchLayout.BatchNumbers(_def.GetVariables());
        Assert.Single(A300FmaSources.All.Select(s => batches[s.Key]).Distinct());
    }

    [Fact]
    public void No_variable_rides_the_batch_twice()
    {
        var names = ContinuousBatchLayout.Order(_def.GetVariables()).Select(kv => ContinuousBatchLayout.FullName(kv.Value).ToUpperInvariant());
        Assert.Equal(names.Count(), names.Distinct().Count());
    }

    [Fact]
    public void Each_column_has_one_monitor_row_named_for_it()
    {
        var rows = MonitorRowBuilder.Build(_def.GetVariables()).ToDictionary(r => r.Key, r => r.Label);
        Assert.Equal("Thrust mode", rows[A300FmaSources.ThrustModeKey]);
        Assert.Equal("Pitch mode", rows[A300FmaSources.PitchModeKey]);
        Assert.Equal("Roll mode", rows[A300FmaSources.RollModeKey]);
        Assert.Equal("Land, flare, rollout and go-around", rows[A300FmaSources.CommonModeKey]);
        Assert.Equal("Armed modes", rows[A300FmaSources.ArmedKey]);
        // The pitch trim levers are FMA sources too, and carry their own row: they speak when they drop.
        var other = A300FmaSources.All.Select(s => s.Key).Except(A300FmaSources.MuteKeys)
            .Except(A300Announcements.SasLevers.Keys).ToHashSet();
        Assert.DoesNotContain(rows.Keys, other.Contains);
    }

    [Theory]
    [InlineData(A300FmaSources.PitchModeKey)]
    [InlineData("A300_FMA_IRS1_ALIGNED")]
    [InlineData("A300_AP_SWITCH_1")]
    public void A_source_delivery_is_consumed_silently(string key)
    {
        Assert.True(_def.ProcessSimVarUpdate(key, 1, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_mode_change_spoken_together_with_its_thrust_mode()
    {
        Flying();
        BatchEnd();
        Set("INI_PITCH_MODE", 22);   // ALT* in profile: the speed word follows the pitch mode
        BatchEnd();
        Assert.Equal(new[] { "Thrust mode: Profile speed", "Pitch mode: Altitude capture" }, _speech.All);
    }

    [Fact]
    public void A_mode_change_is_spoken_at_the_end_of_the_fma_batch()
    {
        Flying();
        BatchEnd();
        Assert.Empty(_speech.All);   // the baseline

        Set("INI_PITCH_MODE", 6);
        BatchEnd();
        Assert.Equal(new[] { "Pitch mode: Altitude" }, _speech.All);
    }

    [Fact]
    public void The_fma_is_read_when_its_own_batch_ends()
    {
        // MainForm releases OnDeferredFlushBatchDelivered only for the batch carrying this variable.
        Assert.Equal(A300FmaSources.PitchModeKey, _def.DeferredFlushWatchVariable);
    }

    [Fact]
    public void Any_batch_ending_does_not_read_the_fma_by_itself()
    {
        Flying();
        BatchEnd();
        Set("INI_PITCH_MODE", 6);
        _def.OnContinuousBatchDelivered(1);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_muted_column_says_nothing_and_the_others_still_speak()
    {
        Flying();
        BatchEnd();
        _muted.Add(A300FmaSources.PitchModeKey);
        Set("INI_PITCH_MODE", 6);
        Set("INI_ROLL_MODE", 1);
        BatchEnd();
        Assert.Equal(new[] { "Roll mode: NAV" }, _speech.All);
    }

    [Fact]
    public void Armed_modes_speak_under_their_own_row()
    {
        Flying();
        BatchEnd();
        Set("INI_PITCH_MODE_ARM", 6);
        BatchEnd();
        Assert.Equal(new[] { "Altitude armed" }, _speech.All);
        _muted.Add(A300FmaSources.ArmedKey);
        Set("INI_ROLL_MODE_ARM", 5);
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void A_context_reset_makes_the_next_reading_a_baseline()
    {
        Flying();
        BatchEnd();
        _def.OnSimContextReset();
        Set("INI_PITCH_MODE", 6);
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void An_unpowered_aircraft_says_nothing()
    {
        BatchEnd();
        Set("INI_PITCH_MODE", 6);
        BatchEnd();
        Assert.Empty(_speech.All);
    }
}
