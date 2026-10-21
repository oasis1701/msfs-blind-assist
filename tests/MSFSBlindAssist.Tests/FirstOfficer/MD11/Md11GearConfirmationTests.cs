using System;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// FO-13 on the MD-11: the gear read-back lines and the read-only gear checks read the lever AND
/// the stock legs (<see cref="Md11GearConfirmation"/>), never the lever alone, which latched "Gear:
/// Down" over gear still in transit. NaN while any input is unread.
/// </summary>
public class Md11GearConfirmationTests
{
    private static Func<string, double> Read(double lever, double nose, double left, double right) => key =>
        key == Md11GearLever.Key ? lever
        : key == TFDiMD11Definition.FoGearLegKeys[0] ? nose
        : key == TFDiMD11Definition.FoGearLegKeys[1] ? left
        : key == TFDiMD11Definition.FoGearLegKeys[2] ? right
        : double.NaN;

    [Fact]
    public void Down_is_the_lever_down_and_every_leg_out()
    {
        Assert.Equal(1.0, Md11GearConfirmation.DownValue(Read(25, 100, 100, 100)));
        Assert.Equal(0.0, Md11GearConfirmation.DownValue(Read(25, 100, 60, 100)));   // a leg in transit
        Assert.Equal(0.0, Md11GearConfirmation.DownValue(Read(0, 100, 100, 100)));   // the lever up
    }

    [Fact]
    public void Up_is_the_lever_up_and_every_leg_in()
    {
        Assert.Equal(1.0, Md11GearConfirmation.UpValue(Read(0, 0, 0, 0)));
        Assert.Equal(0.0, Md11GearConfirmation.UpValue(Read(0, 0, 30, 0)));          // a leg in transit
        Assert.Equal(0.0, Md11GearConfirmation.UpValue(Read(25, 0, 0, 0)));          // the lever down
    }

    [Fact]
    public void The_lever_reads_down_by_TFDis_own_threshold()
    {
        Assert.Equal(1.0, Md11GearConfirmation.DownValue(Read(Md11GearLever.DownThreshold, 100, 100, 100)));
        Assert.Equal(1.0, Md11GearConfirmation.UpValue(Read(Md11GearLever.DownThreshold - 0.5, 0, 0, 0)));
    }

    [Theory]
    [InlineData(double.NaN, 100, 100, 100)]
    [InlineData(25, double.NaN, 100, 100)]
    [InlineData(25, 100, double.NaN, 100)]
    [InlineData(25, 100, 100, double.NaN)]
    public void Any_unread_input_is_unknown(double lever, double nose, double left, double right)
    {
        Assert.True(double.IsNaN(Md11GearConfirmation.DownValue(Read(lever, nose, left, right))));
        Assert.True(double.IsNaN(Md11GearConfirmation.UpValue(Read(lever, nose, left, right))));
    }

    [Fact]
    public void The_evaluator_publishes_both_fields()
    {
        Assert.Equal(1.0, Md11FoStateEvaluator.Compute(Md11GearConfirmation.DownField, Read(25, 100, 100, 100)));
        Assert.Equal(1.0, Md11FoStateEvaluator.Compute(Md11GearConfirmation.UpField, Read(0, 0, 0, 0)));
        Assert.Contains(Md11GearConfirmation.DownField, Md11FoStateEvaluator.SyntheticKeys);
        Assert.Contains(Md11GearConfirmation.UpField, Md11FoStateEvaluator.SyntheticKeys);
    }

    [Fact]
    public void The_legs_are_stock_simvars_registered_for_the_first_officer_and_polled()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        string[] names = { "GEAR CENTER POSITION", "GEAR LEFT POSITION", "GEAR RIGHT POSITION" };
        for (int i = 0; i < 3; i++)
        {
            var def = vars[TFDiMD11Definition.FoGearLegKeys[i]];
            Assert.Equal(names[i], def.Name);
            Assert.Equal(SimVarType.SimVar, def.Type);
            Assert.Equal("percent", def.Units);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
            Assert.True(def.ExcludeFromMonitorManager);
        }
        var polled = new Md11FoStateEvaluator().OnRequestPollFields;
        Assert.All(TFDiMD11Definition.FoGearLegKeys, k => Assert.Contains(k, polled));
        Assert.Contains(Md11GearLever.Key, polled);
    }

    [Fact]
    public void The_gear_read_back_lines_read_the_confirmation_and_the_handle_line_the_lever()
    {
        var items = Md11FoChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        Assert.Equal(Md11GearConfirmation.UpField, items["ATC_GEAR"].StateFieldName);
        Assert.Equal(Md11GearConfirmation.DownField, items["BLC_GEAR"].StateFieldName);
        Assert.Equal("FO_GEAR_DOWN", items["CEC_GEAR"].StateFieldName);   // "Gear Handle: Down" is the handle
    }
}
