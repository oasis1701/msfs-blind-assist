using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// FO-25 on the 777's two fuel-control CUTOFF ticks. The Preflight one is a cold-and-dark check:
/// it cuts the fuel only on the ground with both engines read stopped, because airborne with both
/// engines out the switches must stay at RUN for a relight. Before 2026-10-20 it checked the
/// engines alone while its spoken refusal also named the ground. The Shutdown one is the cut that
/// stops the engines, so it checks the ground alone, and says so. Each refusal names exactly what
/// its line checked.
/// </summary>
public class Pmdg777FuelControlGuardTests
{
    private static ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Item(string id) =>
        PMDG777ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    private static AircraftStateEvaluator State(double? onGround, double? n2)
    {
        var s = new AircraftStateEvaluator();
        var d = new Dictionary<string, double>();
        if (onGround is double g) d["SIM_ON_GROUND"] = g;
        s.SetCachedValueSource(k => d.TryGetValue(k, out var v) ? v : null);
        if (n2 is double n) s.SetEngineN2(n, n);
        return s;
    }

    [Fact]
    public void Preflight_cutoff_needs_the_ground_and_the_engines_stopped()
    {
        var when = Item("PF_FUEL_CONTROL").LeaveAloneWhen!;
        Assert.False(when(State(onGround: 1, n2: 0)));       // parked, cold: cut
        Assert.True(when(State(onGround: 0, n2: 0)));        // airborne, both engines out: keep RUN
        Assert.True(when(State(onGround: 1, n2: 60)));       // on the ground, engines turning
        Assert.True(when(State(onGround: null, n2: 0)));     // air/ground unread
        Assert.True(when(State(onGround: 1, n2: null)));     // engines unread
    }

    [Fact]
    public void Shutdown_cutoff_needs_the_ground_only()
    {
        var when = Item("SD_FUEL_CTRL").LeaveAloneWhen!;
        Assert.False(when(State(onGround: 1, n2: 60)));      // the cut that stops the engines
        Assert.True(when(State(onGround: 0, n2: 60)));
        Assert.True(when(State(onGround: null, n2: 60)));
    }

    [Fact]
    public void Each_refusal_names_exactly_what_its_line_checks()
    {
        Assert.Equal("Fuel control switches left at RUN: not read on the ground, or the engines are not read stopped.",
            Item("PF_FUEL_CONTROL").LeaveAloneText);
        Assert.Equal("Fuel control switches left at RUN: not read on the ground.",
            Item("SD_FUEL_CTRL").LeaveAloneText);
    }
}
