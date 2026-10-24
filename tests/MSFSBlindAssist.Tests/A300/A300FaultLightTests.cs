using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300's fault and warning lights: each on its panel's status box with a Ctrl+M row, speaking
/// both ways, and the lights one batch delivery changes spoken as one sentence when the batch ends.
/// </summary>
public class A300FaultLightTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly Dictionary<string, double> _cache = new();
    private long _now = 10_000;

    public A300FaultLightTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = _ => false,
        };
        _def.Attach(new SimConnectManager(IntPtr.Zero));   // never connected
    }

    private static A300Lamp Lamp(string var) => A300FaultLights.All.Single(l => l.Var == var);

    private void Deliver(A300Lamp lamp, double value) => _def.ProcessSimVarUpdate(lamp.Key, value, _speech);

    /// <summary>A batch end once the changes delivered before it have gathered (A300LampSpeech.GatherMs).</summary>
    private void BatchEnd()
    {
        _now += A300LampSpeech.GatherMs;
        _def.OnContinuousBatchDelivered(1);
    }

    /// <summary>Both buses' light power on, as the cockpit has with power up.</summary>
    private void PowerUp()
    {
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, _speech);
        _def.ProcessSimVarUpdate(A300LampBoard.DcPowerKey, 1, _speech);
    }

    [Fact]
    public void Every_light_is_distinct_named_and_on_a_real_panel()
    {
        var panels = _def.GetPanelStructure().Values.SelectMany(p => p).ToHashSet();
        var displays = _def.GetPanelDisplayVariables();
        Assert.Equal(55, A300FaultLights.All.Count);
        Assert.Equal(A300FaultLights.All.Count, A300FaultLights.All.Select(l => l.Key).Distinct().Count());
        Assert.Equal(A300FaultLights.All.Count, A300FaultLights.All.Select(l => l.Var).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(A300FaultLights.All.Count, A300FaultLights.All.Select(l => l.Name).Distinct().Count());
        foreach (var lamp in A300FaultLights.All)
        {
            Assert.StartsWith("INI_", lamp.Var);
            Assert.EndsWith(" light", lamp.Name);
            Assert.True(lamp.SpeaksOff);
            Assert.Contains(lamp.Panel, panels);
            Assert.Contains(lamp.Key, displays[lamp.Panel]);
        }
    }

    [Fact]
    public void Every_light_streams_on_its_own_subscription_with_a_ctrl_m_row()
    {
        // Not on the batch: 58 more names there pushed the FMA's pitch trim sources into a second
        // batch, and the FMA must read one complete sample (IniA300FmaBehaviourTests).
        var vars = _def.GetVariables();
        foreach (var lamp in A300FaultLights.All)
        {
            var def = vars[lamp.Key];
            Assert.False(ContinuousBatchLayout.RidesBatch(def), lamp.Var);
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced);
            Assert.True(def.ExcludeFromBatch);
            Assert.Equal(lamp.Var == "INI_AUTOLAND_LIGHT", def.HighFrequency);   // once a second is plenty for a steady light
            Assert.False(def.ExcludeFromMonitorManager);
            Assert.Equal(lamp.Name, def.DisplayName);
        }
    }

    [Fact]
    public void The_flashing_autoland_light_streams_every_change()
    {
        // It flashes about 0.6 s on, 0.6 s off (measured 2026-10-10); sampled once a second that aliases to
        // about 3 s on, 3 s off, longer than the off hold, so it would still be spoken on and off.
        var autoland = Lamp("INI_AUTOLAND_LIGHT");
        Assert.True(autoland.Flashes);
        Assert.Equal(new[] { autoland }, A300FaultLights.All.Where(l => l.Flashes));
    }

    [Fact]
    public void The_master_lights_stay_on_the_batch()
    {
        var vars = _def.GetVariables();
        Assert.True(ContinuousBatchLayout.RidesBatch(vars[A300Announcements.MasterWarningKey]));
        Assert.True(ContinuousBatchLayout.RidesBatch(vars[A300Announcements.MasterCautionKey]));
    }

    [Fact]
    public void A_light_speaks_both_ways_at_the_end_of_its_batch()
    {
        PowerUp();
        var gen = Lamp("INI_elec_gen1_fault");
        Assert.True(_def.ProcessSimVarUpdate(gen.Key, 1, _speech));   // baseline, consumed
        BatchEnd();
        Assert.Empty(_speech.All);
        Deliver(gen, 0);
        Assert.Empty(_speech.All);   // held until the batch ends
        BatchEnd();
        Assert.Empty(_speech.All);   // an off waits until the light has stayed dark (A300LampSpeech.OffHoldMs)
        _now += A300LampSpeech.OffHoldMs;
        BatchEnd();
        Deliver(gen, 1);
        BatchEnd();
        Assert.Equal(new[] { "Engine 1 generator fault light off", "Engine 1 generator fault light on" }, _speech.All);
        Assert.Empty(_speech.Interrupts);   // queued, never interrupting
    }

    [Fact]
    public void Lights_changing_together_are_one_sentence_each_way()
    {
        PowerUp();
        var lamps = new[] { Lamp("INI_SPEEDBRAKE7_FAULT"), Lamp("INI_SPEEDBRAKE6_FAULT"), Lamp("INI_SPEEDBRAKE5_FAULT"), Lamp("INI_APU_FAULT"), Lamp("INI_PACK1_FAULT") };
        foreach (var lamp in lamps)
            Deliver(lamp, lamp.Var is "INI_APU_FAULT" or "INI_PACK1_FAULT" ? 0 : 1);
        BatchEnd();
        foreach (var lamp in lamps)
            Deliver(lamp, lamp.Var is "INI_APU_FAULT" or "INI_PACK1_FAULT" ? 1 : 0);
        BatchEnd();
        _now += A300LampSpeech.OffHoldMs;
        BatchEnd();
        Assert.Equal(new[] { "2 lights on: APU fault, Pack 1 fault", "3 lights off: Spoiler 7 fault, Spoiler 6 fault, Spoiler 5 fault" }, _speech.All);
    }

    [Fact]
    public void A_muted_light_is_not_collected()
    {
        PowerUp();
        var gen = Lamp("INI_elec_gen1_fault");
        Deliver(gen, 1);
        _speech.Suppressed = true;   // MainForm's wrap around a muted row's delivery
        Deliver(gen, 0);
        _speech.Suppressed = false;
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_master_lights_still_speak_at_once_on_the_rising_edge_only()
    {
        _def.ProcessSimVarUpdate(A300Announcements.MasterCautionKey, 0, _speech);
        _def.ProcessSimVarUpdate(A300Announcements.MasterCautionKey, 1, _speech);
        Assert.Equal(new[] { "Master caution" }, _speech.All);
        _def.ProcessSimVarUpdate(A300Announcements.MasterCautionKey, 0, _speech);
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void A_context_reset_drops_what_was_waiting()
    {
        PowerUp();
        var gen = Lamp("INI_elec_gen1_fault");
        Deliver(gen, 1);
        Deliver(gen, 0);
        _def.OnSimContextReset();
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_two_light_power_flags_stream_on_their_own_subscriptions_silently_with_no_ctrl_m_row()
    {
        var vars = _def.GetVariables();
        foreach (var (key, name) in new[] { (A300LampBoard.AcPowerKey, "INI_AC_LIGHTS_FAILURE"), (A300LampBoard.DcPowerKey, "INI_DC_LIGHTS_FAILURE") })
        {
            var def = vars[key];
            Assert.Equal((name, UpdateFrequency.Continuous, true, true),
                (def.Name, def.UpdateFrequency, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(def));   // [A300-9]
            Assert.True(_def.ProcessSimVarUpdate(key, 1, _speech));
        }
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_lights_status_box_repaints_when_its_power_changes()
    {
        var vars = _def.GetVariables();
        Assert.Equal(new[] { Lamp("INI_elec_gen1_fault").Key, A300LampBoard.DcPowerKey }, vars[Lamp("INI_elec_gen1_fault").Key].StateVariables);
        Assert.Equal(new[] { Lamp("INI_PACK1_FAULT").Key, A300LampBoard.DcPowerKey }, vars[Lamp("INI_PACK1_FAULT").Key].StateVariables);
        Assert.Equal(new[] { Lamp("INI_ECAM_CLR_LIGHT").Key, A300LampBoard.AcPowerKey }, vars[Lamp("INI_ECAM_CLR_LIGHT").Key].StateVariables);
    }

    [Fact]
    public void A_lights_status_box_shows_what_the_cockpit_shows()
    {
        var gen = Lamp("INI_elec_gen1_fault");
        Assert.False(_def.TryDescribeControlState(gen.Key, out _));   // nothing read: the plain value path
        _cache[gen.Key] = 1;
        _cache[A300LampBoard.DcPowerKey] = 0;
        Assert.True(_def.TryDescribeControlState(gen.Key, out var dark));
        _cache[A300LampBoard.DcPowerKey] = 1;
        Assert.True(_def.TryDescribeControlState(gen.Key, out var lit));
        Assert.Equal(("Off", "On"), (dark, lit));
    }

    [Fact]
    public void Power_coming_on_speaks_the_lights_that_light_and_a_fault_clearing_on_a_dark_bus_stays_silent()
    {
        var standby = Lamp("INI_elec_standby_gen_fault");
        var regulator = Lamp("INI_cabin_sys1_regulator_fault");
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 0, _speech);   // batteries only: AC lights dark
        Deliver(standby, 1);
        Deliver(regulator, 1);
        BatchEnd();
        Deliver(regulator, 0);   // its fault clears while the light is dark
        BatchEnd();
        Assert.Empty(_speech.All);
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, _speech);   // external power
        BatchEnd();
        Assert.Empty(_speech.All);   // still settling
        _now += A300LampSpeech.SettleMs;
        BatchEnd();
        Assert.Equal(new[] { "Standby generator fault light on" }, _speech.All);
    }

    [Fact]
    public void A_fault_clearing_just_after_its_power_comes_on_says_nothing()
    {
        // Measured on external power, 2026-10-09: the cabin regulator fault cleared in the cockpit 500 ms
        // before the AC light power rose, but MSFSBA heard the power first and the clear a second later,
        // and said "Cabin regulator 2 fault light on", then "off".
        var standby = Lamp("INI_elec_standby_gen_fault");
        var regulator = Lamp("INI_cabin_sys2_regulator_fault");
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 0, _speech);
        Deliver(standby, 1);
        Deliver(regulator, 1);
        BatchEnd();
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, _speech);
        BatchEnd();
        _now += 1000;
        Deliver(regulator, 0);
        BatchEnd();
        _now += A300LampSpeech.SettleMs;
        BatchEnd();
        Assert.Equal(new[] { "Standby generator fault light on" }, _speech.All);
    }

    [Fact]
    public void A_light_that_blinks_inside_one_batch_says_on_then_off_once_it_has_stayed_dark()
    {
        // The iFly 737's flash filter (design decision, 2026-10-10): "on" at once, "off" only after the hold.
        PowerUp();
        var pack = Lamp("INI_PACK1_FAULT");
        Deliver(pack, 0);
        BatchEnd();
        Deliver(pack, 1);
        Deliver(pack, 0);
        BatchEnd();
        Assert.Equal(new[] { "Pack 1 fault light on" }, _speech.All);
        _now += A300LampSpeech.OffHoldMs;
        BatchEnd();
        Assert.Equal(new[] { "Pack 1 fault light on", "Pack 1 fault light off" }, _speech.All);
    }

    [Fact]
    public void Power_going_off_waits_too_and_speaks_what_went_dark()
    {
        PowerUp();
        var pack = Lamp("INI_PACK1_FAULT");
        Deliver(pack, 1);
        BatchEnd();
        _now += A300LampSpeech.SettleMs;
        _def.ProcessSimVarUpdate(A300LampBoard.DcPowerKey, 0, _speech);
        BatchEnd();
        Assert.Empty(_speech.All);
        _now += A300LampSpeech.SettleMs;
        BatchEnd();
        Assert.Equal(new[] { "Pack 1 fault light off" }, _speech.All);
    }

    [Fact]
    public void A_light_lit_by_a_power_change_still_honours_its_own_ctrl_m_row()
    {
        var standby = Lamp("INI_elec_standby_gen_fault");
        _def.IsMuted = key => key == standby.Key;
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 0, _speech);
        Deliver(standby, 1);
        BatchEnd();
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, _speech);
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_light_reads_on_or_off_in_its_status_box()
    {
        var vars = _def.GetVariables();
        var gen = Lamp("INI_elec_gen1_fault");
        Assert.Equal("On", vars[gen.Key].ValueDescriptions[1]);
        Assert.Equal("Off", vars[gen.Key].ValueDescriptions[0]);
    }

    [Fact]
    public void One_change_reads_as_its_own_phrase() =>
        Assert.Equal("Engine 1 generator fault light off",
            A300LampCallouts.Compose(new[] { new A300LampChange("Engine 1 generator fault light", false) }));

    [Fact]
    public void Two_changes_the_same_way_are_counted()
    {
        var text = A300LampCallouts.Compose(new[]
        {
            new A300LampChange("Pack 1 fault light", true),
            new A300LampChange("Pack 2 fault light", true),
        });
        Assert.Equal("2 lights on: Pack 1 fault, Pack 2 fault", text);
    }

    [Fact]
    public void Lights_coming_on_are_read_first()
    {
        var text = A300LampCallouts.Compose(new[]
        {
            new A300LampChange("Pack 1 fault light", false),
            new A300LampChange("Engine 1 fire handle light", true),
        });
        Assert.Equal("Engine 1 fire handle light on. Pack 1 fault light off", text);
    }
}
