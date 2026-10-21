using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.A300;

/// <summary>
/// One voice, per control (the MD-11's model, design decision 2026-10-10): what a First Officer write causes on
/// its own row (its lights, its lever call-out, a "did not move" read-back) is recorded, not spoken, for the row's
/// window; the flow's own narration says what happened. Every other light, and the master lights, still speak.
/// Only a system test's run quiets every light.
/// </summary>
public class A300FoQuietWindowTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private long _now = 10_000;

    public A300FoQuietWindowTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = _ => false,
            CanLand = _ => true,
            Send = (_, rpn) => _sent.Add(rpn),
            TypedDelay = _ => Task.CompletedTask,
            ReadFresh = (_, key, _) => Task.FromResult(_cache.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
        _def.ProcessSimVarUpdate(A300LampBoard.AcPowerKey, 1, _speech);
        _def.ProcessSimVarUpdate(A300LampBoard.DcPowerKey, 1, _speech);
    }

    private static A300Lamp Gen1Fault => A300FaultLights.All.Single(l => l.Var == "INI_elec_gen1_fault");

    private void BatchEnd()
    {
        _now += A300LampSpeech.GatherMs;
        _def.OnContinuousBatchDelivered(1);
    }

    private const string Pump = "A300_OUT_TK_PMP_L1";
    private const string PumpLight = "A300_LT_OUT_TK_PMP_L1_SEQ1_LIGHT";

    /// <summary>Delivers a board light's inputs so the light reads lit or dark (A300FoTestLights).</summary>
    private void Light(string lampId, bool on)
    {
        var values = new Dictionary<string, double>();
        A300FoTestLights.Set(values, lampId, on);
        foreach (var (key, value) in values)
            _def.ProcessSimVarUpdate(key, value, _speech);
    }

    [Fact]
    public void The_moved_rows_own_light_is_not_spoken_and_another_light_is()
    {
        Light(PumpLight, false);
        _def.ProcessSimVarUpdate(Gen1Fault.Key, 0, _speech);
        BatchEnd();
        _cache[Pump] = 0;
        Assert.True(_def.FoApply(Pump, 1, _sim, _speech));
        Light(PumpLight, true);
        _def.ProcessSimVarUpdate(Gen1Fault.Key, 1, _speech);
        BatchEnd();
        Assert.Equal(new[] { "Engine 1 generator fault light on" }, _speech.All);
    }

    [Fact]
    public void The_moved_rows_own_light_speaks_again_after_its_window()
    {
        Light(PumpLight, false);
        BatchEnd();
        _cache[Pump] = 0;
        Assert.True(_def.FoApply(Pump, 1, _sim, _speech));
        _now += IniA300Definition.FoQuietMs + 1;
        Light(PumpLight, true);
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void A_master_warning_after_a_write_is_spoken()
    {
        _def.ProcessSimVarUpdate(A300Announcements.MasterWarningKey, 0, _speech);
        _cache["A300_PACK_1_VALVE"] = 0;
        Assert.True(_def.FoApply("A300_PACK_1_VALVE", 1, _sim, _speech));
        _def.ProcessSimVarUpdate(A300Announcements.MasterWarningKey, 1, _speech);
        Assert.Contains("Master warning", _speech.All);
    }

    [Fact]
    public void A_test_quiets_every_light_and_the_master_lights_for_its_run()
    {
        _def.ProcessSimVarUpdate(A300Announcements.MasterWarningKey, 0, _speech);
        _def.ProcessSimVarUpdate(Gen1Fault.Key, 0, _speech);
        BatchEnd();
        _def.QuietAllLightsFor(10_000);
        _def.ProcessSimVarUpdate(A300Announcements.MasterWarningKey, 1, _speech);
        _def.ProcessSimVarUpdate(Gen1Fault.Key, 1, _speech);
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_lever_the_fo_moved_is_not_called_out_and_another_lever_is()
    {
        _def.ProcessSimVarUpdate(A300Announcements.ParkingBrakeKey, 0, _speech);
        _def.ProcessSimVarUpdate(A300Levers.SpoilersArmKey, 0, _speech);
        _cache[A300Announcements.ParkingBrakeKey] = 0;
        Assert.True(_def.FoApply(A300Announcements.ParkingBrakeKey, 1, _sim, _speech));
        _def.ProcessSimVarUpdate(A300Announcements.ParkingBrakeKey, 1, _speech);
        _def.ProcessSimVarUpdate(A300Levers.SpoilersArmKey, 1, _speech);
        Assert.Equal(new[] { "Ground spoilers armed" }, _speech.All);
    }

    [Fact]
    public void FoApply_writes_a_row_through_its_own_panel_path_and_never_says_did_not_move()
    {
        _cache["A300_PACK_1_VALVE"] = 0;   // known off; the read-back will find it still off
        Assert.True(_def.FoApply("A300_PACK_1_VALVE", 1, _sim, _speech));
        Assert.Single(_sent);
        Assert.Contains("AIRLINER_PACK_1_VALVE_Set", _sent[0]);
        Assert.DoesNotContain(_speech.All, s => s.Contains("did not move"));
        Assert.False(_speech.Suppressed);   // put back as it was
    }

    [Fact]
    public void The_pilots_own_write_that_does_not_move_still_says_so()
    {
        // The control for the test above: the same read-back speaks for a pick that is not the First Officer's.
        _cache["A300_PACK_2_VALVE"] = 0;
        Assert.True(_def.HandleUIVariableSet("A300_PACK_2_VALVE", 1, _def.GetVariables()["A300_PACK_2_VALVE"], _sim, _speech));
        Assert.Contains(_speech.All, s => s.EndsWith("did not move"));
    }

    [Fact]
    public void FoApply_refuses_a_key_that_is_not_a_row_and_writes_nothing()
    {
        Assert.False(_def.FoApply("A300_LT_APU_AVAIL_SEQ1_LIGHT", 1, _sim, _speech));
        Assert.Empty(_sent);
    }
}
