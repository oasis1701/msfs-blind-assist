using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE XLS READS THE INDICATION, AND THE NG'S RPM RULE MUST NOT BE INHERITED.
///
/// DISP_* IS what the pilot reads: COWS models an instrument failure as FAILURES_DISP_*,
/// which zeroes the drawn value while the physics carries on. So a readout bound to the
/// physics shows a blind pilot a perfect number off a dead gauge - the exact failure class
/// COWS deliberately modelled, and one a sighted pilot cannot be fooled by.
///
/// The tachometer is where the two variants genuinely differ, and it was flagged as
/// unresolved for months. Counted in both model directories of the installed package:
///
///     NG    DISP_PROP_RPM ✓   PROP_RPM_SENS ✓   FAILURES_DISP_RPM ✗
///     XLS   DISP_PROP_RPM ✓   PROP_RPM_SENS ✗   FAILURES_DISP_RPM ✓  (7 references)
///
/// So the NG reads the SENSOR because its gauge cannot fail and DISP_PROP_RPM is quantised
/// to 10 RPM there; the XLS must read the GAUGE because it can. Injected live on the XLS:
/// FAILURES_DISP_RPM = 1 took DISP_PROP_RPM from 1020 to ZERO while (A:GENERAL ENG RPM:1,
/// rpm) went on reading 1013. Same proved for MAP: DISP_MAP 15.13 to zero while TB_CALC_MAP
/// held 0.51 bar.
/// </summary>
public class CowsDA40XlsIndicationTests
{
    [Theory]
    [InlineData("DA40_XLS_RPM", "DISP_PROP_RPM")]
    [InlineData("DA40_XLS_MAP", "DISP_MAP")]
    [InlineData("DA40_XLS_OIL_PRESSURE", "DISP_OP")]
    [InlineData("DA40_XLS_FUEL_FLOW", "DISP_FF")]
    [InlineData("DA40_XLS_CHT_1", "DISP_CHT:1")]
    [InlineData("DA40_XLS_EGT_1", "DISP_EGT:1")]
    public void EngineReadoutsComeFromTheIndication(string key, string expected)
    {
        var vars = new CowsDA40Definition(DA40Variant.XLS).GetVariables();
        Assert.Equal(expected, vars[key].Name);
    }

    /// <summary>
    /// The NG keeps its sensor, for the reason above. Pinned beside the XLS rule so the two
    /// can never be "harmonised" into one.
    /// </summary>
    [Fact]
    public void TheNgKeepsItsSensorBecauseItsTachometerCannotFail()
    {
        var vars = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        Assert.Equal("PROP_RPM_SENS:1", vars["DA40_POWER_RPM"].Name);
    }

    /// <summary>
    /// ⚠️ THESE TWO WERE THE LAST XLS READOUTS ON THE PHYSICS, AND THIS TEST IS THE
    /// INVERSION OF WHAT IT SAID. It used to pin them as knowingly-unfinished: oil
    /// temperature's indication is FAHRENHEIT while its arc table was CELSIUS, and fuel
    /// pressure's is PSI while its arcs were BAR, and a band is looked up from the RAW
    /// value - so swapping the source alone would have put a green needle in the red.
    ///
    /// Both moved, arcs and all, from the aeroplane's OWN gauge declarations in its
    /// panel.xml rather than by converting the old numbers. That distinction is the whole
    /// lesson: the recorded plan was to convert oil temperature's celsius arcs to
    /// 149 / 230 / 244 F, and the XLS's real gauge is 122 / 275 / 285 F - the old table was
    /// the AUSTRO's arcs sitting on a LYCOMING, so a caution was being called at 111 C on
    /// an engine whose own gauge stays green to 135 C.
    /// </summary>
    [Theory]
    [InlineData("DA40_XLS_OIL_TEMP", "DISP_OT")]
    [InlineData("DA40_XLS_FUEL_PRESSURE", "DISP_FP")]
    public void EveryXlsReadoutWithAnIndicationNowReadsIt(string key, string expected)
    {
        var vars = new CowsDA40Definition(DA40Variant.XLS).GetVariables();
        Assert.Equal(expected, vars[key].Name);

        // ⚠️ AND AS AN L:VAR, IN "number". The continuous batch hands this string straight
        // to SimConnect, so a temperature or pressure unit here would be converted from a
        // base an L:var does not have. The unit is named by the display override instead.
        Assert.Equal(SimVarType.LVar, vars[key].Type);
        Assert.Equal("number", vars[key].Units);
    }
}
