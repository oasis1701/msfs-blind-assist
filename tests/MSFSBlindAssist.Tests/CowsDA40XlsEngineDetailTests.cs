using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS engine detail, and the three rules that decided what went in and what did not.
///
/// The set came from sweeping FS Copilot's own `COWS_DA40XLS.yaml` against the BUILT
/// assembly's `GetVariables()` and then checking every survivor against the installed
/// package. That sweep cannot run in CI - it needs both third-party files - so what is
/// pinned here is its CONCLUSIONS, which are the part a later change can silently undo.
/// </summary>
public class CowsDA40XlsEngineDetailTests
{
    private static CowsDA40Definition Xls() => new(DA40Variant.XLS);
    private static CowsDA40Definition Ng() => new(DA40Variant.NG);

    /// <summary>
    /// The DISP_ rule, as a regression guard. `CHT_C:n`, `CHT_PROBE:n`, `EGT_PROBE:n`,
    /// `EGT_DELTA:n` and `OT_PROBE` are all listed by FS Copilot and all sit BEHIND an
    /// indication this definition already reads. The XLS models FAILURES_DISP_CHT and
    /// FAILURES_DISP_EGT, so binding a probe would show a blind pilot a perfect
    /// temperature off a dead gauge - handing them something the sighted pilot cannot
    /// have, and defeating a failure class COWS deliberately built.
    /// </summary>
    [Theory]
    [InlineData("CHT_C:1")]
    [InlineData("CHT_C:4")]
    [InlineData("CHT_PROBE:1")]
    [InlineData("EGT_PROBE:1")]
    [InlineData("EGT_DELTA:1")]
    [InlineData("OT_PROBE")]
    public void ThePhysicsBehindAnIndicationIsNeverBound(string lvar)
    {
        Assert.DoesNotContain(Xls().GetVariables().Values, d => d.Name == lvar);
    }

    /// <summary>And the indication each of those sits behind IS bound.</summary>
    [Theory]
    [InlineData("DISP_CHT:1")]
    [InlineData("DISP_EGT:1")]
    [InlineData("DISP_LEAN_DELTA:1")]
    public void TheIndicationItselfIsBound(string lvar)
    {
        Assert.Contains(Xls().GetVariables().Values, d => d.Name == lvar);
    }

    /// <summary>
    /// ⚠️ THE XLS SPELLS BLOCK AND OIL DAMAGE WITHOUT AN INDEX. The NG's `DAMAGE_BLOCK:1`
    /// / `DAMAGE_OIL:1` / `HEALTH_OIL:1` do not exist in the XLS package at all, and a
    /// phantom reads 0 - indistinguishable from an undamaged engine.
    /// </summary>
    [Theory]
    [InlineData("DAMAGE_BLOCK")]
    [InlineData("DAMAGE_OIL")]
    [InlineData("HEALTH_OIL")]
    [InlineData("DAMAGE_DUST")]
    public void TheXlsReadsTheUnindexedDamageNames(string lvar)
    {
        Assert.Contains(Xls().GetVariables().Values, d => d.Name == lvar);
    }

