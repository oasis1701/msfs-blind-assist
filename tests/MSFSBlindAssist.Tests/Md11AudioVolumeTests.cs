using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The audio panels' 48 receiver volume knobs (Captain, First Officer and Observer, sixteen each).
/// Each is two controls on one node: the wheel turns the LEVEL (the knob's own var, 0-10; TFDi's
/// tooltip prints it times ten as a percentage, and it is the receiver's COM volume, measured live)
/// and the click pushes the RECEIVER on or off (a second var, <c>…_VOL_BT</c>). The knob used to read
/// the push with no positions, so every volume showed as a read-only 0 or 1 that nothing could turn.
/// </summary>
public class Md11AudioVolumeTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    private static readonly string[] Seats = { "MD11_PED_CPT_AUDIO_PNL", "MD11_PED_FO_AUDIO_PNL", "MD11_OBS_AUDIO_PNL" };
    private static readonly string[] Receivers =
    {
        "VHF1", "VHF2", "VHF3", "HF1", "HF2", "SAT", "INT", "CAB", "PA",
        "ILS1", "ILS2", "VOR1", "VOR2", "ADF1", "ADF2", "MKR",
    };

    private static IEnumerable<(string Knob, string Receiver, string Lamp)> All =>
        from p in Seats from r in Receivers select ($"{p}_{r}_VOL_KB", $"{p}_{r}_VOL_BT", $"{p}_{r}_VOL_LT");

    // The walk below leaves the walker's process-wide polarity hooks alone: only Attach wires them
    // to the settings file and no test calls it, while Md11SelectorWalkerCoreTests installs its own
    // inside single tests, which a constructor here running in parallel would clobber.
    private static Md11Control Control(string id) => Map.Controls.Single(c => c.NodeId == id);

    [Fact]
    public void EveryVolumeKnob_TurnsItsOwnLevel_FromZeroToHundredPercent()
    {
        var expected = Enumerable.Range(0, 11).ToDictionary(n => n.ToString(), n => $"{n * 10}%");
        foreach (var (knob, _, _) in All)
        {
            var c = Control(knob);
            Assert.Equal(Md11Kinds.Knob, c.Kind);
            Assert.Equal(knob, c.StateVar);                        // the level, never the push
            Assert.Equal(expected, c.ValueMap);
            Assert.Equal(new[] { "WHEEL_DOWN", "WHEEL_UP" }, c.Events.Keys.Order().ToArray());
        }
    }

    [Fact]
    public void EveryVolumeKnob_HasAReceiverButton_ThatPushesIt()
    {
        foreach (var (knob, receiver, _) in All)
        {
            var k = Control(knob);
            var c = Control(receiver);
            Assert.Equal(Md11Kinds.Button, c.Kind);
            Assert.Equal(k.Label!.Replace(" Volume", " Receiver"), c.Label);
            Assert.Equal(receiver, c.StateVar);
            Assert.Equal(new[] { "LEFT_BUTTON_DOWN" }, c.Events.Keys.ToArray());
            Assert.DoesNotContain(c.Events["LEFT_BUTTON_DOWN"], k.Events.Values);   // the click moved, it is not fired twice
            // TFDi's own tooltip reads the push var for "Disabled": a proven latch (MD11-3).
            Assert.Equal(receiver, c.State?.Latch?.Var);
            Assert.Equal("On", c.State!.Latch!.On);
            Assert.Equal("Off", c.State.Latch.Off);
        }
    }

    [Fact]
    public void EveryVolumeLight_IsReadAsItsReceiversState()
    {
        // Captain and First Officer have no SAT light; every other receiver has one.
        var lamps = All.Where(x => Map.Controls.Any(c => c.NodeId == x.Lamp)).ToList();
        Assert.Equal(46, lamps.Count);
        foreach (var (_, receiver, lamp) in lamps)
        {
            Assert.Equal(new[] { (lamp, "VOL", "On") },
                         Control(receiver).State!.Lamps.Select(l => (l.Var, l.Legend, l.Lit)).ToArray());
            Assert.EndsWith(" Receiver VOL light", Control(lamp).Label);
        }
    }

    [Fact]
    public void VolumeRow_IsAWalkableCombo_NotAReadOnlyNumber()
    {
        foreach (var (knob, _, _) in All)
        {
            var d = Vars[knob];
            Assert.Equal(knob, d.Name);
            Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
            Assert.False(d.RenderAsReadOnlyStatus);
            Assert.Equal(11, d.ValueDescriptions!.Count);
            Assert.Equal("0%", d.ValueDescriptions[0]);
            Assert.Equal("100%", d.ValueDescriptions[10]);
        }
    }

    [Fact]
    public void ReceiverRow_IsAButton_ReadOnRequest_ThatWatchesItsLatchAndLight()
    {
        foreach (var (_, receiver, lamp) in All)
        {
            var d = Vars[receiver];
            Assert.Equal(receiver, d.Name);
            Assert.True(d.RenderAsButton);
            Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
            Assert.Contains(receiver, d.StateVariables!);
            if (Map.Controls.Any(c => c.NodeId == lamp)) Assert.Contains(lamp, d.StateVariables!);
        }
    }

    [Theory]
    [InlineData(1.0, 1.0, "On")]     // VOL light lit
    [InlineData(0.0, 1.0, "On")]     // light dark, push in: the latch decides (Compose's order)
    [InlineData(0.0, 0.0, "Off")]
    public void Receiver_ReadsItsLight_ThenItsLatch(double lamp, double push, string expected)
    {
        var spec = Control("MD11_PED_CPT_AUDIO_PNL_VHF2_VOL_BT").State;
        var values = new Dictionary<string, double>
        {
            ["MD11_PED_CPT_AUDIO_PNL_VHF2_VOL_LT"] = lamp,
            ["MD11_PED_CPT_AUDIO_PNL_VHF2_VOL_BT"] = push,
        };
        Assert.Equal(expected, Md11ControlState.Compose(spec, v => values.TryGetValue(v, out var x) ? x : null, powered: true));
    }

    [Theory]
    [InlineData(1.0, "On")]
    [InlineData(0.0, "Off")]
    public void ASatelliteReceiverWithNoLight_ReadsItsLatch(double push, string expected)
    {
        var spec = Control("MD11_PED_CPT_AUDIO_PNL_SAT_VOL_BT").State;
        Assert.Empty(spec!.Lamps);
        Assert.Equal(expected, Md11ControlState.Compose(spec, _ => push, powered: true));
    }

    /// <summary>
    /// The walk the combo starts: one WHEEL click per ten percent, in the direction measured live
    /// (WHEEL_UP raises the level), and nothing at all when the knob is already there. Run against a
    /// fake knob with the real control's positions and events (<see cref="Md11FakeKnob"/>).
    /// </summary>
    [Theory]
    [InlineData(0, 5, 5, "WHEEL_UP")]
    [InlineData(9, 0, 9, "WHEEL_DOWN")]
    [InlineData(10, 10, 0, null)]
    public async Task AVolumeWalk_CostsOneWheelClickPerTenPercent(int from, int to, int clicks, string? wheel)
    {
        var real = Control("MD11_PED_CPT_AUDIO_PNL_VHF2_VOL_KB");
        var knob = new Md11FakeKnob(real, level: from, step: 1, max: 10);

        Assert.True(await knob.WalkTo(to));
        Assert.Equal(to, knob.Level);
        Assert.Equal(clicks, knob.Fired.Count);
        if (wheel != null) Assert.All(knob.Fired, id => Assert.Equal(real.Events[wheel], id));
    }
}
