using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 737 First Officer's "Generators: ON", read from each engine generator's GEN OFF
/// BUS light: the app no longer remembers a GEN switch (#295 made GEN 1/2 stateless push
/// pairs), and PMDG's raw ELEC_GenSw bool reads 1 at rest. Pure logic plus the app's own
/// Build() definitions.
/// </summary>
public class Pmdg737GeneratorBusStateTests
{
    private static Func<string, double> Fields(params (string Field, double Value)[] values)
    {
        var map = values.ToDictionary(v => v.Field, v => v.Value);
        return f => map.TryGetValue(f, out var v) ? v : double.NaN;
    }

    [Theory]
    [InlineData(0.0, 1.0, true)]          // light dark, transfer bus powered: on its bus
    [InlineData(1.0, 1.0, false)]         // light lit on a powered bus: the APU holds the bus
    [InlineData(0.0, 0.0, false)]         // light dark for want of power: unpowered aircraft
    [InlineData(double.NaN, 1.0, false)]  // light unreadable
    [InlineData(0.0, double.NaN, false)]  // bus unreadable
    public void Generator_is_on_its_bus_only_with_its_light_dark_and_its_bus_powered(
        double offBusLight, double transferBus, bool expected)
    {
        var read = Fields(("ELEC_annunGEN_BUS_OFF_0", offBusLight), ("ELEC_BusPowered_7", transferBus));
        Assert.Equal(expected, GeneratorBusState.IsOnBus(0, read));
    }

    [Fact]
    public void Each_generator_reads_its_own_light_and_bus()
    {
        // Generator 1 on its bus, generator 2 off (its light lit): never mixed up.
        var read = Fields(
            ("ELEC_annunGEN_BUS_OFF_0", 0.0), ("ELEC_BusPowered_7", 1.0),
            ("ELEC_annunGEN_BUS_OFF_1", 1.0), ("ELEC_BusPowered_8", 1.0));
        Assert.True(GeneratorBusState.IsOnBus(0, read));
        Assert.False(GeneratorBusState.IsOnBus(1, read));
    }

    [Fact]
    public void Generators_checklist_lines_read_the_light_based_fields()
    {
        var groups = PMDG737ChecklistDefinitions.Build();
        foreach (var (group, id) in new[] { ("BEFORE_TAXI", "BT_GEN"), ("BEFORE_TAXI_CL", "BTC_GEN") })
        {
            var item = groups.Single(g => g.Id == group).Items.Single(i => i.Id == id);
            Assert.Equal(GeneratorBusState.OnBusFields[0], item.StateFieldName);
            Assert.Equal(new[] { GeneratorBusState.OnBusFields[1] }, item.AdditionalStateFields);
        }
    }

    [Fact]
    public void No_checklist_line_reads_the_raw_generator_switch()
    {
        var fields = PMDG737ChecklistDefinitions.Build()
            .SelectMany(g => g.Items)
            .SelectMany(i => new[] { i.StateFieldName }.Concat(i.AdditionalStateFields))
            .Where(f => f != null);
        Assert.DoesNotContain(fields, f => f!.StartsWith("ELEC_GenSw", StringComparison.Ordinal));
    }
}
