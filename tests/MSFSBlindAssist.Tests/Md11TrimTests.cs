using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's rudder and aileron trims: their rows read TFDi's own tooltip readout
/// (<see cref="Md11TooltipReadout"/>) instead of a bare "25", and four rows of this app's own turn
/// them (<see cref="Md11TrimButtons"/>). The readout is TFDi's markup from the shipped map, so these
/// tests also pin that the regenerated map still carries it. The presses themselves need a live bus
/// and are verified in the sim.
/// </summary>
public class Md11TrimTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();

    private const string Rudder = "MD11_PED_RUD_TRIM_SW";
    private const string Aileron = "MD11_PED_AIL_TRIM_KB";

    private static Md11Control Control(string nodeId)
        => Map.Controls.Single(c => string.Equals(c.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

    private static string? Read(string nodeId, double value)
    {
        var c = Control(nodeId);
        return Md11TooltipReadout.Evaluate(c.Readout,
            name => string.Equals(name, c.StateVar, StringComparison.OrdinalIgnoreCase) ? value : null);
    }

    // ---- the readout, over the shipped map's own markup ---------------------------------

    [Theory]
    [InlineData(Rudder, 25.0, "Neutral")]
    [InlineData(Rudder, 24.95, "Neutral")]            // TFDi's own band: within 0.1 of 25
    [InlineData(Rudder, 26.27, "0.5 degrees Nose Right")]
    [InlineData(Rudder, 22.0, "1.2 degrees Nose Left")]
    [InlineData(Aileron, 26.0, "0.4 degrees Right Wing Down")]
    [InlineData(Aileron, 24.0, "0.4 degrees Left Wing Down")]
    [InlineData(Aileron, 0.0, "10.0 degrees Left Wing Down")]
    public void TheTrimRows_ReadTfdisTooltipWords(string nodeId, double value, string expected)
        => Assert.Equal(expected, Read(nodeId, value));

    [Fact]
    public void TheReadout_NeverTakesTheComputersDecimalComma()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("1.2 degrees Nose Left", Read(Rudder, 22.0));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void AVarNotYetDelivered_ReadsNothing_NeverAZero()
        => Assert.Null(Md11TooltipReadout.Evaluate(Control(Rudder).Readout, _ => null));

    [Theory]
    [InlineData("%((L:X) 5 -)%!d! degrees UP")]          // the integer format: rounding unmeasured
    [InlineData("%((L:X))%{case}%{:0}Off%{:1}On%{end}")] // a case block
    [InlineData("%((L:X) 2 pow)%!1.1f!")]                 // an operator it does not know
    [InlineData("%((L:X) 2 +%!1.1f!")]                    // unterminated
    [InlineData("%((L:X))%{if}On")]                       // unterminated branch
    public void AShapeItDoesNotKnow_IsRefused_NeverGuessed(string readout)
    {
        Assert.Null(Md11TooltipReadout.Evaluate(readout, _ => 3));
        Assert.False(Md11TooltipReadout.CanEvaluate(readout));
    }

    [Fact]
    public void TheRpn_ReadsLikeTheSims_SecondFromTopFirst()
    {
        Assert.Equal("4.0", Md11TooltipReadout.Evaluate("%(25 (L:X) - 2.5 /)%!1.1f!", _ => 15));
        Assert.Equal("yes", Md11TooltipReadout.Evaluate("%(1 7 (L:X) rng)%{if}yes%{else}no%{end}", _ => 7));
        Assert.Equal("50%", Md11TooltipReadout.Evaluate("%((L:X) 10 *)%!0.0f!%%", _ => 5));
    }

    [Fact]
    public void Vars_NameEveryReadOnce()
        => Assert.Equal(new[] { "MD11_PED_RUD_TRIM_IND" }, Md11TooltipReadout.Vars(Control(Rudder).Readout));

    [Fact]
    public void OnlyTheTwoTrims_HaveAnEvaluableReadout()
    {
        // The weather radar tilt carries one too, in the integer format the evaluator refuses, so its
        // row stays as it was.
        var evaluable = Map.Controls.Where(c => Md11TooltipReadout.CanEvaluate(c.Readout)).Select(c => c.NodeId).OrderBy(n => n);
        Assert.Equal(new[] { Aileron, Rudder }, evaluable);
    }

    // ---- the rows ------------------------------------------------------------------------

    [Theory]
    [InlineData(Rudder)]
    [InlineData(Aileron)]
    public void TheTrimRow_StaysReadOnly_AndWatchesItsOwnVar(string nodeId)
    {
        var d = new TFDiMD11Definition().GetVariables()[nodeId];
        Assert.True(d.RenderAsReadOnlyStatus);
        Assert.Equal(new[] { nodeId }, d.StateVariables);
    }

    [Fact]
    public void TheWeatherRadarTiltRow_IsUnchanged()
        => Assert.Null(new TFDiMD11Definition().GetVariables()["MD11_PED_WXR_TILT_KB"].StateVariables);

    // ---- the buttons ---------------------------------------------------------------------

    private const string LongTrim = "MD11_THR_LONG_TRIM_SW";

    private static List<Md11TrimButton> ButtonsOf(string nodeId)
        => Md11TrimButtons.From(Map.Controls).Where(b => b.Control.NodeId == nodeId).ToList();

    [Fact]
    public void TheRudderTrim_IsHeld_AndReleasedAsItsTemplateReleasesIt()
    {
        var buttons = ButtonsOf(Rudder);
        Assert.Equal(new[] { "Left", "Right" }, buttons.Select(b => b.Word));
        Assert.All(buttons, b => Assert.True(b.IsHold));
        // TFDi_Design_MD11_3Pos_Knob_Hold's REL_CODE: LEFT_BUTTON_UP whichever way it was held.
        Assert.All(buttons, b => Assert.Equal(new[] { "LEFT_BUTTON_UP" }, b.UpEvents));
        Assert.Equal(new[] { "LEFT_BUTTON_DOWN", "RIGHT_BUTTON_DOWN" }, buttons.Select(b => b.DownEvent));
    }

    [Fact]
    public void TheAileronTrim_IsOneWheelClickPerPress_DownIsLeft()
    {
        var buttons = ButtonsOf(Aileron);
        Assert.Equal(new[] { "Left", "Right" }, buttons.Select(b => b.Word));
        Assert.All(buttons, b => Assert.False(b.IsHold));
        Assert.Equal(new[] { "WHEEL_DOWN", "WHEEL_UP" }, buttons.Select(b => b.DownEvent));
    }

    [Fact]
    public void LongTrim_IsHeldBothWays_NamedByTheMeasuredDirections_ReleasedWithBothUps()
    {
        var buttons = ButtonsOf(LongTrim);
        Assert.Equal(new[] { "Nose Up", "Nose Down" }, buttons.Select(b => b.Word));
        Assert.Equal(new[] { "RIGHT_BUTTON_DOWN", "LEFT_BUTTON_DOWN" }, buttons.Select(b => b.DownEvent));
        // MD11_Long_Trim_Switch's REL_CODE sends both, in this order.
        Assert.All(buttons, b => Assert.Equal(new[] { "RIGHT_BUTTON_UP", "LEFT_BUTTON_UP" }, b.UpEvents));
        Assert.All(buttons, b => Assert.True(b.IsHold));
    }

    [Fact]
    public void NoOtherSpringLoadedControl_GetsButtons()
    {
        // The yoke trim switches, the door switches and the climb/descent knob carry a release too, but
        // neither a readout nor curated directions: they stay as they were.
        var withButtons = Md11TrimButtons.From(Map.Controls).Select(b => b.Control.NodeId).Distinct().OrderBy(n => n);
        Assert.Equal(new[] { Aileron, Rudder, LongTrim }.OrderBy(n => n), withButtons);
    }

    [Fact]
    public void TheButtons_SitRightAfterTheirTrimRow_DiscriminatorFirst()
    {
        var def = new TFDiMD11Definition();
        var vars = def.GetVariables();
        var panel = def.GetPanelControls().Values.Single(keys => keys.Contains(Rudder));
        foreach (var nodeId in new[] { Rudder, Aileron, LongTrim })
        {
            int at = panel.IndexOf(nodeId);
            var c = Control(nodeId);
            var buttons = ButtonsOf(nodeId);
            for (int i = 0; i < buttons.Count; i++)
            {
                var key = panel[at + 1 + i];
                Assert.Equal(Md11TrimButtons.KeyFor(c, buttons[i].DownEvent), key);
                Assert.Equal($"{buttons[i].Word} {c.DisplayLabel}", vars[key].DisplayName);
                Assert.True(vars[key].RenderAsButton);
                Assert.Equal(UpdateFrequency.Never, vars[key].UpdateFrequency);
            }
        }
    }

    [Fact]
    public void LongTrim_HasNoReadBackOfItsOwn_TheStockTrimAnnouncementSpeaksIt()
        => Assert.Null(Md11TrimButtons.ReadBackSentence(Control(LongTrim), _ => 3.0));

    [Fact]
    public void TheReadBack_NamesTheTrimAndSaysTfdisWords()
    {
        var c = Control(Aileron);
        Assert.Equal("Aileron Trim: 0.4 degrees Right Wing Down",
            Md11TrimButtons.ReadBackSentence(c, name => name == c.StateVar ? 26 : null));
    }
}
