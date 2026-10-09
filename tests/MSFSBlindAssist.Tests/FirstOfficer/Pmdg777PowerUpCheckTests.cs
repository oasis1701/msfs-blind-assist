using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 777 power-up state Cockpit Prep checks before it starts (2026-10-09): the five
/// Preflight lines the Electrical Power Up flow already set — battery, ADIRU, demand pumps OFF,
/// nav lights, gear lever DOWN. Cockpit Prep had no step behind them, so a finished run latched
/// them over whatever the switches read (FO-7). One read-only check waits on one synthetic field,
/// <see cref="Pmdg777PowerUpCheck.Field"/>: 1 only when all five lines' own conditions hold, 0
/// when one does not, NaN while any input is unread. Pure logic over a reader, the seam
/// <see cref="Pmdg777GearConfirmation"/> uses.
/// </summary>
public class Pmdg777PowerUpCheckTests
{
    private static readonly string[] Lines =
        { "PF_BATTERY", "PF_ADIRU", "PF_DEMAND_PUMPS_OFF", "PF_NAV_LIGHTS", "PF_GEAR_DOWN" };

    private static List<ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator>> PreflightLines() =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == "PREFLIGHT").Items
            .Where(i => Lines.Contains(i.Id)).ToList();

    private static IEnumerable<string> FieldsOf(ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> item) =>
        new[] { item.StateFieldName! }.Concat(item.AdditionalStateFields);

    // Every input at a value its line accepts.
    private static Dictionary<string, double> AllHold() => new()
    {
        ["ELEC_Battery_Sw_ON"] = 1,
        ["ADIRU_Sw_On"] = 1,
        ["HYD_DemandElecPump_Selector_0"] = 0, ["HYD_DemandElecPump_Selector_1"] = 0,
        ["HYD_DemandAirPump_Selector_0"] = 0, ["HYD_DemandAirPump_Selector_1"] = 0,
        ["LTS_NAV_Sw_ON"] = 1,
        ["GEAR_Lever"] = 1,
    };

    private static Func<string, double> Reader(Dictionary<string, double> values) =>
        f => values.TryGetValue(f, out double v) ? v : double.NaN;

    [Fact]
    public void The_check_reads_exactly_the_five_lines_fields()
    {
        Assert.Equal(Lines.Length, PreflightLines().Count);
        Assert.Equal(PreflightLines().SelectMany(FieldsOf).OrderBy(f => f, StringComparer.Ordinal),
            Pmdg777PowerUpCheck.Conditions.Select(c => c.Input).OrderBy(f => f, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Each_input_holds_exactly_when_its_line_says_so(double value)
    {
        foreach (var item in PreflightLines())
            foreach (string field in FieldsOf(item))
            {
                bool line = field == item.StateFieldName ? item.EvaluateState(value) : item.EvaluateAdditionalState(value);
                bool check = Pmdg777PowerUpCheck.Conditions.Single(c => c.Input == field).Holds(value);
                Assert.True(line == check, $"{item.Id} {field} = {value}: the line says {line}, the check {check}");
            }
    }

    [Fact]
    public void One_when_all_five_lines_hold() =>
        Assert.Equal(1, Pmdg777PowerUpCheck.Value(Reader(AllHold())));

    [Theory]
    [InlineData("ELEC_Battery_Sw_ON", 0)]
    [InlineData("ADIRU_Sw_On", 0)]
    [InlineData("HYD_DemandElecPump_Selector_1", 1)]   // AUTO
    [InlineData("HYD_DemandAirPump_Selector_0", 2)]    // ON
    [InlineData("LTS_NAV_Sw_ON", 0)]
    [InlineData("GEAR_Lever", 0)]                      // UP
    public void Zero_when_any_line_does_not_hold(string field, double value)
    {
        var values = AllHold();
        values[field] = value;
        Assert.Equal(0, Pmdg777PowerUpCheck.Value(Reader(values)));
    }

    [Fact]
    public void Unknown_when_any_input_is_unread()
    {
        foreach (var (input, _) in Pmdg777PowerUpCheck.Conditions)
        {
            var values = AllHold();
            values.Remove(input);
            Assert.True(double.IsNaN(Pmdg777PowerUpCheck.Value(Reader(values))), input);
        }
    }

    [Fact]
    public void The_evaluator_serves_the_field_and_it_is_unknown_with_no_data()
    {
        var eval = new AircraftStateEvaluator();
        Assert.True(double.IsNaN(eval.GetValue(Pmdg777PowerUpCheck.Field)));
        Assert.False(eval.IsPowerUpConfirmed());
    }
}