    /// <summary>
    /// The performance-variation set, whole. An all-zero set makes the aeroplane silently
    /// unstartable - fuel pressure is a product of FUEL_SPREAD_PRESSURE and the idle jet
    /// is multiplied by SPREAD_INJ_TRIM - with no CAS message and no failure flag, so a
    /// missing member is a hole in the only channel that can explain it.
    /// </summary>
    [Fact]
    public void TheWholeEngineVariationSetIsReadable()
    {
        var names = Xls().GetVariables().Values.Select(d => d.Name).ToHashSet();

        var expected = new List<string>
        {
            "FUEL_SPREAD_PRESSURE", "SPREAD_INJ_TRIM", "SPREAD_OP", "SPREAD_OC",
            "OP_SPREAD_BYPASS", "SPREAD_ROUGH", "MAG_SPREAD_TIMING", "SPREAD_AIR",
            "SPREAD_ALT", "SPREAD_ALT_OFF", "THROTTLE_SPREAD",
            "PROP_SPREAD_LO", "PROP_SPREAD_HI", "SPREAD_SET", "CYL_SPREAD_SET"
        };
        for (int c = 1; c <= 4; c++)
        {
            expected.Add($"CYL_SPREAD_EGT:{c}");
            expected.Add($"CYL_SPREAD_INJ:{c}");
            expected.Add($"CYL_SPREAD_COOL:{c}");
        }

        var missing = expected.Where(n => !names.Contains(n)).ToList();
        Assert.True(missing.Count == 0, $"unbound: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The variation panel is XLS-only, read-only, and every row on it resolves. A panel
    /// carrying a key no variable defines renders a blank row, which reads as broken.
    /// </summary>
    [Fact]
    public void TheEngineVariationPanelIsXlsOnlyAndEveryRowResolves()
    {
        var xls = Xls();
        Assert.Contains("Engine Variation", xls.GetPanelStructure()["Simulation"]);
        Assert.DoesNotContain("Engine Variation", Ng().GetPanelStructure()["Simulation"]);

        var vars = xls.GetVariables();
        var rows = xls.GetPanelDisplayVariables()["Engine Variation"];
        Assert.NotEmpty(rows);
        foreach (var key in rows)
        {
            Assert.True(vars.ContainsKey(key), $"{key} is on the panel and is not defined");
            Assert.True(vars[key].RenderAsReadOnlyStatus,
                $"{key} is on a read-only panel and is not a read-only status");
        }

        // Nothing on it is settable, so its controls entry is present and empty - present
        // because MainForm returns early for a panel absent from GetPanelControls().
        Assert.Empty(xls.GetPanelControls()["Engine Variation"]);
    }

    /// <summary>
    /// The priming charge, line by line: the spider and all four injector lines, each with
    /// its gram count and its primed latch, plus the system as a whole.
    /// </summary>
    [Fact]
    public void ThePrimingChargeIsReadableLineByLine()
    {
        var names = Xls().GetVariables().Values.Select(d => d.Name).ToHashSet();

        var expected = new List<string>
        {
            "ENG_FUEL_SYSTEM_GRAM", "ENG_FUEL_SYSTEM_PRIMED",
            "ENG_FUEL_LINE_GRAM:S", "ENG_FUEL_LINE_PRIMED:S", "START_MIXTURE_START"
        };
        for (int c = 1; c <= 4; c++)
        {
            expected.Add($"ENG_FUEL_LINE_GRAM:{c}");
            expected.Add($"ENG_FUEL_LINE_PRIMED:{c}");
        }

        var missing = expected.Where(n => !names.Contains(n)).ToList();
        Assert.True(missing.Count == 0, $"unbound: {string.Join(", ", missing)}");

        // And they are on the panel the pilot primes from.
        var priming = Xls().GetPanelDisplayVariables()["Priming"];
        Assert.Contains("DA40_XLS_PRIME_SYS_PRIMED", priming);
        Assert.Contains("DA40_XLS_PRIME_LINE_PRIMED_4", priming);
    }

    /// <summary>Per-plug fouling power, which is what the firing test compares against.</summary>
    [Fact]
    public void EveryPlugReportsThePowerItStillFiresAt()
    {
        var names = Xls().GetVariables().Values.Select(d => d.Name).ToHashSet();
        for (int c = 1; c <= 4; c++)
        {
            Assert.Contains($"ENG_MAG_FOUL_PWR:{c}L", names);
            Assert.Contains($"ENG_MAG_FOUL_PWR:{c}R", names);
            Assert.Contains($"DAMAGE_MAG_FOUL_RATE:{c}", names);
        }

        var magnetos = Xls().GetPanelDisplayVariables()["Magnetos"];
        Assert.Contains("DA40_XLS_FOUL_PWR_1L", magnetos);
    }

    /// <summary>
    /// ⚠️ SPREAD_AIR AND SPREAD_ALT ARE THE STANDBY INSTRUMENTS' VARIATION, not the
    /// engine's, and an earlier pass named them off the abbreviations alone ("Induction",
    /// "Alternator"). SPREAD_AIR scales the standby airspeed computation and
    /// SPREAD_ALT/_OFF the standby altimeter, so they sit with the instruments they
    /// explain - which is also why the panel they are NOT on is called Engine Variation.
    /// </summary>
    [Fact]
    public void TheInstrumentVariationSitsWithTheInstruments()
    {
        var xls = Xls();
        var standby = xls.GetPanelDisplayVariables()["Standby Instruments"];
        var variation = xls.GetPanelDisplayVariables()["Engine Variation"];

        foreach (var key in new[] { "DA40_XLS_VAR_AIR", "DA40_XLS_VAR_ALT", "DA40_XLS_VAR_ALT_OFF" })
        {
            Assert.Contains(key, standby);
            Assert.DoesNotContain(key, variation);
        }

        var vars = xls.GetVariables();
        Assert.Equal("Standby Airspeed Variation", vars["DA40_XLS_VAR_AIR"].DisplayName);
        Assert.Equal("Standby Altimeter Offset", vars["DA40_XLS_VAR_ALT_OFF"].DisplayName);
    }

    /// <summary>
    /// The oil COOLER has no gauge, so it is read from the model; the oil TEMPERATURE has
    /// one, so it is not read from OT_PROBE (pinned above).
    /// </summary>
    [Fact]
    public void TheOilCoolerIsReadFromTheModel()
    {
        var xls = Xls();
        Assert.Contains(xls.GetVariables().Values, d => d.Name == "OC_TEMPERATURE");
        Assert.Contains(xls.GetVariables().Values, d => d.Name == "OC_THERMOSTAT");
        Assert.Contains("DA40_XLS_OIL_COOLER_TEMP",
            xls.GetPanelDisplayVariables()["Power and Levers"]);
    }

    /// <summary>
    /// ⚠️ SIXTEEN NAMES FS COPILOT LISTS ARE NOT IN THE INSTALLED PACKAGE - the same
    /// sixteen as on the NG. A row bound to one would sit at 0 for ever, reporting "no
    /// failure" about a system nothing watches, which is worse than absent: a pilot would
    /// scan it and be reassured.
    /// </summary>
    [Theory]
    [InlineData("AFCS_FAIL_AIL")]
    [InlineData("AFCS_FAIL_ELE")]
    [InlineData("AFCS_FAIL_TRIM")]
    [InlineData("FAILURES_SENS_CHT:1")]
    [InlineData("FAILURES_SENS_EGT:1")]
    [InlineData("FAILURES_SENS_RPM")]
    [InlineData("FAILURES_SENS_VOLT")]
    [InlineData("LIGHTING_PANEL_1")]
    [InlineData("LIGHTING_GLARESHIELD_1")]
    [InlineData("ATT_CAGE_IsDown")]
    [InlineData("RESET_ECU")]
    public void NothingIsBoundToANameThePackageDoesNotHave(string lvar)
    {
        Assert.DoesNotContain(Xls().GetVariables().Values, d => d.Name == lvar);
    }

    /// <summary>
    /// Every one of these is a READOUT. The XLS has no control for its own variation, its
    /// priming charge or its damage - and a read-only row placed in a CONTROLS list reads
    /// as a switch that never announces a background change.
    /// </summary>
    [Fact]
    public void NoneOfTheNewDetailIsSettable()
    {
        var xls = Xls();
        var vars = xls.GetVariables();
        var controls = xls.GetPanelControls().SelectMany(p => p.Value).ToHashSet();

        foreach (var kv in vars.Where(kv => kv.Key.StartsWith("DA40_XLS_VAR_")
                                         || kv.Key.StartsWith("DA40_XLS_PRIME_")
                                         || kv.Key.StartsWith("DA40_XLS_FOUL_PWR_")
                                         || kv.Key.StartsWith("DA40_XLS_FOUL_RATE_")))
        {
            Assert.False(kv.Value.RenderAsButton, $"{kv.Key} renders as a button");
            Assert.Equal(UpdateFrequency.OnRequest, kv.Value.UpdateFrequency);
            Assert.DoesNotContain(kv.Key, controls);
        }
    }
}
