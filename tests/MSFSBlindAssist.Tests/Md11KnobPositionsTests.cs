using System.Globalization;
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
public class Md11KnobPositionsTests
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

    // The walks below leave the walker's process-wide polarity hooks alone: only Attach wires them
    // to the settings file and no test calls it, while Md11SelectorWalkerCoreTests installs its own
    // inside single tests, which a constructor here running in parallel would clobber.
    private static Md11Control Control(string id) => Map.Controls.Single(c => c.NodeId == id);

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

    /// <summary>
    /// The same positions and words under a comma-decimal culture. These are the MD-11's first
    /// fractional keys ("0.5", "2.5", "3.6", "39.6"); parsed by the PC's culture, de-DE would read
    /// "4.5" as 45 and collapse the knob's 21 positions (CORE-17). CI runs under en-US, so only a test
    /// that sets the culture can catch it. A fresh definition, so its parse runs under that culture.
    /// </summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void PercentKnobPositions_AreTheSameUnderACommaDecimalCulture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var vars = new TFDiMD11Definition().GetVariables();
            foreach (var (node, full) in new[] { ("MD11_PED_DU1_BRT_KB", 10.0), ("MD11_LECP_WXBRT_KB", 72.0), ("MD11_LMCDU_BRT_KB", 50.0) })
            {
                var ordered = Md11SelectorWalker.OrderedValues(Control(node));
                var words = vars[node].ValueDescriptions!;
                Assert.Equal(21, ordered.Count);
                Assert.Equal(21, words.Count);
                for (int i = 0; i <= 20; i++)
                {
                    Assert.Equal(full * i / 20, ordered[i], 6);
                    Assert.Equal($"{i * 5}%", words[ordered[i]]);
                }
            }
            Assert.Equal("55%", vars["MD11_LECP_WXBRT_KB"].ValueDescriptions![vars["MD11_LECP_WXBRT_KB"].DescriptionKeyFor(39.60001)]);
            Assert.Equal("45%", vars["MD11_PED_DU1_BRT_KB"].ValueDescriptions![vars["MD11_PED_DU1_BRT_KB"].DescriptionKeyFor(4.4)]);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    /// <summary>
    /// A percent knob is the generator's <c>continuous</c> flag, never its words: percentage words
    /// without the flag (a detented knob) keep exact keys, and the flag survives curated words.
    /// </summary>
    [Fact]
    public void APercentKnob_IsTheGeneratorsFlag_NotItsWords()
    {
        var detented = new Md11Control
        {
            NodeId = "TEST_DETENTED", Kind = Md11Kinds.Knob, StateVar = "TEST_DETENTED",
            ValueMap = new Dictionary<string, string> { ["0"] = "0%", ["50"] = "50%", ["100"] = "100%" },
        };
        var curated = new Md11Control
        {
            NodeId = "TEST_CURATED", Kind = Md11Kinds.Knob, StateVar = "TEST_CURATED", Continuous = true,
            ValueMap = new Dictionary<string, string> { ["0"] = "Off", ["5"] = "50%", ["10"] = "Max" },
        };

        Assert.False(Md11PercentKnob.Is(detented));
        Assert.True(Md11PercentKnob.Is(curated));
        Assert.Equal(68, Map.Controls.Count(c => c.Continuous));   // 48 volumes, 20 lighting and brightness
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
        var knob = new Md11FakeKnob(Control("MD11_PED_DU1_BRT_KB"), level: 4.0, step: 0.5, max: 10);   // 40 %, as DU1 was found

        Assert.True(await knob.WalkTo(7.5));   // 75 %
        Assert.Equal(7.5, knob.Level);
        Assert.Equal(7, knob.Fired.Count);
        Assert.All(knob.Fired, id => Assert.Equal(knob.Up, id));
    }

    /// <summary>
    /// A 0 → 100 % walk is twenty clicks, and one dropped click mid-range ends it where it stands —
    /// the walker never probes away from an end stop. The knob MOVED, so the failure says where it
    /// stopped, never "did not move" (SafeWalk, through Md11WalkFailure.AfterReadBack).
    /// </summary>
    [Fact]
    public async Task ADroppedClickMidWalk_StopsPartway_AndSaysWhere()
    {
        var knob = new Md11FakeKnob(Control("MD11_PED_DU1_BRT_KB"), level: 0, step: 0.5, max: 10) { DropClick = 13 };

        Assert.False(await knob.WalkTo(10));
        Assert.Equal(6.0, knob.Level);        // twelve clicks landed: 60 %
        Assert.Equal(13, knob.Fired.Count);   // and no retry after the dropped one

        var d = Vars["MD11_PED_DU1_BRT_KB"];
        Assert.Equal("DU1 Brightness stopped at 60%, short of 100%.",
            Md11WalkFailure.AfterReadBack("DU1 Brightness", d.DescriptionKeyFor(0), d.DescriptionKeyFor(knob.Level),
                d.DescriptionKeyFor(10), k => d.ValueDescriptions!.TryGetValue(k, out var w) ? w : null));
    }

    /// <summary>
    /// The walk's step budget grows with the control: a full-range walk plus a re-learned polarity
    /// and the landing read must fit with room to spare on every walkable control, the 21-position
    /// percent knobs included (the fixed 24 left them two steps).
    /// </summary>
    [Fact]
    public void EveryWalkableControl_HasAStepBudgetWellPastItsFullRange()
    {
        foreach (var c in Map.Controls.Where(c => Md11ExportBacked.IsPositional(c.Kind)))
        {
            var positions = Md11SelectorWalker.OrderedValues(c).Count;
            Assert.True(Md11SelectorWalker.StepBudget(positions) >= 2 * positions, c.NodeId);
        }
        Assert.Equal(42, Md11SelectorWalker.StepBudget(21));
        Assert.Equal(24, Md11SelectorWalker.StepBudget(3));      // a small control keeps the old budget
    }
}
