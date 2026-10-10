using System;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 Landing Checklist's "Landing gear: DOWN" (FO-13, FOA-9): the lever down AND every gear leg
/// over 98 %, the A380 System Display WHEEL page's own green threshold; NaN while any input is unknown.
/// </summary>
public class FbwA380GearConfirmationTests
{
    private static Func<string, double> Reader(double handle, double c, double l, double r) => f => f switch
    {
        "A32NX_GEAR_HANDLE_POSITION" => handle,
        "A32NX_GEAR_CENTER_POSITION" => c,
        "A32NX_GEAR_LEFT_POSITION" => l,
        "A32NX_GEAR_RIGHT_POSITION" => r,
        _ => double.NaN,
    };

    [Fact]
    public void Down_when_the_lever_is_down_and_every_leg_is_past_98_percent()
        => Assert.Equal(1.0, FbwA380GearConfirmation.DownValue(Reader(1, 100, 98.5, 99)));

    [Fact]
    public void Not_down_at_the_threshold_or_in_transit()
    {
        Assert.Equal(0.0, FbwA380GearConfirmation.DownValue(Reader(1, 100, 98, 100)));
        Assert.Equal(0.0, FbwA380GearConfirmation.DownValue(Reader(1, 100, 50, 100)));
    }

    [Fact]
    public void Not_down_with_the_lever_up()
        => Assert.Equal(0.0, FbwA380GearConfirmation.DownValue(Reader(0, 100, 100, 100)));

    [Fact]
    public void Unknown_input_reads_NaN()
    {
        Assert.True(double.IsNaN(FbwA380GearConfirmation.DownValue(Reader(double.NaN, 100, 100, 100))));
        Assert.True(double.IsNaN(FbwA380GearConfirmation.DownValue(Reader(1, 100, double.NaN, 100))));
    }

    [Fact]
    public void The_evaluator_publishes_the_down_field_and_polls_its_inputs()
    {
        var e = new FbwA380StateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(("A32NX_GEAR_HANDLE_POSITION", 1),
            ("A32NX_GEAR_CENTER_POSITION", 100), ("A32NX_GEAR_LEFT_POSITION", 100), ("A32NX_GEAR_RIGHT_POSITION", 99)));
        Assert.Equal(1.0, e.GetValue(FbwA380GearConfirmation.DownField));
        foreach (var f in new[] { FbwA380GearConfirmation.HandleField }.Concat(FbwA380GearConfirmation.LegFields))
            Assert.Contains(f, e.OnRequestPollFields);
    }

    /// <summary>The legs are not declared literally: the A380 definition registers them through its
    /// System Display WHEEL page loop, as OnRequest L:vars, so the First Officer's poll is their only
    /// delivery route.</summary>
    [Fact]
    public void The_inputs_are_registered_and_the_legs_are_OnRequest_lvars()
    {
        var vars = new FlyByWireA380Definition().GetVariables();
        Assert.True(vars.ContainsKey(FbwA380GearConfirmation.HandleField));
        foreach (var f in FbwA380GearConfirmation.LegFields)
        {
            Assert.True(vars.TryGetValue(f, out var def), f);
            Assert.Equal(SimVarType.LVar, def!.Type);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        }
    }
}
