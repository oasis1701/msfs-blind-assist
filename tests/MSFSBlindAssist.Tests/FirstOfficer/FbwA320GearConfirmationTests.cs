using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwA320GearConfirmationTests
{
    private static Func<string, double> Reader(double handle, double c, double l, double r) => f => f switch
    {
        "GEAR_HANDLE_POSITION" => handle,
        "A32NX_GEAR_CENTER_POSITION" => c,
        "A32NX_GEAR_LEFT_POSITION" => l,
        "A32NX_GEAR_RIGHT_POSITION" => r,
        _ => double.NaN,
    };

    [Fact] public void Up_when_handle_up_and_all_legs_retracted()
        => Assert.Equal(1.0, FbwA320GearConfirmation.UpValue(Reader(0, 0, 2, 4.9)));

    [Fact] public void Not_up_while_any_leg_in_transit()
        => Assert.Equal(0.0, FbwA320GearConfirmation.UpValue(Reader(0, 0, 50, 0)));

    [Fact] public void Not_up_with_handle_down()
        => Assert.Equal(0.0, FbwA320GearConfirmation.UpValue(Reader(1, 0, 0, 0)));

    [Fact] public void Down_when_handle_down_and_all_legs_extended()
        => Assert.Equal(1.0, FbwA320GearConfirmation.DownValue(Reader(1, 100, 96, 99)));

    [Fact] public void Not_down_while_any_leg_in_transit()
        => Assert.Equal(0.0, FbwA320GearConfirmation.DownValue(Reader(1, 100, 80, 100)));

    [Fact]
    public void Unknown_input_reads_NaN_both_ways()
    {
        Assert.True(double.IsNaN(FbwA320GearConfirmation.UpValue(Reader(0, 0, double.NaN, 0))));
        Assert.True(double.IsNaN(FbwA320GearConfirmation.DownValue(Reader(double.NaN, 100, 100, 100))));
    }

    [Fact]
    public void Both_evaluators_poll_the_handle_and_legs_and_report_unknown_with_no_data()
    {
        var a320 = new FbwA320StateEvaluator();
        var a330 = new HwA330StateEvaluator();
        foreach (var f in new[] { FbwA320GearConfirmation.HandleField }.Concat(FbwA320GearConfirmation.LegFields))
        {
            Assert.Contains(f, a320.OnRequestPollFields);
            Assert.Contains(f, a330.OnRequestPollFields);
        }
        Assert.True(double.IsNaN(a320.GetValue(FbwA320GearConfirmation.UpField)));
        Assert.True(double.IsNaN(a330.GetValue(FbwA320GearConfirmation.DownField)));
    }

    [Fact]
    public void The_leg_vars_are_registered_so_the_poll_can_request_them()
    {
        var vars = new FlyByWireA320Definition().GetVariables();
        foreach (var f in FbwA320GearConfirmation.LegFields) Assert.True(vars.ContainsKey(f), f);
        Assert.True(vars.ContainsKey(FbwA320GearConfirmation.HandleField));
    }

    /// <summary>
    /// The legs are not declared literally: the A32NX definition registers them through its
    /// System Display WHEEL page auto-register loop, as OnRequest L:vars. The First Officer's
    /// 1 s poll is then their only delivery route, so they must stay OnRequest and the A330
    /// (which inherits the registration) must keep them.
    /// </summary>
    [Fact]
    public void The_leg_vars_are_OnRequest_lvars_on_both_airframes()
    {
        foreach (var vars in new[]
                 {
                     new FlyByWireA320Definition().GetVariables(),
                     new HeadwindA330Definition().GetVariables(),
                 })
        {
            foreach (var f in FbwA320GearConfirmation.LegFields)
            {
                Assert.True(vars.TryGetValue(f, out var def), f);
                Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.LVar, def!.Type);
                Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.OnRequest, def.UpdateFrequency);
            }
        }
    }
}
