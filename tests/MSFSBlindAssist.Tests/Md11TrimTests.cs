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

    [Fact]
    public void TheRudderTrim_IsHeld_AndReleasedByLeftButtonUpEitherWay()
    {
        var buttons = Md11TrimButtons.From(Map.Controls).Where(b => b.Control.NodeId == Rudder).ToList();
        Assert.Equal(2, buttons.Count);
        Assert.All(buttons, b => Assert.True(b.IsHold));
        Assert.All(buttons, b => Assert.Equal("LEFT_BUTTON_UP", b.UpEvent));
        Assert.Equal("LEFT_BUTTON_DOWN", buttons.Single(b => b.Left).DownEvent);
        Assert.Equal("RIGHT_BUTTON_DOWN", buttons.Single(b => !b.Left).DownEvent);
    }

    [Fact]
    public void TheAileronTrim_IsOneWheelClickPerPress_DownIsLeft()
    {
        var buttons = Md11TrimButtons.From(Map.Controls).Where(b => b.Control.NodeId == Aileron).ToList();
        Assert.Equal(2, buttons.Count);
        Assert.All(buttons, b => Assert.False(b.IsHold));
        Assert.Equal("WHEEL_DOWN", buttons.Single(b => b.Left).DownEvent);
        Assert.Equal("WHEEL_UP", buttons.Single(b => !b.Left).DownEvent);
    }

    [Fact]
    public void TheButtons_SitRightAfterTheirTrimRow_DiscriminatorFirst()
    {
        var def = new TFDiMD11Definition();
        var vars = def.GetVariables();
        var panel = def.GetPanelControls().Values.Single(keys => keys.Contains(Rudder));
        foreach (var nodeId in new[] { Rudder, Aileron })
        {
            int at = panel.IndexOf(nodeId);
            var c = Control(nodeId);
            Assert.Equal(Md11TrimButtons.KeyFor(c, left: true), panel[at + 1]);
            Assert.Equal(Md11TrimButtons.KeyFor(c, left: false), panel[at + 2]);
            Assert.Equal($"Left {c.DisplayLabel}", vars[panel[at + 1]].DisplayName);
            Assert.Equal($"Right {c.DisplayLabel}", vars[panel[at + 2]].DisplayName);
            Assert.True(vars[panel[at + 1]].RenderAsButton);
            Assert.Equal(UpdateFrequency.Never, vars[panel[at + 1]].UpdateFrequency);
        }
    }

    [Fact]
    public void TheReadBack_NamesTheTrimAndSaysTfdisWords()
    {
        var c = Control(Aileron);
        Assert.Equal("Aileron Trim: 0.4 degrees Right Wing Down",
            Md11TrimButtons.ReadBackSentence(c, name => name == c.StateVar ? 26 : null));
    }
}
