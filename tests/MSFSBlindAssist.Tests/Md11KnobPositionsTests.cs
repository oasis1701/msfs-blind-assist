using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The lighting and brightness knobs and the flight-number thumbwheels, which were read-only raw
/// numbers because TFDi's tooltips name no positions. Each lighting knob's tooltip prints its own
/// var scaled to a percentage, and every family measured live steps 5 % of that scale per click
/// (generator: <c>percent_knob_positions</c>); the flight-number digits step 0-9. A knob reading
/// between positions selects the nearest one, never nothing.
/// </summary>
public class Md11KnobPositionsTests : IDisposable
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    /// <summary>Each percent knob and the raw value at 100 % (the tooltip's scale).</summary>
    public static TheoryData<string, double> PercentKnobs => new()
    {
        { "MD11_OBS_CKTBKR_LT_KB", 10 },
        { "MD11_CGS_FLOOD_LT_KB", 10 }, { "MD11_CGS_PNL_LT_KB", 10 },
        { "MD11_OVHD_LTS_INNER_INSTR_PED_PNL_FLOOD_KB", 10 }, { "MD11_OVHD_LTS_INNER_OVHD_PNL_FLOOD_KB", 10 },
        { "MD11_OVHD_LTS_OUTER_INSTR_PED_PNL_FLOOD_KB", 10 }, { "MD11_OVHD_LTS_OUTER_OVHD_PNL_FLOOD_KB", 10 },
        { "MD11_PED_DU1_BRT_KB", 10 }, { "MD11_PED_DU2_BRT_KB", 10 }, { "MD11_PED_DU3_BRT_KB", 10 },
        { "MD11_PED_DU4_BRT_KB", 10 }, { "MD11_PED_DU5_BRT_KB", 10 }, { "MD11_PED_DU6_BRT_KB", 10 },
        { "MD11_LSIDE_BRIEFCASE_KB", 20 }, { "MD11_RSIDE_BRIEFCASE_KB", 20 },
        { "MD11_LECP_WXBRT_KB", 72 }, { "MD11_RECP_WXBRT_KB", 72 },
        { "MD11_LMCDU_BRT_KB", 50 }, { "MD11_CMCDU_BRT_KB", 50 }, { "MD11_RMCDU_BRT_KB", 50 },
    };

    private static Md11Control Control(string id) => Map.Controls.Single(c => c.NodeId == id);

    // The walk below goes through the walker's process-wide polarity hooks: pin them so nothing can
    // persist a test id into the developer's settings (Md11SelectorWalkerCoreTests does the same).
    public Md11KnobPositionsTests()
    {
        Md11SelectorWalker.LoadPolarity = _ => null;
        Md11SelectorWalker.SavePolarity = (_, _) => { };
    }

    public void Dispose()
    {
        Md11SelectorWalker.LoadPolarity = null;
        Md11SelectorWalker.SavePolarity = null;
    }

    [Theory]
    [MemberData(nameof(PercentKnobs))]
    public void PercentKnob_HasTwentyOneFivePercentSteps_OverItsOwnScale(string node, double full)
    {
        var c = Control(node);
        Assert.Equal(node, c.StateVar);
        var ordered = Md11SelectorWalker.OrderedValues(c);
        Assert.Equal(21, ordered.Count);
        var words = Vars[node].ValueDescriptions!;
        for (int i = 0; i <= 20; i++)
        {
            Assert.Equal(full * i / 20, ordered[i], 6);
            Assert.Equal($"{i * 5}%", words[ordered[i]]);
        }
    }

    [Theory]
    [MemberData(nameof(PercentKnobs))]
    public void PercentKnob_IsAWalkableCombo(string node, double full)
    {
        var d = Vars[node];
        Assert.Equal(node, d.Name);
        Assert.False(d.RenderAsReadOnlyStatus);
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.Equal("100%", d.ValueDescriptions![full]);
    }

    /// <summary>
    /// The weather radar brightness steps 3.6 and was read live at 39.60001. Looked up exactly, that
    /// selected nothing and the first Down-arrow committed 0 %; it must select 55 %.
    /// </summary>
    [Theory]
    [InlineData("MD11_LECP_WXBRT_KB", 39.60001, 39.6)]
    [InlineData("MD11_LECP_WXBRT_KB", 36.0, 36.0)]
    [InlineData("MD11_PED_DU1_BRT_KB", 4.4, 4.5)]      // a mouse drag between steps
    [InlineData("MD11_PED_DU1_BRT_KB", 4.0, 4.0)]
    [InlineData("MD11_PED_DU1_BRT_KB", 12.0, 10.0)]    // past the scale: the end position
    public void AKnobBetweenPositions_SelectsTheNearest(string node, double reading, double key)
    {
        var d = Vars[node];
        Assert.Equal(key, d.DescriptionKeyFor(reading), 6);
        Assert.True(d.ValueDescriptions!.ContainsKey(d.DescriptionKeyFor(reading)));
    }

    [Fact]
    public void APercentKnobOnAPosition_StillMapsToItself()
    {
        foreach (var c in Map.Controls.Where(Md11PercentKnob.Is))
        {
            var d = Vars[c.NodeId];
            Assert.NotNull(d.ValueToDescriptionKey);
            foreach (var key in d.ValueDescriptions!.Keys)
                Assert.Equal(key, d.DescriptionKeyFor(key));
        }
        // A detented knob keeps no classifier: its value IS its key (Md11ReadoutTests).
        Assert.False(Md11PercentKnob.Is(Control("MD11_OVHD_PNEU_COCKPIT_TEMP")));
        Assert.Null(Vars["MD11_OVHD_PNEU_COCKPIT_TEMP"].ValueToDescriptionKey);
    }

    [Theory]
    [InlineData("MD11_CTR_FLTNO1_SW")]
    [InlineData("MD11_CTR_FLTNO2_SW")]
    [InlineData("MD11_CTR_FLTNO3_SW")]
    [InlineData("MD11_CTR_FLTNO4_SW")]
    public void FlightNumberDigit_IsAComboFromZeroToNine(string node)
    {
        var d = Vars[node];
        Assert.False(d.RenderAsReadOnlyStatus);
        Assert.Equal(Enumerable.Range(0, 10).Select(n => (double)n), d.ValueDescriptions!.Keys.Order());
        Assert.Equal("7", d.ValueDescriptions[7]);
    }

    /// <summary>
    /// The three passenger-load thumbwheels share one tooltip that reads all three digits, so the Tens
    /// and Units rows read the HUNDREDS var. Each reads its own; they stay read-only digits, because
    /// the selector did not move on the freighter it was probed on.
    /// </summary>
    [Theory]
    [InlineData("MD11_OVHD_1_PAX_LOAD_SW")]
    [InlineData("MD11_OVHD_10_PAX_LOAD_SW")]
    [InlineData("MD11_OVHD_100_PAX_LOAD_SW")]
    public void PassengerLoadWheel_ReadsItsOwnDigit(string node)
    {
        Assert.Equal(node, Control(node).StateVar);
        Assert.Equal(node, Vars[node].Name);
        Assert.True(Vars[node].RenderAsReadOnlyStatus);
    }

    /// <summary>A brightness walk: one wheel click per 5 %, 0.5 raw each on a x10 knob.</summary>
    [Fact]
    public async Task ABrightnessWalk_CostsOneWheelClickPerFivePercent()
    {
        var real = Control("MD11_PED_DU1_BRT_KB");
        var knob = new Md11Control
        {
            NodeId = "TEST_" + Guid.NewGuid().ToString("N"),
            Kind = real.Kind,
            StateVar = real.StateVar,
            ValueMap = real.ValueMap,
            Events = real.Events,
        };
        int up = real.Events["WHEEL_UP"], down = real.Events["WHEEL_DOWN"];
        double level = 4.0;      // 40 %, as DU1 was found
        long clock = 0;
        var fired = new List<int>();
        var io = new Md11WalkIo
        {
            ReadFresh = _ => Task.FromResult<double?>(level),
            ReadCached = () => level,
            RequestRead = () => { },
            Fire = id => { fired.Add(id); level = Math.Clamp(level + (id == up ? 0.5 : id == down ? -0.5 : 0), 0, 10); },
            Pending = () => 0,
            Delay = (ms, _) => { clock += ms; return Task.CompletedTask; },
            Now = () => clock,
            FreshReads = true,
        };

        Assert.True(await Md11SelectorWalker.WalkCoreAsync(knob, 7.5, io));   // 75 %
        Assert.Equal(7.5, level);
        Assert.Equal(7, fired.Count);
        Assert.All(fired, id => Assert.Equal(up, id));
    }
}
